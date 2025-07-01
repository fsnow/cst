using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
    public class BookHit
    {
        public int Count
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
