using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

public readonly record struct SnapPointCandidate(
	Shape Owner,
	SKPoint WorldPoint);

public readonly record struct SnapLineCandidate(
	Shape Owner,
	SKPoint A,
	SKPoint B,
	bool Infinite = false);

public readonly record struct SnapLineSource(
	SKPoint A,
	SKPoint B);

public sealed class SnapResult
{
	public SnapKind Kind { get; init; }
	public SKPoint SnappedWorldPoint { get; init; }
	public SKPoint TargetWorldPoint { get; init; }
	public SKPoint Correction { get; init; }

	public SKPoint? DisplayFromWorldPoint { get; init; }

	public SKPoint? LineA { get; init; }
	public SKPoint? LineB { get; init; }

	public bool SnapX { get; init; }   // true, если выравнивание произошло по X
	public bool SnapY { get; init; }   // true, если выравнивание произошло по Y

	/// <summary>
	/// Цель и точка отсчёта ВТОРОЙ динамической направляющей.
	///
	/// Выравнивание по горизонтали и по вертикали приходит от разных
	/// фигур: по вертикали совместились с одной, по горизонтали с другой.
	/// Одной пары точек для этого мало — ею описывается только одна линия,
	/// и вторая направляющая ни рисовалась, ни учитывалась.
	///
	/// null означает, что направляющая одна, и рисовать нужно по основным
	/// TargetWorldPoint и DisplayFromWorldPoint.
	/// </summary>
	public SKPoint? SecondTargetWorldPoint { get; init; }

	public SKPoint? SecondDisplayFromWorldPoint { get; init; }

	public float DistancePage { get; init; }

	/// <summary>
	/// Прилипание пришло от динамической направляющей, а не от объекта
	/// и не от направляющей, поставленной пользователем.
	///
	/// Нужно приоритету: направляющая, проведённая руками, — ось чертежа
	/// и должна перебивать любое динамическое совпадение, но НЕ должна
	/// перебивать точку на самой фигуре. Без этого признака отличить
	/// одно от другого на выходе поиска было нельзя.
	/// </summary>
	public bool FromDynamicGuide { get; init; }

	public bool IsValid => Kind != SnapKind.None;
}

public static class ShapeSnapGeometry
{
	/// <summary>
	/// Точки прилипания фигуры.
	///
	/// knownBounds — уже посчитанные мировые габариты, если они есть
	/// у вызывающего. Нужны ветке по умолчанию: она строит точки именно
	/// из габаритов, а GetBounds у кривой, размера и текста в области —
	/// это построение всего мирового пути. Поиск подсвеченного узла считает
	/// габариты сам и передаёт их сюда, иначе на каждое движение мыши
	/// выходило бы по два построения на фигуру вместо одного.
	/// </summary>
	public static IEnumerable<SnapPointCandidate> GetSnapPoints(Shape shape, SKRect? knownBounds = null)
	{
		if (shape == null || !shape.IsVisible)
			yield break;

		switch (shape)
		{
			case GuideLine:
				yield break;

			case RectangleShape rect:
				foreach (var p in GetRectPoints(rect.LocalRect).Select(rect.LocalToWorld))
					yield return new SnapPointCandidate(rect, p);
				yield break;

			case RoundedRectangleShape rr:
				foreach (var p in GetRectPoints(rr.LocalRect).Select(rr.LocalToWorld))
					yield return new SnapPointCandidate(rr, p);
				yield break;

			case ChamferedRectangleShape cr:
				foreach (var p in GetChamferedRectPoints(cr).Select(cr.LocalToWorld))
					yield return new SnapPointCandidate(cr, p);
				yield break;

			case TextShape areaText when areaText.InsideOtherShape && areaText.InsideShape != null:
				{
					// Габариты самого текста потребовали бы полной раскладки.
					// Контейнер описывает ту же область и стоит несравнимо дешевле.
					var b = areaText.InsideShape.GetBounds();

					foreach (var p in GetRectPoints(b))
						yield return new SnapPointCandidate(areaText, p);
				}
				yield break;

			case TextShape txt:
				foreach (var p in GetRectPoints(txt.GetLocalBounds()).Select(txt.LocalToWorld))
					yield return new SnapPointCandidate(txt, p);
				yield break;

			// Размер отдаёт только две измеряемые точки.
			//
			// Без этого случая он попадал в default и отдавал углы габаритов,
			// а GetBounds у него строит весь мировой путь — вместе с контурами глифов
			// подписи. На каждое движение мыши и на каждый размер на странице.
			// Сами точки — всего два умножения на матрицу.
			case MeasurementShape measurement:
				yield return new SnapPointCandidate(measurement, measurement.LocalToWorld(measurement.P1));
				yield return new SnapPointCandidate(measurement, measurement.LocalToWorld(measurement.P2));
				yield break;

			case Circle2PShape c2:
				{
					var center = c2.LocalToWorld(c2.CenterPoint);
					var radius = c2.LocalToWorld(c2.RadiusPoint);
					yield return new SnapPointCandidate(c2, center);
					yield return new SnapPointCandidate(c2, radius);
					yield break;
				}

			case Circle3PShape c3:
				yield return new SnapPointCandidate(c3, c3.LocalToWorld(c3.P1));
				yield return new SnapPointCandidate(c3, c3.LocalToWorld(c3.P2));
				yield return new SnapPointCandidate(c3, c3.LocalToWorld(c3.P3));
				{
					var b = c3.GetBounds();
					yield return new SnapPointCandidate(c3, new SKPoint(b.MidX, b.MidY));
				}
				yield break;

			case Ellipse3PShape e3:
				yield return new SnapPointCandidate(e3, e3.LocalToWorld(e3.CenterPoint));
				yield return new SnapPointCandidate(e3, e3.LocalToWorld(e3.RadiusPoint1));
				yield return new SnapPointCandidate(e3, e3.LocalToWorld(e3.RadiusPoint2));
				yield break;

			case Ellipse5PShape e5:
				foreach (var p in e5.Points.Select(e5.LocalToWorld))
					yield return new SnapPointCandidate(e5, p);
				{
					var b = e5.GetBounds();
					yield return new SnapPointCandidate(e5, new SKPoint(b.MidX, b.MidY));
				}
				yield break;

			case PolygonShape polygon:
				polygon.InitializeFromPointsIfNeeded();
				if (polygon.Points.Count > 0)
				{
					foreach (var p in polygon.Points.Select(polygon.LocalToWorld))
						yield return new SnapPointCandidate(polygon, p);

					for (int i = 0; i < polygon.Points.Count; i++)
					{
						int j = (i + 1) % polygon.Points.Count;
						var a = polygon.LocalToWorld(polygon.Points[i]);
						var b = polygon.LocalToWorld(polygon.Points[j]);
						yield return new SnapPointCandidate(polygon, Mid(a, b));
					}

					yield return new SnapPointCandidate(
						polygon,
						polygon.LocalToWorld(polygon.LocalCenter));
				}
				yield break;

			case CurveShape curve:
				if (curve.Nodes.Count > 0)
				{
					foreach (var n in curve.Nodes)
						yield return new SnapPointCandidate(curve, curve.LocalToWorld(n.Position));

					// Середины сегментов — по контурам и по самой кривой.
					//
					// Прежде список узлов перебирался подряд, от первого
					// до последнего, и середина бралась как полусумма
					// соседей. Отсюда две беды, и обе выглядели как точка,
					// взявшаяся ниоткуда.
					//
					// Первая: список узлов хранит ВСЕ контуры фигуры подряд.
					// Между последним узлом одного контура и первым узлом
					// следующего никакого сегмента нет, а полусумма их
					// координат исправно выдавала точку — где-нибудь посреди
					// фигуры, вдали от любой линии.
					//
					// Вторая: полусумма концов — это середина ХОРДЫ, а не
					// кривой. У лепестка, нарисованного одним сегментом
					// с длинными ручками, хорда проходит поперёк него,
					// и её середина оказывается внутри лепестка, тогда
					// как сама кривая идёт вокруг.
					//
					// Теперь сегменты берутся из контуров — вместе
					// с замыкающим у замкнутых, которого прежде не было
					// вовсе, — а точка считается как значение кубики
					// в середине параметра.
					foreach (var range in curve.GetContourRangesForAdorner())
					{
						for (int i = range.Start; i < range.End; i++)
						{
							yield return new SnapPointCandidate(
								curve,
								curve.LocalToWorld(SegmentMidpoint(curve.Nodes[i], curve.Nodes[i + 1])));
						}

						if (range.Closed && range.End > range.Start)
						{
							yield return new SnapPointCandidate(
								curve,
								curve.LocalToWorld(SegmentMidpoint(curve.Nodes[range.End], curve.Nodes[range.Start])));
						}
					}
				}
				yield break;

			default:
				{
					var b = knownBounds ?? shape.GetBounds();

					// Вырожденные габариты пропускаются.
					//
					// У фигуры без геометрии — пустого текста, кривой без
					// узлов, группы без детей — GetBounds отдаёт SKRect.Empty,
					// то есть прямоугольник (0,0,0,0). Девять его точек
					// сходятся в одну, и она лежит в НАЧАЛЕ КООРДИНАТ
					// страницы. На экране появлялась точка прилипания
					// посреди пустого места, никак не связанная с фигурой,
					// и объяснить её происхождение было нечем.
					if (b.Width <= 0f && b.Height <= 0f)
						yield break;

					foreach (var p in GetRectPoints(b))
						yield return new SnapPointCandidate(shape, p);
				}
				yield break;
		}
	}

