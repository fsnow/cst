using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CST;
using CST.Conversion;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP <c>books</c> tool — the corpus catalog, so an agent can resolve a bookId (file name) it got from
    /// search/occurrences into a human-readable name + classification, and discover ids to read. No service
    /// dependency (the catalog is static), so this tool is always available. (#191)
    /// </summary>
    [McpServerToolType]
    internal sealed class BooksMcpTool
    {
        [McpServerTool(Name = "books")]
        [Description("List the corpus's books: for each, its id (file name — the key the other tools take), its "
            + "full and short names in the requested script, its pitaka / commentary-level / type classification, "
            + "and whether it is indexed. Use this to turn a bookId into a recognizable location, or to find "
            + "book ids to read.")]
        public static IReadOnlyList<BookSummary> Books_(
            [Description("Script for the returned book names.")]
            OutputScript outputScript = OutputScript.Latin)
        {
            var target = McpScript.ToScript(outputScript);
            // Nav-path names are stored Devanagari; romanize to the requested script (like /v1/books).
            return Books.Inst.Select(b => new BookSummary(
                b.FileName,
                ScriptConverter.Convert(b.LongNavPath, Script.Devanagari, target),
                ScriptConverter.Convert(b.ShortNavPath, Script.Devanagari, target),
                b.Pitaka, b.Matn, b.BookType, b.DocId >= 0)).ToList();
        }
    }
}
