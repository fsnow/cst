using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
    public static class Ipe2Latn
    {
        private static Dictionary<string, string> ipe2Latn;

        // Fast lookup table for the optimized Convert(): map[c] = replacement string, null = pass through. (#86)
        private const int MapLen = 0x00F0; // covers the IPE code points (U+00C0..U+00EF)
        private static readonly string[] map = new string[MapLen];
        private static int maxValLen = 1;

        static Ipe2Latn()
        {
            ipe2Latn = new Dictionary<string, string>();

            ipe2Latn["\x00C0"] = "\x1E43"; // niggahita
            ipe2Latn["\x00C1"] = "\x1E25"; // visarga (#1049)

            // vowels
            ipe2Latn["\x00C2"] = "a"; // a
            ipe2Latn["\x00C3"] = "\x0101"; // aa
            ipe2Latn["\x00C4"] = "i"; // i
            ipe2Latn["\x00C5"] = "\x012B"; // ii
            ipe2Latn["\x00C6"] = "u"; // u
            ipe2Latn["\x00C7"] = "\x016B"; // uu
            ipe2Latn["\x00C8"] = "\x1E5B"; // vocalic r (#1049)
            ipe2Latn["\x00C9"] = "e"; // e
            ipe2Latn["\x00CA"] = "ai"; // ai: reads back as a + i, see Latn2Ipe (#1049)
            ipe2Latn["\x00CB"] = "o"; // o
            ipe2Latn["\x00CC"] = "au"; // au: reads back as a + u, see Latn2Ipe (#1049)

            // velar stops
            ipe2Latn["\x00CD"] = "k"; // ka
            ipe2Latn["\x00CE"] = "kh"; // kha
            ipe2Latn["\x00CF"] = "g"; // ga
            ipe2Latn["\x00D0"] = "gh"; // gha
            ipe2Latn["\x00D1"] = "\x1E45"; // n overdot a

            // palatal stops
            ipe2Latn["\x00D2"] = "c"; // ca
            ipe2Latn["\x00D3"] = "ch"; // cha
            ipe2Latn["\x00D4"] = "j"; // ja
            ipe2Latn["\x00D5"] = "jh"; // jha
            ipe2Latn["\x00D6"] = "\u00F1"; // n tilde a

            // retroflex stops
            ipe2Latn["\x00D8"] = "\x1E6D"; // t underdot a
            ipe2Latn["\x00D9"] = "\x1E6Dh"; // t underdot ha
            ipe2Latn["\x00DA"] = "\x1E0D"; // d underdot a
            ipe2Latn["\x00DB"] = "\x1E0Dh"; // d underdot ha
            // D7 multiplication sign is unused
            ipe2Latn["\x00DC"] = "\x1E47"; // n underdot a

            // dental stops
            ipe2Latn["\x00DD"] = "t"; // ta
            ipe2Latn["\x00DE"] = "th"; // tha
            ipe2Latn["\x00DF"] = "d"; // da
            ipe2Latn["\x00E0"] = "dh"; // dha
            ipe2Latn["\x00E1"] = "n"; // na

            // labial stops
            ipe2Latn["\x00E2"] = "p"; // pa
            ipe2Latn["\x00E3"] = "ph"; // pha
            ipe2Latn["\x00E4"] = "b"; // ba
            ipe2Latn["\x00E5"] = "bh"; // bha
            ipe2Latn["\x00E6"] = "m"; // ma

            // liquids, fricatives, etc.
            ipe2Latn["\x00E7"] = "y"; // ya
            ipe2Latn["\x00E8"] = "r"; // ra
            ipe2Latn["\x00E9"] = "l"; // la
            ipe2Latn["\x00EA"] = "v"; // va
            ipe2Latn["\x00EB"] = "\x015B"; // palatal sa (#1049)
            ipe2Latn["\x00EC"] = "\x1E63"; // retroflex sa (#1049)
            ipe2Latn["\x00ED"] = "s"; // sa
            ipe2Latn["\x00EE"] = "h"; // ha
            ipe2Latn["\x00EF"] = "\x1E37"; // l underdot a

            // Build the fast lookup table from the same single-char-keyed data (#86).
            foreach (var kvp in ipe2Latn)
                if (kvp.Key.Length == 1 && kvp.Key[0] < MapLen)
                {
                    map[kvp.Key[0]] = kvp.Value;
                    if (kvp.Value.Length > maxValLen) maxValLen = kvp.Value.Length;
                }
        }

        /// <summary>
        /// FROZEN reference implementation (the original readable version) - the correctness oracle for the
        /// optimized Convert(). Do NOT change; tests assert Convert == ConvertReference over the corpus. (#86)
        /// </summary>
        public static string ConvertReference(string ipe)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in ipe)
            {
                if (ipe2Latn.ContainsKey(c.ToString()))
                    sb.Append(ipe2Latn[c.ToString()]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        // Optimized single pass (#86): byte-identical to ConvertReference (verified by tests). Replaces the
        // per-char dictionary lookup (which allocated a string per char via c.ToString()) with one scan over a
        // char buffer using a string[] table.
        public static string Convert(string ipe)
        {
            if (string.IsNullOrEmpty(ipe))
                return ipe;

            int n = ipe.Length;
            var buf = new char[n * maxValLen];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                char c = ipe[i];
                string? m = (c < MapLen) ? map[c] : null;
                if (m == null) buf[k++] = c;
                else if (m.Length == 1) buf[k++] = m[0];
                else { m.CopyTo(0, buf, k, m.Length); k += m.Length; }
            }
            return new string(buf, 0, k);
        }
    }
}
