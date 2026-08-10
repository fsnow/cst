using Avalonia.Input;
using CST.Avalonia.Input;
using Xunit;

namespace CST.Avalonia.Tests.Input;

// #572: which keystrokes count as book zoom. Matching on Avalonia's Key alone is not safe here, because on
// macOS Key is derived from the character the ACTIVE INPUT SOURCE produces. With a Devanagari or other
// Indic layout active — entirely normal for a Pāli reader — the "=" key does not produce '=', the character
// lookup misses, and Avalonia's QWERTY fallback reports it as OemMinus. Matching Key only would make Cmd+
// silently dead for exactly the users most likely to be affected. PhysicalKey is layout-independent, so
// both are consulted. (fable review)
public class ZoomKeysTests
{
    private const KeyModifiers Cmd = KeyModifiers.Meta;

    private static KeyEventArgs Press(Key key, PhysicalKey physical, KeyModifiers modifiers) =>
        new() { Key = key, PhysicalKey = physical, KeyModifiers = modifiers };

    // ---- The ordinary ASCII-layout cases ----------------------------------------------------------

    [Theory]
    [InlineData(Key.OemPlus, PhysicalKey.Equal)]            // unshifted "=", what Cmd++ really delivers
    [InlineData(Key.Add, PhysicalKey.NumPadAdd)]            // numeric keypad
    public void ZoomIn_Spellings(Key key, PhysicalKey physical)
    {
        Assert.Equal(ZoomCommand.In, ZoomKeys.Match(Press(key, physical, Cmd), Cmd));
        // "+" is Shift+"=" on most layouts, so the shifted form must resolve identically.
        Assert.Equal(ZoomCommand.In, ZoomKeys.Match(Press(key, physical, Cmd | KeyModifiers.Shift), Cmd));
    }

    [Theory]
    [InlineData(Key.OemMinus, PhysicalKey.Minus)]
    [InlineData(Key.Subtract, PhysicalKey.NumPadSubtract)]
    public void ZoomOut_Spellings(Key key, PhysicalKey physical)
    {
        Assert.Equal(ZoomCommand.Out, ZoomKeys.Match(Press(key, physical, Cmd), Cmd));
        Assert.Equal(ZoomCommand.Out, ZoomKeys.Match(Press(key, physical, Cmd | KeyModifiers.Shift), Cmd));
    }

    [Theory]
    [InlineData(Key.D0, PhysicalKey.Digit0)]
    [InlineData(Key.NumPad0, PhysicalKey.NumPad0)]
    public void Reset_Spellings(Key key, PhysicalKey physical)
    {
        Assert.Equal(ZoomCommand.Reset, ZoomKeys.Match(Press(key, physical, Cmd), Cmd));
    }

    // ---- The non-Latin input source case, which is the reason this class exists -------------------

    [Fact]
    public void IndicLayout_EqualsKeyReportedAsOemMinus_StillZoomsIn()
    {
        // Avalonia's QWERTY fallback maps the physical "=" key to OemMinus when the layout produces a
        // non-ASCII character. Matching on Key alone would zoom OUT here, or (since the shifted form is
        // unbound) do nothing at all. PhysicalKey.Equal has to win.
        var e = Press(Key.OemMinus, PhysicalKey.Equal, Cmd | KeyModifiers.Shift);
        Assert.Equal(ZoomCommand.In, ZoomKeys.Match(e, Cmd));
    }

    [Fact]
    public void IndicLayout_RealMinusKey_StillZoomsOut()
    {
        // The same fallback reports a genuine "-" as OemMinus too. This must NOT be captured by the
        // physical-Equal rule above — the two cases are distinguished only by PhysicalKey.
        var e = Press(Key.OemMinus, PhysicalKey.Minus, Cmd);
        Assert.Equal(ZoomCommand.Out, ZoomKeys.Match(e, Cmd));
    }

    [Fact]
    public void UnknownKeyOnAKnownPhysicalKey_StillResolves()
    {
        // A layout we have never seen produces some other character on the "=" key.
        Assert.Equal(ZoomCommand.In, ZoomKeys.Match(Press(Key.OemQuestion, PhysicalKey.Equal, Cmd), Cmd));
    }

