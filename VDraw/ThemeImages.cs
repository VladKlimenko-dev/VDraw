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
	private sealed record Entry(Control? Control, ToolStripItem? Item, string Name, ImageSlot Slot);

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

	public static void Add(Control control, string name, ImageSlot slot = ImageSlot.Foreground)
		=> _entries.Add(new Entry(control, null, name, slot));

	/// <summary>
	/// Перегрузка для пунктов меню. ToolStripItem не наследует Control,
	/// поэтому одной перегрузкой обойтись нельзя — разрешение перегрузок
	/// само разведёт вызовы по типу поля.
	/// </summary>
	public static void Add(ToolStripItem item, string name, ImageSlot slot = ImageSlot.Foreground)
		=> _entries.Add(new Entry(null, item, name, slot));

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
			var image = Get(e.Name, dark);

			if (e.Item != null)
			{
				e.Item.Image = image;
				continue;
			}

			if (e.Control == null)
				continue;

			if (e.Slot == ImageSlot.Background)
				e.Control.BackgroundImage = image;
			else if (e.Control is ButtonBase button)
				button.Image = image;
		}
	}

	/// <summary>
	/// Картинка по имени. В тёмной теме сначала ищется вариант с суффиксом,
	/// и при его отсутствии возвращается светлый — так набор значков можно
	/// пополнять постепенно.
	/// </summary>
	public static Image? Get(string name, bool? dark = null)
	{
		bool useDark = dark ?? SkUiTheme.Current.IsDark;

		if (useDark)
		{
			var special = Load(name + "_dark");

			if (special != null)
				return special;
		}

		return Load(name);
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
