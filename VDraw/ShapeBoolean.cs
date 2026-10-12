using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

public enum ShapeBooleanKind
{
	Combine,
	Union,
	Subtract,
	Intersect,
	Exclude
}

public static class ShapeBooleanHelper
{
	public static CurveShape? Union(Page page, IReadOnlyList<Shape> selection)
		=> Apply(page, selection, ShapeBooleanKind.Union);

	public static CurveShape? Subtract(Page page, IReadOnlyList<Shape> selection)
		=> Apply(page, selection, ShapeBooleanKind.Subtract);

	public static CurveShape? Intersect(Page page, IReadOnlyList<Shape> selection)
		=> Apply(page, selection, ShapeBooleanKind.Intersect);

	public static CurveShape? Exclude(Page page, IReadOnlyList<Shape> selection)
		=> Apply(page, selection, ShapeBooleanKind.Exclude);

	public static CurveShape? Apply(Page page, IReadOnlyList<Shape> selection, ShapeBooleanKind kind)
	{
		if (page == null || selection == null)
			return null;

		// Порядок выбора значим для Subtract и определяет стиль результата,
		// поэтому Distinct без сортировки — он сохраняет порядок.
		var operands = selection
			.Where(s => s != null && s.IsVisible)
			.Distinct()
			.ToList();

		if (operands.Count < 2)
			return null;

		// Subtract, Intersect и Exclude определены строго для двух фигур.
		if (RequiresExactlyTwoOperands(kind))
		{
			if (operands.Count != 2)
				return null;
		}

		CurveShape? result;

		if (kind == ShapeBooleanKind.Combine)
		{
			// Combine геометрию не пересчитывает и SKPath.Op не использует.
			result = BuildCombinedCurveShape(operands);
		}
		else
		{
			using var resultPath = BuildBooleanResultPath(operands, kind);
			if (resultPath == null || resultPath.IsEmpty)
				return null;

			result = ConvertWorldPathToCurveShape(resultPath, operands[0], kind);
		}

		if (result == null || result.Nodes.Count == 0)
			return null;

		// Первый операнд задаёт не только стиль, но и тип результата: если им
		// была картинка, результат обязан остаться картинкой с теми же
		// пикселями. Если картинка была вторым операндом, результат — обычная
		// кривая со стилем первого, и пиксели уходят вместе со вторым операндом.
		result = PreserveImageFromFirstOperand(result, operands[0]);

		var undo = page.doc?.Undo;
		undo?.BeginGroup(GetOperationName(kind));

		try
		{
			RemoveShapesFromPage(page, operands);

			result.Layer = operands.Min(s => s.Layer);
			page.AddShape(result);
			page.SortLayers();
			page.doc.need_save = true;
		}
		finally
		{
			undo?.CommitGroup();
		}

		return result;
	}

	private static bool RequiresExactlyTwoOperands(ShapeBooleanKind kind)
	=> kind is ShapeBooleanKind.Subtract
			or ShapeBooleanKind.Intersect
			or ShapeBooleanKind.Exclude;

	private static string GetOperationName(ShapeBooleanKind kind) => kind switch
	{
		ShapeBooleanKind.Combine => "Combine",
		ShapeBooleanKind.Union => "Union",
		ShapeBooleanKind.Subtract => "Subtract",
		ShapeBooleanKind.Intersect => "Intersect",
		ShapeBooleanKind.Exclude => "Exclude",
		_ => "Boolean"
	};

	public static CurveShape? Combine(Page page, IReadOnlyList<Shape> selection)
	{
		if (page == null || selection == null)
			return null;

		var operands = selection
			.Where(s => s != null && s.IsVisible)
			.Distinct()
			.ToList();

		if (operands.Count < 2)
			return null;

		var result = BuildCombinedCurveShape(operands);
		if (result == null || result.Nodes.Count == 0)
			return null;

		result = PreserveImageFromFirstOperand(result, operands[0]);

		RemoveShapesFromPage(page, operands);

		result.Layer = operands.Min(s => s.Layer);
		page.AddShape(result);
		page.SortLayers();
		page.doc.need_save = true;

		return result;
	}

