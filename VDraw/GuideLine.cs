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

public sealed class GuideLine : Shape
{
	public GuideOrientation Orientation { get; set; }
	public GuideTransformMode TransformMode { get; set; } = GuideTransformMode.Move;

	public SKPoint GuidePoint { get; set; }
	public float AngleDeg { get; set; }

	public override SKRect GetLocalBounds()
	{
		const float huge = 100000f;
		const float halfThickness = 0.25f;

		return Orientation == GuideOrientation.Vertical
			? new SKRect(-halfThickness, -huge, halfThickness, huge)
			: new SKRect(-huge, -halfThickness, huge, halfThickness);
	}

	public override SKPath BuildLocalPath()
	{
		var path = new SKPath();

		const float huge = 100000f;
		if (Orientation == GuideOrientation.Vertical)
		{
			path.MoveTo(0, -huge);
			path.LineTo(0, huge);
		}
		else
		{
			path.MoveTo(-huge, 0);
			path.LineTo(huge, 0);
		}

		return path;
	}

	protected override bool IsClosedGeometry() => false;

	public SKPoint GetRotationCenterPage() => GuidePoint;

	public void SetAnchorFromPagePoint(SKPoint pagePoint)
	{
		GuidePoint = pagePoint;
	}

	public void SetRotationCenter(SKPoint pagePoint)
	{
		GuidePoint = pagePoint;
	}

	public override bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		var center = GuidePoint;
		var normal = GetNormal();

		float dx = pagePoint.X - center.X;
		float dy = pagePoint.Y - center.Y;

		float dist = MathF.Abs(dx * normal.X + dy * normal.Y);
		// Допуск уже в единицах страницы — делить не на что. Прежнее
		// деление на px/мм давало миллиметры и сравнивало их с расстоянием
		// в единицах страницы: на дюймовой странице расхождение в 25 раз.
		float tolMm = toleranceUom;

		return dist <= tolMm;
	}

	/// <summary>
	/// Единичное направление линии с учётом её поворота.
	///
	/// Положительный угол означает поворот ПО ЧАСОВОЙ стрелке — одинаково
	/// для обеих ориентаций.
	///
	/// У вертикальной было (sin, cos). Это направление с экранным углом
	/// 90° − AngleDeg, то есть растущий угол разворачивал линию ПРОТИВ
	/// часовой, тогда как у горизонтальной (cos, sin) — по часовой.
	/// Вращение же считается по углу мыши, а он на экране растёт по часовой:
	/// вертикальная направляющая уезжала навстречу курсору.
	///
	/// Знак у синуса это выправляет: экранный угол становится 90° + AngleDeg,
	/// и обе ориентации поворачиваются в одну сторону. При AngleDeg = 0
	/// направление прежнее, поэтому неповёрнутые направляющие в старых
	/// файлах стоят как стояли; у повёрнутой вертикальной угол прочитается
	/// с обратным знаком — то есть так, как его и задумывали, когда крутили
	/// мышью.
	/// </summary>
	public SKPoint GetDirection()
	{
		float angle = AngleDeg * MathF.PI / 180f;

		return Orientation == GuideOrientation.Vertical
			? new SKPoint(-MathF.Sin(angle), MathF.Cos(angle))
			: new SKPoint(MathF.Cos(angle), MathF.Sin(angle));
	}

	/// <summary>Единичная нормаль — направление, вдоль которого линию двигают.</summary>
	public SKPoint GetNormal()
	{
		var dir = GetDirection();

		return new SKPoint(-dir.Y, dir.X);
	}

	public override void Draw(SKCanvas canvas, float pxPerWorldUnit)
	{
		var center = GuidePoint;
		var dir = GetDirection();

		const float huge = 100000f;
		var p1 = new SKPoint(center.X - dir.X * huge, center.Y - dir.Y * huge);
		var p2 = new SKPoint(center.X + dir.X * huge, center.Y + dir.Y * huge);

		// Один знаменатель на толщину и на пунктир: обе величины заданы
		// в экранных пикселях, а рисование идёт в единицах страницы.
		// Считать их по-разному значило бы однажды развести штрих и линию —
		// пунктир остался бы прежним, а толщина поехала бы с масштабом.
		float worldPerPx = 1f / MathF.Max(pxPerWorldUnit, 0.0001f);

		float thicknessPx = IsFocused
			? Settings.guide_focused_thickness
			: (IsSelected ? Settings.guide_selected_thickness : Settings.guide_thickness);

		float dashWorld = 4f * worldPerPx;

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,

			// Толщина переводится в единицы страницы, поэтому на экране
			// остаётся постоянной при любом масштабе. Направляющая —
			// вспомогательная линия, а не часть чертежа: её видимая
			// толщина не должна ничего сообщать о размере.
			StrokeWidth = thicknessPx * worldPerPx,

			Color = IsFocused
				? Settings.guide_focused_color
				: (IsSelected ? Settings.guide_selected_color : Settings.guide_color),

			PathEffect = SKPathEffect.CreateDash(new float[] { dashWorld, dashWorld }, 0)
		};

		canvas.DrawLine(p1, p2, paint);
	}

	public override void MoveByWorld(float dx, float dy)
	{
		GuidePoint = new SKPoint(GuidePoint.X + dx, GuidePoint.Y + dy);
	}
}

