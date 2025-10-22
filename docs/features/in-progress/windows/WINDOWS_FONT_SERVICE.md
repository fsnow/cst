# WindowsFontService Implementation Plan

**Created**: October 22, 2025
**Status**: Design Phase
**Priority**: HIGH - Early testing requirement for Windows 11 port

## Overview

Implement Windows equivalent of `MacFontService` to detect script-compatible fonts using native DirectWrite APIs. This will provide the same high-quality font detection experience on Windows 11 as we have on macOS.

## Architecture

### File Location
```
Services/Platform/Windows/WindowsFontService.cs
```

### Class Structure
```csharp
namespace CST.Avalonia.Services.Platform.Windows
{
    // DirectWrite P/Invoke declarations
    public static partial class DirectWrite { ... }

    // COM interop helpers
    public static partial class ComHelpers { ... }

    // WindowsFontService implementation
    public class WindowsFontService
    {
        Task<List<string>> GetAvailableFontsForScriptAsync(Script script);
        Task<string?> GetSystemDefaultFontForScriptAsync(Script script);
    }
}
```

## Windows DirectWrite API

DirectWrite is Microsoft's modern text rendering API (Windows 7+, excellent support in Windows 11). It provides:
- Font enumeration and querying
- Unicode script detection
- Font fallback mechanisms
- Font metadata access

### Key DirectWrite Interfaces

1. **IDWriteFactory** - Factory for creating DirectWrite objects
2. **IDWriteFontCollection** - System font collection
3. **IDWriteFontFamily** - Group of fonts with same family name
4. **IDWriteFont** - Individual font face
5. **IDWriteLocalizedStrings** - Localized font names
6. **IDWriteTextAnalyzer** - Script analysis for text

### P/Invoke Approach

Unlike macOS Core Text (C-style API), DirectWrite is a **COM-based API**. We have two options:

#### Option A: Direct COM Interop (Recommended)
- Define COM interfaces with `[ComImport]` and `[Guid]` attributes
- Use `Marshal.GetDelegateForFunctionPointer` to call vtable methods
- More complex but avoids external dependencies
- Full control over memory management

#### Option B: Use TerraFX.Interop.Windows NuGet Package
- Pre-built DirectWrite bindings
- Easier to implement
- Additional dependency (~1MB)
- Used by WinUI and other modern Windows apps

**Recommendation**: Start with **Option A** (Direct COM Interop) to match the MacFontService approach and avoid dependencies.

## Implementation Plan

### 1. DirectWrite COM Interfaces (P/Invoke)

```csharp
public static partial class DirectWrite
{
    // dwrite.dll - DirectWrite library
    private const string DWriteLibrary = "dwrite.dll";

    // Factory creation
    [DllImport(DWriteLibrary)]
    public static extern int DWriteCreateFactory(
        DWRITE_FACTORY_TYPE factoryType,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    // COM interface GUIDs
    public static readonly Guid IID_IDWriteFactory =
        new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    // Enums
    public enum DWRITE_FACTORY_TYPE
    {
        DWRITE_FACTORY_TYPE_SHARED = 0,
        DWRITE_FACTORY_TYPE_ISOLATED = 1
    }

    // IDWriteFactory interface definition
    [ComImport]
    [Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDWriteFactory
    {
        // Vtable methods (order matters!)
        void GetSystemFontCollection(out IDWriteFontCollection fontCollection, bool checkForUpdates);
        // ... other methods
    }

    // IDWriteFontCollection interface
    [ComImport]
    [Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDWriteFontCollection
    {
        uint GetFontFamilyCount();
        void GetFontFamily(uint index, out IDWriteFontFamily fontFamily);
        // ... other methods
    }

    // IDWriteFontFamily interface
    [ComImport]
    [Guid("da20d8ef-812a-4c43-9802-62ec4abd7add")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDWriteFontFamily
    {
        void GetFamilyNames(out IDWriteLocalizedStrings names);
        // ... other methods
    }

    // IDWriteLocalizedStrings interface
    [ComImport]
    [Guid("08256209-099a-4b34-b86d-c22b110e7771")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDWriteLocalizedStrings
    {
        uint GetCount();
        void GetLocaleName(uint index, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder localeName, uint size);
        void GetString(uint index, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder stringBuffer, uint size);
        uint GetStringLength(uint index);
        // ... other methods
    }
}
```

