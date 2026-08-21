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
    /// <param name="SelectionText">
    /// Text the reader has selected, in Latin. When it can be found inside the referenced paragraph, the
    /// window is built AROUND it rather than from the paragraph's start — half the budget behind, the rest
    /// ahead, neither crossing a section boundary. (#649)
    ///
    /// <para>Set only by the in-app assistant, which is the only caller that has a selection. It exists on
    /// the request rather than as a separate method so that "which window" stays one decision made in one
    /// place; every other caller leaves it null and gets exactly the behaviour it always had.</para>
    /// </param>
    /// <param name="MaxChars">Rendered-character budget for the window.</param>
    /// <param name="StructuredNotes">Return the apparatus as DATA instead of embedded braces: the <c>Text</c>
    /// comes back brace-free (clean, quotable Pāli) and each note appears in <see cref="PassageResult.Notes"/>
    /// with its offset, reading, and sigla. Independent of <c>IncludeFootnotes</c> (which controls the inline
    /// brace form). (#267)</param>
    public sealed record PassageRequest(
        string BookId,
        NavigationReference? Reference = null,
        int? Cursor = null,
        int MaxChars = 1200,
        Script OutputScript = Script.Latin,
        bool IncludeFootnotes = false,
        bool StructuredNotes = false,
        string? SelectionText = null);

    /// <summary>
    /// A reading window: the text, the citation refs at its start, and cursors to page through. Pass a cursor
    /// back as <see cref="PassageRequest.Cursor"/> to continue; a null cursor means the book start/end.
    /// </summary>
    /// <param name="NormalizedReference">Short human-readable reference for the window. Names a RANGE when the
    /// window spans more than one paragraph (e.g. "paragraphs 21-49 (kn2)") — the character budget is
    /// structurally blind, and on verse texts a modest budget covers many paragraphs, so a start-only reference
    /// understates the extent of what was returned. (#602)</param>
    /// <param name="EndParagraphNumber">The paragraph in effect at the window's END. Equal to
    /// <see cref="PassageResult.ParagraphNumber"/> when the window stayed within one paragraph.</param>
    /// <param name="Text">The passage text in the requested script.</param>
    /// <param name="Pages">The per-edition page(s) at the window start, for citation.</param>
    /// <param name="NoteCount">How many print-edition apparatus notes (<c>{…}</c>) fall in this window. Counted
    /// regardless of <c>IncludeFootnotes</c>, so <c>NoteCount &gt; 0</c> means apparatus is present here (re-read
    /// with <c>includeFootnotes:true</c> to see it). Apparatus lives almost only in MULA texts.</param>
    /// <param name="Notes">The apparatus notes as structured data (offset into <c>Text</c>, full text, and
    /// reading/sigla when simple), populated only when <c>StructuredNotes</c> was requested; empty otherwise. (#267)</param>
    public sealed record PassageResult(
        string BookId,
        string NormalizedReference,
        string Text,
        IReadOnlyList<SnippetPageRef> Pages,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        int? PrevCursor,
        int? NextCursor,
        int NoteCount,
        IReadOnlyList<ApparatusNote> Notes,
        int? EndParagraphNumber = null,
        string? EndParagraphBookCode = null,
        /// <summary>The selection was longer than the cap and was cut. (#672)</summary>
        bool SelectionTruncated = false);
}
