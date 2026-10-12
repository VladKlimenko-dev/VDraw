using SkiaSharp;
using System.Globalization;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Размер (измеренное значение) как на инженерных чертежах.
///
/// Геометрия задаётся тремя величинами в локальных координатах:
///   P1, P2 — измеряемые точки;
///   H      — знаковое расстояние от вектора P1-P2 до размерной линии.
///
/// Фигура состоит из текста и четырёх линий:
///   1) выносная линия из P1 перпендикулярно вектору P1-P2;
///   2) выносная линия из P2 перпендикулярно вектору P1-P2;
///   3) размерная линия от выносной линии 1 до текста;
///   4) размерная линия от текста до выносной линии 2.
///
/// Всё построение ведётся В МИРОВЫХ координатах, а не в локальных с
/// последующей трансформацией. Причина: при растягивании фигуры мышью
/// в LocalTransform попадает масштаб, и локальный путь растянулся бы
/// целиком — вместе с глифами текста. Здесь же масштаб влияет только на
/// положение опорных точек: по горизонтали меняется длина вектора P1-P2,
/// по вертикали — вынос H, а кегль текста остаётся заданным в панели.
/// Именно такое поведение и требуется от размера.
///
/// Цвета: Stroke.Color — цвет линий, Fill.SolidColor — цвет текста.
/// Обводки у текста нет намеренно: у размерной надписи её не бывает.
/// </summary>
public sealed class MeasurementShape : Shape, IShapeAnchorHost
{
	#region Константы

	/// <summary>Ниже этой длины вектор P1-P2 считается вырожденным.</summary>
	private const float MinLength = 0.0001f;

	/// <summary>Высота подписи по умолчанию, в миллиметрах.</summary>
	private const float DefaultTextHeightMm = 4f;

	#endregion

	#region Свойства

	/// <summary>Начальная измеряемая точка, локальные координаты.</summary>
	public SKPoint P1 { get; set; } = SKPoint.Empty;

	/// <summary>Конечная измеряемая точка, локальные координаты.</summary>
	public SKPoint P2 { get; set; } = new SKPoint(10f, 0f);

	/// <summary>
	/// Знаковый вынос размерной линии от вектора P1-P2, локальные единицы.
	/// Знак задаёт сторону: пользователь выбирает её третьим щелчком.
	/// </summary>
	public float H { get; set; } = 10f;

	/// <summary>
	/// Сдвиг подписи вдоль размерной линии от её середины, локальные единицы.
	///
	/// Ноль — подпись посередине, как было раньше. Значение больше половины
	/// длины выносит подпись за пределы измеряемого промежутка: так поступают
	/// на чертежах, когда размер мал, а текст не помещается.
	///
	/// Вдоль линии, а не в виде свободной точки: перпендикулярное положение
	/// подписи задаёт H, и хранить его дважды означало бы дать двум величинам
	/// расходиться.
	/// </summary>
	public float TextOffset { get; set; }

	/// <summary>Знаков после запятой в подписи.</summary>
	public int Decimals { get; set; } = 2;

	/// <summary>
	/// Множитель отображаемого значения.
	///
	/// Меняет только подпись, но не саму геометрию: размер по-прежнему
	/// измеряет реальную длину на листе. Нужен для чертежей в масштабе —
	/// план в масштабе 1:100 рисуют в сантиметрах, а подписывают в метрах,
	/// и множитель 0.01 избавляет от пересчёта каждой подписи вручную.
	///
	/// Ноль запрещён: подпись «0.000» на любом размере выглядела бы
	/// как поломка, а не как настройка.
	/// </summary>
	public float Factor
	{
		get => _factor;
		set => _factor = MathF.Abs(value) < 0.000001f ? 1f : value;
	}

	private float _factor = 1f;

	/// <summary>
	/// Значение, введённое пользователем вместо измеренного.
	///
	/// Нужно там, где чертёж не в масштабе: деталь нарисована условно,
	/// а подписать её надо настоящим размером. От TextOverride отличается
	/// тем, что остаётся ЧИСЛОМ — к нему по-прежнему приписывается единица
	/// измерения и применяется округление до Decimals.
	///
	/// Держится вместе с длиной, при которой было введено. Как только
	/// размер растянут или сдвинута привязанная фигура, длина расходится
	/// с запомненной, и подпись сама возвращается к измеренной. Отдельного
	/// уведомления об изменении геометрии для этого не нужно, а значит,
	/// нет и места, где о нём можно забыть.
	/// </summary>
	public float? ValueOverride { get; private set; }

	private float _valueOverrideLength;

	/// <summary>Длина, при которой введённое значение ещё считается своим.</summary>
	private const float ValueOverrideTolerance = 0.0005f;

