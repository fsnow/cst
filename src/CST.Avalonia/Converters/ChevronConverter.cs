using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CST.Avalonia.Converters
{
    /// <summary>The disclosure triangle for a collapsible group in the AI models list. (#692)</summary>
    /// <remarks>
    /// A glyph rather than two styled Path elements: the settings page has no icon set of its own, and one
    /// character in the UI font renders identically on macOS, Windows and Linux without shipping an asset.
    /// </remarks>
    public class ChevronConverter : IValueConverter
    {
        public static readonly ChevronConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? "▼" : "▶";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
