using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Performance
{
    public class IndexingPerformanceTests : IDisposable
    {
        private readonly Mock<ILogger<IndexingService>> _mockLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private readonly Mock<IXmlFileDatesService> _mockXmlFileDatesService;
        private readonly string _testIndexDir;
        private readonly string _testXmlDir;
        private readonly IndexingService _service;
        private readonly ITestOutputHelper _output;

        public IndexingPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<IndexingService>>();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockXmlFileDatesService = new Mock<IXmlFileDatesService>();
            
            // Create temporary directories for testing
            _testIndexDir = Path.Combine(Path.GetTempPath(), "CST.PerformanceTests", Guid.NewGuid().ToString());
            _testXmlDir = Path.Combine(Path.GetTempPath(), "CST.XmlPerformanceTests", Guid.NewGuid().ToString());
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
        public async Task InitializeAsync_Performance_CompletesQuickly()
        {
            // Arrange
            var stopwatch = Stopwatch.StartNew();

            // Act
            await _service.InitializeAsync();

            // Assert
            stopwatch.Stop();
            _output.WriteLine($"InitializeAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            
            // Should complete initialization in under 1 second
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
                $"Initialization took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
        }

        [Fact]
        public async Task IsIndexValidAsync_Performance_CompletesQuickly()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // Create some fake index files to test file system performance
            var indexFiles = new[]
            {
                Path.Combine(_testIndexDir, "segments_1"),
                Path.Combine(_testIndexDir, "test1.cfs"),
                Path.Combine(_testIndexDir, "test1.fdt"),
                Path.Combine(_testIndexDir, "test2.cfs"),
                Path.Combine(_testIndexDir, "test2.fdt")
            };
            
            foreach (var file in indexFiles)
            {
                await File.WriteAllTextAsync(file, "test content");
            }

            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            stopwatch.Stop();
            _output.WriteLine($"IsIndexValidAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            
            Assert.True(result);
            // Should complete validation in under 100ms even with multiple files
            Assert.True(stopwatch.ElapsedMilliseconds < 100, 
                $"Index validation took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
        }

        [Fact]
        public async Task MultipleIndexValidations_Performance_AreConsistent()
        {
            // Arrange
            await _service.InitializeAsync();
            var indexFile = Path.Combine(_testIndexDir, "test.cfs");
            await File.WriteAllTextAsync(indexFile, "test content");

            var times = new long[10];

            // Act - perform 10 validations and measure times
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await _service.IsIndexValidAsync();
                stopwatch.Stop();
                times[i] = stopwatch.ElapsedMilliseconds;
                
                Assert.True(result);
            }

            // Assert - times should be consistent and fast
            var maxTime = Math.Max(Math.Max(Math.Max(Math.Max(times[0], times[1]), Math.Max(times[2], times[3])), 
                                           Math.Max(Math.Max(times[4], times[5]), Math.Max(times[6], times[7]))), 
                                  Math.Max(times[8], times[9]));
            
            var minTime = Math.Min(Math.Min(Math.Min(Math.Min(times[0], times[1]), Math.Min(times[2], times[3])), 
                                           Math.Min(Math.Min(times[4], times[5]), Math.Min(times[6], times[7]))), 
                                  Math.Min(times[8], times[9]));

            _output.WriteLine($"Index validation times - Min: {minTime}ms, Max: {maxTime}ms");
            
            // Max time should be under 50ms and variation should be reasonable
            Assert.True(maxTime < 50, $"Maximum validation time was {maxTime}ms, expected < 50ms");
            Assert.True(maxTime - minTime < 30, $"Time variation was {maxTime - minTime}ms, expected < 30ms");
        }

        [Fact]
        public async Task OptimizeIndexAsync_Performance_CompletesQuickly()
        {
            // Arrange
            await _service.InitializeAsync();
            var stopwatch = Stopwatch.StartNew();

            // Act
            await _service.OptimizeIndexAsync();

            // Assert
            stopwatch.Stop();
            _output.WriteLine($"OptimizeIndexAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            
            // Since modern Lucene handles optimization automatically, this should be very fast
            Assert.True(stopwatch.ElapsedMilliseconds < 10, 
                $"Index optimization took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
        }

        [Fact]
        public async Task ServiceInitialization_MemoryUsage_IsReasonable()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);

            // Act
            await _service.InitializeAsync();
            var afterInitMemory = GC.GetTotalMemory(false);

            // Force garbage collection and measure again
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var afterGCMemory = GC.GetTotalMemory(true);

            // Assert
            var memoryIncrease = afterInitMemory - initialMemory;
            var stableMemoryIncrease = afterGCMemory - initialMemory;

            _output.WriteLine($"Memory usage - Initial: {initialMemory:N0}, After Init: {afterInitMemory:N0}, After GC: {afterGCMemory:N0}");
            _output.WriteLine($"Memory increase - Immediate: {memoryIncrease:N0} bytes, Stable: {stableMemoryIncrease:N0} bytes");

            // Service initialization should not use excessive memory (< 10MB increase is reasonable)
            Assert.True(stableMemoryIncrease < 10_000_000, 
                $"Service initialization used {stableMemoryIncrease:N0} bytes, expected < 10MB");
        }

        [Fact]
        public void ServiceCreation_Performance_IsFast()
        {
            // Arrange & Act
            var stopwatch = Stopwatch.StartNew();
            
            // Create 100 service instances to test creation overhead
            for (int i = 0; i < 100; i++)
            {
                var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
                // Just create, don't initialize
            }
            
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Creating 100 IndexingService instances took {stopwatch.ElapsedMilliseconds}ms");
            
            // Should be able to create many service instances quickly
            Assert.True(stopwatch.ElapsedMilliseconds < 100, 
                $"Creating 100 services took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
        }
    }
}