using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using CST;
using CST.Conversion;
using mshtml;

namespace CST
{
	public partial class FormBookDisplay : Form
	{
		public FormBookDisplay(Book book, List<string> terms)
		{
			InitializeComponent();

			this.book = book;
			this.terms = terms;
			this.bookScript = AppState.Inst.CurrentScript;

			Init();
		}

		public FormBookDisplay(Book book, string anchor)
		{
			InitializeComponent();

			this.initialAnchor = anchor;
			this.book = book;
			this.bookScript = AppState.Inst.CurrentScript;

			Init();
		}

		public FormBookDisplay(Book book, List<string> terms, Script script)
		{
			InitializeComponent();

			this.book = book;
			this.terms = terms;
			this.bookScript = script;

			Init();
		}

		public FormBookDisplay(MatchingMultiWordBook mmwb)
		{
			InitializeComponent();

			this.book = mmwb.Book;
			this.bookScript = AppState.Inst.CurrentScript;
			this.mmwb = mmwb;

			Init();
		}

		private void Init()
		{
			SetHitButtonsEnabled();
			ResizeBrowserControl();

			// Get the chapter list from the ChapterLists class. If the book contains structural markup,
			// the chapter list was compiled at the same time that the book was indexed for search.
			List<DivTag> chapterList = ChapterLists.Inst[book.Index];
			if (chapterList == null)
				tscbChapterList.Visible = false;
			else
			{
				foreach (DivTag divTag in chapterList)
				{
					divTag.BookScript = this.bookScript;
				}

				tscbChapterList.Items.AddRange(chapterList.ToArray());
				ignoreChapterListChanged = true;

				if (tscbChapterList.Items.Count > 0)
					tscbChapterList.SelectedIndex = 0;
			}

			ChangeScript(bookScript); // change event calls OpenBook

			if (totalHits == 0)
			{
				tsmiShowSearchTerms.Visible = false;
				tsbFirstResult.Visible = false;
				tsbPreviousResult.Visible = false;
				tsbNextResult.Visible = false;
				tsbLastResult.Visible = false;
			}

			vPage = "*";
			mPage = "*";
			tPage = "*";
			pPage = "*";
			oPage = "*";
			

			if (book.MulaIndex < 0 && book.AtthakathaIndex < 0 && book.TikaIndex < 0)
			{
				tsbMula.Visible = false;
				tsbAtthakatha.Visible = false;
				tsbTika.Visible = false;
			}
			else
			{
				tsbMula.Enabled = (book.MulaIndex >= 0);
				tsbAtthakatha.Enabled = (book.AtthakathaIndex >= 0);
				tsbTika.Enabled = (book.TikaIndex >= 0);
			}

			tsslDebug.Text = "";
		}

		public Script BookScript
		{
			get { return bookScript; }
			set { bookScript = value; }
		}
		private Script bookScript;

		public Book Book
		{
			get { return book; }
			set { book = value; }
		}
		private Book book;

		public List<string> Terms
		{
			get { return terms; }
			set { terms = value; }
		}
		private List<string> terms;

		public MatchingMultiWordBook Mmwb
		{
			get { return mmwb; }
			set { mmwb = value; }
		}
		private MatchingMultiWordBook mmwb;


		private string initialAnchor;
		private int currentHit;
		private int totalHits;
		private Dictionary<string, HtmlElement> anchors;
		private int docHeight = 0;
		private List<HtmlElement> vPages;
		private List<HtmlElement> mPages;
		private List<HtmlElement> pPages;
		private List<HtmlElement> tPages;
		private List<HtmlElement> oPages;

		// public so that FormGoTo can read these and disable missing numbering systems
		public string vPage;
		public string mPage;
		public string tPage;
		public string pPage;
		public string oPage;

		private List<HtmlElement> vParas;
		//private string vPara;
		private List<HtmlElement> vParasWithBook;
		//private string vParaWithBook;
		private Dictionary<string, HtmlElement> divDict;
		private List<HtmlElement> divList;
		private string divId;
		private bool ignoreChapterListChanged;
		private int ticks = 0;
		private bool documentCompleted = false;

