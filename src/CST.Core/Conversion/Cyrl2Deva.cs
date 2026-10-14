using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Cyrl2Deva
    {
        private static IDictionary<string, string> cyrl2Dev;
        private static IDictionary<char, string> cyrlChar2Dev;

        // Fast tables for the optimized Convert(): multiStarter[c] marks chars that can begin a multi-char
        // cyrl2Dev key (so the per-position Substring lookups are only attempted when they could match);
        // singleMap[c] is the cyrlChar2Dev value, null = not a single-char key. (#86)
        private const int MapLen = 0x0500; // covers the Cyrillic block + ASCII digits (source chars)
        private static readonly bool[] multiStarter = new bool[MapLen];
        private static readonly string[] singleMap = new string[MapLen];

        static Cyrl2Deva()
        {
            cyrl2Dev = new Dictionary<string, string>();
            cyrlChar2Dev = new Dictionary<char, string>();

            // Multi-character sequences (longest first for proper matching)

            // Consonants with combining marks + \u0445 (aspirates)
            cyrl2Dev["\u0434\u0307\u0445"] = "\u0927"; // dha (dental) - only dot-above, matching Deva2Cyrl output
            cyrl2Dev["\u0433\u0307\u0445"] = "\u0918"; // gha
            cyrl2Dev["\u0436\u0307\u0445"] = "\u091D"; // jha

            // Consonants with combining marks
            cyrl2Dev["\u043C\u0323"] = "\u0902"; // niggahita
            cyrl2Dev["\u0433\u0307"] = "\u0917"; // ga
            cyrl2Dev["\u043D\u0307"] = "\u0919"; // n overdot a (\u1E45)
            cyrl2Dev["\u0436\u0307"] = "\u091C"; // ja
            cyrl2Dev["\u043D\u0303"] = "\u091E"; // n tilde a (\u00F1)
            cyrl2Dev["\u0434\u0323"] = "\u0921"; // d underdot a
            cyrl2Dev["\u043D\u0323"] = "\u0923"; // n underdot a
            cyrl2Dev["\u0434\u0307"] = "\u0924"; // ta (dental)
            cyrl2Dev["\u0442\u0307"] = "\u0925"; // tha (dental)
            cyrl2Dev["\u0434\u0307\u0323"] = "\u0926"; // da (dental)
            cyrl2Dev["\u0431\u0323"] = "\u092C"; // ba
            cyrl2Dev["\u043B\u0323"] = "\u0933"; // l underdot a

            // Multi-letter aspirates (2 chars)
            cyrl2Dev["\u0433\u0445"] = "\u0918"; // gha (simple form without dot)
            cyrl2Dev["\u0436\u0445"] = "\u091D"; // jha (simple form without dot)
            cyrl2Dev["\u0434\u0445"] = "\u0922"; // dha (retroflex)
            cyrl2Dev["\u0431\u0445"] = "\u092D"; // bha

            // Double-letter long vowels (independent and dependent forms map to same)
            cyrl2Dev["\u0430\u0430"] = "\u0906"; // aa (independent) / U+093E (dependent) - we'll handle context later
            cyrl2Dev["\u0438\u0439"] = "\u0908"; // ii (independent) / U+0940 (dependent)
            cyrl2Dev["\u0443\u0443"] = "\u090A"; // uu (independent) / U+0942 (dependent)

            // Sanskrit letters (#1049). Vocalic r, ai and au are vowels like the pairs above: dependent after a
            // consonant, else independent. Visarga is a mark like niggahita. Sha is a consonant.
            cyrl2Dev["\u0440\u0323"] = "\u090B"; // vocalic r / U+0943
            cyrl2Dev["\u0430\u0439"] = "\u0910"; // ai / U+0948
            cyrl2Dev["\u0430\u045E"] = "\u0914"; // au / U+094C
            cyrl2Dev["\u0445\u0323"] = "\u0903"; // visarga
            cyrl2Dev["\u0448\u0301"] = "\u0936"; // sha (palatal)

            // Single-character consonants (velar stops)
            cyrlChar2Dev['\u0433'] = "\u0915"; // ka
            cyrlChar2Dev['\u043A'] = "\u0916"; // kha

            // Single-character consonants (palatal stops)
            cyrlChar2Dev['\u0436'] = "\u091A"; // ca
            cyrlChar2Dev['\u0447'] = "\u091B"; // cha

            // Single-character consonants (retroflex stops)
            cyrlChar2Dev['\u0434'] = "\u091F"; // t underdot a
            cyrlChar2Dev['\u0442'] = "\u0920"; // t underdot ha

            // Single-character consonants (labial stops)
            cyrlChar2Dev['\u0431'] = "\u092A"; // pa
            cyrlChar2Dev['\u043F'] = "\u092B"; // pha
            cyrlChar2Dev['\u043C'] = "\u092E"; // ma

            // Single-character consonants (nasals)
            cyrlChar2Dev['\u043D'] = "\u0928"; // na

            // Single-character consonants (liquids, fricatives)
            cyrlChar2Dev['\u044F'] = "\u092F"; // ya
            cyrlChar2Dev['\u0440'] = "\u0930"; // ra
            cyrlChar2Dev['\u043B'] = "\u0932"; // la
            cyrlChar2Dev['\u0432'] = "\u0935"; // va
            cyrlChar2Dev['\u0441'] = "\u0938"; // sa
            cyrlChar2Dev['\u0445'] = "\u0939"; // ha
            cyrlChar2Dev['\u0448'] = "\u0937"; // ssa (retroflex) (#1049)

            // Independent vowels (single chars)
            cyrlChar2Dev['\u0430'] = "\u0905"; // a (will be context-dependent)
            cyrlChar2Dev['\u0438'] = "\u0907"; // i
            cyrlChar2Dev['\u0443'] = "\u0909"; // u
            cyrlChar2Dev['\u044D'] = "\u090F"; // e (#1059)
            cyrlChar2Dev['\u0437'] = "\u090F"; // e as written before #1059, still read
            cyrlChar2Dev['\u043E'] = "\u0913"; // o

            // Numerals (ASCII)
            cyrlChar2Dev['0'] = "\u0966";
            cyrlChar2Dev['1'] = "\u0967";
            cyrlChar2Dev['2'] = "\u0968";
            cyrlChar2Dev['3'] = "\u0969";
            cyrlChar2Dev['4'] = "\u096A";
            cyrlChar2Dev['5'] = "\u096B";
            cyrlChar2Dev['6'] = "\u096C";
            cyrlChar2Dev['7'] = "\u096D";
            cyrlChar2Dev['8'] = "\u096E";
            cyrlChar2Dev['9'] = "\u096F";

            // Build the fast tables: starter chars from the multi-char keys, and the single-char map. (#86)
            foreach (var kvp in cyrl2Dev)
                if (kvp.Key.Length > 0 && kvp.Key[0] < MapLen) multiStarter[kvp.Key[0]] = true;
            foreach (var kvp in cyrlChar2Dev)
                if (kvp.Key < MapLen) singleMap[kvp.Key] = kvp.Value;
        }

        // Optimized (#86): byte-identical to ConvertReference (verified by tests). Same stateful algorithm, but
        // the multi-char Substring+dictionary probes run only when the current char can start a multi-char key
        // (multiStarter), and single-char lookups use a char-indexed table instead of c.ToString() + the dict.
        public static string Convert(string cyrlStr)
        {
            if (string.IsNullOrEmpty(cyrlStr))
                return cyrlStr;

            StringBuilder sb = new StringBuilder(cyrlStr.Length);
            int i = 0;
            bool lastWasConsonant = false;

            while (i < cyrlStr.Length)
            {
                bool matched = false;
                char c0 = cyrlStr[i];
                bool starter = c0 < MapLen && multiStarter[c0]; // can this char begin a multi-char key?

                if (InherentAThenDiphthong(cyrlStr, i) is string diph) // (#1049)
                {
                    if (!lastWasConsonant) sb.Append('\u0905');
                    sb.Append(diph);
                    i += 3; lastWasConsonant = false;
                    continue;
                }

                // 4-char (none exist today, but kept to mirror the reference precedence exactly)
                if (starter && i + 3 < cyrlStr.Length)
                {
                    string fourChar = cyrlStr.Substring(i, 4);
                    if (cyrl2Dev.TryGetValue(fourChar, out string? four))
                    {
                        if (lastWasConsonant) sb.Append('\u094D'); // virama for cluster
                        sb.Append(four);
                        i += 4; lastWasConsonant = true; matched = true;
                    }
                }

                // 3-char (combining marks on consonants)
                if (!matched && starter && i + 2 < cyrlStr.Length)
                {
                    string threeChar = cyrlStr.Substring(i, 3);
                    if (cyrl2Dev.TryGetValue(threeChar, out string? devaOutput))
                    {
                        bool isMark = IsMark(devaOutput);
                        if (lastWasConsonant && !isMark) sb.Append('\u094D');
                        sb.Append(devaOutput);
                        i += 3; lastWasConsonant = !isMark; matched = true;
                    }
                }

                // 2-char (double vowels, aspirates)
                if (!matched && starter && i + 1 < cyrlStr.Length)
                {
                    string twoChar = cyrlStr.Substring(i, 2);
                    if (cyrl2Dev.TryGetValue(twoChar, out string? devaOutput))
                    {
                        if (DependentVowel(twoChar) is string dependent)
                        {
                            // after a consonant these are dependent vowel signs, else independent
                            if (lastWasConsonant) devaOutput = dependent;
                            sb.Append(devaOutput);
                            i += 2; matched = true; lastWasConsonant = false;
                        }
                        else
                        {
                            bool isMark = IsMark(devaOutput);
                            if (lastWasConsonant && !isMark) sb.Append('\u094D');
                            sb.Append(devaOutput);
                            i += 2; matched = true; lastWasConsonant = !isMark;
                        }
                    }
                }

                if (!matched)
                {
                    char c = cyrlStr[i];
                    string? devaOutput = (c < MapLen) ? singleMap[c] : null;
                    if (devaOutput != null)
                    {
                        if (c == '\u0430') // '\u0430' (a)
                        {
                            if (lastWasConsonant) { i++; lastWasConsonant = false; continue; } // inherent 'a'
                            lastWasConsonant = false;
                        }
                        else if (c == '\u0438' || c == '\u0443' || c == '\u044D' || c == '\u0437' || c == '\u043E')
                        {
                            if (lastWasConsonant)
                            {
                                if (c == '\u0438') devaOutput = "\u093F"; // dependent i
                                else if (c == '\u0443') devaOutput = "\u0941"; // dependent u
                                else if (c == '\u044D' || c == '\u0437') devaOutput = "\u0947"; // dependent e (#1059)
                                else devaOutput = "\u094B"; // dependent o
                            }
                            lastWasConsonant = false;
                        }
                        else if (IsCyrillicConsonant(c))
                        {
                            if (lastWasConsonant) sb.Append('\u094D'); // cluster -> virama first
                            lastWasConsonant = true;
                        }
                        else if (c >= '0' && c <= '9')
                        {
                            lastWasConsonant = false;
                        }
                        sb.Append(devaOutput);
                        i++;
                    }
                    else
                    {
                        sb.Append(c); // pass through
                        i++;
                        lastWasConsonant = false;
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// FROZEN reference implementation - the correctness oracle for the optimized Convert(). Do NOT change;
        /// tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string cyrlStr)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            bool lastWasConsonant = false; // Track if last processed item was a consonant

            while (i < cyrlStr.Length)
            {
                bool matched = false;

                // a, then an independent ai or au (#1049)
                if (InherentAThenDiphthong(cyrlStr, i) is string diph)
                {
                    if (!lastWasConsonant) sb.Append('\u0905'); // independent a; after a consonant it is inherent
                    sb.Append(diph);
                    i += 3;
                    lastWasConsonant = false;
                    continue;
                }

                // Try 4-character mappings first (\u0434\u0307\u0323\u0445)
                if (i + 3 < cyrlStr.Length)
                {
                    string fourChar = cyrlStr.Substring(i, 4);
                    if (cyrl2Dev.ContainsKey(fourChar))
                    {
                        // If last was a consonant, add virama for cluster
                        if (lastWasConsonant)
                        {
                            sb.Append('\u094D'); // virama
                        }
                        sb.Append(cyrl2Dev[fourChar]);
                        i += 4;
                        lastWasConsonant = true; // All multi-char mappings are consonants
                        matched = true;
                    }
                }

                // Try 3-character mappings (combining marks on consonants)
                if (!matched && i + 2 < cyrlStr.Length)
                {
                    string threeChar = cyrlStr.Substring(i, 3);
                    if (cyrl2Dev.ContainsKey(threeChar))
                    {
                        string devaOutput = cyrl2Dev[threeChar];

                        // Niggahita and visarga are marks, not consonants: no virama needed
                        bool isMark = IsMark(devaOutput);

                        // If last was a consonant and this is not a mark, add virama for cluster
                        if (lastWasConsonant && !isMark)
                        {
                            sb.Append('\u094D'); // virama
                        }
                        sb.Append(devaOutput);
                        i += 3;

                        // Update lastWasConsonant state
                        lastWasConsonant = !isMark; // false for marks, true for consonants
                        matched = true;
                    }
                }

                // Try 2-character mappings (double vowels, aspirates)
                if (!matched && i + 1 < cyrlStr.Length)
                {
                    string twoChar = cyrlStr.Substring(i, 2);
                    if (cyrl2Dev.ContainsKey(twoChar))
                    {
                        string devaOutput = cyrl2Dev[twoChar];

                        // Handle two-character vowels: check if last was consonant
                        string? dependent = DependentVowel(twoChar);
                        if (dependent != null)
                        {
                            if (lastWasConsonant)
                            {
                                // COMMON CASE: After consonant \u2192 dependent vowel sign
                                // (long aa, ii, uu; and vocalic r, ai, au for #1049)
                                devaOutput = dependent;

                                sb.Append(devaOutput);
                                i += 2;
                                matched = true;
                            }
                            else
                            {
                                // RARE CASE: Not after consonant \u2192 independent long vowel
                                // Use the mapping from cyrl2Dev dictionary (already set to independent vowels)
                                sb.Append(devaOutput);
                                i += 2;
                                matched = true;
                            }
                            lastWasConsonant = false;
                        }
                        else
                        {
                            // Niggahita and visarga are marks, not consonants: no virama needed
                            bool isMark = IsMark(devaOutput);

                            // Aspirates (\u0433\u0445, \u0436\u0445, \u0434\u0445, \u0431\u0445) and other 2-char consonants
                            // If last was a consonant and this is not a mark, add virama for cluster
                            if (lastWasConsonant && !isMark)
                            {
                                sb.Append('\u094D'); // virama
                            }
                            sb.Append(devaOutput);
                            i += 2;
                            matched = true;

                            // Update lastWasConsonant state
                            lastWasConsonant = !isMark; // false for marks, true for consonants
                        }
                    }
                }

                if (!matched)
                {
                    char c = cyrlStr[i];

                    // Single character mapping
                    if (cyrlChar2Dev.ContainsKey(c))
                    {
                        string devaOutput = cyrlChar2Dev[c];

                        // Special handling for '\u0430' (a)
                        if (c == '\u0430')
                        {
                            // If preceded by a consonant, skip it (inherent 'a')
                            if (lastWasConsonant)
                            {
                                i++;
                                lastWasConsonant = false;
                                continue;
                            }
                            // Otherwise it's independent vowel 'a'
                            lastWasConsonant = false;
                        }
                        // Single vowels (\u0438, \u0443, \u044D or \u0437, \u043E)
                        else if (c == '\u0438' || c == '\u0443' || c == '\u044D' || c == '\u0437' || c == '\u043E')
                        {
                            if (lastWasConsonant)
                            {
                                // Use dependent vowel form
                                if (c == '\u0438') devaOutput = "\u093F"; // dependent i
                                else if (c == '\u0443') devaOutput = "\u0941"; // dependent u
                                else if (c == '\u044D' || c == '\u0437') devaOutput = "\u0947"; // dependent e (#1059)
                                else if (c == '\u043E') devaOutput = "\u094B"; // dependent o
                            }
                            lastWasConsonant = false;
                        }
                        // Consonants
                        else if (IsCyrillicConsonant(c))
                        {
                            // If last was also a consonant, we have a cluster - add virama first
                            if (lastWasConsonant)
                            {
                                sb.Append('\u094D'); // virama
                            }
                            lastWasConsonant = true;
                        }
                        // Numerals
                        else if (c >= '0' && c <= '9')
                        {
                            lastWasConsonant = false;
                        }

                        sb.Append(devaOutput);
                        i++;
                    }
                    else
                    {
                        // Pass through unmapped characters (punctuation, spaces, etc.)
                        sb.Append(c);
                        i++;
                        lastWasConsonant = false; // Reset on non-mapped characters
                    }
                }
            }

            return sb.ToString();
        }

        // The two-character vowels and their dependent forms. (#1049 adds vocalic r, ai and au.)
        private static string? DependentVowel(string twoChar) => twoChar switch
        {
            "\u0430\u0430" => "\u093E",
            "\u0438\u0439" => "\u0940",
            "\u0443\u0443" => "\u0942",
            "\u0440\u0323" => "\u0943",
            "\u0430\u0439" => "\u0948",
            "\u0430\u045E" => "\u094C",
            _ => null,
        };

        // An a followed by an independent ai or au is written aa + short i / short u, which would otherwise read
        // as long aa + a stray letter. Short i / u never follow aa otherwise. The first a is a consonant's inherent
        // a, or an independent a when no consonant precedes it. (#1049)
        private static string? InherentAThenDiphthong(string s, int i)
        {
            if (i + 2 >= s.Length || s[i] != '\u0430' || s[i + 1] != '\u0430') return null;
            return s[i + 2] == '\u0439' ? "\u0910" : s[i + 2] == '\u045E' ? "\u0914" : null;
        }

        // Marks that follow a vowel and are not consonants: niggahita and visarga (#1049).
        private static bool IsMark(string deva) => deva == "\u0902" || deva == "\u0903";

        private static bool IsCyrillicConsonant(char c)
        {
            // Check if character is a Cyrillic consonant (\u0433, \u043A, \u0436, \u0447, \u0434, \u0442, \u0431, \u043F, \u043D, \u043C, \u044F, \u0440, \u043B, \u0432, \u0441, \u0445)
            return c == '\u0433' || c == '\u043A' || c == '\u0436' || c == '\u0447' ||
                   c == '\u0434' || c == '\u0442' || c == '\u0431' || c == '\u043F' ||
                   c == '\u043D' || c == '\u043C' || c == '\u044F' || c == '\u0440' ||
                   c == '\u043B' || c == '\u0432' || c == '\u0441' || c == '\u0445' || c == '\u0448';
        }

        private static bool IsDevanagariConsonant(char c)
        {
            int code = (int)c;
            return code >= 0x0915 && code <= 0x0939; // Devanagari consonant range
        }
    }
}
