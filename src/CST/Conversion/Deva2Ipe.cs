using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Ipe
    {
        private static Dictionary<string, string> deva2Ipe;

        static Deva2Ipe()
        {
            deva2Ipe = new Dictionary<string, string>();

            deva2Ipe["\x0902"] = "\x00C0"; // niggahita

            // independent vowels
            deva2Ipe["\x00C1"] = "\x00C1"; // a (the IPE inherent "a" is inserted by a regex. let it pass through.)
            deva2Ipe["\x0905"] = "\x00C1"; // a
            deva2Ipe["\x0906"] = "\x00C2"; // aa
            deva2Ipe["\x0907"] = "\x00C3"; // i
            deva2Ipe["\x0908"] = "\x00C4"; // ii
            deva2Ipe["\x0909"] = "\x00C5"; // u
            deva2Ipe["\x090A"] = "\x00C6"; // uu
            deva2Ipe["\x090F"] = "\x00C7"; // e
            deva2Ipe["\x0913"] = "\x00C8"; // o

            // velar stops
            deva2Ipe["\x0915"] = "\x00C9"; // ka
            deva2Ipe["\x0916"] = "\x00CA"; // kha
            deva2Ipe["\x0917"] = "\x00CB"; // ga
            deva2Ipe["\x0918"] = "\x00CC"; // gha
            deva2Ipe["\x0919"] = "\x00CD"; // n overdot a

            // palatal stops
            deva2Ipe["\x091A"] = "\x00CE"; // ca
            deva2Ipe["\x091B"] = "\x00CF"; // cha
            deva2Ipe["\x091C"] = "\x00D0"; // ja
            deva2Ipe["\x091D"] = "\x00D1"; // jha
            deva2Ipe["\x091E"] = "\x00D2"; // ña

            // retroflex stops
            deva2Ipe["\x091F"] = "\x00D3"; // t underdot a
            deva2Ipe["\x0920"] = "\x00D4"; // t underdot ha
            deva2Ipe["\x0921"] = "\x00D5"; // d underdot a
            deva2Ipe["\x0922"] = "\x00D6"; // d underdot ha
            // don"t use D7 multiplication sign
            deva2Ipe["\x0923"] = "\x00D8"; // n underdot a

            // dental stops
            deva2Ipe["\x0924"] = "\x00D9"; // ta
            deva2Ipe["\x0925"] = "\x00DA"; // tha
            deva2Ipe["\x0926"] = "\x00DB"; // da
            deva2Ipe["\x0927"] = "\x00DC"; // dha
            deva2Ipe["\x0928"] = "\x00DD"; // na

            // labial stops
            deva2Ipe["\x092A"] = "\x00DE"; // pa
            deva2Ipe["\x092B"] = "\x00DF"; // pha
            deva2Ipe["\x092C"] = "\x00E0"; // ba
            deva2Ipe["\x092D"] = "\x00E1"; // bha
            deva2Ipe["\x092E"] = "\x00E2"; // ma

            // liquids, fricatives, etc.
            deva2Ipe["\x092F"] = "\x00E3"; // ya
            deva2Ipe["\x0930"] = "\x00E4"; // ra
            deva2Ipe["\x0932"] = "\x00E5"; // la
            deva2Ipe["\x0935"] = "\x00E6"; // va
            deva2Ipe["\x0938"] = "\x00E7"; // sa
            deva2Ipe["\x0939"] = "\x00E8"; // ha
            deva2Ipe["\x0933"] = "\x00E9"; // l underdot a

            // dependent vowel signs
            deva2Ipe["\x093E"] = "\x00C2"; // aa
            deva2Ipe["\x093F"] = "\x00C3"; // i
            deva2Ipe["\x0940"] = "\x00C4"; // ii
            deva2Ipe["\x0941"] = "\x00C5"; // u
            deva2Ipe["\x0942"] = "\x00C6"; // uu
            deva2Ipe["\x0947"] = "\x00C7"; // e
            deva2Ipe["\x094B"] = "\x00C8"; // o

            deva2Ipe["\x094D"] = ""; // virama
            deva2Ipe["\x200C"] = ""; // ZWNJ (ignore)
            deva2Ipe["\x200D"] = ""; // ZWJ (ignore)
        }

        public static string Convert(string devStr)
        {
            // insert "a" after all consonants that are not followed by virama, dependent vowel or "a"
            // (This still works after we inserted ZWJ in the Devanagari. The ZWJ goes after virama.)
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])([^\x093E-\x094D\x00C1])", "$1\x00C1$2");
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])([^\x093E-\x094D\x00C1])", "$1\x00C1$2");
            // TODO: figure out how to backtrack so this replace doesn"t have to be done twice

            // insert a after consonant that is at end of string
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])$", "$1\x00C1");

            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr)
            {
                if (deva2Ipe.ContainsKey(c.ToString()))
                    sb.Append(deva2Ipe[c.ToString()]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
