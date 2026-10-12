using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Пошаговое преобразование одной фигуры в другую.
///
/// Инструмент — самостоятельная фигура: его можно выделить, подвинуть,
/// повернуть и растянуть, как любую другую. Собственной геометрии он при
/// этом не имеет вовсе: все шаги пересчитываются из двух привязанных фигур
/// при каждом изменении их формы. Поэтому «трансформация любой из
/// выбранных фигур меняет инструмент» достаётся даром — никаких
/// уведомлений не нужно, достаточно сравнить хеши геометрии.
///
///   ShapeA, ShapeB — привязанные фигуры, шаг 0 и шаг N+1;
///   Steps          — сколько промежуточных шагов между ними;
///   P1, P2         — где стоит первый и последний шаг (локальные координаты);
///   Bias           — распределение расстояний между шагами.
///
/// P1 и P2 хранятся в ЛОКАЛЬНЫХ координатах инструмента, поэтому
/// LocalTransform двигает и поворачивает всю цепочку разом.
/// </summary>
public sealed class TransformShape : Shape
{
	#region Константы

	/// <summary>Сколько точек берётся на контур при пересчёте формы.</summary>
	private const int SamplesPerContour = 96;

	/// <summary>Предел числа шагов. Выше — уже не рисунок, а нагрузка.</summary>
	public const int MaxSteps = 200;

	private const float MinLength = 0.0001f;

	#endregion

	#region Свойства

	private Shape? _shapeA;
	private Shape? _shapeB;
	private Guid _shapeAId;
	private Guid _shapeBId;
	private int _steps = 5;
	private float _bias = 0.5f;
	private SKPoint _p1;
	private SKPoint _p2;
	private bool _pointsInitialized;

	/// <summary>
	/// Идентификатор первой привязанной фигуры — ВЕДУЩЕЕ значение связи.
	///
	/// Ссылка на объект рядом с ним всего лишь кэш. Причина в том, что
	/// фигура на странице заменяется на другой ЭКЗЕМПЛЯР чаще, чем кажется:
	/// конвертация в кривую строит новый CurveShape, булевы операции —
	/// новый результат. Ссылка после такой замены указывает на объект,
	/// снятый со страницы, и инструмент остаётся привязан к фигуре,
	/// которой больше нет. Идентификатор же переживает замену, если её
	/// провели через TransformBindingHelper.Retarget, и переживает
	/// сохранение с загрузкой.
	///
	/// Так же устроены привязки узлов (ShapeAnchor.NodeId) и концов
	/// размера.
	/// </summary>
	public Guid ShapeAId
	{
		get => _shapeAId;
		set { _shapeAId = value; InvalidateGeometry(); }
	}

	/// <summary>Идентификатор второй привязанной фигуры.</summary>
	public Guid ShapeBId
	{
		get => _shapeBId;
		set { _shapeBId = value; InvalidateGeometry(); }
	}

	/// <summary>
	/// Первая привязанная фигура.
	///
	/// Присваивание null НЕ стирает идентификатор: связь при этом только
	/// теряет разрешённую ссылку, но остаётся описанной — иначе загрузка
	/// документа, в котором фигура ещё не создана вторым проходом,
	/// обнуляла бы связь безвозвратно.
	/// </summary>
	public Shape? ShapeA
	{
		get => _shapeA;
		set
		{
			_shapeA = value;

			if (value != null)
				_shapeAId = value.Id;

			InvalidateGeometry();
		}
	}

	/// <summary>Вторая привязанная фигура.</summary>
	public Shape? ShapeB
	{
		get => _shapeB;
		set
		{
			_shapeB = value;

			if (value != null)
				_shapeBId = value.Id;

			InvalidateGeometry();
		}
	}

	/// <summary>Число промежуточных шагов между привязанными фигурами.</summary>
	public int Steps
	{
		get => _steps;
		set
		{
			int v = Math.Clamp(value, 1, MaxSteps);
			if (v == _steps)
				return;

			_steps = v;
			InvalidateGeometry();
		}
	}

	/// <summary>
	/// Положение ползунка между P1 и P2, 0..1.
	///
	/// 0.5 — расстояния между шагами одинаковые. К 0 первые промежутки
	/// сжимаются, а последние растягиваются; к 1 наоборот.
	/// </summary>
	public float Bias
	{
		get => _bias;
		set
		{
			float v = Math.Clamp(value, 0f, 1f);
			if (MathF.Abs(v - _bias) < 1e-6f)
				return;

			_bias = v;
			InvalidateGeometry();
		}
	}

	/// <summary>Начало цепочки шагов, локальные координаты.</summary>
	public SKPoint P1
	{
		get => _p1;
		set { _p1 = value; _pointsInitialized = true; InvalidateGeometry(); }
	}

	/// <summary>Конец цепочки шагов, локальные координаты.</summary>
	public SKPoint P2
	{
		get => _p2;
		set { _p2 = value; _pointsInitialized = true; InvalidateGeometry(); }
	}

	public bool PointsInitialized
	{
		get => _pointsInitialized;
		set => _pointsInitialized = value;
	}

	#endregion

	#region Кэш формы

	// Не readonly: Clone обязан подменить их собственными. MemberwiseClone
	// копирует ссылку, и первый же Dispose у одного экземпляра оставил бы
	// второй с освобождёнными SKPath.
	private List<SKPath> _stepPaths = new();
	private List<StepStyle> _stepStyles = new();
	private SKRect _cachedBounds = SKRect.Empty;
	private int _cachedHash;
	private bool _cacheValid;

	/// <summary>
	/// Оформление одного шага: промежуточные заливка и обводка.
	///
	/// Готовые FillStyle и StrokeStyle, а не пара цветов, как было раньше:
	/// тогда шаг умел только сплошную заливку, и переход между двумя
	/// градиентами по дороге терял градиент. Здесь же краску строит
	/// штатный FillStyle.CreatePaint — со всеми градиентами, паттернами
	/// и текстурами, которые уже умеет рисовать обычная фигура.
	/// </summary>
	private sealed class StepStyle
	{
		public FillStyle Fill = new();
		public StrokeStyle Stroke = new();

		/// <summary>
		/// Ручки заливки в локальных координатах инструмента. В мировые
		/// переводятся при отрисовке: до применения матрицы инструмента
		/// правильных мировых координат у них ещё нет.
		/// </summary>
		public SKPoint FillStartLocal;
		public SKPoint FillEndLocal;
	}

	public TransformShape()
	{
		Name = "Transform";

		// Своих обводки и заливки у инструмента нет: каждый шаг берёт
		// промежуточные между стилями привязанных фигур. Включённые
		// контролы, которые ни на что не влияют, читались бы как
		// неработающая функция — панель их и не показывает.
		Stroke.Enabled = false;
		Fill.Enabled = false;
	}

