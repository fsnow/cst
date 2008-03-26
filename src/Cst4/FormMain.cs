using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using Lucene.Net.Index;
using CST.Conversion;

namespace CST
{
	public partial class FormMain : Form
	{
		[DllImport("gdi32.dll")]
		static extern int AddFontResource(string lpszFilename);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		static extern uint GetWindowsDirectory(StringBuilder lpBuffer, uint uSize);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);


		public FormMain()
		{
			InitializeComponent();

			DateTime t0 = DateTime.Now;

			// Set current directory to be the directory containing the EXE.
			// All file paths are relative to that directory.
			string path = Assembly.GetExecutingAssembly().Location;
			FileInfo fi = new FileInfo(path);
			Directory.SetCurrentDirectory(fi.Directory.FullName);

			SplashScreen.SetStatus("Loading preferences");

			AppState.Deserialize();
			XmlFileDates.Deserialize();
			ChapterLists.Deserialize();

			SplashScreen.SetStatus("Checking Fonts");

			InstallFonts();

			SplashScreen.SetStatus("Validating Tipitaka XML data");

			List<int> changedFiles = ValidateXmlData();

			DateTime t1 = DateTime.Now;
			TimeSpan ts1 = DateTime.Now.Subtract(t0);

			SplashScreen.SetStatus("Checking search indexes");

			// validate Lucene search index
			BookIndexer bookIndexer = new BookIndexer();
			bookIndexer.IndexDirectory = Config.Inst.IndexDirectory;
			bookIndexer.XmlDirectory = Config.Inst.XmlDirectory;
			bookIndexer.IndexAll(this.UpdateSplashStatus, changedFiles);

			SplashScreen.SetStatus("Generating chapter lists");
			ChapterLists.Generate(changedFiles);

			DateTime t2 = DateTime.Now;
			TimeSpan ts2 = DateTime.Now.Subtract(t1);

			SplashScreen.SetStatus("Loading multi-lingual string resources");

			LanguageCollector lc = new LanguageCollector(new CultureInfo("en"));
			int currentLanguage;
			CultureInfoDisplayItem[] lis = lc.GetLanguages(LanguageCollector.LanguageNameDisplay.NativeName, out currentLanguage);
			tscbInterfaceLanguage.Items.AddRange(lis);
			tscbInterfaceLanguage.SelectedIndex = currentLanguage;

			SetScriptDropdown();

			WindowState = FormWindowState.Maximized;

			SplashScreen.SetStatus("Loading previous program state");

			FromAppState();

			PopulateMRU();

			// ensure that splash screen shows for at least 4 seconds
			TimeSpan splashTime = DateTime.Now.Subtract(t0);
			TimeSpan minSplash = new TimeSpan(0, 0, 4);
			if (splashTime < minSplash)
			{
				SplashScreen.SetStatus("");
				Thread.Sleep(minSplash.Subtract(splashTime));
			}

			SplashScreen.CloseForm();

			// ensures that MainForm doesn't end up on the bottom of the z-order after splash screen is closed
			IntPtr hWnd = this.Handle;
			SetForegroundWindow(hWnd);
		}

		private string GetWindowsDirectory()
		{
			try
			{
				const int MaxPathLength = 255;
				StringBuilder sb = new StringBuilder(MaxPathLength);
				int len = (int)GetWindowsDirectory(sb, MaxPathLength);
				return sb.ToString(0, len);
			}
			catch (Exception)
			{
				return "";
			}
		}

		private void InstallFonts()
		{
			try
			{
				DirectoryInfo di = new DirectoryInfo("Fonts");
				FileInfo[] fis = di.GetFiles();
				string windir = GetWindowsDirectory();
				foreach (FileInfo fi in fis)
				{
					string windirFont = windir + Path.DirectorySeparatorChar + "fonts" + 
						Path.DirectorySeparatorChar + fi.Name;

					if (File.Exists(windirFont))
						continue;

					File.Copy(fi.FullName, windirFont);

					int fontsAdded = AddFontResource(windirFont);
					int j = 0;
				}
			}
			catch (Exception ex)
			{
				int j = 0;
			}

			int i = 0;
		}

