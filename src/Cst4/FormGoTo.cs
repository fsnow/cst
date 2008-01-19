using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CST
{
    public partial class FormGoTo : Form
    {
		public FormGoTo(FormBookDisplay formBookDisplay)
        {
            InitializeComponent();

			this.formBookDisplay = formBookDisplay;
        }

		private FormBookDisplay formBookDisplay;

        private void FormGoTo_Load(object sender, EventArgs e)
        {
            radioButtonParagraph.Checked = true;

			if (formBookDisplay.vPage == "*")
				radioButtonVriPage.Enabled = false;

			if (formBookDisplay.mPage == "*")
				radioButtonMyanmarPage.Enabled = false;

			if (formBookDisplay.tPage == "*")
				radioButtonThaiPage.Enabled = false;

			if (formBookDisplay.pPage == "*")
				radioButtonPtsPage.Enabled = false;

			if (formBookDisplay.oPage == "*")
				radioButtonOtherPage.Enabled = false;
        }

		private void radioButtonParagraph_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}

		private void radioButtonVriPage_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}

		private void radioButtonMyanmarPage_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}

		private void radioButtonPtsPage_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}

		private void radioButtonThaiPage_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}

		private void radioButtonOtherPage_Click(object sender, EventArgs e)
		{
			textBoxNumber.Focus();
		}
    }
}