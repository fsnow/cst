using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Platform
{
    /// <summary>
    /// Per-script font detection for every platform that is not macOS. (#29)
    ///
    /// <para>Windows previously had no script-aware path at all: the picker offered every installed font for
    /// all 14 scripts, with no per-script default. Since most installed fonts cannot draw Sinhala or Myanmar,
    /// the list actively misled - the entries that work were indistinguishable from the ones that produce
    /// tofu, in an application whose whole promise is those 14 scripts.</para>
    ///
    /// <para><b>Managed, not DirectWrite.</b> The 2025 design for this called for DirectWrite COM interop -
    /// several hundred lines of P/Invoke - because Avalonia offered nothing equivalent at the time. It does
    /// now: <see cref="FontManager.TryGetGlyphTypeface"/> plus <see cref="IGlyphTypeface.TryGetGlyph"/> answers
    /// "can this font draw this character", which is precisely what <c>IDWriteFont::HasCharacter</c> was
    /// wanted for, and <see cref="FontManager.TryMatchCharacter"/> exposes the platform's own fallback chain,
    /// which is what the hardcoded per-script default table was standing in for. No P/Invoke, no new
    /// dependency, and it works on Linux too - which the DirectWrite route never would have.</para>
    ///
    /// <para><c>MacFontService</c> is deliberately left alone. It ships, it works, and rewriting a working
    /// Core Text implementation to prove a point is not a fix.</para>
    /// </summary>
    public sealed class ScriptFontService
    {
        private readonly ILogger _logger;

        public ScriptFontService(ILogger logger) => _logger = logger;

        /// <summary>
        /// The installed fonts that can actually render this script, sorted by name.
        ///
        /// <para><b>Returns an empty list when nothing on the machine can draw the script</b>, rather than
        /// falling back to the unfiltered list. Offering every installed font in that situation would be
        /// offering nothing but wrong answers, dressed as a working picker - the user would choose one, see
        /// tofu, and have no way to tell whether they had picked badly or the system was missing a font.
        /// Empty plus a warning names the actual problem: install a font for this script.</para>
        /// </summary>
        public List<string> GetAvailableFontsForScript(Script script)
        {
            var all = AllSystemFonts();
            var required = ScriptCoverage.CodepointsFor(script);

            // No opinion about this script (Unknown, Ipe, or a conversion that produced nothing usable).
            if (required.Count == 0) return all;

            var stopwatch = Stopwatch.StartNew();
            var supported = new List<string>();

            foreach (var family in SystemFontFamilies())
            {
                if (Supports(family, required)) supported.Add(family.Name);
            }

            // The font the platform would choose belongs in the list too. It has already been verified to
            // cover the script (see GetSystemDefaultFontForScript), so this cannot reintroduce a font that
            // fails the filter, and it cannot make the no-font case come back non-empty.
            var systemDefault = GetSystemDefaultFontForScript(script);
            if (systemDefault is not null && !supported.Contains(systemDefault))
                supported.Add(systemDefault);

            supported.Sort(StringComparer.CurrentCultureIgnoreCase);
            stopwatch.Stop();

            if (supported.Count == 0)
            {
                _logger.LogWarning(
                    "NO INSTALLED FONT can render {Script}: none of the {Count} fonts on this system covers "
                    + "all {Required} required characters. The font list for this script will be empty, and "
                    + "text in it cannot display correctly until a suitable font is installed.",
                    script, all.Count, required.Count);
                return supported;
            }

            _logger.LogDebug("{Supported} of {Total} installed fonts cover {Script} ({Elapsed} ms)",
                supported.Count, all.Count, script, stopwatch.ElapsedMilliseconds);

            return supported;
        }

        /// <summary>
        /// The font the platform would pick for this script itself, or null when it will not say.
        ///
        /// <para>Asked of the OS through Avalonia's fallback rather than hardcoded per script. A table of
        /// "Nirmala UI for Devanagari, Myanmar Text for Myanmar" is a guess about someone else's machine: it
        /// goes stale with a Windows release, and it is simply wrong on a system where those optional features
        /// are not installed. Asking gets the right answer on the machine in front of us.</para>
        ///
        /// <para><b>The answer is verified, not trusted.</b> A fallback chain's job is to return SOMETHING, so
        /// asked about a script with no font installed it can name one that cannot draw a single character of
        /// it. Reporting that would be the worst of both worlds: a default that fails the very filter used to
        /// build the list beside it, so the picker would name a default it does not offer. Checking coverage
        /// makes the no-font case answer null - no opinion - which the caller renders as "System Default", and
        /// <c>NoFontSupportsScript</c> is what tells the user why.</para>
        /// </summary>
        public string? GetSystemDefaultFontForScript(Script script)
        {
            var codepoint = ScriptCoverage.RepresentativeCodepoint(script);
            if (codepoint is null) return null;

            try
            {
                var matched = FontManager.Current.TryMatchCharacter(
                    codepoint.Value,
                    FontStyle.Normal,
                    FontWeight.Normal,
                    FontStretch.Normal,
                    fontFamily: null,
                    CultureInfo.CurrentCulture,
                    out var typeface);

                if (!matched) return null;

                var name = typeface.FontFamily?.Name;
                if (string.IsNullOrWhiteSpace(name)) return null;

                // Only report it if it can really draw the script - see the note above.
                var required = ScriptCoverage.CodepointsFor(script);
                if (required.Count > 0 && !Supports(typeface.FontFamily!, required))
                {
                    _logger.LogDebug(
                        "Platform fallback offered {Font} for {Script}, but it does not cover the script; "
                        + "reporting no default instead.", name, script);
                    return null;
                }

                return name;
            }
            catch (Exception ex)
            {
                // Never fatal: "no opinion" is a perfectly usable answer here, and the caller treats null as
                // "use the system default".
                _logger.LogDebug("No system default font for {Script} | {Details}", script, ex.Message);
                return null;
            }
        }

        private bool Supports(FontFamily family, IReadOnlyList<int> required)
        {
            try
            {
                if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var glyphTypeface))
                    return false;

                // Every probe character, not most: a font missing one Pāli diacritic renders that character as
                // tofu, and one tofu in a word is enough to make the text wrong.
                foreach (var codepoint in required)
                {
                    if (!glyphTypeface.TryGetGlyph((uint)codepoint, out _)) return false;
                }

                return true;
            }
            catch (Exception)
            {
                // A font whose data cannot be loaded is not one to offer.
                return false;
            }
        }

        private IEnumerable<FontFamily> SystemFontFamilies()
        {
            try
            {
                return FontManager.Current.SystemFonts.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not enumerate system fonts");
                return Array.Empty<FontFamily>();
            }
        }

        private List<string> AllSystemFonts() =>
            SystemFontFamilies().Select(f => f.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
