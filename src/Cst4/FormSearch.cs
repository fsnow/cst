using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Xml;

using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using CST.Conversion;
using CST.Collections;

namespace CST
{
    public partial class FormSearch : Form
    {
        public FormSearch()
        {
            InitializeComponent();

            lblWordStats.Text = "";
            lblBookStats.Text = "";

            comboWildRegex.SelectedIndex = 1;
            comboBookSet.SelectedIndex = 0;

            linkLabelEdit.Visible = false;
            linkLabelDelete.Visible = false;

			lblContextDistance.Visible = false;
			numContextDistance.Visible = false;
        }

		private bool isMultiWord = false;
		private bool isPhrase = false;

		public int ContextDistance
		{
			get
			{
				return (int)numContextDistance.Value;
			}
			set
			{
				if (value >= 1 && value <= 99)
					numContextDistance.Value = (decimal)value;
				else
					numContextDistance.Value = 10;
			}
		}

        public void ChangeScript()
        {
            ReloadListBoxItems(listBoxWords);
            ReloadListBoxItems(listBoxOccurBooks);

            SetListBoxFonts();
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

        private void SetListBoxFonts()
        {
            listBoxOccurBooks.Font = Fonts.GetListBoxFont(AppState.Inst.CurrentScript);
            listBoxWords.Font = Fonts.GetListBoxFont(AppState.Inst.CurrentScript);
        }

        private void UpdateOccurrences()
        {
            listBoxOccurBooks.Items.Clear();

			// merge together results from multiple selected words and sum their occurrence counts
            int occurrences = 0;
			if (isMultiWord)
			{
				List<MatchingMultiWord> mmws = new List<MatchingMultiWord>();

				foreach (int index in listBoxWords.SelectedIndices)
				{
					mmws.Add((MatchingMultiWord)listBoxWords.Items[index]);
				}

				SortedDictionary<int, MatchingMultiWordBook> mmwbs = MatchingMultiWord.MergeMultiWords(mmws);

				foreach (MatchingMultiWordBook mmwb in mmwbs.Values)
				{
					listBoxOccurBooks.Items.Add(mmwb);
					occurrences += mmwb.Count;
				}
			}
			else
			{
				SortedDictionary<int, MatchingWordBook> mwbsd = new SortedDictionary<int, MatchingWordBook>();
				foreach (int index in listBoxWords.SelectedIndices)
				{
					MatchingWord mw = (MatchingWord)listBoxWords.Items[index];
					foreach (MatchingWordBook mwb in mw.MatchingBooks)
					{
						occurrences += mwb.Count;
						if (mwbsd.ContainsKey(mwb.Book.Index))
						{
							((MatchingWordBook)mwbsd[mwb.Book.Index]).Count += mwb.Count;
						}
						else
						{
							// we must use a copy of the MWB object since we are modifying the counts 
							// to be the sum of all of the counts for the selected words
							mwbsd[mwb.Book.Index] = mwb.Copy();
						}
					}
				}

				foreach (KeyValuePair<int, MatchingWordBook> kvp in mwbsd)
				{
					listBoxOccurBooks.Items.Add(kvp.Value);
				}
			}



            lblWordStats.Text = String.Format(
				(isMultiWord ? CST.Properties.Resources.MultiWordStatsFormat : CST.Properties.Resources.WordStatsFormat), 
				listBoxWords.Items.Count, 
				listBoxWords.SelectedIndices.Count);
            lblBookStats.Text = String.Format(CST.Properties.Resources.BookStatsFormat,
				occurrences,
				listBoxOccurBooks.Items.Count);
        }

        private BitArray CalculateBookBits()
        {
            if (comboBookSet.SelectedIndex != 0)
                return ((BookCollection)comboBookSet.Items[comboBookSet.SelectedIndex]).BookBits;

            Books books = Books.Inst;
            int bookCount = books.Count;
            BitArray bookBits = null;
            BitArray clBits = null;
            BitArray pitBits = null;
            bool clSelected = false;
            bool pitSelected = false;

            if (cbMula.Checked || cbAttha.Checked || cbTika.Checked)
            {
                clBits = new BitArray(bookCount);

                if (cbMula.Checked)
                    clBits = clBits.Or(books.MulaBits);
                if (cbAttha.Checked)
                    clBits = clBits.Or(books.AtthaBits);
                if (cbTika.Checked)
                    clBits = clBits.Or(books.TikaBits);

                clSelected = true;
            }

            if (cbVinaya.Checked || cbSutta.Checked || cbAbhi.Checked)
            {
                pitBits = new BitArray(bookCount);

                if (cbVinaya.Checked)
                    pitBits = pitBits.Or(books.VinayaBits);
                if (cbSutta.Checked)
                    pitBits = pitBits.Or(books.SuttaBits);
                if (cbAbhi.Checked)
                    pitBits = pitBits.Or(books.AbhiBits);

                pitSelected = true;
            }

            if (clSelected && pitSelected)
                bookBits = clBits.And(pitBits);
            else if (clSelected)
                bookBits = clBits;
            else if (pitSelected)
                bookBits = pitBits;
            else
                bookBits = new BitArray(bookCount);

            if (cbOtherTexts.Checked)
                bookBits = bookBits.Or(books.OtherBits);

            return bookBits;
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        public void DoSearch()
        {
			txtSearchTerms.Text = txtSearchTerms.Text.Trim();
			if (txtSearchTerms.Text.Length == 0)
				return;

            this.BringToFront();
            btnSearch.Enabled = false;
            btnClose.Enabled = false;
            Cursor.Current = Cursors.WaitCursor;
            SetListBoxFonts();
            ClearResults();

            TermMatchEvaluator tme = null;
            bool isRegex = (comboWildRegex.SelectedIndex == 0);

            // calculate a BitArray representing the books that are included in the search
            BitArray bookBits = CalculateBookBits();

            string ipeTerm = Any2Ipe.Convert(txtSearchTerms.Text);
			ipeTerm = ipeTerm.Replace("  ", " ");

			isPhrase = (ipeTerm.StartsWith("\"") || ipeTerm.EndsWith("\""));

			// delete all kinds of quotation marks from the search terms
			ipeTerm = ipeTerm.Replace("\"", "");
			ipeTerm = ipeTerm.Replace("‘", "");
			ipeTerm = ipeTerm.Replace("’", "");
			ipeTerm = ipeTerm.Replace("“", "");
			ipeTerm = ipeTerm.Replace("”", "");

			if (ipeTerm.Contains(" "))
			{
				isMultiWord = true;
				string[] ipeTerms = ipeTerm.Split(' ');
				TermMatchEvaluator[] tmes = new TermMatchEvaluator[ipeTerms.Length];
				int i = 0;
				foreach (string str in ipeTerms)
				{
					try
					{
						tmes[i] = new TermMatchEvaluator(str, isRegex);
					}
					catch (Exception ex)
					{
						if (ex is ArgumentException)
						{
							MessageBox.Show("Wildcard expression or regular expression (" + str + ") is not valid");
							btnSearch.Enabled = true;
							btnClose.Enabled = true;
							return;
						}
					}

					i++;
				}

				int contextDistance = (int)numContextDistance.Value;
				Search.GetMatchingTermsWithContext(ipeTerms, tmes, listBoxWords, bookBits, contextDistance, isPhrase);
				
			}
			else
			{
				isMultiWord = false;

				try
				{
					tme = new TermMatchEvaluator(ipeTerm, isRegex);
				}
				catch (Exception ex)
				{
					if (ex is ArgumentException)
					{
						MessageBox.Show("Wildcard expression or regular expression is not valid");
						btnSearch.Enabled = true;
						btnClose.Enabled = true;
						return;
					}
				}

				Search.GetMatchingTerms(ipeTerm, tme, listBoxWords, bookBits);
			}

            if (listBoxWords.Items.Count > 0)
            {
                listBoxWords.SelectedIndex = 0;
                btnReport.Enabled = true;
            }
            else
            {
				lblWordStats.Text = CST.Properties.Resources.WordStatsZero;
                btnReport.Enabled = false;
            }

            Cursor.Current = Cursors.Default;
            btnSearch.Enabled = true;
            btnClose.Enabled = true;
        }

        private void ClearResults()
        {
            listBoxWords.Items.Clear();
            listBoxOccurBooks.Items.Clear();
            lblWordStats.Text = "";
            lblBookStats.Text = "";
            btnReport.Enabled = false;
        }

        private void txtSearchTerms_TextChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (txtSearchTerms.Text.Length > 0)
            {
                Script tstScript = Any2Deva.GetScript(txtSearchTerms.Text[0]);
                txtSearchTerms.Font = Fonts.GetControlFont(tstScript);
            }

			string query = txtSearchTerms.Text.Trim();
			// multi-word
			if (query.Contains(" "))
			{
				// phrase
				if (query.StartsWith("\"") || query.EndsWith("\""))
				{
					lblContextDistance.Visible = false;
					numContextDistance.Visible = false;
				}
				// context
				else
				{
					lblContextDistance.Visible = true;
					numContextDistance.Visible = true;
				}
			}
			else
			{
				lblContextDistance.Visible = false;
				numContextDistance.Visible = false;
			}
        }

        private void listBoxWords_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateOccurrences();
        }

