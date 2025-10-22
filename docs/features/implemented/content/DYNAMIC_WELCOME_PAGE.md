# Dynamic Welcome Page Content Strategy

**Created**: September 2025
**Status**: Planning
**Target Release**: 5.0.0-beta.2

## Overview

The Welcome page should serve both as a static reference for the current version and as a dynamic communication channel for updates, announcements, and upgrade prompts. This document outlines the strategy for implementing a hybrid approach that works offline while providing fresh content when online.

## Requirements

1. **Must work offline** - Core functionality should not depend on network connectivity
2. **Version-aware** - Should detect when user is running outdated version
3. **Non-intrusive** - Upgrade prompts should be helpful, not annoying
4. **Maintainable** - Updates should be easy to publish without new app releases
5. **Cacheable** - Recent updates should be available offline for reasonable time

## Proposed Solution: Dual-Mode Content

### Static Content (Embedded with App)
Ships with application, always available:
- Quick Start Guide
- How to report issues
- About the Chaṭṭha Saṅgāyana texts
- VRI acknowledgement
- Basic feedback channels

### Dynamic Content (Fetched from GitHub)
Updated independently of app releases:
- Version announcements
- Critical bug warnings
- Community news
- Upgrade prompts
- Updated download links

## Implementation Architecture

### 1. Content Structure

#### Base HTML (embedded)
- `Resources/welcome-base.html` - Core content that rarely changes
- Ships with application
- Contains placeholders for dynamic content injection

#### Updates JSON (hosted)
```json
{
  "schemaVersion": 1,
  "lastUpdated": "2025-09-20T12:00:00Z",
  "currentVersion": {
    "stable": "4.5.0",
    "beta": "5.0.0-beta.2"
  },
  "messages": {
    "5.0.0-beta.1": {
      "type": "info",
      "title": "Thank you for testing!",
      "content": "You're running the current beta. Please report any issues."
    },
    "5.0.0-beta.2": {
      "type": "upgrade",
      "title": "New Beta Available",
      "content": "Version 5.0.0-beta.2 fixes the keychain prompt issue.",
      "downloadUrl": "https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.2"
    },
    "default": {
      "type": "warning",
      "title": "Outdated Version",
      "content": "A newer version is available with bug fixes and improvements."
    }
  },
  "announcements": [
    {
      "id": "2025-09-keychain-fix",
      "date": "2025-09-20",
      "title": "macOS Keychain Issue Resolved",
      "content": "The keychain prompt on macOS has been fixed in beta.2",
      "showUntil": "2025-10-20",
      "targetVersions": ["5.0.0-beta.1"]
    }
  ],
  "criticalNotices": [
    {
      "id": "2025-09-data-corruption",
      "severity": "critical",
      "affectedVersions": ["5.0.0-alpha.*"],
      "message": "Critical: Please upgrade immediately to avoid data corruption"
    }
  ]
}
```

### 2. GitHub Hosting Strategy

#### Option A: GitHub Raw Content
```
https://raw.githubusercontent.com/fsnow/cst/main/resources/welcome-updates.json
```
- ✅ Simple, no extra setup
- ⚠️ Rate limited (60 requests/hour unauthenticated)
- ⚠️ May be blocked by corporate firewalls

#### Option B: GitHub Pages (Recommended)
```
https://fsnow.github.io/cst/welcome-updates.json
```
- ✅ CDN cached, fast globally
- ✅ No rate limits
- ✅ Better firewall compatibility
- ⚠️ Requires gh-pages branch setup

#### Option C: GitHub Releases API
```csharp
// Using existing Octokit dependency
var releases = await github.Repository.Release.GetAll("fsnow", "cst");
var latest = releases[0];
```
- ✅ Structured data with download URLs
- ✅ Can use existing GitHub token if configured
- ⚠️ More complex implementation
- ⚠️ Rate limited without authentication

### 3. Caching Strategy

#### Cache Location
```
Windows: %APPDATA%\CST.Avalonia\cache\welcome-updates.json
macOS: ~/Library/Application Support/CST.Avalonia/cache/welcome-updates.json
Linux: ~/.config/CST.Avalonia/cache/welcome-updates.json
```

