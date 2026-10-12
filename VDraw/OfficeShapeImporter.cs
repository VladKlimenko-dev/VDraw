using SkiaSharp;
using System.IO.Compression;
using System.Xml.Linq;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

/// <summary>
/// Разбор фигур Office из формата Art::GVML ClipFormat.
///
/// Формат представляет собой ZIP-пакет (начинается с «PK») с разметкой
/// DrawingML внутри. Word и PowerPoint кладут его при копировании любой
/// фигуры вместе с растровыми представлениями — но только он содержит
/// настоящую геометрию.
///
/// Разбор идёт по локальным именам элементов, без привязки к пространствам
/// имён и к путям внутри пакета: Word, PowerPoint и Excel раскладывают файлы
/// по-разному и используют разные префиксы, а имена элементов у них общие.
/// </summary>
public static class OfficeShapeImporter
{
	/// <summary>Английских метрических единиц в одном миллиметре.</summary>
	private const float EmuPerMm = 36000f;

	/// <summary>Единиц угла в одном градусе: DrawingML хранит угол в 1/60000 градуса.</summary>
	private const float AngleUnitsPerDegree = 60000f;

	/// <summary>
	/// Читает фигуры из пакета. Возвращает пустой список, если пакет не
	/// распознан — вызывающий тогда уходит на растровый запасной путь.
	/// </summary>
	public static List<Shape> Import(Stream package, Page page, SKPoint origin)
	{
		var result = new List<Shape>();

		List<XElement> shapeElements;

		try
		{
			shapeElements = ReadShapeElements(package);
		}
		catch
		{
			return result;
		}

		if (shapeElements.Count == 0)
			return result;

		float mmPerUom = MathF.Max(page.MmPerUom, 0.0001f);

		// Габариты всего набора нужны, чтобы вставить его по центру точки
		// вставки, а не левым верхним углом.
		var bounds = GetTotalBounds(shapeElements, mmPerUom);

		var shift = new SKPoint(
			origin.X - bounds.MidX,
			origin.Y - bounds.MidY);

		foreach (var element in shapeElements)
		{
			var shape = BuildShape(element, mmPerUom, shift);

			if (shape != null)
				result.Add(shape);
		}

		return result;
	}

	#region Чтение пакета

	private static List<XElement> ReadShapeElements(Stream package)
	{
		var result = new List<XElement>();

		package.Position = 0;

		using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

		foreach (var entry in archive.Entries)
		{
			if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
				continue;

			XDocument doc;

			try
			{
				using var stream = entry.Open();
				doc = XDocument.Load(stream);
			}
			catch
			{
				// Служебные файлы пакета к разметке отношения не имеют.
				continue;
			}

			// Фигура опознаётся по наличию свойств формы, а не по имени
			// элемента: у Word это wps:wsp, у PowerPoint p:sp, у Excel xdr:sp.
			foreach (var element in doc.Descendants())
			{
				if (element.Elements().Any(e => e.Name.LocalName == "spPr"))
					result.Add(element);
			}
		}

		return result;
	}

	#endregion

	#region Построение фигуры

	private static Shape? BuildShape(XElement element, float mmPerUom, SKPoint shift)
	{
		var spPr = element.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr");

		if (spPr == null)
			return null;

		var xfrm = Descendant(spPr, "xfrm");

		if (xfrm == null)
			return null;

		var off = Descendant(xfrm, "off");
		var ext = Descendant(xfrm, "ext");

		if (off == null || ext == null)
			return null;

		float x = EmuToUom(off.Attribute("x"), mmPerUom) + shift.X;
		float y = EmuToUom(off.Attribute("y"), mmPerUom) + shift.Y;
		float w = EmuToUom(ext.Attribute("cx"), mmPerUom);
		float h = EmuToUom(ext.Attribute("cy"), mmPerUom);

		if (w <= 0f || h <= 0f)
			return null;

		var localRect = new SKRect(-w * 0.5f, -h * 0.5f, w * 0.5f, h * 0.5f);

		Shape? shape = BuildGeometry(spPr, localRect);

		if (shape == null)
			return null;

		// Смещение задаёт ЛЕВЫЙ ВЕРХНИЙ угол, а фигура строится вокруг центра.
		float centerX = x + w * 0.5f;
		float centerY = y + h * 0.5f;

		var transform = SKMatrix.CreateTranslation(centerX, centerY);

		// Поворот вокруг центра фигуры, как и задумано в DrawingML.
		float rotation = ParseFloat(xfrm.Attribute("rot")) / AngleUnitsPerDegree;

		if (MathF.Abs(rotation) > 0.001f)
			transform = Geometry.Multiply(transform, SKMatrix.CreateRotationDegrees(rotation));

		// Отражения задаются флагами, а не отрицательным масштабом.
		float flipX = ParseBool(xfrm.Attribute("flipH")) ? -1f : 1f;
		float flipY = ParseBool(xfrm.Attribute("flipV")) ? -1f : 1f;

		if (flipX < 0f || flipY < 0f)
			transform = Geometry.Multiply(transform, SKMatrix.CreateScale(flipX, flipY));

		shape.LocalTransform = transform;
		shape.Name = GetShapeName(element) ?? shape.Name;

		ApplyFill(shape, spPr);
		ApplyStroke(shape, spPr, mmPerUom);

		return shape;
	}

