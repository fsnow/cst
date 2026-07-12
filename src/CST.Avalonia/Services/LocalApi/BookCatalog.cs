using System.Collections.Generic;
using System.Linq;
using CST;
using CST.Conversion;
using CST.Tools;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Shared projection for the book catalog behind BOTH the MCP <c>books</c> tool and the HTTP
    /// <c>/v1/books</c> route. Optional piṭaka / commentary-level filters + paging keep the 217-book catalog
    /// from overflowing an agent's context (the Cowork friction report: an unfiltered list was ~121K chars).
    /// Nav-path names are stored Devanagari; romanized to the requested script.
    /// </summary>
    internal static class BookCatalog
    {
        public const int DefaultTake = 100;

        public static BookListResult List(
            Script outputScript, Pitaka? pitaka, CommentaryLevel? commentaryLevel, int skip, int take)
        {
            if (skip < 0) skip = 0;
            if (take < 1) take = DefaultTake;

            var filtered = Books.Inst.Where(b =>
                (pitaka is null || b.Pitaka == pitaka.Value)
                && (commentaryLevel is null || b.Matn == commentaryLevel.Value)).ToList();

            var page = filtered.Skip(skip).Take(take)
                .Select(b => new BookSummary(
                    b.FileName,
                    ScriptConverter.Convert(b.LongNavPath, Script.Devanagari, outputScript),
                    ScriptConverter.Convert(b.ShortNavPath, Script.Devanagari, outputScript),
                    b.Pitaka, b.Matn, b.BookType))
                .ToList();

            return new BookListResult(page, page.Count, filtered.Count, skip + page.Count < filtered.Count);
        }
    }
}