		private void FromAppState()
		{
			AppState appState = AppState.Inst;

			// save Maximized or Normal, not Minimized
			if (appState.WindowState != FormWindowState.Minimized)
			{
				this.WindowState = appState.WindowState;
			}

			// restore size and location if window state is Normal
			if (appState.WindowState == FormWindowState.Normal)
			{
				if (appState.Size.Width > 0 && appState.Size.Height > 0)
					this.Size = appState.Size;

				this.StartPosition = FormStartPosition.Manual;
				this.Location = appState.Location;
			}

			int index = tscbInterfaceLanguage.FindString(appState.InterfaceLanguage);
			if (index >= 0)
				tscbInterfaceLanguage.SelectedIndex = index;

			if (appState.SearchFormShown)
			{
				if (searchForm == null)
				{
					searchForm = new FormSearch();
					searchForm.MdiParent = this;
				}

				searchForm.StartPosition = FormStartPosition.Manual;
				searchForm.Location = appState.SearchFormLocation;
				searchForm.txtSearchTerms.Text = appState.SearchTerms;
				searchForm.ContextDistance = appState.SearchContextDistance;
				searchForm.comboWildRegex.SelectedIndex = appState.SearchUse;
				foreach (BookCollection bookColl in BookCollections.Inst.Colls.Values)
				{
					searchForm.comboBookSet.Items.Add(bookColl);
				}
				searchForm.cbVinaya.Checked = appState.SearchVinaya;
				searchForm.cbSutta.Checked = appState.SearchSutta;
				searchForm.cbAbhi.Checked = appState.SearchAbhi;
				searchForm.cbMula.Checked = appState.SearchMula;
				searchForm.cbAttha.Checked = appState.SearchAttha;
				searchForm.cbTika.Checked = appState.SearchTika;
				searchForm.cbOtherTexts.Checked = appState.SearchOtherTexts;
				searchForm.cbAll.Checked = appState.SearchAll;
				searchForm.comboBookSet.SelectedIndex = appState.SearchBookCollSelected;

				// Only execute a search if there were previously results (word(s) selected) AND
				// there were search terms, i.e. the results weren't from the Ctrl-B error check search
				if (searchForm.txtSearchTerms.Text.Length > 0 && appState.SearchWordsSelected != null &&
					appState.SearchWordsSelected.Length > 0)
				{
					searchForm.DoSearch();

					// Make sure that there are at least as many words as the last selected index
					if (appState.SearchWordsSelected != null &&
						appState.SearchWordsSelected.Length > 0 &&
						searchForm.listBoxWords.Items.Count >
						appState.SearchWordsSelected[appState.SearchWordsSelected.Length - 1])
					{
						// unselect the first word which was selected as the default in the search form
						if (searchForm.listBoxWords.SelectedIndices.Count == 1 &&
							searchForm.listBoxWords.SelectedIndices[0] == 0)
						{
							searchForm.listBoxWords.SetSelected(0, false);
						}

						for (int i = 0; i < appState.SearchWordsSelected.Length; i++)
						{
							searchForm.listBoxWords.SetSelected(appState.SearchWordsSelected[i], true);
						}

						if (searchForm.listBoxOccurBooks.Items.Count > appState.SearchBookSelected)
						{
							searchForm.listBoxOccurBooks.SelectedIndex = appState.SearchBookSelected;
						}
					}
				}

				searchForm.Show();
			}

			if (appState.SelectFormShown)
			{
				if (formSelectBook == null)
				{
					formSelectBook = new FormSelectBook();
					formSelectBook.MdiParent = this;
				}

				formSelectBook.StartPosition = FormStartPosition.Manual;
				formSelectBook.Location = appState.SelectFormLocation;
				formSelectBook.Size = appState.SelectFormSize;
				formSelectBook.SetNodeStates(appState.SelectFormNodeStates);

				formSelectBook.Show();
			}

			if (formDict == null)
			{
				formDict = new FormDictionary("");
				formDict.MdiParent = this;
			}

			formDict.StartPosition = FormStartPosition.Manual;
			formDict.Location = appState.DictionaryLocation;

			if (appState.DictionarySize.Height > formDict.MinimumSize.Height &&
				appState.DictionarySize.Width > formDict.MinimumSize.Width)
			{
				formDict.Size = appState.DictionarySize;
			}

			// Don't select a word (by default the first is selected) until the SelectedIndex is set below.
			// This is a wordaround for what might be a bug in the WebBrowser control
			// where it won't display the meaning if it set twice in rapid succession.
			formDict.DontLookup = true;
			formDict.DontSelectFirst = true;
			formDict.txtWord.Text = appState.DictionaryUserText;
			formDict.cbDefinitionLanguage.SelectedIndex = appState.DictionaryLanguageIndex;
			formDict.DontLookup = false;

			// We cannot assume that either assignment above will raise an event that causes the lookup.
			// Setting SelectedIndex will not raise an event for English (index 0)
			// since that's the default value. Setting Text won't raise an event for "".
			formDict.LookupWord();
			formDict.DontSelectFirst = false;

			if (appState.DictionaryWordSelected < formDict.lbWords.Items.Count)
				formDict.lbWords.SelectedIndex = appState.DictionaryWordSelected;

			if (appState.DictionaryShown)
				formDict.Show();

			if (appState.BookWindows != null && appState.BookWindows.Count > 0)
			{
				foreach (AppStateBookWindow cbw in appState.BookWindows)
				{
					FormBookDisplay formBookDisplay;

					if (cbw.Mmwb != null)
						formBookDisplay = new FormBookDisplay(cbw.Mmwb);
					else
						formBookDisplay = new FormBookDisplay(Books.Inst[cbw.BookIndex], cbw.Terms, cbw.BookScript);

					formBookDisplay.MdiParent = this;
					formBookDisplay.StartPosition = FormStartPosition.Manual;
					formBookDisplay.Location = cbw.Location;
					formBookDisplay.Size = cbw.Size;
					formBookDisplay.WindowState = cbw.WindowState;
					if (cbw.ShowFootnotes) formBookDisplay.tsmiShowFootnotes.Select();
					if (cbw.ShowTerms) formBookDisplay.tsmiShowSearchTerms.Select();

					formBookDisplay.Show();
				}

				tsmiCascade.Enabled = true;
				tsmiTileHorizontal.Enabled = true;
				tsmiTileVertical.Enabled = true;
			}
		}

