using SkiaSharp;
using System.Globalization;

using VDraw.Documents;
using VDraw.Editing;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Размер угла между двумя векторами.
///
/// Устроен по образцу <see cref="MeasurementShape"/>, но опорная геометрия
/// другая: там отрезок и вынос по нормали, здесь два вектора, их точка
/// пересечения и дуга вокруг неё.
///
/// Векторы хранятся четырьмя точками, а не привязками к сегментам чужих
/// фигур. Щелчок по сегменту при построении разрешается, но он лишь
/// СЧИТЫВАЕТ концы сегмента: живая связь означала бы, что удаление или
/// правка исходной фигуры молча переопределяет угол, а размер на чертеже
/// должен фиксировать то, что измерили.
///
/// Положение подписи задаётся радиусом и угловым сдвигом от биссектрисы —
/// ровно тем же приёмом, что вынос и сдвиг вдоль линии у линейного
/// размера. Обе величины пересчитываются из точки мыши, поэтому
/// перетаскивание подписи не требует отдельного состояния.
/// </summary>
public sealed class AngleMeasurementShape : Shape
{
	#region Константы

	/// <summary>Ниже этой длины вектор считается вырожденным.</summary>
	private const float MinLength = 0.0001f;

	/// <summary>Синус угла, ниже которого векторы считаются параллельными.</summary>
	private const float MinCross = 0.0001f;

	private const float DefaultTextHeightMm = 4f;

	#endregion

	#region Свойства

	/// <summary>Первая точка первого вектора, локальные координаты.</summary>
	public SKPoint S1P1 { get; set; } = SKPoint.Empty;

	/// <summary>Вторая точка первого вектора.</summary>
	public SKPoint S1P2 { get; set; } = new SKPoint(10f, 0f);

	/// <summary>Первая точка второго вектора.</summary>
	public SKPoint S2P1 { get; set; } = SKPoint.Empty;

	/// <summary>Вторая точка второго вектора.</summary>
	public SKPoint S2P2 { get; set; } = new SKPoint(10f, 10f);

	/// <summary>
	/// Радиус дуги от точки пересечения, локальные единицы.
	/// Задаётся последним щелчком и потом перетаскиванием подписи.
	/// </summary>
	public float Radius { get; set; } = 10f;

	/// <summary>
	/// Угловой сдвиг подписи от биссектрисы, в градусах.
	///
	/// Ноль — подпись посередине дуги. Значение больше половины
	/// измеряемого угла выносит подпись за дугу: так поступают на
	/// чертежах, когда угол мал, а текст не помещается.
	/// </summary>
	public float TextAngleOffset { get; set; }

	/// <summary>
	/// Мерить внешний угол вместо внутреннего.
	///
	/// Внутренний — это угол, внутри которого лежат оба выбранных вектора.
	/// Внешний дополняет его до полного оборота.
	/// </summary>
	public bool InvertAngle { get; set; }

	public int Decimals { get; set; } = 1;

	/// <summary>Дописывать ли знак градуса.</summary>
	public bool ShowUnits { get; set; } = true;

	public string TextOverride { get; set; } = string.Empty;

	/// <summary>Длина наконечника как доля кегля подписи.</summary>
	public float ArrowSizeFactor { get; set; } = 0.8f;

	/// <summary>
	/// Носитель параметров шрифта. Отдельный TextShape нужен, чтобы
	/// переиспользовать готовую панель текста: она типизирована по TextShape.
	/// </summary>
	public TextShape TextStyle { get; private set; } = CreateDefaultTextStyle();

	#endregion

	#region Привязки опорных точек

	/// <summary>
	/// Привязки четырёх опорных точек: 0 и 1 — концы первого вектора,
	/// 2 и 3 — второго.
	///
	/// Массив, а не пара свойств, как у IShapeAnchorHost: у того интерфейса
	/// ровно два конца, и притянуть к нему четырёхточечную фигуру можно
	/// было бы только ценой выдумывания «первого» и «последнего» из
	/// четырёх. Механизм при этом переиспользуется целиком — ShapeAnchor,
	/// ShapeAnchorGeometry и подпись состояния те же, что у размера
	/// расстояния.
	/// </summary>
	public ShapeAnchor[] Anchors { get; private set; } =
		new[] { new ShapeAnchor(), new ShapeAnchor(), new ShapeAnchor(), new ShapeAnchor() };

	public bool HasAnchors =>
		Anchors[0].IsActive || Anchors[1].IsActive || Anchors[2].IsActive || Anchors[3].IsActive;

	private int _anchorsAppliedVersion = -1;

	public void InvalidateAnchors() => _anchorsAppliedVersion = -1;