	/// <summary>
	/// Сбрасывает кэш формы. Публичный, потому что звать его приходится и
	/// снаружи: панель свойств меняет параметры напрямую.
	/// </summary>
	public void InvalidateGeometry()
	{
		_cacheValid = false;
	}

	/// <summary>
	/// Хеш всего, от чего зависит форма шагов.
	///
	/// Сюда входят хеши геометрии привязанных фигур — именно так
	/// инструмент узнаёт, что одну из них подвинули или растянули.
	/// Опрос по хешу, а не подписка на события: событий об изменении
	/// геометрии в модели нет, а заводить их ради одного потребителя —
	/// это ещё и обязанность отписаться, которую легко забыть при
	/// удалении фигуры.
	/// </summary>
	private int ComputeSourceHash()
	{
		var hash = new HashCode();

		hash.Add(_shapeA?.GetGeometryHash() ?? 0);
		hash.Add(_shapeB?.GetGeometryHash() ?? 0);

		// Плюс МИРОВАЯ матрица источников. GetGeometryHash учитывает только
		// собственную LocalTransform фигуры, а форму шагов мы снимаем
		// с BuildWorldPath, куда входят и матрицы родителей. Без этого
		// источник, лежащий в группе, при перемещении самой группы не менял
		// бы хеш — и цепочка осталась бы стоять на прежнем месте.
		AddWorldTransformHash(ref hash, _shapeA);
		AddWorldTransformHash(ref hash, _shapeB);
		hash.Add(_steps);
		hash.Add(_bias);
		hash.Add(_p1.X);
		hash.Add(_p1.Y);
		hash.Add(_p2.X);
		hash.Add(_p2.Y);

		// Стили тоже интерполируются, значит их изменение обязано
		// пересобрать кэш.
		AddStyleHash(ref hash, _shapeA);
		AddStyleHash(ref hash, _shapeB);

		return hash.ToHashCode();
	}

