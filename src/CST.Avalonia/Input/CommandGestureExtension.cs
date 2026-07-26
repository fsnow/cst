using System;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace CST.Avalonia.Input;

/// <summary>
/// XAML form of <see cref="PlatformGesture"/>, so menu declarations stay declarative:
/// <c>Gesture="{input:CommandGesture o}"</c> yields ⌘O on macOS and Ctrl+O on Windows/Linux.
///
/// Write the gesture *without* its command modifier - "o", "shift+e", "OemComma". Spelling the modifier
/// out ("cmd+o") is exactly the bug this replaces.
/// </summary>
public sealed class CommandGestureExtension : MarkupExtension
{
    public CommandGestureExtension()
    {
    }

    public CommandGestureExtension(string gesture) => Gesture = gesture;

    /// <summary>The gesture minus its command modifier, e.g. "shift+p".</summary>
    public string Gesture { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => PlatformGesture.Parse(Gesture);
}
