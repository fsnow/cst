using System;
using System.Collections.Generic;
using System.Text;
using Lucene.Net.Index;

namespace CST
{
    public class TermVectorOffsetInfoComparer : IComparer<TermVectorOffsetInfo>
    {
        public int Compare(TermVectorOffsetInfo x, TermVectorOffsetInfo y)
        {
            if (x.GetStartOffset() < y.GetStartOffset())
                return -1;
            else if (x.GetStartOffset() > y.GetStartOffset())
                return 1;
            else
                return 0;
        }
    }
}
