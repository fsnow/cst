using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
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
    private FSDirectory? _directory;
    private string? _directoryPath;
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

                // Reuse a single FSDirectory across reader refreshes; only (re)open it when missing
                // or the configured index path changes. Opening (and never disposing) a new
                // directory on every refresh leaked native handles for the life of the session.
                if (_directory == null || _directoryPath != indexPath)
                {
                    _directory?.Dispose();
                    _directory = FSDirectory.Open(indexPath);
                    _directoryPath = indexPath;
                }

                var newReader = DirectoryReader.Open(_directory);
                _indexReader?.Dispose();   // dispose the superseded (stale) reader
                _indexReader = newReader;
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

            // Normalize smart double-quotes to ASCII so pasted text still triggers phrase parsing.
            ipeTerm = ipeTerm.Replace("\u201C", "\"").Replace("\u201D", "\"");

            // Parse into units, honoring quoted phrases (do NOT pre-strip quotes). A unit is a
            // single word or a quoted phrase; within a phrase => adjacency, between units => the
            // proximity window. Richer than CST4's single whole-query phrase flag.
            var units = MultiWordSearch.ParseUnits(ipeTerm);

            // Diagnostic/UI flags retained for downstream consumers.
            query.IsPhrase = units.Any(u => u.IsPhrase);
            query.IsMultiWord = units.Count > 1 || (units.Count == 1 && units[0].IsPhrase);

            SearchResult result;
            if (units.Count == 0)
            {
                result = new SearchResult();
            }
            else if (units.Count == 1 && !units[0].IsPhrase)
            {
                result = await SearchSingleTermAsync(reader, units[0].Words[0], query, books, cancellationToken);
            }
            else
            {
                result = await SearchMultiWordAsync(reader, units, query, books, cancellationToken);
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
        if (matchingTerms.Count > termsToProcess.Count)
        {
            result.ResultsTruncated = true;
            result.TruncationMessage = $"Showing the first {termsToProcess.Count:N0} of {matchingTerms.Count:N0} matching words — refine your search to narrow the results.";
        }
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

    /// <summary>
    /// Multi-word / phrase search over a sequence of units. Within a phrase unit the words must be
    /// adjacent (query order); between units the matched occurrences must fall within a proximity
    /// window (all-within-a-window, measured between unit anchors). Produces MatchingTerm results
    /// with two-color highlighting data (first unit = blue, the rest = green). See
    /// <see cref="MultiWordSearch"/> for the pure matching logic and its unit tests.
    /// </summary>
    private async Task<SearchResult> SearchMultiWordAsync(
        DirectoryReader reader,
        List<SearchUnit> units,
        SearchQuery query,
        Books books,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Multi-word search: {UnitCount} units, IsPhrase={IsPhrase}, Distance={Distance}",
            units.Count, query.IsPhrase, query.ProximityDistance);

        var result = new SearchResult();
        var bookBits = CalculateBookBits(query.Filter, books);
        var liveDocs = MultiFields.GetLiveDocs(reader);

        // Step 1: expand wildcards for every word slot of every unit.
        // expandedUnits[u][slot] = concrete index words for that slot.
        var expandedUnits = new List<List<string>[]>(units.Count);
        int truncatedTermCount = 0;
        foreach (var unit in units)
        {
            var slots = new List<string>[unit.Words.Count];
            for (int s = 0; s < unit.Words.Count; s++)
            {
                var w = unit.Words[s];
                if (query.Mode == SearchMode.Wildcard && (w.Contains("*") || w.Contains("?")))
                {
                    slots[s] = ExpandWildcard(w, out var wasTruncated);
                    if (wasTruncated) truncatedTermCount++;
                }
                else
                {
                    slots[s] = new List<string> { w };
                }
            }
            expandedUnits.Add(slots);
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (truncatedTermCount > 0)
        {
            result.ResultsTruncated = true;
            result.TruncationMessage = $"{truncatedTermCount} wildcard word(s) matched more than {WildcardExpansionLimit:N0} forms and were truncated — some results may be missing. Use more specific wildcards for complete results.";
        }

        // Steps 2-3: gather positions for every expanded term in a SINGLE pass per term, bucketed
        // by book, then run the matcher per candidate book. This is O(total postings) rather than
        // O(books x terms) dictionary seeks - the difference between ~1s and ~60s for common stems.
        // slotPositionsByBook[unit][slot]: docId -> positions of any expansion of that slot in that book.
        var slotPositionsByBook = new List<Dictionary<int, List<TermPosition>>[]>(expandedUnits.Count);
        var sw = Stopwatch.StartNew();
        foreach (var slots in expandedUnits)
        {
            var perSlot = new Dictionary<int, List<TermPosition>>[slots.Length];
            for (int s = 0; s < slots.Length; s++)
            {
                var byDoc = new Dictionary<int, List<TermPosition>>();
                foreach (var term in slots[s])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var termBytes = new BytesRef(Encoding.UTF8.GetBytes(term));
                    var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes);
                    if (dape == null)
                        continue;

                    int docId;
                    while ((docId = dape.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS)
                    {
                        int freq = dape.Freq;
                        List<TermPosition>? list = null;
                        for (int i = 0; i < freq; i++)
                        {
                            int pos = dape.NextPosition();
                            int startOffset = dape.StartOffset;
                            int endOffset = dape.EndOffset;
                            if (startOffset < 0 || endOffset <= startOffset)
                                continue;

                            if (list == null && !byDoc.TryGetValue(docId, out list))
                            {
                                list = new List<TermPosition>();
                                byDoc[docId] = list;
                            }
                            list!.Add(new TermPosition
                            {
                                Position = pos,
                                StartOffset = startOffset,
                                EndOffset = endOffset,
                                Word = term
                            });
                        }
                    }
                }
                perSlot[s] = byDoc;
            }
            slotPositionsByBook.Add(perSlot);
        }
        _logger.LogInformation("Multi-word: gathered positions for all expanded terms in {Elapsed}ms", sw.ElapsedMilliseconds);

        // Candidate books = present in EVERY slot of EVERY unit, and in the active filter.
        HashSet<int>? candidateBooks = null;
        foreach (var perSlot in slotPositionsByBook)
        {
            foreach (var byDoc in perSlot)
            {
                if (candidateBooks == null)
                    candidateBooks = new HashSet<int>(byDoc.Keys);
                else
                    candidateBooks.IntersectWith(byDoc.Keys);
                if (candidateBooks.Count == 0) break;
            }
            if (candidateBooks != null && candidateBooks.Count == 0) break;
        }
        candidateBooks ??= new HashSet<int>();
        candidateBooks.RemoveWhere(docId =>
        {
            var b = books.FromDocId(docId);
            return b == null || !bookBits[b.Index];
        });

        _logger.LogInformation("Found {BookCount} candidate books containing all terms", candidateBooks.Count);

        // Per candidate book, resolve unit occurrences and combine into hits.
        var combos = new Dictionary<string, MatchingTerm>(); // key: word0~word1~...
        sw.Restart();

        foreach (var docId in candidateBooks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = books.FromDocId(docId);
            if (book == null)
                continue;

            var unitOccurrences = new List<List<UnitOccurrence>>(slotPositionsByBook.Count);
            bool anyEmpty = false;
            foreach (var perSlot in slotPositionsByBook)
            {
                var wordSlots = new List<TermPosition>[perSlot.Length];
                for (int s = 0; s < perSlot.Length; s++)
                {
                    if (!perSlot[s].TryGetValue(docId, out var list))
                    {
                        anyEmpty = true;
                        break;
                    }
                    list.Sort();
                    wordSlots[s] = list;
                }
                if (anyEmpty) break;

                var occs = MultiWordSearch.FindUnitOccurrences(wordSlots);
                if (occs.Count == 0) { anyEmpty = true; break; }
                unitOccurrences.Add(occs);
            }
            if (anyEmpty)
                continue;

            var hits = MultiWordSearch.FindHits(unitOccurrences, query.ProximityDistance);

            foreach (var hit in hits)
            {
                var ordered = hit.OrderBy(tp => tp.Position).ToList();
                string key = string.Join("~", ordered.Select(tp => tp.Word));

                if (!combos.TryGetValue(key, out var mt))
                {
                    mt = new MatchingTerm
                    {
                        Term = key,
                        DisplayTerm = string.Join(" ", ordered.Select(tp => ConvertToDisplayScript(tp.Word))),
                        Occurrences = new List<BookOccurrence>()
                    };
                    combos[key] = mt;
                }

                var occ = mt.Occurrences.FirstOrDefault(o => o.Book == book);
                if (occ == null)
                {
                    occ = new BookOccurrence { Book = book, Count = 0, Positions = new List<TermPosition>() };
                    mt.Occurrences.Add(occ);
                }

                occ.Positions.AddRange(hit);
                occ.Count++;
                mt.TotalCount++;
            }
        }
        _logger.LogInformation("Multi-word: matched {BookCount} candidate books in {Elapsed}ms", candidateBooks.Count, sw.ElapsedMilliseconds);

        // De-duplicate highlight positions per occurrence (a word can be shared by overlapping
        // hits); prefer the first-term marking. Then sort for highlighting.
        foreach (var mt in combos.Values)
        {
            foreach (var occ in mt.Occurrences)
            {
                var byOffset = new Dictionary<int, TermPosition>();
                foreach (var tp in occ.Positions)
                {
                    if (!byOffset.TryGetValue(tp.StartOffset, out var existing) || (tp.IsFirstTerm && !existing.IsFirstTerm))
                        byOffset[tp.StartOffset] = tp;
                }
                occ.Positions = byOffset.Values.OrderBy(p => p.Position).ToList();
            }
        }

        // Step 4: convert results
        // Order matching combinations alphabetically by their IPE words (word-by-word), matching
        // CST4 and the single-term path. The '~'-joined key sorts word0, then word1, etc.
        result.Terms = combos.Values.OrderBy(t => t.Term, StringComparer.Ordinal).ToList();
        result.TotalTermCount = result.Terms.Count;
        result.TotalOccurrenceCount = result.Terms.Sum(t => t.TotalCount);
        result.TotalBookCount = result.Terms.SelectMany(t => t.Occurrences).Select(o => o.Book).Distinct().Count();

        _logger.LogInformation("Multi-word search found {ComboCount} unique word combinations, {OccurrenceCount} total occurrences",
            result.TotalTermCount, result.TotalOccurrenceCount);

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
                        EndOffset = endOffset,
                        IsFirstTerm = true,  // Single-term search: all positions are "first term"
                        Word = term
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
    // Per-term wildcard expansion cap. High enough not to limit real Pali stems (which run to a
    // few hundred inflected/compound forms), low enough to refuse a runaway pattern like "*".
    // The matcher is position-based, not cartesian, so this bounds postings work, not E^N.
    private const int WildcardExpansionLimit = 5000;

    private List<string> ExpandWildcard(string pattern, out bool truncated, int maxResults = WildcardExpansionLimit)
    {
        truncated = false;
        var reader = _indexReader;
        if (reader == null)
            return new List<string>();

        // Convert wildcard to regex: "bhikkhu*" → "^bhikkhu.*$"
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern);

        var fields = MultiFields.GetFields(reader);
        var terms = fields?.GetTerms("text");
        if (terms == null)
            return new List<string>();

        // The term dictionary is sorted, so seek straight to the literal prefix that precedes the
        // first wildcard char and stop once we leave that prefix range — instead of scanning all
        // ~500K terms from the start on every call. A leading-wildcard pattern has no prefix and
        // still requires a full scan.
        int firstWild = pattern.IndexOfAny(new[] { '*', '?' });
        var literalPrefix = firstWild < 0 ? pattern : pattern.Substring(0, firstWild);

        var termsEnum = terms.GetEnumerator();
        var results = new List<string>();

        if (literalPrefix.Length > 0)
        {
            var status = termsEnum.SeekCeil(new BytesRef(Encoding.UTF8.GetBytes(literalPrefix)));
            if (status != TermsEnum.SeekStatus.END)
            {
                do
                {
                    var term = termsEnum.Term.Utf8ToString();
                    if (!term.StartsWith(literalPrefix, StringComparison.Ordinal))
                        break; // sorted: past the prefix range, nothing more can match
                    if (regex.IsMatch(term))
                    {
                        if (results.Count >= maxResults) { truncated = true; break; }
                        results.Add(term);
                    }
                } while (termsEnum.MoveNext());
            }
        }
        else
        {
            while (termsEnum.MoveNext())
            {
                var term = termsEnum.Term.Utf8ToString();
                if (regex.IsMatch(term))
                {
                    if (results.Count >= maxResults) { truncated = true; break; }
                    results.Add(term);
                }
            }
        }

        if (truncated)
            _logger.LogWarning("Wildcard '{Pattern}' matched more than {Max} terms; truncated. Use a more specific pattern for complete results.", pattern, maxResults);
        else
            _logger.LogDebug("Expanded wildcard '{Pattern}' to {Count} terms", pattern, results.Count);

        return results;
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

    private string GenerateCacheKey(SearchQuery query)
    {
        var sb = new StringBuilder();
        sb.Append(query.QueryText);
        sb.Append('|');
        sb.Append(query.Mode);
        sb.Append('|');
        // Proximity distance affects multi-word results, so it must be part of the key —
        // otherwise changing the Word Distance and re-running returns the stale cached result.
        sb.Append(query.ProximityDistance);
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
        lock (_readerLock)
        {
            _indexReader?.Dispose();
            _indexReader = null;
            _directory?.Dispose();
            _directory = null;
        }
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