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
			this.menuStrip1.AccessibleDescription = null;
			this.menuStrip1.AccessibleName = null;
			resources.ApplyResources(this.menuStrip1, "menuStrip1");
			this.menuStrip1.BackgroundImage = null;
			this.menuStrip1.Font = null;
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
			this.fooToolStripMenuItem.AccessibleDescription = null;
			this.fooToolStripMenuItem.AccessibleName = null;
			resources.ApplyResources(this.fooToolStripMenuItem, "fooToolStripMenuItem");
			this.fooToolStripMenuItem.BackgroundImage = null;
			this.fooToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiOpenBook,
            this.tsmiMru,
            this.tsmiPrint,
            this.tsmiPrintPreview,
            this.tsmiPageSetup,
            this.tsmiSave,
            this.tsmiExit});
			this.fooToolStripMenuItem.Name = "fooToolStripMenuItem";
			this.fooToolStripMenuItem.ShortcutKeyDisplayString = null;
			// 
			// tsmiOpenBook
			// 
			this.tsmiOpenBook.AccessibleDescription = null;
			this.tsmiOpenBook.AccessibleName = null;
			resources.ApplyResources(this.tsmiOpenBook, "tsmiOpenBook");
			this.tsmiOpenBook.BackgroundImage = null;
			this.tsmiOpenBook.Name = "tsmiOpenBook";
			this.tsmiOpenBook.ShortcutKeyDisplayString = null;
			this.tsmiOpenBook.Click += new System.EventHandler(this.tsmiOpenBook_Click);
			// 
			// tsmiMru
			// 
			this.tsmiMru.AccessibleDescription = null;
			this.tsmiMru.AccessibleName = null;
			resources.ApplyResources(this.tsmiMru, "tsmiMru");
			this.tsmiMru.BackgroundImage = null;
			this.tsmiMru.Name = "tsmiMru";
			this.tsmiMru.ShortcutKeyDisplayString = null;
			// 
			// tsmiPrint
			// 
			this.tsmiPrint.AccessibleDescription = null;
			this.tsmiPrint.AccessibleName = null;
			resources.ApplyResources(this.tsmiPrint, "tsmiPrint");
			this.tsmiPrint.BackgroundImage = null;
			this.tsmiPrint.Name = "tsmiPrint";
			this.tsmiPrint.ShortcutKeyDisplayString = null;
			this.tsmiPrint.Click += new System.EventHandler(this.tsmiPrint_Click);
			// 
			// tsmiPrintPreview
			// 
			this.tsmiPrintPreview.AccessibleDescription = null;
			this.tsmiPrintPreview.AccessibleName = null;
			resources.ApplyResources(this.tsmiPrintPreview, "tsmiPrintPreview");
			this.tsmiPrintPreview.BackgroundImage = null;
			this.tsmiPrintPreview.Name = "tsmiPrintPreview";
			this.tsmiPrintPreview.ShortcutKeyDisplayString = null;
			this.tsmiPrintPreview.Click += new System.EventHandler(this.tsmiPrintPreview_Click);
			// 
			// tsmiPageSetup
			// 
			this.tsmiPageSetup.AccessibleDescription = null;
			this.tsmiPageSetup.AccessibleName = null;
			resources.ApplyResources(this.tsmiPageSetup, "tsmiPageSetup");
			this.tsmiPageSetup.BackgroundImage = null;
			this.tsmiPageSetup.Name = "tsmiPageSetup";
			this.tsmiPageSetup.ShortcutKeyDisplayString = null;
			this.tsmiPageSetup.Click += new System.EventHandler(this.tsmiPageSetup_Click);
			// 
			// tsmiSave
			// 
			this.tsmiSave.AccessibleDescription = null;
			this.tsmiSave.AccessibleName = null;
			resources.ApplyResources(this.tsmiSave, "tsmiSave");
			this.tsmiSave.BackgroundImage = null;
			this.tsmiSave.Name = "tsmiSave";
			this.tsmiSave.ShortcutKeyDisplayString = null;
			this.tsmiSave.Click += new System.EventHandler(this.tsmiSave_Click);
			// 
			// tsmiExit
			// 
			this.tsmiExit.AccessibleDescription = null;
			this.tsmiExit.AccessibleName = null;
			resources.ApplyResources(this.tsmiExit, "tsmiExit");
			this.tsmiExit.BackgroundImage = null;
			this.tsmiExit.Name = "tsmiExit";
			this.tsmiExit.ShortcutKeyDisplayString = null;
			this.tsmiExit.Click += new System.EventHandler(this.tsmiExit_Click);
			// 
			// searchToolStripMenuItem
			// 
			this.searchToolStripMenuItem.AccessibleDescription = null;
			this.searchToolStripMenuItem.AccessibleName = null;
			resources.ApplyResources(this.searchToolStripMenuItem, "searchToolStripMenuItem");
			this.searchToolStripMenuItem.BackgroundImage = null;
			this.searchToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiSearchWord,
            this.tsmiDictionary,
            this.advancedToolStripMenuItem});
			this.searchToolStripMenuItem.Name = "searchToolStripMenuItem";
			this.searchToolStripMenuItem.ShortcutKeyDisplayString = null;
			// 
			// tsmiSearchWord
			// 
			this.tsmiSearchWord.AccessibleDescription = null;
			this.tsmiSearchWord.AccessibleName = null;
			resources.ApplyResources(this.tsmiSearchWord, "tsmiSearchWord");
			this.tsmiSearchWord.BackgroundImage = null;
			this.tsmiSearchWord.Name = "tsmiSearchWord";
			this.tsmiSearchWord.ShortcutKeyDisplayString = null;
			this.tsmiSearchWord.Click += new System.EventHandler(this.tsmiSearchWord_Click);
			// 
			// tsmiDictionary
			// 
			this.tsmiDictionary.AccessibleDescription = null;
			this.tsmiDictionary.AccessibleName = null;
			resources.ApplyResources(this.tsmiDictionary, "tsmiDictionary");
			this.tsmiDictionary.BackgroundImage = null;
			this.tsmiDictionary.Name = "tsmiDictionary";
			this.tsmiDictionary.ShortcutKeyDisplayString = null;
			this.tsmiDictionary.Click += new System.EventHandler(this.tsmiDictionary_Click);
			// 
			// advancedToolStripMenuItem
			// 
			this.advancedToolStripMenuItem.AccessibleDescription = null;
			this.advancedToolStripMenuItem.AccessibleName = null;
			resources.ApplyResources(this.advancedToolStripMenuItem, "advancedToolStripMenuItem");
			this.advancedToolStripMenuItem.BackgroundImage = null;
			this.advancedToolStripMenuItem.Name = "advancedToolStripMenuItem";
			this.advancedToolStripMenuItem.ShortcutKeyDisplayString = null;
			this.advancedToolStripMenuItem.Click += new System.EventHandler(this.advancedToolStripMenuItem_Click);
			// 
			// windowToolStripMenuItem
			// 
			this.windowToolStripMenuItem.AccessibleDescription = null;
			this.windowToolStripMenuItem.AccessibleName = null;
			resources.ApplyResources(this.windowToolStripMenuItem, "windowToolStripMenuItem");
			this.windowToolStripMenuItem.BackgroundImage = null;
			this.windowToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiCascade,
            this.tsmiTileHorizontal,
            this.tsmiTileVertical});
			this.windowToolStripMenuItem.Name = "windowToolStripMenuItem";
			this.windowToolStripMenuItem.ShortcutKeyDisplayString = null;
			this.windowToolStripMenuItem.DropDownOpening += new System.EventHandler(this.windowToolStripMenuItem_DropDownOpening);
			// 
			// tsmiCascade
			// 
			this.tsmiCascade.AccessibleDescription = null;
			this.tsmiCascade.AccessibleName = null;
			resources.ApplyResources(this.tsmiCascade, "tsmiCascade");
			this.tsmiCascade.BackgroundImage = null;
			this.tsmiCascade.Name = "tsmiCascade";
			this.tsmiCascade.ShortcutKeyDisplayString = null;
			this.tsmiCascade.Click += new System.EventHandler(this.tsmiCascade_Click);
			// 
			// tsmiTileHorizontal
			// 
			this.tsmiTileHorizontal.AccessibleDescription = null;
			this.tsmiTileHorizontal.AccessibleName = null;
			resources.ApplyResources(this.tsmiTileHorizontal, "tsmiTileHorizontal");
			this.tsmiTileHorizontal.BackgroundImage = null;
			this.tsmiTileHorizontal.Name = "tsmiTileHorizontal";
			this.tsmiTileHorizontal.ShortcutKeyDisplayString = null;
			this.tsmiTileHorizontal.Click += new System.EventHandler(this.tsmiTileHorizontal_Click);
			// 
			// tsmiTileVertical
			// 
			this.tsmiTileVertical.AccessibleDescription = null;
			this.tsmiTileVertical.AccessibleName = null;
			resources.ApplyResources(this.tsmiTileVertical, "tsmiTileVertical");
			this.tsmiTileVertical.BackgroundImage = null;
			this.tsmiTileVertical.Name = "tsmiTileVertical";
			this.tsmiTileVertical.ShortcutKeyDisplayString = null;
			this.tsmiTileVertical.Click += new System.EventHandler(this.tsmiTileVertical_Click);
			// 
			// helpToolStripMenuItem
			// 
			this.helpToolStripMenuItem.AccessibleDescription = null;
			this.helpToolStripMenuItem.AccessibleName = null;
			resources.ApplyResources(this.helpToolStripMenuItem, "helpToolStripMenuItem");
			this.helpToolStripMenuItem.BackgroundImage = null;
			this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsmiContents,
            this.tsmiCheckForUpdates,
            this.tsmiAbout});
			this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
			this.helpToolStripMenuItem.ShortcutKeyDisplayString = null;
			// 
			// tsmiContents
			// 
			this.tsmiContents.AccessibleDescription = null;
			this.tsmiContents.AccessibleName = null;
			resources.ApplyResources(this.tsmiContents, "tsmiContents");
			this.tsmiContents.BackgroundImage = null;
			this.tsmiContents.Name = "tsmiContents";
			this.tsmiContents.ShortcutKeyDisplayString = null;
			this.tsmiContents.Click += new System.EventHandler(this.tsmiContents_Click);
			// 
			// tsmiCheckForUpdates
			// 
			this.tsmiCheckForUpdates.AccessibleDescription = null;
			this.tsmiCheckForUpdates.AccessibleName = null;
			resources.ApplyResources(this.tsmiCheckForUpdates, "tsmiCheckForUpdates");
			this.tsmiCheckForUpdates.BackgroundImage = null;
			this.tsmiCheckForUpdates.Name = "tsmiCheckForUpdates";
			this.tsmiCheckForUpdates.ShortcutKeyDisplayString = null;
			this.tsmiCheckForUpdates.Click += new System.EventHandler(this.tsmiCheckForUpdates_Click);
			// 
			// tsmiAbout
			// 
			this.tsmiAbout.AccessibleDescription = null;
			this.tsmiAbout.AccessibleName = null;
			resources.ApplyResources(this.tsmiAbout, "tsmiAbout");
			this.tsmiAbout.BackgroundImage = null;
			this.tsmiAbout.Name = "tsmiAbout";
			this.tsmiAbout.ShortcutKeyDisplayString = null;
			this.tsmiAbout.Click += new System.EventHandler(this.tsmiAbout_Click);
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
			this.tsbOpenBook.AccessibleDescription = null;
			this.tsbOpenBook.AccessibleName = null;
			resources.ApplyResources(this.tsbOpenBook, "tsbOpenBook");
			this.tsbOpenBook.BackgroundImage = null;
			this.tsbOpenBook.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbOpenBook.Margin = new System.Windows.Forms.Padding(10, 1, 0, 2);
			this.tsbOpenBook.Name = "tsbOpenBook";
			this.tsbOpenBook.Click += new System.EventHandler(this.tsbOpenBook_Click);
			// 
			// tsbSave
			// 
			this.tsbSave.AccessibleDescription = null;
			this.tsbSave.AccessibleName = null;
			resources.ApplyResources(this.tsbSave, "tsbSave");
			this.tsbSave.BackgroundImage = null;
			this.tsbSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSave.Name = "tsbSave";
			this.tsbSave.Click += new System.EventHandler(this.tsbSave_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.AccessibleDescription = null;
			this.toolStripSeparator1.AccessibleName = null;
			resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			// 
			// tsbPrint
			// 
			this.tsbPrint.AccessibleDescription = null;
			this.tsbPrint.AccessibleName = null;
			resources.ApplyResources(this.tsbPrint, "tsbPrint");
			this.tsbPrint.BackgroundImage = null;
			this.tsbPrint.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPrint.Margin = new System.Windows.Forms.Padding(5, 1, 0, 2);
			this.tsbPrint.Name = "tsbPrint";
			this.tsbPrint.Click += new System.EventHandler(this.tsbPrint_Click);
			// 
			// tsbPrintPreview
			// 
			this.tsbPrintPreview.AccessibleDescription = null;
			this.tsbPrintPreview.AccessibleName = null;
			resources.ApplyResources(this.tsbPrintPreview, "tsbPrintPreview");
			this.tsbPrintPreview.BackgroundImage = null;
			this.tsbPrintPreview.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPrintPreview.Name = "tsbPrintPreview";
			this.tsbPrintPreview.Click += new System.EventHandler(this.tsbPrintPreview_Click);
			// 
			// tsbPageSetup
			// 
			this.tsbPageSetup.AccessibleDescription = null;
			this.tsbPageSetup.AccessibleName = null;
			resources.ApplyResources(this.tsbPageSetup, "tsbPageSetup");
			this.tsbPageSetup.BackgroundImage = null;
			this.tsbPageSetup.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPageSetup.Name = "tsbPageSetup";
			this.tsbPageSetup.Click += new System.EventHandler(this.tsbPageSetup_Click);
			// 
			// toolStripSeparator2
			// 
			this.toolStripSeparator2.AccessibleDescription = null;
			this.toolStripSeparator2.AccessibleName = null;
			resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			// 
			// tsbSearchWord
			// 
			this.tsbSearchWord.AccessibleDescription = null;
			this.tsbSearchWord.AccessibleName = null;
			resources.ApplyResources(this.tsbSearchWord, "tsbSearchWord");
			this.tsbSearchWord.BackgroundImage = null;
			this.tsbSearchWord.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSearchWord.Margin = new System.Windows.Forms.Padding(5, 1, 0, 2);
			this.tsbSearchWord.Name = "tsbSearchWord";
			this.tsbSearchWord.Click += new System.EventHandler(this.tsbSearchWord_Click);
			// 
			// tsbDictionary
			// 
			this.tsbDictionary.AccessibleDescription = null;
			this.tsbDictionary.AccessibleName = null;
			resources.ApplyResources(this.tsbDictionary, "tsbDictionary");
			this.tsbDictionary.BackgroundImage = null;
			this.tsbDictionary.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDictionary.Name = "tsbDictionary";
			this.tsbDictionary.Click += new System.EventHandler(this.tsbDictionary_Click);
			// 
			// tsbGoto
			// 
			this.tsbGoto.AccessibleDescription = null;
			this.tsbGoto.AccessibleName = null;
			resources.ApplyResources(this.tsbGoto, "tsbGoto");
			this.tsbGoto.BackgroundImage = null;
			this.tsbGoto.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbGoto.Name = "tsbGoto";
			this.tsbGoto.Click += new System.EventHandler(this.tsbGoto_Click);
			// 
			// tslInterfaceLanguage
			// 
			this.tslInterfaceLanguage.AccessibleDescription = null;
			this.tslInterfaceLanguage.AccessibleName = null;
			resources.ApplyResources(this.tslInterfaceLanguage, "tslInterfaceLanguage");
			this.tslInterfaceLanguage.BackgroundImage = null;
			this.tslInterfaceLanguage.Margin = new System.Windows.Forms.Padding(100, 1, 0, 2);
			this.tslInterfaceLanguage.Name = "tslInterfaceLanguage";
			// 
			// tscbInterfaceLanguage
			// 
			this.tscbInterfaceLanguage.AccessibleDescription = null;
			this.tscbInterfaceLanguage.AccessibleName = null;
			resources.ApplyResources(this.tscbInterfaceLanguage, "tscbInterfaceLanguage");
			this.tscbInterfaceLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.tscbInterfaceLanguage.Name = "tscbInterfaceLanguage";
			this.tscbInterfaceLanguage.SelectedIndexChanged += new System.EventHandler(this.tscbInterfaceLanguage_SelectedIndexChanged);
			// 
			// tslPaliScript
			// 
			this.tslPaliScript.AccessibleDescription = null;
			this.tslPaliScript.AccessibleName = null;
			resources.ApplyResources(this.tslPaliScript, "tslPaliScript");
			this.tslPaliScript.BackgroundImage = null;
			this.tslPaliScript.Margin = new System.Windows.Forms.Padding(30, 1, 0, 2);
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
			// FormMain
			// 
			this.AccessibleDescription = null;
			this.AccessibleName = null;
			resources.ApplyResources(this, "$this");
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackgroundImage = null;
			this.Controls.Add(this.toolStrip1);
			this.Controls.Add(this.menuStrip1);
			this.Font = null;
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