	public void SetValueOverride(float value)
	{
		ValueOverride = value;
		_valueOverrideLength = GetMeasuredValue();
	}

	public void ClearValueOverride() => ValueOverride = null;

	/// <summary>
	/// Число, которое сейчас стоит в подписи: введённое, если оно ещё
	/// в силе, иначе измеренное с учётом множителя.
	/// </summary>
	public float GetDisplayValue()
	{
		float length = GetMeasuredValue();

		return IsValueOverrideActive(length)
			? ValueOverride!.Value
			: length * Factor;
	}

	private bool IsValueOverrideActive(float worldLength)
		=> ValueOverride.HasValue
			&& MathF.Abs(worldLength - _valueOverrideLength) < ValueOverrideTolerance;

	/// <summary>
	/// Длина наконечника как доля кегля подписи.
	///
	/// От кегля, а не от толщины линии, как у остальных фигур. У размера
	/// линии тонкие по определению (0.2 мм по умолчанию), и наконечник
	/// в три толщины оказывался длиной 0.6 мм — то есть практически невидимым
	/// рядом с подписью в 4 мм. На чертежах стрелка соразмерна шрифту,
	/// поэтому она растёт вместе с ним и не требует отдельного контрола.
	/// </summary>
	public float ArrowSizeFactor { get; set; } = 0.8f;

	/// <summary>Дописывать ли к значению название единицы измерения страницы.</summary>
	public bool ShowUnits { get; set; } = true;

	/// <summary>
	/// Ручная подпись вместо измеренного значения. Пустая строка — считать
	/// длину автоматически.
	/// </summary>
	public string TextOverride { get; set; } = string.Empty;

	/// <summary>
	/// Носитель параметров шрифта. Отдельный TextShape нужен, чтобы для этой
	/// фигуры переиспользовать готовую панель текста: она типизирована по
	/// TextShape. Из него берутся только гарнитура, кегль и начертание —
	/// цвет текста задаёт Fill самой фигуры.
	/// </summary>
	public TextShape TextStyle { get; private set; } = CreateDefaultTextStyle();

	#endregion

	#region Конструирование

	public MeasurementShape()
	{
		Name = "Measurement";

		Stroke.Enabled = true;
		Stroke.Color = SKColors.Black;
		Stroke.StartGlyph = LineGlyphLibrary.EngineerArrow;
		Stroke.EndGlyph = LineGlyphLibrary.EngineerArrow;

		Fill.Enabled = true;
		Fill.Kind = FillKind.Solid;
		Fill.SolidColor = SKColors.Black;

		// У размера привязка разрешена сразу, в отличие от кривой:
		// измеряют всегда между точками фигур, а не между произвольными
		// местами листа, и требовать включать галочку каждый раз было бы лишним
		// шагом в самом частом сценарии.
		FirstAnchor.Enabled = true;
		LastAnchor.Enabled = true;
	}

	/// <summary>
	/// Собирает фигуру по трём мировым точкам инструмента измерения.
	/// Третья точка задаёт только вынос: её проекция на направление
	/// P1-P2 отбрасывается, иначе подпись уезжала бы вдоль размера.
	/// </summary>
	public static MeasurementShape CreateFromWorldPoints(
		SKPoint p1World,
		SKPoint p2World,
		SKPoint textWorld,
		float mmPerUom = 1f)
	{
		var shape = new MeasurementShape
		{
			LocalTransform = SKMatrix.CreateTranslation(p1World.X, p1World.Y),
			P1 = SKPoint.Empty,
			P2 = new SKPoint(p2World.X - p1World.X, p2World.Y - p1World.Y)
		};

		// Кегль хранится В ЕДИНИЦАХ СТРАНИЦЫ, а не в миллиметрах, вопреки
		// имени поля: подпись размера намеренно не масштабируется матрицей
		// фигуры (см. описание класса), поэтому её высота задаётся прямо
		// в мировых единицах. Именно поэтому SetShapeNewUoM пересчитывает
		// это значение при смене единиц.
		//
		// Значение по умолчанию тоже обязано быть в единицах страницы.
		// Константа 4 подразумевала миллиметры, и на дюймовой странице
		// новая подпись получалась высотой 4 дюйма вместо 4 мм.
		shape.TextStyle.FontSizeMm = DefaultTextHeightMm / MathF.Max(mmPerUom, 0.0001f);

		shape.SetOffsetFromWorldPoint(textWorld);
		return shape;
	}

	private static TextShape CreateDefaultTextStyle()
	{
		return new TextShape
		{
			Name = "MeasurementText",
			Text = string.Empty,
			FontFamily = "Arial",
			// Значение для миллиметровой страницы. Фабрика
			// CreateFromWorldPoints пересчитывает его под единицы страницы.
			FontSizeMm = DefaultTextHeightMm
		};
	}

