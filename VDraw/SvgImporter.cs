// SKPath в SkiaSharp 4.151 объявлен устаревшим в пользу SKPathBuilder.
// Здесь он нужен как промежуточное представление контура из файла;
// переход на builder пойдёт вместе со всем остальным кодом фигур.
#pragma warning disable CS0618

using System.Globalization;
using System.Xml.Linq;

using SkiaSharp;

using VDraw.Shapes;

namespace VDraw.Documents;

/// <summary>Что получилось из одного файла SVG.</summary>
public sealed class SvgImportResult
{
	public List<Shape> Shapes { get; } = new();

	/// <summary>Чего разборщик не понял. Показывается пользователю списком.</summary>
	public List<string> Warnings { get; } = new();
}

/// <summary>
/// Чтение SVG в фигуры документа.
///
/// Разбирается то, из чего состоят чертежи и значки: контуры, простые
/// фигуры, группы, преобразования, сплошные обводка и заливка. Текст,
/// градиенты, растры, фильтры и ссылки на определения не разбираются —
/// о каждом таком случае файл сообщает предупреждением, а не молчит:
/// пользователь должен понимать, почему из файла пришло меньше, чем
/// он видел в редакторе.
///
/// Единицы. Внутри SVG координаты безразмерны, а размер на бумаге
/// задают width и height у корня. Если они с единицами — берём их;
/// если без, это пиксели, и переводим по 96 на дюйм, как принято
/// в вебе. viewBox растягивает безразмерные координаты на этот размер.
/// Дальше миллиметры переводятся в единицы листа, потому что в них
/// живут все фигуры документа.
///
/// Собственного разбора пути здесь нет: строку «d» читает Skia
/// (SKPath.ParseSvgPathData), она же знает про дуги и сокращённые
/// команды. Нам остаётся превратить готовый путь в узлы кривой.
/// </summary>
public static class SvgImporter
{
	private const float MmPerInch = 25.4f;

	/// <summary>Пикселей в дюйме. Принятое в вебе значение для файлов без единиц.</summary>
	private const float PixelsPerInch = 96f;

	private sealed class Style
	{
		public SKColor? Fill = SKColors.Black;
		public SKColor? Stroke;
		public float StrokeWidth = 1f;

		/// <summary>Прозрачность элемента целиком. Умножается на обе свои.</summary>
		public float Opacity = 1f;

		public float FillOpacity = 1f;
		public float StrokeOpacity = 1f;

		public bool EvenOdd;

		/// <summary>
		/// Заливка или обводка задана ссылкой на определение — градиентом
		/// или узором. Разобрать её нельзя, и об этом надо предупредить.
		/// </summary>
		public bool PaintServer;

		public Style Clone() => (Style)MemberwiseClone();
	}

	#region Точка входа

	/// <summary>
	/// Читает файл и возвращает фигуры в единицах указанного листа.
	///
	/// Фигуры НЕ добавляются на лист и не сдвигаются: куда их положить,
	/// решает вызывающий — при пакетном импорте файлы выстраиваются
	/// в ряд, и сдвиг считается по габаритам всех сразу.
	/// </summary>
	public static SvgImportResult Import(string path, Page targetPage)
	{
		var result = new SvgImportResult();

		XDocument document;

		try
		{
			document = XDocument.Load(path);
		}
		catch (Exception ex)
		{
			result.Warnings.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");

			return result;
		}

		var root = document.Root;

		if (root == null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
		{
			result.Warnings.Add(L.T("{0}: this is not an SVG file.", System.IO.Path.GetFileName(path)));

			return result;
		}

		float mmPerUom = MathF.Max(targetPage.MmPerUom, 0.0001f);

		var toPage = BuildRootTransform(root, mmPerUom);
		var sheet = ReadStyleSheet(root);

		// SVG 2 разрешает преобразование и на самом корне.
		var rootTransform = ParseTransform(root.Attribute("transform")?.Value);

		if (rootTransform != null)
			toPage = Concat(toPage, rootTransform.Value);

		// Стиль корня читается, а не берётся по умолчанию. На нём держится
		// целый класс наборов значков: Feather, Lucide, Tabler и прочие
		// пишут fill="none" stroke="currentColor" stroke-width="2" именно
		// на элементе svg. Без этого весь набор приходил бы чёрными
		// кляксами — с унаследованной чёрной заливкой и без обводки.
		Walk(root, toPage, ReadStyle(root, new Style(), sheet), result, sheet);

		return result;
	}

	/// <summary>
	/// Матрица из координат файла в единицы листа.
	///
	/// Складывается из двух частей: viewBox растягивает безразмерные
	/// координаты на заявленный размер, а размер переводится в единицы
	/// листа. Нет ни того, ни другого — координаты считаются пикселями,
	/// и перевод идёт только по разрешению.
	/// </summary>
	private static SKMatrix BuildRootTransform(XElement root, float mmPerUom)
	{
		float widthMm = ParseLength(root.Attribute("width")?.Value, 0f);
		float heightMm = ParseLength(root.Attribute("height")?.Value, 0f);

		var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);

		float scaleX;
		float scaleY;
		float offsetX = 0f;
		float offsetY = 0f;

		if (viewBox != null && viewBox.Value.Width > 0f && viewBox.Value.Height > 0f)
		{
			var box = viewBox.Value;

			// Без заявленного размера считаем, что единица viewBox —
			// это пиксель: других сведений о размере в файле нет.
			float targetWidthMm = widthMm > 0f ? widthMm : box.Width * MmPerInch / PixelsPerInch;
			float targetHeightMm = heightMm > 0f ? heightMm : box.Height * MmPerInch / PixelsPerInch;

			scaleX = targetWidthMm / box.Width;
			scaleY = targetHeightMm / box.Height;

			offsetX = -box.Left * scaleX;
			offsetY = -box.Top * scaleY;
		}
		else
		{
			scaleX = MmPerInch / PixelsPerInch;
			scaleY = scaleX;
		}

		// Миллиметры — в единицы листа.
		scaleX /= mmPerUom;
		scaleY /= mmPerUom;
		offsetX /= mmPerUom;
		offsetY /= mmPerUom;

		return new SKMatrix
		{
			ScaleX = scaleX,
			ScaleY = scaleY,
			TransX = offsetX,
			TransY = offsetY,
			Persp2 = 1f
		};
	}

