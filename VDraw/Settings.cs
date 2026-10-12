using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;
public static class Settings
{
	/// <summary>Current layer. Positive direction, increasing for each next shape. Higer value - further from camera.</summary>
	public static int layer_start = 0;

	#region Page view
	public static SKColorF background_color = new SKColorF(1f, 1f, 1f, 1f); // White
	public static SKColorF page_default_background_color = new SKColorF(1f, 1f, 1f, 1f); // White
	public static SKColorF page_border_color = new SKColorF(0f, 0f, 0f, 1f);        // Black
	public static SKColorF page_shadow_color = new SKColorF(0f, 0f, 0f, 0.5f);  // Semi-transparent black.
	/// <summary>Цвет рамки печатаемой области принтера по умолчанию.</summary>
	public static SKColor print_border_color = new SKColor(190, 190, 190);
	public static bool show_print_margins = false;
	public static SKColorF selection_line_color = new SKColorF(0f, 0f, 1f, 1f); // Blue
	public static bool cap_scissors = true;
	public static float grip_radius = 20f;
	#endregion

	#region Guidelines
	public static bool show_guidelines = true;
	public static SKColor guide_color = new SKColor(0, 0, 0, 255);
	public static SKColor guide_focused_color = new SKColor(100, 100, 200, 255);
	public static SKColor guide_selected_color = new SKColor(100, 100, 200, 255);
	public static float guide_thickness = 1f;
	public static float guide_focused_thickness = 1.5f;
	public static float guide_selected_thickness = 2.0f;
	#endregion

	#region Grid
	public static bool show_grid = false;
	/// <summary>
	/// Цвета сетки. Три уровня: обычный шаг, каждый пятый (у дюймов —
	/// шестой) и каждый десятый (у дюймов — двенадцатый). Без разной
	/// насыщенности клетки не считаются глазом и сетка читается как фон.
	/// </summary>
	public static SKColor grid_minor_color = new SKColor(210, 210, 210);
	public static SKColor grid_medium_color = new SKColor(185, 185, 185);
	public static SKColor grid_major_color = new SKColor(150, 150, 150);

	/// <summary>
	/// Цвет осей сетки — линий, проходящих через page_zero. Темнее всех
	/// прочих: это не просто «каждая десятая», а начало отсчёта, от
	/// которого считаются координаты фигур и деления линеек.
	/// </summary>
	public static SKColor grid_zero_color = new SKColor(105, 105, 105);
	#endregion

	#region Rulers
	public static bool show_rulers = true;
	public static int RulerThickness = 20;
	public static int RulerZeroMarkSize = 18;
	public static int RulerMajorMarkSize = 15;
	public static int RulerMinorMarkSize = 8;
	#endregion

	#region Resize/Rotate Handles
	public static float handle_size = 8f;
	public static SKColor handle_fill_color = new SKColor(50, 50, 100, 220);
	public static SKColor handle_line_color = new SKColor(0, 0, 0, 255); // Black
	public static SKColor handle_focused_fill_color = new SKColor(150, 20, 150, 255); // Dark Purple
	public static SKColor handle_focused_line_color = new SKColor(50, 50, 50, 255); // Dark gray
	public static float handle_line_thickness = 1f;
	public static float handle_focused_line_thickness = 1.5f;
	public static SKColor handle_control_point_color = new SKColor(255, 0, 0, 200);
	public static SKColor text_handle_fill_color = new SKColor(150, 150, 220, 255);
	public static SKColor text_handle_line_color = new SKColor(100, 100, 100, 255);
	#endregion

	#region ImagePainting
	public static SKColor image_selection_line_color = new SKColor(180, 180, 220, 255);
	public static float image_selection_line_width = 1f;
	public static DashStyle image_selection_line_dash_style = DashStyle.Dash;
	public static SKColor image_paint_fore_color = new SKColor(150, 0, 0, 180);
	public static SKColor image_paint_back_color = new SKColor(255, 255, 255, 255); // White
	public static float image_tool_size = 20f;

	/// <summary>
	/// Привязан ли размер растрового инструмента к экрану.
	///
	/// Выключено (по умолчанию): размер задан В ПИКСЕЛЯХ ИЗОБРАЖЕНИЯ —
	/// «5» значит пять пикселей растра при любом масштабе просмотра,
	/// и мазок получается один и тот же независимо от того, приблизили
	/// картинку или отдалили.
	///
	/// Включено: размер задан в пикселях ЭКРАНА — кисть выглядит
	/// одинаково крупной при любом зуме, но затрагивает тем меньше
	/// пикселей растра, чем сильнее приближено изображение.
	/// </summary>
	public static bool image_tool_size_lock = false;
	public static float image_tool_tolerance = 0.2f;
	public static float image_tool_strength = 0.13f;
	public static float image_tool_smooth = 50f;
	public static ImageToolShapeType image_tool_shape = ImageToolShapeType.Circle;
	#endregion
	
