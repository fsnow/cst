using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	[Serializable()]
	public class WordPosition : IComparable<WordPosition>
	{
		public WordPosition(int wordIndex, int position, int positionIndex, bool isFirstTerm)
		{
			this.wordIndex = wordIndex;
			this.position = position;
			this.positionIndex = positionIndex;
			this.isFirstTerm = isFirstTerm;
		}

		public int WordIndex
		{
			get { return wordIndex; }
			set { wordIndex = value; }
		}
		private int wordIndex;

		public virtual string Word
		{
			get { return word; }
			set { word = value; }
		}
		protected string word;

		public int Position
		{
			get { return position; }
			set { position = value; }
		}
		private int position;

		public int PositionIndex
		{
			get { return positionIndex; }
			set { positionIndex = value; }
		}
		private int positionIndex;

		public bool IsFirstTerm
		{
			get { return isFirstTerm; }
			set { isFirstTerm = value; }
		}
		private bool isFirstTerm;

		public int CompareTo(WordPosition wp)
		{
			return this.Position - wp.Position;
		}

		public int StartOffset
		{
			get { return startOffset; }
			set { startOffset = value; }
		}
		private int startOffset;

		public int EndOffset
		{
			get { return endOffset; }
			set { endOffset = value; }
		}
		private int endOffset;
	}
}
