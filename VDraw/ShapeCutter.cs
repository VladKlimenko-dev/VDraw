using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

/// <summary>
/// Общая часть ножниц: сбор линии разреза, пунктирный предпросмотр
/// и применение разреза с записью в историю.
///
/// Отличие двух инструментов сводится к одному: как набирается линия.
/// Прямые ножницы берут две точки, свободные — след мыши.
/// </summary>
public abstract class ScissorsToolBase : IEditorTool
{
	/// <summary>Цвет линии разреза — бордовый, как заказано.</summary>
	private static readonly SKColor CutColor = new SKColor(128, 0, 32);

	protected readonly List<SKPoint> Points = new();
	protected bool IsDrawing;
	/// <summary>
	/// Допуск прореживания в единицах документа. Пересчитывается из экранных
	/// пикселей, поэтому степень сглаживания одинакова на любом масштабе:
	/// на приближенном холсте пользователь ведёт точнее, и допуск в
	/// миллиметрах убирал бы осмысленные подробности.
	/// </summary>
	protected float SmoothTolerance { get; private set; } = 0.4f;

	public abstract ShapeToolKind Kind { get; }

	public abstract void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button);
	public abstract void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint);
	public abstract void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button);

	public virtual void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData != Keys.Escape)
			return;

		Cancel(view);
	}

	protected void Cancel(MainView view)
	{
		Points.Clear();
		IsDrawing = false;

		view.Cursor = Cursors.Cross;
		view.Invalidate();
	}

	/// <summary>
	/// Пунктирная линия разреза. Рисуется в экранных координатах, поэтому
	/// толщина и шаг пунктира не зависят от масштаба холста.
	/// </summary>
	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (Points.Count < 2)
			return;

		using var path = new SKPath();

		// Предпросмотр строится тем же кодом, что и лезвие, только без
		// продления: пользователь должен видеть именно то, что получится.
		var preview = ShapeCutter.BuildPreviewPath(Points, SmoothTolerance);

		if (preview == null)
			return;

		using (preview)
		{
			// Путь в мировых координатах, оверлей рисуется в экранных.
			var matrix = SKMatrix.CreateScale(1f, 1f);
			preview.Transform(GetWorldToScreenMatrix(view), path);
		}

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = CutColor,
			PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0f)
		};

		canvas.DrawPath(path, paint);
	}

	/// <summary>
	/// Матрица перевода мировых координат в экранные. У вида уже есть
	/// PageToScreen для точек, но для пути нужна именно матрица.
	/// </summary>
	private static SKMatrix GetWorldToScreenMatrix(MainView view)
	{
		var origin = view.PageToScreen(SKPoint.Empty);
		var unit = view.PageToScreen(new SKPoint(1f, 1f));

		float scale = unit.X - origin.X;

		return Geometry.Multiply(
			SKMatrix.CreateTranslation(origin.X, origin.Y),
			SKMatrix.CreateScale(scale, unit.Y - origin.Y));
	}

	/// <summary>
	/// Применяет разрез. Цели: выделенные фигуры, а если выделения нет —
	/// все фигуры страницы, через которые прошла линия.
	/// </summary>
	protected void ApplyCut(MainView view, EditorInteractionContext ctx)
	{
		var page = view.CurrentPage;

		if (page == null || Points.Count < 2)
		{
			Cancel(view);
			return;
		}

		var targets = CollectTargets(view, ctx);

		if (targets.Count == 0)
		{
			Cancel(view);
			return;
		}

		var undo = page.doc?.Undo;

		// Удаление исходных фигур, отвязка от них и добавление частей —
		// один шаг истории.
		undo?.BeginGroup("Cut");

		int cutCount;

		try
		{
			cutCount = ShapeCutter.Cut(page, targets, Points, Settings.cap_scissors, undo);
		}
		finally
		{
			undo?.CommitGroup();
		}

		if (cutCount > 0)
		{
			page.doc.need_save = true;

			// Прежние фигуры исчезли, выделение указывает на них.
			SelectionHelper.Clear(ctx);
			view.RaiseSelectionChanged();
			view.mainForm?.RebuildPropertyPanel();
		}

		Cancel(view);
		view.SetTool(ShapeToolKind.Select);
	}


	/// <summary>
	/// Фигуры под линией разреза. Выделение задаёт область действия;
	/// без выделения режется всё, через что прошла линия.
	/// </summary>
	private List<Shape> CollectTargets(MainView view, EditorInteractionContext ctx)
	{
		var result = new List<Shape>();
		var page = view.CurrentPage;

		if (page == null)
			return result;

		// Выделение — это уже указание, что резать. Проверять пересечение здесь
		// нельзя: ShapeCutter продлевает линию за габариты и работает ей как
		// бесконечной прямой, а фильтр видит только нарисованный отрезок.
		// Фигуры, которых продлённое лезвие не разделит, отсеет сам резак.
		if (ctx.Selection.Count > 0)
		{
			foreach (var shape in ctx.Selection)
			{
				if (shape == null || !shape.IsVisible || shape.IsLocked || shape is GuideLine)
					continue;

				result.Add(shape);
			}

			return result;
		}

		// Без выделения фильтр обязателен, иначе бесконечная прямая разрежет
		// половину страницы.
		foreach (var shape in page.Shapes)
		{
			if (shape == null || !shape.IsVisible || shape.IsLocked || shape is GuideLine)
				continue;

			if (shape is GroupShape group)
			{
				if (AnyChildCrossed(group, Points))
					result.Add(shape);

				continue;
			}

			if (ShapeCutter.CutCrossesShape(shape, Points))
				result.Add(shape);
		}

		return result;
	}

	private static bool AnyChildCrossed(GroupShape group, IReadOnlyList<SKPoint> cut)
	{
		foreach (var child in group.Children)
		{
			if (child is GroupShape nested)
			{
				if (AnyChildCrossed(nested, cut))
					return true;

				continue;
			}

			if (ShapeCutter.CutCrossesShape(child, cut))
				return true;
		}

		return false;
	}
	protected void UpdateSmoothTolerance(MainView view)
	{
		SmoothTolerance = MathF.Max(0.05f, view.ScreenPixelsToPageUnits(2f));
	}
}

/// <summary>Ножницы по прямой: две точки.</summary>
public sealed class ScissorsLineTool : ScissorsToolBase
{
	public override ShapeToolKind Kind => ShapeToolKind.ScissorsLine;