		private void ToAppState()
		{
			AppState appState = AppState.Inst;

			// state state of main form
			appState.WindowState = this.WindowState;
			if (WindowState == FormWindowState.Normal)
			{
				appState.Size = this.Size;
				appState.Location = this.Location;
			}

			appState.InterfaceLanguage = tscbInterfaceLanguage.Text;

			// save state of search form
			if (searchForm == null)
				appState.SearchFormShown = false;
			else
			{
				// The Visible property is always false at the point where Dispose() is called.
				// We track the form visibility as the form is shown and hidden and 
				// set SearchFormShown at that time.
				//appState.SearchFormShown = searchForm.Visible; 
				appState.SearchFormLocation = searchForm.Location;
				appState.SearchTerms = searchForm.txtSearchTerms.Text;
				appState.SearchContextDistance = searchForm.ContextDistance;
				appState.SearchUse = searchForm.comboWildRegex.SelectedIndex;
				appState.SearchVinaya = searchForm.cbVinaya.Checked;
				appState.SearchSutta = searchForm.cbSutta.Checked;
				appState.SearchAbhi = searchForm.cbAbhi.Checked;
				appState.SearchMula = searchForm.cbMula.Checked;
				appState.SearchAttha = searchForm.cbAttha.Checked;
				appState.SearchTika = searchForm.cbTika.Checked;
				appState.SearchOtherTexts = searchForm.cbOtherTexts.Checked;
				appState.SearchAll = searchForm.cbAll.Checked;
				appState.SearchBookCollSelected = searchForm.comboBookSet.SelectedIndex;

				appState.SearchWordsSelected = new int[searchForm.listBoxWords.SelectedIndices.Count];
				for (int i = 0; i < searchForm.listBoxWords.SelectedIndices.Count; i++)
				{
					appState.SearchWordsSelected[i] = searchForm.listBoxWords.SelectedIndices[i];
				}

				appState.SearchBookSelected = searchForm.listBoxOccurBooks.SelectedIndex;
			}

			// save state of Select a Book form
			if (formSelectBook == null)
				appState.SelectFormShown = false;
			else
			{
				//appState.SelectFormShown = formSelectBook.Visible;
				appState.SelectFormLocation = formSelectBook.Location;
				appState.SelectFormSize = formSelectBook.Size;
				// TreeNode.IsExpanded is always false at the point where Dispose() is called.
				//appState.SelectFormNodeStates = formSelectBook.GetNodeStates();
			}

			// save state of Dictionary
			if (formDict == null)
				appState.DictionaryShown = false;
			else
			{
				appState.DictionaryLocation = formDict.Location;
				appState.DictionarySize = formDict.Size;
				appState.DictionaryUserText = formDict.txtWord.Text;
				appState.DictionaryWordSelected = formDict.lbWords.SelectedIndex;
				appState.DictionaryLanguageIndex = formDict.cbDefinitionLanguage.SelectedIndex;
			}

			appState.BookWindows = new List<AppStateBookWindow>();

			foreach (Form form in MdiChildren)
			{
				if (form is FormBookDisplay)
				{
					FormBookDisplay fbd = (FormBookDisplay)form;
					AppStateBookWindow cbw = new AppStateBookWindow();
					cbw.Location = form.Location;
					cbw.Size = form.Size;
					cbw.WindowState = form.WindowState;
					cbw.BookIndex = fbd.Book.Index;
					cbw.BookScript = fbd.BookScript;
					cbw.ShowFootnotes = fbd.tsmiShowFootnotes.Selected;
					cbw.ShowTerms = fbd.tsmiShowSearchTerms.Selected;
					cbw.Terms = fbd.Terms;
					cbw.Mmwb = fbd.Mmwb;

					appState.BookWindows.Add(cbw);
				}
			}
		}