	public static IEnumerable<SnapLineCandidate> GetSnapLines(Shape shape)
	{
		if (shape == null || !shape.IsVisible)
			yield break;

		switch (shape)
		{
			case GuideLine guide:
				{
					// Направление спрашивается у самой направляющей, а
					// не считается здесь заново. Формула была повторена
					// слово в слово, и правка знака для вертикальной
					// ориентации прошла бы мимо этого места: линия
					// прилипания разошлась бы с нарисованной линией.
					SKPoint dir = guide.GetDirection();

					const float huge = 100000f;
					var c = guide.GuidePoint;
					yield return new SnapLineCandidate(
						guide,
						new SKPoint(c.X - dir.X * huge, c.Y - dir.Y * huge),
						new SKPoint(c.X + dir.X * huge, c.Y + dir.Y * huge));
					yield break;
				}

			case RectangleShape rect:
				foreach (var seg in GetRectSegments(rect.LocalRect, rect.LocalToWorld))
					yield return new SnapLineCandidate(rect, seg.A, seg.B);
				yield break;

			case RoundedRectangleShape rr:
				foreach (var seg in GetRectSegments(rr.LocalRect, rr.LocalToWorld))
					yield return new SnapLineCandidate(rr, seg.A, seg.B);
				yield break;

			case ChamferedRectangleShape cr:
				foreach (var seg in GetPolygonSegments(GetChamferedRectPoints(cr), cr.LocalToWorld, true))
					yield return new SnapLineCandidate(cr, seg.A, seg.B);
				yield break;

			case TextShape areaText when areaText.InsideOtherShape && areaText.InsideShape != null:
				{
					var b = areaText.InsideShape.GetBounds();

					foreach (var seg in GetRectSegments(b, p => p))
						yield return new SnapLineCandidate(areaText, seg.A, seg.B);
				}
				yield break;

			case TextShape txt:
				foreach (var seg in GetRectSegments(txt.GetLocalBounds(), txt.LocalToWorld))
					yield return new SnapLineCandidate(txt, seg.A, seg.B);
				yield break;

			// Измеряемый отрезок, и больше ничего. Выносные и размерная
			// линии — оформление подписи, прилипать к ним незачем, а их построение
			// тянет за собой раскладку текста.
			case MeasurementShape measurement:
				yield return new SnapLineCandidate(
					measurement,
					measurement.LocalToWorld(measurement.P1),
					measurement.LocalToWorld(measurement.P2));
				yield break;

			case PolygonShape polygon:
				polygon.InitializeFromPointsIfNeeded();
				foreach (var seg in GetPolygonSegments(polygon.Points, polygon.LocalToWorld, polygon.Points.Count >= 3))
					yield return new SnapLineCandidate(polygon, seg.A, seg.B);
				yield break;

			case CurveShape curve:
				// Только ПРЯМЫЕ отрезки и только внутри контура.
				//
				// Прежде список узлов проходился подряд, и на каждую пару
				// соседей выдавалась прямая через их положения. Для изогнутого
				// сегмента это хорда: кривая идёт стороной, а прилипание
				// держит направление, которого на чертеже нет. Для пары узлов
				// из РАЗНЫХ контуров отрезка нет вовсе, а прямая всё равно
				// выдавалась — через всю фигуру.
				//
				// Прямой сегмент — тот, у которого на концах нет ручек.
				foreach (var range in curve.GetContourRangesForAdorner())
				{
					for (int i = range.Start; i < range.End; i++)
					{
						if (!IsStraightSegment(curve.Nodes[i], curve.Nodes[i + 1]))
							continue;

						yield return new SnapLineCandidate(
							curve,
							curve.LocalToWorld(curve.Nodes[i].Position),
							curve.LocalToWorld(curve.Nodes[i + 1].Position));
					}

					// Замыкающий отрезок: у замкнутого контура он такая же
					// сторона фигуры, как все прочие.
					if (range.Closed
						&& range.End > range.Start
						&& IsStraightSegment(curve.Nodes[range.End], curve.Nodes[range.Start]))
					{
						yield return new SnapLineCandidate(
							curve,
							curve.LocalToWorld(curve.Nodes[range.End].Position),
							curve.LocalToWorld(curve.Nodes[range.Start].Position));
					}
				}
				yield break;
		}
	}

	/// <summary>
	/// Углы, середины сторон и центр прямоугольника — девять точек.
	///
	/// Открыто наружу ради ShapeNodePicker: там из габаритов фигуры
	/// и всего выделения строятся точки отсчёта прилипания, и повторять
	/// этот список вторым экземпляром значило бы однажды получить два
	/// разных набора точек для одного и того же прямоугольника.
	/// </summary>
	public static IEnumerable<SKPoint> GetBoundsPoints(SKRect r) => GetRectPoints(r);

	/// <summary>
	/// Прямой ли сегмент между двумя узлами.
	///
	/// Признак один: на концах сегмента нет ручек. Тип узлов не
	/// спрашивается, хотя при построении пути он и участвует — там
	/// без ручек строится вырожденная кубика, у которой обе опорные
	/// точки сидят на концах. Рисуется она прямой линией, и считать
	/// её изогнутой значило бы потерять направляющую у ровной стороны:
	/// у узла, сведённого из двух, тип остаётся Tangent, а ручек нет.
	///
	/// HasNextControl и HasPrevControl сами требуют непрямого типа,
	/// поэтому отдельная проверка типа ничего бы не добавила.
	/// </summary>
	private static bool IsStraightSegment(CurveNode a, CurveNode b)
	{
		return !a.HasNextControl && !b.HasPrevControl;
	}

	/// <summary>
	/// Середина сегмента кривой — точка НА кривой, при t = 0.5.
	///
	/// Правило «прямой или кубика» то же, что при построении пути:
	/// разойдись они, и точка прилипания сошла бы с нарисованной линии.
	///
	/// Для кубики B(0.5) = (P0 + 3·C1 + 3·C2 + P3) / 8 — это просто
	/// формула Безье с подставленной половиной, без вычислений в цикле.
	/// </summary>
	private static SKPoint SegmentMidpoint(CurveNode a, CurveNode b)
	{
		if (IsStraightSegment(a, b))
			return Mid(a.Position, b.Position);

		var c1 = a.HasNextControl ? a.ControlNext : a.Position;
		var c2 = b.HasPrevControl ? b.ControlPrev : b.Position;

		return new SKPoint(
			(a.Position.X + 3f * c1.X + 3f * c2.X + b.Position.X) * 0.125f,
			(a.Position.Y + 3f * c1.Y + 3f * c2.Y + b.Position.Y) * 0.125f);
	}

	private static IEnumerable<SKPoint> GetRectPoints(SKRect r)
	{
		yield return new SKPoint(r.Left, r.Top);
		yield return new SKPoint(r.MidX, r.Top);
		yield return new SKPoint(r.Right, r.Top);
		yield return new SKPoint(r.Right, r.MidY);
		yield return new SKPoint(r.Right, r.Bottom);
		yield return new SKPoint(r.MidX, r.Bottom);
		yield return new SKPoint(r.Left, r.Bottom);
		yield return new SKPoint(r.Left, r.MidY);
		yield return new SKPoint(r.MidX, r.MidY);
	}

	private static List<SKPoint> GetChamferedRectPoints(ChamferedRectangleShape cr)
	{
		var r = cr.LocalRect;
		float cx = ShapePropertyAdorner.GetEffectiveChamferX(cr);
		float cy = ShapePropertyAdorner.GetEffectiveChamferY(cr);

		return new List<SKPoint>
		{
			new(r.Left + cx, r.Top),
			new(r.Right - cx, r.Top),
			new(r.Right, r.Top + cy),
			new(r.Right, r.Bottom - cy),
			new(r.Right - cx, r.Bottom),
			new(r.Left + cx, r.Bottom),
			new(r.Left, r.Bottom - cy),
			new(r.Left, r.Top + cy),
			new(r.MidX, r.MidY)
		};
	}

	private static IEnumerable<(SKPoint A, SKPoint B)> GetRectSegments(SKRect r, Func<SKPoint, SKPoint> map)
	{
		var p1 = map(new SKPoint(r.Left, r.Top));
		var p2 = map(new SKPoint(r.Right, r.Top));
		var p3 = map(new SKPoint(r.Right, r.Bottom));
		var p4 = map(new SKPoint(r.Left, r.Bottom));

		yield return (p1, p2);
		yield return (p2, p3);
		yield return (p3, p4);
		yield return (p4, p1);
	}

	private static IEnumerable<(SKPoint A, SKPoint B)> GetPolygonSegments(IReadOnlyList<SKPoint> pts, Func<SKPoint, SKPoint> map, bool closed)
	{
		if (pts == null || pts.Count < 2)
			yield break;

		for (int i = 0; i < pts.Count - 1; i++)
			yield return (map(pts[i]), map(pts[i + 1]));

		if (closed)
			yield return (map(pts[pts.Count - 1]), map(pts[0]));
	}

	private static SKPoint Mid(SKPoint a, SKPoint b)
		=> new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
}

public static class ObjectSnapEngine
{
	/// <summary>
	/// Включена ли хоть одна ветка прилипания.
	///
	/// Веток три, и они независимы: узлы и рёбра фигур, направляющие,
	/// динамические направляющие. Вызывающие прежде спрашивали только
	/// про первую — Settings.snap_objects — и, выключив её, пользователь
	/// разом лишался и направляющих, и сетки, и выравнивания по соседям,
	/// хотя выключал он совсем другое.
	///
	/// Внутри движка каждая ветка проверяет свой признак сама, поэтому
	/// звать его достаточно, когда включена хотя бы одна.
	/// </summary>
	public static bool AnySnappingEnabled =>
		Settings.snap_objects || Settings.snap_to_guidelines || Settings.dynamic_guidelines;

