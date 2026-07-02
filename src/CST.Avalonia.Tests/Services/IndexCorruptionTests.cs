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
    public class IndexCorruptionTests : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly IndexingService _service;

        public IndexCorruptionTests()
        {
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            // Create temporary directories for testing
            _testIndexDir = Path.Combine(Path.GetTempPath(), "CST.CorruptionTests", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(Path.GetTempPath(), "CST.XmlCorruptionTests", Guid.NewGuid().ToString());
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

        [Fact]
        public async Task IsIndexValidAsync_WithCorruptedIndexFiles_ReturnsFalse()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // Create a corrupted index file (just empty or invalid content)
            var corruptedFile = Path.Combine(_testIndexDir, "segments.gen");
            await File.WriteAllTextAsync(corruptedFile, "invalid content");
            
            var anotherFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(anotherFile, "more invalid content");

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            // Segment files exist but the index isn't readable -> invalid (SRCH-11).
            Assert.False(result);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithPartiallyDeletedIndex_ReturnsFalse()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // Create some index files then delete the directory partially
            var validFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(validFile, "content");
            
            // Delete all files to simulate corruption
            File.Delete(validFile);

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task BuildIndexAsync_WithCorruptedIndex_DeletesIndexAndResetsFileDates()
        {
            // Arrange - a present-but-unreadable index (garbage where segment files would be).
            await _service.InitializeAsync();
            var corruptSegments = Path.Combine(_testIndexDir, "segments_1");
            await File.WriteAllTextAsync(corruptSegments, "corrupted");
            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "_0.cfs"), "corrupted");

            // Recovery runs before re-detecting changed books; stop there so the test doesn't need the real
            // XML corpus to actually rebuild. The point is that corruption triggers recovery, not a skip. (SRCH-11)
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ThrowsAsync(new InvalidOperationException("stop after recovery"));

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.BuildIndexAsync(null!));

            // Assert - the corrupt index was deleted and the file-dates cache reset (forcing a full rebuild).
            _mockXmlFileDatesService.Verify(x => x.ResetFileDates(), Times.Once);
            Assert.False(File.Exists(corruptSegments));
        }

        [Fact]
        public async Task IsIndexValidAsync_WithNonReadableDirectory_ReturnsFalse()
        {
            // Arrange - use a path that will cause permission issues
            var unreadableDir = "/root/restricted"; // This won't exist and can't be created
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings 
                { 
                    IndexDirectory = unreadableDir,
                    XmlBooksDirectory = _testXmlDir
                });

            var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);

            // Act
            var result = await service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task BuildIndexAsync_WithExceptionDuringIndexing_PropagatesException()
        {
            // Arrange
            await _service.InitializeAsync();
            
            _mockXmlFileDatesService.Setup(x => x.GetChangedBooksAsync())
                .ThrowsAsync(new IOException("Disk full"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IOException>(
                async () => await _service.BuildIndexAsync(null!));
            
            Assert.Equal("Disk full", exception.Message);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithNonIndexFilesPresent_ReturnsFalse()
        {
            // Arrange - stray files that merely share index extensions are not a readable index. (SRCH-11)
            await _service.InitializeAsync();

            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "test.cfs"), "content");
            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "test.fdt"), "content");
            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "test.txt"), "content");

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task IsIndexValidAsync_WithOnlyStrayFdtFile_ReturnsFalse()
        {
            // Arrange
            await _service.InitializeAsync();

            await File.WriteAllTextAsync(Path.Combine(_testIndexDir, "test.fdt"), "content");

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            Assert.False(result);
        }
    }
}