### 2. Character-Based Font Detection (Matching MacFontService)

MacFontService uses `CFCharacterSetCreateWithCharactersInString()` + `CTFontDescriptorCreateMatchingFontDescriptors()`.

Windows equivalent approach:

```csharp
private List<string> GetFontsForScript(Script script)
{
    // Step 1: Get sample characters for script (same as MacFontService)
    string? sampleChars = GetSampleCharactersForScript(script);
    if (string.IsNullOrEmpty(sampleChars))
        return GetFallbackFonts();

    // Step 2: Create DirectWrite factory
    DWriteCreateFactory(
        DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
        DirectWrite.IID_IDWriteFactory,
        out object factoryObj);
    var factory = (IDWriteFactory)factoryObj;

    // Step 3: Get system font collection
    factory.GetSystemFontCollection(out IDWriteFontCollection fontCollection, false);

    // Step 4: Enumerate fonts and test if they support our sample characters
    uint familyCount = fontCollection.GetFontFamilyCount();
    var supportedFonts = new HashSet<string>();

    for (uint i = 0; i < familyCount; i++)
    {
        fontCollection.GetFontFamily(i, out IDWriteFontFamily family);

        // Get family name
        family.GetFamilyNames(out IDWriteLocalizedStrings names);
        string? familyName = GetEnglishFontName(names);

        if (string.IsNullOrEmpty(familyName))
            continue;

        // Check if font supports sample characters
        if (FontSupportsText(family, sampleChars))
        {
            supportedFonts.Add(familyName);
        }
    }

    return supportedFonts.OrderBy(f => f).ToList();
}

private bool FontSupportsText(IDWriteFontFamily family, string text)
{
    // Get first font in family
    family.GetFont(0, out IDWriteFont font);

    // Check if font has glyphs for all characters in text
    foreach (char c in text)
    {
        font.HasCharacter((uint)c, out bool exists);
        if (!exists)
            return false;
    }

    return true;
}
```

### 3. System Default Font Detection

Windows doesn't have a direct equivalent to macOS's `CTFontCreateForStringWithLanguage()`, but we can use:

**Approach**: Use `IDWriteTextLayout` + font fallback to determine what font Windows would choose:

```csharp
private string? GetSystemDefaultFontForScript(Script script)
{
    // Get sample text
    string? sampleText = GetSampleCharactersForScript(script);
    if (string.IsNullOrEmpty(sampleText))
        return null;

    // Create DirectWrite factory
    DWriteCreateFactory(
        DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
        DirectWrite.IID_IDWriteFactory,
        out object factoryObj);
    var factory = (IDWriteFactory)factoryObj;

    // Create text format with default system font
    factory.CreateTextFormat(
        "Segoe UI",  // Default Windows font
        null,        // Font collection (null = system)
        DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
        DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
        16.0f,       // Font size
        "en-us",     // Locale
        out IDWriteTextFormat textFormat);

    // Create text layout for sample text
    factory.CreateTextLayout(
        sampleText,
        (uint)sampleText.Length,
        textFormat,
        1000f,  // Max width
        100f,   // Max height
        out IDWriteTextLayout textLayout);

    // Get font collection from layout (this triggers fallback)
    textLayout.GetFontCollection(0, out IDWriteFontCollection fontCollection, out _);
    textLayout.GetFontFamilyName(0, out string fontFamilyName, 256);

    return fontFamilyName;
}
```

**Alternative Simpler Approach** (May be sufficient):

Just return known defaults based on script:
```csharp
private string? GetSystemDefaultFontForScript(Script script)
{
    return script switch
    {
        Script.Latin => "Segoe UI Variable",
        Script.Cyrillic => "Segoe UI Variable",
        Script.Devanagari => "Nirmala UI",
        Script.Bengali => "Nirmala UI",
        Script.Gujarati => "Nirmala UI",
        Script.Gurmukhi => "Nirmala UI",
        Script.Kannada => "Nirmala UI",
        Script.Malayalam => "Nirmala UI",
        Script.Sinhala => "Nirmala UI",
        Script.Telugu => "Nirmala UI",
        Script.Myanmar => "Myanmar Text",
        Script.Khmer => "Leelawadee UI",
        Script.Thai => "Leelawadee UI",
        Script.Tibetan => "Microsoft Himalaya",
        _ => null
    };
}
```

