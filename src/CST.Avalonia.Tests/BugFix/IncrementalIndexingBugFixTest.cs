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
    /// This test specifically validates the bug fix for incremental indexing.
    /// The bug was that App.axaml.cs only called BuildIndexAsync() when the index was invalid,
    /// but BuildIndexAsync() is designed to handle incremental updates when the index IS valid.
    /// </summary>
    public class IncrementalIndexingBugFixTest : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ILogger<XmlFileDatesService>> _mockXmlLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly string _testAppDataDir;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly ITestOutputHelper _output;

        public IncrementalIndexingBugFixTest(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockXmlLogger = new Mock<ILogger<XmlFileDatesService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            
            // Create temporary directories for testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.BugFixTest", Guid.NewGuid().ToString());
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

        [Fact]
        public async Task BugFix_BuildIndexAsync_AlwaysCallsGetChangedBooksAsync_EvenWithValidIndex()
        {
            // Arrange
            var xmlFileDatesService = new TestableXmlFileDatesService(_mockXmlLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, xmlFileDatesService);

            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create a fake valid index (this is what was causing the bug)
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            // Verify the index appears valid (this was the condition that triggered the bug)
            var isIndexValid = await indexingService.IsIndexValidAsync();
            Assert.True(isIndexValid, "Index should appear valid for this test");

            // Set up changed books - this should be detected even with a valid index
            var changedBooks = new List<int> { 0 }; // Book 0 changed
            xmlFileDatesService.SetMockChangedBooks(changedBooks);

            // Act
            // Before the bug fix, this would have been skipped in App.axaml.cs if index was valid
            // After the fix, this should always be called and should detect the changed books
            try
            {
                await indexingService.BuildIndexAsync(null);
            }
            catch (Exception ex)
            {
                // We expect an exception because we don't have real XML files or a real index
                // But the important thing is that GetChangedBooksAsync was called
                _output.WriteLine($"Expected exception occurred: {ex.GetType().Name}: {ex.Message}");
            }

            // Assert - This is the key test: GetChangedBooksAsync should have been called
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled, 
                "GetChangedBooksAsync should have been called even with a valid index");
            
            _output.WriteLine("✅ Bug Fix Verified: BuildIndexAsync calls GetChangedBooksAsync even with valid index");
        }

        [Fact]
        public async Task BugFix_BuildIndexAsync_WithNoChanges_ReportsUpToDate()
        {
            // Arrange
            var xmlFileDatesService = new TestableXmlFileDatesService(_mockXmlLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, xmlFileDatesService);

            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create a fake valid index
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            // Set up NO changed books
            var changedBooks = new List<int>(); // No changes
            xmlFileDatesService.SetMockChangedBooks(changedBooks);

            var progressReports = new List<IndexingProgress>();
            var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

            // Act
            await indexingService.BuildIndexAsync(progress);

            // Assert
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled, 
                "GetChangedBooksAsync should have been called");
            
            Assert.True(progressReports.Count > 0, 
                "Should have received progress reports");
            
            Assert.Contains(progressReports, p => p.StatusMessage.Contains("up to date") && p.IsComplete);
            
            _output.WriteLine("✅ Bug Fix Verified: Correctly reports 'up to date' when no changes found");
        }

        // Testable version of XmlFileDatesService that tracks method calls
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

            public override async Task<List<int>> GetChangedBooksAsync()
            {
                GetChangedBooksWasCalled = true;
                return await Task.FromResult(new List<int>(_mockChangedBooks));
            }

            protected override string GetAppDataDirectory()
            {
                return _testAppDataDir;
            }
        }
    }
}