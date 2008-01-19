using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
	[Serializable()]
    public class MatchingWordBook
    {
        public override string ToString()
        {
            string snp = Book.ShortNavPath.Replace("/", " / ");

            return ScriptConverter.Convert(
                // convert the number count to Devanagari numbers so that it can be converted to any script
                Latn2Deva.Convert(Count.ToString()) + " - " + snp, 
                Script.Devanagari, 
                AppState.Inst.CurrentScript,
                true);
        }

        // To prevent duplicates when added to Set
        public override int GetHashCode()
        {
            return book.Index;
        }

        /// <summary>
        /// Makes a copy of the MatchingWordBook. The Book member is a reference to this object's book.
        /// </summary>
        /// <returns></returns>
        public virtual MatchingWordBook Copy()
        {
            MatchingWordBook mwb = new MatchingWordBook();
            mwb.Book = this.Book;
            mwb.Count = this.Count;
            return mwb;
        }

        public override bool Equals(object obj)
        {
            if (obj is MatchingWordBook)
                return book.Index == ((MatchingWordBook)obj).book.Index;
            else
                return false;
        }

        public virtual int Count
        {
            get { return count; }
            set { count = value; }
        }
        private int count;

        public Book Book
        {
            get { return book; }
            set { book = value; }
        }
        private Book book;
    }
}