		private void OpenBookSingleWordSearch()
		{
			MatchingWordBook mwb = (MatchingWordBook)listBoxOccurBooks.Items[listBoxOccurBooks.SelectedIndex];
			List<string> terms = new List<string>();

			foreach (int index in listBoxWords.SelectedIndices)
			{
				MatchingWord mw = (MatchingWord)listBoxWords.Items[index];
				terms.Add(mw.Word);
			}

			((FormMain)MdiParent).BookDisplay(mwb.Book, terms);
		}

		private void OpenBookMultiWordSearch()
		{
			MatchingMultiWordBook mmwb = (MatchingMultiWordBook)listBoxOccurBooks.Items[listBoxOccurBooks.SelectedIndex];
			((FormMain)MdiParent).BookDisplay(mmwb);
		}

        private void listBoxOccurBooks_DoubleClick(object sender, EventArgs e)
        {
			if (isMultiWord)
				OpenBookMultiWordSearch();
			else
				OpenBookSingleWordSearch();
        }

        private void SearchForm_Click(object sender, EventArgs e)
        {
            this.BringToFront();
        }

        private void listBoxOccurBooks_Click(object sender, EventArgs e)
        {
            this.BringToFront();
        }

        private void listBoxWords_Click(object sender, EventArgs e)
        {
            this.BringToFront();
        }

