using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CST.Conversion;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CST.Avalonia.Services.Platform.Mac
{
    public static partial class CoreFoundation
    {
        private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        // P/Invoke declaration for CFStringCreateWithCString
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFStringCreateWithCString(
            IntPtr allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr,
            CFStringEncoding encoding);

        // P/Invoke declaration for CFArrayCreate
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFArrayCreate(
            IntPtr allocator,
            IntPtr values, // Pointer to the array of values
            IntPtr numValues,
            IntPtr callBacks);

        // P/Invoke for CFDictionaryCreate
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFDictionaryCreate(
            IntPtr allocator,
            IntPtr keys,    // Pointer to the array of keys
            IntPtr values,  // Pointer to the array of values
            IntPtr numValues,
            IntPtr keyCallBacks,
            IntPtr valueCallBacks);

        // P/Invoke declaration for CFRelease
        [LibraryImport(CoreFoundationLibrary)]
        public static partial void CFRelease(IntPtr cf);

        // P/Invoke declaration for getting the constant kCFTypeArrayCallBacks
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFArrayGetTypeID();
        
        // P/Invoke for CFDictionaryGetCount
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFDictionaryGetCount(IntPtr theDict);
        
        // P/Invoke for CFDictionaryGetKeysAndValues 
        [LibraryImport(CoreFoundationLibrary)]
        public static partial void CFDictionaryGetKeysAndValues(IntPtr theDict, IntPtr keys, IntPtr values);
        
        // CFNumber functions for script codes
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFNumberCreate(IntPtr allocator, CFNumberType theType, IntPtr valuePtr);
        
        public enum CFNumberType : int
        {
            kCFNumberSInt32Type = 3,
            kCFNumberIntType = 9,
        }
        
        // CFCharacterSet functions for character-set-based font detection
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFCharacterSetCreateWithCharactersInString(IntPtr allocator, IntPtr theString);

        // Constant for the standard callbacks when holding CF objects.
        public static readonly IntPtr kCFTypeArrayCallBacks = GetCFTypeArrayCallBacks();
        public static readonly IntPtr kCFTypeDictionaryKeyCallBacks = GetCFTypeDictionaryKeyCallBacks();
        public static readonly IntPtr kCFTypeDictionaryValueCallBacks = GetCFTypeDictionaryValueCallBacks();

        private static IntPtr GetCFTypeArrayCallBacks()
        {
            // On modern macOS, this constant is found via a symbol.
            return GetSymbolFromImage(CoreFoundationLibrary, "kCFTypeArrayCallBacks");
        }

        private static IntPtr GetCFTypeDictionaryKeyCallBacks()
        {
            return GetSymbolFromImage(CoreFoundationLibrary, "kCFTypeDictionaryKeyCallBacks");
        }

        private static IntPtr GetCFTypeDictionaryValueCallBacks()
        {
            return GetSymbolFromImage(CoreFoundationLibrary, "kCFTypeDictionaryValueCallBacks");
        }

        // Proper dlopen/dlsym declarations
        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")]
        private static partial IntPtr dlopen(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, 
            int flags);
            
        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym")]
        private static partial IntPtr dlsym(
            IntPtr handle, 
            [MarshalAs(UnmanagedType.LPUTF8Str)] string symbol);
            
        private const int RTLD_LAZY = 0x1;
        private const int RTLD_NOW = 0x2;
        
        public static IntPtr GetSymbolFromImage(string libraryPath, string symbolName)
        {
            var handle = dlopen(libraryPath, RTLD_LAZY);
            if (handle == IntPtr.Zero) return IntPtr.Zero;
            return dlsym(handle, symbolName);
        }

        // P/Invoke declarations for CFArray functions
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFArrayGetCount(IntPtr theArray);

        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFArrayGetValueAtIndex(IntPtr theArray, IntPtr index);

        // CFString utility functions
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFStringGetCStringPtr(IntPtr theString, CFStringEncoding encoding);
        
        [LibraryImport(CoreFoundationLibrary)]
        public static partial IntPtr CFStringGetLength(IntPtr theString);
        
        [LibraryImport(CoreFoundationLibrary)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CFStringGetCString(IntPtr theString, IntPtr buffer, IntPtr bufferSize, CFStringEncoding encoding);

        // Improved helper method to convert CFStringRef to C# string
        public static string? CFStringToString(IntPtr cfString)
        {
            if (cfString == IntPtr.Zero) return null;
            
            // Try the fast path first
            IntPtr cString = CFStringGetCStringPtr(cfString, CFStringEncoding.kCFStringEncodingUTF8);
            if (cString != IntPtr.Zero) 
                return Marshal.PtrToStringUTF8(cString);
            
            // Fast path failed, use the more robust approach
            IntPtr length = CFStringGetLength(cfString);
            if ((long)length <= 0 || (long)length > 1000) // Safety bounds
                return $"CFString_{cfString:X}";
            
            // Allocate buffer for the string (length * 4 for UTF-8 max + null terminator)
            int bufferSize = (int)(long)length * 4 + 1;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            
            try
            {
                bool success = CFStringGetCString(cfString, buffer, (IntPtr)bufferSize, CFStringEncoding.kCFStringEncodingUTF8);
                if (success)
                {
                    return Marshal.PtrToStringUTF8(buffer);
                }
                else
                {
                    return $"CFString_{cfString:X}";
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // CFString encoding constant
        public enum CFStringEncoding : uint
        {
            kCFStringEncodingUTF8 = 0x08000100
        }
    }

    public static partial class CoreText
    {
        private const string CoreTextLibrary = "/System/Library/Frameworks/CoreText.framework/CoreText";

        // CoreText functions
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontDescriptorCreateWithAttributes(IntPtr attributes);

        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontDescriptorCreateMatchingFontDescriptors(
            IntPtr descriptor,
            IntPtr mandatoryAttributes);

        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontDescriptorCopyAttributes(IntPtr descriptor);

        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontDescriptorCopyAttribute(IntPtr descriptor, IntPtr attribute);
        
        // System font creation and string-based font matching
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontCreateUIFontForLanguage(CTFontUIFontType uiType, double size, IntPtr language);
        
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontCreateForStringWithLanguage(IntPtr font, IntPtr theString, CFRange range, IntPtr language);
        
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontCopyFamilyName(IntPtr font);
        
        // Font UI type enumeration
        public enum CTFontUIFontType : uint
        {
            kCTFontUIFontSystem = 2
        }
        
        // CFRange structure for string operations
        public struct CFRange
        {
            public IntPtr location;
            public IntPtr length;
            
            public CFRange(IntPtr location, IntPtr length)
            {
                this.location = location;
                this.length = length;
            }
        }
        
        // Helper method to create CFRange
        public static CFRange CFRangeMake(int location, int length)
        {
            return new CFRange((IntPtr)location, (IntPtr)length);
        }
        
        // Font Collection functions (for script-based matching)
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontCollectionCreateWithFontDescriptors(IntPtr descriptors, IntPtr options);
        
        [LibraryImport(CoreTextLibrary)]
        public static partial IntPtr CTFontCollectionCreateMatchingFontDescriptors(IntPtr collection, IntPtr options);

        // CoreText Attribute Keys (using symbol lookup with logging)
        public static readonly IntPtr kCTFontFamilyNameAttribute = GetCoreTextSymbol("kCTFontFamilyNameAttribute");
        public static readonly IntPtr kCTFontNameAttribute = GetCoreTextSymbol("kCTFontNameAttribute");
        // Character set attribute for character-set-based font detection
        public static readonly IntPtr kCTFontCharacterSetAttribute = GetCoreTextSymbol("kCTFontCharacterSetAttribute");

        private static IntPtr GetCoreTextSymbol(string symbolName)
        {
            var ptr = CoreFoundation.GetSymbolFromImage(CoreTextLibrary, symbolName);
            System.Diagnostics.Debug.WriteLine($"Core Text symbol lookup for {symbolName}: {ptr} (0x{ptr:X})");
            Log.Debug("[MacFontService] Core Text symbol lookup for {SymbolName}: {Pointer:X}", symbolName, ptr);
            
            // If symbol lookup fails, the symbol should be dereferenced as it points to a CFString
            if (ptr != IntPtr.Zero)
            {
                try
                {
                    // The symbol is a pointer to a pointer to the actual CFString constant
                    var actualPtr = Marshal.ReadIntPtr(ptr);
                    System.Diagnostics.Debug.WriteLine($"Core Text symbol {symbolName} dereferenced: {actualPtr} (0x{actualPtr:X})");
                    Log.Debug("[MacFontService] Core Text symbol {SymbolName} dereferenced: {Pointer:X}", symbolName, actualPtr);
                    return actualPtr;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Failed to dereference symbol {symbolName}: {ex.Message}");
                    Log.Error(ex, "[MacFontService] Failed to dereference symbol {SymbolName}", symbolName);
                    return IntPtr.Zero;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"WARNING: Symbol lookup failed for {symbolName}");
            Log.Warning("[MacFontService] Symbol lookup failed for {SymbolName}", symbolName);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Provides Mac-specific font services using native Core Text APIs.
    /// This class is conditionally compiled and should only be included in macOS builds.
    /// </summary>
    public class MacFontService
    {
        private readonly ILogger<MacFontService> _logger;
        private readonly Dictionary<Script, string?> _systemDefaultFontCache = new();

        public MacFontService(ILogger<MacFontService> logger)
        {
            _logger = logger;
        }

        public Task<List<string>> GetAvailableFontsForScriptAsync(Script script)
        {
            return Task.Run(() =>
            {
                _logger.LogInformation("Querying fonts for script {Script} using character-set-based approach", script);
                return GetFontsForScriptUsingCharacterSet(script);
            });
        }

        public Task<string?> GetSystemDefaultFontForScriptAsync(Script script)
        {
            return Task.Run(() =>
            {
                // Check cache first
                if (_systemDefaultFontCache.TryGetValue(script, out string? cachedFont))
                {
                    _logger.LogInformation("Returning cached system default font '{Font}' for script {Script}", cachedFont, script);
                    return cachedFont;
                }

                // Get system default font for script
                string? systemFont = GetSystemDefaultFontForScript(script);
                
                // Cache the result
                _systemDefaultFontCache[script] = systemFont;
                
                _logger.LogInformation("Cached system default font '{Font}' for script {Script}", systemFont, script);
                return systemFont;
            });
        }

        private List<string> GetFontsForScriptUsingCharacterSet(Script script)
        {
            _logger.LogInformation("CHARACTER-SET APPROACH: Getting fonts for script {Script}", script);
            
            IntPtr characterStringRef = IntPtr.Zero;
            IntPtr characterSetRef = IntPtr.Zero;
            IntPtr attributesDictRef = IntPtr.Zero;
            
            try
            {
                // Step 1: Get sample characters for the script
                string? sampleChars = GetSampleCharactersForScript(script);
                if (string.IsNullOrEmpty(sampleChars))
                {
                    _logger.LogWarning("CHARACTER-SET APPROACH: No sample characters available for script {Script}", script);
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                _logger.LogInformation("CHARACTER-SET APPROACH: Using sample characters '{Chars}' for script {Script}", sampleChars, script);
                
                // Step 2: Create CFString with sample characters
                characterStringRef = CoreFoundation.CFStringCreateWithCString(
                    IntPtr.Zero, 
                    sampleChars, 
                    CoreFoundation.CFStringEncoding.kCFStringEncodingUTF8);
                    
                if (characterStringRef == IntPtr.Zero)
                {
                    _logger.LogError("CHARACTER-SET APPROACH: Failed to create CFString for sample characters");
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 1 SUCCESS - Created CFString with sample characters");
                
                // Step 3: Create CFCharacterSet from the string
                characterSetRef = CoreFoundation.CFCharacterSetCreateWithCharactersInString(IntPtr.Zero, characterStringRef);
                if (characterSetRef == IntPtr.Zero)
                {
                    _logger.LogError("CHARACTER-SET APPROACH: Failed to create CFCharacterSet from sample characters");
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 2 SUCCESS - Created CFCharacterSet");
                
                // Step 4: Create font descriptor with character set attribute
                unsafe
                {
                    IntPtr* keys = stackalloc IntPtr[1];
                    IntPtr* values = stackalloc IntPtr[1];
                    keys[0] = CoreText.kCTFontCharacterSetAttribute;
                    values[0] = characterSetRef;
                    
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
                    _logger.LogError("CHARACTER-SET APPROACH: Failed to create attributes dictionary");
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 3 SUCCESS - Created attributes dictionary");
                
                // Step 5: Create template descriptor
                IntPtr templateDescriptorRef = CoreText.CTFontDescriptorCreateWithAttributes(attributesDictRef);
                if (templateDescriptorRef == IntPtr.Zero)
                {
                    _logger.LogError("CHARACTER-SET APPROACH: Failed to create template descriptor");
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 4 SUCCESS - Created template descriptor");
                
                // Step 6: Find matching font descriptors using Swift approach
                IntPtr matchingDescriptorsRef = CoreText.CTFontDescriptorCreateMatchingFontDescriptors(templateDescriptorRef, IntPtr.Zero);
                CoreFoundation.CFRelease(templateDescriptorRef);
                
                if (matchingDescriptorsRef == IntPtr.Zero)
                {
                    _logger.LogWarning("CHARACTER-SET APPROACH: No matching fonts found for script {Script}", script);
                    return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
                }
                
                IntPtr count = CoreFoundation.CFArrayGetCount(matchingDescriptorsRef);
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 5 SUCCESS - Found {Count} matching font descriptors for script {Script}", (long)count, script);
                
                // Step 7: Extract unique family names (following Swift approach)
                var uniqueFamilies = new HashSet<string>();
                
                for (long i = 0; i < (long)count; i++)
                {
                    IntPtr fontDescriptor = CoreFoundation.CFArrayGetValueAtIndex(matchingDescriptorsRef, (IntPtr)i);
                    
                    IntPtr familyNameRef = CoreText.CTFontDescriptorCopyAttribute(fontDescriptor, CoreText.kCTFontFamilyNameAttribute);
                    if (familyNameRef != IntPtr.Zero)
                    {
                        string? familyName = CoreFoundation.CFStringToString(familyNameRef);
                        if (!string.IsNullOrEmpty(familyName))
                        {
                            uniqueFamilies.Add(familyName);
                            _logger.LogInformation("CHARACTER-SET APPROACH: Found font family: '{FamilyName}'", familyName);
                        }
                        CoreFoundation.CFRelease(familyNameRef);
                    }
                }
                
                CoreFoundation.CFRelease(matchingDescriptorsRef);
                
                var sortedFonts = uniqueFamilies.OrderBy(f => f).ToList();
                _logger.LogInformation("CHARACTER-SET APPROACH: Step 6 SUCCESS - Found {Count} unique font families for script {Script}", sortedFonts.Count, script);
                
                return sortedFonts.Count > 0 ? sortedFonts : new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CHARACTER-SET APPROACH: Exception during character-set-based font detection for script: {Script}", script);
                return new List<string> { "Arial", "Helvetica", "Times New Roman", "Georgia", "Verdana" };
            }
            finally
            {
                if (attributesDictRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(attributesDictRef);
                if (characterSetRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(characterSetRef);
                if (characterStringRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(characterStringRef);
            }
        }

        private string? GetSystemDefaultFontForScript(Script script)
        {
            _logger.LogInformation("Getting system default font for script {Script}", script);
            
            IntPtr sampleStringRef = IntPtr.Zero;
            IntPtr systemFontRef = IntPtr.Zero;
            IntPtr fontForScriptRef = IntPtr.Zero;
            IntPtr familyNameRef = IntPtr.Zero;
            
            try
            {
                // Step 1: Get sample text for the target script - use same text as character set approach
                string? sampleText = GetSampleCharactersForScript(script);
                if (string.IsNullOrEmpty(sampleText))
                {
                    _logger.LogWarning("No sample characters available for script {Script}", script);
                    return null;
                }
                
                _logger.LogInformation("Using sample text '{Text}' for script {Script}", sampleText, script);
                
                // Step 2: Create CFString with sample text
                sampleStringRef = CoreFoundation.CFStringCreateWithCString(
                    IntPtr.Zero,
                    sampleText,
                    CoreFoundation.CFStringEncoding.kCFStringEncodingUTF8);
                    
                if (sampleStringRef == IntPtr.Zero)
                {
                    _logger.LogError("Failed to create CFString for sample text");
                    return null;
                }
                
                // Step 3: Create a default system font
                systemFontRef = CoreText.CTFontCreateUIFontForLanguage(
                    CoreText.CTFontUIFontType.kCTFontUIFontSystem, 
                    18.0, 
                    IntPtr.Zero);
                    
                if (systemFontRef == IntPtr.Zero)
                {
                    _logger.LogError("Failed to create system font");
                    return null;
                }
                
                // Step 4: Use CTFontCreateForStringWithLanguage to determine appropriate font
                IntPtr stringLength = CoreFoundation.CFStringGetLength(sampleStringRef);
                CoreText.CFRange range = CoreText.CFRangeMake(0, (int)(long)stringLength);
                
                fontForScriptRef = CoreText.CTFontCreateForStringWithLanguage(
                    systemFontRef,
                    sampleStringRef,
                    range,
                    IntPtr.Zero);
                    
                if (fontForScriptRef == IntPtr.Zero)
                {
                    _logger.LogError("Failed to create font for script");
                    return null;
                }
                
                // Step 5: Extract the font family name
                familyNameRef = CoreText.CTFontCopyFamilyName(fontForScriptRef);
                if (familyNameRef == IntPtr.Zero)
                {
                    _logger.LogError("Failed to get font family name");
                    return null;
                }
                
                string? familyName = CoreFoundation.CFStringToString(familyNameRef);
                _logger.LogInformation("System will use font family '{FontFamily}' for script {Script}", familyName, script);
                
                return familyName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while getting system default font for script {Script}", script);
                return null;
            }
            finally
            {
                if (familyNameRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(familyNameRef);
                if (fontForScriptRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(fontForScriptRef);
                if (systemFontRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(systemFontRef);
                if (sampleStringRef != IntPtr.Zero)
                    CoreFoundation.CFRelease(sampleStringRef);
            }
        }

        private static string? GetSampleCharactersForScript(Script script)
        {
            // Use proper Pali text with diacritics: "mahāsatipaṭṭhānasuttaṃ"
            const string text = "mahāsatipaṭṭhānasuttaṃ";
            return ScriptConverter.Convert(text, Script.Latin, script);
        }

    }
}