		public void UpdateSplashStatus(string message)
		{
			SplashScreen.SetStatus(message);
		}

		public void SetScriptDropdown()
		{
			switch (AppState.Inst.CurrentScript)
			{
				case Script.Bengali:
					tscbPaliScript.SelectedIndex = 0;
					break;
				case Script.Cyrillic:
					tscbPaliScript.SelectedIndex = 1;
					break;
				case Script.Devanagari:
					tscbPaliScript.SelectedIndex = 2;
					break;
				case Script.Gujarati:
					tscbPaliScript.SelectedIndex = 3;
					break;
				case Script.Gurmukhi:
					tscbPaliScript.SelectedIndex = 4;
					break;
				case Script.Kannada:
					tscbPaliScript.SelectedIndex = 5;
					break;
				case Script.Khmer:
					tscbPaliScript.SelectedIndex = 6;
					break;
				case Script.Malayalam:
					tscbPaliScript.SelectedIndex = 7;
					break;
				case Script.Myanmar:
					tscbPaliScript.SelectedIndex = 8;
					break;
				case Script.Latin: // "Roman"
					tscbPaliScript.SelectedIndex = 9;
					break;
				case Script.Sinhala:
					tscbPaliScript.SelectedIndex = 10;
					break;
				case Script.Telugu:
					tscbPaliScript.SelectedIndex = 11;
					break;
				case Script.Thai:
					tscbPaliScript.SelectedIndex = 12;
					break;
				case Script.Tibetan:
					tscbPaliScript.SelectedIndex = 13;
					break;
			}
		}

		// Returns a list of the book indexes of files that have changed since the last run.
		// Returns all book indexes if the search index has not been created.
		private List<int> ValidateXmlData()
		{
			List<int> changedFiles = new List<int>();

			Books books = Books.Inst;
			XmlFileDates xmlFileDates = XmlFileDates.Inst;

			foreach (Book book in books)
			{
				FileInfo fi = new FileInfo(Config.Inst.XmlDirectory + Path.DirectorySeparatorChar + book.FileName);
				DateTime lastTime = xmlFileDates.FileDates[book.Index];
				if (fi.Exists && lastTime < fi.LastWriteTimeUtc)
				{
					changedFiles.Add(book.Index);
					xmlFileDates.FileDates[book.Index] = fi.LastWriteTimeUtc;
				}
			}

			return changedFiles;
		}

