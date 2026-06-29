using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CST.Conversion
{
    public static class Deva2Beng
    {
        private static IDictionary<char, object> deva2Beng;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Beng()
        {
            deva2Beng = new Dictionary<char, object>();

            deva2Beng['\u0902'] = '\u0982'; // niggahita

            // independent vowels
            deva2Beng['\u0905'] = '\u0985'; // a
            deva2Beng['\u0906'] = '\u0986'; // aa
            deva2Beng['\u0907'] = '\u0987'; // i
            deva2Beng['\u0908'] = '\u0988'; // ii
            deva2Beng['\u0909'] = '\u0989'; // u
            deva2Beng['\u090A'] = '\u098A'; // uu
            deva2Beng['\u090B'] = '\u098B'; // vocalic r
            deva2Beng['\u090C'] = '\u098C'; // vocalic l
            deva2Beng['\u090F'] = '\u098F'; // e
            deva2Beng['\u0910'] = '\u0990'; // ai
            deva2Beng['\u0913'] = '\u0993'; // o
            deva2Beng['\u0914'] = '\u0994'; // au

            // velar stops
            deva2Beng['\u0915'] = '\u0995'; // ka
            deva2Beng['\u0916'] = '\u0996'; // kha
            deva2Beng['\u0917'] = '\u0997'; // ga
            deva2Beng['\u0918'] = '\u0998'; // gha
            deva2Beng['\u0919'] = '\u0999'; // n overdot a

            // palatal stops
            deva2Beng['\u091A'] = '\u099A'; // ca
            deva2Beng['\u091B'] = '\u099B'; // cha
            deva2Beng['\u091C'] = '\u099C'; // ja
            deva2Beng['\u091D'] = '\u099D'; // jha
            deva2Beng['\u091E'] = '\u099E'; // n tilde a

            // retroflex stops
            deva2Beng['\u091F'] = '\u099F'; // t underdot a
            deva2Beng['\u0920'] = '\u09A0'; // t underdot ha
            deva2Beng['\u0921'] = '\u09A1'; // d underdot a
            deva2Beng['\u0922'] = '\u09A2'; // d underdot ha
            deva2Beng['\u0923'] = '\u09A3'; // n underdot a

            // dental stops
            deva2Beng['\u0924'] = '\u09A4'; // ta
            deva2Beng['\u0925'] = '\u09A5'; // tha
            deva2Beng['\u0926'] = '\u09A6'; // da
            deva2Beng['\u0927'] = '\u09A7'; // dha
            deva2Beng['\u0928'] = '\u09A8'; // na

            // labial stops
            deva2Beng['\u092A'] = '\u09AA'; // pa
            deva2Beng['\u092B'] = '\u09AB'; // pha
            deva2Beng['\u092C'] = '\u09AC'; // ba
            deva2Beng['\u092D'] = '\u09AD'; // bha
            deva2Beng['\u092E'] = '\u09AE'; // ma

            // liquids, fricatives, etc.
            deva2Beng['\u092F'] = '\u09AF'; // ya
            deva2Beng['\u0930'] = '\u09B0'; // ra
            deva2Beng['\u0932'] = '\u09B2'; // la
            deva2Beng['\u0933'] = "\u09B2\u09BC"; // l underdot a *** la with dot, there's no l underdot in Bengali***
            deva2Beng['\u0935'] = "\u09F0"; // va *** Bengali ra with middle diagonal. Used for Assamese. ***
            deva2Beng['\u0936'] = '\u09B6'; // sha (palatal)
            deva2Beng['\u0937'] = '\u09B7'; // sha (retroflex)
            deva2Beng['\u0938'] = '\u09B8'; // sa
            deva2Beng['\u0939'] = '\u09B9'; // ha

            // dependent vowel signs
            deva2Beng['\u093E'] = '\u09BE'; // aa
            deva2Beng['\u093F'] = '\u09BF'; // i
            deva2Beng['\u0940'] = '\u09C0'; // ii
            deva2Beng['\u0941'] = '\u09C1'; // u
            deva2Beng['\u0942'] = '\u09C2'; // uu
            deva2Beng['\u0943'] = '\u09C3'; // vocalic r
            deva2Beng['\u0947'] = '\u09C7'; // e
            deva2Beng['\u0948'] = '\u09C8'; // ai
            deva2Beng['\u094B'] = '\u09CB'; // o
            deva2Beng['\u094C'] = '\u09CC'; // au

            deva2Beng['\u094D'] = '\u09CD'; // virama

            // numerals
            deva2Beng['\u0966'] = '\u09E6';
            deva2Beng['\u0967'] = '\u09E7';
            deva2Beng['\u0968'] = '\u09E8';
            deva2Beng['\u0969'] = '\u09E9';
            deva2Beng['\u096A'] = '\u09EA';
            deva2Beng['\u096B'] = '\u09EB';
            deva2Beng['\u096C'] = '\u09EC';
            deva2Beng['\u096D'] = '\u09ED';
            deva2Beng['\u096E'] = '\u09EE';
            deva2Beng['\u096F'] = '\u09EF';

            deva2Beng['\u200C'] = ""; // ZWNJ (remove)
            deva2Beng['\u200D'] = ""; // ZWJ (remove)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in deva2Beng)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Bengali
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-beng.xsl");

            return Convert(devStr);
        }

        /// <summary>
        /// FROZEN reference implementation (the original readable version) - the correctness oracle for the
        /// optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string devStr)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (deva2Beng.ContainsKey(c))
                    sb.Append(deva2Beng[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications or capitalization
        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Replaces the
        // per-char dictionary lookup (ContainsKey + indexer, plus Append(object) boxing) with one scan over a
        // char buffer using a string[] table. Pure dictionary substitution - no inherent-'a' or positional rules.
        public static string Convert(string devStr)
        {
            if (string.IsNullOrEmpty(devStr))
                return devStr;

            int n = devStr.Length;
            var buf = new char[n * maxValLen];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = devStr[i];
                if (c == 0x200C || c == 0x200D) // ZWNJ / ZWJ -> removed (map to "" in the reference)
                    continue;

                string? m = (c < MapLen) ? map[c] : null;
                if (m == null) buf[k++] = c; // pass through (danda, Latin, punctuation, etc.)
                else if (m.Length == 1) buf[k++] = m[0];
                else { m.CopyTo(0, buf, k, m.Length); k += m.Length; }
            }
            return new string(buf, 0, k);
        }
    }
}