	/// <summary>
	/// Прилипание одной движущейся точки.
	///
	/// selfShape — фигура, которую правят прямо сейчас. Её собственные
	/// точки и линии участвуют как цели, кроме перечисленных в ignoreSelf:
	/// там лежит сама перетаскиваемая точка и её ближайшее окружение.
	///
	/// Нужно правке узлов кривой. Прежде фигура исключалась целиком,
	/// и узел не притягивался ни к другим узлам ТОЙ ЖЕ кривой, ни
	/// к продолжениям её отрезков — а именно это и нужно, когда строят
	/// фигуру по себе самой: ставят вершину вровень с противоположной,
	/// продолжают линию под тем же углом. Совсем не исключать фигуру
	/// нельзя: ближайшей целью к перетаскиваемому узлу всегда оказался бы
	/// он сам, и узел прилип бы к своему же положению и не сдвинулся.
	/// </summary>
	public static SnapResult? SnapMovingPoint(
	MainView view,
	IEnumerable<Shape> excludeShapes,
	SKPoint movingPointWorld,
	float snapDistPx,
	Shape? selfShape = null,
	IReadOnlyList<SKPoint>? ignoreSelf = null,
	IReadOnlyList<SnapLineCandidate>? selfLines = null)
	{
		if (view.CurrentPage == null)
			return null;

		// BuildExcluded, а не простой HashSet: кроме самих переданных фигур нужно
		// исключить те, чья геометрия от них зависит. Заодно восстанавливается
		// сравнение по ссылке: без него фигуры сравнивались бы через Equals.
		var excluded = BuildExcluded(view, excludeShapes);
		float snapDistPage = ScreenDistanceToPageDistance(view, snapDistPx);
		float snapDistSq = snapDistPage * snapDistPage;

		var bestOrigin = FindBestPageOriginSnap(view, new[] { movingPointWorld }, snapDistPage);
		if (bestOrigin != null)
			return bestOrigin;

		var scenePoints = new List<SnapPointCandidate>();
		var sceneLines = new List<SnapLineCandidate>();

		scenePoints.AddRange(GetPageSnapPoints(view));
		sceneLines.AddRange(GetPageSnapLines(view));

		if (Settings.snap_to_guidelines)
		{
			scenePoints.AddRange(GetGuideIntersectionSnapPoints(view, excluded));

			foreach (var shape in view.GetShapesBackToFront())
			{
				if (shape is not GuideLine guide)
					continue;

				if (!guide.IsVisible || guide.IsLocked)
					continue;

				if (excluded.Contains(guide))
					continue;

				sceneLines.AddRange(ShapeSnapGeometry.GetSnapLines(guide));
			}
		}

		if (Settings.snap_objects)
		{
			foreach (var shape in view.GetShapesBackToFront())
			{
				if (!shape.IsVisible || shape.IsLocked)
					continue;

				if (excluded.Contains(shape))
					continue;

				if (shape is GuideLine)
					continue;

				scenePoints.AddRange(ShapeSnapGeometry.GetSnapPoints(shape));
				sceneLines.AddRange(ShapeSnapGeometry.GetSnapLines(shape));
			}
		}

		// Правимая фигура разбирается отдельно от чужих и по двум признакам
		// сразу: её узлы — обычное прилипание к объектам, направления её же
		// отрезков — динамические направляющие.
		AddSelfPoints(selfShape, ignoreSelf, selfLines, scenePoints, sceneLines);

		// Пересечения линий — такие же точки прилипания, как узлы фигур.
		//
		// Прежде перебирались только сами линии и только по отдельности:
		// точка садилась на одну из них, а вторая координата оставалась
		// свободной. Между тем совмещение «по вертикали от одной фигуры
		// и по горизонтали от другой» — самое частое, ради чего
		// динамические направляющие и заводят, и перекрестье двух из них
		// задаёт его сразу обеими координатами.
		AddLineIntersections(sceneLines, movingPointWorld, snapDistPage, scenePoints);

		SnapResult? bestP2P = null;
		foreach (var target in scenePoints)
		{
			float d2 = DistSq(movingPointWorld, target.WorldPoint);
			if (d2 > snapDistSq)
				continue;

			float d = MathF.Sqrt(d2);
			if (bestP2P == null || d < bestP2P.DistancePage)
			{
				bestP2P = new SnapResult
				{
					Kind = SnapKind.PointToPoint,
					SnappedWorldPoint = movingPointWorld,
					TargetWorldPoint = target.WorldPoint,
					Correction = Sub(target.WorldPoint, movingPointWorld),
					DistancePage = d,
					SnapX = true,
					SnapY = true
				};
			}
		}

		if (bestP2P != null)
			return bestP2P;

		SnapResult? bestP2L = null;
		foreach (var line in sceneLines)
		{
			// Бесконечная прямая у направляющих и у снимка собственных
			// отрезков правимой фигуры, отрезок — у всего остального.
			//
			// Снимок нужен бесконечным потому, что узел ведут В ПРОДОЛЖЕНИЕ
			// отрезка, а не вдоль него: ставят вершину на одной прямой
			// с уже построенной стороной, уходя за её конец. Ограничься
			// прилипание длиной отрезка, оно пропадало бы ровно там, где
			// нужнее всего.
			var proj = line.Infinite || line.Owner is GuideLine
				? ProjectPointToInfiniteLine(movingPointWorld, line.A, line.B)
				: ProjectPointToSegment(movingPointWorld, line.A, line.B);

			float d2 = DistSq(movingPointWorld, proj);
			if (d2 > snapDistSq)
				continue;

			float d = MathF.Sqrt(d2);
			if (bestP2L == null || d < bestP2L.DistancePage)
			{
				bestP2L = new SnapResult
				{
					Kind = SnapKind.PointToLine,
					SnappedWorldPoint = movingPointWorld,
					TargetWorldPoint = proj,
					Correction = Sub(proj, movingPointWorld),
					DistancePage = d,
					LineA = line.A,
					LineB = line.B,
					SnapX = false,
					SnapY = false
				};
			}
		}

		if (bestP2L != null)
			return bestP2L;

		// Сетка — последняя по приоритету.
		//
		// Узлы и рёбра чужих фигур важнее: к ним привязываются осознанно,
		// а сетка покрывает лист целиком, и выиграй она — точное совмещение
		// с соседней фигурой стало бы невозможным всюду, кроме самих узлов.
		return PageGrid.SnapPoint(view.CurrentPage, movingPointWorld, snapDistPage, GridPxPerUom(view));
	}

	public static SnapResult? SnapMoveSelection(
		MainView view,
		IEnumerable<Shape> excludeShapes,
		SKRect movedSelectionBounds,
		float snapDistPx)
	{
		if (view.CurrentPage == null || movedSelectionBounds.IsEmpty)
			return null;

		var excluded = BuildExcluded(view, excludeShapes);
		float snapDistPage = ScreenDistanceToPageDistance(view, snapDistPx);

		var sourcePoints = GetBoundsSnapPoints(movedSelectionBounds).ToList();

		var bestOrigin = FindBestPageOriginSnap(view, sourcePoints, snapDistPage);
		if (bestOrigin != null)
			return bestOrigin;

		var bestPoint = FindBestPointLikeSnap(view, excluded, sourcePoints, snapDistPage);
		if (bestPoint != null)
			return bestPoint;

		var bestAxis = FindBestAxisSnap(view, excluded, sourcePoints, snapDistPage);
		if (bestAxis != null)
		{
			return new SnapResult
			{
				Kind = bestAxis.Kind,
				SnappedWorldPoint = bestAxis.SnappedWorldPoint,
				DisplayFromWorldPoint = FindBestDisplayFromOnBounds(
					movedSelectionBounds,
					bestAxis.Correction,
					bestAxis.TargetWorldPoint,
					bestAxis.SnapX,
					bestAxis.SnapY),
				TargetWorldPoint = bestAxis.TargetWorldPoint,
				Correction = bestAxis.Correction,
				LineA = bestAxis.LineA,
				LineB = bestAxis.LineB,
				SnapX = bestAxis.SnapX,
				SnapY = bestAxis.SnapY,
				DistancePage = bestAxis.DistancePage
			};
		}

		var bestLine = FindBestLineLikeSnap(view, excluded, sourcePoints, snapDistPage);
		if (bestLine != null)
			return bestLine;

		return PageGrid.SnapBounds(view.CurrentPage, movedSelectionBounds, snapDistPage, GridPxPerUom(view));
	}

	public static SnapResult? SnapResizeSelectionHandle(
		MainView view,
		IEnumerable<Shape> excludeShapes,
		HandleKind handle,
		SKRect selectionStartBounds,
		SKPoint currentHandlePoint,
		float snapDistPx)
	{
		if (view.CurrentPage == null || selectionStartBounds.IsEmpty)
			return null;

		return SnapSinglePoint(view, excludeShapes, currentHandlePoint, snapDistPx);
	}

	/// <summary>
	/// Прилипание ОДНОЙ точки ко всему, к чему включено прилипание:
	/// началу координат, узлам и рёбрам чужих фигур, направляющим,
	/// динамическим направляющим и сетке — в этом порядке важности.
	///
	/// Отличается от SnapMovingPoint тем, что учитывает динамические
	/// направляющие: та функция создавалась для построения фигуры, где
	/// выравнивание по осям соседей только мешает выбирать точку.
	/// Здесь же тянут готовую фигуру, и выравнивание по соседям — ровно
	/// то, чего от перетаскивания ждут.
	/// </summary>
	public static SnapResult? SnapSinglePoint(
		MainView view,
		IEnumerable<Shape> excludeShapes,
		SKPoint pointWorld,
		float snapDistPx)
	{
		if (view.CurrentPage == null)
			return null;

		var excluded = BuildExcluded(view, excludeShapes);
		float snapDistPage = ScreenDistanceToPageDistance(view, snapDistPx);

		var sourcePoints = new[] { pointWorld };

		var bestOrigin = FindBestPageOriginSnap(view, sourcePoints, snapDistPage);
		if (bestOrigin != null)
			return bestOrigin;

		// Направляющая ищется заранее, до всех динамических совпадений.
		//
		// Порядок важнее расстояния. Направляющую пользователь провёл сам
		// и провёл осознанно — это ось чертежа. Динамическая появляется
		// на любой кромке любой фигуры и живёт до конца протяжки. Пока
		// они соревновались по близости, случайное выравнивание с соседней
		// фигурой, оказавшееся ближе на доли миллиметра, перебивало
		// направляющую, и сесть на ось удавалось не с первого раза.
		//
		// Точку на самой фигуре направляющая при этом НЕ перебивает:
		// точка задаёт сразу обе координаты и точнее любой линии.
		var bestGuide = Settings.snap_to_guidelines
			? FindBestGuideLineSnap(view, excluded, sourcePoints, snapDistPage)
			: null;

		var bestPoint = FindBestPointLikeSnap(
			view,
			excluded,
			sourcePoints,
			snapDistPage);

		if (bestPoint != null && !(bestGuide != null && bestPoint.FromDynamicGuide))
			return bestPoint;

		if (bestGuide != null)
			return bestGuide;

		var bestAxis = FindBestAxisSnap(
			view,
			excluded,
			sourcePoints,
			snapDistPage);

		// Выравнивание сразу по обеим осям идёт ВПЕРЁД одиночной точки.
		//
		// Такое совпадение описывает перекрестье двух направляющих —
		// по вертикали с одной фигурой, по горизонтали с другой, — и задаёт
		// положение полностью. Одиночная точка задаёт его не хуже, но
		// встречается чаще, и пока она шла первой, до перекрестья очередь
		// не доходила вовсе.
		if (bestAxis != null && bestAxis.SnapX && bestAxis.SnapY)
			return bestAxis;

		if (bestAxis != null && Settings.dynamic_guidelines)
			return bestAxis;

		var bestLine = FindBestLineLikeSnap(
			view,
			excluded,
			sourcePoints,
			snapDistPage);

		if (bestLine != null)
			return bestLine;

		// Сетка последняя: она покрывает лист целиком, и выиграй она —
		// точное совмещение с соседней фигурой стало бы невозможным.
		return PageGrid.SnapPoint(view.CurrentPage, pointWorld, snapDistPage, GridPxPerUom(view));
	}

	/// <summary>Перевод экранного расстояния в единицы страницы.</summary>
	public static float ScreenToPageDistance(MainView view, float px)
		=> ScreenDistanceToPageDistance(view, px);

