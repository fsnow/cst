using System;
using System.IO;
using System.Linq;

namespace CST.Avalonia.Services;

/// <summary>
/// Locates a bundled resource directory (<c>xsl</c>, <c>dictionaries</c>) that ships with the app and is
/// seeded into the per-user data directory on first run.
///
/// The same folder lives in three different places depending on how the app was started, so both callers
/// need the identical precedence - and, more importantly, the identical handling of build-output depth.
/// </summary>
public static class BundledResourceLocator
{
    /// <summary>
    /// How far up the ancestor chain to look for the source-tree copy. bin/&lt;Config&gt;/&lt;tfm&gt;/&lt;rid&gt;
    /// is four levels below the project directory; the extra headroom absorbs any future nesting.
    /// </summary>
    private const int MaxSourceTreeDepth = 6;

    /// <summary>
    /// Resolves <paramref name="resourceDirName"/>, or null when none of the known locations has it.
    /// </summary>
    public static string? Resolve(string resourceDirName)
    {
        var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        // Development: the copy in the project directory. Walk up the ancestors rather than hopping a
        // fixed number of levels - the output depth is not constant. A RID-less build lands in
        // bin/<Config>/<tfm>, but setting a RuntimeIdentifier adds a segment (bin/<Config>/<tfm>/<rid>),
        // which is the default on ARM64 Windows (see src/CST.Avalonia/Directory.Build.props). A hardcoded
        // hop count silently resolved to the wrong directory there, and the app fell back to an empty
        // user directory - books rendered as "XSL file not found". (#28)
        var ancestor = Directory.GetParent(asmDir);
        for (var i = 0; i < MaxSourceTreeDepth && ancestor != null; i++, ancestor = ancestor.Parent)
        {
            if (FindDirectory(ancestor.FullName, resourceDirName) is { } sourceTree)
                return sourceTree;
        }

        // Packaged beside the executable (Windows/Linux self-contained publish): <app>/<name>. (#403)
        if (FindDirectory(asmDir, resourceDirName) is { } beside)
            return beside;

        // Packaged .app: Contents/MacOS/ -> ../Resources/<name>
        if (FindDirectory(Path.Combine(asmDir, ".."), Path.Combine("Resources", resourceDirName)) is { } bundle)
            return bundle;

        return null;
    }

    /// <summary>
    /// Locates a child directory, falling back to a case-insensitive match.
    ///
    /// The casing genuinely differs by location and no single spelling is correct: the source tree has
    /// <c>Xsl</c>, while both packaging scripts stage it as <c>xsl</c> (macOS
    /// <c>Contents/Resources/xsl</c>, Windows <c>&lt;app&gt;/xsl</c>) - they only manage to read the
    /// source directory at all because macOS and Windows are case-insensitive. An exact match would
    /// therefore fail somewhere on Linux, or on a case-sensitive APFS volume. (#28)
    /// </summary>
    internal static string? FindDirectory(string parent, string name)
    {
        var exact = Path.Combine(parent, name);
        if (Directory.Exists(exact))
            return exact;

        // Walk the name segment by segment so an intermediate component ("Resources") can differ in case
        // too, not just the leaf.
        var current = parent;
        foreach (var segment in name.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            string? match;
            try
            {
                match = Directory.EnumerateDirectories(current)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), segment, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                // Unreadable or missing ancestor - treat as "not here" and let the caller try the next location.
                return null;
            }

            if (match == null)
                return null;

            current = match;
        }

        return current;
    }
}