	/// <summary>
	/// Подтягивает опорные точки к местам привязки.
	///
	/// Вызывается перед каждым построением геометрии. Именно поэтому
	/// измерение переживает любое преобразование родительских фигур:
	/// положение точек не хранится, а вычисляется заново из их текущей
	/// геометрии. Повернули деталь — угол пересчитался, дуга и подпись
	/// встали на место. Отдельной подписки на изменение цели не нужно.
	///
	/// Положение подписи при этом сохраняется: оно задано радиусом и
	/// угловым сдвигом от биссектрисы, то есть ОТНОСИТЕЛЬНО измеряемого
	/// угла, а не абсолютной точкой. Поэтому подпись едет вместе с ним
	/// сама, без пересчёта.
	/// </summary>
	public bool ApplyAnchors()
	{
		if (!HasAnchors)
			return false;

		int signature = ComputeAnchorSignature();

		// Ни цели, ни само измерение не двигались — точки уже на местах.
		if (signature == _anchorsAppliedVersion)
			return false;

		bool changed = false;

		for (int i = 0; i < Anchors.Length; i++)
		{
			if (Anchors[i].IsActive)
				changed |= ApplyAnchor(i);
		}

		// Подпись считается заново: ApplyAnchor мог снять привязку, если
		// цель исчезла, и прежнее значение уже не описывает состояние.
		_anchorsAppliedVersion = ComputeAnchorSignature();

		return changed;
	}

	public bool ClearAnchor(int index)
	{
		if (index < 0 || index >= Anchors.Length || !Anchors[index].IsActive)
			return false;

		Anchors[index].Clear();
		InvalidateAnchors();
		return true;
	}

	public bool SetAnchor(int index, Shape? target, ShapeAnchorKind kind, int anchorIndex, Guid nodeId = default)
	{
		if (index < 0 || index >= Anchors.Length)
			return false;

		var anchor = Anchors[index];

		if (ReferenceEquals(target, this))
			return false;

		// Сравнение идёт по идентификатору, когда он есть: номер того же
		// узла меняется после вставки или удаления соседа.
		bool same = ReferenceEquals(anchor.Target, target)
			&& anchor.Kind == kind
			&& (nodeId != Guid.Empty ? anchor.NodeId == nodeId : anchor.Index == anchorIndex);

		if (same)
			return false;

		anchor.Target = target;
		anchor.Kind = target == null ? ShapeAnchorKind.None : kind;
		anchor.Index = anchorIndex;
		anchor.NodeId = target == null ? Guid.Empty : nodeId;

		InvalidateAnchors();
		return true;
	}

	private bool ApplyAnchor(int index)
	{
		var anchor = Anchors[index];

		if (!ShapeAnchorGeometry.TryResolve(anchor, out var worldPoint, out _))
		{
			// Цель исчезла или её точек стало меньше — связь снимается.
			anchor.Clear();
			return true;
		}

		// Привязка к самому себе строила бы координаты из самой себя
		// и не сходилась бы.
		if (ReferenceEquals(anchor.Target, this))
		{
			anchor.Clear();
			return true;
		}

		var local = WorldToLocal(worldPoint);
		var current = GetPoint(index);

		bool changed = Distance(current, local) > 1e-4f;

		SetPoint(index, local);

		return changed;
	}

