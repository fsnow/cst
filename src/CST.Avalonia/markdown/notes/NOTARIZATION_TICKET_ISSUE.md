# macOS Notarization Ticket Parsing Issue

**Date:** October 4, 2025
**App Version:** 5.0.0-beta.2
**Platform:** macOS Sequoia 15.6.1
**Status:** Unresolved - Blocking Beta 2 Release

## Issue Summary

CST Reader DMGs are successfully notarized by Apple but the notarization ticket cannot be parsed by macOS Sequoia, resulting in app rejection when downloaded from the internet.

### Symptoms

- ✅ Notarization submission succeeds (Apple accepts with status "Accepted")
- ✅ Notarization ticket is created and can be downloaded from Apple's CloudKit servers
- ❌ Stapler fails with Error 65: "Could not validate ticket"
- ❌ macOS system logs show: `syspolicyd: Unable to parse ticket`
- ❌ macOS system logs show: `error registering ticket: -1`
- ❌ Gatekeeper rejects app as "Unnotarized Developer ID"
- ❌ Users see: "Apple could not verify 'CST Reader' is free of malware"

### What Works vs. What Fails

**Local Installation (No Quarantine):**
- Install from local `dist/` folder → ✅ **Works**
- No quarantine attribute applied
- macOS trusts Developer ID signature without checking notarization

**Downloaded Installation (With Quarantine):**
- Download from GitHub → ❌ **Fails**
- Browser applies `com.apple.quarantine` attribute
- macOS requires notarization verification
- Ticket download succeeds but parsing fails
- App is rejected

## Timeline

- **September 24, 2025**: Issue first observed
- **October 4, 2025**: Attempted multiple fixes, all unsuccessful
- **Duration**: 2+ weeks of consistent failure

## Technical Details

### Notarization Evidence

**Successful Notarization:**
```json
{
  "status": "Accepted",
  "statusSummary": "Ready for distribution",
  "id": "8da1570b-dfc0-4057-8e54-a8e3b8838bd9"
}
```

**Ticket Download:**
```
Downloaded ticket has been stored at file:///var/folders/.../xxx.ticket
```

**Ticket Metadata:**
- DMG cdhash: `ffe1ca61d2f007dc8a3279b8f4d247b1e876233a`
- Ticket cdhash: `ffe1ca61d2f007dc8a3279b8f4d247b1e876233a`
- ✅ Hashes match exactly

### System Logs

```
2025-10-04 22:43:02 syspolicyd[665]: Unable to parse ticket.
2025-10-04 22:43:31 syspolicyd[665]: Unable to parse ticket.
2025-10-04 22:43:31 syspolicyd[665]: error registering ticket: -1
2025-10-04 22:43:37 syspolicyd[665]: Adding Gatekeeper denial breadcrumb
```

The system successfully:
1. Establishes TLS connection to Apple's servers
2. Downloads the ticket data
3. **Fails to parse the ticket format**

### Stapler Verbose Output

```
Processing: dist/CST-Reader-arm64.dmg
Properties are {
    NSURLTypeIdentifierKey = "com.apple.disk-image-udif";
}
Codesign offset 0xc03dbb6 length: 9564
Props are {
    cdhash = {length = 20, bytes = 0xffe1ca61d2f007dc8a3279b8f4d247b1e876233a};
    teamId = 69M77LM9K3;
}
Response is <NSHTTPURLResponse: 200 OK>
Size of data is 16177
Downloaded ticket has been stored at file:///.../xxx.ticket
Could not validate ticket for dist/CST-Reader-arm64.dmg
The staple and validate action failed! Error 65.
```

**Key observation:** Ticket downloads successfully (200 OK, 16KB data), but validation fails.

## Attempted Fixes

### 1. Remove DMG Signing (October 4)

**Hypothesis:** Research suggested signing DMG after creation changes cdhash and breaks ticket.

**Action:**
- Removed `codesign --force --sign` step from `package-macos.sh`
- Created unsigned DMG
- Re-notarized

**Result:** ❌ **Failed**
- Notarization succeeded
- Stapling still fails with Error 65
- Ticket still unparseable

