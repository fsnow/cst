using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Styling;
using CST.Avalonia.Converters;
using Xunit;

namespace CST.Avalonia.Tests.Converters;

/// <summary>
/// #971: the multi-value overload, which exists so a theme switch redraws the marks.
///
/// <para><b>What is not covered here.</b> That the switch actually re-runs the binding is a property of the
/// binding, not of this class, and asserting it needs a headless Avalonia application the test project does
/// not have. It was verified instead by loading the exact XAML written into the two views —
/// <c>&lt;Binding Path="ActualThemeVariant" RelativeSource="{RelativeSource Self}"/&gt;</c> as the second
/// value — into a headless app and switching <c>RequestedThemeVariant</c>: the converter ran again, with
/// <c>Dark</c>. See the PR.</para>
///
/// <para>Rendering is likewise out of reach: it needs a service in the container and the UI thread. What is
/// left is the contract between the binding and the converter, which is where a mis-written binding would
/// land, so that is what these cover.</para>
/// </summary>
public class ProviderLogoConverterTests
{
    private static object? Convert(params object?[] values) =>
        ProviderLogoConverter.Instance.Convert(
            new List<object?>(values), typeof(IImage), null, CultureInfo.InvariantCulture);

    /// <summary>Null means draw the monogram, and the row already has one behind the image. A binding that
    /// has not resolved yet — the first evaluation of every row — arrives here as UnsetValue.</summary>
    [Fact]
    public void A_path_that_is_not_there_yet_draws_nothing()
    {
        Assert.Null(Convert(null, ThemeVariant.Dark));
        Assert.Null(Convert("", ThemeVariant.Dark));
        Assert.Null(Convert(global::Avalonia.AvaloniaProperty.UnsetValue, ThemeVariant.Dark));
    }

    /// <summary>A short list is a mis-written binding, and the honest answer to one is the monogram rather
    /// than an exception thrown inside the binding machinery, where it would surface as a blank tile with a
    /// log line nobody reads.</summary>
    [Fact]
    public void A_binding_missing_its_values_draws_nothing()
    {
        Assert.Null(Convert());
        Assert.Null(Convert(new object?[] { null }));
    }

    /// <summary>The variant is a dependency, not an input: it is never read, so the answer cannot turn on
    /// which one arrives — only on the fact that a change brings the converter back.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_variant_itself_is_not_read(string? variant)
    {
        object? v = variant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => null,
        };

        Assert.Null(Convert(null, v));
    }

    [Fact]
    public void Converting_back_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => ProviderLogoConverter.Instance.ConvertBack(
            null, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
