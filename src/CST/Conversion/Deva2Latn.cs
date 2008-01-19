using System;
using System.Collections;
using System.Configuration;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CST.Conversion
{
    public static class Deva2Latn
    {
        private static Hashtable deva2Latn;

        static Deva2Latn()
        {
            deva2Latn = new Hashtable();

            deva2Latn['\x0902'] = '\x1E43'; // niggahita

            // independent vowels
            deva2Latn['\x0905'] = 'a'; // a
            deva2Latn['\x0906'] = '\x0101'; // aa
            deva2Latn['\x0907'] = 'i'; // i
            deva2Latn['\x0908'] = '\x012B'; // ii
            deva2Latn['\x0909'] = 'u'; // u
			deva2Latn['\x0910'] = "ai"; // ai
            deva2Latn['\x090A'] = '\x016B'; // uu
            deva2Latn['\x090F'] = 'e'; // e
            deva2Latn['\x0913'] = 'o'; // o
			deva2Latn['\x0914'] = "au"; // au

            // velar stops
            deva2Latn['\x0915'] = 'k'; // ka
            deva2Latn['\x0916'] = "kh"; // kha
            deva2Latn['\x0917'] = 'g'; // ga
            deva2Latn['\x0918'] = "gh"; // gha
            deva2Latn['\x0919'] = "\x1E45"; // n overdot a
            
            // palatal stops
            deva2Latn['\x091A'] = 'c'; // ca
            deva2Latn['\x091B'] = "ch"; // cha
            deva2Latn['\x091C'] = 'j'; // ja
            deva2Latn['\x091D'] = "jh"; // jha
            deva2Latn['\x091E'] = 'ñ'; // ña

            // retroflex stops
            deva2Latn['\x091F'] = '\x1E6D'; // t underdot a
            deva2Latn['\x0920'] = "\x1E6Dh"; // t underdot ha
            deva2Latn['\x0921'] = '\x1E0D'; // d underdot a
            deva2Latn['\x0922'] = "\x1E0Dh"; // d underdot ha
            deva2Latn['\x0923'] = '\x1E47'; // n underdot a

            // dental stops
            deva2Latn['\x0924'] = 't'; // ta
            deva2Latn['\x0925'] = "th"; // tha
            deva2Latn['\x0926'] = 'd'; // da
            deva2Latn['\x0927'] = "dh"; // dha
            deva2Latn['\x0928'] = 'n'; // na

            // labial stops
            deva2Latn['\x092A'] = 'p'; // pa
            deva2Latn['\x092B'] = "ph"; // pha
            deva2Latn['\x092C'] = 'b'; // ba
            deva2Latn['\x092D'] = "bh"; // bha
            deva2Latn['\x092E'] = 'm'; // ma

            // liquids, fricatives, etc.
            deva2Latn['\x092F'] = 'y'; // ya
            deva2Latn['\x0930'] = 'r'; // ra
            deva2Latn['\x0932'] = 'l'; // la
            deva2Latn['\x0935'] = 'v'; // va
            deva2Latn['\x0938'] = 's'; // sa
            deva2Latn['\x0939'] = 'h'; // ha
            deva2Latn['\x0933'] = "\x1E37"; // l underdot a

            // dependent vowel signs
            deva2Latn['\x093E'] = '\x0101'; // aa
            deva2Latn['\x093F'] = 'i'; // i
            deva2Latn['\x0940'] = "\x012B"; // ii
            deva2Latn['\x0941'] = 'u'; // u
            deva2Latn['\x0942'] = '\x016B'; // uu
            deva2Latn['\x0947'] = 'e'; // e
			deva2Latn['\x0948'] = "ai"; // ai
            deva2Latn['\x094B'] = 'o'; // o
			deva2Latn['\x094C'] = "au"; // au

            deva2Latn['\x094D'] = ""; // virama

            // numerals
            deva2Latn['\x0966'] = '0';
            deva2Latn['\x0967'] = '1';
            deva2Latn['\x0968'] = '2';
            deva2Latn['\x0969'] = '3';
            deva2Latn['\x096A'] = '4';
            deva2Latn['\x096B'] = '5';
            deva2Latn['\x096C'] = '6';
            deva2Latn['\x096D'] = '7';
            deva2Latn['\x096E'] = '8';
            deva2Latn['\x096F'] = '9';

            // we let dandas and double dandas pass through and handle
            // them in ConvertDandas()
            //deva2Latn['\x0964'] = '.'; // danda 
            deva2Latn['\x0970'] = '.'; // Devanagari abbreviation sign
            
            deva2Latn['\x200C'] = ""; // ZWNJ (ignore)
            deva2Latn['\x200D'] = ""; // ZWJ (ignore)
        }

        public static string ConvertBook(string devStr)
        {
            // mark the Devanagari text for programmatic capitalization
            LatinCapitalizer capitalizer = new LatinCapitalizer(
                new string[] {"p", "head", "trailer" }, 
                new string[] {"note"},
                "\x4676");
            devStr = capitalizer.MarkCapitals(devStr);

            // remove Dev abbreviation sign before an ellipsis. We don't want a 4th dot after pe.
            devStr = devStr.Replace("\x0970\x2026", "\x2026");

            string str = Convert(devStr);

            str = capitalizer.Capitalize(str);
            str = ConvertDandas(str);
            str = CleanupPunctuation(str);

            // convert "nti to n"ti, per Dhananjay email 3 Aug 07
            // commenting out per Ramnath/Priti email  29 Aug 07
            //str = Regex.Replace(str, "([’”]*)nti", "n$1ti");

            return str;
        }

        // more generalized, reusable conversion method:
        // no stylesheet modifications, capitalization, etc.
        public static string Convert(string devStr)
        {
            // insert 'a' after all consonants that are not followed by virama, dependent vowel or 'a'
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])([^\x093E-\x094Da])", "$1a$2", RegexOptions.Compiled);
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])([^\x093E-\x094Da])", "$1a$2", RegexOptions.Compiled);
            // TODO: figure out how to backtrack so this replace doesn't have to be done twice

            // subtle bug not encountered in Tipitaka files:
            // insert a after consonant that is at end of string
            devStr = Regex.Replace(devStr, "([\x0915-\x0939])$", "$1a", RegexOptions.Compiled);

            StringBuilder sb = new StringBuilder();
            foreach (char c in devStr.ToCharArray())
            {
                if (deva2Latn.ContainsKey(c))
                    sb.Append(deva2Latn[c]);
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
