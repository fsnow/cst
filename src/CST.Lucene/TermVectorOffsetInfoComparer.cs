using System;
using System.Collections.Generic;
using System.Text;
using Lucene.Net.Index;

namespace CST
{
    public class TermVectorOffsetInfoComparer //: IComparer<TermVectorOffsetInfo>
    {
        // TODO FSnow 2022-04-22: Commenting out until we figure out what the hell is going on
        /*
        public int Compare(TermVectorOffsetInfo x, TermVectorOffsetInfo y)
        {
            if (x.GetStartOffset() < y.GetStartOffset())
                return -1;
            else if (x.GetStartOffset() > y.GetStartOffset())
                return 1;
            else
                return 0;
        }
        */
    }
}
