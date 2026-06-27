using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Mymr
    {
        private static IDictionary<char, object> deva2Mymr;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Mymr()
        {
            deva2Mymr = new Dictionary<char, object>();

            // velar stops
            deva2Mymr['\u0915'] = '\u1000'; // ka
            deva2Mymr['\u0916'] = '\u1001'; // kha
            deva2Mymr['\u0917'] = '\u1002'; // ga
            deva2Mymr['\u0918'] = '\u1003'; // gha
            deva2Mymr['\u0919'] = '\u1004'; // n overdot a
            
            // palatal stops
            deva2Mymr['\u091A'] = '\u1005'; // ca
            deva2Mymr['\u091B'] = '\u1006'; // cha
            deva2Mymr['\u091C'] = '\u1007'; // ja
            deva2Mymr['\u091D'] = '\u1008'; // jha
            deva2Mymr['\u091E'] = '\u1009'; // n tilde a

            // retroflex stops
            deva2Mymr['\u091F'] = '\u100B'; // t underdot a
            deva2Mymr['\u0920'] = '\u100C'; // t underdot ha
            deva2Mymr['\u0921'] = '\u100D'; // d underdot a
            deva2Mymr['\u0922'] = '\u100E'; // d underdot ha
            deva2Mymr['\u0923'] = '\u100F'; // n underdot a

            // dental stops
            deva2Mymr['\u0924'] = '\u1010'; // ta
            deva2Mymr['\u0925'] = '\u1011'; // tha
            deva2Mymr['\u0926'] = '\u1012'; // da
            deva2Mymr['\u0927'] = '\u1013'; // dha
            deva2Mymr['\u0928'] = '\u1014'; // na

            // labial stops
            deva2Mymr['\u092A'] = '\u1015'; // pa
            deva2Mymr['\u092B'] = '\u1016'; // pha
            deva2Mymr['\u092C'] = '\u1017'; // ba
            deva2Mymr['\u092D'] = '\u1018'; // bha
            deva2Mymr['\u092E'] = '\u1019'; // ma

            // liquids, fricatives, etc.
            deva2Mymr['\u092F'] = '\u101A'; // ya
            deva2Mymr['\u0930'] = '\u101B'; // ra
            deva2Mymr['\u0932'] = '\u101C'; // la
            deva2Mymr['\u0935'] = '\u101D'; // va
            deva2Mymr['\u0938'] = '\u101E'; // sa
            deva2Mymr['\u0939'] = '\u101F'; // ha
            deva2Mymr['\u0933'] = '\u1020'; // l underdot a

            // independent vowels
            deva2Mymr['\u0905'] = '\u1021'; // a
            deva2Mymr['\u0906'] = "\u1021\u102C"; // aa
            deva2Mymr['\u0907'] = '\u1023'; // i
            deva2Mymr['\u0908'] = '\u1024'; // ii
            deva2Mymr['\u0909'] = '\u1025'; // u
            deva2Mymr['\u090A'] = '\u1026'; // uu
            deva2Mymr['\u090F'] = '\u1027'; // e
            deva2Mymr['\u0913'] = '\u1029'; // o

            // dependent vowel signs
            deva2Mymr['\u093E'] = '\u102C'; // aa
            deva2Mymr['\u093F'] = '\u102D'; // i
            deva2Mymr['\u0940'] = '\u102E'; // ii
            deva2Mymr['\u0941'] = '\u102F'; // u
            deva2Mymr['\u0942'] = '\u1030'; // uu
            deva2Mymr['\u0947'] = '\u1031'; // e
            deva2Mymr['\u094B'] = "\u1031\u102C"; // o

            // numerals
            deva2Mymr['\u0966'] = '\u1040';
            deva2Mymr['\u0967'] = '\u1041';
            deva2Mymr['\u0968'] = '\u1042';
            deva2Mymr['\u0969'] = '\u1043';
            deva2Mymr['\u096A'] = '\u1044';
            deva2Mymr['\u096B'] = '\u1045';
            deva2Mymr['\u096C'] = '\u1046';
            deva2Mymr['\u096D'] = '\u1047';
            deva2Mymr['\u096E'] = '\u1048';
            deva2Mymr['\u096F'] = '\u1049';

            // other
            deva2Mymr['\u0902'] = '\u1036'; // niggahita
            deva2Mymr['\u094D'] = '\u1039'; // virama
            // we let dandas and double dandas pass through and handle
            // them in ConvertDandas()
            //deva2Mymr['\u0964'] = '\u104B';
            deva2Mymr['\u0970'] = '.'; // Devanagari abbreviation sign
            deva2Mymr['\u200C'] = ""; // ZWNJ (ignore)
            deva2Mymr['\u200D'] = ""; // ZWJ (ignore)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in deva2Mymr)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Myanmar
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-mymr.xsl");

            // convert to Myanmar style of peyyala: double line + pe + double line
            // (we do this here to remove the Dev. abbreviation sign, which would otherwise
            // be converted to period in Convert())
            devStr = devStr.Replace("\u2026\u092A\u094B\u0970\u2026", "\u104B\u1015\u1031\u104B");

            string str = Convert(devStr);

            str = ConvertDandas(str);
            return CleanupPunctuation(str);
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        public static string ConvertReference(string devStr)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (deva2Mymr.ContainsKey(c))
                    sb.Append(deva2Mymr[c]);
                else
                    sb.Append(c);
            }

            string mya = sb.ToString();

            // Replace n(tilde)a + virama + n(tilde)a with n(tilde)n(tilde)a character
            mya = mya.Replace("\u1009\u1039\u1009", "\u100A");

            // use dependent consonant signs ya, ra, wa and ha, if after virama
            mya = mya.Replace("\u1039\u101A", "\u103B");
            mya = mya.Replace("\u1039\u101B", "\u103C");
            mya = mya.Replace("\u1039\u101D", "\u103D");
            mya = mya.Replace("\u1039\u101F", "\u103E");

            // use tall aa after ddh, both in vowels o and aa
            mya = mya.Replace("\u1012\u1039\u1013\u1031\u102C", "\u1012\u1039\u1013\u1031\u102B");
            mya = mya.Replace("\u1012\u1039\u1013\u102C", "\u1012\u1039\u1013\u102B");

            // use tall aa after kh, both in vowels o and aa
            mya = mya.Replace("\u1001\u1031\u102C", "\u1001\u1031\u102B");
            mya = mya.Replace("\u1001\u102C", "\u1001\u102B");

            // use tall aa after g, both in vowels o and aa
            mya = mya.Replace("\u1002\u1031\u102C", "\u1002\u1031\u102B");
            mya = mya.Replace("\u1002\u102C", "\u1002\u102B");

            // use tall aa after p, both in vowels o and aa
            mya = mya.Replace("\u1015\u1031\u102C", "\u1015\u1031\u102B");
            mya = mya.Replace("\u1015\u102C", "\u1015\u102B");

            // use tall aa after v, both in vowels o and aa
            mya = mya.Replace("\u101D\u1031\u102C", "\u101D\u1031\u102B");
            mya = mya.Replace("\u101D\u102C", "\u101D\u102B");

            // use great sa for ssa
            mya = mya.Replace("\u101E\u1039\u101E", "\u103F");

            // fix the rendering of n overdot, adding asat / killer
            mya = mya.Replace("\u1004\u1039", "\u1004\u103A\u1039");

            return mya;
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        // Optimized single pass (#86): folds the char map AND all Myanmar ligature/vowel post-processing
        // (the sequential string.Replace passes in ConvertReference) into ONE traversal, via a small
        // output-buffer state machine. Byte-identical to ConvertReference (verified by tests).
        public static string Convert(string devStr)
        {
            if (string.IsNullOrEmpty(devStr))
                return devStr;

            int n = devStr.Length;
            var buf = new char[n * (maxValLen + 1) + 1]; // slack for inserted asat / killer
            int k = 0;

            for (int i = 0; i < n; i++)
            {
                char c = devStr[i];
                if (c == 0x200C || c == 0x200D) // ZWNJ / ZWJ -> removed (virama maps to a real Myanmar char)
                    continue;

                string? m = (c < MapLen) ? map[c] : null;
                if (m == null)
                {
                    k = Emit(buf, k, c);
                }
                else
                {
                    for (int j = 0; j < m.Length; j++)
                        k = Emit(buf, k, m[j]);
                }
            }

            // n-overdot + virama at end of text also gets the asat/killer (the global Replace matches at EOS)
            if (k >= 2 && buf[k - 1] == 0x1039 && buf[k - 2] == 0x1004)
            {
                buf[k] = (char)0x1039; buf[k - 1] = (char)0x103A; k++;
            }

            return new string(buf, 0, k);
        }

        // Append one Myanmar output char, applying the ligature/vowel rules against the buffer tail -
        // mirrors, position-by-position, the ordered Replace passes of ConvertReference. (#86)
        private static int Emit(char[] buf, int k, char x)
        {
            // n-overdot killer: a \u1004\u1039 not consumed by a following medial (ya/ra/wa/ha) gets \u103A
            if (k >= 2 && buf[k - 1] == 0x1039 && buf[k - 2] == 0x1004
                && x != 0x101A && x != 0x101B && x != 0x101D && x != 0x101F)
            {
                buf[k] = (char)0x1039; buf[k - 1] = (char)0x103A; k++; // insert asat before the virama
            }

            buf[k++] = x;

            if (x == 0x1009 && k >= 3 && buf[k - 2] == 0x1039 && buf[k - 3] == 0x1009)
            {
                buf[k - 3] = (char)0x100A; k -= 2; // n(tilde) + virama + n(tilde) -> single char
            }
            else if (x == 0x101A && k >= 2 && buf[k - 2] == 0x1039) { buf[k - 2] = (char)0x103B; k--; } // medial ya
            else if (x == 0x101B && k >= 2 && buf[k - 2] == 0x1039) { buf[k - 2] = (char)0x103C; k--; } // medial ra
            else if (x == 0x101D && k >= 2 && buf[k - 2] == 0x1039) { buf[k - 2] = (char)0x103D; k--; } // medial wa
            else if (x == 0x101F && k >= 2 && buf[k - 2] == 0x1039) { buf[k - 2] = (char)0x103E; k--; } // medial ha
            else if (x == 0x101E && k >= 3 && buf[k - 2] == 0x1039 && buf[k - 3] == 0x101E)
            {
                buf[k - 3] = (char)0x103F; k -= 2; // sa + virama + sa -> great sa
            }
            else if (x == 0x102C)
            {
                int pp = k - 2;
                if (pp >= 0 && buf[pp] == 0x1031) pp--; // skip the e vowel
                if (pp >= 0 && (buf[pp] == 0x1001 || buf[pp] == 0x1002 || buf[pp] == 0x1015 || buf[pp] == 0x101D
                               || (buf[pp] == 0x1013 && pp >= 2 && buf[pp - 1] == 0x1039 && buf[pp - 2] == 0x1012)))
                {
                    buf[k - 1] = (char)0x102B; // tall aa
                }
            }

            return k;
        }

        public static string ConvertDandas(string str)
        {
            // in gathas, single dandas convert to semicolon, double to period
            // Regex note: the +? is the lazy quantifier which finds the shortest match
            str = Regex.Replace(str, "<p rend=\"gatha[a-z0-9]*\".+?</p>",
                new MatchEvaluator(ConvertGathaDandas), RegexOptions.Compiled);

            // convert all others to double line
            str = str.Replace("\u0964", "\u104B");
            str = str.Replace("\u0965", "\u104B");

            // convert commas to single line
            str = str.Replace(",", "\u104A");

            // convert ellipsis to double line
            str = str.Replace("\u2026", "\u104B");

            return str;
        }

        public static string ConvertGathaDandas(Match m)
        {
            string str = m.Value;
            str = str.Replace(",", "\u104A"); // comma -> single line
            str = str.Replace("\u0964", "\u104B"); // danda -> double line
            str = str.Replace("\u0965", "\u104B"); // double danda -> double line
            return str;
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