	private static CurveShape? BuildCombinedCurveShape(IReadOnlyList<Shape> operands)
	{
		if (operands.Count == 0)
			return null;

		var styleSource = operands[0];

		var result = new CurveShape
		{
			Name = "Combine",
			Fill = styleSource.Fill.Clone(),
			Stroke = styleSource.Stroke.Clone(),
			IsVisible = styleSource.IsVisible,
			IsLocked = false,
			IsFocused = false,
			LocalTransform = SKMatrix.CreateIdentity(),

			// Наложенные контуры должны давать дырки — это смысл Combine.
			FillType = SKPathFillType.EvenOdd
		};

		bool anyAdded = false;

		foreach (var shape in operands)
		{
			CurveShape? sourceCurve = shape as CurveShape ?? shape.ToCurveShape();
			if (sourceCurve == null || sourceCurve.Nodes.Count == 0)
				continue;

			int startIndex = result.Nodes.Count;

			foreach (var srcNode in sourceCurve.Nodes)
			{
				var newNode = srcNode.Clone();

				// Узлы результата не должны быть выделены: иначе следующая же
				// операция над узлами примет их за выбор пользователя.
				newNode.Selected = false;
				newNode.Focused = false;

				newNode.Position = shape.LocalToWorld(srcNode.Position);

				newNode.ControlPrev = srcNode.HasPrevControl
					? shape.LocalToWorld(srcNode.ControlPrev)
					: new SKPoint(float.NaN, float.NaN);

				newNode.ControlNext = srcNode.HasNextControl
					? shape.LocalToWorld(srcNode.ControlNext)
					: new SKPoint(float.NaN, float.NaN);

				// Флаги NewContour / BreakAfter / CloseAfter сохраняются:
				// внутри одной фигуры может быть несколько контуров,
				// и эта структура должна дойти до результата без изменений.

				result.Nodes.Add(newNode);
				anyAdded = true;
			}

			int endIndex = result.Nodes.Count - 1;
			if (endIndex < startIndex)
				continue;

			// Первая точка фигуры обязана начинать контур, даже если
			// у исходной кривой флаг был сбит.
			result.Nodes[startIndex].NewContour = true;

			// Разрыв перед следующей фигурой, иначе BuildLocalPath проведёт
			// сегмент к первому узлу следующей фигуры.
			if (!result.Nodes[endIndex].CloseAfter)
				result.Nodes[endIndex].BreakAfter = true;
		}

		return anyAdded ? result : null;
	}

	/// <summary>
	/// Если базовым операндом была картинка, переносит результат операции в
	/// ImageShape с теми же пикселями.
	///
	/// Узлы результата приходят в мировых координатах, а картинка кладёт
	/// битмап в ImageLocalRect — прямоугольник в ЛОКАЛЬНЫХ координатах, потом
	/// умноженный на трансформацию фигуры. Поэтому здесь, в отличие от
	/// ApplyStyleFromSource, трансформация не обнуляется: результату ставится
	/// мировая матрица исходной картинки, а узлы переводятся в её локальную
	/// систему обратной матрицей. Обнуление матрицы с пересчётом ImageLocalRect
	/// не годится: повёрнутую картинку прямоугольником не описать.
	/// </summary>
	private static CurveShape PreserveImageFromFirstOperand(CurveShape geometry, Shape styleSource)
	{
		if (styleSource is not ImageShape sourceImage)
			return geometry;

		// Clone несёт пиксели, тайлы, коррекции и стиль. Узлы от него тоже
		// приходят — это рамка исходной картинки, её заменяем геометрией результата.
		var image = (ImageShape)sourceImage.Clone();
		image.Nodes.Clear();

		var world = sourceImage.GetWorldTransform();
		if (!world.TryInvert(out var worldToLocal))
			worldToLocal = SKMatrix.CreateIdentity();

		foreach (var node in geometry.Nodes)
		{
			var copy = node.Clone();

			copy.Position = worldToLocal.MapPoint(node.Position);

			copy.ControlPrev = node.HasPrevControl
				? worldToLocal.MapPoint(node.ControlPrev)
				: new SKPoint(float.NaN, float.NaN);

			copy.ControlNext = node.HasNextControl
				? worldToLocal.MapPoint(node.ControlNext)
				: new SKPoint(float.NaN, float.NaN);

			copy.Selected = false;
			copy.Focused = false;

			image.Nodes.Add(copy);
		}

		if (image.Nodes.Count == 0)
			return geometry;

		// Результат кладётся прямо на страницу, поэтому локальная матрица
		// совпадает с мировой.
		image.LocalTransform = world;
		image.Name = geometry.Name;
		image.FillType = geometry.FillType;
		image.IsFocused = false;
		image.IsLocked = false;

		return image;
	}

