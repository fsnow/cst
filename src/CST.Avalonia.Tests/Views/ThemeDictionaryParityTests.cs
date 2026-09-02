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
/// <para>A DynamicResource that resolves to nothing does not throw, log, or fail a build - and what it does
/// instead depends on where it is written. <b>Inside a DataTemplate</b> it binds at
/// <c>BindingPriority.Template</c>, where the miss yields the property's DEFAULT: an unset
/// <c>Foreground</c> is then <b>black</b>. <b>Outside</b> one it publishes <c>UnsetValue</c> at
/// <c>LocalValue</c>, and the property falls through to INHERITED - which usually looks right.</para>
///
/// <para>That split is why #955 read as one panel misbehaving. The Assistant's turn template went black on
/// a dark ground while visually identical text a few lines above it, outside any template, inherited white
/// and looked fine. On a light ground neither is noticeable, because black is what secondary text nearly
/// looks like anyway - so the phantom keys went unremarked for a year.</para>
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
    /// <para>The <c>SystemAccentColor*</c> keys are NOT platform-supplied, though an earlier version of this
    /// comment said so. <c>FluentTheme</c> answers them itself through its <c>SystemAccentColors</c>
    /// resource provider, falling back to a hard-coded <c>#0078D7</c> when there is no
    /// <c>IPlatformSettings</c> - so they resolve in a headless process too. Defining them here would
    /// shadow the user's real accent colour.</para>
    ///
    /// <para><c>ContentControlThemeFontFamily</c> is Fluent's own, from <c>Accents/BaseResources.xaml</c>.
    /// It is listed because a probe that bound a <c>FontFamily</c> to an <c>IBrush</c> property once
    /// reported it missing, and a working attribute was removed on the strength of that.</para>
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

        "ContentControlThemeFontFamily",
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

    /// <summary>
    /// A key repeated inside ONE dictionary is a startup crash, and comparing the two key sets does not
    /// catch it: duplicate the same <c>x:Key</c> in both and the sets still match. The build stays clean
    /// too - the XAML compiler emits the populate method without complaint, and
    /// <c>ArgumentException: An item with the same key has already been added</c> is thrown when
    /// <c>App.Initialize</c> runs it, which no test in this project reaches.
    /// </summary>
    [Fact]
    public void Neither_theme_dictionary_defines_a_key_twice()
    {
        var (light, dark) = ThemeDictionaries();

        foreach (var (variant, keys) in new[] { ("Light", light), ("Dark", dark) })
        {
            var repeated = keys.GroupBy(k => k, StringComparer.Ordinal)
                               .Where(g => g.Count() > 1)
                               .Select(g => $"{g.Key} ×{g.Count()}")
                               .ToList();

            Assert.True(repeated.Count == 0,
                $"The {variant} dictionary defines a key more than once, which throws inside " +
                $"App.Initialize before any window opens:\n  " + string.Join("\n  ", repeated));
        }
    }

    /// <summary>
    /// Markup and code both, because they fail identically and only one of them is obvious. A key renamed
    /// everywhere except <c>ProviderLogoConverter</c>'s <c>TryGetResource</c> call would leave that lookup
    /// returning false forever, and its fallback is good enough that nobody would notice - which is exactly
    /// how the original defect hid for a year.
    /// </summary>
    [Fact]
    public void Every_resource_key_the_app_names_is_defined_somewhere()
    {
        var (light, _) = ThemeDictionaries();
        var known = new HashSet<string>(light, StringComparer.Ordinal);
        known.UnionWith(ProvidedByALoadedTheme);
        known.UnionWith(OurNonThemedResources);

        var unresolved = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        void Collect(string file, string pattern)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), pattern))
            {
                var key = m.Groups[1].Value;
                if (known.Contains(key)) continue;
                if (!unresolved.TryGetValue(key, out var files)) unresolved[key] = files = new List<string>();
                var name = Path.GetFileName(file);
                if (!files.Contains(name)) files.Add(name);
            }
        }

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "CST.Avalonia"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            if (file.EndsWith(".axaml", StringComparison.Ordinal))
                Collect(file, @"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\s*\}");
            else if (file.EndsWith(".cs", StringComparison.Ordinal))
                Collect(file, @"(?:TryGetResource|TryFindResource|FindResource|GetResourceObservable)\(\s*""([A-Za-z0-9_]+)""");
        }

        Assert.True(unresolved.Count == 0,
            "These resource keys are defined by no loaded theme and by no dictionary of ours, so they " +
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
