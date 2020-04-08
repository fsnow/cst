namespace CST
{
	partial class FormMain
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
			ToAppState();
			AppState.Serialize();
			XmlFileDates.Serialize();
			ChapterLists.Serialize();

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fooToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiOpenBook = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiMru = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPrint = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPrintPreview = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiPageSetup = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiSave = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiExit = new System.Windows.Forms.ToolStripMenuItem();
            this.searchToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiSearchWord = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiDictionary = new System.Windows.Forms.ToolStripMenuItem();
            this.advancedToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.windowToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiCascade = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiTileHorizontal = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiTileVertical = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiContents = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiCheckForUpdates = new System.Windows.Forms.ToolStripMenuItem();
            this.tsmiAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.tsbOpenBook = new System.Windows.Forms.ToolStripButton();
            this.tsbSave = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbPrint = new System.Windows.Forms.ToolStripButton();
            this.tsbPrintPreview = new System.Windows.Forms.ToolStripButton();
            this.tsbPageSetup = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbSearchWord = new System.Windows.Forms.ToolStripButton();
            this.tsbDictionary = new System.Windows.Forms.ToolStripButton();
            this.tsbGoto = new System.Windows.Forms.ToolStripButton();
            this.tslInterfaceLanguage = new System.Windows.Forms.ToolStripLabel();
            this.tscbInterfaceLanguage = new System.Windows.Forms.ToolStripComboBox();
            this.tslPaliScript = new System.Windows.Forms.ToolStripLabel();
            this.tscbPaliScript = new System.Windows.Forms.ToolStripComboBox();
            this.menuStrip1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            resources.ApplyResources(this.menuStrip1, "menuStrip1");
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fooToolStripMenuItem,
            this.searchToolStripMenuItem,
            this.windowToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip1.MdiWindowListItem = this.windowToolStripMenuItem;
            this.menuStrip1.Name = "menuStrip1";
            // 
            // fooToolStripMenuItem
            // 
            resources.ApplyResources(this.fooToolStripMenuItem, "fooToolStripMenuItem");
            this.fooToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiOpenBook,
            this.tsmiMru,
            this.tsmiPrint,
            this.tsmiPrintPreview,
            this.tsmiPageSetup,
            this.tsmiSave,
            this.tsmiExit});
            this.fooToolStripMenuItem.Name = "fooToolStripMenuItem";
            // 
            // tsmiOpenBook
            // 
            resources.ApplyResources(this.tsmiOpenBook, "tsmiOpenBook");
            this.tsmiOpenBook.Name = "tsmiOpenBook";
            this.tsmiOpenBook.Click += new System.EventHandler(this.tsmiOpenBook_Click);
            // 
            // tsmiMru
            // 
            resources.ApplyResources(this.tsmiMru, "tsmiMru");
            this.tsmiMru.Name = "tsmiMru";
            // 
            // tsmiPrint
            // 
            resources.ApplyResources(this.tsmiPrint, "tsmiPrint");
            this.tsmiPrint.Name = "tsmiPrint";
            this.tsmiPrint.Click += new System.EventHandler(this.tsmiPrint_Click);
            // 
            // tsmiPrintPreview
            // 
            resources.ApplyResources(this.tsmiPrintPreview, "tsmiPrintPreview");
            this.tsmiPrintPreview.Name = "tsmiPrintPreview";
            this.tsmiPrintPreview.Click += new System.EventHandler(this.tsmiPrintPreview_Click);
            // 
            // tsmiPageSetup
            // 
            resources.ApplyResources(this.tsmiPageSetup, "tsmiPageSetup");
            this.tsmiPageSetup.Name = "tsmiPageSetup";
            this.tsmiPageSetup.Click += new System.EventHandler(this.tsmiPageSetup_Click);
            // 
            // tsmiSave
            // 
            resources.ApplyResources(this.tsmiSave, "tsmiSave");
            this.tsmiSave.Name = "tsmiSave";
            this.tsmiSave.Click += new System.EventHandler(this.tsmiSave_Click);
            // 
            // tsmiExit
            // 
            resources.ApplyResources(this.tsmiExit, "tsmiExit");
            this.tsmiExit.Name = "tsmiExit";
            this.tsmiExit.Click += new System.EventHandler(this.tsmiExit_Click);
            // 
            // searchToolStripMenuItem
            // 
            resources.ApplyResources(this.searchToolStripMenuItem, "searchToolStripMenuItem");
            this.searchToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiSearchWord,
            this.tsmiDictionary,
            this.advancedToolStripMenuItem});
            this.searchToolStripMenuItem.Name = "searchToolStripMenuItem";
            // 
            // tsmiSearchWord
            // 
            resources.ApplyResources(this.tsmiSearchWord, "tsmiSearchWord");
            this.tsmiSearchWord.Name = "tsmiSearchWord";
            this.tsmiSearchWord.Click += new System.EventHandler(this.tsmiSearchWord_Click);
            // 
            // tsmiDictionary
            // 
            resources.ApplyResources(this.tsmiDictionary, "tsmiDictionary");
            this.tsmiDictionary.Name = "tsmiDictionary";
            this.tsmiDictionary.Click += new System.EventHandler(this.tsmiDictionary_Click);
            // 
            // advancedToolStripMenuItem
            // 
            resources.ApplyResources(this.advancedToolStripMenuItem, "advancedToolStripMenuItem");
            this.advancedToolStripMenuItem.Name = "advancedToolStripMenuItem";
            this.advancedToolStripMenuItem.Click += new System.EventHandler(this.advancedToolStripMenuItem_Click);
            // 
            // windowToolStripMenuItem
            // 
            resources.ApplyResources(this.windowToolStripMenuItem, "windowToolStripMenuItem");
            this.windowToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiCascade,
            this.tsmiTileHorizontal,
            this.tsmiTileVertical});
            this.windowToolStripMenuItem.Name = "windowToolStripMenuItem";
            this.windowToolStripMenuItem.DropDownOpening += new System.EventHandler(this.windowToolStripMenuItem_DropDownOpening);
            // 
            // tsmiCascade
            // 
            resources.ApplyResources(this.tsmiCascade, "tsmiCascade");
            this.tsmiCascade.Name = "tsmiCascade";
            this.tsmiCascade.Click += new System.EventHandler(this.tsmiCascade_Click);
            // 
            // tsmiTileHorizontal
            // 
            resources.ApplyResources(this.tsmiTileHorizontal, "tsmiTileHorizontal");
            this.tsmiTileHorizontal.Name = "tsmiTileHorizontal";
            this.tsmiTileHorizontal.Click += new System.EventHandler(this.tsmiTileHorizontal_Click);
            // 
            // tsmiTileVertical
            // 
            resources.ApplyResources(this.tsmiTileVertical, "tsmiTileVertical");
            this.tsmiTileVertical.Name = "tsmiTileVertical";
            this.tsmiTileVertical.Click += new System.EventHandler(this.tsmiTileVertical_Click);
            // 
            // helpToolStripMenuItem
            // 
            resources.ApplyResources(this.helpToolStripMenuItem, "helpToolStripMenuItem");
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiContents,
            this.tsmiCheckForUpdates,
            this.tsmiAbout});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            // 
            // tsmiContents
            // 
            resources.ApplyResources(this.tsmiContents, "tsmiContents");
            this.tsmiContents.Name = "tsmiContents";
            this.tsmiContents.Click += new System.EventHandler(this.tsmiContents_Click);
            // 
            // tsmiCheckForUpdates
            // 
            resources.ApplyResources(this.tsmiCheckForUpdates, "tsmiCheckForUpdates");
            this.tsmiCheckForUpdates.Name = "tsmiCheckForUpdates";
            this.tsmiCheckForUpdates.Click += new System.EventHandler(this.tsmiCheckForUpdates_Click);
            // 
            // tsmiAbout
            // 
            resources.ApplyResources(this.tsmiAbout, "tsmiAbout");
            this.tsmiAbout.Name = "tsmiAbout";
            this.tsmiAbout.Click += new System.EventHandler(this.tsmiAbout_Click);
            // 
            // toolStrip1
            // 
            resources.ApplyResources(this.toolStrip1, "toolStrip1");
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbOpenBook,
            this.tsbSave,
            this.toolStripSeparator1,
            this.tsbPrint,
            this.tsbPrintPreview,
            this.tsbPageSetup,
            this.toolStripSeparator2,
            this.tsbSearchWord,
            this.tsbDictionary,
            this.tsbGoto,
            this.tslInterfaceLanguage,
            this.tscbInterfaceLanguage,
            this.tslPaliScript,
            this.tscbPaliScript});
            this.toolStrip1.Name = "toolStrip1";
            // 
            // tsbOpenBook
            // 
            resources.ApplyResources(this.tsbOpenBook, "tsbOpenBook");
            this.tsbOpenBook.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbOpenBook.Margin = new System.Windows.Forms.Padding(10, 1, 0, 2);
            this.tsbOpenBook.Name = "tsbOpenBook";
            this.tsbOpenBook.Click += new System.EventHandler(this.tsbOpenBook_Click);
            // 
            // tsbSave
            // 
            resources.ApplyResources(this.tsbSave, "tsbSave");
            this.tsbSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbSave.Name = "tsbSave";
            this.tsbSave.Click += new System.EventHandler(this.tsbSave_Click);
            // 
            // toolStripSeparator1
            // 
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // tsbPrint
            // 
            resources.ApplyResources(this.tsbPrint, "tsbPrint");
            this.tsbPrint.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbPrint.Margin = new System.Windows.Forms.Padding(5, 1, 0, 2);
            this.tsbPrint.Name = "tsbPrint";
            this.tsbPrint.Click += new System.EventHandler(this.tsbPrint_Click);
            // 
            // tsbPrintPreview
            // 
            resources.ApplyResources(this.tsbPrintPreview, "tsbPrintPreview");
            this.tsbPrintPreview.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbPrintPreview.Name = "tsbPrintPreview";
            this.tsbPrintPreview.Click += new System.EventHandler(this.tsbPrintPreview_Click);
            // 
            // tsbPageSetup
            // 
            resources.ApplyResources(this.tsbPageSetup, "tsbPageSetup");
            this.tsbPageSetup.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbPageSetup.Name = "tsbPageSetup";
            this.tsbPageSetup.Click += new System.EventHandler(this.tsbPageSetup_Click);
            // 
            // toolStripSeparator2
            // 
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            // 
            // tsbSearchWord
            // 
            resources.ApplyResources(this.tsbSearchWord, "tsbSearchWord");
            this.tsbSearchWord.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbSearchWord.Margin = new System.Windows.Forms.Padding(5, 1, 0, 2);
            this.tsbSearchWord.Name = "tsbSearchWord";
            this.tsbSearchWord.Click += new System.EventHandler(this.tsbSearchWord_Click);
            // 
            // tsbDictionary
            // 
            resources.ApplyResources(this.tsbDictionary, "tsbDictionary");
            this.tsbDictionary.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbDictionary.Name = "tsbDictionary";
            this.tsbDictionary.Click += new System.EventHandler(this.tsbDictionary_Click);
            // 
            // tsbGoto
            // 
            resources.ApplyResources(this.tsbGoto, "tsbGoto");
            this.tsbGoto.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbGoto.Name = "tsbGoto";
            this.tsbGoto.Click += new System.EventHandler(this.tsbGoto_Click);
            // 
            // tslInterfaceLanguage
            // 
            resources.ApplyResources(this.tslInterfaceLanguage, "tslInterfaceLanguage");
            this.tslInterfaceLanguage.Margin = new System.Windows.Forms.Padding(100, 1, 0, 2);
            this.tslInterfaceLanguage.Name = "tslInterfaceLanguage";
            // 
            // tscbInterfaceLanguage
            // 
            resources.ApplyResources(this.tscbInterfaceLanguage, "tscbInterfaceLanguage");
            this.tscbInterfaceLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.tscbInterfaceLanguage.Name = "tscbInterfaceLanguage";
            this.tscbInterfaceLanguage.SelectedIndexChanged += new System.EventHandler(this.tscbInterfaceLanguage_SelectedIndexChanged);
            // 
            // tslPaliScript
            // 
            resources.ApplyResources(this.tslPaliScript, "tslPaliScript");
            this.tslPaliScript.Margin = new System.Windows.Forms.Padding(30, 1, 0, 2);
            this.tslPaliScript.Name = "tslPaliScript";
            // 
            // tscbPaliScript
            // 
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
            // FormMain
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.toolStrip1);
            this.Controls.Add(this.menuStrip1);
            this.IsMdiContainer = true;
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "FormMain";
            this.MdiChildActivate += new System.EventHandler(this.FormMainNew_MdiChildActivate);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormMainNew_KeyDown);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStripMenuItem fooToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem tsmiOpenBook;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbOpenBook;
		private System.Windows.Forms.ToolStripMenuItem tsmiPrint;
		private System.Windows.Forms.ToolStripMenuItem tsmiPrintPreview;
		private System.Windows.Forms.ToolStripMenuItem tsmiPageSetup;
		private System.Windows.Forms.ToolStripMenuItem tsmiSave;
		private System.Windows.Forms.ToolStripMenuItem tsmiExit;
		private System.Windows.Forms.ToolStripMenuItem searchToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem tsmiSearchWord;
		private System.Windows.Forms.ToolStripMenuItem tsmiDictionary;
		private System.Windows.Forms.ToolStripMenuItem windowToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem tsmiCascade;
		private System.Windows.Forms.ToolStripMenuItem tsmiTileHorizontal;
		private System.Windows.Forms.ToolStripMenuItem tsmiTileVertical;
		private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem tsmiContents;
		private System.Windows.Forms.ToolStripMenuItem tsmiCheckForUpdates;
		private System.Windows.Forms.ToolStripMenuItem tsmiAbout;
		private System.Windows.Forms.ToolStripButton tsbSave;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripButton tsbPrint;
		private System.Windows.Forms.ToolStripButton tsbPrintPreview;
		private System.Windows.Forms.ToolStripButton tsbPageSetup;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
		private System.Windows.Forms.ToolStripButton tsbSearchWord;
		private System.Windows.Forms.ToolStripButton tsbGoto;
		private System.Windows.Forms.ToolStripLabel tslInterfaceLanguage;
		private System.Windows.Forms.ToolStripComboBox tscbInterfaceLanguage;
		private System.Windows.Forms.ToolStripLabel tslPaliScript;
		private System.Windows.Forms.ToolStripComboBox tscbPaliScript;
		public System.Windows.Forms.MenuStrip menuStrip1;
		private System.Windows.Forms.ToolStripButton tsbDictionary;
		private System.Windows.Forms.ToolStripMenuItem tsmiMru;
		private System.Windows.Forms.ToolStripMenuItem advancedToolStripMenuItem;
	}
}