	/// <summary>
	/// Подпись входных данных привязок.
	///
	/// Включает и геометрию целей, и собственную мировую трансформацию:
	/// положение точки зависит от того, где стоит цель, и от того, где
	/// стоит само измерение, потому что мировая точка переводится
	/// в его локальные координаты.
	/// </summary>
	private int ComputeAnchorSignature()
	{
		var hash = new HashCode();

		foreach (var anchor in Anchors)
			ShapeAnchorGeometry.AddToSignature(ref hash, anchor);

		var m = GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

	public SKPoint GetPoint(int index) => index switch
	{
		0 => S1P1,
		1 => S1P2,
		2 => S2P1,
		_ => S2P2
	};

	public void SetPoint(int index, SKPoint local)
	{
		switch (index)
		{
			case 0: S1P1 = local; break;
			case 1: S1P2 = local; break;
			case 2: S2P1 = local; break;
			default: S2P2 = local; break;
		}
	}

	#endregion

	#region Конструирование

	public AngleMeasurementShape()
	{
		Name = "Angle";

		Stroke.Enabled = true;
		Stroke.Color = SKColors.Black;
		Stroke.StartGlyph = LineGlyphLibrary.EngineerArrow;
		Stroke.EndGlyph = LineGlyphLibrary.EngineerArrow;

		Fill.Enabled = true;
		Fill.Kind = FillKind.Solid;
		Fill.SolidColor = SKColors.Black;
	}

	/// <summary>
	/// Собирает фигуру по двум мировым векторам и точке подписи.
	///
	/// Локальное начало ставится в первую точку первого вектора, как это
	/// делает линейный размер: так LocalTransform несёт только перенос,
	/// и перемещение фигуры не искажает измеренное значение.
	/// </summary>
	public static AngleMeasurementShape CreateFromWorldPoints(
		SKPoint s1p1World,
		SKPoint s1p2World,
		SKPoint s2p1World,
		SKPoint s2p2World,
		SKPoint textWorld,
		float mmPerUom = 1f)
	{
		var shape = new AngleMeasurementShape
		{
			LocalTransform = SKMatrix.CreateTranslation(s1p1World.X, s1p1World.Y),
			S1P1 = SKPoint.Empty,
			S1P2 = new SKPoint(s1p2World.X - s1p1World.X, s1p2World.Y - s1p1World.Y),
			S2P1 = new SKPoint(s2p1World.X - s1p1World.X, s2p1World.Y - s1p1World.Y),
			S2P2 = new SKPoint(s2p2World.X - s1p1World.X, s2p2World.Y - s1p1World.Y)
		};

		// Кегль хранится в единицах страницы, а не в миллиметрах, вопреки
		// имени поля: подпись намеренно не масштабируется матрицей фигуры,
		// поэтому её высота задаётся прямо в мировых единицах.
		shape.TextStyle.FontSizeMm = DefaultTextHeightMm / MathF.Max(mmPerUom, 0.0001f);

		shape.SetTextPositionFromWorldPoint(textWorld);

		return shape;
	}

	private static TextShape CreateDefaultTextStyle()
	{
		return new TextShape
		{
			Name = "AngleMeasurementText",
			Text = string.Empty,
			FontFamily = "Arial",
			FontSizeMm = DefaultTextHeightMm
		};
	}

	public override Shape Clone()
	{
		var clone = (AngleMeasurementShape)base.Clone();

		// MemberwiseClone копирует ссылку: без явного клонирования копия и
		// оригинал делили бы один носитель шрифта, и правка кегля у одного
		// меняла бы вторую фигуру.
		clone.TextStyle = (TextShape)TextStyle.Clone();

		// Привязки копируются содержимым — и сам массив, и каждая из
		// четырёх. MemberwiseClone копирует ссылку на массив, а его
		// элементы это ссылки на ShapeAnchor: копия и оригинал делили
		// бы одни и те же четыре привязки, и снятие связи у копии
		// снимало бы её у оригинала. Так же устроен MeasurementShape.
		//
		// Enabled переносится отдельно: ShapeAnchor.Clone его не копирует.
		clone.Anchors = new ShapeAnchor[Anchors.Length];

		for (int i = 0; i < Anchors.Length; i++)
		{
			clone.Anchors[i] = Anchors[i].Clone();
			clone.Anchors[i].Enabled = Anchors[i].Enabled;
		}

		clone.InvalidateAnchors();

		return clone;
	}

	#endregion

	#region Геометрия: опорные величины

	public override SKRect GetBounds()
	{
		using var world = BuildWorldPath();
		return world.Bounds;
	}

	public override SKRect GetLocalBounds()
	{
		using var local = BuildLocalPath();
		return local.Bounds;
	}

	/// <summary>Измеренный угол в градусах, с учётом инверсии.</summary>
	public float GetMeasuredValue()
	{
		return TryGetLayout(out var L) ? L.AngleDeg : 0f;
	}

	/// <summary>
	/// Мировая система угла: вершина и два единичных луча.
	///
	/// Направление каждого луча выбирается ОТ вершины К середине своего
	/// отрезка, а не по порядку щелчков. Иначе измеренный угол зависел бы
	/// от того, с какого конца пользователь ткнул в сегмент: два клика по
	/// тем же двум линиям давали бы то 60 градусов, то 120.
	/// </summary>
	private bool TryGetWorldFrame(
		out SKPoint apex,
		out SKPoint r1,
		out SKPoint r2,
		out SKPoint a1,
		out SKPoint b1,
		out SKPoint a2,
		out SKPoint b2)
	{
		// Сначала подтягиваем точки к привязкам: все остальные построения
		// идут от них, и порядок здесь важен.
		ApplyAnchors();

		var m = GetWorldTransform();

		a1 = m.MapPoint(S1P1);
		b1 = m.MapPoint(S1P2);
		a2 = m.MapPoint(S2P1);
		b2 = m.MapPoint(S2P2);

		apex = SKPoint.Empty;
		r1 = new SKPoint(1f, 0f);
		r2 = new SKPoint(0f, 1f);

		if (!TryDirection(a1, b1, out var d1) || !TryDirection(a2, b2, out var d2))
			return false;

		float cross = d1.X * d2.Y - d1.Y * d2.X;

		// Параллельные векторы угла не образуют: точка пересечения уходит
		// в бесконечность, и рисовать нечего.
		if (MathF.Abs(cross) < MinCross)
			return false;

		// Пересечение прямых, а не отрезков: вершина угла часто лежит
		// за пределами обоих выбранных сегментов — именно так меряют угол
		// между двумя гранями, не доходящими друг до друга.
		float t = ((a2.X - a1.X) * d2.Y - (a2.Y - a1.Y) * d2.X) / cross;

		apex = new SKPoint(a1.X + d1.X * t, a1.Y + d1.Y * t);

		var mid1 = new SKPoint((a1.X + b1.X) * 0.5f, (a1.Y + b1.Y) * 0.5f);
		var mid2 = new SKPoint((a2.X + b2.X) * 0.5f, (a2.Y + b2.Y) * 0.5f);

		if (!TryDirection(apex, mid1, out r1) || !TryDirection(apex, mid2, out r2))
			return false;

		return true;
	}

	private static bool TryDirection(SKPoint from, SKPoint to, out SKPoint dir)
	{
		float dx = to.X - from.X;
		float dy = to.Y - from.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < MinLength)
		{
			dir = new SKPoint(1f, 0f);
			return false;
		}

		dir = new SKPoint(dx / len, dy / len);
		return true;
	}