		private void OpenBook()
		{
			string bookPath = Config.Inst.XmlDirectory + Path.DirectorySeparatorChar + book.FileName;
			
			StreamReader sr = new StreamReader(bookPath);
			string devXml = sr.ReadToEnd();
			sr.Close();

			if (terms != null && terms.Count > 0)
				devXml = Search.HighlightTerms(devXml, book.DocId, terms, out totalHits);

			if (mmwb != null && mmwb.WordPositions != null && mmwb.WordPositions.Count > 0)
				devXml = Search.HighlightMultiWordTerms(devXml, book.DocId, mmwb.WordPositions, out totalHits);

			// Remove stylesheet processing directive. 
			// These are not executed in the WebBrowser control.
			devXml = Regex.Replace(devXml, @"<\?xml-stylesheet.+?\?>", "");

			string xml = ScriptConverter.ConvertBook(devXml, bookScript);

			// transform the document with XSL
			XslCompiledTransform xslt = new XslCompiledTransform(true);
			xslt.Load(Config.Inst.XslDirectory + Path.DirectorySeparatorChar + "tipitaka-" +
				ScriptConverter.Iso15924Code(bookScript) + ".xsl");

			byte[] xmlArr = new UnicodeEncoding().GetBytes(xml);
			MemoryStream xmlStream = new MemoryStream(xmlArr);
			xmlStream.Seek(0, SeekOrigin.Begin);

			MemoryStream htmlStream = new MemoryStream();
			xslt.Transform(XmlReader.Create(xmlStream), null, htmlStream);
			htmlStream.Seek(0, SeekOrigin.Begin);
			documentCompleted = false;
			webBrowser.DocumentStream = htmlStream;
			string windowTitle = ScriptConverter.Convert(book.LongNavPath,
				Script.Devanagari, bookScript, true);
			// put spaces around the path slashes for display
			windowTitle = windowTitle.Replace("/", " / ");
			this.Text = windowTitle;

			if (tscbChapterList.Items.Count > 0)
				tscbChapterList.SelectedIndex = 0;
		}

		private string GetBookCode()
		{
			string vParaWithBook = GetParaWithBook();
			int underscoreIndex = vParaWithBook.IndexOf('_');
			if (underscoreIndex > 0 && underscoreIndex < vParaWithBook.Length - 1)
				return vParaWithBook.Substring(underscoreIndex + 1);
			else
				return "";
		}

		// Handles the cases where there are paragraph ranges or gaps in the paragraph numbers.
		// E.g. if you are in AN7 at para 1 and click "Atthakatha" it opens AN7 Attha to para 1-5 (range)
		// if you are in AN7 at para 9 and click "Atthakatha" it opens AN7 Attha to para 8 
		// (there's a gap between 8 and 13)
		private string FindPreviousAnchor(string anchor)
		{
			string newAnchor = "";
			if (anchor.Contains("_"))
			{
				BookPara bp1 = ParseBookParaAnchor(anchor);

				bool found = false;
				int bestPara = 0;

				foreach (HtmlElement element in vParasWithBook)
				{
					if (element.Name.EndsWith("_" + bp1.book) == false)
					{
						if (found)
							break; // we've gone past the correct book
						else
							continue;
					}
					else
						found = true;

					BookPara bp2 = ParseBookParaAnchor(element.Name);
					if (bp2.para <= bp1.para && bp2.para > bestPara)
					{
						bestPara = bp2.para;
						newAnchor = element.Name;

						// we hit on the first number of a range, so no need to look farther
						if (bp1.para == bp2.para)
							break;
					}
				}
			}
			else
			{
				int p1 = ParseParaAnchor(anchor);
				int bestPara = 0;

				foreach (HtmlElement element in vParas)
				{
					int p2 = ParseParaAnchor(element.Name);
					if (p2 <= p1 && p2 > bestPara)
					{
						bestPara = p2;
						newAnchor = element.Name;

						// we hit on the first number of a range, so no need to look farther
						if (p1 == p2)
							break;
					}
				}
			}

			return newAnchor;
		}

		private BookPara ParseBookParaAnchor(string anchor)
		{
			BookPara bp;
			bp.book = "";
			bp.para = 0;

			// anchor looks like "para321_an6" or "para321-324_an6"
			string[] parts = anchor.Split(new char[] { '_' });
			if (parts.Length != 2 || parts[0].StartsWith("para") == false)
				return bp;

			string para = parts[0];
			bp.book = parts[1];

			// chop off "para"
			para = para.Substring(4);

			// chop off the range part, leaving only the first number
			if (para.Contains("-"))
				para = para.Substring(0, para.IndexOf('-'));

			try
			{
				bp.para = Convert.ToInt32(para);
			}
			catch (Exception)
			{ }

			return bp;
		}

		private int ParseParaAnchor(string anchor)
		{
			int nPara = 0;

			// anchor looks like "para321" or "para321-324"
			if (anchor.StartsWith("para") == false)
				return nPara;

			// chop off "para"
			string para = anchor.Substring(4);

			// chop off the range part, leaving only the first number
			if (para.Contains("-"))
				para = para.Substring(0, para.IndexOf('-'));

			try
			{
				nPara = Convert.ToInt32(para);
			}
			catch (Exception)
			{ }

			return nPara;
		}

		private void SafeScrollIntoView(string anchor)
		{
			HtmlElement element = webBrowser.Document.GetElementById(anchor);
			if (element != null)
				element.ScrollIntoView(true);
		}

		private string GetPara()
		{
			int docPos = this.webBrowser.Document.Body.ScrollRectangle.Y;
			return FindScrollTopElementName(vParas, docPos);
		}

