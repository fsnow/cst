namespace CST
{
	partial class FormBookDisplay
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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormBookDisplay));
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tscbChapterList = new System.Windows.Forms.ToolStripComboBox();
			this.tslPaliScript = new System.Windows.Forms.ToolStripLabel();
			this.tscbPaliScript = new System.Windows.Forms.ToolStripComboBox();
			this.tsbFirstResult = new System.Windows.Forms.ToolStripButton();
			this.tsbPreviousResult = new System.Windows.Forms.ToolStripButton();
			this.tsbNextResult = new System.Windows.Forms.ToolStripButton();
			this.tsbLastResult = new System.Windows.Forms.ToolStripButton();
			this.tsbMula = new System.Windows.Forms.ToolStripButton();
			this.tsbAtthakatha = new System.Windows.Forms.ToolStripButton();
			this.tsbTika = new System.Windows.Forms.ToolStripButton();
			this.tsdbbView = new System.Windows.Forms.ToolStripDropDownButton();
			this.tsmiShowSearchTerms = new System.Windows.Forms.ToolStripMenuItem();
			this.tsmiShowFootnotes = new System.Windows.Forms.ToolStripMenuItem();
			this.statusStrip1 = new System.Windows.Forms.StatusStrip();
			this.tsslPages = new System.Windows.Forms.ToolStripStatusLabel();
			this.tsslDebug = new System.Windows.Forms.ToolStripStatusLabel();
			this.webBrowser = new System.Windows.Forms.WebBrowser();
			this.timer1 = new System.Windows.Forms.Timer(this.components);
			this.toolStrip1.SuspendLayout();
			this.statusStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.AccessibleDescription = null;
			this.toolStrip1.AccessibleName = null;
			resources.ApplyResources(this.toolStrip1, "toolStrip1");
			this.toolStrip1.BackgroundImage = null;
			this.toolStrip1.Font = null;
			this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tscbChapterList,
            this.tslPaliScript,
            this.tscbPaliScript,
            this.tsbFirstResult,
            this.tsbPreviousResult,
            this.tsbNextResult,
            this.tsbLastResult,
            this.tsbMula,
            this.tsbAtthakatha,
            this.tsbTika,
            this.tsdbbView});
			this.toolStrip1.Name = "toolStrip1";
			// 
			// tscbChapterList
			// 
			this.tscbChapterList.AccessibleDescription = null;
			this.tscbChapterList.AccessibleName = null;
			resources.ApplyResources(this.tscbChapterList, "tscbChapterList");
			this.tscbChapterList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.tscbChapterList.Name = "tscbChapterList";
			this.tscbChapterList.SelectedIndexChanged += new System.EventHandler(this.tscbChapterList_SelectedIndexChanged);
			// 
			// tslPaliScript
			// 
			this.tslPaliScript.AccessibleDescription = null;
			this.tslPaliScript.AccessibleName = null;
			resources.ApplyResources(this.tslPaliScript, "tslPaliScript");
			this.tslPaliScript.BackgroundImage = null;
			this.tslPaliScript.Margin = new System.Windows.Forms.Padding(10, 1, 0, 2);
			this.tslPaliScript.Name = "tslPaliScript";
			// 
			// tscbPaliScript
			// 
			this.tscbPaliScript.AccessibleDescription = null;
			this.tscbPaliScript.AccessibleName = null;
			resources.ApplyResources(this.tscbPaliScript, "tscbPaliScript");
			this.tscbPaliScript.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.tscbPaliScript.Items.AddRange(new object[] {
            resources.GetString("tscbPaliScript.Items"),
            resources.GetString("tscbPaliScript.Items1"),
            resources.GetString("tscbPaliScript.Items2"),
            resources.GetString("tscbPaliScript.Items3"),
            resources.GetString("tscbPaliScript.Items4"),
            resources.GetString("tscbPaliScript.Items5"),
            resources.GetString("tscbPaliScript.Items6"),
            resources.GetString("tscbPaliScript.Items7"),
            resources.GetString("tscbPaliScript.Items8"),
            resources.GetString("tscbPaliScript.Items9"),
            resources.GetString("tscbPaliScript.Items10"),
            resources.GetString("tscbPaliScript.Items11"),
            resources.GetString("tscbPaliScript.Items12"),
            resources.GetString("tscbPaliScript.Items13")});
			this.tscbPaliScript.Name = "tscbPaliScript";
			this.tscbPaliScript.SelectedIndexChanged += new System.EventHandler(this.tscbPaliScript_SelectedIndexChanged);
			// 
			// tsbFirstResult
			// 
			this.tsbFirstResult.AccessibleDescription = null;
			this.tsbFirstResult.AccessibleName = null;
			resources.ApplyResources(this.tsbFirstResult, "tsbFirstResult");
			this.tsbFirstResult.BackgroundImage = null;
			this.tsbFirstResult.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbFirstResult.Margin = new System.Windows.Forms.Padding(20, 1, 0, 2);
			this.tsbFirstResult.Name = "tsbFirstResult";
			this.tsbFirstResult.Click += new System.EventHandler(this.tsbFirstResult_Click);
			// 
			// tsbPreviousResult
			// 
			this.tsbPreviousResult.AccessibleDescription = null;
			this.tsbPreviousResult.AccessibleName = null;
			resources.ApplyResources(this.tsbPreviousResult, "tsbPreviousResult");
			this.tsbPreviousResult.BackgroundImage = null;
			this.tsbPreviousResult.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPreviousResult.Name = "tsbPreviousResult";
			this.tsbPreviousResult.Click += new System.EventHandler(this.tsbPreviousResult_Click);
			// 
			// tsbNextResult
			// 
			this.tsbNextResult.AccessibleDescription = null;
			this.tsbNextResult.AccessibleName = null;
			resources.ApplyResources(this.tsbNextResult, "tsbNextResult");
			this.tsbNextResult.BackgroundImage = null;
			this.tsbNextResult.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbNextResult.Name = "tsbNextResult";
			this.tsbNextResult.Click += new System.EventHandler(this.tsbNextResult_Click);
			// 
			// tsbLastResult
			// 
			this.tsbLastResult.AccessibleDescription = null;
			this.tsbLastResult.AccessibleName = null;
			resources.ApplyResources(this.tsbLastResult, "tsbLastResult");
			this.tsbLastResult.BackgroundImage = null;
			this.tsbLastResult.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbLastResult.Name = "tsbLastResult";
			this.tsbLastResult.Click += new System.EventHandler(this.tsbLastResult_Click);
			// 
			// tsbMula
			// 
			this.tsbMula.AccessibleDescription = null;
			this.tsbMula.AccessibleName = null;
			resources.ApplyResources(this.tsbMula, "tsbMula");
			this.tsbMula.BackgroundImage = null;
			this.tsbMula.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.tsbMula.Margin = new System.Windows.Forms.Padding(20, 1, 0, 2);
			this.tsbMula.Name = "tsbMula";
			this.tsbMula.Click += new System.EventHandler(this.tsbMula_Click);
			// 
			// tsbAtthakatha
			// 
			this.tsbAtthakatha.AccessibleDescription = null;
			this.tsbAtthakatha.AccessibleName = null;
			resources.ApplyResources(this.tsbAtthakatha, "tsbAtthakatha");
			this.tsbAtthakatha.BackgroundImage = null;
			this.tsbAtthakatha.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.tsbAtthakatha.Name = "tsbAtthakatha";
			this.tsbAtthakatha.Click += new System.EventHandler(this.tsbAtthakatha_Click);
			// 
			// tsbTika
			// 
			this.tsbTika.AccessibleDescription = null;
			this.tsbTika.AccessibleName = null;
			resources.ApplyResources(this.tsbTika, "tsbTika");
			this.tsbTika.BackgroundImage = null;
			this.tsbTika.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.tsbTika.Name = "tsbTika";
			this.tsbTika.Click += new System.EventHandler(this.tsbTika_Click);
			// 
			// tsdbbView
			// 
			this.tsdbbView.AccessibleDescription = null;
			this.tsdbbView.AccessibleName = null;
			resources.ApplyResources(this.tsdbbView, "tsdbbView");
			this.tsdbbView.BackgroundImage = null;
			this.tsdbbView.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this.tsdbbView.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiShowSearchTerms,
            this.tsmiShowFootnotes});
			this.tsdbbView.Margin = new System.Windows.Forms.Padding(20, 1, 0, 2);
			this.tsdbbView.Name = "tsdbbView";
			// 
			// tsmiShowSearchTerms
			// 
			this.tsmiShowSearchTerms.AccessibleDescription = null;
			this.tsmiShowSearchTerms.AccessibleName = null;
			resources.ApplyResources(this.tsmiShowSearchTerms, "tsmiShowSearchTerms");
			this.tsmiShowSearchTerms.BackgroundImage = null;
			this.tsmiShowSearchTerms.Checked = true;
			this.tsmiShowSearchTerms.CheckState = System.Windows.Forms.CheckState.Checked;
			this.tsmiShowSearchTerms.Name = "tsmiShowSearchTerms";
			this.tsmiShowSearchTerms.ShortcutKeyDisplayString = null;
			this.tsmiShowSearchTerms.Click += new System.EventHandler(this.tsmiShowSearchTerms_Click);
			// 
			// tsmiShowFootnotes
			// 
			this.tsmiShowFootnotes.AccessibleDescription = null;
			this.tsmiShowFootnotes.AccessibleName = null;
			resources.ApplyResources(this.tsmiShowFootnotes, "tsmiShowFootnotes");
			this.tsmiShowFootnotes.BackgroundImage = null;
			this.tsmiShowFootnotes.Checked = true;
			this.tsmiShowFootnotes.CheckState = System.Windows.Forms.CheckState.Checked;
			this.tsmiShowFootnotes.Name = "tsmiShowFootnotes";
			this.tsmiShowFootnotes.ShortcutKeyDisplayString = null;
			this.tsmiShowFootnotes.Click += new System.EventHandler(this.tsmiShowFootnotes_Click);
			// 
			// statusStrip1
			// 
			this.statusStrip1.AccessibleDescription = null;
			this.statusStrip1.AccessibleName = null;
			resources.ApplyResources(this.statusStrip1, "statusStrip1");
			this.statusStrip1.BackgroundImage = null;
			this.statusStrip1.Font = null;
			this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsslPages,
            this.tsslDebug});
			this.statusStrip1.Name = "statusStrip1";
			// 
			// tsslPages
			// 
			this.tsslPages.AccessibleDescription = null;
			this.tsslPages.AccessibleName = null;
			resources.ApplyResources(this.tsslPages, "tsslPages");
			this.tsslPages.BackgroundImage = null;
			this.tsslPages.Name = "tsslPages";
			// 
			// tsslDebug
			// 
			this.tsslDebug.AccessibleDescription = null;
			this.tsslDebug.AccessibleName = null;
			resources.ApplyResources(this.tsslDebug, "tsslDebug");
			this.tsslDebug.BackgroundImage = null;
			this.tsslDebug.Margin = new System.Windows.Forms.Padding(50, 3, 0, 2);
			this.tsslDebug.Name = "tsslDebug";
			// 
			// webBrowser
			// 
			this.webBrowser.AccessibleDescription = null;
			this.webBrowser.AccessibleName = null;
			resources.ApplyResources(this.webBrowser, "webBrowser");
			this.webBrowser.MinimumSize = new System.Drawing.Size(20, 20);
			this.webBrowser.Name = "webBrowser";
			this.webBrowser.DocumentCompleted += new System.Windows.Forms.WebBrowserDocumentCompletedEventHandler(this.webBrowser_DocumentCompleted);
			// 
			// timer1
			// 
			this.timer1.Interval = 200;
			this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
			// 
			// FormBookDisplay
			// 
			this.AccessibleDescription = null;
			this.AccessibleName = null;
			resources.ApplyResources(this, "$this");
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackgroundImage = null;
			this.Controls.Add(this.webBrowser);
			this.Controls.Add(this.statusStrip1);
			this.Controls.Add(this.toolStrip1);
			this.Font = null;
			this.KeyPreview = true;
			this.Name = "FormBookDisplay";
			this.ShowInTaskbar = false;
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FormBookDisplay_FormClosed);
			this.Resize += new System.EventHandler(this.FormBookDisplay_Resize);
			this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormBookDisplay_KeyDown);
			this.ResizeEnd += new System.EventHandler(this.FormBookDisplay_ResizeEnd);
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.statusStrip1.ResumeLayout(false);
			this.statusStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.StatusStrip statusStrip1;
		private System.Windows.Forms.ToolStripComboBox tscbChapterList;
		private System.Windows.Forms.ToolStripLabel tslPaliScript;
		private System.Windows.Forms.ToolStripComboBox tscbPaliScript;
		private System.Windows.Forms.ToolStripButton tsbFirstResult;
		private System.Windows.Forms.ToolStripButton tsbPreviousResult;
		private System.Windows.Forms.ToolStripButton tsbNextResult;
		private System.Windows.Forms.ToolStripButton tsbLastResult;
		private System.Windows.Forms.ToolStripButton tsbMula;
		private System.Windows.Forms.ToolStripButton tsbAtthakatha;
		private System.Windows.Forms.ToolStripButton tsbTika;
		private System.Windows.Forms.ToolStripDropDownButton tsdbbView;
		public System.Windows.Forms.ToolStripMenuItem tsmiShowSearchTerms;
		public System.Windows.Forms.ToolStripMenuItem tsmiShowFootnotes;
		private System.Windows.Forms.ToolStripStatusLabel tsslPages;
		private System.Windows.Forms.ToolStripStatusLabel tsslDebug;
		private System.Windows.Forms.WebBrowser webBrowser;
		private System.Windows.Forms.Timer timer1;
	}
}