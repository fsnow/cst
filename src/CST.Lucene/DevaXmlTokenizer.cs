using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using CST.Conversion;

namespace CST
{
    public class DevaXmlTokenizer : Tokenizer
    {
        private StringBuilder text;

        // this is the index into the book string
        private int pos;

        private ImmutableHashSet<char> wordChars;

        // this tokenizer generates three attributes:
        // term offset, positionIncrement and type
        private ICharTermAttribute termAtt;
        private IOffsetAttribute offsetAtt;
        private IPositionIncrementAttribute posIncrAtt;
        private ITypeAttribute typeAtt;

        /// <summary>
        /// Creates a new instance of the <see cref="DevaXmlTokenizer"/>.  Attaches
        /// the <paramref name="input"/> to the newly created JFlex-generated (then ported to .NET) scanner.
        /// </summary>
        /// <param name="matchVersion"> Lucene compatibility version - See <see cref="DevaXmlTokenizer"/> </param>
        /// <param name="input"> The input reader
        public DevaXmlTokenizer(LuceneVersion matchVersion, TextReader input)
            : base(input)
        {
            //Init(input);
            InitInstance(input);
        }

        /// <summary>
        /// Creates a new <see cref="DevaXmlTokenizer"/> with a given <see cref="AttributeSource.AttributeFactory"/> 
        /// </summary>
        public DevaXmlTokenizer(LuceneVersion matchVersion, AttributeFactory factory, TextReader input)
            : base(factory, input)
        {
            //Init(input);
            InitInstance(input);
        }

        private void InitInstance(TextReader input)
        {
            termAtt = AddAttribute<ICharTermAttribute>();
            posIncrAtt = AddAttribute<IPositionIncrementAttribute>();
            offsetAtt = AddAttribute<IOffsetAttribute>();
            typeAtt = AddAttribute<ITypeAttribute>();

            HashSet<char> tempHashSet = new HashSet<char>
            {
                '\x0902', // niggahita

                // independent vowels
                '\x0905', // a
                '\x0906', // aa
                '\x0907', // i
                '\x0908', // ii
                '\x0909', // u
                '\x090A', // uu
                '\x090F', // e
                '\x0913', // o

                // velar stops
                '\x0915', // ka
                '\x0916', // kha
                '\x0917', // ga
                '\x0918', // gha
                '\x0919', // n overdot a

                // palatal stops
                '\x091A', // ca
                '\x091B', // cha
                '\x091C', // ja
                '\x091D', // jha
                '\x091E', // n(tilde)a

                // retroflex stops
                '\x091F', // t underdot a
                '\x0920', // t underdot ha
                '\x0921', // d underdot a
                '\x0922', // d underdot ha
                '\x0923', // n underdot a

                // dental stops
                '\x0924', // ta
                '\x0925', // tha
                '\x0926', // da
                '\x0927', // dha
                '\x0928', // na

                // labial stops
                '\x092A', // pa
                '\x092B', // pha
                '\x092C', // ba
                '\x092D', // bha
                '\x092E', // ma

                // liquids, fricatives, etc.
                '\x092F', // ya
                '\x0930', // ra
                '\x0932', // la
                '\x0935', // va
                '\x0938', // sa
                '\x0939', // ha
                '\x0933', // l underdot a

                // dependent vowel signs
                '\x093E', // aa
                '\x093F', // i
                '\x0940', // ii
                '\x0941', // u
                '\x0942', // uu
                '\x0947', // e
                '\x094B', // o

                '\x094D', // virama
                '\x200C', // ZWNJ (ignore)
                '\x200D', // ZWJ (ignore)

                '-', // hyphen
                '\x2019', // right single quote (in words ending in (quote)ti or (quote)nti
                '_' // placeholder for <hi rend="bold"> and </hi>, which can occur inside words
            };
            wordChars = tempHashSet.ToImmutableHashSet<char>();

            InitPerBook(input);
        }

        public void InitPerBook(TextReader input)
        {
            text = new StringBuilder(input.ReadToEnd());

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
                else if (wordChars.Contains(text[i]) == false)
                {
                    text[i] = ' ';
                }
            }
        }

        /// <summary>
        /// Sets attributes of the next token in the stream and returns true, or false at EOS.
        /// </summary>
        /// <returns>True if there was another token</returns>
        public override sealed bool IncrementToken()
        {
            ClearAttributes();

            while (true)
            {
                if (pos >= text.Length)
                {
                    return false;
                }

                // advance to a deva char
                while (pos < text.Length && wordChars.Contains(text[pos]) == false)
                    pos++;

                if (pos >= text.Length)
                {
                    return false;
                }

                int startPos = pos;
                string token = "";
                while (pos < text.Length && wordChars.Contains(text[pos]))
                {
                    token += text[pos];
                    pos++;
                }

                string foo = token;

                // chop off leading underscores and change the start offset
                while (token.StartsWith("_"))
                {
                    token = token.Substring(1);
                    startPos++;
                }

                int endPos = pos - 1;
                // chop off any final hyphens, right single quotes or underscores
                // and change the end offset
                while (token.EndsWith("-") || token.EndsWith("\x2019") || token.EndsWith("_"))
                {
                    token = token.Substring(0, token.Length - 1);
                    endPos--;
                }

                // remove quotes from within the token, but don't change the end offset
                token = token.Replace("\x2019", "");

                // remove underscores from within the token, but don't change the offsets
                token = token.Replace("_", "");

                token = token.Trim();

                if (token.Length > 0)
                {
                    // We store the token in IPE, "Ideal Pali Encoding".
                    // The offsets are of the original Devanagari word, because we
                    // need those to be correct for search term highlighting
                    string ipeToken = Deva2Ipe.Convert(token);

                    termAtt.SetEmpty();
                    termAtt.Append(ipeToken);
                    termAtt.Length = ipeToken.Length;
                    //termAtt.Length = endPos - startPos + 1;

                    typeAtt.Type = "<ALPHANUM>";

                    posIncrAtt.PositionIncrement = 1;

                    offsetAtt.SetOffset(startPos, endPos);

                    return true;
                }
                else
                {
                    int i = 0;
                }
            }
        }
    }
}
