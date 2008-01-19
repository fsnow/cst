using System;
using System.Collections;
using System.Text;

namespace CST
{
    public class CLBBComparer : IComparer
    {
        public int Compare(Object x, Object y)
        {
            return ((CollectionListBoxBook)x).Book.Index - ((CollectionListBoxBook)y).Book.Index;
        }
    }
}