	private static Shape? BuildGeometry(XElement spPr, SKRect rect)
	{
		var custGeom = Descendant(spPr, "custGeom");

		if (custGeom != null)
			return BuildCustomGeometry(custGeom, rect);

		var prstGeom = Descendant(spPr, "prstGeom");
		string preset = prstGeom?.Attribute("prst")?.Value ?? "rect";

		return BuildPresetGeometry(preset, rect);
	}

	/// <summary>
	/// Готовые формы DrawingML. Покрыт распространённый набор; всё остальное
	/// сводится к прямоугольнику, потому что вставить приблизительную фигуру
	/// лучше, чем не вставить ничего — растр всё равно остаётся в буфере
	/// на случай, если пользователь предпочтёт его.
	/// </summary>
	private static Shape BuildPresetGeometry(string preset, SKRect rect)
	{
		switch (preset)
		{
			case "ellipse":
				return BuildEllipse(rect);

			case "roundRect":
				{
					var shape = new RoundedRectangleShape { LocalRect = rect };

					// Скругление по умолчанию в DrawingML — 16.7% меньшей стороны.
					float r = MathF.Min(rect.Width, rect.Height) * 0.167f;

					shape.RadiusX = r;
					shape.RadiusY = r;

					return shape;
				}

			case "triangle":
				return BuildPolygonFromPoints(new[]
				{
					new SKPoint(rect.MidX, rect.Top),
					new SKPoint(rect.Right, rect.Bottom),
					new SKPoint(rect.Left, rect.Bottom)
				});

			case "diamond":
				return BuildPolygonFromPoints(new[]
				{
					new SKPoint(rect.MidX, rect.Top),
					new SKPoint(rect.Right, rect.MidY),
					new SKPoint(rect.MidX, rect.Bottom),
					new SKPoint(rect.Left, rect.MidY)
				});

			case "rtTriangle":
				return BuildPolygonFromPoints(new[]
				{
					new SKPoint(rect.Left, rect.Top),
					new SKPoint(rect.Right, rect.Bottom),
					new SKPoint(rect.Left, rect.Bottom)
				});

			case "parallelogram":
				{
					float inset = rect.Width * 0.25f;

					return BuildPolygonFromPoints(new[]
					{
						new SKPoint(rect.Left + inset, rect.Top),
						new SKPoint(rect.Right, rect.Top),
						new SKPoint(rect.Right - inset, rect.Bottom),
						new SKPoint(rect.Left, rect.Bottom)
					});
				}

			case "pentagon":
				return BuildRegularPolygon(rect, 5);

			case "hexagon":
				return BuildRegularPolygon(rect, 6);

			case "heptagon":
				return BuildRegularPolygon(rect, 7);

			case "octagon":
				return BuildRegularPolygon(rect, 8);

			case "line":
			case "straightConnector1":
				{
					var curve = new CurveShape { Name = "Line" };

					curve.Nodes.Add(new CurveNode
					{
						Position = new SKPoint(rect.Left, rect.Top),
						Type = CurveNodeType.Straight,
						NewContour = true
					});

					curve.Nodes.Add(new CurveNode
					{
						Position = new SKPoint(rect.Right, rect.Bottom),
						Type = CurveNodeType.Straight,
						BreakAfter = true
					});

					curve.Fill.Enabled = false;

					return curve;
				}

			default:
				return new RectangleShape { LocalRect = rect };
		}
	}

