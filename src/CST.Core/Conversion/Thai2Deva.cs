using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Thai2Deva
    {
        private static IDictionary<string, string> thai2Dev;
        private static IDictionary<char, string> thaiChar2Dev;

        static Thai2Deva()
        {
            thai2Dev = new Dictionary<string, string>();
            thaiChar2Dev = new Dictionary<char, string>();

            // niggahita
            thaiChar2Dev['\u0E4D'] = "\u0902";

            // independent vowels (reverse of Deva2Thai multi-char mappings)
            thai2Dev["\u0E2D"] = "\u0905"; // a
            thai2Dev["\u0E2D\u0E32"] = "\u0906"; // aa
            thai2Dev["\u0E2D\u0E34"] = "\u0907"; // i
            thai2Dev["\u0E2D\u0E35"] = "\u0908"; // ii
            thai2Dev["\u0E2D\u0E38"] = "\u0909"; // u
            thai2Dev["\u0E2D\u0E39"] = "\u090A"; // uu
            thai2Dev["\u0E40\u0E2D"] = "\u090F"; // e
            thai2Dev["\u0E42\u0E2D"] = "\u0913"; // o

            // velar stops
            thaiChar2Dev['\u0E01'] = "\u0915"; // ka
            thaiChar2Dev['\u0E02'] = "\u0916"; // kha
            thaiChar2Dev['\u0E04'] = "\u0917"; // ga
            thaiChar2Dev['\u0E06'] = "\u0918"; // gha
            thaiChar2Dev['\u0E07'] = "\u0919"; // n overdot a

            // palatal stops
            thaiChar2Dev['\u0E08'] = "\u091A"; // ca
            thaiChar2Dev['\u0E09'] = "\u091B"; // cha
            thaiChar2Dev['\u0E0A'] = "\u091C"; // ja
            thaiChar2Dev['\u0E0C'] = "\u091D"; // jha
            thaiChar2Dev['\u0E0D'] = "\u091E"; // n tilde a

            // retroflex stops
            thaiChar2Dev['\u0E0F'] = "\u091F"; // t underdot a
            thaiChar2Dev['\u0E10'] = "\u0920"; // t underdot ha
            thaiChar2Dev['\u0E11'] = "\u0921"; // d underdot a
            thaiChar2Dev['\u0E12'] = "\u0922"; // d underdot ha
            thaiChar2Dev['\u0E13'] = "\u0923"; // n underdot a

            // dental stops
            thaiChar2Dev['\u0E15'] = "\u0924"; // ta
            thaiChar2Dev['\u0E16'] = "\u0925"; // tha
            thaiChar2Dev['\u0E17'] = "\u0926"; // da
            thaiChar2Dev['\u0E18'] = "\u0927"; // dha
            thaiChar2Dev['\u0E19'] = "\u0928"; // na

            // labial stops
            thaiChar2Dev['\u0E1B'] = "\u092A"; // pa
            thaiChar2Dev['\u0E1C'] = "\u092B"; // pha
            thaiChar2Dev['\u0E1E'] = "\u092C"; // ba
            thaiChar2Dev['\u0E20'] = "\u092D"; // bha
            thaiChar2Dev['\u0E21'] = "\u092E"; // ma

            // liquids, fricatives, etc.
            thaiChar2Dev['\u0E22'] = "\u092F"; // ya
            thaiChar2Dev['\u0E23'] = "\u0930"; // ra
            thaiChar2Dev['\u0E25'] = "\u0932"; // la
            thaiChar2Dev['\u0E27'] = "\u0935"; // va
            thaiChar2Dev['\u0E2A'] = "\u0938"; // sa
            thaiChar2Dev['\u0E2B'] = "\u0939"; // ha
            thaiChar2Dev['\u0E2C'] = "\u0933"; // l underdot a

            // dependent vowel signs
            thaiChar2Dev['\u0E32'] = "\u093E"; // aa
            thaiChar2Dev['\u0E34'] = "\u093F"; // i
            thaiChar2Dev['\u0E35'] = "\u0940"; // ii
            thaiChar2Dev['\u0E38'] = "\u0941"; // u
            thaiChar2Dev['\u0E39'] = "\u0942"; // uu
            thaiChar2Dev['\u0E40'] = "\u0947"; // e
            thaiChar2Dev['\u0E42'] = "\u094B"; // o

            // Placeholder characters (used during preprocessing for round-trip preservation)
            thaiChar2Dev['\uFFF0'] = "\u0947"; // placeholder for dependent e (was before consonant in Thai)
            thaiChar2Dev['\uFFF1'] = "\u094B"; // placeholder for dependent o (was before consonant in Thai)

            thaiChar2Dev['\u0E3A'] = "\u094D"; // virama

            // numerals
            thaiChar2Dev['\u0E50'] = "\u0966";
            thaiChar2Dev['\u0E51'] = "\u0967";
            thaiChar2Dev['\u0E52'] = "\u0968";
            thaiChar2Dev['\u0E53'] = "\u0969";
            thaiChar2Dev['\u0E54'] = "\u096A";
            thaiChar2Dev['\u0E55'] = "\u096B";
            thaiChar2Dev['\u0E56'] = "\u096C";
            thaiChar2Dev['\u0E57'] = "\u096D";
            thaiChar2Dev['\u0E58'] = "\u096E";
            thaiChar2Dev['\u0E59'] = "\u096F";
        }

        public static string Convert(string thaiStr)
        {
            // Pre-processing: split the special Thai iṃ character back to i + niggahita
            thaiStr = thaiStr.Replace("\u0E36", "\u0E34\u0E4D");

            // Pre-processing: Use placeholders instead of moving vowels to preserve position information
            // This fixes round-trip conversion ambiguity between independent and dependent vowels

            // Replace เ + consonant (not อ, not followed by อ) with consonant + placeholder
            // Placeholder U+FFF0 represents "dependent e that was before consonant in Thai"
            // Exclude อ because เอ is independent vowel e, not consonant+vowel
            // Also exclude if consonant is followed by อ (independent vowel follows)
            thaiStr = Regex.Replace(thaiStr, "\u0E40([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF0");

            // Replace โ + consonant (not อ, not followed by อ) with consonant + placeholder
            // Placeholder U+FFF1 represents "dependent o that was before consonant in Thai"
            // Exclude อ because โอ is independent vowel o, not consonant+vowel
            // Also exclude if consonant is followed by อ (independent vowel follows)
            thaiStr = Regex.Replace(thaiStr, "\u0E42([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF1");

            StringBuilder sb = new StringBuilder();
            int i = 0;

            while (i < thaiStr.Length)
            {
                bool matched = false;

                // Check for เ or โ followed by consonant (not moved by preprocessing)
                // This happens when consonant is followed by อ (independent vowel)
                if (i + 2 < thaiStr.Length)
                {
                    char first = thaiStr[i];
                    char second = thaiStr[i + 1];
                    char third = thaiStr[i + 2];

                    // เ + consonant + อ → consonant + dependent e (independent vowel e pattern)
                    if (first == '\u0E40' && second >= '\u0E01' && second <= '\u0E2E' && second != '\u0E2D' && third == '\u0E2D')
                    {
                        if (thaiChar2Dev.ContainsKey(second))
                        {
                            sb.Append(thaiChar2Dev[second]);
                            sb.Append('\u0947'); // dependent e
                            i += 2;
                            matched = true;
                        }
                    }
                    // โ + consonant + อ → consonant + dependent o (independent vowel o pattern)
                    else if (first == '\u0E42' && second >= '\u0E01' && second <= '\u0E2E' && second != '\u0E2D' && third == '\u0E2D')
                    {
                        if (thaiChar2Dev.ContainsKey(second))
                        {
                            sb.Append(thaiChar2Dev[second]);
                            sb.Append('\u094B'); // dependent o
                            i += 2;
                            matched = true;
                        }
                    }
                }

                // Try multi-character mappings (for independent vowels)
                if (!matched && i + 1 < thaiStr.Length)
                {
                    string twoChar = thaiStr.Substring(i, 2);
                    if (thai2Dev.ContainsKey(twoChar))
                    {
                        sb.Append(thai2Dev[twoChar]);
                        i += 2;
                        matched = true;
                    }
                }

                if (!matched)
                {
                    // Try single character mapping in thai2Dev first (for standalone 'อ')
                    char c = thaiStr[i];
                    string oneChar = c.ToString();
                    if (thai2Dev.ContainsKey(oneChar))
                    {
                        sb.Append(thai2Dev[oneChar]);
                        i++;
                    }
                    else if (thaiChar2Dev.ContainsKey(c))
                    {
                        sb.Append(thaiChar2Dev[c]);
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