	public override void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button == MouseButtons.Right)
		{
			Cancel(view);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		UpdateSmoothTolerance(view);

		Points.Clear();
		Points.Add(pagePoint);
		Points.Add(pagePoint);

		IsDrawing = true;
		view.Invalidate();
	}

	public override void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!IsDrawing || Points.Count < 2)
			return;

		// Shift ограничивает разрез горизонталью, вертикалью или диагональю —
		// то же поведение, что у остальных инструментов рисования.
		Points[1] = ctx.ShiftPressed ? ConstrainToAxis(Points[0], pagePoint) : pagePoint;

		view.Invalidate();
	}

	public override void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || !IsDrawing)
			return;

		Points[1] = ctx.ShiftPressed ? ConstrainToAxis(Points[0], pagePoint) : pagePoint;

		if (Geometry.Distance(Points[0], Points[1]) < 0.01f)
		{
			Cancel(view);
			return;
		}

		ApplyCut(view, ctx);
	}

	private static SKPoint ConstrainToAxis(SKPoint origin, SKPoint target)
	{
		float dx = target.X - origin.X;
		float dy = target.Y - origin.Y;

		float adx = MathF.Abs(dx);
		float ady = MathF.Abs(dy);

		// Ближе к диагонали, чем к оси — фиксируем сорок пять градусов.
		if (MathF.Abs(adx - ady) < MathF.Max(adx, ady) * 0.3f)
		{
			float d = MathF.Max(adx, ady);
			return new SKPoint(origin.X + MathF.Sign(dx) * d, origin.Y + MathF.Sign(dy) * d);
		}

		return adx > ady
			? new SKPoint(target.X, origin.Y)
			: new SKPoint(origin.X, target.Y);
	}
}

/// <summary>Свободные ножницы: след мыши от нажатия до отпускания.</summary>
public sealed class ScissorsCurveTool : ScissorsToolBase
{
	/// <summary>
	/// Минимальный шаг между точками следа в единицах документа.
	///
	/// Без прореживания за секунду набирается несколько сотен точек: лезвие
	/// становится тяжёлым, а булевы операции — заметно медленнее. Порог
	/// подобран так, чтобы на глаз линия оставалась плавной.
	/// </summary>
	private const float MinPointDistance = 0.3f;

	public override ShapeToolKind Kind => ShapeToolKind.ScissorsCurve;

	public override void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button == MouseButtons.Right)
		{
			Cancel(view);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		UpdateSmoothTolerance(view);

		Points.Clear();
		Points.Add(pagePoint);

		IsDrawing = true;
		view.Invalidate();
	}

	public override void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!IsDrawing)
			return;

		if (Points.Count > 0 && Geometry.Distance(Points[^1], pagePoint) < MinPointDistance)
			return;

		Points.Add(pagePoint);
		view.Invalidate();
	}

	public override void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || !IsDrawing)
			return;

		if (Points.Count == 0 || Geometry.Distance(Points[^1], pagePoint) >= 0.01f)
			Points.Add(pagePoint);

		if (Points.Count < 2)
		{
			Cancel(view);
			return;
		}

		ApplyCut(view, ctx);
	}
}

/// <summary>
/// Разрезание фигур произвольной линией.
///
/// Разрез — открытая линия, а фигура — область. Напрямую булевой операцией их
/// не свести: у линии нет площади. Поэтому линия превращается в «лезвие» —
/// очень тонкую замкнутую ленту вдоль неё, — и вычитается из фигуры. Результат
/// распадается на несвязные контуры, которые и есть части.
///
/// Лента строится обводкой линии через SKPaint.GetFillPath: Skia сама
/// правильно обрабатывает стыки и торцы, чего ручное построение параллельных
/// отрезков не даёт — на изломах они самопересекаются.
///
/// Альтернатива с построением двух полуплоскостей (продлить линию за габариты
/// и замкнуть обходом по углам прямоугольника) даёт математически более чистый
/// результат, но требует обхода углов и ломается на самопересекающихся линиях
/// разреза, которые пользователь легко нарисует от руки.
/// </summary>
public static class ShapeCutter
{
	/// <summary>Один сегмент контура с сохранённым типом и контрольными точками.</summary>
	private readonly struct CutSegment
	{
		public readonly SKPathVerb Verb;
		public readonly SKPoint P0;
		public readonly SKPoint P1;
		public readonly SKPoint P2;
		public readonly SKPoint P3;
		public readonly float Weight;

		private CutSegment(SKPathVerb verb, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float weight)
		{
			Verb = verb;
			P0 = p0;
			P1 = p1;
			P2 = p2;
			P3 = p3;
			Weight = weight;
		}

		public static CutSegment Line(SKPoint a, SKPoint b)
			=> new CutSegment(SKPathVerb.Line, a, a, b, b, 0f);

		public static CutSegment Quad(SKPoint a, SKPoint c, SKPoint b)
			=> new CutSegment(SKPathVerb.Quad, a, c, c, b, 0f);

		public static CutSegment Conic(SKPoint a, SKPoint c, SKPoint b, float w)
			=> new CutSegment(SKPathVerb.Conic, a, c, c, b, w);

		public static CutSegment Cubic(SKPoint a, SKPoint c1, SKPoint c2, SKPoint b)
			=> new CutSegment(SKPathVerb.Cubic, a, c1, c2, b, 0f);
	}

	/// <summary>
	/// Полуширина лезвия относительно размера фигуры.
	///
	/// Абсолютная величина не годится: булевы операции Skia сравнивают
	/// с относительным допуском, и лента шириной 0.01 мм при длине в несколько
	/// сотен миллиметров местами вырождается. Пропорция держит отношение
	/// длины к ширине постоянным.
	/// </summary>
	private const float BladeWidthRatio = 0.0002f;

	/// <summary>
	/// Нижняя граница полуширины. Нужна для очень мелких фигур: у объекта
	/// в доли миллиметра пропорциональная ширина ушла бы в машинный ноль.
	/// </summary>
	private const float MinBladeHalfWidth = 0.002f;

	/// <summary>Полуширина лезвия для фигуры с такими габаритами.</summary>
	private static float GetBladeHalfWidth(SKRect bounds)
	{
		float diagonal = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
		return MathF.Max(MinBladeHalfWidth, diagonal * BladeWidthRatio);
	}


	/// <summary>Шаг обхода незамкнутого контура при поиске пересечений.</summary>
	private const float OpenScanStep = 0.25f;

	#region Точка входа

	public static int Cut(
		Page page,
		IReadOnlyList<Shape> targets,
		IReadOnlyList<SKPoint> cutWorld,
		bool capScissors,
		UndoStack? undo = null,
		float smoothTolerance = 0.4f)
	{

		if (page == null || targets == null || targets.Count == 0)
			return 0;

		if (cutWorld == null || cutWorld.Count < 2)
			return 0;

		// Группы разбираются заранее: резать нужно детей, а сама группа
		// после этого теряет смысл.
		var flat = new List<Shape>();
		var groupsToRemove = new List<GroupShape>();

		foreach (var shape in targets)
			CollectCuttable(shape, flat, groupsToRemove);

		if (flat.Count == 0)
			return 0;

		var unionBounds = SKRect.Empty;

		foreach (var shape in flat)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				unionBounds = unionBounds.IsEmpty ? b : Geometry.Union(unionBounds, b);
		}

