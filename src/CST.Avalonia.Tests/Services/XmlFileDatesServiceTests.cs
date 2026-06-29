using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    public class XmlFileDatesServiceTests : IDisposable
    {
        private readonly Mock<ILogger<XmlFileDatesService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly string _testAppDataDir;
        private readonly string _testXmlDir;
        private readonly XmlFileDatesService _service;

        public XmlFileDatesServiceTests()
        {
            _mockLogger = new Mock<ILogger<XmlFileDatesService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            
            // Create temporary directories for testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.Tests", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(Path.GetTempPath(), "CST.Tests.Xml", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testAppDataDir);
            Directory.CreateDirectory(_testXmlDir);

            // Setup mock settings service
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings { XmlBooksDirectory = _testXmlDir });

            _service = new XmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object);
        }

        public void Dispose()
        {
            // Cleanup test directories
            if (Directory.Exists(_testAppDataDir))
                Directory.Delete(_testAppDataDir, true);
            if (Directory.Exists(_testXmlDir))
                Directory.Delete(_testXmlDir, true);
        }

        [Fact]
        public async Task InitializeAsync_WithNoExistingCache_CreatesEmptyCache()
        {
            // Arrange
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);

            // Act
            await service.InitializeAsync();

            // Assert
            Assert.False(File.Exists(Path.Combine(_testAppDataDir, "file-dates.json")));
        }

        [Fact]
        public async Task InitializeAsync_WithExistingCache_LoadsCache()
        {
            // Arrange
            var testFileDates = new Dictionary<string, DateTime>
            {
                { "test1.xml", DateTime.UtcNow.AddDays(-1) },
                { "test2.xml", DateTime.UtcNow.AddDays(-2) }
            };
            
            var cachePath = Path.Combine(_testAppDataDir, "file-dates.json");
            var json = JsonSerializer.Serialize(testFileDates, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cachePath, json);

            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);

            // Act
            await service.InitializeAsync();

            // Assert - we can't directly verify the internal dictionary, but we can test behavior
            Assert.True(File.Exists(cachePath));
        }

        [Fact]
        public async Task GetChangedBooksAsync_WithEmptyXmlDirectory_ReturnsEmptyList()
        {
            // Arrange
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings { XmlBooksDirectory = "" });

            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            await service.InitializeAsync();

            // Act
            var result = await service.GetChangedBooksAsync();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetChangedBooksAsync_WithNonExistentXmlDirectory_ReturnsEmptyList()
        {
            // Arrange
            var nonExistentDir = Path.Combine(_testXmlDir, "nonexistent");
            _mockSettingsService.Setup(s => s.Settings)
                .Returns(new Settings { XmlBooksDirectory = nonExistentDir });

            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            await service.InitializeAsync();

            // Act
            var result = await service.GetChangedBooksAsync();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task SaveFileDatesAsync_CreatesJsonFile()
        {
            // Arrange
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            await service.InitializeAsync();

            // Act
            await service.SaveFileDatesAsync();

            // Assert
            var cachePath = Path.Combine(_testAppDataDir, "file-dates.json");
            Assert.True(File.Exists(cachePath));
            
            var json = await File.ReadAllTextAsync(cachePath);
            Assert.NotEmpty(json);
        }

        [Fact]
        public async Task SaveFileDatesAsync_WithInvalidPath_HandlesGracefully()
        {
            // Arrange - use an invalid path
            var invalidDir = Path.Combine(Path.GetTempPath(), "invalid\0path");
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, invalidDir);
            await service.InitializeAsync();

            // Act & Assert - should not throw
            await service.SaveFileDatesAsync();
        }

        [Fact]
        public void UpdateFileDate_WithValidBookIndex_UpdatesSuccessfully()
        {
            // Arrange
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var testDate = DateTime.UtcNow;

            // Act & Assert - should not throw for valid index (Books.Inst will handle validation)
            service.UpdateFileDate(0, testDate);
        }

        [Fact]
        public void UpdateFileDate_WithInvalidBookIndex_HandlesGracefully()
        {
            // Arrange
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            var testDate = DateTime.UtcNow;

            // Act & Assert - should not throw for invalid index
            service.UpdateFileDate(-1, testDate);
            service.UpdateFileDate(999999, testDate);
        }

        [Fact]
        public async Task SaveFileDatesDataAsync_PreservesExistingTimestamp_WhenIncomingIsNull()
        {
            // Regression for #40: after indexing records a real timestamp, XmlUpdateService re-saves the
            // file with a null timestamp (its download "needs indexing" marker). The save must preserve
            // the existing timestamp, else the file is dropped from the cache and re-indexed at startup.
            var service = new TestableXmlFileDatesService(_mockLogger.Object, _mockSettingsService.Object, _testAppDataDir);
            await service.InitializeAsync(); // sets the file-dates path
            var indexedTime = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);

            // 1. Indexing recorded a real timestamp + commit hash.
            await service.SaveFileDatesDataAsync(
                new Dictionary<string, FileCommitInfo>
                {
                    ["e0801n.nrf.xml"] = new FileCommitInfo { LastIndexedTimestamp = indexedTime, CommitHash = "abc123" }
                },
                "repoHash1");

            // 2. XmlUpdateService re-saves with a null timestamp (download marker) + new commit hash.
            await service.SaveFileDatesDataAsync(
                new Dictionary<string, FileCommitInfo>
                {
                    ["e0801n.nrf.xml"] = new FileCommitInfo { LastIndexedTimestamp = null, CommitHash = "def456" }
                },
                "repoHash2");

            // 3. Indexed timestamp preserved (not clobbered to null); commit hash takes the new value.
            var data = await service.GetFileDatesDataAsync();
            Assert.NotNull(data);
            Assert.True(data!.Files.TryGetValue("e0801n.nrf.xml", out var info));
            Assert.Equal(indexedTime, info!.LastIndexedTimestamp);
            Assert.Equal("def456", info.CommitHash);
        }

        // Testable version that allows us to override the app data directory
        private class TestableXmlFileDatesService : XmlFileDatesService
        {
            private readonly string _testAppDataDir;

            public TestableXmlFileDatesService(ILogger<XmlFileDatesService> logger, ISettingsService settingsService, string testAppDataDir)
                : base(logger, settingsService)
            {
                _testAppDataDir = testAppDataDir;
            }

            protected override string GetAppDataDirectory()
            {
                return _testAppDataDir;
            }
        }
    }
}