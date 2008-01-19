using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CST
{
	public partial class FormBrowser : Form
	{
		public FormBrowser(string url)
		{
			InitializeComponent();

			ResizeBrowserControl();
			webBrowser.Url = new Uri(url);
		}

		public void ResizeBrowserControl()
		{
			webBrowser.Height = this.ClientRectangle.Height; // -
				//(toolStrip1.ClientRectangle.Height + statusStrip1.ClientRectangle.Height);
			webBrowser.Width = this.ClientRectangle.Width;
		}

		private void webBrowser_Resize(object sender, EventArgs e)
		{
			ResizeBrowserControl();
		}

		private void webBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			this.Text = webBrowser.Document.Title;
		}
	}
}