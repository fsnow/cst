namespace CST
{
    partial class FormGoTo
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormGoTo));
            this.lblGoToWhat = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.radioButtonOtherPage = new System.Windows.Forms.RadioButton();
            this.radioButtonThaiPage = new System.Windows.Forms.RadioButton();
            this.radioButtonPtsPage = new System.Windows.Forms.RadioButton();
            this.radioButtonMyanmarPage = new System.Windows.Forms.RadioButton();
            this.radioButtonVriPage = new System.Windows.Forms.RadioButton();
            this.radioButtonParagraph = new System.Windows.Forms.RadioButton();
            this.lblNumber = new System.Windows.Forms.Label();
            this.textBoxNumber = new System.Windows.Forms.TextBox();
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblGoToWhat
            // 
            resources.ApplyResources(this.lblGoToWhat, "lblGoToWhat");
            this.lblGoToWhat.Name = "lblGoToWhat";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.radioButtonOtherPage);
            this.panel1.Controls.Add(this.radioButtonThaiPage);
            this.panel1.Controls.Add(this.radioButtonPtsPage);
            this.panel1.Controls.Add(this.radioButtonMyanmarPage);
            this.panel1.Controls.Add(this.radioButtonVriPage);
            this.panel1.Controls.Add(this.radioButtonParagraph);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // radioButtonOtherPage
            // 
            resources.ApplyResources(this.radioButtonOtherPage, "radioButtonOtherPage");
            this.radioButtonOtherPage.Name = "radioButtonOtherPage";
            this.radioButtonOtherPage.TabStop = true;
            this.radioButtonOtherPage.UseVisualStyleBackColor = true;
            this.radioButtonOtherPage.Click += new System.EventHandler(this.radioButtonOtherPage_Click);
            // 
            // radioButtonThaiPage
            // 
            resources.ApplyResources(this.radioButtonThaiPage, "radioButtonThaiPage");
            this.radioButtonThaiPage.Name = "radioButtonThaiPage";
            this.radioButtonThaiPage.TabStop = true;
            this.radioButtonThaiPage.UseVisualStyleBackColor = true;
            this.radioButtonThaiPage.Click += new System.EventHandler(this.radioButtonThaiPage_Click);
            // 
            // radioButtonPtsPage
            // 
            resources.ApplyResources(this.radioButtonPtsPage, "radioButtonPtsPage");
            this.radioButtonPtsPage.Name = "radioButtonPtsPage";
            this.radioButtonPtsPage.TabStop = true;
            this.radioButtonPtsPage.UseVisualStyleBackColor = true;
            this.radioButtonPtsPage.Click += new System.EventHandler(this.radioButtonPtsPage_Click);
            // 
            // radioButtonMyanmarPage
            // 
            resources.ApplyResources(this.radioButtonMyanmarPage, "radioButtonMyanmarPage");
            this.radioButtonMyanmarPage.Name = "radioButtonMyanmarPage";
            this.radioButtonMyanmarPage.TabStop = true;
            this.radioButtonMyanmarPage.UseVisualStyleBackColor = true;
            this.radioButtonMyanmarPage.Click += new System.EventHandler(this.radioButtonMyanmarPage_Click);
            // 
            // radioButtonVriPage
            // 
            resources.ApplyResources(this.radioButtonVriPage, "radioButtonVriPage");
            this.radioButtonVriPage.Name = "radioButtonVriPage";
            this.radioButtonVriPage.TabStop = true;
            this.radioButtonVriPage.UseVisualStyleBackColor = true;
            this.radioButtonVriPage.Click += new System.EventHandler(this.radioButtonVriPage_Click);
            // 
            // radioButtonParagraph
            // 
            resources.ApplyResources(this.radioButtonParagraph, "radioButtonParagraph");
            this.radioButtonParagraph.Name = "radioButtonParagraph";
            this.radioButtonParagraph.TabStop = true;
            this.radioButtonParagraph.UseVisualStyleBackColor = true;
            this.radioButtonParagraph.Click += new System.EventHandler(this.radioButtonParagraph_Click);
            // 
            // lblNumber
            // 
            resources.ApplyResources(this.lblNumber, "lblNumber");
            this.lblNumber.Name = "lblNumber";
            // 
            // textBoxNumber
            // 
            resources.ApplyResources(this.textBoxNumber, "textBoxNumber");
            this.textBoxNumber.Name = "textBoxNumber";
            this.textBoxNumber.TextChanged += new System.EventHandler(this.textBoxNumber_TextChanged);
            // 
            // buttonOK
            // 
            this.buttonOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            resources.ApplyResources(this.buttonOK, "buttonOK");
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.UseVisualStyleBackColor = true;
            // 
            // buttonCancel
            // 
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.buttonCancel, "buttonCancel");
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            // 
            // FormGoTo
            // 
            this.AcceptButton = this.buttonOK;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.textBoxNumber);
            this.Controls.Add(this.lblNumber);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblGoToWhat);
            this.MaximizeBox = false;
            this.Name = "FormGoTo";
            this.ShowIcon = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Load += new System.EventHandler(this.FormGoTo_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblGoToWhat;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblNumber;
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        public System.Windows.Forms.TextBox textBoxNumber;
        public System.Windows.Forms.RadioButton radioButtonPtsPage;
        public System.Windows.Forms.RadioButton radioButtonMyanmarPage;
        public System.Windows.Forms.RadioButton radioButtonVriPage;
        public System.Windows.Forms.RadioButton radioButtonParagraph;
        public System.Windows.Forms.RadioButton radioButtonOtherPage;
        public System.Windows.Forms.RadioButton radioButtonThaiPage;
    }
}