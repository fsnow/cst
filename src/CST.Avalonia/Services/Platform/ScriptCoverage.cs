using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CST.Conversion;

namespace CST.Avalonia.Services.Platform
{
    /// <summary>
    /// Which codepoints a font has to be able to draw before it is worth offering for a given script. (#29)
    ///
    /// <para>Deliberately free of any Avalonia or OS dependency, so the part that decides what "supports this
    /// script" means is testable on its own. The font enumeration around it needs a running
    /// <c>FontManager</c>; this does not, and this is where a wrong answer would silently produce a picker
    /// full of fonts that render tofu.</para>
    /// </summary>
    internal static class ScriptCoverage
    {
        /// <summary>
        /// The probe text: <c>mahāsatipaṭṭhānasuttaṃ</c>, the same phrase <c>MacFontService</c> uses, so the two
        /// platforms filter on identical evidence.
        ///
        /// <para>Written with escapes because CLAUDE.md requires it of script-conversion code - and the reason
        /// applies with force here. The Pāli diacritics are exactly what a font is likely to lack, so a
        /// mangled literal would not throw; it would quietly relax the test and admit fonts that cannot render
        /// the text.</para>
        /// </summary>
        internal const string PaliSample = "mah\u0101satipa\u1E6D\u1E6Dh\u0101nasutta\u1E43";

        /// <summary>
        /// Scripts for which filtering is meaningless. <see cref="Script.Unknown"/> is not a script, and
        /// <see cref="Script.Ipe"/> is the internal search encoding, never rendered to a reader - offering a
        /// filtered font list for either would be inventing a requirement.
        /// </summary>
        private static bool IsRenderable(Script script) =>
            script != Script.Unknown && script != Script.Ipe;

        /// <summary>
        /// Every distinct codepoint a font must cover for this script, or an EMPTY list meaning "do not
        /// filter". Empty is returned when the script is not renderable, or when conversion yields nothing
        /// usable - in which case showing every font is honest, where showing none would look like a bug.
        /// </summary>
        internal static IReadOnlyList<int> CodepointsFor(Script script)
        {
            if (!IsRenderable(script)) return Array.Empty<int>();

            var sample = Convert(script);
            if (string.IsNullOrEmpty(sample)) return Array.Empty<int>();

            // By RUNE, not by char: Tibetan and others reach beyond the BMP, and testing a lone surrogate
            // would ask the font for a glyph that cannot exist.
            var codepoints = new List<int>();
            foreach (var rune in sample.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune)) continue;
                if (!codepoints.Contains(rune.Value)) codepoints.Add(rune.Value);
            }

            return codepoints;
        }

        /// <summary>
        /// One codepoint that stands for the script, for asking the platform which font it would itself pick.
        ///
        /// <para>Chooses the first non-ASCII, non-combining character. ASCII would match nearly every font
        /// installed and tell us nothing; a combining mark in isolation asks the question about a character
        /// that never appears on its own.</para>
        /// </summary>
        internal static int? RepresentativeCodepoint(Script script)
        {
            foreach (var codepoint in CodepointsFor(script))
            {
                if (codepoint < 0x80) continue;

                var category = Rune.GetUnicodeCategory(new Rune(codepoint));
                if (category is UnicodeCategory.NonSpacingMark
                             or UnicodeCategory.SpacingCombiningMark
                             or UnicodeCategory.EnclosingMark
                             or UnicodeCategory.Format)
                {
                    continue;
                }

                return codepoint;
            }

            // Latin is the case that legitimately lands here only if the sample lost its diacritics; every
            // other script is non-ASCII throughout. Falling back to the first codepoint keeps a caller from
            // having to special-case null when the list is non-empty.
            return CodepointsFor(script).Cast<int?>().FirstOrDefault();
        }

        private static string? Convert(Script script)
        {
            try
            {
                return ScriptConverter.Convert(PaliSample, Script.Latin, script);
            }
            catch (Exception)
            {
                // A conversion that cannot be performed must not take the font picker down with it. The caller
                // reads an empty list as "offer everything".
                return null;
            }
        }
    }
}
