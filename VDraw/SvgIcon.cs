using System.Globalization;
using System.IO;
using System.Xml.Linq;

using SkiaSharp;

namespace VDraw;

/// <summary>
/// Отрисовка значков из SVG.
///
/// Свой разборщик, а не сторонняя библиотека, по трём причинам.
///
/// Во-первых, значки однородны: все 154 файла состоят только из элементов
/// path с набором атрибутов d, fill, stroke, stroke-width и transform вида
/// translate или matrix. Полный разборщик SVG здесь не нужен, а путь к
/// SKPath и без него читает сама Skia (ParseSvgPathData).
///
/// Во-вторых, без новой зависимости: проект уже стоит на SkiaSharp 4,
/// и подбирать к нему совместимую версию Svg.Skia значило бы рисковать
/// тем же конфликтом версий, что и с Avalonia.
///
/// В-третьих — перекраска. Значки нарисованы чёрным по белому, а приложение
/// умеет тёмную тему. Готовая библиотека отдала бы картинку как есть,
/// и для тёмной темы пришлось бы держать второй комплект файлов. Здесь же
/// чёрный заменяется цветом текста темы, а белый — цветом подложки прямо
/// при отрисовке, и комплект нужен один.
/// </summary>
public static class SvgIcon
{
	/// <summary>Папка со значками рядом с исполняемым файлом.</summary>
	public static string FolderPath =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons");

	public static bool Exists(string name) => File.Exists(PathFor(name));

	private static string PathFor(string name) => Path.Combine(FolderPath, name + ".svg");

