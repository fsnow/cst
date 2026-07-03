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

## Update: October 12, 2025 - Issue Persists on Kestrel After Tahoe Upgrade

**Date:** October 12, 2025

After upgrading Kestrel to macOS Tahoe 26.0.1, the ticket parsing issue **still occurs**.

### Current Test Results

**Machines Tested:**
1. **Caracara** (Sequoia 15.6.1, Intel x64, with dev tools):
   - ✅ Can build, sign, notarize, and **staple** DMGs successfully
   - ✅ Can validate and run its own stapled DMGs after download

2. **Egret** (Tahoe 26.0.1, M4 Apple Silicon, **no dev tools**):
   - ✅ Can **validate** and run Caracara-built stapled DMGs
   - ⚠️ Cannot staple (no dev tools installed to test)

3. **Kestrel** (Tahoe 26.0.1, M4 Apple Silicon, **with dev tools**):
   - ❌ Cannot **staple** - Error 65: "Could not validate ticket"
   - ❌ Cannot **validate** Caracara-built stapled DMGs - "Unable to parse ticket"

### Current System Logs from Kestrel (Tahoe 26.0.1)

```
2025-10-12 15:13:31.316 E  syspolicyd[654:2ebcb] [com.apple.syspolicy:default] Unable to parse ticket.
2025-10-12 15:13:31.374 E  syspolicyd[654:2ebcb] [com.apple.syspolicy:default] Unable to parse ticket.
2025-10-12 15:14:19.616 E  syspolicyd[654:2e97f] [com.apple.syspolicy:default] Unable to parse ticket.
2025-10-12 15:14:19.621 E  syspolicyd[654:2e97f] [com.apple.syspolicy:default] error registering ticket: -1
```

**The same "Unable to parse ticket" error persists even after upgrading to Tahoe 26.0.1.**

### Analysis

The issue is **NOT resolved** by upgrading to Tahoe. Two possible explanations remain:

**Hypothesis 1: Apple Silicon + Tahoe + Dev Tools Issue**
- Dev tools (Xcode Command Line Tools) may interfere with ticket validation on Apple Silicon
- Egret (without dev tools) can validate, but Kestrel (with dev tools) cannot
- Would affect any Apple Silicon Mac with CLT installed running Tahoe

**Hypothesis 2: Kestrel-Specific Corruption**
- Kestrel may have corrupted system state from Sequoia 15.6.1 that persists after Tahoe upgrade
- System caches, Gatekeeper database, or syspolicyd state may need manual cleanup
- Other Apple Silicon + Tahoe + Dev Tools machines might work fine

### Actions Taken on Kestrel (All Failed)
- Upgraded from Sequoia 15.6.1 to Tahoe 26.0.1
- Cleared Gatekeeper cache (`sudo rm -rf /var/db/SystemPolicy*`)
- Restarted syspolicyd (`sudo killall -9 syspolicyd`)
- Removed Apple Root CA certificate from login keychain
- Reinstalled Command Line Tools (16.4.0 → 26.0.0 → 26.0.0.0.1.1757719676)
- Multiple fresh builds and re-notarizations

**None of these actions resolved the ticket parsing error.**

### What We Don't Know Yet

- Whether other Apple Silicon Macs with dev tools can staple successfully
- Whether installing dev tools on Egret would break validation there too
- Whether real-world beta testers can install the Caracara-built DMGs
- Whether this affects only stapling or also end-user validation

### Release Strategy

**Beta 2/Beta 3 releases can proceed** using Caracara for all packaging operations.

**Build/Release Strategy:**
- Use **Caracara (Intel + Sequoia)** for building, notarizing, and stapling all release DMGs
- Caracara-built DMGs validate successfully on:
  - Intel Macs (verified on Caracara itself)
  - Apple Silicon Macs without dev tools (verified on Egret)
- Unknown: Whether they validate on Apple Silicon Macs with dev tools (awaiting beta tester feedback)

**Kestrel Development:**
- Kestrel remains unable to staple or validate DMGs
- This is a development environment limitation, not a release blocker
- For testing on Kestrel, use locally-built apps without quarantine attribute

## Status: UNRESOLVED - But Release Not Blocked

The ticket parsing issue on Kestrel remains **unresolved** even after upgrading to Tahoe 26.0.1. However, this does not block releases since:

1. **Caracara can build and staple successfully** - Release builds work
2. **Egret can validate the stapled DMGs** - End users should be able to install
3. **The issue appears isolated to Kestrel** - Likely machine-specific or dev-tools-related

**Severity:** Medium - Affects development workflow on Kestrel
**Impact:** Cannot use Kestrel for release builds or validation testing
**Workaround:** Use Caracara for all release packaging and stapling
**Root Cause:** Unknown - Either Kestrel-specific corruption or Apple Silicon + Tahoe + Dev Tools incompatibility

## Note: June 21, 2026 - Revisit / debug on Kestrel (TODO)

Flagging this to revisit and debug on Kestrel. Releases are still cut on Caracara in the meantime (Beta 4 is being built/notarized there now).

**Kestrel environment confirmed intact today** (so this is not a creds/cert regression — the failure is still the ticket parsing/staple step):
- macOS **26.5.1** (build 25F80), Apple Silicon — i.e. a later Tahoe point release than the 26.0.1 in the Oct 2025 entry above
- Developer ID signing cert valid: `Developer ID Application: Frank Snow (69M77LM9K3)` (1 valid identity)
- `APPLE_ID` and `APPLE_APP_PASSWORD` env vars set; `APPLE_TEAM_ID` = 69M77LM9K3
- `xcrun notarytool` 1.1.2 (41); Xcode CLT / Xcode at `/Applications/Xcode.app/Contents/Developer`

