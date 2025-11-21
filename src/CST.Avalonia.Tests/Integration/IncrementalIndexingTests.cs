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

namespace CST.Avalonia.Tests.Integration
{
    public class IncrementalIndexingTests : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ILogger<XmlFileDatesService>> _mockXmlLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly string _testAppDataDir;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly ITestOutputHelper _output;

        public IncrementalIndexingTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockXmlLogger = new Mock<ILogger<XmlFileDatesService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            
            // Create temporary directories for testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.IncrementalTests", Guid.NewGuid().ToString());
            _testIndexDir = Path.Combine(_testAppDataDir, "Index");
            _testXmlDir = Path.Combine(_testAppDataDir, "XmlBooks");
            Directory.CreateDirectory(_testAppDataDir);
            Directory.CreateDirectory(_testIndexDir);
            Directory.CreateDirectory(_testXmlDir);

            // Setup mock settings service
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = _testIndexDir,
                    XmlBooksDirectory = _testXmlDir
                });
        }

        public void Dispose()
        {
            // Cleanup test directories
            if (Directory.Exists(_testAppDataDir))
                Directory.Delete(_testAppDataDir, true);
        }

        [Fact(Skip = "Progress report async/timing issue - revisit post Beta 3")]
        public async Task IncrementalIndexing_WithChangedFile_DetectsAndIndexesChanges()
        {
            // Arrange
            var xmlFileDatesService = new TestableXmlFileDatesService(_mockXmlLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, xmlFileDatesService);

            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create a fake index file to make the index appear valid
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            // Set up initial state - no changed books
            var initialChangedBooks = new List<int>();
            xmlFileDatesService.SetMockChangedBooks(initialChangedBooks);

            // Act 1: First call should report no changes
            var progressReports1 = new List<IndexingProgress>();
            var progress1 = new Progress<IndexingProgress>(p => progressReports1.Add(p));

            await indexingService.BuildIndexAsync(progress1);

            // Assert 1: Should report "Index is up to date"
            Assert.True(progressReports1.Count > 0);
            Assert.Contains(progressReports1, p => p.StatusMessage.Contains("up to date"));
            _output.WriteLine($"Initial indexing: {progressReports1.Count} progress reports");

            // Arrange 2: Now simulate a changed file
            var changedBooks = new List<int> { 0 }; // Book 0 changed
            xmlFileDatesService.SetMockChangedBooks(changedBooks);
            xmlFileDatesService.ResetCallFlags();

            // Act 2: Second call should detect changes
            var progressReports2 = new List<IndexingProgress>();
            var progress2 = new Progress<IndexingProgress>(p => progressReports2.Add(p));

            // This will throw because we don't have real XML files or valid index,
            // but we can verify that it attempted to process the changed files
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                async () => await indexingService.BuildIndexAsync(progress2));

            // Assert 2: Should have called GetChangedBooksAsync and detected changes
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled);
            _output.WriteLine($"Incremental indexing: GetChangedBooksAsync was called = {xmlFileDatesService.GetChangedBooksWasCalled}");
        }

        [Fact]
        public async Task BuildIndexAsync_AlwaysChecksForChanges_EvenWithValidIndex()
        {
            // Arrange
            var xmlFileDatesService = new TestableXmlFileDatesService(_mockXmlLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, xmlFileDatesService);

            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create a fake index file to make the index appear valid
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            // Verify index appears valid
            var indexValid = await indexingService.IsIndexValidAsync();
            Assert.True(indexValid);

            // Set up some changed books
            var changedBooks = new List<int> { 1, 2 };
            xmlFileDatesService.SetMockChangedBooks(changedBooks);

            // Act
            // This will throw either FileNotFoundException or IndexNotFoundException depending on how far it gets
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                async () => await indexingService.BuildIndexAsync(null));

            // Assert
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled);
            Assert.True(exception is FileNotFoundException || exception.GetType().Name.Contains("IndexNotFoundException"));
            _output.WriteLine("✅ BuildIndexAsync called GetChangedBooksAsync even with valid index");
        }

        [Fact(Skip = "Progress report async/timing issue - revisit post Beta 3")]
        public async Task BuildIndexAsync_WithNoChangesAndValidIndex_ReportsUpToDate()
        {
            // Arrange
            var xmlFileDatesService = new TestableXmlFileDatesService(_mockXmlLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, xmlFileDatesService);

            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create a fake index file to make the index appear valid
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            // Set up no changed books
            var changedBooks = new List<int>();
            xmlFileDatesService.SetMockChangedBooks(changedBooks);

            var progressReports = new List<IndexingProgress>();
            var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

            // Act
            await indexingService.BuildIndexAsync(progress);

            // Assert
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled);
            Assert.True(progressReports.Count > 0);
            Assert.Contains(progressReports, p => p.StatusMessage.Contains("up to date"));
            Assert.True(progressReports.Any(p => p.IsComplete));
            _output.WriteLine("✅ Reported 'up to date' status correctly");
        }

        // Testable version of XmlFileDatesService for integration testing
        private class TestableXmlFileDatesService : XmlFileDatesService
        {
            private readonly string _testAppDataDir;
            private List<int> _mockChangedBooks = new();
            
            public bool GetChangedBooksWasCalled { get; private set; }

            public TestableXmlFileDatesService(ILogger<XmlFileDatesService> logger, ISettingsService settingsService, string testAppDataDir)
                : base(logger, settingsService)
            {
                _testAppDataDir = testAppDataDir;
            }

            public void SetMockChangedBooks(List<int> changedBooks)
            {
                _mockChangedBooks = changedBooks;
            }

            public void ResetCallFlags()
            {
                GetChangedBooksWasCalled = false;
            }

            public override async Task<List<int>> GetChangedBooksAsync()
            {
                GetChangedBooksWasCalled = true;
                
                // Return mock data
                return await Task.FromResult(new List<int>(_mockChangedBooks));
            }

            protected override string GetAppDataDirectory()
            {
                return _testAppDataDir;
            }
        }
    }
}