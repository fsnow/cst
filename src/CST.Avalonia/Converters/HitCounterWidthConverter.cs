using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CST.Avalonia.Converters;

/// <summary>
/// Reserves a stable MinWidth for the search hit counter ("{current} of {total}") so its width
/// can't change as the current-index digit count grows during navigation — a changing width would
/// reflow the toolbar WrapPanel to a second row and shrink the WebView viewport, scrolling the
/// target hit out of view (#196).
///
/// The reserve is per-book, not a flat constant: the widest string a given search can produce is
/// "{total} of {total}", so we size to the digit count of TotalHits. A 5-hit search reserves far
/// less than a 4-digit one, which minimises the chance of a permanent second row (the over-reserve
/// #196's first fix, a flat MinWidth, was rightly criticised for). Returns a comfortable upper
/// bound on the rendered width so the counter never grows past it (no reflow), while staying as
/// snug as the book allows.
/// </summary>
public class HitCounterWidthConverter : IValueConverter
{
    public static readonly HitCounterWidthConverter Instance = new();

    // Generous per-character upper bound for the toolbar's UI font (Latin digits + " of "); the real
    // advance is smaller, so the reserve always covers the widest "{total} of {total}" without ever
    // being exceeded during navigation.
    private const double PerCharWidth = 9.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int total = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };

        // Digit count of the total (the current index is always <= total, so this bounds both numbers).
        int digits = total <= 0 ? 1 : (int)Math.Floor(Math.Log10(total)) + 1;

        // Widest string is "{digits} of {digits}" = 2*digits + 4 chars (" of " has 4 chars).
        return (2 * digits + 4) * PerCharWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
