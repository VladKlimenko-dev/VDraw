using SkiaSharp;

using VDraw.Documents;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Перенос привязок измерений с исчезающей фигуры на пришедшие ей на смену.
///
/// Нужен при разрезании: куски строятся заново из SKPath, поэтому
/// идентификаторы узлов у них новые, и сопоставить привязку по NodeId
/// невозможно в принципе. Единственное, что переживает разрез, —
/// ПОЛОЖЕНИЕ точек: узел, не попавший под лезвие, остаётся в одном из
/// кусков ровно там же, где был.
///
/// Отсюда способ: перед удалением исходной фигуры мировая точка привязки
/// вычисляется, а затем ищется ближайшая к ней точка привязки среди
/// кусков. Совпадение по виду точки предпочитается совпадению по
/// расстоянию: узел должен переехать в узел, а середина ребра —
/// в середину ребра, иначе после разреза размер, стоявший на углу,
/// мог бы перескочить на близкую середину соседней стороны.
///
/// Не нашлось ничего в пределах допуска — привязка снимается, но точка
/// остаётся на месте: измерение перестаёт следовать за фигурой, однако
/// не схлопывается и не улетает в начало координат.
/// </summary>
public static class MeasurementAnchorRetargeting
{
	/// <summary>
	/// Допуск совпадения точек, в долях габарита исходной фигуры.
	///
	/// В долях, а не в абсолютных единицах: разрез детали в сантиметр
	/// и разрез чертежа в метр должны вести себя одинаково. Нижняя
	/// граница защищает вырожденные фигуры от нулевого допуска.
	/// </summary>
	private const float ToleranceFraction = 0.002f;

	private const float MinTolerance = 0.001f;

	/// <summary>
	/// Переносит привязки всех измерений страницы с <paramref name="oldShape"/>
	/// на подходящие куски.
	///
	/// Вызывать ДО удаления исходной фигуры со страницы: её геометрия нужна,
	/// чтобы вычислить, где привязка находилась.
	/// </summary>
	public static void Retarget(
		Page page,
		Shape oldShape,
		IReadOnlyList<Shape> pieces,
		UndoStack? undo)
	{
		if (page == null || oldShape == null || pieces == null || pieces.Count == 0)
			return;

		float tolerance = ComputeTolerance(oldShape);

		// Точки привязки кусков собираются один раз: измерений на странице
		// может быть много, и пересчитывать перечисление для каждого
		// значило бы гонять одну и ту же работу по кругу.
		var candidates = CollectCandidates(pieces);

		if (candidates.Count == 0)
			return;

		foreach (var shape in EnumerateAll(page.Shapes))
		{
			switch (shape)
			{
				case AngleMeasurementShape angle:
					RetargetAngle(angle, oldShape, candidates, tolerance, undo);
					break;

				case IShapeAnchorHost host:
					RetargetPair(host, oldShape, candidates, tolerance, undo);
					break;
			}
		}
	}

	#region Кандидаты

	/// <summary>Точка привязки куска в мировых координатах.</summary>
	private readonly struct Candidate
	{
		public readonly Shape Owner;
		public readonly ShapeAnchorKind Kind;
		public readonly int Index;
		public readonly Guid NodeId;
		public readonly SKPoint World;

		public Candidate(Shape owner, ShapeAnchorKind kind, int index, Guid nodeId, SKPoint world)
		{
			Owner = owner;
			Kind = kind;
			Index = index;
			NodeId = nodeId;
			World = world;
		}
	}

	private static List<Candidate> CollectCandidates(IReadOnlyList<Shape> pieces)
	{
		var result = new List<Candidate>();

		foreach (var piece in pieces)
		{
			if (piece == null)
				continue;

			// includeCenter: центр — полноценная точка измерения, и если
			// размер стоял на центре разрезаемой фигуры, разумно перевести
			// его на центр ближайшего куска.
			var anchors = ShapeAnchorGeometry.GetLocalAnchors(piece, includeCenter: true);

			foreach (var anchor in anchors)
			{
				result.Add(new Candidate(
					piece,
					anchor.Kind,
					anchor.Index,
					anchor.NodeId,
					piece.LocalToWorld(anchor.Local)));
			}
		}

		return result;
	}