	private static void AddWorldTransformHash(ref HashCode hash, Shape? shape)
	{
		if (shape == null)
			return;

		var m = shape.GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);
	}

	private static void AddStyleHash(ref HashCode hash, Shape? shape)
	{
		if (shape == null)
			return;

		hash.Add(shape.Stroke.Enabled);
		hash.Add(shape.Stroke.Color.GetHashCode());
		hash.Add(shape.Stroke.Thickness);
		hash.Add(shape.Stroke.Pattern);
		hash.Add(shape.Stroke.PatternLength);

		var f = shape.Fill;

		hash.Add(f.Enabled);
		hash.Add((int)f.Kind);
		hash.Add(f.SolidColor.GetHashCode());
		hash.Add(f.GradientColor1.GetHashCode());
		hash.Add(f.GradientColor2.GetHashCode());
		hash.Add(f.GradientMode);
		hash.Add(f.Angle);
		hash.Add(f.PatternFrontColor.GetHashCode());
		hash.Add(f.PatternBackColor.GetHashCode());
		hash.Add(f.PatternWidth);
		hash.Add(f.PatternHeight);
		hash.Add(f.PatternId);
		hash.Add(f.TextureId);
		hash.Add(f.StartPoint.X);
		hash.Add(f.StartPoint.Y);
		hash.Add(f.EndPoint.X);
		hash.Add(f.EndPoint.Y);
		hash.Add(f.Stops.Count);

		foreach (var stop in f.Stops)
		{
			hash.Add(stop.Offset);
			hash.Add(stop.Color.GetHashCode());
		}
	}

	private void EnsureGeometry()
	{
		int hash = ComputeSourceHash();

		if (_cacheValid && hash == _cachedHash)
			return;

		Rebuild();

		_cachedHash = hash;
		_cacheValid = true;
	}

	private void ClearStepPaths()
	{
		foreach (var p in _stepPaths)
			p.Dispose();

		_stepPaths.Clear();
		_stepStyles.Clear();
	}

	private void Rebuild()
	{
		ClearStepPaths();
		_cachedBounds = SKRect.Empty;

		var a = _shapeA;
		var b = _shapeB;

		if (a == null || b == null)
			return;

		using var pathA = a.BuildWorldPath();
		using var pathB = b.BuildWorldPath();

		if (pathA.IsEmpty || pathB.IsEmpty)
			return;

		var contoursA = SamplePath(pathA, SamplesPerContour);
		var contoursB = SamplePath(pathB, SamplesPerContour);

		if (contoursA.Count == 0 || contoursB.Count == 0)
			return;

		PairContours(contoursA, contoursB);

		var boundsA = pathA.Bounds;
		var boundsB = pathB.Bounds;

		// Ручки заливки могут быть ещё не посчитаны: у фигуры, которой
		// ни разу не двигали градиент, StartPoint и EndPoint нулевые.
		// Интерполировать их в таком виде значит стянуть градиент шага
		// в начало координат страницы.
		a.Fill?.EnsureFillTransformDefaults(boundsA);
		b.Fill?.EnsureFillTransformDefaults(boundsB);

		var centreA = new SKPoint(boundsA.MidX, boundsA.MidY);
		var centreB = new SKPoint(boundsB.MidX, boundsB.MidY);

		EnsurePointDefaults(centreA, centreB);

		// Координаты источников берутся как локальные БЕЗ обратного
		// перевода через W-1.
		//
		// Соблазн перевести их выглядит правильным, но Draw применяет
		// к готовому пути мировую матрицу, и W(W-1(x)) = x: матрица
		// инструмента сокращалась и не влияла ни на что. Проявлялось это
		// не сразу — кэш шагов не зависит от LocalTransform, поэтому
		// сдвинутый инструмент оставался сдвинутым, пока не менялись P1,
		// P2 или ползунок. Первое же их изменение запускало пересчёт,
		// сокращение срабатывало, и цепочка прыгала обратно между
		// фигурами — «сброс матрицы», которого на самом деле не было.
		//
		// Считая страничные координаты источников локальными, получаем
		// при единичной матрице цепочку ровно между A и B, а любое
		// преобразование инструмента честно накладывается сверху в Draw.
		bool hasBounds = false;

		for (int i = 1; i <= _steps; i++)
		{
			float u = GetStepParameter(i);

			// Смещение шага: куда его ставят ручки минус то, где он
			// оказался бы сам по себе. При ручках по умолчанию (центры
			// A и B) смещение нулевое, и цепочка идёт ровно между фигурами.
			//
			// Обе точки в одной системе координат — локальной инструмента,
			// куда координаты источников попадают как есть (см. выше).
			var natural = Lerp(centreA, centreB, u);
			var wanted = Lerp(_p1, _p2, u);

			var offset = new SKPoint(wanted.X - natural.X, wanted.Y - natural.Y);

			var path = BuildMorphPath(contoursA, contoursB, u);

			if (path.IsEmpty)
			{
				path.Dispose();
				continue;
			}

			path.Transform(SKMatrix.CreateTranslation(offset.X, offset.Y));

			_stepPaths.Add(path);
			_stepStyles.Add(BlendStyle(a, b, u, offset));

			_cachedBounds = hasBounds ? Geometry.Union(_cachedBounds, path.Bounds) : path.Bounds;
			hasBounds = true;
		}
	}

	private void EnsurePointDefaults(SKPoint centreA, SKPoint centreB)
	{
		if (_pointsInitialized)
			return;

		// Без перевода: центры источников и есть локальные координаты
		// концов цепочки, пока инструмент не преобразован.
		_p1 = centreA;
		_p2 = centreB;
		_pointsInitialized = true;
	}

	/// <summary>
	/// Доля пути для шага i.
	///
	/// Расстояния между соседними шагами образуют геометрическую
	/// прогрессию со знаменателем r. При r = 1 они равны, при r = 2
	/// каждый следующий промежуток вдвое длиннее предыдущего — ровно то
	/// поведение, которое требуется от крайних положений ползунка.
	/// </summary>
	private float GetStepParameter(int i)
	{
		int n = _steps;
		float r = MathF.Pow(2f, (0.5f - _bias) * 2f);

		// r около единицы: формула прогрессии вырождается в 0/0,
		// и деление даёт NaN. Равномерный случай считается напрямую.
		if (MathF.Abs(r - 1f) < 1e-4f)
			return i / (float)(n + 1);

		float num = MathF.Pow(r, i) - 1f;
		float den = MathF.Pow(r, n + 1) - 1f;

		if (MathF.Abs(den) < 1e-6f)
			return i / (float)(n + 1);

		return Math.Clamp(num / den, 0f, 1f);
	}

	private static StepStyle BlendStyle(Shape a, Shape b, float u, SKPoint offset)
	{
		var style = new StepStyle
		{
			Fill = BlendFill(a.Fill, b.Fill, u),
			Stroke = BlendStroke(a.Stroke, b.Stroke, u)
		};

		// Шаг рисуется с заливкой, только если она есть у обеих фигур:
		// иначе к середине цепочки заливка появлялась бы или пропадала
		// скачком, а не переходом.
		style.Fill.Enabled = a.Fill.Enabled && b.Fill.Enabled;
		style.Stroke.Enabled = a.Stroke.Enabled || b.Stroke.Enabled;

		// Ручки заливки идут тем же путём, что и сама форма: интерполяция
		// между источниками плюс смещение шага. Без этого градиент у всех
		// шагов остался бы натянут на исходное место первой фигуры.
		var start = Lerp(a.Fill.StartPoint, b.Fill.StartPoint, u);
		var end = Lerp(a.Fill.EndPoint, b.Fill.EndPoint, u);

		style.FillStartLocal = new SKPoint(start.X + offset.X, start.Y + offset.Y);
		style.FillEndLocal = new SKPoint(end.X + offset.X, end.Y + offset.Y);

		return style;
	}

	/// <summary>
	/// Промежуточная обводка.
	///
	/// Непрерывно меняются только цвет и толщина. Штриховой узор, длина
	/// цикла и глифы концов берутся у ближней фигуры: узор задан строкой,
	/// и «половины» между двумя разными узорами не существует.
	/// </summary>
	private static StrokeStyle BlendStroke(StrokeStyle a, StrokeStyle b, float u)
	{
		var result = (u < 0.5f ? a : b).Clone();

		result.Color = Geometry.LerpColor(a.Color, b.Color, u);
		result.Thickness = a.Thickness + (b.Thickness - a.Thickness) * u;
		result.PatternLength = a.PatternLength + (b.PatternLength - a.PatternLength) * u;

		return result;
	}

	/// <summary>
	/// Промежуточная заливка.
	///
	/// Интерполируется только то, у чего есть непрерывная середина: цвета,
	/// угол, размер плитки, положения точек градиента. Всё остальное —
	/// вид заливки, определение паттерна, растр текстуры — берётся
	/// у ближней фигуры.
	///
	/// Заливки разных видов не смешиваются вовсе: между клеткой и
	/// радиальным градиентом нет промежуточного состояния, и попытка его
	/// изобразить дала бы не переход, а третью, ни на что не похожую
	/// заливку. Вместо этого середина цепочки переключается с одной на
	/// другую — честно и предсказуемо.
	/// </summary>
	private static FillStyle BlendFill(FillStyle a, FillStyle b, float u)
	{
		var nearer = u < 0.5f ? a : b;
		var result = ShallowCopy(nearer);

		if (a.Kind != b.Kind)
			return result;

		result.SolidColor = Geometry.LerpColor(a.SolidColor, b.SolidColor, u);
		result.GradientColor1 = Geometry.LerpColor(a.GradientColor1, b.GradientColor1, u);
		result.GradientColor2 = Geometry.LerpColor(a.GradientColor2, b.GradientColor2, u);
		result.PatternFrontColor = Geometry.LerpColor(a.PatternFrontColor, b.PatternFrontColor, u);
		result.PatternBackColor = Geometry.LerpColor(a.PatternBackColor, b.PatternBackColor, u);

		result.Angle = a.Angle + (b.Angle - a.Angle) * u;

		result.PatternWidth = (int)MathF.Round(a.PatternWidth + (b.PatternWidth - a.PatternWidth) * u);
		result.PatternHeight = (int)MathF.Round(a.PatternHeight + (b.PatternHeight - a.PatternHeight) * u);

		// Точки градиента интерполируются попарно, но только при равном
		// их числе. Сводить пять точек к трём пришлось бы пересэмплированием
		// с потерей ровно тех цветов, ради которых точки и расставляли, —
		// поэтому в таком случае берётся набор ближней фигуры целиком.
		if (a.Stops.Count == b.Stops.Count && a.Stops.Count > 0)
		{
			result.Stops.Clear();

			for (int i = 0; i < a.Stops.Count; i++)
			{
				var sa = a.Stops[i];
				var sb = b.Stops[i];

				result.Stops.Add(new GradientStop(
					sa.Offset + (sb.Offset - sa.Offset) * u,
					Geometry.LerpColor(sa.Color, sb.Color, u)));
			}
		}

		return result;
	}

	/// <summary>
	/// Копия стиля заливки БЕЗ копирования растра текстуры.
	///
	/// FillStyle.Clone выделяет новый SKBitmap и копирует в него пиксели.
	/// Для шагов это недопустимо: копий столько же, сколько шагов, и на
	/// каждый пересчёт цепочки уходили бы десятки мегабайт. Растр здесь
	/// только читают при построении краски, поэтому ссылка разделяется
	/// с исходной фигурой.
	/// </summary>
	private static FillStyle ShallowCopy(FillStyle src)
	{
		var copy = new FillStyle
		{
			Kind = src.Kind,
			Enabled = src.Enabled,
			SolidColor = src.SolidColor,
			Tiling = src.Tiling,
			RotateWithShape = src.RotateWithShape,
			ScaleWithShape = src.ScaleWithShape,
			GradientColor1 = src.GradientColor1,
			GradientColor2 = src.GradientColor2,
			GradientMode = src.GradientMode,
			Angle = src.Angle,
			PatternFrontColor = src.PatternFrontColor,
			PatternBackColor = src.PatternBackColor,
			PatternWidth = src.PatternWidth,
			PatternHeight = src.PatternHeight,
			PatternId = src.PatternId,
			PatternDefinition = src.PatternDefinition,
			TextureId = src.TextureId,
			TextureFilePath = src.TextureFilePath,
			TextureBitmap = src.TextureBitmap,
			StartPoint = src.StartPoint,
			EndPoint = src.EndPoint,
			FillTransformInitialized = true,
			SmoothTexture = src.SmoothTexture
		};

		copy.Stops.Clear();

		foreach (var stop in src.Stops)
			copy.Stops.Add(stop.Clone());

		return copy;
	}

	#endregion

	#region Построение промежуточной формы

	private sealed class Contour
	{
		public List<SKPoint> Points = new();
		public bool Closed;
		public float Length;
		public SKPoint Centroid;
	}

	/// <summary>
	/// Раскладывает путь на контуры, равномерно по длине дуги.
	///
	/// Равномерно именно по длине, а не по узлам: у окружности узлов
	/// четыре, у пятиугольника пять, и поточечная интерполяция между ними
	/// не имела бы смысла. После пересэмплирования у обеих фигур одинаковое
	/// число точек, расставленных сопоставимо.
	/// </summary>
	private static List<Contour> SamplePath(SKPath path, int samples)
	{
		var result = new List<Contour>();

		using var measure = new SKPathMeasure(path, false);

		do
		{
			float length = measure.Length;
			if (length <= MinLength)
				continue;

			var contour = new Contour
			{
				Closed = measure.IsClosed,
				Length = length
			};

			float step = length / samples;
			double sx = 0, sy = 0;

			for (int i = 0; i < samples; i++)
			{
				var p = measure.GetPosition(MathF.Min(i * step, length));
				contour.Points.Add(p);
				sx += p.X;
				sy += p.Y;
			}

			contour.Centroid = new SKPoint((float)(sx / samples), (float)(sy / samples));
			result.Add(contour);
		}
		while (measure.NextContour());

		// Сопоставляем контуры по убыванию длины: у обеих фигур главный
		// контур должен встретиться с главным, а не с первой попавшейся дыркой.
		result.Sort((x, y) => y.Length.CompareTo(x.Length));
		return result;
	}

	/// <summary>
	/// Доводит списки контуров до одинаковой длины и согласует их обход.
	///
	/// Недостающие контуры добавляются вырожденными — схлопнутыми
	/// в центр своей фигуры. Тогда лишняя дырка не исчезает скачком,
	/// а плавно стягивается в точку.
	/// </summary>
	private static void PairContours(List<Contour> a, List<Contour> b)
	{
		var centroidA = a.Count > 0 ? a[0].Centroid : SKPoint.Empty;
		var centroidB = b.Count > 0 ? b[0].Centroid : SKPoint.Empty;

		while (a.Count < b.Count)
			a.Add(Degenerate(centroidA, b[a.Count]));

		while (b.Count < a.Count)
			b.Add(Degenerate(centroidB, a[b.Count]));

		for (int i = 0; i < a.Count; i++)
			Align(a[i], b[i]);
	}

	private static Contour Degenerate(SKPoint centre, Contour like)
	{
		var c = new Contour
		{
			Closed = like.Closed,
			Length = 0f,
			Centroid = centre
		};

		for (int i = 0; i < like.Points.Count; i++)
			c.Points.Add(centre);

		return c;
	}

	/// <summary>
	/// Подбирает начальную точку и направление обхода второго контура так,
	/// чтобы точки сошлись с первым.
	///
	/// Без этого квадрат, переходящий в квадрат, мог бы по дороге
	/// вывернуться: у контуров разные начальные точки и, если фигуры
	/// построены разными инструментами, разное направление обхода.
	/// </summary>
	private static void Align(Contour a, Contour b)
	{
		int n = a.Points.Count;
		if (n == 0 || b.Points.Count != n)
			return;

		int bestShift = 0;
		bool bestReversed = false;
		double bestCost = double.MaxValue;

		var reversed = new List<SKPoint>(b.Points);
		reversed.Reverse();

		for (int pass = 0; pass < 2; pass++)
		{
			var src = pass == 0 ? b.Points : reversed;

			for (int shift = 0; shift < n; shift++)
			{
				double cost = 0;

				for (int i = 0; i < n; i++)
				{
					var pa = a.Points[i];
					var pb = src[(i + shift) % n];

					double dx = pa.X - pb.X;
					double dy = pa.Y - pb.Y;
					cost += dx * dx + dy * dy;

					// Ранний выход: перебор стоит O(n²) на контур, и без
					// отсечения он заметен уже на сотне шагов.
					if (cost >= bestCost)
						break;
				}

				if (cost < bestCost)
				{
					bestCost = cost;
					bestShift = shift;
					bestReversed = pass == 1;
				}
			}
		}

		var source = bestReversed ? reversed : b.Points;
		var aligned = new List<SKPoint>(n);

		for (int i = 0; i < n; i++)
			aligned.Add(source[(i + bestShift) % n]);

		b.Points = aligned;
	}

	private static SKPath BuildMorphPath(List<Contour> a, List<Contour> b, float u)
	{
		var path = new SKPath();

		for (int c = 0; c < a.Count && c < b.Count; c++)
		{
			var ca = a[c];
			var cb = b[c];

			int n = Math.Min(ca.Points.Count, cb.Points.Count);
			if (n < 2)
				continue;

			for (int i = 0; i < n; i++)
			{
				var p = Lerp(ca.Points[i], cb.Points[i], u);

				if (i == 0)
					path.MoveTo(p);
				else
					path.LineTo(p);
			}

			// Замыкаем, только если замкнуты обе: незамкнутая ломаная,
			// которую закрыли, получила бы заливку, которой у исходной
			// фигуры не было.
			if (ca.Closed && cb.Closed)
				path.Close();
		}

		return path;
	}

	private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
		=> new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	#endregion

	#region Геометрия и отрисовка

	public override SKRect GetLocalBounds()
	{
		EnsureGeometry();
		return _cachedBounds;
	}

	public override SKPath BuildLocalPath()
	{
		EnsureGeometry();

		var path = new SKPath();

		foreach (var p in _stepPaths)
			path.AddPath(p);

		return path;
	}

	protected override bool IsClosedGeometry() => false;

	public override int GetGeometryHash()
	{
		var hash = new HashCode();

		hash.Add(ComputeSourceHash());

		var m = LocalTransform;
		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

	public override void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		EnsureGeometry();

		if (_stepPaths.Count == 0)
			return;

		var world = GetWorldTransform();

		// С конца к началу: по условию каждый следующий шаг лежит уровнем
		// ниже предыдущего, а значит первый должен оказаться сверху.
		for (int i = _stepPaths.Count - 1; i >= 0; i--)
		{
			using var worldPath = new SKPath();
			_stepPaths[i].Transform(world, worldPath);

			var style = _stepStyles[i];

			if (style.Fill.Enabled)
			{
				// Ручки переводятся в мировые той же матрицей, что и путь, —
				// иначе градиент шага разъехался бы с его формой при первом
				// же повороте инструмента.
				style.Fill.StartPoint = world.MapPoint(style.FillStartLocal);
				style.Fill.EndPoint = world.MapPoint(style.FillEndLocal);
				style.Fill.FillTransformInitialized = true;

				using var fillPaint = style.Fill.CreatePaint(worldPath.Bounds, world);
				canvas.DrawPath(worldPath, fillPaint);
			}

			if (style.Stroke.Enabled)
			{
				using var strokePaint = style.Stroke.CreatePaint(mmPerWorldUnit);
				canvas.DrawPath(worldPath, strokePaint);
			}
		}
	}

	#endregion

	#region Разворачивание в кривые

	/// <summary>
	/// Превращает цепочку в набор самостоятельных кривых — по одной на шаг.
	///
	/// Не ToCurveShape: тот отдаёт ОДНУ фигуру, а здесь их столько, сколько
	/// шагов, и каждая должна получить свои обводку и заливку — те самые
	/// промежуточные, которые инструмент считал для отрисовки. Сложи мы их
	/// в один контур, все шаги делили бы один стиль, и результат перестал
	/// бы совпадать с тем, что было на экране.
	///
	/// Кривые возвращаются в том же порядке, что и шаги: первый шаг первым.
	/// Расставить слои — забота вызывающего, он знает страницу.
	/// </summary>
	public List<CurveShape> ExpandToCurves()
	{
		EnsureGeometry();

		var result = new List<CurveShape>();

		if (_stepPaths.Count == 0)
			return result;

		var world = GetWorldTransform();

		for (int i = 0; i < _stepPaths.Count && i < _stepStyles.Count; i++)
		{
			var curve = BuildCurveFromPath(_stepPaths[i]);
			if (curve == null)
				continue;

			var style = _stepStyles[i];

			curve.Name = $"{Name} {i + 1}";

			// Узлы записаны в локальных координатах инструмента, поэтому
			// кривая забирает его матрицу и встаёт к тому же родителю —
			// мировое положение шага сохраняется в точности.
			curve.LocalTransform = LocalTransform;
			curve.IsVisible = IsVisible;
			curve.IsLocked = IsLocked;

			// Clone, а не общая ссылка: у самостоятельной фигуры должен быть
			// собственный стиль, иначе правка обводки одного шага меняла бы
			// остальные. Для заливки это ещё и собственный растр текстуры —
			// шаги инструмента делили его с исходной фигурой.
			curve.Stroke = style.Stroke.Clone();
			curve.Fill = style.Fill.Clone();

			// Ручки заливки переводятся в мировые тем же путём, что и при
			// отрисовке: у готовой фигуры они хранятся именно так.
			curve.Fill.StartPoint = world.MapPoint(style.FillStartLocal);
			curve.Fill.EndPoint = world.MapPoint(style.FillEndLocal);
			curve.Fill.FillTransformInitialized = true;

			// Эффекты инструмента достаются каждому шагу: до конвертации
			// они были видны на всей цепочке.
			curve.Shadow = Shadow.Clone();
			curve.Transparency = Transparency.Clone();

			result.Add(curve);
		}

		return result;
	}

	/// <summary>
	/// Собирает кривую по готовому пути шага.
	///
	/// Пути шагов — ломаные (BuildMorphPath ставит только MoveTo, LineTo
	/// и Close), поэтому все узлы прямые и рычаги им не нужны. Число узлов
	/// равно числу точек пересэмплирования: переносится ровно та форма,
	/// что была на экране, без попыток угадать исходные дуги.
	/// </summary>
	private static CurveShape? BuildCurveFromPath(SKPath path)
	{
		var curve = new CurveShape();

		using var iterator = path.CreateIterator(false);
		var points = new SKPoint[4];

		int contourStart = -1;
		SKPathVerb verb;

		while ((verb = iterator.Next(points)) != SKPathVerb.Done)
		{
			switch (verb)
			{
				case SKPathVerb.Move:
					contourStart = curve.Nodes.Count;
					curve.Nodes.Add(MakeNode(points[0], newContour: true));
					break;

				case SKPathVerb.Line:
					curve.Nodes.Add(MakeNode(points[1], newContour: false));
					break;

				case SKPathVerb.Close:
					// Замыкание помечается на последнем узле контура — так же,
					// как это делают ToCurveShape остальных фигур.
					if (contourStart >= 0 && curve.Nodes.Count > contourStart)
						curve.Nodes[curve.Nodes.Count - 1].CloseAfter = true;
					break;
			}
		}

		return curve.Nodes.Count >= 2 ? curve : null;
	}

	private static CurveNode MakeNode(SKPoint position, bool newContour) => new CurveNode
	{
		Position = position,
		Type = CurveNodeType.Straight,
		BreakAfter = false,
		NewContour = newContour,
		CloseAfter = false
	};

	#endregion

	#region Привязки

	/// <summary>
	/// Ссылается ли инструмент на эту фигуру.
	///
	/// Сравнение и по ссылке, и по идентификатору: после замены экземпляра
	/// ссылка ещё указывает на старый объект, а идентификатор уже совпадает
	/// с новым — и оба случая одинаково означают «связан».
	/// </summary>
	public bool References(Shape? shape)
	{
		if (shape == null)
			return false;

		return ReferenceEquals(_shapeA, shape)
			|| ReferenceEquals(_shapeB, shape)
			|| (_shapeAId != Guid.Empty && _shapeAId == shape.Id)
			|| (_shapeBId != Guid.Empty && _shapeBId == shape.Id);
	}

	/// <summary>
	/// Переводит связь со старого экземпляра на новый. Возвращает true,
	/// если что-то поменялось.
	/// </summary>
	public bool Rebind(Shape oldShape, Shape newShape)
	{
		if (oldShape == null || newShape == null)
			return false;

		bool changed = false;

		if (ReferenceEquals(_shapeA, oldShape) || (_shapeAId != Guid.Empty && _shapeAId == oldShape.Id))
		{
			ShapeA = newShape;
			changed = true;
		}

		if (ReferenceEquals(_shapeB, oldShape) || (_shapeBId != Guid.Empty && _shapeBId == oldShape.Id))
		{
			ShapeB = newShape;
			changed = true;
		}

		return changed;
	}

	/// <summary>
	/// Восстанавливает ссылки по идентификаторам. Нужно чтению документа
	/// и любому месту, где ссылки могли устареть.
	/// </summary>
	public void ResolveById(IReadOnlyDictionary<Guid, Shape> byId)
	{
		if (byId == null)
			return;

		if (_shapeAId != Guid.Empty && byId.TryGetValue(_shapeAId, out var a))
			ShapeA = a;

		if (_shapeBId != Guid.Empty && byId.TryGetValue(_shapeBId, out var b))
			ShapeB = b;
	}

	/// <summary>
	/// Годится ли фигура в источники.
	///
	/// Другой инструмент преобразования не годится: его форма считается
	/// из его источников, и взаимная ссылка увела бы пересчёт
	/// в бесконечную рекурсию. Направляющая — разметка, а не рисунок.
	/// </summary>
	public static bool CanBind(Shape? shape)
	{
		return shape != null
			&& shape is not TransformShape
			&& shape is not GuideLine
			&& shape.IsVisible
			&& !shape.IsLocked;
	}

	public override Shape Clone()
	{
		var clone = (TransformShape)base.Clone();

		// Кэш шагов копии — собственный. MemberwiseClone скопировал бы
		// ссылку на те же SKPath, и первый же Dispose у одного экземпляра
		// оставил бы второй с освобождёнными объектами.
		clone._stepPaths = new List<SKPath>();
		clone._stepStyles = new List<StepStyle>();
		clone._cacheValid = false;

		return clone;
	}

	#endregion
}

