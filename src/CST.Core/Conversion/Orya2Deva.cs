using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    /// <summary>
    /// Odia (Oriya) to Devanagari: the reverse of <see cref="Deva2Orya"/>. It also reads the two Odia letters
    /// that Deva2Orya does not write, so that Odia typed or pasted from elsewhere is understood: ya as U+0B2F
    /// (the letter Odia reads as ja) and va as U+0B35.
    /// </summary>
    public static class Orya2Deva
    {
        private static IDictionary<char, object> orya2Deva;

        // Fast reverse-lookup table for the optimized Convert(): map[c] = Deva char, '\0' = pass through. (#86)
        private const int MapLen = 0x0B80; // covers the Oriya block (source chars)
        private static readonly char[] map = new char[MapLen];

        static Orya2Deva()
        {
            orya2Deva = new Dictionary<char, object>();

            // various signs
            orya2Deva['\u0B01'] = '\u0901'; // candrabindu
            orya2Deva['\u0B02'] = '\u0902'; // niggahita

            // independent vowels
            orya2Deva['\u0B05'] = '\u0905'; // a
            orya2Deva['\u0B06'] = '\u0906'; // aa
            orya2Deva['\u0B07'] = '\u0907'; // i
            orya2Deva['\u0B08'] = '\u0908'; // ii
            orya2Deva['\u0B09'] = '\u0909'; // u
            orya2Deva['\u0B0A'] = '\u090A'; // uu
            orya2Deva['\u0B0B'] = '\u090B'; // vocalic r
            orya2Deva['\u0B0C'] = '\u090C'; // vocalic l
            orya2Deva['\u0B0F'] = '\u090F'; // e
            orya2Deva['\u0B10'] = '\u0910'; // ai
            orya2Deva['\u0B13'] = '\u0913'; // o
            orya2Deva['\u0B14'] = '\u0914'; // au

            // velar stops
            orya2Deva['\u0B15'] = '\u0915'; // ka
            orya2Deva['\u0B16'] = '\u0916'; // kha
            orya2Deva['\u0B17'] = '\u0917'; // ga
            orya2Deva['\u0B18'] = '\u0918'; // gha
            orya2Deva['\u0B19'] = '\u0919'; // n overdot a

            // palatal stops
            orya2Deva['\u0B1A'] = '\u091A'; // ca
            orya2Deva['\u0B1B'] = '\u091B'; // cha
            orya2Deva['\u0B1C'] = '\u091C'; // ja
            orya2Deva['\u0B1D'] = '\u091D'; // jha
            orya2Deva['\u0B1E'] = '\u091E'; // n tilde a

            // retroflex stops
            orya2Deva['\u0B1F'] = '\u091F'; // t underdot a
            orya2Deva['\u0B20'] = '\u0920'; // t underdot ha
            orya2Deva['\u0B21'] = '\u0921'; // d underdot a
            orya2Deva['\u0B22'] = '\u0922'; // d underdot ha
            orya2Deva['\u0B23'] = '\u0923'; // n underdot a

            // dental stops
            orya2Deva['\u0B24'] = '\u0924'; // ta
            orya2Deva['\u0B25'] = '\u0925'; // tha
            orya2Deva['\u0B26'] = '\u0926'; // da
            orya2Deva['\u0B27'] = '\u0927'; // dha
            orya2Deva['\u0B28'] = '\u0928'; // na

            // labial stops
            orya2Deva['\u0B2A'] = '\u092A'; // pa
            orya2Deva['\u0B2B'] = '\u092B'; // pha
            orya2Deva['\u0B2C'] = '\u092C'; // ba
            orya2Deva['\u0B2D'] = '\u092D'; // bha
            orya2Deva['\u0B2E'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            orya2Deva['\u0B5F'] = '\u092F'; // ya *** YYA, not U+0B2F, which is read as ja in Odia ***
            orya2Deva['\u0B30'] = '\u0930'; // ra
            orya2Deva['\u0B32'] = '\u0932'; // la
            orya2Deva['\u0B33'] = '\u0933'; // l underdot a
            orya2Deva['\u0B71'] = '\u0935'; // va *** WA, not U+0B35 ***
            orya2Deva['\u0B36'] = '\u0936'; // sha (palatal)
            orya2Deva['\u0B37'] = '\u0937'; // sha (retroflex)
            orya2Deva['\u0B38'] = '\u0938'; // sa
            orya2Deva['\u0B03'] = '\u0903'; // visarga (#1049)
            orya2Deva['\u0B39'] = '\u0939'; // ha

            // dependent vowel signs
            orya2Deva['\u0B3E'] = '\u093E'; // aa
            orya2Deva['\u0B3F'] = '\u093F'; // i
            orya2Deva['\u0B40'] = '\u0940'; // ii
            orya2Deva['\u0B41'] = '\u0941'; // u
            orya2Deva['\u0B42'] = '\u0942'; // uu
            orya2Deva['\u0B43'] = '\u0943'; // vocalic r
            orya2Deva['\u0B47'] = '\u0947'; // e
            orya2Deva['\u0B48'] = '\u0948'; // ai
            orya2Deva['\u0B4B'] = '\u094B'; // o
            orya2Deva['\u0B4C'] = '\u094C'; // au

            orya2Deva['\u0B4D'] = '\u094D'; // virama

            // numerals
            orya2Deva['\u0B66'] = '\u0966';
            orya2Deva['\u0B67'] = '\u0967';
            orya2Deva['\u0B68'] = '\u0968';
            orya2Deva['\u0B69'] = '\u0969';
            orya2Deva['\u0B6A'] = '\u096A';
            orya2Deva['\u0B6B'] = '\u096B';
            orya2Deva['\u0B6C'] = '\u096C';
            orya2Deva['\u0B6D'] = '\u096D';
            orya2Deva['\u0B6E'] = '\u096E';
            orya2Deva['\u0B6F'] = '\u096F';

            // Odia letters that Deva2Orya does not write, read as the same Devanagari letters
            orya2Deva['\u0B2F'] = '\u092F'; // ya (U+0B2F, YA)
            orya2Deva['\u0B35'] = '\u0935'; // va (U+0B35, VA)

            // we let dandas (U+0964) and double dandas (U+0965) pass through

            // Build the fast reverse table from the same data (#86).
            foreach (var kvp in orya2Deva)
                if (kvp.Key < MapLen) map[kvp.Key] = kvp.Value is char ch ? ch : ((string)kvp.Value)[0];
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
                if (orya2Deva.ContainsKey(c))
                    sb.Append(orya2Deva[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). One scan over a
        // char buffer using a reverse char[] table. Pure dictionary substitution - no positional rules.
        public static string Convert(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            int n = str.Length;
            var buf = new char[n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = str[i];
                char m = (c < MapLen) ? map[c] : '\0';
                buf[k++] = (m != '\0') ? m : c; // mapped Deva char, else pass through
            }
            return new string(buf, 0, k);
        }
    }
}
