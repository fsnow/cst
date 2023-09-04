using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CST
{
    public class TupleItem1of2IntsComparer : Comparer<Tuple<int, int>>
    {
        public override int Compare(Tuple<int, int> x, Tuple<int, int> y)
        {
            return x.Item1.CompareTo(y.Item1);
        }
    }
}
