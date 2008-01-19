using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Mlym
    {
        private static Hashtable deva2Mlym;

        static Deva2Mlym()
        {
            deva2Mlym = new Hashtable();

            // various signs
            deva2Mlym['\x0902'] = '\x0D02'; // anusvara
            deva2Mlym['\x0903'] = '\x0D03'; // visarga

            // independent vowels
            deva2Mlym['\x0905'] = '\x0D05'; // a
            deva2Mlym['\x0906'] = '\x0D06'; // aa
            deva2Mlym['\x0907'] = '\x0D07'; // i
            deva2Mlym['\x0908'] = '\x0D08'; // ii
            deva2Mlym['\x0909'] = '\x0D09'; // u
            deva2Mlym['\x090A'] = '\x0D0A'; // uu
            deva2Mlym['\x090B'] = '\x0D0B'; // vocalic r
            deva2Mlym['\x090C'] = '\x0D0C'; // vocalic l
            deva2Mlym['\x090F'] = '\x0D0F'; // e
            deva2Mlym['\x0910'] = '\x0D10'; // ai
            deva2Mlym['\x0913'] = '\x0D13'; // o
            deva2Mlym['\x0914'] = '\x0D14'; // au

            // velar stops
            deva2Mlym['\x0915'] = '\x0D15'; // ka
            deva2Mlym['\x0916'] = '\x0D16'; // kha
            deva2Mlym['\x0917'] = '\x0D17'; // ga
            deva2Mlym['\x0918'] = '\x0D18'; // gha
            deva2Mlym['\x0919'] = '\x0D19'; // n overdot a
 
            // palatal stops
            deva2Mlym['\x091A'] = '\x0D1A'; // ca
            deva2Mlym['\x091B'] = '\x0D1B'; // cha
            deva2Mlym['\x091C'] = '\x0D1C'; // ja
            deva2Mlym['\x091D'] = '\x0D1D'; // jha
            deva2Mlym['\x091E'] = '\x0D1E'; // ña

            // retroflex stops
            deva2Mlym['\x091F'] = '\x0D1F'; // t underdot a
            deva2Mlym['\x0920'] = '\x0D20'; // t underdot ha
            deva2Mlym['\x0921'] = '\x0D21'; // d underdot a
            deva2Mlym['\x0922'] = '\x0D22'; // d underdot ha
            deva2Mlym['\x0923'] = '\x0D23'; // n underdot a

            // dental stops
            deva2Mlym['\x0924'] = '\x0D24'; // ta
            deva2Mlym['\x0925'] = '\x0D25'; // tha
            deva2Mlym['\x0926'] = '\x0D26'; // da
            deva2Mlym['\x0927'] = '\x0D27'; // dha
            deva2Mlym['\x0928'] = '\x0D28'; // na

            // labial stops
            deva2Mlym['\x092A'] = '\x0D2A'; // pa
            deva2Mlym['\x092B'] = '\x0D2B'; // pha
            deva2Mlym['\x092C'] = '\x0D2C'; // ba
            deva2Mlym['\x092D'] = '\x0D2D'; // bha
            deva2Mlym['\x092E'] = '\x0D2E'; // ma

            // liquids, fricatives, etc.
            deva2Mlym['\x092F'] = '\x0D2F'; // ya
            deva2Mlym['\x0930'] = '\x0D30'; // ra
            deva2Mlym['\x0931'] = '\x0D31'; // rra (Dravidian-specific)
            deva2Mlym['\x0932'] = '\x0D32'; // la
            deva2Mlym['\x0933'] = '\x0D33'; // l underdot a
            deva2Mlym['\x0935'] = '\x0D35'; // va
            deva2Mlym['\x0936'] = '\x0D36'; // sha (palatal)
            deva2Mlym['\x0937'] = '\x0D37'; // sha (retroflex)
            deva2Mlym['\x0938'] = '\x0D38'; // sa
            deva2Mlym['\x0939'] = '\x0D39'; // ha

            // dependent vowel signs
            deva2Mlym['\x093E'] = '\x0D3E'; // aa
            deva2Mlym['\x093F'] = '\x0D3F'; // i
            deva2Mlym['\x0940'] = '\x0D40'; // ii
            deva2Mlym['\x0941'] = '\x0D41'; // u
            deva2Mlym['\x0942'] = '\x0D42'; // uu
            deva2Mlym['\x0943'] = '\x0D43'; // vocalic r
            deva2Mlym['\x0947'] = '\x0D47'; // e
            deva2Mlym['\x0948'] = '\x0D48'; // ai
            deva2Mlym['\x094B'] = '\x0D4B'; // o
            deva2Mlym['\x094C'] = '\x0D4C'; // au

            // various signs
            deva2Mlym['\x094D'] = '\x0D4D'; // virama

            // additional vowels for Sanskrit
            deva2Mlym['\x0960'] = '\x0D60'; // vocalic rr
            deva2Mlym['\x0961'] = '\x0D61'; // vocalic ll

            // we let dandas (U+0964) and double dandas (U+0965) pass through 
            // and handle them in ConvertDandas()

            // digits
            deva2Mlym['\x0966'] = '\x0D66';
            deva2Mlym['\x0967'] = '\x0D67';
            deva2Mlym['\x0968'] = '\x0D68';
            deva2Mlym['\x0969'] = '\x0D69';
            deva2Mlym['\x096A'] = '\x0D6A';
            deva2Mlym['\x096B'] = '\x0D6B';
            deva2Mlym['\x096C'] = '\x0D6C';
            deva2Mlym['\x096D'] = '\x0D6D';
            deva2Mlym['\x096E'] = '\x0D6E';
            deva2Mlym['\x096F'] = '\x0D6F';

            // zero-width joiners
            deva2Mlym['\x200C'] = ""; // ZWNJ (remove)
            deva2Mlym['\x200D'] = ""; // ZWJ (remove)
        }

        public static string ConvertBook(string devStr)
        {
            // change name of stylesheet for Gurmukhi
            devStr = devStr.Replace("tipitaka-deva.xsl", "tipitaka-mlym.xsl");

            string str = Convert(devStr);

            str = ConvertDandas(str);
            return CleanupPunctuation(str);
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        public static string Convert(string devStr)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (deva2Mlym.ContainsKey(c))
                    sb.Append(deva2Mlym[c]);
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        public static string ConvertDandas(string str)
        {
            // in gathas, single dandas convert to semicolon, double to period
            // Regex note: the +? is the lazy quantifier which finds the shortest match
            str = Regex.Replace(str, "<p rend=\"gatha[a-z0-9]*\".+?</p>",
                new MatchEvaluator(ConvertGathaDandas), RegexOptions.Compiled);

            // remove double dandas around namo tassa
            str = Regex.Replace(str, "<p rend=\"centre\".+?</p>",
                new MatchEvaluator(RemoveNamoTassaDandas), RegexOptions.Compiled);

            // convert all others to period
            str = str.Replace("\x0964", ".");
            str = str.Replace("\x0965", ".");
            return str;
        }

        public static string ConvertGathaDandas(Match m)
        {
            string str = m.Value;
            str = str.Replace("\x0964", ";");
            str = str.Replace("\x0965", ".");
            return str;
        }

        public static string RemoveNamoTassaDandas(Match m)
        {
            string str = m.Value;
            return str.Replace("\x0965", "");
        }

        // There should be no spaces before these
        // punctuation marks. 
        public static string CleanupPunctuation(string str)
        {
			// two spaces to one
			str = str.Replace("  ", " ");

            str = str.Replace(" ,", ",");
            str = str.Replace(" ?", "?");
            str = str.Replace(" !", "!");
            str = str.Replace(" ;", ";");
            // does not affect peyyalas because they have ellipses now
            str = str.Replace(" .", ".");
            return str;
        }
    }
}
