using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CST.Avalonia.UiTests.TestSupport;

/// <summary>
/// Assertions for "did this style selector actually match anything?". (#655)
///
/// <para>The whole reason this file exists is that comparing a rendered colour is NOT enough. #646's dead
/// selector left the selected row's foreground at the theme's ordinary text colour - which in dark mode is
/// white, the same white the accent rule would have produced. A value-only assertion would have passed the
/// bug in one theme variant and failed it in the other, which is exactly the coin-flip the issue is about.
/// So every assertion here checks the value AND where the value came from: Avalonia records a
/// <see cref="BindingPriority"/> per property, and a value that arrived from a matching style is at
/// <see cref="BindingPriority.Style"/> or <see cref="BindingPriority.StyleTrigger"/> - never
/// <see cref="BindingPriority.Inherited"/>, <see cref="BindingPriority.Template"/> or
/// <see cref="BindingPriority.Unset"/>, which are what a selector that matched nothing leaves behind.</para>
/// </summary>
internal static class StyleProbe
{
    /// <summary>
    /// Shows <paramref name="content"/> in a headless window under a fixed theme variant and runs the
    /// layout pass. Templates are only applied to a control that has been measured and arranged, and
    /// every selector under test reaches into a template, so this is not optional setup.
    /// </summary>
    public static Window Show(Control content, ThemeVariant variant, double width = 420, double height = 640)
    {
        var window = new Window
        {
            RequestedThemeVariant = variant,
            Width = width,
            Height = height,
            Content = content
        };

        window.Show();
        Pump(window);
        return window;
    }

    /// <summary>Drains the dispatcher and forces a layout pass, so container realisation settles.</summary>
    public static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Resolves a resource the way a <c>{DynamicResource}</c> setter would, from the same scope and theme
    /// variant as the element under test - so the expected value cannot drift from the actual one by being
    /// looked up somewhere else.
    /// </summary>
    public static IBrush Brush(StyledElement scope, string key)
    {
        Assert.True(
            scope.TryFindResource(key, scope.ActualThemeVariant, out var value),
            $"Resource '{key}' resolved to nothing under {scope.ActualThemeVariant}. " +
            "A DynamicResource miss is silent at runtime - see ThemeDictionaryParityTests (#955).");

        return Assert.IsAssignableFrom<IBrush>(value);
    }

    /// <summary>
    /// The named part of <paramref name="templatedControl"/>'s own template - the elements a
    /// <c>/template/</c> selector can reach, and only those. Matching on
    /// <see cref="StyledElement.TemplatedParent"/> is what keeps this from finding a nested control's
    /// identically named part, which is the same scoping rule the selector itself obeys.
    /// </summary>
    public static T TemplatePart<T>(Control templatedControl, string name) where T : Control
    {
        var part = templatedControl.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.Name == name && ReferenceEquals(c.TemplatedParent, templatedControl));

        Assert.True(part is not null,
            $"No {typeof(T).Name} named '{name}' in this {templatedControl.GetType().Name}'s own template. " +
            "The theme's template changed, and every selector naming that part is now dead. " +
            "Fix the selectors - do not relax this assertion, or the tests below go vacuous.");

        return part!;
    }

    /// <summary>
    /// Asserts that a style rule matched this element and set this property to this value.
    ///
    /// <para>Both halves matter. The value alone can be right by accident (an inherited white that happens
    /// to equal the accent white); the priority alone only says something matched, not that it was the
    /// rule you meant.</para>
    /// </summary>
    public static void AssertStyled(AvaloniaObject target, AvaloniaProperty property, object expected, string because)
    {
        var actual = target.GetDiagnostic(property);

        Assert.True(
            actual.Priority is BindingPriority.Style or BindingPriority.StyleTrigger,
            $"{because}\n" +
            $"No style setter reached {target.GetType().Name}.{property.Name}: its value is at priority " +
            $"{actual.Priority}, not Style/StyleTrigger. That is what a selector matching NOTHING looks " +
            $"like - the property simply kept whatever the theme or inheritance gave it. " +
            $"Current value: {Describe(actual.Value)}.");

        AssertSameBrushOrValue(expected, actual.Value, because);
    }

    /// <summary>
    /// Asserts that no style setter is holding this property - the counterpart used for scoping claims,
    /// where the point is that a rule did NOT reach an element.
    /// </summary>
    public static void AssertNotStyled(AvaloniaObject target, AvaloniaProperty property, object notExpected, string because)
    {
        var actual = target.GetDiagnostic(property);

        var sameValue = BrushesMatch(notExpected, actual.Value);
        var styled = actual.Priority is BindingPriority.Style or BindingPriority.StyleTrigger;

        Assert.False(
            sameValue && styled,
            $"{because}\n" +
            $"A style setter DID reach {target.GetType().Name}.{property.Name} and gave it " +
            $"{Describe(actual.Value)} at priority {actual.Priority}. The selector is broader than the " +
            "comment claims it is.");
    }

    private static void AssertSameBrushOrValue(object expected, object? actual, string because)
    {
        Assert.True(
            BrushesMatch(expected, actual),
            $"{because}\nExpected {Describe(expected)} but got {Describe(actual)}.");
    }

    // Brushes are compared by colour rather than by reference: a theme is free to hand out a copy, and a
    // reference check would fail for a reason that has nothing to do with the selector under test.
    private static bool BrushesMatch(object? expected, object? actual)
    {
        if (expected is ISolidColorBrush a && actual is ISolidColorBrush b)
            return a.Color == b.Color;

        return Equals(expected, actual);
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        ISolidColorBrush brush => $"{brush.Color} ({brush.GetType().Name})",
        _ => value.ToString() ?? value.GetType().Name
    };
}
