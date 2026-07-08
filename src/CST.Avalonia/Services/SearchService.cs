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
    private const int SearchCacheMaxEntries = 50;
    private readonly BoundedCache<string, SearchResult> _searchCache = new(SearchCacheMaxEntries);
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

    // Returns a reference-counted DirectoryReader; the caller MUST release it with DecRef() in a
    // finally. Reference counting (Lucene IncRef/DecRef) lets a mid-session re-index open a fresh
    // reader without disposing one an in-flight search is still enumerating (which threw
    // AlreadyClosedException) - the old reader closes only once its last in-flight search DecRefs. (SRCH-2)
    private DirectoryReader AcquireReader()
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
                _indexReader?.DecRef();   // release our owner ref; closes once in-flight searches DecRef too
                _indexReader = newReader;
                _logger.LogInformation("Opened index reader with {DocCount} documents", _indexReader.NumDocs);

                // The index changed: drop cached search results so we never serve stale hits.
                _searchCache.Clear();
            }
            _indexReader.IncRef();   // hand out a counted reference; caller DecRefs in a finally
            return _indexReader;
        }
    }

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        DirectoryReader? reader = null;
        try
        {
            _logger.LogInformation("Searching for: {Query} with mode {Mode}", query.QueryText, query.Mode);

            // Refresh the reader first so a mid-session re-index clears stale cache entries
            // (the cache is cleared inside AcquireReader when the reader is reopened)
            // before we consult the cache below.
            reader = AcquireReader();

            // Check cache
            var cacheKey = GenerateCacheKey(query);
            if (_searchCache.TryGet(cacheKey, out var cachedResult) && cachedResult != null)
            {
                _logger.LogDebug("Returning cached result for query: {Query}", query.QueryText);
                return cachedResult;
            }

            var books = Books.Inst;

            // Ensure all books have DocIds (once per reader generation, not every search)
            EnsureDocIds(reader, books);

            // Convert search text to IPE (strip pasted zero-width joiners first, or they'd survive into
            // the IPE term and match no index term — SRCH-3).
            var ipeTerm = Any2Ipe.Convert(MultiWordSearch.StripJoiners(query.QueryText));
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

            // Cache the result (bounded; FIFO eviction)
            _searchCache.Set(cacheKey, result);

            _logger.LogInformation("Search completed in {Duration}ms with {TermCount} terms, {OccurrenceCount} occurrences",
                stopwatch.ElapsedMilliseconds, result.TotalTermCount, result.TotalOccurrenceCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query.QueryText);
            throw;
        }
        finally
        {
            reader?.DecRef();   // release the counted reference (SRCH-2)
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

        // Lazily enumerate matching terms (IPE-ordinal order); the budget below stops the enumeration early.
        var matchingTerms = GetMatchingTerms(reader, termMatcher, cancellationToken);

        // Apply the page budget (Skip + PageSize) to terms that SURVIVE the book filter. Truncating up
        // front (before checking occurrences) spent the budget on terms not in the selected books, dropping
        // legitimate results past the page size when the filter was narrow. (SRCH-12)
        var conversionStopwatch = Stopwatch.StartNew();
        var (selectedTerms, totalOccurrences, hasMore) = await SelectTermsWithinBudgetAsync(
            matchingTerms,
            query.Skip,
            query.PageSize,
            term => GetTermOccurrencesAsync(reader, term, bookBits, books, cancellationToken),
            ConvertToDisplayScript,
            cancellationToken);
        conversionStopwatch.Stop();

        result.Terms.AddRange(selectedTerms);
        result.TotalOccurrenceCount += totalOccurrences;
        result.HasMore = hasMore;
        // Keep the UI's "showing the first N" message for the first page (Skip == 0); the API pages via HasMore.
        if (hasMore && query.Skip == 0)
        {
            result.ResultsTruncated = true;
            result.TruncationMessage = $"Showing the first {query.PageSize:N0} matching words \u2014 refine your search to narrow the results.";
        }
        _logger.LogInformation("Script conversion completed in {Elapsed}ms for {Count} terms",
            conversionStopwatch.ElapsedMilliseconds, result.Terms.Count);

        result.TotalTermCount = result.Terms.Count;
        result.TotalBookCount = result.Terms.SelectMany(t => t.Occurrences).Select(o => o.Book).Distinct().Count();

        return result;
    }

    /// <summary>
    /// From an ordered list of index terms that matched the query, build up to <paramref name="pageSize"/>
    /// <see cref="MatchingTerm"/>s that actually occur in the selected books, preserving order. Terms with
    /// no occurrences in the selected books do NOT consume the page budget, so a narrow book filter can't
    /// drop legitimate terms past the page size (SRCH-12). Returns the built terms, their total occurrence
    /// count, and whether a filter-surviving term was left off the page.
    /// </summary>
    internal static async Task<(List<MatchingTerm> terms, int totalOccurrences, bool hasMore)> SelectTermsWithinBudgetAsync(
        IEnumerable<string> matchingTerms,
        int skip,
        int pageSize,
        Func<string, Task<List<BookOccurrence>>> getOccurrences,
        Func<string, string> toDisplay,
        CancellationToken cancellationToken)
    {
        var terms = new List<MatchingTerm>();
        int totalOccurrences = 0;
        int survivorsSeen = 0;   // filter-surviving terms seen so far - drives both skip and the hasMore (N+1) test

        foreach (var term in matchingTerms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var occurrences = await getOccurrences(term).ConfigureAwait(false);
            if (occurrences.Count == 0)
                continue; // not in the selected books - doesn't survive the filter, doesn't count toward the page

            survivorsSeen++;
            if (survivorsSeen <= skip)
                continue; // before the requested page

            if (terms.Count >= pageSize)
                return (terms, totalOccurrences, true); // a filter-surviving term exists AFTER the page -> hasMore

            var totalCount = occurrences.Sum(o => o.Count);
            terms.Add(new MatchingTerm
            {
                Term = term,
                DisplayTerm = toDisplay(term),
                Occurrences = occurrences,
                TotalCount = totalCount
            });
            totalOccurrences += totalCount;
        }

        return (terms, totalOccurrences, false);
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
                    slots[s] = ExpandWildcard(w, reader, out var wasTruncated);
                    if (wasTruncated) truncatedTermCount++;
                }
                else if (query.Mode == SearchMode.Regex)
                {
                    // Each unit of a multi-word regex must be expanded to its matching terms, just like the
                    // single-term path uses RegexTermMatcher. Previously regex units fell through to the
                    // literal branch below and matched nothing. (#60)
                    slots[s] = ExpandRegex(w, reader, out var wasTruncated);
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
                            // Offsets are exclusive (endOffset = one past the token's last source char),
                            // so valid tokens — including single-source-char ones like "ca" = च — have
                            // endOffset > startOffset. Drop only empty/corrupt spans.
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
                        DisplayTerm = string.Join(" ", ordered.Select(tp => ConvertToDisplayScript(tp.Word ?? string.Empty))),
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

            CST.Book? book = null;
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

    // Lazily yield every index term matching the pattern, in index (IPE-ordinal) order. LAZY so that paging
    // over a very broad pattern (e.g. a `.*` regex matching ~500k terms) stops enumerating as soon as the page
    // is filled, instead of materializing the whole match set. There is NO term cap here — the page budget
    // (Skip + PageSize) bounds the result and the agent pages via HasMore. (The 5,000-form
    // WildcardExpansionLimit is a separate UI-latency bound on multi-word slot expansion, not on this path.)
    private static IEnumerable<string> GetMatchingTerms(
        DirectoryReader reader,
        ITermMatcher matcher,
        CancellationToken cancellationToken)
    {
        var terms = MultiFields.GetFields(reader)?.GetTerms("text");
        if (terms == null) yield break;

        var termsEnum = terms.GetEnumerator();
        while (termsEnum.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var term = termsEnum.Term.Utf8ToString();
            if (matcher.Matches(term))
                yield return term;
        }
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

    // Uses the caller's own (ref-counted) reader, not the mutable _indexReader field, so a concurrent
    // reader refresh can't make a single multi-word search mix two index generations. (SRCH-2)
    private List<string> ExpandWildcard(string pattern, DirectoryReader reader, out bool truncated, int maxResults = WildcardExpansionLimit)
    {
        truncated = false;

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

    // Expand a Regex unit to the index terms it matches, for the multi-word path (mirrors how the
    // single-term path uses RegexTermMatcher). The pattern is already IPE-encoded and matched UNANCHORED
    // (a term that *contains* a match), exactly like RegexTermMatcher. If the pattern is start-anchored
    // (^lit...), seek to its literal prefix to avoid scanning all ~500K terms; otherwise full-scan. An
    // invalid pattern throws RegexParseException, surfaced upstream as the quiet "Invalid regex pattern"
    // hint (#59). (#60)
    private List<string> ExpandRegex(string pattern, DirectoryReader reader, out bool truncated, int maxResults = WildcardExpansionLimit)
    {
        truncated = false;

        var regex = new System.Text.RegularExpressions.Regex(pattern);

        var fields = MultiFields.GetFields(reader);
        var terms = fields?.GetTerms("text");
        if (terms == null)
            return new List<string>();

        // A start-anchored pattern can only match terms beginning with its literal prefix, so seek there.
        // An unanchored pattern matches substrings anywhere and must be full-scanned.
        var literalPrefix = StartAnchoredLiteralPrefix(pattern);

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
            _logger.LogWarning("Regex '{Pattern}' matched more than {Max} terms; truncated. Use a more specific pattern for complete results.", pattern, maxResults);
        else
            _logger.LogDebug("Expanded regex '{Pattern}' to {Count} terms", pattern, results.Count);

        return results;
    }

    // The literal prefix of a start-anchored regex: the run of non-metacharacter chars immediately after a
    // leading '^'. Returns "" if the pattern isn't start-anchored or has no literal prefix (=> full scan).
    private static string StartAnchoredLiteralPrefix(string pattern)
    {
        if (pattern.Length < 2 || pattern[0] != '^')
            return string.Empty;
        const string meta = "\\.*+?()[]{}|^$";
        var sb = new StringBuilder();
        for (int i = 1; i < pattern.Length; i++)
        {
            if (meta.IndexOf(pattern[i]) >= 0) break;
            sb.Append(pattern[i]);
        }
        return sb.ToString();
    }

    public async Task<List<string>> GetAllTermsAsync(string prefix = "", int limit = 100, CancellationToken cancellationToken = default)
    {
        DirectoryReader? reader = null;
        try
        {
            reader = AcquireReader();
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
        finally
        {
            reader?.DecRef();   // release the counted reference (SRCH-2)
        }
    }

    public async Task<SearchResult> GetNextPageAsync(string continuationToken, CancellationToken cancellationToken = default)
    {
        // TODO: Implement pagination using continuation token
        throw new NotImplementedException("Pagination not yet implemented");
    }

    public async Task<List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, CancellationToken cancellationToken = default)
    {
        DirectoryReader? reader = null;
        try
        {
            reader = AcquireReader();
            // Books' string indexer throws on an unknown file name; use a safe lookup so a bad bookId can't
            // become an unhandled 500 in the occurrences endpoint. (#186 cold test)
            var book = Books.Inst.FirstOrDefault(b =>
                string.Equals(b.FileName, bookFileName, StringComparison.OrdinalIgnoreCase));

            if (book == null || book.DocId < 0)
            {
                return new List<TermPosition>();
            }

            // term is a display-script term (e.g. the romanized Latin term from a prior search); convert it
            // to IPE for the index lookup. Any2Ipe auto-detects the input script.
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
        finally
        {
            reader?.DecRef();   // release the counted reference (SRCH-2)
        }
    }

    /// <summary>
    /// Book-scoped multi-word / proximity hits — the book-scoped sibling of <see cref="GetTermPositionsAsync"/>
    /// for reading a multi-word search in context (AI_INTEGRATION.md §6.1). Reuses the pure matcher
    /// (<see cref="MultiWordSearch"/>) over positions gathered for one book only.
    /// </summary>
    public Task<List<List<TermPosition>>> GetMultiWordPositionsAsync(
        string bookFileName, string query, SearchMode mode, int proximityDistance, CancellationToken cancellationToken = default)
    {
        var empty = new List<List<TermPosition>>();
        DirectoryReader? reader = null;
        try
        {
            reader = AcquireReader();
            var book = Books.Inst.FirstOrDefault(b =>
                string.Equals(b.FileName, bookFileName, StringComparison.OrdinalIgnoreCase));
            if (book == null || book.DocId < 0) return Task.FromResult(empty);
            int docId = book.DocId;
            var liveDocs = MultiFields.GetLiveDocs(reader);

            // Same query normalization as SearchAsync: strip pasted joiners, collapse spaces, normalize quotes.
            var ipe = Any2Ipe.Convert(MultiWordSearch.StripJoiners(query ?? string.Empty));
            ipe = ipe.Replace("  ", " ").Trim().Replace("\u201C", "\"").Replace("\u201D", "\"");
            var units = MultiWordSearch.ParseUnits(ipe);
            if (units.Count == 0) return Task.FromResult(empty);

            var unitOccurrences = new List<List<UnitOccurrence>>(units.Count);
            foreach (var unit in units)
            {
                var wordSlots = new List<TermPosition>[unit.Words.Count];
                for (int s = 0; s < unit.Words.Count; s++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var w = unit.Words[s];
                    List<string> expansions =
                        mode == SearchMode.Wildcard && (w.Contains('*') || w.Contains('?')) ? ExpandWildcard(w, reader, out _)
                        : mode == SearchMode.Regex ? ExpandRegex(w, reader, out _)
                        : new List<string> { w };

                    var positions = new List<TermPosition>();
                    foreach (var term in expansions)
                    {
                        var dape = MultiFields.GetTermPositionsEnum(reader, liveDocs, "text",
                            new BytesRef(Encoding.UTF8.GetBytes(term)));
                        if (dape == null || dape.Advance(docId) != docId) continue;
                        int freq = dape.Freq;
                        for (int i = 0; i < freq; i++)
                        {
                            int pos = dape.NextPosition();
                            int so = dape.StartOffset, eo = dape.EndOffset;
                            if (so < 0 || eo <= so) continue;
                            positions.Add(new TermPosition { Position = pos, StartOffset = so, EndOffset = eo, Word = term });
                        }
                    }
                    if (positions.Count == 0) return Task.FromResult(empty); // a slot unmatched in this book -> no hits
                    positions.Sort();
                    wordSlots[s] = positions;
                }
                var occs = MultiWordSearch.FindUnitOccurrences(wordSlots);
                if (occs.Count == 0) return Task.FromResult(empty);
                unitOccurrences.Add(occs);
            }

            return Task.FromResult(MultiWordSearch.FindHits(unitOccurrences, proximityDistance));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get multi-word positions for book: {Book}, query: {Query}", bookFileName, query);
            throw;
        }
        finally
        {
            reader?.DecRef();
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

    // The reader generation whose DocIds we've already applied to Books. DocIds only change when the
    // index (reader) changes, so re-running the whole MaxDoc scan on every search was wasted work AND
    // wrote booksByDocId from concurrent pool threads. Sync once per generation, comparing reader
    // identity (AcquireReader hands out the same instance until it reopens). (CORE-1)
    private DirectoryReader? _docIdSyncedReader;
    private readonly object _docIdSyncLock = new();

    private void EnsureDocIds(DirectoryReader reader, Books books)
    {
        lock (_docIdSyncLock)
        {
            if (ReferenceEquals(_docIdSyncedReader, reader))
                return;

            for (int i = 0; i < reader.MaxDoc; i++)
            {
                var doc = reader.Document(i);
                var fileName = doc.Get("file");
                if (!string.IsNullOrEmpty(fileName))
                {
                    books.SetDocId(fileName, i);
                }
            }

            _docIdSyncedReader = reader;
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
        sb.Append('|');
        // The cached SearchResult bakes its DisplayTerms in the script current at build time, so the
        // display script is part of the result's identity: without it, changing script and re-running
        // the same query returns the previous script's display terms. (SRCH-5)
        sb.Append(_scriptService.CurrentScript);
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
    private readonly System.Text.RegularExpressions.Regex _regex;

    public WildcardTermMatcher(string pattern)
    {
        // Convert the wildcard pattern to a regex and compile it once. Matches() is called for
        // every term in the index, so using the static Regex.IsMatch(term, pattern) per call
        // (which re-evaluates the pattern each time) was needless overhead.
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        _regex = new System.Text.RegularExpressions.Regex(
            regexPattern, System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    public bool Matches(string term) => _regex.IsMatch(term);
}

internal class RegexTermMatcher : ITermMatcher
{
    private readonly System.Text.RegularExpressions.Regex _regex;
    
    public RegexTermMatcher(string pattern)
    {
        // Compiled: Matches() runs against every term in the index for a regex search.
        _regex = new System.Text.RegularExpressions.Regex(
            pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
    }
    
    public bool Matches(string term) => _regex.IsMatch(term);
}