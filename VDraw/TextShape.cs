using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

public sealed partial class TextShape : Shape
{
	public string Text { get; set; } = "Text";
	public SKPoint LocalOrigin { get; set; }
	public float FontSizeMm { get; set; } = 5f;
	public string FontFamily { get; set; } = "Arial";
	public bool Bold { get; set; }
	public bool Italic { get; set; }
	public float CharacterSpacingMm { get; set; } = 0f;
	public float LineSpacingMm { get; set; } = 0f;

	public float ax { get; set; } = 0f; // позиция вдоль контуров shapeAlong, в UoM
	public float ay { get; set; } = 0f; // смещение baseline от линии контура, в UoM
	public bool tangentAlong { get; set; } = true;

	public TextShape()
	{
		Name = "Text";
		Fill.Enabled = true;
		Fill.Kind = FillKind.Solid;
		Fill.SolidColor = Settings.shape_default_fill_color;
		Stroke.Enabled = false;
		Stroke.Color = Settings.shape_default_line_color;
		Stroke.Thickness = Settings.MmToCurrentUom(0.2f);
	}

	private SKFont CreateLocalFont()
	{
		var typeface = SKTypeface.FromFamilyName(
			FontFamily,
			Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
			SKFontStyleWidth.Normal,
			Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

		float sizeMm = MathF.Max(0.0001f, FontSizeMm);
		return new SKFont(typeface, sizeMm);
	}

	public override SKRect GetBounds()
	{
		if (IsAreaText)
		{
			using var localPath = BuildInsideShapeLocalPath();

			if (localPath.IsEmpty)
				return SKRect.Empty;

			using var worldPath = new SKPath();
			localPath.Transform(GetWorldTransform(), worldPath);
			return worldPath.TightBounds;
		}
		if (AlongShape && otherShape != null)
		{
			using var worldPath = BuildAlongShapeWorldPath();
			return worldPath.TightBounds;
		}

		// Обычный текст: локальная коробка переносится матрицей.
		//
		// Прежде звалась базовая реализация, а она берёт SKPath.Bounds
		// мирового пути — коробку по КОНТРОЛЬНЫМ ТОЧКАМ контуров глифов.
		// У букв с круглыми элементами контрольные точки выходят за саму
		// литеру, и рамка получалась шире надписи.
		//
		// Теперь коробка берётся из GetLocalBounds, то есть из метрик
		// шрифта, и переносится в мир по четырём углам. У прямоугольника
		// осевая оболочка повёрнутых углов точна, четырёх точек хватает.
		//
		// Это снимает и расхождение: прилипание брало точки ЛОКАЛЬНОЙ
		// коробки, а рамка выделения — коробку по контурам глифов, и
		// маркеры привязки висели не там, где рамка. Теперь обе считаются
		// из одного источника.
		//
		// Попутно уходит построение всего пути глифов на каждый вызов,
		// а GetBounds зовётся для каждой фигуры страницы на каждое
		// движение мыши.
		var local = GetLocalBounds();

		if (local.Width <= 0f && local.Height <= 0f)
			return SKRect.Empty;

		return RectFromPoints(new[]
		{
			LocalToWorld(new SKPoint(local.Left, local.Top)),
			LocalToWorld(new SKPoint(local.Right, local.Top)),
			LocalToWorld(new SKPoint(local.Right, local.Bottom)),
			LocalToWorld(new SKPoint(local.Left, local.Bottom))
		});
	}

	public override SKRect GetLocalBounds()
	{
		if (IsAreaText)
		{
			using var areaPath = BuildInsideShapeLocalPath();
			return areaPath.IsEmpty ? SKRect.Empty : areaPath.TightBounds;
		}

		if (AlongShape && otherShape != null)
		{
			using var localPath = BuildAlongShapeLocalPath();
			return localPath.IsEmpty ? SKRect.Empty : localPath.TightBounds;
		}

		using var font = CreateLocalFont();

		string[] lines = (Text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

		float maxWidth = 0f;
		float lineHeight = FontSizeMm * 1.2f;
		float extraLine = LineSpacingMm;

		foreach (var line in lines)
			maxWidth = MathF.Max(maxWidth, TextGlyphs.Measure(font, line, CharacterSpacingMm));

		float totalHeight = lines.Length * lineHeight + MathF.Max(0, lines.Length - 1) * extraLine;

		font.GetFontMetrics(out var metrics);
		float top = LocalOrigin.Y + metrics.Ascent;
		float bottom = LocalOrigin.Y + totalHeight - lineHeight + metrics.Descent;

		return new SKRect(LocalOrigin.X, top, LocalOrigin.X + maxWidth, bottom);
	}

	public override SKPath BuildLocalPath()
	{
		// Режим области имеет приоритет. Сеттеры не дают включить оба режима
		// одновременно, но данные могут прийти извне — например из файла,
		// записанного более старой версией.
		if (IsAreaText)
			return BuildInsideShapeLocalPath();

		if (AlongShape && otherShape != null)
			return BuildAlongShapeLocalPath();

		return BuildGlyphsLocalPath();
	}

	private SKPath BuildAlongShapeLocalPath() => BuildAlongShapePath(worldSpace: false);

	private SKPath BuildAlongShapeWorldPath() => BuildAlongShapePath(worldSpace: true);

	/// <summary>
	/// Раскладывает текст вдоль контура другой фигуры.
	///
	/// Одна реализация на оба пространства: прежде это были два почти одинаковых
	/// метода по семьдесят строк, и любая правка требовалась в двух местах —
	/// именно так и разошлись детали вроде домножения charSpacing на масштаб.
	///
	/// Строки укладываются параллельно контуру: каждая следующая смещена по
	/// нормали на высоту строки. Позиция вдоль контура (ax) у всех строк общая.
	/// </summary>
	private SKPath BuildAlongShapePath(bool worldSpace)
	{
		var result = new SKPath();
		result.FillType = SKPathFillType.Winding;

		if (string.IsNullOrEmpty(Text) || otherShape == null)
			return result;

		using var font = CreateLocalFont();
		using var otherWorldPath = otherShape.BuildWorldPath();

		if (otherWorldPath == null || otherWorldPath.IsEmpty)
			return result;

		float scaleX = 1f;
		float scaleY = 1f;

		var samplePath = new SKPath(otherWorldPath);

		if (worldSpace)
		{
			// Глифы строятся в локальных миллиметрах, поэтому их нужно
			// домножить на масштаб фигуры.
			GetWorldScale(out scaleX, out scaleY);
		}
		else
		{
			// Локальное пространство: контур переводится в него, глифы не масштабируются.
			if (!TryGetInverseWorldTransform(out var inverse))
				inverse = SKMatrix.CreateIdentity();

			samplePath.Transform(inverse);
		}

		using (samplePath)
		{
			var sampler = ShapePathSampler.FromPath(samplePath, 0.1f);

			if (sampler.TotalLength <= 1e-6f)
				return result;

			// \r\n и одиночный \r приводятся к \n ДО разбиения. Прежний код
			// удалял \r, и текст с разделителем \r склеивался в одну строку.
			string[] lines = (Text ?? string.Empty)
				.Replace("\r\n", "\n")
				.Replace("\r", "\n")
				.Split('\n');

			float lineStep = (FontSizeMm * 1.2f + LineSpacingMm) * scaleY;
			float charSpacing = CharacterSpacingMm * scaleX;

			for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				string line = lines[lineIndex];

				// Пустая строка ничего не рисует, но занимает своё место:
				// следующая за ней смещается на две высоты, а не на одну.
				if (line.Length == 0)
					continue;

				float normalOffset = ay + lineIndex * lineStep;
				float s = ax;

				foreach (char ch in line)
				{
					string chStr = ch.ToString();

					// Табуляция и пробел контура не имеют: только продвигаемся.
					// Прежде '\t' уходил в GetTextPath и по контуру ложился .notdef.
					if (ch == '\t')
					{
						s += TextGlyphs.GetTabWidth(font) * scaleX + charSpacing;
						continue;
					}

					// Продвижение шрифта, а не ширина чернил: чернила бывают шире
					// продвижения у наклонных букв, и такой символ получал зазор.
					float advance = font.MeasureText(chStr) * scaleX;

					using var glyphRaw = font.GetTextPath(chStr, SKPoint.Empty);

					if (glyphRaw == null || glyphRaw.IsEmpty)
					{
						// Пробел и прочие непечатаемые.
						s += MathF.Max(advance, 0f) + charSpacing;
						continue;
					}

					using var glyph = new SKPath(glyphRaw);

					if (worldSpace)
						glyph.Transform(SKMatrix.CreateScale(scaleX, scaleY));

					float sCenter = s + advance * 0.5f;

					if (!sampler.TryEvaluate(sCenter, out var basePoint, out float tangentAngle))
					{
						s += advance + charSpacing;
						continue;
					}

					float normalAngle = tangentAngle + MathF.PI / 2f;

					var origin = new SKPoint(
						basePoint.X + MathF.Cos(normalAngle) * normalOffset,
						basePoint.Y + MathF.Sin(normalAngle) * normalOffset);

					// Сдвиг на половину ПРОДВИЖЕНИЯ, а не по центру чернил:
					// иначе боковые полуприближения учитываются дважды и
					// интервалы между буквами выходят неравномерными.
					var centerShift = SKMatrix.CreateTranslation(-advance * 0.5f, 0f);
					var rotation = SKMatrix.CreateRotation(tangentAlong ? tangentAngle : 0f);
					var translation = SKMatrix.CreateTranslation(origin.X, origin.Y);

					// Порядок: сдвиг к центру, затем поворот, затем перенос на контур.
					var place = Geometry.Multiply(
						translation,
						Geometry.Multiply(rotation, centerShift));

					using var placed = new SKPath();
					glyph.Transform(place, placed);

					result.AddPath(placed);

					s += advance + charSpacing;
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Масштаб из МИРОВОЙ трансформации, а не из LocalTransform.
	///
	/// Прежний GetLocalScale читал только LocalTransform, поэтому у текста
	/// внутри группы масштаб группы игнорировался и глифы выходили не того
	/// размера, что остальная фигура.
	/// </summary>
	private void GetWorldScale(out float scaleX, out float scaleY)
	{
		var m = GetWorldTransform();

		scaleX = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
		scaleY = MathF.Sqrt(m.SkewX * m.SkewX + m.ScaleY * m.ScaleY);

		if (scaleX < 1e-6f) scaleX = 1f;
		if (scaleY < 1e-6f) scaleY = 1f;
	}

	private SKPath BuildGlyphsWorldPath()
	{
		using var localPath = BuildGlyphsLocalPath();
		if (localPath.IsEmpty)
			return new SKPath();

		var worldPath = new SKPath();
		localPath.Transform(GetWorldTransform(), worldPath);
		worldPath.FillType = localPath.FillType;
		return worldPath;
	}

	/// <summary>
	/// Попадание в текст. Считается в МИРОВЫХ координатах.
	///
	/// Прежде обычный текст проверялся в локальных: точка переводилась
	/// через WorldToLocal, а допуск — толщина обводки, которой «раздували»
	/// контуры глифов, — подставлялся как есть, в мировых единицах.
	/// У нерастянутого текста это совпадает, и ошибка не видна. Но масштаб
	/// от растягивания ручками живёт в LocalTransform, и локальная единица
	/// перестаёт быть равной мировой: при увеличении в N раз допуск
	/// оказывался в N раз больше нужного. Текст, растянутый вдесятеро,
	/// ловил щелчок на расстоянии десяти допусков — далеко за своими
	/// границами.
	///
	/// Переводить допуск обратным масштабом было бы недостаточно: масштаб
	/// бывает разным по осям, и одной толщиной обводки его не выразить.
	/// Поэтому путь строится сразу в мире и раздувается там — ровно так,
	/// как это всегда делали базовая реализация и ветка текста вдоль
	/// контура.
	///
	/// Стыки скруглённые, а не по умолчанию остриём: остриё на острых углах
	/// контура глифа выбрасывает шипы вплоть до четырёх полутолщин, и у
	/// засечек с тонкими штрихами они заметно выходят за букву. Скругление
	/// даёт честную полосу по расстоянию.
	/// </summary>
	public override bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		using var worldPath = AlongShape && otherShape != null
			? BuildAlongShapeWorldPath()
			: BuildWorldPath();

		if (worldPath == null || worldPath.IsEmpty)
			return false;

		if (worldPath.Contains(pagePoint.X, pagePoint.Y))
			return true;

		using var strokePaint = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			StrokeWidth = MathF.Max(toleranceUom, 0.25f / MathF.Max(mmPerUom, 0.0001f)),
			StrokeJoin = SKStrokeJoin.Round,
			StrokeCap = SKStrokeCap.Round,
			IsAntialias = true
		};

		using var fillPath = new SKPath();

		return strokePaint.GetFillPath(worldPath, fillPath)
			&& fillPath.Contains(pagePoint.X, pagePoint.Y);
	}

	public override void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		var world = GetWorldTransform();

		using var glyphsWorld = new SKPath { FillType = SKPathFillType.Winding };
		using var decorationsWorld = new SKPath { FillType = SKPathFillType.Winding };

		SKPath? clipWorld = null;

		try
		{
			if (IsAreaText)
			{
				BuildInsideShapeParts(out var glyphsLocal, out var decorationsLocal, out var clipLocal);

				glyphsLocal.Transform(world, glyphsWorld);
				decorationsLocal.Transform(world, decorationsWorld);

				if (clipLocal != null)
				{
					clipWorld = new SKPath();
					clipLocal.Transform(world, clipWorld);
					clipWorld.FillType = SKPathFillType.Winding;
				}
			}
			else if (AlongShape && otherShape != null)
			{
				// Оформление по контуру не строится: подчёркивание вдоль кривой
				// требует своей геометрии, а не прямоугольника.
				using var alongWorld = BuildAlongShapeWorldPath();
				glyphsWorld.AddPath(alongWorld);
			}
			else
			{
				using var glyphsLocal = new SKPath { FillType = SKPathFillType.Winding };
				using var decorationsLocal = new SKPath { FillType = SKPathFillType.Winding };

				BuildGlyphsParts(glyphsLocal, decorationsLocal);

				glyphsLocal.Transform(world, glyphsWorld);
				decorationsLocal.Transform(world, decorationsWorld);
			}

			if (glyphsWorld.IsEmpty && decorationsWorld.IsEmpty)
				return;

			int restoreTo = canvas.Save();

			try
			{
				if (clipWorld != null && !clipWorld.IsEmpty)
					canvas.ClipPath(clipWorld, SKClipOperation.Intersect, true);

				var bounds = glyphsWorld.IsEmpty ? decorationsWorld.Bounds : glyphsWorld.Bounds;

				if (Fill.Enabled)
				{
					using var fillPaint = Fill.CreatePaint(bounds, world);

					// Два вызова, а не один объединённый путь: слияние заливает
					// дырки букв там, где их пересекает линия оформления.
					if (!glyphsWorld.IsEmpty)
						canvas.DrawPath(glyphsWorld, fillPaint);

					if (!decorationsWorld.IsEmpty)
						canvas.DrawPath(decorationsWorld, fillPaint);
				}

				if (Stroke.Enabled)
				{
					using var strokePaint = Stroke.CreatePaint(mmPerWorldUnit, IsSelected);

					if (!glyphsWorld.IsEmpty)
						canvas.DrawPath(glyphsWorld, strokePaint);

					if (!decorationsWorld.IsEmpty)
						canvas.DrawPath(decorationsWorld, strokePaint);
				}
			}
			finally
			{
				canvas.RestoreToCount(restoreTo);
			}
		}
		finally
		{
			clipWorld?.Dispose();
		}
	}

	public override CurveShape? ToCurveShape()
	{
		if (string.IsNullOrEmpty(Text))
			return null;

		SKPath combinedPath;

		if (IsAreaText)
		{
			combinedPath = BuildInsideShapeLocalPath();
		}
		else if (AlongShape && otherShape != null)
		{
			using var worldPath = BuildAlongShapeWorldPath();
			if (worldPath.IsEmpty)
				return null;

			if (!TryGetInverseWorldTransform(out var invWorld))
				invWorld = SKMatrix.CreateIdentity();

			combinedPath = new SKPath(worldPath);
			combinedPath.Transform(invWorld);
		}
		else
		{
			combinedPath = BuildGlyphsLocalPath();
		}

		using (combinedPath)
		{
			if (combinedPath.IsEmpty)
				return null;

			var curve = TextPathToCurveShape(combinedPath);
			if (curve == null || curve.Nodes.Count == 0)
				return null;

			curve.FillType = SKPathFillType.Winding;
			curve.Name = Name;
			curve.Layer = Layer;
			curve.Stroke = Stroke.Clone();
			curve.Fill = Fill.Clone();
			curve.IsVisible = IsVisible;
			curve.IsLocked = IsLocked;
			curve.IsFocused = IsFocused;
			curve.LocalTransform = LocalTransform;

			return curve;
		}
	}

	private static CurveShape? TextPathToCurveShape(SKPath path)
	{
		var contours = BuildTextContours(path);
		if (contours.Count == 0)
			return null;

		var curve = new CurveShape();

		foreach (var contour in contours)
		{
			if (contour.Count == 0)
				continue;

			int startIndex = curve.Nodes.Count;

			foreach (var node in contour)
				curve.Nodes.Add(node);

			int endIndex = curve.Nodes.Count - 1;

			curve.Nodes[startIndex].NewContour = true;
			curve.Nodes[endIndex].CloseAfter = true;
		}

		return curve;
	}

	private readonly struct TextPathSegment
	{
		public readonly SKPoint Start;
		public readonly SKPoint? C1;
		public readonly SKPoint? C2;
		public readonly SKPoint End;

		public TextPathSegment(SKPoint start, SKPoint? c1, SKPoint? c2, SKPoint end)
		{
			Start = start;
			C1 = c1;
			C2 = c2;
			End = end;
		}
	}

	private static List<List<CurveNode>> BuildTextContours(SKPath path)
	{
		var contours = new List<List<CurveNode>>();
		List<TextPathSegment>? currentSegs = null;
		SKPoint currentStart = default;
		SKPoint currentPoint = default;

		using var iter = path.CreateRawIterator();
		var pts = new SKPoint[4];

		while (true)
		{
			var verb = iter.Next(pts);

			switch (verb)
			{
				case SKPathVerb.Move:
					if (currentSegs != null && currentSegs.Count > 0)
						contours.Add(BuildTextNodesFromSegments(currentSegs));

					currentSegs = new List<TextPathSegment>();
					currentStart = pts[0];
					currentPoint = pts[0];
					break;

				case SKPathVerb.Line:
					currentSegs?.Add(new TextPathSegment(currentPoint, null, null, pts[1]));
					currentPoint = pts[1];
					break;

				case SKPathVerb.Quad:
					{
						var p0 = pts[0];
						var p1 = pts[1];
						var p2 = pts[2];

						var c1 = new SKPoint(
							p0.X + 2f / 3f * (p1.X - p0.X),
							p0.Y + 2f / 3f * (p1.Y - p0.Y));

						var c2 = new SKPoint(
							p2.X + 2f / 3f * (p1.X - p2.X),
							p2.Y + 2f / 3f * (p1.Y - p2.Y));

						currentSegs?.Add(new TextPathSegment(currentPoint, c1, c2, p2));
						currentPoint = p2;
						break;
					}

				case SKPathVerb.Conic:
					{
						var p0 = pts[0];
						var p1 = pts[1];
						var p2 = pts[2];

						var c1 = new SKPoint(
							p0.X + 2f / 3f * (p1.X - p0.X),
							p0.Y + 2f / 3f * (p1.Y - p0.Y));

						var c2 = new SKPoint(
							p2.X + 2f / 3f * (p1.X - p2.X),
							p2.Y + 2f / 3f * (p1.Y - p2.Y));

						currentSegs?.Add(new TextPathSegment(currentPoint, c1, c2, p2));
						currentPoint = p2;
						break;
					}

				case SKPathVerb.Cubic:
					currentSegs?.Add(new TextPathSegment(currentPoint, pts[1], pts[2], pts[3]));
					currentPoint = pts[3];
					break;

				case SKPathVerb.Close:
					if (currentSegs != null && currentSegs.Count > 0 &&
						TextDistance(currentPoint, currentStart) > 0.001f)
					{
						currentSegs.Add(new TextPathSegment(currentPoint, null, null, currentStart));
					}
					currentPoint = currentStart;
					break;

				case SKPathVerb.Done:
					if (currentSegs != null && currentSegs.Count > 0)
						contours.Add(BuildTextNodesFromSegments(currentSegs));
					return contours;
			}
		}
	}

	private static List<CurveNode> BuildTextNodesFromSegments(List<TextPathSegment> segs)
	{
		var nodes = new List<CurveNode>();

		for (int i = 0; i < segs.Count; i++)
		{
			var seg = segs[i];

			if (i == 0)
			{
				nodes.Add(new CurveNode
				{
					Position = seg.Start,
					Type = CurveNodeType.Straight
				});
			}

			var startNode = nodes[i];

			if (seg.C1.HasValue)
			{
				startNode.ControlNext = seg.C1.Value;
				startNode.Type = CurveNodeType.Sharp;
			}

			var endNode = new CurveNode
			{
				Position = seg.End,
				Type = CurveNodeType.Straight
			};

			if (seg.C2.HasValue)
			{
				endNode.ControlPrev = seg.C2.Value;
				endNode.Type = CurveNodeType.Sharp;
			}

			nodes.Add(endNode);
		}

		if (nodes.Count > 1 &&
			TextDistance(nodes[0].Position, nodes[^1].Position) < 0.001f)
		{
			var first = nodes[0];
			var last = nodes[^1];

			if (last.HasPrevControl)
			{
				first.ControlPrev = last.ControlPrev;
				if (first.Type == CurveNodeType.Straight)
					first.Type = CurveNodeType.Sharp;
			}

			nodes.RemoveAt(nodes.Count - 1);
		}

		return nodes;
	}

	private static float TextDistance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}
}

public sealed class ShapePathSampler
{
	private readonly struct Sample
	{
		public readonly SKPoint Point;

		/// <summary>Нормализованное направление сегмента, приходящего в эту точку.</summary>
		public readonly SKPoint Tangent;

		/// <summary>Накопленная длина от начала СВОЕГО контура.</summary>
		public readonly float CumulativeLength;

		public Sample(SKPoint point, SKPoint tangent, float cumulativeLength)
		{
			Point = point;
			Tangent = tangent;
			CumulativeLength = cumulativeLength;
		}
	}