#### Cache Policy
- **TTL**: 24 hours for normal updates
- **Force refresh**: On app startup if cache > 7 days old
- **Immediate retry**: On network failure, use cache if < 30 days old
- **Storage format**: JSON with metadata
```json
{
  "fetchedAt": "2025-09-20T12:00:00Z",
  "etag": "W/\"abc123\"",
  "data": { /* updates JSON */ }
}
```

### 4. Version Comparison Logic

```csharp
public class VersionComparer
{
    // Parse: "5.0.0-beta.1" -> Major.Minor.Patch-PreRelease.Build
    public static VersionComparison Compare(string current, string latest)
    {
        var currentVer = ParseVersion(current);
        var latestVer = ParseVersion(latest);

        if (currentVer.Major < latestVer.Major)
            return VersionComparison.MajorOutdated;
        if (currentVer.Minor < latestVer.Minor)
            return VersionComparison.MinorOutdated;
        if (currentVer.Patch < latestVer.Patch)
            return VersionComparison.PatchOutdated;
        if (IsPreRelease(current) && !IsPreRelease(latest))
            return VersionComparison.PreReleaseToStable;

        return VersionComparison.Current;
    }
}
```

### 5. Content Merge Strategy

```csharp
public async Task<string> BuildWelcomeContent()
{
    // 1. Load base HTML
    var baseHtml = await LoadEmbeddedHtml();

    // 2. Try to fetch updates
    var updates = await FetchOrLoadCachedUpdates();

    if (updates == null)
        return baseHtml; // Offline mode

    // 3. Determine version status
    var versionStatus = VersionComparer.Compare(
        AppVersion.Current,
        updates.CurrentVersion.Beta
    );

    // 4. Inject dynamic content
    var doc = new HtmlDocument(baseHtml);

    // Add version banner
    if (versionStatus != VersionComparison.Current)
    {
        doc.InjectBanner(updates.Messages[AppVersion.Current]);
    }

    // Add announcements
    foreach (var announcement in updates.Announcements)
    {
        if (ShouldShowAnnouncement(announcement))
        {
            doc.InjectAnnouncement(announcement);
        }
    }

    // Add critical notices
    foreach (var notice in updates.CriticalNotices)
    {
        if (IsAffectedVersion(notice))
        {
            doc.InjectCriticalNotice(notice);
        }
    }

    return doc.ToString();
}
```

## User Experience

### For Users on Current Version
```html
<div class="version-badge success">
    ✓ You're running the latest beta (5.0.0-beta.2)
</div>
```
- Green badge indicating up-to-date
- Current release notes prominent
- Recent announcements visible

### For Users on Outdated Version
```html
<div class="version-banner info">
    <h3>New Version Available!</h3>
    <p>Version 5.0.0-beta.2 includes bug fixes and new features.</p>
    <a href="..." class="button">Download Update</a>
    <a href="#" class="dismiss">Remind me later</a>
</div>
```
- Non-blocking banner at top
- Clear but not alarming
- Can dismiss for session
- Shows what's new

### For Offline Users
- Full static content displays
- No error messages
- Small indicator: "⚠️ Unable to check for updates (offline)"
- Cache used if available

### For Critical Issues
```html
<div class="alert critical">
    <strong>Critical Security Update</strong>
    <p>Your version has a known security issue. Please update immediately.</p>
    <a href="..." class="button urgent">Download Now</a>
</div>
```
- Cannot be dismissed
- Red/urgent styling
- Top of page placement

## Implementation Phases

### Phase 1: Static Enhancement (Completed)
- ✅ HTML-based Welcome page
- ✅ WebView display
- ✅ Embedded content only
- ✅ Version-specific content

### Phase 2: Basic Dynamic Content (Completed - December 2024)
- ✅ Fetch updates.json from GitHub
- ✅ Simple version comparison
- ✅ Basic upgrade prompt
- ✅ 24-hour cache

