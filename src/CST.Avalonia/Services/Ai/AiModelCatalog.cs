using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai.Credentials;
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
        IReadOnlyList<string>? SupportedParameters = null,
        IReadOnlyList<string>? ReasoningEfforts = null,
        string? DefaultReasoningEffort = null)
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
        /// Whether the provider says this model produces reasoning at all (#671). Published, never inferred
        /// from the name.
        ///
        /// <para><b>Null means the provider said nothing</b>, which is a different fact from saying no — a
        /// local runner publishes no parameter list at all, and rendering its silence as "No reasoning" would
        /// state something about the model that nobody has established.</para>
        ///
        /// <para><b>This is "emits reasoning", NOT "takes an effort knob"</b>, and the distinction is not
        /// pedantic. The first version matched any parameter containing "reasoning", which catches
        /// <c>reasoning</c> and <c>include_reasoning</c> — both of which mean the model <i>returns</i>
        /// reasoning content. Measured against OpenRouter's live listing: 287 models matched, 142 actually
        /// list <c>reasoning_effort</c>, so <b>51% were false positives</b>. Gating an effort control on this
        /// would put the control on 145 models that publish no such parameter. Use
        /// <see cref="AcceptsReasoningEffort"/> for that. (The old predicate also had a dead arm: it tested
        /// for <c>thinking</c>, which never appears in OpenRouter's vocabulary at all.)</para>
        /// </summary>
        public bool? SupportsReasoning => SupportedParameters is null
            ? null
            : SupportedParameters.Any(p =>
                p.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("thinking", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether the provider says this model accepts <c>reasoning_effort</c> — the question the effort
        /// control actually needs answered. (#671)
        ///
        /// <para>Named exactly, not matched loosely. Null keeps the same meaning as everywhere else here:
        /// the provider published no parameter list, which is silence rather than a no.</para>
        /// </summary>
        public bool? AcceptsReasoningEffort => SupportedParameters is null
            ? null
            : SupportedParameters.Any(
                p => p.Equals("reasoning_effort", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a fetch produced, or why it produced nothing.</summary>
    /// <param name="Problem">A finished sentence for the reader. Never null when <paramref name="Ok"/> is
    /// false, and it names the endpoint — "cannot connect" without saying to what is the message that sends
    /// someone looking in the wrong place.</param>
    /// <param name="Complete">
    /// Whether this listing is everything the endpoint has, so far as we can tell. (#728)
    ///
    /// <para>False when entries were skipped for want of a usable id — observed in the wild, a listing whose
    /// nine entries yielded two we could read — or when the endpoint still says there is another page after
    /// we stopped asking. <b>A short listing is still a listing</b>: it is shown, and every model in it is
    /// real. What it cannot support is the inference in the other direction, that a model absent from it has
    /// been retired, which is why the flag exists rather than a failure.</para>
    ///
    /// <para>Pages are now followed (#769), so the ordinary paged catalogue comes back complete. This stays
    /// false for the cases where following them did not finish: an endpoint that claims another page but
    /// gives no cursor to ask for it, one that ignores the cursor and repeats itself, a page that failed
    /// after earlier pages had already been read, and the page cap.</para>
    /// </param>
    /// <param name="Skipped">
    /// How many entries across every page carried no usable id, and so are not in <paramref name="Models"/>.
    /// (#769)
    ///
    /// <para>Counted rather than only logged. A reader looking at a short list has no way to tell "this
    /// provider offers three models" from "this provider listed nine and we understood three", and the log
    /// line that knew the difference is not somewhere they will ever look.</para>
    /// </param>
    /// <param name="Reachable">
    /// Whether the endpoint answered at all — <b>not</b> whether the listing was useful.
    ///
    /// <para>An HTTP error is proof of contact: a 401, a 402, a 404 all mean something was there to say no.
    /// Only a transport failure means the endpoint could not be reached. Null when nothing was sent, so an
    /// unfinished connection cannot be reported either way.</para>
    /// </param>
    public sealed record AiCatalogResult(
        bool Ok, string? Problem, IReadOnlyList<AiCatalogModel> Models, bool? Reachable = null,
        bool Complete = true, int Skipped = 0)
    {
        public static AiCatalogResult Success(
            IReadOnlyList<AiCatalogModel> models, bool complete = true, int skipped = 0) =>
            new(true, null, models, true, complete, skipped);

        public static AiCatalogResult Fail(string problem, bool? reachable = null) =>
            new(false, problem, Array.Empty<AiCatalogModel>(), reachable, false);
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

        /// <summary>
        /// How many pages to follow before stopping and saying the listing is partial. (#769)
        ///
        /// <para>Anthropic's listing defaults to twenty entries a page, so this reaches five hundred models —
        /// comfortably past any real catalogue, and OpenRouter's four hundred and twenty arrive on one page
        /// because that protocol does not page at all. It is a backstop against an endpoint that always says
        /// there is more, not an opinion about how many models a provider may have.</para>
        /// </summary>
        private const int MaxPages = 25;

        private readonly HttpClient _http;
        private readonly IAiCredentialStore? _credentials;
        private readonly IAiEnvironmentKeys? _environmentKeys;
        private readonly ILogger<AiModelCatalog> _logger;

        public AiModelCatalog(
            HttpClient http, IAiCredentialStore? credentials, ILogger<AiModelCatalog> logger,
            IAiEnvironmentKeys? environmentKeys = null)
        {
            _http = http;
            _credentials = credentials;
            _logger = logger;
            _environmentKeys = environmentKeys;
        }

        public async Task<AiCatalogResult> FetchAsync(AiConnection connection, CancellationToken ct = default)
        {
            if (connection.IsIncomplete)
                return AiCatalogResult.Fail(
                    $"{connection.DisplayName} is not finished being set up, so it cannot be asked for a list.");

            // Settled before authenticating, never before deciding what to show. Already-complete unless a
            // shell probe is genuinely in flight (#817), so this is a no-op on Windows and on every launch
            // that did not prime one. Without it, a connection adopted from a shell-profile variable reports
            // "the provider rejected the key" for the first listing after a relaunch and works on the second
            // — an intermittent authentication failure decided by a race, which is unreportable as a bug.
            if (_environmentKeys is not null && connection.UsesEnvironmentKey)
                await _environmentKeys.Ready.ConfigureAwait(false);

            // A header marked secret with nothing stored, refused here for the same reason the chat path
            // refuses it: sent blank it is dropped at the wire and comes back as a 401 naming nothing, which
            // is the #711 complaint arriving from a third direction. Refused in BOTH places or not at all -
            // a listing that loads while the chat refuses is the surface split #673 exists to prevent, just
            // pointing the other way. (#771)
            var duplicate = connection.Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .GroupBy(h => h.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                return AiCatalogResult.Fail(
                    $"This connection has two {duplicate.Key} headers. Remove one under Settings \u2192 AI.");

            // Unreadable before absent, and the same sentence the chat path uses (#926). The two surfaces send
            // the same credentials, so they must also explain a failure the same way - and "re-enter it"
            // cannot work on macOS for a secret that is stored and merely locked.
            var reads = connection.Headers
                .Where(h => h.Secret)
                .Select(h => (h.Name, Read: _credentials?.Read(connection.Id, AiCredentialNames.Header(h.Name))
                                            ?? CredentialRead.Unavailable))
                .ToList();

            var lockedSecrets = reads
                .Where(r => r.Read.State == CredentialState.Unreadable)
                .Select(r => r.Name)
                .ToList();
            if (lockedSecrets.Count > 0)
                return AiCatalogResult.Fail(
                    CredentialRead.Advice($"The {string.Join(", ", lockedSecrets)} header's value"));

            var missingSecrets = reads
                .Where(r => string.IsNullOrEmpty(r.Read.Secret))
                .Select(r => r.Name)
                .ToList();
            if (missingSecrets.Count > 0)
                return AiCatalogResult.Fail(
                    $"No stored value for the {string.Join(", ", missingSecrets)} header. "
                    + "Re-enter it under Settings \u2192 AI.");

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

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            // Resolved ONCE, outside the page loop (#925). Authenticate used to run per request, so a
            // paginated listing fetched the same secrets again for every page - and on macOS each fetch of a
            // locked key is its own authorization dialog. Anthropic pages at twenty models, so a five-hundred
            // model catalogue was twenty-five prompts for one listing. The credentials cannot change
            // mid-listing anyway; re-reading them per page was never buying freshness.
            var credentials = ResolveCredentials(connection);

            var models = new List<AiCatalogModel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var skipped = 0;
            var complete = true;
            string? cursor = null;

            try
            {
                for (var page = 1; ; page++)
                {
                    var pageUrl = cursor is null ? url : After(url, cursor);
                    using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                    Authenticate(request, connection, credentials);

                    using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

                    // Answered, even if the answer was no. A rejected key and a missing listing both prove the
                    // endpoint is there, which is the fact this reports - it says nothing about whether the
                    // listing was any use.
                    if (!response.IsSuccessStatusCode)
                    {
                        if (page == 1)
                            return AiCatalogResult.Fail(
                                Explain(response.StatusCode, connection, url), reachable: true);

                        // A later page failing costs the REST of the listing, not the part already in hand.
                        // Throwing away twenty real models because the twenty-first page 500'd would turn a
                        // partial answer into no answer, and the reader can use what arrived.
                        _logger.LogWarning(
                            "Listing {Connection}: page {Page} answered {Status}; keeping {Count} models "
                            + "already read", connection.Id, page, (int)response.StatusCode, models.Count);
                        complete = false;
                        break;
                    }

                    var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                    var pageModels = Parse(body);

                    // Both numbers, because one of them cannot answer the question that gets asked of this
                    // line. "Fetched 2 models from cerebras" reads as a fact about the provider, and is
                    // equally consistent with the provider having sent two and with our having understood two
                    // of nine - and the reader who says "but I know they support more" has no way to tell
                    // which, nor did we, from the log.
                    //
                    // Accumulated per page rather than derived at the end from a total, because a gateway that
                    // ignores the cursor sends the same page twice: the ids dedupe, the entry counts do not,
                    // and a subtraction would report entries as unreadable that we read perfectly well.
                    var listed = CountEntries(body);
                    skipped += Math.Max(0, listed - pageModels.Count);
                    if (listed != pageModels.Count)
                        _logger.LogDebug("{Connection} listing page {Page}: {Body}", connection.Id, page, body);

                    var added = 0;
                    foreach (var model in pageModels)
                        if (seen.Add(model.Id))
                        {
                            models.Add(model);
                            added++;
                        }

                    // The end of the listing, and the ordinary exit.
                    if (!HasMore(body)) break;

                    // It says there is more. Ask for it by the last entry the endpoint itself listed - in
                    // DOCUMENT order, which is not the order Parse returns: Parse sorts alphabetically, so
                    // the last model in `pageModels` is generally not the last entry on the page, and paging
                    // from it would skip whatever lies between.
                    var next = LastEntryId(body);

                    if (next is null || string.Equals(next, cursor, StringComparison.Ordinal) || added == 0)
                    {
                        // Three ways an endpoint can promise another page and not deliver one: no cursor to
                        // ask with, the same cursor it gave last time, or a page whose every id we already
                        // hold - which is what an OpenAI-compatible gateway that invents `has_more` but
                        // ignores `after_id` produces. Each would loop forever if followed. (#769)
                        _logger.LogWarning(
                            "Listing {Connection}: page {Page} says there is more but did not advance; "
                            + "keeping {Count} models", connection.Id, page, models.Count);
                        complete = false;
                        break;
                    }

                    cursor = next;

                    if (page >= MaxPages)
                    {
                        // A bound, not a judgment about how many models a provider may have. Without it a
                        // misbehaving endpoint that always says `has_more` and always advances would page
                        // until the 20-second budget ran out, and report nothing at all.
                        _logger.LogWarning(
                            "Listing {Connection}: stopped at the {Max}-page limit with {Count} models",
                            connection.Id, MaxPages, models.Count);
                        complete = false;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (models.Count == 0)
            {
                return AiCatalogResult.Fail(
                    $"No answer from {url} within {Timeout.TotalSeconds:0} seconds.", reachable: false);
            }
            catch (HttpRequestException ex) when (models.Count == 0)
            {
                // The endpoint is named on purpose. "Cannot connect to API" is the sentence OpenCode writes
                // and the reason its users cannot diagnose a stopped local runner.
                _logger.LogDebug(ex, "Model listing failed for {Connection}", connection.Id);
                return AiCatalogResult.Fail($"No response from {url} — is the endpoint running?", reachable: false);
            }
            catch (JsonException) when (models.Count == 0)
            {
                return AiCatalogResult.Fail($"{url} answered, but not with a model listing.", reachable: true);
            }
            catch (Exception ex) when (
                ex is OperationCanceledException or HttpRequestException or JsonException)
            {
                // Same three failures, but pages had already been read. The guards above carry `models.Count
                // == 0` so that the FIRST page still produces its own named sentence; past that point the
                // models in hand are worth more than the sentence, and the flag says they are not everything.
                _logger.LogWarning(ex, "Listing {Connection}: stopped after {Count} models", connection.Id, models.Count);
                complete = false;
            }

            // A 200 carrying no models is not an error, but it is not a success the UI should silently
            // present as an empty list either - the reader would read it as "this provider has none".
            if (models.Count == 0)
                return AiCatalogResult.Fail($"{url} answered, but listed no models.", reachable: true);

            // An entry we could not read is a HOLE in a listing, not a short listing, and the two want
            // different sentences: "there may be more models" sends a reader to look for models that are not
            // there, when the truth is that one of the entries is malformed. Both still clear Complete,
            // because its one job is to gate the inference that a model absent from the listing has been
            // retired - and a skipped entry is exactly a model that is absent without being retired.
            // (egret, on #769)
            if (skipped > 0) complete = false;

            // Re-sorted across pages: Parse orders each page on its own, so concatenating two of them
            // interleaves nothing and leaves the second page's A after the first page's Z.
            models.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (skipped == 0)
                _logger.LogInformation("Fetched {Count} models from {Connection}", models.Count, connection.Id);
            else
                _logger.LogWarning(
                    "Fetched {Count} models from {Connection}, but its listing had {Listed} entries - "
                    + "{Skipped} had no usable id",
                    models.Count, connection.Id, models.Count + skipped, skipped);

            return AiCatalogResult.Success(models, complete, skipped);
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
        /// <summary>Everything a request to this connection has to carry, fetched once per listing. (#925)</summary>
        private sealed record ListingCredentials(string? Key, Dictionary<string, string> Headers);

        /// <summary>
        /// Reads this connection's secrets, once. (#925)
        ///
        /// <para>Separate from <see cref="Authenticate"/> so that applying them to a request - which happens
        /// per page - cannot fetch them again. Every call here can raise a macOS authorization dialog, so the
        /// count of calls is a count of possible prompts.</para>
        /// </summary>
        private ListingCredentials ResolveCredentials(AiConnection connection)
        {
            // Stored, then the environment — the SAME rule the chat path applies, through the same helper.
            // Reading only the store here is the contradiction the summary above forbids: an adopted
            // connection would answer a question and then fail to list its models, reporting "the provider
            // rejected the stored key" for a connection that has no stored key. (#714, fable)
            var key = _credentials?.Get(connection.Id, AiCredentialNames.Primary)
                      ?? AiEnvironmentCredential.For(
                          connection.UsesEnvironmentKey, connection.EnvironmentVariable, _environmentKeys);
            // Same rule as the chat path, for the same reason the summary above gives: the two surfaces send
            // the same credentials, so a secret header must resolve identically here or a provider's model
            // list would load while its answers 401. A secret is a literal, never a template. (#771)
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var header in connection.Headers)
                headers[header.Name] = header.Secret
                    ? _credentials?.Get(connection.Id, AiCredentialNames.Header(header.Name)) ?? string.Empty
                    : AiTemplate.Expand(header.Value ?? string.Empty, connection.Inputs);

            return new ListingCredentials(key, headers);
        }

        private void Authenticate(
            HttpRequestMessage request, AiConnection connection, ListingCredentials credentials)
        {
            var key = credentials.Key;
            var headers = credentials.Headers;

            if (connection.Kind == ChatProviderKind.Anthropic)
            {
                // Anthropic's shape is fixed by its own adapter rather than carried on the connection, so it
                // is named here too. The version header is required on every request, key or no key — but
                // only when the connection did not supply one. TryAddWithoutValidation appends, so adding
                // ours unconditionally sent TWO values to a gateway that pins its own version, on this path
                // only. That is precisely the chat-vs-listing divergence the summary above says cannot
                // happen. (#711)
                AiHttp.ApplyAuth(request, key, AnthropicOptions.AuthHeader, null, headers);
                if (!request.Headers.Contains(AnthropicOptions.VersionHeader))
                    request.Headers.TryAddWithoutValidation(
                        AnthropicOptions.VersionHeader, AnthropicMessagesProvider.AnthropicVersion);
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
        /// The same listing URL, asking for what comes after <paramref name="cursor"/>. (#769)
        ///
        /// <para>Built through <see cref="UriBuilder"/> and not by string concatenation, because the base URL
        /// may already carry a query — an Azure-style endpoint pins <c>api-version</c> there — and appending
        /// a second <c>?</c> produces a URL the endpoint rejects with a message about the wrong thing.</para>
        /// </summary>
        internal static Uri After(Uri url, string cursor)
        {
            var builder = new UriBuilder(url);
            var existing = builder.Query.TrimStart('?');
            var pair = "after_id=" + Uri.EscapeDataString(cursor);
            builder.Query = existing.Length == 0 ? pair : existing + "&" + pair;
            return builder.Uri;
        }

        /// <summary>
        /// The id of the last entry the listing carried, in the order the endpoint wrote them. (#769)
        ///
        /// <para><b>Document order, deliberately.</b> <see cref="Parse"/> sorts alphabetically for display, so
        /// its last element is not the endpoint's last entry, and paging from that one would ask the endpoint
        /// to continue after a model in the middle — silently losing everything between. Anthropic publishes
        /// <c>last_id</c> for exactly this, and it is preferred where it exists; the walk is the fallback for
        /// a gateway that pages without publishing one.</para>
        /// </summary>
        internal static string? LastEntryId(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("last_id", out var last) &&
                    last.ValueKind == JsonValueKind.String &&
                    last.GetString() is { Length: > 0 } published)
                    return published;

                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                    return null;

                string? id = null;
                foreach (var item in data.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("id", out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { Length: > 0 } entry)
                        id = entry;

                return id;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether the endpoint says it has another page. (#728)
        ///
        /// <para>Anthropic's listing is paged and defaults to twenty per page. Since #769 the pages ARE
        /// followed, and this is what drives the walk; before that it existed only to stop a first page being
        /// mistaken for the whole catalogue, which would mark every model after the twentieth as retired.</para>
        /// </summary>
        internal static bool HasMore(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("has_more", out var more) &&
                       more.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                // Unparseable here means unparseable in Parse too, which already yields nothing. Saying "no
                // more pages" about a body we cannot read would be the wrong half of the answer to guess.
                return true;
            }
        }

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
                // Trimmed here, at the one place a listing id enters, so every later ordinal join
                // agrees on it. A gateway that pads its ids used to have a stored model and its own
                // listing entry miss each other: two rows for one model, published facts that never
                // reattached, and a completed fetch marking the reader's enabled model "no longer
                // listed". (#870, fable review)
                if (Text(item, "id")?.Trim() is not { Length: > 0 } id) continue;

                var architecture = Object(item, "architecture");
                var pricing = Object(item, "pricing");
                var reasoning = Object(item, "reasoning");

                models.Add(new AiCatalogModel(
                    id,
                    Text(item, "name") ?? Text(item, "display_name") ?? id,
                    Int(item, "context_length") ?? Int(Object(item, "top_provider"), "context_length"),
                    PerMillion(pricing, "prompt"),
                    PerMillion(pricing, "completion"),
                    Modalities(architecture, "input_modalities"),
                    Modalities(architecture, "output_modalities"),
                    Strings(item, "supported_parameters"),
                    // OpenRouter publishes a richer object beside the flat parameter list, and it is the one
                    // that answers "which values, and which is the default" rather than merely "is the knob
                    // there". Absent everywhere else, which reads as null. (#671)
                    //
                    // NOTE for anyone widening this parse: the same objects carry a `benchmarks` field with
                    // intelligence_index / coding_index / agentic_index on 229 of 420 models. It is
                    // provider-published, so it passes the letter of "a published capability is fine" while
                    // being unambiguously a score. It is NOT read here and must not be: #670/#681.
                    Strings(reasoning, "supported_efforts"),
                    Text(reasoning, "default_effort")));
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
