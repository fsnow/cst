using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #836: the Welcome tab's title takes the reader's Latin face and size, like the book tabs beside it.
///
/// <para><b>Why this exists.</b> The tab strip's style is a compiled binding, and it originally named
/// <c>BookDisplayViewModel</c> as its data type — so it applied to books and silently did nothing for every
/// other tab. Welcome kept the theme's UI font while book titles took the reader's, in the same strip, which
/// reads as a bug because it is one.</para>
///
/// <para><b>And why it asserts the VALUE rather than that a font service was consulted.</b> The first attempt
/// resolved the service in a field initializer. The dock factory builds this document while the layout is
/// being created, which can precede the service provider being populated — so it captured null for the life
/// of the app and fell back to Helvetica at 12. That failure is invisible from the outside: a tab with a
/// fallback font looks exactly like a tab with no binding. Only comparing against what the font service
/// actually reports can tell them apart.</para>
/// </summary>
public class WelcomeTabFontTests
{
    [Fact]
    public void The_welcome_tab_reports_the_readers_Latin_face_and_size()
    {
        var fonts = new StubFonts(family: "Charis SIL", size: 17);

        var vm = new WelcomeViewModel(new WelcomeUpdateService(), fonts);

        Assert.Equal("Charis SIL", vm.CurrentScriptFontFamily);
        Assert.Equal(17, vm.CurrentScriptFontSize);
    }

    /// <summary>
    /// Latin, not whatever script the reader is currently in.
    ///
    /// <para>"Welcome" is a Latin word. Asking a Devanāgarī face to render it would be the same mistake as
    /// the one being fixed, pointed the other way — and on a machine whose Devanāgarī font lacks Latin
    /// coverage it would produce the tofu that started this.</para>
    /// </summary>
    [Fact]
    public void It_asks_for_Latin_specifically()
    {
        var fonts = new StubFonts(family: "Charis SIL", size: 17);

        var vm = new WelcomeViewModel(new WelcomeUpdateService(), fonts);
        _ = vm.CurrentScriptFontFamily;
        _ = vm.CurrentScriptFontSize;

        Assert.Equal(new[] { Script.Latin, Script.Latin }, fonts.Asked);
    }

    /// <summary>
    /// A missing font service degrades to a readable default rather than throwing.
    ///
    /// <para>Reachable in the app: the property can be read before the service provider is up. What must not
    /// happen is an exception on the tab strip's binding path.</para>
    /// </summary>
    [Fact]
    public void With_no_font_service_it_falls_back_instead_of_throwing()
    {
        var vm = new WelcomeViewModel(new WelcomeUpdateService(), fontService: null);

        Assert.False(string.IsNullOrWhiteSpace(vm.CurrentScriptFontFamily));
        Assert.True(vm.CurrentScriptFontSize > 0);
    }

    /// <summary>
    /// The tab follows a later change to the font settings. (#836)
    ///
    /// <para><b>This is the test the reported defect needed and the first version did not have.</b> The tab
    /// strip reads these properties once, while the layout is built — which at startup is BEFORE settings
    /// have loaded, so the first read sees the default size. Everything then depends on the view model
    /// hearing that fonts changed and telling the binding to come back. Without this test, the subscription
    /// could be deleted and every other test still passed: the values were right, just never re-read.</para>
    ///
    /// <para>Asserted as PropertyChanged rather than as a value, because a value read after the change would
    /// be correct whether or not anything was notified — which is precisely the failure being guarded.</para>
    /// </summary>
    [Fact]
    public void A_later_font_change_is_announced_to_the_binding()
    {
        var fonts = new StubFonts(family: "Helvetica", size: 12);
        var vm = new WelcomeViewModel(new WelcomeUpdateService(), fonts);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        fonts.ChangeTo("Charis SIL", 17);

        Assert.Contains(nameof(WelcomeViewModel.CurrentScriptFontFamily), changed);
        Assert.Contains(nameof(WelcomeViewModel.CurrentScriptFontSize), changed);
        Assert.Equal(17, vm.CurrentScriptFontSize);
    }

    private sealed class StubFonts : IFontService
    {
        private string _family;
        private int _size;
        public List<Script> Asked { get; } = new();

        public StubFonts(string family, int size) { _family = family; _size = size; }

        /// <summary>Changes what the service reports and announces it, as loading settings does.</summary>
        public void ChangeTo(string family, int size)
        {
            _family = family; _size = size;
            FontSettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public string? GetScriptFontFamily(Script script) { Asked.Add(script); return _family; }
        public int GetScriptFontSize(Script script) { Asked.Add(script); return _size; }

        public string GetLocalizationFontFamily() => _family;
        public int GetLocalizationFontSize() => _size;
        public void UpdateFontSettings(FontSettings fontSettings) { }
        public event EventHandler? FontSettingsChanged;
        public Task PreloadFontsForAllScriptsAsync() => Task.CompletedTask;
        public Task<List<string>> GetAvailableFontsForScriptAsync(Script script) =>
            Task.FromResult(new List<string> { _family });
        public Task<string?> GetSystemDefaultFontForScriptAsync(Script script) =>
            Task.FromResult<string?>(_family);
    }
}
