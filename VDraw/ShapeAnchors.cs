using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

public enum ShapeAnchorKind
{
	None = 0,

	/// <summary>Узел фигуры. Направление выхода — биссектриса угла.</summary>
	Vertex = 1,

	/// <summary>Середина ребра. Направление выхода — нормаль к ребру.</summary>
	EdgeMidpoint = 2,

	/// <summary>
	/// Точка очертания круга или эллипса: сверху, справа, снизу, слева.
	/// Направления выхода — радиальное.
	/// </summary>
	Compass = 3,

	/// <summary>
	/// Центр фигуры. Направления выхода нет: точка лежит внутри
	/// контура, а не на нём.
	///
	/// Предлагается не всегда, а по запросу — см. параметр includeCenter.
	/// Размеры между центрами — обычное дело на чертежах, а вот связь
	/// во flowchart, втыкающаяся в середину блока, — нет.
	/// </summary>
	Center = 4,

	/// <summary>
	/// Произвольная точка НА поверхности фигуры, заданная долями
	/// её локальных габаритов от центра.
	///
	/// Остальные виды описывают особые места контура — узел, середину
	/// ребра, сторону света, — и их наперечёт. Этот описывает любое
	/// место: конец размера поставлен туда, куда его привели, и должен
	/// там остаться, что бы с фигурой ни делали.
	///
	/// Хранится не координатами, а долями: LocalU и LocalV — смещение
	/// от центра локальных габаритов в половинах их ширины и высоты.
	/// Мировые координаты не годятся, они устаревают при первом же
	/// повороте цели. Локальные координаты переживают поворот и перенос,
	/// но не растягивание: у прямоугольника при изменении размера меняется
	/// LocalRect, а не матрица, и точка осталась бы на месте, отвязавшись
	/// от того места фигуры, которое меряли. Доли переживают и это —
	/// ради чего всё и затевалось: растягивая деталь, пользователь видит,
	/// как меняется измеренная часть, и выставляет общий размер по ней.
	/// </summary>
	Surface = 5
}

/// <summary>
/// Фигура с двумя привязываемыми концами.
///
/// Выделено в интерфейс ради общего кода: панель Link, шаг истории
/// и поиск точки привязки одинаковы для кривой и для размера, а геометрия
/// у них разная и общего базового класса нет.
/// </summary>
public interface IShapeAnchorHost
{
	/// <summary>Привязка первого конца.</summary>
	ShapeAnchor FirstAnchor { get; }

	/// <summary>Привязка второго конца.</summary>
	ShapeAnchor LastAnchor { get; }

	/// <summary>Пересчитать привязки при следующем построении.</summary>
	void InvalidateAnchors();

	/// <summary>Снять привязку конца.</summary>
	bool ClearAnchor(bool first);

	/// <summary>
	/// Задать привязку конца.
	///
	/// nodeId — идентификатор узла кривой, если цель — кривая.
	/// Для остальных фигур пустой: там точки привязки выводятся
	/// из габаритов и номер их описывает однозначно.
	/// </summary>
	bool SetAnchor(bool first, Shape? target, ShapeAnchorKind kind, int index, Guid nodeId = default);
}

/// <summary>
/// Привязка конца кривой к месту на другой фигуре.
///
/// Хранится параметрически: ссылка на фигуру плюс номер узла, ребра или
/// стороны света. Мировая точка не хранится намеренно — при повороте или
/// масштабировании цели она устарела бы, а номер остаётся верным.
/// </summary>
public sealed class ShapeAnchor
{
	public Shape? Target { get; set; }
	public ShapeAnchorKind Kind { get; set; } = ShapeAnchorKind.None;
	public int Index { get; set; }
	public bool Enabled { get; set; }

	/// <summary>
	/// Идентификатор узла кривой, к которому привязан конец.
	/// Guid.Empty — привязка не к узлу, а к точке, выведенной из
	/// габаритов или очертания, и там достаточно номера.
	///
	/// Для середины сегмента хранится узел, с которого сегмент
	/// начинается: исчезнет он — исчезнет и сегмент.
	/// </summary>
	public Guid NodeId { get; set; }

