using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CST.Avalonia.Tests.Views;

/// <summary>
/// A <c>ScrollViewer</c> must not carry its own <c>Padding</c>. (#937)
///
/// <para><b>Padding on a ScrollViewer shortens the scrollable extent instead of lengthening it.</b> Measured
/// in a headless Avalonia 11.3.6 harness, 300px of content in a 150px-tall viewer:</para>
///
/// <code>
/// Padding="20,18" on the viewer     extent 264  max offset 114  last item ends 54px BELOW the viewport
/// Margin="20,18"  on the content    extent 336  max offset 186  last item fully visible
/// </code>
///
/// <para>So with padding the content is arranged 36px shorter than its own children need, the scrollbar
/// reaches its end while the tail is still underneath, and the symptom is a last row that looks clipped by
/// whatever sits below the viewer. In the About box that was the final credit disappearing under the actions
/// bar; <c>AiAssistantPanel.axaml</c> hit the same trap on the horizontal axis, where the right-hand inset
/// scrolled away instead of reserving width and long lines lost their last characters.</para>
///
/// <para>Two panels, two axes, one cause, and in both cases it presented as a mysterious layout bug rather
/// than as anything pointing at the property responsible. Hence a rule rather than two comments: put the
/// inset on the scrolled content as a <c>Margin</c>, where it is added to the extent.</para>
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

            foreach (var e in XDocument.Load(file).Descendants()
                         .Where(e => e.Name.LocalName == "ScrollViewer"))
            {
                var padding = (string?)e.Attribute("Padding");
                if (padding is not null)
                    offenders.Add($"{Path.GetFileName(file)} — Padding=\"{padding}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "A ScrollViewer's Padding shortens its scrollable extent, so the end of the content cannot be " +
            "scrolled into view. Put the inset on the scrolled content as a Margin instead:\n  " +
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
