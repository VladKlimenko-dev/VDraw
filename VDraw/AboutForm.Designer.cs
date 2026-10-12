namespace VDraw
{
	partial class AboutForm
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutForm));
			pictureBox1 = new PictureBox();
			ok_but = new Button();
			richTextBox1 = new RichTextBox();
			((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
			SuspendLayout();
			// 
			// pictureBox1
			// 
			pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
			pictureBox1.Location = new Point(11, 17);
			pictureBox1.Name = "pictureBox1";
			pictureBox1.Size = new Size(102, 98);
			pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
			pictureBox1.TabIndex = 1;
			pictureBox1.TabStop = false;
			// 
			// ok_but
			// 
			ok_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			ok_but.DialogResult = DialogResult.OK;
			ok_but.Location = new Point(537, 308);
			ok_but.Name = "ok_but";
			ok_but.Size = new Size(105, 39);
			ok_but.TabIndex = 2;
			ok_but.Text = "Ok";
			ok_but.UseVisualStyleBackColor = true;
			// 
			// richTextBox1
			// 
			richTextBox1.BorderStyle = BorderStyle.None;
			richTextBox1.Enabled = false;
			richTextBox1.Location = new Point(119, 17);
			richTextBox1.Name = "richTextBox1";
			richTextBox1.ReadOnly = true;
			richTextBox1.ScrollBars = RichTextBoxScrollBars.None;
			richTextBox1.ShortcutsEnabled = false;
			richTextBox1.Size = new Size(523, 280);
			richTextBox1.TabIndex = 3;
			richTextBox1.Text = resources.GetString("richTextBox1.Text");
			// 
			// AboutForm
			// 
			AcceptButton = ok_but;
			AutoScaleDimensions = new SizeF(8F, 20F);
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(654, 357);
			Controls.Add(richTextBox1);
			Controls.Add(ok_but);
			Controls.Add(pictureBox1);
			Icon = (Icon)resources.GetObject("$this.Icon");
			MaximizeBox = false;
			MinimizeBox = false;
			Name = "AboutForm";
			StartPosition = FormStartPosition.CenterScreen;
			Text = "About application";
			((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
			ResumeLayout(false);
		}

		#endregion
		private PictureBox pictureBox1;
		private Button ok_but;
		private RichTextBox richTextBox1;
	}
}