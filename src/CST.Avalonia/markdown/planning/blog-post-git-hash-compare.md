# Avoiding Unnecessary Downloads: Using Git Blob SHA Comparison to Sync Files Efficiently

*I'm currently developing CST Reader, a cross-platform successor to CST4 (a Buddhist text reader), initially targeting Mac. While working with Claude to implement an efficient file synchronization system, we co-developed this technique for avoiding unnecessary downloads from GitHub repositories. The solution turned out to be quite elegant, so I asked Claude to write this blog post to share the approach with the broader developer community.*

When building applications that sync files from GitHub repositories, you often face a common challenge: how do you know which files have changed without downloading everything? GitHub's API rate limits make it expensive to check each file individually, and downloading entire repositories wastes bandwidth on unchanged files.

Recently, while working on an update system for a Buddhist text reader application that syncs 217 XML files (~220MB) from a GitHub repository, I discovered an elegant solution using Git blob SHA comparison. The key insight? **More than half of our files hadn't been updated in 18 years** - downloading them repeatedly was pure waste.

## The Problem

Our application needed to:
- Sync 217 specific files from a large GitHub repository
- Work within GitHub's 60 requests/hour rate limit (unauthenticated)
- Avoid downloading unchanged files
- Handle cases where users have existing files but no tracking data

The naive approach of checking each file individually would consume 217+ API calls - nearly 4 hours worth of rate limit. Downloading everything blindly wastes bandwidth on files that never change.

## The Solution: Git Blob SHA Hash-Compare

GitHub's Tree API returns Git blob SHAs for every file in a repository in a single call. These SHAs are calculated using Git's standard format:

```
SHA1("blob " + filesize + "\0" + content)
```

By calculating the same SHA locally and comparing it to the remote SHA, we can determine exactly which files need updating - all with minimal API usage.

## Implementation

Here's the core hash calculation function in C#:

```csharp
private static string CalculateGitBlobSha(string filePath)
{
    var content = File.ReadAllBytes(filePath);
    var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
    var combined = new byte[header.Length + content.Length];
    
    Array.Copy(header, 0, combined, 0, header.Length);
    Array.Copy(content, 0, combined, header.Length, content.Length);
    
    using var sha1 = SHA1.Create();
    var hash = sha1.ComputeHash(combined);
    return Convert.ToHexString(hash).ToLowerInvariant();
}
```

The complete workflow uses a "hybrid approach":

1. **Get repository tree** (1 API call): `GET /repos/owner/repo/git/trees/SHA?recursive=1`
2. **Compare local vs remote SHAs**: Hash existing files and compare to tree results
3. **Download only changed files**: Via `raw.githubusercontent.com` (no API limits)

## Real-World Performance

Here's a test with two files from our Buddhist text collection:

| File | Age | Local SHA | Remote SHA | Match? | Action |
|------|-----|-----------|------------|---------|---------|
| `s0101m.mul.xml` | Updated 9 months ago | `35d1bda7...` | `a69020ff...` | ❌ | Download |
| `s0101t.tik.xml` | **Unchanged 18 years** | `e127759...` | `e127759...` | ✅ | Skip |

**Performance benefits:**
- **SHA calculation**: ~200-400ms for 220MB of files
- **API usage**: Only 2 calls total (vs 217+ for individual checks)
- **Bandwidth savings**: Skip downloading 100MB+ of unchanged files
- **Rate limit friendly**: Can check hourly using only 48 API calls/day

## The Smart Logic

When no tracking data exists (first run or missing metadata), the system:

1. **Hash-compares existing files** against remote SHAs
2. **Downloads only files that differ** or are missing
3. **Rebuilds tracking metadata** with current SHAs for future speed

```csharp
// Smart comparison logic
if (fileDatesData?.Files != null && fileDatesData.Files.TryGetValue(book.FileName, out var localFile))
{
    // We have tracking data - use stored SHA
    needsUpdate = localFile.CommitHash != repoFile.Sha;
}
else
{
    // No tracking data - hash-compare existing file
    var localPath = Path.Combine(xmlDir, book.FileName);
    if (File.Exists(localPath))
    {
        var localSha = CalculateGitBlobSha(localPath);
        needsUpdate = localSha != repoFile.Sha;
    }
    else
    {
        // File doesn't exist - needs download
        needsUpdate = true;
    }
}
```

## Why This Works So Well

This technique is particularly effective because:

**Git's content-addressable storage**: SHA represents exact file content, making comparison bulletproof
**GitHub's Tree API efficiency**: Get all file SHAs in one call instead of hundreds
**Long-tail content distribution**: Many files in established repositories rarely change
**No authentication required**: Works for any public repository

## Alternative Approaches Considered

- **LibGit2Sharp sparse-checkout**: Failed - downloads entire 4.89GB repository anyway
- **Git archive download**: Downloads everything, then extract specific files
- **Individual file API checks**: Hits rate limits quickly (217+ calls)
- **Last-modified timestamps**: Unreliable, doesn't detect content changes

## Applications Beyond File Syncing

This technique could be valuable for:
- **Static site generators** syncing from content repositories
- **Package managers** checking for updated dependencies
- **Backup systems** implementing incremental sync
- **CI/CD pipelines** optimizing artifact downloads
- **Mobile apps** updating content bundles efficiently

## Code Repository

The complete implementation is part of an open-source Buddhist text reader application. The hybrid approach combines GitHub's Tree API with direct HTTPS downloads to achieve both efficiency and rate-limit compliance.

---

*Have you encountered similar challenges with repository synchronization? I'd love to hear about other approaches to this problem in the comments below.*

## Technical Notes

- **Hash function**: Standard SHA1 (Git's choice, not a security consideration here)
- **Performance**: SHA calculation is I/O bound, very fast on modern systems
- **Compatibility**: Works with any Git hosting service that exposes tree APIs
- **Memory usage**: Files processed individually, minimal memory footprint
- **Error handling**: Falls back to downloading on hash calculation errors

*Tags: Git, GitHub API, File Synchronization, Performance Optimization, SHA Hashing, API Rate Limits*