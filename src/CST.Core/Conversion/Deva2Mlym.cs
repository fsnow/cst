using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Mlym
    {
        private static IDictionary<char, object> deva2Mlym;

        // Fast lookup for the optimized Convert(): map[c] = replacement char, '\0' = pass through. All
        // Malayalam mappings are single-char, so a char[] table suffices. (#86)
        private const int MapLen = 0x0980;
        private static readonly char[] map = new char[MapLen];

        static Deva2Mlym()
        {
            deva2Mlym = new Dictionary<char, object>();

            // various signs
            deva2Mlym['\u0902'] = '\u0D02'; // anusvara
            deva2Mlym['\u0903'] = '\u0D03'; // visarga

            // independent vowels
            deva2Mlym['\u0905'] = '\u0D05'; // a
            deva2Mlym['\u0906'] = '\u0D06'; // aa
            deva2Mlym['\u0907'] = '\u0D07'; // i
            deva2Mlym['\u0908'] = '\u0D08'; // ii
            deva2Mlym['\u0909'] = '\u0D09'; // u
            deva2Mlym['\u090A'] = '\u0D0A'; // uu
            deva2Mlym['\u090B'] = '\u0D0B'; // vocalic r
            deva2Mlym['\u090C'] = '\u0D0C'; // vocalic l
            deva2Mlym['\u090F'] = '\u0D0F'; // e
            deva2Mlym['\u0910'] = '\u0D10'; // ai
            deva2Mlym['\u0913'] = '\u0D13'; // o
            deva2Mlym['\u0914'] = '\u0D14'; // au

            // velar stops
            deva2Mlym['\u0915'] = '\u0D15'; // ka
            deva2Mlym['\u0916'] = '\u0D16'; // kha
            deva2Mlym['\u0917'] = '\u0D17'; // ga
            deva2Mlym['\u0918'] = '\u0D18'; // gha
            deva2Mlym['\u0919'] = '\u0D19'; // n overdot a
 
            // palatal stops
            deva2Mlym['\u091A'] = '\u0D1A'; // ca
            deva2Mlym['\u091B'] = '\u0D1B'; // cha
            deva2Mlym['\u091C'] = '\u0D1C'; // ja
            deva2Mlym['\u091D'] = '\u0D1D'; // jha
            deva2Mlym['\u091E'] = '\u0D1E'; // n tilde a

            // retroflex stops
            deva2Mlym['\u091F'] = '\u0D1F'; // t underdot a
            deva2Mlym['\u0920'] = '\u0D20'; // t underdot ha
            deva2Mlym['\u0921'] = '\u0D21'; // d underdot a
            deva2Mlym['\u0922'] = '\u0D22'; // d underdot ha
            deva2Mlym['\u0923'] = '\u0D23'; // n underdot a

            // dental stops
            deva2Mlym['\u0924'] = '\u0D24'; // ta
            deva2Mlym['\u0925'] = '\u0D25'; // tha
            deva2Mlym['\u0926'] = '\u0D26'; // da
            deva2Mlym['\u0927'] = '\u0D27'; // dha
            deva2Mlym['\u0928'] = '\u0D28'; // na

            // labial stops
            deva2Mlym['\u092A'] = '\u0D2A'; // pa
            deva2Mlym['\u092B'] = '\u0D2B'; // pha
            deva2Mlym['\u092C'] = '\u0D2C'; // ba
            deva2Mlym['\u092D'] = '\u0D2D'; // bha
            deva2Mlym['\u092E'] = '\u0D2E'; // ma

            // liquids, fricatives, etc.
            deva2Mlym['\u092F'] = '\u0D2F'; // ya
            deva2Mlym['\u0930'] = '\u0D30'; // ra
            deva2Mlym['\u0931'] = '\u0D31'; // rra (Dravidian-specific)
            deva2Mlym['\u0932'] = '\u0D32'; // la
            deva2Mlym['\u0933'] = '\u0D33'; // l underdot a
            deva2Mlym['\u0935'] = '\u0D35'; // va
            deva2Mlym['\u0936'] = '\u0D36'; // sha (palatal)
            deva2Mlym['\u0937'] = '\u0D37'; // sha (retroflex)
            deva2Mlym['\u0938'] = '\u0D38'; // sa
            deva2Mlym['\u0939'] = '\u0D39'; // ha

            // dependent vowel signs
            deva2Mlym['\u093E'] = '\u0D3E'; // aa
            deva2Mlym['\u093F'] = '\u0D3F'; // i
            deva2Mlym['\u0940'] = '\u0D40'; // ii
            deva2Mlym['\u0941'] = '\u0D41'; // u
            deva2Mlym['\u0942'] = '\u0D42'; // uu
            deva2Mlym['\u0943'] = '\u0D43'; // vocalic r
            deva2Mlym['\u0947'] = '\u0D47'; // e
            deva2Mlym['\u0948'] = '\u0D48'; // ai
            deva2Mlym['\u094B'] = '\u0D4B'; // o
            deva2Mlym['\u094C'] = '\u0D4C'; // au

            // various signs
            deva2Mlym['\u094D'] = '\u0D4D'; // virama

            // additional vowels for Sanskrit
            deva2Mlym['\u0960'] = '\u0D60'; // vocalic rr
            deva2Mlym['\u0961'] = '\u0D61'; // vocalic ll

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // digits
            deva2Mlym['\u0966'] = '\u0D66';
            deva2Mlym['\u0967'] = '\u0D67';
            deva2Mlym['\u0968'] = '\u0D68';
            deva2Mlym['\u0969'] = '\u0D69';
            deva2Mlym['\u096A'] = '\u0D6A';
            deva2Mlym['\u096B'] = '\u0D6B';
            deva2Mlym['\u096C'] = '\u0D6C';
            deva2Mlym['\u096D'] = '\u0D6D';
            deva2Mlym['\u096E'] = '\u0D6E';
            deva2Mlym['\u096F'] = '\u0D6F';

            // zero-width joiners
            deva2Mlym['\u200C'] = ""; // ZWNJ (remove)
            deva2Mlym['\u200D'] = ""; // ZWJ (remove)

            // Build the fast char table from the same data (single-char values only; ZWNJ/ZWJ "" skipped). (#86)
            foreach (var kvp in deva2Mlym)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length == 1) map[kvp.Key] = v[0];
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Gurmukhi
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-mlym.xsl");

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
                if (deva2Mlym.ContainsKey(c))
                    sb.Append(deva2Mlym[c]);
                else
                    sb.Append(c);
            }

            string mlym = sb.ToString();

            // e and o occur in two forms in Malayalam based on pronunciation.
            // The short forms occur before double consonants
            mlym = Regex.Replace(mlym, "([\u0D0F\u0D13\u0D47\u0D4B])([\u0D15-\u0D39]\u0D4D[\u0D15-\u0D39])",
                    new MatchEvaluator(ConvertEOChars), RegexOptions.Compiled);

            return mlym;
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the dict
        // map AND the post-processing e/o-shortening regex into one scan. The regex shortens a long e/o vowel
        // (\u0D0F\u0D13\u0D47\u0D4B) when immediately followed by a double consonant (C + virama + C); here we
        // detect that pattern by looking back at the buffer tail as the second consonant is emitted.
        public static string Convert(string devStr)
        {
            if (string.IsNullOrEmpty(devStr))
                return devStr;

            int n = devStr.Length;
            var buf = new char[n]; // all mappings are single-char; removals only shrink, so n is enough
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = devStr[i];
                if (c == 0x200C || c == 0x200D) // ZWNJ / ZWJ -> removed
                    continue;

                char x = (c < MapLen && map[c] != '\0') ? map[c] : c;
                buf[k++] = x;

                // short e/o before a double consonant: tail is [longVowel, C, virama, C(=x)]
                if (x >= 0x0D15 && x <= 0x0D39 && k >= 4
                    && buf[k - 2] == 0x0D4D && buf[k - 3] >= 0x0D15 && buf[k - 3] <= 0x0D39)
                {
                    switch (buf[k - 4])
                    {
                        case (char)0x0D0F: buf[k - 4] = (char)0x0D0E; break; // e  -> short e
                        case (char)0x0D13: buf[k - 4] = (char)0x0D12; break; // o  -> short o
                        case (char)0x0D47: buf[k - 4] = (char)0x0D46; break; // e sign -> short
                        case (char)0x0D4B: buf[k - 4] = (char)0x0D4A; break; // o sign -> short
                    }
                }
            }
            return new string(buf, 0, k);
        }

        // two capture groups, 1 is the vowel, 2 is the following double consonants
        public static string ConvertEOChars(Match m)
        {
            string? newVal = null;
            switch (m.Groups[1].Value)
            {
                case "\u0D0F":
                    newVal = "\u0D0E";
                    break;
                case "\u0D13":
                    newVal = "\u0D12";
                    break;
                case "\u0D47":
                    newVal = "\u0D46";
                    break;
                case "\u0D4B":
                    newVal = "\u0D4A";
                    break;
                default:
                    break;
            }

            return newVal + m.Groups[2].Value;
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
