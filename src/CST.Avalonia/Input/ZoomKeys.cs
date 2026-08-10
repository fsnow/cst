using Avalonia.Input;

namespace CST.Avalonia.Input;

public enum ZoomCommand { In, Out, Reset }

/// <summary>
/// Recognises the book-zoom keystrokes. (#572)
///
/// <para>
/// Matching by <see cref="KeyGesture"/> alone is not safe for this app's users. Avalonia derives
/// <see cref="Key"/> on macOS from the character the active input source <i>produces</i>: with an ASCII
/// layout the "=" key yields <see cref="Key.OemPlus"/> and everything works. With a Devanagari or other
/// Indic input source active — entirely normal for a Pāli reader — the character lookup misses and Avalonia
/// falls back to a QWERTY table that maps the physical "=" key to <see cref="Key.OemMinus"/>. So ⌘⇧= would
/// silently do nothing, while the menu's ⌘= kept working through macOS's own ASCII-layout fallback. The
/// symptom is a dead key for exactly the users most likely to have such a layout active. (fable review)
/// </para>
///
/// <para>
/// <see cref="PhysicalKey"/> is layout-independent, so it is checked as well. Both are consulted rather than
/// physical-only: a layout that deliberately relocates these characters should still work by what it
/// produces, and remapped-keyboard users expect the printed legend to win.
/// </para>
/// </summary>
public static class ZoomKeys
{
    /// <summary>
    /// Returns the zoom command for <paramref name="e"/>, or null when it is not a zoom keystroke.
    /// <paramref name="commandModifier"/> is the platform's command modifier (⌘ on macOS, Ctrl elsewhere).
    /// </summary>
    public static ZoomCommand? Match(KeyEventArgs e, KeyModifiers commandModifier)
    {
        if (!e.KeyModifiers.HasFlag(commandModifier)) return null;
        // Alt+Cmd+= is not ours; excluding it keeps this off any system or app combo built on Alt.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return null;

        // Shift is deliberately NOT tested: "+" is Shift+"=" on most layouts, so both the shifted and
        // unshifted forms of each key must resolve to the same command.
        return (e.Key, e.PhysicalKey) switch
        {
            (Key.OemPlus or Key.Add, _) => ZoomCommand.In,
            (_, PhysicalKey.Equal or PhysicalKey.NumPadAdd) => ZoomCommand.In,

            (Key.OemMinus or Key.Subtract, _) => ZoomCommand.Out,
            (_, PhysicalKey.Minus or PhysicalKey.NumPadSubtract) => ZoomCommand.Out,

            (Key.D0 or Key.NumPad0, _) => ZoomCommand.Reset,
            (_, PhysicalKey.Digit0 or PhysicalKey.NumPad0) => ZoomCommand.Reset,

            _ => null,
        };
    }

    /// <summary>
    /// True for the three spellings a macOS NativeMenu item already declares — unshifted main-row
    /// <c>=</c>, <c>-</c> and <c>0</c>.
    ///
    /// <para>
    /// macOS resolves menu key equivalents itself, and it does so against an ASCII-capable layout, so those
    /// three keep working whatever input source is active. A window handler must therefore skip exactly
    /// them, or a single ⌘= would zoom twice. Everything else — the shifted forms and the numpad — a menu
    /// item cannot express, since it carries one gesture each.
    /// </para>
    /// </summary>
    public static bool IsMacMenuEquivalent(KeyEventArgs e) =>
        !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
        (e.PhysicalKey is PhysicalKey.Equal or PhysicalKey.Minus or PhysicalKey.Digit0
         || e.Key is Key.OemPlus or Key.OemMinus or Key.D0);
}
