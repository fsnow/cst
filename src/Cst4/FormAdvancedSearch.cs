using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CST
{
	public partial class FormAdvancedSearch : Form
	{
		public FormAdvancedSearch()
		{
			InitializeComponent();
		}

		private void btnSearch_Click(object sender, EventArgs e)
		{
			txtResults.Text = Search.AdvancedSearch(txtQuery.Text);
		}

		private void FormAdvancedSearch_FormClosing(object sender, FormClosingEventArgs e)
		{
			// if the user closes the form, hide it, don't let it get closed
			Hide();

			if (e.CloseReason == CloseReason.UserClosing)
			{
				//Config.Inst.SearchFormShown = false;
				e.Cancel = true;
			}
		}
	}
}