		private void PopulateMRU()
		{
			if (AppState.Inst.MruList.Count == 0)
				tsmiMru.Enabled = false;
			else
			{
				tsmiMru.Enabled = true;
				tsmiMru.DropDownItems.Clear();

				for (int i = AppState.Inst.MruList.Count - 1; i >= 0; i--)
				{
					MruListItem item = AppState.Inst.MruList[i];
					ToolStripMenuItem tsmi = new ToolStripMenuItem(item.ToString(), 
						null, 
						new EventHandler(MRU_Click),
						i.ToString());
					tsmiMru.DropDownItems.Add(tsmi);
				}
			}
		}

		private void MRU_Click(Object sender, EventArgs e)
		{
			try
			{
				int mruIndex = Convert.ToInt32(((ToolStripMenuItem)sender).Name);
				if (mruIndex >= 0 && mruIndex < AppState.Inst.MruList.Count)
				{
					MruListItem mruItem = AppState.Inst.MruList[mruIndex];

					FormBookDisplay formBookDisplay =
						new FormBookDisplay(Books.Inst[mruItem.Index], null, mruItem.BookScript);
					formBookDisplay.MdiParent = this;
					formBookDisplay.Show();
				}
			}
			catch (Exception)
			{ }
		}

		private FormSelectBook formSelectBook;

		private void SelectBook()
		{
			if (formSelectBook == null)
			{
				formSelectBook = new FormSelectBook();
				formSelectBook.MdiParent = this;
			}
			else
				ResetCloseReason(formSelectBook);

			formSelectBook.Show();
			AppState.Inst.SelectFormShown = true;
			formSelectBook.BringToFront();
		}

		public void BookDisplay(Book book)
		{
			BookDisplay(book, "");
		}

		// for opening book with search results
		public void BookDisplay(Book book, List<string> terms)
		{
			FormBookDisplay formBookDisplay = new FormBookDisplay(book, terms);
			formBookDisplay.MdiParent = this;
			formBookDisplay.Show();

			tsmiCascade.Enabled = true;
			tsmiTileHorizontal.Enabled = true;
			tsmiTileVertical.Enabled = true;
		}

		// for opening book with search results from multi-word search
		public void BookDisplay(MatchingMultiWordBook mmwb)
		{
			FormBookDisplay formBookDisplay = new FormBookDisplay(mmwb);
			formBookDisplay.MdiParent = this;
			formBookDisplay.Show();

			tsmiCascade.Enabled = true;
			tsmiTileHorizontal.Enabled = true;
			tsmiTileVertical.Enabled = true;
		}

		// for opening linked book, e.g. Atthakatha for a Mula book
		public void BookDisplay(Book book, string anchor)
		{
			FormBookDisplay formBookDisplay = new FormBookDisplay(book, anchor);
			formBookDisplay.MdiParent = this;
			formBookDisplay.Show();

			tsmiCascade.Enabled = true;
			tsmiTileHorizontal.Enabled = true;
			tsmiTileVertical.Enabled = true;
		}

		// called by FormBookDisplay's FormClosed event so that Cascade/Tile menu items can be disabled
		public void BookClosed(FormBookDisplay closingForm)
		{
			AppState.Inst.MruList.Add(new MruListItem(closingForm.Book.Index, closingForm.BookScript));
			PopulateMRU();

			bool bookIsOpen = false;
			foreach (Form form in MdiChildren)
			{
				if (form is FormBookDisplay && form != closingForm)
					bookIsOpen = true;
			}

			tsmiCascade.Enabled = bookIsOpen;
			tsmiTileHorizontal.Enabled = bookIsOpen;
			tsmiTileVertical.Enabled = bookIsOpen;
		}

		public void OpenReport(string report, string xslStem)
		{
			FormReport frmReport = new FormReport(report, xslStem);
			frmReport.MdiParent = this;
			frmReport.Show();
		}

		private FormSearch searchForm;

		public void SearchWord()
		{
			SearchWord("");
		}

		public void SearchWord(string term)
		{
			if (searchForm == null)
			{
				searchForm = new FormSearch();
				searchForm.MdiParent = this;
			}
			else
				ResetCloseReason(searchForm);

			searchForm.txtSearchTerms.Text = term;
			searchForm.Show();
			AppState.Inst.SearchFormShown = true;
			searchForm.BringToFront();
			searchForm.DoSearch();
		}

		private FormAdvancedSearch advancedSearchForm;

