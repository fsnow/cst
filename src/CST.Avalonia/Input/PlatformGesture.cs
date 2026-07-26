using System;
using Avalonia;
using Avalonia.Input;

namespace CST.Avalonia.Input;

/// <summary>
/// Builds menu shortcuts using the platform's own "command" modifier: Meta (⌘) on macOS, Control on
/// Windows and Linux.
///
/// Every shortcut used to be spelled literally as "Cmd+X". Avalonia parses Cmd, Win, Meta and Super to
/// the same <see cref="KeyModifiers.Meta"/>, and on Windows Meta is the *Windows key* - so the shortcuts
/// were bound to Win+D, Win+F and friends. Most of those are reserved by Windows itself (Win+D shows the
/// desktop, Win+E opens Explorer, Win+G opens Game Bar), so the app never even saw the keystroke. (#28)
///
/// Avalonia has no platform-adaptive token to write in a gesture string - "$ctrl+D" is a parse error -
/// so the substitution has to happen here.
/// </summary>
public static class PlatformGesture
{
    /// <summary>
    /// The platform's command modifier. Prefers Avalonia's own answer so this tracks any future platform
    /// it learns about; falls back to an OS check because <see cref="Application.PlatformSettings"/> is
    /// not guaranteed to be initialised at XAML parse time, and a null here would break startup.
    /// </summary>
    public static KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
        ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

    /// <summary>
    /// Parses a gesture written *without* its command modifier - "o", "shift+e", "OemComma" - and returns
    /// it bound to the platform's command modifier. Delegates to Avalonia's parser so the accepted key
    /// names stay identical to a hand-written gesture string.
    /// </summary>
    public static KeyGesture Parse(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            throw new ArgumentException("Gesture must name at least a key.", nameof(gesture));

        var prefix = CommandModifier == KeyModifiers.Meta ? "Cmd+" : "Ctrl+";
        return KeyGesture.Parse(prefix + gesture);
    }
}
