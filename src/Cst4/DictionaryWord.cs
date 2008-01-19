using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
    public class DictionaryWord
    {
        public DictionaryWord(string word, string meaning)
        {
            this.word = word;
            this.meaning = meaning;
        }

        public override string ToString()
        {
            return ScriptConverter.Convert(Word, Script.Ipe, AppState.Inst.CurrentScript);
        }

        public string Word
        {
            get { return word; }
            set { word = value; }
        }
        private string word;

        public string Meaning
        {
            get { return meaning; }
            set { meaning = value; }
        }
        private string meaning;
    }
}
