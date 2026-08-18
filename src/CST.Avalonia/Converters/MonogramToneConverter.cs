using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CST.Avalonia.Converters
{
    /// <summary>
    /// The tile colour behind a provider's initial in the AI connections list. (#691)
    ///
    /// <para>A stand-in until real vendor marks land. The tone is a hash of the connection id, so a row keeps
    /// the same colour across launches and the eye can find it by something other than reading — which is the
    /// job a logo does on this screen.</para>
    ///
    /// <para><b>The colour means nothing, deliberately.</b> It is not a status, a kind, or a quality: anything
    /// that ranked or grouped providers by colour would be a judgment about them, which is what #670/#681
    /// removed from this app. Muted enough to sit under either theme without a light and dark variant.</para>
    /// </summary>
    public class MonogramToneConverter : IValueConverter
    {
        public static readonly MonogramToneConverter Instance = new();

        // Six, matching AiMonogram.ToneCount. Desaturated so white text reads on every one of them, in both
        // themes, without the tile competing with the display name beside it.
        private static readonly IBrush[] Tones =
        {
            new SolidColorBrush(Color.FromRgb(0x5B, 0x74, 0x9A)),
            new SolidColorBrush(Color.FromRgb(0x6B, 0x8E, 0x7A)),
            new SolidColorBrush(Color.FromRgb(0x8A, 0x6E, 0x9B)),
            new SolidColorBrush(Color.FromRgb(0x9B, 0x7A, 0x5B)),
            new SolidColorBrush(Color.FromRgb(0x5B, 0x8A, 0x9B)),
            new SolidColorBrush(Color.FromRgb(0x9B, 0x6B, 0x6B)),
        };

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int tone && tone >= 0 ? Tones[tone % Tones.Length] : Tones[0];

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