**Conclusion:** DMG signing order is not the root cause.

### 2. Verify Hash Matching (October 4)

**Action:**
- Compared DMG cdhash to ticket cdhash
- Verified DMG SHA256 matches between local and downloaded

**Result:** ✅ All hashes match exactly
- DMG SHA256: `de9526e10d95da7c7c5aa0251c5410e9f8839fb05a0778abdffb153c7a73ba0f`
- Local vs downloaded: Identical bytes
- cdhash: Matches between DMG and ticket

**Conclusion:** Hash mismatch is not the issue.

### 3. Wait for CDN Propagation

**Action:**
- Waited 10+ seconds between notarization and stapling
- Retried stapling multiple times over hours

**Result:** ❌ **Failed**
- Ticket exists and downloads immediately
- No propagation delay observed
- Error persists regardless of wait time

**Conclusion:** CDN propagation is not the issue.

## Root Cause Analysis

### Why the Ticket is Unparseable

The ticket data is **malformed or in an incompatible format**. Possible causes:

1. **macOS Sequoia Bug**
   - Sequoia 15.6.1 may have a ticket parsing regression
   - No public bug reports found matching this pattern
   - Other apps reportedly notarize successfully on Sequoia

2. **.NET 9 / Avalonia / CEF Combination**
   - Complex app structure with 4 CEF helper bundles
   - .NET 9 with hardened runtime entitlements
   - May trigger edge case in Apple's ticket generation

3. **Apple Notarization Service Issue**
   - Service may be generating malformed tickets for specific app types
   - Ticket format may be incompatible with Sequoia's parser
   - Issue may be intermittent or architecture-specific

### Why Local Install Works

**Without quarantine attribute:**
- macOS sees Developer ID signature
- Assumes app is from trusted developer
- **Does not check notarization**
- App launches successfully

**With quarantine attribute (download):**
- macOS sees Developer ID signature
- **Requires notarization verification** (Sequoia policy)
- Downloads ticket from Apple servers ✅
- Attempts to parse ticket ❌
- Parsing fails → Gatekeeper rejection

## Workarounds

### For Developers (Testing)

Install from local DMG without quarantine:
```bash
# Option 1: Install directly
open dist/CST-Reader-arm64.dmg
# Drag to Applications (no quarantine)

# Option 2: Remove quarantine from downloaded DMG
xattr -rd com.apple.quarantine ~/Downloads/CST-Reader-arm64.dmg
```

### For Users (Current Situation)

**Manual Gatekeeper Bypass:**
1. Download DMG from GitHub
2. Double-click to mount
3. Drag app to Applications
4. When security warning appears, click "Done"
5. Open System Settings → Privacy & Security
6. Scroll to "Security" section
7. Click "Open Anyway" next to CST Reader
8. Confirm in dialog

**Note:** This is not a professional solution for public release.

## Outstanding Questions

1. **Why did this start 2 weeks ago?**
   - Was there a macOS 15.6.1 update that changed ticket parsing?
   - Did Apple's notarization service change ticket format?
   - Did .NET 9 / Avalonia / CEF versions change?

2. **Why does the research suggest signing order matters?**
   - Multiple sources cite signing-after-notarization as causing Error 65
   - Our testing shows this is not the cause in our case
   - May be a different manifestation of Error 65

3. **Can this be fixed on our end?**
   - All attempted fixes have failed
   - Issue appears to be in Apple's infrastructure or macOS parsing
   - May require Apple Developer Support intervention

## Comparison with Other Platforms

### Windows
- Not tested (Windows uses different code signing infrastructure)
- No notarization equivalent on Windows

### macOS (Intel vs ARM)
- Issue affects both architectures
- Both arm64 and x64 DMGs exhibit same behavior

### Previous Versions
- CST Reader 4.x (WinForms) did not have this issue
- CST Reader 5.0.0-beta.1 had different issues (CEF packaging)
- This is a new issue specific to beta.2 packaging

## Next Steps