public static class GuideAdorner
{
	private const float RotationHandleOffsetPx = 100f;
	private const float ArrowRadiusPx = 15f;

	public static void Draw(SKCanvas canvas, MainView view, GuideLine guide)
	{
		var centerScreen = view.PageToScreen(guide.GetRotationCenterPage());

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(255, 255, 255, 220)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(100, 190, 255, 255)
		};

		canvas.DrawCircle(centerScreen, 7f, fill);
		canvas.DrawCircle(centerScreen, 7f, stroke);

		if (guide.TransformMode != GuideTransformMode.Rotate)
			return;

		var handles = GetRotationHandlesScreen(view, guide);
		DrawArcArrow(canvas, handles.ccw, true);
		DrawArcArrow(canvas, handles.cw, false);
	}

	public static (SKPoint ccw, SKPoint cw) GetRotationHandlesScreen(MainView view, GuideLine guide)
	{
		var center = view.PageToScreen(guide.GetRotationCenterPage());

		var dir = Normalize(guide.GetDirection());
		if (dir.LengthSquared <= 0.0001f)
			return (center, center);

		return (
			new SKPoint(center.X - dir.X * RotationHandleOffsetPx, center.Y - dir.Y * RotationHandleOffsetPx),
			new SKPoint(center.X + dir.X * RotationHandleOffsetPx, center.Y + dir.Y * RotationHandleOffsetPx)
		);
	}

	public static (SKPoint ccw, SKPoint cw) GetRotationHandlesPage(MainView view, GuideLine guide)
	{
		var s = GetRotationHandlesScreen(view, guide);
		return (view.ScreenToPage(s.ccw), view.ScreenToPage(s.cw));
	}

	private static SKPoint Normalize(SKPoint v)
	{
		float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
		if (len < 0.0001f)
			return SKPoint.Empty;

		return new SKPoint(v.X / len, v.Y / len);
	}

	private static void DrawArcArrow(SKCanvas canvas, SKPoint center, bool clockwise)
	{
		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(100, 190, 255, 255)
		};

		float r = ArrowRadiusPx;
		var rect = SKRect.Create(center.X - r, center.Y - r, r * 2, r * 2);

		float start = clockwise ? 0f : 0f;
		float sweep = clockwise ? 300f : -300f;

		using var path = new SKPath();
		path.AddArc(rect, start, sweep);
		canvas.DrawPath(path, stroke);

		float endDeg = start + sweep;
		float endRad = endDeg * MathF.PI / 180f;
		var tip = new SKPoint(center.X + r * MathF.Cos(endRad), center.Y + r * MathF.Sin(endRad));

		float tangent = clockwise ? endRad + MathF.PI / 2f : endRad - MathF.PI / 2f;
		float ah = 5f;

		var p1 = new SKPoint(
			tip.X - ah * MathF.Cos(tangent - 0.45f),
			tip.Y - ah * MathF.Sin(tangent - 0.45f));

		var p2 = new SKPoint(
			tip.X - ah * MathF.Cos(tangent + 0.45f),
			tip.Y - ah * MathF.Sin(tangent + 0.45f));

		canvas.DrawLine(tip, p1, stroke);
		canvas.DrawLine(tip, p2, stroke);
	}
}

