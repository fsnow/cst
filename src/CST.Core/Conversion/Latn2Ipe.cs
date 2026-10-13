using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Latn2Ipe
    {
        private static IDictionary<string, string> latn2Ipe;
        private static ISet<char> latnAspiratables;

        // Fast tables for the optimized Convert(): map1[c] = IPE char for a single Latin code point ('\0' =
        // pass through); map2[c] = IPE char for an aspirate (Latin consonant + 'h'). All IPE values are one
        // char. map2[c] != '\0' also signals that c is aspiratable. (#86)
        private const int MapLen = 0x1E70; // covers the Latin Pali letters incl. U+1E6D (t underdot)
        private static readonly char[] map1 = new char[MapLen];
        private static readonly char[] map2 = new char[MapLen];

        static Latn2Ipe()
        {
            latn2Ipe = new Dictionary<string, string>();

            latn2Ipe["\u1E43"] = "\u00C0"; // niggahita (m with dot below)
            latn2Ipe["\u1E41"] = "\u00C0"; // niggahita (m with dot above)
            latn2Ipe["\u1E25"] = "\u00C1"; // visarga (h with dot below) (#1049)

            // vowels
            latn2Ipe["a"] = "\u00C2"; // a
            latn2Ipe["\u0101"] = "\u00C3"; // aa
            latn2Ipe["i"] = "\u00C4"; // i
            latn2Ipe["\u012B"] = "\u00C5"; // ii
            latn2Ipe["u"] = "\u00C6"; // u
            latn2Ipe["\u016B"] = "\u00C7"; // uu
            latn2Ipe["\u1E5B"] = "\u00C8"; // vocalic r (r with dot below) (#1049)
            // ai and au are NOT read as the diphthongs: in Latin they keep meaning a + i / a + u, the Pali
            // vowel hiatus (about 7,850 in the corpus against about 67 diphthongs). fsnow, 2026-10-13: "Keep
            // Pali as is". (#1049)
            latn2Ipe["e"] = "\u00C9"; // e
            latn2Ipe["o"] = "\u00CB"; // o

            // velar stops
            latn2Ipe["k"] = "\u00CD"; // ka
            latn2Ipe["kh"] = "\u00CE"; // kha
            latn2Ipe["g"] = "\u00CF"; // ga
            latn2Ipe["gh"] = "\u00D0"; // gha
            latn2Ipe["\u1E45"] = "\u00D1"; // n overdot a

            // palatal stops
            latn2Ipe["c"] = "\u00D2"; // ca
            latn2Ipe["ch"] = "\u00D3"; // cha
            latn2Ipe["j"] = "\u00D4"; // ja
            latn2Ipe["jh"] = "\u00D5"; // jha
            latn2Ipe["\u00F1"] = "\u00D6"; // n tilde a

            // retroflex stops
            latn2Ipe["\u1E6D"] = "\u00D8"; // t underdot a
            latn2Ipe["\u1E6Dh"] = "\u00D9"; // t underdot ha
            latn2Ipe["\u1E0D"] = "\u00DA"; // d underdot a
            latn2Ipe["\u1E0Dh"] = "\u00DB"; // d underdot ha
            // D7 multiplication sign is unused in IPE
            latn2Ipe["\u1E47"] = "\u00DC"; // n underdot a

            // dental stops
            latn2Ipe["t"] = "\u00DD"; // ta
            latn2Ipe["th"] = "\u00DE"; // tha
            latn2Ipe["d"] = "\u00DF"; // da
            latn2Ipe["dh"] = "\u00E0"; // dha
            latn2Ipe["n"] = "\u00E1"; // na

            // labial stops
            latn2Ipe["p"] = "\u00E2"; // pa
            latn2Ipe["ph"] = "\u00E3"; // pha
            latn2Ipe["b"] = "\u00E4"; // ba
            latn2Ipe["bh"] = "\u00E5"; // bha
            latn2Ipe["m"] = "\u00E6"; // ma

            // liquids, fricatives, etc.
            latn2Ipe["y"] = "\u00E7"; // ya
            latn2Ipe["r"] = "\u00E8"; // ra
            latn2Ipe["l"] = "\u00E9"; // la
            latn2Ipe["v"] = "\u00EA"; // va
            latn2Ipe["\u015B"] = "\u00EB"; // palatal sa (#1049)
            latn2Ipe["\u1E63"] = "\u00EC"; // retroflex sa (#1049)
            latn2Ipe["s"] = "\u00ED"; // sa
            latn2Ipe["h"] = "\u00EE"; // ha
            latn2Ipe["\u1E37"] = "\u00EF"; // l underdot a

            latnAspiratables = new HashSet<char>();
            latnAspiratables.Add('k');
            latnAspiratables.Add('g');
            latnAspiratables.Add('c');
            latnAspiratables.Add('j');
            latnAspiratables.Add('\u1E6D'); // t underdot
            latnAspiratables.Add('\u1E0D'); // d underdot
            latnAspiratables.Add('t');
            latnAspiratables.Add('d');
            latnAspiratables.Add('p');
            latnAspiratables.Add('b');

            // Build the fast tables from the same data: single-char keys -> map1, "<c>h" aspirate keys -> map2.
            // Every latnAspiratables entry is the first char of a 2-char key, so map2 doubles as the test. (#86)
            foreach (var kvp in latn2Ipe)
            {
                if (kvp.Key.Length == 1) { if (kvp.Key[0] < MapLen) map1[kvp.Key[0]] = kvp.Value[0]; }
                else if (kvp.Key.Length == 2 && kvp.Key[1] == 'h' && kvp.Key[0] < MapLen) map2[kvp.Key[0]] = kvp.Value[0];
            }
        }

        /// <summary>
        /// FROZEN reference implementation (the original readable version) - the correctness oracle for the
        /// optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string latn)
        {
            StringBuilder sb = new StringBuilder();
            // Culture-invariant lower-casing: on tr/az locales the default ToLower maps 'I' to the dotless
            // 'ı' (U+0131), which has no latn2Ipe entry and would corrupt the term. (CORE-4)
            char[] arr = latn.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < arr.Length; i++)
            {
                char c = arr[i];
                if (i < arr.Length - 1 && latnAspiratables.Contains(c) && arr[i + 1] == 'h')
                {
                    string str = c.ToString() + "h";
                    if (latn2Ipe.ContainsKey(str))
                        sb.Append(latn2Ipe[str]);
                    i++;
                }
                else
                {
                    if (latn2Ipe.ContainsKey(c.ToString()))
                        sb.Append(latn2Ipe[c.ToString()]);
                    else
                        sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Replaces the
        // per-char c.ToString() dictionary lookups and the aspiratable HashSet with char[] tables. Uses the
        // same culture-invariant lower-casing as the reference so casing behaviour is identical. (CORE-4)
        public static string Convert(string latn)
        {
            if (string.IsNullOrEmpty(latn))
                return latn;

            string lower = latn.ToLowerInvariant();
            int n = lower.Length;
            var buf = new char[n]; // each input char -> at most 1 IPE char; an aspirate consumes 2 -> 1
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = lower[i];
                if (i < n - 1 && c < MapLen && map2[c] != '\0' && lower[i + 1] == 'h')
                {
                    buf[k++] = map2[c]; // aspirate: consonant + 'h' -> single IPE char
                    i++;
                }
                else
                {
                    char m = (c < MapLen) ? map1[c] : '\0';
                    buf[k++] = (m != '\0') ? m : c; // mapped IPE char, else pass through
                }
            }
            return new string(buf, 0, k);
        }
    }
}