	/// <summary>
	/// Один контур пути. Отдельная сущность именно для того, чтобы перескок
	/// от конца одного контура к началу следующего не считался частью линии.
	/// </summary>
	private sealed class ContourSamples
	{
		public readonly List<Sample> Samples = new();
		public float Length;
		public bool Closed;

		/// <summary>Смещение начала этого контура в общей координате s.</summary>
		public float StartOffset;
	}

	private readonly List<ContourSamples> _contours = new();

	public float TotalLength { get; private set; }

	/// <summary>Путь состоит из единственного замкнутого контура.</summary>
	public bool IsClosedLoop => _contours.Count == 1 && _contours[0].Closed;

	public int ContourCount => _contours.Count;

	public static ShapePathSampler FromPath(SKPath path, float flattenTolerance = 0.1f)
		=> new ShapePathSampler(path, flattenTolerance);

	public static ShapePathSampler FromShape(Shape shape, float flattenTolerance = 0.1f)
	{
		using var worldPath = shape.BuildWorldPath();
		return new ShapePathSampler(worldPath, flattenTolerance);
	}

	private ShapePathSampler(SKPath path, float flattenTolerance)
	{
		BuildSamples(path, flattenTolerance);
	}

	#region Построение

	private void BuildSamples(SKPath path, float flattenTolerance)
	{
		if (path == null || path.IsEmpty)
			return;

		// Плоские точки собираются по контурам, а не в один общий список.
		var contours = new List<List<SKPoint>>();
		var closedFlags = new List<bool>();

		List<SKPoint>? current = null;

		SKPoint currentPoint = default;
		SKPoint contourStart = default;

		void AddPoint(SKPoint p)
		{
			if (current == null)
				return;

			if (current.Count > 0 && Distance(current[^1], p) < 1e-5f)
				return;

			current.Add(p);
		}

		void FlattenCubic(SKPoint p0, SKPoint c1, SKPoint c2, SKPoint p3)
		{
			// Шаг подбирается по длине контрольного каркаса: у длинных кривых
			// фиксированные 24 шага давали заметные грани.
			float roughLength =
				Distance(p0, c1) + Distance(c1, c2) + Distance(c2, p3);

			int steps = Math.Clamp(
				(int)MathF.Ceiling(roughLength / MathF.Max(flattenTolerance * 8f, 0.01f)),
				8,
				256);

			for (int i = 1; i <= steps; i++)
			{
				float t = i / (float)steps;
				float mt = 1f - t;

				float a = mt * mt * mt;
				float b = 3f * mt * mt * t;
				float c = 3f * mt * t * t;
				float d = t * t * t;

				AddPoint(new SKPoint(
					a * p0.X + b * c1.X + c * c2.X + d * p3.X,
					a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y));
			}
		}

		void FlattenConic(SKPoint p0, SKPoint p1, SKPoint p2, float weight)
		{
			int steps = Math.Clamp(
				(int)MathF.Ceiling((Distance(p0, p1) + Distance(p1, p2)) / MathF.Max(flattenTolerance * 8f, 0.01f)),
				8,
				256);

			for (int i = 1; i <= steps; i++)
			{
				float t = i / (float)steps;
				float mt = 1f - t;

				float denom = mt * mt + 2f * weight * t * mt + t * t;
				if (MathF.Abs(denom) < 1e-9f)
					denom = 1e-9f;

				AddPoint(new SKPoint(
					(mt * mt * p0.X + 2f * weight * t * mt * p1.X + t * t * p2.X) / denom,
					(mt * mt * p0.Y + 2f * weight * t * mt * p1.Y + t * t * p2.Y) / denom));
			}
		}

		void FinishContour(bool closed)
		{
			if (current == null)
				return;

			if (current.Count >= 2)
			{
				contours.Add(current);
				closedFlags.Add(closed);
			}

			current = null;
		}

		using var iter = path.CreateRawIterator();
		var pts = new SKPoint[4];

		bool done = false;

		while (!done)
		{
			var verb = iter.Next(pts);

			switch (verb)
			{
				case SKPathVerb.Move:
					// Новый контур. Предыдущий закрывается как разомкнутый,
					// если Close до него не встретился.
					FinishContour(false);

					current = new List<SKPoint>();
					contourStart = pts[0];
					currentPoint = pts[0];
					AddPoint(pts[0]);
					break;

				case SKPathVerb.Line:
					AddPoint(pts[1]);
					currentPoint = pts[1];
					break;

				case SKPathVerb.Quad:
					{
						var p0 = pts[0];
						var p1 = pts[1];
						var p2 = pts[2];

						var c1 = new SKPoint(
							p0.X + 2f / 3f * (p1.X - p0.X),
							p0.Y + 2f / 3f * (p1.Y - p0.Y));

						var c2 = new SKPoint(
							p2.X + 2f / 3f * (p1.X - p2.X),
							p2.Y + 2f / 3f * (p1.Y - p2.Y));

						FlattenCubic(p0, c1, c2, p2);
						currentPoint = p2;
						break;
					}

				case SKPathVerb.Conic:
					FlattenConic(pts[0], pts[1], pts[2], iter.ConicWeight());
					currentPoint = pts[2];
					break;

				case SKPathVerb.Cubic:
					FlattenCubic(pts[0], pts[1], pts[2], pts[3]);
					currentPoint = pts[3];
					break;

				case SKPathVerb.Close:
					if (Distance(currentPoint, contourStart) > 1e-5f)
						AddPoint(contourStart);

					currentPoint = contourStart;
					FinishContour(true);
					break;

				case SKPathVerb.Done:
					FinishContour(false);
					done = true;
					break;
			}
		}

		BuildFromContours(contours, closedFlags);
	}