/// <summary>
/// Прилипание направляющей к узлам фигур.
///
/// Обычное прилипание точки здесь не годится: направляющая бесконечна,
/// и совпасть она должна не местом захвата, а всей линией. Узел, к которому
/// её тянут, может лежать на другом конце листа — поиск рядом с курсором
/// его бы не увидел.
///
/// Поэтому мерой близости служит расстояние от узла ДО ЛИНИИ вдоль нормали,
/// а поправка сдвигает направляющую по этой же нормали. Вдоль себя линия
/// не двигается: у бесконечной прямой такое смещение ничего не меняет.
/// </summary>
internal static class GuideSnapping
{
	public static SKPoint Snap(
		MainView view,
		EditorInteractionContext ctx,
		GuideLine guide,
		SKPoint candidateAnchor)
	{
		ctx.ActiveSnap = null;

		if (!Settings.snap_objects || ctx.AltPressed || view.CurrentPage == null)
			return candidateAnchor;

		SKPoint normal = guide.GetNormal();

		float tolerance = ObjectSnapEngine.ScreenToPageDistance(view, Settings.snap_dist);

		float bestOffset = 0f;
		float bestDistance = float.MaxValue;

		SKPoint bestTarget = SKPoint.Empty;
		bool found = false;

		foreach (var shape in view.CurrentPage.Shapes)
		{
			// Направляющие друг к другу не липнут: две совпавшие
			// направляющие неотличимы одна от другой, и пользователь
			// решил бы, что одна из них пропала.
			if (shape is GuideLine || !shape.IsVisible)
				continue;

			foreach (var candidate in ShapeSnapGeometry.GetSnapPoints(shape))
			{
				// Знаковое расстояние: знак говорит, в какую сторону вдоль
				// нормали двигать линию, чтобы она прошла через узел.
				float offset =
					(candidate.WorldPoint.X - candidateAnchor.X) * normal.X +
					(candidate.WorldPoint.Y - candidateAnchor.Y) * normal.Y;

				float distance = MathF.Abs(offset);

				if (distance > tolerance || distance >= bestDistance)
					continue;

				bestDistance = distance;
				bestOffset = offset;
				bestTarget = candidate.WorldPoint;

				found = true;
			}
		}

		if (!found)
			return candidateAnchor;

		// Точка, которой линия «касается» узла до поправки: проекция узла
		// на текущее, ещё не притянутое положение линии.
		//
		// Именно её ждёт адорнер в SnappedWorldPoint. Он рисует ДВЕ отметки —
		// цель и место, куда пришла двигаемая точка, — и складывает вторую
		// как SnappedWorldPoint плюс Correction. Положи сюда сам узел, как
		// было, и вторая отметка уезжает на величину поправки: на экране
		// появляется лишняя точка рядом с настоящей.
		var projection = new SKPoint(
			bestTarget.X - normal.X * bestOffset,
			bestTarget.Y - normal.Y * bestOffset);

		ctx.ActiveSnap = new SnapResult
		{
			Kind = SnapKind.PointToPoint,

			SnappedWorldPoint = projection,
			TargetWorldPoint = bestTarget,

			// Проекция плюс поправка даёт ровно узел, поэтому обе отметки
			// адорнера ложатся друг на друга и видна одна.
			Correction = new SKPoint(normal.X * bestOffset, normal.Y * bestOffset),

			// Откуда тянуло: короткий пунктир от прежнего положения линии
			// к узлу показывает, на сколько её притянуло.
			DisplayFromWorldPoint = projection,

			DistancePage = bestDistance
		};

		return new SKPoint(
			candidateAnchor.X + normal.X * bestOffset,
			candidateAnchor.Y + normal.Y * bestOffset);
	}
}

public sealed class CreateGuideOperation : DragOperation
{
	private readonly GuideLine _guide;

	public CreateGuideOperation(GuideLine guide)
	{
		_guide = guide;
	}