	/// <summary>
	/// Экранных пикселей на единицу страницы. Нужно привязке к сетке,
	/// чтобы взять тот же прореженный шаг, каким сетка нарисована.
	///
	/// Через ScreenPixelsToPageUnits, потому что множитель масштаба
	/// в MainView закрыт, а эта величина — обратная к нему.
	/// </summary>
	private static float GridPxPerUom(MainView view)
	{
		float uomPerPx = view.ScreenPixelsToPageUnits(1f);
		return MathF.Abs(uomPerPx) < 1e-9f ? 0f : 1f / uomPerPx;
	}

	private static SnapResult? FindBestPointLikeSnap(
		MainView view,
		HashSet<Shape> excluded,
		IReadOnlyList<SKPoint> sourcePoints,
		float snapDistPage)
	{
		float snapDistSq = snapDistPage * snapDistPage;
		SnapResult? best = null;

		// Перекрестья линий — такие же цели, как узлы фигур.
		//
		// Прежде динамические направляющие работали поодиночке: фигура
		// садилась на одну из них, а вторая координата оставалась
		// свободной. Между тем совмещение «по вертикали от одной фигуры
		// и по горизонтали от другой» — самое частое, ради чего их
		// и заводят, и перекрестье задаёт его сразу обеими координатами.
		//
		// Перекрестья проверяются ПЕРВЫМИ среди точек: из двух целей
		// на одном расстоянии перекрестье осмысленнее одиночной линии,
		// потому что фиксирует обе координаты, а не одну.
		foreach (var cross in GetLineCrossPoints(view, excluded, sourcePoints, snapDistPage))
		{
			foreach (var source in sourcePoints)
			{
				float d2 = DistSq(source, cross.WorldPoint);
				if (d2 > snapDistSq)
					continue;

				float d = MathF.Sqrt(d2);
				if (best == null || d < best.DistancePage)
				{
					best = new SnapResult
					{
						Kind = SnapKind.PointToPoint,
						SnappedWorldPoint = source,
						TargetWorldPoint = cross.WorldPoint,
						Correction = Sub(cross.WorldPoint, source),
						DistancePage = d,
						SnapX = true,
						SnapY = true,
						FromDynamicGuide = true
					};
				}
			}
		}

		if (Settings.dynamic_guidelines)
		{
			foreach (var target in GetPageSnapPoints(view))
			{
				foreach (var source in sourcePoints)
				{
					float d2 = DistSq(source, target.WorldPoint);
					if (d2 > snapDistSq)
						continue;

					float d = MathF.Sqrt(d2);
					if (best == null || d < best.DistancePage)
					{
						best = new SnapResult
						{
							Kind = SnapKind.PointToPoint,
							SnappedWorldPoint = source,
							TargetWorldPoint = target.WorldPoint,
							Correction = Sub(target.WorldPoint, source),
							DistancePage = d,
							SnapX = true,
							SnapY = true,
							FromDynamicGuide = true
						};
					}
				}
			}
		}

		if (Settings.snap_to_guidelines)
		{
			foreach (var target in GetGuideIntersectionSnapPoints(view, excluded))
			{
				foreach (var source in sourcePoints)
				{
					float d2 = DistSq(source, target.WorldPoint);
					if (d2 > snapDistSq)
						continue;

					float d = MathF.Sqrt(d2);
					if (best == null || d < best.DistancePage)
					{
						best = new SnapResult
						{
							Kind = SnapKind.PointToPoint,
							SnappedWorldPoint = source,
							TargetWorldPoint = target.WorldPoint,
							Correction = Sub(target.WorldPoint, source),
							DistancePage = d,
							SnapX = true,
							SnapY = true
						};
					}
				}
			}
		}

		if (Settings.snap_objects)
		{
			foreach (var shape in view.GetShapesBackToFront())
			{
				if (!shape.IsVisible || shape.IsLocked)
					continue;

				if (excluded.Contains(shape))
					continue;

				if (shape is GuideLine)
					continue;

				foreach (var target in ShapeSnapGeometry.GetSnapPoints(shape))
				{
					foreach (var source in sourcePoints)
					{
						float d2 = DistSq(source, target.WorldPoint);
						if (d2 > snapDistSq)
							continue;

						float d = MathF.Sqrt(d2);
						if (best == null || d < best.DistancePage)
						{
							best = new SnapResult
							{
								Kind = SnapKind.PointToPoint,
								SnappedWorldPoint = source,
								TargetWorldPoint = target.WorldPoint,
								Correction = Sub(target.WorldPoint, source),
								DistancePage = d,
								SnapX = true,
								SnapY = true
							};
						}
					}
				}
			}
		}

		return best;
	}

	/// <summary>
	/// Выравнивание по осям — то, что видно как динамические направляющие.
	///
	/// Лучшее совпадение по горизонтали и лучшее по вертикали ищутся
	/// НЕЗАВИСИМО и, если нашлись оба, применяются вместе.
	///
	/// Прежде хранилось одно лучшее на двоих: нашлось совпадение по X —
	/// совпадение по Y отбрасывалось, даже когда оно было не хуже. Фигура
	/// садилась на одну линию, вторая координата оставалась свободной,
	/// на экране была одна направляющая вместо двух, и попасть в перекрестье
	/// было нельзя в принципе.
	///
	/// Цели берутся только из видимой области. Линия, протянувшаяся
	/// от фигуры за краем экрана, выглядит пришедшей ниоткуда: совмещаться
	/// не с чем, объяснения на экране нет, а фигура при этом прыгает.
	/// </summary>
	private static SnapResult? FindBestAxisSnap(
		MainView view,
		HashSet<Shape> excluded,
		IReadOnlyList<SKPoint> sourcePoints,
		float snapDistPage)
	{
		if (!Settings.dynamic_guidelines)
			return null;

		var visible = view.GetVisibleWorldRectForSnapping();

		float bestDx = float.MaxValue;
		float bestDy = float.MaxValue;

		SKPoint bestXTarget = SKPoint.Empty;
		SKPoint bestYTarget = SKPoint.Empty;
		SKPoint bestXSource = SKPoint.Empty;
		SKPoint bestYSource = SKPoint.Empty;
		float bestXCorrection = 0f;
		float bestYCorrection = 0f;

		bool hasX = false;
		bool hasY = false;

		void Consider(SKPoint target)
		{
			// За краем экрана цель не годится: направляющую от неё
			// пользователь не увидит.
			if (!visible.IsEmpty && !visible.Contains(target.X, target.Y))
				return;

			foreach (var source in sourcePoints)
			{
				float dx = target.X - source.X;
				float dy = target.Y - source.Y;

				float adx = MathF.Abs(dx);
				float ady = MathF.Abs(dy);

				if (adx <= snapDistPage && adx < bestDx)
				{
					bestDx = adx;
					bestXTarget = target;
					bestXSource = source;
					bestXCorrection = dx;
					hasX = true;
				}

				if (ady <= snapDistPage && ady < bestDy)
				{
					bestDy = ady;
					bestYTarget = target;
					bestYSource = source;
					bestYCorrection = dy;
					hasY = true;
				}
			}
		}

		foreach (var target in GetPageSnapPoints(view))
			Consider(target.WorldPoint);

		if (Settings.snap_to_guidelines)
		{
			foreach (var target in GetGuideIntersectionSnapPoints(view, excluded))
				Consider(target.WorldPoint);
		}

		// Фигуры перебираются независимо от Settings.snap_objects.
		//
		// Это и есть динамические направляющие: кромка и узел соседней
		// фигуры — единственное, от чего они строятся. Пока перебор стоял
		// под признаком прилипания к объектам, выключение того признака
		// отнимало и динамические направляющие — при включённой их
		// собственной галочке. Своя проверка у метода уже есть, в самом
		// начале.
		foreach (var shape in view.GetShapesBackToFront())
		{
			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (excluded.Contains(shape))
				continue;

			if (shape is GuideLine)
				continue;

			foreach (var target in ShapeSnapGeometry.GetSnapPoints(shape))
				Consider(target.WorldPoint);
		}

		if (!hasX && !hasY)
			return null;

		var correction = new SKPoint(hasX ? bestXCorrection : 0f, hasY ? bestYCorrection : 0f);

		// Точка отсчёта и цель для отрисовки. Когда сработали обе оси,
		// основная пара описывает вертикальную направляющую, вторая —
		// горизонтальную.
		var primarySource = hasX ? bestXSource : bestYSource;
		var primaryTarget = hasX ? bestXTarget : bestYTarget;

		return new SnapResult
		{
			Kind = SnapKind.PointToPoint,
			SnappedWorldPoint = primarySource,
			TargetWorldPoint = primaryTarget,
			DisplayFromWorldPoint = FindBestDisplayFrom(
				sourcePoints, primarySource, primaryTarget, correction, hasX, hasY),
			SecondTargetWorldPoint = hasX && hasY ? bestYTarget : null,
			SecondDisplayFromWorldPoint = hasX && hasY
				? FindBestDisplayFrom(sourcePoints, bestYSource, bestYTarget, correction, false, true)
				: null,
			Correction = correction,
			DistancePage = MathF.Min(hasX ? bestDx : float.MaxValue, hasY ? bestDy : float.MaxValue),
			SnapX = hasX,
			SnapY = hasY,
			FromDynamicGuide = true
		};
	}

	private static SnapResult? FindBestLineLikeSnap(
		MainView view,
		HashSet<Shape> excluded,
		IReadOnlyList<SKPoint> sourcePoints,
		float snapDistPage)
	{
		if (sourcePoints == null || sourcePoints.Count == 0)
			return null;

		float snapDistSq = snapDistPage * snapDistPage;
		SnapResult? best = null;

		if (Settings.dynamic_guidelines)
		{
			foreach (var line in GetPageSnapLines(view))
			{
				foreach (var source in sourcePoints)
				{
					var proj = ProjectPointToSegment(source, line.A, line.B);
					float d2 = DistSq(source, proj);
					if (d2 > snapDistSq)
						continue;

					float d = MathF.Sqrt(d2);
					if (best == null || d < best.DistancePage)
					{
						best = new SnapResult
						{
							Kind = SnapKind.PointToLine,
							SnappedWorldPoint = source,
							TargetWorldPoint = proj,
							Correction = Sub(proj, source),
							LineA = line.A,
							LineB = line.B,
							SnapX = false,
							SnapY = false,
							DistancePage = d,
							FromDynamicGuide = true
						};
					}
				}
			}
		}

		// Направляющие важнее динамических.
		//
		// Направляющую пользователь поставил сам и поставил осознанно —
		// это ось чертежа. Динамическая появляется на любой кромке любой
		// фигуры и живёт ровно до конца протяжки. Пока их сравнивали
		// по расстоянию, случайная кромка соседней фигуры, оказавшаяся
		// ближе на доли миллиметра, перебивала направляющую, и совместить
		// объект с осью удавалось не с первого раза.
		//
		// Поэтому найденная направляющая принимается безусловно, а не
		// по сравнению расстояний. Расстояние всё равно ограничено сверху:
		// FindBestGuideLineSnap не возвращает ничего дальше snapDistPage,
		// то есть на направляющую надо было именно навести.
		if (Settings.snap_to_guidelines)
		{
			var bestGuide = FindBestGuideLineSnap(view, excluded, sourcePoints, snapDistPage);

			if (bestGuide != null)
				best = bestGuide;
		}

		if (Settings.snap_objects)
		{
			foreach (var shape in view.GetShapesBackToFront())
			{
				if (!shape.IsVisible || shape.IsLocked)
					continue;

				if (excluded.Contains(shape))
					continue;

				if (shape is GuideLine)
					continue;

				foreach (var line in ShapeSnapGeometry.GetSnapLines(shape))
				{
					foreach (var source in sourcePoints)
					{
						var proj = ProjectPointToSegment(source, line.A, line.B);
						float d2 = DistSq(source, proj);
						if (d2 > snapDistSq)
							continue;

						float d = MathF.Sqrt(d2);
						if (best == null || d < best.DistancePage)
						{
							best = new SnapResult
							{
								Kind = SnapKind.PointToLine,
								SnappedWorldPoint = source,
								TargetWorldPoint = proj,
								Correction = Sub(proj, source),
								LineA = line.A,
								LineB = line.B,
								SnapX = false,
								SnapY = false,
								DistancePage = d
							};
						}
					}
				}
			}
		}

		return best;
	}