	/// <summary>
	/// Эллипс четырьмя кубическими сегментами. Отношение 0.5523 — известное
	/// приближение окружности кривыми Безье с погрешностью около 0.02%.
	/// </summary>
	private static CurveShape BuildEllipse(SKRect rect)
	{
		const float K = 0.5522847f;

		float rx = rect.Width * 0.5f;
		float ry = rect.Height * 0.5f;

		float cx = rect.MidX;
		float cy = rect.MidY;

		var points = new[]
		{
			new SKPoint(cx, cy - ry),
			new SKPoint(cx + rx, cy),
			new SKPoint(cx, cy + ry),
			new SKPoint(cx - rx, cy)
		};

		var handles = new[]
		{
			new SKPoint(rx * K, 0f),
			new SKPoint(0f, ry * K),
			new SKPoint(-rx * K, 0f),
			new SKPoint(0f, -ry * K)
		};

		var curve = new CurveShape { Name = "Ellipse", FillType = SKPathFillType.Winding };

		for (int i = 0; i < 4; i++)
		{
			curve.Nodes.Add(new CurveNode
			{
				Position = points[i],
				Type = CurveNodeType.Tangent,
				ControlPrev = new SKPoint(points[i].X - handles[i].X, points[i].Y - handles[i].Y),
				ControlNext = new SKPoint(points[i].X + handles[i].X, points[i].Y + handles[i].Y),
				NewContour = i == 0,
				CloseAfter = i == 3
			});
		}

		return curve;
	}

	private static CurveShape BuildRegularPolygon(SKRect rect, int sides)
	{
		var points = new SKPoint[sides];

		float rx = rect.Width * 0.5f;
		float ry = rect.Height * 0.5f;

		for (int i = 0; i < sides; i++)
		{
			// Первая вершина сверху, как в Office.
			float angle = -MathF.PI / 2f + i * 2f * MathF.PI / sides;

			points[i] = new SKPoint(
				rect.MidX + MathF.Cos(angle) * rx,
				rect.MidY + MathF.Sin(angle) * ry);
		}

		return BuildPolygonFromPoints(points);
	}

	private static CurveShape BuildPolygonFromPoints(IReadOnlyList<SKPoint> points)
	{
		var curve = new CurveShape { Name = "Polygon", FillType = SKPathFillType.Winding };

		for (int i = 0; i < points.Count; i++)
		{
			curve.Nodes.Add(new CurveNode
			{
				Position = points[i],
				Type = CurveNodeType.Straight,
				NewContour = i == 0,
				CloseAfter = i == points.Count - 1
			});
		}

		return curve;
	}

	/// <summary>
	/// Произвольная геометрия: список путей с командами перемещения, отрезков
	/// и кубических кривых. Координаты заданы в собственной сетке пути,
	/// её размер объявлен атрибутами w и h.
	/// </summary>
	private static CurveShape? BuildCustomGeometry(XElement custGeom, SKRect rect)
	{
		var curve = new CurveShape { Name = "Freeform", FillType = SKPathFillType.Winding };

		var pathList = Descendant(custGeom, "pathLst");

		if (pathList == null)
			return null;

		foreach (var path in pathList.Elements().Where(e => e.Name.LocalName == "path"))
		{
			float pw = ParseFloat(path.Attribute("w"));
			float ph = ParseFloat(path.Attribute("h"));

			if (pw <= 0f) pw = 1f;
			if (ph <= 0f) ph = 1f;

			// Сетка пути растягивается на габариты фигуры.
			float sx = rect.Width / pw;
			float sy = rect.Height / ph;

			SKPoint Map(XElement pt) => new SKPoint(
				rect.Left + ParseFloat(pt.Attribute("x")) * sx,
				rect.Top + ParseFloat(pt.Attribute("y")) * sy);

			int contourStart = curve.Nodes.Count;

			foreach (var command in path.Elements())
			{
				switch (command.Name.LocalName)
				{
					case "moveTo":
						{
							var pt = Descendant(command, "pt");

							if (pt == null)
								break;

							contourStart = curve.Nodes.Count;

							curve.Nodes.Add(new CurveNode
							{
								Position = Map(pt),
								Type = CurveNodeType.Straight,
								NewContour = true
							});

							break;
						}

					case "lnTo":
						{
							var pt = Descendant(command, "pt");

							if (pt == null || curve.Nodes.Count == 0)
								break;

							curve.Nodes.Add(new CurveNode
							{
								Position = Map(pt),
								Type = CurveNodeType.Straight
							});

							break;
						}

					case "cubicBezTo":
						{
							var points = command.Elements()
								.Where(e => e.Name.LocalName == "pt")
								.ToList();

							if (points.Count < 3 || curve.Nodes.Count == 0)
								break;

							var previous = curve.Nodes[^1];

							previous.ControlNext = Map(points[0]);

							if (previous.Type == CurveNodeType.Straight)
								previous.Type = CurveNodeType.Sharp;

							curve.Nodes.Add(new CurveNode
							{
								Position = Map(points[2]),
								ControlPrev = Map(points[1]),
								Type = CurveNodeType.Sharp
							});

							break;
						}

					case "close":
						if (curve.Nodes.Count > contourStart)
							curve.Nodes[^1].CloseAfter = true;

						break;
				}
			}
		}

		return curve.Nodes.Count >= 2 ? curve : null;
	}

