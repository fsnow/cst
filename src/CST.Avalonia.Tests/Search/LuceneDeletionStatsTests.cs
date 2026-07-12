using System;
using System.IO;
using System.Text;
using CST.Lucene;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// #311 A4-6: does Lucene 4.8's DocFreq/TotalTermFreq (the counts-only fast path) count deleted-but-unmerged
    /// docs, while the postings path filters liveDocs? If so, an incremental re-index that deletes a book without
    /// a forceMerge double-counts that book's terms until the next merge.
    /// </summary>
    public class LuceneDeletionStatsTests
    {
        private const string Term = "\u0927\u092E\u094D\u092E";   // "dhamma" (Devanagari)
        private readonly ITestOutputHelper _out;
        public LuceneDeletionStatsTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void DocFreq_vs_liveDocs_under_an_unmerged_deletion()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-delstats-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                using var directory = FSDirectory.Open(dir);
                using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);

                using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)))
                {
                    foreach (var file in new[] { "b0", "b1", "b2" })
                    {
                        var doc = new Document();
                        doc.Add(new StringField("file", file, Field.Store.YES));
                        doc.Add(new TextField("text", Term, Field.Store.NO));
                        writer.AddDocument(doc);
                    }
                    writer.Commit();
                    writer.DeleteDocuments(new Term("file", "b1"));   // incremental delete, NOT force-merged
                    writer.Commit();
                }

                using var reader = DirectoryReader.Open(directory);

                // The analyzer stores an analyzed (IPE) token, not the raw Devanagari — every doc shares the one
                // word, so just take the single indexed term rather than seeking by the source string.
                var termsEnum = MultiFields.GetTerms(reader, "text").GetEnumerator();
                Assert.True(termsEnum.MoveNext());
                var indexedTerm = termsEnum.Term;
                int docFreq = termsEnum.DocFreq;
                long totalTermFreq = termsEnum.TotalTermFreq;

                var liveDocs = MultiFields.GetLiveDocs(reader);
                var de = MultiFields.GetTermDocsEnum(reader, liveDocs, "text", indexedTerm);
                int liveCount = 0;
                while (de.NextDoc() != DocIdSetIterator.NO_MORE_DOCS) liveCount++;

                _out.WriteLine($"MaxDoc={reader.MaxDoc} NumDocs={reader.NumDocs} HasDeletions={reader.HasDeletions}");
                _out.WriteLine($"DocFreq={docFreq} TotalTermFreq={totalTermFreq} liveCount(postings+liveDocs)={liveCount}");

                Assert.True(reader.HasDeletions);
                Assert.Equal(2, reader.NumDocs);
                Assert.Equal(2, liveCount);            // the postings + liveDocs path sees the true live count
                // A4-6 confirmed: DocFreq/TotalTermFreq still count the deleted-but-unmerged doc, so the
                // counts-only fast path over-counts under deletions. This is why SearchService falls back to the
                // postings path when reader.HasDeletions.
                Assert.Equal(3, docFreq);
                Assert.Equal(3, totalTermFreq);
                Assert.True(docFreq > liveCount);
            }
            finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
        }
    }
}
