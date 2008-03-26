using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CST.Conversion;

namespace CST
{
	[ComVisibleAttribute(true)]
    public partial class FormDictionary : Form
    {
        public FormDictionary(string searchedWord)
        {
            InitializeComponent();

            wbMeaning.ObjectForScripting = this;
            wbMeaning.BringToFront();

            backStack = new Stack<BackStackItem>();

			DontLookup = true;
			if (cbDefinitionLanguage.SelectedIndex < 0)
				cbDefinitionLanguage.SelectedIndex = 0;
			DontLookup = false;

            if (searchedWord != null && searchedWord.Length > 0)
                this.txtWord.Text = searchedWord.ToLower();

			// sets the words and meanings boxes to be of the same height when dialog is first shown
			ResizeWordsAndMeanings();
         }

        private List<DictionaryWord> enWords;
		private List<DictionaryWord> hiWords;
        private Stack<BackStackItem> backStack;

        public bool DontLookup
        {
            get { return dontLookup; }
			set { dontLookup = value; }
        }
		private bool dontLookup;

		public bool DontSelectFirst
		{
			get { return dontSelectFirst; }
			set { dontSelectFirst = value; }
		}
		private bool dontSelectFirst;

        public void SetSearchedWord(string searchedWord)
        {
            txtWord.Text = searchedWord.ToLower();
        }

        public void LookupWord()
        {
            btnClose.Enabled = false;
            Cursor.Current = Cursors.WaitCursor;

            lbWords.Font = Fonts.GetListBoxFont(AppState.Inst.CurrentScript);
            ClearResults();
            if (enWords == null && cbDefinitionLanguage.SelectedIndex == 0) // English
                LoadEnglishDictionary();
			if (hiWords == null && cbDefinitionLanguage.SelectedIndex == 1) // Hindi
				LoadHindiDictionary();
            Search();

            Cursor.Current = Cursors.Default;
            btnClose.Enabled = true;
        }