**Recommendation**: Start with the **simple hardcoded approach** since we've already researched the defaults. This matches what Windows 11 actually uses and is much simpler to implement and test.

### 4. Sample Character Generation (Reuse Existing Code)

```csharp
private static string? GetSampleCharactersForScript(Script script)
{
    // Identical to MacFontService - use proper Pali text
    const string text = "mahāsatipaṭṭhānasuttaṃ";
    return ScriptConverter.Convert(text, Script.Latin, script);
}
```

## Implementation Priority

### Phase 1: Minimal Implementation (1-2 hours)
1. Create `WindowsFontService.cs` file structure
2. Implement hardcoded `GetSystemDefaultFontForScriptAsync()` (simple approach)
3. Implement `GetAvailableFontsForScriptAsync()` using `FontManager.Current.SystemFonts` (fallback)
4. Register in DI container with `#if WINDOWS` condition
5. **TEST**: Verify all 14 Pali scripts display correctly on Windows 11

### Phase 2: DirectWrite Integration (2-4 hours)
1. Add DirectWrite COM interface definitions
2. Implement character-based font detection in `GetAvailableFontsForScriptAsync()`
3. Add proper error handling and logging
4. **TEST**: Compare detected fonts with macOS results

### Phase 3: Polish (1-2 hours)
1. Add caching (match MacFontService behavior)
2. Improve error messages
3. Performance optimization
4. Documentation

## Testing Strategy

### Early Testing (Phase 1)
- Build on Windows 11 machine
- Run application with minimal WindowsFontService
- Open books in all 14 Pali scripts
- Verify fonts display correctly
- Check Settings > Fonts UI for each script

### Full Testing (Phase 2)
- Compare font lists with macOS (should be similar quality)
- Test on "base" Windows 11 install (user's wife's machine)
- Verify no fonts need to be installed
- Test font settings persistence

### Performance Testing (Phase 3)
- Measure font enumeration time (should be < 100ms)
- Check memory usage
- Verify no COM leaks

## Known Challenges

### 1. COM Memory Management
- Must call `Marshal.ReleaseComObject()` on all COM interfaces
- Use `using` pattern or `try/finally` blocks
- DirectWrite objects must be released in correct order

### 2. Unicode Support
- DirectWrite uses UTF-16 (Windows native)
- C# strings are UTF-16, so no conversion needed
- Watch for surrogate pairs in Tibetan/other scripts

### 3. Font Fallback Chain
- Windows has complex font fallback (Segoe UI → Nirmala UI → etc.)
- Our simple approach may not match system exactly
- Acceptable for CST Reader since we just need "fonts that work"

## Dependencies

### Required References
```xml
<PackageReference Include="System.Runtime.InteropServices" Version="9.0.0" />
```

### No Additional NuGet Packages
- Use built-in .NET 9 P/Invoke and COM interop
- Matches MacFontService approach (no external dependencies)

## Success Criteria

✅ **Minimal Success** (Phase 1 - Required for Windows Port):
- All 14 Pali scripts display correctly on Windows 11
- Font settings UI works
- No crashes or errors

✅ **Full Success** (Phase 2 - Desired):
- Font detection quality matches macOS
- Filtered font lists (not just all system fonts)
- System defaults match Windows 11 behavior

✅ **Polish** (Phase 3 - Nice to Have):
- Fast font enumeration (< 100ms)
- Comprehensive logging
- No COM memory leaks

## Next Steps

1. **Create WindowsFontService.cs** with Phase 1 implementation
2. **Update App.axaml.cs** to register WindowsFontService on Windows:
   ```csharp
   #if WINDOWS
   services.AddSingleton<WindowsFontService>();
   #endif
   ```
3. **Update FontService.cs** to use WindowsFontService on Windows (if platform-specific service exists)
4. **Build on Windows 11** and test all 14 Pali scripts
5. **Document results** and proceed with Phase 2 if needed

## References

- [DirectWrite API Documentation](https://learn.microsoft.com/en-us/windows/win32/directwrite/direct-write-portal)
- [COM Interop in .NET](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop)
- [Windows 11 Font List](https://learn.microsoft.com/en-us/typography/fonts/windows_11_font_list)
- [Script and Font Support in Windows](https://learn.microsoft.com/en-us/globalization/fonts-layout/font-support)
