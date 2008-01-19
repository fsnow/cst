using System;
using System.Collections;
using System.Text;

namespace CST.Conversion
{
    public static class Beng2Deva
    {
        private static Hashtable beng2Deva;

        static Beng2Deva()
        {
            beng2Deva = new Hashtable();

            beng2Deva['\x0982'] = '\x0902'; // niggahita

            // independent vowels
            beng2Deva['\x0985'] = '\x0905'; // a
            beng2Deva['\x0986'] = '\x0906'; // aa
            beng2Deva['\x0987'] = '\x0907'; // i
            beng2Deva['\x0988'] = '\x0908'; // ii
            beng2Deva['\x0989'] = '\x0909'; // u
            beng2Deva['\x098A'] = '\x090A'; // uu
            beng2Deva['\x098B'] = '\x090B'; // vocalic r
            beng2Deva['\x098C'] = '\x090C'; // vocalic l
            beng2Deva['\x098F'] = '\x090F'; // e
            beng2Deva['\x0990'] = '\x0910'; // ai
            beng2Deva['\x0993'] = '\x0913'; // o
            beng2Deva['\x0994'] = '\x0914'; // au

            // velar stops
            beng2Deva['\x0995'] = '\x0915'; // ka
            beng2Deva['\x0996'] = '\x0916'; // kha
            beng2Deva['\x0997'] = '\x0917'; // ga
            beng2Deva['\x0998'] = '\x0918'; // gha
            beng2Deva['\x0999'] = '\x0919'; // n overdot a

            // palatal stops
            beng2Deva['\x099A'] = '\x091A'; // ca
            beng2Deva['\x099B'] = '\x091B'; // cha
            beng2Deva['\x099C'] = '\x091C'; // ja
            beng2Deva['\x099D'] = '\x091D'; // jha
            beng2Deva['\x099E'] = '\x091E'; // ña

            // retroflex stops
            beng2Deva['\x099F'] = '\x091F'; // t underdot a
            beng2Deva['\x09A0'] = '\x0920'; // t underdot ha
            beng2Deva['\x09A1'] = '\x0921'; // d underdot a
            beng2Deva['\x09A2'] = '\x0922'; // d underdot ha
            beng2Deva['\x09A3'] = '\x0923'; // n underdot a

            // dental stops
            beng2Deva['\x09A4'] = '\x0924'; // ta
            beng2Deva['\x09A5'] = '\x0925'; // tha
            beng2Deva['\x09A6'] = '\x0926'; // da
            beng2Deva['\x09A7'] = '\x0927'; // dha
            beng2Deva['\x09A8'] = '\x0928'; // na

            // labial stops
            beng2Deva['\x09AA'] = '\x092A'; // pa
            beng2Deva['\x09AB'] = '\x092B'; // pha
            beng2Deva['\x09AC'] = '\x092C'; // ba
            beng2Deva['\x09AD'] = '\x092D'; // bha
            beng2Deva['\x09AE'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            beng2Deva['\x09AF'] = '\x092F'; // ya
            beng2Deva['\x09B0'] = '\x0930'; // ra
            beng2Deva['\x09B2'] = '\x0932'; // la

            // do the la with a String.Replace before the character replacement loop
            //beng2Deva["\x09B2\x09BC"] = '\x0933'; // l underdot a *** la with dot, there's no l underdot in Bengali***
            
            beng2Deva['\x09F0'] = '\x0935'; // va *** Bengali ra with middle diagonal. Used for Assamese. ***
            beng2Deva['\x09B6'] = '\x0936'; // sha (palatal)
            beng2Deva['\x09B7'] = '\x0937'; // sha (retroflex)
            beng2Deva['\x09B8'] = '\x0938'; // sa
            beng2Deva['\x09B9'] = '\x0939'; // ha

            // dependent vowel signs
            beng2Deva['\x09BE'] = '\x093E'; // aa
            beng2Deva['\x09BF'] = '\x093F'; // i
            beng2Deva['\x09C0'] = '\x0940'; // ii
            beng2Deva['\x09C1'] = '\x0941'; // u
            beng2Deva['\x09C2'] = '\x0942'; // uu
            beng2Deva['\x09C3'] = '\x0943'; // vocalic r
            beng2Deva['\x09C7'] = '\x0947'; // e
            beng2Deva['\x09C8'] = '\x0948'; // ai
            beng2Deva['\x09CB'] = '\x094B'; // o
            beng2Deva['\x09CC'] = '\x094C'; // au

            beng2Deva['\x09CD'] = '\x094D'; // virama

            // numerals
            beng2Deva['\x09E6'] = '\x0966';
            beng2Deva['\x09E7'] = '\x0967';
            beng2Deva['\x09E8'] = '\x0968';
            beng2Deva['\x09E9'] = '\x0969';
            beng2Deva['\x09EA'] = '\x096A';
            beng2Deva['\x09EB'] = '\x096B';
            beng2Deva['\x09EC'] = '\x096C';
            beng2Deva['\x09ED'] = '\x096D';
            beng2Deva['\x09EE'] = '\x096E';
            beng2Deva['\x09EF'] = '\x096F';
        }

        public static string Convert(string str)
        {
            // la with dot
            str = str.Replace("\x09B2\x09BC", "\x0933");

            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (beng2Deva.ContainsKey(c))
                    sb.Append(beng2Deva[c]);
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
