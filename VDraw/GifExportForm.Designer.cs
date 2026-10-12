namespace VDraw.Editing;

partial class GifExportForm
{
	private System.ComponentModel.IContainer components = null;

	protected override void Dispose(bool disposing)
	{
		if (disposing && (components != null))
			components.Dispose();

		base.Dispose(disposing);
	}

	#region Windows Form Designer generated code

	private void InitializeComponent()
	{
		components = new System.ComponentModel.Container();
		preview_panel = new ExportPreviewPanel();
		width_label = new Label();
		width_nud = new NumericUpDown();
		keep_aspect_check = new CheckBox();
		height_label = new Label();
		height_nud = new NumericUpDown();
		transparent_check = new CheckBox();
		background_but = new Button();
		background_swatch = new Panel();
		fps_label = new Label();
		fps_nud = new NumericUpDown();
		use_page_space_check = new CheckBox();
		area_label = new Label();
		area_left_label = new Label();
		area_left_nud = new NumericUpDown();
		area_top_label = new Label();
		area_top_nud = new NumericUpDown();
		area_right_label = new Label();
		area_right_nud = new NumericUpDown();
		area_bottom_label = new Label();
		area_bottom_nud = new NumericUpDown();
		range_label = new Label();
		first_frame_label = new Label();
		first_frame_nud = new NumericUpDown();
		last_frame_label = new Label();
		last_frame_nud = new NumericUpDown();
		info_label = new Label();
		timeline = new AnimationExportTimeline();
		rewind_but = new Button();
		step_back_but = new Button();
		play_but = new Button();
		step_forward_but = new Button();
		forward_but = new Button();
		frame_label = new Label();
		hint_label = new Label();
		ok_but = new Button();
		cancel_but = new Button();
		play_timer = new System.Windows.Forms.Timer(components);
		groupBox1 = new GroupBox();
		((System.ComponentModel.ISupportInitialize)width_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)height_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)fps_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)area_left_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)area_top_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)area_right_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)area_bottom_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)first_frame_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)last_frame_nud).BeginInit();
		groupBox1.SuspendLayout();
		SuspendLayout();
		// 
		// preview_panel
		// 
		preview_panel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		preview_panel.BackColor = Color.FromArgb(150, 150, 150);
		preview_panel.BorderStyle = BorderStyle.FixedSingle;
		preview_panel.Location = new Point(14, 16);
		preview_panel.Margin = new Padding(3, 4, 3, 4);
		preview_panel.Name = "preview_panel";
		preview_panel.Size = new Size(792, 620);
		preview_panel.TabIndex = 0;
		// 
		// width_label
		// 
		width_label.AutoSize = true;
		width_label.Location = new Point(16, 32);
		width_label.Name = "width_label";
		width_label.Size = new Size(79, 20);
		width_label.TabIndex = 0;
		width_label.Text = "Width (px)";
		// 
		// width_nud
		// 
		width_nud.Location = new Point(141, 28);
		width_nud.Margin = new Padding(3, 4, 3, 4);
		width_nud.Maximum = new decimal(new int[] { 4000, 0, 0, 0 });
		width_nud.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
		width_nud.Name = "width_nud";
		width_nud.Size = new Size(91, 27);
		width_nud.TabIndex = 1;
		width_nud.Value = new decimal(new int[] { 400, 0, 0, 0 });
		// 
		// keep_aspect_check
		// 
		keep_aspect_check.AutoSize = true;
		keep_aspect_check.Checked = true;
		keep_aspect_check.CheckState = CheckState.Checked;
		keep_aspect_check.Location = new Point(16, 106);
		keep_aspect_check.Margin = new Padding(3, 4, 3, 4);
		keep_aspect_check.Name = "keep_aspect_check";
		keep_aspect_check.Size = new Size(147, 24);
		keep_aspect_check.TabIndex = 2;
		keep_aspect_check.Text = "Keep aspect ratio";
		// 
		// height_label
		// 
		height_label.AutoSize = true;
		height_label.Location = new Point(16, 66);
		height_label.Name = "height_label";
		height_label.Size = new Size(84, 20);
		height_label.TabIndex = 3;
		height_label.Text = "Height (px)";
		// 
		// height_nud
		// 
		height_nud.Location = new Point(141, 62);
		height_nud.Margin = new Padding(3, 4, 3, 4);
		height_nud.Maximum = new decimal(new int[] { 4000, 0, 0, 0 });
		height_nud.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
		height_nud.Name = "height_nud";
		height_nud.Size = new Size(91, 27);
		height_nud.TabIndex = 4;
		height_nud.Value = new decimal(new int[] { 300, 0, 0, 0 });
		// 
		// transparent_check
		// 
		transparent_check.AutoSize = true;
		transparent_check.Location = new Point(16, 138);
		transparent_check.Margin = new Padding(3, 4, 3, 4);
		transparent_check.Name = "transparent_check";
		transparent_check.Size = new Size(191, 24);
		transparent_check.TabIndex = 5;
		transparent_check.Text = "Transparent background";
		// 
		// background_but
		// 
		background_but.Location = new Point(16, 170);
		background_but.Margin = new Padding(3, 4, 3, 4);
		background_but.Name = "background_but";
		background_but.Size = new Size(137, 35);
		background_but.TabIndex = 6;
		background_but.Text = "Background...";
		background_but.UseVisualStyleBackColor = true;
		// 
		// background_swatch
		// 
		background_swatch.BackColor = Color.White;
		background_swatch.BorderStyle = BorderStyle.FixedSingle;
		background_swatch.Location = new Point(162, 170);
		background_swatch.Margin = new Padding(3, 4, 3, 4);
		background_swatch.Name = "background_swatch";
		background_swatch.Size = new Size(114, 34);
		background_swatch.TabIndex = 7;
		// 
		// fps_label
		// 
		fps_label.AutoSize = true;
		fps_label.Location = new Point(16, 223);
		fps_label.Name = "fps_label";
		fps_label.Size = new Size(133, 20);
		fps_label.TabIndex = 8;
		fps_label.Text = "Frames per second";
		// 
		// fps_nud
		// 
		fps_nud.Location = new Point(162, 219);
		fps_nud.Margin = new Padding(3, 4, 3, 4);
		fps_nud.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
		fps_nud.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
		fps_nud.Name = "fps_nud";
		fps_nud.Size = new Size(91, 27);
		fps_nud.TabIndex = 9;
		fps_nud.Value = new decimal(new int[] { 25, 0, 0, 0 });
		// 
		// use_page_space_check
		// 
		use_page_space_check.AutoSize = true;
		use_page_space_check.Location = new Point(16, 256);
		use_page_space_check.Margin = new Padding(3, 4, 3, 4);
		use_page_space_check.Name = "use_page_space_check";
		use_page_space_check.Size = new Size(205, 24);
		use_page_space_check.TabIndex = 10;
		use_page_space_check.Text = "Use page space for export";
		// 
		// area_label
		// 
		area_label.AutoSize = true;
		area_label.ForeColor = SystemColors.GrayText;
		area_label.Location = new Point(16, 284);
		area_label.Name = "area_label";
		area_label.Size = new Size(179, 20);
		area_label.TabIndex = 11;
		area_label.Text = "Captured area, page units";
		// 
		// area_left_label
		// 
		area_left_label.AutoSize = true;
		area_left_label.Location = new Point(16, 322);
		area_left_label.Name = "area_left_label";
		area_left_label.Size = new Size(34, 20);
		area_left_label.TabIndex = 12;
		area_left_label.Text = "Left";
		// 
		// area_left_nud
		// 
		area_left_nud.DecimalPlaces = 2;
		area_left_nud.Location = new Point(84, 318);
		area_left_nud.Margin = new Padding(3, 4, 3, 4);
		area_left_nud.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
		area_left_nud.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
		area_left_nud.Name = "area_left_nud";
		area_left_nud.Size = new Size(103, 27);
		area_left_nud.TabIndex = 13;
		// 
		// area_top_label
		// 
		area_top_label.AutoSize = true;
		area_top_label.Location = new Point(203, 322);
		area_top_label.Name = "area_top_label";
		area_top_label.Size = new Size(34, 20);
		area_top_label.TabIndex = 14;
		area_top_label.Text = "Top";
		// 
		// area_top_nud
		// 
		area_top_nud.DecimalPlaces = 2;
		area_top_nud.Location = new Point(267, 318);
		area_top_nud.Margin = new Padding(3, 4, 3, 4);
		area_top_nud.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
		area_top_nud.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
		area_top_nud.Name = "area_top_nud";
		area_top_nud.Size = new Size(103, 27);
		area_top_nud.TabIndex = 15;
		// 
		// area_right_label
		// 
		area_right_label.AutoSize = true;
		area_right_label.Location = new Point(16, 362);
		area_right_label.Name = "area_right_label";
		area_right_label.Size = new Size(44, 20);
		area_right_label.TabIndex = 16;
		area_right_label.Text = "Right";
		// 
		// area_right_nud
		// 
		area_right_nud.DecimalPlaces = 2;
		area_right_nud.Location = new Point(84, 358);
		area_right_nud.Margin = new Padding(3, 4, 3, 4);
		area_right_nud.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
		area_right_nud.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
		area_right_nud.Name = "area_right_nud";
		area_right_nud.Size = new Size(103, 27);
		area_right_nud.TabIndex = 17;
		// 
		// area_bottom_label
		// 
		area_bottom_label.AutoSize = true;
		area_bottom_label.Location = new Point(203, 362);
		area_bottom_label.Name = "area_bottom_label";
		area_bottom_label.Size = new Size(59, 20);
		area_bottom_label.TabIndex = 18;
		area_bottom_label.Text = "Bottom";
		// 
		// area_bottom_nud
		// 
		area_bottom_nud.DecimalPlaces = 2;
		area_bottom_nud.Location = new Point(267, 358);
		area_bottom_nud.Margin = new Padding(3, 4, 3, 4);
		area_bottom_nud.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
		area_bottom_nud.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
		area_bottom_nud.Name = "area_bottom_nud";
		area_bottom_nud.Size = new Size(103, 27);
		area_bottom_nud.TabIndex = 19;
		// 
		// range_label
		// 
		range_label.AutoSize = true;
		range_label.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
		range_label.Location = new Point(16, 412);
		range_label.Name = "range_label";
		range_label.Size = new Size(116, 20);
		range_label.TabIndex = 20;
		range_label.Text = "Exported range";
		// 
		// first_frame_label
		// 
		first_frame_label.AutoSize = true;
		first_frame_label.Location = new Point(16, 452);
		first_frame_label.Name = "first_frame_label";
		first_frame_label.Size = new Size(79, 20);
		first_frame_label.TabIndex = 21;
		first_frame_label.Text = "First frame";
		// 
		// first_frame_nud
		// 
		first_frame_nud.Location = new Point(141, 448);
		first_frame_nud.Margin = new Padding(3, 4, 3, 4);
		first_frame_nud.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
		first_frame_nud.Name = "first_frame_nud";
		first_frame_nud.Size = new Size(91, 27);
		first_frame_nud.TabIndex = 22;
		first_frame_nud.Value = new decimal(new int[] { 1, 0, 0, 0 });
		// 
		// last_frame_label
		// 
		last_frame_label.AutoSize = true;
		last_frame_label.Location = new Point(16, 492);
		last_frame_label.Name = "last_frame_label";
		last_frame_label.Size = new Size(78, 20);
		last_frame_label.TabIndex = 23;
		last_frame_label.Text = "Last frame";
		// 
		// last_frame_nud
		// 
		last_frame_nud.Location = new Point(141, 488);
		last_frame_nud.Margin = new Padding(3, 4, 3, 4);
		last_frame_nud.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
		last_frame_nud.Name = "last_frame_nud";
		last_frame_nud.Size = new Size(91, 27);
		last_frame_nud.TabIndex = 24;
		last_frame_nud.Value = new decimal(new int[] { 1, 0, 0, 0 });
		// 
		// info_label
		// 
		info_label.ForeColor = SystemColors.GrayText;
		info_label.Location = new Point(16, 519);
		info_label.Name = "info_label";
		info_label.Size = new Size(377, 80);
		info_label.TabIndex = 25;
		// 
		// timeline
		// 
		timeline.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		timeline.Location = new Point(14, 653);
		timeline.Margin = new Padding(3, 4, 3, 4);
		timeline.Name = "timeline";
		timeline.Size = new Size(1197, 59);
		timeline.TabIndex = 1;
		// 
		// rewind_but
		// 
		rewind_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		rewind_but.Location = new Point(14, 722);
		rewind_but.Margin = new Padding(3, 4, 3, 4);
		rewind_but.Name = "rewind_but";
		rewind_but.Size = new Size(46, 37);
		rewind_but.TabIndex = 26;
		rewind_but.Text = "|<";
		rewind_but.UseVisualStyleBackColor = true;
		// 
		// step_back_but
		// 
		step_back_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		step_back_but.Location = new Point(66, 722);
		step_back_but.Margin = new Padding(3, 4, 3, 4);
		step_back_but.Name = "step_back_but";
		step_back_but.Size = new Size(46, 37);
		step_back_but.TabIndex = 27;
		step_back_but.Text = "<";
		step_back_but.UseVisualStyleBackColor = true;
		// 
		// play_but
		// 
		play_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		play_but.Location = new Point(119, 722);
		play_but.Margin = new Padding(3, 4, 3, 4);
		play_but.Name = "play_but";
		play_but.Size = new Size(69, 37);
		play_but.TabIndex = 28;
		play_but.Text = "Play";
		play_but.UseVisualStyleBackColor = true;
		// 
		// step_forward_but
		// 
		step_forward_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		step_forward_but.Location = new Point(194, 722);
		step_forward_but.Margin = new Padding(3, 4, 3, 4);
		step_forward_but.Name = "step_forward_but";
		step_forward_but.Size = new Size(46, 37);
		step_forward_but.TabIndex = 29;
		step_forward_but.Text = ">";
		step_forward_but.UseVisualStyleBackColor = true;
		// 
		// forward_but
		// 
		forward_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		forward_but.Location = new Point(247, 722);
		forward_but.Margin = new Padding(3, 4, 3, 4);
		forward_but.Name = "forward_but";
		forward_but.Size = new Size(46, 37);
		forward_but.TabIndex = 30;
		forward_but.Text = ">|";
		forward_but.UseVisualStyleBackColor = true;
		// 
		// frame_label
		// 
		frame_label.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		frame_label.AutoSize = true;
		frame_label.Location = new Point(304, 730);
		frame_label.Name = "frame_label";
		frame_label.Size = new Size(84, 20);
		frame_label.TabIndex = 31;
		frame_label.Text = "Frame 1 / 1";
		// 
		// hint_label
		// 
		hint_label.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
		hint_label.AutoSize = true;
		hint_label.ForeColor = SystemColors.GrayText;
		hint_label.Location = new Point(14, 773);
		hint_label.Name = "hint_label";
		hint_label.Size = new Size(323, 20);
		hint_label.TabIndex = 32;
		hint_label.Text = "Drag to pan, wheel to zoom, double-click to fit.";
		// 
		// ok_but
		// 
		ok_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		ok_but.DialogResult = DialogResult.OK;
		ok_but.Location = new Point(947, 768);
		ok_but.Margin = new Padding(3, 4, 3, 4);
		ok_but.Name = "ok_but";
		ok_but.Size = new Size(126, 37);
		ok_but.TabIndex = 33;
		ok_but.Text = "OK";
		ok_but.UseVisualStyleBackColor = true;
		// 
		// cancel_but
		// 
		cancel_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		cancel_but.DialogResult = DialogResult.Cancel;
		cancel_but.Location = new Point(1085, 768);
		cancel_but.Margin = new Padding(3, 4, 3, 4);
		cancel_but.Name = "cancel_but";
		cancel_but.Size = new Size(126, 37);
		cancel_but.TabIndex = 34;
		cancel_but.Text = "Cancel";
		cancel_but.UseVisualStyleBackColor = true;
		// 
		// groupBox1
		// 
		groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		groupBox1.Controls.Add(width_label);
		groupBox1.Controls.Add(info_label);
		groupBox1.Controls.Add(width_nud);
		groupBox1.Controls.Add(last_frame_nud);
		groupBox1.Controls.Add(keep_aspect_check);
		groupBox1.Controls.Add(last_frame_label);
		groupBox1.Controls.Add(height_label);
		groupBox1.Controls.Add(first_frame_nud);
		groupBox1.Controls.Add(height_nud);
		groupBox1.Controls.Add(first_frame_label);
		groupBox1.Controls.Add(transparent_check);
		groupBox1.Controls.Add(range_label);
		groupBox1.Controls.Add(background_but);
		groupBox1.Controls.Add(area_bottom_nud);
		groupBox1.Controls.Add(background_swatch);
		groupBox1.Controls.Add(area_bottom_label);
		groupBox1.Controls.Add(fps_label);
		groupBox1.Controls.Add(area_right_nud);
		groupBox1.Controls.Add(fps_nud);
		groupBox1.Controls.Add(area_right_label);
		groupBox1.Controls.Add(use_page_space_check);
		groupBox1.Controls.Add(area_top_nud);
		groupBox1.Controls.Add(area_label);
		groupBox1.Controls.Add(area_top_label);
		groupBox1.Controls.Add(area_left_label);
		groupBox1.Controls.Add(area_left_nud);
		groupBox1.Location = new Point(812, 16);
		groupBox1.Name = "groupBox1";
		groupBox1.Size = new Size(400, 620);
		groupBox1.TabIndex = 35;
		groupBox1.TabStop = false;
		groupBox1.Text = "Export parameters";
		// 
		// GifExportForm
		// 
		AutoScaleDimensions = new SizeF(8F, 20F);
		AutoScaleMode = AutoScaleMode.Font;
		CancelButton = cancel_but;
		ClientSize = new Size(1224, 821);
		Controls.Add(groupBox1);
		Controls.Add(timeline);
		Controls.Add(rewind_but);
		Controls.Add(step_back_but);
		Controls.Add(play_but);
		Controls.Add(step_forward_but);
		Controls.Add(forward_but);
		Controls.Add(frame_label);
		Controls.Add(hint_label);
		Controls.Add(ok_but);
		Controls.Add(cancel_but);
		Controls.Add(preview_panel);
		Margin = new Padding(3, 4, 3, 4);
		MinimizeBox = false;
		MinimumSize = new Size(797, 731);
		Name = "GifExportForm";
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Export animated GIF";
		((System.ComponentModel.ISupportInitialize)width_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)height_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)fps_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)area_left_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)area_top_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)area_right_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)area_bottom_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)first_frame_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)last_frame_nud).EndInit();
		groupBox1.ResumeLayout(false);
		groupBox1.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private ExportPreviewPanel preview_panel;
	private Label width_label;
	private NumericUpDown width_nud;
	private CheckBox keep_aspect_check;
	private Label height_label;
	private NumericUpDown height_nud;
	private CheckBox transparent_check;
	private Button background_but;
	private Panel background_swatch;
	private Label fps_label;
	private NumericUpDown fps_nud;
	private CheckBox use_page_space_check;
	private Label area_label;
	private Label area_left_label;
	private NumericUpDown area_left_nud;
	private Label area_top_label;
	private NumericUpDown area_top_nud;
	private Label area_right_label;
	private NumericUpDown area_right_nud;
	private Label area_bottom_label;
	private NumericUpDown area_bottom_nud;
	private Label range_label;
	private Label first_frame_label;
	private NumericUpDown first_frame_nud;
	private Label last_frame_label;
	private NumericUpDown last_frame_nud;
	private Label info_label;
	private AnimationExportTimeline timeline;
	private Button rewind_but;
	private Button step_back_but;
	private Button play_but;
	private Button step_forward_but;
	private Button forward_but;
	private Label frame_label;
	private Label hint_label;
	private Button ok_but;
	private Button cancel_but;
	private System.Windows.Forms.Timer play_timer;
	private GroupBox groupBox1;
}
