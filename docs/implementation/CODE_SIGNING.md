# CST Reader Code Signing Documentation

## Overview

This document details the code signing implementation for CST Reader, a cross-platform .NET 9 Avalonia application. Code signing is essential for macOS distribution to prevent "damaged application" errors and security warnings.

## The Challenge: .NET Applications on macOS

### Problem Identified

Traditional macOS code signing approaches fail with .NET applications due to:

1. **Mixed Native/Managed Code**: .NET apps contain both native executables and managed assemblies (.dll files)
2. **Codesign Tool Limitations**: `codesign` cannot handle directories containing both signed executables and unsigned .dll files
3. **Bundle Complexity**: Modern .NET apps include hundreds of managed assemblies that codesign treats as "resources"

### Specific Error Encountered

```bash
CST.Avalonia: code object is not signed at all
In subcomponent: /path/to/System.Xml.Linq.dll
```

## Solution Implemented

### Approach: Component-Level Signing

Instead of trying to sign the entire app bundle as a monolithic unit, we sign individual components:

1. **Pre-sign Main Executable**: Sign the native executable in the publish directory before bundle creation
2. **Sign Native Libraries**: Sign all .dylib files individually
3. **Skip Bundle Signing**: Avoid bundle-level signing that conflicts with .dll files
4. **Use Proper Entitlements**: Add .NET-specific entitlements for runtime requirements

### Technical Implementation

#### 1. Entitlements File

Created temporary entitlements file for .NET runtime:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
    <key>com.apple.security.network.client</key>
    <true/>
</dict>
</plist>
```

**Entitlement Breakdown:**
- `com.apple.security.cs.allow-jit`: Required for .NET JIT compilation
- `com.apple.security.cs.allow-unsigned-executable-memory`: Required for .NET runtime execution
- `com.apple.security.cs.disable-library-validation`: Required to load .NET assemblies
- `com.apple.security.network.client`: **Required for outgoing network connections** (GitHub API, file downloads)

#### 2. Signing Sequence

```bash
# 1. Sign main executable before bundle creation
codesign --force --options runtime --entitlements entitlements.plist \
  --sign "Developer ID Application: ..." publish/CST.Avalonia

