using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Platform.Mac
{
    public class TiroDevanagariTest
    {
        private readonly ILogger<TiroDevanagariTest> _logger;

        public TiroDevanagariTest(ILogger<TiroDevanagariTest> logger)
        {
            _logger = logger;
        }

        public void TestTiroDevanagariFont()
        {
            _logger.LogInformation("=== TESTING TIRO DEVANAGARI SANSKRIT FONT ===");

            try
            {
                // Test 1: Create a font descriptor for "Tiro Devanagari Sanskrit" directly
                TestSpecificFontByName("Tiro Devanagari Sanskrit");
                
                // Test 1b: Examine all attributes of Tiro Devanagari Sanskrit
                TestTiroFontAttributes("Tiro Devanagari Sanskrit");
                
                // Test 2: Query all fonts and look for Devanagari ones
                TestAllFontsForDevanagariNames();
                
                // Test 3: Test script-based filtering specifically
                TestScriptBasedFiltering();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Tiro Devanagari test");
            }
        }

        private void TestSpecificFontByName(string fontName)
        {
            _logger.LogInformation("TEST 1: Looking for specific font: '{FontName}'", fontName);
            
            IntPtr fontNameStringRef = IntPtr.Zero;
            IntPtr attributesDictRef = IntPtr.Zero;
            
            try
            {
                // Create CFString for the font family name
                fontNameStringRef = CoreFoundation.CFStringCreateWithCString(
                    IntPtr.Zero, 
                    fontName, 
                    CoreFoundation.CFStringEncoding.kCFStringEncodingUTF8);
                    
                if (fontNameStringRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1: Failed to create CFString for font name");
                    return;
                }
                
                // Create attributes dictionary with font family name
                unsafe
                {
                    IntPtr* keys = stackalloc IntPtr[1];
                    IntPtr* values = stackalloc IntPtr[1];
                    keys[0] = CoreText.kCTFontFamilyNameAttribute;
                    values[0] = fontNameStringRef;
                    
                    attributesDictRef = CoreFoundation.CFDictionaryCreate(
                        IntPtr.Zero,
                        (IntPtr)keys,
                        (IntPtr)values,
                        (IntPtr)1,
                        CoreFoundation.kCFTypeDictionaryKeyCallBacks,
                        CoreFoundation.kCFTypeDictionaryValueCallBacks
                    );
                }
                
                if (attributesDictRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1: Failed to create attributes dictionary");
                    return;
                }
                
                // Create font descriptor
                IntPtr descriptorRef = CoreText.CTFontDescriptorCreateWithAttributes(attributesDictRef);
                if (descriptorRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1: Failed to create font descriptor");
                    return;
                }
                
                // Check if this font exists by trying to get its attributes back
                IntPtr resultAttributesRef = CoreText.CTFontDescriptorCopyAttributes(descriptorRef);
                if (resultAttributesRef != IntPtr.Zero)
                {
                    _logger.LogInformation("TEST 1: SUCCESS - Font '{FontName}' exists and has attributes", fontName);
                    CoreFoundation.CFRelease(resultAttributesRef);
                }
                else
                {
                    _logger.LogWarning("TEST 1: Font '{FontName}' descriptor created but no attributes returned", fontName);
                }
                
                CoreFoundation.CFRelease(descriptorRef);
            }
            finally
            {
                if (attributesDictRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(attributesDictRef);
                if (fontNameStringRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(fontNameStringRef);
            }
        }

        private void TestTiroFontAttributes(string fontName)
        {
            _logger.LogInformation("TEST 1b: Examining all attributes of font: '{FontName}'", fontName);
            
            IntPtr fontNameStringRef = IntPtr.Zero;
            IntPtr attributesDictRef = IntPtr.Zero;
            
            try
            {
                // Create CFString for the font family name
                fontNameStringRef = CoreFoundation.CFStringCreateWithCString(
                    IntPtr.Zero, 
                    fontName, 
                    CoreFoundation.CFStringEncoding.kCFStringEncodingUTF8);
                    
                if (fontNameStringRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1b: Failed to create CFString for font name");
                    return;
                }
                
                // Create attributes dictionary with font family name
                unsafe
                {
                    IntPtr* keys = stackalloc IntPtr[1];
                    IntPtr* values = stackalloc IntPtr[1];
                    keys[0] = CoreText.kCTFontFamilyNameAttribute;
                    values[0] = fontNameStringRef;
                    
                    attributesDictRef = CoreFoundation.CFDictionaryCreate(
                        IntPtr.Zero,
                        (IntPtr)keys,
                        (IntPtr)values,
                        (IntPtr)1,
                        CoreFoundation.kCFTypeDictionaryKeyCallBacks,
                        CoreFoundation.kCFTypeDictionaryValueCallBacks
                    );
                }
                
                if (attributesDictRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1b: Failed to create attributes dictionary");
                    return;
                }
                
                // Create font descriptor
                IntPtr descriptorRef = CoreText.CTFontDescriptorCreateWithAttributes(attributesDictRef);
                if (descriptorRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1b: Failed to create font descriptor");
                    return;
                }
                
                // Get all attributes from the font descriptor
                IntPtr allAttributesRef = CoreText.CTFontDescriptorCopyAttributes(descriptorRef);
                if (allAttributesRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 1b: Failed to get font descriptor attributes");
                    CoreFoundation.CFRelease(descriptorRef);
                    return;
                }
                
                // Get the count of attributes
                IntPtr attributeCount = CoreFoundation.CFDictionaryGetCount(allAttributesRef);
                _logger.LogInformation("TEST 1b: Font '{FontName}' has {Count} attributes", fontName, (long)attributeCount);
                
                // Extract all keys and values
                long count = (long)attributeCount;
                if (count > 0 && count < 50) // Safety check
                {
                    unsafe
                    {
                        IntPtr* keysArray = stackalloc IntPtr[(int)count];
                        IntPtr* valuesArray = stackalloc IntPtr[(int)count];
                        
                        CoreFoundation.CFDictionaryGetKeysAndValues(allAttributesRef, (IntPtr)keysArray, (IntPtr)valuesArray);
                        
                        for (int i = 0; i < count; i++)
                        {
                            // Try to convert key to string
                            string? keyString = CoreFoundation.CFStringToString(keysArray[i]);
                            string keyDisplay = keyString ?? $"Key_{keysArray[i]:X}";
                            
                            // Try to convert value to string (this might not always work depending on the type)
                            string? valueString = CoreFoundation.CFStringToString(valuesArray[i]);
                            string valueDisplay = valueString ?? $"Value_{valuesArray[i]:X}";
                            
                            _logger.LogInformation("TEST 1b: Attribute {Index}: '{Key}' = '{Value}'", i, keyDisplay, valueDisplay);
                            
                            // Special check for script-related attributes
                            if (keyString != null && (
                                keyString.Contains("Script", StringComparison.OrdinalIgnoreCase) ||
                                keyString.Contains("Language", StringComparison.OrdinalIgnoreCase) ||
                                keyString.Contains("Devanagari", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogWarning("TEST 1b: *** SCRIPT-RELATED ATTRIBUTE FOUND: '{Key}' = '{Value}' ***", keyDisplay, valueDisplay);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("TEST 1b: Attribute count {Count} is outside expected range (0-50)", count);
                }
                
                CoreFoundation.CFRelease(allAttributesRef);
                CoreFoundation.CFRelease(descriptorRef);
            }
            finally
            {
                if (attributesDictRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(attributesDictRef);
                if (fontNameStringRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(fontNameStringRef);
            }
        }

        private void TestAllFontsForDevanagariNames()
        {
            _logger.LogInformation("TEST 2: Querying all available fonts to find Devanagari fonts");
            
            try
            {
                // Create a font collection with all available fonts (no filtering)
                IntPtr allFontsCollectionRef = CoreText.CTFontCollectionCreateWithFontDescriptors(IntPtr.Zero, IntPtr.Zero);
                
                if (allFontsCollectionRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 2: Failed to create font collection");
                    return;
                }
                
                // Get all font descriptors
                IntPtr allDescriptorsRef = CoreText.CTFontCollectionCreateMatchingFontDescriptors(allFontsCollectionRef, IntPtr.Zero);
                CoreFoundation.CFRelease(allFontsCollectionRef);
                
                if (allDescriptorsRef == IntPtr.Zero)
                {
                    _logger.LogError("TEST 2: Failed to get font descriptors");
                    return;
                }
                
                IntPtr count = CoreFoundation.CFArrayGetCount(allDescriptorsRef);
                _logger.LogInformation("TEST 2: Found {Count} total fonts on system", (long)count);
                
                int devanagariCount = 0;
                
                // Look through first 50 fonts for Devanagari names
                long maxToCheck = Math.Min((long)count, 50);
                for (long i = 0; i < maxToCheck; i++)
                {
                    IntPtr fontDescriptor = CoreFoundation.CFArrayGetValueAtIndex(allDescriptorsRef, (IntPtr)i);
                    
                    // Get family name
                    IntPtr familyNameRef = CoreText.CTFontDescriptorCopyAttribute(fontDescriptor, CoreText.kCTFontFamilyNameAttribute);
                    if (familyNameRef != IntPtr.Zero)
                    {
                        string? familyName = CoreFoundation.CFStringToString(familyNameRef);
                        
                        if (!string.IsNullOrEmpty(familyName))
                        {
                            // Check if it contains Devanagari-related terms
                            if (familyName.Contains("Devanagari", StringComparison.OrdinalIgnoreCase) ||
                                familyName.Contains("Tiro", StringComparison.OrdinalIgnoreCase) ||
                                familyName.Contains("Sanskrit", StringComparison.OrdinalIgnoreCase) ||
                                familyName.Contains("Hindi", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("TEST 2: Found potential Devanagari font: '{FontName}'", familyName);
                                devanagariCount++;
                            }
                        }
                        
                        CoreFoundation.CFRelease(familyNameRef);
                    }
                }
                
                _logger.LogInformation("TEST 2: Found {Count} potential Devanagari fonts in first {MaxChecked} system fonts", devanagariCount, maxToCheck);
                CoreFoundation.CFRelease(allDescriptorsRef);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TEST 2: Exception while querying all fonts");
            }
        }

        private void TestScriptBasedFiltering()
        {
            _logger.LogInformation("TEST 3: Testing script-based filtering with Devanagari script code");
            
            // Get MacFontService from DI instead of creating directly
            var serviceProvider = App.ServiceProvider;
            if (serviceProvider == null)
            {
                _logger.LogError("TEST 3: Service provider not available");
                return;
            }
            
            var macFontService = serviceProvider.GetService<MacFontService>();
            if (macFontService == null)
            {
                _logger.LogError("TEST 3: MacFontService not available from DI");
                return;
            }
            
            var result = macFontService.GetAvailableFontsForScriptAsync(CST.Conversion.Script.Devanagari).Result;
            
            _logger.LogInformation("TEST 3: Script-based filtering returned {Count} fonts", result.Count);
            
            foreach (var font in result)
            {
                _logger.LogInformation("TEST 3: Script filter result: '{FontName}'", font);
            }
            
            // Check if any of the results look like actual Devanagari fonts
            bool hasDevanagariFont = result.Any(f => 
                f.Contains("Devanagari", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Tiro", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Sanskrit", StringComparison.OrdinalIgnoreCase));
                
            if (hasDevanagariFont)
            {
                _logger.LogInformation("TEST 3: SUCCESS - Found actual Devanagari fonts in script filtering");
            }
            else
            {
                _logger.LogWarning("TEST 3: WARNING - Script filtering did not return expected Devanagari fonts");
            }
        }
    }
}