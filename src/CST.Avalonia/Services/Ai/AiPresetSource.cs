using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// Whether a preset list is available, and why not. (#737, requested by #739)
    ///
    /// <para><b>Three outcomes, two of which used to arrive as an empty list.</b> "The reader has added them
    /// all", "the catalogue could not be reached" and "not fetched yet" are different situations needing
    /// different words, and the loud one — an unexplained empty catalogue on a fresh install — reads as a
    /// broken feature. A bare <c>IReadOnlyList</c> cannot tell them apart.</para>
    /// </summary>
    public enum AiPresetState
    {
        /// <summary>No attempt has finished yet.</summary>
        Loading,

        /// <summary>A list is available. <b>Includes the build-time snapshot</b> — a fresh offline install
        /// genuinely has those providers and can add them, so that is Ready, not a failure.</summary>
        Ready,

        /// <summary>The hosted catalogue is missing. <b>Not "there is nothing to add"</b>: the local runners
        /// and the custom-endpoint route are still returned, because they need no network to be useful and
        /// are therefore exactly the wrong thing to hide when the network is down.</summary>
        Unavailable,
    }

    public interface IAiPresetSource
    {
        IReadOnlyList<AiProviderPreset> Presets { get; }

        AiPresetState State { get; }

        /// <summary>A finished sentence to show, or null. Set only when <see cref="State"/> is
        /// <see cref="AiPresetState.Unavailable"/>.</summary>
        string? Problem { get; }

        /// <summary>What a Retry button calls. Forces a fetch regardless of the freshness window.</summary>
        Task RefreshAsync(CancellationToken ct = default);

        /// <summary>Loads from whatever is already available, without forcing a fetch.</summary>
        Task EnsureLoadedAsync(CancellationToken ct = default);

        event EventHandler? PresetsChanged;
    }

    /// <summary>
    /// Builds the provider presets from the models.dev catalogue plus a small hand-kept table. (#733, #737)
    ///
    /// <para><b>Why this is not purely generated.</b> Of the catalogue's 192 providers, 166 carry an
    /// <c>api</c> base URL and 26 do not — and the 26 are not unsupported. models.dev records a URL exactly
    /// when a provider is served by the generic OpenAI-compatible adapter, and omits it when a dedicated
    /// SDK package carries its own default. Eight of the 26 are providers we shipped and that worked:
    /// Anthropic, OpenAI, Groq, Cerebras, DeepInfra, Together, xAI, Azure. Emitting only what has an
    /// <c>api</c> field would drop OpenAI and Anthropic.</para>
    ///
    /// <para><b>The inclusion rule, stated here because its absence is what made the old list
    /// undiscoverable.</b> A preset is emitted when we have a base URL — from the catalogue or from
    /// <see cref="AiProviderPresets.HandKept"/> — AND its credential shape is one the resolver serves: a
    /// bearer token, Anthropic's <c>x-api-key</c>, or Azure's <c>api-key</c>. Everything else is skipped and
    /// <b>logged with its id</b>. The previous hand-picked list shrank from ~70 to 25 silently and nobody
    /// could say why; a generated one must not repeat that.</para>
    ///
    /// <para><b>Nothing here ranks.</b> Order is alphabetical by display name, which is mechanical. No
    /// field of the catalogue determines prominence — see #670/#681, and the tests that enforce it.</para>
    /// </summary>
    public sealed class AiPresetSource : IAiPresetSource
    {
        private readonly IModelsDevCatalog _catalog;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public AiPresetSource(IModelsDevCatalog catalog)
        {
            _catalog = catalog;
            Presets = AiProviderPresets.LocalOnly;
        }

        /// <summary>
        /// The presets derivable with no catalogue service at all — built once from the snapshot compiled
        /// into the app.
        ///
        /// <para>The fallback for a caller constructed without an <see cref="IAiPresetSource"/>. Deliberately
        /// the snapshot rather than the hand-kept table alone: the hand-kept entries are the ones the
        /// catalogue cannot supply, so on their own they are a strange, small list that includes OpenAI and
        /// excludes OpenRouter. The snapshot is what production actually falls back to, so a caller without
        /// the service should see the same thing.</para>
        /// </summary>
        public static IReadOnlyList<AiProviderPreset> SnapshotDefaults => LazySnapshot.Value;

        private static readonly Lazy<IReadOnlyList<AiProviderPreset>> LazySnapshot = new(() =>
            Build(ModelsDevCatalog.ReadSnapshot() ?? new Dictionary<string, CatalogProvider>()));

        public IReadOnlyList<AiProviderPreset> Presets { get; private set; }
        public AiPresetState State { get; private set; } = AiPresetState.Loading;
        public string? Problem { get; private set; }

        public event EventHandler? PresetsChanged;

        public Task EnsureLoadedAsync(CancellationToken ct = default) => LoadAsync(force: false, ct);

        public Task RefreshAsync(CancellationToken ct = default) => LoadAsync(force: true, ct);

        private async Task LoadAsync(bool force, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (force) await _catalog.RefreshAsync(force: true, ct).ConfigureAwait(false);

                var result = await _catalog.GetAsync(ct).ConfigureAwait(false);

                var built = Build(result.Providers);
                var hosted = built.Count - AiProviderPresets.LocalOnly.Count;

                Presets = built;

                if (result.Source == CatalogSource.None || hosted == 0)
                {
                    // The local runners are still in the list. "Unavailable" names the HOSTED catalogue as
                    // missing, not the section as empty - a reader with Ollama on this machine can still add
                    // it, and needs no network to do so.
                    State = AiPresetState.Unavailable;
                    Problem = result.Problem ?? "Couldn't reach the provider list.";
                }
                else
                {
                    State = AiPresetState.Ready;
                    Problem = result.Problem;   // e.g. "showing the last copy" - Ready, but worth saying
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Warning(ex, "Could not build the provider presets (#737)");
                Presets = AiProviderPresets.LocalOnly;
                State = AiPresetState.Unavailable;
                Problem = "Couldn't reach the provider list.";
            }
            finally
            {
                _gate.Release();
            }

            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Why a catalogue record cannot become a preset, or null if it can. (#737)
        ///
        /// <para>Every one of these was found in the real document rather than imagined — emitting any of
        /// them would produce a row that looks configured and fails at send time, which is the shape #728
        /// and #735 exist to remove.</para>
        /// </summary>
        internal static string? SkipReason(CatalogProvider provider)
        {
            // The id becomes the credential's keychain account name, so it has to be a safe segment.
            // `wafer.ai` is the real case: a dot in the id.
            if (!SlugPattern.IsMatch(provider.Id)) return "id is not a slug";

            // No URL here and none hand-kept. Not "unsupported" - unreachable until someone resolves it from
            // the vendor's docs and adds it to the table.
            if (string.IsNullOrWhiteSpace(provider.Api)) return "no base URL";

            // The catalogue templates some URLs against environment variables it expects the host to expand:
            // `${DATABRICKS_HOST}`, `${SNOWFLAKE_ACCOUNT}`, `${NEON_AI_GATEWAY_BASE_URL}`. Our templating
            // fills from reader-supplied Inputs declared by PROMPTS, which a catalogue record does not carry -
            // so these can only be served from the hand-kept table, where the prompts live.
            if (provider.Api!.Contains('$') || provider.Api.Contains('{')) return "URL needs values we cannot prompt for";

            if (!Uri.TryCreate(provider.Api, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "base URL is not an absolute http(s) address";

            return null;
        }

        private static readonly System.Text.RegularExpressions.Regex SlugPattern =
            new("^[a-z0-9][a-z0-9_-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Applies the inclusion rule. Hand-kept entries win over the catalogue for the same id: they carry
        /// URLs the catalogue does not record and auth shapes it does not describe.
        /// </summary>
        internal static IReadOnlyList<AiProviderPreset> Build(
            IReadOnlyDictionary<string, CatalogProvider> providers)
        {
            var built = new Dictionary<string, AiProviderPreset>(StringComparer.OrdinalIgnoreCase);

            foreach (var preset in AiProviderPresets.HandKept)
                built[preset.Id] = preset;

            var skipped = new List<string>();

            foreach (var (_, provider) in providers)
            {
                if (built.ContainsKey(provider.Id)) continue;      // hand-kept wins

                if (SkipReason(provider) is { } reason)
                {
                    skipped.Add($"{provider.Id} ({reason})");
                    continue;
                }

                var methods = new List<AiCredentialMethod> { new AiCredentialMethod.Key() };
                if (provider.Env is { Count: > 0 } env)
                    methods.Add(new AiCredentialMethod.Env(env.ToList()));

                built[provider.Id] = new AiProviderPreset(
                    provider.Id,
                    string.IsNullOrWhiteSpace(provider.Name) ? provider.Id : provider.Name!,
                    ChatProviderKind.OpenAiCompatible,
                    provider.Api!,
                    methods);
            }

            if (skipped.Count > 0)
            {
                // Logged, not silent. One skip is noise; forty after an upstream change is a signal, and the
                // old hand-picked list lost ~45 providers without anyone being able to tell.
                Log.Information(
                    "Provider presets: skipped {Count} with no base URL (add to AiProviderPresets.HandKept " +
                    "to support one): {Ids} (#737)",
                    skipped.Count, string.Join(", ", skipped.OrderBy(x => x, StringComparer.Ordinal).Take(40)));
            }

            // Alphabetical by display name. Mechanical on purpose: a reader reasonably reads "first" as
            // "best", and any hand-arranged order would be a claim we refuse to make (#670/#681).
            return built.Values
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
