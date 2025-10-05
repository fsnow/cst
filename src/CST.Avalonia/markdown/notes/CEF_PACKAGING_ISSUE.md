# CEF WebView Exception Loop in Packaged macOS Application

## Issue Summary

**Date Discovered:** October 4, 2025
**Severity:** Critical - Blocking Beta 2 Release
**Symptoms:**
- WebView displays white page in packaged/notarized app
- 117% CPU usage (infinite loop)
- UI completely unresponsive
- App works perfectly in development (`dotnet run`)

## Root Cause Analysis

### Profiling Results

Using `sample` command on running process (PID 69755):
```
Stack trace shows infinite loop in:
libcef.dylib → PAL_DispatchException (in libcoreclr.dylib)

Chromium Embedded Framework attempting to spawn helper processes
→ Helper processes not found in expected location
→ Spawn attempts fail repeatedly
→ Exception handling loop consumes CPU
→ Renderer process can't start → white page
```

### Architecture Issue

**Problem:** CEF (Chromium Embedded Framework) requires 4 separate Helper app bundles in `Contents/Frameworks/` for macOS apps, but our packaging script doesn't create them.

**Current (Broken) Structure:**
```
CST Reader.app/
└── Contents/
    └── MacOS/
        ├── CST.Avalonia (main executable)
        └── CefGlueBrowserProcess/  ← WRONG LOCATION, WRONG STRUCTURE
            ├── Xilium.CefGlue.BrowserProcess
            └── *.dylib files
```

**Required Structure:**
```
CST Reader.app/
├── Contents/
│   ├── MacOS/
│   │   └── CST.Avalonia (main executable)
│   └── Frameworks/
│       ├── CST Reader Helper.app/
│       │   └── Contents/
│       │       ├── Info.plist
│       │       ├── MacOS/
│       │       │   └── CST Reader Helper
│       │       └── Frameworks/
│       ├── CST Reader Helper (GPU).app/
│       │   └── Contents/ (same structure)
│       ├── CST Reader Helper (Plugin).app/
│       │   └── Contents/ (same structure)
│       └── CST Reader Helper (Renderer).app/
│           └── Contents/ (same structure)
```

## Why This Happens

### CEF 3809+ Requirements