	private static SKPath? BuildBooleanResultPath(IReadOnlyList<Shape> operands, ShapeBooleanKind kind)
	{
		var paths = new List<SKPath>();

		foreach (var shape in operands)
		{
			var p = BuildOperandWorldPath(shape);
			if (p != null && !p.IsEmpty)
				paths.Add(p);
		}

		if (paths.Count < 2)
		{
			foreach (var p in paths)
				p.Dispose();

			return null;
		}

		SKPathOp op = kind switch
		{
			ShapeBooleanKind.Union => SKPathOp.Union,
			ShapeBooleanKind.Subtract => SKPathOp.Difference,
			ShapeBooleanKind.Intersect => SKPathOp.Intersect,
			ShapeBooleanKind.Exclude => SKPathOp.Xor,
			_ => SKPathOp.Union
		};

		try
		{
			// Первая выбранная фигура — база. Для Subtract это A в A минус B.
			SKPath? acc = new SKPath(paths[0]);

			// Union определён для любого числа фигур, остальные операции
			// принимают ровно два операнда (проверено в Apply).
			int count = kind == ShapeBooleanKind.Union ? paths.Count : 2;

			for (int i = 1; i < count && i < paths.Count; i++)
			{
				var merged = acc.Op(paths[i], op);

				if (merged == null)
				{
					acc.Dispose();
					return null;
				}

				acc.Dispose();
				acc = merged;
			}

			return acc;
		}
		finally
		{
			foreach (var p in paths)
				p.Dispose();
		}
	}

	private static SKPath? BuildOperandWorldPath(Shape shape)
	{
		Shape source = shape;

		// Для всех не-Curve фигур переходим в каноническую кривую.
		if (shape is not CurveShape)
		{
			var curve = shape.ToCurveShape();
			if (curve != null)
				source = curve;
		}

		var path = source.BuildWorldPath();
		if (path == null)
			return null;

		// FillType остаётся собственным: он определяет, что у фигуры считается
		// внутренностью. Бублик из двух вложенных контуров с EvenOdd при
		// принудительном Winding потерял бы дырку.
		//
		// Simplify переводит путь в канонический Winding с тем же покрытием
		// и убирает самопересечения — без этого SKPath.Op даёт неверный
		// результат на самопересекающихся контурах.
		var simplified = path.Simplify();

		if (simplified == null || simplified.IsEmpty)
			return path;

		path.Dispose();
		return simplified;
	}

	private static SKPathFillType GetResultFillType(ShapeBooleanKind kind)
	{
		return kind == ShapeBooleanKind.Exclude
			? SKPathFillType.EvenOdd   // XOR
			: SKPathFillType.Winding;  // Union/Subtract/Intersect
	}

	private static IEnumerable<Shape> EnumerateLeafShapes(Shape shape)
	{
		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
			{
				foreach (var leaf in EnumerateLeafShapes(child))
					yield return leaf;
			}

			yield break;
		}

