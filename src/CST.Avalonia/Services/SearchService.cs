using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;
using CST.Lucene;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

public class SearchService : ISearchService
{
    private readonly ILogger<SearchService> _logger;
    private readonly IIndexingService _indexingService;
    private readonly IScriptService _scriptService;
    private readonly ISettingsService _settingsService;
    private DirectoryReader? _indexReader;
    private readonly Dictionary<string, SearchResult> _searchCache = new();
    private readonly object _readerLock = new();

    public SearchService(
        ILogger<SearchService> logger,
        IIndexingService indexingService,
        IScriptService scriptService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _indexingService = indexingService;
        _scriptService = scriptService;
        _settingsService = settingsService;
    }

    private async Task<DirectoryReader> GetIndexReaderAsync()
    {
        lock (_readerLock)
        {
            if (_indexReader == null || !_indexReader.IsCurrent())
            {
                var indexPath = _settingsService.Settings.IndexDirectory;
                if (string.IsNullOrEmpty(indexPath))
                {
                    throw new InvalidOperationException("Index directory not configured");
                }

                var directory = FSDirectory.Open(indexPath);
                _indexReader = DirectoryReader.Open(directory);
                _logger.LogInformation("Opened index reader with {DocCount} documents", _indexReader.NumDocs);
            }
            return _indexReader;
        }
    }

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Searching for: {Query} with mode {Mode}", query.QueryText, query.Mode);

            // Check cache
            var cacheKey = GenerateCacheKey(query);
            if (_searchCache.TryGetValue(cacheKey, out var cachedResult))
            {
                _logger.LogDebug("Returning cached result for query: {Query}", query.QueryText);
                return cachedResult;
            }

            var reader = await GetIndexReaderAsync();
            var books = Books.Inst;
            
            // Ensure all books have DocIds
            await EnsureDocIdsAsync(reader, books);

            // Convert search text to IPE
            var ipeTerm = Any2Ipe.Convert(query.QueryText);
            ipeTerm = ipeTerm.Replace("  ", " ").Trim();

            // Check for phrase search
            query.IsPhrase = ipeTerm.StartsWith("\"") || ipeTerm.EndsWith("\"");
            
            // Remove quotation marks
            ipeTerm = CleanQuotationMarks(ipeTerm);

            // Check for multi-word search
            query.IsMultiWord = ipeTerm.Contains(" ");

            SearchResult result;
            if (query.IsMultiWord)
            {
                var terms = ipeTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                result = await SearchMultipleTermsAsync(reader, terms, query, books, cancellationToken);
            }
            else
            {
                result = await SearchSingleTermAsync(reader, ipeTerm, query, books, cancellationToken);
            }

            stopwatch.Stop();
            result.SearchDuration = stopwatch.Elapsed;

            // Cache the result
            _searchCache[cacheKey] = result;