		public void AdvancedSearch()
		{
			if (advancedSearchForm == null)
			{
				advancedSearchForm = new FormAdvancedSearch();
				advancedSearchForm.MdiParent = this;
			}
			else
				ResetCloseReason(advancedSearchForm);

			advancedSearchForm.Show();
			advancedSearchForm.BringToFront();
		}

		public void GoTo()
		{
			FormBookDisplay formBookDisplay = (FormBookDisplay)ActiveMdiChild;
			FormGoTo formGoTo = new FormGoTo(formBookDisplay);

			// center the GoTo window on the book window
			formGoTo.StartPosition = FormStartPosition.Manual;
			int hmiddle = formBookDisplay.Location.X + formBookDisplay.Size.Width / 2;
			int vmiddle = formBookDisplay.Location.Y + formBookDisplay.Size.Height / 2;
			formGoTo.Location = new Point(hmiddle - formGoTo.Size.Width / 2, vmiddle - formGoTo.Size.Height / 2);

			if (formGoTo.ShowDialog(this) == DialogResult.OK)
			{
				string prefix = ""; ;
				string number = formGoTo.textBoxNumber.Text;
				if (formGoTo.radioButtonParagraph.Checked)
					prefix = "para";
				else
				{
					if (formGoTo.radioButtonVriPage.Checked)
						prefix = "V";
					else if (formGoTo.radioButtonThaiPage.Checked)
						prefix = "T";
					else if (formGoTo.radioButtonPtsPage.Checked)
						prefix = "P";
					else if (formGoTo.radioButtonMyanmarPage.Checked)
						prefix = "M";
					else if (formGoTo.radioButtonOtherPage.Checked)
						prefix = "O";
				}
				formBookDisplay.GoToAnchor(prefix, number);
			}
		}

		private FormDictionary formDict;

		public void OpenDictionary(string searchedWord)
		{
			if (formDict == null)
			{
				formDict = new FormDictionary(searchedWord);
				formDict.MdiParent = this;
			}
			else
			{
				ResetCloseReason(formDict);
				formDict.SetSearchedWord(searchedWord);
			}

			formDict.Show();
			AppState.Inst.DictionaryShown = true;
			formDict.BringToFront();
		}

		private void Exit()
		{
			this.Close();
		}

		private void WindowLayout(MdiLayout mdiLayout)
		{
			if (formSelectBook != null && formSelectBook.Visible)
				formSelectBook.Hide();

			if (searchForm != null && searchForm.Visible)
				searchForm.Hide();

			if (formDict != null && formDict.Visible)
				formDict.Hide();

			this.LayoutMdi(mdiLayout);
		}

		private void Print()
		{
			if (ActiveMdiChild is FormBookDisplay)
				((FormBookDisplay)ActiveMdiChild).Print();
			else if (ActiveMdiChild is FormReport)
				((FormReport)ActiveMdiChild).Print();
		}

		private void PrintPreview()
		{
			if (ActiveMdiChild is FormBookDisplay)
				((FormBookDisplay)ActiveMdiChild).PrintPreview();
			else if (ActiveMdiChild is FormReport)
				((FormReport)ActiveMdiChild).PrintPreview();
		}

		private void PageSetup()
		{
			if (ActiveMdiChild is FormBookDisplay)
				((FormBookDisplay)ActiveMdiChild).PageSetup();
			else if (ActiveMdiChild is FormReport)
				((FormReport)ActiveMdiChild).PageSetup();
		}

		private void Save()
		{
		}

		// This is a workaround for the behavior of the MenuStrip's MdiWindowListItem implementation.
		// If you hide a form when a user closes a window, instead of letting the Close() execute, the 
		// window will not show up in the Window menu list when it is reshown.
		// http://forums.microsoft.com/MSDN/ShowPost.aspx?PostID=1090830&SiteID=1
		private void ResetCloseReason(Form form)
		{
			try
			{
				// Hack: reset the form's internal closeReason field
				System.Reflection.FieldInfo fi = typeof(Form).GetField("closeReason", BindingFlags.Instance | BindingFlags.NonPublic);
				fi.SetValue(form, CloseReason.None);
				form.Visible = true;
			}
			catch (Exception)
			{ }
		}

		public void OpenBrowser(string url)
		{
			FormBrowser formBrowser = new FormBrowser(url);
			formBrowser.MdiParent = this;
			formBrowser.Show();
		}

