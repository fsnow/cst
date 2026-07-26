using System;
using Avalonia.Input;
using CST.Avalonia.Input;
using Xunit;

namespace CST.Avalonia.Tests.Input;

// #28: every menu shortcut used to be spelled "Cmd+X". Avalonia parses Cmd/Win/Meta/Super to the same
// KeyModifiers.Meta, and on Windows Meta is the *Windows key* - so the shortcuts were bound to Win+D,
// Win+F and friends, most of which Windows reserves for itself and never delivers to the app.
// These tests pin the platform mapping so the regression cannot come back silently.
//
// Application.Current is null under the headless test host, so these also exercise PlatformGesture's
// OS-check fallback - the path that has to be right when XAML is parsed before PlatformSettings exists.
public class PlatformGestureTests
{
    private static KeyModifiers ExpectedCommandModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    [Fact]
    public void CommandModifier_MatchesTheHostPlatform()
    {
        Assert.Equal(ExpectedCommandModifier, PlatformGesture.CommandModifier);
    }

    [Fact]
    public void CommandModifier_IsNeverMetaOffMacOS()
    {
        if (OperatingSystem.IsMacOS())
            return;

        // The whole point: on Windows/Linux, Meta is the Windows/Super key and must never be used.
        Assert.NotEqual(KeyModifiers.Meta, PlatformGesture.CommandModifier);
    }

    [Theory]
    [InlineData("d", Key.D)]
    [InlineData("o", Key.O)]
    [InlineData("OemComma", Key.OemComma)]
    public void Parse_BindsTheKeyToThePlatformCommandModifier(string gesture, Key expectedKey)
    {
        var parsed = PlatformGesture.Parse(gesture);

        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(ExpectedCommandModifier, parsed.KeyModifiers);
    }

    [Theory]
    [InlineData("shift+e", Key.E)]
    [InlineData("shift+p", Key.P)]
    public void Parse_KeepsAdditionalModifiersAlongsideTheCommandModifier(string gesture, Key expectedKey)
    {
        var parsed = PlatformGesture.Parse(gesture);

        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(ExpectedCommandModifier | KeyModifiers.Shift, parsed.KeyModifiers);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_MatchingAvaloniaGestureParsing()
    {
        Assert.Equal(PlatformGesture.Parse("d"), PlatformGesture.Parse("D"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_RejectsAGestureWithNoKey(string? gesture)
    {
        Assert.Throws<ArgumentException>(() => PlatformGesture.Parse(gesture!));
    }
}