/// <summary>
/// Ручки инструмента преобразования: начало и конец цепочки плюс ползунок
/// распределения шагов между ними. Устройство то же, что у тени.
/// </summary>
public static class TransformAdorner
{
	private const float HandleRadiusPx = 5f;
	private const float HotHandleRadiusPx = 7f;
	private const float HitRadiusPx = 8f;
	private const float SliderThicknessPx = 6f;
	private const float SliderHalfLengthPx = 11f;

	public static bool IsApplicable(Shape? shape)
	{
		return shape is TransformShape t
			&& t.IsVisible
			&& !t.IsLocked
			&& t.ShapeA != null
			&& t.ShapeB != null;
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext context)
	{
		if (context.Selection.Count != 1)
			return;

		if (context.Selection[0] is not TransformShape tool || !IsApplicable(tool))
			return;

		var p1 = view.PageToScreen(tool.LocalToWorld(tool.P1));
		var p2 = view.PageToScreen(tool.LocalToWorld(tool.P2));

		bool startHot = IsHot(context, tool, HitKind.TransformStartHandle);
		bool endHot = IsHot(context, tool, HitKind.TransformEndHandle);
		bool sliderHot = IsHot(context, tool, HitKind.TransformBiasHandle);

		using var line = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(40, 40, 40, 220)
		};

