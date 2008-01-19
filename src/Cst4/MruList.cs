using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	[Serializable()]
	public class MruList : List<MruListItem>
	{
		new public void Add(MruListItem item)
		{
			// remove the book from the queue if it's already there
			Remove(item);

			// add the book to the end of the list
			base.Add(item);

			// trim from the front of the list to get back to MaxItems
			while (Count > MaxItems)
				RemoveAt(0);
		}

		public int MaxItems
		{
			get
			{
				if (maxItems <= 1)
					return 5;

				return maxItems;
			}
			set { maxItems = value; }
		}
		private int maxItems;
	}
}
