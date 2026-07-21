using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP <c>navigate</c> tool — the one tool that ACTS on the user's reader rather than reading the corpus.
    /// It shares <see cref="NavigateService"/> with <c>POST /v1/navigate</c> so both surfaces behave identically,
    /// and is registered only when a reader is available; the consent gate is checked per call, not at
    /// registration, so revoking it in Settings takes effect immediately. (#187)
    /// </summary>
    [McpServerToolType]
    internal sealed class NavigateMcpTool
    {
        [McpServerTool(Name = "navigate")]
        [Description("Show a passage to the USER in the CST Reader window: open a book and optionally scroll to a "
            + "reference and highlight a query's matches. This is the only tool with a visible side effect — it "
            + "changes what the person in front of the app is looking at — so use it when the user asks to be "
            + "SHOWN something, not to read text yourself (use 'passage' for that). It requires the user to have "
            + "enabled remote control in Settings; if they haven't, this returns presented:false with an "
            + "explanation — tell the user how to enable it rather than trying another route. Always check "
            + "'presented': false means the reader did NOT move (app not running, book just opened, or nothing to "
            + "land on), and 'note' explains anything unexpected such as a query with no occurrences in that book.")]
        public static async Task<NavigateResponse> NavigateAsync(
            NavigateService navigate,
            [Description("Book id (file name) from the 'books' tool or a search result, e.g. 's0101m.mul.xml'.")]
            string bookId,
            [Description("Optional reference to scroll to: a page anchor ('V1.0023'), a paragraph ('para5'), or a chapter id.")]
            string? anchor = null,
            [Description("Optional query whose matches are highlighted in the book — same syntax as the 'search' tool.")]
            string? terms = null,
            [Description("Optional 1-based hit to land on. Requires 'terms'.")]
            int? hit = null,
            [Description("How to interpret 'terms'.")]
            SearchToolMode mode = SearchToolMode.Exact,
            [Description("Word distance for multi-word proximity matching (default 10).")]
            int? proximityDistance = null,
            [Description("Script to display the book in. Omit to keep the reader's current script.")]
            OutputScript? outputScript = null,
            CancellationToken cancellationToken = default)
        {
            var request = new NavigateRequest(
                bookId, anchor, terms, hit, McpSearchMode.ToSearchMode(mode), proximityDistance,
                outputScript is { } s ? McpScript.ToScript(s) : null);
            var (_, response) = await navigate.NavigateAsync(request, cancellationToken);
            return response;
        }
    }
}
