namespace CST
{
    partial class FormSearch
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormSearch));
			this.lblSearchFor = new System.Windows.Forms.Label();
			this.txtSearchTerms = new System.Windows.Forms.TextBox();
			this.btnSearch = new System.Windows.Forms.Button();
			this.listBoxWords = new System.Windows.Forms.ListBox();
			this.lblWords = new System.Windows.Forms.Label();
			this.listBoxOccurBooks = new System.Windows.Forms.ListBox();
			this.lblOccurrencesBooks = new System.Windows.Forms.Label();
			this.btnClose = new System.Windows.Forms.Button();
			this.lblWordStats = new System.Windows.Forms.Label();
			this.lblBookStats = new System.Windows.Forms.Label();
			this.lblUse = new System.Windows.Forms.Label();
			this.comboWildRegex = new System.Windows.Forms.ComboBox();
			this.cbVinaya = new System.Windows.Forms.CheckBox();
			this.cbSutta = new System.Windows.Forms.CheckBox();
			this.cbAbhi = new System.Windows.Forms.CheckBox();
			this.cbMula = new System.Windows.Forms.CheckBox();
			this.cbAttha = new System.Windows.Forms.CheckBox();
			this.cbTika = new System.Windows.Forms.CheckBox();
			this.cbOtherTexts = new System.Windows.Forms.CheckBox();
			this.cbAll = new System.Windows.Forms.CheckBox();
			this.gbLimitSearch = new System.Windows.Forms.GroupBox();
			this.comboBookSet = new System.Windows.Forms.ComboBox();
			this.linkLabelDelete = new System.Windows.Forms.LinkLabel();
			this.linkLabelEdit = new System.Windows.Forms.LinkLabel();
			this.lblSelectBookColl = new System.Windows.Forms.Label();
			this.btnReport = new System.Windows.Forms.Button();
			this.lblContextDistance = new System.Windows.Forms.Label();
			this.numContextDistance = new System.Windows.Forms.NumericUpDown();
			this.gbLimitSearch.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.numContextDistance)).BeginInit();
			this.SuspendLayout();
			// 
			// lblSearchFor
			// 
			this.lblSearchFor.AccessibleDescription = null;
			this.lblSearchFor.AccessibleName = null;
			resources.ApplyResources(this.lblSearchFor, "lblSearchFor");
			this.lblSearchFor.Font = null;
			this.lblSearchFor.Name = "lblSearchFor";
			// 
			// txtSearchTerms
			// 
			this.txtSearchTerms.AccessibleDescription = null;
			this.txtSearchTerms.AccessibleName = null;
			resources.ApplyResources(this.txtSearchTerms, "txtSearchTerms");
			this.txtSearchTerms.BackgroundImage = null;
			this.txtSearchTerms.Name = "txtSearchTerms";
			this.txtSearchTerms.TextChanged += new System.EventHandler(this.txtSearchTerms_TextChanged);
			this.txtSearchTerms.Click += new System.EventHandler(this.txtSearchTerms_Click);
			this.txtSearchTerms.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtSearchTerms_KeyDown);
			this.txtSearchTerms.KeyUp += new System.Windows.Forms.KeyEventHandler(this.txtSearchTerms_KeyUp);
			// 
			// btnSearch
			// 
			this.btnSearch.AccessibleDescription = null;
			this.btnSearch.AccessibleName = null;
			resources.ApplyResources(this.btnSearch, "btnSearch");
			this.btnSearch.BackgroundImage = null;
			this.btnSearch.Font = null;
			this.btnSearch.Name = "btnSearch";
			this.btnSearch.UseVisualStyleBackColor = true;
			this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
			// 
			// listBoxWords
			// 
			this.listBoxWords.AccessibleDescription = null;
			this.listBoxWords.AccessibleName = null;
			resources.ApplyResources(this.listBoxWords, "listBoxWords");
			this.listBoxWords.BackgroundImage = null;
			this.listBoxWords.FormattingEnabled = true;
			this.listBoxWords.Name = "listBoxWords";
			this.listBoxWords.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
			this.listBoxWords.Tag = "Pali";
			this.listBoxWords.MouseClick += new System.Windows.Forms.MouseEventHandler(this.listBoxWords_MouseClick);
			this.listBoxWords.SelectedIndexChanged += new System.EventHandler(this.listBoxWords_SelectedIndexChanged);
			this.listBoxWords.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.listBoxWords_KeyPress);
			this.listBoxWords.KeyDown += new System.Windows.Forms.KeyEventHandler(this.listBoxWords_KeyDown);
			this.listBoxWords.Click += new System.EventHandler(this.listBoxWords_Click);
			// 
			// lblWords
			// 
			this.lblWords.AccessibleDescription = null;
			this.lblWords.AccessibleName = null;
			resources.ApplyResources(this.lblWords, "lblWords");
			this.lblWords.Font = null;
			this.lblWords.Name = "lblWords";
			// 
			// listBoxOccurBooks
			// 
			this.listBoxOccurBooks.AccessibleDescription = null;
			this.listBoxOccurBooks.AccessibleName = null;
			resources.ApplyResources(this.listBoxOccurBooks, "listBoxOccurBooks");
			this.listBoxOccurBooks.BackgroundImage = null;
			this.listBoxOccurBooks.FormattingEnabled = true;
			this.listBoxOccurBooks.Name = "listBoxOccurBooks";
			this.listBoxOccurBooks.Tag = "Pali";
			this.listBoxOccurBooks.DoubleClick += new System.EventHandler(this.listBoxOccurBooks_DoubleClick);
			this.listBoxOccurBooks.Click += new System.EventHandler(this.listBoxOccurBooks_Click);
			// 
			// lblOccurrencesBooks
			// 
			this.lblOccurrencesBooks.AccessibleDescription = null;
			this.lblOccurrencesBooks.AccessibleName = null;
			resources.ApplyResources(this.lblOccurrencesBooks, "lblOccurrencesBooks");
			this.lblOccurrencesBooks.Font = null;
			this.lblOccurrencesBooks.Name = "lblOccurrencesBooks";
			// 
			// btnClose
			// 
			this.btnClose.AccessibleDescription = null;
			this.btnClose.AccessibleName = null;
			resources.ApplyResources(this.btnClose, "btnClose");
			this.btnClose.BackgroundImage = null;
			this.btnClose.Font = null;
			this.btnClose.Name = "btnClose";
			this.btnClose.UseVisualStyleBackColor = true;
			this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
			// 
			// lblWordStats
			// 
			this.lblWordStats.AccessibleDescription = null;
			this.lblWordStats.AccessibleName = null;
			resources.ApplyResources(this.lblWordStats, "lblWordStats");
			this.lblWordStats.Name = "lblWordStats";
			// 
			// lblBookStats
			// 
			this.lblBookStats.AccessibleDescription = null;
			this.lblBookStats.AccessibleName = null;
			resources.ApplyResources(this.lblBookStats, "lblBookStats");
			this.lblBookStats.Name = "lblBookStats";
			// 
			// lblUse
			// 
			this.lblUse.AccessibleDescription = null;
			this.lblUse.AccessibleName = null;
			resources.ApplyResources(this.lblUse, "lblUse");
			this.lblUse.Font = null;
			this.lblUse.Name = "lblUse";
			// 
			// comboWildRegex
			// 
			this.comboWildRegex.AccessibleDescription = null;
			this.comboWildRegex.AccessibleName = null;
			resources.ApplyResources(this.comboWildRegex, "comboWildRegex");
			this.comboWildRegex.BackgroundImage = null;
			this.comboWildRegex.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboWildRegex.Font = null;
			this.comboWildRegex.FormattingEnabled = true;
			this.comboWildRegex.Items.AddRange(new object[] {
            resources.GetString("comboWildRegex.Items"),
            resources.GetString("comboWildRegex.Items1")});
			this.comboWildRegex.Name = "comboWildRegex";
			this.comboWildRegex.SelectedIndexChanged += new System.EventHandler(this.comboWildRegex_SelectedIndexChanged);
			// 
			// cbVinaya
			// 
			this.cbVinaya.AccessibleDescription = null;
			this.cbVinaya.AccessibleName = null;
			resources.ApplyResources(this.cbVinaya, "cbVinaya");
			this.cbVinaya.BackgroundImage = null;
			this.cbVinaya.Checked = true;
			this.cbVinaya.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbVinaya.Font = null;
			this.cbVinaya.Name = "cbVinaya";
			this.cbVinaya.UseVisualStyleBackColor = true;
			this.cbVinaya.CheckedChanged += new System.EventHandler(this.cbVinaya_CheckedChanged);
			// 
			// cbSutta
			// 
			this.cbSutta.AccessibleDescription = null;
			this.cbSutta.AccessibleName = null;
			resources.ApplyResources(this.cbSutta, "cbSutta");
			this.cbSutta.BackgroundImage = null;
			this.cbSutta.Checked = true;
			this.cbSutta.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbSutta.Font = null;
			this.cbSutta.Name = "cbSutta";
			this.cbSutta.UseVisualStyleBackColor = true;
			this.cbSutta.CheckedChanged += new System.EventHandler(this.cbSutta_CheckedChanged);
			// 
			// cbAbhi
			// 
			this.cbAbhi.AccessibleDescription = null;
			this.cbAbhi.AccessibleName = null;
			resources.ApplyResources(this.cbAbhi, "cbAbhi");
			this.cbAbhi.BackgroundImage = null;
			this.cbAbhi.Checked = true;
			this.cbAbhi.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAbhi.Font = null;
			this.cbAbhi.Name = "cbAbhi";
			this.cbAbhi.UseVisualStyleBackColor = true;
			this.cbAbhi.CheckedChanged += new System.EventHandler(this.cbAbhi_CheckedChanged);
			// 
			// cbMula
			// 
			this.cbMula.AccessibleDescription = null;
			this.cbMula.AccessibleName = null;
			resources.ApplyResources(this.cbMula, "cbMula");
			this.cbMula.BackgroundImage = null;
			this.cbMula.Checked = true;
			this.cbMula.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbMula.Font = null;
			this.cbMula.Name = "cbMula";
			this.cbMula.UseVisualStyleBackColor = true;
			this.cbMula.CheckedChanged += new System.EventHandler(this.cbMula_CheckedChanged);
			// 
			// cbAttha
			// 
			this.cbAttha.AccessibleDescription = null;
			this.cbAttha.AccessibleName = null;
			resources.ApplyResources(this.cbAttha, "cbAttha");
			this.cbAttha.BackgroundImage = null;
			this.cbAttha.Checked = true;
			this.cbAttha.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAttha.Font = null;
			this.cbAttha.Name = "cbAttha";
			this.cbAttha.UseVisualStyleBackColor = true;
			this.cbAttha.CheckedChanged += new System.EventHandler(this.cbAttha_CheckedChanged);
			// 
			// cbTika
			// 
			this.cbTika.AccessibleDescription = null;
			this.cbTika.AccessibleName = null;
			resources.ApplyResources(this.cbTika, "cbTika");
			this.cbTika.BackgroundImage = null;
			this.cbTika.Checked = true;
			this.cbTika.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbTika.Font = null;
			this.cbTika.Name = "cbTika";
			this.cbTika.UseVisualStyleBackColor = true;
			this.cbTika.CheckedChanged += new System.EventHandler(this.cbTika_CheckedChanged);
			// 
			// cbOtherTexts
			// 
			this.cbOtherTexts.AccessibleDescription = null;
			this.cbOtherTexts.AccessibleName = null;
			resources.ApplyResources(this.cbOtherTexts, "cbOtherTexts");
			this.cbOtherTexts.BackgroundImage = null;
			this.cbOtherTexts.Checked = true;
			this.cbOtherTexts.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbOtherTexts.Font = null;
			this.cbOtherTexts.Name = "cbOtherTexts";
			this.cbOtherTexts.UseVisualStyleBackColor = true;
			this.cbOtherTexts.CheckedChanged += new System.EventHandler(this.cbOtherTexts_CheckedChanged);
			// 
			// cbAll
			// 
			this.cbAll.AccessibleDescription = null;
			this.cbAll.AccessibleName = null;
			resources.ApplyResources(this.cbAll, "cbAll");
			this.cbAll.BackgroundImage = null;
			this.cbAll.Checked = true;
			this.cbAll.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbAll.Font = null;
			this.cbAll.Name = "cbAll";
			this.cbAll.UseVisualStyleBackColor = true;
			this.cbAll.CheckedChanged += new System.EventHandler(this.cbAll_CheckedChanged);
			// 
			// gbLimitSearch
			// 
			this.gbLimitSearch.AccessibleDescription = null;
			this.gbLimitSearch.AccessibleName = null;
			resources.ApplyResources(this.gbLimitSearch, "gbLimitSearch");
			this.gbLimitSearch.BackgroundImage = null;
			this.gbLimitSearch.Controls.Add(this.comboBookSet);
			this.gbLimitSearch.Controls.Add(this.linkLabelDelete);
			this.gbLimitSearch.Controls.Add(this.linkLabelEdit);
			this.gbLimitSearch.Controls.Add(this.cbAll);
			this.gbLimitSearch.Controls.Add(this.cbOtherTexts);
			this.gbLimitSearch.Controls.Add(this.cbTika);
			this.gbLimitSearch.Controls.Add(this.cbAttha);
			this.gbLimitSearch.Controls.Add(this.cbMula);
			this.gbLimitSearch.Controls.Add(this.cbAbhi);
			this.gbLimitSearch.Controls.Add(this.cbSutta);
			this.gbLimitSearch.Controls.Add(this.cbVinaya);
			this.gbLimitSearch.Controls.Add(this.lblSelectBookColl);
			this.gbLimitSearch.Font = null;
			this.gbLimitSearch.Name = "gbLimitSearch";
			this.gbLimitSearch.TabStop = false;
			// 
			// comboBookSet
			// 
			this.comboBookSet.AccessibleDescription = null;
			this.comboBookSet.AccessibleName = null;
			resources.ApplyResources(this.comboBookSet, "comboBookSet");
			this.comboBookSet.BackgroundImage = null;
			this.comboBookSet.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBookSet.Font = null;
			this.comboBookSet.FormattingEnabled = true;
			this.comboBookSet.Items.AddRange(new object[] {
            resources.GetString("comboBookSet.Items"),
            resources.GetString("comboBookSet.Items1")});
			this.comboBookSet.Name = "comboBookSet";
			this.comboBookSet.SelectedIndexChanged += new System.EventHandler(this.comboBookSet_SelectedIndexChanged);
			// 
			// linkLabelDelete
			// 
			this.linkLabelDelete.AccessibleDescription = null;
			this.linkLabelDelete.AccessibleName = null;
			resources.ApplyResources(this.linkLabelDelete, "linkLabelDelete");
			this.linkLabelDelete.Font = null;
			this.linkLabelDelete.Name = "linkLabelDelete";
			this.linkLabelDelete.TabStop = true;
			this.linkLabelDelete.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelDelete_LinkClicked);
			// 
			// linkLabelEdit
			// 
			this.linkLabelEdit.AccessibleDescription = null;
			this.linkLabelEdit.AccessibleName = null;
			resources.ApplyResources(this.linkLabelEdit, "linkLabelEdit");
			this.linkLabelEdit.Font = null;
			this.linkLabelEdit.Name = "linkLabelEdit";
			this.linkLabelEdit.TabStop = true;
			this.linkLabelEdit.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelEdit_LinkClicked);
			// 
			// lblSelectBookColl
			// 
			this.lblSelectBookColl.AccessibleDescription = null;
			this.lblSelectBookColl.AccessibleName = null;
			resources.ApplyResources(this.lblSelectBookColl, "lblSelectBookColl");
			this.lblSelectBookColl.Font = null;
			this.lblSelectBookColl.Name = "lblSelectBookColl";
			// 
			// btnReport
			// 
			this.btnReport.AccessibleDescription = null;
			this.btnReport.AccessibleName = null;
			resources.ApplyResources(this.btnReport, "btnReport");
			this.btnReport.BackgroundImage = null;
			this.btnReport.Font = null;
			this.btnReport.Name = "btnReport";
			this.btnReport.UseVisualStyleBackColor = true;
			this.btnReport.Click += new System.EventHandler(this.btnReport_Click);
			// 
			// lblContextDistance
			// 
			this.lblContextDistance.AccessibleDescription = null;
			this.lblContextDistance.AccessibleName = null;
			resources.ApplyResources(this.lblContextDistance, "lblContextDistance");
			this.lblContextDistance.Font = null;
			this.lblContextDistance.Name = "lblContextDistance";
			// 
			// numContextDistance
			// 
			this.numContextDistance.AccessibleDescription = null;
			this.numContextDistance.AccessibleName = null;
			resources.ApplyResources(this.numContextDistance, "numContextDistance");
			this.numContextDistance.Font = null;
			this.numContextDistance.Maximum = new decimal(new int[] {
            99,
            0,
            0,
            0});
			this.numContextDistance.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
			this.numContextDistance.Name = "numContextDistance";
			this.numContextDistance.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
			// 
			// FormSearch
			// 
			this.AccessibleDescription = null;
			this.AccessibleName = null;
			resources.ApplyResources(this, "$this");
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackgroundImage = null;
			this.Controls.Add(this.lblUse);
			this.Controls.Add(this.numContextDistance);
			this.Controls.Add(this.btnReport);
			this.Controls.Add(this.gbLimitSearch);
			this.Controls.Add(this.comboWildRegex);
			this.Controls.Add(this.lblBookStats);
			this.Controls.Add(this.lblWordStats);
			this.Controls.Add(this.btnClose);
			this.Controls.Add(this.listBoxOccurBooks);
			this.Controls.Add(this.listBoxWords);
			this.Controls.Add(this.btnSearch);
			this.Controls.Add(this.txtSearchTerms);
			this.Controls.Add(this.lblSearchFor);
			this.Controls.Add(this.lblContextDistance);
			this.Controls.Add(this.lblWords);
			this.Controls.Add(this.lblOccurrencesBooks);
			this.Font = null;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.Icon = null;
			this.MaximizeBox = false;
			this.Name = "FormSearch";
			this.ShowIcon = false;
			this.Click += new System.EventHandler(this.SearchForm_Click);
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SearchForm_FormClosing);
			this.gbLimitSearch.ResumeLayout(false);
			this.gbLimitSearch.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.numContextDistance)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblSearchFor;
        private System.Windows.Forms.Label lblWords;
        private System.Windows.Forms.Label lblOccurrencesBooks;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblWordStats;
        private System.Windows.Forms.Label lblBookStats;
        private System.Windows.Forms.Label lblUse;
        private System.Windows.Forms.GroupBox gbLimitSearch;
        private System.Windows.Forms.Label lblSelectBookColl;
        private System.Windows.Forms.Button btnReport;
        private System.Windows.Forms.LinkLabel linkLabelDelete;
        private System.Windows.Forms.LinkLabel linkLabelEdit;
        public System.Windows.Forms.TextBox txtSearchTerms;
        public System.Windows.Forms.ComboBox comboBookSet;
        public System.Windows.Forms.CheckBox cbVinaya;
        public System.Windows.Forms.CheckBox cbSutta;
        public System.Windows.Forms.CheckBox cbAbhi;
        public System.Windows.Forms.CheckBox cbMula;
        public System.Windows.Forms.CheckBox cbAttha;
        public System.Windows.Forms.CheckBox cbTika;
        public System.Windows.Forms.CheckBox cbOtherTexts;
        public System.Windows.Forms.CheckBox cbAll;
        public System.Windows.Forms.ListBox listBoxWords;
        public System.Windows.Forms.ListBox listBoxOccurBooks;
        public System.Windows.Forms.ComboBox comboWildRegex;
		public System.Windows.Forms.Button btnSearch;
		private System.Windows.Forms.Label lblContextDistance;
		private System.Windows.Forms.NumericUpDown numContextDistance;
    }
}