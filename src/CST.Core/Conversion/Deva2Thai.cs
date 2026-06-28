using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Thai
    {
        private static IDictionary<char, object> dev2Thai;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Thai()
        {
            dev2Thai = new Dictionary<char, object>();

            dev2Thai['\u0902'] = '\u0E4D'; // niggahita

            // independent vowels
            dev2Thai['\u0905'] = '\u0E2D'; // a
            dev2Thai['\u0906'] = "\u0E2D\u0E32"; // aa
            dev2Thai['\u0907'] = "\u0E2D\u0E34"; // i
            dev2Thai['\u0908'] = "\u0E2D\u0E35"; // ii
            dev2Thai['\u0909'] = "\u0E2D\u0E38"; // u
            dev2Thai['\u090A'] = "\u0E2D\u0E39"; // uu
            dev2Thai['\u090F'] = "\u0E40\u0E2D"; // e
            dev2Thai['\u0913'] = "\u0E42\u0E2D"; // o

            // velar stops
            dev2Thai['\u0915'] = '\u0E01'; // ka
            dev2Thai['\u0916'] = '\u0E02'; // kha
            dev2Thai['\u0917'] = '\u0E04'; // ga
            dev2Thai['\u0918'] = '\u0E06'; // gha
            dev2Thai['\u0919'] = '\u0E07'; // n overdot a
            
            // palatal stops
            dev2Thai['\u091A'] = '\u0E08'; // ca
            dev2Thai['\u091B'] = '\u0E09'; // cha
            dev2Thai['\u091C'] = '\u0E0A'; // ja
            dev2Thai['\u091D'] = '\u0E0C'; // jha
            dev2Thai['\u091E'] = '\u0E0D'; // n tilde a

            // retroflex stops
            dev2Thai['\u091F'] = '\u0E0F'; // t underdot a
            dev2Thai['\u0920'] = '\u0E10'; // t underdot ha
            dev2Thai['\u0921'] = '\u0E11'; // d underdot a
            dev2Thai['\u0922'] = '\u0E12'; // d underdot ha
            dev2Thai['\u0923'] = '\u0E13'; // n underdot a

            // dental stops
            dev2Thai['\u0924'] = '\u0E15'; // ta
            dev2Thai['\u0925'] = '\u0E16'; // tha
            dev2Thai['\u0926'] = '\u0E17'; // da
            dev2Thai['\u0927'] = '\u0E18'; // dha
            dev2Thai['\u0928'] = '\u0E19'; // na

            // labial stops
            dev2Thai['\u092A'] = '\u0E1B'; // pa
            dev2Thai['\u092B'] = '\u0E1C'; // pha
            dev2Thai['\u092C'] = '\u0E1E'; // ba
            dev2Thai['\u092D'] = '\u0E20'; // bha
            dev2Thai['\u092E'] = '\u0E21'; // ma

            // liquids, fricatives, etc.
            dev2Thai['\u092F'] = '\u0E22'; // ya
            dev2Thai['\u0930'] = '\u0E23'; // ra
            dev2Thai['\u0932'] = '\u0E25'; // la
            dev2Thai['\u0935'] = '\u0E27'; // va
            dev2Thai['\u0938'] = '\u0E2A'; // sa
            dev2Thai['\u0939'] = '\u0E2B'; // ha
            dev2Thai['\u0933'] = '\u0E2C'; // l underdot a

            // dependent vowel signs
            dev2Thai['\u093E'] = '\u0E32'; // aa
            dev2Thai['\u093F'] = '\u0E34'; // i
            dev2Thai['\u0940'] = '\u0E35'; // ii
            dev2Thai['\u0941'] = '\u0E38'; // u
            dev2Thai['\u0942'] = '\u0E39'; // uu
            dev2Thai['\u0947'] = '\u0E40'; // e
            dev2Thai['\u094B'] = '\u0E42'; // o

            dev2Thai['\u094D'] = '\u0E3A'; // virama

            // numerals
            dev2Thai['\u0966'] = '\u0E50';
            dev2Thai['\u0967'] = '\u0E51';
            dev2Thai['\u0968'] = '\u0E52';
            dev2Thai['\u0969'] = '\u0E53';
            dev2Thai['\u096A'] = '\u0E54';
            dev2Thai['\u096B'] = '\u0E55';
            dev2Thai['\u096C'] = '\u0E56';
            dev2Thai['\u096D'] = '\u0E57';
            dev2Thai['\u096E'] = '\u0E58';
            dev2Thai['\u096F'] = '\u0E59';

            // other
            dev2Thai['\u0970'] = '.'; // Dev. abbreviation sign
            dev2Thai['\u200C'] = ""; // ZWNJ (remove)
            dev2Thai['\u200D'] = ""; // ZWJ (remove)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in dev2Thai)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Thai
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-thai.xsl");

            string str = Convert(devStr);

            str = ConvertDandas(str);
            return CleanupPunctuation(str);
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        /// <summary>
        /// FROZEN reference implementation (the original readable version) - the correctness oracle for the
        /// optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string devStr)
        {
            // first remove all the ZWJs
            devStr = devStr.Replace("\u200D", "");

            // pre-processing step for Thai: put the e vowel before its consonant
            // Note: The vowel only applies to the consonant immediately before it,
            // not to an entire consonant cluster
            devStr = Regex.Replace(devStr, "([\u0915-\u0939])\u0947", "\u0E40$1");

            // pre-processing step for Thai: put the o vowel before its consonant
            // Note: The vowel only applies to the consonant immediately before it,
            // not to an entire consonant cluster
            devStr = Regex.Replace(devStr, "([\u0915-\u0939])\u094B", "\u0E42$1");

            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (dev2Thai.ContainsKey(c))
                    sb.Append(dev2Thai[c]);
                else
                    sb.Append(c);
            }

            string thai = sb.ToString();

            // Post-processing: combine i + niggahita into single character
            // In Thai Pali, i\u1E43 has a special single Unicode character
            thai = thai.Replace("\u0E34\u0E4D", "\u0E36");

            return thai;
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the ZWJ
        // removal, the two e/o-vowel reorder regexes, the dict map, and the i+niggahita combine into one scan.
        // A consonant (U+0915-U+0939) immediately followed by the e (U+0947) or o (U+094B) dependent vowel
        // emits the Thai pre-vowel (U+0E40/U+0E42) BEFORE the consonant; U+0E34 + U+0E4D collapses to U+0E36.
        public static string Convert(string devStr)
        {
            if (string.IsNullOrEmpty(devStr))
                return devStr;

            int n = devStr.Length;
            var buf = new char[n * maxValLen + 1];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = devStr[i];
                if (c == 0x200D) continue; // ZWJ removed first (reference step 1)
                if (c == 0x200C) continue; // ZWNJ -> "" in the dict pass

                // reorder: consonant + e/o dependent vowel -> Thai pre-vowel then consonant. ZWJ between the
                // two was already removed before the reference's reorder regex, so skip it when peeking.
                if (c >= 0x0915 && c <= 0x0939)
                {
                    int j = i + 1;
                    while (j < n && devStr[j] == 0x200D) j++;
                    char next = (j < n) ? devStr[j] : '\0';
                    if (next == 0x0947 || next == 0x094B)
                    {
                        k = Emit(buf, k, next == 0x0947 ? (char)0x0E40 : (char)0x0E42);
                        k = Emit(buf, k, (c < MapLen && map[c] != null) ? map[c][0] : c); // consonant (single-char)
                        i = j; // consume the vowel (and any skipped ZWJ)
                        continue;
                    }
                }

                string? m = (c < MapLen) ? map[c] : null;
                if (m == null) k = Emit(buf, k, c);
                else for (int p = 0; p < m.Length; p++) k = Emit(buf, k, m[p]);
            }
            return new string(buf, 0, k);
        }

        // Append one Thai char, collapsing i (U+0E34) + niggahita (U+0E4D) into U+0E36. (#86)
        private static int Emit(char[] buf, int k, char x)
        {
            buf[k++] = x;
            if (x == 0x0E4D && k >= 2 && buf[k - 2] == 0x0E34) { buf[k - 2] = (char)0x0E36; k--; }
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
                new MatchEvaluator(ConvertNamoTassaDandas), RegexOptions.Compiled);

            // convert all others to Thai paiyannoi
            str = str.Replace("\u0964", "\u0E2F");
            str = str.Replace("\u0965", "\u0E2F");
            return str;
        }

        public static string ConvertGathaDandas(Match m)
        {
            string str = m.Value;
            str = str.Replace("\u0964", ";");
            str = str.Replace("\u0965", "\u0E2F");
            return str;
        }

        public static string ConvertNamoTassaDandas(Match m)
        {
            string str = m.Value;
            return str.Replace("\u0965", "\u0E2F");
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