	public override Shape Clone()
	{
		var clone = (MeasurementShape)base.Clone();

		// MemberwiseClone копирует ссылку: без явного клонирования копия и
		// оригинал делили бы один носитель шрифта, и правка кегля у одного
		// меняла бы вторую фигуру.
		clone.TextStyle = (TextShape)TextStyle.Clone();

		// Привязки копируются содержимым: при общей ссылке снятие
		// связи у копии снимало бы её и у оригинала.
		clone.FirstAnchor = FirstAnchor.Clone();
		clone.LastAnchor = LastAnchor.Clone();
		clone.FirstAnchor.Enabled = FirstAnchor.Enabled;
		clone.LastAnchor.Enabled = LastAnchor.Enabled;
		clone.InvalidateAnchors();

		return clone;
	}

	#endregion

	#region Привязки концов

	/// <summary>Привязка точки P1 к месту на другой фигуре.</summary>
	public ShapeAnchor FirstAnchor { get; private set; } = new();

	/// <summary>Привязка точки P2 к месту на другой фигуре.</summary>
	public ShapeAnchor LastAnchor { get; private set; } = new();

	/// <summary>Есть ли хотя бы одна активная привязка.</summary>
	public bool HasAnchors => FirstAnchor.IsActive || LastAnchor.IsActive;

	private int _anchorsAppliedVersion = -1;

	/// <summary>Пересчитать привязки при следующем построении.</summary>
	public void InvalidateAnchors() => _anchorsAppliedVersion = -1;

	/// <summary>
	/// Подтягивает P1 и P2 к точкам привязки.
	///
	/// Вызывается перед каждым построением геометрии. Именно поэтому размер
	/// переживает любое преобразование измеряемой фигуры: положение концов
	/// не хранится, а вычисляется заново из её текущей геометрии. Растянули
	/// прямоугольник — привязанный конец остался на своём углу, длина размера
	/// пересчиталась, подпись обновилась. Отдельной подписки на изменение
	/// цели не нужно.
	///
	/// Оба конца могут быть привязаны к ОДНОЙ фигуре: это обычный случай,
	/// когда меряют сторону или диагональ одного объекта.
	///
	/// Возвращает true, если что-то изменилось.
	/// </summary>
	public bool ApplyAnchors()
	{
		if (!FirstAnchor.IsActive && !LastAnchor.IsActive)
			return false;

		int signature = ComputeAnchorSignature();

		// Ни цель, ни сам размер не двигались — концы уже на местах.
		if (signature == _anchorsAppliedVersion)
			return false;

		bool changed = false;

		if (FirstAnchor.IsActive)
			changed |= ApplyAnchor(FirstAnchor, first: true);

		if (LastAnchor.IsActive)
			changed |= ApplyAnchor(LastAnchor, first: false);

		// Подпись считается заново: ApplyAnchor мог снять привязку, если цель
		// исчезла, и прежнее значение уже не описывает состояние.
		_anchorsAppliedVersion = ComputeAnchorSignature();

		return changed;
	}

	/// <summary>Снять привязку конца.</summary>
	public bool ClearAnchor(bool first)
	{
		var anchor = first ? FirstAnchor : LastAnchor;

		if (!anchor.IsActive)
			return false;

		anchor.Clear();
		InvalidateAnchors();
		return true;
	}

	/// <summary>
	/// Задать привязку конца. Возвращает true, если привязка изменилась.
	/// </summary>
	public bool SetAnchor(bool first, Shape? target, ShapeAnchorKind kind, int index, Guid nodeId = default)
	{
		var anchor = first ? FirstAnchor : LastAnchor;

		if (ReferenceEquals(target, this))
			return false;

		// Сравнение идёт по идентификатору, когда он есть: номер того же
		// узла меняется после вставки или удаления соседа.
		bool same = ReferenceEquals(anchor.Target, target)
			&& anchor.Kind == kind
			&& (nodeId != Guid.Empty ? anchor.NodeId == nodeId : anchor.Index == index);

		if (same)
			return false;

		anchor.Target = target;
		anchor.Kind = target == null ? ShapeAnchorKind.None : kind;
		anchor.Index = index;
		anchor.NodeId = target == null ? Guid.Empty : nodeId;

		InvalidateAnchors();
		return true;
	}

