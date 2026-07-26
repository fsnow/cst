using System;
using System.IO;

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
            var candidate = Path.Combine(ancestor.FullName, resourceDirName);
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Packaged beside the executable (Windows/Linux self-contained publish): <app>/<name>. (#403)
        var beside = Path.Combine(asmDir, resourceDirName);
        if (Directory.Exists(beside))
            return beside;

        // Packaged .app: Contents/MacOS/ -> ../Resources/<name>
        var bundle = Path.Combine(asmDir, "..", "Resources", resourceDirName);
        if (Directory.Exists(bundle))
            return bundle;

        return null;
    }
}