		private string GetParaWithBook()
		{
			int docPos = this.webBrowser.Document.Body.ScrollRectangle.Y;
			return FindScrollTopElementName(vParasWithBook, docPos);
		}

		private void GetInitialPageStatus()
		{
			vPage = ParsePage(FindFirstElementName(vPages));
			mPage = ParsePage(FindFirstElementName(mPages));
			pPage = ParsePage(FindFirstElementName(pPages));
			tPage = ParsePage(FindFirstElementName(tPages));
			oPage = ParsePage(FindFirstElementName(oPages));
			//vPara = FindFirstElementName(vParas);
			//vParaWithBook = FindFirstElementName(vParasWithBook);

			//tsslDebug.Text = vParaWithBook;

			SetPageStatusText();
		}

		private void CalculatePageStatus()
		{
			int docPos = 0;

			// these can be null if the user drags a PDF file onto a book window
			if (webBrowser.Document != null &&
				webBrowser.Document.Body != null &&
				webBrowser.Document.Body.ScrollRectangle != null)
			{
				docPos = webBrowser.Document.Body.ScrollRectangle.Y;
			}

			vPage = ParsePage(FindScrollTopElementName(vPages, docPos));
			mPage = ParsePage(FindScrollTopElementName(mPages, docPos));
			pPage = ParsePage(FindScrollTopElementName(pPages, docPos));
			tPage = ParsePage(FindScrollTopElementName(tPages, docPos));
			oPage = ParsePage(FindScrollTopElementName(oPages, docPos));
			//vPara = FindScrollTopElementName(vParas, docPos);
			//vParaWithBook = FindScrollTopElementName(vParasWithBook, docPos);

			//tsslDebug.Text = vParaWithBook;

			SetPageStatusText();

			// set chapter list selection for current scroll position
			divId = FindScrollTopDivId(docPos);
			//tsslDebug.Text = divId;
			
			// build a hashtable mapping divIds to chapter list indexes instead of doing this linear search?
			int i = 0;
			for (i = 0; i < tscbChapterList.Items.Count; i++)
			{
				DivTag divTag = (DivTag)tscbChapterList.Items[i];
				if (divTag.Id == divId)
					break;
			}

			if (i != tscbChapterList.SelectedIndex && i < tscbChapterList.Items.Count)
			{
				ignoreChapterListChanged = true;
				tscbChapterList.SelectedIndex = i;
				webBrowser.Focus();
			}
		}

		private void SetPageStatusText()
		{
			tsslPages.Text = String.Format(
				CST.Properties.Resources.PageNumbersStatusFormat,
				vPage, mPage, pPage, tPage, oPage);

			//tsslDebug.Text = "DEBUG timer ticks: " + ticks;
		}

		private string FindFirstElementName(List<HtmlElement> pages)
		{
			if (pages.Count == 0)
				return "";
			else
				return pages[0].Name;
		}

		private string FindScrollTopElementName(List<HtmlElement> pages, int docPos)
		{
			if (pages.Count == 0)
				return "";

			// trivial case: top of document
			if (docPos == 0)
				return pages[0].Name;

			// get the document length in pixels if not already set
			if (docHeight == 0)
				docHeight = this.webBrowser.Document.Body.ScrollRectangle.Height;

			// calculate a best guess page index based on the current scroll position
			double scrollRatio = 0.0;
			if (docHeight > 0)
				scrollRatio = ((double)docPos) / ((double)docHeight);

			// sanity check the scroll ratio in case docPos or docHeight are bogus.
			// This has been seen and caused an IndexOutOfBounds exception below.
			if (scrollRatio < 0)
				scrollRatio = 0;

			if (scrollRatio >= 1.0)
				scrollRatio = 0.999;

			int startIndex = (int)Math.Floor(pages.Count * scrollRatio);

			HtmlElement lastElement = null;

			// if the best guess anchor is below the top of the page, search linearly backwards.
			if (pages[startIndex].OffsetRectangle.Y > docPos)
			{
				for (int i = startIndex; i >= 0; i--)
				{
					lastElement = pages[i];

					if (lastElement.OffsetRectangle.Y <= docPos)
						break;
				}
			}
			// else, search linearly forwards
			else
			{
				for (int i = startIndex; i < pages.Count; i++)
				{
					HtmlElement element = pages[i];

					if (lastElement == null)
						lastElement = element;

					if (element.OffsetRectangle.Y > docPos)
						break;

					lastElement = element;
				}
			}

			// return the element name
			if (lastElement != null)
				return lastElement.Name;
			else
				return "";
		}