	#region Shape Fill and Stroke
	public static float shape_selected_line_thickness_rate = 1f;
	public static float shape_focused_line_thickness_rate = 1.5f;
	public static SKColor shape_default_line_color = new SKColor(0, 0, 0, 255); // Black

	/// <summary>
	/// Толщина обводки новой фигуры, В МИЛЛИМЕТРАХ.
	///
	/// Значение по умолчанию задано физической величиной осознанно:
	/// «линия в две десятых миллиметра» — это про то, как она выглядит
	/// на бумаге, и такой смысл не должен зависеть от того, размечена
	/// страница в миллиметрах, дюймах или пикселях. В единицы страницы
	/// оно переводится в момент создания фигуры — см. DefaultLineThickness.
	/// </summary>
	public static float shape_default_line_thickness_mm = 0.5f;

	/// <summary>Длина цикла штрихового узора новой фигуры, В МИЛЛИМЕТРАХ.</summary>
	public static float shape_default_pattern_length_mm = 10f;

	/// <summary>
	/// Миллиметров в одной единице АКТИВНОЙ страницы.
	///
	/// Держится статически по той же причине, что и счётчик слоёв
	/// layer_start: фигура создаётся в двух десятках мест, и почти нигде
	/// у создающего кода нет под рукой страницы, а знать масштаб единиц
	/// он обязан — иначе обводка новой фигуры окажется в чужих единицах.
	/// Обновляется при смене страницы (MainView.SetPage) и при смене
	/// единиц страницы (Page.SetShapesNewUoM).
	/// </summary>
	public static float current_mm_per_uom = 1f;

	/// <summary>Толщина обводки новой фигуры в единицах активной страницы.</summary>
	public static float DefaultLineThickness =>
		shape_default_line_thickness_mm / MathF.Max(current_mm_per_uom, 0.0001f);

	/// <summary>Длина цикла узора новой фигуры в единицах активной страницы.</summary>
	public static float DefaultPatternLength =>
		shape_default_pattern_length_mm / MathF.Max(current_mm_per_uom, 0.0001f);

	/// <summary>Переводит величину из миллиметров в единицы активной страницы.</summary>
	public static float MmToCurrentUom(float mm) =>
		mm / MathF.Max(current_mm_per_uom, 0.0001f);
	public static SKColor shape_default_fill_color = new SKColor(200, 200, 200, 255); // Grey
	public static SKColor shape_default_fountain_fill_color1 = new SKColor(80, 80, 150, 255); // Bluish
	public static SKColor shape_default_fountain_fill_color2 = new SKColor(255, 255, 255, 255); // White
	public static SKColor shape_default_fill_pat_forecolor = new SKColor(0, 0, 0, 255); // Black
	public static SKColor shape_default_fill_pat_backcolor = new SKColor(0, 0, 0, 0); // Transparent
	#endregion

	#region Curve Nodes and Points
	public static SKColorF curve_point_color = new SKColor(255, 255, 0, 255);
	public static SKColorF curve_point_border_color = new SKColor(120, 120, 120, 255);
	public static SKColorF curve_focused_point_color = new SKColor(200, 200, 0, 255);
	public static SKColorF curve_focused_point_border_color = new SKColor(120, 120, 120, 255);
	public static SKColorF curve_selected_point_color = new SKColor(255, 150, 0, 255);
	public static SKColorF curve_selected_point_border_color = new SKColor(120, 120, 255, 255);
	public static SKColorF curve_cp_color = new SKColor(100, 100, 250, 255);
	public static SKColorF curve_cp_border_color = new SKColor(100, 100, 120, 255);
	public static SKColorF curve_control_line_color = new SKColor(50, 50, 50, 150);
	public static float curve_control_line_thickness = 1f;
	public static float curve_point_size = 10f;
	public static float curve_cp_size = 8f;
	public static float curve_cp_focused_size = 12f;
	public static float curve_focused_point_size = 15f;
	public static float curve_selected_point_size = 10f;
	#endregion

	#region Tool points
	public static float tool_point_size = 20f;
	public static SKColorF tool_point_color = new SKColorF(0.5f, 0.0f, 1.0f, 1.0f);
	public static SKColorF tool_point_focused_color = new SKColorF(0.5f, 0.0f, 1.0f, 1.0f);
	public static SKColorF tool_point_border_color = new SKColorF(1.0f, 1.0f, 1.0f, 1.0f);
	public static SKColorF tool_point_focused_border_color = new SKColorF(1.0f, 1.0f, 1.0f, 1.0f);
	#endregion