	#endregion

	#region Раскладка

	/// <summary>
	/// Полная раскладка угла в мировых координатах.
	///
	/// Считается в одном месте, потому что нужна сразу трём потребителям:
	/// построению путей, попаданию курсора по подписи и её перетаскиванию.
	/// Разъехавшись, они дали бы подпись, которая рисуется в одном месте,
	/// а хватается в другом.
	/// </summary>
	internal struct AngleLayout
	{
		public SKPoint Apex;

		/// <summary>Концы выбранных отрезков в мировых координатах.</summary>
		public SKPoint A1, B1, A2, B2;

		/// <summary>
		/// Единичные направления лучей от вершины. Именно вдоль них лежит
		/// дуга, и именно вдоль них должны идти выносные линии.
		/// </summary>
		public SKPoint R1, R2;

		/// <summary>Начало отсчёта дуги и её знаковый размах, в радианах.</summary>
		public float StartAngle, Sweep;

		/// <summary>Измеренное значение в градусах.</summary>
		public float AngleDeg;

		public float RadiusWorld;

		public string Value;
		public float TextWidth, TextHeight, TextGap;

		public SKPoint TextCenter;

		/// <summary>Касательная в точке подписи — вдоль неё разворачивается текст.</summary>
		public SKPoint TextTangent;

		public bool TextInside;

		/// <summary>Края разрыва дуги под подпись — только когда она внутри.</summary>
		public float BreakFrom, BreakTo;

		/// <summary>Полка под подписью — только когда она снаружи.</summary>
		public SKPoint LeaderFrom, LeaderTo;

		/// <summary>Точки на дуге, где стоят наконечники, и касательные в них.</summary>
		public SKPoint ArcStart, ArcEnd, TangentStart, TangentEnd;
	}

	internal bool TryGetLayout(out AngleLayout L)
	{
		L = default;

		if (!TryGetWorldFrame(out var apex, out var r1, out var r2, out var a1, out var b1, out var a2, out var b2))
			return false;

		L.R1 = r1;
		L.R2 = r2;

		float fs = MathF.Max(0.0001f, TextStyle.FontSizeMm);

		L.Apex = apex;
		L.A1 = a1;
		L.B1 = b1;
		L.A2 = a2;
		L.B2 = b2;
		L.TextGap = fs * 0.4f;

		float start = MathF.Atan2(r1.Y, r1.X);
		float end = MathF.Atan2(r2.Y, r2.X);

		// Нормализация в (-pi, pi]: разность двух атангенсов может дать
		// размах больше полного оборота, и дуга накрутила бы лишний виток.
		float sweep = NormalizeSigned(end - start);

		if (InvertAngle)
		{
			// Внешний угол — та же дуга, но в другую сторону вокруг вершины.
			sweep = sweep >= 0f ? sweep - MathF.Tau : sweep + MathF.Tau;
		}

		L.StartAngle = start;
		L.Sweep = sweep;
		L.AngleDeg = MathF.Abs(sweep) * 180f / MathF.PI;

		L.RadiusWorld = LocalLengthToWorld(MathF.Max(MinLength, Radius));

		if (L.RadiusWorld < MinLength)
			return false;

		L.Value = GetDisplayText(L.AngleDeg);

		using (var font = CreateFont())
		{
			L.TextWidth = string.IsNullOrEmpty(L.Value)
				? 0f
				: TextGlyphs.Measure(font, L.Value, TextStyle.CharacterSpacingMm);

			font.GetFontMetrics(out var metrics);
			L.TextHeight = MathF.Max(fs, metrics.Descent - metrics.Ascent);
		}

		// Угловое положение подписи: середина дуги плюс заданный сдвиг.
		float sign = sweep >= 0f ? 1f : -1f;
		float textAngle = start + sweep * 0.5f + TextAngleOffset * MathF.PI / 180f;

		L.ArcStart = PointOnArc(apex, L.RadiusWorld, start);
		L.ArcEnd = PointOnArc(apex, L.RadiusWorld, start + sweep);

		// Наконечники смотрят наружу дуги, то есть по касательной прочь
		// от её середины.
		L.TangentStart = new SKPoint(
			MathF.Sin(start) * sign,
			-MathF.Cos(start) * sign);

		L.TangentEnd = new SKPoint(
			-MathF.Sin(start + sweep) * sign,
			MathF.Cos(start + sweep) * sign);

		// Длина дуги, доступная под подпись. Сравнивается именно она,
		// а не хорда: текст ставится вдоль дуги, и на тупых углах хорда
		// заметно короче — подпись отбраковывалась бы там, где помещается.
		float arcLength = MathF.Abs(sweep) * L.RadiusWorld;
		float halfTextAngle = (L.TextWidth * 0.5f + L.TextGap) / L.RadiusWorld;

		float tFromStart = NormalizeSigned(textAngle - start) * sign;

		L.TextInside =
			L.TextWidth > 0f &&
			L.TextWidth + 2f * L.TextGap <= arcLength &&
			tFromStart - halfTextAngle >= 0f &&
			tFromStart + halfTextAngle <= MathF.Abs(sweep);

		L.TextCenter = PointOnArc(apex, L.RadiusWorld, textAngle);

		// Касательная в точке подписи: текст идёт вдоль дуги.
		L.TextTangent = new SKPoint(
			-MathF.Sin(textAngle) * sign,
			MathF.Cos(textAngle) * sign);

		if (L.TextInside)
		{
			L.BreakFrom = textAngle - halfTextAngle * sign;
			L.BreakTo = textAngle + halfTextAngle * sign;
			return true;
		}

		// Снаружи: подпись отодвигается от дуги на полвысоты, чтобы сидеть
		// НАД полкой, а не на ней — как и у линейного размера.
		var outward = new SKPoint(MathF.Cos(textAngle), MathF.Sin(textAngle));
		float lift = L.TextHeight * 0.5f + L.TextGap * 0.5f;

		L.TextCenter = new SKPoint(
			L.TextCenter.X + outward.X * lift,
			L.TextCenter.Y + outward.Y * lift);

		// Полка тянется к ближнему концу дуги: подпись ушла за начало —
		// к началу, за конец — к концу.
		bool towardsStart = tFromStart < MathF.Abs(sweep) * 0.5f;

		var anchorOnArc = towardsStart ? L.ArcStart : L.ArcEnd;

		var anchorLifted = new SKPoint(
			anchorOnArc.X + outward.X * 0f,
			anchorOnArc.Y + outward.Y * 0f);

		// Дальний от дуги край текста.
		float halfWidth = L.TextWidth * 0.5f;

		var far = new SKPoint(
			L.TextCenter.X - outward.X * lift + L.TextTangent.X * (towardsStart ? -halfWidth : halfWidth),
			L.TextCenter.Y - outward.Y * lift + L.TextTangent.Y * (towardsStart ? -halfWidth : halfWidth));

		L.LeaderFrom = far;
		L.LeaderTo = anchorLifted;

		return true;
	}

