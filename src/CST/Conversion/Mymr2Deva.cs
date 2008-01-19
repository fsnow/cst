using System;
using System.Collections;
using System.IO;
using System.Text;

namespace CST.Conversion
{
    public static class Mymr2Deva
    {
        private static Hashtable mymr2Deva;

        static Mymr2Deva()
        {
            mymr2Deva = new Hashtable();

            // velar stops
            mymr2Deva['\x1000'] = '\x0915'; // ka
            mymr2Deva['\x1001'] = '\x0916'; // kha
            mymr2Deva['\x1002'] = '\x0917'; // ga
            mymr2Deva['\x1003'] = '\x0918'; // gha
            mymr2Deva['\x1004'] = '\x0919'; // n overdot a
            
            // palatal stops
            mymr2Deva['\x1005'] = '\x091A'; // ca
            mymr2Deva['\x1006'] = '\x091B'; // cha
            mymr2Deva['\x1007'] = '\x091C'; // ja
            mymr2Deva['\x1008'] = '\x091D'; // jha
            mymr2Deva['\x1009'] = '\x091E'; // ña
            mymr2Deva['\x100A'] = "\x1009\x1039\x1009"; // double ña

            // retroflex stops
            mymr2Deva['\x100B'] = '\x091F'; // t underdot a
            mymr2Deva['\x100C'] = '\x0920'; // t underdot ha
            mymr2Deva['\x100D'] = '\x0921'; // d underdot a
            mymr2Deva['\x100E'] = '\x0922'; // d underdot ha
            mymr2Deva['\x100F'] = '\x0923'; // n underdot a

            // dental stops
            mymr2Deva['\x1010'] = '\x0924'; // ta
            mymr2Deva['\x1011'] = '\x0925'; // tha
            mymr2Deva['\x1012'] = '\x0926'; // da
            mymr2Deva['\x1013'] = '\x0927'; // dha
            mymr2Deva['\x1014'] = '\x0928'; // na

            // labial stops
            mymr2Deva['\x1015'] = '\x092A'; // pa
            mymr2Deva['\x1016'] = '\x092B'; // pha
            mymr2Deva['\x1017'] = '\x092C'; // ba
            mymr2Deva['\x1018'] = '\x092D'; // bha
            mymr2Deva['\x1019'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            mymr2Deva['\x101A'] = '\x092F'; // ya
            mymr2Deva['\x101B'] = '\x0930'; // ra
            mymr2Deva['\x101C'] = '\x0932'; // la
            mymr2Deva['\x101D'] = '\x0935'; // va
            mymr2Deva['\x101E'] = '\x0938'; // sa
            mymr2Deva['\x101F'] = '\x0939'; // ha
            mymr2Deva['\x1020'] = '\x0933'; // l underdot a

            // independent vowels
            mymr2Deva['\x1021'] = '\x0905'; // a
            //deva2Mymr['\x0906'] = "\x1021\x102C"; // aa
            mymr2Deva['\x1023'] = '\x0907'; // i
            mymr2Deva['\x1024'] = '\x0908'; // ii
            mymr2Deva['\x1025'] = '\x0909'; // u
            mymr2Deva['\x1026'] = '\x090A'; // uu
            mymr2Deva['\x1027'] = '\x090F'; // e
            mymr2Deva['\x1029'] = '\x0913'; // o

            // dependent vowel signs
            mymr2Deva['\x102C'] = '\x093E'; // aa
            mymr2Deva['\x102D'] = '\x093F'; // i
            mymr2Deva['\x102E'] = '\x0940'; // ii
            mymr2Deva['\x102F'] = '\x0941'; // u
            mymr2Deva['\x1030'] = '\x0942'; // uu
            mymr2Deva['\x1031'] = '\x0947'; // e
            //deva2Mymr['\x094B'] = "\x1031\x102C"; // o

            // numerals
            mymr2Deva['\x1040'] = '\x0966';
            mymr2Deva['\x1041'] = '\x0967';
            mymr2Deva['\x1042'] = '\x0968';
            mymr2Deva['\x1043'] = '\x0969';
            mymr2Deva['\x1044'] = '\x096A';
            mymr2Deva['\x1045'] = '\x096B';
            mymr2Deva['\x1046'] = '\x096C';
            mymr2Deva['\x1047'] = '\x096D';
            mymr2Deva['\x1048'] = '\x096E';
            mymr2Deva['\x1049'] = '\x096F';

            // other
            mymr2Deva['\x104A'] = '\x0964'; // danda
            mymr2Deva['\x1036'] = '\x0902'; // niggahita
            mymr2Deva['\x1039'] = '\x094D'; // virama
            //deva2Mymr['\x200C'] = ""; // ZWNJ (ignore)
            //deva2Mymr['\x200D'] = ""; // ZWJ (ignore)
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        public static string Convert(string str)
        {
			str = str.Replace("\x1021\x102C", "\x0906");
			str = str.Replace("\x1031\x102C", "\x094B");

            StringBuilder sb = new StringBuilder();
			foreach (char c in str.ToCharArray())
            {
                if (mymr2Deva.ContainsKey(c))
                    sb.Append(mymr2Deva[c]);
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
