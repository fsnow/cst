using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Platform.Mac;
using CST.Conversion;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Services
{
    public class MacFontServiceTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<ILogger<MacFontService>> _mockLogger;
        private readonly Mock<ILogger<FontService>> _mockFontServiceLogger;
        private readonly Mock<ISettingsService> _mockSettingsService;

        public MacFontServiceTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<ILogger<MacFontService>>();
            _mockFontServiceLogger = new Mock<ILogger<FontService>>();
            _mockSettingsService = new Mock<ISettingsService>();
        }

        // (Removed MacFontService_LanguageCodeMapping_ReturnsCorrectCodes: it reflected on the private static
        // GetLanguageCodeForScript, which no longer exists - the implementation switched to a character-set
        // based approach (GetSampleCharactersForScript). The current behaviour is covered below. (#46))

        [Fact]
        public void MacFontService_CoreFoundation_DiagnosticTest()
        {
            // Skip this test if not running on macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _output.WriteLine("Skipping macOS-specific test on non-macOS platform");
                return;
            }

            try
            {
                // Test if we can access Core Foundation symbols directly
                var macFontService = new MacFontService(_mockLogger.Object);
                
                // Use reflection to check static field values
                var symbolFieldsType = typeof(MacFontService);
                var languagesAttr = symbolFieldsType.GetField("kCTFontLanguagesAttribute", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var familyAttr = symbolFieldsType.GetField("kCTFontFamilyNameAttribute", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (languagesAttr != null)
                {
                    var langValue = (IntPtr)languagesAttr.GetValue(null)!;
                    _output.WriteLine($"kCTFontLanguagesAttribute: {langValue}");
                }
                else
                {
                    _output.WriteLine("Could not find kCTFontLanguagesAttribute field");
                }
                
                if (familyAttr != null)
                {
                    var familyValue = (IntPtr)familyAttr.GetValue(null)!;
                    _output.WriteLine($"kCTFontFamilyNameAttribute: {familyValue}");
                }
                else
                {
                    _output.WriteLine("Could not find kCTFontFamilyNameAttribute field");
                }

                // Test dlopen/dlsym directly
                var coreTextHandle = MacFontService_TestDlopen("/System/Library/Frameworks/CoreText.framework/CoreText");
                _output.WriteLine($"CoreText framework handle: {coreTextHandle}");
                
                if (coreTextHandle != IntPtr.Zero)
                {
                    var langSymbol = MacFontService_TestDlsym(coreTextHandle, "kCTFontLanguagesAttribute");
                    _output.WriteLine($"kCTFontLanguagesAttribute symbol: {langSymbol}");
                    
                    var familySymbol = MacFontService_TestDlsym(coreTextHandle, "kCTFontFamilyNameAttribute");
                    _output.WriteLine($"kCTFontFamilyNameAttribute symbol: {familySymbol}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Diagnostic test failed: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Helper methods for diagnostic testing
        [System.Runtime.InteropServices.DllImport("libSystem.dylib")]
        private static extern IntPtr dlopen(string path, int mode);
        
        [System.Runtime.InteropServices.DllImport("libSystem.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        private IntPtr MacFontService_TestDlopen(string path) => dlopen(path, 0);
        private IntPtr MacFontService_TestDlsym(IntPtr handle, string symbol) => dlsym(handle, symbol);

        [Fact]
        public async Task MacFontService_GetAvailableFontsForScriptAsync_UnsupportedScript_ReturnsFallbackFonts()
        {
            // An unrecognized script has no sample characters, so the service returns its generic fallback
            // font list (no Core Text query needed) and logs a warning. Runs on any platform - this path does
            // not touch the macOS font APIs.
            // Arrange
            var macFontService = new MacFontService(_mockLogger.Object);

            // Act
            var result = await macFontService.GetAvailableFontsForScriptAsync((Script)999);

            // Assert - generic fallback set, not empty
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains("Arial", result);

            // Verify the "no sample characters" warning was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No sample characters available")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task MacFontService_GetAvailableFontsForScriptAsync_ReturnsExpectedForDevanagari()
        {
            // Skip this test if not running on macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _output.WriteLine("Skipping macOS-specific test on non-macOS platform");
                return;
            }

            try
            {
                // Arrange
                var macFontService = new MacFontService(_mockLogger.Object);

                // Act
                var result = await macFontService.GetAvailableFontsForScriptAsync(Script.Devanagari);

                // Assert
                Assert.NotNull(result);
                _output.WriteLine($"Found {result.Count} fonts for Devanagari script");
                
                foreach (var font in result.Take(10)) // Log first 10 fonts
                {
                    _output.WriteLine($"Font: {font}");
                }

                // We expect at least some fonts for Devanagari/Hindi on macOS
                // Even if there are no specific Devanagari fonts, log the result for debugging
                if (result.Count == 0)
                {
                    _output.WriteLine("WARNING: No fonts found for Devanagari script - this might indicate an issue with Core Text API calls");
                }

                // Verify information log was called
                _mockLogger.Verify(
                    x => x.Log(
                        LogLevel.Information,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Querying fonts for script")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        [Fact]
        public async Task MacFontService_GetAvailableFontsForScriptAsync_ReturnsExpectedForMyanmar()
        {
            // Skip this test if not running on macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _output.WriteLine("Skipping macOS-specific test on non-macOS platform");
                return;
            }

            try
            {
                // Arrange
                var macFontService = new MacFontService(_mockLogger.Object);

                // Act
                var result = await macFontService.GetAvailableFontsForScriptAsync(Script.Myanmar);

                // Assert
                Assert.NotNull(result);
                _output.WriteLine($"Found {result.Count} fonts for Myanmar script");
                
                foreach (var font in result.Take(10)) // Log first 10 fonts
                {
                    _output.WriteLine($"Font: {font}");
                }

                // Myanmar might have fewer fonts, but there should be some system fonts
                // Log the result even if it's 0 to help debug
                if (result.Count == 0)
                {
                    _output.WriteLine("WARNING: No fonts found for Myanmar script - this might indicate an issue");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        [Fact]
        public void FontService_Constructor_ChecksForMacOSConstructor()
        {
            // This test verifies if the MACOS symbol is working correctly
            var fontServiceType = typeof(FontService);
            var constructors = fontServiceType.GetConstructors();
            
            _output.WriteLine($"FontService has {constructors.Length} constructors:");
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                _output.WriteLine($"Constructor with {parameters.Length} parameters:");
                foreach (var param in parameters)
                {
                    _output.WriteLine($"  - {param.ParameterType.Name} {param.Name}");
                }
            }

#if MACOS
            _output.WriteLine("MACOS symbol is defined - should have 3-parameter constructor");
            // On macOS, should have constructor with MacFontService parameter
            var macConstructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 3);
            Assert.NotNull(macConstructor);
#else
            _output.WriteLine("MACOS symbol is NOT defined - should have 2-parameter constructor only");
            // On non-macOS, should only have 2-parameter constructor
            Assert.True(constructors.All(c => c.GetParameters().Length == 2));
#endif
        }

        [Fact]
        public async Task FontService_GetAvailableFontsForScriptAsync_UsesMacServiceOnMacOS()
        {
            // Skip this test if not running on macOS since MacFontService won't be registered
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _output.WriteLine("Skipping macOS-specific test on non-macOS platform");
                return;
            }

            try
            {
                // Arrange
                var mockSettings = new CST.Avalonia.Models.Settings();
                _mockSettingsService.Setup(x => x.Settings).Returns(mockSettings);
                
#if MACOS
                var macFontService = new MacFontService(_mockLogger.Object);
                var fontService = new FontService(_mockSettingsService.Object, _mockFontServiceLogger.Object, macFontService);
#else
                var fontService = new FontService(_mockSettingsService.Object, _mockFontServiceLogger.Object);
#endif

                // Act - Test with Devanagari instead of Latin
                var result = await fontService.GetAvailableFontsForScriptAsync(Script.Devanagari);

                // Assert
                Assert.NotNull(result);
                _output.WriteLine($"FontService returned {result.Count} fonts for Devanagari script");
                
                foreach (var font in result.Take(5)) // Log first 5 fonts
                {
                    _output.WriteLine($"Font: {font}");
                }

#if MACOS
                // Verify that the FontService logged that it's using MacFontService
                _mockFontServiceLogger.Verify(
                    x => x.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using MacFontService")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once);
#else
                // Should use fallback on non-macOS
                _mockFontServiceLogger.Verify(
                    x => x.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Using fallback method")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once);
#endif
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Test failed with exception: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // (Removed FontService_GetAvailableFontsForScriptAsync_UsesFallbackOnNonMacOS: the non-macOS fallback
        // path calls Avalonia's FontManager.Current.SystemFonts, which requires an initialized Avalonia
        // application that the headless unit-test host doesn't provide - it exercised the framework's font
        // enumeration rather than our logic. (#46))
    }
}