using System.IO;
using CST.Lucene;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace CST.Avalonia.Tests.TestSupport
{
    /// <summary>
    /// Test helper that writes a real, openable Lucene index. Since IsIndexValidAsync now actually opens
    /// the index (SRCH-11), tests can no longer fake validity by dropping a stray <c>.cfs</c> file.
    /// </summary>
    public static class TestIndex
    {
        public static void CreateMinimal(string indexDir, int docCount = 1)
        {
            System.IO.Directory.CreateDirectory(indexDir);
            using var directory = FSDirectory.Open(indexDir);
            using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
            using var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
            for (int i = 0; i < docCount; i++)
            {
                var doc = new Document();
                doc.Add(new TextField("text", "\u0927\u092E\u094D\u092E", Field.Store.NO)); // "dhamma" (Devanagari)
                writer.AddDocument(doc);
            }
            writer.Commit();
        }
    }
}
