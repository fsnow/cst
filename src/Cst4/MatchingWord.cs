using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
    public class MatchingWord
    {
        public override string ToString()
        {
            return ScriptConverter.Convert(Word, Script.Ipe, AppState.Inst.CurrentScript);
        }

        public virtual string Word
        {
            get { return word; }
            set { word = value; }
        }
        protected string word;

        public List<MatchingWordBook> MatchingBooks
        {
            get { return matchingBooks; }
            set { matchingBooks = value; }
        }
        private List<MatchingWordBook> matchingBooks;
    }
}
