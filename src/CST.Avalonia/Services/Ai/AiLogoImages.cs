using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// Turns a logo cached by <see cref="AiProviderLogos"/> into something a view can draw. (#748)
    ///
    /// <para><b>Why a stylesheet rather than a parser.</b> An earlier draft of this hand-rolled an SVG
    /// subset so the marks could be drawn as Avalonia geometry with no dependency. That was the wrong trade:
    /// it covered ~98 of the 105 logos, and since the logos are fetched at runtime rather than baked in at
    /// build time (<b>[fsnow]</b>), models.dev can publish one using a feature the subset does not cover at
    /// any moment, with no release of ours in between and nothing to say it happened. Avalonia.Svg.Skia is
    /// MIT and binds SkiaSharp, which Avalonia already renders through.</para>
    ///
    /// <para><b>Measured, in the running app.</b> Every logo renders <c>#000000</c> by default — including the
    /// 101 of 105 drawn in <c>currentColor</c>, which would be invisible on a dark ground. The renderer takes
    /// a stylesheet, and <c>* { color: … }</c> repaints exactly those, leaving a logo's own brand colours
    /// alone. <c>svg { color: … }</c> does <b>not</b> work — the declaration does not inherit — and
    /// <c>* { fill: … }</c> is worse than useless: it fills the outline-drawn logos solid.</para>
    /// </summary>
    public interface IAiLogoImages
    {
        /// <summary>The logo at <paramref name="path"/> drawn for <paramref name="foreground"/>, or null when
        /// it cannot be rendered and the caller should fall back to the monogram. Never throws.</summary>
        IImage? Get(string? path, Color foreground);
    }

    /// <inheritdoc cref="IAiLogoImages"/>
    public sealed class AiLogoImages : IAiLogoImages
    {
        /// <summary>
        /// Keyed by file AND colour: the same mark is a different image in light and dark, and a reader can
        /// switch themes without restarting.
        /// </summary>
        private readonly ConcurrentDictionary<(string Path, uint Colour), IImage?> _images = new();

        public IImage? Get(string? path, Color foreground)
        {
            if (string.IsNullOrEmpty(path)) return null;

            return _images.GetOrAdd((path, foreground.ToUInt32()), key =>
            {
                try
                {
                    // Only currentColor is redirected. A logo that names its own colours keeps them - four of
                    // the set do, and repainting a brand mark would be a worse answer than leaving it.
                    var css = string.Create(
                        CultureInfo.InvariantCulture, $"* {{ color: #{foreground.R:X2}{foreground.G:X2}{foreground.B:X2}; }}");

                    var source = SvgSource.Load(key.Path, null, new Svg.Model.SvgParameters(null, css));
                    if (source is null) return null;

                    var image = new SvgImage { Source = source };

                    // A document that parsed but drew nothing is not a logo. Cheaper to catch here than as an
                    // invisible row in the catalogue.
                    return image.Size.Width > 0 && image.Size.Height > 0 ? image : null;
                }
                catch (Exception ex)
                {
                    // Same contract as the fetch: a logo is decoration, so anything unreadable means the
                    // monogram, which is already what the row is showing.
                    Log.Debug(ex, "Could not render the logo at {Path}; falling back to the monogram (#748)", key.Path);
                    return null;
                }
            });
        }
    }
}
