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

        // Fast tables for the optimized Convert(): multiStarter marks chars that can begin a multi-char thai2Dev
        // key; single1 is the thai2Dev value for 1-char keys; charMap is the thaiChar2Dev value (Thai block).
        // The two placeholder chars (U+FFF0/U+FFF1) sit outside the Thai block and are handled inline. (#86)
        private const int ThaiLen = 0x0E80;
        private static readonly bool[] thaiMultiStarter = new bool[ThaiLen];
        private static readonly string[] thaiSingle1 = new string[ThaiLen];
        private static readonly string[] thaiCharMap = new string[ThaiLen];

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

            // Build the fast tables (#86).
            foreach (var kvp in thai2Dev)
            {
                if (kvp.Key.Length >= 1 && kvp.Key[0] < ThaiLen) thaiMultiStarter[kvp.Key[0]] = true;
                if (kvp.Key.Length == 1 && kvp.Key[0] < ThaiLen) thaiSingle1[kvp.Key[0]] = kvp.Value;
            }
            foreach (var kvp in thaiChar2Dev)
                if (kvp.Key < ThaiLen) thaiCharMap[kvp.Key] = kvp.Value;
        }

        // Optimized (#86): byte-identical to ConvertReference (verified by tests). Keeps the reference's three
        // preprocessing passes (the i+niggahita split and the two e/o pre-vowel reorder regexes), then replaces
        // stateful loop's per-position Substring + dictionary lookups with char-indexed table lookups.
        public static string Convert(string thaiStr)
        {
            if (string.IsNullOrEmpty(thaiStr))
                return thaiStr;

            // Pre-processing (identical to the reference): split i+niggahita, then move the e/o pre-vowels into
            // placeholders so independent vs dependent vowels round-trip correctly.
            thaiStr = thaiStr.Replace("\u0E36", "\u0E34\u0E4D");
            thaiStr = Regex.Replace(thaiStr, "\u0E40([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF0");
            thaiStr = Regex.Replace(thaiStr, "\u0E42([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF1");

            StringBuilder sb = new StringBuilder(thaiStr.Length);
            int i = 0;
            while (i < thaiStr.Length)
            {
                bool matched = false;
                char first = thaiStr[i];

                // \u0E40/\u0E42 + consonant + \u0E2D (the independent-vowel patterns the regexes deliberately left in place)
                if (i + 2 < thaiStr.Length)
                {
                    char second = thaiStr[i + 1];
                    char third = thaiStr[i + 2];
                    if (first == '\u0E40' && second >= '\u0E01' && second <= '\u0E2E' && second != '\u0E2D' && third == '\u0E2D')
                    {
                        string? sv = (second < ThaiLen) ? thaiCharMap[second] : null;
                        if (sv != null) { sb.Append(sv); sb.Append('\u0947'); i += 2; matched = true; }
                    }
                    else if (first == '\u0E42' && second >= '\u0E01' && second <= '\u0E2E' && second != '\u0E2D' && third == '\u0E2D')
                    {
                        string? sv = (second < ThaiLen) ? thaiCharMap[second] : null;
                        if (sv != null) { sb.Append(sv); sb.Append('\u094B'); i += 2; matched = true; }
                    }
                }

                // multi-char (independent vowels) - only probe when the char can begin a key
                if (!matched && i + 1 < thaiStr.Length && first < ThaiLen && thaiMultiStarter[first])
                {
                    string twoChar = thaiStr.Substring(i, 2);
                    if (thai2Dev.TryGetValue(twoChar, out string? tv)) { sb.Append(tv); i += 2; matched = true; }
                }

                if (!matched)
                {
                    char c = thaiStr[i];
                    string? one = (c < ThaiLen) ? thaiSingle1[c] : null; // thai2Dev 1-char keys (standalone \u0E2D)
                    if (one != null) { sb.Append(one); i++; }
                    else
                    {
                        string? cm = (c < ThaiLen) ? thaiCharMap[c]
                                     : (c == '\uFFF0' ? "\u0947" : c == '\uFFF1' ? "\u094B" : null);
                        if (cm != null) { sb.Append(cm); i++; }
                        else { sb.Append(c); i++; } // pass through
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// FROZEN reference implementation - the correctness oracle for the optimized Convert(). Do NOT change;
        /// tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string thaiStr)
        {
            // Pre-processing: split the special Thai i\u1E43 character back to i + niggahita
            thaiStr = thaiStr.Replace("\u0E36", "\u0E34\u0E4D");

            // Pre-processing: Use placeholders instead of moving vowels to preserve position information
            // This fixes round-trip conversion ambiguity between independent and dependent vowels

            // Replace \u0E40 + consonant (not \u0E2D, not followed by \u0E2D) with consonant + placeholder
            // Placeholder U+FFF0 represents "dependent e that was before consonant in Thai"
            // Exclude \u0E2D because \u0E40\u0E2D is independent vowel e, not consonant+vowel
            // Also exclude if consonant is followed by \u0E2D (independent vowel follows)
            thaiStr = Regex.Replace(thaiStr, "\u0E40([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF0");

            // Replace \u0E42 + consonant (not \u0E2D, not followed by \u0E2D) with consonant + placeholder
            // Placeholder U+FFF1 represents "dependent o that was before consonant in Thai"
            // Exclude \u0E2D because \u0E42\u0E2D is independent vowel o, not consonant+vowel
            // Also exclude if consonant is followed by \u0E2D (independent vowel follows)
            thaiStr = Regex.Replace(thaiStr, "\u0E42([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\uFFF1");

            StringBuilder sb = new StringBuilder();
            int i = 0;

            while (i < thaiStr.Length)
            {
                bool matched = false;

                // Check for \u0E40 or \u0E42 followed by consonant (not moved by preprocessing)
                // This happens when consonant is followed by \u0E2D (independent vowel)
                if (i + 2 < thaiStr.Length)
                {
                    char first = thaiStr[i];
                    char second = thaiStr[i + 1];
                    char third = thaiStr[i + 2];

                    // \u0E40 + consonant + \u0E2D \u2192 consonant + dependent e (independent vowel e pattern)
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
                    // \u0E42 + consonant + \u0E2D \u2192 consonant + dependent o (independent vowel o pattern)
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
                    // Try single character mapping in thai2Dev first (for standalone '\u0E2D')
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