		canvas.DrawLine(p1, p2, line);

		DrawSlider(canvas, p1, p2, Math.Clamp(tool.Bias, 0f, 1f), sliderHot);

		DrawHandle(canvas, p1, startHot);
		DrawHandle(canvas, p2, endHot);
	}

	private static bool IsHot(EditorInteractionContext context, Shape shape, HitKind kind)
		=> context.HotHit.Kind == kind && ReferenceEquals(context.HotHit.Shape, shape);

	private static void DrawHandle(SKCanvas canvas, SKPoint centre, bool hot)
	{
		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = hot ? new SKColor(255, 180, 60) : new SKColor(255, 120, 190)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = hot ? 2.5f : 1.5f,
			Color = SKColors.Black
		};

		float r = hot ? HotHandleRadiusPx : HandleRadiusPx;

		canvas.DrawCircle(centre, r, fill);
		canvas.DrawCircle(centre, r, stroke);
	}

	private static void DrawSlider(SKCanvas canvas, SKPoint p1, SKPoint p2, float t, bool hot)
	{
		if (!TryGetSliderSegment(p1, p2, t, out var a, out var b))
			return;

		using var outline = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = SliderThicknessPx + 4f,
			StrokeCap = SKStrokeCap.Round,
			Color = SKColors.White
		};

		using var core = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = SliderThicknessPx,
			StrokeCap = SKStrokeCap.Round,
			Color = hot ? new SKColor(255, 180, 60) : SKColors.Black
		};

		canvas.DrawLine(a, b, outline);
		canvas.DrawLine(a, b, core);
	}

	private static bool TryGetSliderSegment(SKPoint p1, SKPoint p2, float t, out SKPoint a, out SKPoint b)
	{
		a = SKPoint.Empty;
		b = SKPoint.Empty;

		float dx = p2.X - p1.X;
		float dy = p2.Y - p1.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 0.5f)
			return false;

		float px = -dy / len;
		float py = dx / len;

		t = Math.Clamp(t, 0f, 1f);

		var centre = new SKPoint(p1.X + dx * t, p1.Y + dy * t);

		a = new SKPoint(centre.X - px * SliderHalfLengthPx, centre.Y - py * SliderHalfLengthPx);
		b = new SKPoint(centre.X + px * SliderHalfLengthPx, centre.Y + py * SliderHalfLengthPx);
		return true;
	}

	public static TransformHandleKind HitTest(MainView view, Shape shape, SKPoint pagePoint)
	{
		if (shape is not TransformShape tool || !IsApplicable(tool))
			return TransformHandleKind.None;

		var p1 = view.PageToScreen(tool.LocalToWorld(tool.P1));
		var p2 = view.PageToScreen(tool.LocalToWorld(tool.P2));
		var mouse = view.PageToScreen(pagePoint);

		if (Geometry.Distance(mouse, p1) <= HitRadiusPx)
			return TransformHandleKind.Start;

		if (Geometry.Distance(mouse, p2) <= HitRadiusPx)
			return TransformHandleKind.End;

		if (TryGetSliderSegment(p1, p2, Math.Clamp(tool.Bias, 0f, 1f), out var a, out var b)
			&& DistanceToSegment(mouse, a, b) <= SliderThicknessPx)
		{
			return TransformHandleKind.Bias;
		}

		return TransformHandleKind.None;
	}

	/// <summary>Доля вдоль P1-P2 для мировой точки, обрезанная концами.</summary>
	public static float ProjectToBias(TransformShape tool, SKPoint pagePoint)
	{
		var p1 = tool.LocalToWorld(tool.P1);
		var p2 = tool.LocalToWorld(tool.P2);

		float dx = p2.X - p1.X;
		float dy = p2.Y - p1.Y;
		float lenSq = dx * dx + dy * dy;

		if (lenSq < 1e-8f)
			return Math.Clamp(tool.Bias, 0f, 1f);

		float t = ((pagePoint.X - p1.X) * dx + (pagePoint.Y - p1.Y) * dy) / lenSq;
		return Math.Clamp(t, 0f, 1f);
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		float lenSq = dx * dx + dy * dy;

		if (lenSq < 1e-6f)
			return Geometry.Distance(p, a);

		float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
		return Geometry.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
	}
}