	/// <summary>
	/// Привязывает конец к точке НА поверхности фигуры.
	///
	/// Отдельно от SetAnchor: там цель описывается номером узла или ребра,
	/// а здесь — положением, и общего параметра у них нет. Точка приходит
	/// в мировых координатах и тут же переводится в доли габаритов цели,
	/// потому что хранить мировую нельзя — она устареет при первом же
	/// движении фигуры.
	///
	/// target = null снимает привязку.
	/// </summary>
	public bool SetSurfaceAnchor(bool first, Shape? target, SKPoint worldPoint)
	{
		var anchor = first ? FirstAnchor : LastAnchor;

		if (ReferenceEquals(target, this))
			return false;

		if (target == null)
		{
			if (!anchor.IsActive)
				return false;

			anchor.Clear();
			InvalidateAnchors();
			return true;
		}

		var probe = new ShapeAnchor();
		probe.SetSurface(target, worldPoint);

		if (anchor.SameTarget(probe))
			return false;

		anchor.CopyFrom(probe);
		anchor.Enabled = true;

		InvalidateAnchors();
		return true;
	}

	/// <summary>
	/// Прямая установка измеряемой точки. Используется инструментами при
	/// перетаскивании: адорнеры рисуются раньше, чем построение вызовет
	/// ApplyAnchors, и без этого маркер отставал бы от курсора на кадр.
	/// </summary>
	public void SetMeasurePoint(bool first, SKPoint local)
	{
		if (first)
			P1 = local;
		else
			P2 = local;
	}

	private bool ApplyAnchor(ShapeAnchor anchor, bool first)
	{
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
		var current = first ? P1 : P2;

		bool changed = Distance(current, local) > 1e-4f;
		SetMeasurePoint(first, local);

		return changed;
	}

	/// <summary>
	/// Подпись входных данных привязок.
	///
	/// Включает и геометрию целей, и собственную мировую трансформацию:
	/// положение конца зависит от того, где стоит цель, и от того, где стоит
	/// сам размер, потому что мировая точка переводится в его локальные
	/// координаты.
	/// </summary>
	private int ComputeAnchorSignature()
	{
		var hash = new HashCode();

		ShapeAnchorGeometry.AddToSignature(ref hash, FirstAnchor);
		ShapeAnchorGeometry.AddToSignature(ref hash, LastAnchor);

		var m = GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
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

	/// <summary>
	/// Измеренное значение в единицах страницы.
	///
	/// Мировая единица проекта совпадает с единицей страницы (Page.uom),
	/// поэтому длина в мировых координатах и есть искомое значение.
	/// </summary>
	public float GetMeasuredValue()
	{
		if (!TryGetWorldFrame(out _, out _, out _, out _, out float length, out _))
			return 0f;

		return length;
	}

	/// <summary>
	/// Пересчитывает вынос H так, чтобы размерная линия прошла через
	/// заданную мировую точку. Используется инструментом на третьем щелчке.
	/// </summary>
	public void SetOffsetFromWorldPoint(SKPoint worldPoint)
	{
		var local = WorldToLocal(worldPoint);

		var n = GetLocalNormal();
		H = (local.X - P1.X) * n.X + (local.Y - P1.Y) * n.Y;
	}

	private SKPoint GetLocalNormal()
	{
		float dx = P2.X - P1.X;
		float dy = P2.Y - P1.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < MinLength)
			return new SKPoint(0f, 1f);

		return new SKPoint(-dy / len, dx / len);
	}

	/// <summary>
	/// Мировая система размера: точки, единичное направление, нормаль,
	/// длина и вынос. false — вектор P1-P2 вырожден, рисовать нечего.
	/// </summary>
	private bool TryGetWorldFrame(
		out SKPoint w1,
		out SKPoint w2,
		out SKPoint u,
		out SKPoint n,
		out float length,
		out float h)
	{
		// Сначала подтягиваем концы к точкам привязки: все остальные
		// построения идут от P1 и P2, и порядок здесь важен.
		ApplyAnchors();

		var m = GetWorldTransform();

		w1 = m.MapPoint(P1);
		w2 = m.MapPoint(P2);

		float dx = w2.X - w1.X;
		float dy = w2.Y - w1.Y;
		length = MathF.Sqrt(dx * dx + dy * dy);

		if (length < MinLength)
		{
			u = new SKPoint(1f, 0f);
			n = new SKPoint(0f, 1f);
			h = 0f;
			return false;
		}

		u = new SKPoint(dx / length, dy / length);
		n = new SKPoint(-u.Y, u.X);

		// Вынос считается через мировой образ точки выноса, а не умножением
		// H на масштаб: так корректно отрабатывают поворот, неравномерный
		// масштаб и отражение — всё, что может оказаться в LocalTransform.
		var localN = GetLocalNormal();
		var offsetLocal = new SKPoint(P1.X + localN.X * H, P1.Y + localN.Y * H);
		var offsetWorld = m.MapPoint(offsetLocal);

		h = (offsetWorld.X - w1.X) * n.X + (offsetWorld.Y - w1.Y) * n.Y;
		return true;
	}

