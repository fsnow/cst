namespace CST
{
    partial class FormDictionary
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormDictionary));
            this.lblWord = new System.Windows.Forms.Label();
            this.txtWord = new System.Windows.Forms.TextBox();
            this.btnClose = new System.Windows.Forms.Button();
            this.lbWords = new System.Windows.Forms.ListBox();
            this.lblWords = new System.Windows.Forms.Label();
            this.lblMeaning = new System.Windows.Forms.Label();
            this.wbMeaning = new System.Windows.Forms.WebBrowser();
            this.txtForBorder = new System.Windows.Forms.TextBox();
            this.lblDefinitionLanguage = new System.Windows.Forms.Label();
            this.cbDefinitionLanguage = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // lblWord
            // 
            resources.ApplyResources(this.lblWord, "lblWord");
            this.lblWord.Name = "lblWord";
            // 
            // txtWord
            // 
            resources.ApplyResources(this.txtWord, "txtWord");
            this.txtWord.Name = "txtWord";
            this.txtWord.TextChanged += new System.EventHandler(this.txtWord_TextChanged);
            this.txtWord.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.txtWord_KeyPress);
            // 
            // btnClose
            // 
            resources.ApplyResources(this.btnClose, "btnClose");
            this.btnClose.Name = "btnClose";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // lbWords
            // 
            resources.ApplyResources(this.lbWords, "lbWords");
            this.lbWords.FormattingEnabled = true;
            this.lbWords.Name = "lbWords";
            this.lbWords.SelectedIndexChanged += new System.EventHandler(this.lbWords_SelectedIndexChanged);
            // 
            // lblWords
            // 
            resources.ApplyResources(this.lblWords, "lblWords");
            this.lblWords.Name = "lblWords";
            // 
            // lblMeaning
            // 
            resources.ApplyResources(this.lblMeaning, "lblMeaning");
            this.lblMeaning.Name = "lblMeaning";
            // 
            // wbMeaning
            // 
            resources.ApplyResources(this.wbMeaning, "wbMeaning");
            this.wbMeaning.Name = "wbMeaning";
            // 
            // txtForBorder
            // 
            resources.ApplyResources(this.txtForBorder, "txtForBorder");
            this.txtForBorder.Name = "txtForBorder";
            // 
            // lblDefinitionLanguage
            // 
            resources.ApplyResources(this.lblDefinitionLanguage, "lblDefinitionLanguage");
            this.lblDefinitionLanguage.Name = "lblDefinitionLanguage";
            // 
            // cbDefinitionLanguage
            // 
            resources.ApplyResources(this.cbDefinitionLanguage, "cbDefinitionLanguage");
            this.cbDefinitionLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbDefinitionLanguage.FormattingEnabled = true;
            this.cbDefinitionLanguage.Items.AddRange(new object[] {
            resources.GetString("cbDefinitionLanguage.Items"),
            resources.GetString("cbDefinitionLanguage.Items1")});
            this.cbDefinitionLanguage.Name = "cbDefinitionLanguage";
            this.cbDefinitionLanguage.SelectedIndexChanged += new System.EventHandler(this.cbDefinitionLanguage_SelectedIndexChanged);
            // 
            // FormDictionary
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.cbDefinitionLanguage);
            this.Controls.Add(this.lblDefinitionLanguage);
            this.Controls.Add(this.txtForBorder);
            this.Controls.Add(this.wbMeaning);
            this.Controls.Add(this.lblMeaning);
            this.Controls.Add(this.lblWords);
            this.Controls.Add(this.lbWords);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.txtWord);
            this.Controls.Add(this.lblWord);
            this.Name = "FormDictionary";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormDictionary_FormClosing);
            this.Resize += new System.EventHandler(this.FormDictionary_Resize);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblWord;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblWords;
        private System.Windows.Forms.Label lblMeaning;
        private System.Windows.Forms.WebBrowser wbMeaning;
        public System.Windows.Forms.TextBox txtWord;
        public System.Windows.Forms.ListBox lbWords;
        public System.Windows.Forms.TextBox txtForBorder;
		private System.Windows.Forms.Label lblDefinitionLanguage;
		public System.Windows.Forms.ComboBox cbDefinitionLanguage;
    }
}