    // ---- Non-matches ------------------------------------------------------------------------------

    [Fact]
    public void WithoutTheCommandModifier_NothingMatches()
    {
        Assert.Null(ZoomKeys.Match(Press(Key.OemPlus, PhysicalKey.Equal, KeyModifiers.None), Cmd));
        Assert.Null(ZoomKeys.Match(Press(Key.OemPlus, PhysicalKey.Equal, KeyModifiers.Shift), Cmd));
    }

    [Fact]
    public void WithAlt_NothingMatches()
    {
        // Keeps this off system and app combos built on Alt.
        Assert.Null(ZoomKeys.Match(Press(Key.OemPlus, PhysicalKey.Equal, Cmd | KeyModifiers.Alt), Cmd));
    }

    [Theory]
    [InlineData(Key.D1, PhysicalKey.Digit1)]
    [InlineData(Key.O, PhysicalKey.O)]
    [InlineData(Key.F, PhysicalKey.F)]
    [InlineData(Key.D, PhysicalKey.D)]
    public void OtherShortcutKeys_AreNotZoom(Key key, PhysicalKey physical)
    {
        // Zoom is matched AFTER the letter-shortcut list in both window handlers, but it must not claim
        // those keys even if the order ever changed.
        Assert.Null(ZoomKeys.Match(Press(key, physical, Cmd), Cmd));
    }

    [Fact]
    public void TheWindowsCommandModifierWorksToo()
    {
        var e = Press(Key.OemPlus, PhysicalKey.Equal, KeyModifiers.Control);
        Assert.Equal(ZoomCommand.In, ZoomKeys.Match(e, KeyModifiers.Control));
        // ...and the wrong modifier for the platform does not match.
        Assert.Null(ZoomKeys.Match(e, KeyModifiers.Meta));
    }

    // ---- The macOS menu-equivalent exclusion ------------------------------------------------------

    [Theory]
    [InlineData(Key.OemPlus, PhysicalKey.Equal)]
    [InlineData(Key.OemMinus, PhysicalKey.Minus)]
    [InlineData(Key.D0, PhysicalKey.Digit0)]
    public void MenuEquivalents_AreExcluded_SoTheyCannotFireTwice(Key key, PhysicalKey physical)
    {
        // macOS resolves ⌘=, ⌘- and ⌘0 through the View menu's key equivalents. The window handler must
        // skip exactly those, or one press would zoom two steps.
        Assert.True(ZoomKeys.IsMacMenuEquivalent(Press(key, physical, Cmd)));
    }

    [Theory]
    [InlineData(Key.Add, PhysicalKey.NumPadAdd)]
    [InlineData(Key.Subtract, PhysicalKey.NumPadSubtract)]
    [InlineData(Key.NumPad0, PhysicalKey.NumPad0)]
    public void NumpadSpellings_AreNotMenuEquivalents(Key key, PhysicalKey physical)
    {
        // A NativeMenuItem carries one gesture, so these have no menu route and the handler must take them.
        Assert.False(ZoomKeys.IsMacMenuEquivalent(Press(key, physical, Cmd)));
    }

    [Fact]
    public void ShiftedPlus_IsNotAMenuEquivalent()
    {
        // ⌘⇧= is the spelling most people actually press for "⌘+", and the menu cannot declare it. macOS
        // matches key equivalents shift-sensitively, so the menu will not fire — the handler must.
        Assert.False(ZoomKeys.IsMacMenuEquivalent(Press(Key.OemPlus, PhysicalKey.Equal, Cmd | KeyModifiers.Shift)));
    }

    [Fact]
    public void MenuEquivalentExclusion_HoldsUnderANonLatinLayout()
    {
        // The Indic-fallback "=" (Key reported as OemMinus) is still the plain ⌘= the menu owns, so it must
        // be excluded here even though Match() would classify it as zoom-in.
        Assert.True(ZoomKeys.IsMacMenuEquivalent(Press(Key.OemMinus, PhysicalKey.Equal, Cmd)));
    }
}