/// <summary>Перетаскивание ручки начала или конца цепочки шагов.</summary>
public sealed class MoveTransformHandleOperation : DragOperation
{
	private readonly TransformShape _tool;
	private readonly TransformHandleKind _kind;

	private SKPoint _startP1;
	private SKPoint _startP2;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveTransformHandle;

	public MoveTransformHandleOperation(TransformShape tool, TransformHandleKind kind)
	{
		_tool = tool ?? throw new ArgumentNullException(nameof(tool));
		_kind = kind;
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		_startP1 = _tool.P1;
		_startP2 = _tool.P2;
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun)
			return;

		// Ручки живут в локальных координатах инструмента, а мышь приходит
		// в координатах страницы.
		var local = _tool.WorldToLocal(pagePoint);

		switch (_kind)
		{
			case TransformHandleKind.Start:
				_tool.P1 = local;
				break;

			case TransformHandleKind.End:
				_tool.P2 = local;
				break;
		}

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		Update(view, context, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext context)
	{
		if (_begun)
		{
			_tool.P1 = _startP1;
			_tool.P2 = _startP2;
		}

		view.Invalidate();
	}
}

/// <summary>Перетаскивание ползунка распределения шагов.</summary>
public sealed class AdjustTransformBiasOperation : DragOperation
{
	private readonly TransformShape _tool;

