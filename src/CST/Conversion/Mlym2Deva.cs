using System;
using System.Collections;
using System.Text;

namespace CST.Conversion
{
    public static class Mlym2Deva
    {
        private static Hashtable mlym2Deva;

        static Mlym2Deva()
        {
            mlym2Deva = new Hashtable();

            // various signs
            mlym2Deva['\x0D02'] = '\x0902'; // anusvara
            mlym2Deva['\x0D03'] = '\x0903'; // visarga

            // independent vowels
            mlym2Deva['\x0D05'] = '\x0905'; // a
            mlym2Deva['\x0D06'] = '\x0906'; // aa
            mlym2Deva['\x0D07'] = '\x0907'; // i
            mlym2Deva['\x0D08'] = '\x0908'; // ii
            mlym2Deva['\x0D09'] = '\x0909'; // u
            mlym2Deva['\x0D0A'] = '\x090A'; // uu
            mlym2Deva['\x0D0B'] = '\x090B'; // vocalic r
            mlym2Deva['\x0D0C'] = '\x090C'; // vocalic l
            mlym2Deva['\x0D0F'] = '\x090F'; // e
            mlym2Deva['\x0D10'] = '\x0910'; // ai
            mlym2Deva['\x0D13'] = '\x0913'; // o
            mlym2Deva['\x0D14'] = '\x0914'; // au

            // velar stops
            mlym2Deva['\x0D15'] = '\x0915'; // ka
            mlym2Deva['\x0D16'] = '\x0916'; // kha
            mlym2Deva['\x0D17'] = '\x0917'; // ga
            mlym2Deva['\x0D18'] = '\x0918'; // gha
            mlym2Deva['\x0D19'] = '\x0919'; // n overdot a

            // palatal stops
            mlym2Deva['\x0D1A'] = '\x091A'; // ca
            mlym2Deva['\x0D1B'] = '\x091B'; // cha
            mlym2Deva['\x0D1C'] = '\x091C'; // ja
            mlym2Deva['\x0D1D'] = '\x091D'; // jha
            mlym2Deva['\x0D1E'] = '\x091E'; // ña

            // retroflex stops
            mlym2Deva['\x0D1F'] = '\x091F'; // t underdot a
            mlym2Deva['\x0D20'] = '\x0920'; // t underdot ha
            mlym2Deva['\x0D21'] = '\x0921'; // d underdot a
            mlym2Deva['\x0D22'] = '\x0922'; // d underdot ha
            mlym2Deva['\x0D23'] = '\x0923'; // n underdot a

            // dental stops
            mlym2Deva['\x0D24'] = '\x0924'; // ta
            mlym2Deva['\x0D25'] = '\x0925'; // tha
            mlym2Deva['\x0D26'] = '\x0926'; // da
            mlym2Deva['\x0D27'] = '\x0927'; // dha
            mlym2Deva['\x0D28'] = '\x0928'; // na

            // labial stops
            mlym2Deva['\x0D2A'] = '\x092A'; // pa
            mlym2Deva['\x0D2B'] = '\x092B'; // pha
            mlym2Deva['\x0D2C'] = '\x092C'; // ba
            mlym2Deva['\x0D2D'] = '\x092D'; // bha
            mlym2Deva['\x0D2E'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            mlym2Deva['\x0D2F'] = '\x092F'; // ya
            mlym2Deva['\x0D30'] = '\x0930'; // ra
            mlym2Deva['\x0D31'] = '\x0931'; // rra (Dravidian-specific)
            mlym2Deva['\x0D32'] = '\x0932'; // la
            mlym2Deva['\x0D33'] = '\x0933'; // l underdot a
            mlym2Deva['\x0D35'] = '\x0935'; // va
            mlym2Deva['\x0D36'] = '\x0936'; // sha (palatal)
            mlym2Deva['\x0D37'] = '\x0937'; // sha (retroflex)
            mlym2Deva['\x0D38'] = '\x0938'; // sa
            mlym2Deva['\x0D39'] = '\x0939'; // ha

            // dependent vowel signs
            mlym2Deva['\x0D3E'] = '\x093E'; // aa
            mlym2Deva['\x0D3F'] = '\x093F'; // i
            mlym2Deva['\x0D40'] = '\x0940'; // ii
            mlym2Deva['\x0D41'] = '\x0941'; // u
            mlym2Deva['\x0D42'] = '\x0942'; // uu
            mlym2Deva['\x0D43'] = '\x0943'; // vocalic r
            mlym2Deva['\x0D47'] = '\x0947'; // e
            mlym2Deva['\x0D48'] = '\x0948'; // ai
            mlym2Deva['\x0D4B'] = '\x094B'; // o
            mlym2Deva['\x0D4C'] = '\x094C'; // au

            // various signs
            mlym2Deva['\x0D4D'] = '\x094D'; // virama

            // additional vowels for Sanskrit
            mlym2Deva['\x0D60'] = '\x0960'; // vocalic rr
            mlym2Deva['\x0D61'] = '\x0961'; // vocalic ll

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // digits
            mlym2Deva['\x0D66'] = '\x0966';
            mlym2Deva['\x0D67'] = '\x0967';
            mlym2Deva['\x0D68'] = '\x0968';
            mlym2Deva['\x0D69'] = '\x0969';
            mlym2Deva['\x0D6A'] = '\x096A';
            mlym2Deva['\x0D6B'] = '\x096B';
            mlym2Deva['\x0D6C'] = '\x096C';
            mlym2Deva['\x0D6D'] = '\x096D';
            mlym2Deva['\x0D6E'] = '\x096E';
            mlym2Deva['\x0D6F'] = '\x096F';
        }

        public static string Convert(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (mlym2Deva.ContainsKey(c))
                    sb.Append(mlym2Deva[c]);
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
