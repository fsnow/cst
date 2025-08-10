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
    Task<List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, CancellationToken cancellationToken = default);
}