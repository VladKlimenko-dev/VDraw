using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

partial class DocPropertiesForm
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
		label1 = new Label();
		title_tb = new TextBox();
		label2 = new Label();
		unit_cb = new ComboBox();
		label3 = new Label();
		format_cb = new ComboBox();
		orientation_cb = new ComboBox();
		label4 = new Label();
		props_ok_but = new Button();
		props_cancel_but = new Button();
		label5 = new Label();
		width_nud = new NumericUpDown();
		height_nud = new NumericUpDown();
		label6 = new Label();
		dpi_nud = new NumericUpDown();
		label8 = new Label();
		((System.ComponentModel.ISupportInitialize)width_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)height_nud).BeginInit();
		((System.ComponentModel.ISupportInitialize)dpi_nud).BeginInit();
		SuspendLayout();
		// 
		// label1
		// 
		label1.AutoSize = true;
		label1.Location = new Point(14, 20);
		label1.Name = "label1";
		label1.Size = new Size(38, 20);
		label1.TabIndex = 0;
		label1.Text = "Title";
		// 
		// doc_title_tb
		// 
		title_tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		title_tb.Location = new Point(128, 16);
		title_tb.Margin = new Padding(3, 4, 3, 4);
		title_tb.Name = "doc_title_tb";
		title_tb.Size = new Size(212, 27);
		title_tb.TabIndex = 1;
		title_tb.Text = "document_title";
		// 
		// label2
		// 
		label2.AutoSize = true;
		label2.Location = new Point(14, 65);
		label2.Name = "label2";
		label2.Size = new Size(42, 20);
		label2.TabIndex = 2;
		label2.Text = "Units";
		// 
		// unit_cb
		// 
		unit_cb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		unit_cb.DropDownStyle = ComboBoxStyle.DropDownList;
		unit_cb.FormattingEnabled = true;
		unit_cb.ItemHeight = 23;
		unit_cb.Location = new Point(128, 55);
		unit_cb.Margin = new Padding(3, 4, 3, 4);
		unit_cb.Name = "unit_cb";
		unit_cb.Size = new Size(212, 29);
		unit_cb.TabIndex = 3;
		unit_cb.SelectedIndexChanged += unis_cb_SelectedIndexChanged;
		// 
		// label3
		// 
		label3.AutoSize = true;
		label3.Location = new Point(14, 111);
		label3.Name = "label3";
		label3.Size = new Size(56, 20);
		label3.TabIndex = 4;
		label3.Text = "Format";
		// 
		// format_cb
		// 
		format_cb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		format_cb.DropDownStyle = ComboBoxStyle.DropDownList;
		format_cb.FormattingEnabled = true;
		format_cb.ItemHeight = 23;
		format_cb.Location = new Point(128, 101);
		format_cb.Margin = new Padding(3, 4, 3, 4);
		format_cb.Name = "format_cb";
		format_cb.Size = new Size(212, 29);
		format_cb.TabIndex = 5;
		format_cb.SelectedIndexChanged += format_cb_SelectedIndexChanged;
		// 
		// orientation_cb
		// 
		orientation_cb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		orientation_cb.DropDownStyle = ComboBoxStyle.DropDownList;
		orientation_cb.FormattingEnabled = true;
		orientation_cb.ItemHeight = 23;
		orientation_cb.Location = new Point(128, 148);
		orientation_cb.Margin = new Padding(3, 4, 3, 4);
		orientation_cb.Name = "orientation_cb";
		orientation_cb.Size = new Size(212, 29);
		orientation_cb.TabIndex = 7;
		orientation_cb.SelectedIndexChanged += orientation_cb_SelectedIndexChanged;
		// 
		// label4
		// 
		label4.AutoSize = true;
		label4.Location = new Point(14, 157);
		label4.Name = "label4";
		label4.Size = new Size(84, 20);
		label4.TabIndex = 6;
		label4.Text = "Orientation";
		// 
		// props_ok_but
		// 
		props_ok_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		props_ok_but.DialogResult = DialogResult.OK;
		props_ok_but.Location = new Point(255, 326);
		props_ok_but.Margin = new Padding(3, 4, 3, 4);
		props_ok_but.Name = "props_ok_but";
		props_ok_but.Size = new Size(86, 31);
		props_ok_but.TabIndex = 8;
		props_ok_but.Text = "Ok";
		props_ok_but.Click += props_ok_but_Click;
		// 
		// props_cancel_but
		// 
		props_cancel_but.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
		props_cancel_but.DialogResult = DialogResult.Cancel;
		props_cancel_but.Location = new Point(163, 326);
		props_cancel_but.Margin = new Padding(3, 4, 3, 4);
		props_cancel_but.Name = "props_cancel_but";
		props_cancel_but.Size = new Size(86, 31);
		props_cancel_but.TabIndex = 9;
		props_cancel_but.Text = "Cancel";
		// 
		// label5
		// 
		label5.AutoSize = true;
		label5.Location = new Point(14, 201);
		label5.Name = "label5";
		label5.Size = new Size(49, 20);
		label5.TabIndex = 10;
		label5.Text = "Width";
		// 
		// doc_width_nud
		// 
		width_nud.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		width_nud.DecimalPlaces = 2;
		width_nud.Location = new Point(128, 195);
		width_nud.Margin = new Padding(3, 4, 3, 4);
		width_nud.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
		width_nud.Name = "doc_width_nud";
		width_nud.Size = new Size(212, 27);
		width_nud.TabIndex = 11;
		width_nud.ThousandsSeparator = true;
		width_nud.ValueChanged += width_nud_ValueChanged;
		// 
		// doc_height_nud
		// 
		height_nud.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		height_nud.DecimalPlaces = 2;
		height_nud.Location = new Point(128, 233);
		height_nud.Margin = new Padding(3, 4, 3, 4);
		height_nud.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
		height_nud.Name = "doc_height_nud";
		height_nud.Size = new Size(212, 27);
		height_nud.TabIndex = 13;
		height_nud.ThousandsSeparator = true;
		height_nud.ValueChanged += height_nud_ValueChanged;
		// 
		// label6
		// 
		label6.AutoSize = true;
		label6.Location = new Point(14, 240);
		label6.Name = "label6";
		label6.Size = new Size(54, 20);
		label6.TabIndex = 12;
		label6.Text = "Height";
		// 
		// doc_dpi_nud
		// 
		dpi_nud.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		dpi_nud.DecimalPlaces = 1;
		dpi_nud.Location = new Point(128, 272);
		dpi_nud.Margin = new Padding(3, 4, 3, 4);
		dpi_nud.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
		dpi_nud.Name = "doc_dpi_nud";
		dpi_nud.Size = new Size(212, 27);
		dpi_nud.TabIndex = 15;
		dpi_nud.ThousandsSeparator = true;
		dpi_nud.ValueChanged += dpi_nud_ValueChanged;
		// 
		// label8
		// 
		label8.AutoSize = true;
		label8.Location = new Point(14, 279);
		label8.Name = "label8";
		label8.Size = new Size(32, 20);
		label8.TabIndex = 14;
		label8.Text = "DPI";
		// 
		// DocPropertiesForm
		// 
		AcceptButton = props_ok_but;
		AutoScaleDimensions = new SizeF(8F, 20F);
		AutoScaleMode = AutoScaleMode.Font;
		CancelButton = props_cancel_but;
		ClientSize = new Size(355, 373);
		Controls.Add(dpi_nud);
		Controls.Add(label8);
		Controls.Add(height_nud);
		Controls.Add(label6);
		Controls.Add(width_nud);
		Controls.Add(label5);
		Controls.Add(props_cancel_but);
		Controls.Add(props_ok_but);
		Controls.Add(orientation_cb);
		Controls.Add(label4);
		Controls.Add(format_cb);
		Controls.Add(label3);
		Controls.Add(unit_cb);
		Controls.Add(label2);
		Controls.Add(title_tb);
		Controls.Add(label1);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		Margin = new Padding(3, 4, 3, 4);
		MaximizeBox = false;
		MinimizeBox = false;
		MinimumSize = new Size(227, 420);
		Name = "DocPropertiesForm";
		StartPosition = FormStartPosition.CenterScreen;
		Text = "Create new document";
		TopMost = true;
		((System.ComponentModel.ISupportInitialize)width_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)height_nud).EndInit();
		((System.ComponentModel.ISupportInitialize)dpi_nud).EndInit();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private Label label1;
    private TextBox title_tb;
    private Label label2;
    private ComboBox unit_cb;
    private Label label3;
    private ComboBox format_cb;
    private ComboBox orientation_cb;
    private Label label4;
    private Button props_ok_but;
    private Button props_cancel_but;
    private Label label5;
    private NumericUpDown width_nud;
    private NumericUpDown height_nud;
    private Label label6;
    private Label label7;
    private NumericUpDown dpi_nud;
    private Label label8;
}