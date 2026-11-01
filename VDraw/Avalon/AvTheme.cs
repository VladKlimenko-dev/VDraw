using System.IO;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using SkiaSharp;

// Проект собран с ImplicitUsings и UseWindowsForms, поэтому в каждом файле
// неявно присутствуют System.Windows.Forms и System.Drawing. Их простые имена
// (Control, Button, Color, Application, Bitmap...) совпадают с именами
// Avalonia, и без псевдонимов каждое такое имя было бы неоднозначным (CS0104).
// Псевдоним приоритетнее using-директивы, поэтому эти строки и решают вопрос.
// Они повторяются в каждом файле папки Avalon — другого способа нет: глобальный
// псевдоним сломал бы остальные сто файлов проекта, написанных на WinForms.
using Application = Avalonia.Application;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Color = Avalonia.Media.Color;

namespace VDraw.Ui.Avalon;

/// <summary>
/// Палитра и размеры интерфейса Avalonia.
///
/// Цвета НЕ задаются здесь заново, а берутся из SkUiTheme.Current. Пока часть
/// интерфейса рисуется через Skia, а часть через Avalonia, два независимых
/// набора цветов разъехались бы при первой же правке — панель свойств
/// отличалась бы от тулбаров на пару тонов, и причину пришлось бы искать
/// в двух местах.
///
/// Динамические ресурсы Avalonia (DynamicResource) здесь сознательно
/// не используются. Панель свойств и без них пересобирается целиком при смене
/// темы — так было и в Skia-версии, потому что вместе с цветами меняются
/// значки. Раз пересборка всё равно происходит, кисти достаточно прочитать
/// в момент создания контрола: это короче, не требует ни словарей тем,
/// ни разметки, и не может «не сработать» на хосте внутри WinForms.
/// </summary>
public static class AvTheme
{
	/// <summary>Радиус скругления полей и кнопок — как у SkUiTheme.Radius.</summary>
	public const double Radius = 6;

	public const double RadiusSmall = 4;

	/// <summary>Высота строки свойства.</summary>
	public const double RowHeight = 28;

	/// <summary>Высота поля ввода.</summary>
	public const double InputHeight = 26;

	/// <summary>Высота заголовка раздела.</summary>
	public const double HeaderHeight = 28;

	/// <summary>Зазор между соседними контролами.</summary>
	public const double Gap = 6;

	/// <summary>Отступ содержимого от края панели.</summary>
	public const double Padding = 8;

	/// <summary>Ширина колонки с подписями свойств.</summary>
	public const double LabelWidth = 96;

	/// <summary>Размер шрифта подписей и полей.</summary>
	public const double FontSize = 12;

	public static IBrush PanelBack => Brush(SkUiTheme.Current.PanelBack);
	public static IBrush PanelBack2 => Brush(SkUiTheme.Current.PanelBack2);
	public static IBrush Surface => Brush(SkUiTheme.Current.Surface);
	public static IBrush Surface2 => Brush(SkUiTheme.Current.Surface2);
	public static IBrush BorderLine => Brush(SkUiTheme.Current.Border);
	public static IBrush Text => Brush(SkUiTheme.Current.Text);
	public static IBrush TextMuted => Brush(SkUiTheme.Current.TextMuted);
	public static IBrush Accent => Brush(SkUiTheme.Current.Accent);
	public static IBrush Hover => Brush(SkUiTheme.Current.Hover);
	public static IBrush Selected => Brush(SkUiTheme.Current.Selected);
	public static IBrush ButtonPressed => Brush(SkUiTheme.Current.ButtonPressed);

	public static bool IsDark => SkUiTheme.Current.IsDark;

	/// <summary>
	/// Переключает вариант темы Avalonia.
	///
	/// Нужно встроенным контролам — TextBox, ComboBox, Slider берут цвета
	/// у темы Fluent, а не у нашей палитры, и без этой строки остались бы
	/// светлыми на тёмной панели.
	/// </summary>
	public static void Apply(SkThemeKind kind)
	{
		if (Application.Current is not { } app)
			return;

		app.RequestedThemeVariant = kind == SkThemeKind.Light
			? ThemeVariant.Light
			: ThemeVariant.Dark;
	}

