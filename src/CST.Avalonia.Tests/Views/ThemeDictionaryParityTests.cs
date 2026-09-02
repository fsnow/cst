using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CST.Avalonia.Tests.Views;

/// <summary>
/// Every <c>{DynamicResource}</c> key the app names has to exist somewhere. (#955)
///
/// <para>A DynamicResource that resolves to nothing does not throw, log, or fail a build. It sets the
/// property to nothing, and the control keeps its default: an unset <c>Background</c> is transparent, an
/// unset <c>BorderBrush</c> draws no edge, and an unset <c>Foreground</c> is <b>black</b>. On a light ground
/// black is what secondary text nearly looks like anyway, so eleven WinUI brush names that no loaded theme
/// defines went unnoticed for a year and surfaced only as "the small text is invisible in dark mode".</para>
///
/// <para>Two rules, and the first is the one that made #955 a dark-mode bug rather than an everywhere bug:
/// #102 added six of these keys to the <c>Light</c> dictionary and left <c>Dark</c> empty, so the same
/// markup resolved in one theme and not the other. A key defined for one theme only is always a defect -
/// either it is needed, and the other theme is missing it, or it is not, and it should go.</para>
///
/// <para>Parsed rather than resolved on purpose. Resolving would need a live Avalonia application with the
/// themes loaded, which this project has no support for (#655); parsing catches the same class of mistake
/// with no framework at all, and runs in milliseconds.</para>
/// </summary>
public class ThemeDictionaryParityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Keys a loaded theme really does provide, so we must NOT define them - shadowing a live theme brush
    /// is how you get an app that ignores its own theme.
    ///
    /// <para>The first five were measured resolving in a headless <c>FluentTheme</c> +
    /// <c>DockFluentTheme</c> harness. The four <c>SystemAccentColor*</c> entries were NOT: the platform
    /// supplies those at runtime and a headless process has no platform, so the harness cannot tell
    /// "absent" from "absent here". They are trusted rather than verified, and that is the one hole in
    /// this test.</para>
    /// </summary>
    private static readonly HashSet<string> ProvidedByALoadedTheme = new(StringComparer.Ordinal)
    {
        "DockApplicationAccentBrushHigh",
        "DockApplicationAccentBrushLow",
        "DockApplicationAccentBrushMed",
        "DockApplicationAccentForegroundBrush",
        "SystemControlBackgroundChromeMediumBrush",

        "SystemAccentColor",
        "SystemAccentColorDark1",
        "SystemAccentColorLight2",
        "SystemAccentColorLight3",
    };

    /// <summary>Our own, declared in App.axaml outside the theme dictionaries.</summary>
    private static readonly HashSet<string> OurNonThemedResources = new(StringComparer.Ordinal)
    {
        "ControlRecyclingKey",
    };

    [Fact]
    public void The_light_and_dark_theme_dictionaries_define_the_same_keys()
    {
        var (light, dark) = ThemeDictionaries();

        Assert.NotEmpty(light);
        Assert.Equal(light.OrderBy(k => k, StringComparer.Ordinal),
                     dark.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_dynamic_resource_key_the_markup_names_is_defined_somewhere()
    {
        var (light, _) = ThemeDictionaries();
        var known = new HashSet<string>(light, StringComparer.Ordinal);
        known.UnionWith(ProvidedByALoadedTheme);
        known.UnionWith(OurNonThemedResources);

        var unresolved = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "CST.Avalonia"), "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{DynamicResource\s+([A-Za-z0-9_]+)\s*\}"))
            {
                var key = m.Groups[1].Value;
                if (known.Contains(key)) continue;
                if (!unresolved.TryGetValue(key, out var files)) unresolved[key] = files = new List<string>();
                var name = Path.GetFileName(file);
                if (!files.Contains(name)) files.Add(name);
            }
        }

        Assert.True(unresolved.Count == 0,
            "These DynamicResource keys are defined by no loaded theme and by no dictionary of ours, so they " +
            "silently set nothing:\n  " +
            string.Join("\n  ", unresolved.Select(kv => $"{kv.Key} — {string.Join(", ", kv.Value)}")));
    }

    private static (List<string> Light, List<string> Dark) ThemeDictionaries()
    {
        var path = Path.Combine(RepoRoot(), "src", "CST.Avalonia", "App.axaml");
        Assert.True(File.Exists(path), path);

        var themed = XDocument.Load(path).Descendants()
            .First(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries");

        List<string> KeysOf(string variant) => themed.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == variant)
            .Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();

        return (KeysOf("Light"), KeysOf("Dark"));
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