	private static float NormalizeSigned(float angle)
	{
		while (angle <= -MathF.PI)
			angle += MathF.Tau;

		while (angle > MathF.PI)
			angle -= MathF.Tau;

		return angle;
	}

	private static SKPoint PointOnArc(SKPoint center, float radius, float angle)
		=> new(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius);

	/// <summary>
	/// Переводит длину из локальных единиц в мировые через образ отрезка,
	/// а не умножением на масштаб: в LocalTransform может лежать поворот
	/// и неравномерное растяжение.
	/// </summary>
	private float LocalLengthToWorld(float localLength)
	{
		var m = GetWorldTransform();

		var p0 = m.MapPoint(SKPoint.Empty);
		var p1 = m.MapPoint(new SKPoint(localLength, 0f));
		var p2 = m.MapPoint(new SKPoint(0f, localLength));

		float sx = Distance(p0, p1);
		float sy = Distance(p0, p2);

		// Среднее из двух осей: у дуги один радиус, и при неравномерном
		// масштабе точного соответствия не существует в принципе.
		return (sx + sy) * 0.5f;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		return MathF.Sqrt(dx * dx + dy * dy);
	}

	#endregion

	#region Подпись: попадание и перетаскивание

	/// <summary>
	/// Лежит ли мировая точка на подписи.
	///
	/// Проверка идёт в системе координат самой подписи — вдоль касательной
	/// и по радиусу. По габаритному прямоугольнику в мировых осях
	/// наклонная подпись давала бы огромную зону захвата, перекрывающую
	/// соседние фигуры.
	/// </summary>
	public bool HitTestText(SKPoint worldPoint, float tolerance)
	{
		if (!TryGetLayout(out var L) || string.IsNullOrEmpty(L.Value) || L.TextWidth <= 0f)
			return false;

		float dx = worldPoint.X - L.TextCenter.X;
		float dy = worldPoint.Y - L.TextCenter.Y;

		var normal = new SKPoint(-L.TextTangent.Y, L.TextTangent.X);

		float along = MathF.Abs(dx * L.TextTangent.X + dy * L.TextTangent.Y);
		float across = MathF.Abs(dx * normal.X + dy * normal.Y);

		return along <= L.TextWidth * 0.5f + tolerance
			&& across <= L.TextHeight * 0.5f + tolerance;
	}

	public bool TryGetTextCenterWorld(out SKPoint center)
	{
		if (TryGetLayout(out var L) && !string.IsNullOrEmpty(L.Value))
		{
			center = L.TextCenter;
			return true;
		}

		center = SKPoint.Empty;
		return false;
	}

