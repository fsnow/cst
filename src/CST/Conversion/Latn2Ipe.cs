using System;
using System.Collections.Generic;
using System.Text;
using CST.Collections;

namespace CST.Conversion
{
    public static class Latn2Ipe
    {
        private static Dictionary<string, string> latn2Ipe;
        private static Set latnAspiratables;

        static Latn2Ipe()
        {
            latn2Ipe = new Dictionary<string, string>();

            latn2Ipe["\x1E43"] = "\x00C0"; // niggahita

            // vowels
            latn2Ipe["a"] = "\x00C1"; // a
            latn2Ipe["\x0101"] = "\x00C2"; // aa
            latn2Ipe["i"] = "\x00C3"; // i
            latn2Ipe["\x012B"] = "\x00C4"; // ii
            latn2Ipe["u"] = "\x00C5"; // u
            latn2Ipe["\x016B"] = "\x00C6"; // uu
            latn2Ipe["e"] = "\x00C7"; // e
            latn2Ipe["o"] = "\x00C8"; // o

            // velar stops
            latn2Ipe["k"] = "\x00C9"; // ka
            latn2Ipe["kh"] = "\x00CA"; // kha
            latn2Ipe["g"] = "\x00CB"; // ga
            latn2Ipe["gh"] = "\x00CC"; // gha
            latn2Ipe["\x1E45"] = "\x00CD"; // n overdot a

            // palatal stops
            latn2Ipe["c"] = "\x00CE"; // ca
            latn2Ipe["ch"] = "\x00CF"; // cha
            latn2Ipe["j"] = "\x00D0"; // ja
            latn2Ipe["jh"] = "\x00D1"; // jha
            latn2Ipe["ñ"] = "\x00D2"; // ña

            // retroflex stops
            latn2Ipe["\x1E6D"] = "\x00D3"; // t underdot a
            latn2Ipe["\x1E6Dh"] = "\x00D4"; // t underdot ha
            latn2Ipe["\x1E0D"] = "\x00D5"; // d underdot a
            latn2Ipe["\x1E0Dh"] = "\x00D6"; // d underdot ha
            // D7 multiplication sign is unused
            latn2Ipe["\x1E47"] = "\x00D8"; // n underdot a

            // dental stops
            latn2Ipe["t"] = "\x00D9"; // ta
            latn2Ipe["th"] = "\x00DA"; // tha
            latn2Ipe["d"] = "\x00DB"; // da
            latn2Ipe["dh"] = "\x00DC"; // dha
            latn2Ipe["n"] = "\x00DD"; // na

            // labial stops
            latn2Ipe["p"] = "\x00DE"; // pa
            latn2Ipe["ph"] = "\x00DF"; // pha
            latn2Ipe["b"] = "\x00E0"; // ba
            latn2Ipe["bh"] = "\x00E1"; // bha
            latn2Ipe["m"] = "\x00E2"; // ma

            // liquids, fricatives, etc.
            latn2Ipe["y"] = "\x00E3"; // ya
            latn2Ipe["r"] = "\x00E4"; // ra
            latn2Ipe["l"] = "\x00E5"; // la
            latn2Ipe["v"] = "\x00E6"; // va
            latn2Ipe["s"] = "\x00E7"; // sa
            latn2Ipe["h"] = "\x00E8"; // ha
            latn2Ipe["\x1E37"] = "\x00E9"; // l underdot a

            latnAspiratables = new Set();
            latnAspiratables.Add('k');
            latnAspiratables.Add('g');
            latnAspiratables.Add('c');
            latnAspiratables.Add('j');
            latnAspiratables.Add('\x1E6D');
            latnAspiratables.Add('\x1E0D');
            latnAspiratables.Add('t');
            latnAspiratables.Add('d');
            latnAspiratables.Add('p');
            latnAspiratables.Add('b');
        }

        public static string Convert(string latn)
        {
            StringBuilder sb = new StringBuilder();
            char[] arr = latn.ToLower().ToCharArray();
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
    }
}