	private float _startBias;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.AdjustTransformBias;

	public AdjustTransformBiasOperation(TransformShape tool)
	{
		_tool = tool ?? throw new ArgumentNullException(nameof(tool));
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		_startBias = _tool.Bias;
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun)
			return;

		_tool.Bias = TransformAdorner.ProjectToBias(_tool, pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		// Панель показывает ту же величину ползунком — её надо подтянуть.
		view.mainForm?.RebuildPropertyPanel();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		Update(view, context, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext context)
	{
		if (_begun)
			_tool.Bias = _startBias;

		view.mainForm?.RebuildPropertyPanel();
		view.Invalidate();
	}
}

/// <summary>
/// Изменение параметров инструмента преобразования: положения ручек,
/// ползунка и числа шагов.
/// </summary>
public sealed class TransformToolStateAction : UndoableActionBase
{
	private readonly struct State
	{
		public readonly SKPoint P1;
		public readonly SKPoint P2;
		public readonly float Bias;
		public readonly int Steps;

		public State(TransformShape tool)
		{
			P1 = tool.P1;
			P2 = tool.P2;
			Bias = tool.Bias;
			Steps = tool.Steps;
		}

		public bool SameAs(State other)
		{
			const float eps = 1e-5f;

			return Steps == other.Steps
				&& MathF.Abs(Bias - other.Bias) < eps
				&& MathF.Abs(P1.X - other.P1.X) < eps
				&& MathF.Abs(P1.Y - other.P1.Y) < eps
				&& MathF.Abs(P2.X - other.P2.X) < eps
				&& MathF.Abs(P2.Y - other.P2.Y) < eps;
		}

		public void ApplyTo(TransformShape tool)
		{
			tool.P1 = P1;
			tool.P2 = P2;
			tool.Bias = Bias;
			tool.Steps = Steps;
		}
	}

	private readonly struct Entry
	{
		public readonly TransformShape Tool;
		public readonly State Before;
		public State After { get; init; }

		public Entry(TransformShape tool)
		{
			Tool = tool;
			Before = new State(tool);
			After = Before;
		}
	}

	private readonly List<Entry> _entries;
	private readonly string _name;

	public override string Name => _name;
	public override long MemoryFootprint => 256 + _entries.Count * 64L;

	/// <summary>Вызывать ДО изменения.</summary>
	public static TransformToolStateAction Capture(string name, IEnumerable<Shape> shapes)
		=> new TransformToolStateAction(
			name,
			shapes.OfType<TransformShape>().Select(t => new Entry(t)).ToList());

	private TransformToolStateAction(string name, List<Entry> entries)
	{
		_name = name;
		_entries = entries;
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push в стек.</summary>
	public void CaptureAfter()
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			var e = _entries[i];
			_entries[i] = e with { After = new State(e.Tool) };
		}
	}

	public bool HasChanges()
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			if (!_entries[i].Before.SameAs(_entries[i].After))
				return true;
		}

		return false;
	}

	public override void Undo()
	{
		foreach (var e in _entries)
			e.Before.ApplyTo(e.Tool);
	}

	public override void Redo()
	{
		foreach (var e in _entries)
			e.After.ApplyTo(e.Tool);
	}
}

/// <summary>
/// Смена привязок инструмента преобразования. Хранит и ссылки, и
/// идентификаторы: восстанавливать надо оба, иначе откат вернул бы ссылку
/// на старый экземпляр, оставив ключ от нового.
/// </summary>
public sealed class TransformBindingAction : UndoableActionBase
{
	private readonly TransformShape _tool;

	private readonly Shape? _beforeA;
	private readonly Shape? _beforeB;
	private readonly Guid _beforeAId;
	private readonly Guid _beforeBId;

	private Shape? _afterA;
	private Shape? _afterB;
	private Guid _afterAId;
	private Guid _afterBId;

	/// <summary>Вызывать ДО смены привязки.</summary>
	public TransformBindingAction(TransformShape tool)
	{
		_tool = tool;
		_beforeA = tool.ShapeA;
		_beforeB = tool.ShapeB;
		_beforeAId = tool.ShapeAId;
		_beforeBId = tool.ShapeBId;
	}

	/// <summary>Вызывать ПОСЛЕ смены, перед Push в стек.</summary>
	public void CaptureAfter()
	{
		_afterA = _tool.ShapeA;
		_afterB = _tool.ShapeB;
		_afterAId = _tool.ShapeAId;
		_afterBId = _tool.ShapeBId;
	}

	public bool HasChanges()
		=> !ReferenceEquals(_beforeA, _afterA)
		|| !ReferenceEquals(_beforeB, _afterB)
		|| _beforeAId != _afterAId
		|| _beforeBId != _afterBId;

	public override string Name => "Transform binding";

	public override void Undo() => Apply(_beforeA, _beforeB, _beforeAId, _beforeBId);
	public override void Redo() => Apply(_afterA, _afterB, _afterAId, _afterBId);

	private void Apply(Shape? a, Shape? b, Guid aId, Guid bId)
	{
		// Идентификаторы после ссылок: сеттер ShapeA подтягивает Id
		// за собой, и обратный порядок затёр бы восстановленный ключ.
		_tool.ShapeA = a;
		_tool.ShapeB = b;
		_tool.ShapeAId = aId;
		_tool.ShapeBId = bId;
		_tool.InvalidateGeometry();
	}
}

