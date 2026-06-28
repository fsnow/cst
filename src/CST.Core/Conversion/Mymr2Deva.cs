using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CST.Conversion
{
    public static class Mymr2Deva
    {
        private static IDictionary<char, object> mymr2Deva;

        // Fast reverse-lookup table for the optimized Convert(): map[c] = Deva string, null = pass through,
        // "" = removed (the Myanmar asat U+103A). (#86)
        private const int MapLen = 0x1080; // covers the Myanmar block (source chars)
        private static readonly string[] map = new string[MapLen];

        static Mymr2Deva()
        {
            mymr2Deva = new Dictionary<char, object>();

            // velar stops
            mymr2Deva['\u1000'] = '\u0915'; // ka
            mymr2Deva['\u1001'] = '\u0916'; // kha
            mymr2Deva['\u1002'] = '\u0917'; // ga
            mymr2Deva['\u1003'] = '\u0918'; // gha
            mymr2Deva['\u1004'] = '\u0919'; // n overdot a
            
            // palatal stops
            mymr2Deva['\u1005'] = '\u091A'; // ca
            mymr2Deva['\u1006'] = '\u091B'; // cha
            mymr2Deva['\u1007'] = '\u091C'; // ja
            mymr2Deva['\u1008'] = '\u091D'; // jha
            mymr2Deva['\u1009'] = '\u091E'; // n tilde a
            mymr2Deva['\u100A'] = "\u091E\u094D\u091E"; // double n tilde a

            // retroflex stops
            mymr2Deva['\u100B'] = '\u091F'; // t underdot a
            mymr2Deva['\u100C'] = '\u0920'; // t underdot ha
            mymr2Deva['\u100D'] = '\u0921'; // d underdot a
            mymr2Deva['\u100E'] = '\u0922'; // d underdot ha
            mymr2Deva['\u100F'] = '\u0923'; // n underdot a

            // dental stops
            mymr2Deva['\u1010'] = '\u0924'; // ta
            mymr2Deva['\u1011'] = '\u0925'; // tha
            mymr2Deva['\u1012'] = '\u0926'; // da
            mymr2Deva['\u1013'] = '\u0927'; // dha
            mymr2Deva['\u1014'] = '\u0928'; // na

            // labial stops
            mymr2Deva['\u1015'] = '\u092A'; // pa
            mymr2Deva['\u1016'] = '\u092B'; // pha
            mymr2Deva['\u1017'] = '\u092C'; // ba
            mymr2Deva['\u1018'] = '\u092D'; // bha
            mymr2Deva['\u1019'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            mymr2Deva['\u101A'] = '\u092F'; // ya
            mymr2Deva['\u101B'] = '\u0930'; // ra
            mymr2Deva['\u101C'] = '\u0932'; // la
            mymr2Deva['\u101D'] = '\u0935'; // va
            mymr2Deva['\u101E'] = '\u0938'; // sa
            mymr2Deva['\u101F'] = '\u0939'; // ha
            mymr2Deva['\u1020'] = '\u0933'; // l underdot a

            // independent vowels
            mymr2Deva['\u1021'] = '\u0905'; // a
            //deva2Mymr['\u0906'] = "\u1021\u102C"; // independent aa handled by regex in Convert()
            mymr2Deva['\u1023'] = '\u0907'; // i
            mymr2Deva['\u1024'] = '\u0908'; // ii
            mymr2Deva['\u1025'] = '\u0909'; // u
            mymr2Deva['\u1026'] = '\u090A'; // uu
            mymr2Deva['\u1027'] = '\u090F'; // e
            mymr2Deva['\u1029'] = '\u0913'; // o

            // dependent vowel signs
            mymr2Deva['\u102C'] = '\u093E'; // aa
            mymr2Deva['\u102D'] = '\u093F'; // i
            mymr2Deva['\u102E'] = '\u0940'; // ii
            mymr2Deva['\u102F'] = '\u0941'; // u
            mymr2Deva['\u1030'] = '\u0942'; // uu
            mymr2Deva['\u1031'] = '\u0947'; // e
            //deva2Mymr['\u094B'] = "\u1031\u102C"; // dependent o handled by regex in Convert()

            // remove asat/killer, used for rendering in Myanmar like ZWJ/ZWNJ in Deva
            mymr2Deva['\u103A'] = "";

            // replace the dependent consonant signs ya, ra, wa and ha (with no preceding virama) with virama + deva letter
            mymr2Deva['\u103B'] = "\u094D\u092F";
            mymr2Deva['\u103C'] = "\u094D\u0930";
            mymr2Deva['\u103D'] = "\u094D\u0935";
            mymr2Deva['\u103E'] = "\u094D\u0939";

            // Myanmar great sa becomes Deva sa + virama + sa
            mymr2Deva['\u103F'] = "\u0938\u094D\u0938";

            // numerals
            mymr2Deva['\u1040'] = '\u0966';
            mymr2Deva['\u1041'] = '\u0967';
            mymr2Deva['\u1042'] = '\u0968';
            mymr2Deva['\u1043'] = '\u0969';
            mymr2Deva['\u1044'] = '\u096A';
            mymr2Deva['\u1045'] = '\u096B';
            mymr2Deva['\u1046'] = '\u096C';
            mymr2Deva['\u1047'] = '\u096D';
            mymr2Deva['\u1048'] = '\u096E';
            mymr2Deva['\u1049'] = '\u096F';

            // other
            mymr2Deva['\u104A'] = '\u0964'; // danda
            mymr2Deva['\u1036'] = '\u0902'; // niggahita
            mymr2Deva['\u1039'] = '\u094D'; // virama
            //deva2Mymr['\u200C'] = ""; // ZWNJ (ignore)
            //deva2Mymr['\u200D'] = ""; // ZWJ (ignore)

            // Build the fast reverse table from the same data (#86).
            foreach (var kvp in mymr2Deva)
            {
                if (kvp.Key >= MapLen) continue;
                map[kvp.Key] = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
            }
            // tall aa (U+102B) is normalized to sign aa (U+102C) by the reference's first Replace, so it maps
            // the same way when it is not consumed by the 1021/1031 + aa combines handled in Convert. (#86)
            map[0x102B] = map[0x102C];
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the three
        // pre-Replace normalizations (tall-aa, independent a+aa, dependent e+aa), the reverse dict map, and the
        // 11 ZWJ-conjunct Replace passes into one scan.
        public static string Convert(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            int n = str.Length;
            var buf = new char[n * 4]; // value up to 3 chars + a possible inserted ZWJ
            int k = 0;
            int prevC2Index = -1; char prevC1 = '\0', prevC2 = '\0'; // last inserted conjunct, for non-overlap
            for (int i = 0; i < n; i++)
            {
                char c = str[i];
                char next = (i + 1 < n) ? str[i + 1] : '\0';
                bool nextIsAa = (next == 0x102C || next == 0x102B); // U+102B normalizes to U+102C first

                // independent a (U+1021) + aa -> independent aa (U+0906); dependent e (U+1031) + aa -> o (U+094B)
                if (c == 0x1021 && nextIsAa)
                {
                    k = EmitDeva(buf, k, (char)0x0906, ref prevC2Index, ref prevC1, ref prevC2);
                    i++; continue;
                }
                if (c == 0x1031 && nextIsAa)
                {
                    k = EmitDeva(buf, k, (char)0x094B, ref prevC2Index, ref prevC1, ref prevC2);
                    i++; continue;
                }

                string? m = (c < MapLen) ? map[c] : null;
                if (m == null) k = EmitDeva(buf, k, c, ref prevC2Index, ref prevC1, ref prevC2);
                else for (int j = 0; j < m.Length; j++) k = EmitDeva(buf, k, m[j], ref prevC2Index, ref prevC1, ref prevC2);
            }
            return new string(buf, 0, k);
        }

        // Append one Deva char, inserting ZWJ into the registered conjuncts: C1 + virama + C2 -> C1 + virama
        // + ZWJ + C2 (mirrors the reference's ordered ZWJ-conjunct Replace passes). (#86)
        private static int EmitDeva(char[] buf, int k, char x, ref int prevC2Index, ref char prevC1, ref char prevC2)
        {
            buf[k++] = x;
            if (k >= 3 && buf[k - 2] == 0x094D)
            {
                char c1 = buf[k - 3];
                if (IsZwjConjunct(c1, x))
                {
                    // Each conjunct is one ordered, non-overlapping string.Replace in the reference. Skip only
                    // when this match overlaps the previously inserted one AND is the SAME pair (so it belongs
                    // to the same Replace call, which would not match overlapping text). Different pairs are
                    // independent Replace calls and both fire.
                    bool samePairOverlap = (k - 3 == prevC2Index) && c1 == prevC1 && x == prevC2;
                    if (!samePairOverlap)
                    {
                        buf[k - 1] = (char)0x200D;
                        buf[k++] = x;
                        prevC2Index = k - 1; prevC1 = c1; prevC2 = x;
                    }
                }
            }
            return k;
        }

        // The 11 Devanagari conjuncts that take a ZWJ between virama and the second consonant. (#86)
        private static bool IsZwjConjunct(char c1, char c2)
        {
            switch (c1)
            {
                case (char)0x0915: return c2 == 0x0915 || c2 == 0x0932 || c2 == 0x0935; // ka + ka/la/va
                case (char)0x091A: return c2 == 0x091A;                                 // ca + ca
                case (char)0x091C: return c2 == 0x091C;                                 // ja + ja
                case (char)0x091E: return c2 == 0x091A || c2 == 0x091C || c2 == 0x091E; // nya + ca/ja/nya
                case (char)0x0928: return c2 == 0x0928;                                 // na + na
                case (char)0x092A: return c2 == 0x0932;                                 // pa + la
                case (char)0x0932: return c2 == 0x0932;                                 // la + la
                default: return false;
            }
        }

        /// <summary>
        /// FROZEN reference implementation (the original readable version) - the correctness oracle for the
        /// optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string str)
        {
            // replace all sign tall aa with sign aa
            str = str.Replace("\u102B", "\u102C");

            // independent "a" plus dependent sign aa = independent aa
			str = str.Replace("\u1021\u102C", "\u0906");

            // dependent e plus dependent sign aa = dependent o
			str = str.Replace("\u1031\u102C", "\u094B");

            StringBuilder sb = new StringBuilder();
			foreach (char c in str.ToCharArray())
            {
                if (mymr2Deva.ContainsKey(c))
                    sb.Append(mymr2Deva[c]);
                else
                    sb.Append(c);
            }

			str = sb.ToString();

			// insert ZWJ in some Devanagari conjuncts
			str = str.Replace("\u0915\u094D\u0915", "\u0915\u094D\u200D\u0915"); // ka + ka
			str = str.Replace("\u0915\u094D\u0932", "\u0915\u094D\u200D\u0932"); // ka + la
			str = str.Replace("\u0915\u094D\u0935", "\u0915\u094D\u200D\u0935"); // ka + va
			str = str.Replace("\u091A\u094D\u091A", "\u091A\u094D\u200D\u091A"); // ca + ca
			str = str.Replace("\u091C\u094D\u091C", "\u091C\u094D\u200D\u091C"); // ja + ja
			str = str.Replace("\u091E\u094D\u091A", "\u091E\u094D\u200D\u091A"); // �a + ca
			str = str.Replace("\u091E\u094D\u091C", "\u091E\u094D\u200D\u091C"); // �a + ja
			str = str.Replace("\u091E\u094D\u091E", "\u091E\u094D\u200D\u091E"); // �a + �a
			str = str.Replace("\u0928\u094D\u0928", "\u0928\u094D\u200D\u0928"); // na + na
			str = str.Replace("\u092A\u094D\u0932", "\u092A\u094D\u200D\u0932"); // pa + la
			str = str.Replace("\u0932\u094D\u0932", "\u0932\u094D\u200D\u0932"); // la + la

			return str;  
        }
    }
}