        private void LoadEnglishDictionary()
        {
			enWords = new List<DictionaryWord>();

			Dictionary<string, string> wordDict = new Dictionary<string,string>();

            try
            {
				DirectoryInfo di = new DirectoryInfo(
					Config.Inst.ReferenceDirectory + Path.DirectorySeparatorChar +
					Config.Inst.EnglishDictionaryDirectory);
				FileInfo[] fis = di.GetFiles();
				foreach (FileInfo fi in fis)
				{
					StreamReader sr = new StreamReader(fi.FullName);

					while (true)
					{
						string word = sr.ReadLine();
						if (word == null)
							break;

						string meaning = sr.ReadLine();
						if (meaning == null)
							break;

						if (word == null || word.Length == 0 ||
							meaning == null || meaning.Length == 0)
						{
							continue;
						}

						word = Any2Ipe.Convert(word);

						if (wordDict.ContainsKey(word))
						{
							string newMeaning = wordDict[word];
							newMeaning += "</p><hr/<p>" + meaning;
							wordDict.Remove(word);
							wordDict.Add(word, newMeaning);
						}
						else
							wordDict.Add(word, meaning);
					}

					sr.Close();
				}

				foreach (string word in wordDict.Keys)
				{
					enWords.Add(new DictionaryWord(word, wordDict[word]));
				}

                enWords.Sort(new DictionaryWordComparer());

                
            }
            catch (Exception ex)
            {
                enWords = null;
                MessageBox.Show("Error", "Error reading Pali-English dictionary: " + ex.ToString(), 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

		private void LoadHindiDictionary()
		{
			hiWords = new List<DictionaryWord>();

			try
			{
				StreamReader sr = new StreamReader(Config.Inst.ReferenceDirectory + Path.DirectorySeparatorChar +
					Config.Inst.PaliHindiDictionaryFile);

				while (true)
				{
					string word = sr.ReadLine();
					if (word == null)
						break;

					string meaning = sr.ReadLine();
					if (meaning == null)
						break;

					if (word == null || word.Length == 0 ||
						meaning == null || meaning.Length == 0)
					{
						continue;
					}

					word = Any2Ipe.Convert(word);
					hiWords.Add(new DictionaryWord(word, meaning));
				}

				hiWords.Sort(new DictionaryWordComparer());

				sr.Close();
			}
			catch (Exception)
			{
				hiWords = null;
				MessageBox.Show("Error", "Error reading Pali-Hindi dictionary",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

        private void Search()
        {
			List<DictionaryWord> words = null;
			if (cbDefinitionLanguage.SelectedIndex == 0)
				words = enWords;
			else if (cbDefinitionLanguage.SelectedIndex == 1)
				words = hiWords;

            // in case LoadDictionary fails
            if (words == null)
                return;

            string word = txtWord.Text;
            if (word == null || word.Length == 0)
            {
                DisplayMeaning("");
                return;
            }

            word = Any2Ipe.Convert(word);

            int index = words.BinarySearch(new DictionaryWord(word, ""), new DictionaryWordComparer());
            // word is in the list
            if (index >= 0)
            {
                lbWords.Items.Add(words[index]);
                index++;

                // look ahead in the list for words that start with the word that was searched for
                while (index < words.Count)
                {
                    if (words[index].Word.StartsWith(word))
                        lbWords.Items.Add(words[index]);
                    else
                        break;

                    index++;
                }

                if (lbWords.Items.Count > 0 && DontSelectFirst == false)
                    lbWords.SelectedIndex = 0;
            }
            else
            {
                index = ~index;
                int startIndex = index;

                // determine which way(s) to look for candidate words
                DictionaryWord wordBehind = null;
                DictionaryWord wordAhead = null;

				int commonBehind = 0;
				int commonAhead = 0;

				if (index - 1 >= 0 && index - 1 < words.Count)
				{
					wordBehind = words[index - 1];
					commonBehind = CountCommonStartLetters(word, wordBehind.Word);
				}

				if (index >= 0 && index < words.Count)
				{
					wordAhead = words[index];
					commonAhead = CountCommonStartLetters(word, wordAhead.Word);
				}

                // look behind this word for candidate words
                if (commonBehind >= commonAhead && commonBehind > 0)
                {
                    Stack<DictionaryWord> wordStack = new Stack<DictionaryWord>();
                    index--;
                    DictionaryWord word2 = null;

                    while (index >= 0 && index < words.Count)
                    {
                        word2 = words[index];

                        if (CountCommonStartLetters(word, word2.Word) == commonBehind)
                            wordStack.Push(word2);
                        else
                            break;

                        index--;
                    }

                    while (wordStack.Count > 0)
                    {
                        word2 = wordStack.Pop();
                        lbWords.Items.Add(word2);
                    }
                }

                // look ahead in the list for words that start with the word that was searched for
                if (commonAhead >= commonBehind && commonAhead > 0)
                {
                    index = startIndex;
                    while (index < words.Count)
                    {
                        if (CountCommonStartLetters(words[index].Word, word) == commonAhead)
                            lbWords.Items.Add(words[index]);
                        else
                            break;

                        index++;
                    }
                }

				if (lbWords.Items.Count == 0)
					DisplayMeaning("");
                else if (lbWords.Items.Count > 0 && DontSelectFirst == false)
                    lbWords.SelectedIndex = 0;
            }
        }

        private int CountCommonStartLetters(string str1, string str2)
        {
            if (str1 == null || str1.Length == 0 ||
                str2 == null || str2.Length == 0)
            {
                return 0;
            }

            int shortLen = (str1.Length < str2.Length ? str1.Length : str2.Length);
            int i = 0;
            for (i = 0; i < shortLen; i++)
            {
                if (str1[i] != str2[i])
                    break;
            }

            return i;
        }

        public void DisplayMeaning(string meaning)
        {
			string style = "";

			if (cbDefinitionLanguage.SelectedIndex == 0)
				style = "body { font-family:Tahoma; font-size:9.75pt; border-top:0; }";
			else if (cbDefinitionLanguage.SelectedIndex == 1)
				style = "body { font-family:CDAC-GISTSurekh; font-size:11pt; border-top:0; };";

			meaning = Regex.Replace(meaning, "<see>(.+?)</see>",
				"<a onclick=\"window.external.SeeAlso('$1')\" href=\"#\">$1</a>");

            string html = "<html>" +
				"<head><style type=\"text/css\">" + style + "</style></head>" +
				"<body><p>" + meaning + "</p>";
            if (backStack.Count > 0)
            {
                string top = ScriptConverter.Convert(backStack.Peek().Word, Script.Ipe, AppState.Inst.CurrentScript);
                html += "<p>Back to <a onclick=\"window.external.SeeAlsoBack('" +
                    top + "')\" href=\"#\">" + top + "</a></p>";
            }

            html += "</body></html>";
            wbMeaning.DocumentText = html;
        }

        private void ClearResults()
        {
            lbWords.Items.Clear();
        }

        public void ChangeScript()
        {
            ReloadListBoxItems(lbWords);
            lbWords.Font = Fonts.GetListBoxFont(AppState.Inst.CurrentScript);
			ResizeWordsAndMeanings();
        }

        private void ReloadListBoxItems(System.Windows.Forms.ListBox listBox)
        {
            if (listBox.Items.Count > 0)
            {
                object[] items = new object[listBox.Items.Count];
                listBox.Items.CopyTo(items, 0);
                int[] selectedItems = new int[listBox.SelectedIndices.Count];
                listBox.SelectedIndices.CopyTo(selectedItems, 0);
                listBox.Items.Clear();
                for (int i = 0; i < items.Length; i++)
                    listBox.Items.Add(items[i]);
                for (int i = 0; i < selectedItems.Length; i++)
                    listBox.SetSelected(selectedItems[i], true);
            }
        }

        private void lbWords_SelectedIndexChanged(object sender, EventArgs e)
        {
            // do this before DisplayMeaning()
           if (lbWords.SelectedIndex > 0)
                backStack.Clear();

            DictionaryWord dictWord = (DictionaryWord)lbWords.Items[lbWords.SelectedIndex];
            DisplayMeaning(dictWord.Meaning);
        }

        private void FormDictionary_FormClosing(object sender, FormClosingEventArgs e)
        {
            // if the user closes the form, hide it, don't let it get closed
            Hide();

            if (e.CloseReason == CloseReason.UserClosing)
            {
				AppState.Inst.DictionaryShown = false;
                e.Cancel = true;
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Hide();
			AppState.Inst.DictionaryShown = false;
        }

		private void txtWord_TextChanged(object sender, EventArgs e)
        {
            if (txtWord.Text.Length > 0)
            {
                Script tstScript = Any2Deva.GetScript(txtWord.Text[0]);
                txtWord.Font = Fonts.GetControlFont(tstScript);
            }
            // to prevent the word from being half selected, if there was a selection then the text changes
            // to a longer word
            if (txtWord.SelectionLength > 0)
                txtWord.SelectionLength = 0;

			if (DontLookup == false)
				LookupWord();
        }

        public void SeeAlso(string word)
        {
            backStack.Push(new BackStackItem(
                    txtWord.Text,
                    ((DictionaryWord)lbWords.Items[lbWords.SelectedIndex]).Word, 
                    lbWords.SelectedIndex));
            txtWord.Text = word;
        }

        public void SeeAlsoBack(string word)
        {
            BackStackItem bsi = backStack.Pop();
            txtWord.Text = bsi.UserText;
            if (bsi.SelectedIndex < lbWords.Items.Count)
                lbWords.SelectedIndex = bsi.SelectedIndex;
        }

		private void ResizeWordsAndMeanings()
		{
			// The lbWords height changes in increments that are multiples of the height of one line of text
			// in the list box and is dependent on the current font. In order to make the words list and meaning
			// browser the same height, we must set the lbWords height first and set the other height off of the
			// actual height of lbWords.

			// This code also fixes the bug where the words and meanings boxes were different heights after the
			// Pali script is changed.

			int bottomSpace = 20;
			int rightSpace = 20;

			lbWords.Height = this.ClientRectangle.Height - lbWords.Location.Y - bottomSpace;

			txtForBorder.Height = lbWords.Height;
			wbMeaning.Height = txtForBorder.Height - 4;

			txtForBorder.Width = this.ClientRectangle.Width - txtForBorder.Location.X - rightSpace;
			wbMeaning.Width = txtForBorder.Width - 4;
		}

        private void txtWord_KeyPress(object sender, KeyPressEventArgs e)
        {
            backStack.Clear();
        }

		private void cbDefinitionLanguage_SelectedIndexChanged(object sender, EventArgs e)
		{
			backStack.Clear();

			if (DontLookup == false)
				LookupWord();
		}

		private void FormDictionary_Resize(object sender, EventArgs e)
		{
			ResizeWordsAndMeanings();
		}
    }   
}