	private void BuildFromContours(List<List<SKPoint>> contours, List<bool> closedFlags)
	{
		float offset = 0f;

		for (int c = 0; c < contours.Count; c++)
		{
			var points = contours[c];

			if (points.Count < 2)
				continue;

			var contour = new ContourSamples
			{
				Closed = closedFlags[c],
				StartOffset = offset
			};

			float acc = 0f;

			contour.Samples.Add(new Sample(
				points[0],
				SafeDir(points[0], points[1]),
				0f));

			for (int i = 1; i < points.Count; i++)
			{
				acc += Distance(points[i - 1], points[i]);
				contour.Samples.Add(new Sample(points[i], SafeDir(points[i - 1], points[i]), acc));
			}

			contour.Length = acc;

			if (contour.Length <= 1e-6f)
				continue;

			_contours.Add(contour);

			// Смещение накапливается только по фактической длине контуров.
			// Перескок между ними в общую координату s не входит — из-за него
			// текст раньше шёл по несуществующей линии между контурами.
			offset += contour.Length;
		}

		TotalLength = offset;
	}

	#endregion

	#region Вычисление позиции

	/// <summary>
	/// Точка и угол касательной на расстоянии s от начала пути.
	/// Для единственного замкнутого контура s заворачивается по кругу,
	/// для остальных случаев за границами идёт экстраполяция по касательной.
	/// </summary>
	public bool TryEvaluate(float s, out SKPoint point, out float tangentAngleRad)
	{
		point = default;
		tangentAngleRad = 0f;

		if (_contours.Count == 0 || TotalLength <= 1e-6f)
			return false;

		if (IsClosedLoop)
		{
			s %= TotalLength;

			if (s < 0f)
				s += TotalLength;
		}
		else if (s < 0f)
		{
			var first = _contours[0].Samples[0];

			tangentAngleRad = MathF.Atan2(first.Tangent.Y, first.Tangent.X);
			point = new SKPoint(
				first.Point.X + first.Tangent.X * s,
				first.Point.Y + first.Tangent.Y * s);

			return true;
		}
		else if (s > TotalLength)
		{
			var lastContour = _contours[^1];
			var last = lastContour.Samples[^1];

			float extra = s - TotalLength;

			tangentAngleRad = MathF.Atan2(last.Tangent.Y, last.Tangent.X);
			point = new SKPoint(
				last.Point.X + last.Tangent.X * extra,
				last.Point.Y + last.Tangent.Y * extra);

			return true;
		}

		// Контур, в который попадает s.
		var target = _contours[^1];

		for (int i = 0; i < _contours.Count; i++)
		{
			if (s <= _contours[i].StartOffset + _contours[i].Length)
			{
				target = _contours[i];
				break;
			}
		}

		float local = Math.Clamp(s - target.StartOffset, 0f, target.Length);

		var samples = target.Samples;

		int lo = 0;
		int hi = samples.Count - 1;

		while (lo < hi)
		{
			int mid = (lo + hi) / 2;

			if (samples[mid].CumulativeLength < local)
				lo = mid + 1;
			else
				hi = mid;
		}

		int idx = Math.Max(1, lo);

		var a = samples[idx - 1];
		var b = samples[idx];

		float segLen = b.CumulativeLength - a.CumulativeLength;
		float t = segLen > 1e-6f ? (local - a.CumulativeLength) / segLen : 0f;

		point = new SKPoint(
			a.Point.X + (b.Point.X - a.Point.X) * t,
			a.Point.Y + (b.Point.Y - a.Point.Y) * t);

		tangentAngleRad = MathF.Atan2(b.Tangent.Y, b.Tangent.X);
		return true;
	}