	/// <summary>
	/// Ищет замену привязке. Сначала среди точек того же вида, затем среди
	/// любых: потеряться совсем хуже, чем переехать с угла на близкую
	/// характерную точку.
	/// </summary>
	private static bool TryFindReplacement(
		SKPoint world,
		ShapeAnchorKind kind,
		List<Candidate> candidates,
		float tolerance,
		out Candidate best)
	{
		if (TryFindNearest(world, kind, candidates, tolerance, out best))
			return true;

		return TryFindNearest(world, ShapeAnchorKind.None, candidates, tolerance, out best);
	}

	private static bool TryFindNearest(
		SKPoint world,
		ShapeAnchorKind kind,
		List<Candidate> candidates,
		float tolerance,
		out Candidate best)
	{
		best = default;

		float bestDistance = tolerance;
		bool found = false;

		foreach (var candidate in candidates)
		{
			// ShapeAnchorKind.None здесь означает «любой вид».
			if (kind != ShapeAnchorKind.None && candidate.Kind != kind)
				continue;

			float d = Distance(world, candidate.World);

			if (d > bestDistance)
				continue;

			bestDistance = d;
			best = candidate;
			found = true;
		}

		return found;
	}

	#endregion

	#region Перенос

	private static void RetargetPair(
		IShapeAnchorHost host,
		Shape oldShape,
		List<Candidate> candidates,
		float tolerance,
		UndoStack? undo)
	{
		bool touchesFirst = ReferenceEquals(host.FirstAnchor.Target, oldShape);
		bool touchesLast = ReferenceEquals(host.LastAnchor.Target, oldShape);

		if (!touchesFirst && !touchesLast)
			return;

		var action = CurveAnchorAction.Capture(host);

		if (touchesFirst)
			RetargetOne(host.FirstAnchor, candidates, tolerance, a => host.ClearAnchor(true),
				(t, k, i, n) => host.SetAnchor(true, t, k, i, n));

		if (touchesLast)
			RetargetOne(host.LastAnchor, candidates, tolerance, a => host.ClearAnchor(false),
				(t, k, i, n) => host.SetAnchor(false, t, k, i, n));

		host.InvalidateAnchors();

		action.CaptureAfter();

		if (action.HasChanges())
			undo?.Push(action);
	}

	/// <summary>
	/// Перенос привязок измерения угла.
	///
	/// Точки обрабатываются ПАРАМИ, а не поодиночке: 0 и 1 — концы первого
	/// вектора, 2 и 3 — второго. Разрез по измеряемому ребру оставляет
	/// в куске только ОДИН из двух его узлов — второй лезвие съедает,
	/// а на его месте появляется новый узел в точке реза. Поэтому поиск
	/// по положению находит один конец и теряет другой, и обработка
	/// поодиночке неминуемо рвала бы вектор.
	///
	/// Спасает то, что углу нужно не положение второй точки, а НАПРАВЛЕНИЕ.
	/// Новый узел на месте реза лежит на том же луче, что и потерянный:
	/// ребро укоротилось, но не повернулось. Значит, уцелевшего конца и
	/// прежнего направления достаточно, чтобы найти замену — ближайшую
	/// вершину того же куска, лежащую на этом луче.
	/// </summary>
	private static void RetargetAngle(
		AngleMeasurementShape angle,
		Shape oldShape,
		List<Candidate> candidates,
		float tolerance,
		UndoStack? undo)
	{
		bool touches = false;

		foreach (var anchor in angle.Anchors)
		{
			if (ReferenceEquals(anchor.Target, oldShape))
			{
				touches = true;
				break;
			}
		}

		if (!touches)
			return;

		var action = AngleAnchorAction.Capture(angle);

		RetargetVector(angle, oldShape, 0, 1, candidates, tolerance);
		RetargetVector(angle, oldShape, 2, 3, candidates, tolerance);

		angle.InvalidateAnchors();

		action.CaptureAfter();

		if (action.HasChanges())
			undo?.Push(action);
	}

