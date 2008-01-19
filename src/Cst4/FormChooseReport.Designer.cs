namespace CST
{
    partial class FormChooseReport
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormChooseReport));
			this.btnOK = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this.radioButtonSelectedWords = new System.Windows.Forms.RadioButton();
			this.panel1 = new System.Windows.Forms.Panel();
			this.radioButtonOccAllWords = new System.Windows.Forms.RadioButton();
			this.radioButtonOccSelWords = new System.Windows.Forms.RadioButton();
			this.radioButtonAllWords = new System.Windows.Forms.RadioButton();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// btnOK
			// 
			this.btnOK.AccessibleDescription = null;
			this.btnOK.AccessibleName = null;
			resources.ApplyResources(this.btnOK, "btnOK");
			this.btnOK.BackgroundImage = null;
			this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.btnOK.Font = null;
			this.btnOK.Name = "btnOK";
			this.btnOK.UseVisualStyleBackColor = true;
			// 
			// btnCancel
			// 
			this.btnCancel.AccessibleDescription = null;
			this.btnCancel.AccessibleName = null;
			resources.ApplyResources(this.btnCancel, "btnCancel");
			this.btnCancel.BackgroundImage = null;
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Font = null;
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.UseVisualStyleBackColor = true;
			// 
			// radioButtonSelectedWords
			// 
			this.radioButtonSelectedWords.AccessibleDescription = null;
			this.radioButtonSelectedWords.AccessibleName = null;
			resources.ApplyResources(this.radioButtonSelectedWords, "radioButtonSelectedWords");
			this.radioButtonSelectedWords.BackgroundImage = null;
			this.radioButtonSelectedWords.Font = null;
			this.radioButtonSelectedWords.Name = "radioButtonSelectedWords";
			this.radioButtonSelectedWords.TabStop = true;
			this.radioButtonSelectedWords.UseVisualStyleBackColor = true;
			// 
			// panel1
			// 
			this.panel1.AccessibleDescription = null;
			this.panel1.AccessibleName = null;
			resources.ApplyResources(this.panel1, "panel1");
			this.panel1.BackgroundImage = null;
			this.panel1.Controls.Add(this.radioButtonOccAllWords);
			this.panel1.Controls.Add(this.radioButtonOccSelWords);
			this.panel1.Controls.Add(this.radioButtonAllWords);
			this.panel1.Controls.Add(this.radioButtonSelectedWords);
			this.panel1.Font = null;
			this.panel1.Name = "panel1";
			// 
			// radioButtonOccAllWords
			// 
			this.radioButtonOccAllWords.AccessibleDescription = null;
			this.radioButtonOccAllWords.AccessibleName = null;
			resources.ApplyResources(this.radioButtonOccAllWords, "radioButtonOccAllWords");
			this.radioButtonOccAllWords.BackgroundImage = null;
			this.radioButtonOccAllWords.Font = null;
			this.radioButtonOccAllWords.Name = "radioButtonOccAllWords";
			this.radioButtonOccAllWords.TabStop = true;
			this.radioButtonOccAllWords.UseVisualStyleBackColor = true;
			// 
			// radioButtonOccSelWords
			// 
			this.radioButtonOccSelWords.AccessibleDescription = null;
			this.radioButtonOccSelWords.AccessibleName = null;
			resources.ApplyResources(this.radioButtonOccSelWords, "radioButtonOccSelWords");
			this.radioButtonOccSelWords.BackgroundImage = null;
			this.radioButtonOccSelWords.Font = null;
			this.radioButtonOccSelWords.Name = "radioButtonOccSelWords";
			this.radioButtonOccSelWords.TabStop = true;
			this.radioButtonOccSelWords.UseVisualStyleBackColor = true;
			// 
			// radioButtonAllWords
			// 
			this.radioButtonAllWords.AccessibleDescription = null;
			this.radioButtonAllWords.AccessibleName = null;
			resources.ApplyResources(this.radioButtonAllWords, "radioButtonAllWords");
			this.radioButtonAllWords.BackgroundImage = null;
			this.radioButtonAllWords.Font = null;
			this.radioButtonAllWords.Name = "radioButtonAllWords";
			this.radioButtonAllWords.TabStop = true;
			this.radioButtonAllWords.UseVisualStyleBackColor = true;
			// 
			// FormChooseReport
			// 
			this.AccessibleDescription = null;
			this.AccessibleName = null;
			resources.ApplyResources(this, "$this");
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackgroundImage = null;
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.btnCancel);
			this.Controls.Add(this.btnOK);
			this.Font = null;
			this.Icon = null;
			this.Name = "FormChooseReport";
			this.ShowIcon = false;
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Panel panel1;
        public System.Windows.Forms.RadioButton radioButtonSelectedWords;
        public System.Windows.Forms.RadioButton radioButtonAllWords;
        public System.Windows.Forms.RadioButton radioButtonOccAllWords;
        public System.Windows.Forms.RadioButton radioButtonOccSelWords;
    }
}