	private static SKPoint FindBestDisplayFrom(
		IReadOnlyList<SKPoint> sourcePoints,
		SKPoint snappedSource,
		SKPoint targetPoint,
		SKPoint correction,
		bool snapX,
		bool snapY)
	{
		if (sourcePoints == null || sourcePoints.Count == 0)
			return snappedSource;

		SKPoint best = snappedSource;
		float bestPrimary = float.MaxValue;
		float bestSecondary = float.MaxValue;

		foreach (var p in sourcePoints)
		{
			var moved = new SKPoint(
				p.X + correction.X,
				p.Y + correction.Y);

			float primary;
			float secondary;

			if (snapX && !snapY)
			{
				primary = MathF.Abs(moved.Y - targetPoint.Y);
				secondary = DistSq(moved, targetPoint);
			}
			else if (!snapX && snapY)
			{
				primary = MathF.Abs(moved.X - targetPoint.X);
				secondary = DistSq(moved, targetPoint);
			}
			else
			{
				primary = DistSq(moved, targetPoint);
				secondary = primary;
			}

			if (primary < bestPrimary ||
				(MathF.Abs(primary - bestPrimary) < 0.0001f && secondary < bestSecondary))
			{
				bestPrimary = primary;
				bestSecondary = secondary;
				best = p;
			}
		}

		return best;
	}

	private static SKPoint FindBestDisplayFromOnBounds(
		SKRect boundsBeforeMove,
		SKPoint correction,
		SKPoint targetPoint,
		bool snapX,
		bool snapY)
	{
		var moved = new SKRect(
			boundsBeforeMove.Left + correction.X,
			boundsBeforeMove.Top + correction.Y,
			boundsBeforeMove.Right + correction.X,
			boundsBeforeMove.Bottom + correction.Y);

		if (snapX && !snapY)
		{
			float clampedY = Math.Clamp(targetPoint.Y, moved.Top, moved.Bottom);

			float leftDist = MathF.Abs(targetPoint.X - moved.Left);
			float rightDist = MathF.Abs(targetPoint.X - moved.Right);
			float x = leftDist <= rightDist ? moved.Left : moved.Right;

			return new SKPoint(x - correction.X, clampedY - correction.Y);
		}

		if (!snapX && snapY)
		{
			float clampedX = Math.Clamp(targetPoint.X, moved.Left, moved.Right);

			float topDist = MathF.Abs(targetPoint.Y - moved.Top);
			float bottomDist = MathF.Abs(targetPoint.Y - moved.Bottom);
			float y = topDist <= bottomDist ? moved.Top : moved.Bottom;

			return new SKPoint(clampedX - correction.X, y - correction.Y);
		}

		float px = Math.Clamp(targetPoint.X, moved.Left, moved.Right);
		float py = Math.Clamp(targetPoint.Y, moved.Top, moved.Bottom);
		return new SKPoint(px - correction.X, py - correction.Y);
	}

	private static SnapResult? FindBestGuideLineSnap(
		MainView view,
		HashSet<Shape> excluded,
		IReadOnlyList<SKPoint> sourcePoints,
		float snapDistPage)
	{
		if (sourcePoints == null || sourcePoints.Count == 0)
			return null;

		float snapDistSq = snapDistPage * snapDistPage;
		SnapResult? best = null;

		foreach (var shape in view.GetShapesBackToFront())
		{
			if (shape is not GuideLine guide)
				continue;

			if (!guide.IsVisible || guide.IsLocked)
				continue;

			if (excluded.Contains(guide))
				continue;

			foreach (var line in ShapeSnapGeometry.GetSnapLines(guide))
			{
				foreach (var source in sourcePoints)
				{
					var proj = ProjectPointToInfiniteLine(source, line.A, line.B);
					float d2 = DistSq(source, proj);
					if (d2 > snapDistSq)
						continue;

					float d = MathF.Sqrt(d2);
					if (best == null || d < best.DistancePage)
					{
						best = new SnapResult
						{
							Kind = SnapKind.PointToLine,
							SnappedWorldPoint = source,
							TargetWorldPoint = proj,
							Correction = Sub(proj, source),
							LineA = line.A,
							LineB = line.B,
							SnapX = false,
							SnapY = false,
							DistancePage = d
						};
					}
				}
			}
		}

		return best;
	}

	private static SnapResult? FindBestPageOriginSnap(
	MainView view,
	IReadOnlyList<SKPoint> sourcePoints,
	float snapDistPage)
	{
		if (view.CurrentPage == null || sourcePoints == null || sourcePoints.Count == 0)
			return null;

		float snapDistSq = snapDistPage * snapDistPage;
		var origin = GetPageOriginWorld(view);
		SnapResult? best = null;

		foreach (var source in sourcePoints)
		{
			float d2 = DistSq(source, origin);
			if (d2 > snapDistSq)
				continue;

			float d = MathF.Sqrt(d2);
			if (best == null || d < best.DistancePage)
			{
				best = new SnapResult
				{
					Kind = SnapKind.PointToPoint,
					SnappedWorldPoint = source,
					TargetWorldPoint = origin,
					Correction = Sub(origin, source),
					DistancePage = d,
					SnapX = true,
					SnapY = true
				};
			}
		}

		return best;
	}

	private static IEnumerable<SnapLineCandidate> GetPageSnapLines(MainView view)
	{
		if (view.CurrentPage == null)
			yield break;

		var page = view.CurrentPage;
		var left = page.page_origin.X;
		var top = page.page_origin.Y;
		var right = left + page.size_uom.Width;
		var bottom = top + page.size_uom.Height;

		var p1 = new SKPoint(left, top);
		var p2 = new SKPoint(right, top);
		var p3 = new SKPoint(right, bottom);
		var p4 = new SKPoint(left, bottom);

		yield return new SnapLineCandidate(default!, p1, p2);
		yield return new SnapLineCandidate(default!, p2, p3);
		yield return new SnapLineCandidate(default!, p3, p4);
		yield return new SnapLineCandidate(default!, p4, p1);

		yield return new SnapLineCandidate(default!, new SKPoint((left + right) * 0.5f, top), new SKPoint((left + right) * 0.5f, bottom));
		yield return new SnapLineCandidate(default!, new SKPoint(left, (top + bottom) * 0.5f), new SKPoint(right, (top + bottom) * 0.5f));
	}

	private static SKPoint GetPageOriginWorld(MainView view)
	{
		return SKPoint.Empty;
	}

	/// <summary>
	/// Перекрестья динамических направляющих вблизи перемещаемых точек.
	///
	/// Линии сначала отбираются по близости к источникам, и пересекаются
	/// только отобранные. Отбор не теряет ни одного нужного перекрестья:
	/// если оно лежит ближе snapDist к источнику, то и каждая из двух
	/// линий проходит не дальше — они через него идут. Зато перебор пар
	/// сводится к считанным штукам вместо квадрата от числа линий
	/// на листе.
	/// </summary>
	private static IEnumerable<SnapPointCandidate> GetLineCrossPoints(
		MainView view,
		HashSet<Shape> excluded,
		IReadOnlyList<SKPoint> sourcePoints,
		float snapDistPage)
	{
		if (!Settings.dynamic_guidelines)
			yield break;

		float snapDistSq = snapDistPage * snapDistPage;

		var near = new List<SnapLineCandidate>();

		foreach (var line in GetPageSnapLines(view))
		{
			foreach (var source in sourcePoints)
			{
				var proj = ProjectPointToInfiniteLine(source, line.A, line.B);

				if (DistSq(source, proj) <= snapDistSq)
				{
					near.Add(line);
					break;
				}
			}
		}

		for (int i = 0; i < near.Count; i++)
		{
			for (int j = i + 1; j < near.Count; j++)
			{
				if (!TryIntersectInfiniteLines(near[i].A, near[i].B, near[j].A, near[j].B, out var cross))
					continue;

				foreach (var source in sourcePoints)
				{
					if (DistSq(source, cross) <= snapDistSq)
					{
						yield return new SnapPointCandidate(near[i].Owner, cross);
						break;
					}
				}
			}
		}
	}

	private static IEnumerable<SnapPointCandidate> GetGuideIntersectionSnapPoints(MainView view, HashSet<Shape> excluded)
	{
		var guides = new List<GuideLine>();

		foreach (var shape in view.GetShapesBackToFront())
		{
			if (shape is not GuideLine guide)
				continue;

			if (!guide.IsVisible || guide.IsLocked)
				continue;

			if (excluded.Contains(guide))
				continue;

			guides.Add(guide);
		}

		for (int i = 0; i < guides.Count; i++)
		{
			var l1 = ShapeSnapGeometry.GetSnapLines(guides[i]).FirstOrDefault();

			for (int j = i + 1; j < guides.Count; j++)
			{
				var l2 = ShapeSnapGeometry.GetSnapLines(guides[j]).FirstOrDefault();

				if (TryIntersectInfiniteLines(l1.A, l1.B, l2.A, l2.B, out var p))
					yield return new SnapPointCandidate(guides[i], p);
			}
		}
	}

