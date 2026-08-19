using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// Turns a logo cached by <see cref="AiProviderLogos"/> into something a view can draw. (#748)
    ///
    /// <para><b>Why a library rather than a parser.</b> An earlier draft hand-rolled an SVG subset so the
    /// marks could be drawn as Avalonia geometry with no dependency. That was the wrong trade: it covered ~98
    /// of the 105 logos, and since the logos are fetched at runtime rather than baked in at build time
    /// (<b>[fsnow]</b>), models.dev can publish one using a feature the subset does not cover at any moment,
    /// with no release of ours in between and nothing to say it happened.</para>
    ///
    /// <para><b>Theming, measured in the running app.</b> Every logo renders <c>#000000</c> by default —
    /// including the 101 of 105 drawn in <c>currentColor</c>, which would be invisible on a dark ground. The
    /// renderer takes a stylesheet, and <c>* { color: … }</c> repaints exactly those, leaving a logo's own
    /// brand colours alone. <c>svg { color: … }</c> does <b>not</b> work — the declaration does not inherit —
    /// and <c>* { fill: … }</c> is worse than useless: it fills the outline-drawn logos solid.</para>
    /// </summary>
    public interface IAiLogoImages
    {
        /// <summary>
        /// The logo at <paramref name="path"/> drawn for <paramref name="foreground"/>, or null when it cannot
        /// be rendered and the caller should fall back to the monogram. Does not throw.
        /// </summary>
        /// <remarks>
        /// <b>Call this on the UI thread.</b> It builds an <c>SvgImage</c>, and every <c>AvaloniaObject</c>
        /// verifies thread access in its constructor. Called from anywhere else it returns null rather than
        /// throwing, and says so in the log. (fable review)
        /// </remarks>
        IImage? Get(string? path, Color foreground);
    }

    /// <inheritdoc cref="IAiLogoImages"/>
    public sealed class AiLogoImages : IAiLogoImages
    {
        /// <summary>
        /// Far above any real mark — the largest in the current set is under 3 KB — and low enough that a
        /// file which is not really a logo cannot cost a visible pause. These arrive over the network, so
        /// their size is not ours to assume. (fable review)
        /// </summary>
        internal const int MaxBytes = 256 * 1024;

        /// <summary>
        /// Any absolute URL, once the namespace declarations that legitimately contain one are removed.
        /// </summary>
        private static readonly Regex ExternalReference =
            new(@"\b(?:https?|ftp|file)://", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NamespaceDeclaration =
            new(@"xmlns(:[\w-]+)?\s*=\s*""[^""]*""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Keyed by file AND colour: the same mark is a different image in light and dark, and a reader can
        /// switch themes without restarting.
        ///
        /// <para>Only successes are stored. A failure says nothing durable about the file — it may be
        /// mid-rewrite by a concurrent fetch, since #738 deletes and re-fetches to the same path when it
        /// heals a poisoned entry — and remembering one would leave that row on a monogram for the session.
        /// That is the policy #738 already states for its own failures. (fable review)</para>
        /// </summary>
        private readonly ConcurrentDictionary<(string Path, uint Colour), IImage> _images = new();

        public IImage? Get(string? path, Color foreground)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                // Not cached: caching this would poison the entry for every later, correct call.
                Log.Warning("Logo images must be requested on the UI thread; {Path} was not (#748)", path);
                return null;
            }

            // Alpha is deliberately not part of the key, because it is deliberately not part of the render:
            // the stylesheet takes an opaque colour. A caller wanting a faded mark should set Opacity on the
            // control, which is the only way it can match the text beside it. (fable review)
            var key = (path, (uint)((foreground.R << 16) | (foreground.G << 8) | foreground.B));
            if (_images.TryGetValue(key, out var cached)) return cached;

            var image = Render(path, foreground);
            if (image is null) return null;

            _images[key] = image;
            return image;
        }

        private static IImage? Render(string path, Color foreground)
        {
            try
            {
                if (Screen(path) is { } refused)
                {
                    Log.Warning("Not rendering {Path} as a logo: {Reason} (#748)", path, refused);
                    return null;
                }

                // Only currentColor is redirected. A logo that names its own colours keeps them - four of the
                // set do, and repainting a brand mark would be a worse answer than leaving it.
                var css = string.Create(
                    CultureInfo.InvariantCulture,
                    $"* {{ color: #{foreground.R:X2}{foreground.G:X2}{foreground.B:X2}; }}");

                var source = SvgSource.Load(path, null, new Svg.Model.SvgParameters(null, css));
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
                Log.Debug(ex, "Could not render the logo at {Path}; falling back to the monogram (#748)", path);
                return null;
            }
        }

        /// <summary>
        /// Why this file should not be handed to the renderer, or null to go ahead.
        ///
        /// <para>These files come off the network, and the renderer does two things with a document that a
        /// decorative icon has no business doing: it expands XML entities with no quota, so a few KB of
        /// nested declarations become gigabytes; and it fetches <c>&lt;image href="https://…"&gt;</c>
        /// synchronously, on the calling thread, which is the UI thread. Neither appears in any of today's
        /// logos, and neither should ever be worth a frozen window. (fable review)</para>
        ///
        /// <para>Kept as plain file-and-string work so it can be tested without a render backend.</para>
        /// </summary>
        internal static string? Screen(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) return "no such file";
            if (file.Length == 0) return "empty";
            if (file.Length > MaxBytes)
                return $"{file.Length} bytes, over the {MaxBytes} limit";

            var text = File.ReadAllText(path);

            if (text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
                return "declares XML entities";

            // The SVG namespace is itself an http URL, so those have to come out before asking the question.
            if (ExternalReference.IsMatch(NamespaceDeclaration.Replace(text, string.Empty)))
                return "references a remote resource";

            return null;
        }
    }
}
