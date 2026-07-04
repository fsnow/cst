using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Navigation;

namespace CST.Tools
{
    /// <summary>
    /// The passage-fetch tool exposed to agents (AI_INTEGRATION.md surface C). Returns the *text* of a
    /// passage — not rendered HTML — so an agent gets grounded source it can quote/cite. Text is in the
    /// requested script (default romanized Latin). Unlike search/dictionary, this has no existing service to
    /// wrap: the implementation needs a pure-logic TEI reader (shared with the anchor catalog).
    /// </summary>
    public interface IPassageTool
    {
        /// <summary>Fetch the text at a reference, optionally with neighboring paragraphs for context.</summary>
        Task<PassageResult> FetchPassageAsync(PassageRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// A passage request. <see cref="Reference"/> reuses the navigation core's reference model (paragraph /
    /// page / chapter / raw anchor), so resolving and fetching speak the same language.
    /// </summary>
    /// <param name="ContextParagraphs">Neighbors to include on each side (0 = just the target).</param>
    /// <param name="IncludeMarkup">Return light structural markup instead of plain text.</param>
    public sealed record PassageRequest(
        string BookId,
        NavigationReference Reference,
        int ContextParagraphs = 0,
        Script OutputScript = Script.Latin,
        bool IncludeMarkup = false);

    /// <summary>The fetched passage plus the anchors needed to page forward/backward and cite pages.</summary>
    /// <param name="Anchor">The normalized anchor the text starts at.</param>
    /// <param name="NormalizedReference">Short human-readable reference (e.g. "paragraph 123 (an5)").</param>
    /// <param name="Text">The passage text in the requested script.</param>
    /// <param name="Pages">The per-edition page(s) this passage spans, for citation.</param>
    /// <param name="PreviousAnchor">Anchor of the preceding paragraph, or null at the start.</param>
    /// <param name="NextAnchor">Anchor of the following paragraph, or null at the end.</param>
    public sealed record PassageResult(
        string BookId,
        string Anchor,
        string NormalizedReference,
        string Text,
        IReadOnlyList<PassagePageRef> Pages,
        string? PreviousAnchor,
        string? NextAnchor);

    /// <summary>A page reference a passage spans (edition + volume + page).</summary>
    public sealed record PassagePageRef(PageEdition Edition, int Volume, int Number);
}
