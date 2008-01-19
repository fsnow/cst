using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using CST.Conversion;

namespace CST
{
    public partial class FormSelectBook : Form
    {
        public FormSelectBook()
        {
            InitializeComponent();

            ImageList imageList = new ImageList();
            imageList.ColorDepth = ColorDepth.Depth24Bit;
			imageList.Images.Add(CST.Properties.Resources.FolderClosed);
			imageList.Images.Add(CST.Properties.Resources.FolderOpen);
			imageList.Images.Add(CST.Properties.Resources.BookHardCvr);
            treeView1.ImageList = imageList;

            TreeNode node;
            foreach (Book book in Books.Inst)
            {
                string[] parts = book.LongNavPath.Split('/');
                // add a new root node
                node = treeView1.Nodes[parts[0]];
                if (node == null)
                {
                    node = treeView1.Nodes.Add(parts[0], GetNodeText(parts[0]));
                    node.Tag = new BookOpenTag(parts[0], null);

                    // there are no books at the root level
                    node.ImageIndex = closedFolderImage; 
                    node.SelectedImageIndex = closedFolderImage;
                }

                // add everything under the root
                for (int i = 1; i < parts.Length; i++)
                {
                    TreeNode node2 = node.Nodes[parts[i]];
                    if (node2 == null)
                    {
                        node = node.Nodes.Add(parts[i], GetNodeText(parts[i]));
                        node.Tag = new BookOpenTag(parts[i], ((i == parts.Length - 1) ? book : null));

                        if (i == parts.Length - 1)
                        {
                            // root nodes have a book image
                            node.ImageIndex = bookImage;
                            node.SelectedImageIndex = bookImage;
                        }
                        else
                        {
                            node.ImageIndex = closedFolderImage;
                            node.SelectedImageIndex = closedFolderImage;
                        }
                    }
                    else
                        node = node2;
                }
            }

            treeView1.Height = this.ClientRectangle.Height;
            treeView1.Width = this.ClientRectangle.Width;

            treeView1.Font = Fonts.GetControlFont(AppState.Inst.CurrentScript);
        }

        private int closedFolderImage = 0;
        private int openFolderImage = 1;
        private int bookImage = 2;

        private string GetNodeText(string dev)
        {
            return ScriptConverter.Convert(dev, Script.Devanagari, AppState.Inst.CurrentScript, true);
        }

        public void ChangeScript()
        {
            treeView1.BeginUpdate();
            treeView1.Font = Fonts.GetControlFont(AppState.Inst.CurrentScript);
            ChangeScript(treeView1.Nodes);
            treeView1.EndUpdate();
        }

        private void ChangeScript(TreeNodeCollection nodes)
        {
            if (nodes == null)
                return;

            foreach (TreeNode node in nodes)
            {
                BookOpenTag tagObj = (BookOpenTag)node.Tag;
                node.Text = GetNodeText(tagObj.Str);
                ChangeScript(node.Nodes);
            }
        }

		public void SetNodeStates(BitArray nodeStates)
		{
			if (nodeStates == null ||
				nodeStates.Count != treeView1.GetNodeCount(true))
				return;

			this.nodeStates = nodeStates;
			nodeNumber = 0;
			SetNodeStates(treeView1.Nodes);
		}

		private BitArray nodeStates;
		private int nodeNumber = 0;

		private void SetNodeStates(TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				if (nodeStates[nodeNumber])
					node.Expand();

				nodeNumber++;

				if (node.Nodes != null)
					SetNodeStates(node.Nodes);
			}
		}

		public BitArray GetNodeStates()
		{
			nodeStates = new BitArray(treeView1.GetNodeCount(true));
			nodeNumber = 0;
			GetNodeStates(treeView1.Nodes);
			return nodeStates;
		}

		private void GetNodeStates(TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				nodeStates[nodeNumber] = node.IsExpanded;

				nodeNumber++;

				if (node.Nodes != null)
					GetNodeStates(node.Nodes);
			}
		}



        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            BookOpenTag tagObj = (BookOpenTag)e.Node.Tag;

            // selects only double clicked leaf nodes
            if (tagObj.Book != null)
            {
                Cursor.Current = Cursors.WaitCursor;
                ((FormMain)MdiParent).BookDisplay(tagObj.Book);
                Cursor.Current = Cursors.Default;
            }
        }

        private void FormBookOpen_FormClosing(object sender, FormClosingEventArgs e)
        {
			AppState.Inst.SelectFormNodeStates = GetNodeStates();

            Hide();

            if (e.CloseReason == CloseReason.UserClosing)
            {
                AppState.Inst.SelectFormShown = false;
                e.Cancel = true;
            }
        }

        private void FormBookOpen_Resize(object sender, EventArgs e)
        {
            treeView1.Height = this.ClientRectangle.Height;
            treeView1.Width = this.ClientRectangle.Width;
        }

        private void treeView1_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            e.Node.ImageIndex = closedFolderImage;
        }

        private void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            e.Node.ImageIndex = openFolderImage;
        }

		private void treeView1_KeyDown(object sender, KeyEventArgs e)
		{
			// open book if leaf node is selected and Enter is pressed
			if (e.KeyCode == Keys.Enter)
			{
				if (treeView1.SelectedNode == null)
					return;

				BookOpenTag tagObj = (BookOpenTag)treeView1.SelectedNode.Tag;

				// selects only double clicked leaf nodes
				if (tagObj.Book != null)
				{
					Cursor.Current = Cursors.WaitCursor;
					((FormMain)MdiParent).BookDisplay(tagObj.Book);
					Cursor.Current = Cursors.Default;
				}
			}
		}

		private void treeView1_Click(object sender, EventArgs e)
		{
			BringToFront();
		}
    }

    // the TreeNode's Tag property will contain an object with two members:
    // 1) the Devanagari text for that node
    // 2) the Book object if it is a leaf node, otherwise null
    public class BookOpenTag : CstControlTag
    {
        public BookOpenTag(string str, Book b)
        {
            this.Str = str;
            this.Book = b;
            this.SourceScript = Script.Devanagari;
        }        

        public Book Book
        {
            get { return book; }
            set { book = value; }
        }
        private Book book;
    }
}