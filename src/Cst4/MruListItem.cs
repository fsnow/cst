using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using CST.Conversion;

namespace CST
{
	[Serializable()]
	public class MruListItem : IEquatable<MruListItem>
	{
		public MruListItem(int index, Script script)
		{
			this.index = index;
			this.bookScript = script;
		}

		public int Index
		{
			get { return index; }
			set { index = value; }
		}
		private int index;

		public Script BookScript
		{
			get { return bookScript; }
			set { bookScript = value; }
		}
		private Script bookScript;

		public bool Equals(MruListItem other)
		{
			return (other.bookScript == this.bookScript && other.index == this.index);
		}

		public override string ToString()
		{
			return ScriptConverter.Convert(
				Books.Inst[index].LongNavPath.Replace("/", " / "), 
				Script.Devanagari,
				BookScript, 
				true);
		}
	}
}