		private void tsbOpenBook_Click(object sender, EventArgs e)
		{
			SelectBook();
		}

		private void tsbSave_Click(object sender, EventArgs e)
		{
			Save();
		}

		private void tsbPrint_Click(object sender, EventArgs e)
		{
			Print();
		}

		private void tsbPrintPreview_Click(object sender, EventArgs e)
		{
			PrintPreview();
		}

		private void tsbPageSetup_Click(object sender, EventArgs e)
		{
			PageSetup();
		}

		private void tsbSearchWord_Click(object sender, EventArgs e)
		{
			SearchWord();
		}

		private void tsbGoto_Click(object sender, EventArgs e)
		{
			GoTo();
		}

		private void tsmiOpenBook_Click(object sender, EventArgs e)
		{
			SelectBook();
		}

		private void tsmiPrint_Click(object sender, EventArgs e)
		{
			Print();
		}

		private void tsmiPrintPreview_Click(object sender, EventArgs e)
		{
			PrintPreview();
		}

		private void tsmiPageSetup_Click(object sender, EventArgs e)
		{
			PageSetup();
		}

		private void tsmiSave_Click(object sender, EventArgs e)
		{
			Save();
		}

		private void tsmiExit_Click(object sender, EventArgs e)
		{
			Exit();
		}

		private void tsmiSearchWord_Click(object sender, EventArgs e)
		{
			SearchWord();
		}

		private void tsmiDictionary_Click(object sender, EventArgs e)
		{
			OpenDictionary("");
		}

		private void tsmiCascade_Click(object sender, EventArgs e)
		{
			WindowLayout(System.Windows.Forms.MdiLayout.Cascade);
		}

		private void tsmiTileHorizontal_Click(object sender, EventArgs e)
		{
			WindowLayout(System.Windows.Forms.MdiLayout.TileHorizontal);
		}

		private void tsmiTileVertical_Click(object sender, EventArgs e)
		{
			WindowLayout(System.Windows.Forms.MdiLayout.TileVertical);
		}

		private void tsmiContents_Click(object sender, EventArgs e)
		{

		}

		private void tsmiCheckForUpdates_Click(object sender, EventArgs e)
		{
			MessageBox.Show(Fonts.WindowsVersion.ToString());
		}

		private void tsmiAbout_Click(object sender, EventArgs e)
		{
			//SplashScreen.ShowAboutScreen(3000);
			AboutBox about = new AboutBox();
			about.ShowDialog();
		}

		private void tscbPaliScript_SelectedIndexChanged(object sender, EventArgs e)
		{
			Cursor.Current = Cursors.WaitCursor;

			int index = tscbPaliScript.SelectedIndex;
			Script script = Script.Devanagari;

			switch (index)
			{
				case 0:
					script = Script.Bengali;
					break;
				case 1:
					script = Script.Cyrillic;
					break;
				case 2:
					script = Script.Devanagari;
					break;
				case 3:
					script = Script.Gujarati;
					break;
				case 4:
					script = Script.Gurmukhi;
					break;
				case 5:
					script = Script.Kannada;
					break;
				case 6:
					script = Script.Khmer;
					break;
				case 7:
					script = Script.Malayalam;
					break;
				case 8:
					script = Script.Myanmar;
					break;
				case 9: // "Roman"
					script = Script.Latin;
					break;
				case 10:
					script = Script.Sinhala;
					break;
				case 11:
					script = Script.Telugu;
					break;
				case 12:
					script = Script.Thai;
					break;
				case 13:
					script = Script.Tibetan;
					break;
			}

			AppState.Inst.CurrentScript = script;

			foreach (Form form in MdiChildren)
			{
				if (form is FormBookDisplay)
				{
					((FormBookDisplay)form).ChangeScript(AppState.Inst.CurrentScript);
				}
				else if (form is FormSelectBook)
				{
					((FormSelectBook)form).ChangeScript();
				}
				else if (form is FormSearch)
				{
					((FormSearch)form).ChangeScript();
				}
				else if (form is FormDictionary)
				{
					((FormDictionary)form).ChangeScript();
				}
			}

			Cursor.Current = Cursors.Default;
		}

