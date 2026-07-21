using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CST.Avalonia.Tests.TestSupport;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Performance
{
    /// <summary>
    /// Exercises the indexing service's hot paths and RECORDS how long they take.
    ///
    /// These deliberately do not assert wall-clock bounds. A duration threshold is not an invariant of the
    /// product — it is a property of the machine — so on a loaded runner (the full suite in parallel, a GC
    /// pause, first-call JIT) an assert like "under 50ms" fails while every call returned the correct result.
    /// That reports the CI box, not a regression, and a suite that goes red for reasons unrelated to the code
    /// stops being believed. The timings are written to test output instead, where a human comparing runs can
    /// still spot a real slowdown. (#412)
    /// </summary>
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
        public async Task InitializeAsync_succeeds_and_its_duration_is_recorded()
        {
            // Arrange
            var stopwatch = Stopwatch.StartNew();

            // Act
            await _service.InitializeAsync();

            // Assert
            stopwatch.Stop();
            _output.WriteLine($"InitializeAsync completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task IsIndexValidAsync_reports_a_valid_index_and_its_duration_is_recorded()
        {
            // Arrange
            await _service.InitializeAsync();
            
            // A real index so validity (which now actually opens the index) passes. (SRCH-11)
            TestIndex.CreateMinimal(_testIndexDir);

            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await _service.IsIndexValidAsync();

            // Assert
            stopwatch.Stop();
            _output.WriteLine($"IsIndexValidAsync completed in {stopwatch.ElapsedMilliseconds}ms");
            
            Assert.True(result);
        }

        [Fact]
        public async Task MultipleIndexValidations_all_report_valid_and_their_durations_are_recorded()
        {
            // Arrange
            await _service.InitializeAsync();
            TestIndex.CreateMinimal(_testIndexDir);

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

            // The correctness assertion is inside the loop: all 10 calls reported a valid index. What is left
            // here is measurement. (The old asserts — max < 50ms and spread < 30ms — were the flake in #412:
            // a first-call JIT or a GC pause during a parallel suite run trips them with min 0ms / max ~40ms.)
            _output.WriteLine($"Index validation times (ms): {string.Join(", ", times)}");
            _output.WriteLine($"  min {times.Min()}ms, max {times.Max()}ms, spread {times.Max() - times.Min()}ms");
        }

        [Fact]
        public async Task OptimizeIndexAsync_completes_and_its_duration_is_recorded()
        {
            // Arrange
            await _service.InitializeAsync();
            var stopwatch = Stopwatch.StartNew();

            // Act
            await _service.OptimizeIndexAsync();

            // Assert
            stopwatch.Stop();
            // Modern Lucene handles optimization automatically, so this is expected to be near-instant — but
            // "< 10ms" was the tightest bound in the file and the least defensible on a loaded machine.
            _output.WriteLine($"OptimizeIndexAsync completed in {stopwatch.ElapsedMilliseconds}ms");
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

            // Kept, unlike the timing asserts: 10MB of headroom against a service that should allocate almost
            // nothing is a wide enough margin to be a real signal. Note the residual hazard — GetTotalMemory is
            // PROCESS-wide, so a parallel test allocating heavily in this window could still trip it. If that is
            // ever observed, this should go the same way as the timing asserts rather than being loosened. (#412)
            Assert.True(stableMemoryIncrease < 10_000_000, 
                $"Service initialization used {stableMemoryIncrease:N0} bytes, expected < 10MB");
        }

        [Fact]
        public void ServiceCreation_succeeds_repeatedly_and_its_duration_is_recorded()
        {
            // Arrange & Act
            var stopwatch = Stopwatch.StartNew();
            
            // Create 100 service instances to test creation overhead
            for (int i = 0; i < 100; i++)
            {
                // Construct only — no initialization. Assert it actually built, so the loop cannot be optimized
                // away and the test still verifies something now that the timing bound is gone.
                var service = new IndexingService(_mockLogger.Object, _mockSettingsService.Object, _mockXmlFileDatesService.Object);
                Assert.NotNull(service);
            }
            
            stopwatch.Stop();

            _output.WriteLine($"Creating 100 IndexingService instances took {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}