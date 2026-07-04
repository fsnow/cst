using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;

namespace CST.Tools
{
    /// <summary>
    /// The dictionary tool exposed to agents (AI_INTEGRATION.md surface C + §9). A thin wrapper over the
    /// app's dictionary service, which owns the on-disk format — an agent calls <c>lookup</c> and never
    /// parses dictionary files. The contract is format-agnostic, so added dictionaries don't change it.
    /// </summary>
    public interface IDictionaryTool
    {
        /// <summary>Available dictionary language codes (e.g. "en", "hi").</summary>
        IReadOnlyList<string> Languages { get; }

        /// <summary>
        /// Look up a headword (exact match plus the prefix run, or the nearest-neighbor run on a miss).
        /// The query may be in any script; headwords are returned in the requested output script.
        /// </summary>
        Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default);
    }

    /// <summary>A dictionary lookup request.</summary>
    public sealed record DictionaryRequest(
        string Language,
        string Query,
        Script OutputScript = Script.Latin,
        int MaxEntries = 25);

    /// <summary>One dictionary entry.</summary>
    /// <param name="Headword">The headword in the requested output script.</param>
    /// <param name="HeadwordIpe">The stable IPE form of the headword.</param>
    /// <param name="MeaningHtml">The definition as an HTML fragment (the source format).</param>
    public sealed record DictionaryEntry(
        string Headword,
        string HeadwordIpe,
        string MeaningHtml);
}
