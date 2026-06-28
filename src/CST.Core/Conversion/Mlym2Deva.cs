using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Mlym2Deva
    {
        private static IDictionary<char, object> mlym2Deva;

        // Fast reverse-lookup table for the optimized Convert(): map[c] = Deva char, '\0' = pass through. (#86)
        private const int MapLen = 0x0D80; // covers the Malayalam block (source chars)
        private static readonly char[] map = new char[MapLen];

        static Mlym2Deva()
        {
            mlym2Deva = new Dictionary<char, object>();

            // various signs
            mlym2Deva['\u0D02'] = '\u0902'; // anusvara
            mlym2Deva['\u0D03'] = '\u0903'; // visarga

            // independent vowels
            mlym2Deva['\u0D05'] = '\u0905'; // a
            mlym2Deva['\u0D06'] = '\u0906'; // aa
            mlym2Deva['\u0D07'] = '\u0907'; // i
            mlym2Deva['\u0D08'] = '\u0908'; // ii
            mlym2Deva['\u0D09'] = '\u0909'; // u
            mlym2Deva['\u0D0A'] = '\u090A'; // uu
            mlym2Deva['\u0D0B'] = '\u090B'; // vocalic r
            mlym2Deva['\u0D0C'] = '\u090C'; // vocalic l
            mlym2Deva['\u0D0E'] = '\u090F'; // e -- both the long and short forms of Malayalam e map to Devanagari e
            mlym2Deva['\u0D0F'] = '\u090F'; // e
            mlym2Deva['\u0D10'] = '\u0910'; // ai
            mlym2Deva['\u0D12'] = '\u0913'; // o -- both the long and short forms of Malayalam o map to Devanagari o
            mlym2Deva['\u0D13'] = '\u0913'; // o
            mlym2Deva['\u0D14'] = '\u0914'; // au

            // velar stops
            mlym2Deva['\u0D15'] = '\u0915'; // ka
            mlym2Deva['\u0D16'] = '\u0916'; // kha
            mlym2Deva['\u0D17'] = '\u0917'; // ga
            mlym2Deva['\u0D18'] = '\u0918'; // gha
            mlym2Deva['\u0D19'] = '\u0919'; // n overdot a

            // palatal stops
            mlym2Deva['\u0D1A'] = '\u091A'; // ca
            mlym2Deva['\u0D1B'] = '\u091B'; // cha
            mlym2Deva['\u0D1C'] = '\u091C'; // ja
            mlym2Deva['\u0D1D'] = '\u091D'; // jha
            mlym2Deva['\u0D1E'] = '\u091E'; // n tilde a

            // retroflex stops
            mlym2Deva['\u0D1F'] = '\u091F'; // t underdot a
            mlym2Deva['\u0D20'] = '\u0920'; // t underdot ha
            mlym2Deva['\u0D21'] = '\u0921'; // d underdot a
            mlym2Deva['\u0D22'] = '\u0922'; // d underdot ha
            mlym2Deva['\u0D23'] = '\u0923'; // n underdot a

            // dental stops
            mlym2Deva['\u0D24'] = '\u0924'; // ta
            mlym2Deva['\u0D25'] = '\u0925'; // tha
            mlym2Deva['\u0D26'] = '\u0926'; // da
            mlym2Deva['\u0D27'] = '\u0927'; // dha
            mlym2Deva['\u0D28'] = '\u0928'; // na

            // labial stops
            mlym2Deva['\u0D2A'] = '\u092A'; // pa
            mlym2Deva['\u0D2B'] = '\u092B'; // pha
            mlym2Deva['\u0D2C'] = '\u092C'; // ba
            mlym2Deva['\u0D2D'] = '\u092D'; // bha
            mlym2Deva['\u0D2E'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            mlym2Deva['\u0D2F'] = '\u092F'; // ya
            mlym2Deva['\u0D30'] = '\u0930'; // ra
            mlym2Deva['\u0D31'] = '\u0931'; // rra (Dravidian-specific)
            mlym2Deva['\u0D32'] = '\u0932'; // la
            mlym2Deva['\u0D33'] = '\u0933'; // l underdot a
            mlym2Deva['\u0D35'] = '\u0935'; // va
            mlym2Deva['\u0D36'] = '\u0936'; // sha (palatal)
            mlym2Deva['\u0D37'] = '\u0937'; // sha (retroflex)
            mlym2Deva['\u0D38'] = '\u0938'; // sa
            mlym2Deva['\u0D39'] = '\u0939'; // ha

            // dependent vowel signs
            mlym2Deva['\u0D3E'] = '\u093E'; // aa
            mlym2Deva['\u0D3F'] = '\u093F'; // i
            mlym2Deva['\u0D40'] = '\u0940'; // ii
            mlym2Deva['\u0D41'] = '\u0941'; // u
            mlym2Deva['\u0D42'] = '\u0942'; // uu
            mlym2Deva['\u0D43'] = '\u0943'; // vocalic r
            mlym2Deva['\u0D46'] = '\u0947'; // e -- both the long and short forms of Malayalam e map to Devanagari e
            mlym2Deva['\u0D47'] = '\u0947'; // e
            mlym2Deva['\u0D48'] = '\u0948'; // ai
            mlym2Deva['\u0D4A'] = '\u094B'; // o -- both the long and short forms of Malayalam o map to Devanagari o
            mlym2Deva['\u0D4B'] = '\u094B'; // o
            mlym2Deva['\u0D4C'] = '\u094C'; // au

            // various signs
            mlym2Deva['\u0D4D'] = '\u094D'; // virama

            // additional vowels for Sanskrit
            mlym2Deva['\u0D60'] = '\u0960'; // vocalic rr
            mlym2Deva['\u0D61'] = '\u0961'; // vocalic ll

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // digits
            mlym2Deva['\u0D66'] = '\u0966';
            mlym2Deva['\u0D67'] = '\u0967';
            mlym2Deva['\u0D68'] = '\u0968';
            mlym2Deva['\u0D69'] = '\u0969';
            mlym2Deva['\u0D6A'] = '\u096A';
            mlym2Deva['\u0D6B'] = '\u096B';
            mlym2Deva['\u0D6C'] = '\u096C';
            mlym2Deva['\u0D6D'] = '\u096D';
            mlym2Deva['\u0D6E'] = '\u096E';
            mlym2Deva['\u0D6F'] = '\u096F';

            // Build the fast reverse table from the same data (#86).
            foreach (var kvp in mlym2Deva)
                if (kvp.Key < MapLen) map[kvp.Key] = kvp.Value is char ch ? ch : ((string)kvp.Value)[0];
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Folds the reverse
        // dict map AND the 11 ZWJ-conjunct string.Replace passes into one scan, inserting ZWJ (U+200D) into the
        // registered C1+virama+C2 conjuncts against the buffer tail as the second consonant is emitted.
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
                if (mlym2Deva.ContainsKey(c))
                    sb.Append(mlym2Deva[c]);
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
