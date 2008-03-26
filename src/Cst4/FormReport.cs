using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Xsl;
using CST.Conversion;

namespace CST
{
    public partial class FormReport : Form
    {
        public FormReport(string report, string xslStem)
        {
            InitializeComponent();

            ResizeBrowserControl();

			if (xslStem == null || xslStem.Length == 0)
				return;

            // load XSL file
            XslCompiledTransform xslt = new XslCompiledTransform();
            xslt.Load(Config.Inst.XslDirectory + Path.DirectorySeparatorChar + xslStem + "-" +
                ScriptConverter.Iso15924Code(AppState.Inst.CurrentScript) + ".xsl");

            // load report string into MemoryStream
            byte[] xmlArr = new UnicodeEncoding().GetBytes(report);
            MemoryStream xmlStream = new MemoryStream(xmlArr);
            xmlStream.Seek(0, SeekOrigin.Begin);

            // apply XSL transform to report
            MemoryStream htmlStream = new MemoryStream();
            xslt.Transform(XmlReader.Create(xmlStream), null, htmlStream);
            htmlStream.Seek(0, SeekOrigin.Begin);

            // load HTML into browser
            webBrowser.DocumentStream = htmlStream;
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

        private void ResizeBrowserControl()
        {
            webBrowser.Height = this.ClientRectangle.Height;
            webBrowser.Width = this.ClientRectangle.Width;
        }

        private void FormReport_Resize(object sender, EventArgs e)
        {
            ResizeBrowserControl();
        }
    }
}