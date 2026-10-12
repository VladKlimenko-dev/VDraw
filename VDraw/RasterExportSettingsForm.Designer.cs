namespace VDraw.Editing
{
	partial class RasterExportSettingsForm
	{
		/// <summary>Требуется конструктору. Не удалять.</summary>
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}

			base.Dispose(disposing);
		}

		#region Код, автоматически созданный конструктором форм Windows

		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RasterExportSettingsForm));
			preview = new ExportPreviewPanel();
			widthLabel = new Label();
			widthBox = new NumericUpDown();
			keepAspect = new CheckBox();
			heightLabel = new Label();
			heightBox = new NumericUpDown();
			transparent = new CheckBox();
			paddingLabel = new Label();
			leftLabel = new Label();
			padLeft = new NumericUpDown();
			topLabel = new Label();
			padTop = new NumericUpDown();
			rightLabel = new Label();
			padRight = new NumericUpDown();
			bottomLabel = new Label();
			padBottom = new NumericUpDown();
			backgroundButton = new Button();
			backgroundSwatch = new Panel();
			info = new Label();
			navigationHint = new Label();
			okButton = new Button();
			cancelButton = new Button();
			previewTimer = new System.Windows.Forms.Timer(components);
			groupBox1 = new GroupBox();
			((System.ComponentModel.ISupportInitialize)widthBox).BeginInit();
			((System.ComponentModel.ISupportInitialize)heightBox).BeginInit();
			((System.ComponentModel.ISupportInitialize)padLeft).BeginInit();
			((System.ComponentModel.ISupportInitialize)padTop).BeginInit();
			((System.ComponentModel.ISupportInitialize)padRight).BeginInit();
			((System.ComponentModel.ISupportInitialize)padBottom).BeginInit();
			groupBox1.SuspendLayout();
			SuspendLayout();
			// 
			// preview
			// 
			preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			preview.BackColor = Color.FromArgb(150, 150, 150);
			preview.BorderStyle = BorderStyle.FixedSingle;
			preview.Location = new Point(12, 12);
			preview.Name = "preview";
			preview.Size = new Size(472, 460);
			preview.TabIndex = 0;
			// 
			// widthLabel
			// 
			widthLabel.AutoSize = true;
			widthLabel.Location = new Point(17, 33);
			widthLabel.Name = "widthLabel";
			widthLabel.Size = new Size(79, 20);
			widthLabel.TabIndex = 1;
			widthLabel.Text = "Width (px)";
			// 
			// widthBox
			// 
			widthBox.Location = new Point(130, 31);
			widthBox.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
			widthBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			widthBox.Name = "widthBox";
			widthBox.Size = new Size(89, 27);
			widthBox.TabIndex = 2;
			widthBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// keepAspect
			// 
			keepAspect.AutoSize = true;
			keepAspect.Checked = true;
			keepAspect.CheckState = CheckState.Checked;
			keepAspect.Location = new Point(17, 100);
			keepAspect.Name = "keepAspect";
			keepAspect.Size = new Size(147, 24);
			keepAspect.TabIndex = 3;
			keepAspect.Text = "Keep aspect ratio";
			// 
			// heightLabel
			// 
			heightLabel.AutoSize = true;
			heightLabel.Location = new Point(17, 65);
			heightLabel.Name = "heightLabel";
			heightLabel.Size = new Size(84, 20);
			heightLabel.TabIndex = 4;
			heightLabel.Text = "Height (px)";
			// 
			// heightBox
			// 
			heightBox.Location = new Point(130, 63);
			heightBox.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
			heightBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			heightBox.Name = "heightBox";
			heightBox.Size = new Size(89, 27);
			heightBox.TabIndex = 5;
			heightBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// transparent
			// 
			transparent.AutoSize = true;
			transparent.Checked = true;
			transparent.CheckState = CheckState.Checked;
			transparent.Location = new Point(17, 130);
			transparent.Name = "transparent";
			transparent.Size = new Size(191, 24);
			transparent.TabIndex = 6;
			transparent.Text = "Transparent background";
			// 
			// paddingLabel
			// 
			paddingLabel.AutoSize = true;
			paddingLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
			paddingLabel.Location = new Point(17, 182);
			paddingLabel.Name = "paddingLabel";
			paddingLabel.Size = new Size(99, 20);
			paddingLabel.TabIndex = 7;
			paddingLabel.Text = "Padding (px)";
			// 
			// leftLabel
			// 
			leftLabel.AutoSize = true;
			leftLabel.Location = new Point(17, 214);
			leftLabel.Name = "leftLabel";
			leftLabel.Size = new Size(34, 20);
			leftLabel.TabIndex = 8;
			leftLabel.Text = "Left";
			// 
			// padLeft
			// 
			padLeft.Location = new Point(77, 211);
			padLeft.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			padLeft.Name = "padLeft";
			padLeft.Size = new Size(87, 27);
			padLeft.TabIndex = 9;
			// 
			// topLabel
			// 
			topLabel.AutoSize = true;
			topLabel.Location = new Point(185, 214);
			topLabel.Name = "topLabel";
			topLabel.Size = new Size(34, 20);
			topLabel.TabIndex = 10;
			topLabel.Text = "Top";
			// 
			// padTop
			// 
			padTop.Location = new Point(259, 211);
			padTop.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			padTop.Name = "padTop";
			padTop.Size = new Size(87, 27);
			padTop.TabIndex = 11;
			// 
			// rightLabel
			// 
			rightLabel.AutoSize = true;
			rightLabel.Location = new Point(17, 244);
			rightLabel.Name = "rightLabel";
			rightLabel.Size = new Size(44, 20);
			rightLabel.TabIndex = 12;
			rightLabel.Text = "Right";
			// 
			// padRight
			// 
			padRight.Location = new Point(77, 241);
			padRight.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			padRight.Name = "padRight";
			padRight.Size = new Size(87, 27);
			padRight.TabIndex = 13;
			// 
			// bottomLabel
			// 
			bottomLabel.AutoSize = true;
			bottomLabel.Location = new Point(185, 244);
			bottomLabel.Name = "bottomLabel";
			bottomLabel.Size = new Size(59, 20);
			bottomLabel.TabIndex = 14;
			bottomLabel.Text = "Bottom";
			// 
			// padBottom
			// 
			padBottom.Location = new Point(259, 241);
			padBottom.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
			padBottom.Name = "padBottom";
			padBottom.Size = new Size(87, 27);
			padBottom.TabIndex = 15;
			// 
			// backgroundButton
			// 
			backgroundButton.Location = new Point(17, 279);
			backgroundButton.Name = "backgroundButton";
			backgroundButton.Size = new Size(147, 36);
			backgroundButton.TabIndex = 16;
			backgroundButton.Text = "Background...";
			backgroundButton.UseVisualStyleBackColor = true;
			// 
			// backgroundSwatch
			// 
			backgroundSwatch.BackColor = Color.White;
			backgroundSwatch.BorderStyle = BorderStyle.FixedSingle;
			backgroundSwatch.Location = new Point(170, 279);
			backgroundSwatch.Name = "backgroundSwatch";
			backgroundSwatch.Size = new Size(176, 36);
			backgroundSwatch.TabIndex = 17;
			// 
			// info
			// 
			info.ForeColor = SystemColors.GrayText;
			info.Location = new Point(17, 318);
			info.Name = "info";
			info.Size = new Size(335, 72);
			info.TabIndex = 18;
			// 
			// navigationHint
			// 
			navigationHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
			navigationHint.ForeColor = SystemColors.GrayText;
			navigationHint.Location = new Point(12, 475);
			navigationHint.Name = "navigationHint";
			navigationHint.Size = new Size(400, 32);
			navigationHint.TabIndex = 19;
			navigationHint.Text = "Drag to pan, wheel to zoom, double-click to fit.";
			// 
			// okButton
			// 
			okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			okButton.DialogResult = DialogResult.OK;
			okButton.Location = new Point(622, 466);
			okButton.Name = "okButton";
			okButton.Size = new Size(110, 41);
			okButton.TabIndex = 20;
			okButton.Text = "OK";
			okButton.UseVisualStyleBackColor = true;
			// 
			// cancelButton
			// 
			cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			cancelButton.DialogResult = DialogResult.Cancel;
			cancelButton.Location = new Point(738, 466);
			cancelButton.Name = "cancelButton";
			cancelButton.Size = new Size(110, 41);
			cancelButton.TabIndex = 21;
			cancelButton.Text = "Cancel";
			cancelButton.UseVisualStyleBackColor = true;
			// 
			// previewTimer
			// 
			previewTimer.Interval = 220;
			// 
			// groupBox1
			// 
			groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			groupBox1.Controls.Add(widthLabel);
			groupBox1.Controls.Add(info);
			groupBox1.Controls.Add(widthBox);
			groupBox1.Controls.Add(backgroundSwatch);
			groupBox1.Controls.Add(keepAspect);
			groupBox1.Controls.Add(backgroundButton);
			groupBox1.Controls.Add(heightLabel);
			groupBox1.Controls.Add(padBottom);
			groupBox1.Controls.Add(heightBox);
			groupBox1.Controls.Add(bottomLabel);
			groupBox1.Controls.Add(transparent);
			groupBox1.Controls.Add(padRight);
			groupBox1.Controls.Add(paddingLabel);
			groupBox1.Controls.Add(rightLabel);
			groupBox1.Controls.Add(leftLabel);
			groupBox1.Controls.Add(padTop);
			groupBox1.Controls.Add(padLeft);
			groupBox1.Controls.Add(topLabel);
			groupBox1.Location = new Point(490, 12);
			groupBox1.Name = "groupBox1";
			groupBox1.Size = new Size(358, 406);
			groupBox1.TabIndex = 22;
			groupBox1.TabStop = false;
			groupBox1.Text = "Export parameters";
			// 
			// RasterExportSettingsForm
			// 
			AcceptButton = okButton;
			AutoScaleDimensions = new SizeF(8F, 20F);
			AutoScaleMode = AutoScaleMode.Font;
			CancelButton = cancelButton;
			ClientSize = new Size(860, 521);
			Controls.Add(groupBox1);
			Controls.Add(navigationHint);
			Controls.Add(okButton);
			Controls.Add(cancelButton);
			Controls.Add(preview);
			Icon = (Icon)resources.GetObject("$this.Icon");
			MinimizeBox = false;
			MinimumSize = new Size(743, 527);
			Name = "RasterExportSettingsForm";
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			Text = "Export image";
			((System.ComponentModel.ISupportInitialize)widthBox).EndInit();
			((System.ComponentModel.ISupportInitialize)heightBox).EndInit();
			((System.ComponentModel.ISupportInitialize)padLeft).EndInit();
			((System.ComponentModel.ISupportInitialize)padTop).EndInit();
			((System.ComponentModel.ISupportInitialize)padRight).EndInit();
			((System.ComponentModel.ISupportInitialize)padBottom).EndInit();
			groupBox1.ResumeLayout(false);
			groupBox1.PerformLayout();
			ResumeLayout(false);
		}

		#endregion

		private ExportPreviewPanel preview;
		private Label widthLabel;
		private NumericUpDown widthBox;
		private CheckBox keepAspect;
		private Label heightLabel;
		private NumericUpDown heightBox;
		private CheckBox transparent;
		private Label paddingLabel;
		private Label leftLabel;
		private NumericUpDown padLeft;
		private Label topLabel;
		private NumericUpDown padTop;
		private Label rightLabel;
		private NumericUpDown padRight;
		private Label bottomLabel;
		private NumericUpDown padBottom;
		private Button backgroundButton;
		private Panel backgroundSwatch;
		private Label info;
		private Label navigationHint;
		private Button okButton;
		private Button cancelButton;
		private System.Windows.Forms.Timer previewTimer;
		private GroupBox groupBox1;
	}
}
