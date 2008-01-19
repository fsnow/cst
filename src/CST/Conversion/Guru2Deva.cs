using System;
using System.Collections;
using System.Text;

namespace CST.Conversion
{
    public static class Guru2Deva
    {
        private static Hashtable guru2Deva;

        static Guru2Deva()
        {
            guru2Deva = new Hashtable();

            // various signs
            guru2Deva['\x0A01'] = '\x0901'; // candrabindhu
            guru2Deva['\x0A02'] = '\x0902'; // anusvara
            guru2Deva['\x0A03'] = '\x0903'; // visarga

            // independent vowels
            guru2Deva['\x0A05'] = '\x0905'; // a
            guru2Deva['\x0A06'] = '\x0906'; // aa
            guru2Deva['\x0A07'] = '\x0907'; // i
            guru2Deva['\x0A08'] = '\x0908'; // ii
            guru2Deva['\x0A09'] = '\x0909'; // u
            guru2Deva['\x0A0A'] = '\x090A'; // uu
            guru2Deva['\x0A0F'] = '\x090F'; // e
            guru2Deva['\x0A10'] = '\x0910'; // ai
            guru2Deva['\x0A13'] = '\x0913'; // o
            guru2Deva['\x0A14'] = '\x0914'; // au

            // velar stops
            guru2Deva['\x0A15'] = '\x0915'; // ka
            guru2Deva['\x0A16'] = '\x0916'; // kha
            guru2Deva['\x0A17'] = '\x0917'; // ga
            guru2Deva['\x0A18'] = '\x0918'; // gha
            guru2Deva['\x0A19'] = '\x0919'; // n overdot a

            // palatal stops
            guru2Deva['\x0A1A'] = '\x091A'; // ca
            guru2Deva['\x0A1B'] = '\x091B'; // cha
            guru2Deva['\x0A1C'] = '\x091C'; // ja
            guru2Deva['\x0A1D'] = '\x091D'; // jha
            guru2Deva['\x0A1E'] = '\x091E'; // ña

            // retroflex stops
            guru2Deva['\x0A1F'] = '\x091F'; // t underdot a
            guru2Deva['\x0A20'] = '\x0920'; // t underdot ha
            guru2Deva['\x0A21'] = '\x0921'; // d underdot a
            guru2Deva['\x0A22'] = '\x0922'; // d underdot ha
            guru2Deva['\x0A23'] = '\x0923'; // n underdot a

            // dental stops
            guru2Deva['\x0A24'] = '\x0924'; // ta
            guru2Deva['\x0A25'] = '\x0925'; // tha
            guru2Deva['\x0A26'] = '\x0926'; // da
            guru2Deva['\x0A27'] = '\x0927'; // dha
            guru2Deva['\x0A28'] = '\x0928'; // na

            // labial stops
            guru2Deva['\x0A2A'] = '\x092A'; // pa
            guru2Deva['\x0A2B'] = '\x092B'; // pha
            guru2Deva['\x0A2C'] = '\x092C'; // ba
            guru2Deva['\x0A2D'] = '\x092D'; // bha
            guru2Deva['\x0A2E'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            guru2Deva['\x0A2F'] = '\x092F'; // ya
            guru2Deva['\x0A30'] = '\x0930'; // ra
            guru2Deva['\x0A32'] = '\x0932'; // la
            guru2Deva['\x0A33'] = '\x0933'; // l underdot a
            guru2Deva['\x0AB5'] = '\x0935'; // va
            guru2Deva['\x0A36'] = '\x0936'; // sha (palatal)
            guru2Deva['\x0A38'] = '\x0938'; // sa
            guru2Deva['\x0A39'] = '\x0939'; // ha

            // dependent vowel signs
            guru2Deva['\x0A3E'] = '\x093E'; // aa
            guru2Deva['\x0A3F'] = '\x093F'; // i
            guru2Deva['\x0A40'] = '\x0940'; // ii
            guru2Deva['\x0A41'] = '\x0941'; // u
            guru2Deva['\x0A42'] = '\x0942'; // uu
            guru2Deva['\x0A47'] = '\x0947'; // e
            guru2Deva['\x0A48'] = '\x0948'; // ai
            guru2Deva['\x0A4B'] = '\x094B'; // o
            guru2Deva['\x0A4C'] = '\x094C'; // au

            // various signs
            guru2Deva['\x0A4D'] = '\x094D'; // virama

            // let Devanagari danda (U+0964) and double danda (U+0965) 
            // pass through unmodified

            // digits
            guru2Deva['\x0A66'] = '\x0966';
            guru2Deva['\x0A67'] = '\x0967';
            guru2Deva['\x0A68'] = '\x0968';
            guru2Deva['\x0A69'] = '\x0969';
            guru2Deva['\x0A6A'] = '\x096A';
            guru2Deva['\x0A6B'] = '\x096B';
            guru2Deva['\x0A6C'] = '\x096C';
            guru2Deva['\x0A6D'] = '\x096D';
            guru2Deva['\x0A6E'] = '\x096E';
            guru2Deva['\x0A6F'] = '\x096F';
        }

        public static string Convert(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (guru2Deva.ContainsKey(c))
                    sb.Append(guru2Deva[c]);
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