### Option 1: Release with Workaround Documentation
- Document Gatekeeper bypass in release notes
- Provide clear instructions for users
- Note this as a known limitation
- ❌ **Not professional for public release**

### Option 2: Contact Apple Developer Support
- Submit ticket with full diagnostic information
- Provide sample DMG and notarization logs
- Request investigation into ticket format
- ⏱️ **May take days/weeks**

### Option 3: Test on Older macOS
- Try notarizing on macOS 14 (Sonoma)
- Try stapling on macOS 14
- See if ticket format is compatible
- 🔍 **Worth attempting**

### Option 4: Alternative Distribution
- Distribute unsigned app with instructions
- Use different packaging tool (hdiutil vs create-dmg)
- Submit app bundle directly instead of DMG
- 🤔 **May not solve underlying issue**

## Related Issues

- **AVALONIA_HIGH_CPU.md**: macOS performance issue (separate from notarization)
- **CEF_HELPER_PACKAGING.md**: CEF subprocess packaging (resolved)
- **CODE_SIGNING.md**: Code signing implementation (working correctly)

## References

### Apple Documentation
- [Notarizing macOS Software](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
- [Resolving Common Notarization Issues](https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution/resolving_common_notarization_issues)

### Known Stapler Error 65 Causes
- Signing after notarization (not our case)
- Hash mismatch between signed binary and ticket (not our case)
- Certificate trust settings (verified correct)
- Network issues preventing ticket download (not our case)

### System Commands Used
```bash
# Submit for notarization
xcrun notarytool submit DMG --apple-id --team-id --password --wait

# Attempt stapling
xcrun stapler staple DMG

# Verify signatures
codesign -dvvv DMG
spctl --assess --verbose DMG

# Check system logs
log show --predicate 'process == "syspolicyd"' --last 5m

# Check quarantine
xattr -l DMG
```

## Resolution: KESTREL-SPECIFIC ISSUE

**Date:** October 12, 2025

After extensive testing on multiple machines, the notarization ticket parsing issue is **isolated to Kestrel only**.

### Test Results

**Machines Tested:**
1. **Caracara** (Sequoia 15.6.1, Intel x64): ✅ **WORKS** - DMG validates and installs successfully
2. **Egret** (M4 Mac Mini, Apple Silicon): ✅ **WORKS** - DMG validates and installs successfully
3. **Kestrel** (Sequoia 15.6.1, Apple Silicon M2): ❌ **FAILS** - Unable to parse ticket

### Root Cause

Kestrel has a machine-specific corruption or misconfiguration in its notarization ticket parsing system (syspolicyd). The issue is NOT:
- ❌ Architecture-related (Intel vs Apple Silicon both work)
- ❌ macOS version-related (same Sequoia 15.6.1 works on Caracara)
- ❌ Ticket format issue (tickets work on 2 out of 3 machines)
- ❌ App signing issue (app properly signed and notarized)

The issue IS:
- ✅ Kestrel-specific system corruption affecting syspolicyd's ticket parser

### Actions Taken on Kestrel (All Failed)
- Cleared Gatekeeper cache (`sudo rm -rf /var/db/SystemPolicy*`)
- Restarted syspolicyd (`sudo killall -9 syspolicyd`)
- Removed Apple Root CA certificate from login keychain
- Multiple rebuilds and re-notarizations

### Release Decision

**Beta 2 release can proceed.** The stapled DMGs work correctly on multiple test machines and will work for end users. The issue is isolated to Kestrel's development environment and does not affect distribution.

**Build/Release Strategy:**
- Use Caracara for building, notarizing, and stapling DMGs
- Upload stapled DMGs from Caracara to GitHub releases
- DMGs will install successfully for end users

## Status: RESOLVED - Release Unblocked

Beta 2 release is **unblocked**. The notarization and stapling process works correctly; Kestrel has a local system issue that does not affect production releases.

**Severity:** Low - Development environment only
**Impact:** Kestrel machine only (not users)
**Workaround Available:** Yes (use Caracara or Egret for validation)
**Fix Available:** Kestrel system reinstall or repair (not required for release)
