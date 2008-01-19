using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	public class BackStackItem
	{
		public BackStackItem(string userText, string word, int selectedIndex)
		{
			this.userText = userText;
			this.word = word;
			this.selectedIndex = selectedIndex;
		}

		public string UserText
		{
			get { return userText; }
			set { userText = value; }
		}
		private string userText;

		public string Word
		{
			get { return word; }
			set { word = value; }
		}
		private string word;

		public int SelectedIndex
		{
			get { return selectedIndex; }
			set { selectedIndex = value; }
		}
		private int selectedIndex;
	}
}