        private void txtSearchTerms_Click(object sender, EventArgs e)
        {
            this.BringToFront();
        }

        private void txtSearchTerms_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnSearch.PerformClick();
            }
        }

        private void SearchForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // if the user closes the form, hide it, don't let it get closed
            Hide();

            if (e.CloseReason == CloseReason.UserClosing)
            {
                AppState.Inst.SearchFormShown = false;
                e.Cancel = true;
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
			AppState.Inst.SearchFormShown = false;
            Hide();
        }

        private void listBoxWords_MouseClick(object sender, MouseEventArgs e)
        {
            UpdateOccurrences();
        }

        private void listBoxWords_KeyPress(object sender, KeyPressEventArgs e)
        {
            UpdateOccurrences();
        }

        private void cbAll_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbAll.Checked)
            {
                cbVinaya.Checked = true;
                cbSutta.Checked = true;
                cbAbhi.Checked = true;
                cbMula.Checked = true;
                cbAttha.Checked = true;
                cbTika.Checked = true;
                cbOtherTexts.Checked = true;
            }
        }

        private void cbVinaya_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbVinaya.Checked == false)
                cbAll.Checked = false;
        }

        private void cbSutta_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbSutta.Checked == false)
                cbAll.Checked = false;
        }

        private void cbAbhi_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbAbhi.Checked == false)
                cbAll.Checked = false;
        }

        private void cbMula_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbMula.Checked == false)
                cbAll.Checked = false;
        }

        private void cbAttha_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbAttha.Checked == false)
                cbAll.Checked = false;
        }

        private void cbTika_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbTika.Checked == false)
                cbAll.Checked = false;
        }

        private void cbOtherTexts_CheckedChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (cbOtherTexts.Checked == false)
                cbAll.Checked = false;
        }



        private void btnReport_Click(object sender, EventArgs e)
        {
            FormChooseReport frmCR = new FormChooseReport();
            if (frmCR.ShowDialog(this) == DialogResult.OK)
            {
                string report = "";
                string xslStem = "";
                if (frmCR.radioButtonAllWords.Checked == true)
                {
                    report = GenerateAllWordsReport();
                    xslStem = "report-all-words";
                }

                ((FormMain)MdiParent).OpenReport(report, xslStem);
            }
        }

        private string GenerateAllWordsReport()
        {
            XmlDocument doc = new XmlDocument();
            XmlNode root = doc.AppendChild((XmlNode)doc.CreateElement("words"));
            foreach (MatchingWord mw in listBoxWords.Items)
            {
                XmlNode wordNode = root.AppendChild((XmlNode)doc.CreateElement("word"));
                string word = ScriptConverter.Convert(mw.Word, Script.Ipe, AppState.Inst.CurrentScript);
                wordNode.AppendChild(doc.CreateTextNode(word));
            }

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + doc.OuterXml;
        }

        private void txtSearchTerms_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.B && e.Control) == false)
                return;

            // The following code implements the special Ctrl-B error checking search.

            this.BringToFront();
            btnSearch.Enabled = false;
            btnClose.Enabled = false;
            txtSearchTerms.Text = "";
            ClearResults();
            Cursor.Current = Cursors.WaitCursor;

            SetListBoxFonts();

            BitArray bookBits = CalculateBookBits();

            IpeWordChecker wordChecker = new IpeWordChecker();
            TermEnum terms = Search.NdxReader.Terms(new Term("text", ""));
            while (terms.Next())
            {
                Term term = terms.Term();
                wordChecker.Word = term.Text();
                if (wordChecker.IsBad())
                {
                    MatchingWord mw = new MatchingWord();
                    mw.Word = term.Text();
					List<MatchingWordBook> matchingBooks = Search.GetMatchingWordBooks(mw.Word, bookBits);
                    if (matchingBooks.Count > 0)
                    {
                        mw.MatchingBooks = matchingBooks;
                        listBoxWords.Items.Add(mw);
                    }
                }
            }

            if (listBoxWords.Items.Count > 0)
            {
                listBoxWords.SelectedIndex = 0;
                btnReport.Enabled = true;
            }
            else
            {
                btnReport.Enabled = false;
            }

            Cursor.Current = Cursors.Default;
            btnSearch.Enabled = true;
            btnClose.Enabled = true;
        }

        private void listBoxWords_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Control)
            {
				//listBoxWords.SuspendLayout();
				int topIndex = listBoxWords.TopIndex;
				listBoxWords.SelectedIndexChanged -= new System.EventHandler(this.listBoxWords_SelectedIndexChanged);
                
                for (int i = 0; i < listBoxWords.Items.Count; i++)
                {
                    listBoxWords.SetSelected(i, true);
                }

				listBoxWords.SelectedIndexChanged += new System.EventHandler(this.listBoxWords_SelectedIndexChanged);
				listBoxWords.TopIndex = topIndex;
				//listBoxWords.ResumeLayout();

				UpdateOccurrences();
			}
        }

        private void comboBookSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            ClearResults();

            if (comboBookSet.SelectedIndex == 0)
            {
                linkLabelEdit.Visible = false;
                linkLabelDelete.Visible = false;
                EnableSearchCheckboxes(true);
            }
            else if (comboBookSet.SelectedIndex == 1)
            {
                FormBookCollEditor fbce = new FormBookCollEditor(null);
                if (fbce.ShowDialog(this) == DialogResult.OK)
                {
                    SaveBookCollection(fbce);
                }
                else
                {
                    comboBookSet.SelectedIndex = 0;
                    linkLabelEdit.Visible = false;
                    linkLabelDelete.Visible = false;
                    EnableSearchCheckboxes(true);
                }
            }
            else
            {
                linkLabelEdit.Visible = true;
                linkLabelDelete.Visible = true;
                EnableSearchCheckboxes(false);
            }
        }

        private void EnableSearchCheckboxes(bool enabled)
        {
            cbVinaya.Enabled = enabled;
            cbSutta.Enabled = enabled;
            cbAbhi.Enabled = enabled;
            cbMula.Enabled = enabled;
            cbAttha.Enabled = enabled;
            cbTika.Enabled = enabled;
            cbOtherTexts.Enabled = enabled;
            cbAll.Enabled = enabled;
        }

        private void SaveBookCollection(FormBookCollEditor fbce)
        {
            BookCollection bookColl = new BookCollection();
            bookColl.Name = fbce.textBoxCollName.Text.Trim();
            foreach (CollectionListBoxBook clbb in fbce.listBoxInCollection.Items)
            {
                bookColl.BookBits.Set(clbb.Book.Index, true);
            }

			BookCollections.Inst.Colls[bookColl.Name] = bookColl;

            comboBookSet.SelectedIndex = comboBookSet.Items.Add(bookColl);
            linkLabelEdit.Visible = true;
            linkLabelDelete.Visible = true;
            EnableSearchCheckboxes(false);
        }

        private void linkLabelEdit_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FormBookCollEditor fbce = new FormBookCollEditor((BookCollection)comboBookSet.Items[comboBookSet.SelectedIndex]);
            if (fbce.ShowDialog(this) == DialogResult.OK)
            {
                SaveBookCollection(fbce);
            }
        }

        private void linkLabelDelete_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
			if (MessageBox.Show(CST.Properties.Resources.DeleteBookCollectionWarning, "", 
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                BookCollection bookColl = (BookCollection)comboBookSet.Items[comboBookSet.SelectedIndex];
				if (BookCollections.Inst.Colls.ContainsKey(bookColl.Name))
					BookCollections.Inst.Colls.Remove(bookColl.Name);

                comboBookSet.Items.RemoveAt(comboBookSet.SelectedIndex);
                comboBookSet.SelectedIndex = 0;
            }
        }

        private void comboWildRegex_SelectedIndexChanged(object sender, EventArgs e)
        {
            ClearResults();
        }

        private void gbLimitSearch_Enter(object sender, EventArgs e)
        {

        }
    }
}