# Loading Large HTML Content in CefGlue.Avalonia

## Problem
The default approach of using data URIs to load HTML content has a 2MB limit due to URL length restrictions:
```csharp
string dataUri = "data:text/html;charset=utf-8," + Uri.EscapeDataString(htmlContent);
_cefBrowser.Address = dataUri; // Fails for content > 2MB
```

## Solutions

### 1. Custom Scheme Handler (Implemented)
This is the most robust solution that bypasses all URL length limitations.

**Implementation:**
- `CefCustomSchemeHandler.cs` - Defines a custom scheme handler factory and resource handler
- Registers a custom scheme `cst://local/` that serves HTML content from memory
- No size limitations - can handle any amount of HTML content

**Usage:**
```csharp
// Set the HTML content
CstSchemeHandlerFactory.SetHtmlContent(htmlContent);

// Load using custom scheme URL
_cefBrowser.Address = "cst://local/content.html";
```

**Advantages:**
- No size limitations
- Clean URL in address bar
- Can be extended to serve multiple resources (CSS, JS, images)
- Works with all CEF versions

**Configuration:**
The custom scheme is registered in `Program.cs` during CefGlue initialization:
```csharp
CefRuntime.RegisterSchemeHandlerFactory(
    CstSchemeHandlerFactory.SchemeName,
    CstSchemeHandlerFactory.DomainName,
    new CstSchemeHandlerFactory()
);
```

### 2. Direct Load via Browser API (Alternative)
Using `CefDirectLoadHelper.cs` for direct browser frame access.

**Method 1: LoadRequest with POST data**
```csharp
CefDirectLoadHelper.LoadHtmlDirect(browser, htmlContent);
```
This creates a POST request with the HTML as body data.

**Method 2: LoadString (if available)**
```csharp
CefDirectLoadHelper.TryLoadString(browser, htmlContent);
```
Uses reflection to check if the deprecated LoadString method is available.

### 3. File-Based Approach (Not Implemented)
Write HTML to a temporary file and load via file:// URL:
```csharp
var tempFile = Path.GetTempFileName() + ".html";
File.WriteAllText(tempFile, htmlContent);
_cefBrowser.Address = "file://" + tempFile;
```

## Current Implementation

The project currently uses **Solution 1 (Custom Scheme Handler)** as it's the most reliable and cross-platform approach. The implementation in `BookDisplayView.axaml.cs` has been updated to:

1. Store HTML content in the scheme handler factory
2. Load content using the custom scheme URL
3. No longer check for 2MB limit since there isn't one

## Testing

To test with large content:
1. Load a large book that generates > 2MB of HTML
2. Verify it loads successfully without falling back to text display
3. Check console output for "Loading content via custom scheme" message

## Future Enhancements

1. **Multiple Content Support**: Extend the scheme handler to serve multiple resources (CSS, JavaScript, images) from memory
2. **Caching**: Add caching to the scheme handler to improve performance
3. **Streaming**: For very large content, implement streaming in the resource handler
4. **Error Handling**: Add better error handling and fallback mechanisms

## References

- CEF Custom Scheme Documentation: https://bitbucket.org/chromiumembedded/cef/wiki/GeneralUsage#markdown-header-custom-schemes
- CefGlue Examples: https://gitlab.com/xiliumhq/chromiumembedded/cefglue
- CEF Forum discussions on LoadString deprecation and alternatives