	#endregion

	#region Подпись: попадание и перетаскивание

	/// <summary>
	/// Лежит ли мировая точка на подписи.
	///
	/// Проверка идёт в системе координат самой подписи — вдоль оси размера
	/// и по нормали к ней. По габаритному прямоугольнику в мировых осях
	/// наклонный размер давал бы огромную зону захвата, перекрывающую
	/// соседние фигуры.
	/// </summary>
	public bool HitTestText(SKPoint worldPoint, float tolerance)
	{
		if (!TryGetLayout(out var L) || string.IsNullOrEmpty(L.Value) || L.TextWidth <= 0f)
			return false;

		var d = Delta(L.TextCenter, worldPoint);

		float along = MathF.Abs(Dot(d, L.U));
		float across = MathF.Abs(Dot(d, L.N));

		return along <= L.TextWidth * 0.5f + tolerance
			&& across <= L.TextHeight * 0.5f + tolerance;
	}

	/// <summary>Центр подписи в мировых координатах — нужен адорнеру.</summary>
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
	/// Точка раскладывается на две составляющие: вдоль оси размера она
	/// становится TextOffset, поперёк — выносом H. Поэтому подпись
	/// перемещается куда угодно, а размерная линия и стрелки следуют
	/// за ней сами, без отдельного состояния.
	/// </summary>
	public void SetTextPositionFromWorldPoint(SKPoint worldPoint)
	{
		if (!TryGetWorldFrame(out var w1, out var w2, out var u, out var n, out float length, out _))
			return;

		// Поперёк: тем же способом, что и третий щелчок инструмента.
		SetOffsetFromWorldPoint(worldPoint);

		// Вдоль: расстояние от середины отрезка, переведённое в локальные
		// единицы. Делим на мировую длину и умножаем на локальную — так
		// уцелеет масштаб из LocalTransform.
		var mid = new SKPoint((w1.X + w2.X) * 0.5f, (w1.Y + w2.Y) * 0.5f);
		float alongWorld = Dot(Delta(mid, worldPoint), u);

		float localLength = MathF.Sqrt(
			(P2.X - P1.X) * (P2.X - P1.X) + (P2.Y - P1.Y) * (P2.Y - P1.Y));

		if (length < MinLength || localLength < MinLength)
			return;

		TextOffset = alongWorld * (localLength / length);
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
	/// Любой из аргументов может быть null, если нужна только одна часть.
	/// </summary>
	public void BuildWorldParts(SKPath? lines, SKPath? text)
	{
		if (!TryGetLayout(out var L))
			return;

		if (lines != null)
		{
			// 1 и 2: выносные линии, перпендикулярные вектору P1-P2.
			AddLine(lines, Offset(L.W1, L.N, L.Side * L.StartGap), Offset(L.A1, L.N, L.Side * L.Overshoot));
			AddLine(lines, Offset(L.W2, L.N, L.Side * L.StartGap), Offset(L.A2, L.N, L.Side * L.Overshoot));

			if (L.TextInside)
			{
				// 3 и 4: размерная линия, разорванная подписью.
				if (Dot(Delta(L.LineStart, L.BreakStart), L.U) > 0f)
					AddLine(lines, L.LineStart, L.BreakStart);

				if (Dot(Delta(L.BreakEnd, L.LineEnd), L.U) > 0f)
					AddLine(lines, L.BreakEnd, L.LineEnd);
			}
			else
			{
				// Подпись не помещается между выносными либо уведена наружу:
				// размерная линия становится сплошной, как на чертежах.
				AddLine(lines, L.LineStart, L.LineEnd);

				// Полка под подписью. Идёт от дальнего края текста до точки
				// пересечения выносной и размерной линий, поэтому при
				// подписи впритык её длина равна ширине текста, а при
				// уведённой дальше — растягивается, не теряя связи с размером.
				AddLine(lines, L.LeaderFrom, L.LeaderTo);
			}
		}

		if (text != null && !string.IsNullOrEmpty(L.Value))
		{
			using var font = CreateFont();
			AppendText(text, font, L.Value, L.TextCenter, L.U, L.TextWidth, L.TextLift);
		}
	}

	/// <summary>
	/// Полная раскладка размера в мировых координатах.
	///
	/// Считается в одном месте, потому что нужна сразу трём потребителям:
	/// построению путей, попаданию курсора по подписи и её перетаскиванию.
	/// Разъехавшись, они дали бы подпись, которая рисуется в одном месте,
	/// а хватается в другом.
	/// </summary>
	internal struct MeasurementLayout
	{
		public SKPoint W1, W2, U, N;
		public SKPoint A1, A2;
		public float Length, H;
		public float Side, StartGap, Overshoot, TextGap;

		public string Value;
		public float TextWidth, TextHeight;

		/// <summary>Центр подписи в мировых координатах.</summary>
		public SKPoint TextCenter;

		/// <summary>Подъём подписи над размерной линией. Ноль — подпись на линии.</summary>
		public float TextLift;

		/// <summary>
		/// Знак, с которым подъём откладывается по нормали, чтобы подпись
		/// оказалась НАД линией с точки зрения читающего.
		///
		/// Верх глифов после поворота на угол линии смотрит в -N. Но подпись
		/// разворачивается на 180 градусов, когда размер идёт справа налево
		/// (иначе читалась бы вверх ногами), и вместе с ней переворачивается
		/// её верх — тогда это +N.
		/// </summary>
		public float TextUpSign;

		public bool TextInside;

		/// <summary>Концы размерной линии с учётом отступа под стрелки.</summary>
		public SKPoint LineStart, LineEnd;

		/// <summary>Края разрыва под подпись — только когда она внутри.</summary>
		public SKPoint BreakStart, BreakEnd;

		/// <summary>Полка под подписью — только когда она снаружи.</summary>
		public SKPoint LeaderFrom, LeaderTo;
	}

	internal bool TryGetLayout(out MeasurementLayout L)
	{
		L = default;

		if (!TryGetWorldFrame(out var w1, out var w2, out var u, out var n, out float length, out float h))
			return false;

		float fs = MathF.Max(0.0001f, TextStyle.FontSizeMm);

		L.W1 = w1;
		L.W2 = w2;
		L.U = u;
		L.N = n;
		L.Length = length;
		L.H = h;

		// Отступы выносных линий и разрыв под подпись пропорциональны кеглю:
		// при мелком шрифте фиксированные значения выглядели бы грубо.
		L.StartGap = fs * 0.25f;
		L.Overshoot = fs * 0.5f;
		L.TextGap = fs * 0.4f;
		L.Side = h >= 0f ? 1f : -1f;

		// Тот же признак, по которому AppendText разворачивает подпись.
		// Держать эти два места в согласии обязательно: разойдись они,
		// подпись уезжала бы под линию на половине направлений.
		L.TextUpSign = u.X < 0f ? 1f : -1f;

		L.A1 = Offset(w1, n, h);
		L.A2 = Offset(w2, n, h);

		L.Value = GetDisplayText(length);

		using (var font = CreateFont())
		{
			L.TextWidth = string.IsNullOrEmpty(L.Value)
				? 0f
				: TextGlyphs.Measure(font, L.Value, TextStyle.CharacterSpacingMm);

			font.GetFontMetrics(out var metrics);
			L.TextHeight = MathF.Max(fs, metrics.Descent - metrics.Ascent);
		}

		// Сдвиг подписи задан в локальных единицах, а раскладка мировая:
		// переводим через образ отрезка, чтобы уцелели поворот и масштаб.
		float offsetWorld = LocalLengthToWorldAlong(TextOffset, u);

		var mid = new SKPoint((L.A1.X + L.A2.X) * 0.5f, (L.A1.Y + L.A2.Y) * 0.5f);
		var textCenterOnLine = Offset(mid, u, offsetWorld);

		// Отступ концов линии под стрелочные глифы — тот же, что и раньше.
		float insetStart = LineGlyphLibrary.GetLineInsetWorld(
			Stroke.StartGlyph,
			GetArrowSizeWorld(Stroke.StartGlyphSize));

		float insetEnd = LineGlyphLibrary.GetLineInsetWorld(
			Stroke.EndGlyph,
			GetArrowSizeWorld(Stroke.EndGlyphSize));

		L.LineStart = Offset(L.A1, u, insetStart);
		L.LineEnd = Offset(L.A2, u, -insetEnd);

		float half = L.TextWidth * 0.5f + L.TextGap;

		// Подпись считается вместившейся, только если целиком лежит между
		// выносными линиями. Проверяются оба края: смещённый текст может
		// вылезти за один конец, оставаясь короче самого промежутка.
		float tCenter = Dot(Delta(L.A1, textCenterOnLine), u);

		L.TextInside =
			L.TextWidth > 0f &&
			L.TextWidth + 2f * L.TextGap <= length &&
			tCenter - half >= 0f &&
			tCenter + half <= length;

		if (L.TextInside)
		{
			L.TextCenter = textCenterOnLine;
			L.TextLift = 0f;
			L.BreakStart = Offset(textCenterOnLine, u, -half);
			L.BreakEnd = Offset(textCenterOnLine, u, half);
			return true;
		}

		// Снаружи: подпись сидит НАД полкой, а не на линии.
		L.TextLift = L.TextHeight * 0.5f + L.TextGap * 0.5f;
		// По ориентации ПОДПИСИ, а не по стороне выноса.
		//
		// Side говорит лишь о том, с какой стороны от измеряемых точек
		// проведена размерная линия, и к тому, где у подписи верх,
		// отношения не имеет. При выносе вниз (H отрицательный) Side
		// уводил подпись под линию, хотя на чертеже она всегда стоит
		// над размерной линией — с какой бы стороны та ни шла.
		L.TextCenter = Offset(textCenterOnLine, n, L.TextUpSign * L.TextLift);

		// Полка тянется к ближней точке пересечения: слева — к A1, справа — к A2.
		bool towardsStart = tCenter < length * 0.5f;

		var anchor = towardsStart ? L.A1 : L.A2;

		// Дальний от размера край текста.
		var far = Offset(textCenterOnLine, u, towardsStart ? -L.TextWidth * 0.5f : L.TextWidth * 0.5f);

		// Полка идёт на уровне размерной линии, под подписью.
		L.LeaderFrom = far;
		L.LeaderTo = anchor;

		// Вырожденный случай: подпись стоит ровно над точкой пересечения,
		// полка схлопнулась бы в точку. Даём ей длину текста, иначе подпись
		// повисла бы ни на чём.
		if (MathF.Abs(Dot(Delta(L.LeaderFrom, L.LeaderTo), u)) < L.TextWidth)
			L.LeaderFrom = Offset(anchor, u, towardsStart ? -L.TextWidth : L.TextWidth);

		return true;
	}

	/// <summary>
	/// Переводит длину, заданную в локальных единицах вдоль оси размера,
	/// в мировую. Через образ отрезка, а не умножением на масштаб: в
	/// LocalTransform может лежать поворот и неравномерное растяжение.
	/// </summary>
	private float LocalLengthToWorldAlong(float localLength, SKPoint worldU)
	{
		if (MathF.Abs(localLength) < MinLength)
			return 0f;

		float dx = P2.X - P1.X;
		float dy = P2.Y - P1.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < MinLength)
			return 0f;

		var m = GetWorldTransform();

		var p0 = m.MapPoint(P1);
		var p1 = m.MapPoint(new SKPoint(P1.X + dx / len * localLength, P1.Y + dy / len * localLength));

		return Dot(Delta(p0, p1), worldU);
	}