**What was implemented:**
- `Services/WelcomeUpdateService.cs` - Fetches and caches updates from GitHub with 24-hour TTL
- `Services/VersionComparer.cs` - Semantic version parsing and comparison logic
- `Models/WelcomeUpdates.cs` - Data models for JSON structure
- `ViewModels/WelcomeViewModel.cs` - Updated to inject dynamic content into base HTML
- `resources/welcome-updates.json` - Sample update configuration file
- `CST.Avalonia.Tests/Services/VersionComparerTests.cs` - Comprehensive test suite (34 tests)

### Phase 3: Advanced Features
- [ ] Announcement system
- [ ] Critical notices
- [ ] Dismissible banners
- [ ] Analytics (optional)
- [ ] A/B testing messages

### Phase 4: Simple Main Branch Deployment (Implemented)
- [x] JSON file in repository root
- [x] Service uses raw GitHub URL from main branch
- [x] Simple maintenance workflow
- [ ] Consider CDN/caching solution if rate limits become an issue

**Current Implementation:**
- `welcome-updates.json` lives in the repository root (main branch)
- URL: `https://raw.githubusercontent.com/fsnow/cst/main/welcome-updates.json`
- Simple to maintain: edit the file directly in the main branch
- No additional setup required

**Maintenance:**
1. Edit `welcome-updates.json` in the repository root
2. Commit and push to main branch
3. Changes are immediately available to all users

**Note on Rate Limits:**
- GitHub raw content has a 60 requests/hour limit for unauthenticated requests
- The 24-hour cache in the app mitigates this for most users
- If rate limits become an issue, consider:
  - GitHub Pages deployment
  - CDN service (e.g., jsDelivr)
  - Authenticated requests with GitHub token

## Security Considerations

1. **Content Validation**: Verify JSON schema version before parsing
2. **HTML Sanitization**: Sanitize any HTML in announcements
3. **HTTPS Only**: Require HTTPS for all remote fetches
4. **Certificate Pinning**: Consider for high-security deployments
5. **Rate Limiting**: Implement client-side rate limiting
6. **Cache Tampering**: Validate cache integrity with checksums

## Maintenance Workflow

### To Add an Announcement
1. Edit `welcome-updates.json` in repository
2. Add announcement with target versions and expiry
3. Commit to main branch (or gh-pages)
4. Users see within 24 hours (cache TTL)

### To Announce New Version
1. Update `currentVersion` in JSON
2. Add version-specific message
3. Include download URL
4. Set message priority (info/warning/critical)

### To Issue Critical Notice
1. Add to `criticalNotices` array
2. Specify affected versions (glob patterns supported)
3. Cannot be dismissed by users
4. Consider also email notification for critical issues

## Success Metrics

1. **Adoption Rate**: % of users on latest version
2. **Update Latency**: Time from release to user upgrade
3. **Offline Resilience**: % of successful page loads offline
4. **Message Effectiveness**: Click-through on upgrade prompts
5. **Performance**: Page load time with/without updates

## Alternative Approaches Considered

### RSS/Atom Feed
- ❌ More complex to parse
- ❌ Not JSON-native
- ✅ Standard format

### WebSocket/Real-time
- ❌ Overkill for this use case
- ❌ Requires persistent connection
- ❌ Complex infrastructure

### In-App Update System
- ❌ Platform-specific complications
- ❌ App store policy issues
- ✅ Seamless experience

## Decision

Proceed with **Phase 2** implementation using **GitHub Raw Content** initially, with plan to migrate to **GitHub Pages** in Phase 4 once proven. This provides quick iteration while building toward the optimal solution.

## Next Steps

1. Implement `WelcomeUpdateService` class
2. Add version comparison logic
3. Create GitHub-hosted JSON file
4. Add caching layer
5. Update WelcomeViewModel to use service
6. Test offline scenarios
7. Add monitoring/analytics

## References

- [Semantic Versioning](https://semver.org/)
- [GitHub Pages Documentation](https://pages.github.com/)
- [Octokit.NET Documentation](https://octokitnet.readthedocs.io/)