		private void tscbInterfaceLanguage_SelectedIndexChanged(object sender, EventArgs e)
		{
			FormLanguageSwitchSingleton.Instance.ChangeCurrentThreadUICulture(((CultureInfoDisplayItem)tscbInterfaceLanguage.SelectedItem).CultureInfo);
			FormLanguageSwitchSingleton.Instance.ChangeLanguage(this);

			foreach (Form form in MdiChildren)
			{
				if (form is FormBookDisplay)
				{
					((FormBookDisplay)form).ResizeBrowserControl();
				}
			}
		}

		private void FormMainNew_MdiChildActivate(object sender, EventArgs e)
		{
			tsbGoto.Enabled = (ActiveMdiChild is FormBookDisplay);

			if (ActiveMdiChild is FormBookDisplay ||
				ActiveMdiChild is FormReport)
			{
				tsbPrint.Enabled = true;
				tsmiPrint.Enabled = true;

				tsbPageSetup.Enabled = true;
				tsmiPageSetup.Enabled = true;

				tsbPrintPreview.Enabled = true;
				tsmiPrintPreview.Enabled = true;

				tsbSave.Enabled = true;
				tsmiSave.Enabled = true;

				//((FormBookDisplay)ActiveMdiChild).SetWebBrowserUnFocus();
			}
			else
			{
				tsbPrint.Enabled = false;
				tsmiPrint.Enabled = false;

				tsbPageSetup.Enabled = false;
				tsmiPageSetup.Enabled = false;

				tsbPrintPreview.Enabled = false;
				tsmiPrintPreview.Enabled = false;

				tsbSave.Enabled = false;
				tsmiSave.Enabled = false;
			}

			if (ActiveMdiChild is FormSelectBook)
			{
				tsbOpenBook.Enabled = false;
				tsmiOpenBook.Enabled = false;
			}
			else
			{
				tsbOpenBook.Enabled = true;
				tsmiOpenBook.Enabled = true;
			}

			if (ActiveMdiChild is FormSearch)
			{
				tsbSearchWord.Enabled = false;
				tsmiSearchWord.Enabled = false;
			}
			else
			{
				tsbSearchWord.Enabled = true;
				tsmiSearchWord.Enabled = true;
			}

			if (ActiveMdiChild is FormDictionary)
			{
				tsbDictionary.Enabled = false;
				tsmiDictionary.Enabled = false;
			}
			else
			{
				tsbDictionary.Enabled = true;
				tsmiDictionary.Enabled = true;
			}
		}

		private void FormMainNew_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control)
			{
				if (e.KeyCode == Keys.D)
				{
					// prevent a double call to OpenDictionary, the first
					// from FormBookDisplay
					if ((ActiveMdiChild is FormDictionary) == false)
						OpenDictionary("");
				}
				else if (e.KeyCode == Keys.O)
					SelectBook();
				else if (e.KeyCode == Keys.W)
				{
					// prevent a double call to SearchWord, the first
					// from FormBookDisplay
					if ((ActiveMdiChild is FormSearch) == false)
						SearchWord();
				}
			}
		}

		private void tsbDictionary_Click(object sender, EventArgs e)
		{
			if (ActiveMdiChild is FormBookDisplay)
			{
				string selection = ((FormBookDisplay)ActiveMdiChild).GetBrowserSelection();

				// don't lookup multi-word selection
				if (selection.IndexOf(" ") >= 0)
					selection = "";

				OpenDictionary(selection);
			}
			else
				OpenDictionary("");
		}

		private void windowToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
		{
			// Hide separator if it is the last menu strip item in
			// the window list menu
			ToolStripItemCollection items = menuStrip1.MdiWindowListItem.DropDownItems;
			if (items[items.Count - 1] is ToolStripSeparator)
				items.RemoveAt(items.Count - 1);

			// Force a refresh of the window captions in the Window menu. This workaround from here:
			// http://forums.microsoft.com/MSDN/ShowPost.aspx?PostID=377528&SiteID=1
			if (this.ActiveMdiChild != null)
			{
				Form activeChild = this.ActiveMdiChild;

				ActivateMdiChild(null);
				ActivateMdiChild(activeChild);
			}
		}

		private void advancedToolStripMenuItem_Click(object sender, EventArgs e)
		{
			AdvancedSearch();
		}
	}
}