using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Tibt2Deva
    {
        private static IDictionary<string, string> tibt2Deva;
        private static IDictionary<char, string> tibtChar2Deva;

        static Tibt2Deva()
        {
            tibt2Deva = new Dictionary<string, string>();
            tibtChar2Deva = new Dictionary<char, string>();

            // niggahita
            tibtChar2Deva['\u0F7E'] = "\u0902";

            // independent vowels (reverse of Deva2Tibt multi-char mappings)
            tibtChar2Deva['\u0F68'] = "\u0905"; // a (single char)
            tibt2Deva["\u0F68\u0F71"] = "\u0906"; // aa
            tibt2Deva["\u0F68\u0F72"] = "\u0907"; // i
            tibt2Deva["\u0F68\u0F71\u0F72"] = "\u0908"; // ii
            tibt2Deva["\u0F68\u0F74"] = "\u0909"; // u
            tibt2Deva["\u0F68\u0F71\u0F74"] = "\u090A"; // uu
            tibt2Deva["\u0F68\u0F7A"] = "\u090F"; // e
            tibt2Deva["\u0F68\u0F7C"] = "\u0913"; // o

            // velar stops
            tibtChar2Deva['\u0F40'] = "\u0915"; // ka
            tibtChar2Deva['\u0F41'] = "\u0916"; // kha
            tibtChar2Deva['\u0F42'] = "\u0917"; // ga
            tibtChar2Deva['\u0F43'] = "\u0918"; // gha
            tibtChar2Deva['\u0F44'] = "\u0919"; // n overdot a

            // palatal stops (tsa, tsha, dza, dzha series)
            tibtChar2Deva['\u0F59'] = "\u091A"; // ca
            tibtChar2Deva['\u0F5A'] = "\u091B"; // cha
            tibtChar2Deva['\u0F5B'] = "\u091C"; // ja
            tibtChar2Deva['\u0F5C'] = "\u091D"; // jha
            tibtChar2Deva['\u0F49'] = "\u091E"; // n tilde a

            // retroflex stops
            tibtChar2Deva['\u0F4A'] = "\u091F"; // t underdot a
            tibtChar2Deva['\u0F4B'] = "\u0920"; // t underdot ha
            tibtChar2Deva['\u0F4C'] = "\u0921"; // d underdot a
            tibtChar2Deva['\u0F4D'] = "\u0922"; // d underdot ha
            tibtChar2Deva['\u0F4E'] = "\u0923"; // n underdot a

            // dental stops
            tibtChar2Deva['\u0F4F'] = "\u0924"; // ta
            tibtChar2Deva['\u0F50'] = "\u0925"; // tha
            tibtChar2Deva['\u0F51'] = "\u0926"; // da
            tibtChar2Deva['\u0F52'] = "\u0927"; // dha
            tibtChar2Deva['\u0F53'] = "\u0928"; // na

            // labial stops
            tibtChar2Deva['\u0F54'] = "\u092A"; // pa
            tibtChar2Deva['\u0F55'] = "\u092B"; // pha
            tibtChar2Deva['\u0F56'] = "\u092C"; // ba
            tibtChar2Deva['\u0F57'] = "\u092D"; // bha
            tibtChar2Deva['\u0F58'] = "\u092E"; // ma

            // liquids, fricatives, etc.
            tibtChar2Deva['\u0F61'] = "\u092F"; // ya
            tibtChar2Deva['\u0F62'] = "\u0930"; // ra
            tibtChar2Deva['\u0F63'] = "\u0932"; // la
            tibtChar2Deva['\u0F5D'] = "\u0935"; // va
            tibtChar2Deva['\u0F66'] = "\u0938"; // sa
            tibtChar2Deva['\u0F67'] = "\u0939"; // ha
            tibt2Deva["\u0F63\u0F39"] = "\u0933"; // l underdot a (multi-char)

            // dependent vowel signs (including multi-char sequences)
            tibtChar2Deva['\u0F71'] = "\u093E"; // aa (single)
            tibtChar2Deva['\u0F72'] = "\u093F"; // i (single)
            tibt2Deva["\u0F71\u0F72"] = "\u0940"; // ii
            tibtChar2Deva['\u0F74'] = "\u0941"; // u (single)
            tibt2Deva["\u0F71\u0F74"] = "\u0942"; // uu
            tibtChar2Deva['\u0F7A'] = "\u0947"; // e
            tibtChar2Deva['\u0F7C'] = "\u094B"; // o

            tibtChar2Deva['\u0F84'] = "\u094D"; // virama
            tibtChar2Deva['\u0F0D'] = "\u0964"; // danda
            tibtChar2Deva['\u0F0E'] = "\u0965"; // double danda

            // numerals
            tibtChar2Deva['\u0F20'] = "\u0966";
            tibtChar2Deva['\u0F21'] = "\u0967";
            tibtChar2Deva['\u0F22'] = "\u0968";
            tibtChar2Deva['\u0F23'] = "\u0969";
            tibtChar2Deva['\u0F24'] = "\u096A";
            tibtChar2Deva['\u0F25'] = "\u096B";
            tibtChar2Deva['\u0F26'] = "\u096C";
            tibtChar2Deva['\u0F27'] = "\u096D";
            tibtChar2Deva['\u0F28'] = "\u096E";
            tibtChar2Deva['\u0F29'] = "\u096F";

            // Build subjoined consonant mappings (U+0F90-0FB9 → halant + consonant)
            // These map subjoined forms back to virama + base consonant
            for (int i = 0; i <= 39; i++)
            {
                char subjoinedChar = (char)(0xF90 + i);
                char baseChar = (char)(0xF40 + i);

                // Map subjoined consonant to virama + base consonant in Tibetan
                // We'll convert this to Devanagari later
                tibtChar2Deva[subjoinedChar] = "\u0F84" + baseChar;
            }

            // Special cases for fixed-form subjoined consonants
            tibtChar2Deva['\u0FBB'] = "\u0F84\u0F61"; // subjoined ya (yya)
            tibtChar2Deva['\u0FBA'] = "\u0F84\u0F5D"; // subjoined va (vva)
        }

        public static string Convert(string tibtStr)
        {
            // Pre-processing: Handle special exceptions that use explicit halant
            // jjha: \u0F5B\u0F84\u0F5C → \u0F5B\u0FAC
            tibtStr = tibtStr.Replace("\u0F5B\u0F84\u0F5C", "\u0F5B\u0FAC");
            // yha: \u0F61\u0F84\u0F67 → \u0F61\u0FB7
            tibtStr = tibtStr.Replace("\u0F61\u0F84\u0F67", "\u0F61\u0FB7");
            // vha: \u0F5D\u0F84\u0F67 → \u0F5D\u0FB7
            tibtStr = tibtStr.Replace("\u0F5D\u0F84\u0F67", "\u0F5D\u0FB7");

            // Pre-processing: Handle fixed-form subjoined consonants
            // yya: \u0F61\u0FBB → \u0F61\u0FB1
            tibtStr = tibtStr.Replace("\u0F61\u0FBB", "\u0F61\u0FB1");
            // vva: \u0F5D\u0FBA → \u0F5D\u0FAD
            tibtStr = tibtStr.Replace("\u0F5D\u0FBA", "\u0F5D\u0FAD");

            // Remove intersyllabic tsheg (U+0F0B)
            tibtStr = tibtStr.Replace("\u0F0B", "");

            StringBuilder sb = new StringBuilder();
            int i = 0;

            while (i < tibtStr.Length)
            {
                bool matched = false;

                // Try four-character mappings first (e.g., independent vowels)
                if (i + 3 < tibtStr.Length)
                {
                    string fourChar = tibtStr.Substring(i, 4);
                    if (tibt2Deva.ContainsKey(fourChar))
                    {
                        sb.Append(tibt2Deva[fourChar]);
                        i += 4;
                        matched = true;
                    }
                }

                // Try three-character mappings
                if (!matched && i + 2 < tibtStr.Length)
                {
                    string threeChar = tibtStr.Substring(i, 3);
                    if (tibt2Deva.ContainsKey(threeChar))
                    {
                        sb.Append(tibt2Deva[threeChar]);
                        i += 3;
                        matched = true;
                    }
                }

                // Try two-character mappings (dependent vowels, l underdot)
                if (!matched && i + 1 < tibtStr.Length)
                {
                    string twoChar = tibtStr.Substring(i, 2);
                    if (tibt2Deva.ContainsKey(twoChar))
                    {
                        sb.Append(tibt2Deva[twoChar]);
                        i += 2;
                        matched = true;
                    }
                }

                if (!matched)
                {
                    // Try single character mapping
                    char c = tibtStr[i];
                    if (tibtChar2Deva.ContainsKey(c))
                    {
                        sb.Append(tibtChar2Deva[c]);
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

            string intermediateStr = sb.ToString();

            // Post-processing: Convert Tibetan virama+consonant sequences to Devanagari
            // At this point, subjoined consonants have been converted to \u0F84 + base Tibetan consonant
            // We need to convert these Tibetan sequences to Devanagari
            StringBuilder finalSb = new StringBuilder();
            i = 0;

            while (i < intermediateStr.Length)
            {
                // Check for Tibetan virama + consonant pattern
                if (i + 1 < intermediateStr.Length && intermediateStr[i] == '\u0F84')
                {
                    // Next char is Tibetan consonant, convert both to Devanagari
                    char tibConsonant = intermediateStr[i + 1];
                    if (tibtChar2Deva.ContainsKey(tibConsonant))
                    {
                        finalSb.Append("\u094D"); // Devanagari virama
                        finalSb.Append(tibtChar2Deva[tibConsonant]); // Devanagari consonant
                        i += 2;
                    }
                    else
                    {
                        // Shouldn't happen, but pass through
                        finalSb.Append(intermediateStr[i]);
                        i++;
                    }
                }
                else
                {
                    finalSb.Append(intermediateStr[i]);
                    i++;
                }
            }

            return finalSb.ToString();
        }
    }
}
