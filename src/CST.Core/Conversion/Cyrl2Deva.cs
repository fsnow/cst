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

            // Consonants with combining marks + х (aspirates)
            cyrl2Dev["д\u0307х"] = "\u0927"; // dha (dental) - only dot-above, matching Deva2Cyrl output
            cyrl2Dev["г\u0307х"] = "\u0918"; // gha
            cyrl2Dev["ж\u0307х"] = "\u091D"; // jha

            // Consonants with combining marks
            cyrl2Dev["м\u0323"] = "\u0902"; // niggahita
            cyrl2Dev["г\u0307"] = "\u0917"; // ga
            cyrl2Dev["н\u0307"] = "\u0919"; // n overdot a (ṅ)
            cyrl2Dev["ж\u0307"] = "\u091C"; // ja
            cyrl2Dev["н\u0303"] = "\u091E"; // n tilde a (ñ)
            cyrl2Dev["д\u0323"] = "\u0921"; // d underdot a
            cyrl2Dev["н\u0323"] = "\u0923"; // n underdot a
            cyrl2Dev["д\u0307"] = "\u0924"; // ta (dental)
            cyrl2Dev["т\u0307"] = "\u0925"; // tha (dental)
            cyrl2Dev["д\u0307\u0323"] = "\u0926"; // da (dental)
            cyrl2Dev["б\u0323"] = "\u092C"; // ba
            cyrl2Dev["л\u0323"] = "\u0933"; // l underdot a

            // Multi-letter aspirates (2 chars)
            cyrl2Dev["гх"] = "\u0918"; // gha (simple form without dot)
            cyrl2Dev["жх"] = "\u091D"; // jha (simple form without dot)
            cyrl2Dev["дх"] = "\u0922"; // dha (retroflex)
            cyrl2Dev["бх"] = "\u092D"; // bha

            // Double-letter long vowels (independent and dependent forms map to same)
            cyrl2Dev["аа"] = "\u0906"; // aa (independent) / U+093E (dependent) - we'll handle context later
            cyrl2Dev["ий"] = "\u0908"; // ii (independent) / U+0940 (dependent)
            cyrl2Dev["уу"] = "\u090A"; // uu (independent) / U+0942 (dependent)

            // Single-character consonants (velar stops)
            cyrlChar2Dev['г'] = "\u0915"; // ka
            cyrlChar2Dev['к'] = "\u0916"; // kha

            // Single-character consonants (palatal stops)
            cyrlChar2Dev['ж'] = "\u091A"; // ca
            cyrlChar2Dev['ч'] = "\u091B"; // cha

            // Single-character consonants (retroflex stops)
            cyrlChar2Dev['д'] = "\u091F"; // t underdot a
            cyrlChar2Dev['т'] = "\u0920"; // t underdot ha

            // Single-character consonants (labial stops)
            cyrlChar2Dev['б'] = "\u092A"; // pa
            cyrlChar2Dev['п'] = "\u092B"; // pha
            cyrlChar2Dev['м'] = "\u092E"; // ma

            // Single-character consonants (nasals)
            cyrlChar2Dev['н'] = "\u0928"; // na

            // Single-character consonants (liquids, fricatives)
            cyrlChar2Dev['я'] = "\u092F"; // ya
            cyrlChar2Dev['р'] = "\u0930"; // ra
            cyrlChar2Dev['л'] = "\u0932"; // la
            cyrlChar2Dev['в'] = "\u0935"; // va
            cyrlChar2Dev['с'] = "\u0938"; // sa
            cyrlChar2Dev['х'] = "\u0939"; // ha

            // Independent vowels (single chars)
            cyrlChar2Dev['а'] = "\u0905"; // a (will be context-dependent)
            cyrlChar2Dev['и'] = "\u0907"; // i
            cyrlChar2Dev['у'] = "\u0909"; // u
            cyrlChar2Dev['з'] = "\u090F"; // e
            cyrlChar2Dev['о'] = "\u0913"; // o

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

                // 4-char (none exist today, but kept to mirror the reference precedence exactly)
                if (starter && i + 3 < cyrlStr.Length)
                {
                    string fourChar = cyrlStr.Substring(i, 4);
                    if (cyrl2Dev.TryGetValue(fourChar, out string? four))
                    {
                        if (lastWasConsonant) sb.Append('्'); // virama for cluster
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
                        bool isNiggahita = (devaOutput == "ं");
                        if (lastWasConsonant && !isNiggahita) sb.Append('्');
                        sb.Append(devaOutput);
                        i += 3; lastWasConsonant = !isNiggahita; matched = true;
                    }
                }

                // 2-char (double vowels, aspirates)
                if (!matched && starter && i + 1 < cyrlStr.Length)
                {
                    string twoChar = cyrlStr.Substring(i, 2);
                    if (cyrl2Dev.TryGetValue(twoChar, out string? devaOutput))
                    {
                        if (twoChar == "аа" || twoChar == "ий" || twoChar == "уу")
                        {
                            // after a consonant these are dependent long vowels, else independent
                            if (lastWasConsonant)
                            {
                                if (twoChar == "аа") devaOutput = "ा";
                                else if (twoChar == "ий") devaOutput = "ी";
                                else devaOutput = "ू";
                            }
                            sb.Append(devaOutput);
                            i += 2; matched = true; lastWasConsonant = false;
                        }
                        else
                        {
                            bool isNiggahita = (devaOutput == "ं");
                            if (lastWasConsonant && !isNiggahita) sb.Append('्');
                            sb.Append(devaOutput);
                            i += 2; matched = true; lastWasConsonant = !isNiggahita;
                        }
                    }
                }

                if (!matched)
                {
                    char c = cyrlStr[i];
                    string? devaOutput = (c < MapLen) ? singleMap[c] : null;
                    if (devaOutput != null)
                    {
                        if (c == 'а') // 'а' (a)
                        {
                            if (lastWasConsonant) { i++; lastWasConsonant = false; continue; } // inherent 'a'
                            lastWasConsonant = false;
                        }
                        else if (c == 'и' || c == 'у' || c == 'з' || c == 'о') // и у з о
                        {
                            if (lastWasConsonant)
                            {
                                if (c == 'и') devaOutput = "ि"; // dependent i
                                else if (c == 'у') devaOutput = "ु"; // dependent u
                                else if (c == 'з') devaOutput = "े"; // dependent e
                                else devaOutput = "ो"; // dependent o
                            }
                            lastWasConsonant = false;
                        }
                        else if (IsCyrillicConsonant(c))
                        {
                            if (lastWasConsonant) sb.Append('्'); // cluster -> virama first
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

                // Try 4-character mappings first (д\u0307\u0323х)
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

                        // Check if this is niggahita (NOT a consonant - no virama needed)
                        bool isNiggahita = (devaOutput == "\u0902");

                        // If last was a consonant and this is not niggahita, add virama for cluster
                        if (lastWasConsonant && !isNiggahita)
                        {
                            sb.Append('\u094D'); // virama
                        }
                        sb.Append(devaOutput);
                        i += 3;

                        // Update lastWasConsonant state
                        lastWasConsonant = !isNiggahita; // false for niggahita, true for consonants
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

                        // Handle double-letter vowels: check if last was consonant
                        if (twoChar == "аа" || twoChar == "ий" || twoChar == "уу")
                        {
                            if (lastWasConsonant)
                            {
                                // COMMON CASE: After consonant → dependent long vowel
                                // "аа" → dependent ā (U+093E), "ий" → dependent ī (U+0940), "уу" → dependent ū (U+0942)
                                if (twoChar == "аа") devaOutput = "\u093E"; // dependent long ā
                                else if (twoChar == "ий") devaOutput = "\u0940"; // dependent long ī
                                else if (twoChar == "уу") devaOutput = "\u0942"; // dependent long ū

                                sb.Append(devaOutput);
                                i += 2;
                                matched = true;
                            }
                            else
                            {
                                // RARE CASE: Not after consonant → independent long vowel
                                // Use the mapping from cyrl2Dev dictionary (already set to independent vowels)
                                sb.Append(devaOutput);
                                i += 2;
                                matched = true;
                            }
                            lastWasConsonant = false;
                        }
                        else
                        {
                            // Check if this is niggahita (NOT a consonant - no virama needed)
                            bool isNiggahita = (devaOutput == "\u0902");

                            // Aspirates (гх, жх, дх, бх) and other 2-char consonants
                            // If last was a consonant and this is not niggahita, add virama for cluster
                            if (lastWasConsonant && !isNiggahita)
                            {
                                sb.Append('\u094D'); // virama
                            }
                            sb.Append(devaOutput);
                            i += 2;
                            matched = true;

                            // Update lastWasConsonant state
                            lastWasConsonant = !isNiggahita; // false for niggahita, true for consonants
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

                        // Special handling for 'а' (a)
                        if (c == 'а')
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
                        // Single vowels (и, у, з, о)
                        else if (c == 'и' || c == 'у' || c == 'з' || c == 'о')
                        {
                            if (lastWasConsonant)
                            {
                                // Use dependent vowel form
                                if (c == 'и') devaOutput = "\u093F"; // dependent i
                                else if (c == 'у') devaOutput = "\u0941"; // dependent u
                                else if (c == 'з') devaOutput = "\u0947"; // dependent e
                                else if (c == 'о') devaOutput = "\u094B"; // dependent o
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

        private static bool IsCyrillicConsonant(char c)
        {
            // Check if character is a Cyrillic consonant (г, к, ж, ч, д, т, б, п, н, м, я, р, л, в, с, х)
            return c == 'г' || c == 'к' || c == 'ж' || c == 'ч' ||
                   c == 'д' || c == 'т' || c == 'б' || c == 'п' ||
                   c == 'н' || c == 'м' || c == 'я' || c == 'р' ||
                   c == 'л' || c == 'в' || c == 'с' || c == 'х';
        }

        private static bool IsDevanagariConsonant(char c)
        {
            int code = (int)c;
            return code >= 0x0915 && code <= 0x0939; // Devanagari consonant range
        }
    }
}
