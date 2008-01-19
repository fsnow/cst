using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using CST.Conversion;

namespace CST
{
    [Serializable()]
    public class AppStateBookWindow
    {
        public FormWindowState WindowState
        {
            get { return windowState; }
            set { windowState = value; }
        }
        private FormWindowState windowState;

        public Size Size
        {
            get { return size; }
            set { size = value; }
        }
        private Size size;

        public Point Location
        {
            get { return location; }
            set { location = value; }
        }
        private Point location;

        public int BookIndex
        {
            get { return bookIndex; }
            set { bookIndex = value; }
        }
        private int bookIndex;

        public Script BookScript
        {
            get { return bookScript; }
            set { bookScript = value; }
        }
        private Script bookScript;

        public bool ShowFootnotes
        {
            get { return showFootnotes; }
            set { showFootnotes = value; }
        }
        private bool showFootnotes;

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

        public bool ShowTerms
        {
            get { return showTerms; }
            set { showTerms = value; }
        }
        private bool showTerms;
    }
}