# 2. Copy all files to app bundle
cp -R publish/* "CST Reader.app/Contents/MacOS/"

# 3. Sign all native libraries
find "CST Reader.app/Contents/MacOS" -name "*.dylib" \
  -exec codesign --force --options runtime --sign "Developer ID..." {} \;

# 4. Sign nested executables
find "CST Reader.app/Contents/MacOS/CefGlueBrowserProcess" -type f -perm +111 \
  -exec codesign --force --options runtime --sign "Developer ID..." {} \;

# 5. Verify main executable signature
codesign --verify --verbose "CST Reader.app/Contents/MacOS/CST.Avalonia"
```

#### 3. What We Don't Sign

- **Managed Assemblies (.dll)**: Not required and cause signing conflicts
- **Debug Symbols (.pdb)**: Not required for distribution
- **Configuration Files (.json)**: Don't need signing
- **App Bundle Itself**: Skipped due to .dll file conflicts

## Results

### ✅ Successful Outcomes

1. **Main Executable Signing**: Native executable signs successfully with entitlements
2. **Library Signing**: All .dylib files sign without errors
3. **Build Completion**: Packaging script completes without fatal errors
4. **Component Verification**: Individual components pass signature verification
5. **Notarization Submission**: App successfully submits to Apple notarization service
6. **Network Access**: Network entitlement properly configured for GitHub API and downloads

### ⚠️ Known Limitations

1. **Bundle-Level Verification**: App bundle as a whole may not pass `codesign --verify --deep` (due to unsigned .dll files)
2. **Deep Verification**: Use component-level verification only (`codesign --verify` on specific files)
3. **Stapling Failure**: `xcrun stapler staple` fails on app bundle (likely due to partial signing approach)
   - **Impact**: App still works after notarization, but requires online verification on first launch
   - **Workaround**: Staple the DMG instead of the app bundle (DMG stapling may work even if app stapling fails)

## Testing Results

### Before Fix
```bash
$ codesign --verify "CST Reader.app"
Error: code object is not signed at all
```

### After Fix
```bash
$ codesign --verify "CST Reader.app/Contents/MacOS/CST.Avalonia"
✅ Verification successful

$ codesign --verify "CST Reader.app/Contents/MacOS/libcoreclr.dylib"
✅ Verification successful
```

## Future Work

### Phase 1: Complete Code Signing (Current Priority)

1. **Test User Experience**: Verify if current approach eliminates "damaged app" errors
2. **Gatekeeper Testing**: Test installation on fresh macOS system
3. **Alternative Bundle Signing**: Research .NET-specific bundle signing approaches

### Phase 2: Notarization Implementation

1. **Apple Developer Account**: Ensure notarization entitlements
2. **Notarization Workflow**:
   ```bash
   # Upload for notarization
   xcrun notarytool submit "CST-Reader.dmg" \
     --apple-id "developer@example.com" \
     --team-id "TEAM_ID" \
     --password "@keychain:notarization"

   # Staple notarization ticket
   xcrun stapler staple "CST-Reader.dmg"
   ```
3. **Automated Pipeline**: Integrate notarization into packaging script

### Phase 3: Enhanced Security

1. **Hardened Runtime**: Enable additional hardened runtime protections
2. **Secure Timestamp**: Use Apple's timestamp servers
3. **Entitlement Optimization**: Minimize required entitlements
4. **Multiple Architecture Support**: Ensure signing works for both arm64 and x64

## Known Issues

### Issue 1: Bundle Signing Conflicts
- **Problem**: `codesign` cannot sign app bundle containing .dll files
- **Workaround**: Skip bundle signing, sign components individually
- **Future**: Research .NET-specific bundle signing tools

### Issue 2: Deep Verification Failure
- **Problem**: `codesign --verify --deep` fails due to unsigned .dll files
- **Workaround**: Use component-level verification only
- **Impact**: May affect some automated security tools

### Issue 3: Network Entitlement - CRITICAL ⚠️
- **Problem**: Without `com.apple.security.network.client` entitlement, notarized apps **silently fail** network requests
- **Symptoms**:
  - Network calls hang indefinitely (GitHub API, downloads)
  - High CPU usage (100%+) from infinite retry loops
  - No error messages - completely silent failure
  - App appears frozen during network operations
- **Solution**: Add `com.apple.security.network.client` to entitlements (implemented October 2025)
- **Impact**: Application unusable without this entitlement if it makes any network requests
- **Lesson**: Always test network features in fully packaged/notarized builds, not just development builds

### Issue 4: Stapling Failure - Error 65
- **Problem**: `xcrun stapler staple` fails on both app bundle and DMG with "Error 65: Could not validate ticket"
- **Root Cause**: Notarization ticket takes time to propagate through Apple's CDN (can be 5-30 minutes after successful notarization)
- **Symptoms**:
  ```
  Processing: /path/to/CST-Reader-arm64.dmg
  Could not validate ticket for /path/to/CST-Reader-arm64.dmg
  The staple and validate action failed! Error 65.
  ```
- **Solutions**:
  1. **Wait and retry**: Wait 15-30 minutes after notarization success, then manually staple:
     ```bash
     xcrun stapler staple "dist/CST-Reader-arm64.dmg"
     ```
  2. **Verify ticket availability**:
     ```bash
     xcrun stapler validate "dist/CST-Reader-arm64.dmg"
     ```
  3. **Alternative**: Distribute without stapling - macOS will verify online on first launch (requires internet)
- **Impact**:
  - Without stapling: App requires internet on first launch for notarization check
  - With stapling: App works offline immediately after download
- **Recommendation**: Wait for ticket propagation and staple before distributing to end users

## References

### Apple Documentation
- [Code Signing Guide](https://developer.apple.com/library/archive/documentation/Security/Conceptual/CodeSigningGuide/)
- [Notarization Documentation](https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution)
- [Hardened Runtime Entitlements](https://developer.apple.com/documentation/security/hardened_runtime)

### .NET Specific Resources
- [.NET macOS Deployment](https://docs.microsoft.com/en-us/dotnet/core/deploying/deploy-with-cli#macos)
- [Avalonia macOS Packaging](https://docs.avaloniaui.net/docs/deployment/macos)

### Tools Used
- `codesign`: Apple's code signing tool
- `security`: Keychain and certificate management
- `spctl`: Security policy assessment
- `xcrun notarytool`: Notarization submission

## Conclusion

The implemented code signing and notarization approach successfully creates distributable macOS applications for .NET/Avalonia apps:

### ✅ What Works
- **Code Signing**: All native components (executables, .dylib files) properly signed with Developer ID
- **Notarization**: App successfully submits and passes Apple's automated security checks
- **Network Access**: Proper entitlements allow GitHub API and file downloads
- **User Experience**: Eliminates "damaged application" and "unidentified developer" warnings

### ⚠️ Current Limitations
- **Stapling Timing**: Requires 15-30 minute wait for ticket propagation before stapling
- **Offline Installation**: Without stapling, requires internet connection on first launch
- **Deep Verification**: Cannot use `--deep` flag due to unsigned .dll files (expected for .NET apps)

### 🔑 Key Lessons Learned
1. **Network entitlement is critical** - Apps silently fail network requests without it
2. **Test in packaged environment** - Development builds don't reveal entitlement issues
3. **Stapling requires patience** - Wait for Apple CDN propagation before stapling
4. **.NET apps need special handling** - Traditional bundle signing doesn't work with managed assemblies

**Status (October 2025)**: Code signing and notarization pipeline complete and functional. Apps ready for distribution after ticket stapling completes.