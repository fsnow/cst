using System;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using CST.Conversion;

namespace CST
{
    public partial class FormBookCollEditor : Form
    {
        public FormBookCollEditor(BookCollection bookColl)
        {
            InitializeComponent();

            notInColl = new ArrayList();
            inColl = new ArrayList();
            clbbComparer = new CLBBComparer();

            int index = 0;
            foreach (Book book in Books.Inst)
            {
                CollectionListBoxBook clbb = new CollectionListBoxBook();
                clbb.Book = book;

                if (bookColl != null && bookColl.BookBits[index])
                    inColl.Add(clbb);
                else
                    notInColl.Add(clbb);

                index++;
            }

            listBoxInCollection.DataSource = inColl;
            listBoxInCollection.SelectedIndex = -1;
            listBoxNotInCollection.DataSource = notInColl;

            if (bookColl != null)
            {
                editing = true;
                textBoxCollName.Text = bookColl.Name;
            }
        }

        private bool moveTo = true;
        private ArrayList notInColl;
        private ArrayList inColl;
        private CLBBComparer clbbComparer;
        private bool collChanged = false;
        private bool editing = false;
        private bool nameChanged = false;


        private void listBoxNotInCollection_Click(object sender, EventArgs e)
        {
            if (listBoxNotInCollection.Items.Count > 0)
            {
                btnMove.Text = ">>>";
                moveTo = true;
                listBoxInCollection.SelectedIndex = -1;
            }
        }

        private void listBoxInCollection_Click(object sender, EventArgs e)
        {
            if (listBoxInCollection.Items.Count > 0)
            {
                btnMove.Text = "<<<";
                moveTo = false;
                listBoxNotInCollection.SelectedIndex = -1;
            }
        }

        private void btnMove_Click(object sender, EventArgs e)
        {
            if (moveTo && listBoxNotInCollection.SelectedIndices.Count > 0)
            {
                MoveRight();
            }
            else if (moveTo == false && listBoxInCollection.SelectedIndices.Count > 0)
            {
                MoveLeft();
            }

            SetSaveButtonEnabled();
        }

        private void MoveRight()
        {
            int[] selectedItems = new int[listBoxNotInCollection.SelectedIndices.Count];
            listBoxNotInCollection.SelectedIndices.CopyTo(selectedItems, 0);

            for (int i = selectedItems.Length - 1; i >= 0; i--)
            {
                int index = selectedItems[i];
                CollectionListBoxBook clbb = (CollectionListBoxBook)notInColl[index];
                notInColl.RemoveAt(index);
                inColl.Add(clbb);
            }

            inColl.Sort(clbbComparer);

            listBoxInCollection.DataSource = inColl.Clone();
            listBoxInCollection.SelectedIndex = -1;
            listBoxNotInCollection.DataSource = notInColl.Clone();

            collChanged = true;
        }

        private void MoveLeft()
        {
            int[] selectedItems = new int[listBoxInCollection.SelectedIndices.Count];
            listBoxInCollection.SelectedIndices.CopyTo(selectedItems, 0);

            for (int i = selectedItems.Length - 1; i >= 0; i--)
            {
                int index = selectedItems[i];
                CollectionListBoxBook clbb = (CollectionListBoxBook)inColl[index];
                inColl.RemoveAt(index);
                notInColl.Add(clbb);
            }

            notInColl.Sort(clbbComparer);

            listBoxInCollection.DataSource = inColl.Clone();
            listBoxNotInCollection.DataSource = notInColl.Clone();
            listBoxNotInCollection.SelectedIndex = -1;

            collChanged = true;
        }

        private void textBoxCollName_TextChanged(object sender, EventArgs e)
        {
            SetSaveButtonEnabled();
            nameChanged = true;
        }

        private void SetSaveButtonEnabled()
        {
            btnSave.Enabled = ((collChanged || (editing && nameChanged)) &&
                                inColl.Count > 0 &&
                                textBoxCollName.Text.Trim().Length > 0);
        }

        private void listBoxNotInCollection_DoubleClick(object sender, EventArgs e)
        {
            MoveRight();
            SetSaveButtonEnabled();
        }

        private void listBoxInCollection_DoubleClick(object sender, EventArgs e)
        {
            MoveLeft();
            SetSaveButtonEnabled();
        }
    }
}