	#endregion

	#region Таблица стилей

	/// <summary>
	/// Правила из элементов style.
	///
	/// Полноценный CSS здесь не нужен и не делается: разбираются только
	/// селекторы по классу (.cls-1), по имени элемента (path) и по
	/// идентификатору (#id) — именно их пишет экспорт из редакторов.
	/// Всё остальное (вложенность, сочетания, медиазапросы) пропускается:
	/// молча пропустить непонятный селектор безопаснее, чем применить его
	/// не к тому элементу.
	/// </summary>
	private sealed class Sheet
	{
		/// <summary>Объявления по имени элемента, например «path».</summary>
		public readonly Dictionary<string, string> ByTag = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Объявления по классу, без точки.</summary>
		public readonly Dictionary<string, string> ByClass = new(StringComparer.Ordinal);

		/// <summary>Объявления по идентификатору, без решётки.</summary>
		public readonly Dictionary<string, string> ById = new(StringComparer.Ordinal);

		public bool IsEmpty => ByTag.Count == 0 && ByClass.Count == 0 && ById.Count == 0;

		/// <summary>
		/// Накладывает правила на стиль — от слабого селектора к сильному:
		/// имя элемента, класс, идентификатор. Так же их упорядочивает CSS.
		/// </summary>
		public void ApplyTo(Style style, XElement element)
		{
			if (IsEmpty)
				return;

			if (ByTag.TryGetValue(element.Name.LocalName, out string? byTag))
				ApplyDeclarations(style, byTag);

			string? classes = element.Attribute("class")?.Value;

			if (classes != null)
			{
				foreach (string name in classes.Split(
					new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (ByClass.TryGetValue(name, out string? byClass))
						ApplyDeclarations(style, byClass);
				}
			}

			string? id = element.Attribute("id")?.Value;

			if (id != null && ById.TryGetValue(id, out string? byId))
				ApplyDeclarations(style, byId);
		}
	}

	/// <summary>
	/// Собирает правила из всех элементов style документа.
	///
	/// Их может быть несколько, и лежать они могут где угодно — чаще
	/// всего в defs, куда общий обход дерева не заходит. Поэтому читаются
	/// отдельным проходом, до него.
	/// </summary>
	private static Sheet ReadStyleSheet(XElement root)
	{
		var sheet = new Sheet();

		foreach (var element in root.Descendants())
		{
			if (!string.Equals(element.Name.LocalName, "style", StringComparison.OrdinalIgnoreCase))
				continue;

			ParseRules(element.Value, sheet);
		}

		return sheet;
	}

	private static void ParseRules(string css, Sheet sheet)
	{
		if (string.IsNullOrWhiteSpace(css))
			return;

		// Комментарии убираются до разбора: внутри них бывают и фигурные
		// скобки, и они сбили бы деление на правила.
		css = RemoveCssComments(css);

		int index = 0;

		while (index < css.Length)
		{
			int open = css.IndexOf('{', index);

			if (open < 0)
				break;

			int close = css.IndexOf('}', open);

			if (close < 0)
				break;

			string selectors = css[index..open];
			string body = css[(open + 1)..close];

			foreach (string selector in selectors.Split(',', StringSplitOptions.RemoveEmptyEntries))
			{
				string name = selector.Trim();

				if (name.Length == 0)
					continue;

				// Составной селектор («g path», «.a > .b») не разбирается:
				// применить его правильно нельзя, а применить как попало
				// хуже, чем не применять.
				if (name.IndexOfAny(new[] { ' ', '>', '+', '~', ':', '[', '*' }) >= 0)
					continue;

				if (name[0] == '.')
					Merge(sheet.ByClass, name[1..], body);
				else if (name[0] == '#')
					Merge(sheet.ById, name[1..], body);
				else if (name[0] != '@')
					Merge(sheet.ByTag, name, body);
			}

			index = close + 1;
		}
	}