	/// <summary>Переносит пару привязок одного вектора.</summary>
	private static void RetargetVector(
		AngleMeasurementShape angle,
		Shape oldShape,
		int slotA,
		int slotB,
		List<Candidate> candidates,
		float tolerance)
	{
		var anchorA = angle.Anchors[slotA];
		var anchorB = angle.Anchors[slotB];

		bool touchesA = ReferenceEquals(anchorA.Target, oldShape);
		bool touchesB = ReferenceEquals(anchorB.Target, oldShape);

		if (!touchesA && !touchesB)
			return;

		// Мировые точки берутся у ещё живой исходной фигуры: после её
		// удаления восстанавливать было бы уже не из чего.
		bool hasA = ShapeAnchorGeometry.TryResolve(anchorA, out var worldA, out _);
		bool hasB = ShapeAnchorGeometry.TryResolve(anchorB, out var worldB, out _);

		// Переменные объявляются заранее: при коротком замыкании && значение
		// из out осталось бы неприсвоенным, и компилятор не дал бы им
		// воспользоваться ниже.
		Candidate newA = default;
		Candidate newB = default;

		bool foundA = touchesA && hasA
			&& TryFindReplacement(worldA, anchorA.Kind, candidates, tolerance, out newA);

		bool foundB = touchesB && hasB
			&& TryFindReplacement(worldB, anchorB.Kind, candidates, tolerance, out newB);

		// Привязка, чья цель не разрезалась, остаётся как есть и считается
		// найденной: переносить её некуда и незачем.
		if (!touchesA)
		{
			foundA = hasA;
			newA = new Candidate(anchorA.Target!, anchorA.Kind, anchorA.Index, anchorA.NodeId, worldA);
		}

		if (!touchesB)
		{
			foundB = hasB;
			newB = new Candidate(anchorB.Target!, anchorB.Kind, anchorB.Index, anchorB.NodeId, worldB);
		}

		if (!hasA || !hasB)
		{
			// Направление неизвестно — достраивать не от чего.
			ApplyOrClear(angle, slotA, foundA, newA);
			ApplyOrClear(angle, slotB, foundB, newB);
			return;
		}

		var direction = Normalize(worldB.X - worldA.X, worldB.Y - worldA.Y);
		var back = new SKPoint(-direction.X, -direction.Y);

		// Условие touchesA && touchesB существенно: сводить концы на одну
		// фигуру нужно только у вектора, который БЫЛ ребром разрезанной
		// фигуры. Вектор, заданный двумя свободными щелчками, законно
		// опирается на две разные фигуры, и стягивать его на одну значило бы
		// сломать построение, которое пользователь сделал намеренно.
		if (touchesA && touchesB && foundA && foundB &&
			!ReferenceEquals(newA.Owner, newB.Owner))
		{
			// Концы разъехались по разным кускам.
			//
			// Разрез поперёк измеряемого ребра оставляет ОБА его узла, но
			// в разных обломках, и поиск по положению честно находит каждый
			// у своего. Вектор при этом перестаёт быть отрезком одной
			// фигуры: сдвиньте один обломок — и угол поедет, хотя оба конца
			// формально привязаны.
			//
			// Поэтому оба конца сводятся на один кусок: партнёр достраивается
			// по лучу внутри той же фигуры. Сначала пробуем оставить кусок
			// первого конца, затем второго.
			if (TryFindAlongRay(newA.Owner, newA.World, direction, candidates, out var sameAsA))
			{
				newB = sameAsA;
			}
			else if (TryFindAlongRay(newB.Owner, newB.World, back, candidates, out var sameAsB))
			{
				newA = sameAsB;
			}
			else
			{
				// Ни в одном куске продолжения нет — вектор восстановить
				// нечем. Лучше снять обе привязки, чем оставить измерение,
				// растянутое между двумя фигурами.
				foundA = false;
				foundB = false;
			}
		}
		else if (foundA && !foundB &&
			TryFindAlongRay(newA.Owner, newA.World, direction, candidates, out newB))
		{
			foundB = true;
		}
		else if (foundB && !foundA &&
			TryFindAlongRay(newB.Owner, newB.World, back, candidates, out newA))
		{
			foundA = true;
		}

		ApplyOrClear(angle, slotA, foundA, newA);
		ApplyOrClear(angle, slotB, foundB, newB);
	}

	private static void ApplyOrClear(AngleMeasurementShape angle, int slot, bool found, Candidate candidate)
	{
		if (found && candidate.Owner != null)
			angle.SetAnchor(slot, candidate.Owner, candidate.Kind, candidate.Index, candidate.NodeId);
		else
			angle.ClearAnchor(slot);
	}

