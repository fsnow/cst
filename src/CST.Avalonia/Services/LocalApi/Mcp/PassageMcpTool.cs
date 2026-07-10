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
            + "nextCursor/prevCursor to page. With neither paragraph nor cursor, reads from the book start.")]
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
            [Description("Rendered-character budget for the window. May overshoot to reach the next sentence boundary, so the returned text can exceed this.")]
            int maxChars = 1200,
            [Description("Script for the returned text.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("Include apparatus/variant readings (braced) in the text.")]
            bool includeVariantReadings = false,
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
                IncludeVariantReadings: includeVariantReadings);
            return await passage.FetchPassageAsync(request, ct).ConfigureAwait(false);
        }
    }
}
