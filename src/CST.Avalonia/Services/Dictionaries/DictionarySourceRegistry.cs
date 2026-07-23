using System;
using System.Collections.Generic;
using System.Linq;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// The single registry of dictionary sources — the one list both the reader's dictionary panel and the
    /// <c>/v1/dictionary</c> tool read from, so the UI and the API can't advertise different sets (#466). The
    /// registered set is fixed at startup (derived assets activate on the next launch by design); which of them
    /// is <see cref="IDictionarySource.IsAvailable"/> is evaluated live, so an asset installed after launch is
    /// simply not yet available rather than unknown.
    /// </summary>
    public sealed class DictionarySourceRegistry
    {
        private readonly IReadOnlyList<IDictionarySource> _sources;

        public DictionarySourceRegistry(IEnumerable<IDictionarySource> sources)
        {
            // De-dup by id, first registration wins — a reserved id (e.g. "dpd") shadows a flat-file language of
            // the same name so it can't double-list. Preserves registration order otherwise.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sources = (sources ?? Enumerable.Empty<IDictionarySource>())
                .Where(s => s is not null && seen.Add(s.Id))
                .ToList();
        }

        /// <summary>Every registered source, available or not.</summary>
        public IReadOnlyList<IDictionarySource> All => _sources;

        /// <summary>The sources whose data is currently installed — what the UI and API advertise.</summary>
        public IReadOnlyList<IDictionarySource> Available => _sources.Where(s => s.IsAvailable).ToList();

        /// <summary>The available source with this id, or null. Case-insensitive, matching the wire.</summary>
        public IDictionarySource? ById(string? id) =>
            string.IsNullOrEmpty(id)
                ? null
                : _sources.FirstOrDefault(s => s.IsAvailable && string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