	/// <summary>
	/// Ищет вершину того же куска, лежащую на луче из <paramref name="origin"/>
	/// в направлении <paramref name="direction"/>.
	///
	/// Из подходящих берётся БЛИЖАЙШАЯ: на месте реза появился новый узел,
	/// и именно он — законный преемник съеденного. Дальние вершины на том
	/// же луче встречаются у прямоугольников и ломаных с коллинеарными
	/// рёбрами, и выбор дальней растянул бы вектор без нужды.
	/// </summary>
	private static bool TryFindAlongRay(
		Shape owner,
		SKPoint origin,
		SKPoint direction,
		List<Candidate> candidates,
		out Candidate best)
	{
		best = default;

		if (direction.X == 0f && direction.Y == 0f)
			return false;

		// Косинус допустимого отклонения от луча. Точка реза лежит на ребре
		// точно, так что запас нужен только на погрешность округления
		// при перестроении куска из пути.
		const float MinCosine = 0.999f;

		float bestDistance = float.MaxValue;
		bool found = false;

		foreach (var candidate in candidates)
		{
			// Только тот же кусок: вершина соседнего обломка может случайно
			// оказаться на том же луче, и вектор ушёл бы на чужую фигуру.
			if (!ReferenceEquals(candidate.Owner, owner))
				continue;

			if (candidate.Kind != ShapeAnchorKind.Vertex)
				continue;

			float dx = candidate.World.X - origin.X;
			float dy = candidate.World.Y - origin.Y;

			float distance = MathF.Sqrt(dx * dx + dy * dy);

			if (distance < 1e-4f)
				continue;

			float cosine = (dx * direction.X + dy * direction.Y) / distance;

			if (cosine < MinCosine)
				continue;

			if (distance >= bestDistance)
				continue;

			bestDistance = distance;
			best = candidate;
			found = true;
		}

		return found;
	}

	private static SKPoint Normalize(float x, float y)
	{
		float len = MathF.Sqrt(x * x + y * y);

		return len < 1e-6f ? SKPoint.Empty : new SKPoint(x / len, y / len);
	}

	private static void RetargetOne(
		ShapeAnchor anchor,
		List<Candidate> candidates,
		float tolerance,
		Action<ShapeAnchor> clear,
		Action<Shape, ShapeAnchorKind, int, Guid> set)
	{
		// Мировая точка берётся у ещё живой исходной фигуры: после её
		// удаления восстанавливать было бы уже не из чего.
		if (!ShapeAnchorGeometry.TryResolve(anchor, out var world, out _))
		{
			clear(anchor);
			return;
		}

		if (TryFindReplacement(world, anchor.Kind, candidates, tolerance, out var best))
			set(best.Owner, best.Kind, best.Index, best.NodeId);
		else
			clear(anchor);
	}

	#endregion

	#region Вспомогательное

	private static float ComputeTolerance(Shape shape)
	{
		var bounds = shape.GetBounds();

		if (bounds.IsEmpty)
			return MinTolerance;

		float span = MathF.Max(bounds.Width, bounds.Height);

		return MathF.Max(span * ToleranceFraction, MinTolerance);
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		return MathF.Sqrt(dx * dx + dy * dy);
	}

	/// <summary>
	/// Снимает привязки измерений с фигур, которые сейчас будут удалены.
	///
	/// Вызывать ДО удаления: нужно ещё видеть, на что указывают привязки.
	///
	/// Без этого Target оставался ссылкой на фигуру, которой на странице
	/// больше нет. В памяти это сходило с рук — объект жив, пока на него
	/// ссылаются, и размер продолжал мериться по нему как ни в чём
	/// не бывало. Ломалось при сохранении: в файл уходил идентификатор
	/// фигуры, которой в файле нет, и при следующем открытии привязка
	/// молча терялась. Пользователь узнавал об этом, только подвинув
	/// деталь и обнаружив, что размер за ней не пошёл.
	///
	/// Точки концов при этом остаются на месте. Измерение перестаёт
	/// следовать за фигурой, но не схлопывается и не улетает в начало
	/// координат — ровно так же ведёт себя Retarget, когда подходящей
	/// цели не нашлось.
	/// </summary>
	public static void DetachFromDeleted(Page page, IReadOnlyList<Shape> deleted, UndoStack? undo)
	{
		if (page == null || deleted == null || deleted.Count == 0)
			return;

		// Удаление группы уносит с собой детей, а привязка могла указывать
		// именно на ребёнка.
		var doomed = new HashSet<Shape>(ReferenceEqualityComparer.Instance);

		foreach (var shape in EnumerateAll(deleted))
			doomed.Add(shape);

		foreach (var shape in EnumerateAll(page.Shapes))
		{
			// Фигуру, которую саму удаляют, отвязывать незачем: она уходит
			// вместе со своими привязками.
			if (doomed.Contains(shape))
				continue;

			if (shape is IShapeAnchorHost host)
				DetachHost(host, doomed, undo);

			// Размер угла держит четыре привязки и под IShapeAnchorHost
			// не подходит: у того интерфейса ровно два конца.
			if (shape is AngleMeasurementShape angle)
				DetachAngle(angle, doomed, undo);
		}
	}