	#region Snapping
	public static bool snap_objects = true;
	public static bool snap_to_guidelines = true;
	public static bool dynamic_guidelines = true;
	public static float snap_dist = 20f;
	public static SKColor snap_point_color = new SKColor(0, 0, 255, 255);
	public static SKColor snap_point_border_color = new SKColor(255, 255, 255, 255);
	public static SKColor snap_line_color = new SKColor(50, 50, 160, 255);
	public static float snap_point_size = 5f;
	#endregion

	#region Pattern fill
	/// <summary>
	/// Default left-mouse click color for pattern drawing
	/// </summary>
	public static Color fill_pat_default_forecolor = Color.Black;
	/// <summary>
	/// Default right-mouse click color for pattern drawing
	/// </summary>
	public static Color fill_pat_default_backcolor = Color.Transparent;
	/// <summary>
	/// Start of pattern image file name
	/// </summary>
	public static string pat_brush_image_file_name = "pat_brush_image_";
	/// <summary>
	/// Start of texture image file name
	/// </summary>
	public static string tex_image_file_name = "tex_image_";
	/// <summary>
	/// List of pattern images
	/// </summary>
	public static List<Image> pat_brushes_images = new List<Image>();
	/// <summary>
	/// List of texture images
	/// </summary>
	public static List<Image> tex_images = new List<Image>();
	/// <summary>
	/// List of patterns images files - Path, file name, extension
	/// </summary>
	public static List<string> pat_brushes_files = new List<string>();
	/// <summary>
	/// List of texture images files - Path, file name, extension
	/// </summary>
	public static List<string> tex_images_files = new List<string>();
	/// <summary>
	/// Check if file name has proper image extension.
	/// </summary>
	/// <param name="name">File name</param>
	/// <returns>True if file has image extension</returns>
	public static bool IsAcceptableImageFileName(string name)
	{
		if (name.EndsWith(".jpg") || name.EndsWith(".png") || name.EndsWith(".bmp") || name.EndsWith(".jpeg"))
			return true;
		return false;
	}
	/// <summary>
	/// Add image to patterns list if file is image. Add file name to the patterns files list.
	/// </summary>
	/// <param name="file_path_name">Existing file,path,name</param>
	/// <returns>Image, file name without extension, index of image in the list</returns>
	public static (Image, string, int)? AddPatternImage(string file_path_name)
	{
		if (!File.Exists(file_path_name))
			return null;
		if (!IsAcceptableImageFileName(file_path_name))
			return null;
		string file_name = Path.GetFileName(file_path_name);
		string new_dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\Brushes\";
		string new_file_name = pat_brush_image_file_name + DateTime.Now.Ticks.ToString();
		string new_file_path_name = new_dir + new_file_name + ".png";
		if (pat_brushes_files.Contains(new_file_path_name))
			return null;
		for (int i = 0; i < pat_brushes_files.Count; i++)
			if (pat_brushes_files[i].Contains(file_name))
				return null;
		File.Copy(file_path_name, new_file_path_name);
		Image im = Image.FromFile(new_file_path_name);
		pat_brushes_images.Add(im);
		pat_brushes_files.Add(new_file_path_name);
		return new(im, new_file_name, pat_brushes_files.Count - 1);
	}
	/// <summary>
	/// Add image to patterns list if file is image. Add file name to the patterns files list.
	/// </summary>
	/// <param name="file_path_name">Existing file,path,name</param>
	/// <returns>Image, file name without extension, index of image in the list</returns>
	public static (Image, string, int)? AddTextureImage(string file_path_name)
	{
		if (!File.Exists(file_path_name))
			return null;
		if (!IsAcceptableImageFileName(file_path_name))
			return null;
		string file_name = Path.GetFileName(file_path_name);
		string new_dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\Brushes\";
		string new_file_name = tex_image_file_name + DateTime.Now.Ticks.ToString();
		string new_file_path_name = new_dir + new_file_name + ".png";
		if (tex_images_files.Contains(new_file_path_name))
			return null;
		for (int i = 0; i < tex_images_files.Count; i++)
			if (tex_images_files[i].Contains(file_name))
				return null;
		File.Copy(file_path_name, new_file_path_name);
		Image im = Image.FromFile(new_file_path_name);
		tex_images.Add(im);
		tex_images_files.Add(new_file_path_name);
		return new(im, new_file_name, tex_images_files.Count - 1);
	}

