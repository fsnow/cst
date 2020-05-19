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
            resources.ApplyResources(this.lblSearchFor, "lblSearchFor");
            this.lblSearchFor.Name = "lblSearchFor";
            // 
            // txtSearchTerms
            // 
            resources.ApplyResources(this.txtSearchTerms, "txtSearchTerms");
            this.txtSearchTerms.Name = "txtSearchTerms";
            this.txtSearchTerms.Click += new System.EventHandler(this.txtSearchTerms_Click);
            this.txtSearchTerms.TextChanged += new System.EventHandler(this.txtSearchTerms_TextChanged);
            this.txtSearchTerms.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtSearchTerms_KeyDown);
            this.txtSearchTerms.KeyUp += new System.Windows.Forms.KeyEventHandler(this.txtSearchTerms_KeyUp);
            // 
            // btnSearch
            // 
            resources.ApplyResources(this.btnSearch, "btnSearch");
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.UseVisualStyleBackColor = true;
            this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
            // 
            // listBoxWords
            // 
            resources.ApplyResources(this.listBoxWords, "listBoxWords");
            this.listBoxWords.FormattingEnabled = true;
            this.listBoxWords.Name = "listBoxWords";
            this.listBoxWords.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.listBoxWords.Tag = "Pali";
            this.listBoxWords.Click += new System.EventHandler(this.listBoxWords_Click);
            this.listBoxWords.MouseClick += new System.Windows.Forms.MouseEventHandler(this.listBoxWords_MouseClick);
            this.listBoxWords.SelectedIndexChanged += new System.EventHandler(this.listBoxWords_SelectedIndexChanged);
            this.listBoxWords.KeyDown += new System.Windows.Forms.KeyEventHandler(this.listBoxWords_KeyDown);
            this.listBoxWords.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.listBoxWords_KeyPress);
            // 
            // lblWords
            // 
            resources.ApplyResources(this.lblWords, "lblWords");
            this.lblWords.Name = "lblWords";
            // 
            // listBoxOccurBooks
            // 
            resources.ApplyResources(this.listBoxOccurBooks, "listBoxOccurBooks");
            this.listBoxOccurBooks.FormattingEnabled = true;
            this.listBoxOccurBooks.Name = "listBoxOccurBooks";
            this.listBoxOccurBooks.Tag = "Pali";
            this.listBoxOccurBooks.Click += new System.EventHandler(this.listBoxOccurBooks_Click);
            this.listBoxOccurBooks.DoubleClick += new System.EventHandler(this.listBoxOccurBooks_DoubleClick);
            // 
            // lblOccurrencesBooks
            // 
            resources.ApplyResources(this.lblOccurrencesBooks, "lblOccurrencesBooks");
            this.lblOccurrencesBooks.Name = "lblOccurrencesBooks";
            // 
            // btnClose
            // 
            resources.ApplyResources(this.btnClose, "btnClose");
            this.btnClose.Name = "btnClose";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // lblWordStats
            // 
            resources.ApplyResources(this.lblWordStats, "lblWordStats");
            this.lblWordStats.Name = "lblWordStats";
            // 
            // lblBookStats
            // 
            resources.ApplyResources(this.lblBookStats, "lblBookStats");
            this.lblBookStats.Name = "lblBookStats";
            // 
            // lblUse
            // 
            resources.ApplyResources(this.lblUse, "lblUse");
            this.lblUse.Name = "lblUse";
            // 
            // comboWildRegex
            // 
            resources.ApplyResources(this.comboWildRegex, "comboWildRegex");
            this.comboWildRegex.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboWildRegex.FormattingEnabled = true;
            this.comboWildRegex.Items.AddRange(new object[] {
            resources.GetString("comboWildRegex.Items"),
            resources.GetString("comboWildRegex.Items1")});
            this.comboWildRegex.Name = "comboWildRegex";
            this.comboWildRegex.SelectedIndexChanged += new System.EventHandler(this.comboWildRegex_SelectedIndexChanged);
            // 
            // cbVinaya
            // 
            resources.ApplyResources(this.cbVinaya, "cbVinaya");
            this.cbVinaya.Checked = true;
            this.cbVinaya.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbVinaya.Name = "cbVinaya";
            this.cbVinaya.UseVisualStyleBackColor = true;
            this.cbVinaya.CheckedChanged += new System.EventHandler(this.cbVinaya_CheckedChanged);
            // 
            // cbSutta
            // 
            resources.ApplyResources(this.cbSutta, "cbSutta");
            this.cbSutta.Checked = true;
            this.cbSutta.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbSutta.Name = "cbSutta";
            this.cbSutta.UseVisualStyleBackColor = true;
            this.cbSutta.CheckedChanged += new System.EventHandler(this.cbSutta_CheckedChanged);
            // 
            // cbAbhi
            // 
            resources.ApplyResources(this.cbAbhi, "cbAbhi");
            this.cbAbhi.Checked = true;
            this.cbAbhi.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbAbhi.Name = "cbAbhi";
            this.cbAbhi.UseVisualStyleBackColor = true;
            this.cbAbhi.CheckedChanged += new System.EventHandler(this.cbAbhi_CheckedChanged);
            // 
            // cbMula
            // 
            resources.ApplyResources(this.cbMula, "cbMula");
            this.cbMula.Checked = true;
            this.cbMula.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbMula.Name = "cbMula";
            this.cbMula.UseVisualStyleBackColor = true;
            this.cbMula.CheckedChanged += new System.EventHandler(this.cbMula_CheckedChanged);
            // 
            // cbAttha
            // 
            resources.ApplyResources(this.cbAttha, "cbAttha");
            this.cbAttha.Checked = true;
            this.cbAttha.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbAttha.Name = "cbAttha";
            this.cbAttha.UseVisualStyleBackColor = true;
            this.cbAttha.CheckedChanged += new System.EventHandler(this.cbAttha_CheckedChanged);
            // 
            // cbTika
            // 
            resources.ApplyResources(this.cbTika, "cbTika");
            this.cbTika.Checked = true;
            this.cbTika.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbTika.Name = "cbTika";
            this.cbTika.UseVisualStyleBackColor = true;
            this.cbTika.CheckedChanged += new System.EventHandler(this.cbTika_CheckedChanged);
            // 
            // cbOtherTexts
            // 
            resources.ApplyResources(this.cbOtherTexts, "cbOtherTexts");
            this.cbOtherTexts.Checked = true;
            this.cbOtherTexts.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbOtherTexts.Name = "cbOtherTexts";
            this.cbOtherTexts.UseVisualStyleBackColor = true;
            this.cbOtherTexts.CheckedChanged += new System.EventHandler(this.cbOtherTexts_CheckedChanged);
            // 
            // cbAll
            // 
            resources.ApplyResources(this.cbAll, "cbAll");
            this.cbAll.Checked = true;
            this.cbAll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbAll.Name = "cbAll";
            this.cbAll.UseVisualStyleBackColor = true;
            this.cbAll.CheckedChanged += new System.EventHandler(this.cbAll_CheckedChanged);
            // 
            // gbLimitSearch
            // 
            resources.ApplyResources(this.gbLimitSearch, "gbLimitSearch");
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
            this.gbLimitSearch.Name = "gbLimitSearch";
            this.gbLimitSearch.TabStop = false;
            this.gbLimitSearch.Enter += new System.EventHandler(this.gbLimitSearch_Enter);
            // 
            // comboBookSet
            // 
            resources.ApplyResources(this.comboBookSet, "comboBookSet");
            this.comboBookSet.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBookSet.FormattingEnabled = true;
            this.comboBookSet.Items.AddRange(new object[] {
            resources.GetString("comboBookSet.Items"),
            resources.GetString("comboBookSet.Items1")});
            this.comboBookSet.Name = "comboBookSet";
            this.comboBookSet.SelectedIndexChanged += new System.EventHandler(this.comboBookSet_SelectedIndexChanged);
            // 
            // linkLabelDelete
            // 
            resources.ApplyResources(this.linkLabelDelete, "linkLabelDelete");
            this.linkLabelDelete.Name = "linkLabelDelete";
            this.linkLabelDelete.TabStop = true;
            this.linkLabelDelete.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelDelete_LinkClicked);
            // 
            // linkLabelEdit
            // 
            resources.ApplyResources(this.linkLabelEdit, "linkLabelEdit");
            this.linkLabelEdit.Name = "linkLabelEdit";
            this.linkLabelEdit.TabStop = true;
            this.linkLabelEdit.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelEdit_LinkClicked);
            // 
            // lblSelectBookColl
            // 
            resources.ApplyResources(this.lblSelectBookColl, "lblSelectBookColl");
            this.lblSelectBookColl.Name = "lblSelectBookColl";
            // 
            // btnReport
            // 
            resources.ApplyResources(this.btnReport, "btnReport");
            this.btnReport.Name = "btnReport";
            this.btnReport.UseVisualStyleBackColor = true;
            this.btnReport.Click += new System.EventHandler(this.btnReport_Click);
            // 
            // lblContextDistance
            // 
            resources.ApplyResources(this.lblContextDistance, "lblContextDistance");
            this.lblContextDistance.Name = "lblContextDistance";
            // 
            // numContextDistance
            // 
            resources.ApplyResources(this.numContextDistance, "numContextDistance");
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
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
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
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "FormSearch";
            this.ShowIcon = false;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SearchForm_FormClosing);
            this.Click += new System.EventHandler(this.SearchForm_Click);
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