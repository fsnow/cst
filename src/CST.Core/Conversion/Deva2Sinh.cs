using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Sinh
    {
        private static IDictionary<char, object> deva2Sinh;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Sinh()
        {
            deva2Sinh = new Dictionary<char, object>();

            deva2Sinh['\u0902'] = '\u0D82'; // niggahita

            // independent vowels
            deva2Sinh['\u0905'] = '\u0D85'; // a
            deva2Sinh['\u0906'] = '\u0D86'; // aa
            deva2Sinh['\u0907'] = '\u0D89'; // i
            deva2Sinh['\u0908'] = '\u0D8A'; // ii
            deva2Sinh['\u0909'] = '\u0D8B'; // u
            deva2Sinh['\u090A'] = '\u0D8C'; // uu
            deva2Sinh['\u090F'] = '\u0D91'; // e
            deva2Sinh['\u0913'] = '\u0D94'; // o

            // velar stops
            deva2Sinh['\u0915'] = '\u0D9A'; // ka
            deva2Sinh['\u0916'] = '\u0D9B'; // kha
            deva2Sinh['\u0917'] = '\u0D9C'; // ga
            deva2Sinh['\u0918'] = '\u0D9D'; // gha
            deva2Sinh['\u0919'] = '\u0D9E'; // n overdot a
            
            // palatal stops
            deva2Sinh['\u091A'] = '\u0DA0'; // ca
            deva2Sinh['\u091B'] = '\u0DA1'; // cha
            deva2Sinh['\u091C'] = '\u0DA2'; // ja
            deva2Sinh['\u091D'] = '\u0DA3'; // jha
            deva2Sinh['\u091E'] = '\u0DA4'; // n tilde a

            // retroflex stops
            deva2Sinh['\u091F'] = '\u0DA7'; // t underdot a
            deva2Sinh['\u0920'] = '\u0DA8'; // t underdot ha
            deva2Sinh['\u0921'] = '\u0DA9'; // d underdot a
            deva2Sinh['\u0922'] = '\u0DAA'; // d underdot ha
            deva2Sinh['\u0923'] = '\u0DAB'; // n underdot a

            // dental stops
            deva2Sinh['\u0924'] = '\u0DAD'; // ta
            deva2Sinh['\u0925'] = '\u0DAE'; // tha
            deva2Sinh['\u0926'] = '\u0DAF'; // da
            deva2Sinh['\u0927'] = '\u0DB0'; // dha
            deva2Sinh['\u0928'] = '\u0DB1'; // na

            // labial stops
            deva2Sinh['\u092A'] = '\u0DB4'; // pa
            deva2Sinh['\u092B'] = '\u0DB5'; // pha
            deva2Sinh['\u092C'] = '\u0DB6'; // ba
            deva2Sinh['\u092D'] = '\u0DB7'; // bha
            deva2Sinh['\u092E'] = '\u0DB8'; // ma

            // liquids, fricatives, etc.
            deva2Sinh['\u092F'] = '\u0DBA'; // ya
            deva2Sinh['\u0930'] = '\u0DBB'; // ra
            deva2Sinh['\u0932'] = '\u0DBD'; // la
            deva2Sinh['\u0935'] = '\u0DC0'; // va
            deva2Sinh['\u0938'] = '\u0DC3'; // sa
            deva2Sinh['\u0939'] = '\u0DC4'; // ha
            deva2Sinh['\u0933'] = '\u0DC5'; // l underdot a

            // dependent vowel signs
            deva2Sinh['\u093E'] = '\u0DCF'; // aa
            deva2Sinh['\u093F'] = '\u0DD2'; // i
            deva2Sinh['\u0940'] = '\u0DD3'; // ii
            deva2Sinh['\u0941'] = '\u0DD4'; // u
            deva2Sinh['\u0942'] = '\u0DD6'; // uu
            deva2Sinh['\u0947'] = '\u0DD9'; // e
            deva2Sinh['\u094B'] = '\u0DDC'; // o

            // various signs
            deva2Sinh['\u094D'] = "\u0DCA\u200C"; // virama -> Sinhala virama + ZWNJ

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // numerals
            deva2Sinh['\u0966'] = '0';
            deva2Sinh['\u0967'] = '1';
            deva2Sinh['\u0968'] = '2';
            deva2Sinh['\u0969'] = '3';
            deva2Sinh['\u096A'] = '4';
            deva2Sinh['\u096B'] = '5';
            deva2Sinh['\u096C'] = '6';
            deva2Sinh['\u096D'] = '7';
            deva2Sinh['\u096E'] = '8';
            deva2Sinh['\u096F'] = '9';

            // other
            deva2Sinh['\u0970'] = '.'; // Dev. abbreviation sign

            // zero-width joiners
            deva2Sinh['\u200C'] = ""; // ZWNJ (ignore)
            deva2Sinh['\u200D'] = ""; // ZWJ (ignore)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in deva2Sinh)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Sinhala
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-sinh.xsl");

            string str = Convert(devStr);

            str = ConvertDandas(str);
            return CleanupPunctuation(str);
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
                if (deva2Sinh.ContainsKey(c))
                    sb.Append(deva2Sinh[c]);
                else
                    sb.Append(c);
            }

            string str = sb.ToString();

            // a few special cases in Sinhala, per Vincent's email

            // change joiners before U+0DBA Yayanna to Virama + ZWJ
            str = str.Replace("\u0DCA\u200C\u0DBA", "\u0DCA\u200D\u0DBA");

            // change joiners before U+0DBB Rayanna to Virama + ZWJ
            str = str.Replace("\u0DCA\u200C\u0DBB", "\u0DCA\u200D\u0DBB");

            // change joiners between "kk"
            str = str.Replace("\u0D9A\u0DCA\u200C\u0D9A", "\u0D9A\u0DCA\u200D\u0D9A");

            return str;
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the dict
        // map (virama -> U+0DCA U+200C) AND the three joiner-fixup string.Replace passes into one scan, applying
        // the ZWNJ(U+200C)->ZWJ(U+200D) change against the buffer tail as each triggering letter is emitted.
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
                if (c == 0x200C || c == 0x200D) // ZWNJ / ZWJ -> removed
                    continue;

                string? m = (c < MapLen) ? map[c] : null;
                if (m == null) k = Emit(buf, k, c);
                else for (int j = 0; j < m.Length; j++) k = Emit(buf, k, m[j]);
            }
            return new string(buf, 0, k);
        }

        // Append one Sinhala char, applying the virama-joiner fixups against the buffer tail. (#86)
        private static int Emit(char[] buf, int k, char x)
        {
            buf[k++] = x;
            // virama (U+0DCA) + ZWNJ before ya (U+0DBA) / ra (U+0DBB), or between two ka (U+0D9A): ZWNJ -> ZWJ
            if ((x == 0x0DBA || x == 0x0DBB) && k >= 3 && buf[k - 2] == 0x200C && buf[k - 3] == 0x0DCA)
                buf[k - 2] = (char)0x200D;
            else if (x == 0x0D9A && k >= 4 && buf[k - 2] == 0x200C && buf[k - 3] == 0x0DCA && buf[k - 4] == 0x0D9A)
                buf[k - 2] = (char)0x200D;
            return k;
        }

        public static string ConvertDandas(string str)
        {
            // in gathas, single dandas convert to semicolon, double to period
            // Regex note: the +? is the lazy quantifier which finds the shortest match
            str = Regex.Replace(str, "<p rend=\"gatha[a-z0-9]*\".+?</p>",
                new MatchEvaluator(ConvertGathaDandas), RegexOptions.Compiled);

            // remove double dandas around namo tassa
            str = Regex.Replace(str, "<p rend=\"centre\".+?</p>",
                new MatchEvaluator(RemoveNamoTassaDandas), RegexOptions.Compiled);

            // convert all others to period
            str = str.Replace("\u0964", ".");
            str = str.Replace("\u0965", ".");
            return str;
        }

        public static string ConvertGathaDandas(Match m)
        {
            string str = m.Value;
            str = str.Replace("\u0964", ";");
            str = str.Replace("\u0965", ".");
            return str;
        }

        public static string RemoveNamoTassaDandas(Match m)
        {
            string str = m.Value;
            return str.Replace("\u0965", "");
        }

        // There should be no spaces before these
        // punctuation marks. 
        public static string CleanupPunctuation(string str)
        {
			// two spaces to one
			str = str.Replace("  ", " ");

            str = str.Replace(" ,", ",");
            str = str.Replace(" ?", "?");
            str = str.Replace(" !", "!");
            str = str.Replace(" ;", ";");
            // does not affect peyyalas because they have ellipses now
            str = str.Replace(" .", ".");
            return str;
        }
    }
}