	/// <summary>
	/// Правило с тем же селектором не заменяет прежнее, а дописывается:
	/// в CSS побеждает последнее объявление, а объявления применяются
	/// по порядку строки.
	/// </summary>
	private static void Merge(Dictionary<string, string> rules, string key, string body)
	{
		if (key.Length == 0)
			return;

		rules[key] = rules.TryGetValue(key, out string? existing)
			? existing + ";" + body
			: body;
	}

	private static string RemoveCssComments(string css)
	{
		int start = css.IndexOf("/*", StringComparison.Ordinal);

		if (start < 0)
			return css;

		var builder = new System.Text.StringBuilder(css.Length);
		int index = 0;

		while (start >= 0)
		{
			builder.Append(css, index, start - index);

			int end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);

			if (end < 0)
				return builder.ToString();

			index = end + 2;
			start = css.IndexOf("/*", index, StringComparison.Ordinal);
		}

		builder.Append(css, index, css.Length - index);

		return builder.ToString();
	}

	#endregion

	#region Обход дерева

	private static void Walk(
		XElement element, SKMatrix parent, Style inherited, SvgImportResult result, Sheet sheet)
	{
		foreach (var child in element.Elements())
		{
			string tag = child.Name.LocalName.ToLowerInvariant();

			// Скрытый элемент в редакторе не виден, и переносить его
			// на лист — значит отдать пользователю фигуру, которой он
			// в исходнике не видел. Проверяется и у групп: display="none"
			// на группе скрывает всё её содержимое.
			if (IsHidden(child))
				continue;

			WarnAboutIgnoredAttributes(child, result);

			var transform = ParseTransform(child.Attribute("transform")?.Value);
			var matrix = transform == null ? parent : Concat(parent, transform.Value);

			var style = ReadStyle(child, inherited, sheet);

			switch (tag)
			{
				case "g":
				case "svg":
				case "a":
					Walk(child, matrix, style, result, sheet);
					break;

				case "defs":
				case "symbol":
				case "clippath":
				case "mask":
				case "marker":
					// Определения сами ничего не рисуют, поэтому внутрь
					// обход не заходит. О ссылках на них предупреждают
					// другие места: use — ветка по умолчанию, clip-path
					// и mask — WarnAboutIgnoredAttributes, градиенты —
					// AddShape по признаку Style.PaintServer.
					break;

				case "title":
				case "desc":
				case "metadata":
				case "style":
					break;

				case "path":
				case "rect":
				case "circle":
				case "ellipse":
				case "line":
				case "polyline":
				case "polygon":
					{
						using var path = BuildPath(child, tag);

						if (path != null && !path.IsEmpty)
							AddShape(path, matrix, style, result, child);

						break;
					}

				default:
					Warn(result, tag);
					break;
			}
		}
	}

	/// <summary>
	/// Свойства, которых импорт не умеет, но которые меняют вид рисунка.
	///
	/// Молчать о них нельзя: обрезка и маска убирают часть изображения,
	/// и без них на лист придёт то, чего пользователь в исходнике
	/// не видел, — а пунктир придёт сплошной линией.
	/// </summary>
	private static void WarnAboutIgnoredAttributes(XElement element, SvgImportResult result)
	{
		if (element.Attribute("clip-path") != null || element.Attribute("mask") != null)
			Warn(result, "clip-path");

		if (element.Attribute("stroke-dasharray") != null)
			Warn(result, "stroke-dasharray");
	}