            _logger.LogInformation("Search completed in {Duration}ms with {TermCount} terms, {OccurrenceCount} occurrences",
                stopwatch.ElapsedMilliseconds, result.TotalTermCount, result.TotalOccurrenceCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query.QueryText);
            throw;
        }
    }

    private async Task<SearchResult> SearchSingleTermAsync(
        DirectoryReader reader,
        string ipeTerm,
        SearchQuery query,
        Books books,
        CancellationToken cancellationToken)
    {
        var result = new SearchResult();
        var bookBits = CalculateBookBits(query.Filter, books);

        // Create term matcher based on mode
        ITermMatcher termMatcher = query.Mode switch
        {
            SearchMode.Wildcard => new WildcardTermMatcher(ipeTerm),
            SearchMode.Regex => new RegexTermMatcher(ipeTerm),
            _ => new ExactTermMatcher(ipeTerm)
        };

        // Get all matching terms
        var matchingTerms = await GetMatchingTermsAsync(reader, termMatcher, cancellationToken);

        foreach (var term in matchingTerms.Take(query.PageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matchingTerm = new MatchingTerm
            {
                Term = term,
                DisplayTerm = ConvertToDisplayScript(term)
            };

            // Get occurrences for this term
            var occurrences = await GetTermOccurrencesAsync(reader, term, bookBits, books, cancellationToken);
            matchingTerm.Occurrences = occurrences;
            matchingTerm.TotalCount = occurrences.Sum(o => o.Count);

            result.Terms.Add(matchingTerm);
            result.TotalOccurrenceCount += matchingTerm.TotalCount;
        }

        result.TotalTermCount = result.Terms.Count;
        result.TotalBookCount = result.Terms.SelectMany(t => t.Occurrences).Select(o => o.Book).Distinct().Count();

        return result;
    }

    private async Task<SearchResult> SearchMultipleTermsAsync(
        DirectoryReader reader,
        string[] ipeTerms,
        SearchQuery query,
        Books books,
        CancellationToken cancellationToken)
    {
        // TODO: Implement multi-word search with proximity/phrase support
        // For now, search for each term independently
        var result = new SearchResult();
        
        foreach (var term in ipeTerms)
        {
            var singleQuery = new SearchQuery
            {
                QueryText = term,
                Mode = query.Mode,
                Filter = query.Filter,
                PageSize = query.PageSize
            };
            
            var singleResult = await SearchSingleTermAsync(reader, term, singleQuery, books, cancellationToken);
            
            // Merge results
            result.Terms.AddRange(singleResult.Terms);
            result.TotalOccurrenceCount += singleResult.TotalOccurrenceCount;
        }
        
        result.TotalTermCount = result.Terms.Count;
        result.TotalBookCount = result.Terms.SelectMany(t => t.Occurrences).Select(o => o.Book).Distinct().Count();
        
        return result;
    }

    private async Task<List<BookOccurrence>> GetTermOccurrencesAsync(
        DirectoryReader reader,
        string term,
        BitArray bookBits,
        Books books,
        CancellationToken cancellationToken)
    {
        var occurrences = new List<BookOccurrence>();
        var liveDocs = MultiFields.GetLiveDocs(reader);
        var termBytes = new BytesRef(Encoding.UTF8.GetBytes(term));
        
        var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes);
        if (dape == null) return occurrences;

        int lastDocId = -1;
        int docId = dape.NextDoc();

        while (docId != DocIdSetIterator.NO_MORE_DOCS)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Handle duplicate document IDs (as noted in test file)
            if (docId == lastDocId)
            {
                docId = dape.NextDoc();
                continue;
            }
            lastDocId = docId;

            var book = books.FromDocId(docId);
            if (book == null || !bookBits[book.Index])
            {
                docId = dape.NextDoc();
                continue;
            }

            var occurrence = new BookOccurrence
            {
                Book = book,
                Count = dape.Freq
            };

            // Get positions (limited to first 100 for performance)
            var positions = new List<TermPosition>();
            int posCount = 0;
            int maxPositions = Math.Min(dape.Freq, 100);
            
            while (posCount < maxPositions)
            {
                int pos = dape.NextPosition();
                int startOffset = dape.StartOffset;
                int endOffset = dape.EndOffset;

                positions.Add(new TermPosition
                {
                    Position = pos,
                    StartOffset = startOffset,
                    EndOffset = endOffset
                });

                posCount++;
            }

            occurrence.Positions = positions;
            occurrences.Add(occurrence);

            docId = dape.NextDoc();
        }

        return occurrences;
    }

    private async Task<List<string>> GetMatchingTermsAsync(
        DirectoryReader reader,
        ITermMatcher matcher,
        CancellationToken cancellationToken)
    {
        var matchingTerms = new List<string>();
        var fields = MultiFields.GetFields(reader);
        var terms = fields.GetTerms("text");
        
        if (terms == null) return matchingTerms;
        
        var termsEnum = terms.GetEnumerator();

        while (termsEnum.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var term = termsEnum.Term.Utf8ToString();
            if (matcher.Matches(term))
            {
                matchingTerms.Add(term);
            }
        }

        return matchingTerms;
    }

    public async Task<List<string>> GetAllTermsAsync(string prefix = "", int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = await GetIndexReaderAsync();
            var allTerms = new List<string>();
            var fields = MultiFields.GetFields(reader);
            var terms = fields.GetTerms("text");
            
            if (terms == null) return allTerms;
            
            var termsEnum = terms.GetEnumerator();
            var ipePrefix = string.IsNullOrEmpty(prefix) ? "" : Any2Ipe.Convert(prefix);

            while (termsEnum.MoveNext() && allTerms.Count < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var term = termsEnum.Term.Utf8ToString();
                if (string.IsNullOrEmpty(ipePrefix) || term.StartsWith(ipePrefix))
                {
                    allTerms.Add(ConvertToDisplayScript(term));
                }
            }

            return allTerms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all terms with prefix: {Prefix}", prefix);
            throw;
        }
    }

    public async Task<SearchResult> GetNextPageAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        // TODO: Implement pagination using continuation token
        throw new NotImplementedException("Pagination not yet implemented");
    }

    public async Task<List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = await GetIndexReaderAsync();
            var books = Books.Inst;
            var book = books[bookFileName];
            
            if (book == null || book.DocId < 0)
            {
                return new List<TermPosition>();
            }

            var ipeTerm = Any2Ipe.Convert(term);
            var termBytes = new BytesRef(Encoding.UTF8.GetBytes(ipeTerm));
            var liveDocs = MultiFields.GetLiveDocs(reader);
            var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes);
            
            if (dape == null) return new List<TermPosition>();

            var positions = new List<TermPosition>();
            
            while (dape.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
            {
                if (dape.DocID != book.DocId) continue;
                
                int posCount = 0;
                while (posCount < dape.Freq)
                {
                    int pos = dape.NextPosition();
                    int startOffset = dape.StartOffset;
                    int endOffset = dape.EndOffset;

                    positions.Add(new TermPosition
                    {
                        Position = pos,
                        StartOffset = startOffset,
                        EndOffset = endOffset
                    });

                    posCount++;
                }
                break;
            }

            return positions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get term positions for book: {Book}, term: {Term}", bookFileName, term);
            throw;
        }
    }

    private BitArray CalculateBookBits(BookFilter filter, Books books)
    {
        int bookCount = books.Count;
        BitArray bookBits = null;
        BitArray clBits = null;
        BitArray pitBits = null;
        bool clSelected = false;
        bool pitSelected = false;

        // Handle text class filters (Mula, Attha, Tika)
        if (filter.IncludeMula || filter.IncludeAttha || filter.IncludeTika)
        {
            clBits = new BitArray(bookCount);
            if (filter.IncludeMula)
                clBits = clBits.Or(books.MulaBits);
            if (filter.IncludeAttha)
                clBits = clBits.Or(books.AtthaBits);
            if (filter.IncludeTika)
                clBits = clBits.Or(books.TikaBits);
            clSelected = true;
        }

        // Handle Pitaka filters (Vinaya, Sutta, Abhidhamma)
        if (filter.IncludeVinaya || filter.IncludeSutta || filter.IncludeAbhidhamma)
        {
            pitBits = new BitArray(bookCount);
            if (filter.IncludeVinaya)
                pitBits = pitBits.Or(books.VinayaBits);
            if (filter.IncludeSutta)
                pitBits = pitBits.Or(books.SuttaBits);
            if (filter.IncludeAbhidhamma)
                pitBits = pitBits.Or(books.AbhiBits);
            pitSelected = true;
        }

        // Combine filters
        if (clSelected && pitSelected)
            bookBits = clBits.And(pitBits);
        else if (clSelected)
            bookBits = clBits;
        else if (pitSelected)
            bookBits = pitBits;
        else
            bookBits = new BitArray(bookCount, true); // Include all if no filters

        // Add other texts if selected
        if (filter.IncludeOther)
            bookBits = bookBits.Or(books.OtherBits);

        return bookBits;
    }

    private async Task EnsureDocIdsAsync(DirectoryReader reader, Books books)
    {
        // Ensure all books have their DocIds set
        for (int i = 0; i < reader.MaxDoc; i++)
        {
            var doc = reader.Document(i);
            var fileName = doc.Get("file");
            if (!string.IsNullOrEmpty(fileName))
            {
                books.SetDocId(fileName, i);
            }
        }
    }

    private string ConvertToDisplayScript(string ipeTerm)
    {
        var currentScript = _scriptService.CurrentScript;
        return ScriptConverter.Convert(ipeTerm, Script.Ipe, currentScript);
    }

    private string CleanQuotationMarks(string text)
    {
        return text
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("\u2019", "")  // Right single quotation mark
            .Replace("\u201C", "")  // Left double quotation mark
            .Replace("\u201D", "")  // Right double quotation mark
            .Replace("\u201E", "");  // Double low-9 quotation mark
    }

    private string GenerateCacheKey(SearchQuery query)
    {
        var sb = new StringBuilder();
        sb.Append(query.QueryText);
        sb.Append('|');
        sb.Append(query.Mode);
        sb.Append('|');
        sb.Append(query.Filter.IncludeVinaya);
        sb.Append(query.Filter.IncludeSutta);
        sb.Append(query.Filter.IncludeAbhidhamma);
        sb.Append(query.Filter.IncludeMula);
        sb.Append(query.Filter.IncludeAttha);
        sb.Append(query.Filter.IncludeTika);
        sb.Append(query.Filter.IncludeOther);
        return sb.ToString();
    }

    public void Dispose()
    {
        _indexReader?.Dispose();
    }
}

// Term matching interfaces and implementations
internal interface ITermMatcher
{
    bool Matches(string term);
}

internal class ExactTermMatcher : ITermMatcher
{
    private readonly string _target;
    
    public ExactTermMatcher(string target)
    {
        _target = target;
    }
    
    public bool Matches(string term) => term == _target;
}

internal class WildcardTermMatcher : ITermMatcher
{
    private readonly string _pattern;
    
    public WildcardTermMatcher(string pattern)
    {
        _pattern = pattern;
    }
    
    public bool Matches(string term)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(_pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(term, regexPattern);
    }
}

internal class RegexTermMatcher : ITermMatcher
{
    private readonly System.Text.RegularExpressions.Regex _regex;
    
    public RegexTermMatcher(string pattern)
    {
        _regex = new System.Text.RegularExpressions.Regex(pattern);
    }
    
    public bool Matches(string term) => _regex.IsMatch(term);
}