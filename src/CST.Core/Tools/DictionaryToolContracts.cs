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
        /// <summary>Available dictionaries, each with its language code and source attribution (#268), so an
        /// agent producing scholarly output can cite the gloss and weigh its authority. <c>Source</c> is null
        /// when a dictionary carries no authoritative metadata — it is never inferred/guessed.</summary>
        IReadOnlyList<DictionaryLanguageInfo> Languages { get; }

        /// <summary>
        /// Look up a headword (exact match plus the prefix run, or the nearest-neighbor run on a miss).
        /// The query may be in any script; headwords are returned in the requested output script.
        /// </summary>
        Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default);
    }

    /// <summary>A dictionary's language code and its source attribution (null if none is recorded). (#268)</summary>
    public sealed record DictionaryLanguageInfo(string Language, DictionarySourceInfo? Source);

    /// <summary>Authoritative citation for a dictionary — populated verbatim from the dictionary's own
    /// <c>source.json</c>, never inferred. Every field is optional; an unpopulated source is null. (#268)</summary>
    public sealed record DictionarySourceInfo(
        string? Title,
        string? Compiler,
        string? Edition,
        string? Year,
        string? Publisher,
        string? License,
        string? Url);

    /// <summary>A dictionary lookup request.</summary>
    public sealed record DictionaryRequest(
        string Language,
        string Query,
        Script OutputScript = Script.Latin,
        int MaxEntries = 25);

    /// <summary>One dictionary entry.</summary>
    /// <param name="Headword">The headword in the requested output script.</param>
    /// <param name="MeaningHtml">The definition as an HTML fragment (the source format).</param>
    /// <param name="Source">The dictionary's title, for inline attribution (null when unrecorded); the full
    /// citation for this language is in <see cref="IDictionaryTool.Languages"/>. (#268)</param>
    public sealed record DictionaryEntry(
        string Headword,
        string MeaningHtml,
        string? Source);
}
