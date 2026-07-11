using System.ComponentModel;
using CST;
using CST.Avalonia.Services.LocalApi;
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
            + "book ids to read. The full catalog is 217 books and large — FILTER by pitaka and/or commentary "
            + "level to narrow it (e.g. pitaka:Abhidhamma), and page with skip/take. Returns "
            + "{ books, returnedCount, total, hasMore }.")]
        public static BookListResult Books_(
            [Description("Script for the returned book names.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("Restrict to a piṭaka: Vinaya, Sutta, Abhidhamma, or Other. Omit for all.")]
            Pitaka? pitaka = null,
            [Description("Restrict to a commentary level: Mula (root), Atthakatha (commentary), Tika (sub-commentary), or Other. Omit for all.")]
            CommentaryLevel? commentaryLevel = null,
            [Description("How many books to skip (paging).")]
            int skip = 0,
            [Description("How many books to return per page.")]
            int take = 100)
            => BookCatalog.List(McpScript.ToScript(outputScript), pitaka, commentaryLevel, skip, take);
    }
}
