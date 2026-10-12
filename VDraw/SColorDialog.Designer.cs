using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

partial class SColorDialog
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
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SColorDialog));
		SV_Slider = new PictureBox();
		H_Slider = new PictureBox();
		A_Slider = new PictureBox();
		cur_color_pb = new PictureBox();
		ok_but = new Button();
		cancel_but = new Button();
		add_custom_color_but = new Button();
		sample_but = new Button();
		((System.ComponentModel.ISupportInitialize)SV_Slider).BeginInit();
		((System.ComponentModel.ISupportInitialize)H_Slider).BeginInit();
		((System.ComponentModel.ISupportInitialize)A_Slider).BeginInit();
		((System.ComponentModel.ISupportInitialize)cur_color_pb).BeginInit();
		SuspendLayout();
		// 
		// SV_Slider
		// 
		SV_Slider.Location = new Point(12, 12);
		SV_Slider.Name = "SV_Slider";
		SV_Slider.Size = new Size(213, 165);
		SV_Slider.TabIndex = 0;
		SV_Slider.TabStop = false;
		SV_Slider.Paint += SV_Slider_Paint;
		SV_Slider.MouseMove += SV_Slider_MouseMove;
		// 
		// H_Slider
		// 
		H_Slider.Location = new Point(12, 183);
		H_Slider.Name = "H_Slider";
		H_Slider.Size = new Size(213, 16);
		H_Slider.TabIndex = 1;
		H_Slider.TabStop = false;
		H_Slider.Paint += H_Slider_Paint;
		H_Slider.MouseMove += H_Slider_MouseMove;
		// 
		// A_Slider
		// 
		A_Slider.BackgroundImage = (Image)resources.GetObject("A_Slider.BackgroundImage");
		A_Slider.Location = new Point(12, 205);
		A_Slider.Name = "A_Slider";
		A_Slider.Size = new Size(213, 16);
		A_Slider.TabIndex = 2;
		A_Slider.TabStop = false;
		A_Slider.Paint += A_Slider_Paint;
		A_Slider.MouseMove += A_Slider_MouseMove;
		// 
		// cur_color_pb
		// 
		cur_color_pb.BackgroundImage = (Image)resources.GetObject("cur_color_pb.BackgroundImage");
		cur_color_pb.ErrorImage = null;
		cur_color_pb.InitialImage = null;
		cur_color_pb.Location = new Point(231, 12);
		cur_color_pb.Name = "cur_color_pb";
		cur_color_pb.Size = new Size(65, 65);
		cur_color_pb.TabIndex = 3;
		cur_color_pb.TabStop = false;
		cur_color_pb.Paint += cur_color_pb_Paint;
		// 
		// ok_but
		// 
		ok_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		ok_but.DialogResult = DialogResult.OK;
		ok_but.Location = new Point(221, 477);
		ok_but.Name = "ok_but";
		ok_but.Size = new Size(70, 31);
		ok_but.TabIndex = 4;
		ok_but.Text = "Ok";
		ok_but.UseVisualStyleBackColor = true;
		// 
		// cancel_but
		// 
		cancel_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		cancel_but.DialogResult = DialogResult.Cancel;
		cancel_but.Location = new Point(145, 477);
		cancel_but.Name = "cancel_but";
		cancel_but.Size = new Size(70, 31);
		cancel_but.TabIndex = 5;
		cancel_but.Text = "Cancel";
		cancel_but.UseVisualStyleBackColor = true;
		// 
		// add_custom_color_but
		// 
		add_custom_color_but.Location = new Point(231, 83);
		add_custom_color_but.Name = "add_custom_color_but";
		add_custom_color_but.Size = new Size(65, 31);
		add_custom_color_but.TabIndex = 6;
		add_custom_color_but.Text = "Add";
		add_custom_color_but.UseVisualStyleBackColor = true;
		add_custom_color_but.Click += add_custom_color_but_Click;
		// 
		// sample_but
		// 
		sample_but.Location = new Point(238, 120);
		sample_but.Name = "sample_but";
		sample_but.Size = new Size(56, 56);
		sample_but.TabIndex = 7;
		sample_but.Text = "button1";
		sample_but.UseVisualStyleBackColor = true;
		sample_but.Click += sample_but_Click;
		// 
		// SColorDialog
		// 
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		BackColor = Color.White;
		ClientSize = new Size(303, 520);
		Controls.Add(sample_but);
		Controls.Add(add_custom_color_but);
		Controls.Add(cancel_but);
		Controls.Add(ok_but);
		Controls.Add(cur_color_pb);
		Controls.Add(A_Slider);
		Controls.Add(H_Slider);
		Controls.Add(SV_Slider);
		DoubleBuffered = true;
		FormBorderStyle = FormBorderStyle.Fixed3D;
		MaximizeBox = false;
		MinimizeBox = false;
		Name = "SColorDialog";
		Text = "Color picker";
		TopMost = true;
		((System.ComponentModel.ISupportInitialize)SV_Slider).EndInit();
		((System.ComponentModel.ISupportInitialize)H_Slider).EndInit();
		((System.ComponentModel.ISupportInitialize)A_Slider).EndInit();
		((System.ComponentModel.ISupportInitialize)cur_color_pb).EndInit();
		ResumeLayout(false);

	}

	#endregion

	private PictureBox SV_Slider;
    private PictureBox H_Slider;
    private PictureBox A_Slider;
    private PictureBox cur_color_pb;
    private Button ok_but;
    private Button cancel_but;
    private Button add_custom_color_but;
	private Button sample_but;
}