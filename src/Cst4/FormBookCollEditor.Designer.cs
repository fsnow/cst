namespace CST
{
    partial class FormBookCollEditor
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormBookCollEditor));
			this.btnSave = new System.Windows.Forms.Button();
			this.listBoxNotInCollection = new System.Windows.Forms.ListBox();
			this.listBoxInCollection = new System.Windows.Forms.ListBox();
			this.btnMove = new System.Windows.Forms.Button();
			this.lblBooksNotInCollection = new System.Windows.Forms.Label();
			this.lblBooksInCollection = new System.Windows.Forms.Label();
			this.btnClose = new System.Windows.Forms.Button();
			this.lblCollectionName = new System.Windows.Forms.Label();
			this.textBoxCollName = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// btnSave
			// 
			this.btnSave.AccessibleDescription = null;
			this.btnSave.AccessibleName = null;
			resources.ApplyResources(this.btnSave, "btnSave");
			this.btnSave.BackgroundImage = null;
			this.btnSave.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.btnSave.Font = null;
			this.btnSave.Name = "btnSave";
			this.btnSave.UseVisualStyleBackColor = true;
			// 
			// listBoxNotInCollection
			// 
			this.listBoxNotInCollection.AccessibleDescription = null;
			this.listBoxNotInCollection.AccessibleName = null;
			resources.ApplyResources(this.listBoxNotInCollection, "listBoxNotInCollection");
			this.listBoxNotInCollection.BackgroundImage = null;
			this.listBoxNotInCollection.Font = null;
			this.listBoxNotInCollection.FormattingEnabled = true;
			this.listBoxNotInCollection.Name = "listBoxNotInCollection";
			this.listBoxNotInCollection.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
			this.listBoxNotInCollection.DoubleClick += new System.EventHandler(this.listBoxNotInCollection_DoubleClick);
			this.listBoxNotInCollection.Click += new System.EventHandler(this.listBoxNotInCollection_Click);
			// 
			// listBoxInCollection
			// 
			this.listBoxInCollection.AccessibleDescription = null;
			this.listBoxInCollection.AccessibleName = null;
			resources.ApplyResources(this.listBoxInCollection, "listBoxInCollection");
			this.listBoxInCollection.BackgroundImage = null;
			this.listBoxInCollection.Font = null;
			this.listBoxInCollection.FormattingEnabled = true;
			this.listBoxInCollection.Name = "listBoxInCollection";
			this.listBoxInCollection.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
			this.listBoxInCollection.DoubleClick += new System.EventHandler(this.listBoxInCollection_DoubleClick);
			this.listBoxInCollection.Click += new System.EventHandler(this.listBoxInCollection_Click);
			// 
			// btnMove
			// 
			this.btnMove.AccessibleDescription = null;
			this.btnMove.AccessibleName = null;
			resources.ApplyResources(this.btnMove, "btnMove");
			this.btnMove.BackgroundImage = null;
			this.btnMove.Name = "btnMove";
			this.btnMove.UseVisualStyleBackColor = true;
			this.btnMove.Click += new System.EventHandler(this.btnMove_Click);
			// 
			// lblBooksNotInCollection
			// 
			this.lblBooksNotInCollection.AccessibleDescription = null;
			this.lblBooksNotInCollection.AccessibleName = null;
			resources.ApplyResources(this.lblBooksNotInCollection, "lblBooksNotInCollection");
			this.lblBooksNotInCollection.Font = null;
			this.lblBooksNotInCollection.Name = "lblBooksNotInCollection";
			// 
			// lblBooksInCollection
			// 
			this.lblBooksInCollection.AccessibleDescription = null;
			this.lblBooksInCollection.AccessibleName = null;
			resources.ApplyResources(this.lblBooksInCollection, "lblBooksInCollection");
			this.lblBooksInCollection.Font = null;
			this.lblBooksInCollection.Name = "lblBooksInCollection";
			// 
			// btnClose
			// 
			this.btnClose.AccessibleDescription = null;
			this.btnClose.AccessibleName = null;
			resources.ApplyResources(this.btnClose, "btnClose");
			this.btnClose.BackgroundImage = null;
			this.btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnClose.Font = null;
			this.btnClose.Name = "btnClose";
			this.btnClose.UseVisualStyleBackColor = true;
			// 
			// lblCollectionName
			// 
			this.lblCollectionName.AccessibleDescription = null;
			this.lblCollectionName.AccessibleName = null;
			resources.ApplyResources(this.lblCollectionName, "lblCollectionName");
			this.lblCollectionName.Font = null;
			this.lblCollectionName.Name = "lblCollectionName";
			// 
			// textBoxCollName
			// 
			this.textBoxCollName.AccessibleDescription = null;
			this.textBoxCollName.AccessibleName = null;
			resources.ApplyResources(this.textBoxCollName, "textBoxCollName");
			this.textBoxCollName.BackgroundImage = null;
			this.textBoxCollName.Font = null;
			this.textBoxCollName.Name = "textBoxCollName";
			this.textBoxCollName.TextChanged += new System.EventHandler(this.textBoxCollName_TextChanged);
			// 
			// FormBookCollEditor
			// 
			this.AccessibleDescription = null;
			this.AccessibleName = null;
			resources.ApplyResources(this, "$this");
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackgroundImage = null;
			this.Controls.Add(this.textBoxCollName);
			this.Controls.Add(this.lblCollectionName);
			this.Controls.Add(this.btnClose);
			this.Controls.Add(this.lblBooksInCollection);
			this.Controls.Add(this.lblBooksNotInCollection);
			this.Controls.Add(this.btnMove);
			this.Controls.Add(this.listBoxInCollection);
			this.Controls.Add(this.listBoxNotInCollection);
			this.Controls.Add(this.btnSave);
			this.Font = null;
			this.Icon = null;
			this.MaximizeBox = false;
			this.Name = "FormBookCollEditor";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.ListBox listBoxNotInCollection;
        private System.Windows.Forms.Button btnMove;
        private System.Windows.Forms.Label lblBooksNotInCollection;
        private System.Windows.Forms.Label lblBooksInCollection;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblCollectionName;
        public System.Windows.Forms.ListBox listBoxInCollection;
        public System.Windows.Forms.TextBox textBoxCollName;
    }
}