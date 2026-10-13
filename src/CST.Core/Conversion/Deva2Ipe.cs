using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Ipe
    {
        private static IDictionary<string, string> deva2Ipe;

        // Fast lookup table indexed by char code: fastArr[c] = mapped char, or '\0' = no mapping (pass
        // through). The Devanagari block and the Latin-1 IPE values all fit under U+0980. Empty-value
        // entries (virama, ZWNJ, ZWJ) are skipped here and removed explicitly in Convert(). (#86)
        private const int FastArrLen = 0x0980;
        private static readonly char[] fastArr = new char[FastArrLen];

        static Deva2Ipe()
        {
            deva2Ipe = new Dictionary<string, string>();

            deva2Ipe["\u0902"] = "\u00C0"; // niggahita
            deva2Ipe["\u0903"] = "\u00C1"; // visarga (#1049)

            // independent vowels
            deva2Ipe["\u00C2"] = "\u00C2"; // a (the IPE inherent "a" is inserted by a regex. let it pass through.)
            deva2Ipe["\u0905"] = "\u00C2"; // a
            deva2Ipe["\u0906"] = "\u00C3"; // aa
            deva2Ipe["\u0907"] = "\u00C4"; // i
            deva2Ipe["\u0908"] = "\u00C5"; // ii
            deva2Ipe["\u0909"] = "\u00C6"; // u
            deva2Ipe["\u090A"] = "\u00C7"; // uu
            deva2Ipe["\u090B"] = "\u00C8"; // vocalic r (#1049)
            deva2Ipe["\u090F"] = "\u00C9"; // e
            deva2Ipe["\u0910"] = "\u00CA"; // ai (#1049)
            deva2Ipe["\u0913"] = "\u00CB"; // o
            deva2Ipe["\u0914"] = "\u00CC"; // au (#1049)

            // velar stops
            deva2Ipe["\u0915"] = "\u00CD"; // ka
            deva2Ipe["\u0916"] = "\u00CE"; // kha
            deva2Ipe["\u0917"] = "\u00CF"; // ga
            deva2Ipe["\u0918"] = "\u00D0"; // gha
            deva2Ipe["\u0919"] = "\u00D1"; // n overdot a

            // palatal stops
            deva2Ipe["\u091A"] = "\u00D2"; // ca
            deva2Ipe["\u091B"] = "\u00D3"; // cha
            deva2Ipe["\u091C"] = "\u00D4"; // ja
            deva2Ipe["\u091D"] = "\u00D5"; // jha
            deva2Ipe["\u091E"] = "\u00D6"; // n tilde a

            // retroflex stops
            deva2Ipe["\u091F"] = "\u00D8"; // t underdot a
            deva2Ipe["\u0920"] = "\u00D9"; // t underdot ha
            deva2Ipe["\u0921"] = "\u00DA"; // d underdot a
            deva2Ipe["\u0922"] = "\u00DB"; // d underdot ha
            // don"t use D7 multiplication sign
            deva2Ipe["\u0923"] = "\u00DC"; // n underdot a

            // dental stops
            deva2Ipe["\u0924"] = "\u00DD"; // ta
            deva2Ipe["\u0925"] = "\u00DE"; // tha
            deva2Ipe["\u0926"] = "\u00DF"; // da
            deva2Ipe["\u0927"] = "\u00E0"; // dha
            deva2Ipe["\u0928"] = "\u00E1"; // na

            // labial stops
            deva2Ipe["\u092A"] = "\u00E2"; // pa
            deva2Ipe["\u092B"] = "\u00E3"; // pha
            deva2Ipe["\u092C"] = "\u00E4"; // ba
            deva2Ipe["\u092D"] = "\u00E5"; // bha
            deva2Ipe["\u092E"] = "\u00E6"; // ma

            // liquids, fricatives, etc.
            deva2Ipe["\u092F"] = "\u00E7"; // ya
            deva2Ipe["\u0930"] = "\u00E8"; // ra
            deva2Ipe["\u0932"] = "\u00E9"; // la
            deva2Ipe["\u0935"] = "\u00EA"; // va
            deva2Ipe["\u0936"] = "\u00EB"; // palatal sa (#1049)
            deva2Ipe["\u0937"] = "\u00EC"; // retroflex sa (#1049)
            deva2Ipe["\u0938"] = "\u00ED"; // sa
            deva2Ipe["\u0939"] = "\u00EE"; // ha
            deva2Ipe["\u0933"] = "\u00EF"; // l underdot a

            // dependent vowel signs
            deva2Ipe["\u093E"] = "\u00C3"; // aa
            deva2Ipe["\u093F"] = "\u00C4"; // i
            deva2Ipe["\u0940"] = "\u00C5"; // ii
            deva2Ipe["\u0941"] = "\u00C6"; // u
            deva2Ipe["\u0942"] = "\u00C7"; // uu
            deva2Ipe["\u0943"] = "\u00C8"; // vocalic r (#1049)
            deva2Ipe["\u0947"] = "\u00C9"; // e
            deva2Ipe["\u0948"] = "\u00CA"; // ai (#1049)
            deva2Ipe["\u094B"] = "\u00CB"; // o
            deva2Ipe["\u094C"] = "\u00CC"; // au (#1049)

            deva2Ipe["\u094D"] = ""; // virama
            deva2Ipe["\u200C"] = ""; // ZWNJ (ignore)
            deva2Ipe["\u200D"] = ""; // ZWJ (ignore)

            // Derive the fast table from the same data (single-char -> single-char entries only), so the
            // two implementations share one source of truth.
            foreach (var kvp in deva2Ipe)
                if (kvp.Key.Length == 1 && kvp.Value.Length == 1 && kvp.Key[0] < FastArrLen)
                    fastArr[kvp.Key[0]] = kvp.Value[0];
        }

        /// <summary>
        /// FROZEN reference implementation - the original readable version, kept verbatim as the correctness
        /// oracle for the optimized <see cref="Convert"/>. Do NOT change this; tests assert
        /// Convert == ConvertReference across the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string devStr)
        {
            // insert "a" after all consonants that are not followed by virama, dependent vowel or "a"
            // (This still works after we inserted ZWJ in the Devanagari. The ZWJ goes after virama.)
            devStr = Regex.Replace(devStr, "([\u0915-\u0939])([^\u093E-\u094D\u00C2])", "$1\u00C2$2");
            devStr = Regex.Replace(devStr, "([\u0915-\u0939])([^\u093E-\u094D\u00C2])", "$1\u00C2$2");
            // TODO: figure out how to backtrack so this replace doesn"t have to be done twice

            // insert a after consonant that is at end of string
            devStr = Regex.Replace(devStr, "([\u0915-\u0939])$", "$1\u00C2");

            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr)
            {
                if (deva2Ipe.ContainsKey(c.ToString()))
                    sb.Append(deva2Ipe[c.ToString()]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Optimized single-pass conversion. Folds the reference's three inherent-"a" regex passes and the
        /// per-char dictionary lookup (which allocated a string per char via c.ToString()) into one pass.
        /// Byte-identical to <see cref="ConvertReference"/> for all real Devanagari input (verified by tests). (#86)
        ///
        /// A Devanagari consonant (U+0915-U+0939) carries an inherent "a" (U+00C2 in IPE) unless the next
        /// char is a dependent vowel sign or the virama (U+093E-U+094D), which the reference enforced via regex.
        /// </summary>
        public static string Convert(string devStr)
        {
            if (string.IsNullOrEmpty(devStr))
                return devStr;

            int n = devStr.Length;
            // Each input char yields at most 2 output chars (mapped char + inherent vowel); size for that.
            var buf = new char[2 * n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = devStr[i];

                // virama (U+094D) / ZWNJ (U+200C) / ZWJ (U+200D) -> removed (reference maps these to "")
                if (c == 0x094D || c == 0x200C || c == 0x200D)
                    continue;

                char mapped = (c < FastArrLen) ? fastArr[c] : '\0';
                buf[k++] = (mapped != '\0') ? mapped : c; // mapped value, else pass through

                // A consonant (U+0915-U+0939) carries an inherent "a" (U+00C2) unless the next char is a
                // dependent vowel sign or the virama (U+093E-U+094D).
                if (c >= 0x0915 && c <= 0x0939)
                {
                    char next = (i + 1 < n) ? devStr[i + 1] : '\0';
                    if (next < 0x093E || next > 0x094D)
                        buf[k++] = (char)0x00C2; // inherent a
                }
            }

            return new string(buf, 0, k);
        }
    }
}
