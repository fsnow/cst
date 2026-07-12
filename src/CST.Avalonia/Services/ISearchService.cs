using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
    Task<List<string>> GetAllTermsAsync(string prefix = "", int limit = 100, CancellationToken cancellationToken = default);
    Task<SearchResult> GetNextPageAsync(string continuationToken, CancellationToken cancellationToken = default);
    // term is a display-script term (e.g. the romanized Latin term from a prior search); it is converted to
    // IPE internally for the index lookup.
    Task<List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, CancellationToken cancellationToken = default);

    /// <summary>
    /// Book-scoped multi-word / proximity hits: parse <paramref name="query"/> into units (spaces = separate
    /// proximity units, quotes = an adjacent phrase), expand wildcard/regex slots per <paramref name="mode"/>,
    /// and return the co-occurrence hits within one book. Each hit is the matched word positions, exactly one
    /// flagged <see cref="TermPosition.IsFirstTerm"/> (the navigable anchor).
    /// </summary>
    Task<List<List<TermPosition>>> GetMultiWordPositionsAsync(
        string bookFileName, string query, SearchMode mode, int proximityDistance, CancellationToken cancellationToken = default);
}