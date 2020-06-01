using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Lucene.Net.Analysis;
using CST.Conversion;

namespace CST
{
    public class DevaXmlTokenizer : Tokenizer
    {
        private StringBuilder text;
        private int pos;
        private Dictionary<char, int> wordChars;

        public DevaXmlTokenizer(System.IO.TextReader reader): base(reader)
        {
            text = new StringBuilder(reader.ReadToEnd());

            wordChars = new Dictionary<char, int>();

            wordChars['\x0902'] = 1; // niggahita

            // independent vowels
            wordChars['\x0905'] = 1; // a
            wordChars['\x0906'] = 1; // aa
            wordChars['\x0907'] = 1; // i
            wordChars['\x0908'] = 1; // ii
            wordChars['\x0909'] = 1; // u
            wordChars['\x090A'] = 1; // uu
            wordChars['\x090F'] = 1; // e
            wordChars['\x0913'] = 1; // o

            // velar stops
            wordChars['\x0915'] = 1; // ka
            wordChars['\x0916'] = 1; // kha
            wordChars['\x0917'] = 1; // ga
            wordChars['\x0918'] = 1; // gha
            wordChars['\x0919'] = 1; // n overdot a

            // palatal stops
            wordChars['\x091A'] = 1; // ca
            wordChars['\x091B'] = 1; // cha
            wordChars['\x091C'] = 1; // ja
            wordChars['\x091D'] = 1; // jha
            wordChars['\x091E'] = 1; // ña

            // retroflex stops
            wordChars['\x091F'] = 1; // t underdot a
            wordChars['\x0920'] = 1; // t underdot ha
            wordChars['\x0921'] = 1; // d underdot a
            wordChars['\x0922'] = 1; // d underdot ha
            wordChars['\x0923'] = 1; // n underdot a

            // dental stops
            wordChars['\x0924'] = 1; // ta
            wordChars['\x0925'] = 1; // tha
            wordChars['\x0926'] = 1; // da
            wordChars['\x0927'] = 1; // dha
            wordChars['\x0928'] = 1; // na

            // labial stops
            wordChars['\x092A'] = 1; // pa
            wordChars['\x092B'] = 1; // pha
            wordChars['\x092C'] = 1; // ba
            wordChars['\x092D'] = 1; // bha
            wordChars['\x092E'] = 1; // ma

            // liquids, fricatives, etc.
            wordChars['\x092F'] = 1; // ya
            wordChars['\x0930'] = 1; // ra
            wordChars['\x0932'] = 1; // la
            wordChars['\x0935'] = 1; // va
            wordChars['\x0938'] = 1; // sa
            wordChars['\x0939'] = 1; // ha
            wordChars['\x0933'] = 1; // l underdot a

            // dependent vowel signs
            wordChars['\x093E'] = 1; // aa
            wordChars['\x093F'] = 1; // i
            wordChars['\x0940'] = 1; // ii
            wordChars['\x0941'] = 1; // u
            wordChars['\x0942'] = 1; // uu
            wordChars['\x0947'] = 1; // e
            wordChars['\x094B'] = 1; // o

            wordChars['\x094D'] = 1; // virama
            wordChars['\x200C'] = 1; // ZWNJ (ignore)
            wordChars['\x200D'] = 1; // ZWJ (ignore)

            wordChars['-'] = 1; // hyphen
            wordChars['’'] = 1; // right single quote (in words ending in ’ti or ’nti
			wordChars['_'] = 1; // placeholder for <hi rend="bold"> and </hi>, which can occur inside words
            FirstPass();

            pos = 0;
        }

        /// <summary>
        /// Removes XML and non-token characters from the document, replacing with spaces.
        /// Does not change the offsets of valid tokens.
        /// </summary>
        private void FirstPass()
        {
			string openHiBold = "<hi rend=\"bold\">";
			string closeHiBold = "</hi>";
			string str = text.ToString();
			str = str.Replace(openHiBold, "".PadRight(openHiBold.Length, '_'));
			str = str.Replace(closeHiBold, "".PadRight(closeHiBold.Length, '_'));
			text = new StringBuilder(str);

			bool inXml = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    inXml = true;
                    text[i] = ' ';
                }
                else if (text[i] == '>')
                {
                    inXml = false;
                    text[i] = ' ';
                }
                else if (inXml)
                {
                    text[i] = ' ';
                }
                else if (wordChars.ContainsKey(text[i]) == false)
                {
                    text[i] = ' ';
                }
            }
        }

        /// <summary>
        /// Returns the next token in the stream, or null at EOS.
        /// </summary>
        /// <returns>A Token</returns>
        public override Token Next()
        {
            if (pos >= text.Length)
                return null;

            // advance to a deva char
            while (pos < text.Length && wordChars.ContainsKey(text[pos]) == false)
                pos++;

            if (pos >= text.Length)
                return null;

            int startPos = pos;
            string token = "";
            while (pos < text.Length && wordChars.ContainsKey(text[pos]))
            {
                token += text[pos];
                pos++;
            }

            int endPos = pos - 1;
            // chop off any final hyphens or right single quotes and change the end offset
            while (token.EndsWith("-") || token.EndsWith("’"))
            {
                token = token.Substring(0, token.Length - 1);
                endPos--;
            }

            // remove quotes from within the token, but don't change the end offset
            token = token.Replace("’", "");

			// remove underscores from within the token, but don't change the offsets
			token = token.Replace("_", "");

			token = token.Trim();

            string ipeToken = Deva2Ipe.Convert(token);
            return new Token(ipeToken, startPos, endPos);
        }
    }
}
