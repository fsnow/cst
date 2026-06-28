using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CST.Conversion
{
    public static class Deva2Guru
    {
        private static IDictionary<char, object> deva2Guru;

        // Fast lookup for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x0980;
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Deva2Guru()
        {
            deva2Guru = new Dictionary<char, object>();

            // various signs
            deva2Guru['\u0901'] = '\u0A01'; // candrabindhu
            deva2Guru['\u0902'] = '\u0A02'; // anusvara
            deva2Guru['\u0903'] = '\u0A03'; // visarga

            // independent vowels
            deva2Guru['\u0905'] = '\u0A05'; // a
            deva2Guru['\u0906'] = '\u0A06'; // aa
            deva2Guru['\u0907'] = '\u0A07'; // i
            deva2Guru['\u0908'] = '\u0A08'; // ii
            deva2Guru['\u0909'] = '\u0A09'; // u
            deva2Guru['\u090A'] = '\u0A0A'; // uu
            deva2Guru['\u090F'] = '\u0A0F'; // e
            deva2Guru['\u0910'] = '\u0A10'; // ai
            deva2Guru['\u0913'] = '\u0A13'; // o
            deva2Guru['\u0914'] = '\u0A14'; // au

            // velar stops
            deva2Guru['\u0915'] = '\u0A15'; // ka
            deva2Guru['\u0916'] = '\u0A16'; // kha
            deva2Guru['\u0917'] = '\u0A17'; // ga
            deva2Guru['\u0918'] = '\u0A18'; // gha
            deva2Guru['\u0919'] = '\u0A19'; // n overdot a
            
            // palatal stops
            deva2Guru['\u091A'] = '\u0A1A'; // ca
            deva2Guru['\u091B'] = '\u0A1B'; // cha
            deva2Guru['\u091C'] = '\u0A1C'; // ja
            deva2Guru['\u091D'] = '\u0A1D'; // jha
            deva2Guru['\u091E'] = '\u0A1E'; // n tilde a

            // retroflex stops
            deva2Guru['\u091F'] = '\u0A1F'; // t underdot a
            deva2Guru['\u0920'] = '\u0A20'; // t underdot ha
            deva2Guru['\u0921'] = '\u0A21'; // d underdot a
            deva2Guru['\u0922'] = '\u0A22'; // d underdot ha
            deva2Guru['\u0923'] = '\u0A23'; // n underdot a

            // dental stops
            deva2Guru['\u0924'] = '\u0A24'; // ta
            deva2Guru['\u0925'] = '\u0A25'; // tha
            deva2Guru['\u0926'] = '\u0A26'; // da
            deva2Guru['\u0927'] = '\u0A27'; // dha
            deva2Guru['\u0928'] = '\u0A28'; // na

            // labial stops
            deva2Guru['\u092A'] = '\u0A2A'; // pa
            deva2Guru['\u092B'] = '\u0A2B'; // pha
            deva2Guru['\u092C'] = '\u0A2C'; // ba
            deva2Guru['\u092D'] = '\u0A2D'; // bha
            deva2Guru['\u092E'] = '\u0A2E'; // ma

            // liquids, fricatives, etc.
            deva2Guru['\u092F'] = '\u0A2F'; // ya
            deva2Guru['\u0930'] = '\u0A30'; // ra
            deva2Guru['\u0932'] = '\u0A32'; // la
            deva2Guru['\u0933'] = '\u0A33'; // l underdot a
            deva2Guru['\u0935'] = '\u0AB5'; // va
            deva2Guru['\u0936'] = '\u0A36'; // sha (palatal)
            deva2Guru['\u0938'] = '\u0A38'; // sa
            deva2Guru['\u0939'] = '\u0A39'; // ha

            // dependent vowel signs
            deva2Guru['\u093E'] = '\u0A3E'; // aa
            deva2Guru['\u093F'] = '\u0A3F'; // i
            deva2Guru['\u0940'] = '\u0A40'; // ii
            deva2Guru['\u0941'] = '\u0A41'; // u
            deva2Guru['\u0942'] = '\u0A42'; // uu
            deva2Guru['\u0947'] = '\u0A47'; // e
            deva2Guru['\u0948'] = '\u0A48'; // ai
            deva2Guru['\u094B'] = '\u0A4B'; // o
            deva2Guru['\u094C'] = '\u0A4C'; // au

            // various signs
            deva2Guru['\u094D'] = '\u0A4D'; // virama

            // let Devanagari danda (U+0964) and double danda (U+0965) 
            // pass through unmodified

            // digits
            deva2Guru['\u0966'] = '\u0A66';
            deva2Guru['\u0967'] = '\u0A67';
            deva2Guru['\u0968'] = '\u0A68';
            deva2Guru['\u0969'] = '\u0A69';
            deva2Guru['\u096A'] = '\u0A6A';
            deva2Guru['\u096B'] = '\u0A6B';
            deva2Guru['\u096C'] = '\u0A6C';
            deva2Guru['\u096D'] = '\u0A6D';
            deva2Guru['\u096E'] = '\u0A6E';
            deva2Guru['\u096F'] = '\u0A6F';

            // zero-width joiners
            deva2Guru['\u200C'] = ""; // ZWNJ (remove)
            deva2Guru['\u200D'] = ""; // ZWJ (remove)

            // Build the fast lookup table from the same data (#86).
            foreach (var kvp in deva2Guru)
            {
                if (kvp.Key >= MapLen) continue;
                string v = kvp.Value is char ch ? ch.ToString() : (string)kvp.Value;
                if (v.Length > 0) { map[kvp.Key] = v; if (v.Length > maxValLen) maxValLen = v.Length; }
            }
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Gurmukhi
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-guru.xsl");

            return Convert(devStr);
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
                if (deva2Guru.ContainsKey(c))
                    sb.Append(deva2Guru[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Replaces the
        // per-char dictionary lookup (ContainsKey + indexer, plus Append(object) boxing) with one scan over a
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