	private static IEnumerable<SnapPointCandidate> GetPageSnapPoints(MainView view)
	{
		if (view.CurrentPage == null || !Settings.dynamic_guidelines)
			yield break;

		var page = view.CurrentPage;
		var r = GetPageBoundsWorld(page);

		foreach (var p in GetBoundsSnapPoints(r))
			yield return new SnapPointCandidate(null!, p);

		yield return new SnapPointCandidate(null!, SKPoint.Empty);
	}

	private static SKRect GetPageBoundsWorld(Page page)
	{
		float left = page.page_origin.X;
		float top = page.page_origin.Y;
		float right = left + page.size_uom.Width;
		float bottom = top + page.size_uom.Height;

		return new SKRect(
			Math.Min(left, right),
			Math.Min(top, bottom),
			Math.Max(left, right),
			Math.Max(top, bottom));
	}

	private static bool TryIntersectInfiniteLines(
		SKPoint a1,
		SKPoint a2,
		SKPoint b1,
		SKPoint b2,
		out SKPoint intersection)
	{
		float x1 = a1.X;
		float y1 = a1.Y;
		float x2 = a2.X;
		float y2 = a2.Y;
		float x3 = b1.X;
		float y3 = b1.Y;
		float x4 = b2.X;
		float y4 = b2.Y;

		float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);

		if (MathF.Abs(denom) < 1e-6f)
		{
			intersection = default;
			return false;
		}

		float det1 = x1 * y2 - y1 * x2;
		float det2 = x3 * y4 - y3 * x4;

		float px = (det1 * (x3 - x4) - (x1 - x2) * det2) / denom;
		float py = (det1 * (y3 - y4) - (y1 - y2) * det2) / denom;

		intersection = new SKPoint(px, py);
		return true;
	}

	private static IEnumerable<SKPoint> GetBoundsSnapPoints(SKRect r)
	{
		yield return new SKPoint(r.Left, r.Top);
		yield return new SKPoint(r.MidX, r.Top);
		yield return new SKPoint(r.Right, r.Top);
		yield return new SKPoint(r.Right, r.MidY);
		yield return new SKPoint(r.Right, r.Bottom);
		yield return new SKPoint(r.MidX, r.Bottom);
		yield return new SKPoint(r.Left, r.Bottom);
		yield return new SKPoint(r.Left, r.MidY);
		yield return new SKPoint(r.MidX, r.MidY);
	}

	private static float ScreenDistanceToPageDistance(MainView view, float px)
	{
		var a = view.ScreenToPage(new SKPoint(0, 0));
		var b = view.ScreenToPage(new SKPoint(px, 0));
		return MathF.Abs(b.X - a.X);
	}

	private static SKPoint ProjectPointToInfiniteLine(SKPoint p, SKPoint a, SKPoint b)
	{
		float vx = b.X - a.X;
		float vy = b.Y - a.Y;
		float len2 = vx * vx + vy * vy;
		if (len2 < 1e-8f)
			return a;

		float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
		return new SKPoint(a.X + vx * t, a.Y + vy * t);
	}

	private static SKPoint ProjectPointToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float vx = b.X - a.X;
		float vy = b.Y - a.Y;
		float len2 = vx * vx + vy * vy;
		if (len2 < 1e-8f)
			return a;

		float t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
		t = Math.Clamp(t, 0f, 1f);
		return new SKPoint(a.X + vx * t, a.Y + vy * t);
	}

	private static float DistSq(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}

	private static SKPoint Sub(SKPoint a, SKPoint b)
		=> new(a.X - b.X, a.Y - b.Y);

	/// <summary>
	/// Расширяет список исключений зависимой геометрией.
	///
	/// Если фигура перемещается или масштабируется, вместе с ней меняется
	/// и весь текст, привязанный к ней как к контуру или как к контейнеру.
	/// Такой текст не может быть целью прилипания: его точки уедут вместе
	/// с источником, и поправка окажется посчитанной по устаревшему положению.
	///
	/// Обход повторяется, пока список растёт: текст может быть привязан
	/// к другому тексту, а тот — к третьей фигуре.
	/// </summary>
	/// <summary>
	/// Добавляет пересечения линий как точки прилипания.
	///
	/// Сначала отбираются линии, проходящие вблизи движущейся точки,
	/// и пересекаются только они. Отбор не теряет ни одного нужного
	/// пересечения: если перекрестье лежит ближе snapDist к точке, то
	/// и каждая из двух линий проходит не дальше — они через него идут.
	/// Зато он сводит перебор пар к считанным штукам вместо квадрата
	/// от числа линий на листе.
	/// </summary>
	private static void AddLineIntersections(
		List<SnapLineCandidate> lines,
		SKPoint point,
		float snapDistPage,
		List<SnapPointCandidate> points)
	{
		if (lines.Count < 2)
			return;

		var near = new List<SnapLineCandidate>();

		foreach (var line in lines)
		{
			var proj = line.Infinite || line.Owner is GuideLine
				? ProjectPointToInfiniteLine(point, line.A, line.B)
				: ProjectPointToSegment(point, line.A, line.B);

			if (DistSq(point, proj) <= snapDistPage * snapDistPage)
				near.Add(line);
		}

		float snapDistSq = snapDistPage * snapDistPage;

		for (int i = 0; i < near.Count; i++)
		{
			for (int j = i + 1; j < near.Count; j++)
			{
				if (!TryIntersectLines(near[i], near[j], out var cross))
					continue;

				if (DistSq(point, cross) > snapDistSq)
					continue;

				points.Add(new SnapPointCandidate(near[i].Owner, cross));
			}
		}
	}

	/// <summary>
	/// Точка пересечения двух прямых. false — прямые параллельны либо
	/// перекрестье лежит за пределами отрезка, который бесконечным
	/// не объявлен.
	/// </summary>
	private static bool TryIntersectLines(SnapLineCandidate a, SnapLineCandidate b, out SKPoint cross)
	{
		cross = SKPoint.Empty;

		float ax = a.B.X - a.A.X;
		float ay = a.B.Y - a.A.Y;
		float bx = b.B.X - b.A.X;
		float by = b.B.Y - b.A.Y;

		float denom = ax * by - ay * bx;

		// Параллельны или вырождены: пересечения нет либо их бесконечно
		// много, и ни то, ни другое точкой прилипания не является.
		if (MathF.Abs(denom) < 1e-9f)
			return false;

		float dx = b.A.X - a.A.X;
		float dy = b.A.Y - a.A.Y;

		float t = (dx * by - dy * bx) / denom;
		float u = (dx * ay - dy * ax) / denom;

		// Отрезок остаётся отрезком: продлевать ребро чужой фигуры
		// до бесконечности нельзя, иначе лист покрылся бы перекрестьями
		// там, где никакой геометрии нет.
		const float eps = 1e-4f;

		bool aInfinite = a.Infinite || a.Owner is GuideLine;
		bool bInfinite = b.Infinite || b.Owner is GuideLine;

		if (!aInfinite && (t < -eps || t > 1f + eps))
			return false;

		if (!bInfinite && (u < -eps || u > 1f + eps))
			return false;

		cross = new SKPoint(a.A.X + ax * t, a.A.Y + ay * t);
		return true;
	}

	/// <summary>
	/// Добавляет собственные точки и линии правимой фигуры, кроме тех,
	/// что перечислены в ignore.
	///
	/// Сравнение с допуском, а не на равенство: точка приходит из той же
	/// геометрии, но по дороге проходит через матрицу преобразования,
	/// и последний разряд расходится.
	/// </summary>
	private static void AddSelfPoints(
		Shape? selfShape,
		IReadOnlyList<SKPoint>? ignore,
		IReadOnlyList<SnapLineCandidate>? presetLines,
		List<SnapPointCandidate> points,
		List<SnapLineCandidate> lines)
	{
		if (selfShape == null || !selfShape.IsVisible || selfShape.IsLocked)
			return;

		const float eps = 1e-4f;

		// Собственные узлы фигуры — обычное прилипание к объектам.
		if (Settings.snap_objects)
		{
			foreach (var candidate in ShapeSnapGeometry.GetSnapPoints(selfShape))
			{
				bool skip = false;

				if (ignore != null)
				{
					foreach (var p in ignore)
					{
						if (MathF.Abs(p.X - candidate.WorldPoint.X) < eps
							&& MathF.Abs(p.Y - candidate.WorldPoint.Y) < eps)
						{
							skip = true;
							break;
						}
					}
				}

				if (!skip)
					points.Add(candidate);
			}
		}

		// Направления собственных отрезков — это динамические направляющие,
		// и висят они на своей галочке.
		//
		// Прежде весь разбор своей фигуры стоял под признаком прилипания
		// к объектам, и выключить направляющие по своей же кривой было
		// нечем: узел продолжал держать направление соседней стороны
		// при погашенной галочке и отпускал только вместе со всем
		// прилипанием разом.
		if (!Settings.dynamic_guidelines)
			return;

		// Отрезки берутся из снимка, если он передан.
		//
		// Снимок обязателен для правки узлов. Два отрезка упираются
		// в перетаскиваемый узел и поворачиваются вместе с ним, а узел
		// пытается прилипнуть к их направлению — то есть к направлению,
		// которое сам же и задаёт. Получается положительная обратная
		// связь: узел притягивается, отрезок доворачивается, узел
		// притягивается снова. Со стороны это дёрганое, неуправляемое
		// движение.
		//
		// Снимок, снятый до начала протяжки, этот круг разрывает: цели
		// неподвижны, как и положено целям.
		if (presetLines != null)
		{
			// Снимок кладётся как бесконечные прямые: прилипание должно
			// держать направление и за концом отрезка.
			foreach (var line in presetLines)
				lines.Add(line with { Infinite = true });

			return;
		}

		foreach (var line in ShapeSnapGeometry.GetSnapLines(selfShape))
			lines.Add(line);
	}

	private static HashSet<Shape> BuildExcluded(MainView view, IEnumerable<Shape>? excludeShapes)
	{
		// Группа исключается вместе со всем содержимым.
		//
		// Сама она целью прилипания и так не была бы — исключена явно, —
		// но привязки размеров указывают не на группу, а на КОНКРЕТНОГО
		// ребёнка внутри неё. Не будь детей в списке, DependsOnExcluded
		// не признал бы такой размер зависимым, и он остался бы законной
		// целью. А точки у него при этом едут вместе с группой: курсор
		// тянет группу, размер догоняет её, цель прилипания убегает,
		// поправка пересчитывается — и фигура дрожит на каждом движении мыши.
		var excluded = new HashSet<Shape>(
			EnumerateAllShapes(excludeShapes ?? Enumerable.Empty<Shape>()),
			ReferenceEqualityComparer.Instance);

		if (excluded.Count == 0 || view.CurrentPage == null)
			return excluded;

		// Ограничение на число проходов: даже при повреждённых данных
		// с циклом ссылок цикл обязан завершиться.
		const int MaxPasses = 8;

		for (int pass = 0; pass < MaxPasses; pass++)
		{
			bool added = false;

			foreach (var shape in EnumerateAllShapes(view.CurrentPage.Shapes))
			{
				if (excluded.Contains(shape))
					continue;

				if (!DependsOnExcluded(shape, excluded))
					continue;

				excluded.Add(shape);
				added = true;
			}

			if (!added)
				break;
		}

		return excluded;
	}

	/// <summary>
	/// Зависит ли геометрия фигуры от чего-то исключённого.
	///
	/// Проверяются только АКТИВНЫЕ привязки: если режим выключен, ссылка
	/// сохранена, но на геометрию не влияет, и такая фигура остаётся законной
	/// целью прилипания.
	/// </summary>
	private static bool DependsOnExcluded(Shape shape, HashSet<Shape> excluded)
	{
		// Фигура с привязанными концами — размер или связь во flowchart.
		// Её концы пересчитываются из геометрии цели на каждом построении,
		// поэтому во время трансформации цели она едет вместе с ней. Прилипать
		// к такой фигуре значит прилипать к собственному же движению: точка
		// притяжения убегает от курсора ровно на ту величину, на которую притягивает.
		if (shape is IShapeAnchorHost anchorHost
			&& (IsAnchorExcluded(anchorHost.FirstAnchor, excluded)
				|| IsAnchorExcluded(anchorHost.LastAnchor, excluded)))
		{
			return true;
		}

		// Размер угла держит четыре привязки и под IShapeAnchorHost
		// не подходит: у того интерфейса ровно два конца. Ведёт он себя
		// при этом точно так же — едет за своими целями.
		if (shape is AngleMeasurementShape angle)
		{
			foreach (var anchor in angle.Anchors)
			{
				if (IsAnchorExcluded(anchor, excluded))
					return true;
			}
		}

		if (shape is not TextShape text)
			return false;

		if (text.AlongShape && text.otherShape != null && excluded.Contains(text.otherShape))
			return true;

		if (text.InsideOtherShape && text.InsideShape != null && excluded.Contains(text.InsideShape))
			return true;

		return false;
	}

	private static bool IsAnchorExcluded(ShapeAnchor anchor, HashSet<Shape> excluded)
		=> anchor != null && anchor.IsActive && excluded.Contains(anchor.Target!);

	private static IEnumerable<Shape> EnumerateAllShapes(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateAllShapes(group.Children))
					yield return child;
			}
		}
	}

	/// <summary>
	/// Фигуры-цели привязки: верхний уровень плюс содержимое групп.
	///
	/// Группа отдаёт и себя, и своих детей. Себя — потому что её габаритная
	/// рамка остаётся законной целью: размер «по всему блоку» ставят чаще,
	/// чем по отдельной детали внутри. Детей — потому что мерят всё-таки
	/// детали, а требовать разгруппировать объект ради простановки размера
	/// значило бы ломать чертёж ради инструмента.
	///
	/// Порядок важен: группа идёт ПЕРЕД своими детьми. В TryFindAnchor
	/// кандидат с тем же расстоянием вытесняет предыдущего, поэтому при
	/// совпадении угла группы с углом её ребёнка выигрывает ребёнок —
	/// то есть более точная цель, а не рамка вокруг неё.
	///
	/// Невидимая или запертая группа отсекается вместе с поддеревом:
	/// у детей собственные флаги остаются поднятыми, и без этой проверки
	/// скрытая группа продолжала бы притягивать курсор своими узлами.
	/// </summary>
	private static IEnumerable<Shape> EnumerateAnchorTargets(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			if (!shape.IsVisible || shape.IsLocked)
				continue;

			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateAnchorTargets(group.Children))
					yield return child;
			}
		}
	}

	/// <summary>
	/// Ближайшая точка привязки к перетаскиваемому концу кривой.
	///
	/// Работает независимо от Settings.snap_objects: связь во flowchart нужна
	/// и тогда, когда обычное прилипание пользователь отключил. Цели тоже
	/// другие — только узлы, середины рёбер и стороны света, а не все точки
	/// прилипания подряд.
	///
	/// nodeId возвращает узел кривой, породивший найденную точку. Его
	/// нужно передать в SetAnchor: привязка по номеру перескочит на чужой
	/// узел, как только у кривой изменится число узлов.
	///
	/// includeCenter добавляет в цели центры фигур. Нужен размеру:
	/// расстояние между центрами — обычная величина на чертежах. Связям
	/// во flowchart не нужен: стрелка должна касаться границы блока,
	/// а не втыкаться в его середину.
	///
	/// includeGroupChildren пускает поиск внутрь групп. Выключено по
	/// умолчанию, чтобы не менять молча поведение связей во flowchart:
	/// там стрелка к блоку-группе должна цепляться за её рамку, а не
	/// проваливаться на случайный внутренний штрих. Размеру же нужно
	/// обратное — он меряет детали, а не рамки.
	/// </summary>
	public static bool TryFindAnchor(
		MainView view,
		Shape? curve,
		SKPoint worldPoint,
		float snapDistPx,
		out Shape? target,
		out ShapeAnchorKind kind,
		out int index,
		out SKPoint anchorWorldPoint,
		out Guid nodeId,
		bool includeCenter = false,
		bool includeGroupChildren = false)
	{
		target = null;
		kind = ShapeAnchorKind.None;
		index = 0;
		anchorWorldPoint = worldPoint;
		nodeId = Guid.Empty;

		if (view.CurrentPage == null)
			return false;

		float snapDistPage = ScreenDistanceToPageDistance(view, snapDistPx);
		float bestSq = snapDistPage * snapDistPage;
		bool found = false;

		var targets = includeGroupChildren
			? EnumerateAnchorTargets(view.GetShapesBackToFront())
			: view.GetShapesBackToFront();

		foreach (var shape in targets)
		{
			// Проверка остаётся и здесь: при includeGroupChildren = false
			// перечисление отдаёт фигуры без всякой фильтрации.
			if (!shape.IsVisible || shape.IsLocked)
				continue;

			// К самой себе кривая привязаться не может: координаты конца
			// вычислялись бы из её же геометрии и не сходились бы.
			if (ReferenceEquals(shape, curve) || shape is GuideLine)
				continue;

			foreach (var candidate in ShapeAnchorGeometry.GetLocalAnchors(shape, includeCenter))
			{
				// Мировая точка, а не локальная с трансформацией страницы:
				// LocalToWorld идёт по цепочке Parent, поэтому ребёнок группы
				// даёт правильную позицию без отдельного учёта матрицы группы.
				var world = shape.LocalToWorld(candidate.Local);

				float dx = world.X - worldPoint.X;
				float dy = world.Y - worldPoint.Y;
				float d2 = dx * dx + dy * dy;

				if (d2 > bestSq)
					continue;

				bestSq = d2;
				target = shape;
				kind = candidate.Kind;
				index = candidate.Index;
				anchorWorldPoint = world;

				// У точек кривой здесь лежит идентификатор узла, у остальных
				// — Guid.Empty, и привязка останется на номере, как было.
				nodeId = candidate.NodeId;
				found = true;
			}
		}

		return found;
	}
}

