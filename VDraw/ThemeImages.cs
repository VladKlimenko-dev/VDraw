using System.Drawing;
using System.IO;

using SkiaSharp;

using VDraw.Ui;

namespace VDraw;

/// <summary>Куда у элемента подставляется картинка.</summary>
public enum ImageSlot
{
	/// <summary>Image — значок кнопки или пункта меню.</summary>
	Foreground,

	/// <summary>BackgroundImage — подложка, например шахматка под цветом.</summary>
	Background
}

/// <summary>
/// Значки кнопок в двух темах.
///
/// Картинки лежат в ресурсах проекта. Тёмный вариант — тот же файл
/// с суффиксом «_dark»: «rect» и «rect_dark». Суффикс, а не отдельная
/// папка ресурсов, потому что ресурсы проекта — плоский список имён,
/// и вторая ветка потребовала бы второго .resx и правил его сборки.
///
/// Если тёмного варианта нет, берётся светлый. Это позволяет рисовать
/// значки по одному, а не все сразу: программа продолжает работать
/// с наполовину готовым набором, просто часть значков в тёмной теме
/// пока выглядит по-старому.
///
/// Соответствие «элемент — имя картинки» задано здесь таблицей, а не
/// вытащено из дизайнера: связь теряется в момент присваивания,
/// Properties.Resources.rect возвращает новый Bitmap на каждый вызов,
/// и по самой картинке узнать её имя уже нельзя.
/// </summary>
public static class ThemeImages
{
	private sealed record Entry(
		Control? Control,
		ToolStripItem? Item,
		string Name,
		ImageSlot Slot,
		int Size,
		bool ClearText);

	/// <summary>Размер значка кнопки тулбара по умолчанию.</summary>
	public const int ButtonIconSize = 36;

	/// <summary>Размер значка пункта меню по умолчанию.</summary>
	public const int MenuIconSize = 24;

	private static readonly List<Entry> _entries = new();

	/// <summary>
	/// Кэш загруженных картинок.
	///
	/// ResourceManager отдаёт НОВЫЙ Bitmap на каждый запрос, а тема
	/// переключается сколько угодно раз за сеанс. Без кэша каждое
	/// переключение оставляло бы позади полсотни неосвобождённых растров.
	/// </summary>
	private static readonly Dictionary<string, Image?> _cache = new(StringComparer.Ordinal);

	private static bool _hooked;

	#region Регистрация

	/// <summary>
	/// Регистрирует значок кнопки.
	///
	/// clearText = true нужен кнопкам, которые раньше показывали символ
	/// иконочного шрифта: подпись с глифом снимается, как только для
	/// кнопки нашёлся настоящий значок. Снимается именно при наличии
	/// значка — пока файла нет, глиф остаётся на месте, и набор можно
	/// переводить на SVG по частям.
	/// </summary>
	public static void Add(
		Control control,
		string name,
		ImageSlot slot = ImageSlot.Foreground,
		int size = 0,
		bool clearText = false)
		=> _entries.Add(new Entry(control, null, name, slot,
			size > 0 ? size : ButtonIconSize, clearText));

	/// <summary>
	/// Перегрузка для пунктов меню. ToolStripItem не наследует Control,
	/// поэтому одной перегрузкой обойтись нельзя — разрешение перегрузок
	/// само разведёт вызовы по типу поля.
	/// </summary>
	public static void Add(
		ToolStripItem item,
		string name,
		ImageSlot slot = ImageSlot.Foreground,
		int size = 0)
		=> _entries.Add(new Entry(null, item, name, slot,
			size > 0 ? size : MenuIconSize, false));

	/// <summary>
	/// Заносит в таблицу все значки, расставленные дизайнером, и
	/// подставляет их по текущей теме.
	///
	/// Вызывать из конструктора формы после InitializeComponent. Список
	/// повторяет присваивания в дизайнере: добавили там новый значок —
	/// добавьте строку и сюда, иначе он останется светлым навсегда.
	/// </summary>
	public static void RegisterMainFormImages(MainForm form)
	{
		if (_entries.Count > 0)
			return;

		form.RegisterThemeImages();

		if (!_hooked)
		{
			_hooked = true;
			SkUiTheme.ThemeChanged += Apply;
		}

		Apply();
	}

	#endregion

	#region Применение

	/// <summary>Подставляет вариант картинки, отвечающий текущей теме.</summary>
	public static void Apply()
	{
		bool dark = SkUiTheme.Current.IsDark;

		foreach (var e in _entries)
		{
			var image = Get(e.Name, dark, e.Size);

			if (e.Item != null)
			{
				e.Item.Image = image;
				continue;
			}

			if (e.Control == null)
				continue;

			if (e.Slot == ImageSlot.Background)
			{
				e.Control.BackgroundImage = image;
				continue;
			}

			if (e.Control is not ButtonBase button)
				continue;

			button.Image = image;

			if (e.ClearText && image != null)
				button.Text = string.Empty;
		}
	}