	public static Color ToAv(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	public static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);

	/// <summary>
	/// Кисти кэшируются по цвету: одна и та же строка свойств создаётся
	/// десятками, и каждая просила бы себе отдельный объект кисти.
	/// </summary>
	private static readonly Dictionary<uint, SolidColorBrush> _brushes = new();

	public static IBrush Brush(SKColor c)
	{
		uint key = (uint)c;

		if (_brushes.TryGetValue(key, out var brush))
			return brush;

		brush = new SolidColorBrush(ToAv(c));
		_brushes[key] = brush;

		return brush;
	}
}

/// <summary>
/// Мосты между Skia и Avalonia.
///
/// Превью узоров, текстур и штриховок строятся существующим кодом в SKBitmap,
/// и переписывать их ради Avalonia незачем — достаточно перевести готовую
/// картинку. Перевод идёт через PNG в памяти: он короче и надёжнее прямого
/// копирования пикселей, а картинки здесь мелкие (значок 36x36, превью
/// градиента в несколько десятков пикселей), поэтому цена перекодирования
/// незаметна.
/// </summary>
public static class AvInterop
{
	public static Bitmap? ToAvalonia(this SKBitmap? bmp)
	{
		if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
			return null;

		try
		{
			using var image = SKImage.FromBitmap(bmp);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);

			if (data == null)
				return null;

			using var ms = new MemoryStream();

			data.SaveTo(ms);
			ms.Position = 0;

			return new Bitmap(ms);
		}
		catch
		{
			// Картинка — украшение, а не содержание: кнопка без значка
			// работает, а исключение при сборке панели оставило бы
			// пользователя вообще без свойств.
			return null;
		}
	}

	/// <summary>
	/// Значок для панели свойств.
	///
	/// Сначала ищется SVG в Resources\Icons — он перекрашивается под тему
	/// при отрисовке. Если файла нет, берётся прежний PNG из Resources:
	/// так набор значков можно пополнять по частям, и панель не остаётся
	/// пустой там, где нового значка ещё не нарисовали.
	///
	/// Имя принимается и с расширением («fill_none.png»), и без него:
	/// перенесённый из Skia-версии код передаёт имя файла PNG, а новый —
	/// имя значка.
	/// </summary>
	public static Bitmap? LoadUiBitmap(string fileName, int size = 32)
	{
		string key = fileName + "#" + size + (SkUiTheme.Current.IsDark ? "#d" : "#l");

		if (_uiBitmaps.TryGetValue(key, out var cached))
			return cached;

		Bitmap? result = LoadSvg(fileName, size) ?? LoadPng(fileName);

		// Кэшируется и неудача: панель пересобирается при каждой смене
		// выделения, и отсутствующий файл иначе искался бы на диске заново
		// десятки раз в секунду.
		_uiBitmaps[key] = result;

		return result;
	}

	private static Bitmap? LoadSvg(string fileName, int size)
	{
		string name = Path.GetFileNameWithoutExtension(fileName);

		if (string.IsNullOrEmpty(name))
			return null;

		name = ThemeImages.SvgNameFor(name);

		var palette = SkUiTheme.Current;

		using var rendered = SvgIcon.Render(name, size, palette.Text, palette.Surface);

		return rendered.ToAvalonia();
	}

	private static Bitmap? LoadPng(string fileName)
	{
		try
		{
			if (!Path.HasExtension(fileName))
				fileName += ".png";

			string path = Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);

			return File.Exists(path) ? new Bitmap(path) : null;
		}
		catch
		{
			return null;
		}
	}

	private static readonly Dictionary<string, Bitmap?> _uiBitmaps =
		new(StringComparer.OrdinalIgnoreCase);
}