**When debugging, capture (didn't this time — was deferred):**
- The live failure symptom on a fresh DMG: `xcrun stapler staple -v dist/CST-Reader-arm64.dmg` and whether it's still Error 65 "Could not validate ticket"
- syspolicyd logs during a validate attempt: `log show --predicate 'process == "syspolicyd"' --last 5m` (compare to the "Unable to parse ticket" signature above)
- `xcrun notarytool history` / `notarytool log <submission-id>` to confirm auth + a clean Accepted ticket
- Whether the later Tahoe point release (26.5.1) changes the outcome vs. 26.0.1
- Open thread from Oct 2025: is this Apple-Silicon-+-Tahoe-+-dev-tools, or Kestrel-specific corruption? (Egret without dev tools validated fine.)

## June 21, 2026 - Deep debug session: root cause localized (still unfixed)

Spent a full session debugging on Kestrel (macOS 26.5.1, Apple Silicon) against the Beta 4 arm64 DMG downloaded from GitHub (quarantined). **Outcome: root cause localized to corrupted data-volume security state; not fixable by any targeted reset we could apply. Releases unaffected — Caracara builds + Egret validates; Beta 4 shipped fine.**

### Conclusively established
- **It is NOT a build problem.** The Beta 4 DMG is correctly notarized AND stapled — `stapler validate` "worked!" and `spctl` "Notarized Developer ID / accepted" on **Caracara** (Intel/Sequoia) and it opens clean on **Egret** (Apple Silicon, no dev tools, downloaded from GitHub → only the benign "downloaded from the internet" prompt). Apple `notarytool history` shows both DMGs **Accepted**.
- **It is SYSTEMIC on Kestrel, not CST-specific.** `stapler validate` exits 65 for **every vendor's** stapled ticket too — Brave, Chrome, VS Code, Slack all fail identically (Zoom is merely "not stapled", a different case). And **beta.3** — which previously validated on Kestrel — now fails identically. So Kestrel cannot validate *any* notarization ticket; it regressed at some point (an OS update, between when beta.3 worked and now).
- **Exact failure path** (from live `log stream` of syspolicyd/securityd/trustd):
  ```
  stapler → SecAssessmentTicketRegisterXPC
          → Security::CodeSigning::registerStapledTicketWithSystem
          → DiskImageRep::registerStapledTicket()
  syspolicyd: "Unable to parse ticket."  →  "error registering ticket: -1"
  securityd:  Error registering stapled ticket: NSOSStatusErrorDomain Code=-1
  ```
  The ticket's CMS signature **verifies fine** (`SecKeyVerifySignature` succeeds in trustd) — then syspolicyd fails to **parse/register** the ticket blob with a generic `-1`. (The "-1 / Could not parse the request/response" string is just the catalog of meanings for OSStatus −1, not a literal HTTP error.) Recurring nearby: `securityd[xpc] no query dict ... for system keychain: Code=-50` — possibly noise, possibly relevant.

### Ruled out
- Build / notarization / stapling (good on Caracara + Egret; Apple-side Accepted).
- Network (TLS to Apple connects; and the stapled-parse failure happens *before* any network).
- Disk space (227 GB free; no `SQLITE_FULL`).
- Trust-setting overrides (user domain empty; admin domain has Apple Root CA - G3 + Developer ID CA with 0 settings, which is the **normal default** for Developer ID validation, not an artifact).
- Date/clock (correct), SIP (was enabled; we later disabled it to test), MDM/profiles (none).

### Fixes ATTEMPTED that did NOT work
- **Reset the local notarization Tickets DB.** `/var/db/SystemPolicyConfiguration/Tickets` was wedged (main DB frozen **Dec 2 2025** with a stale **1.8 MB** WAL that never checkpointed across many reboots + the OS upgrade → every registration failing into the WAL). But this is SIP-protected, so:
  - `sudo rm`, `sudo killall syspolicyd`, `sudo launchctl kickstart` all refused while SIP engaged ("Operation not permitted while System Integrity Protection is engaged").
  - **Disabled SIP** (Recovery → `csrutil disable`), then `sudo rm -f Tickets Tickets-shm Tickets-wal` + `sudo launchctl kickstart -k system/com.apple.security.syspolicy`. DB was recreated fresh (4 KB) with a new syspolicyd → **still exit 65 / rejected.** So the wedged DB was a *symptom* (registrations can't commit), not the cause.
- **Restarted syspolicyd + trustd** (`sudo killall trustd syspolicyd`, SIP off) → no change.

### Why it survives everything
An OS upgrade replaces the sealed **system** volume but never the **data** volume. The corrupt state lives on the data volume (in the security subsystem), so upgrades, reboots, and the Tickets-DB reset all leave it intact. The parser itself is sealed OS code (can't be corrupt), so it's failing on some data-volume state it consults during parse.

### Realistic remaining options (not yet tried)
- **Fresh-user-account test** — cheap diagnostic to tell user-level vs machine-wide corruption (if a new user validates → user-level/keychain, fixable without erase; if it still fails → machine-wide).
- **`softwareupdate --background-critical`** / reset trustd's `valid.sqlite3` + Supplementals — force re-download of security config assets (low-to-moderate odds).
- **Erase-install** — the hammer that would actually clear data-volume corruption. An **in-place reinstall would NOT help** (spares the data volume).

### Decision
Recommendation: wind down — keep the **Caracara-build / Egret-verify** workflow (the documented status quo); not worth an erase-install for a dev-box quirk that doesn't block releases.

**ACTION STILL PENDING:** SIP was left **DISABLED** during this session (Recovery → `csrutil disable`). Re-enable it: Recovery → Terminal → `csrutil enable` → reboot. Until then Kestrel is running with SIP off.