/// <summary>
/// Узел фигуры под курсором и поиск ближайшего узла.
///
/// «Узлом» здесь считается то же, что и точкой прилипания: вершины,
/// середины рёбер, центры, концы размеров. Иначе подсветка обещала бы
/// одно, а прилипало бы другое — а именно ради совпадения этих двух
/// вещей подсветка и нужна.
/// </summary>
public static class ShapeNodePicker
{
	/// <summary>
	/// Источники узлов: перечисленные фигуры плюс содержимое групп.
	///
	/// Группа сама по себе отдаёт только девять точек габаритов — она
	/// попадает в GetSnapPoints в ветку default. Для перетаскивания этого
	/// мало: пользователь целится в угол конкретной детали внутри группы,
	/// а получает угол рамки вокруг всей группы, то есть точку, которой
	/// на чертеже вообще нет.
	///
	/// Дети идут ПЕРЕД самой группой. Оба поиска ниже держат первого из
	/// равных (сравнение строгое), поэтому там, где угол детали совпадает
	/// с углом габаритов группы, выигрывает деталь — более осмысленная
	/// из двух совпавших точек.
	///
	/// Рамка группы при этом остаётся в списке: за неё тоже тянут, и у
	/// группы из двух далеко разнесённых фигур её углы — единственное,
	/// что описывает выделение целиком.
	/// </summary>
	private static IEnumerable<Shape> EnumerateNodeSources(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			if (shape is GuideLine || !shape.IsVisible || shape.IsLocked)
				continue;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateNodeSources(group.Children))
					yield return child;
			}

			yield return shape;
		}
	}

	/// <summary>
	/// Точки фигуры, пригодные как точка отсчёта прилипания: её
	/// собственные узлы ПЛЮС углы, середины сторон и центр габаритов.
	///
	/// Габариты добавляются здесь, а не в ShapeSnapGeometry, намеренно.
	/// Там точки служат ЦЕЛЯМИ — тем, к чему тянутся, — и лишние девять
	/// штук вокруг каждой кривой на листе превратили бы прилипание
	/// в лотерею. Здесь же точки служат ИСТОЧНИКОМ: это места самой
	/// перетаскиваемой фигуры, за которые её можно нести, и чем их больше,
	/// тем точнее выходит совмещение.
	///
	/// Нужда в них видна на кривой и на отрезке: у них есть узлы,
	/// но нет ни угла рамки, ни центра, и поставить кривую углом
	/// её габаритов в угол прямоугольника было нечем.
	///
	/// Повторы не отсеиваются: у неповёрнутого прямоугольника девять
	/// точек габаритов совпадают с его собственными. Выигрывает всё равно
	/// ближайшая, а на совпавших выбор безразличен.
	/// </summary>
	private static IEnumerable<SnapPointCandidate> GetSourcePoints(Shape shape)
	{
		foreach (var candidate in ShapeSnapGeometry.GetSnapPoints(shape))
			yield return candidate;

		var bounds = shape.GetBounds();

		if (bounds.Width <= 0f && bounds.Height <= 0f)
			yield break;

		foreach (var p in ShapeSnapGeometry.GetBoundsPoints(bounds))
			yield return new SnapPointCandidate(shape, p);
	}

	/// <summary>
	/// Точки габаритов ВСЕГО выделения: углы, середины сторон, центр.
	///
	/// Отдельно от точек каждой фигуры, потому что описывают то, чего
	/// нет ни у одной из них по отдельности. Выделив три детали и таща
	/// их вместе, пользователь совмещает по краю или середине всей
	/// связки, а не по краю случайной из трёх.
	///
	/// Для одной фигуры не считается: её габариты уже пришли
	/// из GetSourcePoints, и повторять их незачем.
	/// </summary>
	private static IEnumerable<SnapPointCandidate> GetSelectionBoundsPoints(IReadOnlyList<Shape> shapes)
	{
		if (shapes.Count < 2)
			yield break;

		bool any = false;
		float left = 0f, top = 0f, right = 0f, bottom = 0f;

		foreach (var shape in shapes)
		{
			if (shape is GuideLine || !shape.IsVisible)
				continue;

			var b = shape.GetBounds();

			if (!any)
			{
				left = b.Left;
				top = b.Top;
				right = b.Right;
				bottom = b.Bottom;
				any = true;
				continue;
			}

			left = MathF.Min(left, b.Left);
			top = MathF.Min(top, b.Top);
			right = MathF.Max(right, b.Right);
			bottom = MathF.Max(bottom, b.Bottom);
		}

		if (!any)
			yield break;

		var union = new SKRect(left, top, right, bottom);

		// Владельцем назначается первая фигура выделения: поле нужно
		// подсветке и отбору «своё или чужое», а точка принадлежит
		// не фигуре, а связке — выбрать из них всё равно некого.
		foreach (var p in ShapeSnapGeometry.GetBoundsPoints(union))
			yield return new SnapPointCandidate(shapes[0], p);
	}

	/// <summary>
	/// Ближайший узел среди перечисленных фигур. Возвращает null, если
	/// в пределах maxDistPx ничего нет.
	/// </summary>
	public static SnapPointCandidate? FindNearest(
		MainView view,
		IEnumerable<Shape> shapes,
		SKPoint pagePoint,
		float maxDistPx)
	{
		float maxDistPage = ObjectSnapEngine.ScreenToPageDistance(view, maxDistPx);
		float maxDistSq = maxDistPage * maxDistPage;

		SnapPointCandidate? best = null;
		float bestSq = float.MaxValue;

		foreach (var shape in EnumerateNodeSources(shapes))
		{
			// Сначала собственные точки фигуры, потом точки габаритов.
			//
			// Порядок решает совпадения: сравнение ниже строгое, и при
			// равном расстоянии остаётся первый найденный. У неповёрнутого
			// прямоугольника угол габаритов лежит ровно на его собственном
			// угле, и подсветить разумнее собственный — он принадлежит
			// самой геометрии, а не рамке вокруг неё.
			// Габариты считаются ЗДЕСЬ и отдаются в GetSnapPoints готовыми:
			// ветка по умолчанию строит свои точки из них же, и без передачи
			// каждая такая фигура считала бы их дважды за одно движение мыши.
			var bounds = shape.GetBounds();

			foreach (var candidate in ShapeSnapGeometry.GetSnapPoints(shape, bounds))
				Consider(candidate);

			// Точки габаритов: четыре угла, четыре середины сторон и центр.
			//
			// Прежде их здесь не было — из-за цены. Поиск идёт на каждое
			// движение мыши по всем фигурам листа, а GetBounds у текста
			// и у размера строил весь мировой путь вместе с контурами
			// глифов. Отсюда и выходило, что углы рамки подсвечивались
			// у прямоугольника и у группы — они отдают их как СВОИ точки
			// в GetSnapPoints, — а у кривой, эллипса и текста не
			// подсвечивались вовсе.
			//
			// Цена с тех пор упала: габариты обычного текста считаются
			// из метрик шрифта, без построения пути. Дороже всего остались
			// кривая, размер, текст в области и вдоль контура — им путь
			// строить приходится. Польза при этом прямая: угол и центр
			// рамки — это места, за которые фигуру несут и которыми её
			// совмещают с другими, и у кривой их взять больше неоткуда.
			if (bounds.Width <= 0f && bounds.Height <= 0f)
				continue;

			foreach (var p in ShapeSnapGeometry.GetBoundsPoints(bounds))
				Consider(new SnapPointCandidate(shape, p));
		}

		return best;

		void Consider(SnapPointCandidate candidate)
		{
			float dx = candidate.WorldPoint.X - pagePoint.X;
			float dy = candidate.WorldPoint.Y - pagePoint.Y;
			float d2 = dx * dx + dy * dy;

			if (d2 > maxDistSq || d2 >= bestSq)
				return;

			bestSq = d2;
			best = candidate;
		}
	}

	/// <summary>
	/// Ближайший узел без ограничения расстояния — нужен, когда фигуру
	/// потянули не за узел: точкой отсчёта прилипания всё равно должен
	/// стать какой-то узел, и разумнее всего ближайший к курсору.
	/// </summary>
	public static SnapPointCandidate? FindNearestUnbounded(
		IEnumerable<Shape> shapes,
		SKPoint pagePoint)
	{
		SnapPointCandidate? best = null;
		float bestSq = float.MaxValue;

		var list = shapes as IReadOnlyList<Shape> ?? shapes.ToList();

		foreach (var shape in EnumerateNodeSources(list))
		{
			foreach (var candidate in GetSourcePoints(shape))
				Consider(candidate);
		}

		foreach (var candidate in GetSelectionBoundsPoints(list))
			Consider(candidate);

		return best;

		void Consider(SnapPointCandidate candidate)
		{
			float dx = candidate.WorldPoint.X - pagePoint.X;
			float dy = candidate.WorldPoint.Y - pagePoint.Y;
			float d2 = dx * dx + dy * dy;

			if (d2 >= bestSq)
				return;

			bestSq = d2;
			best = candidate;
		}
	}
}