	#endregion

	private static SKPoint SafeDir(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 1e-6f)
			return new SKPoint(1f, 0f);

		return new SKPoint(dx / len, dy / len);
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}
}

public enum TextAreaHAlign
{
	Left = 0,
	Center = 1,
	Right = 2
}

public enum TextAreaVAlign
{
	Top = 0,
	Middle = 1,
	Bottom = 2
}

/// <summary>
/// Раскладка текста внутри произвольной области.
///
/// Область задаётся путём, препятствия — отдельным списком путей. Каждая строка
/// разбивается на отрезки, попадающие внутрь области и не попадающие ни в одно
/// препятствие. Слова раскладываются по этим отрезкам слева направо, поэтому
/// строка, упёршаяся в левый край препятствия, продолжается справа от него.
///
/// Препятствия НЕ вычитаются из области заранее через SKPath.Op: эта операция
/// может вернуть null на самопересекающихся или вырожденных путях, и тогда
/// препятствие молча переставало учитываться. Вычитание интервалов внутри
/// строки — операция над числами, она не может не сработать.
/// </summary>
public static class TextAreaLayout
{
	public sealed class Options
	{
		public float LineHeight = 6f;
		public float Ascent = 4f;
		/// <summary>
		/// Спуск шрифта: насколько буквы уходят ниже базовой линии. Вместе
		/// с Ascent задаёт вертикальный габарит строки, по которому проверяется
		/// попадание в область и в препятствия.
		/// </summary>
		public float Descent = 1.5f;

		/// <summary>
		/// Сколько отсчётов по вертикали делать в пределах строки.
		///
		/// Два (верх и низ) достаточно для выпуклых препятствий, где крайние
		/// значения ширины лежат на границах полосы. Пять покрывает и случай,
		/// когда препятствие выпирает в середине высоты строки. Больше почти
		/// никогда не нужно: строка редко бывает выше нескольких миллиметров.
		/// </summary>
		public int VerticalSamples = 5;


		public int Columns = 1;
		public float ColumnGap = 5f;

		public TextAreaHAlign HAlign = TextAreaHAlign.Left;
		public TextAreaVAlign VAlign = TextAreaVAlign.Top;

		public float CharacterSpacing = 0f;

		/// <summary>Отступ от границ области и от препятствий.</summary>
		public float Padding = 0f;

		/// <summary>
		/// Препятствия в тех же координатах, что и область. Строка обходит их,
		/// продолжаясь с другой стороны.
		/// </summary>
		public IReadOnlyList<SKPath>? Obstacles;

		/// <summary>
		/// Отрезок уже этой ширины пропускается: в него всё равно не поместится
		/// ничего осмысленного, а строка из одной буквы в щели выглядит мусором.
		/// </summary>
		public float MinSpanWidth = 2f;

		/// <summary>
		/// Шаг проверки принадлежности. Меньше — точнее границы строк, но
		/// дороже: проверок на строку получается ширина области, делённая на шаг.
		/// </summary>
		public float ProbeStep = 0.5f;
	}

	public readonly struct PlacedLine
	{
		public readonly string Text;
		public readonly float X;
		public readonly float Baseline;
		public readonly float Width;

		public PlacedLine(string text, float x, float baseline, float width)
		{
			Text = text;
			X = x;
			Baseline = baseline;
			Width = width;
		}
	}

	public sealed class Result
	{
		public readonly List<PlacedLine> Lines = new();

		/// <summary>Часть текста не поместилась.</summary>
		public bool Overflow;

		/// <summary>Нижняя граница занятого текстом места.</summary>
		public float UsedBottom;
	}

	private readonly struct Span
	{
		public readonly float Left;
		public readonly float Right;

		public Span(float left, float right)
		{
			Left = left;
			Right = right;
		}

		public float Width => Right - Left;
	}

	public static Result Layout(string? text, SKFont font, SKPath region, Options options)
	{
		if (string.IsNullOrEmpty(text) || font == null || region == null || region.IsEmpty)
			return new Result();

		var bounds = region.Bounds;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
			return new Result();

		// Вертикальное выравнивание требует знать занятую высоту заранее, а она
		// зависит от раскладки. Отсюда два прохода: первый сверху измеряет,
		// второй раскладывает со сдвигом.
		//
		// Для невыпуклых областей это приближение: сдвиг меняет форму отрезков,
		// и занятая высота во втором проходе может отличаться. Для
		// прямоугольников, эллипсов и прочих выпуклых фигур результат точен.
		var firstPass = LayoutPass(text, font, region, options, 0f);

		if (options.VAlign == TextAreaVAlign.Top || firstPass.Lines.Count == 0)
			return firstPass;

		float usableBottom = bounds.Bottom - options.Padding;
		float freeHeight = MathF.Max(0f, usableBottom - firstPass.UsedBottom);

		float offset = options.VAlign == TextAreaVAlign.Middle
			? freeHeight * 0.5f
			: freeHeight;

		return offset <= 0.01f
			? firstPass
			: LayoutPass(text, font, region, options, offset);
	}

	private static Result LayoutPass(string text, SKFont font, SKPath region, Options options, float yOffset)
	{
		var result = new Result();
		var bounds = region.Bounds;

		float padding = MathF.Max(0f, options.Padding);

		// Отступ сжимает область со всех сторон. По вертикали — от габаритов,
		// по горизонтали — от каждого найденного отрезка, то есть от реального
		// контура в этой строке.
		float areaLeft = bounds.Left + padding;
		float areaRight = bounds.Right - padding;
		float areaTop = bounds.Top + padding;
		float areaBottom = bounds.Bottom - padding;

		if (areaRight - areaLeft <= 0f || areaBottom - areaTop <= 0f)
			return result;

		int columns = Math.Clamp(options.Columns <= 0 ? 1 : options.Columns, 1, 10);
		float gap = MathF.Max(0f, options.ColumnGap);

		float columnWidth = (areaRight - areaLeft - gap * (columns - 1)) / columns;

		// Колонок больше, чем влезает: сводим к одной. Иначе ширина уходит
		// в отрицательные значения и раскладка молча пустеет.
		if (columnWidth <= 0f)
		{
			columns = 1;
			gap = 0f;
			columnWidth = areaRight - areaLeft;
		}

		float lineHeight = MathF.Max(options.LineHeight, 0.01f);

		var words = BuildTokens(text);
		int wordIndex = 0;

		result.UsedBottom = areaTop + yOffset;

		for (int column = 0; column < columns && wordIndex < words.Count; column++)
		{
			float columnLeft = areaLeft + column * (columnWidth + gap);
			float columnRight = columnLeft + columnWidth;

			float y = areaTop + yOffset;

			while (y + lineHeight <= areaBottom + 0.001f && wordIndex < words.Count)
			{
				// Маркер абзаца в начале строки — это пустая строка текста.
				// Обработать его нужно ЗДЕСЬ: FillSpan на маркере сразу выходит,
				// не сдвигая индекс, и раскладка застревала бы на нём до конца
				// области, теряя весь оставшийся текст.
				if (words[wordIndex].IsBreak)
				{
					wordIndex++;

					// Пустая строка занимает своё место по вертикали.
					result.UsedBottom = MathF.Max(result.UsedBottom, y + lineHeight);
					y += lineHeight;
					continue;
				}

				float baseline = y + options.Ascent;

				// Вертикальный габарит чернил строки. Проверяется вся полоса,
				// а не средняя линия: у изогнутого препятствия ширина меняется
				// по высоте, и буквы заходили на него сверху и снизу.
				float bandTop = baseline - options.Ascent;
				float bandBottom = baseline + options.Descent;

				var spans = BuildRowSpans(
					region, options, columnLeft, columnRight, bandTop, bandBottom, padding);


				// Отрезки идут слева направо, поэтому строка, упёршаяся в левый
				// край препятствия, продолжается в следующем отрезке — справа
				// от него, на той же строке.
				foreach (var span in spans)
				{
					if (wordIndex >= words.Count)
						break;

					// Маркер мог оказаться текущим после заполнения предыдущего
					// отрезка этой же строки — остаток строки не заполняем.
					if (words[wordIndex].IsBreak)
						break;

					if (span.Width < options.MinSpanWidth)
						continue;

					string line = FillSpan(words, ref wordIndex, font, span, options, out float lineWidth);

					if (string.IsNullOrEmpty(line))
						continue;

					float x = options.HAlign switch
					{
						TextAreaHAlign.Center => span.Left + (span.Width - lineWidth) * 0.5f,
						TextAreaHAlign.Right => span.Right - lineWidth,
						_ => span.Left
					};

					result.Lines.Add(new PlacedLine(line, x, baseline, lineWidth));
					result.UsedBottom = MathF.Max(result.UsedBottom, y + lineHeight);

					// Явный перевод строки: остаток строки не заполняем.
					if (wordIndex < words.Count && words[wordIndex].IsBreak)
					{
						wordIndex++;
						break;
					}
				}

				y += lineHeight;
			}
		}

		result.Overflow = wordIndex < words.Count;
		return result;
	}

