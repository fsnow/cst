using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.BugFix
{
    /// <summary>
    /// This test validates that when no index directory is configured, 
    /// the default index directory gets saved to settings.
    /// </summary>
    public class DefaultIndexDirectorySettingTest : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ILogger<XmlFileDatesService>> _mockXmlLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testAppDataDir;
        private readonly string _testXmlDir;
        private readonly ITestOutputHelper _output;
        private readonly Settings _testSettings;

        public DefaultIndexDirectorySettingTest(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockXmlLogger = new Mock<ILogger<XmlFileDatesService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            // Create temporary directories for testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.SettingsTest", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(_testAppDataDir, "XmlBooks");
            Directory.CreateDirectory(_testAppDataDir);
            Directory.CreateDirectory(_testXmlDir);

            // Create a settings object with empty IndexDirectory to simulate first run
            _testSettings = new Settings 
            { 
                IndexDirectory = "", // Empty to trigger default behavior
                XmlBooksDirectory = _testXmlDir
            };

            // Setup mock settings service
            _mockSettingsService.Setup(s => s.Settings).Returns(_testSettings);
            _mockSettingsService.Setup(s => s.SaveSettingsAsync()).Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            // Cleanup test directories
            if (Directory.Exists(_testAppDataDir))
                Directory.Delete(_testAppDataDir, true);
        }

        [Fact]
        public async Task DefaultIndexDirectory_GetsSavedToSettings_OnFirstRun()
        {
            // Arrange
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);

            // Verify initial state - IndexDirectory should be empty
            Assert.Empty(_testSettings.IndexDirectory);
            _output.WriteLine($"Initial IndexDirectory setting: '{_testSettings.IndexDirectory}'");

            // Act
            await indexingService.InitializeAsync();

            // Assert
            // After initialization, the IndexDirectory should be populated with the default path
            Assert.NotEmpty(_testSettings.IndexDirectory);
            Assert.Contains("CST.Avalonia", _testSettings.IndexDirectory);
            Assert.Contains("Index", _testSettings.IndexDirectory);
            
            _output.WriteLine($"Final IndexDirectory setting: '{_testSettings.IndexDirectory}'");

            // Verify that SaveSettingsAsync was called
            _mockSettingsService.Verify(s => s.SaveSettingsAsync(), Times.Once);
            
            _output.WriteLine("✅ Default index directory was saved to settings");
        }

        [Fact]
        public async Task ConfiguredIndexDirectory_DoesNotGetOverwritten()
        {
            // Arrange
            var configuredPath = Path.Combine(_testAppDataDir, "CustomIndex");
            _testSettings.IndexDirectory = configuredPath;
            
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);

            _output.WriteLine($"Configured IndexDirectory: '{_testSettings.IndexDirectory}'");

            // Act
            await indexingService.InitializeAsync();

            // Assert
            // The configured directory should remain unchanged
            Assert.Equal(configuredPath, _testSettings.IndexDirectory);
            Assert.Equal(configuredPath, indexingService.IndexDirectory);

            // SaveSettingsAsync should NOT be called when using configured directory
            _mockSettingsService.Verify(s => s.SaveSettingsAsync(), Times.Never);
            
            _output.WriteLine("✅ Configured index directory was preserved and settings were not saved");
        }

        [Fact]
        public async Task DefaultIndexDirectory_IsValidPath()
        {
            // Arrange
            var indexingService = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);

            // Act
            await indexingService.InitializeAsync();

            // Assert
            var savedPath = _testSettings.IndexDirectory;
            
            // Path should be absolute
            Assert.True(Path.IsPathRooted(savedPath), $"Path should be absolute: {savedPath}");
            
            // Path should be valid for directory creation
            Assert.True(Directory.Exists(savedPath), $"Directory should exist after initialization: {savedPath}");
            
            // Path should be platform-appropriate
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains("AppData", savedPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Assert.Contains("Library/Application Support", savedPath);
            }
            else // Linux
            {
                Assert.Contains(".config", savedPath);
            }

            _output.WriteLine($"✅ Default path is valid and platform-appropriate: {savedPath}");
        }
    }
}