	/// <summary>
	/// Выпускать линию по нормали к ребру либо по биссектрисе угла.
	/// Выключено — направление задаёт пользователь ручкой узла.
	/// </summary>
	public bool TangentToEdge { get; set; } = true;

	/// <summary>
	/// Смещение точки от центра локальных габаритов цели, в половинах их
	/// ширины. Ноль — центр, единица — правый край, минус единица — левый.
	/// Осмысленно только при Kind = Surface.
	/// </summary>
	public float LocalU { get; set; }

	/// <summary>То же по вертикали: минус единица — верх, единица — низ.</summary>
	public float LocalV { get; set; }

	public bool IsActive => Target != null && Kind != ShapeAnchorKind.None;

	public ShapeAnchor Clone() => new ShapeAnchor
	{
		Target = Target,
		Kind = Kind,
		Index = Index,
		NodeId = NodeId,
		TangentToEdge = TangentToEdge,
		LocalU = LocalU,
		LocalV = LocalV
	};

	public void Clear()
	{
		Target = null;
		Kind = ShapeAnchorKind.None;
		Index = 0;
		NodeId = Guid.Empty;
		LocalU = 0f;
		LocalV = 0f;
	}

	public void CopyFrom(ShapeAnchor other)
	{
		Target = other.Target;
		Kind = other.Kind;
		Index = other.Index;
		NodeId = other.NodeId;
		TangentToEdge = other.TangentToEdge;
		LocalU = other.LocalU;
		LocalV = other.LocalV;
	}

	/// <summary>
	/// Привязывает конец к точке на поверхности цели.
	///
	/// worldPoint переводится в доли локальных габаритов немедленно:
	/// хранить мировую точку нельзя, она устареет при первом же движении
	/// цели, а пересчитывать её потом будет не из чего.
	/// </summary>
	public void SetSurface(Shape target, SKPoint worldPoint)
	{
		Target = target;
		Kind = ShapeAnchorKind.Surface;
		Index = 0;
		NodeId = Guid.Empty;
		Enabled = true;

		var local = target.WorldToLocal(worldPoint);
		var bounds = target.GetLocalBounds();

		// Вырожденный габарит: у горизонтального отрезка высота ноль,
		// делить на неё нельзя. Доля в таком измерении всегда нулевая —
		// точка и так лежит на оси.
		float halfW = bounds.Width * 0.5f;
		float halfH = bounds.Height * 0.5f;

		LocalU = MathF.Abs(halfW) > 1e-6f ? (local.X - bounds.MidX) / halfW : 0f;
		LocalV = MathF.Abs(halfH) > 1e-6f ? (local.Y - bounds.MidY) / halfH : 0f;
	}

	/// <summary>
	/// Локальная точка, которую описывают доли, по нынешним габаритам цели.
	/// </summary>
	public SKPoint GetSurfaceLocal(Shape target)
	{
		var bounds = target.GetLocalBounds();

		return new SKPoint(
			bounds.MidX + LocalU * bounds.Width * 0.5f,
			bounds.MidY + LocalV * bounds.Height * 0.5f);
	}

	/// <summary>
	/// Та же цель. У привязки к узлу сравнивается идентификатор,
	/// а не номер: номер у того же узла меняется после правки кривой.
	/// </summary>
	public bool SameTarget(ShapeAnchor other)
	{
		if (!ReferenceEquals(Target, other.Target) || Kind != other.Kind)
			return false;

		// У точки на поверхности ни номера, ни узла нет — различает их
		// только положение. Сравнение с допуском, а не на равенство:
		// доли считаются из мировой точки и на одном и том же месте
		// разойдутся в последнем разряде.
		if (Kind == ShapeAnchorKind.Surface)
		{
			return MathF.Abs(LocalU - other.LocalU) < 1e-4f
				&& MathF.Abs(LocalV - other.LocalV) < 1e-4f;
		}

		return NodeId == other.NodeId
			&& (NodeId != Guid.Empty || Index == other.Index);
	}
}

