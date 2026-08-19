using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Keeps the About box's library list honest. (#746)
///
/// <para>#746 left "generated or written" open: reading PackageReference at build time keeps the list true,
/// a written list reads better. This is the third option — written, and checked here against the csproj
/// files that actually ship. A dependency added without a line in <see cref="AboutViewModel.Libraries"/>
/// fails a test rather than quietly going uncredited, and a line naming a package that has since been
/// dropped fails too, so the list cannot accumulate ghosts.</para>
///
/// <para>Asserted against the project files rather than the loaded assemblies: what we owe an
/// acknowledgement for is what is <i>referenced and shipped</i>, and the test host loads a different set
/// (xunit, Moq) while a reference used only at runtime may not be loaded at all.</para>
/// </summary>
public class AboutInventoryTests
{
    /// <summary>
    /// The projects that ship inside the app: CST.Avalonia and everything it references. The tests project
    /// and the command-line tools (CST.CharacterAnalysis, CST.ScriptValidation) are deliberately absent —
    /// nothing they reference reaches a reader.
    /// </summary>
    private static readonly string[] ShippingProjects =
    [
        Path.Combine("CST.Avalonia", "CST.Avalonia.csproj"),
        Path.Combine("CST.Core", "CST.Core.csproj"),
        Path.Combine("CST.Lucene", "CST.Lucene.csproj"),
        Path.Combine("CST.Lemma", "CST.Lemma.csproj"),
        Path.Combine("CST.Lexicon", "CST.Lexicon.csproj"),
    ];

    [Fact]
    public void Every_shipped_package_is_acknowledged()
    {
        var credited = CreditedPackages();
        var uncredited = ReferencedPackages().Where(p => !credited.Contains(p)).ToList();

        Assert.True(uncredited.Count == 0,
            "These packages ship but no line in AboutViewModel.Libraries names them: "
            + string.Join(", ", uncredited)
            + ". Add them to an existing line's package list, or give them a line of their own.");
    }

    [Fact]
    public void No_line_credits_a_package_that_is_gone()
    {
        var referenced = ReferencedPackages();
        var stale = CreditedPackages().Where(p => !referenced.Contains(p)).ToList();

        Assert.True(stale.Count == 0,
            "AboutViewModel.Libraries names packages that are no longer referenced: "
            + string.Join(", ", stale));
    }

    [Fact]
    public void Every_line_names_at_least_one_package()
    {
        // A line with no packages behind it is invisible to both tests above — it would be a name the
        // inventory can neither confirm nor retire.
        var empty = new AboutViewModel().Libraries.Where(l => l.Packages.Count == 0).Select(l => l.Name);

        Assert.Empty(empty);
    }

    private static SortedSet<string> CreditedPackages()
    {
        var credited = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in new AboutViewModel().Libraries)
            foreach (var package in library.Packages)
                credited.Add(package);
        return credited;
    }

    private static SortedSet<string> ReferencedPackages()
    {
        var referenced = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in ShippingProjects)
        {
            var path = Path.Combine(RepoRoot(), "src", project);
            Assert.True(File.Exists(path), $"Shipping project not found: {path}");

            // The same id appears more than once in CST.Avalonia.csproj, under different RuntimeIdentifier
            // conditions (the CEF packages), so this is a set rather than a list.
            foreach (Match m in Regex.Matches(File.ReadAllText(path), "PackageReference\\s+Include=\"([^\"]+)\""))
                referenced.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(referenced);
        return referenced;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
