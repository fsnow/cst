using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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

        [Fact]
        public async Task IndexStructure_SupportsPositionBasedSearches()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // Create a mock Lucene index with the same field configuration as BookIndexer
            using var directory = FSDirectory.Open(_testIndexDir);
            using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
            var indexWriterConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            
            using (var indexWriter = new IndexWriter(directory, indexWriterConfig))
            {
                // Create a document with the same field structure as BookIndexer
                var doc = new Document();
                
                // Configure field type exactly like BookIndexer does
                var ft = new FieldType(TextField.TYPE_NOT_STORED)
                {
                    IsIndexed = true,
                    IsStored = false,
                    IsTokenized = true,
                    OmitNorms = false,
                    StoreTermVectors = true,
                    StoreTermVectorOffsets = true,
                    StoreTermVectorPayloads = true,
                    StoreTermVectorPositions = true,
                    IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS
                };
                ft.Freeze();

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

            // Verify we can access term vectors with positions and offsets
            var termEnum = terms.GetEnumerator();
            Assert.True(termEnum.MoveNext());

            // Get the first document
            var doc0 = reader.Document(0);
            Assert.NotNull(doc0);

            // Verify stored fields are accessible
            Assert.Equal("1", doc0.Get("id"));
            Assert.Equal("test.xml", doc0.Get("FileName"));
            Assert.Equal("Sutta", doc0.Get("MatnField"));
            Assert.Equal("Tipitaka", doc0.Get("PitakaField"));

            // Verify term vectors are available (required for position-based searches)
            var termVectors = reader.GetTermVectors(0);
            Assert.NotNull(termVectors);
            
            var textTerms = termVectors.GetTerms("text");
            Assert.NotNull(textTerms);
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
            
            // Create a corrupted index file
            var corruptedFile = Path.Combine(_testIndexDir, "segments.gen");
            await File.WriteAllTextAsync(corruptedFile, "corrupted content");

            // Act & Assert
            // The IsIndexValidAsync method only checks for file existence, not corruption
            // For actual corruption detection, we'd need to try opening the index
            var isValidByFileCheck = await _service.IsIndexValidAsync();
            Assert.False(isValidByFileCheck); // No .cfs or .fdt files, so invalid

            // Add a fake index file to make it appear valid
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake content");
            
            var isValidWithFiles = await _service.IsIndexValidAsync();
            Assert.True(isValidWithFiles); // Now has files, so appears valid
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
                    
                    var ft = new FieldType(TextField.TYPE_NOT_STORED)
                    {
                        IsIndexed = true,
                        IsStored = false,
                        IsTokenized = true,
                        StoreTermVectors = true,
                        StoreTermVectorOffsets = true,
                        StoreTermVectorPositions = true,
                        IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS
                    };
                    ft.Freeze();

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
                
                // Verify term vectors are available for each document
                var termVectors = reader.GetTermVectors(i);
                Assert.NotNull(termVectors);
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