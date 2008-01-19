using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	[Serializable()]
	public class MatchingMultiWordBook : MatchingWordBook
	{
		public MatchingMultiWordBook()
		{
			WordPositions = new SortedDictionary<int, WordPosition>();
		}

		public SortedDictionary<int, WordPosition> WordPositions
		{
			get { return wordPositions; }
			set { wordPositions = value; }
		}
		private SortedDictionary<int, WordPosition> wordPositions;

		public override int Count
		{
			get 
			{
				int count = 0;
				foreach (WordPosition wp in WordPositions.Values)
				{
					if (wp.IsFirstTerm)
						count++;
				}

				return count; 
			}
			//set { count = value; }
		}
	}
}