	private static bool IsHidden(XElement element)
	{
		if (string.Equals(
			element.Attribute("display")?.Value?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string? visibility = element.Attribute("visibility")?.Value?.Trim();

		if (string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(visibility, "collapse", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string? inline = element.Attribute("style")?.Value;

		if (inline == null)
			return false;

		// Пробелы выбрасываются до поиска: «display : none» и «display:none»
		// значат одно и то же, а перечислять все написания по отдельности —
		// значит пропустить то, которого не предусмотрел.
		string packed = string.Concat(inline.Where(c => !char.IsWhiteSpace(c)));

		return packed.Contains("display:none", StringComparison.OrdinalIgnoreCase)
			|| packed.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase)
			|| packed.Contains("visibility:collapse", StringComparison.OrdinalIgnoreCase);
	}

	private static void Warn(SvgImportResult result, string tag)
	{
		string message = tag switch
		{
			"text" or "tspan" => L.T("Text from the SVG file was not imported."),
			"image" => L.T("Raster images from the SVG file were not imported."),
			"use" => L.T("References to definitions (use) were not imported."),
			"lineargradient" or "radialgradient" => L.T("Gradients from the SVG file were not imported."),
			"filter" => L.T("Filters from the SVG file were not imported."),
			"clip-path" => L.T("Clipping and masks were not imported: the whole drawing came in."),
			"stroke-dasharray" => L.T("Dashed strokes came in as solid lines."),
			_ => L.T("Unknown SVG element: {0}.", tag)
		};

		if (!result.Warnings.Contains(message))
			result.Warnings.Add(message);
	}

	#endregion

	#region Фигуры

	private static SKPath? BuildPath(XElement element, string tag)
	{
		switch (tag)
		{
			case "path":
				{
					string? d = element.Attribute("d")?.Value;

					return string.IsNullOrWhiteSpace(d) ? null : SKPath.ParseSvgPathData(d);
				}

			case "rect":
				{
					float w = Number(element, "width");
					float h = Number(element, "height");

					if (w <= 0f || h <= 0f)
						return null;

					float x = Number(element, "x");
					float y = Number(element, "y");

					float rx = Number(element, "rx");
					float ry = Number(element, "ry");

					// Один из радиусов задан, другой нет — по правилам SVG
					// второй равен первому.
					if (rx > 0f && ry <= 0f)
						ry = rx;
					else if (ry > 0f && rx <= 0f)
						rx = ry;

					var builder = new SKPathBuilder();
					var rect = new SKRect(x, y, x + w, y + h);

					if (rx > 0f && ry > 0f)
						builder.AddRoundRect(rect, MathF.Min(rx, w * 0.5f), MathF.Min(ry, h * 0.5f));
					else
						builder.AddRect(rect);

					return builder.Detach();
				}

			case "circle":
				{
					float r = Number(element, "r");

					if (r <= 0f)
						return null;

					var builder = new SKPathBuilder();

					builder.AddCircle(Number(element, "cx"), Number(element, "cy"), r);

					return builder.Detach();
				}

			case "ellipse":
				{
					float rx = Number(element, "rx");
					float ry = Number(element, "ry");

					if (rx <= 0f || ry <= 0f)
						return null;

					float cx = Number(element, "cx");
					float cy = Number(element, "cy");

					var builder = new SKPathBuilder();

					builder.AddOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry));

					return builder.Detach();
				}

			case "line":
				{
					var builder = new SKPathBuilder();

					builder.MoveTo(Number(element, "x1"), Number(element, "y1"));
					builder.LineTo(Number(element, "x2"), Number(element, "y2"));

					return builder.Detach();
				}

			case "polyline":
			case "polygon":
				{
					var points = ParsePoints(element.Attribute("points")?.Value);

					if (points.Count < 2)
						return null;

					var builder = new SKPathBuilder();

					builder.MoveTo(points[0].X, points[0].Y);

					for (int i = 1; i < points.Count; i++)
						builder.LineTo(points[i].X, points[i].Y);

					if (tag == "polygon")
						builder.Close();

					return builder.Detach();
				}
		}

		return null;
	}

	private static void AddShape(
		SKPath path, SKMatrix matrix, Style style, SvgImportResult result, XElement element)
	{
		using var mapped = new SKPath();

		path.Transform(matrix, mapped);

		var shape = BuildCurve(mapped, style.EvenOdd);

		if (shape == null)
			return;

		// Имя — идентификатор из файла, если он там есть. Прежде каждому
		// контуру доставалось имя файла, и в дереве объектов группа из
		// сотни контуров выглядела сотней одинаковых строк.
		string? id = element.Attribute("id")?.Value;

		if (!string.IsNullOrWhiteSpace(id))
			shape.Name = id.Trim();

		// Заливка ссылкой на определение (градиент, узор) разобрана быть
		// не может, и предупредить об этом можно только здесь: сами
		// определения лежат в defs, куда обход не заходит.
		if (style.PaintServer)
			Warn(result, "lineargradient");

		ApplyStyle(shape, style, matrix);

		result.Shapes.Add(shape);
	}

	private static void ApplyStyle(CurveShape shape, Style style, SKMatrix matrix)
	{
		// Прозрачность элемента и прозрачность его заливки — разные
		// свойства SVG, и действуют они вместе.
		float fillOpacity = Math.Clamp(style.Opacity, 0f, 1f) * Math.Clamp(style.FillOpacity, 0f, 1f);
		float strokeOpacity = Math.Clamp(style.Opacity, 0f, 1f) * Math.Clamp(style.StrokeOpacity, 0f, 1f);

		if (style.Fill is { } fill)
		{
			var color = WithOpacity(fill, fillOpacity);

			shape.Fill.Enabled = color.Alpha > 0;
			shape.Fill.Kind = FillKind.Solid;
			shape.Fill.SolidColor = color;
		}
		else
		{
			shape.Fill.Enabled = false;
		}

		if (style.Stroke is { } stroke)
		{
			var color = WithOpacity(stroke, strokeOpacity);

			shape.Stroke.Enabled = color.Alpha > 0;
			shape.Stroke.Color = color;

			shape.Stroke.Thickness = MathF.Max(style.StrokeWidth * UniformScale(matrix), 0.01f);
		}
		else
		{
			shape.Stroke.Enabled = false;
		}
	}

	/// <summary>
	/// Во сколько раз матрица растягивает длины.
	///
	/// Берётся корень из определителя, а не ScaleX: у матрицы с поворотом
	/// ScaleX — это косинус угла, а вовсе не масштаб, и обводка повёрнутой
	/// группы истончалась бы тем сильнее, чем круче поворот. У неравномерного
	/// масштаба обводка в SVG тоже неравномерна, а у нас толщина одна, —
	/// корень из определителя даёт среднее геометрическое по осям.
	/// </summary>
	private static float UniformScale(SKMatrix matrix)
	{
		float determinant = MathF.Abs(matrix.ScaleX * matrix.ScaleY - matrix.SkewX * matrix.SkewY);

		if (determinant > 0.0000001f)
			return MathF.Sqrt(determinant);

		// Выродившаяся матрица: масштаба нет, но и ноль возвращать нельзя —
		// обводка исчезла бы совсем.
		return MathF.Max(
			(MathF.Abs(matrix.ScaleX) + MathF.Abs(matrix.ScaleY)) * 0.5f, 0.0001f);
	}

	private static SKColor WithOpacity(SKColor color, float opacity)
		=> opacity >= 1f ? color : color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0f, 255f));

	#endregion

	#region Путь в кривую

	/// <summary>
	/// Путь Skia в узлы кривой.
	///
	/// Квадратичные кривые и дуги приводятся к кубическим: у CurveNode
	/// по одному рычагу с каждой стороны, то есть ровно кубика Безье.
	/// Перевод точный — квадратичная кривая представима кубической.
	/// </summary>
	private static CurveShape? BuildCurve(SKPath path, bool evenOdd)
	{
		var shape = new CurveShape
		{
			FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding
		};

		using var iterator = path.CreateRawIterator();

		var points = new SKPoint[4];

		CurveNode? previous = null;
		int contourStart = -1;
		SKPoint start = default;

		while (true)
		{
			var verb = iterator.Next(points);

			switch (verb)
			{
				case SKPathVerb.Move:
					contourStart = shape.Nodes.Count;
					start = points[0];
					previous = Add(shape, points[0], newContour: true);
					break;

				case SKPathVerb.Line:
					previous = Add(shape, points[1], newContour: false);
					break;

				case SKPathVerb.Quad:
					previous = AddCubic(
						shape, previous,
						Lerp(points[0], points[1], 2f / 3f),
						Lerp(points[2], points[1], 2f / 3f),
						points[2]);

					break;

				case SKPathVerb.Conic:
					{
						// Коника — это дуга, заданная весом. Точного
						// представления кубикой у неё нет, поэтому Skia
						// просят разложить её на квадратичные, а те уже
						// переводятся точно. Так же поступает вывод
						// в метафайл — см. MetafileExporter.
						var quads = SKPath.ConvertConicToQuads(
							points[0], points[1], points[2], iterator.ConicWeight(), 2);

						for (int i = 0; i + 2 < quads.Length; i += 2)
						{
							previous = AddCubic(
								shape, previous,
								Lerp(quads[i], quads[i + 1], 2f / 3f),
								Lerp(quads[i + 2], quads[i + 1], 2f / 3f),
								quads[i + 2]);
						}

						break;
					}

				case SKPathVerb.Cubic:
					previous = AddCubic(shape, previous, points[1], points[2], points[3]);
					break;

				case SKPathVerb.Close:
					{
						if (contourStart >= 0 && shape.Nodes.Count > contourStart)
						{
							// Узел, совпавший с началом контура, лишний:
							// замыкание и так соединит последний с первым,
							// а два узла в одной точке дают излом рычагов.
							var last = shape.Nodes[^1];

							if (shape.Nodes.Count - contourStart > 2
								&& Distance(last.Position, start) < 0.0001f)
							{
								var first = shape.Nodes[contourStart];

								if (last.HasPrevControl)
								{
									first.ControlPrev = last.ControlPrev;
									first.Type = CurveNodeType.Sharp;
								}

								shape.Nodes.RemoveAt(shape.Nodes.Count - 1);
							}

							shape.Nodes[^1].CloseAfter = true;
						}

						previous = null;
						break;
					}

				case SKPathVerb.Done:
					return shape.Nodes.Count > 0 ? Recenter(shape) : null;
			}
		}
	}

	private static CurveNode Add(CurveShape shape, SKPoint position, bool newContour)
	{
		var node = new CurveNode
		{
			Position = position,
			Type = CurveNodeType.Straight,
			NewContour = newContour
		};

		shape.Nodes.Add(node);

		return node;
	}

	private static CurveNode AddCubic(
		CurveShape shape, CurveNode? from, SKPoint c1, SKPoint c2, SKPoint end)
	{
		if (from != null)
		{
			from.ControlNext = c1;
			from.Type = CurveNodeType.Sharp;
		}

		var node = Add(shape, end, newContour: false);

		node.ControlPrev = c2;
		node.Type = CurveNodeType.Sharp;

		return node;
	}

	private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
		=> new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	private static float Distance(SKPoint a, SKPoint b)
		=> MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

	/// <summary>
	/// Узлы сдвигаются к своему центру, а смещение уходит в матрицу
	/// фигуры. Так же поступает импорт PDF: фигура с центром в начале
	/// своих координат правильно вращается и отражается.
	/// </summary>
	private static CurveShape Recenter(CurveShape shape)
	{
		var bounds = shape.GetLocalBounds();

		float cx = bounds.MidX;
		float cy = bounds.MidY;

		if (MathF.Abs(cx) < 0.0001f && MathF.Abs(cy) < 0.0001f)
			return shape;

		foreach (var node in shape.Nodes)
		{
			node.Position = new SKPoint(node.Position.X - cx, node.Position.Y - cy);

			if (node.HasPrevControl)
				node.ControlPrev = new SKPoint(node.ControlPrev.X - cx, node.ControlPrev.Y - cy);

			if (node.HasNextControl)
				node.ControlNext = new SKPoint(node.ControlNext.X - cx, node.ControlNext.Y - cy);
		}

		shape.LocalTransform = SKMatrix.CreateTranslation(cx, cy);

		return shape;
	}

	#endregion

	#region Разбор значений

	private static Style ReadStyle(XElement element, Style inherited, Sheet sheet)
	{
		var style = inherited.Clone();

		// Порядок — по старшинству, от слабого к сильному: отдельные
		// атрибуты, затем правила таблицы стилей, затем атрибут style.
		// Так определено в CSS, и именно на этом порядке держится
		// экспорт из Illustrator: цвета там лежат в таблице, а не
		// в атрибутах, и правило обязано перебить атрибут.
		Set(style, "fill", element.Attribute("fill")?.Value);
		Set(style, "stroke", element.Attribute("stroke")?.Value);
		Set(style, "stroke-width", element.Attribute("stroke-width")?.Value);
		Set(style, "fill-rule", element.Attribute("fill-rule")?.Value);
		Set(style, "opacity", element.Attribute("opacity")?.Value);
		Set(style, "fill-opacity", element.Attribute("fill-opacity")?.Value);
		Set(style, "stroke-opacity", element.Attribute("stroke-opacity")?.Value);

		sheet.ApplyTo(style, element);

		ApplyDeclarations(style, element.Attribute("style")?.Value);

		return style;
	}

	/// <summary>Список объявлений вида «свойство: значение», разделённый точками с запятой.</summary>
	private static void ApplyDeclarations(Style style, string? declarations)
	{
		if (string.IsNullOrWhiteSpace(declarations))
			return;

		foreach (string part in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			int colon = part.IndexOf(':');

			if (colon <= 0)
				continue;

			Set(style, part[..colon].Trim().ToLowerInvariant(), part[(colon + 1)..].Trim());
		}
	}

	/// <summary>
	/// Одно свойство стиля. Имена у атрибута и у записи внутри style
	/// одни и те же, поэтому разбор у них общий.
	/// </summary>
	private static void Set(Style style, string key, string? value)
	{
		if (value == null)
			return;

		switch (key)
		{
			case "fill":
				style.PaintServer |= IsPaintServer(value);
				style.Fill = ParseColor(value, style.Fill);
				break;

			case "stroke":
				style.PaintServer |= IsPaintServer(value);
				style.Stroke = ParseColor(value, style.Stroke);
				break;

			case "stroke-width":
				style.StrokeWidth = ParseUserUnits(value, style.StrokeWidth);
				break;

			case "fill-rule":
				style.EvenOdd = string.Equals(value, "evenodd", StringComparison.OrdinalIgnoreCase);
				break;

			case "opacity":
				style.Opacity = ParseRatio(value, style.Opacity);
				break;

			case "fill-opacity":
				style.FillOpacity = ParseRatio(value, style.FillOpacity);
				break;

			case "stroke-opacity":
				style.StrokeOpacity = ParseRatio(value, style.StrokeOpacity);
				break;
		}
	}

	private static bool IsPaintServer(string? value)
		=> value != null && value.TrimStart().StartsWith("url(", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Цвет SVG. null означает «не рисовать»: именно так читается none,
	/// и именно этим «нет заливки» отличается от «заливка чёрная».
	/// </summary>
	private static SKColor? ParseColor(string? value, SKColor? fallback)
	{
		value = value?.Trim();

		if (string.IsNullOrEmpty(value))
			return fallback;

		if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (string.Equals(value, "currentColor", StringComparison.OrdinalIgnoreCase))
			return fallback;

		// Градиенты и ссылки на определения: цвета у нас для них нет,
		// и серая заливка честнее невидимой фигуры.
		if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
			return new SKColor(0xB0, 0xB0, 0xB0);

		if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
		{
			int open = value.IndexOf('(');
			int close = value.IndexOf(')');

			if (open > 0 && close > open)
			{
				var parts = value[(open + 1)..close]
					.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);

				if (parts.Length >= 3)
				{
					byte r = ParseChannel(parts[0]);
					byte g = ParseChannel(parts[1]);
					byte b = ParseChannel(parts[2]);

					byte a = parts.Length >= 4
						? (byte)Math.Clamp(ParseFloat(parts[3], 1f) * 255f, 0f, 255f)
						: (byte)255;

					return new SKColor(r, g, b, a);
				}
			}
		}

		if (SKColor.TryParse(value, out var parsed))
			return parsed;

		return ParseNamedColor(value) ?? fallback;
	}

	/// <summary>
	/// Названный цвет CSS: red, white, cornflowerblue и остальные сто с чем-то.
	///
	/// Берётся таблица System.Drawing: имена в CSS и в HTML одни и те же,
	/// и держать свою копию полутора сотен названий незачем. SKColor.TryParse
	/// их не знает — он понимает только шестнадцатеричную запись, и без этой
	/// ветки заливка «red» молча превратилась бы в унаследованную.
	/// </summary>
	private static SKColor? ParseNamedColor(string value)
	{
		var known = System.Drawing.Color.FromName(value);

		// IsKnownColor истинно и для системных цветов оформления Windows:
		// без этой проверки fill="window" или fill="menu" подставил бы
		// цвет темы рабочего стола вместо цвета из файла.
		if (!known.IsKnownColor || known.IsSystemColor)
			return null;

		return new SKColor(known.R, known.G, known.B, known.A);
	}

	private static byte ParseChannel(string text)
	{
		text = text.Trim();

		if (text.EndsWith('%'))
			return (byte)Math.Clamp(ParseFloat(text[..^1], 0f) * 2.55f, 0f, 255f);

		return (byte)Math.Clamp(ParseFloat(text, 0f), 0f, 255f);
	}

	/// <summary>Длина SVG в миллиметрах. Без единиц — пиксели.</summary>
	private static float ParseLength(string? value, float fallback)
	{
		value = value?.Trim();

		if (string.IsNullOrEmpty(value))
			return fallback;

		// Единица по умолчанию — пиксель, а не миллиметр: в SVG число
		// без единиц означает пользовательскую единицу, а она равна
		// пикселю. Миллиметром по умолчанию считать нельзя — значок
		// размером «24» лёг бы на лист квадратом в два с половиной
		// сантиметра вместо шести миллиметров.
		float factor = MmPerInch / PixelsPerInch;

		foreach (var (suffix, perUnit) in new (string, float)[]
		{
			("mm", 1f),
			("cm", 10f),
			("in", MmPerInch),
			("pt", MmPerInch / 72f),
			("pc", MmPerInch / 6f),
			("px", MmPerInch / PixelsPerInch),
			("%", 0f)
		})
		{
			if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				continue;

			// Проценты считать не от чего: размер родителя сюда
			// не передаётся, и честнее отказаться, чем выдумать.
			if (perUnit == 0f)
				return fallback;

			factor = perUnit;
			value = value[..^suffix.Length];

			break;
		}

		return ParseFloat(value, fallback) * factor;
	}

	/// <summary>
	/// Значение в собственных координатах файла.
	///
	/// Толщина обводки — не физическая длина: она задана в той же системе,
	/// что и координаты пути, и растягивается вместе с ними через viewBox.
	/// Поэтому в миллиметры число не переводится — перевод сделает матрица,
	/// как и для координат.
	///
	/// Физическая единица при этом не отбрасывается, а приводится
	/// к пользовательской через пиксель: так её определяет и CSS, и браузер.
	/// Иначе «stroke-width: 0.5mm» читалось бы как полединицы файла, и
	/// на значке с viewBox в двадцать четыре единицы линия выходила бы
	/// в несколько раз тоньше, а на карте с viewBox в тысячи — на порядки.
	/// </summary>
	private static float ParseUserUnits(string? value, float fallback)
	{
		value = value?.Trim();

		if (string.IsNullOrEmpty(value))
			return fallback;

		// px и пользовательская единица — одно и то же, множитель у них 1.
		// em, ex и проценты считать не от чего: ни кегля, ни размера
		// родителя здесь нет, и отказ честнее выдуманного числа.
		foreach (var (suffix, perUnit) in new (string, float)[]
		{
			("mm", PixelsPerInch / MmPerInch),
			("cm", PixelsPerInch * 10f / MmPerInch),
			("in", PixelsPerInch),
			("pt", PixelsPerInch / 72f),
			("pc", PixelsPerInch / 6f),
			("px", 1f),
			("em", 0f),
			("ex", 0f),
			("%", 0f)
		})
		{
			if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				continue;

			if (perUnit == 0f)
				return fallback;

			return ParseFloat(value[..^suffix.Length], fallback) * perUnit;
		}

		return ParseFloat(value, fallback);
	}

	/// <summary>Доля от нуля до единицы. Проценты приводятся к доле.</summary>
	private static float ParseRatio(string? value, float fallback)
	{
		value = value?.Trim();

		if (string.IsNullOrEmpty(value))
			return fallback;

		if (value.EndsWith('%'))
			return ParseFloat(value[..^1], fallback * 100f) / 100f;

		return ParseFloat(value, fallback);
	}

	private static float Number(XElement element, string name)
		=> ParseFloat(element.Attribute(name)?.Value, 0f);

	private static float ParseFloat(string? value, float fallback)
		=> float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
			? result
			: fallback;

	private static SKRect? ParseViewBox(string? value)
	{
		var numbers = ParseNumbers(value);

		if (numbers.Count < 4)
			return null;

		return new SKRect(
			numbers[0], numbers[1], numbers[0] + numbers[2], numbers[1] + numbers[3]);
	}

	private static List<SKPoint> ParsePoints(string? value)
	{
		var numbers = ParseNumbers(value);
		var points = new List<SKPoint>(numbers.Count / 2);

		for (int i = 0; i + 1 < numbers.Count; i += 2)
			points.Add(new SKPoint(numbers[i], numbers[i + 1]));

		return points;
	}

	private static List<float> ParseNumbers(string? value)
	{
		var numbers = new List<float>();

		if (string.IsNullOrWhiteSpace(value))
			return numbers;

		foreach (string part in value.Split(
			new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
				numbers.Add(number);
		}

		return numbers;
	}

	/// <summary>
	/// Преобразование из атрибута transform. Список применяется слева
	/// направо, как и положено в SVG.
	/// </summary>
	private static SKMatrix? ParseTransform(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		SKMatrix? result = null;

		int index = 0;

		while (index < value.Length)
		{
			int open = value.IndexOf('(', index);

			if (open < 0)
				break;

			int close = value.IndexOf(')', open);

			if (close < 0)
				break;

			string name = value[index..open].Trim(' ', ',', '\t', '\r', '\n').ToLowerInvariant();
			var numbers = ParseNumbers(value[(open + 1)..close]);

			SKMatrix? step = name switch
			{
				"translate" when numbers.Count >= 1 => SKMatrix.CreateTranslation(
					numbers[0], numbers.Count > 1 ? numbers[1] : 0f),

				"scale" when numbers.Count >= 1 => SKMatrix.CreateScale(
					numbers[0], numbers.Count > 1 ? numbers[1] : numbers[0]),

				"rotate" when numbers.Count >= 3 => SKMatrix.CreateRotationDegrees(
					numbers[0], numbers[1], numbers[2]),

				"rotate" when numbers.Count >= 1 => SKMatrix.CreateRotationDegrees(numbers[0]),

				"matrix" when numbers.Count >= 6 => new SKMatrix
				{
					ScaleX = numbers[0],
					SkewY = numbers[1],
					SkewX = numbers[2],
					ScaleY = numbers[3],
					TransX = numbers[4],
					TransY = numbers[5],
					Persp2 = 1f
				},

				// Перекос собирается вручную, а не через SKMatrix: у Skia
				// свой порядок полей, и наглядная запись надёжнее угадывания,
				// какая из осей считается первой.
				"skewx" when numbers.Count >= 1 => Skew(
					MathF.Tan(numbers[0] * MathF.PI / 180f), 0f),

				"skewy" when numbers.Count >= 1 => Skew(
					0f, MathF.Tan(numbers[0] * MathF.PI / 180f)),

				_ => null
			};

			if (step != null)
				result = result == null ? step : Concat(result.Value, step.Value);

			index = close + 1;
		}

		return result;
	}

	/// <summary>
	/// Перекос: x' = x + skewX·y, y' = y + skewY·x. Именно это и означают
	/// skewX и skewY в SVG.
	/// </summary>
	private static SKMatrix Skew(float skewX, float skewY) => new()
	{
		ScaleX = 1f,
		ScaleY = 1f,
		SkewX = skewX,
		SkewY = skewY,
		Persp2 = 1f
	};

	/// <summary>
	/// Сначала применяется inner, затем outer.
	///
	/// Через Geometry.Multiply, а не PreConcat: тем же способом матрицы
	/// перемножаются во всём остальном проекте, и порядок множителей
	/// здесь читается так же, как там.
	/// </summary>
	private static SKMatrix Concat(SKMatrix outer, SKMatrix inner)
		=> Geometry.Multiply(outer, inner);

	#endregion
}
