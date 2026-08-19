using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// One model as the provider describes it. (#674)
    ///
    /// <para><b>Everything here is the provider's own words, verbatim.</b> Nothing is scored, tiered, ranked or
    /// recommended, and no field is ours — that is what makes a fetched listing safe where a table we
    /// maintained would not be (#670/#681). Display it, attribute it, never re-rank on it.</para>
    ///
    /// <para>Providers publish wildly different amounts. OpenRouter gives context length, price and
    /// <c>supported_parameters</c> per model; a local Ollama gives an id and nothing else. Every field beyond
    /// <paramref name="Id"/> is therefore optional, and the UI degrades to a bare id rather than requiring
    /// metadata that may not exist.</para>
    /// </summary>
    /// <param name="PromptPricePerMillion">USD per million prompt tokens, as published. Null when the provider
    /// says nothing about price — which is not the same as free, and must not be rendered as free.</param>
    public sealed record AiCatalogModel(
        string Id,
        string DisplayName,
        int? ContextLength = null,
        decimal? PromptPricePerMillion = null,
        decimal? CompletionPricePerMillion = null,
        IReadOnlyList<string>? InputModalities = null,
        IReadOnlyList<string>? OutputModalities = null,
        IReadOnlyList<string>? SupportedParameters = null)
    {
        /// <summary>
        /// Whether the provider publishes a price above zero for this model.
        ///
        /// <para><b>A fact, not a judgment.</b> What a provider charges is something it states; filtering on
        /// it removes nothing on the grounds of quality, which is the line #670/#681 draws. It replaced a
        /// modality filter that was correct and useless: of OpenRouter's 415 models every single one outputs
        /// text, so "can this answer in text?" excluded nothing at all, while 395 of them cost money.</para>
        ///
        /// <para><b>Unknown is not costly.</b> A provider that publishes no price — every local runner — must
        /// not have its models hidden by a field it never sent. Only a price we can read and that is above
        /// zero counts.</para>
        /// </summary>
        public bool CostsMoney =>
            PromptPricePerMillion > 0m || CompletionPricePerMillion > 0m;

        /// <summary>
        /// Whether the provider says it accepts a reasoning-effort parameter (#671). Published, never
        /// inferred from the name.
        ///
        /// <para><b>Null means the provider said nothing</b>, which is a different fact from saying no — a
        /// local runner publishes no parameter list at all, and rendering its silence as "No reasoning" would
        /// state something about the model that nobody has established.</para>
        /// </summary>
        public bool? SupportsReasoning => SupportedParameters is null
            ? null
            : SupportedParameters.Any(p =>
                p.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("thinking", StringComparison.OrdinalIgnoreCase));

    }

    /// <summary>What a fetch produced, or why it produced nothing.</summary>
    /// <param name="Problem">A finished sentence for the reader. Never null when <paramref name="Ok"/> is
    /// false, and it names the endpoint — "cannot connect" without saying to what is the message that sends
    /// someone looking in the wrong place.</param>
    /// <param name="Reachable">
    /// Whether the endpoint answered at all — <b>not</b> whether the listing was useful.
    ///
    /// <para>An HTTP error is proof of contact: a 401, a 402, a 404 all mean something was there to say no.
    /// Only a transport failure means the endpoint could not be reached. Null when nothing was sent, so an
    /// unfinished connection cannot be reported either way.</para>
    /// </param>
    public sealed record AiCatalogResult(
        bool Ok, string? Problem, IReadOnlyList<AiCatalogModel> Models, bool? Reachable = null)
    {
        public static AiCatalogResult Success(IReadOnlyList<AiCatalogModel> models) =>
            new(true, null, models, true);

        public static AiCatalogResult Fail(string problem, bool? reachable = null) =>
            new(false, problem, Array.Empty<AiCatalogModel>(), reachable);
    }

    /// <summary>
    /// Asks a connection what models it offers. (#674)
    ///
    /// <para><b>Additive, never load-bearing.</b> A failed fetch must never block anything: it reports why,
    /// names the endpoint, and leaves the reader exactly where they were. For a custom endpoint the typed
    /// model list is unaffected; for a named provider there is nothing to disturb.</para>
    /// </summary>
    public interface IAiModelCatalog
    {
        Task<AiCatalogResult> FetchAsync(AiConnection connection, CancellationToken ct = default);
    }

    /// <summary>
    /// <c>GET {baseUrl}/models</c>, in whichever of the two protocols the connection speaks. (#674)
    ///
    /// <para>One parser serves both, because both answer with a <c>data[]</c> array of objects carrying an
    /// <c>id</c>. Everything past that is optional and provider-specific: OpenRouter adds <c>name</c>,
    /// <c>context_length</c>, <c>pricing</c>, <c>architecture</c> and <c>supported_parameters</c>; Anthropic
    /// adds <c>display_name</c>; Ollama adds nothing at all. Reading them as optional is what lets one code
    /// path serve a hosted gateway and a laptop daemon.</para>
    /// </summary>
    public sealed class AiModelCatalog : IAiModelCatalog
    {
        /// <summary>Long enough for a slow listing, short enough that a wrong URL is not a two-minute wait.
        /// This is a directory lookup, not a generation — none of the patience #673 grants a local runner
        /// applies.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        private readonly HttpClient _http;
        private readonly IAiCredentialStore? _credentials;
        private readonly ILogger<AiModelCatalog> _logger;

        public AiModelCatalog(HttpClient http, IAiCredentialStore? credentials, ILogger<AiModelCatalog> logger)
        {
            _http = http;
            _credentials = credentials;
            _logger = logger;
        }

        public async Task<AiCatalogResult> FetchAsync(AiConnection connection, CancellationToken ct = default)
        {
            if (connection.IsIncomplete)
                return AiCatalogResult.Fail(
                    $"{connection.DisplayName} is not finished being set up, so it cannot be asked for a list.");

            Uri url;
            try
            {
                url = AiHttp.ResolveEndpoint(
                    connection.ResolvedBaseUrl, "v1/models", "models",
                    connection.Kind == ChatProviderKind.Anthropic
                        ? BaseUrlConvention.ExcludesVersion
                        : BaseUrlConvention.IncludesVersion);
            }
            catch (AiException ex)
            {
                return AiCatalogResult.Fail(ex.Error.Message);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            Authenticate(request, connection);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            try
            {
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

                // Answered, even if the answer was no. A rejected key and a missing listing both prove the
                // endpoint is there, which is the fact this reports - it says nothing about whether the
                // listing was any use.
                if (!response.IsSuccessStatusCode)
                    return AiCatalogResult.Fail(
                        Explain(response.StatusCode, connection, url), reachable: true);

                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                var models = Parse(body);

                // A 200 carrying no models is not an error, but it is not a success the UI should silently
                // present as an empty list either - the reader would read it as "this provider has none".
                if (models.Count == 0)
                    return AiCatalogResult.Fail($"{url} answered, but listed no models.", reachable: true);

                // Both numbers, because one of them cannot answer the question that gets asked of this line.
                // "Fetched 2 models from cerebras" reads as a fact about the provider, and is equally
                // consistent with the provider having sent two and with our having understood two of nine -
                // and the reader who says "but I know they support more" has no way to tell which, nor did
                // we, from the log.
                var listed = CountEntries(body);
                if (listed == models.Count)
                {
                    _logger.LogInformation(
                        "Fetched {Count} models from {Connection}", models.Count, connection.Id);
                }
                else
                {
                    _logger.LogWarning(
                        "Fetched {Count} models from {Connection}, but its listing had {Listed} entries - "
                        + "{Skipped} had no usable id", models.Count, connection.Id, listed, listed - models.Count);
                    _logger.LogDebug("{Connection} listing: {Body}", connection.Id, body);
                }
                return AiCatalogResult.Success(models);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return AiCatalogResult.Fail(
                    $"No answer from {url} within {Timeout.TotalSeconds:0} seconds.", reachable: false);
            }
            catch (HttpRequestException ex)
            {
                // The endpoint is named on purpose. "Cannot connect to API" is the sentence OpenCode writes
                // and the reason its users cannot diagnose a stopped local runner.
                _logger.LogDebug(ex, "Model listing failed for {Connection}", connection.Id);
                return AiCatalogResult.Fail($"No response from {url} — is the endpoint running?", reachable: false);
            }
            catch (JsonException)
            {
                return AiCatalogResult.Fail($"{url} answered, but not with a model listing.", reachable: true);
            }
        }

        /// <summary>
        /// Authenticates exactly as a chat request to the same endpoint would.
        ///
        /// <para>Routed through <see cref="AiHttp.ApplyAuth"/> rather than reimplemented: a listing and a
        /// question go to the same host with the same credential, and two implementations would eventually
        /// disagree. The visible symptom would be a provider whose model list loads while its answers 401 —
        /// two surfaces contradicting each other, which is the failure #673 exists to prevent.</para>
        ///
        /// <para>Header values are templates like the base URL is: Azure and Cloudflare put reader-supplied
        /// inputs in them.</para>
        /// </summary>
        private void Authenticate(HttpRequestMessage request, AiConnection connection)
        {
            var key = _credentials?.GetApiKey(connection.Id);
            var headers = connection.Headers.ToDictionary(
                h => h.Key, h => AiTemplate.Expand(h.Value, connection.Inputs));

            if (connection.Kind == ChatProviderKind.Anthropic)
            {
                // Anthropic's shape is fixed by its own adapter rather than carried on the connection, so it
                // is named here too. The version header is required on every request, key or no key.
                AiHttp.ApplyAuth(request, key, "x-api-key", null, headers);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                return;
            }

            AiHttp.ApplyAuth(request, key, connection.AuthHeaderName, connection.AuthScheme, headers);
        }

        /// <summary>A status code turned into something worth reading. The distinction that matters is
        /// credential versus endpoint: a rejected key and a wrong URL are different problems and must never
        /// produce the same sentence.</summary>
        private static string Explain(
            System.Net.HttpStatusCode status, AiConnection connection, Uri url) => (int)status switch
        {
            401 or 403 =>
                $"{connection.DisplayName} rejected the stored key. Check it under Providers.",
            404 =>
                $"{url} is not a model listing — this endpoint may not publish one.",
            429 =>
                $"{connection.DisplayName} is rate-limiting requests. Try again shortly.",
            >= 500 =>
                $"{connection.DisplayName} returned an error ({(int)status}). That is the provider's end, not yours.",
            _ =>
                $"{url} answered {(int)status}.",
        };

        /// <summary>
        /// Reads the fields we name, and only those.
        ///
        /// <para><b>Never "whatever the source says".</b> A listing can gain a field that ranks or scores
        /// models in a release nobody read, and rendering it wholesale would adopt that judgment silently. So
        /// each field is pulled by name, and adding one is a deliberate act.</para>
        /// </summary>
        internal static IReadOnlyList<AiCatalogModel> Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return Array.Empty<AiCatalogModel>();

            var models = new List<AiCatalogModel>(data.GetArrayLength());

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (Text(item, "id") is not { Length: > 0 } id) continue;

                var architecture = Object(item, "architecture");
                var pricing = Object(item, "pricing");

                models.Add(new AiCatalogModel(
                    id,
                    Text(item, "name") ?? Text(item, "display_name") ?? id,
                    Int(item, "context_length") ?? Int(Object(item, "top_provider"), "context_length"),
                    PerMillion(pricing, "prompt"),
                    PerMillion(pricing, "completion"),
                    Modalities(architecture, "input_modalities"),
                    Modalities(architecture, "output_modalities"),
                    Strings(item, "supported_parameters")));
            }

            // Alphabetical by the name the provider published. Mechanical, and the only ordering allowed:
            // "newest first" is the rule upstream uses and is an editorial claim, since a newer point release
            // can be worse at Pali (#689).
            models.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return models;
        }

        /// <summary>
        /// How many entries the listing carried, whether or not we could read them.
        ///
        /// <para>Counted separately from parsing so the two can disagree in the log. A provider that offers
        /// nine models and a parser that understands two produce the same "2" otherwise, and the difference
        /// is the whole diagnosis.</para>
        /// </summary>
        internal static int CountEntries(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("data", out var data) &&
                       data.ValueKind == JsonValueKind.Array
                    ? data.GetArrayLength()
                    : 0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        private static JsonElement? Object(JsonElement? parent, string name) =>
            parent is { } p && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : null;

        private static string? Text(JsonElement? parent, string name) =>
            parent is { } p && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int? Int(JsonElement? parent, string name) =>
            parent is { } p && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
                ? number
                : null;

        /// <summary>
        /// OpenRouter publishes price per <i>token</i>, as a decimal string. Per million is the unit anyone
        /// actually compares in, and the conversion is arithmetic on the provider's own number rather than a
        /// judgment about it.
        /// </summary>
        private static decimal? PerMillion(JsonElement? pricing, string name)
        {
            if (Text(pricing, name) is not { Length: > 0 } raw) return null;
            if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var perToken)) return null;
            return perToken * 1_000_000m;
        }

        /// <summary>Modalities, from either the list form or the older <c>"text-&gt;text"</c> string.</summary>
        private static IReadOnlyList<string>? Modalities(JsonElement? architecture, string name)
        {
            if (Strings(architecture, name) is { Count: > 0 } list) return list;

            if (Text(architecture, "modality") is not { Length: > 0 } modality) return null;

            var halves = modality.Split("->", StringSplitOptions.TrimEntries);
            if (halves.Length != 2) return null;

            var half = name.StartsWith("input", StringComparison.Ordinal) ? halves[0] : halves[1];
            return half.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static IReadOnlyList<string>? Strings(JsonElement? parent, string name)
        {
            if (parent is not { } p || p.ValueKind != JsonValueKind.Object) return null;
            if (!p.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;

            var items = new List<string>();
            foreach (var element in value.EnumerateArray())
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } text)
                    items.Add(text);

            return items;
        }
    }
}
