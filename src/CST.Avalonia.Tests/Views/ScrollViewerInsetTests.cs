using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CST.Avalonia.Tests.Views;

/// <summary>
/// A <c>ScrollViewer</c> must not carry its own <c>Padding</c> — <b>while we are on Avalonia 11.3.x or 12.0</b>. (#937)
///
/// <para><b>The mechanism.</b> <c>ScrollContentPresenter</c> applies <c>Padding</c> in Arrange but not in
/// Measure: the child is measured against the full constraint and then arranged into less, and
/// <c>ComputeExtent</c> reports the arranged child, so the padding is <i>subtracted</i> from the scrollable
/// extent. A <c>Margin</c> on the content is added to it instead. Measured, 300px of content in a 150px
/// viewer:</para>
///
/// <code>
/// Padding="20,18" on the viewer     extent 264  max offset 114  last item ends 54px BELOW the viewport
/// Margin="20,18"  on the content    extent 336  max offset 186  last item fully visible
/// </code>
///
/// <para>So the scrollbar reaches its end while the tail is still underneath, and the symptom is a last row
/// that looks clipped by whatever sits below the viewer. In the About box that was the final credit under
/// the actions bar. <c>AiAssistantPanel.axaml</c> met the same root from the other direction: there nothing
/// scrolled horizontally at all (that viewer disables it), but a wrapped line kept the width it was
/// measured at and ran under the overlay scrollbar.</para>
///
/// <para><b>This rule has an expiry date.</b> It is Avalonia's defect, not <c>ScrollViewer</c>'s nature —
/// <see href="https://github.com/AvaloniaUI/Avalonia/issues/17158">AvaloniaUI/Avalonia#17158</see>, fixed by
/// PR #21872 and shipping in <b>12.1</b>, which moves the padding inside the scrolling area to match WinUI.
/// 11.3.6 and 12.0 both still have it. <b>When #49 lands the Avalonia 12 upgrade, revisit this test</b>:
/// past 12.1 it forbids a construct that works.</para>
///
/// <para><b>Known limits of the scan.</b> It reads the <c>Padding</c> attribute on <c>ScrollViewer</c>
/// elements only, so a <c>Style</c>/<c>ControlTheme</c> setter, a <c>&lt;ScrollViewer.Padding&gt;</c>
/// property element, or code-behind would slip past — the first two are checked for as well, the third
/// cannot be. And it would wrongly block a legitimate case: a <c>ScrollViewer</c> driving a logically
/// scrolling child (a re-templated <c>ListBox</c>) takes the plain <c>ContentPresenter</c> path, where
/// Padding behaves correctly. Neither exists in this app today; if one appears, exempt it here by name
/// rather than deleting the rule.</para>
/// </summary>
public class ScrollViewerInsetTests
{
    [Fact]
    public void No_scroll_viewer_sets_its_own_padding()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "CST.Avalonia"), "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var doc = XDocument.Load(file);

            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "ScrollViewer"))
            {
                // The attribute form, and the property-element form that an attribute scan would miss.
                var padding = (string?)e.Attribute("Padding")
                              ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "ScrollViewer.Padding")?.Value;
                if (padding is not null)
                    offenders.Add($"{Path.GetFileName(file)} — Padding=\"{padding.Trim()}\"");
            }

            // And a Style or ControlTheme that sets it on every ScrollViewer at once, which no element
            // in the markup would show.
            foreach (var style in doc.Descendants()
                         .Where(e => e.Name.LocalName is "Style" or "ControlTheme"))
            {
                var target = (string?)style.Attribute("Selector") ?? (string?)style.Attribute("TargetType");
                if (target is null || !target.Contains("ScrollViewer", StringComparison.Ordinal)) continue;

                foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
                {
                    if ((string?)setter.Attribute("Property") != "Padding") continue;
                    offenders.Add($"{Path.GetFileName(file)} — a setter puts Padding on \"{target}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "On Avalonia 11.3.x and 12.0 a ScrollViewer's Padding shortens its scrollable extent, so the " +
            "end of the content cannot be scrolled into view (AvaloniaUI/Avalonia#17158, fixed in 12.1). " +
            "Put the inset on the scrolled content as a Margin instead:\n  " +
            string.Join("\n  ", offenders));
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