	/// <summary>
	/// Ставит подпись под мировую точку.
	///
	/// Точка раскладывается на две составляющие: расстояние от вершины
	/// становится радиусом дуги, угловое отклонение от биссектрисы —
	/// сдвигом подписи. Поэтому подпись перемещается куда угодно, а дуга
	/// и наконечники следуют за ней сами, без отдельного состояния.
	/// </summary>
	public void SetTextPositionFromWorldPoint(SKPoint worldPoint)
	{
		if (!TryGetWorldFrame(out var apex, out var r1, out var r2, out _, out _, out _, out _))
			return;

		float distWorld = Distance(apex, worldPoint);

		if (distWorld < MinLength)
			return;

		// Радиус переводится обратно в локальные единицы тем же множителем,
		// каким прямое преобразование переводит их в мировые.
		float unitWorld = LocalLengthToWorld(1f);

		if (unitWorld > MinLength)
			Radius = distWorld / unitWorld;

		float start = MathF.Atan2(r1.Y, r1.X);
		float end = MathF.Atan2(r2.Y, r2.X);

		float sweep = NormalizeSigned(end - start);

		if (InvertAngle)
			sweep = sweep >= 0f ? sweep - MathF.Tau : sweep + MathF.Tau;

		float pointAngle = MathF.Atan2(worldPoint.Y - apex.Y, worldPoint.X - apex.X);
		float mid = start + sweep * 0.5f;

		TextAngleOffset = NormalizeSigned(pointAngle - mid) * 180f / MathF.PI;
	}

	#endregion

	#region Геометрия: построение путей

	public override SKPath BuildLocalPath()
	{
		using var world = BuildWorldPath();

		var local = new SKPath();

		if (!TryGetInverseWorldTransform(out var inv))
		{
			local.AddPath(world);
			return local;
		}

		world.Transform(inv, local);
		return local;
	}

	public override SKPath BuildWorldPath()
	{
		var path = new SKPath();

		using var lines = new SKPath();
		using var text = new SKPath();

		BuildWorldParts(lines, text);

		path.AddPath(lines);
		path.AddPath(text);

		return path;
	}

	/// <summary>
	/// Строит обе части фигуры в мировых координатах. Они разделены, потому
	/// что красятся разными кистями: линии — обводкой, текст — заливкой.
	/// </summary>
	public void BuildWorldParts(SKPath? lines, SKPath? text)
	{
		if (!TryGetLayout(out var L))
			return;

		if (lines != null)
		{
			AppendExtensionLine(lines, L.Apex, L.R1, L.A1, L.B1, L.RadiusWorld, L.TextGap);
			AppendExtensionLine(lines, L.Apex, L.R2, L.A2, L.B2, L.RadiusWorld, L.TextGap);

			var oval = new SKRect(
				L.Apex.X - L.RadiusWorld,
				L.Apex.Y - L.RadiusWorld,
				L.Apex.X + L.RadiusWorld,
				L.Apex.Y + L.RadiusWorld);

			float startDeg = L.StartAngle * 180f / MathF.PI;
			float sweepDeg = L.Sweep * 180f / MathF.PI;

			if (L.TextInside)
			{
				float breakFromDeg = L.BreakFrom * 180f / MathF.PI;
				float breakToDeg = L.BreakTo * 180f / MathF.PI;

				AddArc(lines, oval, startDeg, breakFromDeg - startDeg);
				AddArc(lines, oval, breakToDeg, startDeg + sweepDeg - breakToDeg);
			}
			else
			{
				// Подпись не помещается на дуге либо уведена наружу: дуга
				// становится сплошной, как на чертежах, и появляется полка.
				AddArc(lines, oval, startDeg, sweepDeg);

				AddLine(lines, L.LeaderFrom, L.LeaderTo);
			}
		}

		if (text != null && !string.IsNullOrEmpty(L.Value))
		{
			using var font = CreateFont();
			AppendText(text, font, L.Value, L.TextCenter, L.TextTangent, L.TextWidth);
		}
	}