		private string FindScrollTopDivId(int docPos)
		{
			if (divList.Count == 0)
				return "";

			// trivial case: top of document
			if (docPos == 0)
				return divList[0].Name;

			// add a few pixels to docPos, so that if a chapter heading is displaying at the top, but has
			// a small amount of whitespace above it, this chapter will be displayed in the dropdown
			docPos += 20;

			// get the document length in pixels if not already set
			if (docHeight == 0)
				docHeight = this.webBrowser.Document.Body.ScrollRectangle.Height;

			// calculate a best guess page index based on the current scroll position
			double scrollRatio = 0.0;
			if (docHeight > 0)
				scrollRatio = ((double)docPos) / ((double)docHeight);

			// sanity check the scroll ratio in case docPos or docHeight are bogus.
			// This has been seen and caused an IndexOutOfBounds exception below.
			if (scrollRatio < 0)
				scrollRatio = 0;

			if (scrollRatio >= 1.0)
				scrollRatio = 0.999;

			int startIndex = (int)Math.Floor(divList.Count * scrollRatio);

			HtmlElement lastElement = null;

			// if the best guess anchor is below the top of the page, search linearly backwards.
			if (divList[startIndex].OffsetRectangle.Y > docPos)
			{
				for (int i = startIndex; i >= 0; i--)
				{
					lastElement = divList[i];

					if (lastElement.OffsetRectangle.Y <= docPos)
						break;
				}
			}
			// else, search linearly forwards
			else
			{
				for (int i = startIndex; i < divList.Count; i++)
				{
					HtmlElement element = divList[i];

					if (lastElement == null)
						lastElement = element;

					if (element.OffsetRectangle.Y > docPos)
						break;

					lastElement = element;
				}
			}

			// return the element name
			if (lastElement != null)
				return lastElement.Name;
			else
				return "";
		}

		private string ParsePage(string anchorName)
		{
			if (anchorName == null || anchorName.Length == 0)
				return "*";

			return (anchorName[1] == '0' ? "" : anchorName[1] + ".") + Convert.ToInt32(anchorName.Substring(3));
		}

		public void Print()
		{
			webBrowser.ShowPrintDialog();
		}

		public void PageSetup()
		{
			webBrowser.ShowPageSetupDialog();
		}

		public void PrintPreview()
		{
			webBrowser.ShowPrintPreviewDialog();
		}

		public void ResizeBrowserControl()
		{
			webBrowser.Height = this.ClientRectangle.Height -
				(toolStrip1.ClientRectangle.Height + statusStrip1.ClientRectangle.Height);
			webBrowser.Width = this.ClientRectangle.Width;
		}

		public void WebBrowserFocusAwayAndBack()
		{
			tscbPaliScript.Focus();
			webBrowser.Focus();
		}

		private void SetHitButtonsEnabled()
		{
			if (currentHit == 0 || totalHits <= 1)
			{
				tsbFirstResult.Enabled = false;
				tsbPreviousResult.Enabled = false;
			}
			else
			{
				tsbFirstResult.Enabled = true;
				tsbPreviousResult.Enabled = true;
			}

			if (currentHit >= totalHits - 1 || totalHits <= 1)
			{
				tsbNextResult.Enabled = false;
				tsbLastResult.Enabled = false;
			}
			else
			{
				tsbNextResult.Enabled = true;
				tsbLastResult.Enabled = true;
			}
		}

