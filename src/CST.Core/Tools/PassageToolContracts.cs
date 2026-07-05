using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Navigation;
using CST.Search;

namespace CST.Tools
{
    /// <summary>
    /// The passage-fetch tool exposed to agents (AI_INTEGRATION.md surface C). Returns the *text* of a
    /// passage — not rendered HTML — as a bounded, paged reading window (the level-2 zoom above a search
    /// snippet), so an agent gets grounded source it can quote/cite without a wall of text. Text is in the
    /// requested script (default romanized Latin).
    /// </summary>
    public interface IPassageTool
    {
        /// <summary>Read a bounded window of text at a reference (or continue from a cursor), with page cursors.</summary>
        Task<PassageResult> FetchPassageAsync(PassageRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// A passage request. Provide either a <see cref="Reference"/> (paragraph — the ref an occurrence reports)
    /// to start reading there, or a <see cref="Cursor"/> from a previous result to page forward/backward. The
    /// window is bounded by <see cref="MaxChars"/> of rendered text and ends at a sentence boundary, so a
    /// long paragraph becomes page 1 of N rather than a wall.
    /// </summary>
    /// <param name="Cursor">A page cursor from a prior <see cref="PassageResult"/>; overrides <see cref="Reference"/> when set.</param>
    /// <param name="MaxChars">Rendered-character budget for the window.</param>
    public sealed record PassageRequest(
        string BookId,
        NavigationReference? Reference = null,
        int? Cursor = null,
        int MaxChars = 1200,
        Script OutputScript = Script.Latin,
        bool IncludeVariantReadings = false);

    /// <summary>
    /// A reading window: the text, the citation refs at its start, and cursors to page through. Pass a cursor
    /// back as <see cref="PassageRequest.Cursor"/> to continue; a null cursor means the book start/end.
    /// </summary>
    /// <param name="NormalizedReference">Short human-readable reference at the window start (e.g. "paragraph 123 (an5)").</param>
    /// <param name="Text">The passage text in the requested script.</param>
    /// <param name="Pages">The per-edition page(s) at the window start, for citation.</param>
    public sealed record PassageResult(
        string BookId,
        string NormalizedReference,
        string Text,
        IReadOnlyList<SnippetPageRef> Pages,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        int? PrevCursor,
        int? NextCursor);
}