	public override DragOperationKind Kind => DragOperationKind.CreateGuide;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide.SetAnchorFromPagePoint(pagePoint);
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide.SetAnchorFromPagePoint(GuideSnapping.Snap(view, ctx, _guide, pagePoint));
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide.SetAnchorFromPagePoint(GuideSnapping.Snap(view, ctx, _guide, pagePoint));

		// Метка прилипания живёт только на время протяжки: оставшись
		// на экране, она указывала бы на узел, к которому уже ничего
		// не притягивается.
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}
}

public sealed class MoveGuideCenterOperation : DragOperation
{
	private GuideLine? _guide;
	private SKPoint _startMouse;
	private SKPoint _startCenter;

	public override DragOperationKind Kind => DragOperationKind.MoveGuideCenter;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide = ctx.SelectedGuide;
		if (_guide == null)
			return;

		_startMouse = pagePoint;
		_startCenter = _guide.GetRotationCenterPage();
	}

	/// <summary>
	/// Центр — обычная точка, и прилипает он как точка: по обеим
	/// координатам сразу.
	///
	/// Не так, как сама линия. Линию двигают поперёк себя, и ей довольно
	/// одной координаты — расстояния вдоль нормали; вдоль себя бесконечная
	/// прямая не сдвигается. Центр же стоит НА линии в определённом месте:
	/// он задаёт ось вращения и точку отсчёта. Поставить его точно в узел —
	/// ровно то, ради чего направляющую и крутят вокруг угла детали.
	/// Прежде он не прилипал вовсе и вставал туда, куда попал курсор.
	/// </summary>
	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (_guide == null)
			return;

		float dx = pagePoint.X - _startMouse.X;
		float dy = pagePoint.Y - _startMouse.Y;

		var candidate = new SKPoint(_startCenter.X + dx, _startCenter.Y + dy);

		if (!ctx.AltPressed && ObjectSnapEngine.AnySnappingEnabled)
		{
			// Сама направляющая исключается: её собственная линия проходит
			// через центр, и он прилипал бы к ней же, никуда не двигаясь.
			var snap = ObjectSnapEngine.SnapMovingPoint(
				view,
				new Shape[] { _guide },
				candidate,
				Settings.snap_dist);

			ctx.ActiveSnap = snap;

			if (snap != null && snap.IsValid)
				candidate = new SKPoint(candidate.X + snap.Correction.X, candidate.Y + snap.Correction.Y);
		}
		else
		{
			ctx.ActiveSnap = null;
		}

		_guide.SetRotationCenter(candidate);

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}
}

public sealed class MoveGuideOperation : DragOperation
{
	private GuideLine? _guide;
	private SKPoint _start;
	private SKPoint _startGuidePoint;

	public override DragOperationKind Kind => DragOperationKind.MoveGuide;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide = ctx.SelectedGuide;
		if (_guide == null)
			return;

		_start = pagePoint;
		_startGuidePoint = _guide.GuidePoint;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (_guide == null)
			return;

		float dx = pagePoint.X - _start.X;
		float dy = pagePoint.Y - _start.Y;

		var candidate = new SKPoint(_startGuidePoint.X + dx, _startGuidePoint.Y + dy);

		_guide.GuidePoint = GuideSnapping.Snap(view, ctx, _guide, candidate);

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}
}

public sealed class RotateGuideOperation : DragOperation
{
	private GuideLine? _guide;
	private float _startAngleDeg;
	private float _startMouseAngleDeg;
	private SKPoint _pivot;

	public override DragOperationKind Kind => DragOperationKind.RotateGuide;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_guide = ctx.SelectedGuide;
		if (_guide == null)
			return;

		_pivot = _guide.GetRotationCenterPage();
		_startAngleDeg = _guide.AngleDeg;
		_startMouseAngleDeg = GetMouseAngle(_pivot, pagePoint);
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (_guide == null)
			return;

		float currentMouseAngle = GetMouseAngle(_pivot, pagePoint);
		_guide.AngleDeg = _startAngleDeg + (currentMouseAngle - _startMouseAngleDeg);
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	private static float GetMouseAngle(SKPoint center, SKPoint p)
	{
		return MathF.Atan2(p.Y - center.Y, p.X - center.X) * 180f / MathF.PI;
	}
}