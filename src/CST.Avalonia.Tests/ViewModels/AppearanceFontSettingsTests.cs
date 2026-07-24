using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

// Unit coverage for the #67 Part 1 font-load-race fix: the pure selection resolver, and the
// ScriptFontSettingViewModel selection/persist behavior that fixes Bug A (stale overwrite) and Bug B
// (ItemsSource-reset null write-back wiping a saved font). The async cancellation/latest-wins glue in
// LoadAvailableFontsForScript composes the BeginLoad/IsCurrentLoad primitives tested here; the live
// ComboBox binding behavior is validated in the Settings dialog.
public class AppearanceFontSettingsTests
{
    // ReactiveUI 23 needs explicit init before any WhenAnyValue use (the AppearanceSettingsViewModel ctor).
    public AppearanceFontSettingsTests() => ReactiveUiTestInit.Ensure();

    // ---- ResolveFontSelection (pure) ----

    [Theory]
    [InlineData("Sanskrit 2003", "Sanskrit 2003")]   // exact match
    [InlineData("sanskrit 2003", "Sanskrit 2003")]   // case-insensitive
    [InlineData("  Sanskrit 2003  ", "Sanskrit 2003")] // trim-insensitive
    public void ResolveFontSelection_returns_the_matching_installed_font(string saved, string expected)
    {
        var fonts = new List<string> { "System Default", "Noto Sans", "Sanskrit 2003" };
        Assert.Equal(expected, AppearanceSettingsViewModel.ResolveFontSelection(fonts, saved));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A Font That Is Not Installed")]
    public void ResolveFontSelection_falls_back_to_System_Default(string? saved)
    {
        var fonts = new List<string> { "System Default", "Noto Sans" };
        Assert.Equal("System Default", AppearanceSettingsViewModel.ResolveFontSelection(fonts, saved));
    }

    // ---- ScriptFontSettingViewModel: selection/persist (Parent left null → a FontFamily change is the
    // observable proxy for "this would persist / notify the FontService") ----

    private static ScriptFontSettingViewModel NewScript(string? savedFont) =>
        new() { ScriptName = "Roman", FontFamily = savedFont, FontSize = 12 };

    [Fact]
    public void ApplyLoadedFonts_sets_the_display_selection_without_erasing_a_missing_saved_font()
    {
        // The saved font is not in the enumerated list (temporarily uninstalled) → the picker shows
        // "System Default", but the saved value must NOT be wiped. (#67 Bug B)
        var vm = NewScript("Noto Serif Devanagari");
        vm.ApplyLoadedFonts(new List<string> { "System Default", "Sanskrit 2003" }, "System Default");

        Assert.Equal("System Default", vm.SelectedFontFamily);        // display
        Assert.Equal("Noto Serif Devanagari", vm.FontFamily);         // saved value preserved
        Assert.Contains("Sanskrit 2003", vm.AvailableFonts);
    }

    [Fact]
    public void SelectedFontFamily_ignores_a_null_write_back()
    {
        // Avalonia writes null into SelectedItem when the ItemsSource is swapped — it must never persist. (#67 Bug B)
        var vm = NewScript("Noto Sans");
        vm.SelectedFontFamily = null!;
        Assert.Equal("Noto Sans", vm.FontFamily);                     // unchanged, not wiped
    }

    [Fact]
    public void SelectedFontFamily_a_genuine_user_pick_persists()
    {
        var vm = NewScript(null);
        vm.ApplyLoadedFonts(new List<string> { "System Default", "Sanskrit 2003" }, "System Default");
        vm.SelectedFontFamily = "Sanskrit 2003";
        Assert.Equal("Sanskrit 2003", vm.FontFamily);                 // flows to the saved value
        Assert.Equal("Sanskrit 2003", vm.SelectedFontFamily);
    }

    [Fact]
    public void BeginLoad_makes_the_previous_load_version_stale()
    {
        // The latest-wins primitive: a slower earlier load can't apply over a newer one. (#67 Bug A)
        var vm = NewScript(null);
        int v1 = vm.BeginLoad();
        Assert.True(vm.IsCurrentLoad(v1));
        int v2 = vm.BeginLoad();
        Assert.False(vm.IsCurrentLoad(v1));
        Assert.True(vm.IsCurrentLoad(v2));
    }

    // ---- Integration: the real LoadAvailableFontsForScript through the injected seams (fake IFontService +
    // synchronous dispatcher hop). Proves the async cancellation/latest-wins glue + Bug A end-to-end. ----

    // Synchronous dispatcher hop so the apply runs inline in tests.
    private static readonly Func<Action, Task> SyncUi = a => { a(); return Task.CompletedTask; };

    private static (Mock<ISettingsService> settings, Mock<IFontService> font) Mocks(params (string Script, string Font)[] scripts)
    {
        var s = new Settings();
        s.FontSettings.ScriptFonts.Clear();
        foreach (var (script, font) in scripts)
            s.FontSettings.ScriptFonts[script] = new ScriptFontSetting { FontFamily = font, FontSize = 12 };
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(x => x.Settings).Returns(s);
        return (settings, new Mock<IFontService>());
    }

    [Fact]
    public void Ctor_loads_the_default_script_prepending_System_Default()
    {
        var (settings, font) = Mocks(("Roman", ""));
        font.Setup(f => f.GetAvailableFontsForScriptAsync(Script.Latin))
            .ReturnsAsync(new List<string> { "Noto Sans" });   // completes synchronously during ctor

        var vm = new AppearanceSettingsViewModel(settings.Object, font.Object, SyncUi);

        var roman = vm.ScriptFontSettings.Single(s => s.ScriptName == "Roman");
        Assert.Equal(new[] { "System Default", "Noto Sans" }, roman.AvailableFonts.ToArray());
        Assert.Equal("System Default", roman.SelectedFontFamily);   // no saved font
    }

    [Fact]
    public void A_load_in_flight_does_not_revert_a_font_the_user_changed_meanwhile()
    {
        // #67 Bug A end-to-end: the parked load resolves its selection against the CURRENT saved font at apply
        // time, so it lands on the user's newer pick instead of reverting to the pre-load value.
        var (settings, font) = Mocks(("Roman", ""));
        var tcs = new TaskCompletionSource<List<string>>();
        font.Setup(f => f.GetAvailableFontsForScriptAsync(Script.Latin)).Returns(() => tcs.Task);

        var vm = new AppearanceSettingsViewModel(settings.Object, font.Object, SyncUi);   // Roman load parked
        var roman = vm.ScriptFontSettings.Single(s => s.ScriptName == "Roman");

        roman.SelectedFontFamily = "User Pick";                    // user changes the font while the load is in flight
        tcs.SetResult(new List<string> { "User Pick", "Other" });  // load applies

        Assert.Equal("User Pick", roman.FontFamily);               // not reverted
        Assert.Equal("User Pick", roman.SelectedFontFamily);
    }

    [Fact]
    public void A_font_enumeration_failure_applies_the_System_Default_fallback()
    {
        var (settings, font) = Mocks(("Roman", ""));
        font.Setup(f => f.GetAvailableFontsForScriptAsync(Script.Latin))
            .Returns(Task.FromException<List<string>>(new InvalidOperationException("boom")));

        var vm = new AppearanceSettingsViewModel(settings.Object, font.Object, SyncUi);

        var roman = vm.ScriptFontSettings.Single(s => s.ScriptName == "Roman");
        Assert.Equal(new[] { "System Default" }, roman.AvailableFonts.ToArray());
        Assert.Equal("System Default", roman.SelectedFontFamily);
    }
}
