using System;
using System.Collections.Generic;
using System.Text;
using Lucene.Net.Analysis;

namespace CST
{
/*
    /// <summary>Normalizes tokens extracted with {@link StandardTokenizer}. </summary>
    public class IpeFilter : TokenFilter
    {
        /// <summary>Construct filtering <i>in</i>. </summary>
        public IpeFilter(TokenStream in_Renamed)
            : base(in_Renamed)
        {
        }

        private static readonly System.String APOSTROPHE_TYPE = Lucene.Net.Analysis.Standard.StandardTokenizerConstants.tokenImage[Lucene.Net.Analysis.Standard.StandardTokenizerConstants.APOSTROPHE];
        private static readonly System.String ACRONYM_TYPE = Lucene.Net.Analysis.Standard.StandardTokenizerConstants.tokenImage[Lucene.Net.Analysis.Standard.StandardTokenizerConstants.ACRONYM];

        /// <summary>Returns the next token in the stream, or null at EOS.
        /// <p>Removes <tt>'s</tt> from the end of words.
        /// <p>Removes dots from acronyms.
        /// </summary>
        public override Lucene.Net.Analysis.Token Next()
        {
            Lucene.Net.Analysis.Token t = input.Next();

            if (t == null)
                return null;

            System.String text = t.TermText();
            System.String type = t.Type();

            if (type == APOSTROPHE_TYPE && (text.EndsWith("'s") || text.EndsWith("'S")))
            {
                return new Lucene.Net.Analysis.Token(text.Substring(0, (text.Length - 2) - (0)), t.StartOffset(), t.EndOffset(), type);
            }
            else if (type == ACRONYM_TYPE)
            {
                // remove dots
                System.Text.StringBuilder trimmed = new System.Text.StringBuilder();
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c != '.')
                        trimmed.Append(c);
                }
                return new Lucene.Net.Analysis.Token(trimmed.ToString(), t.StartOffset(), t.EndOffset(), type);
            }
            else
            {
                return t;
            }
        }
    }
*/
}
