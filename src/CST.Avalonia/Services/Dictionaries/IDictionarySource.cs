using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Tools;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>A rendering/labeling hint for a dictionary source — not a lookup difference.</summary>
    public enum DictionarySourceKind
    {
        /// <summary>Word glosses (Childers, DPD).</summary>
        General,
        /// <summary>An entity / proper-name reference (DPPN).</summary>
        ProperNames
    }

    /// <summary>
    /// One dictionary the reader can look a word up in — a flat-file dictionary (en/hi), DPD, a downloaded
    /// lexicon (DPPN), or a user import. The <see cref="DictionarySourceRegistry"/> is the single place both the
    /// UI and the <c>/v1/dictionary</c> tool enumerate and query sources, so they can't drift (#466). A source
    /// that is not installed reports <see cref="IsAvailable"/> false rather than being absent, so the set is
    /// stable and a just-installed asset simply flips available.
    /// </summary>
    public interface IDictionarySource
    {
        /// <summary>Stable id, also the wire value on <c>/v1/dictionary</c> (e.g. "en", "hi", "dpd", "dppn").</summary>
        string Id { get; }

        /// <summary>Human-readable name for a source picker (e.g. "DPD", "Childers 1875").</summary>
        string DisplayName { get; }

        /// <summary>The language definitions are written in ("en", "hi"). Two sources may share it.</summary>
        string DefinitionLanguage { get; }

        DictionarySourceKind Kind { get; }

        /// <summary>Whether this source's data is present. Derived assets are present-iff-installed.</summary>
        bool IsAvailable { get; }

        /// <summary>Source attribution (#268), or null when unrecorded — never guessed.</summary>
        DictionarySourceInfo? Attribution { get; }

        /// <summary>Look up a headword. The request's <c>Language</c> is this source's id (already routed);
        /// the source uses <c>Query</c>, <c>OutputScript</c>, and <c>MaxEntries</c>.</summary>
        Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default);
    }
}