	private void AppendText(SKPath target, SKFont font, string value, SKPoint center, SKPoint u, float textWidth, float lift)
	{
		font.GetFontMetrics(out var metrics);

		// Подпись центрируется на размерной линии: по горизонтали — половиной
		// ширины, по вертикали — серединой между верхом и низом шрифта,
		// а не базовой линией, иначе текст сидел бы заметно выше линии.
		var origin = new SKPoint(
			-textWidth * 0.5f,
			-(metrics.Ascent + metrics.Descent) * 0.5f);

		using var glyphs = new SKPath();
		TextGlyphs.AppendPath(glyphs, font, value, origin, TextStyle.CharacterSpacingMm);
		TextStyle.AppendDecorations(glyphs, font, value, origin, textWidth);

		float angleDeg = MathF.Atan2(u.Y, u.X) * 180f / MathF.PI;

		// Разворот на 180° для размеров, идущих справа налево: иначе подпись
		// читалась бы вверх ногами. Текст центрирован, поэтому положение
		// от разворота не меняется.
		if (u.X < 0f)
			angleDeg += 180f;

		// Подъём над полкой уже учтён в center раскладкой: подпись
		// смещена по нормали, а не по своей локальной оси, иначе при
		// повёрнутом размере она уехала бы не в ту сторону.
		_ = lift;

		var m = SKMatrix.CreateRotationDegrees(angleDeg);
		m = m.PostConcat(SKMatrix.CreateTranslation(center.X, center.Y));

		using var placed = new SKPath();
		glyphs.Transform(m, placed);

		target.AddPath(placed);
	}