		if (unionBounds.IsEmpty)
			return 0;

		int cutCount = 0;

		var removed = new List<Shape>();
		var added = new List<Shape>();
		var orphans = new List<Shape>();

		// Соответствие «что разрезали» — «на что распалось». Плоского
		// списка added для перепривязки мало: искать замену нужно среди
		// кусков ИМЕННО той фигуры, к которой шла привязка, иначе размер
		// перескочил бы на случайно оказавшийся рядом обломок соседней.
		var replacements = new List<(Shape Source, List<Shape> Pieces)>();

		var groupChildren = new HashSet<Shape>(ReferenceEqualityComparer.Instance);

		foreach (var group in groupsToRemove)
		{
			foreach (var child in group.Children)
				groupChildren.Add(child);
		}

		foreach (var shape in flat)
		{
			var pieces = CutShape(shape, cutWorld, capScissors, smoothTolerance);

			if (pieces == null || pieces.Count < 2)
			{
				if (groupChildren.Contains(shape))
					orphans.Add(shape);

				continue;
			}

			if (!groupChildren.Contains(shape))
				removed.Add(shape);

			added.AddRange(pieces);
			replacements.Add((shape, pieces));
			cutCount++;
		}

		if (cutCount == 0)
			return 0;

		foreach (var group in groupsToRemove)
		{
			if (!removed.Contains(group))
				removed.Add(group);
		}

		// Целые дети разобранных групп возвращаются на страницу.
		foreach (var shape in orphans)
			shape.SetParent(null);

		if (orphans.Count > 0)
			page.AddShapes(orphans);

		page.AddShapes(added);

		// Перепривязка ДО удаления и до отвязки: мировая точка привязки
		// вычисляется из геометрии исходной фигуры, а после её удаления
		// восстанавливать было бы уже не из чего.
		foreach (var (source, pieces) in replacements)
			MeasurementAnchorRetargeting.Retarget(page, source, pieces, undo);

		// Отвязка идёт ЗДЕСЬ, а не в инструменте: только сейчас известно,
		// какие фигуры действительно исчезают. Прежняя версия отвязывала
		// текст и связи от всех целей, включая уцелевшие.
		//
		// И до удаления: DetachFromDeleted записывает шаг истории, который
		// при отмене должен выполниться ПОСЛЕ возврата фигур на страницу,
		// а группа откатывает шаги в обратном порядке.
		TextBindingHelper.DetachFromDeleted(page, removed, undo);

		page.RemoveShapes(removed);
		page.SortLayers();

