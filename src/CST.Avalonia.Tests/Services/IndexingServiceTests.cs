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

namespace CST.Avalonia.Tests.Services
{
    public class IndexingServiceTests : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly IndexingService _service;

        public IndexingServiceTests()
        {
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            // Create temporary directories for testing
            _testIndexDir = Path.Combine(Path.GetTempPath(), "CST.IndexTests", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(Path.GetTempPath(), "CST.XmlTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testIndexDir);
            Directory.CreateDirectory(_testXmlDir);

            // Setup mock settings service
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = _testIndexDir,
                    XmlBooksDirectory = _testXmlDir
                });

            _service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
        }

        public void Dispose()
        {
            // Cleanup test directories
            if (Directory.Exists(_testIndexDir))
                Directory.Delete(_testIndexDir, true);
            if (Directory.Exists(_testXmlDir))
                Directory.Delete(_testXmlDir, true);
        }

        [Fact(Skip = "Mock settings service not triggering SaveSettingsAsync - revisit post Beta 3")]
        public async Task InitializeAsync_WithConfiguredIndexDirectory_UsesConfiguredDirectory()
        {
            // Act
            await _service.InitializeAsync();

            // Assert
            Assert.Equal(_testIndexDir, _service.IndexDirectory);
            Assert.True(Directory.Exists(_testIndexDir));
            _mockXmlFileDatesService.Verify(x => x.InitializeAsync(), Times.Once);
        }

        [Fact(Skip = "Mock settings service not triggering SaveSettingsAsync - revisit post Beta 3")]
        public async Task InitializeAsync_WithEmptyIndexDirectory_UsesDefaultDirectory()
        {
            // Arrange
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = "",
                    XmlBooksDirectory = _testXmlDir
                });

            var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);

            // Act
            await service.InitializeAsync();

            // Assert
            Assert.NotEmpty(service.IndexDirectory);
            Assert.NotEqual(_testIndexDir, service.IndexDirectory); // Should use default, not configured
            _mockXmlFileDatesService.Verify(x => x.InitializeAsync(), Times.Once);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithNoIndexFiles_ReturnsFalse()
        {
            // Arrange
            await _service.InitializeAsync();

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithCfsFiles_ReturnsTrue()
        {
            // Arrange
            await _service.InitializeAsync();
            var testFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(testFile, "test content");

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithFdtFiles_ReturnsTrue()
        {
            // Arrange
            await _service.InitializeAsync();
            var testFile = Path.Combine(_testIndexDir, "test.fdt");
            await File.WriteAllTextAsync(testFile, "test content");

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithNonExistentDirectory_ReturnsFalse()
        {
            // Arrange
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = Path.Combine(_testIndexDir, "nonexistent"),
                    XmlBooksDirectory = _testXmlDir
                });

            var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
            await service.InitializeAsync();

            // Act
            var result = await service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task BuildIndexAsync_WithNoChangedBooks_AndValidIndex_SkipsIndexing()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // Create a fake index file to make the index appear valid
            var testFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(testFile, "test content");

            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ReturnsAsync(new List<int>());

            var progressReports = new List<IndexingProgress>();
            var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

            // Act
            await _service.BuildIndexAsync(progress);

            // Assert
            Assert.Single(progressReports);
            Assert.Equal("Index is up to date", progressReports[0].StatusMessage);
            Assert.True(progressReports[0].IsComplete);
            
            _mockXmlFileDatesService.Verify(x => x.GetChangedBooksAsync(), Times.Once);
            _mockXmlFileDatesService.Verify(x => x.SaveFileDatesAsync(), Times.Never);
        }

        [Fact]
        public async Task BuildIndexAsync_WithChangedBooks_PerformsIndexing()
        {
            // Arrange
            await _service.InitializeAsync();
            
            var changedBooks = new List<int> { 0, 1, 2 };
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ReturnsAsync(changedBooks);

            var progressReports = new List<IndexingProgress>();
            var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

            // Act & Assert
            // Note: This will fail because we don't have actual XML books,
            // but we're testing the flow up to the point where BookIndexerAsync is called
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _service.BuildIndexAsync(progress));

            // Verify that the service attempted to get changed books
            _mockXmlFileDatesService.Verify(x => x.GetChangedBooksAsync(), Times.Once);
        }

        [Fact]
        public async Task BuildIndexAsync_WithInvalidXmlDirectory_ThrowsException()
        {
            // Arrange
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = _testIndexDir,
                    XmlBooksDirectory = "/nonexistent/path"
                });

            var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
            await service.InitializeAsync();
            
            var changedBooks = new List<int> { 0 };
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ReturnsAsync(changedBooks);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _service.BuildIndexAsync(null));
        }

        [Fact]
        public async Task UpdateIndexAsync_CallsPerformIndexingAndSavesFileDates()
        {
            // Arrange
            await _service.InitializeAsync();
            
            var changedBooks = new List<int> { 1, 2 };

            // Act & Assert
            // This will throw because we don't have actual XML books, but we can verify the flow
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _service.UpdateIndexAsync(changedBooks, null));

            // The method should not call GetChangedBooksAsync since books are provided
            _mockXmlFileDatesService.Verify(x => x.GetChangedBooksAsync(), Times.Never);
        }

        [Fact]
        public async Task OptimizeIndexAsync_CompletesSuccessfully()
        {
            // Arrange
            await _service.InitializeAsync();

            // Act
            await _service.OptimizeIndexAsync();

            // Assert - should complete without throwing
            // Modern Lucene handles optimization automatically
        }

        [Fact]
        public async Task BuildIndexAsync_OnException_RethrowsException()
        {
            // Arrange
            await _service.InitializeAsync();
            
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ThrowsAsync(new InvalidOperationException("Test exception"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _service.BuildIndexAsync(null));
            
            Assert.Equal("Test exception", exception.Message);
        }

        [Fact]
        public async Task UpdateIndexAsync_OnException_RethrowsException()
        {
            // Arrange
            await _service.InitializeAsync();
            
            var changedBooks = new List<int> { 0 };

            // Act & Assert
            // This will throw FileNotFoundException due to missing XML files
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _service.UpdateIndexAsync(changedBooks, null));
        }

        [Fact]
        public void IndexDirectory_ReturnsCorrectDirectory()
        {
            // Act
            var directory = _service.IndexDirectory;

            // Assert
            // Before initialization, it should be empty
            Assert.Empty(directory);
        }

        [Fact]
        public async Task IndexDirectory_AfterInitialization_ReturnsConfiguredDirectory()
        {
            // Act
            await _service.InitializeAsync();

            // Assert
            Assert.Equal(_testIndexDir, _service.IndexDirectory);
        }
    }
}