	private SKFont CreateFont()
	{
		var typeface = SKTypeface.FromFamilyName(
			TextStyle.FontFamily,
			TextStyle.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
			SKFontStyleWidth.Normal,
			TextStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

		return new SKFont(typeface, MathF.Max(0.0001f, TextStyle.FontSizeMm));
	}

	private string GetDisplayText(float worldLength)
	{
		if (!string.IsNullOrEmpty(TextOverride))
			return TextOverride;

		int decimals = Math.Clamp(Decimals, 0, 6);

		// Множитель применяется к подписи, а не к длине: GetMeasuredValue
		// и вся геометрия обязаны остаться в единицах листа, иначе
		// привязки и вынос размерной линии поехали бы вместе с подписью.
		//
		// Введённое значение множителем не трогается: пользователь ввёл
		// уже то, что хочет видеть.
		float value = IsValueOverrideActive(worldLength)
			? ValueOverride!.Value
			: worldLength * Factor;

		string number = value.ToString("F" + decimals, CultureInfo.InvariantCulture);

		if (!ShowUnits)
			return number;

		string unit = MainForm.CurrentPage?.uom?.title ?? string.Empty;
		return string.IsNullOrEmpty(unit) ? number : $"{number} {unit}";
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
	/// Наконечники на размерных линиях 3 и 4 — тех самых, которые
	/// примыкают к подписи. Каждый стоит на внешнем конце своей линии,
	/// у соответствующей выносной, и смотрит наружу — как принято на чертежах.
	///
	/// Тип глифа берётся из панели Line (по умолчанию Engineer arrow),
	/// размер — из GetArrowSizeWorld.
	/// </summary>
	private void DrawEndGlyphs(SKCanvas canvas, SKPaint strokePaint)
	{
		if (string.IsNullOrEmpty(Stroke.StartGlyph) && string.IsNullOrEmpty(Stroke.EndGlyph))
			return;

		if (!TryGetWorldFrame(out var w1, out var w2, out var u, out var n, out _, out float h))
			return;

		var a1 = Offset(w1, n, h);
		var a2 = Offset(w2, n, h);

		if (!string.IsNullOrEmpty(Stroke.StartGlyph))
		{
			LineGlyphLibrary.Draw(
				canvas,
				a1,
				new SKPoint(-u.X, -u.Y),
				GetArrowSizeWorld(Stroke.StartGlyphSize),
				Stroke.StartGlyph,
				strokePaint);
		}

		if (!string.IsNullOrEmpty(Stroke.EndGlyph))
		{
			LineGlyphLibrary.Draw(
				canvas,
				a2,
				u,
				GetArrowSizeWorld(Stroke.EndGlyphSize),
				Stroke.EndGlyph,
				strokePaint);
		}
	}

	/// <summary>
	/// Длина наконечника в мировых единицах.
	///
	/// За основу взят кегль подписи, а не толщина линии, как в StrokeStyle:
	/// при толщине 0.2 мм штатный множитель 3 давал бы стрелку в 0.6 мм
	/// рядом с четырёхмиллиметровой подписью — практически невидимую.
	///
	/// Множитель из панели Line всё же учитывается, приведённый к своему
	/// штатному значению 3: иначе поля размера глифа при выбранном размере
	/// оставались бы включёнными, но ни на что не влиящими.
	/// </summary>
	private float GetArrowSizeWorld(float strokeMultiplier)
	{
		float baseSize = MathF.Max(0.0001f, TextStyle.FontSizeMm) * MathF.Max(0.05f, ArrowSizeFactor);
		return baseSize * MathF.Max(0.1f, strokeMultiplier) / 3f;
	}

	#endregion

	#region Взаимодействие

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new MeasurementGeometrySnapshot { P1 = P1, P2 = P2, H = H, TextOffset = TextOffset };

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not MeasurementGeometrySnapshot s)
			return;

		P1 = s.P1;
		P2 = s.P2;
		H = s.H;
		TextOffset = s.TextOffset;

		// Концы восстановлены вручную, кэш привязок про это не знает:
		// без сброса ApplyAnchors посчитала бы, что пересчитывать нечего,
		// и при следующей отрисовке ничего бы не изменилось.
		InvalidateAnchors();
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(P1.X);
		hash.Add(P1.Y);
		hash.Add(P2.X);
		hash.Add(P2.Y);
		hash.Add(H);
		hash.Add(TextOffset);
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

	private static SKPoint Delta(SKPoint from, SKPoint to)
		=> new(to.X - from.X, to.Y - from.Y);

	private static float Dot(SKPoint a, SKPoint b)
		=> a.X * b.X + a.Y * b.Y;

	private static SKPoint Offset(SKPoint origin, SKPoint direction, float distance)
		=> new(origin.X + direction.X * distance, origin.Y + direction.Y * distance);

	#endregion
}

/// <summary>
/// Перетаскивание подписи размера.
///
/// Подпись задаётся двумя величинами — выносом H и сдвигом вдоль линии, —
/// поэтому отдельного состояния операции не нужно: обе пересчитываются
/// из текущей точки мыши.
/// </summary>
public sealed class MoveMeasurementTextOperation : DragOperation
{
	private readonly MeasurementShape _shape;

	private float _startH;
	private float _startOffset;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveMeasurementText;

	public MoveMeasurementTextOperation(MeasurementShape shape)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_startH = _shape.H;
		_startOffset = _shape.TextOffset;
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
			_shape.H = _startH;
			_shape.TextOffset = _startOffset;
		}

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}
}