		return cutCount;
	}

	/// <summary>
	/// Линия разреза в двух согласованных представлениях.
	///
	/// Path — сглаженный и продлённый путь с кубическими сегментами, из него
	/// строится лезвие. Polyline — его же плотная ломаная аппроксимация для
	/// проверок пересечения и расстояния.
	/// </summary>
	private sealed class CutGeometry : IDisposable
	{
		public required SKPath Path { get; init; }
		public required List<SKPoint> Polyline { get; init; }

		public void Dispose() => Path.Dispose();
	}

	/// <summary>
	/// Раскладывает группы на детей. Вложенные группы разбираются рекурсивно.
	/// </summary>
	private static void CollectCuttable(Shape? shape, List<Shape> flat, List<GroupShape> groups)
	{
		if (shape == null || !shape.IsVisible || shape.IsLocked || shape is GuideLine)
			return;

		if (shape is GroupShape group)
		{
			groups.Add(group);

			foreach (var child in group.Children)
				CollectCuttable(child, flat, groups);

			return;
		}

		if (!flat.Contains(shape))
			flat.Add(shape);
	}

	/// <summary>
	/// Прореживание по Рамеру-Дугласу-Пекеру: выбрасывает точки, отстоящие
	/// от хорды между сохранёнными соседями меньше чем на допуск.
	///
	/// В отличие от прореживания по шагу, сохраняет форму: на прямом участке
	/// остаются только концы, на повороте — все точки, которые его задают.
	/// </summary>
	private static List<SKPoint> SimplifyPolyline(IReadOnlyList<SKPoint> points, float tolerance)
	{
		if (points.Count <= 2 || tolerance <= 0f)
			return new List<SKPoint>(points);

		var keep = new bool[points.Count];
		keep[0] = true;
		keep[^1] = true;

		SimplifySegment(points, 0, points.Count - 1, tolerance, keep);

		var result = new List<SKPoint>();

		for (int i = 0; i < points.Count; i++)
		{
			if (keep[i])
				result.Add(points[i]);
		}

		return result;
	}

	private static void SimplifySegment(
		IReadOnlyList<SKPoint> points,
		int first,
		int last,
		float tolerance,
		bool[] keep)
	{
		if (last <= first + 1)
			return;

		float maxDistance = 0f;
		int maxIndex = -1;

		for (int i = first + 1; i < last; i++)
		{
			float d = DistanceToSegment(points[i], points[first], points[last]);

			if (d > maxDistance)
			{
				maxDistance = d;
				maxIndex = i;
			}
		}

		if (maxIndex < 0 || maxDistance <= tolerance)
			return;

		keep[maxIndex] = true;

		// Рекурсия по обе стороны от самой выступающей точки: она и определяет,
		// какие участки требуют детализации.
		SimplifySegment(points, first, maxIndex, tolerance, keep);
		SimplifySegment(points, maxIndex, last, tolerance, keep);
	}

	/// <summary>
	/// Строит линию разреза: прореживает точки, проводит через них кубические
	/// сегменты и продлевает прямыми хвостами за габариты фигуры.
	///
	/// Продление делается отрезками, а не продолжением кривой: за пределами
	/// фигуры форма не важна, а прямая не может уйти в сторону и задеть
	/// то, чего пользователь не намеревался резать.
	/// </summary>
	private static CutGeometry? BuildCutGeometry(
		IReadOnlyList<SKPoint> raw,
		SKRect bounds,
		float tolerance)
	{
		if (raw == null || raw.Count < 2)
			return null;

		var points = SimplifyPolyline(raw, tolerance);

		if (points.Count < 2)
			return null;

		float reach = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) + 1f;

		var head = Extend(points[0], points[1], reach);
		var tail = Extend(points[^1], points[^2], reach);

		var path = new SKPath();

		path.MoveTo(head);
		path.LineTo(points[0]);

		if (points.Count == 2)
		{
			// Две точки задают отрезок: кубик через них выродится в прямую,
			// но лишние вычисления незачем — это случай прямых ножниц.
			path.LineTo(points[1]);
		}
		else
		{
			for (int i = 0; i < points.Count - 1; i++)
			{
				GetCatmullRomControls(points, i, out var c1, out var c2);
				path.CubicTo(c1, c2, points[i + 1]);
			}
		}

		path.LineTo(tail);

		var polyline = FlattenPath(path);

		if (polyline.Count < 2)
		{
			path.Dispose();
			return null;
		}

		return new CutGeometry { Path = path, Polyline = polyline };
	}

	/// <summary>
	/// Контрольные точки кубического сегмента между points[i] и points[i+1]
	/// по схеме Catmull-Rom.
	///
	/// Направление касательной в узле задаётся хордой между его соседями,
	/// длина — шестой частью этой хорды. Кривая при этом проходит ровно через
	/// исходные точки, а не рядом с ними, как было бы при аппроксимации.
	/// </summary>
	private static void GetCatmullRomControls(
		IReadOnlyList<SKPoint> points,
		int i,
		out SKPoint c1,
		out SKPoint c2)
	{
		int last = points.Count - 1;

		var p0 = points[Math.Max(i - 1, 0)];
		var p1 = points[i];
		var p2 = points[i + 1];
		var p3 = points[Math.Min(i + 2, last)];

		c1 = new SKPoint(
			p1.X + (p2.X - p0.X) / 6f,
			p1.Y + (p2.Y - p0.Y) / 6f);

		c2 = new SKPoint(
			p2.X - (p3.X - p1.X) / 6f,
			p2.Y - (p3.Y - p1.Y) / 6f);
	}

	/// <summary>Плотная ломаная аппроксимация пути для проверок расстояния.</summary>
	private static List<SKPoint> FlattenPath(SKPath path)
	{
		var result = new List<SKPoint>();

		using var measure = new SKPathMeasure(path, false);

		float length = measure.Length;

		if (length <= 0f)
			return result;

		// Шаг мелкий: по этой ломаной определяется, лежит ли ребро на разрезе,
		// и грубая аппроксимация давала бы ложные срабатывания на изгибах.
		float step = MathF.Max(length / 2000f, 0.05f);

		for (float d = 0f; d < length; d += step)
			result.Add(measure.GetPosition(d));

		result.Add(measure.GetPosition(length));

		return result;
	}

	/// <summary>
	/// Сглаженная линия разреза без продления — для предпросмотра.
	/// Вызывающий обязан освободить путь.
	/// </summary>
	public static SKPath? BuildPreviewPath(IReadOnlyList<SKPoint> raw, float tolerance)
	{
		if (raw == null || raw.Count < 2)
			return null;

		var points = SimplifyPolyline(raw, tolerance);

		if (points.Count < 2)
			return null;

		var path = new SKPath();
		path.MoveTo(points[0]);

		if (points.Count == 2)
		{
			path.LineTo(points[1]);
			return path;
		}

		for (int i = 0; i < points.Count - 1; i++)
		{
			GetCatmullRomControls(points, i, out var c1, out var c2);
			path.CubicTo(c1, c2, points[i + 1]);
		}

		return path;
	}

	#endregion

	#region Лезвие
	private static SKPath? BuildBlade(SKPath cutPath, float halfWidth)
	{
		if (cutPath == null || cutPath.IsEmpty)
			return null;

		using var pen = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			StrokeWidth = halfWidth * 2f,
			StrokeJoin = SKStrokeJoin.Round,
			StrokeCap = SKStrokeCap.Square
		};

		var blade = new SKPath();

		if (!pen.GetFillPath(cutPath, blade))
		{
			blade.Dispose();
			return null;
		}

		var simplified = blade.Simplify();

		if (simplified == null)
			return blade;

		blade.Dispose();
		simplified.FillType = SKPathFillType.Winding;

		return simplified;
	}

	/// <summary>
	/// Продлевает линию разреза за габариты по касательным её концов.
	/// </summary>
	private static List<SKPoint> ExtendCut(IReadOnlyList<SKPoint> cut, SKRect bounds)
	{
		var result = new List<SKPoint>(cut.Count + 2);

		if (cut.Count < 2)
		{
			result.AddRange(cut);
			return result;
		}

		float reach = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) + 1f;

		var head = Extend(cut[0], cut[1], reach);
		var tail = Extend(cut[^1], cut[^2], reach);

		result.Add(head);
		result.AddRange(cut);
		result.Add(tail);

		return result;
	}

	private static SKPoint Extend(SKPoint from, SKPoint towards, float distance)
	{
		float dx = from.X - towards.X;
		float dy = from.Y - towards.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 1e-6f)
			return from;

		return new SKPoint(from.X + dx / len * distance, from.Y + dy / len * distance);
	}

	#endregion

	#region Разрез одной фигуры

	private static List<Shape>? CutShape(
		Shape shape,
		IReadOnlyList<SKPoint> cutWorld,
		bool capScissors,
		float smoothTolerance)
	{
		var bounds = shape.GetBounds();

		if (bounds.IsEmpty)
			return null;

		float halfWidth = GetBladeHalfWidth(bounds);
		float onBlade = halfWidth * 4f;

		using var cut = BuildCutGeometry(cutWorld, bounds, smoothTolerance);

		if (cut == null)
			return null;

		using var blade = BuildBlade(cut.Path, halfWidth);

		if (blade == null || blade.IsEmpty)
			return null;

		if (shape is ImageShape image)
			return CutImage(image, blade, cut.Polyline, onBlade, capScissors);

		var source = shape as CurveShape ?? shape.ToCurveShape();

		if (source == null || source.Nodes.Count < 2)
			return null;

		using var worldPath = source.BuildWorldPath();

		if (worldPath == null || worldPath.IsEmpty)
			return null;

		var pieces = HasClosedContour(worldPath)
			? CutClosed(worldPath, blade, cut.Polyline, onBlade, capScissors)
			: CutOpen(worldPath, cut.Polyline);

		if (pieces == null || pieces.Count < 2)
		{
			DisposeAll(pieces);
			return null;
		}

		var result = new List<Shape>(pieces.Count);

		foreach (var piece in pieces)
		{
			var curve = BuildCurveFromWorldPath(piece, shape);

			if (curve != null)
				result.Add(curve);

			piece.Dispose();
		}

		return result.Count >= 2 ? result : null;
	}

	/// <summary>
	/// Разрез замкнутой области: вычитание лезвия и разбор результата
	/// на несвязные части.
	/// </summary>
	private static List<SKPath>? CutClosed(
		SKPath worldPath,
		SKPath blade,
		IReadOnlyList<SKPoint> extendedCut,
		float onBlade,
		bool capScissors)
	{
		using var simple = worldPath.Simplify() ?? new SKPath(worldPath);

		var carved = simple.Op(blade, SKPathOp.Difference);

		if (carved == null)
			return null;

		using (carved)
		{
			var pieces = SplitIntoPieces(carved);

			if (pieces.Count < 2)
			{
				DisposeAll(pieces);
				return null;
			}

			if (capScissors)
				return pieces;

			// Флаг выключен: рёбра вдоль линии разреза убираются, и части
			// становятся незамкнутыми. Эти рёбра лежат ровно на лезвии,
			// поэтому опознаются по расстоянию до линии разреза.
			var opened = new List<SKPath>(pieces.Count);

			foreach (var piece in pieces)
			{
				opened.Add(RemoveEdgesOnCut(piece, extendedCut, onBlade));
				piece.Dispose();
			}

			return opened;
		}
	}

	/// <summary>
	/// Разрез незамкнутой линии: булевы операции здесь не годятся — Op неявно
	/// замкнёт открытый путь по правилу заполнения и вернёт бессмыслицу.
	/// Вместо этого ищутся точки пересечения и путь делится по ним.
	///
	/// Длины пересечений находятся по ломаной аппроксимации, а сами куски
	/// извлекаются через SKPathMeasure.GetSegment — то есть кривые Безье
	/// сохраняются, спрямления не происходит.
	/// </summary>
	private static List<SKPath>? CutOpen(SKPath worldPath, IReadOnlyList<SKPoint> cut)
	{
		var result = new List<SKPath>();

		using var measure = new SKPathMeasure(worldPath, false);

		float length = measure.Length;

		if (length <= 0f)
			return null;

		var crossings = new List<float>();

		var previous = measure.GetPosition(0f);

		for (float d = OpenScanStep; d <= length; d += OpenScanStep)
		{
			var current = measure.GetPosition(MathF.Min(d, length));

			if (SegmentCrossesPolyline(previous, current, cut))
			{
				// Точность до шага обхода: для разреза этого достаточно,
				// смещение меньше четверти миллиметра.
				crossings.Add(d - OpenScanStep * 0.5f);
			}

			previous = current;
		}

		if (crossings.Count == 0)
			return null;

		float start = 0f;

		foreach (float crossing in crossings)
		{
			AddSegment(measure, start, crossing, result);
			start = crossing;
		}

		AddSegment(measure, start, length, result);

		return result.Count >= 2 ? result : null;
	}

	/// <summary>
	/// Пересекает ли линия разреза контур фигуры.
	///
	/// Проверяется именно пересечение ОЧЕРТАНИЯ, а не попадание внутрь области:
	/// разрез разделяет фигуру только если пересекает её границу. Проверка
	/// работает одинаково для замкнутых и незамкнутых путей, поэтому линии
	/// и ломаные учитываются наравне с залитыми фигурами.
	/// </summary>
	public static bool CutCrossesShape(Shape shape, IReadOnlyList<SKPoint> cut)
	{
		if (shape == null || cut == null || cut.Count < 2)
			return false;

		using var worldPath = shape.BuildWorldPath();

		if (worldPath == null || worldPath.IsEmpty)
			return false;

		// Дешёвая отбраковка по габаритам до подробной проверки.
		if (!worldPath.Bounds.IntersectsWith(GetBounds(cut)))
			return false;

		foreach (var contour in FlattenAllContours(worldPath))
		{
			for (int i = 1; i < contour.Count; i++)
			{
				if (SegmentCrossesPolyline(contour[i - 1], contour[i], cut))
					return true;
			}
		}

		return false;
	}

	/// <summary>Габариты линии разреза.</summary>
	public static SKRect GetBounds(IReadOnlyList<SKPoint> points)
	{
		if (points.Count == 0)
			return SKRect.Empty;

		float left = points[0].X, right = points[0].X;
		float top = points[0].Y, bottom = points[0].Y;

		foreach (var p in points)
		{
			left = MathF.Min(left, p.X);
			right = MathF.Max(right, p.X);
			top = MathF.Min(top, p.Y);
			bottom = MathF.Max(bottom, p.Y);
		}

		return new SKRect(left, top, right, bottom);
	}

	/// <summary>
	/// Ломаная аппроксимация всех контуров пути.
	///
	/// SKPathMeasure обходит по одному контуру за раз, поэтому нужен цикл
	/// с NextContour: без него терялись бы дырки и вложенные контуры, и
	/// разрез через отверстие кольца не находился бы.
	/// </summary>
	private static List<List<SKPoint>> FlattenAllContours(SKPath path)
	{
		var result = new List<List<SKPoint>>();

		using var measure = new SKPathMeasure(path, false);

		do
		{
			float length = measure.Length;

			if (length <= 0f)
				continue;

			var points = new List<SKPoint>();
			float step = MathF.Max(length / 400f, 0.1f);

			for (float d = 0f; d < length; d += step)
				points.Add(measure.GetPosition(d));

			points.Add(measure.GetPosition(length));

			if (points.Count >= 2)
				result.Add(points);
		}
		while (measure.NextContour());

		return result;
	}

	private static void AddSegment(SKPathMeasure measure, float start, float stop, List<SKPath> target)
	{
		if (stop - start < 1e-3f)
			return;

		var segment = new SKPath();

		// В SkiaSharp 4 параметр dst у GetSegment имеет тип SKPathBuilder.
		// Если сборка ругается на тип, используйте билдер и Detach.
		if (measure.GetSegment(start, stop, segment, true) && !segment.IsEmpty)
			target.Add(segment);
		else
			segment.Dispose();
	}

	private static bool SegmentCrossesPolyline(SKPoint a, SKPoint b, IReadOnlyList<SKPoint> polyline)
	{
		for (int i = 1; i < polyline.Count; i++)
		{
			if (SegmentsIntersect(a, b, polyline[i - 1], polyline[i]))
				return true;
		}

		return false;
	}

	private static bool SegmentsIntersect(SKPoint p1, SKPoint p2, SKPoint p3, SKPoint p4)
	{
		float d1 = Cross(p3, p4, p1);
		float d2 = Cross(p3, p4, p2);
		float d3 = Cross(p1, p2, p3);
		float d4 = Cross(p1, p2, p4);

		return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
			&& ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
	}

	private static float Cross(SKPoint a, SKPoint b, SKPoint p)
		=> (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

	#endregion

	#region Разбор результата

	/// <summary>
	/// Делит путь на несвязные части. Контур считается дыркой, если лежит
	/// внутри другого; дырки уходят к своему внешнему контуру.
	/// </summary>
	private static List<SKPath> SplitIntoPieces(SKPath path)
	{
		var contours = ExtractContours(path);

		if (contours.Count <= 1)
			return contours;

		var outers = new List<SKPath>();
		var holes = new List<SKPath>();

		foreach (var contour in contours)
		{
			bool isHole = false;

			foreach (var other in contours)
			{
				if (ReferenceEquals(other, contour))
					continue;

				if (ContainsContour(other, contour))
				{
					isHole = true;
					break;
				}
			}

			(isHole ? holes : outers).Add(contour);
		}

		var pieces = new List<SKPath>(outers.Count);

		foreach (var outer in outers)
		{
			var piece = new SKPath(outer) { FillType = SKPathFillType.EvenOdd };

			foreach (var hole in holes)
			{
				if (ContainsContour(outer, hole))
					piece.AddPath(hole);
			}

			pieces.Add(piece);
			outer.Dispose();
		}

		foreach (var hole in holes)
			hole.Dispose();

		return pieces;
	}

	private static bool ContainsContour(SKPath outer, SKPath inner)
	{
		if (!outer.Bounds.Contains(inner.Bounds))
			return false;

		var probe = inner.PointCount > 0 ? inner[0] : inner.Bounds.Location;
		return outer.Contains(probe.X, probe.Y);
	}

	private static List<SKPath> ExtractContours(SKPath path)
	{
		var result = new List<SKPath>();

		using var iter = path.CreateRawIterator();
		var pts = new SKPoint[4];

		SKPath? current = null;
		bool done = false;

		while (!done)
		{
			switch (iter.Next(pts))
			{
				case SKPathVerb.Move:
					current = new SKPath { FillType = SKPathFillType.Winding };
					current.MoveTo(pts[0]);
					result.Add(current);
					break;

				case SKPathVerb.Line:
					current?.LineTo(pts[1]);
					break;

				case SKPathVerb.Quad:
					current?.QuadTo(pts[1], pts[2]);
					break;

				case SKPathVerb.Conic:
					current?.ConicTo(pts[1], pts[2], iter.ConicWeight());
					break;

				case SKPathVerb.Cubic:
					current?.CubicTo(pts[1], pts[2], pts[3]);
					break;

				case SKPathVerb.Close:
					current?.Close();
					break;

				case SKPathVerb.Done:
					done = true;
					break;
			}
		}

		result.RemoveAll(p =>
		{
			if (p.PointCount >= 2)
				return false;

			p.Dispose();
			return true;
		});

		return result;
	}

	/// <summary>
	/// Убирает из части рёбра, лежащие на линии разреза, размыкая её контуры.
	///
	/// Работает посегментно: сегмент, целиком лежащий на разрезе, отбрасывается,
	/// остальные переносятся без изменений. Поэтому кривые Безье исходной
	/// границы сохраняются — спрямляются только те прямые отрезки, которые
	/// и так были прямыми, потому что их создало лезвие.
	/// </summary>
	private static SKPath RemoveEdgesOnCut(SKPath piece, IReadOnlyList<SKPoint> cut, float onBlade)
	{
		var result = new SKPath { FillType = SKPathFillType.Winding };

		var contours = ExtractContours(piece);

		if (contours.Count == 0)
			return new SKPath(piece);

		foreach (var contour in contours)
		{
			OpenContour(contour, cut, onBlade, result);
			contour.Dispose();
		}

		return result.IsEmpty ? new SKPath(piece) : result;
	}

	/// <summary>
	/// Переносит в target те сегменты контура, что не лежат на линии разреза.
	/// </summary>
	private static void OpenContour(SKPath contour, IReadOnlyList<SKPoint> cut, float onBlade, SKPath target)
	{
		var segments = ReadSegments(contour, out bool closed);

		if (segments.Count == 0)
			return;

		var onCut = new bool[segments.Count];
		bool anyKept = false;

		for (int i = 0; i < segments.Count; i++)
		{
			onCut[i] = IsSegmentOnCut(segments[i], cut, onBlade);

			if (!onCut[i])
				anyKept = true;
		}

		// Ни один сегмент не на разрезе — контур переносится целиком.
		if (!anyKept)
			return;

		if (Array.TrueForAll(onCut, v => !v))
		{
			AppendRun(segments, 0, segments.Count, target);
			return;
		}

		// Обход начинается с первого сегмента ВНЕ разреза. Иначе участок,
		// проходящий через начало списка, разорвался бы надвое.
		int startIndex = Array.FindIndex(onCut, v => !v);

		int count = segments.Count;
		int runStart = -1;

		for (int k = 0; k <= count; k++)
		{
			int i = (startIndex + k) % count;
			bool skip = k == count || onCut[i];

			if (skip)
			{
				if (runStart >= 0)
				{
					AppendRunCyclic(segments, startIndex, runStart, k, target);
					runStart = -1;
				}

				continue;
			}

			if (runStart < 0)
				runStart = k;
		}

		// Незамкнутый контур, у которого ничего не отрезано, остаётся как был.
		if (!closed && runStart >= 0)
			AppendRunCyclic(segments, startIndex, runStart, count, target);
	}

	private static void AppendRun(List<CutSegment> segments, int from, int to, SKPath target)
	{
		if (to <= from)
			return;

		target.MoveTo(segments[from].P0);

		for (int i = from; i < to; i++)
			AppendSegment(segments[i], target);
	}

	private static void AppendRunCyclic(List<CutSegment> segments, int offset, int fromK, int toK, SKPath target)
	{
		if (toK <= fromK)
			return;

		int count = segments.Count;

		target.MoveTo(segments[(offset + fromK) % count].P0);

		for (int k = fromK; k < toK; k++)
			AppendSegment(segments[(offset + k) % count], target);
	}

	private static void AppendSegment(CutSegment s, SKPath target)
	{
		switch (s.Verb)
		{
			case SKPathVerb.Line:
				target.LineTo(s.P3);
				break;

			case SKPathVerb.Quad:
				target.QuadTo(s.P1, s.P3);
				break;

			case SKPathVerb.Conic:
				target.ConicTo(s.P1, s.P3, s.Weight);
				break;

			case SKPathVerb.Cubic:
				target.CubicTo(s.P1, s.P2, s.P3);
				break;
		}
	}

	private static bool IsSegmentOnCut(CutSegment s, IReadOnlyList<SKPoint> cut, float onBlade)
	{
		// Прямому отрезку достаточно середины и концов, кривой нужны
		// промежуточные точки: она может касаться разреза концами, уходя дугой.
		float[] samples = s.Verb == SKPathVerb.Line
			? new[] { 0f, 0.5f, 1f }
			: new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };

		foreach (float t in samples)
		{
			if (DistanceToPolyline(Evaluate(s, t), cut) > onBlade)
				return false;
		}

		return true;
	}

	private static SKPoint Evaluate(CutSegment s, float t)
	{
		switch (s.Verb)
		{
			case SKPathVerb.Cubic:
				{
					float mt = 1f - t;

					float a = mt * mt * mt;
					float b = 3f * mt * mt * t;
					float c = 3f * mt * t * t;
					float d = t * t * t;

					return new SKPoint(
						a * s.P0.X + b * s.P1.X + c * s.P2.X + d * s.P3.X,
						a * s.P0.Y + b * s.P1.Y + c * s.P2.Y + d * s.P3.Y);
				}

			case SKPathVerb.Quad:
				{
					float mt = 1f - t;

					return new SKPoint(
						mt * mt * s.P0.X + 2f * mt * t * s.P1.X + t * t * s.P3.X,
						mt * mt * s.P0.Y + 2f * mt * t * s.P1.Y + t * t * s.P3.Y);
				}

			case SKPathVerb.Conic:
				{
					float mt = 1f - t;
					float w = s.Weight;

					float denom = mt * mt + 2f * w * t * mt + t * t;

					if (MathF.Abs(denom) < 1e-9f)
						denom = 1e-9f;

					return new SKPoint(
						(mt * mt * s.P0.X + 2f * w * t * mt * s.P1.X + t * t * s.P3.X) / denom,
						(mt * mt * s.P0.Y + 2f * w * t * mt * s.P1.Y + t * t * s.P3.Y) / denom);
				}

			default:
				return new SKPoint(
					s.P0.X + (s.P3.X - s.P0.X) * t,
					s.P0.Y + (s.P3.Y - s.P0.Y) * t);
		}
	}

	/// <summary>
	/// Разбирает контур на сегменты. Замыкающее ребро добавляется явно:
	/// в пути оно задано глаголом Close и своих точек не имеет, а нам нужно
	/// проверить и его — оно вполне может оказаться частью разреза.
	/// </summary>
	private static List<CutSegment> ReadSegments(SKPath contour, out bool closed)
	{
		var segments = new List<CutSegment>();

		closed = false;

		using var iter = contour.CreateRawIterator();
		var pts = new SKPoint[4];

		SKPoint start = default;
		SKPoint current = default;
		bool done = false;

		while (!done)
		{
			switch (iter.Next(pts))
			{
				case SKPathVerb.Move:
					start = pts[0];
					current = pts[0];
					break;

				case SKPathVerb.Line:
					segments.Add(CutSegment.Line(current, pts[1]));
					current = pts[1];
					break;

				case SKPathVerb.Quad:
					segments.Add(CutSegment.Quad(current, pts[1], pts[2]));
					current = pts[2];
					break;

				case SKPathVerb.Conic:
					segments.Add(CutSegment.Conic(current, pts[1], pts[2], iter.ConicWeight()));
					current = pts[2];
					break;

				case SKPathVerb.Cubic:
					segments.Add(CutSegment.Cubic(current, pts[1], pts[2], pts[3]));
					current = pts[3];
					break;

				case SKPathVerb.Close:
					closed = true;

					if (Geometry.Distance(current, start) > 1e-4f)
					{
						segments.Add(CutSegment.Line(current, start));
						current = start;
					}

					break;

				case SKPathVerb.Done:
					done = true;
					break;
			}
		}

		return segments;
	}

	private static float DistanceToPolyline(SKPoint p, IReadOnlyList<SKPoint> polyline)
	{
		float best = float.MaxValue;

		for (int i = 1; i < polyline.Count; i++)
			best = MathF.Min(best, DistanceToSegment(p, polyline[i - 1], polyline[i]));

		return best;
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		float lenSq = dx * dx + dy * dy;

		if (lenSq < 1e-9f)
			return Geometry.Distance(p, a);

		float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);

		return Geometry.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
	}

	#endregion

	#region Сборка фигур

	/// <summary>
	/// Строит кривую из мирового пути, наследуя стиль исходной фигуры.
	/// Трансформация единичная: узлы уже в мировых координатах.
	/// </summary>
	private static CurveShape? BuildCurveFromWorldPath(SKPath worldPath, Shape styleSource)
	{
		if (worldPath.IsEmpty)
			return null;

		var curve = new CurveShape
		{
			Name = styleSource.Name,
			Layer = styleSource.Layer,
			Stroke = styleSource.Stroke.Clone(),
			Fill = styleSource.Fill.Clone(),
			IsVisible = styleSource.IsVisible,
			IsLocked = false,
			IsFocused = false,
			LocalTransform = SKMatrix.CreateIdentity(),
			FillType = worldPath.FillType
		};

		// Эффекты — такая же часть оформления, как обводка и заливка,
		// и обе половины должны выглядеть как исходная фигура. Клоны,
		// а не общие ссылки: иначе перетаскивание ручки тени у одной
		// половины двигало бы тень у второй.
		curve.Shadow = styleSource.Shadow.Clone();
		curve.Transparency = styleSource.Transparency.Clone();

		using var iter = worldPath.CreateRawIterator();
		var pts = new SKPoint[4];

		bool done = false;
		int contourStart = -1;

		while (!done)
		{
			switch (iter.Next(pts))
			{
				case SKPathVerb.Move:
					contourStart = curve.Nodes.Count;
					AddNode(curve, pts[0], newContour: true);
					break;

				case SKPathVerb.Line:
					AddNode(curve, pts[1], newContour: false);
					break;

				case SKPathVerb.Quad:
					{
						// Квадратичная кривая переводится в кубическую:
						// в модели узлов только кубические сегменты.
						var c1 = Lerp(pts[0], pts[1], 2f / 3f);
						var c2 = Lerp(pts[2], pts[1], 2f / 3f);
						AddCubic(curve, c1, c2, pts[2]);
						break;
					}

				case SKPathVerb.Conic:
					{
						var c1 = Lerp(pts[0], pts[1], 2f / 3f);
						var c2 = Lerp(pts[2], pts[1], 2f / 3f);
						AddCubic(curve, c1, c2, pts[2]);
						break;
					}

				case SKPathVerb.Cubic:
					AddCubic(curve, pts[1], pts[2], pts[3]);
					break;

				case SKPathVerb.Close:
					if (contourStart >= 0 && curve.Nodes.Count > contourStart)
						curve.Nodes[^1].CloseAfter = true;
					break;

				case SKPathVerb.Done:
					done = true;
					break;
			}
		}

		return curve.Nodes.Count >= 2 ? curve : null;
	}

	private static void AddNode(CurveShape curve, SKPoint position, bool newContour)
	{
		curve.Nodes.Add(new CurveNode
		{
			Position = position,
			Type = CurveNodeType.Straight,
			NewContour = newContour
		});
	}

	private static void AddCubic(CurveShape curve, SKPoint c1, SKPoint c2, SKPoint end)
	{
		if (curve.Nodes.Count == 0)
			return;

		var from = curve.Nodes[^1];

		from.ControlNext = c1;

		if (from.Type == CurveNodeType.Straight)
			from.Type = CurveNodeType.Sharp;

		curve.Nodes.Add(new CurveNode
		{
			Position = end,
			ControlPrev = c2,
			Type = CurveNodeType.Sharp
		});
	}

	private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
		=> new SKPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	private static bool HasClosedContour(SKPath path)
	{
		using var iter = path.CreateRawIterator();
		var pts = new SKPoint[4];

		while (true)
		{
			var verb = iter.Next(pts);

			if (verb == SKPathVerb.Close)
				return true;

			if (verb == SKPathVerb.Done)
				return false;
		}
	}

	private static void DisposeAll(List<SKPath>? paths)
	{
		if (paths == null)
			return;

		foreach (var p in paths)
			p.Dispose();

		paths.Clear();
	}

	#endregion

	#region Изображение

	/// <summary>
	/// Разрез изображения: рамка режется как обычная фигура, а растр
	/// разделяется на два новых битмапа по форме частей.
	///
	/// Каждая часть получает обрезанный до своих габаритов кусок исходного
	/// растра с прозрачностью вне своей формы. Полный битмап в обеих частях
	/// был бы проще, но удваивал бы память на каждый разрез.
	/// </summary>
	private static List<Shape>? CutImage(
		ImageShape image,
		SKPath blade,
		IReadOnlyList<SKPoint> extendedCut,
		float onBlade,
		bool capScissors)
	{
		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return null;

		using var worldPath = image.BuildWorldPath();

		if (worldPath == null || worldPath.IsEmpty)
			return null;

		// Рамка изображения всегда режется замкнутой: растр делится по форме
		// частей, и незамкнутая часть маску задать не сможет.
		var pieces = CutClosed(worldPath, blade, extendedCut, onBlade, capScissors: true);

		if (pieces == null || pieces.Count < 2)
		{
			DisposeAll(pieces);
			return null;
		}

		var result = new List<Shape>(pieces.Count);

		foreach (var piece in pieces)
		{
			var part = BuildImagePart(image, piece);

			if (part != null)
				result.Add(part);

			piece.Dispose();
		}

		return result.Count >= 2 ? result : null;
	}

	private static ImageShape? BuildImagePart(ImageShape source, SKPath worldPiece)
	{
		var sourceBitmap = source.Bitmap!;

		// Мировой путь части переводится в пиксели исходного растра.
		if (!source.TryGetInverseWorldTransform(out var inverse))
			inverse = SKMatrix.CreateIdentity();

		using var localPiece = new SKPath();
		worldPiece.Transform(inverse, localPiece);

		var rect = source.ImageLocalRect;

		if (rect.Width <= 0f || rect.Height <= 0f)
			return null;

		float sx = sourceBitmap.Width / rect.Width;
		float sy = sourceBitmap.Height / rect.Height;

		var toPixels = Geometry.Multiply(
			SKMatrix.CreateScale(sx, sy),
			SKMatrix.CreateTranslation(-rect.Left, -rect.Top));

		using var pixelPiece = new SKPath();
		localPiece.Transform(toPixels, pixelPiece);

		var pb = pixelPiece.Bounds;

		int left = Math.Clamp((int)MathF.Floor(pb.Left), 0, sourceBitmap.Width);
		int top = Math.Clamp((int)MathF.Floor(pb.Top), 0, sourceBitmap.Height);
		int right = Math.Clamp((int)MathF.Ceiling(pb.Right), 0, sourceBitmap.Width);
		int bottom = Math.Clamp((int)MathF.Ceiling(pb.Bottom), 0, sourceBitmap.Height);

		int width = right - left;
		int height = bottom - top;

		if (width <= 0 || height <= 0)
			return null;

		var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
		var cropped = new SKBitmap(info);

		using (var canvas = new SKCanvas(cropped))
		{
			canvas.Clear(SKColors.Transparent);
			canvas.Translate(-left, -top);

			// Отсечение по форме части: пиксели вне неё остаются прозрачными.
			canvas.ClipPath(pixelPiece, SKClipOperation.Intersect, true);

			canvas.DrawBitmap(
				sourceBitmap,
				0f,
				0f,
				new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
		}

		// Обратный перевод габаритов куска в локальные координаты.
		var partRect = new SKRect(
			rect.Left + left / sx,
			rect.Top + top / sy,
			rect.Left + right / sx,
			rect.Top + bottom / sy);

		var part = new ImageShape
		{
			Name = source.Name,
			Layer = source.Layer,
			Stroke = source.Stroke.Clone(),
			Fill = source.Fill.Clone(),
			IsVisible = source.IsVisible,
			LocalTransform = source.LocalTransform,
			ImagePath = source.ImagePath,
			ImageLocalRect = partRect
		};

		foreach (var node in CurveNodesFromPath(localPiece))
			part.Nodes.Add(node);

		part.SetOriginalBitmap(cropped);
		cropped.Dispose();

		// Коррекция изображения переносится на обе половины.
		//
		// Она НЕ запечена в пиксели: Adjustments применяются на этапе
		// отрисовки через кэшированный SKPaint, а Bitmap хранит исходные
		// значения (см. комментарий к ImageShape). Поэтому обрезанный растр
		// приходит без яркости и контраста, и без этой строки разрез
		// сбрасывал всю настройку в identity.
		//
		// Двойного применения тут быть не может ровно по той же причине:
		// копируется настройка, а не результат.
		//
		// CopyFrom, а не присваивание клона: сеттер Adjustments закрыт,
		// и правка идёт в уже созданный объект. Version внутри CopyFrom
		// увеличивается, поэтому закэшированная краска пересоберётся.
		part.Adjustments.CopyFrom(source.Adjustments);

		// По той же причине переносятся тень и прозрачность.
		part.Shadow = source.Shadow.Clone();
		part.Transparency = source.Transparency.Clone();

		return part.Nodes.Count >= 2 ? part : null;
	}

	private static IEnumerable<CurveNode> CurveNodesFromPath(SKPath path)
	{
		var temp = new CurveShape();

		var built = BuildCurveFromWorldPath(path, temp);

		return built?.Nodes ?? Enumerable.Empty<CurveNode>();
	}

	#endregion
}