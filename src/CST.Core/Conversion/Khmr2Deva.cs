using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Khmr2Deva
    {
        private static IDictionary<string, string> khmer2Dev;
        private static IDictionary<char, string> khmerChar2Dev;

        // Fast reverse table for the optimized Convert(): charMap[c] = Deva char, '\0' = pass through. The only
        // multi-char rule (independent a U+17A2 + sign aa U+17B6 -> independent aa) is handled inline. (#86)
        private const int MapLen = 0x1800; // covers the Khmer block (source chars)
        private static readonly char[] charMap = new char[MapLen];

        static Khmr2Deva()
        {
            khmer2Dev = new Dictionary<string, string>();
            khmerChar2Dev = new Dictionary<char, string>();

            // niggahita
            khmerChar2Dev['\u17C6'] = "\u0902";

            // independent vowels (reverse of Deva2Khmr multi-char mappings)
            khmer2Dev["\u17A2"] = "\u0905"; // a
            khmer2Dev["\u17A2\u17B6"] = "\u0906"; // aa (two-char sequence)
            khmerChar2Dev['\u17A5'] = "\u0907"; // i
            khmerChar2Dev['\u17A6'] = "\u0908"; // ii
            khmerChar2Dev['\u17A7'] = "\u0909"; // u
            khmerChar2Dev['\u17A9'] = "\u090A"; // uu
            khmerChar2Dev['\u17AF'] = "\u090F"; // e
            khmerChar2Dev['\u17B1'] = "\u0913"; // o

            // velar stops
            khmerChar2Dev['\u1780'] = "\u0915"; // ka
            khmerChar2Dev['\u1781'] = "\u0916"; // kha
            khmerChar2Dev['\u1782'] = "\u0917"; // ga
            khmerChar2Dev['\u1783'] = "\u0918"; // gha
            khmerChar2Dev['\u1784'] = "\u0919"; // n overdot a

            // palatal stops
            khmerChar2Dev['\u1785'] = "\u091A"; // ca
            khmerChar2Dev['\u1786'] = "\u091B"; // cha
            khmerChar2Dev['\u1787'] = "\u091C"; // ja
            khmerChar2Dev['\u1788'] = "\u091D"; // jha
            khmerChar2Dev['\u1789'] = "\u091E"; // n tilde a

            // retroflex stops
            khmerChar2Dev['\u178A'] = "\u091F"; // t underdot a
            khmerChar2Dev['\u178B'] = "\u0920"; // t underdot ha
            khmerChar2Dev['\u178C'] = "\u0921"; // d underdot a
            khmerChar2Dev['\u178D'] = "\u0922"; // d underdot ha
            khmerChar2Dev['\u178E'] = "\u0923"; // n underdot a

            // dental stops
            khmerChar2Dev['\u178F'] = "\u0924"; // ta
            khmerChar2Dev['\u1790'] = "\u0925"; // tha
            khmerChar2Dev['\u1791'] = "\u0926"; // da
            khmerChar2Dev['\u1792'] = "\u0927"; // dha
            khmerChar2Dev['\u1793'] = "\u0928"; // na

            // labial stops
            khmerChar2Dev['\u1794'] = "\u092A"; // pa
            khmerChar2Dev['\u1795'] = "\u092B"; // pha
            khmerChar2Dev['\u1796'] = "\u092C"; // ba
            khmerChar2Dev['\u1797'] = "\u092D"; // bha
            khmerChar2Dev['\u1798'] = "\u092E"; // ma

            // liquids, fricatives, etc.
            khmerChar2Dev['\u1799'] = "\u092F"; // ya
            khmerChar2Dev['\u179A'] = "\u0930"; // ra
            khmerChar2Dev['\u179B'] = "\u0932"; // la
            khmerChar2Dev['\u179C'] = "\u0935"; // va
            khmerChar2Dev['\u179F'] = "\u0938"; // sa
            khmerChar2Dev['\u17A0'] = "\u0939"; // ha
            khmerChar2Dev['\u17A1'] = "\u0933"; // l underdot a

            // dependent vowel signs
            khmerChar2Dev['\u17B6'] = "\u093E"; // aa
            khmerChar2Dev['\u17B7'] = "\u093F"; // i
            khmerChar2Dev['\u17B8'] = "\u0940"; // ii
            khmerChar2Dev['\u17BB'] = "\u0941"; // u
            khmerChar2Dev['\u17BC'] = "\u0942"; // uu
            khmerChar2Dev['\u17C1'] = "\u0947"; // e
            khmerChar2Dev['\u17C4'] = "\u094B"; // o

            khmerChar2Dev['\u17D2'] = "\u094D"; // virama

            // numerals
            khmerChar2Dev['\u17E0'] = "\u0966";
            khmerChar2Dev['\u17E1'] = "\u0967";
            khmerChar2Dev['\u17E2'] = "\u0968";
            khmerChar2Dev['\u17E3'] = "\u0969";
            khmerChar2Dev['\u17E4'] = "\u096A";
            khmerChar2Dev['\u17E5'] = "\u096B";
            khmerChar2Dev['\u17E6'] = "\u096C";
            khmerChar2Dev['\u17E7'] = "\u096D";
            khmerChar2Dev['\u17E8'] = "\u096E";
            khmerChar2Dev['\u17E9'] = "\u096F";

            // Build the fast single-char reverse table from khmerChar2Dev (values are all single chars). The
            // 1-char khmer2Dev key U+17A2 is intentionally left out and handled inline in Convert. (#86)
            foreach (var kvp in khmerChar2Dev)
                if (kvp.Key < MapLen) charMap[kvp.Key] = kvp.Value[0];
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Replaces the
        // per-position Substring + dictionary lookups with one scan over a char buffer using a reverse table;
        // the lone two-char sequence (U+17A2 U+17B6 -> independent aa) is matched inline.
        public static string Convert(string khmerStr)
        {
            if (string.IsNullOrEmpty(khmerStr))
                return khmerStr;

            int n = khmerStr.Length;
            var buf = new char[n]; // each input char -> at most 1 Deva char (the 2-char "aa" collapses to 1)
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = khmerStr[i];
                if (c == 0x17A2) // independent a; with a following sign aa (U+17B6) it becomes independent aa
                {
                    if (i + 1 < n && khmerStr[i + 1] == 0x17B6) { buf[k++] = (char)0x0906; i++; }
                    else buf[k++] = (char)0x0905;
                    continue;
                }
                char m = (c < MapLen) ? charMap[c] : '\0';
                buf[k++] = (m != '\0') ? m : c;
            }
            return new string(buf, 0, k);
        }

        /// <summary>
        /// FROZEN reference implementation - the correctness oracle for the optimized Convert(). Do NOT change;
        /// tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string khmerStr)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;

            while (i < khmerStr.Length)
            {
                bool matched = false;

                // Try multi-character mappings first (for independent vowel aa)
                if (i + 1 < khmerStr.Length)
                {
                    string twoChar = khmerStr.Substring(i, 2);
                    if (khmer2Dev.ContainsKey(twoChar))
                    {
                        sb.Append(khmer2Dev[twoChar]);
                        i += 2;
                        matched = true;
                    }
                }

                if (!matched)
                {
                    // Try single character mapping in khmer2Dev first (for standalone 'ឣ')
                    char c = khmerStr[i];
                    string oneChar = c.ToString();
                    if (khmer2Dev.ContainsKey(oneChar))
                    {
                        sb.Append(khmer2Dev[oneChar]);
                        i++;
                    }
                    else if (khmerChar2Dev.ContainsKey(c))
                    {
                        sb.Append(khmerChar2Dev[c]);
                        i++;
                    }
                    else
                    {
                        // Pass through unmapped characters
                        sb.Append(c);
                        i++;
                    }
                }
            }

            return sb.ToString();
        }
    }
}