	/// <summary>
	/// Рисует значок в квадрат заданного размера.
	///
	/// foreground заменяет чёрный, background — белый. Остальные цвета
	/// (серый у шахматки, жёлтый у подсветки узла) остаются своими:
	/// они несут смысл, а не просто контраст с фоном.
	/// </summary>
	public static SKBitmap? Render(string name, int size, SKColor foreground, SKColor background)
	{
		if (size <= 0)
			return null;

		string path = PathFor(name);

		if (!File.Exists(path))
			return null;

		try
		{
			var root = XDocument.Load(path).Root;

			if (root == null)
				return null;

			float srcW = Length(root.Attribute("width")?.Value, 0f);
			float srcH = Length(root.Attribute("height")?.Value, 0f);

			if (srcW <= 0f || srcH <= 0f)
				return null;

			var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);

			using (var canvas = new SKCanvas(bitmap))
			{
				canvas.Clear(SKColors.Transparent);

				// Поле в одну точку: обводка толщиной 4 при уменьшении
				// до 32 точек даёт около полутора точек, и половина
				// наружного края иначе срезалась бы границей картинки.
				const float pad = 1.5f;

				float scale = Math.Min(
					(size - pad * 2) / srcW,
					(size - pad * 2) / srcH);

				canvas.Translate(
					(size - srcW * scale) / 2f,
					(size - srcH * scale) / 2f);

				canvas.Scale(scale);

				foreach (var element in root.Descendants())
				{
					if (element.Name.LocalName != "path")
						continue;

					DrawPath(canvas, element, foreground, background);
				}
			}

			return bitmap;
		}
		catch
		{
			// Значок — украшение: кнопка без него работает, а исключение
			// при сборке формы оставило бы пользователя без окна.
			return null;
		}
	}

	private static void DrawPath(SKCanvas canvas, XElement element, SKColor fg, SKColor bg)
	{
		string? data = element.Attribute("d")?.Value;

		if (string.IsNullOrWhiteSpace(data))
			return;

		using var path = SKPath.ParseSvgPathData(data);

		if (path == null)
			return;

		string? transform = element.Attribute("transform")?.Value;

		if (!string.IsNullOrWhiteSpace(transform))
			path.Transform(ParseTransform(transform));

		path.FillType = string.Equals(
			element.Attribute("fill-rule")?.Value, "evenodd", StringComparison.OrdinalIgnoreCase)
			? SKPathFillType.EvenOdd
			: SKPathFillType.Winding;

		// Заливка. Отсутствие атрибута в SVG означает чёрный, а не «без
		// заливки», — поэтому проверяется именно значение «none», а не null.
		string? fill = element.Attribute("fill")?.Value;

		if (!IsNone(fill))
		{
			using var paint = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Color = MapColor(fill, fg, bg)
			};

			canvas.DrawPath(path, paint);
		}

		string? stroke = element.Attribute("stroke")?.Value;

		if (stroke == null || IsNone(stroke))
			return;

		using var strokePaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = Length(element.Attribute("stroke-width")?.Value, 1f),
			StrokeCap = SKStrokeCap.Round,
			StrokeJoin = SKStrokeJoin.Round,
			Color = MapColor(stroke, fg, bg)
		};

		canvas.DrawPath(path, strokePaint);
	}

	private static bool IsNone(string? value)
		=> string.Equals(value?.Trim(), "none", StringComparison.OrdinalIgnoreCase);

	#region Разбор значений

	private static float Length(string? value, float fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
			return fallback;

		// Единицы у этих файлов не встречаются, но «10px» разобрать
		// дешевле, чем потом искать, почему значок не появился.
		value = value.Trim().TrimEnd('p', 'x', 'P', 'X');

		return float.TryParse(
			value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
			? result
			: fallback;
	}

	/// <summary>Поддерживаются translate и matrix — других в наборе нет.</summary>
	private static SKMatrix ParseTransform(string value)
	{
		value = value.Trim();

		int open = value.IndexOf('(');
		int close = value.LastIndexOf(')');

		if (open <= 0 || close <= open)
			return SKMatrix.CreateIdentity();

		string kind = value.Substring(0, open).Trim();
		float[] a = Numbers(value.Substring(open + 1, close - open - 1));

		if (kind.Equals("translate", StringComparison.OrdinalIgnoreCase))
		{
			return SKMatrix.CreateTranslation(
				a.Length > 0 ? a[0] : 0f,
				a.Length > 1 ? a[1] : 0f);
		}

		if (kind.Equals("matrix", StringComparison.OrdinalIgnoreCase) && a.Length >= 6)
		{
			var m = SKMatrix.CreateIdentity();

			m.ScaleX = a[0];
			m.SkewY = a[1];
			m.SkewX = a[2];
			m.ScaleY = a[3];
			m.TransX = a[4];
			m.TransY = a[5];

			return m;
		}

		if (kind.Equals("scale", StringComparison.OrdinalIgnoreCase) && a.Length >= 1)
			return SKMatrix.CreateScale(a[0], a.Length > 1 ? a[1] : a[0]);

		return SKMatrix.CreateIdentity();
	}

	private static float[] Numbers(string text)
	{
		var parts = text.Split(
			new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

		var result = new float[parts.Length];

		for (int i = 0; i < parts.Length; i++)
		{
			float.TryParse(
				parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]);
		}

		return result;
	}

	/// <summary>
	/// Цвет из SVG в цвет на экране.
	///
	/// Чёрный и белый — служебные: первый несёт рисунок, второй вырезает
	/// в нём просветы. Они и подменяются цветами темы. Названный цвет
	/// разбирается по короткой таблице: SKColor.TryParse понимает только
	/// шестнадцатеричную запись, а в наборе встречаются white и yellow.
	/// </summary>
	private static SKColor MapColor(string? value, SKColor fg, SKColor bg)
	{
		value = value?.Trim();

		if (string.IsNullOrEmpty(value))
			return fg;

		switch (value.ToLowerInvariant())
		{
			case "black":
			case "#000":
			case "#000000":
				return fg;

			case "white":
			case "#fff":
			case "#ffffff":
				return bg;

			case "yellow":
				return new SKColor(255, 214, 0);

			case "red":
				return new SKColor(220, 60, 50);

			case "green":
				return new SKColor(60, 170, 90);

			case "blue":
				return new SKColor(60, 120, 220);

			case "gray":
			case "grey":
				return new SKColor(128, 128, 128);
		}

		return SKColor.TryParse(value, out var parsed) ? parsed : fg;
	}

	#endregion
}
