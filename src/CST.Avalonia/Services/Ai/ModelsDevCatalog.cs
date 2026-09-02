using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>One provider as models.dev describes it. Only the fields we consume are named. (#736)</summary>
    /// <param name="Api">The OpenAI-compatible base URL, or null. <b>Null does not mean unsupported</b> — it
    /// means a dedicated SDK package carries the provider's own default, so models.dev has no reason to record
    /// one. 26 of 192 are like this, including OpenAI and Anthropic. See #737 for how those are resolved.</param>
    /// <param name="Env">Environment variables that conventionally hold this provider's key, in precedence
    /// order. Feeds #714.</param>
    /// <param name="Doc">Provider documentation — in practice a models page, not an account page.</param>
    public sealed record CatalogProvider(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("api")] string? Api = null,
        [property: JsonPropertyName("npm")] string? Npm = null,
        [property: JsonPropertyName("doc")] string? Doc = null,
        [property: JsonPropertyName("env")] IReadOnlyList<string>? Env = null);

    /// <summary>Where a catalogue came from. Surfaced so a failure can be reported honestly rather than as
    /// an empty list, which reads as a broken feature (#739).</summary>
    public enum CatalogSource
    {
        /// <summary>Nothing available — no cache, no snapshot, no network.</summary>
        None,

        /// <summary>The snapshot compiled into the app. Always present, so this is the floor.</summary>
        Snapshot,

        /// <summary>A previously fetched copy on disk.</summary>
        Cache,

        /// <summary>Fetched from models.dev during this session.</summary>
        Network,
    }

    public sealed record CatalogResult(
        IReadOnlyDictionary<string, CatalogProvider> Providers,
        CatalogSource Source,
        DateTimeOffset? FetchedUtc = null,
        string? Problem = null);

    public interface IModelsDevCatalog
    {
        /// <summary>The catalogue, from the best source available. A failure to LOAD arrives as a
        /// <see cref="CatalogResult.Problem"/> alongside whatever fallback was reachable, rather than as an
        /// exception; cancelling <paramref name="ct"/> still throws, as it should.</summary>
        Task<CatalogResult> GetAsync(CancellationToken ct = default);

        /// <summary>Fetches unless the cache is younger than the freshness window. <paramref name="force"/>
        /// ignores that window — for a reader who pressed retry.</summary>
        Task RefreshAsync(bool force = false, CancellationToken ct = default);
    }

    /// <summary>
    /// Keeps a local copy of models.dev's provider catalogue. (#733, #736)
    ///
    /// <para><b>Load order: cache → snapshot → network</b>, matching opencode
    /// (<c>packages/core/src/models-dev.ts</c>). Reading before fetching is what makes a cold start instant
    /// and an offline start possible; the network then supersedes it in the background.</para>
    ///
    /// <para><b>The document is stored whole.</b> Subsetting on ingest is not a safety control — a stored
    /// field can only reach a reader through code somebody writes — and storing whole makes a refresh a file
    /// replace with no transform to get wrong. The constraint that matters lives on display and ordering:
    /// nothing here may rank, score or recommend, and #737's tests enforce that where it counts.</para>
    ///
    /// <para><b>A failure never destroys what we had.</b> The previous copy is kept and reported as such;
    /// the shipped snapshot is the floor beneath it. That is why an outage degrades to "slightly stale"
    /// rather than to an empty provider list.</para>
    /// </summary>
    public sealed class ModelsDevCatalog : IModelsDevCatalog
    {
        internal const string DefaultSource = "https://models.dev/api.json";
        internal const string SnapshotResource = "CST.Avalonia.Resources.Ai.models-dev-snapshot.json";

        /// <summary>How long a cached copy is treated as fresh. Matches opencode's guard; the point is that
        /// several starts in an hour do not each hit the network.</summary>
        internal static readonly TimeSpan Freshness = TimeSpan.FromHours(1);

        /// <summary>
        /// Fewest providers a fetched document may contain before we refuse it. (fable review)
        ///
        /// <para>A count of one is not a sanity check. An API error body — <c>{"error":{"id":"rate_limited"}}</c>
        /// — is valid JSON that deserializes to a single record carrying an id, so it passed, overwrote a good
        /// 192-provider cache, and became the answer for every subsequent start until a successful refetch.
        /// A reader who then went offline was stuck with one provider. Matches the floor
        /// <c>refresh-models-snapshot.sh</c> already applies to the same document.</para>
        /// </summary>
        internal const int MinimumPlausibleProviders = 50;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _http;
        private readonly string _cachePath;
        private readonly string _source;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private CatalogResult? _current;

        /// <param name="minimumProviders">
        /// The plausibility floor, injectable so the cache-read rule can be tested with small fixtures
        /// rather than a fifty-provider one. Production always uses
        /// <see cref="MinimumPlausibleProviders"/>. (R4-5)
        /// </param>
        public ModelsDevCatalog(HttpClient? http = null, string? cachePath = null, string? source = null,
                                int minimumProviders = MinimumPlausibleProviders)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _cachePath = cachePath ?? Path.Combine(AppConstants.DataDirectory, "models-dev.json");
            _source = source ?? DefaultSource;
            _minimumProviders = minimumProviders;
        }

        private readonly int _minimumProviders;

        public async Task<CatalogResult> GetAsync(CancellationToken ct = default)
        {
            if (_current is { } cached) return cached;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_current is { } raced) return raced;
                _current = LoadWithoutNetwork();
                return _current;
            }
            finally { _gate.Release(); }
        }

        public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
        {
            if (!force && IsCacheFresh()) return;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!force && IsCacheFresh()) return;   // re-check under the gate

                string text;
                try
                {
                    text = await _http.GetStringAsync(_source, ct).ConfigureAwait(false);
                }
                // Filtering on the token, not the exception type: HttpClient.Timeout surfaces as
                // TaskCanceledException, so `is not OperationCanceledException` would let a hung models.dev
                // throw straight out of a method whose contract is that failures arrive as Problem - which
                // #739's retry button calls directly. (fable review)
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Keep whatever we had. An unreachable models.dev must degrade to "slightly stale",
                    // never to nothing - the reader may be about to configure a local runner that needs
                    // no network at all.
                    Log.Warning(ex, "Could not fetch the provider catalogue; keeping the existing copy (#736)");
                    _current = (_current ?? LoadWithoutNetwork()) with
                    {
                        Problem = "Couldn't reach the provider list. Showing the last copy.",
                    };
                    return;
                }

                if (Parse(text) is not { } parsed || parsed.Count < _minimumProviders)
                {
                    // A 200 carrying an error page is the realistic failure, not a 404 - so validate the
                    // shape rather than trusting the status code.
                    Log.Warning("The provider catalogue did not parse, or held implausibly few providers; " +
                                "keeping the existing copy (#736)");
                    _current = (_current ?? LoadWithoutNetwork()) with
                    {
                        Problem = "The provider list could not be read. Showing the last copy.",
                    };
                    return;
                }

                WriteCache(text);
                _current = new CatalogResult(parsed, CatalogSource.Network, DateTimeOffset.UtcNow);
                Log.Information("Provider catalogue refreshed: {Count} providers (#736)", parsed.Count);
            }
            finally { _gate.Release(); }
        }

        /// <summary>Cache, then snapshot. Never touches the network, so it is safe on a startup path.</summary>
        private CatalogResult LoadWithoutNetwork()
        {
            if (ReadCache() is { Count: > 0 } cached)
                return new CatalogResult(cached, CatalogSource.Cache, CacheWrittenUtc());

            if (ReadSnapshot() is { Count: > 0 } snapshot)
                return new CatalogResult(snapshot, CatalogSource.Snapshot);

            // Only reachable if the embedded snapshot is missing or corrupt, which is a build fault rather
            // than a runtime one - so say so plainly instead of pretending there are no providers.
            Log.Error("No provider catalogue available: cache and embedded snapshot both unreadable (#736)");
            return new CatalogResult(
                new Dictionary<string, CatalogProvider>(), CatalogSource.None,
                Problem: "No provider list is available.");
        }

        private bool IsCacheFresh() =>
            CacheWrittenUtc() is { } written && DateTimeOffset.UtcNow - written < Freshness;

        private DateTimeOffset? CacheWrittenUtc() =>
            File.Exists(_cachePath) ? File.GetLastWriteTimeUtc(_cachePath) : null;

        private IReadOnlyDictionary<string, CatalogProvider>? ReadCache()
        {
            try
            {
                if (!File.Exists(_cachePath)) return null;

                var parsed = Parse(File.ReadAllText(_cachePath));
                if (parsed is not null && parsed.Count >= _minimumProviders) return parsed;

                // THE SAME FLOOR THE NETWORK PATH APPLIES (:165). It guarded only the fetch, so a sub-floor
                // file on disk - hand-edited, truncated by an outside tool, or written by a build older than
                // the floor - was served as the catalogue with no Problem reported: AiPresetSource's collapse
                // guard fires only at zero hosted providers, and one is enough to clear it. The result is a
                // quietly tiny provider list, permanent while offline. (R4-5)
                //
                // Unparseable rather than unreadable: Parse returns null instead of throwing, so this case
                // never reaches the catch below. Discard it either way - a cache that cannot be read will
                // fail identically on every subsequent start until something removes it.
                Log.Warning("Provider catalogue cache did not parse, or held implausibly few providers; " +
                            "discarding it (#736)");
                try { File.Delete(_cachePath); } catch { /* best effort */ }
                return null;
            }
            catch (Exception ex)
            {
                // A corrupt cache is recoverable - delete it so the next refresh starts clean, and fall
                // through to the snapshot rather than failing.
                Log.Warning(ex, "Provider catalogue cache unreadable; discarding it (#736)");
                try { File.Delete(_cachePath); } catch { /* best effort */ }
                return null;
            }
        }

        internal static IReadOnlyDictionary<string, CatalogProvider>? ReadSnapshot()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SnapshotResource);
                if (stream is null) return null;
                using var reader = new StreamReader(stream);
                return Parse(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "The embedded provider catalogue snapshot could not be read (#736)");
                return null;
            }
        }

        private void WriteCache(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);

                // Temp-then-rename: a process killed mid-write must not leave a half-file that the next
                // start reads as a corrupt cache.
                var tmp = _cachePath + ".tmp";
                File.WriteAllText(tmp, text);
                File.Move(tmp, _cachePath, overwrite: true);
            }
            catch (Exception ex)
            {
                // Not fatal: we have the document in memory for this session and will fetch again next time.
                Log.Warning(ex, "Could not write the provider catalogue cache (#736)");
            }
        }

        internal static IReadOnlyDictionary<string, CatalogProvider>? Parse(string text)
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, CatalogProvider>>(text, Json);
                if (raw is null) return null;

                var ok = new Dictionary<string, CatalogProvider>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in raw)
                {
                    // A record with no id cannot be used for anything, and its presence signals a document
                    // that is not what we think it is - skip rather than carry it.
                    if (value is null || string.IsNullOrWhiteSpace(value.Id)) continue;
                    ok[key] = value;
                }
                return ok;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
