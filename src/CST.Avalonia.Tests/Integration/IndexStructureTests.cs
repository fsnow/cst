using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST;
using CST.Conversion;
using CST.Lucene;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CST.Avalonia.Tests.TestSupport;

namespace CST.Avalonia.Tests.Integration
{
    public class IndexStructureTests : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testIndexDir;
        private readonly IndexingService _service;

        public IndexStructureTests()
        {
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            _testIndexDir = Path.Combine(Path.GetTempPath(), "CST.StructureTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(_testIndexDir);

            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings { IndexDirectory = _testIndexDir });

            _service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_testIndexDir))
                System.IO.Directory.Delete(_testIndexDir, true);
        }

        // The exact FieldType BookIndexer writes (kept in lock-step with BookIndexer.cs). Since #55 the
        // corpus is indexed WITHOUT term vectors and WITHOUT norms — positions (proximity) and offsets
        // (highlighting) are read straight from the postings, never from term vectors, and search never
        // scores. Tests build docs through this so a divergence from the product config is caught here.
        private static FieldType TextFieldType()
        {
            var ft = new FieldType(TextField.TYPE_NOT_STORED)
            {
                IsIndexed = true,
                IsStored = false,
                IsTokenized = true,
                OmitNorms = true,
                StoreTermVectors = false,
                StoreTermVectorOffsets = false,
                StoreTermVectorPayloads = false,
                StoreTermVectorPositions = false,
                IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS
            };
            ft.Freeze();
            return ft;
        }

        [Fact]
        public void BookIndexer_WritesNoTermVectors_ButKeepsPostingsOffsets()
        {
            // Exercise the REAL BookIndexer (not the mirrored TextFieldType above) so that re-enabling term
            // vectors — or dropping offsets — in BookIndexer.cs is caught here. That silent index-bloat
            // regression is exactly what #55 removes; the config-mirror tests can't see BookIndexer drift.
            var root = Path.Combine(Path.GetTempPath(), "cst55-bookindexer-" + Guid.NewGuid().ToString("N"));
            var xmlDir = Path.Combine(root, "xml");
            var indexDir = Path.Combine(root, "index");
            System.IO.Directory.CreateDirectory(xmlDir);
            System.IO.Directory.CreateDirectory(indexDir);
            try
            {
                // A real catalog book so Books.Inst resolves it; synthetic Devanagari body generated from
                // ASCII-Latin (no non-Latin literals — the tokenizer converts Devanagari -> IPE).
                var book = Books.Inst.First(b => b.FileName.EndsWith(".mul.xml", StringComparison.Ordinal));
                string deva = ScriptConverter.Convert("dhamma citta kamma", Script.Latin, Script.Devanagari);
                string xml = "<body><div id=\"dn1\" type=\"book\"><pb ed=\"V\" n=\"1.0001\"/>" +
                             "<p rend=\"bodytext\" n=\"1\">" + deva + "</p></div></body>";
                File.WriteAllText(Path.Combine(xmlDir, book.FileName), xml, Encoding.Unicode);

                new BookIndexer { XmlDirectory = xmlDir, IndexDirectory = indexDir }
                    .IndexAll(_ => { }, new List<int> { book.Index });

                using var directory = FSDirectory.Open(indexDir);
                using var reader = DirectoryReader.Open(directory);
                Assert.True(reader.NumDocs > 0);

                // #55: the real indexer stores NO term vectors ...
                Assert.Null(reader.GetTermVectors(0));

                // ... but the postings still carry positions AND offsets (proximity + highlighting).
                var terms = MultiFields.GetTerms(reader, "text");
                Assert.NotNull(terms);
                Assert.True(terms.HasPositions);
                Assert.True(terms.HasOffsets);
            }
            finally
            {
                try { System.IO.Directory.Delete(root, true); } catch { /* best-effort temp cleanup */ }
            }
        }

        [Fact]
        public async Task IndexStructure_ServesPositionsAndOffsetsFromPostings_WithoutTermVectors()
        {
            // Arrange
            await _service.InitializeAsync();

            using var directory = FSDirectory.Open(_testIndexDir);
            using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
            var indexWriterConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);

            using (var indexWriter = new IndexWriter(directory, indexWriterConfig))
            {
                var doc = new Document();
                var ft = TextFieldType();

                // Add test content with various fields
                doc.Add(new Field("text", "बुद्ध धम्म संघ tipitaka pali", ft));
                doc.Add(new StringField("id", "1", Field.Store.YES));
                doc.Add(new StringField("FileName", "test.xml", Field.Store.YES));
                doc.Add(new StringField("MatnField", "Sutta", Field.Store.YES));
                doc.Add(new StringField("PitakaField", "Tipitaka", Field.Store.YES));

                indexWriter.AddDocument(doc);
                indexWriter.Commit();
            }

            // Act & Assert - Verify the index structure supports position-based operations
            using var reader = DirectoryReader.Open(directory);
            var terms = MultiFields.GetTerms(reader, "text");

            Assert.NotNull(terms);
            Assert.True(terms.Count > 0);
            // Postings must carry both positions and offsets (what search + highlighting read).
            Assert.True(terms.HasPositions);
            Assert.True(terms.HasOffsets);

            // Get the first document
            var doc0 = reader.Document(0);
            Assert.NotNull(doc0);

            // Verify stored fields are accessible
            Assert.Equal("1", doc0.Get("id"));
            Assert.Equal("test.xml", doc0.Get("FileName"));
            Assert.Equal("Sutta", doc0.Get("MatnField"));
            Assert.Equal("Tipitaka", doc0.Get("PitakaField"));

            // #55: term vectors are NO LONGER stored — highlighting/proximity read from the postings.
            Assert.Null(reader.GetTermVectors(0));

            // The property the (postings-based) highlight + proximity paths actually depend on: a term's
            // positions AND character offsets come back straight from the postings, no term vectors needed.
            // Use the first ACTUAL indexed term (the analyzer emits IPE, not ASCII) so this is encoding-agnostic.
            var termEnum = terms.GetEnumerator();
            Assert.True(termEnum.MoveNext());
            var firstTerm = BytesRef.DeepCopyOf(termEnum.Term);

            var liveDocs = MultiFields.GetLiveDocs(reader);
            var dape = MultiFields.GetTermPositionsEnum(
                reader, liveDocs, "text", firstTerm,
                DocsAndPositionsFlags.OFFSETS);
            Assert.NotNull(dape);
            Assert.NotEqual(DocIdSetIterator.NO_MORE_DOCS, dape.NextDoc());
            dape.NextPosition();
            Assert.True(dape.StartOffset >= 0);
            Assert.True(dape.EndOffset > dape.StartOffset);
        }

        [Fact]
        public async Task IndexIntegrityCheck_EmptyIndex_ReportsCorrectly()
        {
            // Arrange
            await _service.InitializeAsync();

            // Act
            var isValid = await _service.IsIndexValidAsync();

            // Assert
            Assert.False(isValid); // Empty index should be invalid
        }

        [Fact]
        public async Task IndexIntegrityCheck_CorruptedIndex_DetectedCorrectly()
        {
            // Arrange
            await _service.InitializeAsync();

            // Garbage that isn't a readable index -> invalid (SRCH-11: validity means openable, not
            // merely that files exist).
            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "segments.gen"), "corrupted content");
            Assert.False(await _service.IsIndexValidAsync());

            // A stray file with an index extension is still not a real index.
            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "test.cfs"), "fake content");
            Assert.False(await _service.IsIndexValidAsync());

            // A real index is valid.
            foreach (var f in System.IO.Directory.GetFiles(_testIndexDir)) File.Delete(f);
            TestIndex.CreateMinimal(_testIndexDir);
            Assert.True(await _service.IsIndexValidAsync());
        }

        [Fact]
        public async Task IndexStructure_HandlesMultipleDocuments()
        {
            // Arrange
            await _service.InitializeAsync();
            
            using var directory = FSDirectory.Open(_testIndexDir);
            using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
            var indexWriterConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            
            using (var indexWriter = new IndexWriter(directory, indexWriterConfig))
            {
                // Add multiple documents
                for (int i = 0; i < 5; i++)
                {
                    var doc = new Document();

                    var ft = TextFieldType();

                    doc.Add(new Field("text", $"Document {i} content धम्म", ft));
                    doc.Add(new StringField("id", i.ToString(), Field.Store.YES));
                    doc.Add(new StringField("FileName", $"doc{i}.xml", Field.Store.YES));
                    
                    indexWriter.AddDocument(doc);
                }
                
                indexWriter.Commit();
            }

            // Act & Assert
            using var reader = DirectoryReader.Open(directory);
            
            Assert.Equal(5, reader.NumDocs);
            
            // Verify each document has the expected structure
            for (int i = 0; i < 5; i++)
            {
                var doc = reader.Document(i);
                Assert.Equal(i.ToString(), doc.Get("id"));
                Assert.Equal($"doc{i}.xml", doc.Get("FileName"));

                // #55: term vectors are no longer stored for any document.
                Assert.Null(reader.GetTermVectors(i));
            }
        }

        [Fact]
        public void AnalyzerConfiguration_IsCorrectForPositionBasedSearch()
        {
            // Arrange & Act
            using var devaAnalyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
            using var ipeAnalyzer = new IpeAnalyzer(LuceneVersion.LUCENE_48);
            
            // Assert - analyzers should be properly configured
            Assert.NotNull(devaAnalyzer);
            Assert.NotNull(ipeAnalyzer);
            
            // Test that analyzers can create token streams (basic functionality test)
            using var devaStream = devaAnalyzer.GetTokenStream("text", new StringReader("धम्म"));
            using var ipeStream = ipeAnalyzer.GetTokenStream("text", new StringReader("dhamma"));
            
            Assert.NotNull(devaStream);
            Assert.NotNull(ipeStream);
        }

        [Fact]
        public async Task IndexDirectory_CreatedWithCorrectStructure()
        {
            // Arrange & Act
            await _service.InitializeAsync();

            // Assert
            Assert.True(System.IO.Directory.Exists(_testIndexDir));
            Assert.Equal(_testIndexDir, _service.IndexDirectory);
            
            // Directory should be empty initially but accessible
            var files = System.IO.Directory.GetFiles(_testIndexDir);
            // Should be empty initially
            Assert.Empty(files);
        }
    }
}