		public void ChangeScript(Script script)
		{
			bookScript = script;

			switch (bookScript)
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
				case Script.Latin:  // "Roman"
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

		public void GoToAnchor(string prefix, string number)
		{
			if (prefix == "para")
			{
				string goToAnchor;

				// In the Multi case, we need to jump to the para in the book that we are in now, e.g. para234_an5.
				if (book.BookType == BookType.Multi)
					goToAnchor = prefix + number + "_" + GetBookCode();
				else
					goToAnchor = prefix + number;

				// handle the cases of ranges and gaps in paragraph numbers
				if (anchors.ContainsKey(goToAnchor) == false)
					goToAnchor = FindPreviousAnchor(goToAnchor);

				SafeScrollIntoView(goToAnchor);
			}
			else
			{
				// look for anchors that start with "0." up to "9."
				for (int i = 0; i <= 9; i++)
				{
					string anchor = prefix + i + "." + number.PadLeft(4, '0');
					if (anchors.ContainsKey(anchor))
					{
						HtmlElement target = anchors[anchor];
						if (target != null)
						{
							target.ScrollIntoView(true);
							break;
						}
					}
				}
			}
		}

		private void OpenLinkedBook(CommentaryLevel linkedBookType)
		{
			Book linkedBook = null;
			if (linkedBookType == CommentaryLevel.Mula)
				linkedBook = Books.Inst[book.MulaIndex];
			else if (linkedBookType == CommentaryLevel.Atthakatha)
				linkedBook = Books.Inst[book.AtthakathaIndex];
			else if (linkedBookType == CommentaryLevel.Tika)
				linkedBook = Books.Inst[book.TikaIndex];
			else
				return;

			if (book.BookType == BookType.Whole)
			{
				// linkedBook is only correct if this book and the linked book are of type WholeBook.
				if (linkedBook.BookType == BookType.Whole)
					((FormMain)MdiParent).BookDisplay(linkedBook, GetPara());
				else if (linkedBook.BookType == BookType.Multi)
				{
					// get book code and jump to the correct anchor with book, e.g. para345_an4
					((FormMain)MdiParent).BookDisplay(linkedBook, GetParaWithBook());
				}
				else if (linkedBook.BookType == BookType.Split)
				{
					// parse integer from vPara and then do conditional for each case
					string para = GetPara();
					int paraNum = ParseParaAnchor(para);

					if (book.Index == 29) // Theragatha
					{
						if (paraNum > 266)
							linkedBook = Books.Inst[84];
					}
					else if (book.Index == 43 || book.Index == 100)
					{
						if (paraNum > 23)
							linkedBook = Books.Inst[128];
					}

					((FormMain)MdiParent).BookDisplay(linkedBook, para);
				}
				else
					return;
			}
			else if (book.BookType == BookType.Multi)
			{
				if (linkedBook.BookType == BookType.Whole)
				{
					// get book code for current position and jump to the book. conditionals for each case.
					string bookCode = GetBookCode();
					if (book.Index == 73 || book.Index == 122) // an2/3/4 attha or tika -> mul
					{
						// linkedBook is already an2, index 12
						if (bookCode.Equals("an3"))
							linkedBook = Books.Inst[13];
						else if (bookCode.Equals("an4"))
							linkedBook = Books.Inst[14];
					}
					else if (book.Index == 74 || book.Index == 123) // an5/6/7 attha or tika -> mul
					{
						// linkedBook is already an5, index 15
						if (bookCode.Equals("an6"))
							linkedBook = Books.Inst[16];
						else if (bookCode.Equals("an7"))
							linkedBook = Books.Inst[17];
					}
					else if (book.Index == 75 || book.Index == 124) // an8/9/10/11 attha or tika -> mul
					{
						// linkedBook is already an8, index 18
						if (bookCode.Equals("an9"))
							linkedBook = Books.Inst[19];
						else if (bookCode.Equals("an10"))
							linkedBook = Books.Inst[20];
						else if (bookCode.Equals("an11"))
							linkedBook = Books.Inst[21];
					}

					((FormMain)MdiParent).BookDisplay(linkedBook, GetParaWithBook());
				}
				else if (linkedBook.BookType == BookType.Multi)
				{
					// get book code for current position and jump to the correct 
					// anchor with book, e.g. para345_an4. conditionals for each case.
					((FormMain)MdiParent).BookDisplay(linkedBook, GetParaWithBook());
				}
				else if (linkedBook.BookType == BookType.Split)
				{ }
				else
					return;
			}
			else if (book.BookType == BookType.Split)
			{
				if (linkedBook.BookType == BookType.Whole)
					((FormMain)MdiParent).BookDisplay(linkedBook, GetPara());
				else if (linkedBook.BookType == BookType.Multi)
				{ }
				else if (linkedBook.BookType == BookType.Split)
				{ }
				else
					return;
			}
			else
				return;
		}

		private void Translate()
		{
			//MessageBox.Show("Translate " + divId);

			// map the divId to a URL by some magic...
			string url = "";

			if (divId == "dn1_2")
				url = "http://www.accesstoinsight.org/tipitaka/dn/dn.02.0.than.html";
			else if (divId == "dn1_9")
				url = "http://www.accesstoinsight.org/tipitaka/dn/dn.09.0.than.html";

			if (url != null && url.Length > 0)
				((FormMain)MdiParent).OpenBrowser(url);
		}

		private void ShowSource(bool is1957)
		{
			//MessageBox.Show("Show Source " + divId + ", mPage " + mPage);

			Sources.SourceType st;
			if (is1957)
				st = Sources.SourceType.Burmese1957;
			else
				st = Sources.SourceType.Burmese2010;

			Sources.Source source = Sources.Inst.GetSource(book.FileName, st);
			if (source != null)
            {
				string url = source.Url;

				int pageOffset = 0;
				int dotIndex = mPage.LastIndexOf('.');
				if (dotIndex >= 0)
                {
					string afterDot = mPage.Substring(dotIndex + 1);
					if (Int32.TryParse(afterDot, out int pageNum))
						pageOffset = pageNum - 1;
				}
				url += "#page=" + (source.PageStart + pageOffset);

				((FormMain)MdiParent).OpenBrowser(url);
			}
		}

		private void tscbPaliScript_SelectedIndexChanged(object sender, EventArgs e)
		{
			/* // sometimes hangs the app
				if (this.webBrowser.Document != null)
				{
					height = this.webBrowser.Document.Body.ScrollRectangle.Height;
					y = this.webBrowser.Document.Body.ScrollRectangle.Y;
				}
				else
				{
					height = 1;
					y = 0;
				}
				*/

			switch (tscbPaliScript.SelectedIndex)
			{
				case 0:
					bookScript = Script.Bengali;
					break;
				case 1:
					bookScript = Script.Cyrillic;
					break;
				case 2:
					bookScript = Script.Devanagari;
					break;
				case 3:
					bookScript = Script.Gujarati;
					break;
				case 4:
					bookScript = Script.Gurmukhi;
					break;
				case 5:
					bookScript = Script.Kannada;
					break;
				case 6:
					bookScript = Script.Khmer;
					break;
				case 7:
					bookScript = Script.Malayalam;
					break;
				case 8:
					bookScript = Script.Myanmar;
					break;
				case 9: // "Roman"
					bookScript = Script.Latin;
					break;
				case 10:
					bookScript = Script.Sinhala;
					break;
				case 11:
					bookScript = Script.Telugu;
					break;
				case 12:
					bookScript = Script.Thai;
					break;
				case 13:
					bookScript = Script.Tibetan;
					break;
			}

			OpenBook();
			ReloadTSCBItems(tscbChapterList);
			SetHitButtonsEnabled();

			if (MdiParent != null)
				((FormMain)MdiParent).menuStrip1.Refresh();
		}

		private void ReloadTSCBItems(ToolStripComboBox tscb)
		{
			if (tscb.Items.Count > 0)
			{
				ignoreChapterListChanged = true;

				object[] items = new object[tscb.Items.Count];
				tscb.Items.CopyTo(items, 0);

				foreach (DivTag divTag in items)
				{
					divTag.BookScript = bookScript;
				}

				int selectedIndex = tscb.SelectedIndex;
				tscb.Items.Clear();
				for (int i = 0; i < items.Length; i++)
					tscb.Items.Add(items[i]);
				tscb.SelectedIndex = selectedIndex;
			}
		}
		private void webBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			/*
            double scrollRatio = 0;
            if (height > 0)
                scrollRatio = (double)y / (double)height;

            height = webBrowser.Document.Body.ScrollRectangle.Height;
            y = (int)System.Math.Round(height * scrollRatio);

            webBrowser.Document.Body.ScrollTop = y;*/

			documentCompleted = true;

			if (anchors == null)
				anchors = new Dictionary<string, HtmlElement>();
			else
				anchors.Clear();

			if (vPages == null)
				vPages = new List<HtmlElement>();
			else
				vPages.Clear();

			if (mPages == null)
				mPages = new List<HtmlElement>();
			else
				mPages.Clear();

			if (pPages == null)
				pPages = new List<HtmlElement>();
			else
				pPages.Clear();

			if (tPages == null)
				tPages = new List<HtmlElement>();
			else
				tPages.Clear();

			if (oPages == null)
				oPages = new List<HtmlElement>();
			else
				oPages.Clear();

			if (vParas == null)
				vParas = new List<HtmlElement>();
			else
				vParas.Clear();

			if (vParasWithBook == null)
				vParasWithBook = new List<HtmlElement>();
			else
				vParasWithBook.Clear();

			if (divList == null)
				divList = new List<HtmlElement>();
			else
				divList.Clear();

			if (divDict == null)
				divDict = new Dictionary<string, HtmlElement>();
			else
				divDict.Clear();

			// A start at customizing the context menu of the web browser
			// see: http://www.codeproject.com/csharp/advhost.asp
			// and also Google for IDocHostUIHandler
			//SHDocVw.WebBrowser browser = webBrowser.ActiveXInstance as SHDocVw.WebBrowser;
			//browser.DocumentComplete += new SHDocVw.DWebBrowserEvents2_DocumentCompleteEventHandler(browser_DocumentComplete);

			try
			{
				// Document is null if a PDF file is dropped onto a book window
				if (webBrowser.Document == null)
					return;

				HtmlElementCollection hec = webBrowser.Document.GetElementsByTagName("a");
				foreach (HtmlElement element in hec)
				{
					string elementName = element.Name;

					if (anchors.ContainsKey(elementName))
					{
						// TODO: uncomment this and fix data files where page markers are duplicate
						//MessageBox.Show("Anchor already added: " + element.Name);
					}
					else
						anchors.Add(elementName, element);

					if (elementName.StartsWith("V"))
						vPages.Add(element);
					else if (elementName.StartsWith("M"))
						mPages.Add(element);
					else if (elementName.StartsWith("P"))
						pPages.Add(element);
					else if (elementName.StartsWith("T"))
						tPages.Add(element);
					else if (elementName.StartsWith("O"))
						oPages.Add(element);
					else if (elementName.StartsWith("dn") ||
							 elementName.StartsWith("mn") ||
							 elementName.StartsWith("an") ||
							 elementName.StartsWith("sn") ||
							 elementName.StartsWith("kn") ||
							 elementName.StartsWith("vin") ||
							 elementName.StartsWith("abhi"))
					{
						divList.Add(element);

						if (divDict.ContainsKey(elementName) == false)
							divDict.Add(elementName, element);
					}
					else if (elementName.Contains("para"))
					{
						// para elements will be of two types, without book (e.g. para11) and
						// with book (e.g. para11_an2). The latter are for handling the case where there are
						// multiple books in one file and consequently multiple paras of a given number.
						if (elementName.Contains("_"))
							vParasWithBook.Add(element);
						else
							vParas.Add(element);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}

			GetInitialPageStatus();

			// scroll to first highlighted term
			if ((terms != null && terms.Count > 0) || 
				(mmwb != null && mmwb.WordPositions != null && mmwb.WordPositions.Count > 0))
			{
				SafeScrollIntoView("hit0");
				currentHit = 0;
			}
			else if (initialAnchor != null && initialAnchor.Length > 0)
			{
				// handle the cases of ranges and gaps in paragraph numbers
				if (anchors.ContainsKey(initialAnchor) == false)
					initialAnchor = FindPreviousAnchor(initialAnchor);

				SafeScrollIntoView(initialAnchor);
			}

			webBrowser.Document.Window.Scroll += new HtmlElementEventHandler(Window_Scroll);
			webBrowser.Document.Body.KeyPress += new HtmlElementEventHandler(Body_KeyPress);
			webBrowser.Document.Body.KeyDown += new HtmlElementEventHandler(Body_KeyDown);
			webBrowser.Document.Body.Click += new HtmlElementEventHandler(Body_Click);
			webBrowser.Document.Body.DoubleClick += new HtmlElementEventHandler(Body_DoubleClick);

			ticks = 0;
			if (timer1.Enabled == false)
				timer1.Start();
		}

		void Body_Click(object sender, HtmlElementEventArgs e)
		{
			this.BringToFront();
			//tscbPaliScript.Focus();
			//webBrowser.Focus();
		}

		private void Window_Scroll(object sender, HtmlElementEventArgs e)
		{
			ticks = 0;
			if (timer1.Enabled == false)
				timer1.Start();
		}

		void Body_DoubleClick(object sender, HtmlElementEventArgs e)
		{
			// HACK: Doubleclicking a word in the browser to select it was causing a state where the 
			// keystrokes for Dictionary and GoTo were not working. This workaround, discovered by much trial
			// and error, takes away focus from the web browser then brings it back.
			this.BringToFront();
			tscbPaliScript.Focus();
			webBrowser.Focus();
		}

		public string GetBrowserSelection()
		{
			IHTMLDocument2 document = (IHTMLDocument2)webBrowser.Document.DomDocument;
			if (document.selection.type.ToLower() == "text")
			{
				IHTMLTxtRange range = (IHTMLTxtRange)document.selection.createRange();
				if (range == null || range.text == null)
					return "";

				return range.text.Trim();
			}
			else
				return "";
		}

		void Body_KeyDown(object sender, HtmlElementEventArgs e)
		{
			// doesn't work!
			if (e.KeyPressedCode == 116)
				e.ReturnValue = true;

			//this.toolStripStatusLabelDebug.Text = e.KeyPressedCode.ToString();
		}

		// The docs say that KeyPressedCode should be the ASCII value of the key pressed, but it
		// is actually the number corresponding to the letter of the alphabet. b = 2, etc.
		void Body_KeyPress(object sender, HtmlElementEventArgs e)
		{
			//this.toolStripStatusLabelDebug.Text = e.KeyPressedCode.ToString();

			if (e.CtrlKeyPressed)
			{
				// ctrl-G: GoTo.
				if (e.KeyPressedCode == 7)
				{
					((FormMain)MdiParent).GoTo();
				}
				// ctrl-D: Dictionary
				else if (e.KeyPressedCode == 4 || e.KeyPressedCode == 23)
				{
					string selection = GetBrowserSelection();

					// don't search or do a dictionary lookup for a multi-word selection
					if (selection.IndexOf(" ") < 0)
					{
						// Dictionary
						if (e.KeyPressedCode == 4)
							((FormMain)MdiParent).OpenDictionary(selection);
						// Word Search - IE eats ctrl-W
						else if (e.KeyPressedCode == 23)
							((FormMain)MdiParent).SearchWord(selection);
					}
				}
				// Q (Show Burmese 1957 edition)
				else if (e.KeyPressedCode == 17)
					ShowSource(true);
				// E (Show Burmese 2010 edition)
				else if (e.KeyPressedCode == 5)
					ShowSource(false);
				// T
				else if (e.KeyPressedCode == 20)
					Translate();
			}
			else
			{
				// doesn't work! (suppress Refresh)
				if (e.KeyPressedCode == 116)
				{
					e.ReturnValue = true;
				}
			}
		}

		private void FormBookDisplay_FormClosed(object sender, FormClosedEventArgs e)
		{
			((FormMain)MdiParent).BookClosed(this);
		}

		private void FormBookDisplay_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control)
			{
				if (e.KeyCode == Keys.D)
				{
					tscbPaliScript.Focus();
					webBrowser.Focus();

					string selection = GetBrowserSelection();

					// don't lookup multi-word selection
					if (selection.IndexOf(" ") >= 0)
						selection = "";
					
					((FormMain)MdiParent).OpenDictionary(selection);

				}
				else if (e.KeyCode == Keys.W)
				{
					tscbPaliScript.Focus();
					webBrowser.Focus();

					string selection = GetBrowserSelection();

					// don't lookup multi-word selection
					if (selection.IndexOf(" ") >= 0)
						selection = "";

					((FormMain)MdiParent).SearchWord(selection);
				}
				else if (e.KeyCode == Keys.Q)
					ShowSource(true);
				else if (e.KeyCode == Keys.E)
					ShowSource(false);
				else if (e.KeyCode == Keys.T)
					Translate();
			}
		}