	#endregion

	#region Стиль

	private static void ApplyFill(Shape shape, XElement spPr)
	{
		if (Descendant(spPr, "noFill") != null)
		{
			shape.Fill.Enabled = false;
			return;
		}

		var solid = Descendant(spPr, "solidFill");

		if (solid == null)
		{
			// Градиенты, картинки и узоры не разбираются: их описание
			// в DrawingML сложнее самих фигур, а вставить залитую сплошным
			// цветом фигуру полезнее, чем не вставить ничего.
			return;
		}

		var color = ReadColor(solid);

		if (color == null)
			return;

		shape.Fill.Enabled = true;
		shape.Fill.Kind = FillKind.Solid;
		shape.Fill.SolidColor = color.Value;
	}

	private static void ApplyStroke(Shape shape, XElement spPr, float mmPerUom)
	{
		var ln = spPr.Elements().FirstOrDefault(e => e.Name.LocalName == "ln");

		if (ln == null)
			return;

		if (ln.Elements().Any(e => e.Name.LocalName == "noFill"))
		{
			shape.Stroke.Enabled = false;
			return;
		}

		shape.Stroke.Enabled = true;

		float widthEmu = ParseFloat(ln.Attribute("w"));

		// EMU -> миллиметры -> единицы страницы: толщина хранится
		// в тех же единицах, что и координаты.
		if (widthEmu > 0f)
			shape.Stroke.Thickness = widthEmu / EmuPerMm / MathF.Max(mmPerUom, 0.0001f);

		var solid = ln.Elements().FirstOrDefault(e => e.Name.LocalName == "solidFill");

		if (solid == null)
			return;

		var color = ReadColor(solid);

		if (color != null)
			shape.Stroke.Color = color.Value;
	}

	/// <summary>
	/// Цвет из элемента заливки.
	///
	/// Разбирается только srgbClr — точное значение. Цвета темы (schemeClr)
	/// пропускаются: их разрешение требует файла темы документа, которого
	/// в буфере обмена нет, и подстановка наугад дала бы фигуру не того цвета
	/// без всякого предупреждения.
	/// </summary>
	private static SKColor? ReadColor(XElement fill)
	{
		var srgb = Descendant(fill, "srgbClr");

		if (srgb == null)
			return null;

		string? hex = srgb.Attribute("val")?.Value;

		if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6)
			return null;

		if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
			return null;

		byte alpha = 255;

		var alphaElement = Descendant(srgb, "alpha");

		if (alphaElement != null)
		{
			// Прозрачность задана в тысячных долях процента.
			float value = ParseFloat(alphaElement.Attribute("val")) / 100000f;
			alpha = (byte)Math.Clamp(value * 255f, 0f, 255f);
		}

		return new SKColor(
			(byte)((rgb >> 16) & 0xFF),
			(byte)((rgb >> 8) & 0xFF),
			(byte)(rgb & 0xFF),
			alpha);
	}

	#endregion

	#region Вспомогательное

	private static XElement? Descendant(XElement parent, string localName)
		=> parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

	private static string? GetShapeName(XElement element)
	{
		var nv = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "cNvPr");
		string? name = nv?.Attribute("name")?.Value;

		return string.IsNullOrWhiteSpace(name) ? null : name;
	}

	private static float EmuToUom(XAttribute? attribute, float mmPerUom)
		=> ParseFloat(attribute) / EmuPerMm / mmPerUom;

	private static float ParseFloat(XAttribute? attribute)
	{
		if (attribute == null)
			return 0f;

		return float.TryParse(
			attribute.Value,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out float value)
			? value
			: 0f;
	}

	private static bool ParseBool(XAttribute? attribute)
	{
		string? value = attribute?.Value;

		return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
	}

	private static SKRect GetTotalBounds(List<XElement> elements, float mmPerUom)
	{
		var bounds = SKRect.Empty;

		foreach (var element in elements)
		{
			var spPr = element.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr");
			var xfrm = spPr == null ? null : Descendant(spPr, "xfrm");

			var off = xfrm == null ? null : Descendant(xfrm, "off");
			var ext = xfrm == null ? null : Descendant(xfrm, "ext");

			if (off == null || ext == null)
				continue;

			float x = EmuToUom(off.Attribute("x"), mmPerUom);
			float y = EmuToUom(off.Attribute("y"), mmPerUom);
			float w = EmuToUom(ext.Attribute("cx"), mmPerUom);
			float h = EmuToUom(ext.Attribute("cy"), mmPerUom);

			var rect = new SKRect(x, y, x + w, y + h);

			bounds = bounds.IsEmpty ? rect : Geometry.Union(bounds, rect);
		}

		return bounds;
	}

	#endregion
}