	#region Отрезки строки
	/// <summary>
	/// Пересечение двух наборов интервалов. Оба набора отсортированы по левому
	/// краю и не перекрываются внутри себя — так их отдаёт ScanIntervals.
	/// </summary>
	private static List<Span> Intersect(List<Span> a, List<Span> b)
	{
		var result = new List<Span>(Math.Max(a.Count, b.Count));

		int i = 0;
		int j = 0;

		while (i < a.Count && j < b.Count)
		{
			float left = MathF.Max(a[i].Left, b[j].Left);
			float right = MathF.Min(a[i].Right, b[j].Right);

			if (right > left)
				result.Add(new Span(left, right));

			// Дальше двигаем тот интервал, который заканчивается раньше.
			if (a[i].Right < b[j].Right)
				i++;
			else
				j++;
		}

		return result;
	}

	/// <summary>
	/// Отрезки, доступные для текста в одной строке: то, что внутри области
	/// на ВСЕЙ высоте строки, минус то, что занято препятствиями хотя бы
	/// на какой-то её высоте, с учётом отступа.
	/// </summary>
	private static List<Span> BuildRowSpans(
		SKPath region,
		Options options,
		float left,
		float right,
		float bandTop,
		float bandBottom,
		float padding)
	{
		// Полоса расширяется на отступ вверх и вниз: padding должен работать
		// и по вертикали, иначе буквы прижимаются к препятствию сверху и снизу.
		float top = bandTop - padding;
		float bottom = bandBottom + padding;

		if (bottom < top)
			(top, bottom) = (bottom, top);

		int samples = Math.Clamp(options.VerticalSamples, 2, 16);

		// Область: пересечение по всем отсчётам. Текст должен помещаться
		// на всей высоте строки, поэтому годится только общая часть.
		List<Span>? spans = null;

		for (int i = 0; i < samples; i++)
		{
			float t = samples == 1 ? 0.5f : i / (float)(samples - 1);
			float y = top + (bottom - top) * t;

			var current = ScanIntervals(region, left, right, y, options.ProbeStep);

			spans = spans == null ? current : Intersect(spans, current);

			// Пересечение опустело — ниже уже ничего не появится.
			if (spans.Count == 0)
				return spans;
		}

		if (spans == null || spans.Count == 0)
			return spans ?? new List<Span>();

		// Отступ от контура по горизонтали: каждый отрезок сжимается с обеих
		// сторон. По вертикали отступ уже учтён расширением полосы.
		if (padding > 0f)
		{
			for (int i = spans.Count - 1; i >= 0; i--)
			{
				var s = new Span(spans[i].Left + padding, spans[i].Right - padding);

				if (s.Width <= 0f)
					spans.RemoveAt(i);
				else
					spans[i] = s;
			}
		}

		if (options.Obstacles == null || options.Obstacles.Count == 0)
			return spans;

		// Препятствия: объединение по всем отсчётам. Препятствие мешает, если
		// задевает строку хотя бы на какой-то высоте.
		var blocked = new List<Span>();

		foreach (var obstacle in options.Obstacles)
		{
			if (obstacle == null || obstacle.IsEmpty)
				continue;

			var b = obstacle.Bounds;

			// Препятствие не задевает полосу по вертикали или столбец
			// по горизонтали — сканировать незачем.
			if (bottom < b.Top || top > b.Bottom)
				continue;

			if (b.Right + padding < left || b.Left - padding > right)
				continue;

			float scanLeft = MathF.Max(left, b.Left - padding);
			float scanRight = MathF.Min(right, b.Right + padding);

			for (int i = 0; i < samples; i++)
			{
				float t = samples == 1 ? 0.5f : i / (float)(samples - 1);
				float y = top + (bottom - top) * t;

				if (y < b.Top || y > b.Bottom)
					continue;

				foreach (var hit in ScanIntervals(obstacle, scanLeft, scanRight, y, options.ProbeStep))
					blocked.Add(new Span(hit.Left - padding, hit.Right + padding));
			}
		}

		return blocked.Count == 0 ? spans : Subtract(spans, blocked);
	}


	/// <summary>
	/// Интервалы по горизонтали, попадающие внутрь пути, на заданной высоте.
	///
	/// Реализовано выборкой с шагом: прямого способа получить пересечения
	/// горизонтали с SKPath в Skia нет, а Op с прямоугольником на каждую строку
	/// обошёлся бы дороже и всё равно потребовал бы разбора контуров результата.
	/// </summary>
	private static List<Span> ScanIntervals(SKPath path, float left, float right, float y, float step)
	{
		var spans = new List<Span>();

		if (right <= left)
			return spans;

		step = MathF.Max(step, 0.05f);

		bool inside = false;
		float runStart = 0f;
		float x = left;

		while (x <= right)
		{
			bool nowInside = path.Contains(x, y);

			if (nowInside && !inside)
			{
				inside = true;
				runStart = x;
			}
			else if (!nowInside && inside)
			{
				inside = false;
				spans.Add(new Span(runStart, x));
			}

			x += step;
		}

		if (inside)
			spans.Add(new Span(runStart, right));

		return spans;
	}

	/// <summary>
	/// Вычитает занятые интервалы из доступных. Операция над числами, поэтому
	/// в отличие от SKPath.Op не может завершиться неудачей.
	/// </summary>
	private static List<Span> Subtract(List<Span> spans, List<Span> blocked)
	{
		var result = new List<Span>(spans.Count + blocked.Count);

		blocked.Sort((a, b) => a.Left.CompareTo(b.Left));

		foreach (var span in spans)
		{
			float cursor = span.Left;

			foreach (var block in blocked)
			{
				if (block.Right <= cursor)
					continue;

				if (block.Left >= span.Right)
					break;

				if (block.Left > cursor)
					result.Add(new Span(cursor, MathF.Min(block.Left, span.Right)));

				cursor = MathF.Max(cursor, block.Right);

				if (cursor >= span.Right)
					break;
			}

			if (cursor < span.Right)
				result.Add(new Span(cursor, span.Right));
		}

		return result;
	}

	#endregion

	#region Разбиение на слова
	private readonly struct Token
	{
		public readonly string Text;
		public readonly bool IsBreak;

		public Token(string text, bool isBreak)
		{
			Text = text;
			IsBreak = isBreak;
		}
	}

	private static List<Token> BuildTokens(string text)
	{
		var tokens = new List<Token>();

		string[] paragraphs = text
			.Replace("\r\n", "\n")
			.Replace("\r", "\n")
			.Split('\n');

		for (int p = 0; p < paragraphs.Length; p++)
		{
			foreach (var word in paragraphs[p].Split(' ', StringSplitOptions.RemoveEmptyEntries))
				tokens.Add(new Token(word, false));

			if (p < paragraphs.Length - 1)
				tokens.Add(new Token(string.Empty, true));
		}

		return tokens;
	}

	/// <summary>
	/// Набирает слова в один отрезок. Слово, не влезающее целиком, переносится
	/// в следующий отрезок — и только если отрезок пуст, рвётся по символам,
	/// иначе одно длинное слово остановило бы раскладку насовсем.
	/// </summary>
	private static string FillSpan(
		List<Token> words,
		ref int index,
		SKFont font,
		Span span,
		Options options,
		out float lineWidth)
	{
		var sb = new System.Text.StringBuilder();
		lineWidth = 0f;

		float spaceWidth = MeasureWithSpacing(" ", font, options.CharacterSpacing);

		while (index < words.Count)
		{
			var token = words[index];

			if (token.IsBreak)
				break;

			float wordWidth = MeasureWithSpacing(token.Text, font, options.CharacterSpacing);
			float withSeparator = sb.Length == 0 ? wordWidth : spaceWidth + wordWidth;

			if (lineWidth + withSeparator <= span.Width)
			{
				if (sb.Length > 0)
				{
					sb.Append(' ');
					lineWidth += spaceWidth;
				}

				sb.Append(token.Text);
				lineWidth += wordWidth;
				index++;
				continue;
			}

			// В отрезке уже что-то есть — остальное уйдёт в следующий отрезок
			// этой же строки либо на следующую строку.
			if (sb.Length > 0)
				break;

			// Отрезок пуст и слово не влезает. Рвём по символам только если
			// отрезок достаточно широк: в узкой щели между препятствием и краем
			// фигуры обрывок слова выглядит мусором, лучше пропустить отрезок.
			if (span.Width < options.MinSpanWidth * 3f)
				break;

			int fits = CountFittingChars(token.Text, font, options.CharacterSpacing, span.Width);

			if (fits <= 0)
				break;

			string head = token.Text[..fits];
			sb.Append(head);
			lineWidth += MeasureWithSpacing(head, font, options.CharacterSpacing);

			// Хвост возвращается в очередь на место текущего элемента.
			words[index] = new Token(token.Text[fits..], false);
			break;
		}

		return sb.ToString();
	}

	private static int CountFittingChars(string word, SKFont font, float charSpacing, float available)
	{
		int fits = 0;
		float width = 0f;

		for (int i = 0; i < word.Length; i++)
		{
			// Продвижение символа берётся с учётом табуляции: иначе слово
			// с табуляцией рвалось бы не там, где рисуется.
			float charWidth = TextGlyphs.GetCharAdvance(font, word[i], charSpacing);

			if (width + charWidth > available)
				break;

			width += charWidth;
			fits++;
		}

		return fits;
	}

	private static float MeasureWithSpacing(string s, SKFont font, float charSpacing)
		=> TextGlyphs.Measure(font, s, charSpacing);
	#endregion
}

/// <summary>
/// Текст внутри фигуры, режимы размещения и оформление шрифта.
///
/// Отдельный файл частичного класса: в TextShape.cs остаются только восемь
/// точечных правок (см. TextAreaInstall.md), всё новое живёт здесь.
/// </summary>
public sealed partial class TextShape
{
	#region Оформление шрифта
	public bool Underline { get; set; } = false;
	public bool Strikethrough { get; set; } = false;
	#endregion

	#region Режимы размещения
	private bool _alongShape;
	private bool _insideOtherShape;
	private Shape? _insideShape;
	private float _padding = 0f;
	private bool _clipByShape = true;

