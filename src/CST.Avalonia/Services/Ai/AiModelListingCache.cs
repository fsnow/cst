using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Avalonia.Constants;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>The last listing we read from a connection's endpoint, and when. (#790)</summary>
    public sealed class AiCachedListing
    {
        [JsonPropertyName("fetchedAt")]
        public DateTimeOffset FetchedAt { get; set; }

        [JsonPropertyName("models")]
        public List<AiCatalogModel> Models { get; set; } = new();
    }

    /// <summary>
    /// Remembers each connection's model listing between sessions. (#790)
    ///
    /// <para><b>Why this exists.</b> The listing lived in memory for as long as the Models tab was open and
    /// was then thrown away, and it is fetched only when a group is first expanded. So every launch began by
    /// showing the reader their own saved models where the provider offers many more — three against thirteen,
    /// in the report that prompted this. The number was correct and useless: someone who configured a provider
    /// wants to see what it offers, and reading "3" immediately after an incident that really did lose models
    /// sent them looking for a loss that had not happened.</para>
    ///
    /// <para><b>What it is for, and what it is not.</b> This is what the reader SEES before a live fetch
    /// returns. It is never what the app CONCLUDES. In particular it must never drive #728's "no longer
    /// listed" marking, which needs a fetch that succeeded and was complete: a cache records what was true
    /// once, and marking from it would report a live model as retired — precisely the false alarm #728 exists
    /// to prevent.</para>
    ///
    /// <para><b>Not in <c>settings.json</c>.</b> It is the provider's data rather than the reader's
    /// configuration, it runs to hundreds of entries per connection (OpenRouter returns 422), and everything
    /// sharing that file shares its failure mode — which #784 is a fresh reminder of. It sits beside
    /// <c>models-dev.json</c>, which caches the provider catalogue for the same reasons.</para>
    ///
    /// <para><b>No freshness window.</b> <see cref="ModelsDevCatalog"/> has one because refetching it is a
    /// 4.2 MB download; a single connection's listing is small, and the rule here is simpler and easier to
    /// reason about: show the newest we have, and refresh whenever the reader looks. A stale entry is
    /// corrected the moment they open the tab, and until then it is a far better answer than their own
    /// saved list.</para>
    /// </summary>
    public interface IAiModelListingCache
    {
        /// <summary>The last listing read from this connection, or empty when there is none.</summary>
        IReadOnlyList<AiCatalogModel> Get(string connectionId);

        /// <summary>Record a listing. Only ever called with one a live fetch actually returned.</summary>
        void Put(string connectionId, IReadOnlyList<AiCatalogModel> models);

        /// <summary>
        /// Drop one connection's listing, because that connection was removed.
        ///
        /// <para>Without this a connection removed and recreated under the same id inherits the old one's
        /// listing — which looks like the app knowing something it cannot know, and is wrong the moment the
        /// new connection points somewhere else.</para>
        ///
        /// <para><b>Told, never inferred.</b> The first cut of this took the live connection list and deleted
        /// everything absent from it. That is wrong twice over, and both were caught by its own tests: it runs
        /// on every rebind including the first, where the list is empty because nothing has loaded yet and it
        /// deletes the entire cache; and mid-load it deletes the entry for every connection that has not been
        /// added back yet. A snapshot of what exists right now is not a statement that everything else is
        /// gone — the same reasoning behind #728's marking rules and <c>Reachability</c>'s third state.</para>
        /// </summary>
        void Forget(string connectionId);
    }

    /// <inheritdoc cref="IAiModelListingCache"/>
    public sealed class AiModelListingCache : IAiModelListingCache
    {
        private readonly string _path;
        private readonly object _gate = new();
        private Dictionary<string, AiCachedListing>? _entries;

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public AiModelListingCache(string? path = null) =>
            _path = path ?? Path.Combine(AppConstants.DataDirectory, "ai-model-listings.json");

        public IReadOnlyList<AiCatalogModel> Get(string connectionId)
        {
            lock (_gate)
            {
                Load();
                return _entries!.TryGetValue(connectionId, out var entry)
                    ? entry.Models
                    : Array.Empty<AiCatalogModel>();
            }
        }

        public void Put(string connectionId, IReadOnlyList<AiCatalogModel> models)
        {
            lock (_gate)
            {
                Load();
                _entries![connectionId] = new AiCachedListing
                {
                    FetchedAt = DateTimeOffset.Now,
                    Models = models.ToList(),
                };
                Save();
            }
        }

        public void Forget(string connectionId)
        {
            lock (_gate)
            {
                Load();
                if (_entries!.Remove(connectionId)) Save();
            }
        }

        /// <summary>
        /// Reads the file once per session, and treats every failure as "nothing cached".
        ///
        /// <para>A cache that cannot be read is an inconvenience — the reader waits for a fetch. Throwing
        /// would make it a failure to show the Models tab at all, which is a far worse trade for data whose
        /// entire purpose is to be a faster copy of something we can ask for again.</para>
        /// </summary>
        private void Load()
        {
            if (_entries is not null) return;

            try
            {
                if (File.Exists(_path))
                {
                    _entries = JsonSerializer
                        .Deserialize<Dictionary<string, AiCachedListing>>(File.ReadAllText(_path), Json);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read the model listing cache; treating it as empty (#790)");
            }

            _entries ??= new Dictionary<string, AiCachedListing>(StringComparer.Ordinal);
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                // Temp then replace, like every other file this app writes: a torn cache would read as empty
                // on the next start, which is recoverable, but there is no reason to accept even that.
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));

                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not write the model listing cache (#790)");
            }
        }
    }
}
