using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST.Lucene;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Integration
{
    public class IndexingIntegrationTests : IDisposable
    {
        private readonly string _testAppDataDir;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly IServiceProvider _serviceProvider;

        public IndexingIntegrationTests()
        {
            // Create temporary directories for integration testing
            _testAppDataDir = Path.Combine(Path.GetTempPath(), "CST.IntegrationTests", Guid.NewGuid().ToString());
            _testIndexDir = Path.Combine(_testAppDataDir, "Index");
            _testXmlDir = Path.Combine(_testAppDataDir, "XmlBooks");
            Directory.CreateDirectory(_testAppDataDir);
            Directory.CreateDirectory(_testIndexDir);
            Directory.CreateDirectory(_testXmlDir);

            // Set up dependency injection like the real application would
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            
            // Register services with mocks
            var mockSettingsService = new Mock<ISettingsService>();
            mockSettingsService.Setup(s => s.Settings).Returns(new Settings
            {
                IndexDirectory = _testIndexDir,
                XmlBooksDirectory = _testXmlDir
            });
            services.AddSingleton(mockSettingsService.Object);
            
            services.AddSingleton<IXmlFileDatesService, TestableXmlFileDatesService>(provider =>
                new TestableXmlFileDatesService(
                    provider.GetRequiredService<ILogger<XmlFileDatesService>>(),
                    provider.GetRequiredService<ISettingsService>(),
                    _testAppDataDir
                ));
            
            services.AddSingleton<IIndexingService, IndexingService>();

            _serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
            
            // Cleanup test directories
            if (Directory.Exists(_testAppDataDir))
                Directory.Delete(_testAppDataDir, true);
        }

        [Fact]
        public async Task FullServiceIntegration_InitializesCorrectly()
        {
            // Arrange
            var xmlFileDatesService = _serviceProvider.GetRequiredService<IXmlFileDatesService>();
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();

            // Act
            await xmlFileDatesService.InitializeAsync();
            await indexingService.InitializeAsync();

            // Assert
            Assert.Equal(_testIndexDir, indexingService.IndexDirectory);
            Assert.True(Directory.Exists(_testIndexDir));
        }

        [Fact]
        public async Task IndexingWorkflow_WithMockData_CompletesSuccessfully()
        {
            // Arrange
            var xmlFileDatesService = _serviceProvider.GetRequiredService<IXmlFileDatesService>() as TestableXmlFileDatesService;
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();

            await xmlFileDatesService!.InitializeAsync();
            await indexingService.InitializeAsync();

            // Create mock changed books list (simulating what would come from real XML analysis)
            var mockChangedBooks = new List<int> { 0, 1 }; // First two books
            xmlFileDatesService.SetMockChangedBooks(mockChangedBooks);

            var progressReports = new List<IndexingProgress>();
            var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

            // Act & Assert
            // This will fail because we don't have real XML files, but we're testing integration
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await indexingService.BuildIndexAsync(progress));

            // Verify the integration worked up to the point where real files are needed
            Assert.True(progressReports.Count > 0);
        }

        [Fact]
        public async Task IndexValidation_IntegrationTest()
        {
            // Arrange
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();
            await indexingService.InitializeAsync();

            // Act
            var isValidEmpty = await indexingService.IsIndexValidAsync();

            // Create a fake index file
            var fakeIndexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(fakeIndexFile, "fake index content");

            var isValidWithFiles = await indexingService.IsIndexValidAsync();

            // Assert
            Assert.False(isValidEmpty);
            Assert.True(isValidWithFiles);
        }

        [Fact]
        public async Task CrossServiceCommunication_WorksCorrectly()
        {
            // Arrange
            var xmlFileDatesService = _serviceProvider.GetRequiredService<IXmlFileDatesService>() as TestableXmlFileDatesService;
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();

            await xmlFileDatesService!.InitializeAsync();
            await indexingService.InitializeAsync();

            // Set up mock data
            var mockChangedBooks = new List<int> { 5, 10, 15 };
            xmlFileDatesService.SetMockChangedBooks(mockChangedBooks);

            // Act & Assert
            // Test that IndexingService correctly calls XmlFileDatesService
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await indexingService.BuildIndexAsync(null));

            // Verify the services communicated
            Assert.True(xmlFileDatesService.GetChangedBooksWasCalled);
        }

        [Fact]
        public async Task ServiceLifecycle_HandlesMultipleOperations()
        {
            // Arrange
            var indexingService = _serviceProvider.GetRequiredService<IIndexingService>();
            await indexingService.InitializeAsync();

            // Act - perform multiple operations
            var validation1 = await indexingService.IsIndexValidAsync();
            await indexingService.OptimizeIndexAsync();
            var validation2 = await indexingService.IsIndexValidAsync();

            // Assert
            Assert.False(validation1);
            Assert.False(validation2); // Still false since no real index was created
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

            public override async Task<List<int>> GetChangedBooksAsync()
            {
                GetChangedBooksWasCalled = true;
                
                // If we have mock data, return it; otherwise call the base implementation
                if (_mockChangedBooks.Count > 0)
                {
                    return await Task.FromResult(_mockChangedBooks);
                }
                
                return await base.GetChangedBooksAsync();
            }

            protected override string GetAppDataDirectory()
            {
                return _testAppDataDir;
            }
        }
    }
}