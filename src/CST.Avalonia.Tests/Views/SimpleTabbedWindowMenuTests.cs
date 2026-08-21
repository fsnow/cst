using System;
using System.IO;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.Tests.Views;

/// <summary>
/// The window menu's markup and the code that reaches into it at runtime. (#778)
///
/// <para>Three items are added from C# rather than declared in <c>SimpleTabbedWindow.axaml</c>, because macOS
/// supplies them in the application menu and an item declared in the window's markup would appear on both:
/// Tools &gt; Settings (#28), Help &gt; About (#746), and File &gt; Exit (#778). Each one finds its parent menu
/// by matching a header string against the markup.</para>
///
/// <para>That coupling is invisible when it breaks. Reword a header in the XAML and the lookup finds nothing:
/// the item is simply never added, the menu still opens, everything still looks right, and the only trace is a
/// warning in a log nobody reads. Same class of silent failure that earned
/// <c>App.AboutMenuHeader</c> its test.</para>
/// </summary>
public class SimpleTabbedWindowMenuTests
{
    private static string WindowMarkup()
    {
        var path = Path.Combine(RepoRoot(), "src", "CST.Avalonia", "Views", "SimpleTabbedWindow.axaml");
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_File_menu_header_matches_the_constant_the_code_looks_it_up_by()
    {
        // AddExitMenuItemOffMacOS walks the window's NativeMenu for this exact header. If the markup no longer
        // carries it, Exit stops being added and Windows loses its only in-menu way to quit.
        Assert.Contains(
            $"Header=\"{SimpleTabbedWindow.FileMenuHeader}\"",
            WindowMarkup(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_is_not_declared_in_the_markup()
    {
        // It must be added from code, not XAML. An Exit declared here would also appear on macOS, where
        // quitting belongs in the application menu as Quit - duplicating it and putting it somewhere the
        // platform does not use. This is the assertion that would catch someone "simplifying" the code-side
        // construction back into the markup.
        Assert.DoesNotContain("Header=\"Exit\"", WindowMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_menus_the_code_reaches_into_are_all_present()
    {
        // The other two runtime-added items depend on the same kind of lookup, by "Tools" and "File". Pinning
        // them here means a markup rename fails a test rather than quietly removing a menu item.
        var markup = WindowMarkup();

        Assert.Contains("Header=\"File\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Tools\"", markup, StringComparison.Ordinal);
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