	/// <summary>
	/// Обрезать текст по контуру контейнера. Относится к отрисовке, а не
	/// к обтеканию: раскладкой строк управляют WrapInsideShape и
	/// WrapOtherShapes, а это — что делать с тем, что вышло за контур.
	/// </summary>
	public bool ClipByShape
	{
		get => _clipByShape;
		set { if (_clipByShape != value) { _clipByShape = value; InvalidateAreaCache(); } }
	}

	/// <summary>
	/// Отступ текста от границ контейнера и от обтекаемых фигур, общий для всех
	/// сторон, в единицах документа.
	/// </summary>
	public float Padding
	{
		get => _padding;
		set
		{
			float v = MathF.Max(0f, value);

			if (MathF.Abs(_padding - v) > 1e-4f)
			{
				_padding = v;
				InvalidateAreaCache();
			}
		}
	}

	/// <summary>
	/// Текст идёт по контуру фигуры otherShape.
	/// Включение сбрасывает InsideOtherShape: режимы взаимоисключающие.
	/// </summary>
	public bool AlongShape
	{
		get => _alongShape;
		set
		{
			if (_alongShape == value)
				return;

			_alongShape = value;

			// Присваивание идёт напрямую в поле, а не через второе свойство:
			// через свойство получилась бы взаимная рекурсия сеттеров.
			if (value && _insideOtherShape)
			{
				_insideOtherShape = false;
				InvalidateAreaCache();
			}
		}
	}

	/// <summary>
	/// Текст заполняет внутреннюю область фигуры InsideShape.
	/// Включение сбрасывает AlongShape.
	/// </summary>
	public bool InsideOtherShape
	{
		get => _insideOtherShape;
		set
		{
			if (_insideOtherShape == value)
				return;

			_insideOtherShape = value;
			InvalidateAreaCache();

			if (value)
				_alongShape = false;
		}
	}

	/// <summary>
	/// Фигура-контейнер. Отдельная ссылка, а не otherShape: иначе включение
	/// одного режима сбрасывало бы выбор фигуры для другого.
	/// </summary>
	public Shape? InsideShape
	{
		get => _insideShape;
		set
		{
			if (ReferenceEquals(_insideShape, value))
				return;

			_insideShape = value;
			InvalidateAreaCache();
		}
	}

	/// <summary>Режим «внутри фигуры» активен и контейнер выбран.</summary>
	public bool IsAreaText => _insideOtherShape && _insideShape != null;

	#endregion

	#region Параметры области

	private TextAreaHAlign _areaHAlign = TextAreaHAlign.Left;
	private TextAreaVAlign _areaVAlign = TextAreaVAlign.Top;

	private bool _wrapInsideShape = true;
	private bool _wrapOtherShapes = false;

	private int _columns = 1;
	private float _distanceBetweenColumns = 5f;

	public TextAreaHAlign AreaHAlign
	{
		get => _areaHAlign;
		set { if (_areaHAlign != value) { _areaHAlign = value; InvalidateAreaCache(); } }
	}

	public TextAreaVAlign AreaVAlign
	{
		get => _areaVAlign;
		set { if (_areaVAlign != value) { _areaVAlign = value; InvalidateAreaCache(); } }
	}

	/// <summary>
	/// Строки следуют контуру контейнера. Выключено — текст заполняет
	/// габаритный прямоугольник контейнера ровным блоком.
	/// </summary>
	public bool WrapInsideShape
	{
		get => _wrapInsideShape;
		set { if (_wrapInsideShape != value) { _wrapInsideShape = value; InvalidateAreaCache(); } }
	}

	/// <summary>Обтекать фигуры, пересекающие контейнер.</summary>
	public bool WrapOtherShapes
	{
		get => _wrapOtherShapes;
		set { if (_wrapOtherShapes != value) { _wrapOtherShapes = value; InvalidateAreaCache(); } }
	}

	/// <summary>Число колонок. 0 и 1 означают одну колонку.</summary>
	public int Columns
	{
		get => _columns;
		set
		{
			int v = Math.Clamp(value, 0, 10);

			if (_columns != v)
			{
				_columns = v;
				InvalidateAreaCache();
			}
		}
	}

	public float DistanceBetweenColumns
	{
		get => _distanceBetweenColumns;
		set
		{
			float v = MathF.Max(0f, value);

			if (MathF.Abs(_distanceBetweenColumns - v) > 1e-4f)
			{
				_distanceBetweenColumns = v;
				InvalidateAreaCache();
			}
		}
	}
	#endregion

	#region Кэш раскладки
	private SKPath? _areaGlyphCache;
	private SKPath? _areaDecorationCache;
	private SKPath? _areaClipCache;
	private int _areaSignature;

	/// <summary>
	/// Сбросить кэш раскладки. Вызывается из сеттеров и извне, когда изменилась
	/// геометрия контейнера или обтекаемых фигур.
	/// </summary>
	public void InvalidateAreaCache()
	{
		_areaGlyphCache?.Dispose();
		_areaDecorationCache?.Dispose();
		_areaClipCache?.Dispose();

		_areaGlyphCache = null;
		_areaDecorationCache = null;
		_areaClipCache = null;

		_areaSignature = 0;
	}

	/// <summary>
	/// Освободить ресурсы кэша. Вызывать при удалении фигуры со страницы.
	/// </summary>
	public void DisposeAreaResources() => InvalidateAreaCache();

	/// <summary>
	/// Подпись входных данных раскладки: пересчёт запускается при её изменении.
	///
	/// Раскладка внутри области стоит дорого — на каждую строку выполняется
	/// сканирование SKPath.Contains с шагом ProbeStep. Для области 200 мм при
	/// кегле 5 мм это порядка 16 тысяч вызовов Contains на пересчёт, поэтому
	/// без кэша делать это на каждый кадр нельзя.
	/// </summary>
	private int ComputeAreaSignature()
	{
		var hash = new HashCode();

		hash.Add(Text);
		hash.Add(FontFamily);
		hash.Add(FontSizeMm);
		hash.Add(Bold);
		hash.Add(Italic);
		hash.Add(Underline);
		hash.Add(Strikethrough);
		hash.Add(CharacterSpacingMm);
		hash.Add(LineSpacingMm);
		hash.Add(_areaHAlign);
		hash.Add(_areaVAlign);
		hash.Add(_wrapInsideShape);
		hash.Add(_clipByShape);
		hash.Add(_wrapOtherShapes);
		hash.Add(_columns);
		hash.Add(_distanceBetweenColumns);
		hash.Add(_padding);

		// Своя трансформация влияет на область: она переводится в локальные
		// координаты текста.
		var self = GetWorldTransform();
		hash.Add(self.ScaleX);
		hash.Add(self.ScaleY);
		hash.Add(self.SkewX);
		hash.Add(self.SkewY);
		hash.Add(self.TransX);
		hash.Add(self.TransY);

		AddShapeGeometry(ref hash, _insideShape);

		// Обтекаемые фигуры меняются независимо от контейнера.
		if (_wrapOtherShapes)
		{
			foreach (var shape in EnumerateWrapShapes())
				AddShapeGeometry(ref hash, shape);
		}

		return hash.ToHashCode();
	}