/// <summary>
/// Замена одной фигуры набором других.
///
/// Своё действие, а не пара AddShapesAction и RemoveShapesAction:
/// разворачивание цепочки должно отменяться единственным нажатием и
/// возвращать инструмент ровно на то место в порядке перекрытия, где он
/// стоял. Работает и с фигурой внутри группы — там своих событий
/// у страницы нет, и запись истории целиком на этом действии.
/// </summary>
public sealed class ReplaceShapeWithManyAction : UndoableActionBase
{
	private readonly Page _page;
	private readonly Shape _old;
	private readonly List<Shape> _new;
	private readonly GroupShape? _parent;
	private readonly int _index;

	/// <summary>Вызывать ДО замены: конструктор запоминает позицию.</summary>
	public static ReplaceShapeWithManyAction Capture(Page page, Shape old, IEnumerable<Shape> replacements)
	{
		var parent = old.Parent as GroupShape;

		int index = parent != null
			? parent.Children.IndexOf(old)
			: page.Shapes.IndexOf(old);

		return new ReplaceShapeWithManyAction(page, old, replacements.ToList(), parent, Math.Max(0, index));
	}

	private ReplaceShapeWithManyAction(Page page, Shape old, List<Shape> replacements, GroupShape? parent, int index)
	{
		_page = page;
		_old = old;
		_new = replacements;
		_parent = parent;
		_index = index;
	}

	public override string Name => _new.Count == 1
		? "Convert shape"
		: $"Convert to {_new.Count} shapes";

	public override long MemoryFootprint
	{
		get
		{
			long total = 256 + UndoMemory.OfShape(_old);

			foreach (var s in _new)
				total += UndoMemory.OfShape(s);

			return total;
		}
	}

	/// <summary>Применяет замену. Ею же действие выполняется в первый раз.</summary>
	public override void Redo()
	{
		Remove(_old);
		Insert(_new, _index);

		_old.IsSelected = false;
	}

	public override void Undo()
	{
		foreach (var s in _new)
		{
			s.IsSelected = false;
			Remove(s);
		}

		Insert(new[] { _old }, _index);
	}

	private void Remove(Shape shape)
	{
		if (_parent != null)
		{
			if (_parent.Children.Remove(shape))
				shape.SetParent(null);

			return;
		}

		// Silent: шаг истории — это само действие, и обычное RemoveShape
		// подняло бы событие, из которого записался бы ВТОРОЙ шаг.
		_page.RemoveShapeSilent(shape);
	}

	private void Insert(IReadOnlyList<Shape> shapes, int index)
	{
		for (int i = 0; i < shapes.Count; i++)
		{
			var shape = shapes[i];

			if (_parent != null)
			{
				shape.SetParent(_parent);
				_parent.Children.Insert(Math.Clamp(index + i, 0, _parent.Children.Count), shape);
			}
			else
			{
				_page.InsertShapeSilent(shape, index + i);
			}
		}

		if (_parent != null)
			_parent.SortChildrenByLayer();
		else
			_page.SortLayers();
	}
}

/// <summary>
/// Сопровождение привязок инструмента преобразования при удалении фигур.
/// </summary>
public static class TransformBindingHelper
{
	/// <summary>
	/// Переводит привязки со старого экземпляра фигуры на новый.
	///
	/// Нужен там же, где TextBindingHelper.Retarget: конвертация в кривую
	/// и булевы операции не правят фигуру на месте, а СТРОЯТ НОВУЮ и
	/// подменяют ею старую на странице. Без переназначения инструмент
	/// остаётся привязан к экземпляру, снятому со страницы: он никогда
	/// больше не меняется, поэтому и пересчёт не запускается — цепочка
	/// молча застывает в том виде, в каком была до конвертации.
	/// </summary>
	public static void Retarget(Page page, Shape oldShape, Shape newShape, UndoStack? undo)
	{
		if (page == null || oldShape == null || newShape == null)
			return;

		// Инструмент сам может лежать внутри группы, поэтому обход глубокий.
		foreach (var shape in EnumerateAll(page.Shapes))
		{
			if (shape is not TransformShape tool)
				continue;

			// Привязка к самому себе не имеет смысла и увела бы пересчёт
			// формы в бесконечную рекурсию.
			if (ReferenceEquals(tool, newShape))
				continue;

			var action = new TransformBindingAction(tool);

			if (!tool.Rebind(oldShape, newShape))
				continue;

			action.CaptureAfter();

			if (action.HasChanges())
				undo?.Push(action);
		}
	}

	/// <summary>Восстанавливает ссылки всех инструментов страницы по идентификаторам.</summary>
	public static void ResolveAll(Page page)
	{
		if (page == null)
			return;

		var byId = new Dictionary<Guid, Shape>();

		foreach (var shape in EnumerateAll(page.Shapes))
			byId[shape.Id] = shape;

		foreach (var shape in EnumerateAll(page.Shapes))
		{
			if (shape is TransformShape tool)
				tool.ResolveById(byId);
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

	/// <summary>
	/// Дополняет список удаляемых инструментами, которые ссылаются на них.
	///
	/// Инструмент без одной из двух фигур бессмыслен: показывать ему
	/// нечего. Вместо того чтобы гасить его и оставлять на странице
	/// невидимым мусором, он удаляется вместе с источником.
	///
	/// Вызывать ДО фактического удаления и внутри той же группы истории,
	/// что и само удаление, — тогда отмена вернёт и фигуру, и инструмент
	/// одним нажатием. Ровно тот же порядок, что у
	/// TextBindingHelper.DetachFromDeleted.
	/// </summary>
	public static void AppendDependentTools(Page page, List<Shape> doomed)
	{
		if (page == null || doomed == null || doomed.Count == 0)
			return;

		// Удаление группы уносит с собой детей, а ссылаться могли и на них.
		var removed = new HashSet<Shape>(ReferenceEqualityComparer.Instance);

		foreach (var shape in doomed)
			CollectWithChildren(shape, removed);

		var extra = new List<Shape>();

		foreach (var shape in page.Shapes)
		{
			if (shape is not TransformShape tool)
				continue;

			if (removed.Contains(tool))
				continue;

			// Через References, а не сравнением ссылок: связь ведёт
			// идентификатор, и после подмены экземпляра ссылка может ещё
			// указывать на старый объект, а ключ — уже на удаляемый новый.
			bool loses = false;

			foreach (var doomedShape in removed)
			{
				if (tool.References(doomedShape))
				{
					loses = true;
					break;
				}
			}

			if (loses)
				extra.Add(tool);
		}

		foreach (var tool in extra)
		{
			if (!doomed.Contains(tool))
				doomed.Add(tool);
		}
	}

	/// <summary>
	/// Фигура вместе со всем содержимым. Именно через GroupShape.Children,
	/// а не через Shape.GetChildren: группа его не переопределяет, и base
	/// вернул бы пустую последовательность — дети группы не попали бы
	/// в набор, а ссылавшийся на них инструмент пережил бы своё содержимое.
	/// </summary>
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
}