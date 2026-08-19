using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CST.Avalonia.Services.Ai;

namespace CST.Avalonia.Converters
{
    /// <summary>
    /// Turns a cached logo path into something a row can draw. (#740)
    ///
    /// <para>The path comes from the view model, which resolves it per row and leaves it null when the
    /// provider has none. Null here means <b>draw the monogram</b>, which is why this returns null rather than
    /// a placeholder: the row already has a tile behind it, and the binding simply never replaces it.</para>
    ///
    /// <para><b>The colour is read from the theme at conversion time</b>, because a mark drawn in
    /// <c>currentColor</c> is invisible against its own background in one of the two themes. A row built
    /// before a theme switch keeps the colour it was built with until the list is rebuilt — acceptable
    /// because the settings window is short-lived and a theme change is rare, and worth stating so nobody
    /// reads it as a bug.</para>
    /// </summary>
    public class ProviderLogoConverter : IValueConverter
    {
        public static readonly ProviderLogoConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || path.Length == 0) return null;
            if (App.TryGetService<IAiLogoImages>() is not { } images) return null;

            return images.Get(path, Foreground());
        }

        /// <summary>The theme's own primary text colour, so a monochrome mark reads like the name beside
        /// it. Falls back to a mid grey that is legible on either ground rather than to black, which
        /// disappears in dark mode.</summary>
        private static Color Foreground()
        {
            if (Application.Current is { } app &&
                app.TryGetResource("TextFillColorPrimaryBrush", app.ActualThemeVariant, out var found) &&
                found is ISolidColorBrush brush)
            {
                return brush.Color;
            }

            return Color.FromRgb(0x80, 0x80, 0x80);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
