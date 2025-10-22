using System;
using System.Collections.Generic;
using System.Text;

namespace CST.Conversion
{
    public static class Telu2Deva
    {
        private static IDictionary<char, char> telu2Dev;

        static Telu2Deva()
        {
            telu2Dev = new Dictionary<char, char>();

            telu2Dev['\u0C02'] = '\u0902'; // niggahita

            // independent vowels
            telu2Dev['\u0C05'] = '\u0905'; // a
            telu2Dev['\u0C06'] = '\u0906'; // aa
            telu2Dev['\u0C07'] = '\u0907'; // i
            telu2Dev['\u0C08'] = '\u0908'; // ii
            telu2Dev['\u0C09'] = '\u0909'; // u
            telu2Dev['\u0C0A'] = '\u090A'; // uu
            telu2Dev['\u0C0F'] = '\u090F'; // e
            telu2Dev['\u0C13'] = '\u0913'; // o

            // velar stops
            telu2Dev['\u0C15'] = '\u0915'; // ka
            telu2Dev['\u0C16'] = '\u0916'; // kha
            telu2Dev['\u0C17'] = '\u0917'; // ga
            telu2Dev['\u0C18'] = '\u0918'; // gha
            telu2Dev['\u0C19'] = '\u0919'; // n overdot a

            // palatal stops
            telu2Dev['\u0C1A'] = '\u091A'; // ca
            telu2Dev['\u0C1B'] = '\u091B'; // cha
            telu2Dev['\u0C1C'] = '\u091C'; // ja
            telu2Dev['\u0C1D'] = '\u091D'; // jha
            telu2Dev['\u0C1E'] = '\u091E'; // n tilde a

            // retroflex stops
            telu2Dev['\u0C1F'] = '\u091F'; // t underdot a
            telu2Dev['\u0C20'] = '\u0920'; // t underdot ha
            telu2Dev['\u0C21'] = '\u0921'; // d underdot a
            telu2Dev['\u0C22'] = '\u0922'; // d underdot ha
            telu2Dev['\u0C23'] = '\u0923'; // n underdot a

            // dental stops
            telu2Dev['\u0C24'] = '\u0924'; // ta
            telu2Dev['\u0C25'] = '\u0925'; // tha
            telu2Dev['\u0C26'] = '\u0926'; // da
            telu2Dev['\u0C27'] = '\u0927'; // dha
            telu2Dev['\u0C28'] = '\u0928'; // na

            // labial stops
            telu2Dev['\u0C2A'] = '\u092A'; // pa
            telu2Dev['\u0C2B'] = '\u092B'; // pha
            telu2Dev['\u0C2C'] = '\u092C'; // ba
            telu2Dev['\u0C2D'] = '\u092D'; // bha
            telu2Dev['\u0C2E'] = '\u092E'; // ma

            // liquids, fricatives, etc.
            telu2Dev['\u0C2F'] = '\u092F'; // ya
            telu2Dev['\u0C30'] = '\u0930'; // ra
            telu2Dev['\u0C32'] = '\u0932'; // la
            telu2Dev['\u0C35'] = '\u0935'; // va
            telu2Dev['\u0C38'] = '\u0938'; // sa
            telu2Dev['\u0C39'] = '\u0939'; // ha
            telu2Dev['\u0C33'] = '\u0933'; // l underdot a

            // dependent vowel signs
            telu2Dev['\u0C3E'] = '\u093E'; // aa
            telu2Dev['\u0C3F'] = '\u093F'; // i
            telu2Dev['\u0C40'] = '\u0940'; // ii
            telu2Dev['\u0C41'] = '\u0941'; // u
            telu2Dev['\u0C42'] = '\u0942'; // uu
            telu2Dev['\u0C47'] = '\u0947'; // e
            telu2Dev['\u0C4B'] = '\u094B'; // o

            telu2Dev['\u0C4D'] = '\u094D'; // virama

            // numerals
            telu2Dev['\u0C66'] = '\u0966';
            telu2Dev['\u0C67'] = '\u0967';
            telu2Dev['\u0C68'] = '\u0968';
            telu2Dev['\u0C69'] = '\u0969';
            telu2Dev['\u0C6A'] = '\u096A';
            telu2Dev['\u0C6B'] = '\u096B';
            telu2Dev['\u0C6C'] = '\u096C';
            telu2Dev['\u0C6D'] = '\u096D';
            telu2Dev['\u0C6E'] = '\u096E';
            telu2Dev['\u0C6F'] = '\u096F';
        }

        public static string Convert(string teluStr)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in teluStr.ToCharArray())
            {
                if (telu2Dev.ContainsKey(c))
                    sb.Append(telu2Dev[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
