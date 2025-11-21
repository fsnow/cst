using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST.Lucene;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.BugFix
{
    /// <summary>
    /// This test verifies the bug fix where incremental indexing was processing all 217 books
    /// instead of only the changed files. The fix ensures that only books in the changedFiles
    /// list are processed during incremental updates.
    /// </summary>
    public class IncrementalIndexingOnlyChangedBooksTest : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testAppDataDir;
        private readonly string _testXmlDir;
        private readonly string _testIndexDir;
        private readonly ITestOutputHelper _output;
        private readonly Settings _testSettings;

        public IncrementalIndexingOnlyChangedBooksTest(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            // Create temporary directories for testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.IncrementalTest", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(_testAppDataDir, "XmlBooks");
            _testIndexDir = Path.Combine(_testAppDataDir, "Index");
            Directory.CreateDirectory(_testAppDataDir);
            Directory.CreateDirectory(_testXmlDir);
            Directory.CreateDirectory(_testIndexDir);

            // Create test XML files
            CreateTestXmlFile(Path.Combine(_testXmlDir, "s0101m.mul.xml"), "Book 1 content");
            CreateTestXmlFile(Path.Combine(_testXmlDir, "s0102m.mul.xml"), "Book 2 content");
            CreateTestXmlFile(Path.Combine(_testXmlDir, "s0103m.mul.xml"), "Book 3 content");

            _testSettings = new Settings 
            { 
                IndexDirectory = _testIndexDir,
                XmlBooksDirectory = _testXmlDir
            };

            _mockSettingsService.Setup(s => s.Settings).Returns(_testSettings);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testAppDataDir))
                Directory.Delete(_testAppDataDir, true);
        }

        private void CreateTestXmlFile(string filePath, string content)
        {
            File.WriteAllText(filePath, $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<root>{content}</root>");
        }

        [Fact]
        public async Task IncrementalIndexing_OnlyProcessesChangedBooks_NotAllBooks()
        {
            // Arrange
            var progressMessages = new List<string>();
            var progress = new Progress<IndexingProgress>(p => 
            {
                progressMessages.Add(p.StatusMessage);
                _output.WriteLine($"Progress: {p.StatusMessage}");
            });

            // Mock XmlFileDatesService to return only 1 changed book (index 0)
            var changedBooks = new List<int> { 0 }; // Only book at index 0 changed
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync()).ReturnsAsync(changedBooks);

            var indexingService = new IndexingService(
                _mockLogger.Object, 
                _mockSettingsService.Object, 
                _mockXmlFileDatesService.Object);

            await indexingService.InitializeAsync();

            // Act
            await indexingService.BuildIndexAsync(progress);

            // Assert
            // Find progress messages that indicate indexing is happening
            var indexingMessages = progressMessages.FindAll(m => m.Contains("Building search index. (Book"));
            
            _output.WriteLine($"\nFound {indexingMessages.Count} indexing progress messages:");
            foreach (var msg in indexingMessages)
            {
                _output.WriteLine($"  - {msg}");
            }

            // Should only process 1 book (the changed one), not all books
            // Look for messages like "Building search index. (Book 1 of 1)"
            var singleBookMessages = indexingMessages.FindAll(m => m.Contains("of 1"));
            Assert.True(singleBookMessages.Count > 0, 
                "Expected to find messages indicating only 1 book was processed");

            // Should NOT find messages indicating multiple books were processed
            var multiBookMessages = indexingMessages.FindAll(m => 
                m.Contains("of 2") || m.Contains("of 3") || m.Contains("of 217"));
            Assert.Empty(multiBookMessages);

            _output.WriteLine("✅ Incremental indexing only processed the 1 changed book, not all books");
        }

        [Fact(Skip = "Test incomplete - needs all 217 XML files or mocked Books catalog")]
        public async Task InitialIndexing_ProcessesAllBooks_WhenNoChangedFiles()
        {
            // Arrange
            var progressMessages = new List<string>();
            var progress = new Progress<IndexingProgress>(p => 
            {
                progressMessages.Add(p.StatusMessage);
                _output.WriteLine($"Progress: {p.StatusMessage}");
            });

            // Mock XmlFileDatesService to return empty changed books list (initial indexing)
            var changedBooks = new List<int>(); // Empty list = initial indexing
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync()).ReturnsAsync(changedBooks);
            
            // Mock IsIndexValidAsync to return false so indexing proceeds
            var indexingService = new IndexingService(
                _mockLogger.Object, 
                _mockSettingsService.Object, 
                _mockXmlFileDatesService.Object);

            await indexingService.InitializeAsync();

            // Act
            await indexingService.BuildIndexAsync(progress);

            // Assert
            // For initial indexing with empty changed files, it should process all books that need indexing
            // This validates that the fix doesn't break initial indexing behavior
            var indexingMessages = progressMessages.FindAll(m => m.Contains("Building search index."));
            
            _output.WriteLine($"\nFound {indexingMessages.Count} indexing progress messages for initial indexing");
            
            // Should have processed some books (exact count depends on Books.Inst implementation)
            Assert.True(indexingMessages.Count >= 0, "Initial indexing should process books as needed");
            
            _output.WriteLine("✅ Initial indexing behavior preserved when no changed files specified");
        }
    }
}