/// <summary>Точка привязки в локальных координатах фигуры-цели.</summary>
public readonly struct ShapeAnchorPoint
{
	public readonly ShapeAnchorKind Kind;
	public readonly int Index;

	/// <summary>Узел кривой, породивший эту точку. Guid.Empty — точка не от узла.</summary>
	public readonly Guid NodeId;

	/// <summary>Сама точка.</summary>
	public readonly SKPoint Local;

	/// <summary>
	/// Соседние точки: для ребра — его концы, для узла — предыдущий и
	/// следующий узлы, для стороны света — центр фигуры в поле A.
	/// </summary>
	public readonly SKPoint LocalA;
	public readonly SKPoint LocalB;

	public ShapeAnchorPoint(
		ShapeAnchorKind kind,
		int index,
		SKPoint local,
		SKPoint a,
		SKPoint b,
		Guid nodeId = default)
	{
		Kind = kind;
		Index = index;
		Local = local;
		LocalA = a;
		LocalB = b;
		NodeId = nodeId;
	}
}

/// <summary>
/// Перечисление и разрешение точек привязки.
///
/// Индексы считаются по ЛОКАЛЬНОЙ геометрии цели, поэтому переживают её
/// перемещение, масштабирование и поворот: пересчитывается только мировая
/// позиция, номер остаётся тем же.
/// </summary>
public static class ShapeAnchorGeometry
{
	/// <summary>
	/// Точки привязки фигуры в её локальных координатах.
	///
	/// includeCenter добавляет центр фигуры. По умолчанию выключено, чтобы
	/// не менять поведение связей во flowchart: им центр как цель не нужен.
	/// При разрешении уже сохранённой привязки центр включается всегда —
	/// иначе ссылка на центр не нашлась бы и связь снималась.
	/// </summary>
	public static List<ShapeAnchorPoint> GetLocalAnchors(Shape? shape, bool includeCenter = false)
	{
		var result = new List<ShapeAnchorPoint>();

		if (shape == null || !shape.IsVisible || shape is GuideLine)
			return result;

		switch (shape)
		{
			// Размер — не объект измерения, а подпись к нему. Собственных
			// точек привязки он не даёт: иначе размеры цеплялись бы друг за
			// друга и за себя, а координаты строились бы из самих себя.
			case MeasurementShape:
				return result;

			case CurveShape curve:
				AddCurveAnchors(curve, result);
				break;
			// Полигон хранит настоящие вершины: привязка идёт по ним, а не
			// по габаритам. При ненулевом RotationRad эти множества
			// расходятся полностью.
			case PolygonShape polygon:
				AddPolylineAnchors(polygon.Points, closed: polygon.Points.Count >= 3, result);
				break;
			// Круг и эллипс: четыре точки очертания. Узлов и рёбер у них нет,
			// поэтому направление выхода радиальное.
			case Circle2PShape:
			case Circle3PShape:
			case Ellipse3PShape:
			case Ellipse5PShape:
				AddCompassAnchors(shape, result);
				break;

			default:
				// Прямоугольник, скруглённый и срезанный прямоугольник,
				// изображение, текст, группа: углы и середины сторон габаритов.
				AddRectAnchors(shape.GetLocalBounds(), result);
				break;
		}

		if (includeCenter)
			AddCenterAnchor(shape, result);

		return result;
	}

	/// <summary>
	/// Центр фигуры — середина её локальных габаритов.
	///
	/// Габариты, а не центроид контура: пользователь целится в середину
	/// того, что видит, а видит он рамку выделения. Для круга и эллипса
	/// это и есть настоящий центр.
	///
	/// Соседние точки равны самой: направления выхода у центра нет.
	/// </summary>
	private static void AddCenterAnchor(Shape shape, List<ShapeAnchorPoint> result)
	{
		var r = shape.GetLocalBounds();

		if (r.IsEmpty)
			return;

		var center = new SKPoint(r.MidX, r.MidY);

		result.Add(new ShapeAnchorPoint(ShapeAnchorKind.Center, 0, center, center, center));
	}