	private static void AddShapeGeometry(ref HashCode hash, Shape? shape)
	{
		if (shape == null)
		{
			hash.Add(0);
			return;
		}

		// Собственная форма фигуры. Для кривой это координаты всех узлов,
		// поэтому перемещение узла внутрь контура тоже меняет подпись.
		hash.Add(shape.GetGeometryHash());

		// Положение фигуры в мире: она могла не измениться сама, но переехать
		// вместе с родительской группой.
		var m = shape.GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);
	}
	#endregion

	#region Построение пути
	/// <summary>
	/// Единый путь текста внутри области. Используется для габаритов, проверки
	/// попадания и преобразования в кривые — там раздельная геометрия не нужна.
	/// Для ОТРИСОВКИ используйте BuildInsideShapeParts: слияние заливает дырки
	/// букв в полосе подчёркивания и зачёркивания.
	/// </summary>
	public SKPath BuildInsideShapeLocalPath()
	{
		BuildInsideShapeParts(out var glyphs, out var decorations, out var clip);

		var combined = new SKPath(glyphs) { FillType = SKPathFillType.Winding };

		if (!decorations.IsEmpty)
			combined.AddPath(decorations);

		if (clip == null || combined.IsEmpty)
			return combined;

		// Габариты должны учитывать обрезку, иначе рамка выделения окажется
		// шире видимого текста.
		var clipped = combined.Op(clip, SKPathOp.Intersect);

		if (clipped == null)
			return combined;

		combined.Dispose();
		clipped.FillType = SKPathFillType.Winding;
		return clipped;
	}

	/// <summary>
	/// Части текста внутри области: контуры глифов, отдельная геометрия
	/// оформления и контур для обрезки. Все три в локальных координатах текста.
	///
	/// Ничего не объединяется: оформление рисуется вторым проходом поверх
	/// глифов, обрезка выполняется канвой. Булевых операций над путём текста
	/// не производится, поэтому дырки букв остаются нетронутыми.
	///
	/// Возвращаемые пути принадлежат фигуре — освобождать их вызывающему НЕ нужно.
	/// </summary>
	private void BuildInsideShapeParts(out SKPath glyphs, out SKPath decorations, out SKPath? clip)
	{
		int signature = ComputeAreaSignature();

		if (_areaGlyphCache != null && _areaSignature == signature)
		{
			glyphs = _areaGlyphCache;
			decorations = _areaDecorationCache ?? EmptyPath;
			clip = _areaClipCache;
			return;
		}

		InvalidateAreaCache();

		_areaGlyphCache = new SKPath { FillType = SKPathFillType.Winding };
		_areaDecorationCache = new SKPath { FillType = SKPathFillType.Winding };

		BuildInsideShapeParts(_areaGlyphCache, _areaDecorationCache, out _areaClipCache);

		_areaSignature = signature;

		glyphs = _areaGlyphCache;
		decorations = _areaDecorationCache;
		clip = _areaClipCache;
	}

	private static readonly SKPath EmptyPath = new SKPath();

	private void BuildInsideShapeParts(SKPath glyphs, SKPath decorations, out SKPath? clip)
	{
		clip = null;

		if (string.IsNullOrEmpty(Text) || _insideShape == null)
			return;

		if (!TryBuildFlowRegions(out var layoutRegion, out var clipRegion, out var obstacles) ||
			layoutRegion == null || clipRegion == null)
		{
			DisposeRegions(layoutRegion, clipRegion, obstacles);
			return;
		}

		try
		{
			using var font = CreateLocalFont();
			var metrics = font.Metrics;

			var options = new TextAreaLayout.Options
			{
				LineHeight = FontSizeMm * 1.2f + LineSpacingMm,
				Ascent = -metrics.Ascent,
				Descent = metrics.Descent,
				Columns = _columns,
				ColumnGap = _distanceBetweenColumns,
				HAlign = _areaHAlign,
				VAlign = _areaVAlign,
				CharacterSpacing = CharacterSpacingMm,
				Padding = _padding,
				Obstacles = obstacles,
				MinSpanWidth = MathF.Max(FontSizeMm * 0.5f, 0.5f),
				ProbeStep = Math.Clamp(FontSizeMm * 0.1f, 0.1f, 2f)
			};

			var layout = TextAreaLayout.Layout(Text, font, layoutRegion, options);

			foreach (var line in layout.Lines)
			{
				var origin = new SKPoint(line.X, line.Baseline);

				AppendLineGlyphs(glyphs, font, line.Text, origin);
				AppendDecorations(decorations, font, line.Text, origin, line.Width);
			}

			// Контур обрезки сохраняется для отрисовки: обрезать будет канва,
			// а не булева операция над путём.
			if (_clipByShape)
			{
				clip = new SKPath(clipRegion);
				clip.FillType = SKPathFillType.Winding;
			}
		}
		finally
		{
			DisposeRegions(layoutRegion, clipRegion, obstacles);
		}
	}

	/// <summary>Части обычного текста: глифы и оформление отдельно.</summary>
	private void BuildGlyphsParts(SKPath glyphs, SKPath decorations)
	{
		using var font = CreateLocalFont();

		string[] lines = (Text ?? string.Empty)
			.Replace("\r\n", "\n")
			.Replace("\r", "\n")
			.Split('\n');

		float lineStep = FontSizeMm * 1.2f + LineSpacingMm;
		float y = LocalOrigin.Y;

		foreach (var line in lines)
		{
			if (!string.IsNullOrEmpty(line))
			{
				var origin = new SKPoint(LocalOrigin.X, y);

				AppendLineGlyphs(glyphs, font, line, origin);
				AppendDecorations(decorations, font, line, origin, 0f);
			}

			y += lineStep;
		}
	}

	private SKPath BuildGlyphsLocalPath()
	{
		var combined = new SKPath { FillType = SKPathFillType.Winding };
		using var decorations = new SKPath { FillType = SKPathFillType.Winding };

		BuildGlyphsParts(combined, decorations);

		if (!decorations.IsEmpty)
			combined.AddPath(decorations);

		return combined;
	}


	/// <summary>
	/// Освобождение путей области. layout и clip могут быть одним объектом,
	/// поэтому проверка ссылки обязательна: двойной Dispose одного SKPath
	/// падает в нативном коде, а не бросает управляемое исключение.
	/// </summary>
	private static void DisposeRegions(SKPath? layout, SKPath? clip, List<SKPath>? obstacles)
	{
		if (obstacles != null)
		{
			foreach (var obstacle in obstacles)
				obstacle.Dispose();

			obstacles.Clear();
		}

		if (clip != null && !ReferenceEquals(clip, layout))
			clip.Dispose();

		layout?.Dispose();
	}

	/// <summary>
	/// Области и препятствия в локальных координатах текста.
	///
	/// layout — где раскладывать строки: контур контейнера либо его габаритный
	/// прямоугольник, в зависимости от WrapInsideShape.
	/// clip — контур контейнера для обрезки.
	/// obstacles — пути обтекаемых фигур, если включено WrapOtherShapes.
	///
	/// Пути layout и clip могут оказаться одним объектом: когда WrapInsideShape
	/// включён, копировать область незачем.
	/// </summary>
	private bool TryBuildFlowRegions(out SKPath? layout, out SKPath? clip, out List<SKPath> obstacles)
	{
		layout = null;
		clip = null;
		obstacles = new List<SKPath>();

		if (_insideShape == null)
			return false;

		using var containerWorld = _insideShape.BuildWorldPath();

		if (containerWorld == null || containerWorld.IsEmpty)
			return false;

		var containerBoundsWorld = containerWorld.Bounds;

		if (!TryGetInverseWorldTransform(out var inverse))
			inverse = SKMatrix.CreateIdentity();

		// Контур контейнера. Simplify нужен для корректной проверки
		// принадлежности у самопересекающихся фигур.
		SKPath clipRegion = containerWorld.Simplify() ?? new SKPath(containerWorld);

		clipRegion.Transform(inverse);
		clipRegion.FillType = SKPathFillType.Winding;

		SKPath layoutRegion;

		if (_wrapInsideShape)
		{
			layoutRegion = clipRegion;
		}
		else
		{
			// Раскладка по габаритам контейнера, обрезка по-прежнему по контуру.
			layoutRegion = new SKPath { FillType = SKPathFillType.Winding };
			layoutRegion.AddRect(clipRegion.Bounds);
		}

		if (_wrapOtherShapes)
		{
			foreach (var shape in EnumerateWrapShapes())
			{
				using var obstacleWorld = shape.BuildWorldPath();

				if (obstacleWorld == null || obstacleWorld.IsEmpty)
					continue;

				if (!obstacleWorld.Bounds.IntersectsWith(containerBoundsWorld))
					continue;

				// Simplify здесь тоже полезен, но НЕ обязателен: если он
				// вернёт null, работаем с исходным путём. Прежняя схема на
				// неудаче Op теряла препятствие целиком.
				var obstacle = obstacleWorld.Simplify() ?? new SKPath(obstacleWorld);

				obstacle.Transform(inverse);
				obstacle.FillType = SKPathFillType.Winding;

				obstacles.Add(obstacle);
			}
		}

		layout = layoutRegion;
		clip = clipRegion;

		return !layoutRegion.IsEmpty;
	}


	/// <summary>
	/// Обтекаемые фигуры: всё на странице, кроме самого текста и контейнера.
	/// Направляющие исключены как вспомогательные.
	/// </summary>
	private IEnumerable<Shape> EnumerateWrapShapes()
	{
		var page = MainForm.CurrentPage;

		if (page == null)
			yield break;

		foreach (var shape in page.Shapes)
		{
			if (ReferenceEquals(shape, this) || ReferenceEquals(shape, _insideShape))
				continue;

			if (shape is GuideLine || !shape.IsVisible)
				continue;

			yield return shape;
		}
	}
	#endregion

	#region Глифы и оформление

	/// <summary>
	/// Глифы одной строки с учётом межсимвольного интервала и табуляции.
	/// Общий код для обычного текста и текста внутри области.
	/// </summary>
	internal void AppendLineGlyphs(SKPath target, SKFont font, string line, SKPoint origin)
	{
		TextGlyphs.AppendPath(target, font, line, origin, CharacterSpacingMm);
	}

	/// <summary>
	/// Подчёркивание и зачёркивание. Добавляются в тот же путь, что и глифы,
	/// поэтому заливка и обводка применяются к ним автоматически, без
	/// отдельной кисти.
	///
	/// width = 0 означает «измерить строку самостоятельно».
	/// </summary>
	internal void AppendDecorations(SKPath target, SKFont font, string line, SKPoint origin, float width)
	{
		if (!Underline && !Strikethrough)
			return;

		if (string.IsNullOrEmpty(line))
			return;

		if (width <= 0f)
		{
			if (width <= 0f)
				width = TextGlyphs.Measure(font, line, CharacterSpacingMm);

		}

		var metrics = font.Metrics;

		float thickness = metrics.UnderlineThickness ?? 0f;

		if (thickness <= 0f)
			thickness = FontSizeMm * 0.06f;

		if (Underline)
		{
			float position = metrics.UnderlinePosition ?? 0f;

			if (position <= 0f)
				position = FontSizeMm * 0.12f;

			float top = origin.Y + position;
			target.AddRect(new SKRect(origin.X, top, origin.X + width, top + thickness));
		}

		if (Strikethrough)
		{
			// StrikeoutPosition в Skia — смещение ВВЕРХ от базовой линии,
			// то есть отрицательное при оси Y, направленной вниз.
			float position = metrics.StrikeoutPosition ?? 0f;

			if (position == 0f)
				position = -FontSizeMm * 0.28f;

			float top = origin.Y + position;
			target.AddRect(new SKRect(origin.X, top, origin.X + width, top + thickness));
		}
	}

	/// <summary>
	/// Приводит путь к простому виду. Обязательно перед любым SKPath.Op:
	/// на путях с наложенными контурами — а путь глифов именно такой —
	/// результат Op неверен, и чаще всего это выражается в заливке дырок букв.
	///
	/// Возвращает новый путь и освобождает исходный. Если Simplify не справился,
	/// возвращает исходный без изменений: лучше неидеальная геометрия, чем
	/// пустой текст.
	/// </summary>
	private static SKPath Simplified(SKPath path)
	{
		if (path == null || path.IsEmpty)
			return path;

		var simplified = path.Simplify();

		if (simplified == null)
			return path;

		simplified.FillType = SKPathFillType.Winding;
		path.Dispose();

		return simplified;
	}

	#endregion
}

/// <summary>
/// Привязки текста к другой фигуре: контур для режима «по контуру» и контейнер
/// для режима «внутри фигуры». Хранится и восстанавливается как единое целое.
///
/// Ссылка и флаг режима — одна логическая величина. Восстанавливать их по
/// отдельности нельзя: без ссылки включённый режим ничего не рисует, а без
/// флага ссылка ни на что не влияет.
/// </summary>
public sealed class TextBindingAction : UndoableActionBase
{
	private readonly struct Binding
	{
		public readonly Shape? PathShape;
		public readonly Shape? ContainerShape;
		public readonly bool Along;
		public readonly bool Inside;

		public Binding(Shape? pathShape, Shape? containerShape, bool along, bool inside)
		{
			PathShape = pathShape;
			ContainerShape = containerShape;
			Along = along;
			Inside = inside;
		}
	}

	private readonly Shape _shape;
	private readonly Binding _before;
	private Binding _after;

	private TextBindingAction(Shape shape, Binding before)
	{
		_shape = shape;
		_before = before;
		_after = Capture(shape);
	}

