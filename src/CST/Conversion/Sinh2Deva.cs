using System;
using System.Collections;
using System.Text;

namespace CST.Conversion
{
    public static class Sinh2Deva
    {
        private static Hashtable sinh2Deva;

        static Sinh2Deva()
        {
            sinh2Deva = new Hashtable();

            sinh2Deva['\x0D82'] = '\x0902'; // niggahita

            // independent vowels
            sinh2Deva['\x0D85'] = '\x0905'; // a
            sinh2Deva['\x0D86'] = '\x0906'; // aa
            sinh2Deva['\x0D89'] = '\x0907'; // i
            sinh2Deva['\x0D8A'] = '\x0908'; // ii
            sinh2Deva['\x0D8B'] = '\x0909'; // u
            sinh2Deva['\x0D8C'] = '\x090A'; // uu
            sinh2Deva['\x0D91'] = '\x090F'; // e
            sinh2Deva['\x0D94'] = '\x0913'; // o

            // velar stops
            sinh2Deva['\x0D9A'] = '\x0915'; // ka
            sinh2Deva['\x0D9B'] = '\x0916'; // kha
            sinh2Deva['\x0D9C'] = '\x0917'; // ga
            sinh2Deva['\x0D9D'] = '\x0918'; // gha
            sinh2Deva['\x0D9E'] = '\x0919'; // n overdot a

            // palatal stops
            sinh2Deva['\x0DA0'] = '\x091A'; // ca
            sinh2Deva['\x0DA1'] = '\x091B'; // cha
            sinh2Deva['\x0DA2'] = '\x091C'; // ja
            sinh2Deva['\x0DA3'] = '\x091D'; // jha
            sinh2Deva['\x0DA4'] = '\x091E'; // ña

            // retroflex stops
            sinh2Deva['\x0DA7'] = '\x091F'; // t underdot a
            sinh2Deva['\x0DA8'] = '\x0920'; // t underdot ha
            sinh2Deva['\x0DA9'] = '\x0921'; // d underdot a
            sinh2Deva['\x0DAA'] = '\x0922'; // d underdot ha
            sinh2Deva['\x0DAB'] = '\x0923'; // n underdot a

            // dental stops
            sinh2Deva['\x0DAD'] = '\x0924'; // ta
            sinh2Deva['\x0DAE'] = '\x0925'; // tha
            sinh2Deva['\x0DAF'] = '\x0926'; // da
            sinh2Deva['\x0DB0'] = '\x0927'; // dha
            sinh2Deva['\x0DB1'] = '\x0928'; // na

            // labial stops
            sinh2Deva['\x0DB4'] = '\x092A'; // pa
            sinh2Deva['\x0DB5'] = '\x092B'; // pha
            sinh2Deva['\x0DB6'] = '\x092C'; // ba
            sinh2Deva['\x0DB7'] = '\x092D'; // bha
            sinh2Deva['\x0DB8'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            sinh2Deva['\x0DBA'] = '\x092F'; // ya
            sinh2Deva['\x0DBB'] = '\x0930'; // ra
            sinh2Deva['\x0DBD'] = '\x0932'; // la
            sinh2Deva['\x0DC0'] = '\x0935'; // va
            sinh2Deva['\x0DC3'] = '\x0938'; // sa
            sinh2Deva['\x0DC4'] = '\x0939'; // ha
            sinh2Deva['\x0DC5'] = '\x0933'; // l underdot a

            // dependent vowel signs
            sinh2Deva['\x0DCF'] = '\x093E'; // aa
            sinh2Deva['\x0DD2'] = '\x093F'; // i
            sinh2Deva['\x0DD3'] = '\x0940'; // ii
            sinh2Deva['\x0DD4'] = '\x0941'; // u
            sinh2Deva['\x0DD6'] = '\x0942'; // uu
            sinh2Deva['\x0DD9'] = '\x0947'; // e
            sinh2Deva['\x0DDC'] = '\x094B'; // o

            // various signs
            sinh2Deva['\x0DCA'] = '\x094D'; // Sinhala virama -> Dev. virama

            // zero-width joiners
            sinh2Deva['\x200C'] = ""; // ZWNJ (remove)
            sinh2Deva['\x200D'] = ""; // ZWJ (remove)
        }

        public static string Convert(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (sinh2Deva.ContainsKey(c))
                    sb.Append(sinh2Deva[c]);
                else
                    sb.Append(c);
            }

			str = sb.ToString();

			// insert ZWJ in some Devanagari conjuncts
			str = str.Replace("\x0915\x094D\x0915", "\x0915\x094D\x200D\x0915"); // ka + ka
			str = str.Replace("\x0915\x094D\x0932", "\x0915\x094D\x200D\x0932"); // ka + la
			str = str.Replace("\x0915\x094D\x0935", "\x0915\x094D\x200D\x0935"); // ka + va
			str = str.Replace("\x091A\x094D\x091A", "\x091A\x094D\x200D\x091A"); // ca + ca
			str = str.Replace("\x091C\x094D\x091C", "\x091C\x094D\x200D\x091C"); // ja + ja
			str = str.Replace("\x091E\x094D\x091A", "\x091E\x094D\x200D\x091A"); // ña + ca
			str = str.Replace("\x091E\x094D\x091C", "\x091E\x094D\x200D\x091C"); // ña + ja
			str = str.Replace("\x091E\x094D\x091E", "\x091E\x094D\x200D\x091E"); // ña + ña
			str = str.Replace("\x0928\x094D\x0928", "\x0928\x094D\x200D\x0928"); // na + na
			str = str.Replace("\x092A\x094D\x0932", "\x092A\x094D\x200D\x0932"); // pa + la
			str = str.Replace("\x0932\x094D\x0932", "\x0932\x094D\x200D\x0932"); // la + la

			return str;
        }
    }
}
