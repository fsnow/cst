using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
    public class IpeComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            int i = 0;
            while (x.Length > i && y.Length > i)
            {
                if (x[i] < y[i])
                    return -1;
                else if (x[i] > y[i])
                    return 1;

                i++;
            }
            // we exited the loop without returning, so the strings are the same
            // up to the length of the shorter string.
            // either the strings are the same or one is longer than the other
            return (x.Length - y.Length);
        }
    }
}