	/// <summary>
	/// Выносная линия, соединяющая измеряемое ребро с дугой.
	///
	/// Отрезок описывается ПРОЕКЦИЯМИ его концов на луч дуги — тем самым
	/// направлением, вдоль которого дуга построена. Получается промежуток
	/// [ближняя, дальняя], и дуга по отношению к нему может лежать
	/// в трёх положениях:
	///
	///   — за отрезком. Так выходит у смежных рёбер: вершина угла лежит
	///     на их общем конце, и при большом радиусе дуга уходит за них.
	///     Линия ведётся от дальнего конца наружу, до дуги с вылетом.
	///
	///   — перед отрезком. Так выходит у рёбер ЧЕРЕЗ ОДНО: их прямые
	///     пересекаются вне фигуры, отрезки отстоят от вершины далеко,
	///     и дуга оказывается ближе них. Линия ведётся от дуги наружу,
	///     до ближнего конца отрезка. Прежняя версия этот случай не
	///     рассматривала вовсе и просто ничего не рисовала — отсюда дуга,
	///     висящая в стороне от фигуры без единой связи с ней.
	///
	///   — внутри отрезка. Дуга пересекает само ребро, соединять нечего.
	///
	/// Отрицательные проекции отбрасываются: конец по другую сторону
	/// вершины к этому лучу отношения не имеет.
	/// </summary>
	private static void AppendExtensionLine(
		SKPath path, SKPoint apex, SKPoint ray, SKPoint a, SKPoint b, float radiusWorld, float overshoot)
	{
		float ta = (a.X - apex.X) * ray.X + (a.Y - apex.Y) * ray.Y;
		float tb = (b.X - apex.X) * ray.X + (b.Y - apex.Y) * ray.Y;

		float near = MathF.Max(MathF.Min(ta, tb), 0f);
		float far = MathF.Max(MathF.Max(ta, tb), 0f);

		if (far <= 0f)
			return;

		float from;
		float to;

		if (far < radiusWorld)
		{
			// Дуга за отрезком.
			from = far;
			to = radiusWorld + overshoot;
		}
		else if (near > radiusWorld)
		{
			// Дуга перед отрезком.
			from = MathF.Max(radiusWorld - overshoot, 0f);
			to = near;
		}
		else
		{
			// Дуга пересекает ребро.
			return;
		}

		if (to - from < 1e-4f)
			return;

		AddLine(
			path,
			new SKPoint(apex.X + ray.X * from, apex.Y + ray.Y * from),
			new SKPoint(apex.X + ray.X * to, apex.Y + ray.Y * to));
	}

	private static void AddArc(SKPath path, SKRect oval, float startDeg, float sweepDeg)
	{
		if (MathF.Abs(sweepDeg) < 0.01f)
			return;

		path.AddArc(oval, startDeg, sweepDeg);
	}

	private void AppendText(SKPath target, SKFont font, string value, SKPoint center, SKPoint tangent, float textWidth)
	{
		font.GetFontMetrics(out var metrics);

		// Подпись центрируется по горизонтали половиной ширины, по вертикали —
		// серединой между верхом и низом шрифта, а не базовой линией, иначе
		// текст сидел бы заметно выше дуги.
		var origin = new SKPoint(
			-textWidth * 0.5f,
			-(metrics.Ascent + metrics.Descent) * 0.5f);

		using var glyphs = new SKPath();
		TextGlyphs.AppendPath(glyphs, font, value, origin, TextStyle.CharacterSpacingMm);
		TextStyle.AppendDecorations(glyphs, font, value, origin, textWidth);

		float angleDeg = MathF.Atan2(tangent.Y, tangent.X) * 180f / MathF.PI;

		// Разворот на 180 для подписи, оказавшейся вверх ногами. Текст
		// центрирован, поэтому положение от разворота не меняется.
		if (tangent.X < 0f)
			angleDeg += 180f;

		var m = SKMatrix.CreateRotationDegrees(angleDeg);
		m = m.PostConcat(SKMatrix.CreateTranslation(center.X, center.Y));

		using var placed = new SKPath();
		glyphs.Transform(m, placed);

		target.AddPath(placed);
	}

	private SKFont CreateFont()
	{
		var typeface =
			EmbeddedFontRegistry.Resolve(TextStyle.FontFamily, TextStyle.Bold, TextStyle.Italic)
			?? SKTypeface.FromFamilyName(
				TextStyle.FontFamily,
				TextStyle.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
				SKFontStyleWidth.Normal,
				TextStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

		return new SKFont(typeface, MathF.Max(0.0001f, TextStyle.FontSizeMm));
	}

	private string GetDisplayText(float angleDeg)
	{
		if (!string.IsNullOrEmpty(TextOverride))
			return TextOverride;

		int decimals = Math.Clamp(Decimals, 0, 6);
		string number = angleDeg.ToString("F" + decimals, CultureInfo.InvariantCulture);

		return ShowUnits ? number + "\u00B0" : number;
	}

	#endregion

	#region Отрисовка

	public override void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		using var lines = new SKPath();
		using var text = new SKPath();

		BuildWorldParts(lines, text);

		if (Stroke.Enabled && !lines.IsEmpty)
		{
			using var strokePaint = Stroke.CreatePaint(mmPerWorldUnit, IsSelected);
			canvas.DrawPath(lines, strokePaint);
			DrawEndGlyphs(canvas, strokePaint);
		}

		if (text.IsEmpty)
			return;

		// Текст рисуется всегда заливкой и никогда обводкой: у размерной
		// надписи обриса не бывает, а Stroke здесь отвечает за линии.
		using var textPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Fill?.SolidColor ?? SKColors.Black
		};

