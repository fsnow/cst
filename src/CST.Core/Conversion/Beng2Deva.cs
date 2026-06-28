using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Beng2Deva
    {
        private static IDictionary<char, object> beng2Deva;

        // Fast reverse-lookup table for the optimized Convert(): map[c] = Deva char, '\0' = pass through. (#86)
        private const int MapLen = 0x0A00; // covers the Bengali block (source chars)
        private static readonly char[] map = new char[MapLen];

        static Beng2Deva()
        {
            beng2Deva = new Dictionary<char, object>();

            beng2Deva['\u0982'] = '\u0902'; // niggahita

            // independent vowels
            beng2Deva['\u0985'] = '\u0905'; // a
            beng2Deva['\u0986'] = '\u0906'; // aa
            beng2Deva['\u0987'] = '\u0907'; // i
            beng2Deva['\u0988'] = '\u0908'; // ii
            beng2Deva['\u0989'] = '\u0909'; // u
            beng2Deva['\u098A'] = '\u090A'; // uu
            beng2Deva['\u098B'] = '\u090B'; // vocalic r
            beng2Deva['\u098C'] = '\u090C'; // vocalic l
            beng2Deva['\u098F'] = '\u090F'; // e
            beng2Deva['\u0990'] = '\u0910'; // ai
            beng2Deva['\u0993'] = '\u0913'; // o
            beng2Deva['\u0994'] = '\u0914'; // au

            // velar stops
            beng2Deva['\u0995'] = '\u0915'; // ka
            beng2Deva['\u0996'] = '\u0916'; // kha
            beng2Deva['\u0997'] = '\u0917'; // ga
            beng2Deva['\u0998'] = '\u0918'; // gha
            beng2Deva['\u0999'] = '\u0919'; // n overdot a

            // palatal stops
            beng2Deva['\u099A'] = '\u091A'; // ca
            beng2Deva['\u099B'] = '\u091B'; // cha
            beng2Deva['\u099C'] = '\u091C'; // ja
            beng2Deva['\u099D'] = '\u091D'; // jha
            beng2Deva['\u099E'] = '\u091E'; // n tilde a

            // retroflex stops
            beng2Deva['\u099F'] = '\u091F'; // t underdot a
            beng2Deva['\u09A0'] = '\u0920'; // t underdot ha
            beng2Deva['\u09A1'] = '\u0921'; // d underdot a
            beng2Deva['\u09A2'] = '\u0922'; // d underdot ha
            beng2Deva['\u09A3'] = '\u0923'; // n underdot a

            // dental stops
            beng2Deva['\u09A4'] = '\u0924'; // ta
            beng2Deva['\u09A5'] = '\u0925'; // tha
            beng2Deva['\u09A6'] = '\u0926'; // da
            beng2Deva['\u09A7'] = '\u0927'; // dha
            beng2Deva['\u09A8'] = '\u0928'; // na

            // labial stops
            beng2Deva['\u09AA'] = '\u092A'; // pa
            beng2Deva['\u09AB'] = '\u092B'; // pha
            beng2Deva['\u09AC'] = '\u092C'; // ba
            beng2Deva['\u09AD'] = '\u092D'; // bha
            beng2Deva['\u09AE'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            beng2Deva['\u09AF'] = '\u092F'; // ya
            beng2Deva['\u09B0'] = '\u0930'; // ra
            beng2Deva['\u09B2'] = '\u0932'; // la

            // do the la with a String.Replace before the character replacement loop
            //beng2Deva["\u09B2\u09BC"] = '\u0933'; // l underdot a *** la with dot, there's no l underdot in Bengali***
            
            beng2Deva['\u09F0'] = '\u0935'; // va *** Bengali ra with middle diagonal. Used for Assamese. ***
            beng2Deva['\u09B6'] = '\u0936'; // sha (palatal)
            beng2Deva['\u09B7'] = '\u0937'; // sha (retroflex)
            beng2Deva['\u09B8'] = '\u0938'; // sa
            beng2Deva['\u09B9'] = '\u0939'; // ha

            // dependent vowel signs
            beng2Deva['\u09BE'] = '\u093E'; // aa
            beng2Deva['\u09BF'] = '\u093F'; // i
            beng2Deva['\u09C0'] = '\u0940'; // ii
            beng2Deva['\u09C1'] = '\u0941'; // u
            beng2Deva['\u09C2'] = '\u0942'; // uu
            beng2Deva['\u09C3'] = '\u0943'; // vocalic r
            beng2Deva['\u09C7'] = '\u0947'; // e
            beng2Deva['\u09C8'] = '\u0948'; // ai
            beng2Deva['\u09CB'] = '\u094B'; // o
            beng2Deva['\u09CC'] = '\u094C'; // au

            beng2Deva['\u09CD'] = '\u094D'; // virama

            // numerals
            beng2Deva['\u09E6'] = '\u0966';
            beng2Deva['\u09E7'] = '\u0967';
            beng2Deva['\u09E8'] = '\u0968';
            beng2Deva['\u09E9'] = '\u0969';
            beng2Deva['\u09EA'] = '\u096A';
            beng2Deva['\u09EB'] = '\u096B';
            beng2Deva['\u09EC'] = '\u096C';
            beng2Deva['\u09ED'] = '\u096D';
            beng2Deva['\u09EE'] = '\u096E';
            beng2Deva['\u09EF'] = '\u096F';

            // Build the fast reverse table from the same data (#86).
            foreach (var kvp in beng2Deva)
                if (kvp.Key < MapLen) map[kvp.Key] = kvp.Value is char ch ? ch : ((string)kvp.Value)[0];
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the la+nukta
        // pre-Replace, the reverse dict map, and the 11 ZWJ-conjunct string.Replace passes into one scan.
        public static string Convert(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            int n = str.Length;
            var buf = new char[n * 2]; // each input char -> 1 Deva char plus at most one inserted ZWJ
            int k = 0;
            int prevC2Index = -1; char prevC1 = '\0', prevC2 = '\0'; // last inserted conjunct, for non-overlap
            for (int i = 0; i < n; i++)
            {
                char c = str[i];
                // la (U+09B2) + nukta (U+09BC) -> Deva l-underdot (U+0933), bypassing the dict (pre-Replace)
                if (c == 0x09B2 && i + 1 < n && str[i + 1] == 0x09BC)
                {
                    k = EmitDeva(buf, k, (char)0x0933, ref prevC2Index, ref prevC1, ref prevC2);
                    i++; // consume the nukta
                    continue;
                }
                char m = (c < MapLen) ? map[c] : '\0';
                k = EmitDeva(buf, k, (m != '\0') ? m : c, ref prevC2Index, ref prevC1, ref prevC2);
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
            // la with dot
            str = str.Replace("\u09B2\u09BC", "\u0933");

            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (beng2Deva.ContainsKey(c))
                    sb.Append(beng2Deva[c]);
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
			str = str.Replace("\u091E\u094D\u091A", "\u091E\u094D\u200D\u091A"); // n(tilde)a + ca
			str = str.Replace("\u091E\u094D\u091C", "\u091E\u094D\u200D\u091C"); // n(tilde)a + ja
			str = str.Replace("\u091E\u094D\u091E", "\u091E\u094D\u200D\u091E"); // n(tilde)a + n(tilde)a
			str = str.Replace("\u0928\u094D\u0928", "\u0928\u094D\u200D\u0928"); // na + na
			str = str.Replace("\u092A\u094D\u0932", "\u092A\u094D\u200D\u0932"); // pa + la
			str = str.Replace("\u0932\u094D\u0932", "\u0932\u094D\u200D\u0932"); // la + la

			return str;
        }
    }
}