	/// <summary>
	/// Создаёт шаг истории. before снимается вызывающим кодом до изменения
	/// через CaptureState, after читается здесь из текущего состояния.
	/// </summary>
	public static TextBindingAction Create(Shape shape, object beforeState)
		=> new TextBindingAction(shape, (Binding)beforeState);

	/// <summary>Состояние привязок для последующей передачи в Create.</summary>
	public static object CaptureState(Shape shape) => Capture(shape);

	private static Binding Capture(Shape shape)
	{
		var text = shape as TextShape;

		return new Binding(
			shape.otherShape,
			text?.InsideShape,
			text?.AlongShape ?? false,
			text?.InsideOtherShape ?? false);
	}

	public override string Name => "Text binding";
	public override long MemoryFootprint => 192;

	public override void Undo() => Apply(_before);
	public override void Redo() => Apply(_after);

	private void Apply(Binding b)
	{
		_shape.otherShape = b.PathShape;

		if (_shape is not TextShape text)
			return;

		text.InsideShape = b.ContainerShape;

		// Порядок важен: сеттеры режимов взаимно гасят друг друга, поэтому
		// сначала выключаем оба ненужных, потом включаем нужный. Иначе
		// установка первого флага сбросила бы второй.
		if (!b.Along) text.AlongShape = false;
		if (!b.Inside) text.InsideOtherShape = false;
		if (b.Along) text.AlongShape = true;
		if (b.Inside) text.InsideOtherShape = true;
	}

	/// <summary>Изменилось ли что-нибудь. Пустой шаг в историю не пишется.</summary>
	public bool HasChanges()
	{
		return !ReferenceEquals(_before.PathShape, _after.PathShape)
			|| !ReferenceEquals(_before.ContainerShape, _after.ContainerShape)
			|| _before.Along != _after.Along
			|| _before.Inside != _after.Inside;
	}
}

/// <summary>
/// Обслуживание привязок текста при удалении и замене фигур.
/// </summary>
public static class TextBindingHelper
{
	/// <summary>
	/// Отвязывает текст от удаляемых фигур: ссылка сбрасывается в null,
	/// соответствующий режим выключается.
	///
	/// Вызывать ДО фактического удаления, внутри той же группы истории.
	/// Порядок важен: CompositeUndoableAction откатывает шаги в обратном
	/// порядке, поэтому отвязка, записанная первой, при отмене выполнится
	/// последней — уже после того, как фигуры вернутся на страницу.
	/// </summary>
	public static void DetachFromDeleted(Page page, IReadOnlyList<Shape> deleted, UndoStack? undo)
	{
		if (page == null || deleted == null || deleted.Count == 0)
			return;

		// Удаление группы уносит с собой детей, а ссылаться могли и на них.
		var doomed = new HashSet<Shape>(ReferenceEqualityComparer.Instance);

		foreach (var shape in deleted)
			CollectWithChildren(shape, doomed);

		foreach (var shape in EnumerateAll(page.Shapes))
		{
			// Фигура, которую саму удаляют, отвязывать незачем.
			if (doomed.Contains(shape))
				continue;

			bool touchesPath = shape.otherShape != null && doomed.Contains(shape.otherShape);

			bool touchesContainer =
				shape is TextShape t &&
				t.InsideShape != null &&
				doomed.Contains(t.InsideShape);

			if (!touchesPath && !touchesContainer)
				continue;

			var before = TextBindingAction.CaptureState(shape);

			if (touchesPath)
			{
				shape.otherShape = null;

				// Режим без контура ничего не рисует, поэтому выключаем его,
				// а не оставляем включённым с пустой ссылкой.
				if (shape is TextShape text)
					text.AlongShape = false;
			}

			if (touchesContainer && shape is TextShape textShape)
			{
				textShape.InsideShape = null;
				textShape.InsideOtherShape = false;
			}

			var action = TextBindingAction.Create(shape, before);

			if (action.HasChanges())
				undo?.Push(action);
		}
	}

	/// <summary>
	/// Переназначает привязки со старой фигуры на новую. Используется при
	/// преобразовании в кривые и при булевых операциях, где фигура не исчезает,
	/// а заменяется другой — там режим выключать не нужно.
	/// </summary>
	public static void Retarget(Page page, Shape oldShape, Shape newShape, UndoStack? undo)
	{
		if (page == null || oldShape == null || newShape == null)
			return;

		foreach (var shape in EnumerateAll(page.Shapes))
		{
			bool touchesPath = ReferenceEquals(shape.otherShape, oldShape);

			bool touchesContainer =
				shape is TextShape t && ReferenceEquals(t.InsideShape, oldShape);

			if (!touchesPath && !touchesContainer)
				continue;

			var before = TextBindingAction.CaptureState(shape);

			if (touchesPath)
				shape.otherShape = newShape;

			if (touchesContainer && shape is TextShape textShape)
				textShape.InsideShape = newShape;

			var action = TextBindingAction.Create(shape, before);

			if (action.HasChanges())
				undo?.Push(action);
		}
	}

	private static void CollectWithChildren(Shape? shape, HashSet<Shape> target)
	{
		if (shape == null || !target.Add(shape))
			return;

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				CollectWithChildren(child, target);
		}
	}

	private static IEnumerable<Shape> EnumerateAll(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateAll(group.Children))
					yield return child;
			}
		}
	}
}

/// <summary>
/// Построение и измерение текста с учётом табуляции.
///
/// Табуляция — один символ шириной в четыре пробела текущего шрифта. Не замена
/// на четыре пробела: символ в строке остаётся одним, поэтому каретка
/// перескакивает его целиком, а копирование возвращает именно '\t'.
///
/// Единая точка для всех режимов TextShape: обычный текст, текст по контуру и
/// текст внутри фигуры. Прежде каждый режим измерял и рисовал строку сам, и
/// '\t' попадал в GetTextPath как обычный символ — шрифт рисовал на его месте
/// .notdef, тот самый квадратик.
/// </summary>
public static class TextGlyphs
{
	/// <summary>Ширина табуляции в пробелах.</summary>
	public const int TabSpaces = 4;

	/// <summary>Ширина табуляции для конкретного шрифта.</summary>
	public static float GetTabWidth(SKFont font)
	{
		if (font == null)
			return 0f;

		float space = font.MeasureText(" ");

		// Экзотический шрифт может не сообщить ширину пробела. Тогда берём
		// четверть кегля — так поступает большинство движков вёрстки.
		if (space <= 0f)
			space = font.Size * 0.25f;

		return space * TabSpaces;
	}

	/// <summary>
	/// Продвижение одного символа с учётом межсимвольного интервала.
	/// Табуляция не имеет глифа, но имеет ширину.Toyota 383, Mitsubisy 115
	/// </summary>
	public static float GetCharAdvance(SKFont font, char ch, float characterSpacing)
	{
		float advance = ch == '\t'
			? GetTabWidth(font)
			: font.MeasureText(ch.ToString());

		return advance + characterSpacing;
	}

	/// <summary>
	/// Ширина строки. Текст разбивается на отрезки по табуляциям, отрезки
	/// измеряются шрифтом, табуляции добавляются фиксированной шириной.
	///
	/// Разбиение то же, что в AppendPath, поэтому измеренная ширина всегда
	/// совпадает с нарисованной. Кернинг через границу отрезка теряется, но
	/// теряется одинаково в обоих местах.
	/// </summary>
	public static float Measure(SKFont font, string? text, float characterSpacing = 0f)
	{
		if (font == null || string.IsNullOrEmpty(text))
			return 0f;

		// Быстрый путь для строк без табуляций — их большинство.
		if (text.IndexOf('\t') < 0)
		{
			float w = font.MeasureText(text);

			if (characterSpacing != 0f)
				w += MathF.Max(0, text.Length - 1) * characterSpacing;

			return w;
		}

		float width = 0f;
		float tabWidth = GetTabWidth(font);
		int segmentStart = 0;

		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != '\t')
				continue;

			if (i > segmentStart)
				width += font.MeasureText(text[segmentStart..i]);

			width += tabWidth;
			segmentStart = i + 1;
		}

		if (segmentStart < text.Length)
			width += font.MeasureText(text[segmentStart..]);

		if (characterSpacing != 0f)
			width += MathF.Max(0, text.Length - 1) * characterSpacing;

		return width;
	}

	/// <summary>
	/// Добавляет контуры глифов строки в путь. На месте табуляции ничего не
	/// рисуется, только сдвигается курсор.
	/// </summary>
	public static void AppendPath(
		SKPath target,
		SKFont font,
		string? text,
		SKPoint origin,
		float characterSpacing = 0f)
	{
		if (target == null || font == null || string.IsNullOrEmpty(text))
			return;

		// Без межсимвольного интервала и без табуляций строку можно построить
		// одним вызовом — так сохраняется кернинг шрифта.
		if (characterSpacing == 0f && text.IndexOf('\t') < 0)
		{
			using var linePath = font.GetTextPath(text, origin);

			if (linePath != null && !linePath.IsEmpty)
				target.AddPath(linePath);

			return;
		}

		if (characterSpacing == 0f)
		{
			// Табуляции есть, интервала нет: рисуем отрезками между ними,
			// внутри отрезка кернинг сохраняется.
			float tabWidth = GetTabWidth(font);
			float cursor = origin.X;
			int segmentStart = 0;

			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] != '\t')
					continue;

				if (i > segmentStart)
				{
					string segment = text[segmentStart..i];
					AppendSegment(target, font, segment, cursor, origin.Y);
					cursor += font.MeasureText(segment);
				}

				cursor += tabWidth;
				segmentStart = i + 1;
			}

			if (segmentStart < text.Length)
				AppendSegment(target, font, text[segmentStart..], cursor, origin.Y);

			return;
		}

		// Межсимвольный интервал требует посимвольного размещения.
		float x = origin.X;

		foreach (char ch in text)
		{
			if (ch != '\t')
				AppendSegment(target, font, ch.ToString(), x, origin.Y);

			x += GetCharAdvance(font, ch, characterSpacing);
		}
	}

	private static void AppendSegment(SKPath target, SKFont font, string segment, float x, float y)
	{
		using var path = font.GetTextPath(segment, new SKPoint(x, y));

		if (path != null && !path.IsEmpty)
			target.AddPath(path);
	}
}