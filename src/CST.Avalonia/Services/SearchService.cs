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
        
        _logger.LogInformation("Using {Mode} matcher for IPE term: '{IpeTerm}'", query.Mode, ipeTerm);

        // Get all matching terms
        var matchingTerms = await GetMatchingTermsAsync(reader, termMatcher, cancellationToken);

        var termsToProcess = matchingTerms.Take(query.PageSize).ToList();
        _logger.LogInformation("Processing {Count} terms for display conversion", termsToProcess.Count);
        
        // Debug: Log first and last few terms to understand the order
        if (termsToProcess.Count > 0)
        {
            var firstTerms = string.Join(", ", termsToProcess.Take(5));
            var lastTerms = string.Join(", ", termsToProcess.TakeLast(5));
            _logger.LogInformation("First 5 terms: {FirstTerms}", firstTerms);
            _logger.LogInformation("Last 5 terms: {LastTerms}", lastTerms);
        }
        
        var conversionStopwatch = Stopwatch.StartNew();
        foreach (var term in termsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var convertedTerm = ConvertToDisplayScript(term);
            var matchingTerm = new MatchingTerm
            {
                Term = term,
                DisplayTerm = convertedTerm
            };

            // Get occurrences for this term
            var occurrences = await GetTermOccurrencesAsync(reader, term, bookBits, books, cancellationToken);
            
            // Only add the term if it has occurrences in the selected books
            if (occurrences.Any())
            {
                matchingTerm.Occurrences = occurrences;
                matchingTerm.TotalCount = occurrences.Sum(o => o.Count);
                result.Terms.Add(matchingTerm);
            }
            else
            {
                _logger.LogDebug("Skipping term '{Term}' - no occurrences in selected books", term);
            }
            result.TotalOccurrenceCount += matchingTerm.TotalCount;
        }
        
        conversionStopwatch.Stop();
        _logger.LogInformation("Script conversion completed in {Elapsed}ms for {Count} terms", 
            conversionStopwatch.ElapsedMilliseconds, termsToProcess.Count);

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
        // Route to phrase/proximity search if enabled
        if (query.IsPhrase || query.ProximityDistance > 0)
        {
            return await SearchMultiWordAsync(reader, ipeTerms, query, books, cancellationToken);
        }

        // Fallback: search for each term independently (old behavior)
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

    /// <summary>
    /// Searches for multiple words with phrase or proximity matching.
    /// Based on CST4's GetMatchingTermsWithContext algorithm.
    /// </summary>
    /// <param name="reader">Index reader</param>
    /// <param name="ipeTerms">Array of IPE-encoded search terms</param>
    /// <param name="query">Search query with IsPhrase and ProximityDistance</param>
    /// <param name="books">Books collection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results with unique word pairs and multi-color highlighting data</returns>
    private async Task<SearchResult> SearchMultiWordAsync(
        DirectoryReader reader,
        string[] ipeTerms,
        SearchQuery query,
        Books books,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Multi-word search: {TermCount} terms, IsPhrase={IsPhrase}, Distance={Distance}",
            ipeTerms.Length, query.IsPhrase, query.ProximityDistance);

        var result = new SearchResult();
        var bookBits = CalculateBookBits(query.Filter, books);
        var liveDocs = MultiFields.GetLiveDocs(reader);

        // Step 1: Expand wildcards for each term
        var expandedTerms = new List<string>[ipeTerms.Length];
        for (int i = 0; i < ipeTerms.Length; i++)
        {
            var term = ipeTerms[i];

            if (query.Mode == SearchMode.Wildcard && (term.Contains("*") || term.Contains("?")))
            {
                expandedTerms[i] = ExpandWildcard(term, 100);
                _logger.LogDebug("Word {Index}: '{Term}' expanded to {Count} terms", i, term, expandedTerms[i].Count);
            }
            else
            {
                expandedTerms[i] = new List<string> { term };
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        // Step 2: Filter books - only keep books containing ALL terms (from at least one expansion)
        var booksWithAllTerms = new HashSet<int>();

        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = books.FromDocId(docId);
            if (book == null || !bookBits[book.Index])
                continue;

            bool hasAllTerms = true;

            // Check if this book contains at least one variant of each word
            foreach (var termVariants in expandedTerms)
            {
                bool hasThisTerm = false;

                foreach (var term in termVariants)
                {
                    var termBytes = new BytesRef(Encoding.UTF8.GetBytes(term));
                    var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes);

                    if (dape != null)
                    {
                        int advancedDoc = dape.Advance(docId);
                        if (advancedDoc == docId)
                        {
                            hasThisTerm = true;
                            break;
                        }
                    }
                }

                if (!hasThisTerm)
                {
                    hasAllTerms = false;
                    break;
                }
            }

            if (hasAllTerms)
            {
                booksWithAllTerms.Add(docId);
            }
        }

        _logger.LogInformation("Found {BookCount} books containing all terms", booksWithAllTerms.Count);

        // Step 3: For each book with all terms, find proximity/phrase matches
        var wordPairs = new Dictionary<string, MatchingTerm>(); // Key: "term1|term2"

        foreach (var docId in booksWithAllTerms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = books.FromDocId(docId);
            if (book == null)
                continue;

            // Get all combinations of term1 and term2 (only handling 2-word searches for now)
            if (expandedTerms.Length == 2)
            {
                foreach (var term1 in expandedTerms[0])
                {
                    foreach (var term2 in expandedTerms[1])
                    {
                        var positions1 = GetTermPositions(reader, liveDocs, term1, docId);
                        var positions2 = GetTermPositions(reader, liveDocs, term2, docId);

                        if (positions1.Count == 0 || positions2.Count == 0)
                            continue;

                        // Find matches based on search type
                        List<(int pos1, int pos2)> matches;

                        if (query.IsPhrase)
                        {
                            matches = FindPhraseMatches(positions1.Select(p => p.Position).ToList(),
                                                       positions2.Select(p => p.Position).ToList());
                        }
                        else
                        {
                            var proximityMatches = FindProximityMatches(
                                positions1.Select(p => p.Position).ToList(),
                                positions2.Select(p => p.Position).ToList(),
                                query.ProximityDistance);
                            matches = proximityMatches.Select(m => (m.pos1, m.pos2)).ToList();
                        }

                        if (matches.Count > 0)
                        {
                            // Create unique key for this word pair
                            string pairKey = $"{term1}|{term2}";

                            if (!wordPairs.ContainsKey(pairKey))
                            {
                                wordPairs[pairKey] = new MatchingTerm
                                {
                                    Term = pairKey,
                                    DisplayTerm = $"{ConvertToDisplayScript(term1)} {ConvertToDisplayScript(term2)}",
                                    Occurrences = new List<BookOccurrence>()
                                };
                            }

                            // Build TermPosition objects with WordIndex and IsFirstTerm
                            var termPositions = new List<TermPosition>();

                            foreach (var (pos1, pos2) in matches)
                            {
                                // Add first term position
                                var tp1 = positions1.FirstOrDefault(p => p.Position == pos1);
                                if (tp1 != null)
                                {
                                    termPositions.Add(new TermPosition
                                    {
                                        Position = tp1.Position,
                                        StartOffset = tp1.StartOffset,
                                        EndOffset = tp1.EndOffset,
                                        WordIndex = 0,
                                        IsFirstTerm = true,
                                        Word = term1
                                    });
                                }

                                // Add second term position
                                var tp2 = positions2.FirstOrDefault(p => p.Position == pos2);
                                if (tp2 != null)
                                {
                                    termPositions.Add(new TermPosition
                                    {
                                        Position = tp2.Position,
                                        StartOffset = tp2.StartOffset,
                                        EndOffset = tp2.EndOffset,
                                        WordIndex = 1,
                                        IsFirstTerm = false,
                                        Word = term2
                                    });
                                }
                            }

                            // Sort by position for proper highlighting
                            termPositions.Sort();

                            var occurrence = new BookOccurrence
                            {
                                Book = book,
                                Count = matches.Count,
                                Positions = termPositions
                            };

                            wordPairs[pairKey].Occurrences.Add(occurrence);
                            wordPairs[pairKey].TotalCount += matches.Count;
                        }
                    }
                }
            }
        }

        // Step 4: Convert results
        result.Terms = wordPairs.Values.ToList();
        result.TotalTermCount = result.Terms.Count;
        result.TotalOccurrenceCount = result.Terms.Sum(t => t.TotalCount);
        result.TotalBookCount = result.Terms.SelectMany(t => t.Occurrences).Select(o => o.Book).Distinct().Count();

        _logger.LogInformation("Multi-word search found {PairCount} unique word pairs, {OccurrenceCount} total occurrences",
            result.TotalTermCount, result.TotalOccurrenceCount);

        return result;
    }

    /// <summary>
    /// Helper to get term positions for a specific document.
    /// </summary>
    private List<TermPosition> GetTermPositions(DirectoryReader reader, IBits liveDocs, string term, int targetDocId)
    {
        var positions = new List<TermPosition>();
        var termBytes = new BytesRef(Encoding.UTF8.GetBytes(term));
        var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes);

        if (dape == null)
            return positions;

        int docId = dape.Advance(targetDocId);
        if (docId != targetDocId)
            return positions;

        int freq = dape.Freq;
        for (int i = 0; i < freq; i++)
        {
            int pos = dape.NextPosition();
            int startOffset = dape.StartOffset;
            int endOffset = dape.EndOffset;

            if (startOffset >= 0 && endOffset > startOffset)
            {
                positions.Add(new TermPosition
                {
                    Position = pos,
                    StartOffset = startOffset,
                    EndOffset = endOffset
                });
            }
        }

        return positions;
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

            CST.Book book = null;
            try
            {
                book = books.FromDocId(docId);
            }
            catch (KeyNotFoundException)
            {
                // DocId not in dictionary - skip this document
                _logger.LogDebug("DocId {DocId} not found in books dictionary", docId);
                docId = dape.NextDoc();
                continue;
            }
            
            if (book == null || !bookBits[book.Index])
            {
                docId = dape.NextDoc();
                continue;
            }

            // Get positions for highlighting
            var positions = new List<TermPosition>();
            int count = dape.Freq;
            
            for (int i = 0; i < count; i++)
            {
                int pos = dape.NextPosition();
                int startOffset = dape.StartOffset;
                int endOffset = dape.EndOffset;
                
                if (startOffset >= 0 && endOffset > startOffset)
                {
                    positions.Add(new TermPosition
                    {
                        Position = pos,
                        StartOffset = startOffset,
                        EndOffset = endOffset
                    });
                }
            }

            var occurrence = new BookOccurrence
            {
                Book = book,
                Count = count,
                Positions = positions
            };

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

    /// <summary>
    /// Expands a wildcard pattern (e.g., "bhikkhu*") to matching terms in the index.
    /// </summary>
    /// <param name="pattern">Wildcard pattern (* = any chars, ? = single char)</param>
    /// <param name="maxResults">Maximum number of terms to return (default 100)</param>
    /// <returns>List of matching IPE-encoded terms</returns>
    private List<string> ExpandWildcard(string pattern, int maxResults = 100)
    {
        var reader = _indexReader;
        if (reader == null)
            return new List<string>();

        // Convert wildcard to regex: "bhikkhu*" → "^bhikkhu.*$"
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern);

        var fields = MultiFields.GetFields(reader);
        var terms = fields.GetTerms("text");
        if (terms == null)
            return new List<string>();

        var termsEnum = terms.GetEnumerator();
        var results = new List<string>();

        while (termsEnum.MoveNext() && results.Count < maxResults)
        {
            var term = termsEnum.Term.Utf8ToString();
            if (regex.IsMatch(term))
            {
                results.Add(term);
            }
        }

        _logger.LogDebug("Expanded wildcard '{Pattern}' to {Count} terms", pattern, results.Count);
        return results;
    }

    /// <summary>
    /// Finds positions where two terms appear within specified distance.
    /// </summary>
    /// <param name="positions1">Positions of first term</param>
    /// <param name="positions2">Positions of second term</param>
    /// <param name="maxDistance">Maximum word distance (e.g., 10)</param>
    /// <returns>List of matching position pairs with distances</returns>
    private List<(int pos1, int pos2, int distance)> FindProximityMatches(
        List<int> positions1,
        List<int> positions2,
        int maxDistance)
    {
        var matches = new List<(int, int, int)>();
        foreach (var pos1 in positions1)
        {
            foreach (var pos2 in positions2)
            {
                int distance = Math.Abs(pos1 - pos2);
                if (distance <= maxDistance && distance > 0)
                {
                    matches.Add((pos1, pos2, distance));
                }
            }
        }
        return matches;
    }

    /// <summary>
    /// Finds positions where term2 appears exactly after term1 (adjacent words).
    /// </summary>
    /// <param name="positions1">Positions of first term</param>
    /// <param name="positions2">Positions of second term</param>
    /// <returns>List of matching adjacent position pairs</returns>
    private List<(int pos1, int pos2)> FindPhraseMatches(
        List<int> positions1,
        List<int> positions2)
    {
        var positions2Set = new HashSet<int>(positions2);
        var matches = new List<(int, int)>();

        foreach (var pos1 in positions1)
        {
            // For phrase search, term2 must be exactly at position (term1 + 1)
            if (positions2Set.Contains(pos1 + 1))
            {
                matches.Add((pos1, pos1 + 1));
            }
        }
        return matches;
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
        BitArray? bookBits = null;
        BitArray? clBits = null;
        BitArray? pitBits = null;
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
        if (clSelected && pitSelected && clBits != null && pitBits != null)
            bookBits = clBits.And(pitBits);
        else if (clSelected && clBits != null)
            bookBits = clBits;
        else if (pitSelected && pitBits != null)
            bookBits = pitBits;
        else
            bookBits = new BitArray(bookCount, false); // Exclude all if no filters

        // Add other texts if selected
        if (filter.IncludeOther && bookBits != null)
            bookBits = bookBits.Or(books.OtherBits);

        return bookBits ?? new BitArray(bookCount, false); // Return empty if somehow null
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
        _logger.LogDebug("Converting term to display script: {IpeTerm}", ipeTerm);
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
    private readonly string _regexPattern;
    
    public WildcardTermMatcher(string pattern)
    {
        _pattern = pattern;
        // Convert wildcard pattern to regex
        _regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(_pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        // Regex pattern created for wildcard search
    }
    
    public bool Matches(string term)
    {
        var matches = System.Text.RegularExpressions.Regex.IsMatch(term, _regexPattern);
        // Debug logging removed for production use
        return matches;
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