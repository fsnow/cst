using System;
using System.Collections;
using System.Text;

namespace CST
{
    [Serializable()]
    public class BookCollection
    {
        public BookCollection()
        {
            bookBits = new BitArray(Books.Inst.Count);
        }

        public override string ToString()
        {
            return Name;
        }

        public string Name
        {
            get { return name; }
            set { name = value; }
        }
        private string name;

        public BitArray BookBits
        {
            get { return bookBits; }
            set { bookBits = value; }
        }
        private BitArray bookBits;
    }
}