		private void FormBookDisplay_Resize(object sender, EventArgs e)
		{
			ResizeBrowserControl();
		}

		private void FormBookDisplay_ResizeEnd(object sender, EventArgs e)
		{
			if (documentCompleted == false)
				return;

			// these can be null if the user drags a PDF file onto a book window
			if (this.webBrowser == null || 
				this.webBrowser.Document == null || 
				this.webBrowser.Document.Body == null || 
				this.webBrowser.Document.Body.ScrollRectangle == null)
				return;

			int height = this.webBrowser.Document.Body.ScrollRectangle.Height;

			if (height != docHeight)
				docHeight = height;

			ticks = 0;
			if (timer1.Enabled == false)
				timer1.Start();
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			if (documentCompleted == false)
				return;

			CalculatePageStatus();
			ticks++;

			if (ticks >= 2)
				timer1.Stop();
		}

		private void tscbChapterList_SelectedIndexChanged(object sender, EventArgs e)
		{
			// 1) when the user scrolls over a chapter boundary, we change the chapter list selection.
			// 2) when the user changes the chapter list selection, we navigate to an anchor at the top of the chapter.
			// in order to do #1 without doing #2 (and jumping to an anchor), we set a flag before 
			// setting SelectedIndex.
			if (ignoreChapterListChanged)
			{
				ignoreChapterListChanged = false;
				return;
			}

			DivTag divTag = (DivTag)tscbChapterList.SelectedItem;
			HtmlElement target = divDict[divTag.Id];
			if (target != null)
				target.ScrollIntoView(true);

			webBrowser.Focus();
		}

