using System;
using System.IO;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #28: the bundled resource directory is spelled differently depending on where it came from - the source
// tree has "Xsl", while both packaging scripts stage it as "xsl" (macOS Contents/Resources/xsl, Windows
// <app>/xsl). Windows and default macOS are case-insensitive, so an exact-match lookup appears to work on
// the machines we develop on and would only fail on Linux or a case-sensitive APFS volume - the kind of
// bug that ships. These tests pin the case-insensitive behaviour.
public class BundledResourceLocatorTests : IDisposable
{
    private readonly string _root;

    public BundledResourceLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cst-locator-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void FindDirectory_FindsAnExactMatch()
    {
        Directory.CreateDirectory(Path.Combine(_root, "xsl"));

        var found = BundledResourceLocator.FindDirectory(_root, "xsl");

        Assert.NotNull(found);
        Assert.Equal("xsl", Path.GetFileName(found));
    }

    [Fact]
    public void FindDirectory_FindsTheSourceTreeCapitalXsl_WhenAskedForLowercase()
    {
        // The real source-tree spelling. Searching for "xsl" must still resolve to it.
        var created = Directory.CreateDirectory(Path.Combine(_root, "Xsl")).FullName;

        var found = BundledResourceLocator.FindDirectory(_root, "xsl");

        // Assert on resolution, not on the returned spelling: a case-insensitive filesystem satisfies
        // this via the exact-match branch (returning the requested casing), while a case-sensitive one
        // takes the enumeration fallback and returns the on-disk casing. Both are correct.
        Assert.NotNull(found);
        Assert.True(Directory.Exists(found));
        Assert.Equal(created, Path.GetFullPath(found!), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindDirectory_FindsLowercase_WhenAskedForTheCapitalisedName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "xsl"));

        var found = BundledResourceLocator.FindDirectory(_root, "Xsl");

        Assert.NotNull(found);
    }

    [Fact]
    public void FindDirectory_MatchesEveryPathSegmentCaseInsensitively()
    {
        // The macOS bundle path is Resources/xsl; an intermediate segment can differ in case too,
        // so the walk must not special-case only the leaf.
        var created = Directory.CreateDirectory(Path.Combine(_root, "resources", "XSL")).FullName;

        var found = BundledResourceLocator.FindDirectory(_root, Path.Combine("Resources", "xsl"));

        Assert.NotNull(found);
        Assert.True(Directory.Exists(found));
        Assert.Equal(created, Path.GetFullPath(found!), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindDirectory_ReturnsNullWhenAbsent()
    {
        Assert.Null(BundledResourceLocator.FindDirectory(_root, "dictionaries"));
    }

    [Fact]
    public void FindDirectory_ReturnsNullForAnUnreadableOrMissingParent()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.Null(BundledResourceLocator.FindDirectory(missing, "xsl"));
    }

    [Fact]
    public void FindDirectory_DoesNotMatchAFileOfTheSameName()
    {
        File.WriteAllText(Path.Combine(_root, "xsl"), "not a directory");

        Assert.Null(BundledResourceLocator.FindDirectory(_root, "xsl"));
    }
}