	/// <summary>
	/// Точки привязки для последовательности точек: сами точки как вершины
	/// и середины отрезков как рёбра.
	///
	/// closed = true добавляет замыкающее ребро от последней точки к первой
	/// и делает биссектрису первой и последней вершины полноценной: у них
	/// появляются оба соседа.
	/// </summary>
	private static void AddPolylineAnchors(IReadOnlyList<SKPoint> points, bool closed, List<ShapeAnchorPoint> result)
	{
		int count = points.Count;

		if (count == 0)
			return;

		if (count == 1)
		{
			result.Add(new ShapeAnchorPoint(
				ShapeAnchorKind.Vertex, 0, points[0], points[0], points[0]));

			return;
		}

		// Вершины. Соседи нужны для биссектрисы; у концов разомкнутой ломаной
		// соседа с одной стороны нет, и вместо него подставляется сама точка —
		// тогда биссектриса вырождается в нормаль к единственному ребру.
		for (int i = 0; i < count; i++)
		{
			SKPoint prev = i > 0
				? points[i - 1]
				: (closed ? points[count - 1] : points[i]);

			SKPoint next = i < count - 1
				? points[i + 1]
				: (closed ? points[0] : points[i]);

			result.Add(new ShapeAnchorPoint(ShapeAnchorKind.Vertex, i, points[i], prev, next));
		}

		// Рёбра. Номер ребра равен номеру его начальной точки.
		int edgeCount = closed ? count : count - 1;

		for (int i = 0; i < edgeCount; i++)
		{
			var a = points[i];
			var b = points[(i + 1) % count];

			result.Add(new ShapeAnchorPoint(
				ShapeAnchorKind.EdgeMidpoint,
				i,
				new SKPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f),
				a,
				b));
		}
	}

	private static void AddRectAnchors(SKRect r, List<ShapeAnchorPoint> result)
	{
		if (r.IsEmpty)
			return;

		// Порядок фиксирован: он и есть номер привязки.
		var corners = new[]
		{
			new SKPoint(r.Left, r.Top),
			new SKPoint(r.Right, r.Top),
			new SKPoint(r.Right, r.Bottom),
			new SKPoint(r.Left, r.Bottom)
		};

		for (int i = 0; i < 4; i++)
		{
			var prev = corners[(i + 3) % 4];
			var next = corners[(i + 1) % 4];

			result.Add(new ShapeAnchorPoint(ShapeAnchorKind.Vertex, i, corners[i], prev, next));
		}

		for (int i = 0; i < 4; i++)
		{
			var a = corners[i];
			var b = corners[(i + 1) % 4];

			result.Add(new ShapeAnchorPoint(
				ShapeAnchorKind.EdgeMidpoint,
				i,
				new SKPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f),
				a,
				b));
		}
	}

	private static void AddCompassAnchors(Shape shape, List<ShapeAnchorPoint> result)
	{
		var r = shape.GetLocalBounds();

		if (r.IsEmpty)
			return;

		var center = new SKPoint(r.MidX, r.MidY);

		// Направления: верх, право, низ, лево. Порядок фиксирован — он и есть
		// номер привязки.
		var directions = new[]
		{
			new SKPoint(0f, -1f),
			new SKPoint(1f, 0f),
			new SKPoint(0f, 1f),
			new SKPoint(-1f, 0f)
		};

		// Максимальная дальность луча: половина диагонали габаритов с запасом.
		float maxDistance = MathF.Sqrt(r.Width * r.Width + r.Height * r.Height) * 0.5f + 1f;

		using var localPath = shape.BuildLocalPath();

		for (int i = 0; i < 4; i++)
		{
			var point = ProjectOntoOutline(localPath, center, directions[i], maxDistance);
			result.Add(new ShapeAnchorPoint(ShapeAnchorKind.Compass, i, point, center, center));
		}
	}

	/// <summary>
	/// Точка выхода луча из центра за пределы контура.
	///
	/// Двоичный поиск по принадлежности: находим отрезок, на одном конце
	/// которого точка внутри, на другом снаружи, и делим его пополам.
	/// Работает для любого выпуклого очертания независимо от того, повёрнуто
	/// оно внутри локальных координат или нет.
	///
	/// Если центр оказался снаружи — так бывает у невыпуклых фигур — точка
	/// на габаритах возвращается как есть: это не хуже прежнего поведения.
	/// </summary>
	private static SKPoint ProjectOntoOutline(SKPath? path, SKPoint center, SKPoint dir, float maxDistance)
	{
		var fallback = new SKPoint(
			center.X + dir.X * maxDistance,
			center.Y + dir.Y * maxDistance);

		if (path == null || path.IsEmpty || !path.Contains(center.X, center.Y))
			return fallback;

		float lo = 0f;
		float hi = maxDistance;

		// Двадцати делений хватает: погрешность падает до миллионной доли
		// габаритов, что заведомо меньше видимой точности.
		for (int i = 0; i < 20; i++)
		{
			float mid = (lo + hi) * 0.5f;

			var probe = new SKPoint(center.X + dir.X * mid, center.Y + dir.Y * mid);

			if (path.Contains(probe.X, probe.Y))
				lo = mid;
			else
				hi = mid;
		}

		return new SKPoint(center.X + dir.X * lo, center.Y + dir.Y * lo);
	}

	private static void AddCurveAnchors(CurveShape curve, List<ShapeAnchorPoint> result)
	{
		var nodes = curve.Nodes;

		if (nodes.Count == 0)
			return;

		// Узлы. Соседи берутся внутри своего контура, поэтому биссектриса
		// на стыке контуров не строится по чужим точкам.
		for (int i = 0; i < nodes.Count; i++)
		{
			GetContourNeighbours(curve, i, out var prev, out var next);

			result.Add(new ShapeAnchorPoint(
				ShapeAnchorKind.Vertex,
				i,
				nodes[i].Position,
				prev,
				next,
				nodes[i].Id));
		}

		// Середины сегментов. Номер ребра равен номеру начального узла.
		for (int i = 0; i < nodes.Count; i++)
		{
			if (!TryGetSegmentEnd(curve, i, out int endIndex))
				continue;

			var a = nodes[i].Position;
			var b = nodes[endIndex].Position;

			result.Add(new ShapeAnchorPoint(
				ShapeAnchorKind.EdgeMidpoint,
				i,
				new SKPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f),
				a,
				b,
				nodes[i].Id));
		}
	}

	/// <summary>Соседи узла внутри его контура. При отсутствии соседа возвращается сам узел.</summary>
	private static void GetContourNeighbours(CurveShape curve, int index, out SKPoint prev, out SKPoint next)
	{
		var nodes = curve.Nodes;

		prev = nodes[index].Position;
		next = nodes[index].Position;

		var range = curve.GetContourRangeFor(index);

		if (range.End <= range.Start)
			return;

		int prevIndex = index > range.Start
			? index - 1
			: (range.Closed ? range.End : -1);

		int nextIndex = index < range.End
			? index + 1
			: (range.Closed ? range.Start : -1);

		if (prevIndex >= 0) prev = nodes[prevIndex].Position;
		if (nextIndex >= 0) next = nodes[nextIndex].Position;
	}

	/// <summary>Конец сегмента, начинающегося в узле index. false, если сегмента нет.</summary>
	private static bool TryGetSegmentEnd(CurveShape curve, int index, out int endIndex)
	{
		endIndex = -1;

		var range = curve.GetContourRangeFor(index);

		if (range.End <= range.Start)
			return false;

		if (index < range.End)
		{
			endIndex = index + 1;
			return true;
		}

		if (range.Closed)
		{
			endIndex = range.Start;
			return true;
		}

		return false;
	}

	/// <summary>Точка привязки и направление выхода наружу, в мировых координатах.</summary>
	public static bool TryResolve(ShapeAnchor anchor, out SKPoint worldPoint, out SKPoint outwardWorld)
	{
		worldPoint = default;
		outwardWorld = new SKPoint(1f, 0f);

		if (anchor == null || !anchor.IsActive)
			return false;

		var target = anchor.Target!;

		// Точка на поверхности не ищется среди особых мест контура —
		// её там нет. Она считается прямо из долей и нынешних габаритов,
		// поэтому переживает и перенос цели, и поворот, и растягивание.
		if (anchor.Kind == ShapeAnchorKind.Surface)
		{
			worldPoint = target.LocalToWorld(anchor.GetSurfaceLocal(target));

			// Наружу — от центра фигуры через саму точку. Иного разумного
			// направления у внутренней точки нет, а OrientOutward развернёт
			// вектор, если он смотрит внутрь.
			var centerWorld = target.LocalToWorld(
				new SKPoint(target.GetLocalBounds().MidX, target.GetLocalBounds().MidY));

			var away = Normalize(new SKPoint(
				worldPoint.X - centerWorld.X,
				worldPoint.Y - centerWorld.Y));

			outwardWorld = Length(away) < 1e-6f ? new SKPoint(1f, 0f) : away;
			return true;
		}

		bool byNode = anchor.NodeId != Guid.Empty;

		foreach (var candidate in GetLocalAnchors(target, includeCenter: true))
		{
			if (candidate.Kind != anchor.Kind)
				continue;

			// Привязка к узлу ищется только по идентификатору. Отката
			// на номер нет намеренно: если узла больше нет, его номер
			// теперь принадлежит чужому узлу, и связь туда перескочила бы.
			if (byNode)
			{
				if (candidate.NodeId != anchor.NodeId)
					continue;
			}
			else if (candidate.Index != anchor.Index)
			{
				continue;
			}

			worldPoint = target.LocalToWorld(candidate.Local);
			outwardWorld = ComputeOutward(target, candidate, worldPoint);
			return true;
		}

		// Точка исчезла: узел удалён либо цель отредактирована.
		// Вызывающий снимет привязку, оставив конец там, где он был.
		return false;
	}

	private static SKPoint ComputeOutward(Shape target, ShapeAnchorPoint candidate, SKPoint worldPoint)
	{
		SKPoint dir;

		switch (candidate.Kind)
		{
			// У центра направления выхода нет. Возвращаем горизонталь и сразу,
			// минуя OrientOutward: точка лежит внутри контура, и проба
			// на принадлежность только развернула бы вектор без всякого смысла.
			case ShapeAnchorKind.Center:
				return new SKPoint(1f, 0f);

			case ShapeAnchorKind.EdgeMidpoint:
				{
					var a = target.LocalToWorld(candidate.LocalA);
					var b = target.LocalToWorld(candidate.LocalB);

					// Нормаль к ребру.
					dir = Normalize(new SKPoint(-(b.Y - a.Y), b.X - a.X));
					break;
				}

			case ShapeAnchorKind.Compass:
				{
					var center = target.LocalToWorld(candidate.LocalA);
					dir = Normalize(new SKPoint(worldPoint.X - center.X, worldPoint.Y - center.Y));
					break;
				}

			default:
				{
					var prev = target.LocalToWorld(candidate.LocalA);
					var next = target.LocalToWorld(candidate.LocalB);

					var toPrev = Normalize(new SKPoint(prev.X - worldPoint.X, prev.Y - worldPoint.Y));
					var toNext = Normalize(new SKPoint(next.X - worldPoint.X, next.Y - worldPoint.Y));

					// Биссектриса угла, направленная внутрь: сумма единичных
					// векторов к соседям. Наружу — противоположная.
					var inward = new SKPoint(toPrev.X + toNext.X, toPrev.Y + toNext.Y);

					if (Length(inward) < 1e-4f)
					{
						// Соседние рёбра коллинеарны, биссектриса не определена.
						// Берём нормаль к любому из них.
						dir = Normalize(new SKPoint(-toNext.Y, toNext.X));
					}
					else
					{
						dir = Normalize(new SKPoint(-inward.X, -inward.Y));
					}

					break;
				}
		}

		return OrientOutward(target, worldPoint, dir);
	}

	/// <summary>
	/// Разворачивает направление наружу фигуры.
	///
	/// Проверка идёт попаданием точки в путь, а не сравнением с центроидом:
	/// у невыпуклых фигур — звезды, результата вычитания, буквы с вырезом —
	/// центроид может лежать снаружи, и знак получился бы неверным.
	/// </summary>
	private static SKPoint OrientOutward(Shape target, SKPoint worldPoint, SKPoint dir)
	{
		if (Length(dir) < 1e-6f)
			return new SKPoint(1f, 0f);

		using var worldPath = target.BuildWorldPath();

		if (worldPath == null || worldPath.IsEmpty)
			return dir;

		var bounds = worldPath.Bounds;

		// Шаг пробы соразмерен фигуре: слишком мелкий утонет в погрешности,
		// слишком крупный выйдет за противоположную стенку тонкой фигуры.
		float eps = MathF.Max(0.01f, MathF.Min(bounds.Width, bounds.Height) * 0.02f);

		var probe = new SKPoint(worldPoint.X + dir.X * eps, worldPoint.Y + dir.Y * eps);

		if (worldPath.Contains(probe.X, probe.Y))
			return new SKPoint(-dir.X, -dir.Y);

		return dir;
	}

	/// <summary>
	/// Добавляет в подпись всё, от чего зависит положение привязанного
	/// конца: саму привязку, форму цели и её положение в мире. Потребители
	/// сравнивают подпись с предыдущей и пропускают пересчёт, когда ничего
	/// не менялось: разрешение привязки перебирает все точки цели и делать
	/// это по нескольку раз на кадр накладно.
	/// </summary>
	public static void AddToSignature(ref HashCode hash, ShapeAnchor anchor)
	{
		hash.Add(anchor.Kind);
		hash.Add(anchor.Index);
		hash.Add(anchor.NodeId);
		hash.Add(anchor.TangentToEdge);
		hash.Add(anchor.Enabled);
		hash.Add(anchor.LocalU);
		hash.Add(anchor.LocalV);

		if (anchor.Target == null)
		{
			hash.Add(0);
			return;
		}

		hash.Add(anchor.Target.GetGeometryHash());

		var m = anchor.Target.GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);
	}

	private static SKPoint Normalize(SKPoint v)
	{
		float len = Length(v);
		return len < 1e-6f ? new SKPoint(0f, 0f) : new SKPoint(v.X / len, v.Y / len);
	}

	private static float Length(SKPoint v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);
}

