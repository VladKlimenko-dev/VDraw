using SkiaSharp;
using System.Data;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public partial class MainForm : Form
{
	#region Variables
	public static System.Drawing.Font? fSnipit, ftext_align;
	public static FontFamily? fSnipItFamily;
	public static SKFont? skFont;
	public static PrivateFontCollection font_collection = new PrivateFontCollection();
	private System.Windows.Forms.ToolTip toolTip = new System.Windows.Forms.ToolTip();

	public static Document? cur_doc = null;
	public static SelectionManager sel;
	private SkPropertyPanelControl _propPanel = null!;
	private PageTabsControl _pageTabs = null!;
	private AnimationPanel _animPanel = null!;
	private UndoStack? _undoStack;
	public MainView? _mainView;
	private ColorsPanel colors_panel;
	private Action? _undoChangedHandler;

	/// <summary>
	/// Опрос состояний, у которых нет события: признак изменений документа
	/// и содержимое системного буфера обмена.
	/// </summary>
	private System.Windows.Forms.Timer? _commandStateTimer;

	/// <summary>
	/// Что уже применено к дереву контролов: есть документ или нет.
	/// null — не применялось ни разу.
	///
	/// Нужно, чтобы ApplyDocumentPresence срабатывал на ПЕРЕХОДЕ, а не
	/// на каждом опросе. Метод включает всё дерево целиком, а вызывается
	/// он из UpdateCommandButtons, то есть четыре раза в секунду по
	/// таймеру. Каждый такой проход поднимал Enabled у кнопок, которые
	/// строкой ниже гасились обратно, — отсюда и мигание.
	/// </summary>
	private bool? _documentPresenceApplied;

	private ShapeToolKind _selectedCircleTool = ShapeToolKind.Circle2P;
	private ShapeToolKind _selectedEllipseTool = ShapeToolKind.Ellipse3P;
	private ShapeToolKind _selectedRectangleTool = ShapeToolKind.Rectangle;

	public static EditorMode Mode
	{
		get
		{
			return _mode;
		}
	}
	private static EditorMode _mode = EditorMode.Shape;
	public static Page? CurrentPage { get; private set; }
	private List<Shape> _clipboardShapes = new();
	private const float PasteOffset = 0f;
	/// <summary>
	/// Идёт программное обновление поля масштаба.
	///
	/// Флаг нужен потому, что поток изменений двусторонний: пользователь меняет
	/// поле и вид перестраивается, вид меняется колесом мыши и поле обновляется.
	/// Без подавления второе вызвало бы первое и масштаб дёргался бы на каждом
	/// шаге приближения.
	/// </summary>
	private bool _suppressScaleCombo;

	#endregion

	#region Form Initialization methods

	public MainForm()
	{
		Icon = new Icon("Resources\\vdraw.ico");

		InitializeComponent();
		SetIcons();
		SetToolTips();
		InitCircleSplitButton();
		InitEllipseSplitButton();
		InitRectangleSplitButton();
		InitColorPanel();
		InitImageToolbar();
		InitCustomShapes();

		// Последним: запоминает исходные английские тексты, уже
		// расставленные дизайнером и SetToolTips.
		InitLanguage();
		WinFormsTheme.Register(this);
		ThemeImages.RegisterMainFormImages(this);
	}

	protected void SetIcons()
	{
		font_collection.AddFontFile("Resources\\FluentSystemIcons-Resizable.ttf");
		fSnipItFamily = new FontFamily("FluentSystemIcons-Resizable", font_collection);
		fSnipit = new System.Drawing.Font(fSnipItFamily, 36.0f, FontStyle.Regular, GraphicsUnit.Pixel);
		ftext_align = new System.Drawing.Font(fSnipItFamily, 18.0f, FontStyle.Regular, GraphicsUnit.Pixel);
		skFont = new SKFont(SKTypeface.FromFile("Resources\\FluentSystemIcons-Resizable.ttf"), 34);

		s_cursor_but.Font = fSnipit;
		s_cursor_but.Text = "\ue597";

		image_mode_but.Font = fSnipit;
		image_mode_but.Text = "\ue98e";

		s_group_but.Font = fSnipit;
		s_group_but.Text = "\ue8fb";
		s_ungroup_but.Font = fSnipit;
		s_ungroup_but.Text = "\ue8fd";
		flip_horizontal_but.Font = fSnipit;
		flip_horizontal_but.Text = "\ue833";
		flip_vertical_but.Font = fSnipit;
		flip_vertical_but.Text = "\ue835";
		s_line_color_but.Text = "";
		s_fill_color_but.Text = "";
		s_bring_to_front_but.Font = fSnipit;
		s_bring_to_front_but.Text = "\ued94";
		s_send_back_but.Font = fSnipit;
		s_send_back_but.Text = "\ued92";
		s_new_text_but.Font = fSnipit;
		s_new_text_but.Text = "\uf190"; //Ab
		s_new_image_but.Font = fSnipit;
		s_new_image_but.Text = "\ue97c";
		s_morth_but.Font = fSnipit;
		s_morth_but.Text = "\uf0e9";

		page_props_but.Font = fSnipit;
		page_props_but.Text = "\ue703";
		page_lock_ratio_but.Font = fSnipit;
		page_lock_ratio_but.Text = "\ueab6";
		page_print_borders_but.Font = fSnipit;
		page_print_borders_but.Text = "\ue68f"; // Print borders
		page_grid_but.Font = fSnipit;
		page_grid_but.Text = "\ue244";
		page_vertical_but.Font = fSnipit;
		page_vertical_but.Text = "\ue655"; // Vertical page
		page_horizontal_but.Font = fSnipit;
		page_horizontal_but.Text = "\ue6af"; // Horizontal page
		undo_but.Font = fSnipit;
		undo_but.Text = "\ue0f7";
		redo_but.Font = fSnipit;
		redo_but.Text = "\ue0f9";
		format_but.Font = fSnipit;
		format_but.Text = "\uec02";

		d_new_but.Font = fSnipit;
		d_new_but.Text = "\ue655";
		d_open_but.Font = fSnipit;
		d_open_but.Text = "\ue859";
		d_import_but.Font = fSnipit;
		d_import_but.Text = "\ue0cf";
		d_export_but.Font = fSnipit;
		d_export_but.Text = "\ue0d5";
		d_save_but.Font = fSnipit;
		d_save_but.Text = "\uee5f";
		d_save_as_but.Font = fSnipit;
		d_save_as_but.Text = "\uee65";
		d_print_but.Font = fSnipit;
		d_print_but.Text = "\ue6ef";

		copy_but.Font = fSnipit;
		copy_but.Text = "\ued8e"; // copy
		paste_but.Font = fSnipit;
		paste_but.Text = "\ue490"; // copy
		delete_but.Font = fSnipit;
		delete_but.Text = "\ue63e"; // xmark

		i_select_rect_but.Font = fSnipit;
		i_select_rect_but.Text = "\ue252";
		i_select_lasso_but.Font = fSnipit;
		i_select_lasso_but.Text = "\ue9f3";
		i_brush_but.Font = fSnipit;
		i_brush_but.Text = "\ue45e";
		i_erasor_but.Font = fSnipit;
		i_erasor_but.Text = "\ue7cd";
		i_light_but.Font = fSnipit;
		i_light_but.Text = "\ue2a8";
		i_dark_but.Font = fSnipit;
		i_dark_but.Text = "\ue2a7";
		i_fill_but.Font = fSnipit;
		i_fill_but.Text = "\ue4fc";
		i_contrast_but.Font = fSnipit;
		i_contrast_but.Text = "\ue014";
		i_blackwhite_but.Font = fSnipit;
		i_blackwhite_but.Text = "\ue43c";
		rotate_CW_90_but.Font = fSnipit;
		rotate_CW_90_but.Text = "\ue129";
		rotate_CCW_90_but.Font = fSnipit;
		rotate_CCW_90_but.Text = "\ue12b";

		v_scale_selected_but.Font = fSnipit;
		v_scale_selected_but.Text = "\ueea7";
		v_scale_all_but.Font = fSnipit;
		v_scale_all_but.Text = "\uee9d";
		v_scale_page_but.Font = fSnipit;
		v_scale_page_but.Text = "\ue701";
		help_but.Font = fSnipit;
		help_but.Text = "\ue22c";

		s_line_color_but.PreviewColor = Color.FromArgb((int)(Settings.shape_default_line_color.Alpha), (int)(Settings.shape_default_line_color.Red), (int)(Settings.shape_default_line_color.Green), (int)(Settings.shape_default_line_color.Blue));
		s_fill_color_but.PreviewColor = Color.FromArgb((int)(Settings.shape_default_fill_color.Alpha), (int)(Settings.shape_default_fill_color.Red), (int)(Settings.shape_default_fill_color.Green), (int)(Settings.shape_default_fill_color.Blue));
		i_fore_color_but.PreviewColor = Color.FromArgb((int)(Settings.image_paint_fore_color.Alpha), (int)(Settings.image_paint_fore_color.Red), (int)(Settings.image_paint_fore_color.Green), (int)(Settings.image_paint_fore_color.Blue));
		i_back_color_but.PreviewColor = Color.FromArgb((int)(Settings.image_paint_back_color.Alpha), (int)(Settings.image_paint_back_color.Red), (int)(Settings.image_paint_back_color.Green), (int)(Settings.image_paint_back_color.Blue));
		page_background_color_but.PreviewColor = Color.White;

		SetButtonChecked(v_show_rulers_but, Settings.show_rulers);
		SetButtonChecked(v_show_guidelines_but, Settings.show_guidelines);
		SetButtonChecked(v_snap_but, Settings.snap_objects);
		SetButtonChecked(v_dynamic_guidelines_but, Settings.dynamic_guidelines);
		SetButtonChecked(format_but, false);
		SetButtonChecked(page_grid_but, false);
		SetButtonChecked(page_print_borders_but, false);
		SetButtonChecked(s_cap_scissors_but, false);
	}

	/// <summary>
	/// Подсказки ко всем видимым контролам формы.
	///
	/// Сочетания клавиш вынесены в сам текст подсказки: так пользователь
	/// учит их по ходу работы, а не ищет в справке. Указаны только те, что
	/// действительно назначены в обработчиках клавиатуры.
	///
	/// У полей ввода подсказка вешается и на само поле, и на его подпись:
	/// подписи вроде «W» или «Smooth» сами по себе ничего не объясняют,
	/// а курсор часто оказывается именно над ними.
	/// </summary>
	private void SetToolTips()
	{
		toolTip.ShowAlways = true;
		toolTip.InitialDelay = 500;
		toolTip.ReshowDelay = 200;
		toolTip.AutoPopDelay = 8000;

		SetDocumentToolTips();
		SetEditToolTips();
		SetShapeToolTips();
		SetArrangeToolTips();
		SetBooleanToolTips();
		SetNodeToolTips();
		SetViewToolTips();
		SetPageToolTips();
		SetImageToolTips();
	}

	#region Подсказки по группам

	private void SetDocumentToolTips()
	{
		toolTip.SetToolTip(d_new_but, L.T("New document  (Ctrl+N)\r\nAsks for paper format, then replaces the current document."));
		toolTip.SetToolTip(d_open_but, L.T("Open document  (Ctrl+O)\r\nOpens a .vdr file. Unsaved changes are offered for saving first."));
		toolTip.SetToolTip(d_save_but, L.T("Save  (Ctrl+S)\r\nSaves to the current file. A new document asks for a name."));
		toolTip.SetToolTip(d_save_as_but, L.T("Save as...  (Ctrl+Shift+S)\r\nSaves under a new name and keeps working in the new file."));
		toolTip.SetToolTip(d_import_but, L.T("Import  (Ctrl+I)\r\nInserts shapes from a .vdr or pages from a .pdf into the current document, and adds .vdrs sets and Visio stencils to custom shapes."));
		toolTip.SetToolTip(d_export_but, L.T("Export selection  (Ctrl+E)\r\nWrites the selected shapes to PNG, JPEG, BMP or SVG."));
		toolTip.SetToolTip(d_print_but, L.T("Print  (Ctrl+P)"));
		toolTip.SetToolTip(help_but, L.T("Help  (F1)"));
	}

	private void SetEditToolTips()
	{
		toolTip.SetToolTip(copy_but, L.T("Copy  (Ctrl+C)\r\nCopies the selected shapes. In image mode copies the pixel selection."));
		toolTip.SetToolTip(paste_but, L.T("Paste  (Ctrl+V)"));
		toolTip.SetToolTip(format_but,
			"Copy format  (Ctrl+Shift+C)\r\n"
			+ "Takes stroke and fill from the selected shape, then click shapes on the canvas to apply.\r\n"
			+ "Esc, right-click or this button again ends painting.\r\n"
			+ "Ctrl+Shift+V applies the format to the whole selection at once.");
		toolTip.SetToolTip(undo_but, L.T("Undo  (Ctrl+Z)"));
		toolTip.SetToolTip(redo_but, L.T("Redo  (Ctrl+Y)"));
		toolTip.SetToolTip(delete_but, L.T("Delete selected  (Del)"));
	}

	private void SetShapeToolTips()
	{
		toolTip.SetToolTip(s_cursor_but, L.T("Select  (Esc)\r\nPick, move, resize and rotate shapes."));
		toolTip.SetToolTip(s_new_line_but, L.T("Straight lines  (L)\r\nClick to add nodes, right-click or Enter to finish."));
		toolTip.SetToolTip(s_new_curve_but, L.T("Curve  (B)\r\nClick to add nodes, right-click or Enter to finish."));
		toolTip.SetToolTip(s_new_rc_but, L.T("Rectangle  (R)\r\nArrow opens plain, rounded and chamfered variants."));
		toolTip.SetToolTip(s_new_circle_but, L.T("Circle  (C)\r\nArrow switches between by centre and radius, and by three points."));
		toolTip.SetToolTip(s_new_ellipse_but, L.T("Ellipse  (E)\r\nArrow switches between by three points and by five points."));
		toolTip.SetToolTip(s_new_poly_but, L.T("Regular polygon  (P)\r\nFirst click sets the centre, second the radius."));
		toolTip.SetToolTip(s_new_free_poly_but, L.T("Free polygon  (Shift+P)\r\nClick to add vertices, right-click or Enter to close."));
		toolTip.SetToolTip(s_new_text_but, L.T("Text  (T)"));
		toolTip.SetToolTip(s_new_image_but, L.T("Image  (Ctrl+Shift+I)\r\nPlaces a raster image into a closed contour."));
		toolTip.SetToolTip(measure_distance_but, L.T("Measure distance  (M)\r\nThree clicks: from, to, and the height of the value label. Ends snap to shape points."));
		toolTip.SetToolTip(image_mode_but, L.T("Image editing mode  (Ctrl+M)\r\nSwitches the toolbar to brushes and pixel selection for the selected image."));
		toolTip.SetToolTip(s_line_color_but, L.T("Default outline colour\r\nApplies to newly created shapes."));
		toolTip.SetToolTip(s_fill_color_but, L.T("Default fill colour\r\nApplies to newly created shapes."));
	}

	private void SetArrangeToolTips()
	{
		toolTip.SetToolTip(s_group_but, L.T("Group  (Ctrl+G)"));
		toolTip.SetToolTip(s_ungroup_but, L.T("Ungroup  (Ctrl+Shift+G)"));
		toolTip.SetToolTip(s_bring_to_front_but, L.T("Bring to front  (Ctrl+Shift+Up)"));
		toolTip.SetToolTip(s_send_back_but, L.T("Send to back  (Ctrl+Shift+Down)"));
		toolTip.SetToolTip(flip_horizontal_but, L.T("Flip horizontally  (Shift+H)"));
		toolTip.SetToolTip(flip_vertical_but, L.T("Flip vertically  (Shift+V)"));
		toolTip.SetToolTip(rotate_CW_90_but, L.T("Rotate 90 clockwise  (Ctrl+Right)"));
		toolTip.SetToolTip(rotate_CCW_90_but, L.T("Rotate 90 counter-clockwise  (Ctrl+Left)"));
	}

	private void SetBooleanToolTips()
	{
		toolTip.SetToolTip(s_union_but, L.T("Union  (Ctrl+Alt+U)\r\nMerges the selected shapes into one."));
		toolTip.SetToolTip(s_substract_but, L.T("Subtract  (Ctrl+Alt+S)\r\nRemoves the upper shapes from the lower one."));
		toolTip.SetToolTip(s_intersect_but, L.T("Intersect  (Ctrl+Alt+I)\r\nKeeps only the overlapping area."));
		toolTip.SetToolTip(s_xor_but, L.T("Exclude  (Ctrl+Alt+X)\r\nKeeps everything except the overlapping area."));
		toolTip.SetToolTip(s_combine_but, L.T("Combine  (Ctrl+Alt+C)\r\nJoins the shapes into one curve keeping separate contours."));
		toolTip.SetToolTip(s_convert_to_curve_but, L.T("Convert to curve  (Ctrl+Q)\r\nTurns a primitive into an editable curve. The step cannot be reversed except by Undo."));
		toolTip.SetToolTip(s_curve_edit_but, L.T("Edit nodes  (N)\r\nShows the nodes and control handles of the selected curve."));
		toolTip.SetToolTip(s_scissors_line_but, L.T("Cut with a straight line  (K)"));
		toolTip.SetToolTip(s_scissors_curve_but, L.T("Cut with a curve  (Shift+K)"));
		toolTip.SetToolTip(s_cap_scissors_but, L.T("Close the cut edges\r\nWhen on, the parts produced by a cut get a closed contour."));
	}

	private void SetNodeToolTips()
	{
		toolTip.SetToolTip(nodes_merge_but, L.T("Merge nodes  (Ctrl+J)\r\nJoins two selected ends into one node."));
		toolTip.SetToolTip(nodes_split_but, L.T("Split node  (Ctrl+Shift+J)\r\nBreaks the contour at the focused node."));
		toolTip.SetToolTip(nodes_straight_but, L.T("Straight node  (1)\r\nDrops the control handles: both segments become straight."));
		toolTip.SetToolTip(node_tangent_but, L.T("Smooth node  (2)\r\nControl handles stay collinear, the curve passes through smoothly."));
		toolTip.SetToolTip(node_sharp_but, L.T("Cusp node  (3)\r\nControl handles move independently, a corner is allowed."));
		toolTip.SetToolTip(node_delete_but, L.T("Delete nodes  (Del in node mode)"));
	}

	private void SetViewToolTips()
	{
		toolTip.SetToolTip(v_show_rulers_but, L.T("Rulers  (Ctrl+R)"));
		toolTip.SetToolTip(v_show_guidelines_but, L.T("Guides  (Ctrl+;)\r\nDrag a guide out of a ruler."));
		toolTip.SetToolTip(v_snap_but, L.T("Snap to objects  (Ctrl+Shift+N)\r\nHold Ctrl while dragging to suspend it temporarily."));
		toolTip.SetToolTip(v_dynamic_guidelines_but, L.T("Dynamic guides\r\nShows alignment lines to nearby shapes while dragging."));
		toolTip.SetToolTip(v_scale_combo, L.T("Zoom\r\nPick a value or type your own. Ctrl+wheel zooms at the cursor."));
		toolTip.SetToolTip(v_scale_selected_but, L.T("Zoom to selection  (Ctrl+Shift+0)"));
		toolTip.SetToolTip(v_scale_all_but, L.T("Zoom to all shapes  (Ctrl+0)"));
		toolTip.SetToolTip(v_scale_page_but, L.T("Fit page  (Ctrl+1)"));
	}

	private void SetPageToolTips()
	{
		toolTip.SetToolTip(page_props_but, L.T("Page properties\r\nFormat, size, units and background of the current page."));
		toolTip.SetToolTip(page_paper_size_combo, L.T("Paper format\r\nChoose Custom to enter your own width and height."));

		SetToolTipWithLabel(page_size_w_nud, label1, "Page width, in the current units.");
		SetToolTipWithLabel(page_size_h_nud, label2, "Page height, in the current units.");

		toolTip.SetToolTip(page_lock_ratio_but, L.T("Lock aspect ratio\r\nWhen on, changing one side changes the other proportionally."));
		toolTip.SetToolTip(page_vertical_but, L.T("Portrait orientation"));
		toolTip.SetToolTip(page_horizontal_but, L.T("Landscape orientation"));

		SetToolTipWithLabel(page_uom_combo, label3, "Page units. Shape sizes are converted, their real dimensions do not change.");
		SetToolTipWithLabel(page_zero_x_nud, label4, "Origin along X. Coordinates on the rulers are counted from this point.");
		SetToolTipWithLabel(page_zero_y_nud, label5, "Origin along Y. Coordinates on the rulers are counted from this point.");

		toolTip.SetToolTip(page_print_borders_but, L.T("Printable area borders"));
		toolTip.SetToolTip(page_grid_but, L.T("Grid  (Ctrl+#)"));
		toolTip.SetToolTip(page_background_color_but, L.T("Page background colour"));
	}

	private void SetImageToolTips()
	{
		toolTip.SetToolTip(i_erasor_but, L.T("Eraser  (E in image mode)"));
		toolTip.SetToolTip(i_brush_but, L.T("Brush  (B in image mode)\r\nSoft edge, strength and smoothing apply."));
		toolTip.SetToolTip(i_select_rect_but, L.T("Rectangular selection  (R in image mode)"));
		toolTip.SetToolTip(i_select_lasso_but, L.T("Lasso selection  (L in image mode)\r\nShift adds to the selection, Alt subtracts."));
		toolTip.SetToolTip(i_fill_but, L.T("Fill  (G in image mode)\r\nFills an area of similar colour. Tolerance sets how similar."));
		toolTip.SetToolTip(i_light_but, L.T("Lighten\r\nBrightens pixels under the brush."));
		toolTip.SetToolTip(i_dark_but, L.T("Darken\r\nDarkens pixels under the brush."));
		toolTip.SetToolTip(i_contrast_but, L.T("Contrast\r\nRaises contrast under the brush."));
		toolTip.SetToolTip(i_blackwhite_but, L.T("Black and white\r\nDesaturates pixels under the brush."));
		toolTip.SetToolTip(i_fore_color_but, L.T("Foreground colour\r\nDrawing colour for brush and fill."));
		toolTip.SetToolTip(i_back_color_but, L.T("Background colour\r\nWhat the eraser leaves behind."));

		SetToolTipWithLabel(i_tool_size_nud, label6, "Tool size in pixels.");
		SetToolTipWithLabel(i_tool_tolerance_nud, label7, "Colour tolerance: how far a pixel may differ and still be affected.");
		SetToolTipWithLabel(i_tool_strength_nud, label8, "Strength of the effect per stroke.");
		SetToolTipWithLabel(i_tool_smooth_nud, label9, "Edge softness of the stroke.");
		SetToolTipWithLabel(i_tool_shape_combo, label10, "Brush tip shape.");
	}

	/// <summary>
	/// Один текст на поле ввода и на его подпись.
	/// Подпись узкая, и попасть курсором в неё легко; подсказка только
	/// на поле оставляла бы часть наведений без ответа.
	/// </summary>
	private void SetToolTipWithLabel(Control editor, Control caption, string text)
	{
		toolTip.SetToolTip(editor, text);
		toolTip.SetToolTip(caption, text);
	}

	#endregion

	#region Сочетания клавиш

	/// <summary>
	/// Перехват клавиш до того, как их получит контрол с фокусом.
	///
	/// ProcessCmdKey, а не KeyPreview с OnKeyDown: KeyPreview не видит клавиш,
	/// которые контрол считает командными — стрелки, Tab, Enter, — а без них
	/// не сделать сдвиг выделения стрелками.
	/// </summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (TryHandleShortcut(keyData))
			return true;

		return base.ProcessCmdKey(ref msg, keyData);
	}

	/// <summary>
	/// Разбор сочетания. true — клавиша обработана и дальше не идёт.
	///
	/// Порядок групп важен только в одном месте: сочетания правки проверяются
	/// раньше инструментов, иначе Ctrl+C ушло бы в инструмент «окружность».
	/// </summary>
	private bool TryHandleShortcut(Keys keyData)
	{
		if (_mainView == null || cur_doc == null)
			return false;

		// Клавиши холста отдаются холсту целиком.
		if (IsCanvasFocused() && BelongsToCanvas(keyData))
			return false;

		bool typing = IsTextEntryFocused();

		// Пока идёт набор текста, поле забирает клавиатуру себе. Пропускаются
		// только команды документа: Ctrl+S во время правки подписи должен
		// сохранять документ, а не вставлять символ.
		if (typing && !IsAllowedWhileTyping(keyData))
			return false;

		if (TryHandleFileShortcut(keyData))
			return true;

		if (TryHandleEditShortcut(keyData))
			return true;

		if (typing)
			return false;

		return TryHandleToolShortcut(keyData)
			|| TryHandleArrangeShortcut(keyData)
			|| TryHandleNodeShortcut(keyData)
			|| TryHandleViewShortcut(keyData);
	}

	/// <summary>
	/// Клавиши, у которых в MainView.OnKeyDown своя обработка, зависящая от
	/// режима — фигуры, узлы или изображение. Форма их не трогает.
	///
	/// Список составлен по switch в OnKeyDown и должен меняться вместе с ним.
	/// </summary>
	private static bool BelongsToCanvas(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;

		switch (key)
		{
			case Keys.Escape:
			case Keys.Delete:
			case Keys.Enter:
				return true;

			// Копирование и вставка: в режиме изображения они работают
			// с пиксельным выделением, а не с фигурами.
			case Keys.C:
			case Keys.V:
				return true;

			// Отмену холст обрабатывает сам и умеет отдавать её растровому
			// инструменту посреди мазка.
			case Keys.Z:
			case Keys.Y:
				return (keyData & Keys.Control) == Keys.Control;

			default:
				return false;
		}
	}

	/// <summary>
	/// Находится ли фокус на холсте. Проверка по ссылке, а не по типу:
	/// холст в приложении один, и подменять его нечем.
	/// </summary>
	private bool IsCanvasFocused()
	{
		return _mainView != null && _mainView.Focused;
	}

	/// <summary>
	/// Что разрешено во время набора текста.
	///
	/// Только файловые команды и справка. Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z,
	/// Ctrl+Y и Ctrl+A принадлежат полю ввода: перехватив их, мы отняли бы
	/// у пользователя правку текста.
	/// </summary>
	private static bool IsAllowedWhileTyping(Keys keyData) => keyData switch
	{
		Keys.Control | Keys.N => true,
		Keys.Control | Keys.O => true,
		Keys.Control | Keys.S => true,
		Keys.Control | Keys.Shift | Keys.S => true,
		Keys.Control | Keys.I => true,
		Keys.Control | Keys.E => true,
		Keys.Control | Keys.P => true,
		Keys.F1 => true,
		_ => false
	};


	/// <summary>
	/// Находится ли фокус в поле ввода.
	///
	/// ActiveControl вложенных контейнеров приходится обходить вручную:
	/// у формы он указывает на панель, а не на поле внутри неё.
	/// Отдельная проверка для панели свойств — она рисует свои контролы сама,
	/// и WinForms о её текстовых полях ничего не знает.
	/// </summary>
	private bool IsTextEntryFocused()
	{
		Control? focused = this;

		while (focused is ContainerControl container && container.ActiveControl != null)
			focused = container.ActiveControl;

		if (focused is SkPropertyPanelControl panel)
			return panel.IsTextEditing;

		// Панель анимации тоже рисует свои поля сама: набор числа кадров
		// не должен переключать инструменты.
		if (focused is AnimationPanel animPanel)
			return animPanel.IsTextEditing;

		// Выпадающий список без поля ввода клавиатуру не занимает: по букве
		// он лишь перескакивает на элемент, и инструмент важнее.
		if (focused is System.Windows.Forms.ComboBox combo)
			return combo.DropDownStyle != ComboBoxStyle.DropDownList;

		return focused is TextBoxBase || focused is NumericUpDown;
	}

	private bool TryHandleFileShortcut(Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Control | Keys.N: d_new_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.O: d_open_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.S: d_save_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.S: d_save_as_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.I: d_import_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.E: d_export_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.P: d_print_but_Click(this, EventArgs.Empty); return true;
			case Keys.F1: help_but_Click(this, EventArgs.Empty); return true;
			default: return false;
		}
	}

	private bool TryHandleEditShortcut(Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Control | Keys.Z: undo_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Y: redo_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.Z: redo_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.C: CopyShapeFormat(); return true;
			case Keys.Control | Keys.Shift | Keys.V: PasteShapeFormat(); return true;
			case Keys.Control | Keys.A: _mainView.SelectAllShapes(); return true;
			default: return false;
		}
	}

	/// <summary>
	/// Выбор инструмента. Одиночные буквы — только здесь, и только когда
	/// фокус не в поле ввода: проверка стоит в TryHandleShortcut.
	/// </summary>
	private bool TryHandleToolShortcut(Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Escape: s_cursor_but_Click(this, EventArgs.Empty); return true;
			case Keys.L: s_new_line_but_Click(this, EventArgs.Empty); return true;
			case Keys.B: s_new_curve_but_Click(this, EventArgs.Empty); return true;
			case Keys.T: s_new_text_but_Click(this, EventArgs.Empty); return true;
			case Keys.M: measure_distance_but_Click(this, EventArgs.Empty); return true;
			case Keys.N: s_curve_edit_but_Click(this, EventArgs.Empty); return true;
			case Keys.P: s_new_poly_but_Click(this, EventArgs.Empty); return true;
			case Keys.Shift | Keys.P: s_new_free_poly_but_Click(this, EventArgs.Empty); return true;
			case Keys.K: s_scissors_line_but_Click(this, EventArgs.Empty); return true;
			case Keys.Shift | Keys.K: s_scissors_curve_but_Click(this, EventArgs.Empty); return true;

			// У прямоугольника, окружности и эллипса кнопка составная: клавиша
			// включает тот вариант, что выбран в её списке последним. Так же
			// ведёт себя щелчок по самой кнопке.
			case Keys.R: _mainView.SetTool(_selectedRectangleTool); return true;
			case Keys.C: _mainView.SetTool(_selectedCircleTool); return true;
			case Keys.E: _mainView.SetTool(_selectedEllipseTool); return true;

			case Keys.Control | Keys.Shift | Keys.I: s_new_image_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.M: s_image_mode_but_Click(this, EventArgs.Empty); return true;
			default: return false;
		}
	}

	private bool TryHandleArrangeShortcut(Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Delete: DeleteSelectedShapes(); return true;

			case Keys.Control | Keys.G: s_group_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.G: s_ungroup_but_Click(this, EventArgs.Empty); return true;

			case Keys.Control | Keys.Shift | Keys.Up: s_bring_to_front_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.Down: s_send_back_but_Click(this, EventArgs.Empty); return true;

			case Keys.Shift | Keys.H: flip_horizontal_but_Click(this, EventArgs.Empty); return true;
			case Keys.Shift | Keys.V: flip_vertical_but_Click(this, EventArgs.Empty); return true;

			case Keys.Control | Keys.Right: rotate_CW_90_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Left: rotate_CCW_90_but_Click(this, EventArgs.Empty); return true;

			case Keys.Control | Keys.Alt | Keys.U: s_union_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Alt | Keys.S: s_substract_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Alt | Keys.I: s_intersect_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Alt | Keys.X: s_exclude_xor_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Alt | Keys.C: s_combine_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Q: s_convert_to_curve_but_Click(this, EventArgs.Empty); return true;

			case Keys.Left: NudgeSelection(-NudgeStep, 0f); return true;
			case Keys.Right: NudgeSelection(NudgeStep, 0f); return true;
			case Keys.Up: NudgeSelection(0f, -NudgeStep); return true;
			case Keys.Down: NudgeSelection(0f, NudgeStep); return true;

			case Keys.Shift | Keys.Left: NudgeSelection(-NudgeStepLarge, 0f); return true;
			case Keys.Shift | Keys.Right: NudgeSelection(NudgeStepLarge, 0f); return true;
			case Keys.Shift | Keys.Up: NudgeSelection(0f, -NudgeStepLarge); return true;
			case Keys.Shift | Keys.Down: NudgeSelection(0f, NudgeStepLarge); return true;

			default: return false;
		}
	}

	/// <summary>
	/// Правка узлов. Клавиши действуют только в режиме узлов: цифры 1, 2 и 3
	/// в обычном режиме должны остаться свободными.
	/// </summary>
	private bool TryHandleNodeShortcut(Keys keyData)
	{
		if (Mode != EditorMode.Curve)
			return false;

		switch (keyData)
		{
			case Keys.D1:
			case Keys.NumPad1: nodes_straight_but_Click(this, EventArgs.Empty); return true;

			case Keys.D2:
			case Keys.NumPad2: node_tangent_but_Click(this, EventArgs.Empty); return true;

			case Keys.D3:
			case Keys.NumPad3: node_sharp_but_Click(this, EventArgs.Empty); return true;

			case Keys.Control | Keys.J: nodes_merge_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.J: nodes_split_but_Click(this, EventArgs.Empty); return true;

			default: return false;
		}
	}

	private bool TryHandleViewShortcut(Keys keyData)
	{
		switch (keyData)
		{
			case Keys.Control | Keys.D0: v_scale_all_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.D1: v_scale_page_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.D0: v_scale_selected_but_Click(this, EventArgs.Empty); return true;

			case Keys.Control | Keys.R: v_show_rulers_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Oemcomma: s_show_guidelines_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.D3: page_grid_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.N: v_snap_but_Click(this, EventArgs.Empty); return true;
			case Keys.Control | Keys.Shift | Keys.D: v_dynamic_guidelines_but_Click(this, EventArgs.Empty); return true;

			default: return false;
		}
	}

	/// <summary>Шаг сдвига стрелками, в единицах страницы.</summary>
	private const float NudgeStep = 0.5f;

	/// <summary>Крупный шаг сдвига, по Shift.</summary>
	private const float NudgeStepLarge = 5f;

	/// <summary>
	/// Сдвиг выделения стрелками.
	///
	/// Ключ слияния привязан к набору фигур, поэтому серия нажатий по одному
	/// и тому же выделению остаётся одним шагом отмены, а сдвиг другой фигуры
	/// начинает новый.
	/// </summary>
	private void NudgeSelection(float dx, float dy)
	{
		var shapes = _mainView.Selection.Where(s => !s.IsLocked).ToList();

		if (shapes.Count == 0)
			return;

		// Ключом служит сама первая фигура вместе с числом выделенных: разное
		// выделение даёт разный ключ и, значит, отдельный шаг.
		var mergeKey = (shapes[0], shapes.Count, "Nudge");

		var action = TransformShapesAction.Capture("Nudge", shapes, mergeKey);

		foreach (var shape in shapes)
			shape.MoveByWorld(dx, dy);

		action.CaptureAfter();

		if (action.HasChanges())
			CurrentUndoStack()?.Push(action);
		else
			action.Discard();

		cur_doc.need_save = true;

		sel.NotifyGeometryChanged();
		_mainView.Invalidate();
	}
	#endregion

	private void InitPageData()
	{
		if (cur_doc == null || cur_doc.pages.Count == 0 || cur_doc.ActivePage == null)
		{
			page_paper_size_combo.Enabled = false;
			page_props_but.Enabled = false;
			page_size_w_nud.Enabled = false;
			page_size_h_nud.Enabled = false;
			page_uom_combo.Enabled = false;
			page_zero_x_nud.Enabled = false;
			page_zero_y_nud.Enabled = false;
			page_vertical_but.Enabled = false;
			page_horizontal_but.Enabled = false;
			page_print_borders_but.Enabled = false;
			page_grid_but.Enabled = false;
			return;
		}
		page_paper_size_combo.Enabled = true;
		page_props_but.Enabled = true;
		page_size_w_nud.Enabled = true;
		page_size_h_nud.Enabled = true;
		page_uom_combo.Enabled = true;
		page_zero_x_nud.Enabled = true;
		page_zero_y_nud.Enabled = true;
		page_vertical_but.Enabled = true;
		page_horizontal_but.Enabled = true;
		page_print_borders_but.Enabled = true;
		page_grid_but.Enabled = true;

		page_paper_size_combo.DropDownStyle = ComboBoxStyle.DropDownList;
		page_uom_combo.DropDownStyle = ComboBoxStyle.DropDownList;
		page_paper_size_combo.SelectedIndexChanged -= page_paper_size_combo_SelectedIndexChanged;
		page_size_w_nud.ValueChanged -= page_size_w_nud_ValueChanged;
		page_size_h_nud.ValueChanged -= page_size_h_nud_ValueChanged;
		page_uom_combo.SelectedIndexChanged -= page_uom_combo_SelectedIndexChanged;

		page_paper_size_combo.Items.Clear();
		for (int i = 0; i < Settings.paper_formats.Count; i++)
			page_paper_size_combo.Items.Add(Settings.paper_formats[i].title);
		page_paper_size_combo.SelectedIndex = Settings.paper_formats.IndexOf(CurrentPage.paper_format);

		page_uom_combo.Items.Clear();
		for (int i = 0; i < Settings.units.Count; i++)
			page_uom_combo.Items.Add(Settings.units[i].title);
		page_uom_combo.SelectedIndex = Settings.units.IndexOf(CurrentPage.uom);

		page_size_w_nud.Minimum = (decimal)0.01;
		page_size_w_nud.Maximum = 1000000;
		page_size_h_nud.Minimum = (decimal)0.01;
		page_size_h_nud.Maximum = 1000000;
		page_zero_x_nud.Minimum = -1000000;
		page_zero_x_nud.Maximum = 1000000;
		page_zero_y_nud.Minimum = -1000000;
		page_zero_y_nud.Maximum = 1000000;

		page_size_w_nud.Value = (decimal)CurrentPage.size_uom.Width;
		page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
		SetButtonChecked(page_lock_ratio_but, CurrentPage.LockRatio);
		// Через SyncPageZeroEditors: здесь стоял ПЛЮС, хотя во всех
		// остальных местах величина берётся с минусом, и при открытии
		// документа со сдвинутым нулём поля показывали его зеркально.
		SyncPageZeroEditors();
		SetButtonChecked(page_vertical_but, CurrentPage.Orientation == PageOrientation.Vertical);
		SetButtonChecked(page_horizontal_but, CurrentPage.Orientation == PageOrientation.Horizontal);
		SetButtonChecked(page_print_borders_but, CurrentPage.PrintBordersEnabled);
		SetButtonChecked(page_grid_but, CurrentPage.GridEnabled);

		page_paper_size_combo.SelectedIndexChanged += page_paper_size_combo_SelectedIndexChanged;
		page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
		page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;
		page_uom_combo.SelectedIndexChanged += page_uom_combo_SelectedIndexChanged;
	}

	private void InitColorPanel()
	{
		colors_panel = new ColorsPanel()
		{
			Dock = DockStyle.Right,
			Location = new Point(1299, 90),
			Margin = new Padding(0),
			Name = "colors_panel",
			Size = new Size(28, 720),
		};
		Controls.Add(colors_panel);
	}

	private void InitImageToolbar()
	{
		i_tool_shape_combo.SelectedIndexChanged -= i_tool_shape_combo_SelectedIndexChanged;
		i_tool_shape_combo.SelectedValue = (int)Settings.image_tool_shape;
		i_tool_shape_combo.SelectedIndexChanged += i_tool_shape_combo_SelectedIndexChanged;

		i_tool_size_nud.ValueChanged -= i_tool_size_nud_ValueChanged;
		i_tool_size_nud.Value = (decimal)Settings.image_tool_size;
		i_tool_size_nud.ValueChanged += i_tool_size_nud_ValueChanged;

		i_size_lock_check.CheckedChanged -= i_size_lock_check_CheckedChanged;
		i_size_lock_check.Checked = Settings.image_tool_size_lock;
		i_size_lock_check.CheckedChanged += i_size_lock_check_CheckedChanged;

		i_tool_strength_nud.ValueChanged -= i_tool_strength_nud_ValueChanged;
		i_tool_strength_nud.Value = (decimal)Settings.image_tool_strength;
		i_tool_strength_nud.ValueChanged += i_tool_strength_nud_ValueChanged;

		i_tool_tolerance_nud.ValueChanged -= i_tool_tolerance_nud_ValueChanged;
		i_tool_tolerance_nud.Value = (decimal)Settings.image_tool_tolerance;
		i_tool_tolerance_nud.ValueChanged += i_tool_tolerance_nud_ValueChanged;

		i_tool_smooth_nud.ValueChanged -= i_tool_smooth_nud_ValueChanged;
		i_tool_smooth_nud.Value = (decimal)Settings.image_tool_smooth;
		i_tool_smooth_nud.ValueChanged += i_tool_smooth_nud_ValueChanged;
	}

	private void InitScaleCombo()
	{
		v_scale_combo.Items.Clear();
		v_scale_combo.Items.Add("100%");
		v_scale_combo.Items.Add("75%");
		v_scale_combo.Items.Add("50%");
		v_scale_combo.Items.Add("25%");
		v_scale_combo.Items.Add("10%");
		v_scale_combo.Items.Add("125%");
		v_scale_combo.Items.Add("150%");
		v_scale_combo.Items.Add("175%");
		v_scale_combo.Items.Add("200%");
		v_scale_combo.Items.Add("All");
		UpdateScaleCombo();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);

		Settings.SetupUnits();
		Settings.SetupPaperFormats();
		PatternLibrary.Initialize();

		// Опрос принтеров и их форматов — в фоне, при запуске.
		// Раньше это делал диалог печати в момент открытия, и пользователь
		// ждал сетевые очереди именно тогда, когда собирался печатать.
		// Рамке печатаемой области поля тоже нужны заранее.
		PrinterCatalog.BeginInitialize();

		// Нажатие любой кнопки прочих панелей гасит режим растрового
		// редактирования.
		HookImageModeExitOnToolbars();

		// Визуальное разделение секций на панелях инструментов.
		InsertToolbarSeparators();
		TextureLibrary.Initialize();

		cur_doc = new Document();
		sel = new SelectionManager();
		SkUiTheme.SetTheme(SkThemeKind.Light);

		_mainView = new MainView
		{
			Dock = DockStyle.Fill,
			mainForm = this
		};
		CurrentPage = cur_doc.pages[0];
		_mainView.SetPage(cur_doc.pages[0]);
		HookUndoStack();

		_propPanel = new SkPropertyPanelControl();
		_mainView.SelectionChanged += (_, __) =>
		{
			// Передаётся всё выделение, а не только одиночная фигура:
			// панель свойств строится по этому списку, и при выделении
			// нескольких объектов он оказывался пустым.
			sel.Set(_mainView.Selection);
		};

		_propPanel.RequestCanvasRefresh += () =>
		{
			CurrentPage.SortLayers();
			_mainView.Invalidate();
		};

		_propPanel.ShapePickRequested += target =>
		{
			SelectOtherShapeTool.PickTarget = target;
			_mainView.SetTool(ShapeToolKind.SelectOtherShape);
		};

		Controls.Add(_mainView);
		_mainView.BringToFront();
		Controls.Add(_propPanel);
		InitPageData();

		// Сетка: холст занимает верхнюю левую ячейку, полосы — правую колонку
		// и нижнюю строку. Полосы становятся соседями холста, а не его детьми:
		// SKGLControl выводит кадр OpenGL на всю свою поверхность и затирает
		// любые дочерние оконные контролы.
		var host = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};

		host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SystemInformation.VerticalScrollBarWidth));
		host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		host.RowStyles.Add(new RowStyle(SizeType.Absolute, SystemInformation.HorizontalScrollBarHeight));

		host.Controls.Add(_mainView, 0, 0);
		host.Controls.Add(_mainView.VerticalScrollBar, 1, 0);
		host.Controls.Add(_mainView.HorizontalScrollBar, 0, 1);

		Controls.Add(host);

		// Панель анимации садится под холст, над полосой страниц: она
		// добавлена в Controls РАНЬШЕ полосы, а стыковка идёт с конца
		// коллекции, поэтому самый край забирает полоса страниц.
		//
		// Дочерним контролом MainView панель быть не может: SKGLControl
		// затирает кадром OpenGL всё, что лежит поверх него.
		_animPanel = new AnimationPanel();
		_animPanel.RequestCanvasRefresh += () =>
		{
			CurrentPage?.SortLayers();
			_mainView?.Invalidate();
		};

		Controls.Add(_animPanel);

		// true — панель встаёт свёрнутой: при запуске место нужнее холсту.
		_animPanel.SetPage(CurrentPage, documentChanged: true);

		// Полоса страниц примыкает к холсту снизу, над строкой состояния.
		_pageTabs = new PageTabsControl();
		_pageTabs.PageActivated += OnPageTabActivated;
		_pageTabs.PagesChanged += OnPageTabsChanged;

		Controls.Add(_pageTabs);
		_pageTabs.SetDocument(cur_doc);

		// Порядок пристыковки обратен порядку в Controls: кто последний
		// в коллекции, тот пристыковывается первым и занимает самый край.
		// Строка состояния уходит вниз до самого края, полоса страниц садится
		// над ней, а холст с Dock.Fill забирает остаток.
		host.BringToFront();

		InitScaleCombo();

		_mainView.SelectionChanged += MainView_SelectionChanged;
		_mainView.ScaleChanged += UpdateScaleCombo;

		// Подписка именно на SelectionManager, а не на MainView.SelectionChanged:
		// выделение меняют и другие места кода (вставка, undo, инструменты),
		// и они уведомляют через sel.
		sel.SelectionChanged += UpdateSelectionToolbars;

		// need_save — обычное поле, которое присваивают из десятков мест без
		// события. Опросить его иначе как по таймеру нельзя. Системный буфер
		// сюда не входит: о нём сообщает WM_CLIPBOARDUPDATE.
		_commandStateTimer = new System.Windows.Forms.Timer { Interval = 250 };
		_commandStateTimer.Tick += (_, __) => UpdateCommandButtons();
		_commandStateTimer.Start();

		_clipboardListenerRegistered = AddClipboardFormatListener(Handle);
		RefreshSystemClipboardState();

		UpdateSelectionToolbars();

		colors_panel.SendToBack();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		base.OnFormClosing(e);

		if (cur_doc != null && !cur_doc.Close())
		{
			e.Cancel = true;
			return;
		}

		_commandStateTimer?.Stop();

		if (_clipboardListenerRegistered)
		{
			RemoveClipboardFormatListener(Handle);
			_clipboardListenerRegistered = false;
		}
	}

	/// <summary>
	/// Имя открытого документа в заголовке окна.
	///
	/// Обновляется из UpdateCommandButtons, а не из мест смены документа.
	/// Причина та же, по которой там опрашивается need_save: имя файла
	/// меняется не только при создании и открытии, но и при «Сохранить
	/// как», и при импорте, и при откате — то есть в тех же десятках мест,
	/// которые события не поднимают. Расставлять вызовы по всем ним значит
	/// однажды пропустить один и получить заголовок, отставший от файла.
	///
	/// Расширение опускается: оно у всех документов одно и в заголовке
	/// не несёт сведений.
	/// </summary>
	private void UpdateWindowTitle()
	{
		// Закрытый документ и новый несохранённый — разные вещи, и в
		// заголовке они должны различаться.
		if (cur_doc == null)
		{
			if (Text != "VDraw")
				Text = "VDraw";

			return;
		}

		string name = cur_doc.IsNewDocument
			? L.T("Untitled")
			: Path.GetFileNameWithoutExtension(cur_doc.file_path_name);

		// Звёздочка — общепринятая пометка несохранённого. Ставится перед
		// именем, а не после: в панели задач заголовок обрезается справа,
		// и пометка в конце пропала бы первой.
		string marker = cur_doc?.need_save == true ? "* " : string.Empty;

		string title = $"{marker}{name} — VDraw";

		// Присваивание Text перерисовывает заголовок и мигает полосой
		// в панели задач, а метод вызывается четыре раза в секунду.
		if (Text != title)
			Text = title;
	}

	private void HookUndoStack()
	{
		// Отписка от прежнего стека: иначе кнопки продолжат слушать историю
		// закрытого документа, а сам документ не будет собран сборщиком мусора.
		if (_undoStack != null && _undoChangedHandler != null)
			_undoStack.Changed -= _undoChangedHandler;

		_undoStack = _mainView?.Undo;
		_undoChangedHandler = null;

		if (_undoStack == null)
		{
			undo_but.Enabled = false;
			redo_but.Enabled = false;
			return;
		}

		var undo = _undoStack;

		void Refresh()
		{
			undo_but.Enabled = undo.CanUndo;
			redo_but.Enabled = undo.CanRedo;

			toolTip.SetToolTip(undo_but, undo.CanUndo ? $"Undo: {undo.NextUndoName}" : "Undo");
			toolTip.SetToolTip(redo_but, undo.CanRedo ? $"Redo: {undo.NextRedoName}" : "Redo");
		}

		_undoChangedHandler = Refresh;
		undo.Changed += Refresh;

		Refresh();
	}

	/// <summary>
	/// Пересобрать панель свойств под текущее выделение. В отличие от
	/// UpdatePropertiesPanel, которая только перерисовывает существующие
	/// контролы, здесь контролы создаются заново — нужно, когда изменилось
	/// то, что попадает в них при создании.
	/// </summary>
	public void RebuildPropertyPanel()
	{
		_propPanel?.RebuildForSelection();
	}

	#endregion

	#region Form, view and panels update methods
	public void UpdateView()
	{
		_mainView?.Invalidate();
	}

	public void UpdatePropertiesPanel()
	{
		_propPanel.Invalidate();
	}

	private void MainView_SelectionChanged(object? sender, EventArgs e)
	{
		ApplyEditorModeFromSelection();
		UpdateCommandButtons();
	}

	/// <summary>
	/// Обновляет панели инструментов, применимые только к одной выделенной
	/// фигуре определённого типа.
	///
	/// Панель растрового редактора больше не прячется. Появление и
	/// исчезновение полосы высотой в тулбар сдвигало холст под курсором
	/// на каждую смену выделения, и по ней приходилось гадать, доступно
	/// редактирование картинки или нет. Теперь она на месте всегда,
	/// а кнопки гаснут, когда выделено не изображение — состояние видно
	/// сразу и ничего не прыгает.
	///
	/// Панель узлов по-прежнему скрывается: она к чужому типу фигуры
	/// неприменима целиком.
	/// </summary>
	private void UpdateSelectionToolbars()
	{
		var selected = sel?.Selected;
		Shape? single = selected != null && selected.Count == 1 ? selected[0] : null;

		// ImageShape наследует CurveShape, поэтому проверка на изображение
		// должна идти первой везде, где типы различаются.
		bool isSingleImage = single is ImageShape;
		bool isSingleCurve = single is CurveShape;

		SetPanelVisibleForSelection(true, isSingleImage || isSingleCurve);
		UpdateImageToolsEnabled();
		SyncActualResolutionCheck();
		UpdateCommandButtons();
	}

	/// <summary>
	/// Сводит кнопки-образцы цвета с тем, что они показывают.
	///
	/// Раньше их значения ставились только в обработчиках диалогов,
	/// поэтому цвет фона страницы оставался белым на кнопке после
	/// открытия документа и после перехода на другую страницу,
	/// а цвета кисти расходились с настройками при любом изменении мимо
	/// диалога. Присваивание идёт через PreviewColor, который меняет
	/// картинку только при действительном отличии, поэтому вызов
	/// из периодического обновления кнопок ничего не мигает.
	/// </summary>
	private void SyncColorButtons()
	{
		s_line_color_but.SetSKColor(Settings.shape_default_line_color);
		s_fill_color_but.SetSKColor(Settings.shape_default_fill_color);
		i_fore_color_but.SetSKColor(Settings.image_paint_fore_color);
		i_back_color_but.SetSKColor(Settings.image_paint_back_color);

		if (CurrentPage != null)
			page_background_color_but.SetSKColor(CurrentPage.page_bg_color);
	}

	/// <summary>
	/// Гасит панель растрового редактора, когда выделено не изображение.
	///
	/// Вызывается и по смене выделения, и из UpdateCommandButtons: при
	/// открытии документа ApplyDocumentPresence включает всё дерево
	/// контролов разом, и без повторного вызова кнопки остались бы
	/// доступными при пустом выделении.
	///
	/// Заодно гасится и сам режим: инструмент мог остаться включённым
	/// с прошлой картинки, и рисовать им, когда её больше не выделено,
	/// нечем и некуда.
	/// </summary>
	private void UpdateImageToolsEnabled()
	{
		var selected = sel?.Selected;
		bool isSingleImage = selected != null && selected.Count == 1 && selected[0] is ImageShape;

		SetChildrenEnabled(image_tools_panel, isSingleImage);

		if (!isSingleImage && _mainView != null && _mainView.IsImageEditMode)
			ExitImageToolMode();
	}

	/// <summary>
	/// Проставляет Enabled всем потомкам контейнера, не трогая его самого.
	///
	/// Сам контейнер остаётся включённым: выключенная панель красит своих
	/// детей серым независимо от их собственных флагов, и вернуть им вид
	/// доступных было бы уже нечем.
	/// </summary>
	private static void SetChildrenEnabled(Control parent, bool enabled)
	{
		if (parent == null)
			return;

		foreach (Control child in parent.Controls)
		{
			if (child.HasChildren)
			{
				SetChildrenEnabled(child, enabled);
				SetEnabled(child, true);
				continue;
			}

			SetEnabled(child, enabled);
		}
	}

	private void SetPanelVisibleForSelection(bool imagePanel, bool nodesFlow)
	{
		// Где холст был до перестроения. Сравнение экранных координат
		// точнее, чем высота панели: к ней добавляются отступы, а панелей
		// может измениться сразу две.
		Point before = _mainView != null
			? _mainView.PointToScreen(Point.Empty)
			: Point.Empty;

		// Двигать курсор имеет смысл, только если он над холстом. Если
		// выделение сменилось щелчком по панели слоёв или кнопке, рывок
		// указателя в сторону холста был бы необъяснимым.
		bool cursorOverCanvas = _mainView != null
			&& _mainView.IsHandleCreated
			&& _mainView.ClientRectangle.Contains(
				_mainView.PointToClient(System.Windows.Forms.Cursor.Position));

		// Значение присваивается напрямую, без предварительной проверки
		// panel.Visible: геттер возвращает эффективную видимость с учётом
		// родителя, и до показа формы он даёт false для всех панелей. Проверка
		// «уже скрыта — не трогаем» на старте пропустила бы скрытие, и панели
		// всплыли бы при первом показе окна.
		SuspendLayout();

		image_tools_panel.Visible = imagePanel;
		c_nodes_tools_flow.Visible = nodesFlow;

		ResumeLayout(true);

		if (cursorOverCanvas)
			KeepCursorOverSamePagePoint(before);
	}

	/// <summary>
	/// Сдвигает курсор вслед за холстом.
	///
	/// Панели инструментов появляются и исчезают по смене выделения, и
	/// холст при этом съезжает. Курсор остаётся на месте, под ним
	/// оказывается другая точка страницы — и фигура, которую в этот момент
	/// тащат, прыгает на высоту панели.
	///
	/// Сдвинув указатель на то же расстояние, мы оставляем под ним ту же
	/// точку листа: в клиентских координатах холста ничего не изменилось,
	/// перетаскивание продолжается без скачка.
	/// </summary>
	private void KeepCursorOverSamePagePoint(Point canvasScreenPositionBefore)
	{
		if (_mainView == null || !_mainView.IsHandleCreated)
			return;

		Point after = _mainView.PointToScreen(Point.Empty);

		int dx = after.X - canvasScreenPositionBefore.X;
		int dy = after.Y - canvasScreenPositionBefore.Y;

		if (dx == 0 && dy == 0)
			return;

		var position = System.Windows.Forms.Cursor.Position;

		System.Windows.Forms.Cursor.Position = new Point(position.X + dx, position.Y + dy);
	}

	/// <summary>
	/// Есть ли выделенная область пикселей внутри картинки. Для копирования и
	/// удаления это равноценно выделенной фигуре: пользователь что-то выбрал.
	/// </summary>
	private bool HasImagePixelSelection()
	{
		var session = _mainView?.ImageEditSession;
		return session != null && (session.HasSelection || session.HasFloatingSelection);
	}

	/// <summary>
	/// Пересчёт доступности команд. Один метод на все кнопки: их состояния
	/// зависят от пересекающихся условий (выделение, буфер, признак
	/// изменений), и разнесённые по обработчикам присваивания разъезжались бы.
	/// </summary>
	private void UpdateCommandButtons()
	{
		// Состояние «нет документа» перекрывает всё остальное: считать
		// доступность операций над выделением, когда выделять негде,
		// незачем, а результат такого счёта всё равно был бы затёрт.
		if (cur_doc == null)
		{
			ApplyDocumentPresence(false);
			UpdateWindowTitle();
			return;
		}

		ApplyDocumentPresence(true);

		// После ApplyDocumentPresence: тот включает всё дерево разом,
		// и панель картинки надо погасить обратно.
		UpdateImageToolsEnabled();
		SyncColorButtons();

		var selection = _mainView?.Selection;
		int count = selection?.Count ?? 0;

		bool anyShape = count > 0;
		bool anySelection = anyShape || HasImagePixelSelection();

		UpdateBooleanButtons();

		// Конвертация нужна только там, где есть что конвертировать: набор
		// из одних кривых и картинок операцию не меняет.
		s_convert_to_curve_but.Enabled =
			anyShape && selection!.Any(CanConvertToCurve);

		s_group_but.Enabled = count >= 2;

		s_ungroup_but.Enabled = count == 1 && selection![0] is GroupShape;

		copy_but.Enabled = anySelection;
		delete_but.Enabled = anySelection;
		paste_but.Enabled = CanPaste();
		UpdateFormatButton();

		d_export_but.Enabled = anyShape;
		d_import_but.Enabled = true;

		bool needSave = cur_doc?.need_save == true;
		d_save_but.Enabled = needSave;
		d_save_as_but.Enabled = needSave;

		UpdateWindowTitle();
	}

	public void SetEditorMode(EditorMode mode)
	{
		_mode = mode;
		ApplyEditorModeUI();
		_mainView.Focus();
	}

	public void ApplyEditorModeFromSelection()
	{
		if (_mainView == null)
			return;

		var shape = _mainView.SelectedShape;

		// Curve и Image по выделению НЕ переключаются вовсе.
		//
		// Выделение меняется по нажатию кнопки мыши, и любое правило вида
		// «выделили кривую — включили режим узлов» срабатывало бы ещё до
		// отпускания, то есть режим менялся бы посреди начатого
		// перетаскивания. Вход и выход задаются только явными действиями:
		// двойным щелчком, кнопками панели, Escape и щелчком мимо фигуры
		// (последнее — в MainView.OnMouseClick).
		if (Mode == EditorMode.Curve || Mode == EditorMode.Image)
			return;

		// Текст — исключение: без своего режима его нельзя редактировать,
		// и отдельного жеста для входа в него нет.
		// Проверка обязана идти до CurveShape: TextShape от неё наследуется.
		if (shape is TextShape)
		{
			SetEditorMode(EditorMode.Text);
			return;
		}

		SetEditorMode(EditorMode.Shape);
	}

	private void ApplyEditorModeUI()
	{
		if (_mainView == null)
			return;
		_mainView.IsCurveEditMode = Mode == EditorMode.Curve;

		switch (Mode)
		{
			case EditorMode.Curve:
				break;

			case EditorMode.Image:
				break;

			case EditorMode.Text:
				break;

			case EditorMode.Shape:
			default:
				break;
		}
	}

	/// <summary>
	/// Что остаётся доступным при закрытом документе.
	///
	/// Перечислены ИМЕНА контролов и пунктов меню. Проверьте их по своему
	/// файлу дизайнера: имя, которого там нет, просто не найдётся, и пункт
	/// молча погаснет вместе с остальными.
	/// </summary>
	private static readonly HashSet<string> _worksWithoutDocument = new(StringComparer.OrdinalIgnoreCase)
	{
		"d_open_but",
		"d_new_but",
		"menu_file_new",
		"menu_file_open",
		"menu_file_exit",

		"menu_about",
		"menu_manual",
	};

	/// <summary>
	/// Приводит окно в состояние «документ есть» или «документа нет».
	///
	/// Без документа почти всё бессмысленно: рисовать некуда, сохранять
	/// нечего, свойства не у чего показывать. Оставлены только открытие,
	/// выход и справка.
	/// </summary>
	private void ApplyDocumentPresence(bool hasDocument)
	{
		// Ничего не изменилось — дерево не трогаем. Проверка обязана быть
		// здесь, а не у вызывающего: точек вызова несколько, и забыть её
		// в одной из них означало бы вернуть мигание.
		if (_documentPresenceApplied == hasDocument)
			return;

		_documentPresenceApplied = hasDocument;

		SetControlsEnabled(this, hasDocument);
		SetMenuEnabled(menuStrip1.Items, hasDocument);

		// SetControlsEnabled только что включил undo и redo вместе со всем
		// остальным, а их состояние считается не по наличию документа,
		// а по содержимому стека истории. Без этого вызова кнопки остаются
		// доступными на пустой истории до первого же действия.
		_undoChangedHandler?.Invoke();

		// Панели не выключаются, а прячутся: выключенная панель свойств
		// осталась бы серым столбцом в пол-окна и выглядела бы поломкой,
		// а не отсутствием документа.
		if (_propPanel != null)
			_propPanel.Visible = hasDocument;

		if (_animPanel != null)
			_animPanel.Visible = hasDocument;

		if (_pageTabs != null)
			_pageTabs.Visible = hasDocument;

		if (colors_panel != null)
			colors_panel.Visible = hasDocument;
	}

	/// <summary>
	/// Выключает всё, кроме перечисленного в _worksWithoutDocument.
	///
	/// Контейнеры остаются включёнными всегда: выключенная панель гасит
	/// своих детей независимо от их собственного состояния, и кнопка
	/// открытия погасла бы вместе с панелью, на которой стоит.
	/// </summary>
	private void SetControlsEnabled(Control root, bool enabled)
	{
		foreach (Control control in root.Controls)
		{
			// Меню обрабатывается отдельно: у его пунктов своё дерево.
			if (control is MenuStrip || control is StatusStrip)
				continue;

			if (_worksWithoutDocument.Contains(control.Name))
			{
				SetEnabled(control, true);
				continue;
			}

			if (control.HasChildren)
			{
				SetControlsEnabled(control, enabled);
				SetEnabled(control, true);
				continue;
			}

			SetEnabled(control, enabled);
		}
	}

	/// <summary>
	/// Присваивает Enabled только при действительном изменении.
	///
	/// Сеттер WinForms сравнивает не флаг контрола, а ЭФФЕКТИВНОЕ значение
	/// с учётом родителей. У контрола внутри выключенной панели флаг может
	/// стоять, а эффективное значение быть false, и присваивание true
	/// каждый раз выглядит для него как изменение: летит EnabledChanged,
	/// за ним Invalidate — и кнопка перерисовывается на ровном месте.
	/// </summary>
	private static void SetEnabled(Control control, bool enabled)
	{
		if (control.Enabled != enabled)
			control.Enabled = enabled;
	}

	/// <summary>
	/// Выключает пункты меню. Возвращает признак того, что внутри
	/// осталось хоть что-то доступное.
	///
	/// По нему гасится и сам заголовок раздела: меню, все пункты которого
	/// недоступны, лучше закрыть целиком — иначе пользователь открывает
	/// его и видит список из одних серых строк.
	/// </summary>
	private bool SetMenuEnabled(ToolStripItemCollection items, bool enabled)
	{
		bool anyEnabled = false;

		foreach (ToolStripItem item in items)
		{
			if (item is ToolStripSeparator)
				continue;

			if (item is ToolStripMenuItem menu && menu.HasDropDownItems)
			{
				bool inside = SetMenuEnabled(menu.DropDownItems, enabled);

				if (menu.Enabled != inside)
					menu.Enabled = inside;

				anyEnabled |= inside;

				continue;
			}

			bool itemEnabled = enabled || _worksWithoutDocument.Contains(item.Name ?? string.Empty);

			if (item.Enabled != itemEnabled)
				item.Enabled = itemEnabled;

			anyEnabled |= itemEnabled;
		}

		return anyEnabled;
	}

	#endregion

	#region Menu items
	private void d_new_but_Click(object sender, EventArgs e)
	{
		Document doc_new = new Document();

		using (DocPropertiesForm dpf = new DocPropertiesForm(doc_new))
		{
			if (dpf.ShowDialog() != DialogResult.OK)
				return;
		}

		// Незакрытый документ спрашивает про сохранение сам. Отказ
		// пользователя отменяет создание: раньше здесь вызывался
		// ReleaseResources, который просто выбрасывал прежний документ
		// вместе с историей, и несохранённая работа пропадала молча.
		//
		// Порядок именно такой: сначала свойства нового документа, потом
		// вопрос о сохранении прежнего. Close освобождает ресурсы, и задай
		// он вопрос раньше, отказ от диалога свойств оставил бы текущий
		// документ уже разобранным.
		if (cur_doc != null && !cur_doc.Close())
			return;

		cur_doc = doc_new;

		CurrentPage = doc_new.pages[0];
		doc_new.ActivePage = CurrentPage;
		_mainView.SetPage(doc_new.pages[0]);
		_animPanel?.SetPage(CurrentPage, documentChanged: true);

		_pageTabs?.SetDocument(doc_new);

		// История у нового документа своя — перепривязываем кнопки.
		HookUndoStack();

		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	/// <summary>
	/// Переключение страницы с полосы вкладок.
	///
	/// Document.ActivePage к этому моменту уже переставлен самим контролом —
	/// именно это поле уходит в файл как ActivePageIndex и возвращается
	/// при загрузке. Здесь остаётся подтянуть вид и панели.
	/// </summary>
	private void OnPageTabActivated(Page page)
	{
		if (_mainView == null || page == null)
			return;

		CurrentPage = page;
		_mainView.SetPage(page);
		_animPanel?.SetPage(page);

		// Выделение относилось к прежней странице и на новой бессмысленно.
		sel.Clear();

		InitPageData();
		_mainView.DocumentChanged();
		RebuildPropertyPanel();

		_mainView.Invalidate();
		_mainView.Focus();
	}

	/// <summary>Состав, порядок или названия страниц изменились.</summary>
	private void OnPageTabsChanged()
	{
		UpdateCommandButtons();
	}

	private void d_save_but_Click(object sender, EventArgs e)
	{
		cur_doc?.Save();
	}

	private void d_save_as_but_Click(object sender, EventArgs e)
	{
		cur_doc?.SaveAs();
	}

	private void menu_file_save_Click(object sender, EventArgs e)
	{
		cur_doc?.Save();
	}

	private void menu_file_save_as_Click(object sender, EventArgs e)
	{
		cur_doc?.SaveAs();
	}

	private void menu_file_export_Click(object sender, EventArgs e)
	{
		d_export_but_Click(null, null);
	}

	private void menu_file_print_Click(object sender, EventArgs e)
	{
		d_print_but_Click(null, null);
	}

	private void menu_file_close_Click(object sender, EventArgs e)
	{
		if (cur_doc == null)
			return;

		// Отказ от сохранения отменяет закрытие: иначе работа пропала бы
		// молча.
		if (!cur_doc.Close())
			return;

		cur_doc.ReleaseResources();
		cur_doc = null;

		// Ссылки на страницу закрытого документа обязаны обнулиться здесь.
		// Иначе холст продолжит рисовать её содержимое, а панели —
		// показывать свойства фигур из документа, которого больше нет.
		CurrentPage = null;
		sel.Clear();

		_mainView?.SetPage(null);
		_animPanel?.SetPage(null, documentChanged: true);
		_pageTabs?.SetDocument(null);

		UpdateCommandButtons();

		_mainView?.Invalidate();
	}

	private void menu_document_Click(object sender, EventArgs e)
	{
		if (cur_doc == null)
			return;
		PaperFormatData pfd = cur_doc.ClonePaperFormatData();
		using (DocPropertiesForm dpf = new DocPropertiesForm(pfd))
		{
			if (dpf.ShowDialog() == DialogResult.OK)
			{
				if (dpf.paper_changed)
				{
					cur_doc.SetFormatData(pfd);
					cur_doc.need_save = true;
				}
				_mainView.DocumentChanged();
				_mainView.Invalidate();
			}
		}
	}

	private void menu_page_Click(object sender, EventArgs e)
	{
		if (cur_doc == null || CurrentPage == null)
			return;
		using (DocPropertiesForm dpf = new DocPropertiesForm(CurrentPage))
		{
			if (dpf.ShowDialog() == DialogResult.OK)
			{
				if (dpf.paper_changed)
					cur_doc.need_save = true;
				_mainView.DocumentChanged();
				_mainView.Invalidate();
			}
		}
	}

	private void menu_obj_lines_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Line);

	private void menu_obj_curve_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Curve);

	private void menu_obj_rect_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Rectangle);

	private void menu_obj_rounded_rect_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.RoundedRectangle);

	private void menu_obj_circle2p_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Circle2P);

	private void menu_obj_circle3p_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Circle3P);

	private void menu_obj_ellispe3p_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Ellipse3P);

	private void menu_obj_ellipse5p_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Ellipse5P);

	private void menu_obj_poly_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Polygon);

	private void menu_obj_text_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Text);

	private void menu_obj_image_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Image);

	private void menu_obj_measurement_distance_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.Measure);

	/// <summary>
	/// Преобразование в кривую работает не с инструментом, а с выделением,
	/// поэтому пункт повторяет кнопку панели, а не вызывает SetTool.
	/// </summary>
	private void convertToCurveToolStripMenuItem_Click(object sender, EventArgs e) =>
		s_convert_to_curve_but_Click(sender, e);

	private void measureAngleToolStripMenuItem_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.MeasureAngle);

	private void menu_obj_chamfered_rect_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.ChamferedRectangle);

	private void menu_about_Click(object sender, EventArgs e)
	{
		using (AboutForm af = new AboutForm())
		{
			af.ShowDialog();
		}
	}

	private void menu_manual_Click(object sender, EventArgs e)
	{
		help_but_Click(null, null);
	}

	private void menu_settings_Click(object sender, EventArgs e)
	{
		using (SettingsForm sf = new SettingsForm())
		{
			// Подписка живёт ровно столько, сколько открыто окно.
			// Событие статическое, и постоянная подписка удерживала бы
			// форму в памяти; а держать её только на время диалога и
			// проще, и снимает вопрос об отписке совсем.
			//
			// Нужна она из-за кнопки «Применить»: та меняет настройки,
			// не закрывая окно, и без обновления по событию пользователь
			// увидел бы результат только после закрытия.
			SettingsForm.SettingsApplied += RefreshSettingsDependentUi;

			try
			{
				// С владельцем: иначе StartPosition = CenterParent
				// отсчитывается от экрана, и окно уезжает от главного.
				sf.ShowDialog(this);
			}
			finally
			{
				SettingsForm.SettingsApplied -= RefreshSettingsDependentUi;
			}
		}

		// Ещё раз после закрытия: по «ОК» настройки применяются, но
		// событие приходит до того, как окно освободит поверхность,
		// и последняя перерисовка холста пропала бы.
		RefreshSettingsDependentUi();
	}

	/// <summary>
	/// Приводит окно в согласие с настройками.
	///
	/// Перерисовки холста мало. Четыре кнопки-выключателя на панели
	/// показывают те же значения, что правит окно настроек, и кнопка,
	/// оставшаяся нажатой после выключения линеек, при следующем щелчке
	/// сработает наоборот — включит то, что пользователь только что убрал.
	///
	/// Превью цветов у кнопок обводки и заливки показывают значения
	/// по умолчанию для новых фигур, и их окно тоже меняет.
	/// </summary>
	private void RefreshSettingsDependentUi()
	{
		SetButtonChecked(v_show_rulers_but, Settings.show_rulers);
		SetButtonChecked(v_show_guidelines_but, Settings.show_guidelines);
		SetButtonChecked(v_snap_but, Settings.snap_objects);
		SetButtonChecked(v_dynamic_guidelines_but, Settings.dynamic_guidelines);

		s_line_color_but.PreviewColor = Color.FromArgb(
			(int)Settings.shape_default_line_color.Alpha,
			(int)Settings.shape_default_line_color.Red,
			(int)Settings.shape_default_line_color.Green,
			(int)Settings.shape_default_line_color.Blue);

		s_fill_color_but.PreviewColor = Color.FromArgb(
			(int)Settings.shape_default_fill_color.Alpha,
			(int)Settings.shape_default_fill_color.Red,
			(int)Settings.shape_default_fill_color.Green,
			(int)Settings.shape_default_fill_color.Blue);

		// Толщина линеек влияет на размер холста, а не только на его
		// содержимое: без пересчёта раскладки полосы перекроют лист.
		_mainView?.PerformLayout();
		_mainView?.Invalidate();
	}

	private void menu_file_exit_Click(object sender, EventArgs e)
	{
		Close();
	}
	#endregion

	#region Color management
	/// <summary>
	/// Цвет линии выделенных фигур — щелчок по палитре справа.
	///
	/// Изменение попадает в историю: щелчок по палитре правит документ
	/// ровно так же, как поле в панели свойств, и отменяться должен так же.
	/// Раньше стили менялись на месте, мимо истории, и Ctrl+Z после щелчка
	/// откатывал не цвет, а предыдущее действие — то есть уносил то, что
	/// пользователь менять не собирался.
	///
	/// Прозрачный цвет означает выключенную обводку, а не чёрную с нулевой
	/// альфой: иначе выключить линию с палитры было бы нечем.
	/// </summary>
	public void SetShapeDefaultLineColor(Color c)
	{
		if (cur_doc == null || CurrentPage == null || _mainView == null)
			return;
		if (sel.Selected == null || sel.Selected.Count == 0)
			return;

		var action = ShapeStyleAction.Capture("Line color", sel.Selected);

		bool use_stroke = c.A != 0;
		for (int i = 0; i < sel.Selected.Count; i++)
		{
			if (!sel.Selected[i].Stroke.Enabled && use_stroke)
				sel.Selected[i].Stroke.Enabled = true;
			if (!use_stroke && sel.Selected[i].Stroke.Enabled)
			{
				sel.Selected[i].Stroke.Enabled = false;
				continue;
			}
			sel.Selected[i].Stroke.Color = new SKColor(c.R, c.G, c.B, c.A);
		}

		CommitStyleChange(action);

		// Кнопка тулбара показывает цвет, которым рисуют дальше: палитра
		// задаёт его наравне с диалогом, и расходиться им нельзя.
		Settings.shape_default_line_color = new SKColor(c.R, c.G, c.B, c.A);
		s_line_color_but.SetSKColor(Settings.shape_default_line_color);

		_mainView.Invalidate();
		_propPanel.RebuildForSelection();
	}

	/// <summary>
	/// Цвет кисти растрового редактора — щелчок по палитре справа.
	///
	/// Метод был пустым, и палитра в режиме изображения не делала ничего:
	/// ColorsPanel.ApplyColor вызывает именно его, а рисование берёт цвет
	/// из Settings.image_paint_fore_color.
	/// </summary>
	public void SetImageDefaultFrontColor(Color c)
	{
		Settings.image_paint_fore_color = new SKColor(c.R, c.G, c.B, c.A);
		i_fore_color_but.SetSKColor(Settings.image_paint_fore_color);
	}

	/// <summary>
	/// Цвет заливки выделенных фигур — щелчок по палитре справа.
	/// Историю ведёт так же, как SetShapeDefaultLineColor.
	/// </summary>
	public void SetShapeDefaultFillColor(Color c)
	{
		if (cur_doc == null || CurrentPage == null || _mainView == null)
			return;
		if (sel.Selected == null || sel.Selected.Count == 0)
			return;

		var action = ShapeStyleAction.Capture("Fill color", sel.Selected);

		bool use_fill = c.A != 0;
		for (int i = 0; i < sel.Selected.Count; i++)
		{
			if (!sel.Selected[i].Fill.Enabled && use_fill)
				sel.Selected[i].Fill.Enabled = true;
			if (!use_fill && sel.Selected[i].Fill.Enabled)
			{
				sel.Selected[i].Fill.Enabled = false;
				continue;
			}
			sel.Selected[i].Fill.Kind = FillKind.Solid;
			sel.Selected[i].Fill.SolidColor = new SKColor(c.R, c.G, c.B, c.A);
		}

		CommitStyleChange(action);

		Settings.shape_default_fill_color = new SKColor(c.R, c.G, c.B, c.A);
		s_fill_color_but.SetSKColor(Settings.shape_default_fill_color);

		_mainView.Invalidate();
		_propPanel.RebuildForSelection();
	}

	/// <summary>
	/// Закрывает снимок стилей: кладёт его в историю, если что-то менялось,
	/// и выбрасывает, если нет.
	///
	/// Отдельный метод, потому что вызывается из обеих кнопок палитры
	/// и порядок шагов здесь важен. Discard обязателен: снимок держит копии
	/// стилей всех выделенных фигур, и повторные щелчки одним и тем же
	/// цветом иначе набивали бы историю пустыми шагами, каждый из которых
	/// ещё и занимает память.
	/// </summary>
	private void CommitStyleChange(ShapeStyleAction action)
	{
		action.CaptureAfter();

		if (!action.HasChanges())
		{
			action.Discard();
			return;
		}

		cur_doc!.Undo.Push(action);
		cur_doc.need_save = true;
	}

	/// <summary>
	/// Цвет фона растрового редактора — щелчок правой кнопкой по палитре.
	/// Был пустым по той же причине, что и SetImageDefaultFrontColor.
	/// </summary>
	public void SetImageDefaultBackColor(Color c)
	{
		Settings.image_paint_back_color = new SKColor(c.R, c.G, c.B, c.A);
		i_back_color_but.SetSKColor(Settings.image_paint_back_color);
	}
	#endregion

	#region Shape Toolbar buttons
	private void InitCircleSplitButton()
	{
		var menu = new ContextMenuStrip
		{
			ShowImageMargin = true,
			ImageScalingSize = new Size(48, 48)
		};

		void SelectCircle(ShapeToolKind kind, System.Drawing.Image image)
		{
			_selectedCircleTool = kind;
			s_new_circle_but.SelectedImage = image;
			_mainView.SetTool(kind);

			foreach (ToolStripItem item in menu.Items)
			{
				if (item is ToolStripMenuItem mi)
					mi.Checked = Equals(mi.Tag, kind);
			}
		}

		var circle2PItem = new ToolStripMenuItem("Circle 2P", Properties.Resources.circle2p,
			(_, __) => SelectCircle(ShapeToolKind.Circle2P, Properties.Resources.circle2p))
		{
			Tag = ShapeToolKind.Circle2P,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		var circle3PItem = new ToolStripMenuItem("Circle 3P", Properties.Resources.circle3p,
			(_, __) => SelectCircle(ShapeToolKind.Circle3P, Properties.Resources.circle3p))
		{
			Tag = ShapeToolKind.Circle3P,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		menu.Items.Add(circle2PItem);
		menu.Items.Add(circle3PItem);

		s_new_circle_but.DropDownMenu = menu;
		s_new_circle_but.SelectedImage = Properties.Resources.circle2p;
		_selectedCircleTool = ShapeToolKind.Circle2P;
		circle2PItem.Checked = true;

		s_new_circle_but.MainButtonClick -= CircleMainButtonClick;
		s_new_circle_but.MainButtonClick += CircleMainButtonClick;
	}

	private void CircleMainButtonClick(object? sender, EventArgs e)
	{
		_mainView.SetTool(_selectedCircleTool);
	}

	private void InitEllipseSplitButton()
	{
		var menu = new ContextMenuStrip
		{
			ShowImageMargin = true,
			ImageScalingSize = new Size(48, 48)
		};

		void SelectEllipse(ShapeToolKind kind, System.Drawing.Image image)
		{
			_selectedEllipseTool = kind;
			s_new_ellipse_but.SelectedImage = image;
			_mainView.SetTool(kind);

			foreach (ToolStripItem item in menu.Items)
			{
				if (item is ToolStripMenuItem mi)
					mi.Checked = Equals(mi.Tag, kind);
			}
		}

		var ellipse3PItem = new ToolStripMenuItem("Ellipse 3P", Properties.Resources.ellipse3p,
			(_, __) => SelectEllipse(ShapeToolKind.Ellipse3P, Properties.Resources.ellipse3p))
		{
			Tag = ShapeToolKind.Ellipse3P,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		var ellipse5PItem = new ToolStripMenuItem("Ellipse 5P", Properties.Resources.ellipse5p,
			(_, __) => SelectEllipse(ShapeToolKind.Ellipse5P, Properties.Resources.ellipse5p))
		{
			Tag = ShapeToolKind.Ellipse5P,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		menu.Items.Add(ellipse3PItem);
		menu.Items.Add(ellipse5PItem);

		s_new_ellipse_but.DropDownMenu = menu;
		s_new_ellipse_but.SelectedImage = Properties.Resources.ellipse3p;
		_selectedEllipseTool = ShapeToolKind.Ellipse3P;
		ellipse3PItem.Checked = true;

		s_new_ellipse_but.MainButtonClick -= EllipseMainButtonClick;
		s_new_ellipse_but.MainButtonClick += EllipseMainButtonClick;
	}

	private void EllipseMainButtonClick(object? sender, EventArgs e)
	{
		_mainView.SetTool(_selectedEllipseTool);
	}

	private void InitRectangleSplitButton()
	{
		var menu = new ContextMenuStrip
		{
			ShowImageMargin = true,
			ImageScalingSize = new Size(48, 48)
		};

		ToolStripMenuItem CreateItem(string text, System.Drawing.Image image, ShapeToolKind kind, Action<ShapeToolKind, System.Drawing.Image> onSelect)
		{
			var item = new ToolStripMenuItem(text, image, (_, __) => onSelect(kind, image))
			{
				Tag = kind,
				AutoSize = false,
				Height = 48,
				DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
				ImageScaling = ToolStripItemImageScaling.None
			};

			return item;
		}

		void SelectRectangle(ShapeToolKind kind, System.Drawing.Image image)
		{
			_selectedRectangleTool = kind;
			s_new_rc_but.SelectedImage = image;
			_mainView.SetTool(kind);

			foreach (ToolStripItem item in menu.Items)
			{
				if (item is ToolStripMenuItem mi)
					mi.Checked = Equals(mi.Tag, kind);
			}
		}

		var rectItem = new ToolStripMenuItem("Rectangle", Properties.Resources.rect,
			(_, __) => SelectRectangle(ShapeToolKind.Rectangle, Properties.Resources.rect))
		{
			Tag = ShapeToolKind.Rectangle,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		var roundedRectItem = new ToolStripMenuItem("Rounded Rectangle", Properties.Resources.rounded_rect,
			(_, __) => SelectRectangle(ShapeToolKind.RoundedRectangle, Properties.Resources.rounded_rect))
		{
			Tag = ShapeToolKind.RoundedRectangle,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		var chamferedRectItem = new ToolStripMenuItem("Chamfered Rectangle", Properties.Resources.chamfered_rect,
			(_, __) => SelectRectangle(ShapeToolKind.ChamferedRectangle, Properties.Resources.chamfered_rect))
		{
			Tag = ShapeToolKind.ChamferedRectangle,
			DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
		};

		menu.Items.Add(rectItem);
		menu.Items.Add(roundedRectItem);
		menu.Items.Add(chamferedRectItem);

		s_new_rc_but.DropDownMenu = menu;
		s_new_rc_but.SelectedImage = Properties.Resources.rect;
		_selectedRectangleTool = ShapeToolKind.Rectangle;
		rectItem.Checked = true;

		s_new_rc_but.MainButtonClick -= RectangleMainButtonClick;
		s_new_rc_but.MainButtonClick += RectangleMainButtonClick;
	}

	private void RectangleMainButtonClick(object? sender, EventArgs e)
	{
		_mainView.SetTool(_selectedRectangleTool);
	}

	private void s_cursor_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		_mainView.SetMode(EditorMode.Shape);
		_mainView.SetTool(ShapeToolKind.Select);
	}

	private void s_curve_edit_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		_mainView.SetTool(ShapeToolKind.Select);
		_mainView.SetMode(EditorMode.Curve);
	}

	private void s_image_mode_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		_mainView.SetTool(ShapeToolKind.Select);
		_mainView.SetMode(EditorMode.Image);
	}

	private void s_group_but_Click(object sender, EventArgs e)
	{
		if (_mainView.CurrentPage == null)
			return;

		var page = _mainView.CurrentPage;
		var selected = _mainView.Selection.ToList();

		if (selected.Count < 2)
			return;

		var group = GroupingHelper.GroupSelected(page, selected);
		if (group == null)
			return;

		var ctx = GetInteractionContext(_mainView);
		if (ctx == null)
			return;

		SelectionHelper.Clear(ctx);
		ctx.Selection.Add(group);
		group.IsSelected = true;

		_mainView.RaiseSelectionChanged();
		_mainView.DocumentChanged();
		_mainView.Invalidate();
	}

	private static EditorInteractionContext? GetInteractionContext(MainView view)
	{
		return view?.InteractionContext;
	}

	private void s_ungroup_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		var page = _mainView.CurrentPage;
		var selected = _mainView.Selection.ToList();

		if (selected.Count == 0)
			return;

		var ungrouped = GroupingHelper.UngroupSelected(page, selected);
		if (ungrouped.Count == 0)
			return;

		var ctx = GetInteractionContext(_mainView);
		if (ctx == null)
			return;

		SelectionHelper.Clear(ctx);

		foreach (var shape in ungrouped)
		{
			ctx.Selection.Add(shape);
			shape.IsSelected = true;
		}

		_mainView.RaiseSelectionChanged();
		_mainView.DocumentChanged();
		_mainView.Invalidate();
	}

	private void s_send_back_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		var changed = LayerArrangeHelper.SendToBack(
			_mainView.CurrentPage,
			_mainView.Selection.ToList());

		if (!changed)
			return;

		_mainView.DocumentChanged();
		_mainView.Invalidate();
	}

	private void s_bring_to_front_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		var changed = LayerArrangeHelper.BringToFront(
			_mainView.CurrentPage,
			_mainView.Selection.ToList());

		if (!changed)
			return;

		_mainView.DocumentChanged();
		_mainView.Invalidate();
	}

	private void v_show_rulers_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		Settings.show_rulers = !Settings.show_rulers;
		SetButtonChecked(v_show_rulers_but, Settings.show_rulers);
		_mainView.Invalidate();
	}

	private void v_snap_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		Settings.snap_objects = !Settings.snap_objects;
		SetButtonChecked(v_snap_but, Settings.snap_objects);
		_mainView.Invalidate();
	}

	private void v_dynamic_guidelines_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		Settings.dynamic_guidelines = !Settings.dynamic_guidelines;
		SetButtonChecked(v_dynamic_guidelines_but, Settings.dynamic_guidelines);
		_mainView.Invalidate();
	}

	private void measure_distance_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null)
			_mainView.SetTool(ShapeToolKind.Measure);
	}

	private void measure_angle_but_Click(object sender, EventArgs e) =>
		ActivateShapeTool(ShapeToolKind.MeasureAngle);

	private void ActivateShapeTool(ShapeToolKind kind)
	{
		if (_mainView == null)
			return;

		_mainView.SetMode(EditorMode.Shape);
		_mainView.SetTool(kind);
	}

	private void s_new_line_but_Click(object sender, EventArgs e)
	{
		_mainView.SetTool(ShapeToolKind.Line);
	}

	private void s_new_curve_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null)
			_mainView.SetTool(ShapeToolKind.Curve);
	}

	private void s_new_poly_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null) _mainView.SetTool(ShapeToolKind.Polygon);
	}

	private void s_new_free_poly_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null) _mainView.SetTool(ShapeToolKind.FreePolygon);
	}

	private void s_new_text_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null) _mainView.SetTool(ShapeToolKind.Text);
	}

	private void s_new_image_but_Click(object sender, EventArgs e)
	{
		if (_mainView != null) _mainView.SetTool(ShapeToolKind.Image);

	}

	private void s_line_color_but_Click(object sender, EventArgs e)
	{
		SColorDialog scd = new SColorDialog();
		scd.SetColorV4(Settings.shape_default_line_color);
		if (scd.ShowDialog() == DialogResult.OK)
		{
			Settings.shape_default_line_color = scd.GetSKColor();
			s_line_color_but.SetSKColor(Settings.shape_default_line_color);
		}
	}

	private void s_fill_color_but_Click(object sender, EventArgs e)
	{
		SColorDialog scd = new SColorDialog();
		scd.SetColorV4(Settings.shape_default_fill_color);
		if (scd.ShowDialog() == DialogResult.OK)
		{
			Settings.shape_default_fill_color = scd.GetSKColor();
			s_fill_color_but.SetSKColor(Settings.shape_default_fill_color);
		}
	}

	private void s_show_guidelines_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		Settings.show_guidelines = !Settings.show_guidelines;
		SetButtonChecked(v_show_guidelines_but, Settings.show_guidelines);
		_mainView.Invalidate();
	}

	private void SetButtonChecked(System.Windows.Forms.Button but, bool is_checked)
	{
		// Состояние запоминает тема: она же перекрашивает кнопки при
		// запуске и при смене оформления, и без этого её перекраска
		// стирала бы отметку.
		WinFormsTheme.SetChecked(but, is_checked);
	}
	#endregion

	#region Main toolbar buttons
	private void d_open_but_Click(object sender, EventArgs e)
	{
		using var ofd = new OpenFileDialog
		{
			Title = "Open VDraw document",
			InitialDirectory = Document.DefaultDocumentsFolder,
			Filter = "VDraw document (*.vdr)|*.vdr|JSON files (*.json)|*.json|All files (*.*)|*.*",
			CheckFileExists = true
		};

		if (ofd.ShowDialog(this) != DialogResult.OK)
			return;

		Document opened;

		try
		{
			opened = Document.Load(ofd.FileName);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				$"Failed to open document.\r\n\r\n{ex.Message}",
				"Open error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return;
		}

		// Незакрытый документ спрашивает про сохранение сам. Отказ пользователя
		// отменяет открытие: иначе несохранённая работа пропала бы молча.
		if (cur_doc != null && !cur_doc.Close())
			return;

		cur_doc = opened;
		CurrentPage = opened.ActivePage ?? opened.pages[0];
		opened.ActivePage = CurrentPage;
		_mainView.SetPage(CurrentPage);
		_animPanel?.SetPage(CurrentPage, documentChanged: true);

		_pageTabs?.SetDocument(opened);

		// История у открытого документа своя — перепривязываем кнопки.
		HookUndoStack();

		_mainView.DocumentChanged();
		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	/// <summary>
	/// Вставляет содержимое другого файла в текущий документ.
	///
	/// Для .vdr это фигуры: настройки листа импортируемого документа
	/// игнорируются, иначе импорт молча переформатировал бы страницу,
	/// в которую вставляют. Для .pdf выбор страниц отдаётся пользователю,
	/// а одностраничный файл вставляется сразу.
	/// </summary>
	private void d_import_but_Click(object sender, EventArgs e)
	{
		if (CurrentPage == null || _mainView == null)
			return;

		using var ofd = new OpenFileDialog
		{
			Title = "Import",
			InitialDirectory = Document.DefaultDocumentsFolder,
			Filter =
				"All supported (*.vdr;*.json;*.pdf;*.vdrs;*.vss;*.vssx;*.png;*.jpg;*.bmp)" +
				"|*.vdr;*.json;*.pdf;*.vdrs;*.vsdm;*.vss;*.vssx;*.vssm;*.vsx;*.vdx;" +
				ImageFilterExtensions + "|" +
				"VDraw document (*.vdr)|*.vdr|" +
				"JSON files (*.json)|*.json|" +
				"PDF document (*.pdf)|*.pdf|" +
				ImageFilter + "|" +
				CustomShapeFilter + "|" +
				StencilFilter + "|" +
				"All files (*.*)|*.*",
			Multiselect = true,
			CheckFileExists = true
		};

		if (ofd.ShowDialog(this) != DialogResult.OK)
			return;

		ImportFiles(ofd.FileNames);
	}

	/// <summary>
	/// Раскладывает выбранные файлы по способам импорта.
	///
	/// Ветвление по расширению, а не по индексу выбранного фильтра:
	/// в пункте «All files» индекс о типе файла ничего не говорит.
	///
	/// Наборы фигур и клипарты собираются в пачки и отдаются своим
	/// импортёрам разом: каждый из них отчитывается одним окном, и
	/// вызов по файлу выдал бы пользователю их столько же, сколько
	/// он выбрал файлов.
	/// </summary>
	private void ImportFiles(IReadOnlyList<string> files)
	{
		var customShapes = new List<string>();
		var stencils = new List<string>();
		var images = new List<string>();

		foreach (var file in files)
		{
			switch (Path.GetExtension(file).ToLowerInvariant())
			{
				case ".pdf":
					ImportPdfDocument(file);
					break;

				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".bmp":
				case ".gif":
				case ".webp":
					images.Add(file);
					break;

				case ".vdrs":
					customShapes.Add(file);
					break;

				case ".vsdm":
				case ".vss":
				case ".vssx":
				case ".vssm":
				case ".vsx":
				case ".vdx":
					stencils.Add(file);
					break;

				default:
					// Расширение может быть незнакомым или просто чужим,
					// поэтому последнее слово за содержимым файла: без этой
					// проверки Visio уходил в разбор JSON и падал на первом
					// же байте своей сигнатуры.
					if (VisioStencilImporter.CanImport(file))
						stencils.Add(file);
					else
						ImportVDrawDocument(file);

					break;
			}
		}

		ImportCustomShapeFiles(customShapes);
		ImportVisioStencilFiles(stencils);
		ImportImageFiles(images);
	}

	/// <summary>
	/// Расширения растровых файлов для строки фильтра. Держатся отдельной
	/// константой, потому что попадают в диалог дважды: в общий пункт
	/// «All supported» и в собственный пункт «Images».
	/// </summary>
	private const string ImageFilterExtensions =
		"*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";

	private const string ImageFilter =
		"Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|" + ImageFilterExtensions;

	/// <summary>
	/// Вставляет растровые файлы как изображения на текущий лист.
	///
	/// Размер берётся физический: пиксели переводятся в единицы листа
	/// через DPI страницы. Картинка 300 dpi ложится на чертёж тем самым
	/// размером, каким её и печатали, а не условной рамкой в сорок единиц,
	/// как это делает инструмент вставки по щелчку. Если получившийся
	/// прямоугольник крупнее листа, он ужимается с сохранением пропорций:
	/// фотография с телефона иначе накрыла бы страницу целиком и на порядок
	/// вышла бы за её края.
	///
	/// Все картинки добавляются одним вызовом AddShapes, то есть одним
	/// шагом истории: выбрал пять файлов — отменяется одним Ctrl+Z.
	/// </summary>
	private void ImportImageFiles(IReadOnlyList<string> files)
	{
		if (files.Count == 0 || CurrentPage == null)
			return;

		var page = CurrentPage;

		var added = new List<Shape>();
		var failed = new List<string>();

		// Каскад: без сдвига картинки легли бы точно друг на друга,
		// и выделить нижнюю можно было бы только перебором.
		float step = MathF.Max(page.size_uom.Width, page.size_uom.Height) * 0.02f;

		foreach (var file in files)
		{
			var shape = TryCreateImportedImage(page, file, step * added.Count);

			if (shape == null)
			{
				failed.Add(Path.GetFileName(file));
				continue;
			}

			added.Add(shape);
		}

		if (added.Count > 0)
		{
			page.AddShapes(added);

			sel.Set(added);
			page.doc.need_save = true;

			sel.NotifyGeometryChanged();
			_mainView?.Invalidate();
		}

		if (failed.Count > 0)
		{
			MessageBox.Show(
				this,
				"Не удалось прочитать:" + Environment.NewLine +
				string.Join(Environment.NewLine, failed.Take(10)),
				"Import",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}
	}

	/// <summary>
	/// Готовая фигура-изображение либо null, если файл не читается.
	///
	/// Null, а не исключение: в одном выборе могут оказаться и годные
	/// файлы, и битый, и падать на нём, потеряв остальные, незачем.
	/// </summary>
	private static ImageShape? TryCreateImportedImage(Page page, string file, float offset)
	{
		SKBitmap? probe = null;

		try
		{
			probe = SKBitmap.Decode(file);

			if (probe == null || probe.Width <= 0 || probe.Height <= 0)
				return null;

			GetImportedImageSize(page, probe.Width, probe.Height, out float w, out float h);

			// Центр листа: место, которое пользователь заведомо видит.
			// Привязываться к видимой области холста не стоит — импорт
			// вызывают и из меню, когда лист может быть отмотан далеко
			// в сторону, и картинка ушла бы в пустоту за краем страницы.
			float x = page.page_origin.X + (page.size_uom.Width - w) * 0.5f + offset;
			float y = page.page_origin.Y + (page.size_uom.Height - h) * 0.5f + offset;

			var frame = new RectangleShape
			{
				Name = Path.GetFileNameWithoutExtension(file),
				LocalTransform = SKMatrix.CreateTranslation(x, y),
				LocalRect = new SKRect(0f, 0f, w, h)
			};

			return ImageShape.FromRectangle(frame, file);
		}
		catch
		{
			// Битый файл, чужое расширение, нет прав на чтение — всё это
			// для вызывающего одно и то же: картинки не будет.
			return null;
		}
		finally
		{
			probe?.Dispose();
		}
	}

	/// <summary>
	/// Размер картинки в единицах листа: физический, но не больше листа.
	/// </summary>
	private static void GetImportedImageSize(
		Page page, int pixelWidth, int pixelHeight, out float w, out float h)
	{
		float mmPerUom = MathF.Max(page.MmPerUom, 0.0001f);
		float dpi = page.DPI > 0 ? page.DPI : 96f;

		// Пиксели -> миллиметры -> единицы листа.
		float uomPerPixel = 25.4f / dpi / mmPerUom;

		w = pixelWidth * uomPerPixel;
		h = pixelHeight * uomPerPixel;

		// Вырожденный случай: единицы или DPI заданы так, что размер ушёл
		// в ноль. Лучше показать что-то маленькое, чем невидимую фигуру.
		if (w < 0.01f || h < 0.01f)
		{
			w = 40f;
			h = 40f * pixelHeight / pixelWidth;
			return;
		}

		// Поля в десятую часть листа: картинка встык к обрезу выглядит
		// ошибкой вёрстки, а не осознанной вставкой.
		float maxW = page.size_uom.Width * 0.9f;
		float maxH = page.size_uom.Height * 0.9f;

		float scale = MathF.Min(
			maxW > 0f ? maxW / w : 1f,
			maxH > 0f ? maxH / h : 1f);

		if (scale < 1f)
		{
			w *= scale;
			h *= scale;
		}
	}

	private void ImportVDrawDocument(string filePath)
	{
		// Проверка стоит ЗДЕСЬ, а не только в разборе по расширению,
		// потому что это единственное место, где рождается сообщение
		// «Failed to import document», — и значит, единственное, через
		// которое файл может сюда попасть, каким бы путём его ни выбрали.
		// Разбор по расширению остаётся быстрым путём, а это — последняя
		// проверка перед тем, как отдать файл разбору JSON.
		if (VisioStencilImporter.CanImport(filePath))
		{
			ImportVisioStencilFiles(new[] { filePath });
			return;
		}

		try
		{
			var dto = Document.ReadFile(filePath);

			// Импорт идёт через временный документ: маппер восстанавливает
			// ссылки между фигурами в пределах страницы, и делать это в обход
			// него значило бы повторить весь второй проход чтения.
			var source = new Document();

			foreach (var page in source.pages.ToList())
				source.PageRecorder.Detach(page);

			source.pages.Clear();
			source.ActivePage = null;

			DocumentMapper.Populate(source, dto);

			var imported = source.pages
				.SelectMany(p => p.Shapes)
				.Where(shape => shape is not GuideLine)
				.ToList();

			if (imported.Count == 0)
			{
				MessageBox.Show(this, "The document contains no shapes to import.", "Import");
				return;
			}

			// Фигуры отвязываются от исходных страниц: одна фигура не может
			// принадлежать двум страницам сразу.
			foreach (var page in source.pages)
				page.Shapes.Clear();

			CurrentPage!.AddShapes(imported);
			CurrentPage.doc.need_save = true;

			MainForm.sel.NotifyGeometryChanged();
			_mainView!.Invalidate();
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				$"Failed to import document.\r\n\r\n{Path.GetFileName(filePath)}\r\n{ex.Message}",
				"Import error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
		}
	}

	private void d_export_but_Click(object sender, EventArgs e)
	{
		if (CurrentPage == null || _mainView == null)
			return;
		var selection = _mainView.Selection?.ToList() ?? new List<Shape>();
		ShapeExporter.ExportWithDialog(this, CurrentPage, selection);
	}

	private void d_print_but_Click(object sender, EventArgs e)
	{
		if (cur_doc == null || cur_doc.pages.Count == 0)
			return;

		// Текущая страница согласуется до открытия: диалог открывает
		// предпросмотр именно на ней, и вариант «Current page» тоже опирается на неё.
		if (CurrentPage != null)
			cur_doc.ActivePage = CurrentPage;

		using var dialog = new PrintForm(cur_doc);
		dialog.ShowDialog(this);

		// В диалоге могли выбрать другой принтер или формат — поля печати
		// пересчитаются при следующей отрисовке рамки.
		PrinterCatalog.ResetDefaultMargins();
		_mainView?.Invalidate();
	}

	private void copy_but_Click(object sender, EventArgs e)
	{
		CopySelectedShapes();
	}

	/// <summary>
	/// Имя руководства пользователя в папке Resources.
	/// Точное имя пробуется первым, но не единственным: номер версии внутри
	/// имени файла означает, что следующая редакция руководства называется
	/// иначе, и кнопка молча перестала бы работать. Поэтому при промахе
	/// ищется свежий файл по маске HelpFileMask.
	/// </summary>
	private const string HelpFileName = "VDraw_User_Manual_v1.0.pdf";

	private const string HelpFileMask = "VDraw_User_Manual*.pdf";
	private void help_but_Click(object sender, EventArgs e)
	{
		string? path = ResolveHelpFile();

		if (path == null)
		{
			MessageBox.Show(this,
				$"User manual not found.\r\n\r\nExpected file:\r\n{ResourcePaths.File(HelpFileName)}",
				"Help",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return;
		}
		try
		{
			// UseShellExecute = true обязателен. По умолчанию в .NET он false,
			// и тогда Process.Start ждёт исполняемый файл: PDF просто
			// не откроется. С true путь отдаётся оболочке Windows, и документ
			// показывает та программа, которая назначена для .pdf.
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			// Сюда попадает и случай, когда для .pdf в системе не назначена
			// программа: показываем путь, чтобы файл можно было открыть руками.
			MessageBox.Show(this,
				$"Could not open the user manual.\r\n\r\n{path}\r\n\r\n{ex.Message}",
				"Help",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}
	}

	/// <summary>
	/// Путь к руководству или null, если файла нет. При отсутствии точного
	/// имени берётся самый новый подходящий файл из Resources.
	/// </summary>
	private static string? ResolveHelpFile()
	{
		string exact = ResourcePaths.File(HelpFileName);
		if (System.IO.File.Exists(exact))
			return exact;
		if (!System.IO.Directory.Exists(ResourcePaths.Root))
			return null;
		return System.IO.Directory
			.EnumerateFiles(ResourcePaths.Root, HelpFileMask)
			.OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
			.FirstOrDefault();
	}

	#endregion

	#region Copy/Paste

	public void CopySelectedShapes()
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		var selected = _mainView.Selection.ToList();

		if (selected.Count == 0)
			return;

		// Свой буфер: полные копии фигур со всеми свойствами. Вставка внутри
		// приложения идёт именно отсюда и ничего не теряет.
		//
		// Ссылки внутри выделения перенаправляются на копии здесь же,
		// а не только при вставке. Иначе цепочка рвётся: в буфере лежали
		// бы размеры, привязанные к фигурам НА СТРАНИЦЕ, и вставка,
		// связывающая копии буфера с копиями на листе, ничего бы
		// не нашла — привязка так и осталась бы на исходной фигуре.
		_clipboardShapes = CloneShapesWithLinks(selected);

		// Системный буфер: растр для других программ. Неудача здесь
		// не должна ломать копирование внутри приложения, поэтому
		// результат не проверяется.
		if (!ClipboardExporter.CopyShapes(_mainView.CurrentPage, selected, out string error))
			MessageBox.Show(error, "Копирование в буфер");

		// Свой буфер наполнился — кнопка вставки должна включиться сразу,
		// не дожидаясь тика таймера.
		UpdateCommandButtons();
	}

	private void paste_but_Click(object sender, EventArgs e)
	{
		PasteCopiedShapes();
	}

	/// <summary>
	/// Вставляет содержимое буфера.
	///
	/// targetPage — куда положить вставленное, в координатах страницы.
	/// null означает «на прежнее место»: так работает Ctrl+V, и это
	/// правильно, потому что у клавиши нет своей точки на листе. Вызов
	/// из контекстного меню передаёт место щелчка, и вставленное встаёт
	/// туда, куда пользователь показал.
	/// </summary>
	public void PasteCopiedShapes(SKPoint? targetPage = null)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var page = CurrentPage;
		var undo = page.doc?.Undo;
		var ctx = GetInteractionContext(_mainView);

		var pasted = new List<Shape>();

		undo?.BeginGroup("Paste");

		try
		{
			// Свой буфер приоритетнее: он заполняется только явным
			// копированием внутри приложения и точно отражает намерение.
			if (_clipboardShapes.Count > 0)
			{
				// Ссылки внутри вставляемого набора переводятся на его же
				// копии: размер, скопированный вместе со своей деталью,
				// должен мерить эту деталь, а не ту, с которой копировали.
				pasted.AddRange(CloneShapesWithLinks(_clipboardShapes));
			}
			else
			{
				pasted.AddRange(ClipboardImporter.Paste(_mainView, page));
			}

			if (pasted.Count == 0)
				return;

			if (targetPage != null)
				MovePastedTo(pasted, targetPage.Value);

			// Вставленное ложится поверх существующего.
			//
			// Номера считает страница: Clone копирует слой оригинала, поэтому
			// без явного назначения вставленное перемешалось бы с исходным.
			int topLayer = page.GetTopLayer();

			foreach (var shape in pasted)
				shape.Layer = topLayer++;

			page.AddShapes(pasted);
			page.SortLayers();
			page.doc.need_save = true;
		}
		finally
		{
			undo?.CommitGroup();
		}

		if (pasted.Count == 0)
			return;

		if (ctx != null)
		{
			SelectionHelper.Clear(ctx);

			if (pasted.Count == 1)
				SelectionHelper.SetSingle(ctx, pasted[0]);
			else
			{
				foreach (var shape in pasted)
					SelectionHelper.Add(ctx, shape);
			}
		}

		_mainView.RaiseSelectionChanged();
		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	/// <summary>
	/// Сдвигает вставленное так, чтобы середина его габаритов попала
	/// в указанную точку страницы.
	///
	/// Середина, а не левый верхний угол: пользователь показывает курсором
	/// туда, где хочет ВИДЕТЬ вставленное, и набор, повешенный на курсор
	/// углом, каждый раз оказывался бы правее и ниже ожидаемого.
	///
	/// Сдвиг общий на весь набор, поэтому взаимное расположение фигур
	/// сохраняется — иначе рассыпался бы любой чертёж из нескольких
	/// деталей.
	/// </summary>
	private static void MovePastedTo(IReadOnlyList<Shape> pasted, SKPoint targetPage)
	{
		// Границы собираются вручную, а не через SKRect.Union с пустым
		// начальным значением: у горизонтального отрезка высота ноль,
		// SKRect.IsEmpty для него истинно, и такая фигура выпала бы
		// из объединения — вместе с ней уехала бы и вся вставка,
		// если отрезок в наборе единственный.
		bool any = false;
		float left = 0f, top = 0f, right = 0f, bottom = 0f;

		foreach (var shape in pasted)
		{
			var b = shape.GetBounds();

			if (!any)
			{
				left = b.Left;
				top = b.Top;
				right = b.Right;
				bottom = b.Bottom;
				any = true;
				continue;
			}

			left = MathF.Min(left, b.Left);
			top = MathF.Min(top, b.Top);
			right = MathF.Max(right, b.Right);
			bottom = MathF.Max(bottom, b.Bottom);
		}

		if (!any)
			return;

		float dx = targetPage.X - (left + right) * 0.5f;
		float dy = targetPage.Y - (top + bottom) * 0.5f;

		// MoveByWorld, а не правка LocalTransform: он же переносит и концы
		// градиента, заданные в мировых координатах, в том числе у детей
		// внутри групп. Без этого заливка осталась бы на прежнем месте,
		// и вставленная фигура залилась бы одним крайним цветом.
		foreach (var shape in pasted)
			shape.MoveByWorld(dx, dy);
	}

	/// <summary>
	/// Копирует набор фигур, переводя ссылки ВНУТРИ набора на копии.
	///
	/// Само по себе Clone копирует привязку вместе с её целью, то есть
	/// оставляет копию размера привязанной к исходной фигуре. Пока цель
	/// в набор не входит, это верно — размер скопировали отдельно, и мерить
	/// ему по-прежнему ту же деталь. Но если деталь копируется вместе
	/// с размером, ссылаться на оригинал уже неправильно: пользователь
	/// получал пару, где копия размера продолжала следить за исходной
	/// деталью и уезжала к ней при первом же её сдвиге.
	///
	/// Отсюда правило: цель есть в наборе — ссылка переводится на её копию;
	/// цели в наборе нет — ссылка остаётся прежней.
	/// </summary>
	private static List<Shape> CloneShapesWithLinks(IReadOnlyList<Shape> source)
	{
		var clones = new List<Shape>(source.Count);

		// Сравнение по ссылке: ключом служит сам объект, а Equals у фигур
		// не переопределён и совпадений по значению здесь быть не должно.
		var map = new Dictionary<Shape, Shape>(ReferenceEqualityComparer.Instance);

		foreach (var shape in source)
		{
			var clone = shape.Clone();

			if (clone == null)
				continue;

			clones.Add(clone);
			MapClonePair(shape, clone, map);
		}

		// Вторым проходом: к этому моменту в таблице есть ВЕСЬ набор,
		// и размер, скопированный раньше своей детали, тоже найдёт её копию.
		foreach (var clone in clones)
			RemapClonedLinks(clone, map);

		return clones;
	}

	/// <summary>
	/// Заносит в таблицу пару «оригинал — копия» вместе со всем содержимым
	/// групп.
	///
	/// Дети сопоставляются по порядку: GroupShape.Clone копирует их
	/// последовательно и порядок сохраняет, так что i-й ребёнок копии
	/// отвечает i-му ребёнку оригинала.
	/// </summary>
	private static void MapClonePair(Shape source, Shape clone, Dictionary<Shape, Shape> map)
	{
		map[source] = clone;

		if (source is not GroupShape sourceGroup || clone is not GroupShape cloneGroup)
			return;

		int count = Math.Min(sourceGroup.Children.Count, cloneGroup.Children.Count);

		for (int i = 0; i < count; i++)
			MapClonePair(sourceGroup.Children[i], cloneGroup.Children[i], map);
	}

	/// <summary>
	/// Переводит на копии все ссылки, которые фигура держит на другие
	/// фигуры: привязки концов, четыре привязки углового размера, пару
	/// пошагового преобразования и текст внутри контейнера.
	/// </summary>
	private static void RemapClonedLinks(Shape clone, Dictionary<Shape, Shape> map)
	{
		foreach (var shape in CustomShapeLibrary.EnumerateDeep(new[] { clone }))
		{
			if (shape.otherShape != null && map.TryGetValue(shape.otherShape, out var other))
				shape.otherShape = other;

			if (shape is IShapeAnchorHost host)
			{
				bool changed = RemapAnchor(host.FirstAnchor, map);
				changed |= RemapAnchor(host.LastAnchor, map);

				// Пересчёт обязателен: концы стоят там, где их поставила
				// прежняя цель, и без сброса подписи копия дождалась бы
				// первого движения исходной детали, прежде чем встать
				// на место.
				if (changed)
					host.InvalidateAnchors();
			}

			if (shape is AngleMeasurementShape angle)
			{
				bool changed = false;

				foreach (var anchor in angle.Anchors)
					changed |= RemapAnchor(anchor, map);

				if (changed)
					angle.InvalidateAnchors();
			}

			if (shape is TransformShape transform)
			{
				// Ключ переписывать отдельно не нужно: сеттер ShapeA
				// подставляет его сам и сбрасывает геометрию.
				if (transform.ShapeA != null && map.TryGetValue(transform.ShapeA, out var a))
					transform.ShapeA = a;

				if (transform.ShapeB != null && map.TryGetValue(transform.ShapeB, out var b))
					transform.ShapeB = b;
			}

			// Сеттер InsideShape сам сбрасывает кэш области.
			if (shape is TextShape text
				&& text.InsideShape != null
				&& map.TryGetValue(text.InsideShape, out var container))
			{
				text.InsideShape = container;
			}
		}
	}

	/// <summary>
	/// Переводит одну привязку на копию цели. Возвращает true, если
	/// перевод состоялся.
	/// </summary>
	private static bool RemapAnchor(ShapeAnchor anchor, Dictionary<Shape, Shape> map)
	{
		if (anchor.Target == null || !map.TryGetValue(anchor.Target, out var target))
			return false;

		anchor.Target = target;
		return true;
	}

	/// <summary>
	/// Есть ли что вставлять. Проверяются оба источника: свой буфер и системный.
	///
	/// Состояние системного буфера берётся из кэша, а не опрашивается: каждый
	/// вызов Clipboard.Contains* — обращение к общему на всю систему ресурсу,
	/// и делать четыре таких вызова на каждый пересчёт кнопок нельзя. Кэш
	/// обновляет WM_CLIPBOARDUPDATE.
	/// </summary>
	private bool CanPaste()
	{
		return _clipboardShapes.Count > 0 || _systemClipboardHasContent;
	}

	private const int WM_CLIPBOARDUPDATE = 0x031D;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool AddClipboardFormatListener(IntPtr hwnd);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

	/// <summary>Кэш ответа на вопрос «есть ли в системном буфере что-то пригодное».</summary>
	private bool _systemClipboardHasContent;

	private bool _clipboardListenerRegistered;

	protected override void WndProc(ref Message m)
	{
		base.WndProc(ref m);

		if (m.Msg == WM_CLIPBOARDUPDATE)
		{
			RefreshSystemClipboardState();
			UpdateCommandButtons();
		}
	}

	private void RefreshSystemClipboardState()
	{
		try
		{
			_systemClipboardHasContent =
				Clipboard.ContainsImage()
				|| Clipboard.ContainsText()
				|| Clipboard.ContainsFileDropList()
				|| Clipboard.ContainsData(ClipboardImporter.OfficeDrawingFormat);
		}
		catch
		{
			// Буфер занят другим процессом — не повод гасить кнопку навсегда.
			_systemClipboardHasContent = true;
		}
	}
	#endregion

	#region Operations
	public void DeleteSelectedShapes()
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		// В режиме узлов Delete относится к УЗЛАМ, а не к фигуре.
		//
		// Развилка стоит здесь, а не в обработчике клавиши, потому что
		// сюда ведут три пути: клавиша при фокусе на холсте (MainView),
		// та же клавиша при фокусе на панели (TryHandleArrangeShortcut)
		// и кнопка на панели инструментов. Поставь проверку в одном из
		// них — два других продолжили бы удалять кривую целиком.
		//
		// Если ни один узел не выделен, не происходит ничего:
		// DeleteSelectedNodes в этом случае возвращает false, и выход
		// всё равно безусловный — иначе Delete по пустому выделению
		// узлов сносил бы кривую, то есть ровно то, чего быть не должно.
		if (Mode == EditorMode.Curve)
		{
			var editedCurve = GetSelectedCurve();

			if (editedCurve != null)
			{
				if (editedCurve.DeleteSelectedNodes(CurrentUndoStack()))
				{
					_mainView.CurrentPage.doc.need_save = true;
					sel.NotifyGeometryChanged();
					_mainView.DocumentChanged();
					_mainView.Invalidate();
				}

				return;
			}
		}

		var page = CurrentPage;
		if (page == null) return;

		var doomed = _mainView.Selection.ToList();
		if (doomed.Count == 0) return;

		var undo = page.doc?.Undo;

		// Отвязка и удаление — один шаг истории: Page.RemoveShapes открывает
		// свою группу, но вложенные группы схлопываются в одну.
		undo?.BeginGroup(doomed.Count == 1 ? "Delete shape" : $"Delete {doomed.Count} shapes");

		try
		{
			// Инструмент преобразования без одной из двух фигур показывать
			// нечего, поэтому он удаляется вместе с источником. Дополняем
			// список ДО отвязки текста: инструмент — такая же фигура, и на
			// него самого мог ссылаться текст.
			TransformBindingHelper.AppendDependentTools(page, doomed);

			// Текст, привязанный к удаляемой фигуре, теряет и ссылку, и режим.
			TextBindingHelper.DetachFromDeleted(page, doomed, undo);

			// Размеры, привязанные к удаляемой фигуре, отпускают её.
			// Без этого Target оставался ссылкой на фигуру, которой
			// на странице больше нет: в памяти это сходило с рук,
			// а в сохранённом файле привязка терялась молча.
			MeasurementAnchorRetargeting.DetachFromDeleted(page, doomed, undo);

			page.RemoveShapes(doomed);
		}
		finally
		{
			undo?.CommitGroup();
		}

		var ctx = GetInteractionContext(_mainView);

		page.SortLayers();
		page.doc.need_save = true;

		if (ctx != null)
			SelectionHelper.Clear(ctx);

		_mainView.RaiseSelectionChanged();
		_mainView.DocumentChanged();

		// Состав панели зависит от выделения, а оно только что очищено.
		RebuildPropertyPanel();

		_mainView.Invalidate();
	}

	private void flip_horizontal_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (Mode == EditorMode.Shape)
		{
			_mainView.FlipSelectionHorizontal();
		}
		if (Mode == EditorMode.Image)
		{
			if (!EnsureSelectedImageForPaint(out _))
				return;
			var ctx = _mainView.InteractionContext;
			// EnsureImageEditSession, а не new: созданная вручную сессия оставалась
			// без истории, и отмена для всего, что она делала, не работала.
			_mainView.EnsureImageEditSession();
			ImageTransformActions.Apply(_mainView, ctx, _mainView.ImageEditSession, ImageTransformKind.FlipHorizontal);
		}
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void flip_vertical_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (Mode == EditorMode.Shape)
		{
			_mainView.FlipSelectionVertical();
		}
		if (Mode == EditorMode.Image)
		{
			if (!EnsureSelectedImageForPaint(out _))
				return;
			var ctx = _mainView.InteractionContext;
			// EnsureImageEditSession, а не new: созданная вручную сессия оставалась
			// без истории, и отмена для всего, что она делала, не работала.
			_mainView.EnsureImageEditSession();
			ImageTransformActions.Apply(_mainView, ctx, _mainView.ImageEditSession, ImageTransformKind.FlipVertical);
		}
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void rotate_CW_90_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		if (Mode == EditorMode.Shape)
		{
			_mainView.RotateSelection90(clockwise: true);
		}

		if (Mode == EditorMode.Image)
		{
			if (!EnsureSelectedImageForPaint(out _))
				return;

			var ctx = _mainView.InteractionContext;

			// EnsureImageEditSession, а не new: созданная вручную сессия оставалась
			// без истории, и отмена для всего, что она делала, не работала.
			_mainView.EnsureImageEditSession();

			ImageTransformActions.Apply(
				_mainView,
				ctx,
				_mainView.ImageEditSession,
				ImageTransformKind.Rotate90CW);
		}

		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void rotate_CCW_90_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		if (Mode == EditorMode.Shape)
		{
			_mainView.RotateSelection90(clockwise: false);
		}

		if (Mode == EditorMode.Image)
		{
			if (!EnsureSelectedImageForPaint(out _))
				return;

			var ctx = _mainView.InteractionContext;

			// EnsureImageEditSession, а не new: созданная вручную сессия оставалась
			// без истории, и отмена для всего, что она делала, не работала.
			_mainView.EnsureImageEditSession();

			ImageTransformActions.Apply(
				_mainView,
				ctx,
				_mainView.ImageEditSession,
				ImageTransformKind.Rotate90CCW);
		}

		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void delete_but_Click(object sender, EventArgs e)
	{
		DeleteSelectedShapes();
	}

	private void undo_but_Click(object sender, EventArgs e)
	{
		if (!_mainView.PerformUndo())
			return;

		_mainView.DocumentChanged();

		// Состав панели зависит от выделения, а откат мог его изменить.
		RebuildPropertyPanel();
	}

	private void redo_but_Click(object sender, EventArgs e)
	{
		if (!_mainView.PerformRedo())
			return;

		_mainView.DocumentChanged();
		RebuildPropertyPanel();
	}

	/// <summary>
	/// Кнопка формата берёт образец и включает кисть.
	///
	/// Дальше каждый щелчок по фигуре на холсте заменяет её обводку
	/// и заливку скопированными. Повторный щелчок по кнопке выключает
	/// кисть, как и Esc либо правая кнопка на холсте.
	/// </summary>
	private void format_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;

		// Кисть уже включена — выключаем. Буфер при этом сохраняется:
		// вернуться к покраске можно одним щелчком, не выбирая образец заново.
		if (_mainView.CurrentToolKind == ShapeToolKind.FormatPainter)
		{
			_mainView.SetTool(ShapeToolKind.Select);
			UpdateFormatButton();
			return;
		}

		CopyShapeFormat();

		if (!ShapeFormatClipboard.HasFormat)
			return;

		_mainView.SetTool(ShapeToolKind.FormatPainter);
		UpdateFormatButton();
	}

	/// <summary>
	/// Состояние кнопки формата.
	///
	/// Отдельный метод, а не часть UpdateCommandButtons: его зовёт и сам
	/// инструмент, когда выходит по Esc или правой кнопке, а ждать тика
	/// таймера в четверть секунды здесь заметно.
	/// </summary>
	public void UpdateFormatButton()
	{
		bool painting = _mainView?.CurrentToolKind == ShapeToolKind.FormatPainter;
		bool anyShape = (_mainView?.Selection.Count ?? 0) > 0;

		format_but.Enabled = anyShape || ShapeFormatClipboard.HasFormat;
		SetButtonChecked(format_but, painting);
	}

	/// <summary>Забирает обводку и заливку выделенной фигуры в буфер формата.</summary>
	public void CopyShapeFormat()
	{
		if (_mainView == null)
			return;

		// Источник — первая фигура выделения. Спрашивать, какой из нескольких
		// брать, было бы навязчиво: взять можно только один.
		var source = _mainView.Selection.FirstOrDefault(s => s is not GroupShape && s is not GuideLine);

		if (!ShapeFormatClipboard.Copy(source))
			return;

		UpdateCommandButtons();
	}

	/// <summary>Применяет формат из буфера ко всем выделенным фигурам.</summary>
	public void PasteShapeFormat()
	{
		if (_mainView == null || CurrentPage == null)
			return;

		int changed = ShapeFormatClipboard.Apply(_mainView.Selection, CurrentUndoStack());

		if (changed == 0)
			return;

		CurrentPage.doc.need_save = true;

		RebuildPropertyPanel();
		_mainView.Invalidate();
		UpdateCommandButtons();
	}

	private void v_scale_selected_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		// Без выделения показываем всё: отказывать молча хуже, чем сделать
		// ближайшее осмысленное действие.
		if (!_mainView.ZoomToSelection())
			_mainView.ZoomToAllShapes();
		UpdateScaleCombo();
	}

	private void v_scale_all_but_Click(object sender, EventArgs e)
	{
		_mainView?.ZoomToAllShapes();
		UpdateScaleCombo();
	}

	private void v_scale_page_but_Click(object sender, EventArgs e)
	{
		_mainView?.ZoomToPage();
		UpdateScaleCombo();
	}

	/// <summary>Показывает текущий масштаб вида в поле.</summary>
	private void UpdateScaleCombo()
	{
		if (_mainView == null)
			return;

		_suppressScaleCombo = true;

		try
		{
			// Текст, а не выбранный пункт: масштаб после подгонки под выделение
			// почти никогда не совпадает со значениями списка.
			v_scale_combo.Text = $"{_mainView.scale * 100f:0}%";
		}
		finally
		{
			// finally обязателен: исключение внутри навсегда заблокировало бы
			// поле от пользователя.
			_suppressScaleCombo = false;
		}
	}

	private void v_scale_combo_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (_suppressScaleCombo)
			return;

		ApplyScaleFromCombo(v_scale_combo.SelectedItem?.ToString());
	}

	/// <summary>
	/// Ввод с клавиатуры. SelectedIndexChanged на него не срабатывает,
	/// поэтому значение применяется по Enter и по потере фокуса.
	/// </summary>
	private void v_scale_combo_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode != Keys.Enter)
			return;

		e.SuppressKeyPress = true;

		ApplyScaleFromCombo(v_scale_combo.Text);
	}

	private void v_scale_combo_Leave(object sender, EventArgs e)
	{
		if (_suppressScaleCombo)
			return;

		ApplyScaleFromCombo(v_scale_combo.Text);
	}

	/// <summary>
	/// Разбирает текст и применяет масштаб. При неразборчивом вводе поле
	/// возвращается к текущему значению, а не оставляет мусор.
	/// </summary>
	private void ApplyScaleFromCombo(string? text)
	{
		if (_mainView == null || string.IsNullOrWhiteSpace(text))
			return;

		text = text.Trim();

		if (string.Equals(text, "All", StringComparison.OrdinalIgnoreCase))
		{
			_mainView.ZoomToAllShapes();
			return;
		}

		// Знак процента необязателен: пользователь чаще набирает просто число.
		string number = text.TrimEnd('%', ' ');

		if (!float.TryParse(
				number.Replace(',', '.'),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out float percent))
		{
			UpdateScaleCombo();
			return;
		}

		_mainView.SetScale(percent / 100f);
	}

	/// <summary>
	/// Создаёт инструмент преобразования между двумя выделенными фигурами.
	///
	/// Источники берутся из выделения, а не выбираются щелчками по холсту:
	/// пара уже выделена, и требовать указать её заново значило бы
	/// повторять работу, которую пользователь только что сделал. Кнопка
	/// доступна ровно тогда, когда выделены две подходящие фигуры, —
	/// см. UpdateCommandButtons.
	/// </summary>
	private void s_transform_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var page = CurrentPage;

		var pair = _mainView.Selection.Where(TransformShape.CanBind).ToList();
		if (pair.Count != 2)
			return;

		// Порядок выделения задаёт направление: от первой выделенной
		// фигуры ко второй.
		var tool = new TransformShape
		{
			ShapeA = pair[0],
			ShapeB = pair[1]
		};

		var undo = page.doc?.Undo;

		// Одна группа: добавление фигуры и смена выделения должны
		// отменяться единственным нажатием. Сам шаг «создание фигуры»
		// запишет PageUndoRecorder через событие Page.ShapeAdded.
		undo?.BeginGroup("Add transform");

		try
		{
			page.AddShape(tool);
		}
		finally
		{
			undo?.CommitGroup();
		}

		page.doc.need_save = true;

		var ctx = GetInteractionContext(_mainView);
		if (ctx != null)
			SelectionHelper.SetSingle(ctx, tool);

		_mainView.RaiseSelectionChanged();

		RebuildPropertyPanel();

		_mainView.DocumentChanged();
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void s_scissors_line_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		_mainView.SetTool(ShapeToolKind.ScissorsLine);
	}

	private void s_scissors_curve_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		_mainView.SetTool(ShapeToolKind.ScissorsCurve);
	}

	private void s_cap_scissors_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null)
			return;
		Settings.cap_scissors = !Settings.cap_scissors;
		SetButtonChecked(s_cap_scissors_but, Settings.cap_scissors);
	}
	#endregion

	#region Page tab buttons
	private void page_paper_size_combo_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (CurrentPage == null)
			return;

		PaperFormat fmt = Settings.paper_formats[page_paper_size_combo.SelectedIndex];
		CurrentPage.paper_format = fmt;

		if (fmt.title == "Custom")
		{
			_mainView.DocumentChanged();
			return;
		}

		// Update size_uom according to format, orientation, and current uom
		float wMm = fmt.width_mm;
		float hMm = fmt.height_mm;

		if (CurrentPage.Orientation == PageOrientation.Horizontal)
			(wMm, hMm) = (hMm, wMm);

		// Convert mm → current uom units
		float uomFactor = CurrentPage.MmPerUom;
		CurrentPage.size_uom = new SKSize(wMm / uomFactor, hMm / uomFactor);

		// Reflect in UI
		page_size_w_nud.ValueChanged -= page_size_w_nud_ValueChanged;
		page_size_w_nud.Value = (decimal)CurrentPage.size_uom.Width;
		page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
		page_size_h_nud.ValueChanged -= page_size_h_nud_ValueChanged;
		page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
		page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;

		if (CurrentPage.LockRatio)
			CurrentPage.locked_ratio = CurrentPage.size_uom.Width / CurrentPage.size_uom.Height;

		_mainView.DocumentChanged();
	}

	private void page_props_but_Click(object sender, EventArgs e)
	{
		if (CurrentPage == null || CurrentPage.uom == null)
			return;
		PaperFormatData pfd = CurrentPage.ClonePaperFormatData();
		UoM old_uom = CurrentPage.uom;
		using (DocPropertiesForm dpf = new DocPropertiesForm(pfd))
		{
			if (dpf.ShowDialog() == DialogResult.OK)
			{
				if (dpf.paper_changed)
				{
					CurrentPage.SetFormatData(pfd);
					CurrentPage.SetShapesNewUoM(old_uom, pfd.uom);
					InitPageData();
					cur_doc.need_save = true;
					_mainView?.DocumentChanged();
					_mainView?.Invalidate();
				}
			}
		}
	}

	#region История настроек листа

	/// <summary>
	/// Снимок настроек листа для истории отмены.
	///
	/// Настройки страницы до сих пор в историю не попадали вовсе: размер,
	/// ориентация, единицы, начало координат, сетка, поля печати и цвет
	/// фона менялись мимо неё, и Ctrl+Z после такой правки откатывал
	/// предыдущее действие — то есть уносил то, что пользователь менять
	/// не собирался.
	///
	/// Имя шага передаётся ради слияния: подряд идущие правки ОДНОЙ
	/// настройки склеиваются в один шаг, разных — остаются отдельными.
	/// </summary>
	private PageSettingsAction? BeginPageSettingsChange(string name)
	{
		if (CurrentPage == null)
			return null;

		// InitPageData заполняет поля панели листа из страницы — тем же
		// вызовом их и возвращаем после отката. Отдельный метод обновления
		// заводить не стал: он повторил бы InitPageData слово в слово
		// и разошёлся бы с ним при первой же правке.
		return PageSettingsAction.Capture(name, CurrentPage, () =>
		{
			InitPageData();
			_mainView?.DocumentChanged();
		});
	}

	/// <summary>
	/// Закрывает снимок: кладёт в историю, если что-то изменилось,
	/// и выбрасывает, если нет.
	///
	/// Проверка обязательна: обработчики полей срабатывают и тогда, когда
	/// значение подставлено программно при обновлении панели, а такие
	/// вызовы истории не касаются.
	/// </summary>
	private void CommitPageSettingsChange(PageSettingsAction? action)
	{
		if (action == null || cur_doc == null)
			return;

		action.CaptureAfter();

		if (!action.HasChanges())
		{
			action.Discard();
			return;
		}

		cur_doc.Undo.Push(action);
		cur_doc.need_save = true;
	}

	#endregion

	private void page_size_w_nud_ValueChanged(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page width");

		try
		{
			if (CurrentPage == null)
				return;

			float newW = (float)page_size_w_nud.Value;
			float newH = CurrentPage.size_uom.Height;

			if (CurrentPage.LockRatio && newW > 0.0001f)
			{
				// Maintain W/H ratio
				newH = newW / CurrentPage.locked_ratio;
				page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
			}

			CurrentPage.size_uom = new SKSize(newW, newH);
			CurrentPage.doc.need_save = true;

			(PaperFormat format, PageOrientation orientation)? available = Settings.GetPaperFormatMM(CurrentPage.uom.GetMM(newW, CurrentPage.DPI), CurrentPage.uom.GetMM(newH, CurrentPage.DPI));
			page_paper_size_combo.SelectedIndexChanged -= page_paper_size_combo_SelectedIndexChanged;
			if (available != null)
			{
				page_paper_size_combo.SelectedIndex = Settings.paper_formats.IndexOf(available.Value.format);
				CurrentPage.Orientation = available.Value.orientation;
				if (available.Value.orientation == PageOrientation.Vertical)
				{
					SetButtonChecked(page_vertical_but, true);
					SetButtonChecked(page_horizontal_but, false);
				}
				else
				{
					SetButtonChecked(page_vertical_but, false);
					SetButtonChecked(page_horizontal_but, true);
				}
			}
			else
				page_paper_size_combo.SelectedIndex = page_paper_size_combo.Items.Count - 1;
			page_paper_size_combo.SelectedIndexChanged += page_paper_size_combo_SelectedIndexChanged;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_size_h_nud_ValueChanged(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page height");

		try
		{
			if (CurrentPage == null)
				return;

			float newH = (float)page_size_h_nud.Value;
			float newW = CurrentPage.size_uom.Width;

			if (CurrentPage.LockRatio && newW > 0.0001f)
			{
				// Maintain W/H ratio
				newW = newH * CurrentPage.locked_ratio;
				page_size_w_nud.Value = (decimal)newW;
			}

			CurrentPage.size_uom = new SKSize(newW, newH);
			CurrentPage.doc.need_save = true;

			(PaperFormat format, PageOrientation orientation)? available = Settings.GetPaperFormatMM(CurrentPage.uom.GetMM(newW, CurrentPage.DPI), CurrentPage.uom.GetMM(newH, CurrentPage.DPI));
			page_paper_size_combo.SelectedIndexChanged -= page_paper_size_combo_SelectedIndexChanged;
			if (available != null)
			{
				page_paper_size_combo.SelectedIndex = Settings.paper_formats.IndexOf(available.Value.format);
				CurrentPage.Orientation = available.Value.orientation;
				if (available.Value.orientation == PageOrientation.Vertical)
				{
					SetButtonChecked(page_vertical_but, true);
					SetButtonChecked(page_horizontal_but, false);
				}
				else
				{
					SetButtonChecked(page_vertical_but, false);
					SetButtonChecked(page_horizontal_but, true);
				}
			}
			else
				page_paper_size_combo.SelectedIndex = page_paper_size_combo.Items.Count - 1;
			page_paper_size_combo.SelectedIndexChanged += page_paper_size_combo_SelectedIndexChanged;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_lock_ratio_but_Click(object sender, EventArgs e)
	{
		if (CurrentPage == null)
			return;

		CurrentPage.LockRatio = !CurrentPage.LockRatio;
		SetButtonChecked(page_lock_ratio_but, CurrentPage.LockRatio);

		if (CurrentPage.LockRatio)
		{
			// Remember current ratio
			float w = CurrentPage.size_uom.Width;
			float h = CurrentPage.size_uom.Height;
			if (h < 0.0001f)
				CurrentPage.locked_ratio = 1f;
			else
				CurrentPage.locked_ratio = w / h;
		}
	}

	private void page_vertical_but_Click(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page orientation");

		try
		{
			if (CurrentPage == null)
				return;

			if (CurrentPage.Orientation == PageOrientation.Vertical)
				return;

			CurrentPage.Orientation = PageOrientation.Vertical;

			// Swap width and height:
			float wuom = CurrentPage.size_uom.Width;
			float huom = CurrentPage.size_uom.Height;
			CurrentPage.size_uom = new SKSize(huom, wuom);

			// Update UI:
			SetButtonChecked(page_vertical_but, true);
			SetButtonChecked(page_horizontal_but, false);

			page_size_w_nud.ValueChanged -= page_size_w_nud_ValueChanged;
			page_size_w_nud.Value = (decimal)CurrentPage.size_uom.Width;
			page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
			page_size_h_nud.ValueChanged -= page_size_h_nud_ValueChanged;
			page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
			page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;

			CurrentPage.doc.need_save = true;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_horizontal_but_Click(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page orientation");

		try
		{
			if (CurrentPage == null)
				return;

			if (CurrentPage.Orientation == PageOrientation.Horizontal)
				return;

			CurrentPage.Orientation = PageOrientation.Horizontal;

			// Swap width and height:
			float wuom = CurrentPage.size_uom.Width;
			float huom = CurrentPage.size_uom.Height;
			CurrentPage.size_uom = new SKSize(huom, wuom);

			// Update UI:
			SetButtonChecked(page_vertical_but, false);
			SetButtonChecked(page_horizontal_but, true);

			page_size_w_nud.ValueChanged -= page_size_w_nud_ValueChanged;
			page_size_w_nud.Value = (decimal)CurrentPage.size_uom.Width;
			page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
			page_size_h_nud.ValueChanged -= page_size_h_nud_ValueChanged;
			page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
			page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;

			CurrentPage.doc.need_save = true;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_uom_combo_SelectedIndexChanged(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page units");

		try
		{
			if (CurrentPage == null)
				return;

			UoM new_uom = Settings.units[page_uom_combo.SelectedIndex];
			var oldUom = CurrentPage.uom;

			// Сравнение по названию, а не по коэффициенту: у px и mm он
			// совпал бы при 25.4 DPI, и переключение молча не сработало бы.
			if (oldUom.title == new_uom.title)
				return;

			// Коэффициенты берутся при ТЕКУЩЕМ DPI страницы: для пикселей
			// они от него и зависят.
			float oldMmPerUom = oldUom.MmPerUnit(CurrentPage.DPI);
			float newMmPerUom = new_uom.MmPerUnit(CurrentPage.DPI);

			if (Math.Abs(oldMmPerUom - newMmPerUom) < 1e-9f)
			{
				CurrentPage.uom = new_uom;
				return;
			}

			// Convert size_uom to new units
			float widthMm = CurrentPage.SizeMm.Width;
			float heightMm = CurrentPage.SizeMm.Height;

			CurrentPage.uom = new_uom;
			CurrentPage.size_uom = new SKSize(
				widthMm / newMmPerUom,
				heightMm / newMmPerUom);

			// Начало координат хранится в единицах страницы, поэтому переводится
			// СТАРЫМ коэффициентом в миллиметры и новым — обратно. Прежнее
			// деление только на новый коэффициент было верно лишь тогда, когда
			// прежними единицами были миллиметры.
			CurrentPage.page_origin = new SKPoint(
				CurrentPage.page_origin.X * oldMmPerUom / newMmPerUom,
				CurrentPage.page_origin.Y * oldMmPerUom / newMmPerUom);

			page_size_w_nud.ValueChanged -= page_size_w_nud_ValueChanged;
			page_size_w_nud.Value = (decimal)CurrentPage.size_uom.Width;
			page_size_w_nud.ValueChanged += page_size_w_nud_ValueChanged;
			page_size_h_nud.ValueChanged -= page_size_h_nud_ValueChanged;
			page_size_h_nud.Value = (decimal)CurrentPage.size_uom.Height;
			page_size_h_nud.ValueChanged += page_size_h_nud_ValueChanged;
			SyncPageZeroEditors();

			CurrentPage.SetShapesNewUoM(oldUom, new_uom);

			CurrentPage.doc.need_save = true;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	/// <summary>
	/// Подтягивает поля начала координат под фактическое page_origin.
	///
	/// Знак минус не описка: в page_origin хранится, НА СКОЛЬКО СДВИНУТ
	/// угол листа относительно нуля, а пользователю в полях показывается
	/// положение самого нуля — это противоположные по знаку величины.
	/// Ту же формулу применяют обработчики полей и смена единиц измерения.
	///
	/// Отписка на время присваивания обязательна: иначе программная
	/// установка значения тут же сработала бы как правка пользователем
	/// и записала бы обратно уже округлённое число.
	/// </summary>
	public void SyncPageZeroEditors()
	{
		if (CurrentPage == null)
			return;

		decimal x = (decimal)(-CurrentPage.page_origin.X);
		decimal y = (decimal)(-CurrentPage.page_origin.Y);

		// Значение вне заданных границ вызвало бы исключение у NumericUpDown.
		x = Math.Clamp(x, page_zero_x_nud.Minimum, page_zero_x_nud.Maximum);
		y = Math.Clamp(y, page_zero_y_nud.Minimum, page_zero_y_nud.Maximum);

		page_zero_x_nud.ValueChanged -= page_zero_x_nud_ValueChanged;
		page_zero_y_nud.ValueChanged -= page_zero_y_nud_ValueChanged;

		try
		{
			page_zero_x_nud.Value = x;
			page_zero_y_nud.Value = y;
		}
		finally
		{
			page_zero_x_nud.ValueChanged += page_zero_x_nud_ValueChanged;
			page_zero_y_nud.ValueChanged += page_zero_y_nud_ValueChanged;
		}
	}

	private void page_zero_x_nud_ValueChanged(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page origin");

		try
		{
			if (CurrentPage == null)
				return;
			CurrentPage.page_origin = new SKPoint(-(float)page_zero_x_nud.Value, CurrentPage.page_origin.Y);
			CurrentPage.doc.need_save = true;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_zero_y_nud_ValueChanged(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page origin");

		try
		{
			if (CurrentPage == null)
				return;
			CurrentPage.page_origin = new SKPoint(CurrentPage.page_origin.X, -(float)page_zero_y_nud.Value);
			CurrentPage.doc.need_save = true;
			_mainView.DocumentChanged();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_print_borders_but_Click(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Print borders");

		try
		{
			CurrentPage.PrintBordersEnabled = !CurrentPage.PrintBordersEnabled;
			SetButtonChecked(page_print_borders_but, CurrentPage.PrintBordersEnabled);
			CurrentPage.doc.need_save = true;
			_mainView?.Invalidate();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_grid_but_Click(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page grid");

		try
		{
			CurrentPage.GridEnabled = !CurrentPage.GridEnabled;
			SetButtonChecked(page_grid_but, CurrentPage.GridEnabled);
			CurrentPage.doc.need_save = true;
			_mainView?.Invalidate();
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}

	private void page_background_color_but_Click(object sender, EventArgs e)
	{
		var undoStep = BeginPageSettingsChange("Page background");

		try
		{
			if (_mainView == null || CurrentPage == null)
				return;
			using (SColorDialog cd = new SColorDialog())
			{
				cd.SetSKColor(CurrentPage.page_bg_color);
				if (cd.ShowDialog() == DialogResult.OK)
				{
					CurrentPage.page_bg_color = cd.GetSKColor();

					// Своя кнопка, а не i_fore_color_but. Прежде цвет фона
					// страницы показывался на кнопке цвета кисти растрового
					// редактора: сама кнопка фона не менялась никогда,
					// а чужая меняла вид без всякой причины.
					page_background_color_but.SetSKColor(CurrentPage.page_bg_color);

					if (cur_doc != null)
						cur_doc.need_save = true;

					_mainView.Invalidate();
				}
			}
		}
		finally
		{
			CommitPageSettingsChange(undoStep);
		}
	}
	#endregion

	#region Curve edit tab buttons
	/// <summary>
	/// История текущего документа.
	///
	/// Методы редактирования узлов принимают стек параметром со значением
	/// по умолчанию null. Снимок они строят всегда, но без стека молча
	/// выбрасывают — именно так и терялась отмена для кнопок этой панели.
	/// </summary>
	private static UndoStack? CurrentUndoStack() => CurrentPage?.doc?.Undo;

	private CurveShape? GetSelectedCurve()
	{
		if (_mainView == null)
			return null;
		return _mainView.SelectedShape as CurveShape;
	}

	private void nodes_merge_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		if (curve.MergeSelectedNodes(CurrentUndoStack()))
		{
			_mainView.CurrentPage!.doc.need_save = true;
			MainForm.sel.NotifyGeometryChanged();
			_mainView.Invalidate();
		}
	}

	private void nodes_split_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		int idx = curve.FindFocusedOrSelectedNodeIndex();
		if (idx < 0) return;

		if (curve.SplitNode(idx, CurrentUndoStack()))
		{
			_mainView.CurrentPage!.doc.need_save = true;
			MainForm.sel.NotifyGeometryChanged();
			_mainView.Invalidate();
		}
	}

	private void nodes_straight_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		curve.SetSelectedNodeType(CurveNodeType.Straight, undo: CurrentUndoStack());
		_mainView.CurrentPage!.doc.need_save = true;
		MainForm.sel.NotifyGeometryChanged();
		_mainView.Invalidate();
	}

	private void node_tangent_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		curve.SetSelectedNodeType(CurveNodeType.Tangent, undo: CurrentUndoStack());
		_mainView.CurrentPage!.doc.need_save = true;
		sel.NotifyGeometryChanged();
		_mainView.Invalidate();
	}

	private void node_sharp_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		curve.SetSelectedNodeType(CurveNodeType.Sharp, undo: CurrentUndoStack());
		_mainView.CurrentPage!.doc.need_save = true;
		MainForm.sel.NotifyGeometryChanged();
		_mainView.Invalidate();
	}

	private void node_delete_but_Click(object sender, EventArgs e)
	{
		var curve = GetSelectedCurve();
		if (curve == null) return;

		curve.DeleteSelectedNodes(CurrentUndoStack());
		_mainView.CurrentPage!.doc.need_save = true;
		MainForm.sel.NotifyGeometryChanged();
		_mainView.Invalidate();
	}

	/// <summary>
	/// Можно ли превратить фигуру в кривую. CurveShape (и ImageShape как её
	/// наследник) уже кривые, у GroupShape ToCurveShape возвращает null.
	/// Проверка типом, а не вызовом ToCurveShape: тот строит геометрию, а для
	/// текста ещё и глифы, и гонять это на каждое обновление кнопок незачем.
	/// </summary>
	private static bool CanConvertToCurve(Shape? shape)
	{
		return shape is not null
			&& shape is not CurveShape
			&& shape is not GroupShape;
	}

	private void s_convert_to_curve_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null) return;

		var view = _mainView;
		var page = view.CurrentPage;
		if (page == null)
			return;

		var ctx = GetInteractionContext(view);
		if (ctx == null)
			return;

		// Снимок выделения: замена фигур перестраивает и Selection, и Shapes.
		var selected = view.Selection?.ToList() ?? new List<Shape>();
		if (selected.Count == 0)
			return;

		var undo = page.doc?.Undo;
		undo?.BeginGroup(selected.Count == 1 ? "Convert to curve" : "Convert to curves");

		// Порядок выделения после конвертации сохраняем: уже готовые кривые
		// и группы остаются в списке как есть, конвертированные встают на своё место.
		var newSelection = new List<Shape>();
		bool anyConverted = false;

		try
		{
			foreach (var shape in selected)
			{
				if (!CanConvertToCurve(shape))
				{
					newSelection.Add(shape);
					continue;
				}

				// Инструмент преобразования разворачивается не в одну кривую,
				// а в столько, сколько у него шагов: каждый шаг становится
				// самостоятельной фигурой со своими обводкой и заливкой.
				if (shape is TransformShape morph)
				{
					var steps = morph.ExpandToCurves();

					if (steps.Count == 0)
					{
						newSelection.Add(shape);
						continue;
					}

					// Порядок перекрытия сохраняется: первый шаг рисовался
					// поверх остальных, значит ему достаётся старший слой.
					// Слои берутся сверху страницы — раздать им номер
					// инструмента нельзя, он один, а фигур теперь много.
					int top = page.GetTopLayer();

					for (int i = 0; i < steps.Count; i++)
						steps[i].Layer = top + (steps.Count - 1 - i);

					var replace = ReplaceShapeWithManyAction.Capture(page, shape, steps);
					replace.Redo();
					undo?.Push(replace);

					foreach (var step in steps)
						newSelection.Add(step);

					anyConverted = true;
					continue;
				}

				var curve = shape.ToCurveShape();
				if (curve == null || curve.Nodes.Count == 0)
				{
					newSelection.Add(shape);
					continue;
				}

				// Замена через отменяемое действие, а не правкой page.Shapes
				// напрямую: прямая правка идёт мимо событий страницы, на
				// которых висит PageUndoRecorder, и конвертация не попадала
				// в историю вовсе — Ctrl+Z после неё фигуру не возвращал.
				//
				// То же действие, что и у разворачивания цепочки шагов: там
				// фигур несколько, здесь одна, но позиция в порядке
				// перекрытия и работа внутри группы нужны одинаково.
				curve.Layer = shape.Layer;

				var replaceOne = ReplaceShapeWithManyAction.Capture(page, shape, new[] { curve });
				replaceOne.Redo();
				undo?.Push(replaceOne);

				curve.IsSelected = true;

				TextBindingHelper.Retarget(page, shape, curve, undo);

				// Кривая строится заново и идентификаторы узлов у неё новые,
				// поэтому привязки измерений переносятся по положению точек.
				MeasurementAnchorRetargeting.Retarget(page, shape, new[] { curve }, undo);

				// Инструмент преобразования держит фигуру по идентификатору,
				// но конвертация строит НОВЫЙ экземпляр с новым Id — связь
				// надо перевести на него явно.
				TransformBindingHelper.Retarget(page, shape, curve, undo);

				curve.ClearNodeSelection();
				newSelection.Add(curve);
				anyConverted = true;
			}
		}
		finally
		{
			undo?.CommitGroup();
		}

		if (!anyConverted)
			return;

		SelectionHelper.Clear(ctx);

		foreach (var shape in newSelection)
		{
			ctx.Selection.Add(shape);
			shape.IsSelected = true;
		}

		// Фокус и первый узел готовятся заранее, но РЕЖИМ НЕ МЕНЯЕТСЯ:
		// конвертация не входит в список разрешённых входов в режим узлов.
		// Пользователь войдёт в него двойным щелчком или кнопкой, и всё
		// уже будет подготовлено.
		if (newSelection.Count == 1 && newSelection[0] is CurveShape single)
		{
			single.IsFocused = true;

			if (single.Nodes.Count > 0)
			{
				single.Nodes[0].Selected = true;
				single.Nodes[0].Focused = true;
			}
		}

		page.doc.need_save = true;
		sel?.NotifyGeometryChanged();
		view.RaiseSelectionChanged();
		view.Invalidate();
		view.Focus();

		ApplyEditorModeFromSelection();
	}

	/// <summary>
	/// Переназначает ссылки фигур со старого объекта на новый: контур для
	/// текста вдоль пути и контейнер для текста внутри фигуры.
	/// </summary>

	private void s_combine_but_Click(object sender, EventArgs e)
	{
		ApplyBoolean(ShapeBooleanKind.Combine);
	}

	private void s_union_but_Click(object sender, EventArgs e)
	{
		ApplyBoolean(ShapeBooleanKind.Union);
	}

	private void s_substract_but_Click(object sender, EventArgs e)
	{
		ApplyBoolean(ShapeBooleanKind.Subtract);
	}

	private void s_intersect_but_Click(object sender, EventArgs e)
	{
		ApplyBoolean(ShapeBooleanKind.Intersect);
	}

	private void s_exclude_xor_but_Click(object sender, EventArgs e)
	{
		ApplyBoolean(ShapeBooleanKind.Exclude);
	}

	private void ApplyBoolean(ShapeBooleanKind kind)
	{
		if (_mainView == null || _mainView.CurrentPage == null)
			return;

		var selection = _mainView.Selection?.ToList();
		if (selection == null || selection.Count < 2)
			return;

		var page = _mainView.CurrentPage;
		var undo = page.doc?.Undo;
		undo?.BeginGroup("Boolean operation");


		try
		{
			var result = ShapeBooleanHelper.Apply(page, selection, kind);

			if (result == null)
				return;

			// Каждый операнд мог быть контуром или контейнером для чужого текста.
			// Переназначаем ссылки на результат операции.
			foreach (var operand in selection)
			{
				TextBindingHelper.Retarget(page, operand, result, page.doc?.Undo);
				TransformBindingHelper.Retarget(page, operand, result, page.doc?.Undo);
				MeasurementAnchorRetargeting.Retarget(page, operand, new[] { result }, page.doc?.Undo);
			}

			var ctx = _mainView.InteractionContext;
			SelectionHelper.SetSingle(ctx, result);

			sel.Clear();
			sel.SetSingle(result);

			RebuildPropertyPanel();

			_mainView.Invalidate();
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	/// <summary>
	/// Гасит кнопки булевых операций по числу выделенных фигур.
	/// Все пять определены ровно для двух операндов: у Subtract важен порядок
	/// A минус B, а у Combine и Union первый операнд задаёт стиль и тип
	/// результата, и с тремя фигурами это перестаёт быть очевидным.
	/// </summary>
	private void UpdateBooleanButtons()
	{
		bool exactlyTwo = (_mainView?.Selection?.Count ?? 0) == 2;

		s_combine_but.Enabled = exactlyTwo;
		s_union_but.Enabled = exactlyTwo;
		s_substract_but.Enabled = exactlyTwo;
		s_intersect_but.Enabled = exactlyTwo;
		s_xor_but.Enabled = exactlyTwo;
	}
	#endregion

	#region Разделители секций тулбаров
	/// <summary>
	/// Расставляет ToolsSplit между соседними секциями на всех панелях.
	///
	/// Программно, а не в дизайнере, по двум причинам. Секций больше
	/// двадцати, и разделители пришлось бы вписывать вручную для каждой
	/// пары, ничего не забыв. И, что важнее, добавленная секция получала
	/// бы разделитель сама, тогда как в дизайнере о нём каждый раз надо
	/// помнить отдельно.
	/// </summary>
	private void InsertToolbarSeparators()
	{
		InsertSeparators(main_toolbar_panel);
		InsertSeparators(shapes_tools_panel);
		InsertSeparators(image_tools_panel);
		InsertSeparators(page_tools_panel);
	}

	/// <summary>
	/// Вставляет разделитель между каждой парой соседних FlowLayoutPanel.
	///
	/// Соседство считается по порядку в Controls, а не по экранным
	/// координатам: панели состыкованы через Dock, и именно этот порядок
	/// определяет, что за чем идёт. Полагаться на Location нельзя — на
	/// момент вызова раскладка ещё может быть не посчитана.
	///
	/// Обход идёт С КОНЦА: вставка сдвигает индексы всех элементов правее,
	/// и при проходе с начала каждая следующая позиция оказывалась бы
	/// смещённой на уже вставленные разделители.
	/// </summary>
	private static void InsertSeparators(Panel? panel)
	{
		if (panel == null)
			return;

		var children = panel.Controls.Cast<Control>().ToList();

		panel.SuspendLayout();

		try
		{
			for (int i = children.Count - 1; i >= 1; i--)
			{
				if (children[i] is not FlowLayoutPanel current)
					continue;

				if (children[i - 1] is not FlowLayoutPanel)
					continue;

				var separator = new ToolsSplit
				{
					// Тот же Dock, что у секций: иначе разделитель не встал бы
					// в общий ряд, а лёг поверх панели.
					Dock = current.Dock,
					Name = "tools_split_" + i
				};

				panel.Controls.Add(separator);
				panel.Controls.SetChildIndex(separator, i);
			}
		}
		finally
		{
			panel.ResumeLayout(true);
		}
	}

	#endregion

	#region Image tools buttons
	private bool EnsureSelectedImageForPaint(out ImageShape? image)
	{
		if (_mainView == null)
		{
			image = null;
			return false;
		}

		image = _mainView.SelectedShape as ImageShape;
		if (image == null)
		{
			MessageBox.Show(
				"Select Image first.",
				"Image editing",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
			return false;
		}

		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
		{
			MessageBox.Show(
				"Could not load image.",
				"Image editing",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return false;
		}

		return true;
	}

	#region Режим инструментов работы с изображением

	/// <summary>
	/// Кнопки инструментов растрового редактирования и соответствующие им виды.
	///
	/// Таблица, а не разбор в каждом обработчике: включение одной кнопки
	/// обязано гасить остальные, и держать это знание в семи местах —
	/// готовый источник рассогласований.
	/// </summary>
	// System.Windows.Forms.Button полным именем: в этом файле есть
	// using static ...VisualStyles.VisualStyleElement, который вносит
	// в область видимости вложенный тип Button и делает короткое имя
	// неоднозначным. Правится здесь, а не удалением using: он мог
	// понадобиться другому коду файла.
	private (System.Windows.Forms.Button Button, ImageEditToolKind Kind)[] ImageToolButtons => new[]
	{
		(i_erasor_but, ImageEditToolKind.Eraser),
		(i_brush_but, ImageEditToolKind.Brush),
		(i_fill_but, ImageEditToolKind.Fill),
		(i_light_but, ImageEditToolKind.Lighten),
		(i_dark_but, ImageEditToolKind.Darken),
		(i_contrast_but, ImageEditToolKind.Contrast),
		(i_blackwhite_but, ImageEditToolKind.BW),
		(i_select_rect_but, ImageEditToolKind.RectSelect),
		(i_select_lasso_but, ImageEditToolKind.LassoSelect)
	};

	/// <summary>
	/// Включает инструмент, а повторное нажатие по включённому — выключает.
	///
	/// Через один метод, чтобы переключение, подсветка кнопок и выход
	/// из режима жили в одном месте.
	/// </summary>
	private void ActivateImageTool(ImageEditToolKind kind)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		// Повторное нажатие по нажатой кнопке — выход из режима.
		if (_mainView.ActiveImageToolKind == kind)
		{
			ExitImageToolMode();
			return;
		}

		if (!EnsureSelectedImageForPaint(out _))
			return;

		_mainView.SetImageEditTool(kind);
		UpdateImageToolButtons();
		_mainView.Focus();
	}

	/// <summary>
	/// Выход из режима растрового редактирования.
	///
	/// Публичный: сюда же обращается холст, когда щёлкнули мимо картинки,
	/// и общий перехватчик нажатий на прочих панелях инструментов.
	/// </summary>
	public void ExitImageToolMode()
	{
		if (_mainView == null)
			return;

		if (_mainView.ActiveImageToolKind == ImageEditToolKind.None && !_mainView.IsImageEditMode)
		{
			UpdateImageToolButtons();
			return;
		}

		// None обнуляет imageEditTool, а вместе с ним пропадает и контур
		// кисти: он рисуется только через DrawOverlay активного инструмента.
		_mainView.SetImageEditTool(ImageEditToolKind.None);
		UpdateImageToolButtons();
	}

	/// <summary>Подсветка кнопок по фактически включённому инструменту.</summary>
	public void UpdateImageToolButtons()
	{
		var active = _mainView?.ActiveImageToolKind ?? ImageEditToolKind.None;

		foreach (var (button, kind) in ImageToolButtons)
		{
			if (button != null)
				SetButtonChecked(button, kind == active);
		}
	}

	/// <summary>
	/// Вешает на кнопки ОСТАЛЬНЫХ панелей выход из режима изображения.
	///
	/// Подписка идёт после конструктора формы, поэтому наш обработчик
	/// срабатывает ПОСЛЕ собственного обработчика кнопки: сначала команда
	/// делает свою работу, затем гасится режим. Обратный порядок был бы
	/// неверен — команда выполнялась бы уже вне режима.
	///
	/// Панели самого растрового редактора исключены по имени: их элементы
	/// названы с префикса i_, и гасить режим при выборе цвета кисти или
	/// формы штампа нельзя.
	/// </summary>
	private void HookImageModeExitOnToolbars()
	{
		void Walk(Control parent)
		{
			foreach (Control child in parent.Controls)
			{
				if (child.Name != null && child.Name.StartsWith("i_", StringComparison.Ordinal))
					continue;

				if (child is System.Windows.Forms.Button button)
					button.Click += ToolbarButton_ExitImageMode;

				if (child.HasChildren)
					Walk(child);
			}
		}

		if (main_toolbar_panel != null)
			Walk(main_toolbar_panel);

		if (drawing_toolbar_panel != null)
			Walk(drawing_toolbar_panel);
	}

	private void ToolbarButton_ExitImageMode(object? sender, EventArgs e)
	{
		ExitImageToolMode();
	}

	#endregion

	private void i_eraser_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Eraser);
	}

	private void i_brush_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Brush);
	}

	private void i_select_rect_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.RectSelect);
	}

	private void i_select_lasso_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.LassoSelect);
	}

	private void i_light_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Lighten);
	}

	private void i_dark_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Darken);
	}

	private void i_fore_color_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		using (SColorDialog cd = new SColorDialog())
		{
			cd.SetSKColor(Settings.image_paint_fore_color);
			if (cd.ShowDialog() == DialogResult.OK)
			{
				Settings.image_paint_fore_color = cd.GetSKColor();
				i_fore_color_but.SetSKColor(Settings.image_paint_fore_color);
			}
		}
	}

	private void i_back_color_but_Click(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		using (SColorDialog cd = new SColorDialog())
		{
			cd.SetSKColor(Settings.image_paint_back_color);
			if (cd.ShowDialog() == DialogResult.OK)
			{
				Settings.image_paint_back_color = cd.GetSKColor();
				i_back_color_but.SetSKColor(Settings.image_paint_back_color);
			}
		}
	}

	private void i_fill_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Fill);
	}

	private void i_contrast_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.Contrast);
	}

	private void i_blackwhite_but_Click(object sender, EventArgs e)
	{
		ActivateImageTool(ImageEditToolKind.BW);
	}

	private void i_tool_tolerance_nud_ValueChanged(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		Settings.image_tool_tolerance = (float)i_tool_tolerance_nud.Value;
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void i_tool_size_nud_ValueChanged(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		Settings.image_tool_size = (float)i_tool_size_nud.Value;
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void i_tool_strength_nud_ValueChanged(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		Settings.image_tool_strength = (float)i_tool_strength_nud.Value;
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void i_tool_smooth_nud_ValueChanged(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		Settings.image_tool_smooth = (float)i_tool_smooth_nud.Value;
		_mainView.Invalidate();
		_mainView.Focus();
	}

	private void i_tool_shape_combo_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;
		if (!EnsureSelectedImageForPaint(out _))
			return;
		Settings.image_tool_shape = (ImageToolShapeType)i_tool_shape_combo.SelectedIndex;
		_mainView.Invalidate();
		_mainView.Focus();
	}

	/// <summary>
	/// Привязка размера инструмента к экрану.
	///
	/// Выключено: размер понимается в пикселях изображения — «5» это пять
	/// пикселей растра при любом масштабе просмотра. Включено: размер
	/// экранный, кисть выглядит одинаково при любом зуме, но затрагивает
	/// тем меньше пикселей, чем сильнее приближена картинка.
	///
	/// Проверки на выделенное изображение здесь нет намеренно: это
	/// настройка инструмента, а не действие над картинкой, и переключать
	/// её осмысленно и до того, как что-то выбрано.
	/// </summary>
	private void i_size_lock_check_CheckedChanged(object sender, EventArgs e)
	{
		Settings.image_tool_size_lock = i_size_lock_check.Checked;

		// Перерисовка нужна сразу: под курсором висит кружок предпросмотра
		// размера, и он обязан поменяться вместе с настройкой.
		_mainView?.Invalidate();
		_mainView?.Focus();
	}

	/// <summary>
	/// Показ выделенного изображения в разрешении документа.
	///
	/// Флаг живёт на самой фигуре, а не в Settings: у двух картинок на
	/// одном листе режим может отличаться, и общая настройка переключала бы
	/// их обе. Чекбокс панели инструментов — второй вид на то же свойство,
	/// что и строка «Actual dpi» в панели свойств.
	/// </summary>
	private void i_actual_resolution_check_CheckedChanged(object sender, EventArgs e)
	{
		if (_syncingActualResolution)
			return;

		var image = GetSingleSelectedImage();

		if (image == null)
			return;

		bool value = i_actual_resolution_check.Checked;

		if (image.ActualResolution == value)
			return;

		bool before = image.ActualResolution;

		image.ActualResolution = value;

		CurrentPage?.doc.Undo.Push(
			new PropertyChangeAction<bool>(
				L.T("Actual dpi"),
				before,
				value,
				v =>
				{
					image.ActualResolution = v;
					SyncActualResolutionCheck();
					_mainView?.Invalidate();
				}));

		if (CurrentPage != null)
			CurrentPage.doc.need_save = true;

		RebuildPropertyPanel();

		_mainView?.Invalidate();
		_mainView?.Focus();
	}

	private bool _syncingActualResolution;

	private ImageShape? GetSingleSelectedImage()
	{
		var selected = sel?.Selected;

		return selected != null && selected.Count == 1
			? selected[0] as ImageShape
			: null;
	}

	/// <summary>
	/// Подтягивает состояние чекбокса под выделение. Подписка снимается
	/// на время присваивания, иначе синхронизация сама вызвала бы
	/// обработчик и записала бы значение обратно в фигуру.
	/// </summary>
	private void SyncActualResolutionCheck()
	{
		var image = GetSingleSelectedImage();

		_syncingActualResolution = true;

		try
		{
			i_actual_resolution_check.Enabled = image != null;
			i_actual_resolution_check.Checked = image?.ActualResolution ?? false;
		}
		finally
		{
			_syncingActualResolution = false;
		}
	}

	#endregion
}