	/// <summary>
	/// Картинка по имени. В тёмной теме сначала ищется вариант с суффиксом,
	/// и при его отсутствии возвращается светлый — так набор значков можно
	/// пополнять постепенно.
	/// </summary>
	public static Image? Get(string name, bool? dark = null, int size = 0)
	{
		bool useDark = dark ?? SkUiTheme.Current.IsDark;

		if (size <= 0)
			size = ButtonIconSize;

		// Сначала SVG: он перекрашивается под тему на лету, поэтому
		// отдельного «_dark» ему не нужно.
		var vector = LoadSvg(name, size, useDark);

		if (vector != null)
			return vector;

		if (useDark)
		{
			var special = Load(name + "_dark");

			if (special != null)
				return special;
		}

		return Load(name);
	}

	/// <summary>
	/// Соответствие «имя ресурса PNG — имя файла SVG».
	///
	/// Таблица нужна потому, что имена у двух наборов разные: PNG
	/// назывались по форме («rect», «Combine_curve»), а SVG — по действию
	/// («shape-rect», «bool-combine»). Переименовывать регистрации
	/// в дизайнере и в RegisterThemeImages значило бы трогать полсотни
	/// строк ради косметики; таблица делает то же самое в одном месте.
	///
	/// Имя, которого здесь нет, ищется среди SVG как есть: новые
	/// регистрации сразу пишутся именем файла значка.
	/// </summary>
	private static readonly Dictionary<string, string> _svgNames =
		new(StringComparer.OrdinalIgnoreCase)
		{
			["lines"] = "shape-lines",
			["curve"] = "shape-curve",
			["rect"] = "shape-rect",
			["rounded_rect"] = "shape-rect-rounded",
			["chamfered_rect"] = "shape-rect-chamfered",
			["circle2p"] = "shape-circle-2p",
			["circle3p"] = "shape-circle-3p",
			["ellipse3p"] = "shape-ellipse-3p",
			["ellipse5p"] = "shape-ellipse-5p",
			["poly"] = "shape-poly",
			["free_poly"] = "shape-poly-free",
			["text"] = "shape-text",
			["image"] = "shape-image",
			["measure_dist"] = "measure-distance",
			["measure_angle"] = "measure-angle",
			["custom_shapes"] = "customShapesToolStripMenuItem",
			["convert_to_curve"] = "convert-to-curve",
			["Combine_curve"] = "bool-combine",
			["union_curve"] = "bool-union",
			["substract_curve"] = "bool-subtract",
			["intersect_curve"] = "bool-intersect",
			["xor_curve"] = "bool-exclude",
			["scissors_line"] = "cut-line",
			["scissors_line1"] = "cut-line",
			["scissors_curve"] = "cut-curve",
			["scissors_curve1"] = "cut-curve",
			["scissors_cap"] = "cut-cap",
			["Nodes_merge"] = "node-merge",
			["Nodes_split"] = "node-split",
			["Nodes_straight"] = "node-straight",
			["Node_tangent"] = "node-tangent",
			["Node_sharp"] = "node-sharp",
			["X"] = "node-delete",
			["node_new"] = "node-insert",
			["rulers"] = "view-rulers",
			["guidelines"] = "view-guides",
			["snapping"] = "snap-objects",
			["dynamic_guidelines"] = "snap-dynamic-guides",
			["curve_edit"] = "tool-edit-nodes",
			["fill_none"] = "fill-none",
			["fill_uniform"] = "fill-solid",
			["fill_pattern"] = "fill-pattern",
			["fill_texture"] = "fill-texture",
			["fill_steps"] = "fill-steps",
			["fountain_fill_linear"] = "fill-gradient-linear",
			["fountain_fill_radial"] = "fill-gradient-radial",
			["fountain_fill_square"] = "fill-gradient-rect"
		};

	/// <summary>Имя файла значка для имени, под которым он зарегистрирован.</summary>
	public static string SvgNameFor(string name)
		=> _svgNames.TryGetValue(name, out var mapped) ? mapped : name;

	private static Image? LoadSvg(string name, int size, bool dark)
	{
		string file = SvgNameFor(name);
		string key = file + "#" + size + (dark ? "#d" : "#l");

		if (_cache.TryGetValue(key, out var cached))
			return cached;

		var palette = dark ? SkUiTheme.Dark : SkUiTheme.Light;

		using var rendered = SvgIcon.Render(file, size, palette.Text, palette.Surface);

		Image? image = ToGdi(rendered);

		_cache[key] = image;

		return image;
	}

	/// <summary>
	/// Растр Skia в картинку WinForms.
	///
	/// Через PNG в памяти, а не копированием пикселей: значок рисуется
	/// один раз на размер и тему и остаётся в кэше, поэтому короткий
	/// и заведомо правильный путь здесь важнее быстрого.
	/// </summary>
	private static Image? ToGdi(SKBitmap? bitmap)
	{
		if (bitmap == null)
			return null;

		try
		{
			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);

			if (data == null)
				return null;

			var stream = new MemoryStream();

			data.SaveTo(stream);
			stream.Position = 0;

			// Поток не освобождается намеренно: Bitmap читает из него
			// по мере надобности и падает, если поток закрыт раньше.
			return new Bitmap(stream);
		}
		catch
		{
			return null;
		}
	}

	private static Image? Load(string name)
	{
		if (_cache.TryGetValue(name, out var cached))
			return cached;

		Image? image = null;

		try
		{
			image = Properties.Resources.ResourceManager.GetObject(name) as Image;
		}
		catch
		{
			// Ресурса с таким именем нет — для «_dark» это обычное дело.
		}

		_cache[name] = image;
		return image;
	}

	#endregion
}