/// <summary>
/// Шаг истории для изменения привязок. Работает с любой фигурой,
/// у которой есть два привязываемых конца — с кривой и с размером.
/// </summary>
public sealed class CurveAnchorAction : UndoableActionBase
{
	private readonly IShapeAnchorHost _curve;
	private readonly ShapeAnchor _firstBefore;
	private readonly ShapeAnchor _lastBefore;
	private ShapeAnchor _firstAfter;
	private ShapeAnchor _lastAfter;

	/// <summary>Вызывать ДО изменения.</summary>
	public static CurveAnchorAction Capture(IShapeAnchorHost curve) => new CurveAnchorAction(curve);

	private CurveAnchorAction(IShapeAnchorHost curve)
	{
		_curve = curve;
		_firstBefore = curve.FirstAnchor.Clone();
		_lastBefore = curve.LastAnchor.Clone();
		_firstAfter = _firstBefore.Clone();
		_lastAfter = _lastBefore.Clone();
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push.</summary>
	public void CaptureAfter()
	{
		_firstAfter = _curve.FirstAnchor.Clone();
		_lastAfter = _curve.LastAnchor.Clone();
	}

	public bool HasChanges()
		=> !_firstBefore.SameTarget(_firstAfter)
		|| !_lastBefore.SameTarget(_lastAfter)
		|| _firstBefore.TangentToEdge != _firstAfter.TangentToEdge
		|| _lastBefore.TangentToEdge != _lastAfter.TangentToEdge;

	public override string Name => "Link anchor";
	public override long MemoryFootprint => 256;

	public override void Undo() => Apply(_firstBefore, _lastBefore);
	public override void Redo() => Apply(_firstAfter, _lastAfter);

	private void Apply(ShapeAnchor first, ShapeAnchor last)
	{
		_curve.FirstAnchor.CopyFrom(first);
		_curve.LastAnchor.CopyFrom(last);
		_curve.InvalidateAnchors();
	}

	/// <summary>
	/// Привязки не склеиваются.
	///
	/// Каждая запись рождается от отдельного действия пользователя — щелчка
	/// по галочке в панели или перетаскивания конца. Слияние по одной лишь
	/// общей фигуре делало из двух разных правок один шаг: включил привязку
	/// P1, затем P2 — одна отмена снимала обе.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next) => false;
}