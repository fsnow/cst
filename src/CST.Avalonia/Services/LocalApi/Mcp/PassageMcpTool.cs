using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Navigation;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP <c>passage</c> tool over <see cref="IPassageTool"/> — reads a bounded window of a book's TEXT (the
    /// level-2 zoom above a search snippet), so an agent gets grounded source it can quote/cite. Start at a
    /// paragraph, or continue from a <c>cursor</c> returned by a prior passage / occurrence. (#191)
    /// </summary>
    [McpServerToolType]
    internal sealed class PassageMcpTool
    {
        [McpServerTool(Name = "passage")]
        [Description("Read a bounded window of a book's text, in the requested script. Provide a paragraph to "
            + "start there, or a cursor from a prior 'occurrences'/'passage' result to read the exact spot / "
            + "page forward. Both ends snap to sentence boundaries: a cursor (which points AT a hit) is pulled "
            + "back to the start of its enclosing sentence, and the window extends to the next sentence end — so "
            + "the hit is read with its full governing clause, never mid-sentence. Use the returned "
            + "nextCursor/prevCursor to page. With neither paragraph nor cursor, reads from the book start. "
            + "'noteCount' is how many {…} apparatus notes fall in the window (counted regardless of "
            + "includeFootnotes) — noteCount>0 means apparatus is present here.")]
        public static async Task<PassageResult> PassageAsync(
            IPassageTool passage,
            [Description("The book's id (file name), e.g. from the 'books' tool or a search result.")]
            string bookId,
            [Description("Paragraph number to start reading at. Omit to use cursor, or to start at the book's beginning.")]
            int? paragraph = null,
            [Description("Sub-book code for a Multi-type volume (e.g. 'an5'); usually omit.")]
            string? bookCode = null,
            [Description("A page cursor from a prior result; when set, overrides paragraph and reads that exact spot.")]
            int? cursor = null,
            [Description("Rendered-character budget for the window — a SOFT floor, not a ceiling. The window extends "
                + "past it to the next sentence end, and Pali sentences (with '…pe…' peyyala elisions) can be very "
                + "long, so the overshoot is unbounded and can be a large fraction over budget (occasionally several-"
                + "fold). Don't rely on maxChars for fixed-size chunking; page with the returned cursor instead.")]
            int maxChars = 1200,
            [Description("Script for the returned text.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("Include the print-edition footnotes (the braced {…} apparatus — variant readings, "
                + "cross-references, editorial notes) in the text. ONLY the {…} apparatus is toggled — parenthetical "
                + "(…) source citations in the body are always present; identical true/false output means no {…} "
                + "apparatus here, not 'no references'. Apparatus lives almost only in MULA texts.")]
            bool includeFootnotes = false,
            CancellationToken ct = default)
        {
            NavigationReference reference = paragraph is int n
                ? new NavigationReference.Paragraph(n, bookCode)
                : new NavigationReference.WholeBook();
            var request = new PassageRequest(
                BookId: bookId ?? string.Empty,
                Reference: reference,
                Cursor: cursor,
                MaxChars: maxChars,
                OutputScript: McpScript.ToScript(outputScript),
                IncludeFootnotes: includeFootnotes);
            return await passage.FetchPassageAsync(request, ct).ConfigureAwait(false);
        }
    }
}
