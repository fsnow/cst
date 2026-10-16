using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    /// <summary>
    /// Devanagari to Odia (Oriya). The letter choices are those of Tipitaka.org's Deva2Orya
    /// (VipassanaTech/tipitaka-xml#2728), which follow the Odia transliteration of Abhiraj Bibhar
    /// (VipassanaTech/tipitaka-xml#2707): va is WA (U+0B71) and ya is YYA (U+0B5F), everywhere, including
    /// after a virama. The dandas and the abbreviation sign stay Devanagari: Odia has none of its own.
    /// The virama needs no special case, because Odia shows it when no consonant follows (#1048).
    /// </summary>
    public static class Deva2Orya
    {
        private static IDictionary<char, object> deva2Orya;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Orya()
        {
            deva2Orya = new Dictionary<char, object>();

            // various signs
            deva2Orya['\u0901'] = '\u0B01'; // candrabindu
            deva2Orya['\u0902'] = '\u0B02'; // niggahita

            // independent vowels
            deva2Orya['\u0905'] = '\u0B05'; // a
            deva2Orya['\u0906'] = '\u0B06'; // aa
            deva2Orya['\u0907'] = '\u0B07'; // i
            deva2Orya['\u0908'] = '\u0B08'; // ii
            deva2Orya['\u0909'] = '\u0B09'; // u
            deva2Orya['\u090A'] = '\u0B0A'; // uu
            deva2Orya['\u090B'] = '\u0B0B'; // vocalic r
            deva2Orya['\u090C'] = '\u0B0C'; // vocalic l
            deva2Orya['\u090F'] = '\u0B0F'; // e
            deva2Orya['\u0910'] = '\u0B10'; // ai
            deva2Orya['\u0913'] = '\u0B13'; // o
            deva2Orya['\u0914'] = '\u0B14'; // au

            // velar stops
            deva2Orya['\u0915'] = '\u0B15'; // ka
            deva2Orya['\u0916'] = '\u0B16'; // kha
            deva2Orya['\u0917'] = '\u0B17'; // ga
            deva2Orya['\u0918'] = '\u0B18'; // gha
            deva2Orya['\u0919'] = '\u0B19'; // n overdot a

            // palatal stops
            deva2Orya['\u091A'] = '\u0B1A'; // ca
            deva2Orya['\u091B'] = '\u0B1B'; // cha
            deva2Orya['\u091C'] = '\u0B1C'; // ja
            deva2Orya['\u091D'] = '\u0B1D'; // jha
            deva2Orya['\u091E'] = '\u0B1E'; // n tilde a

            // retroflex stops
            deva2Orya['\u091F'] = '\u0B1F'; // t underdot a
            deva2Orya['\u0920'] = '\u0B20'; // t underdot ha
            deva2Orya['\u0921'] = '\u0B21'; // d underdot a
            deva2Orya['\u0922'] = '\u0B22'; // d underdot ha
            deva2Orya['\u0923'] = '\u0B23'; // n underdot a

            // dental stops
            deva2Orya['\u0924'] = '\u0B24'; // ta
            deva2Orya['\u0925'] = '\u0B25'; // tha
            deva2Orya['\u0926'] = '\u0B26'; // da
            deva2Orya['\u0927'] = '\u0B27'; // dha
            deva2Orya['\u0928'] = '\u0B28'; // na

            // labial stops
            deva2Orya['\u092A'] = '\u0B2A'; // pa
            deva2Orya['\u092B'] = '\u0B2B'; // pha
            deva2Orya['\u092C'] = '\u0B2C'; // ba
            deva2Orya['\u092D'] = '\u0B2D'; // bha
            deva2Orya['\u092E'] = '\u0B2E'; // ma

            // liquids, fricatives, etc.
            deva2Orya['\u092F'] = '\u0B5F'; // ya *** YYA, not U+0B2F, which is read as ja in Odia ***
            deva2Orya['\u0930'] = '\u0B30'; // ra
            deva2Orya['\u0932'] = '\u0B32'; // la
            deva2Orya['\u0933'] = '\u0B33'; // l underdot a
            deva2Orya['\u0935'] = '\u0B71'; // va *** WA, not U+0B35 ***
            deva2Orya['\u0936'] = '\u0B36'; // sha (palatal)
            deva2Orya['\u0937'] = '\u0B37'; // sha (retroflex)
            deva2Orya['\u0938'] = '\u0B38'; // sa
            deva2Orya['\u0903'] = '\u0B03'; // visarga (#1049)
            deva2Orya['\u0939'] = '\u0B39'; // ha

            // dependent vowel signs
            deva2Orya['\u093E'] = '\u0B3E'; // aa
            deva2Orya['\u093F'] = '\u0B3F'; // i
            deva2Orya['\u0940'] = '\u0B40'; // ii
            deva2Orya['\u0941'] = '\u0B41'; // u
            deva2Orya['\u0942'] = '\u0B42'; // uu
            deva2Orya['\u0943'] = '\u0B43'; // vocalic r
            deva2Orya['\u0947'] = '\u0B47'; // e
            deva2Orya['\u0948'] = '\u0B48'; // ai
            deva2Orya['\u094B'] = '\u0B4B'; // o
            deva2Orya['\u094C'] = '\u0B4C'; // au

            deva2Orya['\u094D'] = '\u0B4D'; // virama

            // numerals
            deva2Orya['\u0966'] = '\u0B66';
            deva2Orya['\u0967'] = '\u0B67';
            deva2Orya['\u0968'] = '\u0B68';
            deva2Orya['\u0969'] = '\u0B69';
            deva2Orya['\u096A'] = '\u0B6A';
            deva2Orya['\u096B'] = '\u0B6B';
            deva2Orya['\u096C'] = '\u0B6C';
            deva2Orya['\u096D'] = '\u0B6D';
            deva2Orya['\u096E'] = '\u0B6E';
            deva2Orya['\u096F'] = '\u0B6F';

            // The ZWJ in some conjuncts is for Devanagari presentation only. [fsnow] "our addition
            // of 200D is for Devanagari presentation only so if he removed them for Odia then we will too."
            deva2Orya['\u200C'] = ""; // ZWNJ (remove)
            deva2Orya['\u200D'] = ""; // ZWJ (remove)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in deva2Orya)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // #42: one stylesheet serves every script (see Deva2Beng.ConvertBook).
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka.xsl");

            return Convert(devStr);
        }

        /// <summary>
        /// FROZEN reference implementation (the readable version, the same as Tipitaka.org's) - the correctness
        /// oracle for the optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the
        /// corpus. (#86)
        /// </summary>
        public static string ConvertReference(string devStr)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (deva2Orya.ContainsKey(c))
                    sb.Append(deva2Orya[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). One scan over a
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
