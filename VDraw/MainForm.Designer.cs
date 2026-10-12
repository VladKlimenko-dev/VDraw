using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw
{
	partial class MainForm
	{
		/// <summary>
		///  Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		///  Clean up any resources being used.
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
		///  Required method for Designer support - do not modify
		///  the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			menuStrip1 = new MenuStrip();
			menu_file = new ToolStripMenuItem();
			menu_file_new = new ToolStripMenuItem();
			menu_file_open = new ToolStripMenuItem();
			menu_file_import = new ToolStripMenuItem();
			menu_file_save = new ToolStripMenuItem();
			menu_file_save_as = new ToolStripMenuItem();
			menu_file_export = new ToolStripMenuItem();
			menu_file_print = new ToolStripMenuItem();
			menu_file_close = new ToolStripMenuItem();
			toolStripMenuItem1 = new ToolStripSeparator();
			menu_settings = new ToolStripMenuItem();
			toolStripMenuItem2 = new ToolStripSeparator();
			menu_file_exit = new ToolStripMenuItem();
			menu_document = new ToolStripMenuItem();
			menu_page = new ToolStripMenuItem();
			menu_objects = new ToolStripMenuItem();
			menu_obj_lines = new ToolStripMenuItem();
			menu_obj_curve = new ToolStripMenuItem();
			menu_obj_rect = new ToolStripMenuItem();
			menu_obj_rounded_rect = new ToolStripMenuItem();
			menu_obj_circle2p = new ToolStripMenuItem();
			menu_obj_circle3p = new ToolStripMenuItem();
			menu_obj_ellispe3p = new ToolStripMenuItem();
			menu_obj_ellipse5p = new ToolStripMenuItem();
			menu_obj_poly = new ToolStripMenuItem();
			menu_obj_text = new ToolStripMenuItem();
			menu_obj_image = new ToolStripMenuItem();
			menu_obj_measurement_distance = new ToolStripMenuItem();
			measureAngleToolStripMenuItem = new ToolStripMenuItem();
			customShapesToolStripMenuItem = new ToolStripMenuItem();
			menu_obj_add_to_custom_shapes = new ToolStripMenuItem();
			menu_obj_import_vdrs = new ToolStripMenuItem();
			menu_obj_import_visio = new ToolStripMenuItem();
			toolStripSeparator1 = new ToolStripSeparator();
			convertToCurveToolStripMenuItem = new ToolStripMenuItem();
			helpToolStripMenuItem = new ToolStripMenuItem();
			menu_about = new ToolStripMenuItem();
			menu_manual = new ToolStripMenuItem();
			page_view_flow = new FlowLayoutPanel();
			page_print_borders_but = new Button();
			page_grid_but = new Button();
			page_zero_flow = new FlowLayoutPanel();
			label4 = new Label();
			page_zero_x_nud = new NumericUpDown();
			label5 = new Label();
			page_zero_y_nud = new NumericUpDown();
			page_layout_flow = new FlowLayoutPanel();
			page_vertical_but = new Button();
			page_horizontal_but = new Button();
			label3 = new Label();
			page_uom_combo = new ComboBox();
			page_background_color_but = new ColorPreviewButton();
			page_lock_ratio_flow = new FlowLayoutPanel();
			page_lock_ratio_but = new Button();
			page_size_flow = new FlowLayoutPanel();
			label1 = new Label();
			page_size_w_nud = new NumericUpDown();
			label2 = new Label();
			page_size_h_nud = new NumericUpDown();
			page_paper_flow = new FlowLayoutPanel();
			page_paper_size_combo = new ComboBox();
			page_props_but = new Button();
			s_scissors_flow = new FlowLayoutPanel();
			s_scissors_line_but = new Button();
			s_scissors_curve_but = new Button();
			s_cap_scissors_but = new Button();
			s_colors_flow = new FlowLayoutPanel();
			s_line_color_but = new ColorPreviewButton();
			s_fill_color_but = new ColorPreviewButton();
			s_layout_flow = new FlowLayoutPanel();
			s_send_back_but = new Button();
			s_bring_to_front_but = new Button();
			s_group_but = new Button();
			s_ungroup_but = new Button();
			s_boolean_tools_flow = new FlowLayoutPanel();
			s_convert_to_curve_but = new Button();
			s_combine_but = new Button();
			s_union_but = new Button();
			s_substract_but = new Button();
			s_intersect_but = new Button();
			s_xor_but = new Button();
			i_tools_settings_flow = new FlowLayoutPanel();
			label10 = new Label();
			i_tool_shape_combo = new ComboBox();
			label6 = new Label();
			i_tool_size_nud = new NumericUpDown();
			label7 = new Label();
			i_tool_tolerance_nud = new NumericUpDown();
			label8 = new Label();
			i_tool_strength_nud = new NumericUpDown();
			label9 = new Label();
			i_tool_smooth_nud = new NumericUpDown();
			i_colors_flow = new FlowLayoutPanel();
			i_fore_color_but = new ColorPreviewButton();
			i_back_color_but = new ColorPreviewButton();
			flip_flow = new FlowLayoutPanel();
			rotate_CW_90_but = new Button();
			rotate_CCW_90_but = new Button();
			flip_horizontal_but = new Button();
			flip_vertical_but = new Button();
			image_tools_flow = new FlowLayoutPanel();
			i_select_rect_but = new Button();
			i_select_lasso_but = new Button();
			i_brush_but = new Button();
			i_erasor_but = new Button();
			i_light_but = new Button();
			i_dark_but = new Button();
			i_fill_but = new Button();
			i_contrast_but = new Button();
			i_blackwhite_but = new Button();
			c_nodes_tools_flow = new FlowLayoutPanel();
			nodes_merge_but = new Button();
			nodes_split_but = new Button();
			nodes_straight_but = new Button();
			node_tangent_but = new Button();
			node_sharp_but = new Button();
			node_delete_but = new Button();
			view_flow = new FlowLayoutPanel();
			v_show_rulers_but = new Button();
			v_show_guidelines_but = new Button();
			v_snap_but = new Button();
			v_dynamic_guidelines_but = new Button();
			v_scale_combo = new ComboBox();
			v_scale_selected_but = new Button();
			v_scale_all_but = new Button();
			v_scale_page_but = new Button();
			measure_distance_but = new Button();
			s_cursor_but = new Button();
			s_curve_edit_but = new Button();
			image_mode_but = new Button();
			s_new_line_but = new Button();
			s_new_curve_but = new Button();
			s_new_rc_but = new SkOptionsButton();
			s_new_circle_but = new SkOptionsButton();
			s_new_ellipse_but = new SkOptionsButton();
			s_new_poly_but = new Button();
			s_new_free_poly_but = new Button();
			s_new_text_but = new Button();
			s_new_image_but = new Button();
			drawing_toolbar_panel = new Panel();
			measure_angle_but = new Button();
			s_morth_but = new Button();
			main_toolbar_panel = new Panel();
			language_switch_but = new Button();
			help_but = new Button();
			main_tools_flow = new FlowLayoutPanel();
			copy_but = new Button();
			paste_but = new Button();
			delete_but = new Button();
			undo_but = new Button();
			redo_but = new Button();
			format_but = new Button();
			doc_toolbar_flow = new FlowLayoutPanel();
			d_new_but = new Button();
			d_open_but = new Button();
			d_save_but = new Button();
			d_save_as_but = new Button();
			d_import_but = new Button();
			d_export_but = new Button();
			d_print_but = new Button();
			shapes_tools_panel = new Panel();
			image_tools_panel = new Panel();
			i_actual_resolution_check = new CheckBox();
			i_size_lock_check = new CheckBox();
			page_tools_panel = new Panel();
			statusStrip = new StatusStrip();
			status_strip_label = new ToolStripStatusLabel();
			menuStrip1.SuspendLayout();
			page_view_flow.SuspendLayout();
			page_zero_flow.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)page_zero_x_nud).BeginInit();
			((System.ComponentModel.ISupportInitialize)page_zero_y_nud).BeginInit();
			page_layout_flow.SuspendLayout();
			page_lock_ratio_flow.SuspendLayout();
			page_size_flow.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)page_size_w_nud).BeginInit();
			((System.ComponentModel.ISupportInitialize)page_size_h_nud).BeginInit();
			page_paper_flow.SuspendLayout();
			s_scissors_flow.SuspendLayout();
			s_colors_flow.SuspendLayout();
			s_layout_flow.SuspendLayout();
			s_boolean_tools_flow.SuspendLayout();
			i_tools_settings_flow.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)i_tool_size_nud).BeginInit();
			((System.ComponentModel.ISupportInitialize)i_tool_tolerance_nud).BeginInit();
			((System.ComponentModel.ISupportInitialize)i_tool_strength_nud).BeginInit();
			((System.ComponentModel.ISupportInitialize)i_tool_smooth_nud).BeginInit();
			i_colors_flow.SuspendLayout();
			flip_flow.SuspendLayout();
			image_tools_flow.SuspendLayout();
			c_nodes_tools_flow.SuspendLayout();
			view_flow.SuspendLayout();
			drawing_toolbar_panel.SuspendLayout();
			main_toolbar_panel.SuspendLayout();
			main_tools_flow.SuspendLayout();
			doc_toolbar_flow.SuspendLayout();
			shapes_tools_panel.SuspendLayout();
			image_tools_panel.SuspendLayout();
			page_tools_panel.SuspendLayout();
			statusStrip.SuspendLayout();
			SuspendLayout();
			// 
			// menuStrip1
			// 
			menuStrip1.ImageScalingSize = new Size(20, 20);
			menuStrip1.Items.AddRange(new ToolStripItem[] { menu_file, menu_document, menu_page, menu_objects, helpToolStripMenuItem });
			menuStrip1.Location = new Point(0, 0);
			menuStrip1.Name = "menuStrip1";
			menuStrip1.Size = new Size(1599, 28);
			menuStrip1.TabIndex = 0;
			menuStrip1.Text = "menuStrip1";
			// 
			// menu_file
			// 
			menu_file.DropDownItems.AddRange(new ToolStripItem[] { menu_file_new, menu_file_open, menu_file_import, menu_file_save, menu_file_save_as, menu_file_export, menu_file_print, menu_file_close, toolStripMenuItem1, menu_settings, toolStripMenuItem2, menu_file_exit });
			menu_file.Name = "menu_file";
			menu_file.Size = new Size(46, 24);
			menu_file.Text = "File";
			// 
			// menu_file_new
			// 
			menu_file_new.Name = "menu_file_new";
			menu_file_new.Size = new Size(145, 26);
			menu_file_new.Text = "New";
			menu_file_new.Click += d_new_but_Click;
			// 
			// menu_file_open
			// 
			menu_file_open.Name = "menu_file_open";
			menu_file_open.Size = new Size(145, 26);
			menu_file_open.Text = "Open";
			menu_file_open.Click += d_open_but_Click;
			// 
			// menu_file_import
			// 
			menu_file_import.Name = "menu_file_import";
			menu_file_import.Size = new Size(145, 26);
			menu_file_import.Text = "Import";
			menu_file_import.Click += d_import_but_Click;
			// 
			// menu_file_save
			// 
			menu_file_save.Name = "menu_file_save";
			menu_file_save.Size = new Size(145, 26);
			menu_file_save.Text = "Save";
			menu_file_save.Click += menu_file_save_Click;
			// 
			// menu_file_save_as
			// 
			menu_file_save_as.Name = "menu_file_save_as";
			menu_file_save_as.Size = new Size(145, 26);
			menu_file_save_as.Text = "Save as";
			menu_file_save_as.Click += menu_file_save_as_Click;
			// 
			// menu_file_export
			// 
			menu_file_export.Name = "menu_file_export";
			menu_file_export.Size = new Size(145, 26);
			menu_file_export.Text = "Export";
			menu_file_export.Click += menu_file_export_Click;
			// 
			// menu_file_print
			// 
			menu_file_print.Name = "menu_file_print";
			menu_file_print.Size = new Size(145, 26);
			menu_file_print.Text = "Print";
			menu_file_print.Click += menu_file_print_Click;
			// 
			// menu_file_close
			// 
			menu_file_close.Name = "menu_file_close";
			menu_file_close.Size = new Size(145, 26);
			menu_file_close.Text = "Close";
			menu_file_close.Click += menu_file_close_Click;
			// 
			// toolStripMenuItem1
			// 
			toolStripMenuItem1.Name = "toolStripMenuItem1";
			toolStripMenuItem1.Size = new Size(142, 6);
			// 
			// menu_settings
			// 
			menu_settings.Name = "menu_settings";
			menu_settings.Size = new Size(145, 26);
			menu_settings.Text = "Settings";
			menu_settings.Click += menu_settings_Click;
			// 
			// toolStripMenuItem2
			// 
			toolStripMenuItem2.Name = "toolStripMenuItem2";
			toolStripMenuItem2.Size = new Size(142, 6);
			// 
			// menu_file_exit
			// 
			menu_file_exit.Name = "menu_file_exit";
			menu_file_exit.Size = new Size(145, 26);
			menu_file_exit.Text = "Exit";
			menu_file_exit.Click += menu_file_exit_Click;
			// 
			// menu_document
			// 
			menu_document.Name = "menu_document";
			menu_document.Size = new Size(92, 24);
			menu_document.Text = "Document";
			menu_document.Click += menu_document_Click;
			// 
			// menu_page
			// 
			menu_page.Name = "menu_page";
			menu_page.Size = new Size(55, 24);
			menu_page.Text = "Page";
			menu_page.Click += menu_page_Click;
			// 
			// menu_objects
			// 
			menu_objects.DropDownItems.AddRange(new ToolStripItem[] { menu_obj_lines, menu_obj_curve, menu_obj_rect, menu_obj_rounded_rect, menu_obj_circle2p, menu_obj_circle3p, menu_obj_ellispe3p, menu_obj_ellipse5p, menu_obj_poly, menu_obj_text, menu_obj_image, menu_obj_measurement_distance, measureAngleToolStripMenuItem, customShapesToolStripMenuItem, toolStripSeparator1, convertToCurveToolStripMenuItem });
			menu_objects.Name = "menu_objects";
			menu_objects.Size = new Size(73, 24);
			menu_objects.Text = "Objects";
			// 
			// menu_obj_lines
			// 
			menu_obj_lines.AutoSize = false;
			menu_obj_lines.Image = Properties.Resources.lines;
			menu_obj_lines.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_lines.Name = "menu_obj_lines";
			menu_obj_lines.Size = new Size(224, 48);
			menu_obj_lines.Text = "Lines";
			menu_obj_lines.TextAlign = ContentAlignment.MiddleLeft;
			menu_obj_lines.Click += menu_obj_lines_Click;
			// 
			// menu_obj_curve
			// 
			menu_obj_curve.AutoSize = false;
			menu_obj_curve.Image = Properties.Resources.curve;
			menu_obj_curve.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_curve.Name = "menu_obj_curve";
			menu_obj_curve.Size = new Size(224, 48);
			menu_obj_curve.Text = "Curve";
			// 
			// menu_obj_rect
			// 
			menu_obj_rect.AutoSize = false;
			menu_obj_rect.Image = Properties.Resources.rect;
			menu_obj_rect.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_rect.Name = "menu_obj_rect";
			menu_obj_rect.Size = new Size(224, 48);
			menu_obj_rect.Text = "Rectangle";
			// 
			// menu_obj_rounded_rect
			// 
			menu_obj_rounded_rect.AutoSize = false;
			menu_obj_rounded_rect.Image = Properties.Resources.rounded_rect;
			menu_obj_rounded_rect.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_rounded_rect.Name = "menu_obj_rounded_rect";
			menu_obj_rounded_rect.Size = new Size(224, 48);
			menu_obj_rounded_rect.Text = "Rounded Rectangle";
			// 
			// menu_obj_circle2p
			// 
			menu_obj_circle2p.AutoSize = false;
			menu_obj_circle2p.Image = Properties.Resources.circle2p;
			menu_obj_circle2p.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_circle2p.Name = "menu_obj_circle2p";
			menu_obj_circle2p.Size = new Size(224, 48);
			menu_obj_circle2p.Text = "Circle 2-points";
			// 
			// menu_obj_circle3p
			// 
			menu_obj_circle3p.AutoSize = false;
			menu_obj_circle3p.Image = Properties.Resources.circle3p;
			menu_obj_circle3p.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_circle3p.Name = "menu_obj_circle3p";
			menu_obj_circle3p.Size = new Size(224, 48);
			menu_obj_circle3p.Text = "Circle 3-points";
			// 
			// menu_obj_ellispe3p
			// 
			menu_obj_ellispe3p.AutoSize = false;
			menu_obj_ellispe3p.Image = Properties.Resources.ellipse3p;
			menu_obj_ellispe3p.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_ellispe3p.Name = "menu_obj_ellispe3p";
			menu_obj_ellispe3p.Size = new Size(224, 48);
			menu_obj_ellispe3p.Text = "Ellipse 3-points";
			// 
			// menu_obj_ellipse5p
			// 
			menu_obj_ellipse5p.AutoSize = false;
			menu_obj_ellipse5p.Image = Properties.Resources.ellipse5p;
			menu_obj_ellipse5p.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_ellipse5p.Name = "menu_obj_ellipse5p";
			menu_obj_ellipse5p.Size = new Size(224, 48);
			menu_obj_ellipse5p.Text = "Ellipse 5-points";
			// 
			// menu_obj_poly
			// 
			menu_obj_poly.AutoSize = false;
			menu_obj_poly.Image = Properties.Resources.poly;
			menu_obj_poly.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_poly.Name = "menu_obj_poly";
			menu_obj_poly.Size = new Size(224, 48);
			menu_obj_poly.Text = "Polygon";
			// 
			// menu_obj_text
			// 
			menu_obj_text.AutoSize = false;
			menu_obj_text.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_text.Name = "menu_obj_text";
			menu_obj_text.Size = new Size(224, 48);
			menu_obj_text.Text = "Text";
			// 
			// menu_obj_image
			// 
			menu_obj_image.AutoSize = false;
			menu_obj_image.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_image.Name = "menu_obj_image";
			menu_obj_image.Size = new Size(224, 48);
			menu_obj_image.Text = "Image";
			// 
			// menu_obj_measurement_distance
			// 
			menu_obj_measurement_distance.AutoSize = false;
			menu_obj_measurement_distance.Image = Properties.Resources.measure_dist;
			menu_obj_measurement_distance.ImageScaling = ToolStripItemImageScaling.None;
			menu_obj_measurement_distance.Name = "menu_obj_measurement_distance";
			menu_obj_measurement_distance.Size = new Size(224, 48);
			menu_obj_measurement_distance.Text = "Measure distance";
			// 
			// measureAngleToolStripMenuItem
			// 
			measureAngleToolStripMenuItem.Image = Properties.Resources.measure_angle;
			measureAngleToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
			measureAngleToolStripMenuItem.Name = "measureAngleToolStripMenuItem";
			measureAngleToolStripMenuItem.Size = new Size(246, 50);
			measureAngleToolStripMenuItem.Text = "Measure angle";
			// 
			// customShapesToolStripMenuItem
			// 
			customShapesToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { menu_obj_add_to_custom_shapes, menu_obj_import_vdrs, menu_obj_import_visio });
			customShapesToolStripMenuItem.Name = "customShapesToolStripMenuItem";
			customShapesToolStripMenuItem.Size = new Size(246, 50);
			customShapesToolStripMenuItem.Text = "Custom shapes";
			// 
			// menu_obj_add_to_custom_shapes
			// 
			menu_obj_add_to_custom_shapes.Name = "menu_obj_add_to_custom_shapes";
			menu_obj_add_to_custom_shapes.Size = new Size(327, 26);
			menu_obj_add_to_custom_shapes.Text = "Add selection to custom shape";
			// 
			// menu_obj_import_vdrs
			// 
			menu_obj_import_vdrs.Name = "menu_obj_import_vdrs";
			menu_obj_import_vdrs.Size = new Size(327, 26);
			menu_obj_import_vdrs.Text = "Import custom shapes (*.vdrs)";
			// 
			// menu_obj_import_visio
			// 
			menu_obj_import_visio.Name = "menu_obj_import_visio";
			menu_obj_import_visio.Size = new Size(327, 26);
			menu_obj_import_visio.Text = "Import Visio file (*.vsdx, *.vss, *.vssx)";
			// 
			// toolStripSeparator1
			// 
			toolStripSeparator1.Name = "toolStripSeparator1";
			toolStripSeparator1.Size = new Size(243, 6);
			// 
			// convertToCurveToolStripMenuItem
			// 
			convertToCurveToolStripMenuItem.AutoSize = false;
			convertToCurveToolStripMenuItem.Image = Properties.Resources.convert_to_curve;
			convertToCurveToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
			convertToCurveToolStripMenuItem.Name = "convertToCurveToolStripMenuItem";
			convertToCurveToolStripMenuItem.Size = new Size(224, 48);
			convertToCurveToolStripMenuItem.Text = "Convert to curve";
			// 
			// helpToolStripMenuItem
			// 
			helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { menu_about, menu_manual });
			helpToolStripMenuItem.Name = "helpToolStripMenuItem";
			helpToolStripMenuItem.Size = new Size(55, 24);
			helpToolStripMenuItem.Text = "Help";
			// 
			// menu_about
			// 
			menu_about.Name = "menu_about";
			menu_about.Size = new Size(174, 26);
			menu_about.Text = "About ...";
			menu_about.Click += menu_about_Click;
			// 
			// menu_manual
			// 
			menu_manual.Name = "menu_manual";
			menu_manual.Size = new Size(174, 26);
			menu_manual.Text = "User manual";
			menu_manual.Click += menu_manual_Click;
			// 
			// page_view_flow
			// 
			page_view_flow.Controls.Add(page_print_borders_but);
			page_view_flow.Controls.Add(page_grid_but);
			page_view_flow.Dock = DockStyle.Left;
			page_view_flow.Location = new Point(846, 0);
			page_view_flow.Name = "page_view_flow";
			page_view_flow.Padding = new Padding(2, 2, 0, 0);
			page_view_flow.Size = new Size(117, 58);
			page_view_flow.TabIndex = 8;
			// 
			// page_print_borders_but
			// 
			page_print_borders_but.Anchor = AnchorStyles.Left;
			page_print_borders_but.Location = new Point(5, 5);
			page_print_borders_but.Name = "page_print_borders_but";
			page_print_borders_but.Size = new Size(48, 48);
			page_print_borders_but.TabIndex = 8;
			page_print_borders_but.Text = "PB";
			page_print_borders_but.UseVisualStyleBackColor = true;
			page_print_borders_but.Click += page_print_borders_but_Click;
			// 
			// page_grid_but
			// 
			page_grid_but.Anchor = AnchorStyles.Left;
			page_grid_but.Location = new Point(59, 5);
			page_grid_but.Name = "page_grid_but";
			page_grid_but.Size = new Size(48, 48);
			page_grid_but.TabIndex = 9;
			page_grid_but.Text = "G";
			page_grid_but.UseVisualStyleBackColor = true;
			page_grid_but.Click += page_grid_but_Click;
			// 
			// page_zero_flow
			// 
			page_zero_flow.Controls.Add(label4);
			page_zero_flow.Controls.Add(page_zero_x_nud);
			page_zero_flow.Controls.Add(label5);
			page_zero_flow.Controls.Add(page_zero_y_nud);
			page_zero_flow.Dock = DockStyle.Left;
			page_zero_flow.Location = new Point(679, 0);
			page_zero_flow.MinimumSize = new Size(167, 65);
			page_zero_flow.Name = "page_zero_flow";
			page_zero_flow.Size = new Size(167, 65);
			page_zero_flow.TabIndex = 7;
			// 
			// label4
			// 
			label4.AutoSize = true;
			label4.Location = new Point(3, 5);
			label4.Margin = new Padding(3, 5, 3, 0);
			label4.Name = "label4";
			label4.Size = new Size(53, 20);
			label4.TabIndex = 6;
			label4.Text = "Zero X";
			// 
			// page_zero_x_nud
			// 
			page_zero_x_nud.DecimalPlaces = 2;
			page_zero_x_nud.Location = new Point(62, 3);
			page_zero_x_nud.Margin = new Padding(3, 3, 3, 0);
			page_zero_x_nud.Name = "page_zero_x_nud";
			page_zero_x_nud.Size = new Size(96, 27);
			page_zero_x_nud.TabIndex = 4;
			page_zero_x_nud.ValueChanged += page_zero_x_nud_ValueChanged;
			// 
			// label5
			// 
			label5.Location = new Point(3, 33);
			label5.Margin = new Padding(3, 3, 3, 0);
			label5.Name = "label5";
			label5.Size = new Size(53, 20);
			label5.TabIndex = 7;
			label5.Text = "Zero Y";
			// 
			// page_zero_y_nud
			// 
			page_zero_y_nud.DecimalPlaces = 2;
			page_zero_y_nud.Location = new Point(62, 30);
			page_zero_y_nud.Margin = new Padding(3, 0, 3, 3);
			page_zero_y_nud.Name = "page_zero_y_nud";
			page_zero_y_nud.Size = new Size(96, 27);
			page_zero_y_nud.TabIndex = 5;
			page_zero_y_nud.ValueChanged += page_zero_y_nud_ValueChanged;
			// 
			// page_layout_flow
			// 
			page_layout_flow.Controls.Add(page_vertical_but);
			page_layout_flow.Controls.Add(page_horizontal_but);
			page_layout_flow.Controls.Add(label3);
			page_layout_flow.Controls.Add(page_uom_combo);
			page_layout_flow.Controls.Add(page_background_color_but);
			page_layout_flow.Dock = DockStyle.Left;
			page_layout_flow.Location = new Point(163, 0);
			page_layout_flow.Name = "page_layout_flow";
			page_layout_flow.Padding = new Padding(2, 2, 0, 0);
			page_layout_flow.Size = new Size(322, 58);
			page_layout_flow.TabIndex = 6;
			// 
			// page_vertical_but
			// 
			page_vertical_but.Location = new Point(5, 5);
			page_vertical_but.Name = "page_vertical_but";
			page_vertical_but.Size = new Size(48, 48);
			page_vertical_but.TabIndex = 8;
			page_vertical_but.Text = "V";
			page_vertical_but.UseVisualStyleBackColor = true;
			page_vertical_but.Click += page_vertical_but_Click;
			// 
			// page_horizontal_but
			// 
			page_horizontal_but.Location = new Point(59, 5);
			page_horizontal_but.Name = "page_horizontal_but";
			page_horizontal_but.Size = new Size(48, 48);
			page_horizontal_but.TabIndex = 9;
			page_horizontal_but.Text = "H";
			page_horizontal_but.UseVisualStyleBackColor = true;
			page_horizontal_but.Click += page_horizontal_but_Click;
			// 
			// label3
			// 
			label3.Anchor = AnchorStyles.Left;
			label3.AutoSize = true;
			label3.Location = new Point(113, 21);
			label3.Margin = new Padding(3, 5, 3, 0);
			label3.Name = "label3";
			label3.Size = new Size(45, 20);
			label3.TabIndex = 3;
			label3.Text = "Units:";
			// 
			// page_uom_combo
			// 
			page_uom_combo.Anchor = AnchorStyles.Left;
			page_uom_combo.AutoCompleteMode = AutoCompleteMode.Suggest;
			page_uom_combo.AutoCompleteSource = AutoCompleteSource.ListItems;
			page_uom_combo.Location = new Point(164, 15);
			page_uom_combo.MinimumSize = new Size(99, 0);
			page_uom_combo.Name = "page_uom_combo";
			page_uom_combo.Size = new Size(99, 28);
			page_uom_combo.TabIndex = 7;
			page_uom_combo.SelectedIndexChanged += page_uom_combo_SelectedIndexChanged;
			// 
			// page_background_color_but
			// 
			page_background_color_but.BackColor = Color.Transparent;
			page_background_color_but.BackgroundImage = Properties.Resources.transparentbg_mini;
			page_background_color_but.Location = new Point(269, 5);
			page_background_color_but.MinimumSize = new Size(48, 48);
			page_background_color_but.Name = "page_background_color_but";
			page_background_color_but.Size = new Size(48, 48);
			page_background_color_but.TabIndex = 10;
			page_background_color_but.Click += page_background_color_but_Click;
			// 
			// page_lock_ratio_flow
			// 
			page_lock_ratio_flow.Controls.Add(page_lock_ratio_but);
			page_lock_ratio_flow.Dock = DockStyle.Left;
			page_lock_ratio_flow.Location = new Point(619, 0);
			page_lock_ratio_flow.Margin = new Padding(0);
			page_lock_ratio_flow.Name = "page_lock_ratio_flow";
			page_lock_ratio_flow.Padding = new Padding(2, 2, 0, 0);
			page_lock_ratio_flow.Size = new Size(60, 58);
			page_lock_ratio_flow.TabIndex = 1;
			// 
			// page_lock_ratio_but
			// 
			page_lock_ratio_but.Location = new Point(5, 5);
			page_lock_ratio_but.Name = "page_lock_ratio_but";
			page_lock_ratio_but.Size = new Size(48, 48);
			page_lock_ratio_but.TabIndex = 10;
			page_lock_ratio_but.Text = "LR";
			page_lock_ratio_but.UseVisualStyleBackColor = true;
			page_lock_ratio_but.Click += page_lock_ratio_but_Click;
			// 
			// page_size_flow
			// 
			page_size_flow.Controls.Add(label1);
			page_size_flow.Controls.Add(page_size_w_nud);
			page_size_flow.Controls.Add(label2);
			page_size_flow.Controls.Add(page_size_h_nud);
			page_size_flow.Dock = DockStyle.Left;
			page_size_flow.Location = new Point(485, 0);
			page_size_flow.Margin = new Padding(0);
			page_size_flow.MinimumSize = new Size(134, 65);
			page_size_flow.Name = "page_size_flow";
			page_size_flow.Size = new Size(134, 65);
			page_size_flow.TabIndex = 0;
			// 
			// label1
			// 
			label1.AutoSize = true;
			label1.Location = new Point(3, 5);
			label1.Margin = new Padding(3, 5, 3, 0);
			label1.Name = "label1";
			label1.Size = new Size(23, 20);
			label1.TabIndex = 2;
			label1.Text = "W";
			// 
			// page_size_w_nud
			// 
			page_size_w_nud.DecimalPlaces = 2;
			page_size_w_nud.Location = new Point(32, 3);
			page_size_w_nud.Margin = new Padding(3, 3, 3, 0);
			page_size_w_nud.Name = "page_size_w_nud";
			page_size_w_nud.Size = new Size(96, 27);
			page_size_w_nud.TabIndex = 0;
			page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
			// 
			// label2
			// 
			label2.AutoSize = true;
			label2.Location = new Point(3, 33);
			label2.Margin = new Padding(3, 3, 3, 0);
			label2.Name = "label2";
			label2.Size = new Size(24, 20);
			label2.TabIndex = 3;
			label2.Text = "H ";
			// 
			// page_size_h_nud
			// 
			page_size_h_nud.DecimalPlaces = 2;
			page_size_h_nud.Location = new Point(33, 30);
			page_size_h_nud.Margin = new Padding(3, 0, 3, 3);
			page_size_h_nud.Name = "page_size_h_nud";
			page_size_h_nud.Size = new Size(96, 27);
			page_size_h_nud.TabIndex = 1;
			page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;
			// 
			// page_paper_flow
			// 
			page_paper_flow.Controls.Add(page_paper_size_combo);
			page_paper_flow.Controls.Add(page_props_but);
			page_paper_flow.Dock = DockStyle.Left;
			page_paper_flow.Location = new Point(0, 0);
			page_paper_flow.MinimumSize = new Size(163, 65);
			page_paper_flow.Name = "page_paper_flow";
			page_paper_flow.Padding = new Padding(2, 2, 0, 0);
			page_paper_flow.Size = new Size(163, 65);
			page_paper_flow.TabIndex = 4;
			// 
			// page_paper_size_combo
			// 
			page_paper_size_combo.Anchor = AnchorStyles.Left;
			page_paper_size_combo.AutoCompleteMode = AutoCompleteMode.Suggest;
			page_paper_size_combo.AutoCompleteSource = AutoCompleteSource.ListItems;
			page_paper_size_combo.Location = new Point(5, 15);
			page_paper_size_combo.Name = "page_paper_size_combo";
			page_paper_size_combo.Size = new Size(95, 28);
			page_paper_size_combo.TabIndex = 6;
			page_paper_size_combo.SelectedIndexChanged += page_paper_size_combo_SelectedIndexChanged;
			// 
			// page_props_but
			// 
			page_props_but.Anchor = AnchorStyles.Left;
			page_props_but.Location = new Point(106, 5);
			page_props_but.Name = "page_props_but";
			page_props_but.Size = new Size(48, 48);
			page_props_but.TabIndex = 7;
			page_props_but.Text = "Prop";
			page_props_but.UseVisualStyleBackColor = true;
			page_props_but.Click += page_props_but_Click;
			// 
			// s_scissors_flow
			// 
			s_scissors_flow.Controls.Add(s_scissors_line_but);
			s_scissors_flow.Controls.Add(s_scissors_curve_but);
			s_scissors_flow.Controls.Add(s_cap_scissors_but);
			s_scissors_flow.Dock = DockStyle.Left;
			s_scissors_flow.Location = new Point(669, 0);
			s_scissors_flow.Name = "s_scissors_flow";
			s_scissors_flow.Padding = new Padding(2, 2, 0, 0);
			s_scissors_flow.Size = new Size(177, 58);
			s_scissors_flow.TabIndex = 4;
			// 
			// s_scissors_line_but
			// 
			s_scissors_line_but.Image = Properties.Resources.scissors_line;
			s_scissors_line_but.Location = new Point(5, 5);
			s_scissors_line_but.MinimumSize = new Size(48, 48);
			s_scissors_line_but.Name = "s_scissors_line_but";
			s_scissors_line_but.Size = new Size(48, 48);
			s_scissors_line_but.TabIndex = 19;
			s_scissors_line_but.UseVisualStyleBackColor = true;
			s_scissors_line_but.Click += s_scissors_line_but_Click;
			// 
			// s_scissors_curve_but
			// 
			s_scissors_curve_but.Image = Properties.Resources.scissors_curve1;
			s_scissors_curve_but.Location = new Point(59, 5);
			s_scissors_curve_but.MinimumSize = new Size(48, 48);
			s_scissors_curve_but.Name = "s_scissors_curve_but";
			s_scissors_curve_but.Size = new Size(48, 48);
			s_scissors_curve_but.TabIndex = 20;
			s_scissors_curve_but.UseVisualStyleBackColor = true;
			s_scissors_curve_but.Click += s_scissors_curve_but_Click;
			// 
			// s_cap_scissors_but
			// 
			s_cap_scissors_but.Image = Properties.Resources.scissors_cap;
			s_cap_scissors_but.Location = new Point(113, 5);
			s_cap_scissors_but.MinimumSize = new Size(48, 48);
			s_cap_scissors_but.Name = "s_cap_scissors_but";
			s_cap_scissors_but.Size = new Size(48, 48);
			s_cap_scissors_but.TabIndex = 21;
			s_cap_scissors_but.UseVisualStyleBackColor = true;
			s_cap_scissors_but.Click += s_cap_scissors_but_Click;
			// 
			// s_colors_flow
			// 
			s_colors_flow.Controls.Add(s_line_color_but);
			s_colors_flow.Controls.Add(s_fill_color_but);
			s_colors_flow.Dock = DockStyle.Left;
			s_colors_flow.Location = new Point(558, 0);
			s_colors_flow.Name = "s_colors_flow";
			s_colors_flow.Padding = new Padding(2, 2, 0, 0);
			s_colors_flow.Size = new Size(111, 58);
			s_colors_flow.TabIndex = 3;
			// 
			// s_line_color_but
			// 
			s_line_color_but.BackColor = Color.Transparent;
			s_line_color_but.BackgroundImage = Properties.Resources.transparentbg_mini;
			s_line_color_but.Location = new Point(5, 5);
			s_line_color_but.MinimumSize = new Size(48, 48);
			s_line_color_but.Name = "s_line_color_but";
			s_line_color_but.Size = new Size(48, 48);
			s_line_color_but.TabIndex = 9;
			s_line_color_but.Click += s_line_color_but_Click;
			// 
			// s_fill_color_but
			// 
			s_fill_color_but.BackColor = Color.Transparent;
			s_fill_color_but.BackgroundImage = Properties.Resources.transparentbg_mini;
			s_fill_color_but.Location = new Point(59, 5);
			s_fill_color_but.MinimumSize = new Size(48, 48);
			s_fill_color_but.Name = "s_fill_color_but";
			s_fill_color_but.Size = new Size(48, 48);
			s_fill_color_but.TabIndex = 10;
			s_fill_color_but.Click += s_fill_color_but_Click;
			// 
			// s_layout_flow
			// 
			s_layout_flow.Controls.Add(s_send_back_but);
			s_layout_flow.Controls.Add(s_bring_to_front_but);
			s_layout_flow.Controls.Add(s_group_but);
			s_layout_flow.Controls.Add(s_ungroup_but);
			s_layout_flow.Dock = DockStyle.Left;
			s_layout_flow.Location = new Point(331, 0);
			s_layout_flow.Name = "s_layout_flow";
			s_layout_flow.Padding = new Padding(2, 2, 0, 0);
			s_layout_flow.Size = new Size(227, 58);
			s_layout_flow.TabIndex = 2;
			// 
			// s_send_back_but
			// 
			s_send_back_but.Location = new Point(5, 5);
			s_send_back_but.MinimumSize = new Size(48, 48);
			s_send_back_but.Name = "s_send_back_but";
			s_send_back_but.Size = new Size(48, 48);
			s_send_back_but.TabIndex = 12;
			s_send_back_but.Text = "SB";
			s_send_back_but.UseVisualStyleBackColor = true;
			s_send_back_but.Click += s_send_back_but_Click;
			// 
			// s_bring_to_front_but
			// 
			s_bring_to_front_but.Location = new Point(59, 5);
			s_bring_to_front_but.MinimumSize = new Size(48, 48);
			s_bring_to_front_but.Name = "s_bring_to_front_but";
			s_bring_to_front_but.Size = new Size(48, 48);
			s_bring_to_front_but.TabIndex = 11;
			s_bring_to_front_but.Text = "BF";
			s_bring_to_front_but.UseVisualStyleBackColor = true;
			s_bring_to_front_but.Click += s_bring_to_front_but_Click;
			// 
			// s_group_but
			// 
			s_group_but.Location = new Point(113, 5);
			s_group_but.MinimumSize = new Size(48, 48);
			s_group_but.Name = "s_group_but";
			s_group_but.Size = new Size(48, 48);
			s_group_but.TabIndex = 7;
			s_group_but.Text = "Group";
			s_group_but.UseVisualStyleBackColor = true;
			s_group_but.Click += s_group_but_Click;
			// 
			// s_ungroup_but
			// 
			s_ungroup_but.Location = new Point(167, 5);
			s_ungroup_but.MinimumSize = new Size(48, 48);
			s_ungroup_but.Name = "s_ungroup_but";
			s_ungroup_but.Size = new Size(48, 48);
			s_ungroup_but.TabIndex = 8;
			s_ungroup_but.Text = "Ungroup";
			s_ungroup_but.UseVisualStyleBackColor = true;
			s_ungroup_but.Click += s_ungroup_but_Click;
			// 
			// s_boolean_tools_flow
			// 
			s_boolean_tools_flow.Controls.Add(s_convert_to_curve_but);
			s_boolean_tools_flow.Controls.Add(s_combine_but);
			s_boolean_tools_flow.Controls.Add(s_union_but);
			s_boolean_tools_flow.Controls.Add(s_substract_but);
			s_boolean_tools_flow.Controls.Add(s_intersect_but);
			s_boolean_tools_flow.Controls.Add(s_xor_but);
			s_boolean_tools_flow.Dock = DockStyle.Left;
			s_boolean_tools_flow.Location = new Point(0, 0);
			s_boolean_tools_flow.Name = "s_boolean_tools_flow";
			s_boolean_tools_flow.Padding = new Padding(2, 2, 0, 0);
			s_boolean_tools_flow.Size = new Size(331, 58);
			s_boolean_tools_flow.TabIndex = 1;
			// 
			// s_convert_to_curve_but
			// 
			s_convert_to_curve_but.Image = Properties.Resources.convert_to_curve;
			s_convert_to_curve_but.Location = new Point(5, 5);
			s_convert_to_curve_but.MinimumSize = new Size(48, 48);
			s_convert_to_curve_but.Name = "s_convert_to_curve_but";
			s_convert_to_curve_but.Size = new Size(48, 48);
			s_convert_to_curve_but.TabIndex = 18;
			s_convert_to_curve_but.UseVisualStyleBackColor = true;
			s_convert_to_curve_but.Click += s_convert_to_curve_but_Click;
			// 
			// s_combine_but
			// 
			s_combine_but.Image = Properties.Resources.Combine_curve;
			s_combine_but.Location = new Point(59, 5);
			s_combine_but.MinimumSize = new Size(48, 48);
			s_combine_but.Name = "s_combine_but";
			s_combine_but.Size = new Size(48, 48);
			s_combine_but.TabIndex = 19;
			s_combine_but.UseVisualStyleBackColor = true;
			s_combine_but.Click += s_combine_but_Click;
			// 
			// s_union_but
			// 
			s_union_but.Image = Properties.Resources.union_curve;
			s_union_but.Location = new Point(113, 5);
			s_union_but.MinimumSize = new Size(48, 48);
			s_union_but.Name = "s_union_but";
			s_union_but.Size = new Size(48, 48);
			s_union_but.TabIndex = 13;
			s_union_but.UseVisualStyleBackColor = true;
			s_union_but.Click += s_union_but_Click;
			// 
			// s_substract_but
			// 
			s_substract_but.Image = Properties.Resources.substract_curve;
			s_substract_but.Location = new Point(167, 5);
			s_substract_but.MinimumSize = new Size(48, 48);
			s_substract_but.Name = "s_substract_but";
			s_substract_but.Size = new Size(48, 48);
			s_substract_but.TabIndex = 17;
			s_substract_but.UseVisualStyleBackColor = true;
			s_substract_but.Click += s_substract_but_Click;
			// 
			// s_intersect_but
			// 
			s_intersect_but.Image = Properties.Resources.intersect_curve;
			s_intersect_but.Location = new Point(221, 5);
			s_intersect_but.MinimumSize = new Size(48, 48);
			s_intersect_but.Name = "s_intersect_but";
			s_intersect_but.Size = new Size(48, 48);
			s_intersect_but.TabIndex = 16;
			s_intersect_but.UseVisualStyleBackColor = true;
			s_intersect_but.Click += s_intersect_but_Click;
			// 
			// s_xor_but
			// 
			s_xor_but.Image = Properties.Resources.xor_curve;
			s_xor_but.Location = new Point(275, 5);
			s_xor_but.MinimumSize = new Size(48, 48);
			s_xor_but.Name = "s_xor_but";
			s_xor_but.Size = new Size(48, 48);
			s_xor_but.TabIndex = 15;
			s_xor_but.UseVisualStyleBackColor = true;
			s_xor_but.Click += s_exclude_xor_but_Click;
			// 
			// i_tools_settings_flow
			// 
			i_tools_settings_flow.Controls.Add(label10);
			i_tools_settings_flow.Controls.Add(i_tool_shape_combo);
			i_tools_settings_flow.Controls.Add(label6);
			i_tools_settings_flow.Controls.Add(i_tool_size_nud);
			i_tools_settings_flow.Controls.Add(label7);
			i_tools_settings_flow.Controls.Add(i_tool_tolerance_nud);
			i_tools_settings_flow.Controls.Add(label8);
			i_tools_settings_flow.Controls.Add(i_tool_strength_nud);
			i_tools_settings_flow.Controls.Add(label9);
			i_tools_settings_flow.Controls.Add(i_tool_smooth_nud);
			i_tools_settings_flow.Dock = DockStyle.Left;
			i_tools_settings_flow.FlowDirection = FlowDirection.TopDown;
			i_tools_settings_flow.Location = new Point(605, 0);
			i_tools_settings_flow.Name = "i_tools_settings_flow";
			i_tools_settings_flow.Padding = new Padding(5, 0, 0, 0);
			i_tools_settings_flow.Size = new Size(446, 59);
			i_tools_settings_flow.TabIndex = 5;
			// 
			// label10
			// 
			label10.AutoSize = true;
			label10.Location = new Point(8, 0);
			label10.Name = "label10";
			label10.Padding = new Padding(0, 0, 0, 3);
			label10.Size = new Size(53, 23);
			label10.TabIndex = 8;
			label10.Text = "Shape:";
			// 
			// i_tool_shape_combo
			// 
			i_tool_shape_combo.Dock = DockStyle.Left;
			i_tool_shape_combo.FormattingEnabled = true;
			i_tool_shape_combo.Items.AddRange(new object[] { "Circle", "Square" });
			i_tool_shape_combo.Location = new Point(8, 26);
			i_tool_shape_combo.Name = "i_tool_shape_combo";
			i_tool_shape_combo.Size = new Size(102, 28);
			i_tool_shape_combo.TabIndex = 9;
			i_tool_shape_combo.SelectedIndexChanged += i_tool_shape_combo_SelectedIndexChanged;
			// 
			// label6
			// 
			label6.AutoSize = true;
			label6.Location = new Point(116, 0);
			label6.Name = "label6";
			label6.Padding = new Padding(0, 0, 0, 3);
			label6.Size = new Size(39, 23);
			label6.TabIndex = 1;
			label6.Text = "Size:";
			// 
			// i_tool_size_nud
			// 
			i_tool_size_nud.DecimalPlaces = 2;
			i_tool_size_nud.Location = new Point(116, 26);
			i_tool_size_nud.Name = "i_tool_size_nud";
			i_tool_size_nud.Size = new Size(76, 27);
			i_tool_size_nud.TabIndex = 0;
			i_tool_size_nud.ValueChanged += i_tool_size_nud_ValueChanged;
			// 
			// label7
			// 
			label7.AutoSize = true;
			label7.Location = new Point(198, 0);
			label7.Name = "label7";
			label7.Padding = new Padding(0, 0, 0, 3);
			label7.Size = new Size(76, 23);
			label7.TabIndex = 3;
			label7.Text = "Tolerance:";
			// 
			// i_tool_tolerance_nud
			// 
			i_tool_tolerance_nud.DecimalPlaces = 2;
			i_tool_tolerance_nud.Dock = DockStyle.Left;
			i_tool_tolerance_nud.Location = new Point(198, 26);
			i_tool_tolerance_nud.Name = "i_tool_tolerance_nud";
			i_tool_tolerance_nud.Size = new Size(76, 27);
			i_tool_tolerance_nud.TabIndex = 2;
			i_tool_tolerance_nud.ValueChanged += i_tool_tolerance_nud_ValueChanged;
			// 
			// label8
			// 
			label8.AutoSize = true;
			label8.Location = new Point(280, 0);
			label8.Name = "label8";
			label8.Padding = new Padding(0, 0, 0, 3);
			label8.Size = new Size(68, 23);
			label8.TabIndex = 5;
			label8.Text = "Strength:";
			// 
			// i_tool_strength_nud
			// 
			i_tool_strength_nud.DecimalPlaces = 2;
			i_tool_strength_nud.Dock = DockStyle.Left;
			i_tool_strength_nud.Location = new Point(280, 26);
			i_tool_strength_nud.Name = "i_tool_strength_nud";
			i_tool_strength_nud.Size = new Size(76, 27);
			i_tool_strength_nud.TabIndex = 4;
			i_tool_strength_nud.ValueChanged += i_tool_strength_nud_ValueChanged;
			// 
			// label9
			// 
			label9.AutoSize = true;
			label9.Location = new Point(362, 0);
			label9.Name = "label9";
			label9.Padding = new Padding(0, 0, 0, 3);
			label9.Size = new Size(64, 23);
			label9.TabIndex = 7;
			label9.Text = "Smooth:";
			// 
			// i_tool_smooth_nud
			// 
			i_tool_smooth_nud.DecimalPlaces = 2;
			i_tool_smooth_nud.Dock = DockStyle.Left;
			i_tool_smooth_nud.Location = new Point(362, 26);
			i_tool_smooth_nud.Name = "i_tool_smooth_nud";
			i_tool_smooth_nud.Size = new Size(76, 27);
			i_tool_smooth_nud.TabIndex = 6;
			i_tool_smooth_nud.ValueChanged += i_tool_smooth_nud_ValueChanged;
			// 
			// i_colors_flow
			// 
			i_colors_flow.Controls.Add(i_fore_color_but);
			i_colors_flow.Controls.Add(i_back_color_but);
			i_colors_flow.Dock = DockStyle.Left;
			i_colors_flow.Location = new Point(494, 0);
			i_colors_flow.Name = "i_colors_flow";
			i_colors_flow.Padding = new Padding(2, 2, 0, 0);
			i_colors_flow.Size = new Size(111, 59);
			i_colors_flow.TabIndex = 4;
			// 
			// i_fore_color_but
			// 
			i_fore_color_but.BackColor = Color.Transparent;
			i_fore_color_but.BackgroundImage = Properties.Resources.transparentbg_mini;
			i_fore_color_but.Location = new Point(5, 5);
			i_fore_color_but.Name = "i_fore_color_but";
			i_fore_color_but.Size = new Size(48, 48);
			i_fore_color_but.TabIndex = 9;
			i_fore_color_but.Click += i_fore_color_but_Click;
			// 
			// i_back_color_but
			// 
			i_back_color_but.BackColor = Color.Transparent;
			i_back_color_but.BackgroundImage = Properties.Resources.transparentbg_mini;
			i_back_color_but.Location = new Point(59, 5);
			i_back_color_but.Name = "i_back_color_but";
			i_back_color_but.Size = new Size(48, 48);
			i_back_color_but.TabIndex = 10;
			i_back_color_but.Click += i_back_color_but_Click;
			// 
			// flip_flow
			// 
			flip_flow.Controls.Add(rotate_CW_90_but);
			flip_flow.Controls.Add(rotate_CCW_90_but);
			flip_flow.Controls.Add(flip_horizontal_but);
			flip_flow.Controls.Add(flip_vertical_but);
			flip_flow.Dock = DockStyle.Left;
			flip_flow.Location = new Point(718, 0);
			flip_flow.Name = "flip_flow";
			flip_flow.Padding = new Padding(2, 2, 0, 0);
			flip_flow.Size = new Size(225, 58);
			flip_flow.TabIndex = 6;
			// 
			// rotate_CW_90_but
			// 
			rotate_CW_90_but.Location = new Point(5, 5);
			rotate_CW_90_but.Name = "rotate_CW_90_but";
			rotate_CW_90_but.Size = new Size(48, 48);
			rotate_CW_90_but.TabIndex = 35;
			rotate_CW_90_but.Text = "CW";
			rotate_CW_90_but.UseVisualStyleBackColor = true;
			rotate_CW_90_but.Click += rotate_CW_90_but_Click;
			// 
			// rotate_CCW_90_but
			// 
			rotate_CCW_90_but.Location = new Point(59, 5);
			rotate_CCW_90_but.Name = "rotate_CCW_90_but";
			rotate_CCW_90_but.Size = new Size(48, 48);
			rotate_CCW_90_but.TabIndex = 36;
			rotate_CCW_90_but.Text = "CCW";
			rotate_CCW_90_but.UseVisualStyleBackColor = true;
			rotate_CCW_90_but.Click += rotate_CCW_90_but_Click;
			// 
			// flip_horizontal_but
			// 
			flip_horizontal_but.Location = new Point(113, 5);
			flip_horizontal_but.Name = "flip_horizontal_but";
			flip_horizontal_but.Size = new Size(48, 48);
			flip_horizontal_but.TabIndex = 37;
			flip_horizontal_but.Text = "FH";
			flip_horizontal_but.UseVisualStyleBackColor = true;
			flip_horizontal_but.Click += flip_horizontal_but_Click;
			// 
			// flip_vertical_but
			// 
			flip_vertical_but.Location = new Point(167, 5);
			flip_vertical_but.Name = "flip_vertical_but";
			flip_vertical_but.Size = new Size(48, 48);
			flip_vertical_but.TabIndex = 38;
			flip_vertical_but.Text = "FV";
			flip_vertical_but.UseVisualStyleBackColor = true;
			flip_vertical_but.Click += flip_vertical_but_Click;
			// 
			// image_tools_flow
			// 
			image_tools_flow.Controls.Add(i_select_rect_but);
			image_tools_flow.Controls.Add(i_select_lasso_but);
			image_tools_flow.Controls.Add(i_brush_but);
			image_tools_flow.Controls.Add(i_erasor_but);
			image_tools_flow.Controls.Add(i_light_but);
			image_tools_flow.Controls.Add(i_dark_but);
			image_tools_flow.Controls.Add(i_fill_but);
			image_tools_flow.Controls.Add(i_contrast_but);
			image_tools_flow.Controls.Add(i_blackwhite_but);
			image_tools_flow.Dock = DockStyle.Left;
			image_tools_flow.Location = new Point(0, 0);
			image_tools_flow.Name = "image_tools_flow";
			image_tools_flow.Padding = new Padding(2, 2, 0, 0);
			image_tools_flow.Size = new Size(494, 59);
			image_tools_flow.TabIndex = 0;
			// 
			// i_select_rect_but
			// 
			i_select_rect_but.Location = new Point(5, 5);
			i_select_rect_but.Name = "i_select_rect_but";
			i_select_rect_but.Size = new Size(48, 48);
			i_select_rect_but.TabIndex = 26;
			i_select_rect_but.Text = "SR";
			i_select_rect_but.UseVisualStyleBackColor = true;
			i_select_rect_but.Click += i_select_rect_but_Click;
			// 
			// i_select_lasso_but
			// 
			i_select_lasso_but.Location = new Point(59, 5);
			i_select_lasso_but.Name = "i_select_lasso_but";
			i_select_lasso_but.Size = new Size(48, 48);
			i_select_lasso_but.TabIndex = 27;
			i_select_lasso_but.Text = "SL";
			i_select_lasso_but.UseVisualStyleBackColor = true;
			i_select_lasso_but.Click += i_select_lasso_but_Click;
			// 
			// i_brush_but
			// 
			i_brush_but.Location = new Point(113, 5);
			i_brush_but.Name = "i_brush_but";
			i_brush_but.Size = new Size(48, 48);
			i_brush_but.TabIndex = 25;
			i_brush_but.Text = "Br";
			i_brush_but.UseVisualStyleBackColor = true;
			i_brush_but.Click += i_brush_but_Click;
			// 
			// i_erasor_but
			// 
			i_erasor_but.Location = new Point(167, 5);
			i_erasor_but.Name = "i_erasor_but";
			i_erasor_but.Size = new Size(48, 48);
			i_erasor_but.TabIndex = 23;
			i_erasor_but.Text = "E";
			i_erasor_but.UseVisualStyleBackColor = true;
			i_erasor_but.Click += i_eraser_but_Click;
			// 
			// i_light_but
			// 
			i_light_but.Location = new Point(221, 5);
			i_light_but.Name = "i_light_but";
			i_light_but.Size = new Size(48, 48);
			i_light_but.TabIndex = 28;
			i_light_but.Text = "Li";
			i_light_but.UseVisualStyleBackColor = true;
			i_light_but.Click += i_light_but_Click;
			// 
			// i_dark_but
			// 
			i_dark_but.Location = new Point(275, 5);
			i_dark_but.Name = "i_dark_but";
			i_dark_but.Size = new Size(48, 48);
			i_dark_but.TabIndex = 29;
			i_dark_but.Text = "Da";
			i_dark_but.UseVisualStyleBackColor = true;
			i_dark_but.Click += i_dark_but_Click;
			// 
			// i_fill_but
			// 
			i_fill_but.Location = new Point(329, 5);
			i_fill_but.Name = "i_fill_but";
			i_fill_but.Size = new Size(48, 48);
			i_fill_but.TabIndex = 30;
			i_fill_but.Text = "FF";
			i_fill_but.UseVisualStyleBackColor = true;
			i_fill_but.Click += i_fill_but_Click;
			// 
			// i_contrast_but
			// 
			i_contrast_but.Location = new Point(383, 5);
			i_contrast_but.Name = "i_contrast_but";
			i_contrast_but.Size = new Size(48, 48);
			i_contrast_but.TabIndex = 31;
			i_contrast_but.Text = "Con";
			i_contrast_but.UseVisualStyleBackColor = true;
			i_contrast_but.Click += i_contrast_but_Click;
			// 
			// i_blackwhite_but
			// 
			i_blackwhite_but.Location = new Point(437, 5);
			i_blackwhite_but.Name = "i_blackwhite_but";
			i_blackwhite_but.Size = new Size(48, 48);
			i_blackwhite_but.TabIndex = 32;
			i_blackwhite_but.Text = "BW";
			i_blackwhite_but.UseVisualStyleBackColor = true;
			i_blackwhite_but.Click += i_blackwhite_but_Click;
			// 
			// c_nodes_tools_flow
			// 
			c_nodes_tools_flow.Controls.Add(nodes_merge_but);
			c_nodes_tools_flow.Controls.Add(nodes_split_but);
			c_nodes_tools_flow.Controls.Add(nodes_straight_but);
			c_nodes_tools_flow.Controls.Add(node_tangent_but);
			c_nodes_tools_flow.Controls.Add(node_sharp_but);
			c_nodes_tools_flow.Controls.Add(node_delete_but);
			c_nodes_tools_flow.Dock = DockStyle.Left;
			c_nodes_tools_flow.Location = new Point(846, 0);
			c_nodes_tools_flow.Name = "c_nodes_tools_flow";
			c_nodes_tools_flow.Padding = new Padding(2, 2, 0, 0);
			c_nodes_tools_flow.Size = new Size(334, 58);
			c_nodes_tools_flow.TabIndex = 0;
			// 
			// nodes_merge_but
			// 
			nodes_merge_but.Image = Properties.Resources.Nodes_merge;
			nodes_merge_but.Location = new Point(5, 5);
			nodes_merge_but.MinimumSize = new Size(48, 48);
			nodes_merge_but.Name = "nodes_merge_but";
			nodes_merge_but.Size = new Size(48, 48);
			nodes_merge_but.TabIndex = 10;
			nodes_merge_but.UseVisualStyleBackColor = true;
			nodes_merge_but.Click += nodes_merge_but_Click;
			// 
			// nodes_split_but
			// 
			nodes_split_but.Image = Properties.Resources.Nodes_split;
			nodes_split_but.Location = new Point(59, 5);
			nodes_split_but.MinimumSize = new Size(48, 48);
			nodes_split_but.Name = "nodes_split_but";
			nodes_split_but.Size = new Size(48, 48);
			nodes_split_but.TabIndex = 11;
			nodes_split_but.UseVisualStyleBackColor = true;
			nodes_split_but.Click += nodes_split_but_Click;
			// 
			// nodes_straight_but
			// 
			nodes_straight_but.Image = Properties.Resources.Nodes_straight;
			nodes_straight_but.Location = new Point(113, 5);
			nodes_straight_but.MinimumSize = new Size(48, 48);
			nodes_straight_but.Name = "nodes_straight_but";
			nodes_straight_but.Size = new Size(48, 48);
			nodes_straight_but.TabIndex = 12;
			nodes_straight_but.UseVisualStyleBackColor = true;
			nodes_straight_but.Click += nodes_straight_but_Click;
			// 
			// node_tangent_but
			// 
			node_tangent_but.Image = Properties.Resources.Node_tangent;
			node_tangent_but.Location = new Point(167, 5);
			node_tangent_but.MinimumSize = new Size(48, 48);
			node_tangent_but.Name = "node_tangent_but";
			node_tangent_but.Size = new Size(48, 48);
			node_tangent_but.TabIndex = 13;
			node_tangent_but.UseVisualStyleBackColor = true;
			node_tangent_but.Click += node_tangent_but_Click;
			// 
			// node_sharp_but
			// 
			node_sharp_but.Image = Properties.Resources.Node_sharp;
			node_sharp_but.Location = new Point(221, 5);
			node_sharp_but.MinimumSize = new Size(48, 48);
			node_sharp_but.Name = "node_sharp_but";
			node_sharp_but.Size = new Size(48, 48);
			node_sharp_but.TabIndex = 14;
			node_sharp_but.UseVisualStyleBackColor = true;
			node_sharp_but.Click += node_sharp_but_Click;
			// 
			// node_delete_but
			// 
			node_delete_but.Image = Properties.Resources.X;
			node_delete_but.Location = new Point(275, 5);
			node_delete_but.MinimumSize = new Size(48, 48);
			node_delete_but.Name = "node_delete_but";
			node_delete_but.Size = new Size(48, 48);
			node_delete_but.TabIndex = 16;
			node_delete_but.UseVisualStyleBackColor = true;
			node_delete_but.Click += node_delete_but_Click;
			// 
			// view_flow
			// 
			view_flow.Controls.Add(v_show_rulers_but);
			view_flow.Controls.Add(v_show_guidelines_but);
			view_flow.Controls.Add(v_snap_but);
			view_flow.Controls.Add(v_dynamic_guidelines_but);
			view_flow.Controls.Add(v_scale_combo);
			view_flow.Controls.Add(v_scale_selected_but);
			view_flow.Controls.Add(v_scale_all_but);
			view_flow.Controls.Add(v_scale_page_but);
			view_flow.Dock = DockStyle.Left;
			view_flow.Location = new Point(943, 0);
			view_flow.Name = "view_flow";
			view_flow.Padding = new Padding(2, 2, 0, 0);
			view_flow.Size = new Size(488, 58);
			view_flow.TabIndex = 5;
			// 
			// v_show_rulers_but
			// 
			v_show_rulers_but.Image = Properties.Resources.rulers;
			v_show_rulers_but.Location = new Point(5, 5);
			v_show_rulers_but.Name = "v_show_rulers_but";
			v_show_rulers_but.Size = new Size(48, 48);
			v_show_rulers_but.TabIndex = 9;
			v_show_rulers_but.UseVisualStyleBackColor = true;
			v_show_rulers_but.Click += v_show_rulers_but_Click;
			// 
			// v_show_guidelines_but
			// 
			v_show_guidelines_but.Image = Properties.Resources.guidelines;
			v_show_guidelines_but.Location = new Point(59, 5);
			v_show_guidelines_but.Name = "v_show_guidelines_but";
			v_show_guidelines_but.Size = new Size(48, 48);
			v_show_guidelines_but.TabIndex = 12;
			v_show_guidelines_but.UseVisualStyleBackColor = true;
			v_show_guidelines_but.Click += s_show_guidelines_but_Click;
			// 
			// v_snap_but
			// 
			v_snap_but.Image = Properties.Resources.snapping;
			v_snap_but.Location = new Point(113, 5);
			v_snap_but.Name = "v_snap_but";
			v_snap_but.Size = new Size(48, 48);
			v_snap_but.TabIndex = 10;
			v_snap_but.UseVisualStyleBackColor = true;
			v_snap_but.Click += v_snap_but_Click;
			// 
			// v_dynamic_guidelines_but
			// 
			v_dynamic_guidelines_but.Image = Properties.Resources.dynamic_guidelines;
			v_dynamic_guidelines_but.Location = new Point(167, 5);
			v_dynamic_guidelines_but.Name = "v_dynamic_guidelines_but";
			v_dynamic_guidelines_but.Size = new Size(48, 48);
			v_dynamic_guidelines_but.TabIndex = 11;
			v_dynamic_guidelines_but.UseVisualStyleBackColor = true;
			v_dynamic_guidelines_but.Click += v_dynamic_guidelines_but_Click;
			// 
			// v_scale_combo
			// 
			v_scale_combo.FormattingEnabled = true;
			v_scale_combo.Location = new Point(221, 5);
			v_scale_combo.MinimumSize = new Size(94, 0);
			v_scale_combo.Name = "v_scale_combo";
			v_scale_combo.Size = new Size(101, 28);
			v_scale_combo.TabIndex = 0;
			v_scale_combo.SelectedIndexChanged += v_scale_combo_SelectedIndexChanged;
			// 
			// v_scale_selected_but
			// 
			v_scale_selected_but.Location = new Point(328, 5);
			v_scale_selected_but.MinimumSize = new Size(48, 48);
			v_scale_selected_but.Name = "v_scale_selected_but";
			v_scale_selected_but.Size = new Size(48, 48);
			v_scale_selected_but.TabIndex = 10;
			v_scale_selected_but.Text = "VS";
			v_scale_selected_but.UseVisualStyleBackColor = true;
			v_scale_selected_but.Click += v_scale_selected_but_Click;
			// 
			// v_scale_all_but
			// 
			v_scale_all_but.Location = new Point(382, 5);
			v_scale_all_but.MinimumSize = new Size(48, 48);
			v_scale_all_but.Name = "v_scale_all_but";
			v_scale_all_but.Size = new Size(48, 48);
			v_scale_all_but.TabIndex = 13;
			v_scale_all_but.Text = "VA";
			v_scale_all_but.UseVisualStyleBackColor = true;
			v_scale_all_but.Click += v_scale_all_but_Click;
			// 
			// v_scale_page_but
			// 
			v_scale_page_but.Location = new Point(436, 5);
			v_scale_page_but.MinimumSize = new Size(48, 48);
			v_scale_page_but.Name = "v_scale_page_but";
			v_scale_page_but.Size = new Size(48, 48);
			v_scale_page_but.TabIndex = 14;
			v_scale_page_but.Text = "VP";
			v_scale_page_but.UseVisualStyleBackColor = true;
			v_scale_page_but.Click += v_scale_page_but_Click;
			// 
			// measure_distance_but
			// 
			measure_distance_but.Dock = DockStyle.Top;
			measure_distance_but.Image = Properties.Resources.measure_dist;
			measure_distance_but.Location = new Point(0, 626);
			measure_distance_but.Name = "measure_distance_but";
			measure_distance_but.Size = new Size(69, 48);
			measure_distance_but.TabIndex = 12;
			measure_distance_but.UseVisualStyleBackColor = true;
			measure_distance_but.Click += measure_distance_but_Click;
			// 
			// s_cursor_but
			// 
			s_cursor_but.Dock = DockStyle.Top;
			s_cursor_but.Location = new Point(0, 2);
			s_cursor_but.Name = "s_cursor_but";
			s_cursor_but.Size = new Size(69, 48);
			s_cursor_but.TabIndex = 0;
			s_cursor_but.Text = "Cursor";
			s_cursor_but.UseVisualStyleBackColor = true;
			s_cursor_but.Click += s_cursor_but_Click;
			// 
			// s_curve_edit_but
			// 
			s_curve_edit_but.Dock = DockStyle.Top;
			s_curve_edit_but.Image = Properties.Resources.curve_edit;
			s_curve_edit_but.Location = new Point(0, 50);
			s_curve_edit_but.Name = "s_curve_edit_but";
			s_curve_edit_but.Size = new Size(69, 48);
			s_curve_edit_but.TabIndex = 5;
			s_curve_edit_but.UseVisualStyleBackColor = true;
			s_curve_edit_but.Click += s_curve_edit_but_Click;
			// 
			// image_mode_but
			// 
			image_mode_but.Dock = DockStyle.Top;
			image_mode_but.Location = new Point(0, 98);
			image_mode_but.Name = "image_mode_but";
			image_mode_but.Size = new Size(69, 48);
			image_mode_but.TabIndex = 22;
			image_mode_but.Text = "I";
			image_mode_but.UseVisualStyleBackColor = true;
			image_mode_but.Click += s_image_mode_but_Click;
			// 
			// s_new_line_but
			// 
			s_new_line_but.Dock = DockStyle.Top;
			s_new_line_but.Image = Properties.Resources.lines;
			s_new_line_but.Location = new Point(0, 146);
			s_new_line_but.Name = "s_new_line_but";
			s_new_line_but.Size = new Size(69, 48);
			s_new_line_but.TabIndex = 12;
			s_new_line_but.UseVisualStyleBackColor = true;
			s_new_line_but.Click += s_new_line_but_Click;
			// 
			// s_new_curve_but
			// 
			s_new_curve_but.Dock = DockStyle.Top;
			s_new_curve_but.Image = Properties.Resources.curve;
			s_new_curve_but.Location = new Point(0, 194);
			s_new_curve_but.Name = "s_new_curve_but";
			s_new_curve_but.Size = new Size(69, 48);
			s_new_curve_but.TabIndex = 11;
			s_new_curve_but.UseVisualStyleBackColor = true;
			s_new_curve_but.Click += s_new_curve_but_Click;
			// 
			// s_new_rc_but
			// 
			s_new_rc_but.BackColor = SystemColors.Control;
			s_new_rc_but.Dock = DockStyle.Top;
			s_new_rc_but.ForeColor = SystemColors.ControlText;
			s_new_rc_but.Location = new Point(0, 242);
			s_new_rc_but.Name = "s_new_rc_but";
			s_new_rc_but.Padding = new Padding(8, 0, 28, 0);
			s_new_rc_but.Size = new Size(69, 48);
			s_new_rc_but.TabIndex = 20;
			// 
			// s_new_circle_but
			// 
			s_new_circle_but.BackColor = SystemColors.Control;
			s_new_circle_but.Dock = DockStyle.Top;
			s_new_circle_but.ForeColor = SystemColors.ControlText;
			s_new_circle_but.Location = new Point(0, 290);
			s_new_circle_but.Name = "s_new_circle_but";
			s_new_circle_but.Padding = new Padding(8, 0, 28, 0);
			s_new_circle_but.Size = new Size(69, 48);
			s_new_circle_but.TabIndex = 2;
			// 
			// s_new_ellipse_but
			// 
			s_new_ellipse_but.BackColor = SystemColors.Control;
			s_new_ellipse_but.Dock = DockStyle.Top;
			s_new_ellipse_but.ForeColor = SystemColors.ControlText;
			s_new_ellipse_but.Location = new Point(0, 338);
			s_new_ellipse_but.Name = "s_new_ellipse_but";
			s_new_ellipse_but.Padding = new Padding(8, 0, 28, 0);
			s_new_ellipse_but.Size = new Size(69, 48);
			s_new_ellipse_but.TabIndex = 21;
			// 
			// s_new_poly_but
			// 
			s_new_poly_but.Dock = DockStyle.Top;
			s_new_poly_but.Image = Properties.Resources.poly;
			s_new_poly_but.Location = new Point(0, 386);
			s_new_poly_but.Name = "s_new_poly_but";
			s_new_poly_but.Size = new Size(69, 48);
			s_new_poly_but.TabIndex = 16;
			s_new_poly_but.UseVisualStyleBackColor = true;
			s_new_poly_but.Click += s_new_poly_but_Click;
			// 
			// s_new_free_poly_but
			// 
			s_new_free_poly_but.Dock = DockStyle.Top;
			s_new_free_poly_but.Image = Properties.Resources.free_poly;
			s_new_free_poly_but.Location = new Point(0, 434);
			s_new_free_poly_but.Name = "s_new_free_poly_but";
			s_new_free_poly_but.Size = new Size(69, 48);
			s_new_free_poly_but.TabIndex = 19;
			s_new_free_poly_but.UseVisualStyleBackColor = true;
			s_new_free_poly_but.Click += s_new_free_poly_but_Click;
			// 
			// s_new_text_but
			// 
			s_new_text_but.Dock = DockStyle.Top;
			s_new_text_but.Location = new Point(0, 482);
			s_new_text_but.Name = "s_new_text_but";
			s_new_text_but.Size = new Size(69, 48);
			s_new_text_but.TabIndex = 17;
			s_new_text_but.Text = "T";
			s_new_text_but.UseVisualStyleBackColor = true;
			s_new_text_but.Click += s_new_text_but_Click;
			// 
			// s_new_image_but
			// 
			s_new_image_but.Dock = DockStyle.Top;
			s_new_image_but.Location = new Point(0, 530);
			s_new_image_but.Name = "s_new_image_but";
			s_new_image_but.Size = new Size(69, 48);
			s_new_image_but.TabIndex = 18;
			s_new_image_but.Text = "Im";
			s_new_image_but.UseVisualStyleBackColor = true;
			s_new_image_but.Click += s_new_image_but_Click;
			// 
			// drawing_toolbar_panel
			// 
			drawing_toolbar_panel.BackColor = SystemColors.Control;
			drawing_toolbar_panel.Controls.Add(measure_angle_but);
			drawing_toolbar_panel.Controls.Add(measure_distance_but);
			drawing_toolbar_panel.Controls.Add(s_morth_but);
			drawing_toolbar_panel.Controls.Add(s_new_image_but);
			drawing_toolbar_panel.Controls.Add(s_new_text_but);
			drawing_toolbar_panel.Controls.Add(s_new_free_poly_but);
			drawing_toolbar_panel.Controls.Add(s_new_poly_but);
			drawing_toolbar_panel.Controls.Add(s_new_ellipse_but);
			drawing_toolbar_panel.Controls.Add(s_new_circle_but);
			drawing_toolbar_panel.Controls.Add(s_new_rc_but);
			drawing_toolbar_panel.Controls.Add(s_new_curve_but);
			drawing_toolbar_panel.Controls.Add(s_new_line_but);
			drawing_toolbar_panel.Controls.Add(image_mode_but);
			drawing_toolbar_panel.Controls.Add(s_curve_edit_but);
			drawing_toolbar_panel.Controls.Add(s_cursor_but);
			drawing_toolbar_panel.Dock = DockStyle.Left;
			drawing_toolbar_panel.Location = new Point(0, 261);
			drawing_toolbar_panel.Name = "drawing_toolbar_panel";
			drawing_toolbar_panel.Padding = new Padding(0, 2, 0, 0);
			drawing_toolbar_panel.Size = new Size(69, 732);
			drawing_toolbar_panel.TabIndex = 2;
			// 
			// measure_angle_but
			// 
			measure_angle_but.Dock = DockStyle.Top;
			measure_angle_but.Image = Properties.Resources.measure_angle;
			measure_angle_but.Location = new Point(0, 674);
			measure_angle_but.Name = "measure_angle_but";
			measure_angle_but.Size = new Size(69, 48);
			measure_angle_but.TabIndex = 26;
			measure_angle_but.UseVisualStyleBackColor = true;
			measure_angle_but.Click += measure_angle_but_Click;
			// 
			// s_morth_but
			// 
			s_morth_but.Dock = DockStyle.Top;
			s_morth_but.Location = new Point(0, 578);
			s_morth_but.Name = "s_morth_but";
			s_morth_but.Size = new Size(69, 48);
			s_morth_but.TabIndex = 25;
			s_morth_but.Text = "Prop";
			s_morth_but.UseVisualStyleBackColor = true;
			s_morth_but.Click += s_transform_but_Click;
			// 
			// main_toolbar_panel
			// 
			main_toolbar_panel.BackColor = SystemColors.Control;
			main_toolbar_panel.Controls.Add(language_switch_but);
			main_toolbar_panel.Controls.Add(help_but);
			main_toolbar_panel.Controls.Add(view_flow);
			main_toolbar_panel.Controls.Add(flip_flow);
			main_toolbar_panel.Controls.Add(main_tools_flow);
			main_toolbar_panel.Controls.Add(doc_toolbar_flow);
			main_toolbar_panel.Dock = DockStyle.Top;
			main_toolbar_panel.Location = new Point(0, 28);
			main_toolbar_panel.Name = "main_toolbar_panel";
			main_toolbar_panel.Size = new Size(1599, 58);
			main_toolbar_panel.TabIndex = 3;
			// 
			// language_switch_but
			// 
			language_switch_but.Dock = DockStyle.Left;
			language_switch_but.Location = new Point(1479, 0);
			language_switch_but.MinimumSize = new Size(48, 48);
			language_switch_but.Name = "language_switch_but";
			language_switch_but.Size = new Size(48, 58);
			language_switch_but.TabIndex = 9;
			language_switch_but.Text = "EN";
			language_switch_but.UseVisualStyleBackColor = true;
			language_switch_but.Click += language_switch_but_Click;
			// 
			// help_but
			// 
			help_but.Dock = DockStyle.Left;
			help_but.Location = new Point(1431, 0);
			help_but.MinimumSize = new Size(48, 48);
			help_but.Name = "help_but";
			help_but.Size = new Size(48, 58);
			help_but.TabIndex = 15;
			help_but.Text = "H";
			help_but.UseVisualStyleBackColor = true;
			help_but.Click += help_but_Click;
			// 
			// main_tools_flow
			// 
			main_tools_flow.Controls.Add(copy_but);
			main_tools_flow.Controls.Add(paste_but);
			main_tools_flow.Controls.Add(delete_but);
			main_tools_flow.Controls.Add(undo_but);
			main_tools_flow.Controls.Add(redo_but);
			main_tools_flow.Controls.Add(format_but);
			main_tools_flow.Dock = DockStyle.Left;
			main_tools_flow.Location = new Point(384, 0);
			main_tools_flow.Name = "main_tools_flow";
			main_tools_flow.Padding = new Padding(2, 2, 0, 0);
			main_tools_flow.Size = new Size(334, 58);
			main_tools_flow.TabIndex = 4;
			// 
			// copy_but
			// 
			copy_but.Location = new Point(5, 5);
			copy_but.Name = "copy_but";
			copy_but.Size = new Size(48, 48);
			copy_but.TabIndex = 2;
			copy_but.Text = "Copy";
			copy_but.UseVisualStyleBackColor = true;
			copy_but.Click += copy_but_Click;
			// 
			// paste_but
			// 
			paste_but.Location = new Point(59, 5);
			paste_but.Name = "paste_but";
			paste_but.Size = new Size(48, 48);
			paste_but.TabIndex = 3;
			paste_but.Text = "Paste";
			paste_but.UseVisualStyleBackColor = true;
			paste_but.Click += paste_but_Click;
			// 
			// delete_but
			// 
			delete_but.Location = new Point(113, 5);
			delete_but.Name = "delete_but";
			delete_but.Size = new Size(48, 48);
			delete_but.TabIndex = 4;
			delete_but.Text = "Del";
			delete_but.UseVisualStyleBackColor = true;
			delete_but.Click += delete_but_Click;
			// 
			// undo_but
			// 
			undo_but.Location = new Point(167, 5);
			undo_but.Name = "undo_but";
			undo_but.Size = new Size(48, 48);
			undo_but.TabIndex = 5;
			undo_but.Text = "U";
			undo_but.UseVisualStyleBackColor = true;
			undo_but.Click += undo_but_Click;
			// 
			// redo_but
			// 
			redo_but.Location = new Point(221, 5);
			redo_but.Name = "redo_but";
			redo_but.Size = new Size(48, 48);
			redo_but.TabIndex = 6;
			redo_but.Text = "R";
			redo_but.UseVisualStyleBackColor = true;
			redo_but.Click += redo_but_Click;
			// 
			// format_but
			// 
			format_but.Location = new Point(275, 5);
			format_but.Name = "format_but";
			format_but.Size = new Size(48, 48);
			format_but.TabIndex = 7;
			format_but.Text = "F";
			format_but.UseVisualStyleBackColor = true;
			format_but.Click += format_but_Click;
			// 
			// doc_toolbar_flow
			// 
			doc_toolbar_flow.Controls.Add(d_new_but);
			doc_toolbar_flow.Controls.Add(d_open_but);
			doc_toolbar_flow.Controls.Add(d_save_but);
			doc_toolbar_flow.Controls.Add(d_save_as_but);
			doc_toolbar_flow.Controls.Add(d_import_but);
			doc_toolbar_flow.Controls.Add(d_export_but);
			doc_toolbar_flow.Controls.Add(d_print_but);
			doc_toolbar_flow.Dock = DockStyle.Left;
			doc_toolbar_flow.Location = new Point(0, 0);
			doc_toolbar_flow.Name = "doc_toolbar_flow";
			doc_toolbar_flow.Padding = new Padding(2, 2, 0, 0);
			doc_toolbar_flow.Size = new Size(384, 58);
			doc_toolbar_flow.TabIndex = 4;
			// 
			// d_new_but
			// 
			d_new_but.Location = new Point(5, 5);
			d_new_but.MinimumSize = new Size(48, 48);
			d_new_but.Name = "d_new_but";
			d_new_but.Size = new Size(48, 48);
			d_new_but.TabIndex = 1;
			d_new_but.Text = "New";
			d_new_but.UseVisualStyleBackColor = true;
			d_new_but.Click += d_new_but_Click;
			// 
			// d_open_but
			// 
			d_open_but.Location = new Point(59, 5);
			d_open_but.Name = "d_open_but";
			d_open_but.Size = new Size(48, 48);
			d_open_but.TabIndex = 8;
			d_open_but.Text = "O";
			d_open_but.UseVisualStyleBackColor = true;
			d_open_but.Click += d_open_but_Click;
			// 
			// d_save_but
			// 
			d_save_but.Location = new Point(113, 5);
			d_save_but.Name = "d_save_but";
			d_save_but.Size = new Size(48, 48);
			d_save_but.TabIndex = 2;
			d_save_but.Text = "S";
			d_save_but.UseVisualStyleBackColor = true;
			d_save_but.Click += d_save_but_Click;
			// 
			// d_save_as_but
			// 
			d_save_as_but.Location = new Point(167, 5);
			d_save_as_but.Name = "d_save_as_but";
			d_save_as_but.Size = new Size(48, 48);
			d_save_as_but.TabIndex = 4;
			d_save_as_but.Text = "SA";
			d_save_as_but.UseVisualStyleBackColor = true;
			d_save_as_but.Click += d_save_as_but_Click;
			// 
			// d_import_but
			// 
			d_import_but.Location = new Point(221, 5);
			d_import_but.Name = "d_import_but";
			d_import_but.Size = new Size(48, 48);
			d_import_but.TabIndex = 3;
			d_import_but.Text = "I";
			d_import_but.UseVisualStyleBackColor = true;
			d_import_but.Click += d_import_but_Click;
			// 
			// d_export_but
			// 
			d_export_but.Location = new Point(275, 5);
			d_export_but.Name = "d_export_but";
			d_export_but.Size = new Size(48, 48);
			d_export_but.TabIndex = 5;
			d_export_but.Text = "E";
			d_export_but.UseVisualStyleBackColor = true;
			d_export_but.Click += d_export_but_Click;
			// 
			// d_print_but
			// 
			d_print_but.Location = new Point(329, 5);
			d_print_but.Name = "d_print_but";
			d_print_but.Size = new Size(48, 48);
			d_print_but.TabIndex = 6;
			d_print_but.Text = "P";
			d_print_but.UseVisualStyleBackColor = true;
			d_print_but.Click += d_print_but_Click;
			// 
			// shapes_tools_panel
			// 
			shapes_tools_panel.BackColor = SystemColors.Control;
			shapes_tools_panel.Controls.Add(c_nodes_tools_flow);
			shapes_tools_panel.Controls.Add(s_scissors_flow);
			shapes_tools_panel.Controls.Add(s_colors_flow);
			shapes_tools_panel.Controls.Add(s_layout_flow);
			shapes_tools_panel.Controls.Add(s_boolean_tools_flow);
			shapes_tools_panel.Dock = DockStyle.Top;
			shapes_tools_panel.Location = new Point(0, 144);
			shapes_tools_panel.Name = "shapes_tools_panel";
			shapes_tools_panel.Size = new Size(1599, 58);
			shapes_tools_panel.TabIndex = 5;
			// 
			// image_tools_panel
			// 
			image_tools_panel.BackColor = SystemColors.Control;
			image_tools_panel.Controls.Add(i_actual_resolution_check);
			image_tools_panel.Controls.Add(i_size_lock_check);
			image_tools_panel.Controls.Add(i_tools_settings_flow);
			image_tools_panel.Controls.Add(i_colors_flow);
			image_tools_panel.Controls.Add(image_tools_flow);
			image_tools_panel.Dock = DockStyle.Top;
			image_tools_panel.Location = new Point(0, 202);
			image_tools_panel.Name = "image_tools_panel";
			image_tools_panel.Size = new Size(1599, 59);
			image_tools_panel.TabIndex = 6;
			// 
			// i_actual_resolution_check
			// 
			i_actual_resolution_check.AutoSize = true;
			i_actual_resolution_check.Dock = DockStyle.Left;
			i_actual_resolution_check.Location = new Point(1140, 0);
			i_actual_resolution_check.Name = "i_actual_resolution_check";
			i_actual_resolution_check.Size = new Size(163, 59);
			i_actual_resolution_check.TabIndex = 11;
			i_actual_resolution_check.Text = "Use page resolution";
			i_actual_resolution_check.UseVisualStyleBackColor = true;
			i_actual_resolution_check.CheckedChanged += i_actual_resolution_check_CheckedChanged;
			// 
			// i_size_lock_check
			// 
			i_size_lock_check.AutoSize = true;
			i_size_lock_check.Dock = DockStyle.Left;
			i_size_lock_check.Location = new Point(1051, 0);
			i_size_lock_check.Name = "i_size_lock_check";
			i_size_lock_check.Size = new Size(89, 59);
			i_size_lock_check.TabIndex = 10;
			i_size_lock_check.Text = "Size lock";
			i_size_lock_check.UseVisualStyleBackColor = true;
			i_size_lock_check.CheckedChanged += i_size_lock_check_CheckedChanged;
			// 
			// page_tools_panel
			// 
			page_tools_panel.BackColor = SystemColors.Control;
			page_tools_panel.Controls.Add(page_view_flow);
			page_tools_panel.Controls.Add(page_zero_flow);
			page_tools_panel.Controls.Add(page_lock_ratio_flow);
			page_tools_panel.Controls.Add(page_size_flow);
			page_tools_panel.Controls.Add(page_layout_flow);
			page_tools_panel.Controls.Add(page_paper_flow);
			page_tools_panel.Dock = DockStyle.Top;
			page_tools_panel.Location = new Point(0, 86);
			page_tools_panel.Name = "page_tools_panel";
			page_tools_panel.Size = new Size(1599, 58);
			page_tools_panel.TabIndex = 7;
			// 
			// statusStrip
			// 
			statusStrip.ImageScalingSize = new Size(20, 20);
			statusStrip.Items.AddRange(new ToolStripItem[] { status_strip_label });
			statusStrip.Location = new Point(0, 993);
			statusStrip.Name = "statusStrip";
			statusStrip.Size = new Size(1599, 26);
			statusStrip.TabIndex = 8;
			statusStrip.Text = "statusStrip1";
			// 
			// status_strip_label
			// 
			status_strip_label.Name = "status_strip_label";
			status_strip_label.Size = new Size(49, 20);
			status_strip_label.Text = "Status";
			// 
			// MainForm
			// 
			ClientSize = new Size(1599, 1019);
			Controls.Add(drawing_toolbar_panel);
			Controls.Add(image_tools_panel);
			Controls.Add(shapes_tools_panel);
			Controls.Add(page_tools_panel);
			Controls.Add(main_toolbar_panel);
			Controls.Add(menuStrip1);
			Controls.Add(statusStrip);
			Icon = (Icon)resources.GetObject("$this.Icon");
			MainMenuStrip = menuStrip1;
			Name = "MainForm";
			Text = "VDraw";
			WindowState = FormWindowState.Maximized;
			menuStrip1.ResumeLayout(false);
			menuStrip1.PerformLayout();
			page_view_flow.ResumeLayout(false);
			page_zero_flow.ResumeLayout(false);
			page_zero_flow.PerformLayout();
			((System.ComponentModel.ISupportInitialize)page_zero_x_nud).EndInit();
			((System.ComponentModel.ISupportInitialize)page_zero_y_nud).EndInit();
			page_layout_flow.ResumeLayout(false);
			page_layout_flow.PerformLayout();
			page_lock_ratio_flow.ResumeLayout(false);
			page_size_flow.ResumeLayout(false);
			page_size_flow.PerformLayout();
			((System.ComponentModel.ISupportInitialize)page_size_w_nud).EndInit();
			((System.ComponentModel.ISupportInitialize)page_size_h_nud).EndInit();
			page_paper_flow.ResumeLayout(false);
			s_scissors_flow.ResumeLayout(false);
			s_colors_flow.ResumeLayout(false);
			s_layout_flow.ResumeLayout(false);
			s_boolean_tools_flow.ResumeLayout(false);
			i_tools_settings_flow.ResumeLayout(false);
			i_tools_settings_flow.PerformLayout();
			((System.ComponentModel.ISupportInitialize)i_tool_size_nud).EndInit();
			((System.ComponentModel.ISupportInitialize)i_tool_tolerance_nud).EndInit();
			((System.ComponentModel.ISupportInitialize)i_tool_strength_nud).EndInit();
			((System.ComponentModel.ISupportInitialize)i_tool_smooth_nud).EndInit();
			i_colors_flow.ResumeLayout(false);
			flip_flow.ResumeLayout(false);
			image_tools_flow.ResumeLayout(false);
			c_nodes_tools_flow.ResumeLayout(false);
			view_flow.ResumeLayout(false);
			drawing_toolbar_panel.ResumeLayout(false);
			main_toolbar_panel.ResumeLayout(false);
			main_tools_flow.ResumeLayout(false);
			doc_toolbar_flow.ResumeLayout(false);
			shapes_tools_panel.ResumeLayout(false);
			image_tools_panel.ResumeLayout(false);
			image_tools_panel.PerformLayout();
			page_tools_panel.ResumeLayout(false);
			statusStrip.ResumeLayout(false);
			statusStrip.PerformLayout();
			ResumeLayout(false);
			PerformLayout();

		}

		#endregion

		private MenuStrip menuStrip1;
		private ToolStripMenuItem menu_file;
		private ToolStripMenuItem menu_file_new;
		private ToolStripMenuItem menu_file_open;
		private ToolStripMenuItem menu_file_import;
		private ToolStripMenuItem menu_file_save;
		private ToolStripMenuItem menu_file_save_as;
		private ToolStripMenuItem menu_file_export;
		private ToolStripMenuItem menu_file_print;
		private ToolStripMenuItem menu_file_close;
		private ToolStripMenuItem menu_file_exit;
		private ToolStripMenuItem menu_document;
		private ToolStripMenuItem menu_objects;
		private ToolStripMenuItem menu_obj_lines;
		private ToolStripMenuItem menu_obj_curve;
		private ToolStripMenuItem menu_obj_rect;
		private ToolStripMenuItem menu_obj_circle2p;
		private ToolStripMenuItem menu_obj_circle3p;
		private ToolStripMenuItem menu_obj_ellispe3p;
		private ToolStripMenuItem menu_obj_ellipse5p;
		private ToolStripMenuItem menu_obj_poly;
		private ToolStripMenuItem menu_obj_text;
		private ToolStripMenuItem menu_obj_image;
		private ToolStripMenuItem menu_obj_rounded_rect;
		private Button s_cursor_but;
		private Button s_copy_but;
		private Button s_paste_but;
		private Button s_format_but;
		private Button s_undo_but;
		private Button s_redo_but;
		private Button s_delete_but;
		private Button s_group_but;
		private Button s_ungroup_but;
		private Button s_bring_to_front_but;
		private Button s_send_back_but;
		private Button s_union_but;
		private Button s_xor_but;
		private Button s_intersect_but;
		private Button s_substract_but;
		private FlowLayoutPanel page_size_flow;
		private FlowLayoutPanel s_layout_flow;
		private FlowLayoutPanel s_boolean_tools_flow;
		private ColorPreviewButton s_line_color_but;
		private ColorPreviewButton s_fill_color_but;
		private FlowLayoutPanel page_paper_flow;
		private Button s_new_line_but;
		private Button s_new_curve_but;
		private Button s_new_poly_but;
		private Button s_new_text_but;
		private Button s_new_image_but;
		private ToolStripMenuItem menu_page;
		private FlowLayoutPanel view_flow;
		private Button v_show_rulers_but;
		private Button v_dynamic_guidelines_but;
		private Button v_snap_but;
		private Button measure_distance_but;
		private Button s_new_free_poly_but;
		private SkOptionsButton s_new_circle_but;
		private SkOptionsButton s_new_rc_but;
		private SkOptionsButton s_new_ellipse_but;
		private Button v_show_guidelines_but;
		private Panel drawing_toolbar_panel;
		private Panel main_toolbar_panel;
		private FlowLayoutPanel doc_toolbar_flow;
		private Button d_new_but;
		private Button d_save_but;
		private Button d_import_but;
		private Button language_switch_but;
		private Button d_save_as_but;
		private Button d_export_but;
		private Button d_print_but;
		private FlowLayoutPanel main_tools_flow;
		private Button copy_but;
		private Button paste_but;
		private Button delete_but;
		private Button undo_but;
		private Button redo_but;
		private Button format_but;
		private ComboBox page_paper_size_combo;
		private FlowLayoutPanel flowLayoutPanel4;
		private NumericUpDown page_size_w_nud;
		private Label label1;
		private NumericUpDown page_size_h_nud;
		private Label label2;
		private Button page_props_but;
		private FlowLayoutPanel page_layout_flow;
		private Button page_vertical_but;
		private Button page_horizontal_but;
		private FlowLayoutPanel page_zero_flow;
		private Label label3;
		private ComboBox page_uom_combo;
		private Label label4;
		private NumericUpDown page_zero_x_nud;
		private Label label5;
		private NumericUpDown page_zero_y_nud;
		private Button d_open_but;
		private FlowLayoutPanel page_view_flow;
		private FlowLayoutPanel s_colors_flow;
		private Button page_print_borders_but;
		private Button page_grid_but;
		private FlowLayoutPanel c_nodes_tools_flow;
		private Button nodes_merge_but;
		private Button nodes_split_but;
		private Button nodes_straight_but;
		private Button node_tangent_but;
		private Button node_sharp_but;
		private Button node_delete_but;
		private Button s_convert_to_curve_but;
		private Button s_curve_edit_but;
		private FlowLayoutPanel flowLayoutPanel8;
		private FlowLayoutPanel page_lock_ratio_flow;
		private Button page_lock_ratio_but;
		private Button s_combine_but;
		private Button image_mode_but;
		private FlowLayoutPanel image_tools_flow;
		private Button i_erasor_but;
		private Button i_brush_but;
		private Button i_select_rect_but;
		private FlowLayoutPanel i_colors_flow;
		private ColorPreviewButton i_fore_color_but;
		private ColorPreviewButton i_back_color_but;
		private Button i_select_lasso_but;
		private Button i_light_but;
		private Button i_dark_but;
		private Button i_fill_but;
		private Button i_contrast_but;
		private Button i_blackwhite_but;
		private FlowLayoutPanel i_tools_settings_flow;
		private NumericUpDown i_tool_size_nud;
		private Label label6;
		private Label label7;
		private NumericUpDown i_tool_tolerance_nud;
		private Label label8;
		private NumericUpDown i_tool_strength_nud;
		private Label label9;
		private NumericUpDown i_tool_smooth_nud;
		private Label label10;
		private ComboBox i_tool_shape_combo;
		private FlowLayoutPanel flip_flow;
		private Button rotate_CW_90_but;
		private Button rotate_CCW_90_but;
		private FlowLayoutPanel s_scissors_flow;
		private Button s_scissors_line_but;
		private Button s_scissors_curve_but;
		private Button s_cap_scissors_but;
		private Panel shapes_tools_panel;
		private Panel image_tools_panel;
		private Panel page_tools_panel;
		private FlowLayoutPanel v_scale_flow;
		private ComboBox v_scale_combo;
		private Button v_scale_selected_but;
		private Button flip_horizontal_but;
		private Button flip_vertical_but;
		private Button v_scale_all_but;
		private Button v_scale_page_but;
		private Button help_but;
		private ColorPreviewButton page_background_color_but;
		private Button s_morth_but;
		private CheckBox i_size_lock_check;
		private ToolStripMenuItem menu_obj_measurement_distance;
		private ToolStripSeparator toolStripSeparator1;
		private ToolStripMenuItem convertToCurveToolStripMenuItem;
		private CheckBox i_actual_resolution_check;
		private ToolStripMenuItem measureAngleToolStripMenuItem;
		private Button measure_angle_but;
		private StatusStrip statusStrip;
		private ToolStripStatusLabel status_strip_label;
		private ToolStripMenuItem customShapesToolStripMenuItem;
		private ToolStripMenuItem menu_obj_add_to_custom_shapes;
		private ToolStripMenuItem menu_obj_import_visio;
		private ToolStripMenuItem menu_obj_import_vdrs;
		private ToolStripMenuItem helpToolStripMenuItem;
		private ToolStripMenuItem menu_about;
		private ToolStripMenuItem menu_manual;
		private ToolStripSeparator toolStripMenuItem1;
		private ToolStripMenuItem settingsToolStripMenuItem;
		private ToolStripSeparator toolStripMenuItem2;
		private ToolStripMenuItem menu_settings;
	}
}