	public static void LoadBrushesImages()
	{
		List<string> fs = System.IO.Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\Brushes\").ToList<string>();
		pat_brushes_files = fs.FindAll(o => o.Contains(pat_brush_image_file_name) && o.EndsWith(".png"));
		tex_images_files = fs.FindAll(o => o.Contains(tex_image_file_name) && o.EndsWith(".png"));
		pat_brushes_images.Clear();
		for (int i = 0; i < pat_brushes_files.Count; i++)
		{
			using (var bmpTemp = new Bitmap(pat_brushes_files[i]))
			{
				pat_brushes_images.Add(new Bitmap(bmpTemp));
			}
		}
		tex_images.Clear();
		for (int i = 0; i < tex_images_files.Count; i++)
		{
			using (var bmpTemp = new Bitmap(tex_images_files[i]))
			{
				tex_images.Add(new Bitmap(bmpTemp));
			}
		}
	}
	#endregion

	#region Units and Paper
	/// <summary>List of available paper formats</summary>
	public static List<PaperFormat> paper_formats = new List<PaperFormat>();
	/// <summary>List of available units of measurement</summary>
	public static List<UoM> units = new List<UoM>();

	public static void SetupUnits()
	{
		units.Clear();
		units.Add(new UoM("mm", 1.0f));
		units.Add(new UoM("cm", 10.0f));
		units.Add(new UoM("m", 1000.0f));
		units.Add(new UoM("in", 25.4f));
		units.Add(new UoM("ft", 304.8f));
		units.Add(new UoM("px", 1.0f));
	}

	public static void SetupPaperFormats()
	{
		paper_formats.Clear();
		paper_formats.Add(new PaperFormat("A7", 74, 105));
		paper_formats.Add(new PaperFormat("A6", 105, 148));
		paper_formats.Add(new PaperFormat("A5", 148, 210));
		paper_formats.Add(new PaperFormat("A4", 210, 297));
		paper_formats.Add(new PaperFormat("A3", 297, 420));
		paper_formats.Add(new PaperFormat("A2", 420, 594));
		paper_formats.Add(new PaperFormat("A1", 594, 841));
		paper_formats.Add(new PaperFormat("A0", 841, 1189));
		paper_formats.Add(new PaperFormat("Custom", 100, 100));
	}

	public static (PaperFormat fortmat, PageOrientation oritentation)? GetPaperFormatMM(float w, float h)
	{
		for (int i = 0; i < paper_formats.Count; i++)
		{
			if (paper_formats[i].width_mm == w && paper_formats[i].height_mm == h)
				return (paper_formats[i], PageOrientation.Vertical);
			if (paper_formats[i].width_mm == h && paper_formats[i].height_mm == w)
				return (paper_formats[i], PageOrientation.Horizontal);
		}
		return null;
	}
	#endregion
}

public class PaperFormat
{
	public string title;
	public float width_mm;
	public float height_mm;

	public PaperFormat(string title, float width_mm, float height_mm)
	{
		this.title = title;
		this.width_mm = width_mm;
		this.height_mm = height_mm;
	}

	public override string ToString()
	{
		return $"{title}, {width_mm}mm x {height_mm}mm";
	}
}

public class UoM
{
	public string title;
	/// <summary>
	/// Coefficient to convert to mm
	/// </summary>
	public float cmm = 1.0f;
	/// <summary>
	/// Coefficient to convert to inches
	/// </summary>
	public float cin = 1.0f;

	public UoM(string title, float coefficient_to_mm)
	{
		this.title = title;
		this.cmm = coefficient_to_mm;
		this.cin = coefficient_to_mm / 25.4f;
	}

	/// <summary>Заголовок пиксельных единиц. Сравнение по строке — как и везде в UoM.</summary>
	public const string PixelTitle = "px";

	/// <summary>
	/// Пиксель — единица не физическая: его размер задаётся разрешением
	/// страницы, а не константой. Поэтому cmm для него смысла не имеет,
	/// и все пересчёты идут через MmPerUnit.
	/// </summary>
	public bool IsPixels => title == PixelTitle;

	/// <summary>
	/// Миллиметров в одной единице при заданном разрешении.
	///
	/// Для физических единиц разрешение не важно и возвращается cmm.
	/// Для пикселей — 25.4 / DPI: именно столько миллиметров занимает
	/// один пиксель. Раньше px был заведён с cmm = 1, то есть вёл себя
	/// как миллиметр, и страница в пикселях получалась в 3,8 раза
	/// крупнее настоящей при стандартных 96 DPI.
	/// </summary>
	public float MmPerUnit(int dpi)
	{
		if (!IsPixels)
			return cmm;

		return 25.4f / Math.Max(1, dpi);
	}

	/// <summary>Переводит значение из этих единиц в миллиметры.</summary>
	public float GetMM(float value, int dpi)
		=> value * MmPerUnit(dpi);
	public override string ToString()
	{
		return $"{title}, {cmm}";
	}
}