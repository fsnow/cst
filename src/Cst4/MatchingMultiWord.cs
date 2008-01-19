using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
	public class MatchingMultiWord : MatchingWord, IComparable<MatchingMultiWord>
	{
		public override string ToString()
		{
			return ScriptConverter.Convert(Word, Script.Ipe, AppState.Inst.CurrentScript);
		}

		// takes the multiword hash key with tilde separators and converts to string array
		public void SetMultiWord(string multi)
		{
			wordArray = multi.Split(new char[]{'~'});
		}

		public string[] WordArray
		{
			get { return wordArray; }
			set { wordArray = value; }
		}
		private string[] wordArray;

		public bool IsPhrase
		{
			get { return isPhrase; }
			set { isPhrase = value; }
		}
		private bool isPhrase;

		public override string Word
		{
			get {
				if (wordArray.Length < 2)
					throw new Exception("Too few words in wordArray");

				string w = WordArray[0];
				for (int i = 1; i < wordArray.Length; i++)
				{
					w += (IsPhrase ? " " : ",  ") + wordArray[i];
				}
				return w;
			}
			set { word = value; }
		}

		public virtual int CompareTo(MatchingMultiWord mmw)
		{
			IpeComparer ipeComparer = new IpeComparer();

			for (int i = 0; i < wordArray.Length; i++)
			{
				if (i >= mmw.WordArray.Length)
					return -1;

				int res = ipeComparer.Compare(WordArray[i], mmw.WordArray[i]);
				if (res != 0)
					return res;
			}

			return 0;
		}

		public void SetBookResults(SortedDictionary<int, SortedDictionary<int, WordPosition>> bookDict)
		{
			this.bookDict = bookDict;

			MatchingBooks = new List<MatchingWordBook>();
			foreach (int bookIndex in bookDict.Keys)
			{
				MatchingMultiWordBook mmwb = new MatchingMultiWordBook();
				mmwb.Book = Books.Inst[bookIndex];
				mmwb.WordPositions = bookDict[bookIndex];
				MatchingBooks.Add(mmwb);
			}
		}

		private SortedDictionary<int, SortedDictionary<int, WordPosition>> bookDict;

		/// <summary>
		/// Takes a list of selected multi-words and returns the corresponding books with the WordPositions merged
		/// </summary>
		/// <param name="mmws">A list of selected word combinations</param>
		/// <returns>A SortedDictionary with book index as the key and MatchingMultiWordBook as the value</returns>
		public static SortedDictionary<int, MatchingMultiWordBook> MergeMultiWords(List<MatchingMultiWord> mmws)
		{
			SortedDictionary<int, MatchingMultiWordBook> mmwbsd = new SortedDictionary<int, MatchingMultiWordBook>();

			// iterate over all book indexes
			for (int i = 0; i < Books.Inst.Count; i++)
			{
				// iterate over all multi-words
				foreach (MatchingMultiWord mmw in mmws)
				{
					if (mmw.bookDict.ContainsKey(i) == false)
						continue;

					MatchingMultiWordBook mmwb;

					if (mmwbsd.ContainsKey(i))
						mmwb = mmwbsd[i];
					else
					{
						mmwb = new MatchingMultiWordBook();
						mmwb.Book = Books.Inst[i];
						mmwbsd.Add(i, mmwb);
					}

					foreach (int pos in mmw.bookDict[i].Keys)
					{
						if (mmwb.WordPositions.ContainsKey(pos))
						{
							if (mmw.bookDict[i][pos].IsFirstTerm)
							{
								mmwb.WordPositions.Remove(pos);
								mmwb.WordPositions.Add(pos, mmw.bookDict[i][pos]);
							}
						}
						else
							mmwb.WordPositions.Add(pos, mmw.bookDict[i][pos]);
					}
				}
			}

			return mmwbsd;
		}
	}
}