		yield return shape;
	}

	/// <summary>
	/// Переносит стиль первой выбранной фигуры на результат.
	///
	/// LocalTransform принудительно Identity: узлы результата уже в мировых
	/// координатах, и наследование трансформации исходной фигуры применило бы
	/// её второй раз.
	/// </summary>
	private static void ApplyStyleFromSource(CurveShape target, Shape styleSource, string name)
	{
		target.Name = name;
		target.Stroke = styleSource.Stroke.Clone();
		target.Fill = styleSource.Fill.Clone();
		target.IsVisible = styleSource.IsVisible;
		target.IsLocked = false;
		target.IsFocused = false;
		target.LocalTransform = SKMatrix.CreateIdentity();

		// Заливка наследуется как есть. Прежний код включал её принудительно,
		// и результат оказывался залитым даже когда у исходной фигуры
		// заливки не было.
	}


	private static CurveShape? ConvertWorldPathToCurveShape(SKPath worldPath, Shape styleSource, ShapeBooleanKind kind)
	{
		string name = GetOperationName(kind);

		if (TryBuildEllipseFromPath(worldPath, out var ellipseCurve))
		{
			ApplyStyleFromSource(ellipseCurve, styleSource, name);
			return ellipseCurve;
		}

		// Общий путь: несколько контуров, дырки и прочее.
		var contours = BuildContoursFromPath(worldPath);
		if (contours.Count == 0)
			return null;

		var curve = new CurveShape();
		ApplyStyleFromSource(curve, styleSource, name);

		foreach (var contour in contours)
		{
			if (contour.Count == 0)
				continue;

			int startIndex = curve.Nodes.Count;

			foreach (var node in contour)
			{
				node.Selected = false;
				node.Focused = false;
				curve.Nodes.Add(node);
			}

			int endIndex = curve.Nodes.Count - 1;

			// Результат SKPath.Op состоит только из замкнутых контуров.
			curve.Nodes[startIndex].NewContour = true;
			curve.Nodes[endIndex].CloseAfter = true;
		}

		return curve.Nodes.Count > 0 ? curve : null;
	}


	private static bool TryBuildEllipseFromPath(SKPath path, out CurveShape curve)
	{
		curve = null!;

		// Эллипс должен быть одним контуром без дырок
		var flat = FlattenPath(path, 0.5f);
		var contours = SplitContours(flat);

		if (contours.Count != 1)
			return false;

		var pts = contours[0];
		if (pts.Count < 8) // слишком грубый контур — не эллипс
			return false;

		var bounds = path.Bounds;
		if (bounds.IsEmpty || bounds.Width <= 0.0001f || bounds.Height <= 0.0001f)
			return false;

		var center = new SKPoint(bounds.MidX, bounds.MidY);
		float rx = bounds.Width * 0.5f;
		float ry = bounds.Height * 0.5f;

		const float relTolerance = 0.015f; // 1.5% допуска

		foreach (var p in pts)
		{
			float dx = p.X - center.X;
			float dy = p.Y - center.Y;

			// Нормализованное расстояние: для идеального эллипса == 1
			float normalized = MathF.Sqrt(
				(dx * dx) / (rx * rx) +
				(dy * dy) / (ry * ry));

			if (MathF.Abs(normalized - 1f) > relTolerance)
				return false;
		}

		curve = Shape.BuildEllipseCurve(center, rx, ry);
		return true;
	}

	private readonly struct PathSegment
	{
		public readonly SKPoint Start;
		public readonly SKPoint? C1;
		public readonly SKPoint? C2;
		public readonly SKPoint End;

		public PathSegment(SKPoint start, SKPoint? c1, SKPoint? c2, SKPoint end)
		{
			Start = start;
			C1 = c1;
			C2 = c2;
			End = end;
		}
	}

	private static List<List<CurveNode>> BuildContoursFromPath(SKPath path)
	{
		var contours = new List<List<CurveNode>>();
		List<PathSegment>? currentSegs = null;
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
						contours.Add(BuildNodesFromSegments(currentSegs));

					currentSegs = new List<PathSegment>();
					currentStart = pts[0];
					currentPoint = pts[0];
					break;

				case SKPathVerb.Line:
					currentSegs?.Add(new PathSegment(currentPoint, null, null, pts[1]));
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

						currentSegs?.Add(new PathSegment(currentPoint, c1, c2, p2));
						currentPoint = p2;
						break;
					}

				case SKPathVerb.Conic:
					{
						float w = iter.ConicWeight();
						var p0 = pts[0];
						var p1 = pts[1];
						var p2 = pts[2];

						var quads = ConicToQuads(p0, p1, p2, w);
						foreach (var q in quads)
						{
							var c1 = new SKPoint(
								q.p0.X + 2f / 3f * (q.p1.X - q.p0.X),
								q.p0.Y + 2f / 3f * (q.p1.Y - q.p0.Y));

							var c2 = new SKPoint(
								q.p2.X + 2f / 3f * (q.p1.X - q.p2.X),
								q.p2.Y + 2f / 3f * (q.p1.Y - q.p2.Y));

							currentSegs?.Add(new PathSegment(currentPoint, c1, c2, q.p2));
							currentPoint = q.p2;
						}
						break;
					}

				case SKPathVerb.Cubic:
					currentSegs?.Add(new PathSegment(currentPoint, pts[1], pts[2], pts[3]));
					currentPoint = pts[3];
					break;

				case SKPathVerb.Close:
					if (currentSegs != null && currentSegs.Count > 0 &&
						Distance(currentPoint, currentStart) > 0.001f)
					{
						currentSegs.Add(new PathSegment(currentPoint, null, null, currentStart));
					}
					currentPoint = currentStart;
					break;

				case SKPathVerb.Done:
					if (currentSegs != null && currentSegs.Count > 0)
						contours.Add(BuildNodesFromSegments(currentSegs));
					return contours;
			}
		}
	}

	private static List<(SKPoint p0, SKPoint p1, SKPoint p2)> ConicToQuads(SKPoint p0, SKPoint p1, SKPoint p2, float w)
	{
		// Простая аппроксимация одной дугой-quad — достаточно для скруглений прямоугольников.
		return new List<(SKPoint, SKPoint, SKPoint)> { (p0, p1, p2) };
	}

	private static List<CurveNode> BuildNodesFromSegments(List<PathSegment> segs)
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
			Distance(nodes[0].Position, nodes[^1].Position) < 0.001f)
		{
			// Последний узел дублирует первый (замкнутый контур через Close) — сливаем контролы и убираем дубликат.
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

	private static List<SKPoint?> FlattenPath(SKPath path, float tolerance)
	{
		var result = new List<SKPoint?>();
		using var iter = path.CreateRawIterator();

		SKPoint[] pts = new SKPoint[4];

		while (true)
		{
			var verb = iter.Next(pts);
			switch (verb)
			{
				case SKPathVerb.Move:
					result.Add(null);
					result.Add(pts[0]);
					break;

				case SKPathVerb.Line:
					result.Add(pts[1]);
					break;

				case SKPathVerb.Quad:
					AppendQuadratic(result, pts[0], pts[1], pts[2], tolerance);
					break;

				case SKPathVerb.Conic:
					result.Add(pts[2]);
					break;

				case SKPathVerb.Cubic:
					AppendCubic(result, pts[0], pts[1], pts[2], pts[3], tolerance);
					break;

				case SKPathVerb.Close:
					result.Add(null);
					break;

				case SKPathVerb.Done:
					return result;
			}
		}
	}

	private static List<List<SKPoint>> SplitContours(List<SKPoint?> flat)
	{
		var contours = new List<List<SKPoint>>();
		List<SKPoint>? current = null;

		foreach (var p in flat)
		{
			if (p == null)
			{
				if (current != null && current.Count > 0)
					contours.Add(RemoveNearDuplicatesAndTrailingClosure(current));
				current = new List<SKPoint>();
				continue;
			}

			current ??= new List<SKPoint>();
			current.Add(p.Value);
		}

		if (current != null && current.Count > 0)
			contours.Add(RemoveNearDuplicatesAndTrailingClosure(current));

		return contours.Where(c => c.Count > 0).ToList();
	}

	private static List<SKPoint> RemoveNearDuplicatesAndTrailingClosure(List<SKPoint> pts)
	{
		const float eps = 0.001f;
		var result = new List<SKPoint>();

		foreach (var p in pts)
		{
			if (result.Count == 0 || Distance(result[^1], p) > eps)
				result.Add(p);
		}

		if (result.Count > 1 && Distance(result[0], result[^1]) <= eps)
			result.RemoveAt(result.Count - 1);

		return result;
	}

	private static void AppendQuadratic(List<SKPoint?> dst, SKPoint p0, SKPoint p1, SKPoint p2, float tolerance)
	{
		const int steps = 16;
		for (int i = 1; i <= steps; i++)
		{
			float t = i / (float)steps;
			float mt = 1f - t;
			dst.Add(new SKPoint(
				mt * mt * p0.X + 2f * mt * t * p1.X + t * t * p2.X,
				mt * mt * p0.Y + 2f * mt * t * p1.Y + t * t * p2.Y));
		}
	}

	private static void AppendCubic(List<SKPoint?> dst, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tolerance)
	{
		const int steps = 24;
		for (int i = 1; i <= steps; i++)
		{
			float t = i / (float)steps;
			float mt = 1f - t;
			float mt2 = mt * mt;
			float mt3 = mt2 * mt;
			float t2 = t * t;
			float t3 = t2 * t;

			dst.Add(new SKPoint(
				mt3 * p0.X + 3f * mt2 * t * p1.X + 3f * mt * t2 * p2.X + t3 * p3.X,
				mt3 * p0.Y + 3f * mt2 * t * p1.Y + 3f * mt * t2 * p2.Y + t3 * p3.Y));
		}
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	private static void RemoveShapesFromPage(Page page, IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes.Distinct().ToList())
			RemoveShapeRecursive(page, shape);
	}

	private static void RemoveShapeRecursive(Page page, Shape shape)
	{
		if (shape.Parent is GroupShape parentGroup)
			parentGroup.RemoveChild(shape);
		else
			page.RemoveShape(shape);
	}
}