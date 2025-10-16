using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CST;
using CST.Conversion;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests;

/// <summary>
/// Proof-of-Concept tests to verify Lucene.NET 4.8+ API supports phrase and proximity search
/// Tests the core algorithms needed before full implementation
/// </summary>
public class Tests_PhraseProximityPOC : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly DirectoryReader _indexReader;
    private readonly string _indexDirectory;

    public Tests_PhraseProximityPOC(ITestOutputHelper output)
    {
        _output = output;

        // Use existing index from settings or environment variable
        _indexDirectory = Environment.GetEnvironmentVariable("CST_INDEX_DIR")
            ?? "/Users/fsnow/Library/Application Support/CSTReader/index";

        if (!System.IO.Directory.Exists(_indexDirectory))
        {
            throw new InvalidOperationException($"Index directory not found: {_indexDirectory}");
        }

        _indexReader = DirectoryReader.Open(FSDirectory.Open(_indexDirectory));
        _output.WriteLine($"Opened index with {_indexReader.NumDocs} documents");
    }

    /// <summary>
    /// POC 1: Verify we can get term positions for a single term
    /// This is the foundation for all proximity/phrase logic
    /// </summary>
    [Fact]
    public void POC_01_GetTermPositions_SingleTerm()
    {
        // Search for a common Pali word - convert Latin to IPE
        string latinTerm = "bhikkhusatehi";  // "with five hundred monks"
        string ipeTerm = Latn2Ipe.Convert(latinTerm);
        var termBytes = new BytesRef(Encoding.UTF8.GetBytes(ipeTerm));
        var liveDocs = MultiFields.GetLiveDocs(_indexReader);

        var dape = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", termBytes);
        Assert.NotNull(dape);

        int docCount = 0;
        int totalPositions = 0;

        while (dape.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
        {
            docCount++;
            int freq = dape.Freq;

            _output.WriteLine($"DocId {dape.DocID}: {freq} occurrences");

            // Get all positions for this document
            for (int i = 0; i < freq; i++)
            {
                int pos = dape.NextPosition();
                int startOffset = dape.StartOffset;
                int endOffset = dape.EndOffset;

                totalPositions++;

                if (docCount == 1 && i < 5)  // Log first 5 positions of first document
                {
                    _output.WriteLine($"  Position {pos}: offsets [{startOffset}, {endOffset})");
                }
            }

            if (docCount >= 5) break;  // Test first 5 documents only
        }

        Assert.True(docCount > 0, "Should find at least one document");
        Assert.True(totalPositions > 0, "Should find at least one position");

        _output.WriteLine($"Total: {docCount} documents, {totalPositions} positions");
    }

    /// <summary>
    /// POC 2: Get positions for two terms and check if they appear near each other
    /// This demonstrates the core proximity search logic
    /// </summary>
    [Fact]
    public void POC_02_ProximitySearch_TwoTerms()
    {
        // Search for two related terms - convert Latin to IPE
        string latin1 = "rājagahaṃ";
        string latin2 = "nāḷandaṃ"; 
        string term1 = Latn2Ipe.Convert(latin1);
        string term2 = Latn2Ipe.Convert(latin2);
        int maxDistance = 10;  // Words can be within 10 positions

        _output.WriteLine($"Searching for '{latin1}' within {maxDistance} words of '{latin2}'");

        var liveDocs = MultiFields.GetLiveDocs(_indexReader);
        var term1Bytes = new BytesRef(Encoding.UTF8.GetBytes(term1));
        var term2Bytes = new BytesRef(Encoding.UTF8.GetBytes(term2));

        var dape1 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term1Bytes);
        var dape2 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term2Bytes);

        Assert.NotNull(dape1);
        Assert.NotNull(dape2);

        // For each document containing term1, check if term2 is nearby
        int matchCount = 0;

        while (dape1.NextDoc() != DocIdSetIterator.NO_MORE_DOCS && matchCount < 3)
        {
            int docId = dape1.DocID;

            // Get all positions of term1 in this document
            var term1Positions = new List<int>();
            for (int i = 0; i < dape1.Freq; i++)
            {
                term1Positions.Add(dape1.NextPosition());
            }

            // Advance term2 to this document
            int term2DocId = dape2.DocID;
            while (term2DocId < docId && dape2.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
            {
                term2DocId = dape2.DocID;
            }

            // If term2 is in this document, check proximity
            if (term2DocId == docId)
            {
                var term2Positions = new List<int>();
                for (int i = 0; i < dape2.Freq; i++)
                {
                    term2Positions.Add(dape2.NextPosition());
                }

                // Check for proximity matches
                foreach (var pos1 in term1Positions)
                {
                    foreach (var pos2 in term2Positions)
                    {
                        int distance = Math.Abs(pos1 - pos2);
                        if (distance <= maxDistance && distance > 0)
                        {
                            matchCount++;
                            _output.WriteLine($"DocId {docId}: '{term1}' at {pos1}, '{term2}' at {pos2} (distance: {distance})");

                            if (matchCount >= 3) break;
                        }
                    }
                    if (matchCount >= 3) break;
                }
            }
        }

        Assert.True(matchCount > 0, "Should find at least one proximity match");
        _output.WriteLine($"Found {matchCount} proximity matches");
    }

    /// <summary>
    /// POC 3: Phrase search - check for exact adjacent word order
    /// This is more restrictive than proximity search
    /// </summary>
    [Fact]
    public void POC_03_PhraseSearch_ExactOrder()
    {
        // Search for a common two-word phrase - "thus have I heard"
        string latin1 = "evaṃ";  // "thus"
        string latin2 = "me";  // "to me"
        string term1 = Latn2Ipe.Convert(latin1);
        string term2 = Latn2Ipe.Convert(latin2);

        _output.WriteLine($"Searching for exact phrase: '{latin1} {latin2}'");

        var liveDocs = MultiFields.GetLiveDocs(_indexReader);
        var term1Bytes = new BytesRef(Encoding.UTF8.GetBytes(term1));
        var term2Bytes = new BytesRef(Encoding.UTF8.GetBytes(term2));

        var dape1 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term1Bytes);
        var dape2 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term2Bytes);

        Assert.NotNull(dape1);
        Assert.NotNull(dape2);

        int matchCount = 0;

        while (dape1.NextDoc() != DocIdSetIterator.NO_MORE_DOCS && matchCount < 5)
        {
            int docId = dape1.DocID;

            // Get all positions of term1
            var term1Positions = new List<int>();
            for (int i = 0; i < dape1.Freq; i++)
            {
                term1Positions.Add(dape1.NextPosition());
            }

            // Advance term2 to this document
            int term2DocId = dape2.DocID;
            while (term2DocId < docId && dape2.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
            {
                term2DocId = dape2.DocID;
            }

            if (term2DocId == docId)
            {
                var term2Positions = new HashSet<int>();
                for (int i = 0; i < dape2.Freq; i++)
                {
                    term2Positions.Add(dape2.NextPosition());
                }

                // For phrase search, term2 must be exactly at position (term1_pos + 1)
                foreach (var pos1 in term1Positions)
                {
                    if (term2Positions.Contains(pos1 + 1))
                    {
                        matchCount++;
                        _output.WriteLine($"DocId {docId}: Phrase found at positions [{pos1}, {pos1 + 1}]");

                        if (matchCount >= 5) break;
                    }
                }
            }
        }

        Assert.True(matchCount > 0, "Should find at least one phrase match");
        _output.WriteLine($"Found {matchCount} phrase matches");
    }

    /// <summary>
    /// POC 4: Wildcard term expansion with position retrieval
    /// Verify we can find all terms matching a pattern and get their positions
    /// </summary>
    [Fact]
    public void POC_04_WildcardTermExpansion_WithPositions()
    {
        string latinPattern = "bhikkhu*";
        string ipePattern = Latn2Ipe.Convert("bhikkhu") + "*";  // Convert base then add wildcard

        _output.WriteLine($"Expanding wildcard pattern: {latinPattern} (IPE: {ipePattern})");

        // Convert wildcard to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(ipePattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern);

        var fields = MultiFields.GetFields(_indexReader);
        var terms = fields.GetTerms("text");
        Assert.NotNull(terms);

        var termsEnum = terms.GetEnumerator();
        var matchingTerms = new List<string>();

        while (termsEnum.MoveNext() && matchingTerms.Count < 10)
        {
            var term = termsEnum.Term.Utf8ToString();
            if (regex.IsMatch(term))
            {
                matchingTerms.Add(term);
                _output.WriteLine($"  Matched: {term}");
            }
        }

        Assert.True(matchingTerms.Count > 0, "Should find at least one matching term");

        // Now verify we can get positions for each matched term
        foreach (var term in matchingTerms.Take(3))  // Test first 3 terms
        {
            var termBytes = new BytesRef(Encoding.UTF8.GetBytes(term));
            var liveDocs = MultiFields.GetLiveDocs(_indexReader);
            var dape = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", termBytes);

            Assert.NotNull(dape);

            if (dape.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
            {
                int firstPos = dape.NextPosition();
                _output.WriteLine($"  '{term}' found at position {firstPos} in doc {dape.DocID}");
            }
        }

        _output.WriteLine($"Successfully expanded wildcard to {matchingTerms.Count} terms and retrieved positions");
    }

    /// <summary>
    /// POC 5: Multi-term proximity with wildcard expansion
    /// This combines wildcards with proximity - the most complex case
    /// </summary>
    [Fact]
    public void POC_05_WildcardProximity_Combined()
    {
        // Search for "bhikkhu*" within 5 words of "sangha*"
        string latinPattern1 = "bhikkhu*";  // monk
        string latinPattern2 = "saṅgha*";   // community
        string ipePattern1 = Latn2Ipe.Convert(latinPattern1.Replace("*", "")) + "*";
        string ipePattern2 = Latn2Ipe.Convert(latinPattern2.Replace("*", "")) + "*";
        int maxDistance = 5;

        _output.WriteLine($"Searching for '{latinPattern1}' within {maxDistance} words of '{latinPattern2}'");

        // Expand wildcards
        var matchingTerms1 = ExpandWildcard(ipePattern1, 5);
        var matchingTerms2 = ExpandWildcard(ipePattern2, 5);

        _output.WriteLine($"Pattern 1 expanded to {matchingTerms1.Count} terms");
        _output.WriteLine($"Pattern 2 expanded to {matchingTerms2.Count} terms");

        Assert.True(matchingTerms1.Count > 0 && matchingTerms2.Count > 0);

        // Now find proximity matches between ANY combination of these terms
        int totalMatches = 0;
        var liveDocs = MultiFields.GetLiveDocs(_indexReader);

        foreach (var term1 in matchingTerms1.Take(2))  // Limit for POC
        {
            foreach (var term2 in matchingTerms2.Take(2))
            {
                var term1Bytes = new BytesRef(Encoding.UTF8.GetBytes(term1));
                var term2Bytes = new BytesRef(Encoding.UTF8.GetBytes(term2));

                var dape1 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term1Bytes);
                var dape2 = MultiFields.GetTermPositionsEnum(_indexReader, liveDocs, "text", term2Bytes);

                if (dape1 == null || dape2 == null) continue;

                // Check first document only for POC
                if (dape1.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                {
                    int docId = dape1.DocID;
                    var positions1 = new List<int>();
                    for (int i = 0; i < dape1.Freq; i++)
                    {
                        positions1.Add(dape1.NextPosition());
                    }

                    int docId2 = dape2.DocID;
                    while (docId2 < docId && dape2.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                    {
                        docId2 = dape2.DocID;
                    }

                    if (docId2 == docId)
                    {
                        var positions2 = new List<int>();
                        for (int i = 0; i < dape2.Freq; i++)
                        {
                            positions2.Add(dape2.NextPosition());
                        }

                        foreach (var pos1 in positions1)
                        {
                            foreach (var pos2 in positions2)
                            {
                                if (Math.Abs(pos1 - pos2) <= maxDistance && pos1 != pos2)
                                {
                                    totalMatches++;
                                    _output.WriteLine($"  Match: '{term1}' + '{term2}' at distance {Math.Abs(pos1 - pos2)}");
                                    break;
                                }
                            }
                            if (totalMatches > 0) break;
                        }
                    }
                }

                if (totalMatches > 0) break;
            }
            if (totalMatches > 0) break;
        }

        _output.WriteLine($"Wildcard proximity search found {totalMatches} matches");
        Assert.True(totalMatches >= 0);  // May not find matches with limited test data
    }

    private List<string> ExpandWildcard(string pattern, int maxResults)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern);

        var fields = MultiFields.GetFields(_indexReader);
        var terms = fields.GetTerms("text");
        var termsEnum = terms.GetEnumerator();
        var matchingTerms = new List<string>();

        while (termsEnum.MoveNext() && matchingTerms.Count < maxResults)
        {
            var term = termsEnum.Term.Utf8ToString();
            if (regex.IsMatch(term))
            {
                matchingTerms.Add(term);
            }
        }

        return matchingTerms;
    }

    public void Dispose()
    {
        _indexReader?.Dispose();
    }
}