		canvas.DrawPath(text, textPaint);
	}

	/// <summary>
	/// Наконечники на концах дуги. Стоят по касательной и смотрят наружу —
	/// как принято на чертежах.
	/// </summary>
	private void DrawEndGlyphs(SKCanvas canvas, SKPaint strokePaint)
	{
		if (string.IsNullOrEmpty(Stroke.StartGlyph) && string.IsNullOrEmpty(Stroke.EndGlyph))
			return;

		if (!TryGetLayout(out var L))
			return;

		if (!string.IsNullOrEmpty(Stroke.StartGlyph))
		{
			LineGlyphLibrary.Draw(
				canvas,
				L.ArcStart,
				L.TangentStart,
				GetArrowSizeWorld(Stroke.StartGlyphSize),
				Stroke.StartGlyph,
				strokePaint);
		}

		if (!string.IsNullOrEmpty(Stroke.EndGlyph))
		{
			LineGlyphLibrary.Draw(
				canvas,
				L.ArcEnd,
				L.TangentEnd,
				GetArrowSizeWorld(Stroke.EndGlyphSize),
				Stroke.EndGlyph,
				strokePaint);
		}
	}

	/// <summary>
	/// Длина наконечника в мировых единицах. За основу взят кегль подписи,
	/// а не толщина линии: при 0.2 мм штатный множитель дал бы стрелку
	/// в 0.6 мм рядом с четырёхмиллиметровой подписью.
	/// </summary>
	private float GetArrowSizeWorld(float strokeMultiplier)
	{
		float baseSize = MathF.Max(0.0001f, TextStyle.FontSizeMm) * MathF.Max(0.05f, ArrowSizeFactor);
		return baseSize * MathF.Max(0.1f, strokeMultiplier) / 3f;
	}

	#endregion

	#region Взаимодействие

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new AngleMeasurementGeometrySnapshot
		{
			S1P1 = S1P1,
			S1P2 = S1P2,
			S2P1 = S2P1,
			S2P2 = S2P2,
			Radius = Radius,
			TextAngleOffset = TextAngleOffset
		};

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not AngleMeasurementGeometrySnapshot s)
			return;

		S1P1 = s.S1P1;
		S1P2 = s.S1P2;
		S2P1 = s.S2P1;
		S2P2 = s.S2P2;
		Radius = s.Radius;
		TextAngleOffset = s.TextAngleOffset;
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();

		hash.Add(base.GetGeometryHash());
		hash.Add(S1P1.X);
		hash.Add(S1P1.Y);
		hash.Add(S1P2.X);
		hash.Add(S1P2.Y);
		hash.Add(S2P1.X);
		hash.Add(S2P1.Y);
		hash.Add(S2P2.X);
		hash.Add(S2P2.Y);
		hash.Add(Radius);
		hash.Add(TextAngleOffset);
		hash.Add(InvertAngle);
		hash.Add(TextStyle.FontSizeMm);
		hash.Add(TextStyle.FontFamily);

		return hash.ToHashCode();
	}

	public override bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		using var lines = new SKPath();
		using var text = new SKPath();

		BuildWorldParts(lines, text);

		if (!text.IsEmpty && text.Contains(pagePoint.X, pagePoint.Y))
			return true;

		if (lines.IsEmpty)
			return false;

		using var hitPaint = Stroke.CreatePaint(mmPerUom);
		hitPaint.StrokeWidth = MathF.Max(hitPaint.StrokeWidth, toleranceUom);
		hitPaint.PathEffect = null;

		return StrokeHit(lines, hitPaint, pagePoint);
	}

	protected override bool IsClosedGeometry() => false;

	public override void SetShapeNewUoM(float oldMmPerUom, float newMmPerUom)
	{
		base.SetShapeNewUoM(oldMmPerUom, newMmPerUom);

		// Кегль задан в тех же единицах, что и геометрия, поэтому при смене
		// единиц его нужно пересчитать — иначе подпись скачком меняет размер.
		float ratio = oldMmPerUom / MathF.Max(newMmPerUom, 0.0001f);
		TextStyle.FontSizeMm *= ratio;
	}

	#endregion

	#region Векторная арифметика

	private static void AddLine(SKPath path, SKPoint from, SKPoint to)
	{
		path.MoveTo(from);
		path.LineTo(to);
	}

	#endregion
}

/// <summary>
/// Перетаскивание подписи угла.
///
/// Подпись задаётся радиусом и угловым сдвигом, поэтому отдельного
/// состояния операции не нужно: обе величины пересчитываются из текущей
/// точки мыши.
/// </summary>
public sealed class MoveAngleMeasurementTextOperation : DragOperation
{
	private readonly AngleMeasurementShape _shape;

	private float _startRadius;
	private float _startOffset;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveMeasurementText;

	public MoveAngleMeasurementTextOperation(AngleMeasurementShape shape)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_startRadius = _shape.Radius;
		_startOffset = _shape.TextAngleOffset;
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (!_begun)
			return;

		_shape.SetTextPositionFromWorldPoint(pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (_begun)
		{
			_shape.Radius = _startRadius;
			_shape.TextAngleOffset = _startOffset;
		}

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}
}