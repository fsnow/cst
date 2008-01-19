using System;
using System.Collections;
using System.Text;

namespace CST.Conversion
{
    public static class Gujr2Deva
    {
        private static Hashtable gujr2Deva;

        static Gujr2Deva()
        {
            gujr2Deva = new Hashtable();

            // various signs
            gujr2Deva['\x0A82'] = '\x0902'; // niggahita

            // independent vowels
            gujr2Deva['\x0A85'] = '\x0905'; // a
            gujr2Deva['\x0A86'] = '\x0906'; // aa
            gujr2Deva['\x0A87'] = '\x0907'; // i
            gujr2Deva['\x0A88'] = '\x0908'; // ii
            gujr2Deva['\x0A89'] = '\x0909'; // u
            gujr2Deva['\x0A8A'] = '\x090A'; // uu
            gujr2Deva['\x0A8F'] = '\x090F'; // e
            gujr2Deva['\x0A93'] = '\x0913'; // o

            // velar stops
            gujr2Deva['\x0A95'] = '\x0915'; // ka
            gujr2Deva['\x0A96'] = '\x0916'; // kha
            gujr2Deva['\x0A97'] = '\x0917'; // ga
            gujr2Deva['\x0A98'] = '\x0918'; // gha
            gujr2Deva['\x0A99'] = '\x0919'; // n overdot a

            // palatal stops
            gujr2Deva['\x0A9A'] = '\x091A'; // ca
            gujr2Deva['\x0A9B'] = '\x091B'; // cha
            gujr2Deva['\x0A9C'] = '\x091C'; // ja
            gujr2Deva['\x0A9D'] = '\x091D'; // jha
            gujr2Deva['\x0A9E'] = '\x091E'; // ña

            // retroflex stops
            gujr2Deva['\x0A9F'] = '\x091F'; // t underdot a
            gujr2Deva['\x0AA0'] = '\x0920'; // t underdot ha
            gujr2Deva['\x0AA1'] = '\x0921'; // d underdot a
            gujr2Deva['\x0AA2'] = '\x0922'; // d underdot ha
            gujr2Deva['\x0AA3'] = '\x0923'; // n underdot a

            // dental stops
            gujr2Deva['\x0AA4'] = '\x0924'; // ta
            gujr2Deva['\x0AA5'] = '\x0925'; // tha
            gujr2Deva['\x0AA6'] = '\x0926'; // da
            gujr2Deva['\x0AA7'] = '\x0927'; // dha
            gujr2Deva['\x0AA8'] = '\x0928'; // na

            // labial stops
            gujr2Deva['\x0AAA'] = '\x092A'; // pa
            gujr2Deva['\x0AAB'] = '\x092B'; // pha
            gujr2Deva['\x0AAC'] = '\x092C'; // ba
            gujr2Deva['\x0AAD'] = '\x092D'; // bha
            gujr2Deva['\x0AAE'] = '\x092E'; // ma

            // liquids, fricatives, etc.
            gujr2Deva['\x0AAF'] = '\x092F'; // ya
            gujr2Deva['\x0AB0'] = '\x0930'; // ra
            gujr2Deva['\x0AB2'] = '\x0932'; // la
            gujr2Deva['\x0AB3'] = '\x0933'; // l underdot a
            gujr2Deva['\x0AB5'] = '\x0935'; // va
            gujr2Deva['\x0AB6'] = '\x0936'; // sha (palatal)
            gujr2Deva['\x0AB7'] = '\x0937'; // sha (retroflex)
            gujr2Deva['\x0AB8'] = '\x0938'; // sa
            gujr2Deva['\x0AB9'] = '\x0939'; // ha

            // dependent vowel signs
            gujr2Deva['\x0ABE'] = '\x093E'; // aa
            gujr2Deva['\x0ABF'] = '\x093F'; // i
            gujr2Deva['\x0AC0'] = '\x0940'; // ii
            gujr2Deva['\x0AC1'] = '\x0941'; // u
            gujr2Deva['\x0AC2'] = '\x0942'; // uu
            gujr2Deva['\x0AC7'] = '\x0947'; // e
            gujr2Deva['\x0ACB'] = '\x094B'; // o

            // various signs
            gujr2Deva['\x0ACD'] = '\x094D'; // virama

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // numerals
            gujr2Deva['\x0AE6'] = '\x0966';
            gujr2Deva['\x0AE7'] = '\x0967';
            gujr2Deva['\x0AE8'] = '\x0968';
            gujr2Deva['\x0AE9'] = '\x0969';
            gujr2Deva['\x0AEA'] = '\x096A';
            gujr2Deva['\x0AEB'] = '\x096B';
            gujr2Deva['\x0AEC'] = '\x096C';
            gujr2Deva['\x0AED'] = '\x096D';
            gujr2Deva['\x0AEE'] = '\x096E';
            gujr2Deva['\x0AEF'] = '\x096F';
        }

        public static string Convert(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str.ToCharArray())
            {
                if (gujr2Deva.ContainsKey(c))
                    sb.Append(gujr2Deva[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}