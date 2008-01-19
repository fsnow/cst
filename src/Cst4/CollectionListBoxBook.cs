using System;
using System.Collections.Generic;
using System.Text;

using CST.Conversion;

namespace CST
{
    public class CollectionListBoxBook
    {
        public override string ToString()
        {
            return ScriptConverter.Convert(
                Book.ShortNavPath.Replace("/", " / "),
                Script.Devanagari,
                AppState.Inst.CurrentScript,
                true);
        }

        public string DisplayName
        {
            get
            {
                return ToString();
            }
        }

        public Book Book
        {
            get { return book; }
            set { book = value; }
        }
        private Book book;
    }
}
