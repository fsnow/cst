namespace CST
{
	partial class FormAdvancedSearch
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
			this.txtQuery = new System.Windows.Forms.TextBox();
			this.txtResults = new System.Windows.Forms.TextBox();
			this.btnSearch = new System.Windows.Forms.Button();
			this.label1 = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// txtQuery
			// 
			this.txtQuery.Location = new System.Drawing.Point(12, 60);
			this.txtQuery.Multiline = true;
			this.txtQuery.Name = "txtQuery";
			this.txtQuery.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtQuery.Size = new System.Drawing.Size(450, 99);
			this.txtQuery.TabIndex = 0;
			// 
			// txtResults
			// 
			this.txtResults.Location = new System.Drawing.Point(12, 180);
			this.txtResults.Multiline = true;
			this.txtResults.Name = "txtResults";
			this.txtResults.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtResults.Size = new System.Drawing.Size(450, 207);
			this.txtResults.TabIndex = 1;
			// 
			// btnSearch
			// 
			this.btnSearch.Location = new System.Drawing.Point(494, 12);
			this.btnSearch.Name = "btnSearch";
			this.btnSearch.Size = new System.Drawing.Size(75, 23);
			this.btnSearch.TabIndex = 2;
			this.btnSearch.Text = "Search";
			this.btnSearch.UseVisualStyleBackColor = true;
			this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.Location = new System.Drawing.Point(156, 22);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(224, 16);
			this.label1.TabIndex = 3;
			this.label1.Text = "******* Under development *****";
			// 
			// FormAdvancedSearch
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(581, 412);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.btnSearch);
			this.Controls.Add(this.txtResults);
			this.Controls.Add(this.txtQuery);
			this.Name = "FormAdvancedSearch";
			this.Text = "Advanced Search";
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormAdvancedSearch_FormClosing);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox txtQuery;
		private System.Windows.Forms.TextBox txtResults;
		private System.Windows.Forms.Button btnSearch;
		private System.Windows.Forms.Label label1;
	}
}