	private static void DetachHost(IShapeAnchorHost host, HashSet<Shape> doomed, UndoStack? undo)
	{
		bool first = PointsAtDoomed(host.FirstAnchor, doomed);
		bool last = PointsAtDoomed(host.LastAnchor, doomed);

		if (!first && !last)
			return;

		var action = CurveAnchorAction.Capture(host);

		if (first)
			host.FirstAnchor.Clear();

		if (last)
			host.LastAnchor.Clear();

		host.InvalidateAnchors();

		action.CaptureAfter();

		if (action.HasChanges())
			undo?.Push(action);
	}

	private static void DetachAngle(AngleMeasurementShape angle, HashSet<Shape> doomed, UndoStack? undo)
	{
		bool any = false;

		foreach (var anchor in angle.Anchors)
			any |= PointsAtDoomed(anchor, doomed);

		if (!any)
			return;

		var action = AngleAnchorAction.Capture(angle);

		foreach (var anchor in angle.Anchors)
		{
			if (PointsAtDoomed(anchor, doomed))
				anchor.Clear();
		}

		angle.InvalidateAnchors();

		action.CaptureAfter();

		if (action.HasChanges())
			undo?.Push(action);
	}

	private static bool PointsAtDoomed(ShapeAnchor anchor, HashSet<Shape> doomed)
		=> anchor.Target != null && doomed.Contains(anchor.Target);

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

	#endregion
}

/// <summary>
/// Шаг истории для четырёх привязок измерения угла.
///
/// Отдельный класс, а не CurveAnchorAction: тот работает через
/// IShapeAnchorHost, у которого ровно два конца.
/// </summary>
public sealed class AngleAnchorAction : UndoableActionBase
{
	private readonly AngleMeasurementShape _shape;
	private readonly ShapeAnchor[] _before;
	private ShapeAnchor[] _after;

	/// <summary>Вызывать ДО изменения.</summary>
	public static AngleAnchorAction Capture(AngleMeasurementShape shape) => new AngleAnchorAction(shape);

	private AngleAnchorAction(AngleMeasurementShape shape)
	{
		_shape = shape;
		_before = CloneAll(shape);
		_after = CloneAll(shape);
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push.</summary>
	public void CaptureAfter() => _after = CloneAll(_shape);

	public bool HasChanges()
	{
		for (int i = 0; i < _before.Length; i++)
		{
			if (!_before[i].SameTarget(_after[i]))
				return true;
		}

		return false;
	}

	public override string Name => "Link anchor";

	public override long MemoryFootprint => 512;

	public override void Undo() => Apply(_before);

	public override void Redo() => Apply(_after);

	private void Apply(ShapeAnchor[] state)
	{
		for (int i = 0; i < state.Length && i < _shape.Anchors.Length; i++)
			_shape.Anchors[i].CopyFrom(state[i]);

		_shape.InvalidateAnchors();
	}

	private static ShapeAnchor[] CloneAll(AngleMeasurementShape shape)
	{
		var result = new ShapeAnchor[shape.Anchors.Length];

		for (int i = 0; i < result.Length; i++)
			result[i] = shape.Anchors[i].Clone();

		return result;
	}

	/// <summary>
	/// Привязки не склеиваются — по той же причине, что и у кривой:
	/// каждая запись рождается от отдельного действия пользователя.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next) => false;
}