/// <summary>Подсветка узла под курсором.</summary>
public static class HoveredNodeAdorner
{
	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.HoveredNodeWorld == null)
			return;

		// Во время перетаскивания подсветку заменяет метка прилипания:
		// две отметки на одной точке читались бы как две разные цели.
		if (ctx.IsDragging)
			return;

		var p = view.PageToScreen(ctx.HoveredNodeWorld.Value);
		float r = Settings.snap_point_size;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Settings.snap_point_color.WithAlpha(90)
		};

		// Светлый ободок снаружи цветного.
		//
		// Метка попадает и на центр рамки выделения, а тот рисуется тёмным
		// кружком того же порядка размера: синяя окружность на нём
		// пропадала, и понять, взялся ли центр под курсор, было нельзя.
		// Контрастная кайма решает это независимо от того, что окажется
		// под меткой, — в отличие от подбора другого цвета, который
		// пришлось бы менять под каждый новый фон.
		using var halo = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 4f,
			Color = new SKColor(255, 255, 255, 220)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = Settings.snap_point_color
		};

		canvas.DrawCircle(p, r, fill);

		// Кайма шире цветной линии и рисуется первой: цветная ложится
		// поверх неё серединой, и по обе стороны остаётся по светлому
		// пикселю. Нарисуй наоборот — светлая перекрыла бы цветную.
		canvas.DrawCircle(p, r, halo);
		canvas.DrawCircle(p, r, stroke);
	}
}

public static class SnapAdorner
{
	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		var snap = ctx.ActiveSnap;
		if (snap == null || !snap.IsValid)
			return;

		var drawFromPage = snap.DisplayFromWorldPoint ?? snap.SnappedWorldPoint;
		var snapSourcePage = snap.SnappedWorldPoint;
		var targetPage = snap.TargetWorldPoint;

		SKPoint drawSnappedPage;

		if (snap.Kind == SnapKind.PointToLine)
		{
			drawSnappedPage = new SKPoint(
				snapSourcePage.X + snap.Correction.X,
				snapSourcePage.Y + snap.Correction.Y);
		}
		else if (snap.SnapX && !snap.SnapY)
		{
			drawSnappedPage = new SKPoint(targetPage.X, drawFromPage.Y);
		}
		else if (!snap.SnapX && snap.SnapY)
		{
			drawSnappedPage = new SKPoint(drawFromPage.X, targetPage.Y);
		}
		else
		{
			drawSnappedPage = new SKPoint(
				snapSourcePage.X + snap.Correction.X,
				snapSourcePage.Y + snap.Correction.Y);
		}

		var from = view.PageToScreen(drawFromPage);
		var target = view.PageToScreen(targetPage);
		var snappedTo = view.PageToScreen(drawSnappedPage);

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Settings.snap_point_color
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = Settings.snap_point_border_color
		};

		using var linePaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = Settings.snap_line_color,
			PathEffect = SKPathEffect.CreateDash(new float[] { 8f, 8f }, 0)
		};

		canvas.DrawCircle(target, Settings.snap_point_size, fill);
		canvas.DrawCircle(target, Settings.snap_point_size, stroke);

		if (snap.Kind == SnapKind.PointToLine && snap.LineA.HasValue && snap.LineB.HasValue)
		{
			var a = view.PageToScreen(snap.LineA.Value);
			var b = view.PageToScreen(snap.LineB.Value);

			canvas.DrawLine(a, b, linePaint);
			canvas.DrawLine(from, snappedTo, linePaint);

			canvas.DrawCircle(snappedTo, Settings.snap_point_size, fill);
			canvas.DrawCircle(snappedTo, Settings.snap_point_size, stroke);
			return;
		}

		if ((snap.SnapX || snap.SnapY) && Settings.dynamic_guidelines)
		{
			if (snap.SnapX)
			{
				var corner = view.PageToScreen(new SKPoint(targetPage.X, drawFromPage.Y));
				canvas.DrawLine(from, corner, linePaint);
				canvas.DrawLine(corner, target, linePaint);
			}

			if (snap.SnapY)
			{
				// Вторая направляющая идёт от своей цели: выравнивание
				// по горизонтали пришло от другой фигуры, и тянуть обе
				// линии к одной точке значило бы показать несуществующее
				// совпадение.
				var yTargetPage = snap.SecondTargetWorldPoint ?? targetPage;
				var yFromPage = snap.SecondDisplayFromWorldPoint ?? drawFromPage;

				var yFrom = view.PageToScreen(yFromPage);
				var yTarget = view.PageToScreen(yTargetPage);

				var corner = view.PageToScreen(new SKPoint(yFromPage.X, yTargetPage.Y));

				canvas.DrawLine(yFrom, corner, linePaint);
				canvas.DrawLine(corner, yTarget, linePaint);

				if (snap.SecondTargetWorldPoint != null)
				{
					canvas.DrawCircle(yTarget, Settings.snap_point_size, fill);
					canvas.DrawCircle(yTarget, Settings.snap_point_size, stroke);
				}
			}
		}
		else
		{
			canvas.DrawLine(from, target, linePaint);
		}

		canvas.DrawCircle(snappedTo, Settings.snap_point_size, fill);
		canvas.DrawCircle(snappedTo, Settings.snap_point_size, stroke);
	}
}