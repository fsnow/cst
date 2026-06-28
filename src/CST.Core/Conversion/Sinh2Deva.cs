using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Sinh2Deva
    {
        private static IDictionary<char, object> sinh2Deva;

        // Fast reverse-lookup table for the optimized Convert(): map[c] = Deva char, '\0' = pass through. (#86)
        private const int MapLen = 0x0E00; // covers the Sinhala block (source chars)
        private static readonly char[] map = new char[MapLen];

        static Sinh2Deva()
        {
            sinh2Deva = new Dictionary<char, object>();

            sinh2Deva['\u0D82'] = '\u0902'; // niggahita

            // independent vowels
            sinh2Deva['\u0D85'] = '\u0905'; // a
            sinh2Deva['\u0D86'] = '\u0906'; // aa
            sinh2Deva['\u0D89'] = '\u0907'; // i
            sinh2Deva['\u0D8A'] = '\u0908'; // ii
            sinh2Deva['\u0D8B'] = '\u0909'; // u
            sinh2Deva['\u0D8C'] = '\u090A'; // uu
            sinh2Deva['\u0D91'] = '\u090F'; // e (short - but Pali only has one 'e')
            sinh2Deva['\u0D92'] = '\u090F'; // ē (long - map to same Deva 'e' since Pali has only one)
            sinh2Deva['\u0D94'] = '\u0913'; // o (short - but Pali only has one 'o')
            sinh2Deva['\u0D95'] = '\u0913'; // ō (long - map to same Deva 'o' since Pali has only one)

            // velar stops
            sinh2Deva['\u0D9A'] = '\u0915'; // ka
            sinh2Deva['\u0D9B'] = '\u0916'; // kha
            sinh2Deva['\u0D9C'] = '\u0917'; // ga
            sinh2Deva['\u0D9D'] = '\u0918'; // gha
            sinh2Deva['\u0D9E'] = '\u0919'; // n overdot a

            // palatal stops
            sinh2Deva['\u0DA0'] = '\u091A'; // ca
            sinh2Deva['\u0DA1'] = '\u091B'; // cha
            sinh2Deva['\u0DA2'] = '\u091C'; // ja
            sinh2Deva['\u0DA3'] = '\u091D'; // jha
            sinh2Deva['\u0DA4'] = '\u091E'; // n tilde a

            // retroflex stops
            sinh2Deva['\u0DA7'] = '\u091F'; // t underdot a
            sinh2Deva['\u0DA8'] = '\u0920'; // t underdot ha
            sinh2Deva['\u0DA9'] = '\u0921'; // d underdot a
            sinh2Deva['\u0DAA'] = '\u0922'; // d underdot ha
            sinh2Deva['\u0DAB'] = '\u0923'; // n underdot a

            // dental stops
            sinh2Deva['\u0DAD'] = '\u0924'; // ta
            sinh2Deva['\u0DAE'] = '\u0925'; // tha
            sinh2Deva['\u0DAF'] = '\u0926'; // da
            sinh2Deva['\u0DB0'] = '\u0927'; // dha
            sinh2Deva['\u0DB1'] = '\u0928'; // na

            // labial stops
            sinh2Deva['\u0DB4'] = '\u092A'; // pa
            sinh2Deva['\u0DB5'] = '\u092B'; // pha
            sinh2Deva['\u0DB6'] = '\u092C'; // ba
            sinh2Deva['\u0DB7'] = '\u092D'; // bha
            sinh2Deva['\u0DB8'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            sinh2Deva['\u0DBA'] = '\u092F'; // ya
            sinh2Deva['\u0DBB'] = '\u0930'; // ra
            sinh2Deva['\u0DBD'] = '\u0932'; // la
            sinh2Deva['\u0DC0'] = '\u0935'; // va
            sinh2Deva['\u0DC3'] = '\u0938'; // sa
            sinh2Deva['\u0DC4'] = '\u0939'; // ha
            sinh2Deva['\u0DC5'] = '\u0933'; // l underdot a

            // dependent vowel signs
            sinh2Deva['\u0DCF'] = '\u093E'; // aa
            sinh2Deva['\u0DD2'] = '\u093F'; // i
            sinh2Deva['\u0DD3'] = '\u0940'; // ii
            sinh2Deva['\u0DD4'] = '\u0941'; // u
            sinh2Deva['\u0DD6'] = '\u0942'; // uu
            sinh2Deva['\u0DD9'] = '\u0947'; // e (short - but Pali only has one 'e')
            sinh2Deva['\u0DDA'] = '\u0947'; // ē (long - map to same Deva 'e' since Pali has only one)
            sinh2Deva['\u0DDC'] = '\u094B'; // o (short - but Pali only has one 'o')
            sinh2Deva['\u0DDD'] = '\u094B'; // ō (long - map to same Deva 'o' since Pali has only one)

            // various signs
            sinh2Deva['\u0DCA'] = '\u094D'; // Sinhala virama -> Dev. virama

            // zero-width joiners
            sinh2Deva['\u200C'] = ""; // ZWNJ (remove)
            sinh2Deva['\u200D'] = ""; // ZWJ (remove)

            // Build the fast reverse table from the same data (ZWNJ/ZWJ "" entries are skipped). (#86)
            foreach (var kvp in sinh2Deva)
                if (kvp.Key < MapLen) map[kvp.Key] = kvp.Value is char ch ? ch : ((string)kvp.Value)[0];
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the ZWNJ/ZWJ
        // removal, the reverse dict map, and the 11 ZWJ-conjunct string.Replace passes into one scan, inserting
        // ZWJ (U+200D) into the registered C1+virama+C2 conjuncts against the buffer tail.
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
                if (c == 0x200C || c == 0x200D) continue; // removed in the reference dict pass
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
            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (sinh2Deva.ContainsKey(c))
                    sb.Append(sinh2Deva[c]);
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