Starting with CEF version 3809 (we're using CEF 120.1.8 via WebViewControl-Avalonia), macOS apps **must** contain 4 separate Helper app bundles:

1. **Main Helper** - Manages sub-processes
2. **GPU Helper** - Handles graphics/rendering acceleration
3. **Plugin Helper** - Manages plugin processes
4. **Renderer Helper** - Executes web content rendering (JavaScript, HTML)

### Why Development Works but Packaged Fails

**Development (`dotnet run`):**
- .NET runs CEF in a different process model
- CEF finds helper executables in publish directory structure
- Less strict about macOS bundle hierarchy

**Packaged/Notarized App:**
- macOS enforces strict bundle hierarchy
- CEF expects helpers in `Contents/Frameworks/` as `.app` bundles
- Missing bundles → spawn failures → exception loop

## Technical Details

### Helper Bundle Requirements

Each helper bundle needs:

1. **Proper `.app` structure** with `Contents/MacOS/` and `Contents/Frameworks/`
2. **Individual Info.plist** with unique identifiers:
   - `CFBundleExecutable`: Helper name
   - `CFBundleIdentifier`: Unique ID (e.g., `com.cst.avalonia.helper.gpu`)
   - `LSBackgroundOnly`: `true` (helpers are background processes)
3. **Individual code signing** with appropriate entitlements
4. **Chromium Embedded Framework.framework** (optional, can be shared)

### Entitlements Per Helper Type

**GPU Helper** (needs graphics access):
- `com.apple.security.cs.allow-jit` ✓
- `com.apple.security.cs.allow-unsigned-executable-memory` ✓
- `com.apple.security.cs.disable-library-validation` ✓
- `com.apple.security.network.client` ✓

**Renderer Helper** (needs JIT for JavaScript):
- Same as GPU (JavaScript execution requires JIT)

**Plugin Helper** (fewer permissions):
- `com.apple.security.cs.allow-unsigned-executable-memory` ✓
- `com.apple.security.cs.disable-library-validation` ✓

**Main Helper**:
- All entitlements (spawns other processes)

### CEF Initialization Code

In C# code, must set browser subprocess path:

```csharp
#if MACOS
var mainBundlePath = Foundation.NSBundle.MainBundle.BundlePath;
var helperPath = Path.Combine(mainBundlePath,
    "Contents/Frameworks/CST Reader Helper.app/Contents/MacOS/CST Reader Helper");

cefSettings.BrowserSubprocessPath = helperPath;
#endif
```

CEF automatically discovers variant helpers (GPU, Plugin, Renderer) based on main helper path.

## WebViewControl-Avalonia Package Limitations

The `WebViewControl-Avalonia` package (version 3.120.9) used in this project:
- Is a wrapper around CefGlue (which wraps CEF)
- Does **NOT** automatically create required Helper app bundles for macOS
- Provides `CefGlueBrowserProcess/` as flat directory with helper executable
- **Requires manual packaging** into proper macOS app bundle structure

This is not a bug in the package - it's a macOS-specific packaging requirement that developers must handle.

## Signing Requirements

### Critical Signing Order

Must sign from **inside-out** (deepest components first):

1. Sign all `.dylib` files in each helper bundle
2. Sign helper executables (with entitlements)
3. Sign helper app bundles (with entitlements)
4. Sign main app frameworks/libraries
5. Sign main executable (with entitlements)
6. Sign main app bundle (with entitlements)

**DO NOT** use `--deep` flag - it doesn't work correctly with embedded app bundles. Sign each component individually.

### Verification Commands

```bash
# Verify entire app structure
codesign --verify --deep --strict --verbose=2 "CST Reader.app"

# Check helper bundle signature
codesign --display --verbose=4 "CST Reader.app/Contents/Frameworks/CST Reader Helper.app"

# Verify helper executable entitlements
codesign -d --entitlements - "CST Reader.app/Contents/Frameworks/CST Reader Helper.app/Contents/MacOS/CST Reader Helper"
```

## Common macOS CEF Packaging Mistakes

1. ❌ **Placing CEF libraries in `Contents/MacOS/`** instead of `Contents/Frameworks/`
2. ❌ **Using flat directories** instead of proper `.app` bundle structures
3. ❌ **Missing helper variants** (only creating one helper instead of four)
4. ❌ **Signing helpers without individual entitlements**
5. ❌ **Using `--deep` signing flag** (doesn't work with embedded bundles)
6. ❌ **Not setting `BrowserSubprocessPath`** in CEF initialization code
7. ❌ **Incorrect signing order** (must be inside-out, not outside-in)

## Diagnostic History

### Timeline of Discovery

**October 3, 2025:**
- Removed splash screen, added Welcome page status banner
- Added network timeouts and entitlements
- All changes tested in development - worked perfectly

**October 4, 2025 (Morning):**
- Packaged app with network entitlement
- App built at 10:51 AM

**October 4, 2025 (Afternoon):**
- Tested packaged app: white page, 117% CPU, unresponsive
- Log showed successful initialization (network calls worked, indexing completed)
- UI stuck showing "Getting repository information..."
- Status updates not reaching UI thread

**Profiling Investigation:**
- Used `sample` command on running process
- Discovered CEF exception loop in `libcef.dylib`
- Stack trace: `PAL_DispatchException` called repeatedly
- Identified root cause: missing Helper app bundles

### Key Insight

The issue was **never** about our code fixes (welcome page, timeouts, entitlements). Those all worked correctly as evidenced by successful network calls and indexing in the logs.

The issue was **CEF packaging** - the WebView couldn't function because required helper processes couldn't be spawned due to missing app bundle structure.

## Solution Implementation

### Files to Modify

1. **`/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/package-macos.sh`**
   - Add helper bundle creation after line 80
   - Update signing section (lines 157-175) to sign helpers first
   - Remove CefGlueBrowserProcess from MacOS directory

2. **`/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/App.axaml.cs`**
   - Add CEF subprocess path configuration before CEF initialization
   - Use conditional compilation for macOS-specific code

### Testing Checklist

After implementing fix:

- [ ] Verify bundle structure: `ls -la "CST Reader.app/Contents/Frameworks/"`
- [ ] Verify all 4 helper bundles exist
- [ ] Check helper executable names match Info.plist
- [ ] Verify code signatures: `codesign --verify --deep --strict --verbose=2 "CST Reader.app"`
- [ ] Test packaged app launches without exception loop
- [ ] Verify WebView displays content (not white page)
- [ ] Check CPU usage is normal (~5-10% idle, not 117%)
- [ ] Test on second Mac (Caracara - Intel) for cross-architecture validation

## References

### CEF Documentation
- **macOS Bundle Structure**: https://bitbucket.org/chromiumembedded/cef/wiki/GeneralUsage#markdown-header-macos-bundle-structure
- **CEF Issue #2744**: Documents code signing with notarization requirements
- **Helper Process Requirements (CEF 3809+)**: Apps must contain 4 helper bundles for different process types

### Apple Documentation
- **Hardened Runtime**: https://developer.apple.com/documentation/security/hardened_runtime
- **App Bundle Structure**: https://developer.apple.com/library/archive/documentation/CoreFoundation/Conceptual/CFBundles/BundleTypes/BundleTypes.html
- **Notarization Requirements**: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution

### Related Issues
- CEF macOS notarization requires proper helper bundle structure (discovered by many developers migrating to macOS 10.15+)
- Hardened Runtime enforcement stricter in notarized apps vs. development builds
- WebViewControl-Avalonia doesn't automatically handle macOS-specific packaging

## Lessons Learned

1. **Development != Production**: Development builds bypass many macOS restrictions that packaged apps must follow
2. **Profile Before Assuming**: Used `sample` to find actual CPU hotspot instead of guessing
3. **WebView is Complex**: CEF requires significant macOS-specific packaging that's not obvious from documentation
4. **Verify Assumptions**: Multiple times we assumed code was the issue when it was actually packaging structure
5. **Test Packaged Apps Early**: Don't wait until release to test fully packaged/notarized builds

## Impact on Beta 2 Release

**Original Plan:** Release Beta 2 today (October 4, 2025)
**Blocker:** CEF packaging issue prevents WebView from working in packaged apps
**New Plan:**
1. Implement helper bundle packaging (this session)
2. Test on both Kestrel (M4/arm64) and Caracara (Intel/x64)
3. Release Beta 2 once WebView confirmed working in packaged apps

**Confidence Level:** HIGH - Root cause clearly identified, solution well-documented in CEF documentation, implementation straightforward.