		private void tsbFirstResult_Click(object sender, EventArgs e)
		{
			webBrowser.Document.GetElementById("hit0").ScrollIntoView(true);
			currentHit = 0;
			SetHitButtonsEnabled();
		}

		private void tsbPreviousResult_Click(object sender, EventArgs e)
		{
			if (currentHit > 0)
			{
				currentHit--;
				webBrowser.Document.GetElementById("hit" + currentHit).ScrollIntoView(true);
				SetHitButtonsEnabled();
			}
		}

		private void tsbNextResult_Click(object sender, EventArgs e)
		{
			if (currentHit < totalHits - 1)
			{
				currentHit++;
				webBrowser.Document.GetElementById("hit" + currentHit).ScrollIntoView(true);
				SetHitButtonsEnabled();
			}
		}

		private void tsbLastResult_Click(object sender, EventArgs e)
		{
			webBrowser.Document.GetElementById("hit" + (totalHits - 1)).ScrollIntoView(true);
			currentHit = totalHits - 1;
			SetHitButtonsEnabled();
		}

		private void tsbMula_Click(object sender, EventArgs e)
		{
			OpenLinkedBook(CommentaryLevel.Mula);
		}

		private void tsbAtthakatha_Click(object sender, EventArgs e)
		{
			OpenLinkedBook(CommentaryLevel.Atthakatha);
		}

		private void tsbTika_Click(object sender, EventArgs e)
		{
			OpenLinkedBook(CommentaryLevel.Tika);
		}

		private void tsmiShowSearchTerms_Click(object sender, EventArgs e)
		{
			if (tsmiShowSearchTerms.Checked)
			{
				tsmiShowSearchTerms.Checked = false;
				webBrowser.Document.InvokeScript("setHitsVisibility", new object[] { false });
			}
			else
			{
				tsmiShowSearchTerms.Checked = true;
				webBrowser.Document.InvokeScript("setHitsVisibility", new object[] { true });
			}

			webBrowser.Document.GetElementById("hit" + currentHit).ScrollIntoView(true);
		}

		private void tsmiShowFootnotes_Click(object sender, EventArgs e)
		{
			if (tsmiShowFootnotes.Checked)
			{
				tsmiShowFootnotes.Checked = false;
				webBrowser.Document.InvokeScript("setFootnotesVisibility", new object[] { false });
			}
			else
			{
				tsmiShowFootnotes.Checked = true;
				webBrowser.Document.InvokeScript("setFootnotesVisibility", new object[] { true });
			}
		}
	}

	public struct BookPara
	{
		public string book;
		public int para;
	}
}