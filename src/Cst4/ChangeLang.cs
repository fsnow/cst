using System;
using System.Drawing;
using System.Collections;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Forms;

namespace CST
{
    /// <summary>
    /// Summary description for ChangeLang.
    /// </summary>
    public class ChangeLang : System.Windows.Forms.Form
    {
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.ComboBox comboBox;
        private System.Windows.Forms.Label label;
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.Container components = null;

        public ChangeLang()
        {
            InitializeComponent();
            LanguageCollector lc = new LanguageCollector();
            //LanguageCollector lc = new LanguageCollector(CultureInfo.CurrentUICulture);
            int currentLanguage;
            CultureInfoDisplayItem[] lis = lc.GetLanguages(LanguageCollector.LanguageNameDisplay.NativeName, out currentLanguage);
            comboBox.Items.AddRange(lis);
            comboBox.SelectedIndex = currentLanguage;
        }

        public CultureInfo CultureInfo
        {
            get { return ((CultureInfoDisplayItem)comboBox.SelectedItem).CultureInfo; }
            //set { comboBox.SelectedItem = value; }
        }

        public bool ChangeCurrentThreadLanguage
        {
            get { return true; }
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChangeLang));
            this.comboBox = new System.Windows.Forms.ComboBox();
            this.label = new System.Windows.Forms.Label();
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // comboBox
            // 
            this.comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.comboBox, "comboBox");
            this.comboBox.Name = "comboBox";
            // 
            // label
            // 
            resources.ApplyResources(this.label, "label");
            this.label.Name = "label";
            // 
            // buttonOK
            // 
            this.buttonOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(this.buttonOK, "buttonOK");
            this.buttonOK.Name = "buttonOK";
            // 
            // buttonCancel
            // 
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.buttonCancel, "buttonCancel");
            this.buttonCancel.Name = "buttonCancel";
            // 
            // ChangeLang
            // 
            this.AcceptButton = this.buttonOK;
            resources.ApplyResources(this, "$this");
            this.CancelButton = this.buttonCancel;
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.label);
            this.Controls.Add(this.comboBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ChangeLang";
            this.ResumeLayout(false);

        }
        #endregion
    }
}
