using SkiaSharp;
using System.Reflection;
using System.Windows.Forms.VisualStyles;
using System.Xml.Linq;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

public abstract class Shape
{
	#region Свойства

	/// <summary>
	/// Идентификатор фигуры.
	///
	/// Сеттер внутренний и нужен только загрузке документа: привязки концов
	/// и ссылки текста хранятся в файле идентификаторами, и если выдать
	/// загруженной фигуре новый, ссылка на неё никуда не приведёт.
	/// </summary>
	public Guid Id { get; internal set; } = Guid.NewGuid();
	public string Name { get; set; } = "Shape";
	/// <summary>Слой ещё не выдан: фигура не попадала на страницу.</summary>
	public const int UnassignedLayer = int.MinValue;

	/// <summary>Слой предпросмотра: всегда поверх остального.</summary>
	public const int PreviewLayer = int.MaxValue;

	/// <summary>
	/// Порядковый номер слоя. Выдаётся при добавлении на страницу,
	/// а не в конструкторе.
	///
	/// Раньше каждый созданный экземпляр сразу забирал номер из общего
	/// счётчика. Инструменты, пересобирающие предпросмотр на каждое движение
	/// мыши, тратили сотни номеров за одну постройку, и готовая фигура
	/// получала номер, оторванный от соседей по странице.
	/// </summary>
	public int Layer { get; set; } = UnassignedLayer;

	public StrokeStyle Stroke { get; set; } = new();
	public FillStyle Fill { get; set; } = new();

	/// <summary>
	/// Тень или свечение фигуры. Объект есть всегда, выключенный по
	/// умолчанию: панель свойств и инструменты правят его на месте, и
	/// проверять на null в каждой точке пришлось бы вместо одного флага.
	/// </summary>
	public ShadowEffect Shadow { get; set; } = new();

	/// <summary>
	/// Плавная прозрачность фигуры. Как и тень, объект есть всегда и по
	/// умолчанию выключен.
	/// </summary>
	public TransparencyEffect Transparency { get; set; } = new();

	public Shape? otherShape = null;

	public bool IsSelected { get; set; }
	public bool IsFocused { get; set; }
	public bool IsLocked { get; set; }
	public bool IsVisible { get; set; } = true;

	public Shape? Parent { get; private set; }
	/// <summary>
	/// Собственное преобразование фигуры.
	///
	/// Сеттер не допускает ВЫРОЖДЕННОЙ матрицы — такой, у которой нулевой
	/// определитель. Обратной у неё не существует, а на обращении держится
	/// половина редактора: WorldToLocal при неудаче возвращает точку
	/// как есть, и попадание курсора начинает проверяться в мировых
	/// координатах против локального пути. Фигура при этом видна на своём
	/// месте, а ловится совсем в другом — обычно у начала координат
	/// страницы. Так же ломаются правка узлов, прилипание и привязки.
	///
	/// Нулевой масштаб заменяется на исчезающе малый: фигура выглядит
	/// схлопнутой ровно так же, но матрица остаётся обратимой, и операция
	/// перестаёт быть необратимой.
	/// </summary>
	public SKMatrix LocalTransform
	{
		get => _localTransform;
		set => _localTransform = Sanitize(value);
	}

	private SKMatrix _localTransform = SKMatrix.CreateIdentity();

	/// <summary>Наименьший допустимый масштаб по оси.</summary>
	private const float MinAxisScale = 0.0001f;

	private static SKMatrix Sanitize(SKMatrix m)
	{
		float det = m.ScaleX * m.ScaleY - m.SkewX * m.SkewY;

		if (MathF.Abs(det) > 1e-12f)
			return m;

		// Определитель нулевой: одна из осей схлопнута. Раздвигаем ту,
		// что вырождена, оставив вторую нетронутой.
		float lenX = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
		float lenY = MathF.Sqrt(m.SkewX * m.SkewX + m.ScaleY * m.ScaleY);

		if (lenX < MinAxisScale)
		{
			m.ScaleX = m.ScaleX >= 0f ? MinAxisScale : -MinAxisScale;
			m.SkewY = 0f;
		}

		if (lenY < MinAxisScale)
		{
			m.ScaleY = m.ScaleY >= 0f ? MinAxisScale : -MinAxisScale;
			m.SkewX = 0f;
		}

		// Оси не схлопнуты по отдельности, но коллинеарны: чистого способа
		// развести их нет, поэтому слегка отклоняем вторую.
		det = m.ScaleX * m.ScaleY - m.SkewX * m.SkewY;

		if (MathF.Abs(det) <= 1e-12f)
			m.ScaleY += MinAxisScale;

		return m;
	}

	public readonly float pointTolerance = 0.01f;

	public bool GeometryInitialized
	{
		get; private set;
	}
	#endregion

	#region Состояние геометрии

	/// <summary>
	/// Выдаёт слой, если его ещё нет. Повторные вызовы безвредны:
	/// фигура, возвращённая отменой удаления, обязана сохранить своё место
	/// в порядке перекрытия.
	/// </summary>
	public void EnsureLayerAssigned()
	{
		if (Layer == UnassignedLayer || Layer == PreviewLayer)
			Layer = Settings.layer_start++;
	}

	public void MarkGeometryInitialized()
	{
		GeometryInitialized = true;
	}

	/// <summary>
	/// Хеш собственной геометрии фигуры, без учёта трансформации родителей.
	///
	/// Нужен потребителям, которые кэшируют результат, зависящий от формы
	/// фигуры: текст внутри области пересчитывает раскладку только при
	/// изменении этого значения.
	///
	/// Реализация по умолчанию покрывает фигуры, форма которых однозначно
	/// задаётся габаритами и трансформацией. Наследники со своей геометрией
	/// обязаны переопределить метод — см. CurveShape.
	/// </summary>
	public virtual int GetGeometryHash()
	{
		var b = GetLocalBounds();
		var m = LocalTransform;

		var hash = new HashCode();

		hash.Add(b.Left);
		hash.Add(b.Top);
		hash.Add(b.Right);
		hash.Add(b.Bottom);

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

	#endregion

	#region Иерархия и трансформации

	public void SetParent(Shape? parent) => Parent = parent;

	public SKMatrix GetWorldTransform()
	{
		if (Parent == null)
			return LocalTransform;

		return Geometry.Multiply(Parent.GetWorldTransform(), LocalTransform);
	}

	public bool TryGetInverseWorldTransform(out SKMatrix inverse)
	{
		return GetWorldTransform().TryInvert(out inverse);
	}

	public SKPoint LocalToWorld(SKPoint p) => GetWorldTransform().MapPoint(p);

	public SKPoint WorldToLocal(SKPoint p)
	{
		if (!TryGetInverseWorldTransform(out var inv))
			return p;

		return inv.MapPoint(p);
	}

	public void ReparentPreserveWorld(Shape? newParent)
	{
		var world = GetWorldTransform();

		Parent = newParent;

		var parentWorld = Parent?.GetWorldTransform() ?? SKMatrix.CreateIdentity();
		if (!parentWorld.TryInvert(out var inv))
			inv = SKMatrix.CreateIdentity();

		LocalTransform = Geometry.Multiply(inv, world);
	}

	#endregion

	#region Геометрия и попадание

	public abstract SKRect GetLocalBounds();
	public abstract SKPath BuildLocalPath();
	protected virtual bool IsClosedGeometry() => true;

	public virtual SKPath BuildWorldPath()
	{
		using var localPath = BuildLocalPath();
		var worldPath = new SKPath();
		localPath.Transform(GetWorldTransform(), worldPath);
		worldPath.FillType = localPath.FillType;
		return worldPath;
	}

	/// <summary>
	/// Мировые габариты фигуры.
	///
	/// SKPath.Bounds — это коробка по КОНТРОЛЬНЫМ ТОЧКАМ, а не по самой
	/// кривой. Для отрезков и прямоугольников это одно и то же, а для
	/// дуг — нет, и овалы её переопределяют.
	/// </summary>
	public virtual SKRect GetBounds()
	{
		using var worldPath = BuildWorldPath();
		return worldPath.Bounds;
	}

	/// <summary>
	/// Точные мировые габариты овала, вписанного в заданный локальный
	/// прямоугольник.
	///
	/// AddOval и AddCircle строят овал из четырёх коник, чьи контрольные
	/// точки стоят в УГЛАХ описанного прямоугольника. Пока овал не
	/// повёрнут, коробка по ним совпадает с коробкой по кривой. После
	/// поворота углы разъезжаются, и осевая коробка по ним заметно больше
	/// самого эллипса — у круга, повёрнутого на 45 градусов, в корень из двух.
	///
	/// Здесь габариты берутся аналитически. Точка эллипса — это
	///     x(t) = cx + rx·cos t,   y(t) = cy + ry·sin t,
	/// после преобразования по каждой оси получается сумма синуса
	/// и косинуса, амплитуда которой равна корню из суммы квадратов
	/// коэффициентов. Работает при любом повороте, масштабе и сдвиге.
	/// </summary>
	protected SKRect GetOvalWorldBounds(SKRect localOvalRect)
	{
		float rx = localOvalRect.Width * 0.5f;
		float ry = localOvalRect.Height * 0.5f;

		if (rx <= 0.0001f || ry <= 0.0001f)
			return SKRect.Empty;

		var m = GetWorldTransform();
		var center = m.MapPoint(new SKPoint(localOvalRect.MidX, localOvalRect.MidY));

		float hx = MathF.Sqrt(m.ScaleX * m.ScaleX * rx * rx + m.SkewX * m.SkewX * ry * ry);
		float hy = MathF.Sqrt(m.SkewY * m.SkewY * rx * rx + m.ScaleY * m.ScaleY * ry * ry);

		return new SKRect(center.X - hx, center.Y - hy, center.X + hx, center.Y + hy);
	}

	/// <summary>
	/// Попадание курсора по фигуре.
	///
	/// ОБА параметра — в единицах страницы, а не в пикселях и миллиметрах:
	/// геометрия фигуры задана именно в них.
	///   toleranceUom — запас вокруг контура;
	///   mmPerUom     — миллиметров в одной единице, нужно CreatePaint,
	///                  чтобы перевести толщину обводки из мм.
	///
	/// Прежде сюда приходило px/мм. На миллиметровой странице это
	/// случайно совпадало с верным значением, и ошибка не проявлялась;
	/// на дюймах зона захвата раздувалась в разы и попадание смещалось.
	/// </summary>
	/// <summary>
	/// Попадание курсора по фигуре.
	///
	/// Проверка идёт В МИРОВЫХ координатах. Прежде точка переводилась
	/// в локальные и сравнивалась с локальным путём, а толщина и допуск
	/// подставлялись мировые — то есть величины из разных систем. Пока
	/// в LocalTransform лежал один перенос, разницы не было; стоило
	/// появиться масштабу, и допуск переставал соответствовать
	/// действительности. У линии, сжатой по высоте в тысячу раз, зона
	/// захвата по вертикали сжималась во столько же, и попасть по ней
	/// становилось невозможно: щелчок уходил мимо, а раз ничего не задето —
	/// начиналась рамка выделения, и фигура теряла выделение.
	///
	/// Штриховка при проверке снимается: у пунктирной линии промежутки
	/// между штрихами не должны быть дырами в зоне захвата.
	/// </summary>
	public virtual bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		using var path = BuildWorldPath();

		if (path == null || path.IsEmpty)
			return false;

		using var strokePaint = Stroke.CreatePaint(mmPerUom);
		strokePaint.StrokeWidth = MathF.Max(strokePaint.StrokeWidth, toleranceUom);
		strokePaint.PathEffect = null;

		if (Fill.Enabled && IsClosedGeometry() && path.Contains(pagePoint.X, pagePoint.Y))
			return true;

		return StrokeHit(path, strokePaint, pagePoint);
	}

	#endregion

	#region Отрисовка

	/// <summary>
	/// Пока true, Render рисует только саму фигуру, без эффектов.
	///
	/// Ставится на время отрисовки тени. Без него тень группы получалась бы
	/// суммой теней её детей: GroupShape.Draw зовёт Render каждого ребёнка,
	/// и внутри слоя тени каждый нарисовал бы ещё и свою.
	///
	/// Статика допустима: вся отрисовка идёт в потоке интерфейса, и вложенных
	/// друг в друга проходов тени не бывает — флаг снимается до выхода.
	/// </summary>
	private static bool _suppressEffects;

	/// <summary>
	/// Единая точка отрисовки фигуры со всеми эффектами.
	///
	/// Обёртка вокруг Draw, а не правка самого Draw: он переопределён
	/// группой, кривой, текстом, изображением и размером, и каждому из них
	/// пришлось бы повторять одну и ту же машинерию слоёв. Вызывающие
	/// (MainView, PageRenderer, ShapeExporter и GroupShape) обращаются
	/// сюда, а Draw остаётся тем, чем был, — отрисовкой собственной
	/// геометрии.
	/// </summary>
	public void Render(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		if (_suppressEffects)
		{
			Draw(canvas, mmPerWorldUnit);
			return;
		}

		bool masked = Transparency != null && Transparency.Enabled;

		// Слой открывается ДО тени: прозрачность гасит объект целиком,
		// вместе с его тенью. Иначе у полностью прозрачного края фигуры
		// оставалась бы висеть непрозрачная тень.
		if (masked)
			canvas.SaveLayer(null);

		try
		{
			DrawShadow(canvas, mmPerWorldUnit);
			Draw(canvas, mmPerWorldUnit);

			if (masked)
				ApplyTransparencyMask(canvas);
		}
		finally
		{
			if (masked)
				canvas.Restore();
		}
	}

	/// <summary>
	/// Умножает альфу уже нарисованного слоя на градиент прозрачности.
	///
	/// DstIn, а не рисование фигуры полупрозрачной краской: краска
	/// применяется к каждому примитиву отдельно, и в местах, где обводка
	/// ложится на заливку, полупрозрачность сложилась бы дважды — шов был
	/// бы виден. Маска же действует на готовое изображение слоя разом,
	/// поэтому одинаково верна и для группы из десятка фигур.
	/// </summary>
	private void ApplyTransparencyMask(SKCanvas canvas)
	{
		var bounds = GetRenderBounds();
		if (bounds.IsEmpty)
			return;

		using var shader = Transparency.CreateMaskShader(GetBounds());
		if (shader == null)
			return;

		using var maskPaint = new SKPaint
		{
			IsAntialias = false,
			Shader = shader,
			BlendMode = SKBlendMode.DstIn
		};

		// Прямоугольник обязан перекрыть весь слой: непокрытая часть
		// осталась бы с исходной альфой, то есть непрозрачной. Запас
		// берётся от габаритов с эффектами, а градиент за пределами
		// отрезка продлевается постоянным (Clamp).
		float pad = MathF.Max(bounds.Width, bounds.Height) + 1f;
		canvas.DrawRect(Geometry.Inflate(bounds, pad, pad), maskPaint);
	}

	/// <summary>
	/// Габариты фигуры вместе с эффектами.
	///
	/// Отсечение по видимой области считает именно по ним. По одним
	/// габаритам фигуры тень пропадала бы, едва фигура уходила за край
	/// экрана, — хотя сама тень оставалась бы видна.
	/// </summary>
	public virtual SKRect GetRenderBounds()
	{
		var bounds = GetBounds();

		if (Shadow == null || !Shadow.Enabled || bounds.IsEmpty)
			return bounds;

		return Geometry.Union(bounds, Shadow.GetShadowBounds(bounds));
	}

	/// <summary>
	/// Тень как силуэт фигуры.
	///
	/// Рисуется не по BuildWorldPath, а слоем с фильтром SrcIn: контур
	/// годится только для простых фигур, а у группы BuildLocalPath отдаёт
	/// габаритный прямоугольник, у текста — вообще ничего похожего на глифы.
	/// Слой же перекрашивает в цвет тени ровно то, что фигура нарисовала,
	/// какой бы она ни была, — включая картинки с их альфа-каналом.
	/// </summary>
	protected void DrawShadow(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (Shadow == null || !Shadow.Enabled)
			return;

		var bounds = GetBounds();
		if (bounds.IsEmpty)
			return;

		Shadow.EnsureDefaults(bounds);

		float k = Shadow.GetScale(bounds);
		var centre = new SKPoint(bounds.MidX, bounds.MidY);
		var p1 = Shadow.GetCenterPoint(bounds);
		float sigma = Shadow.GetBlurSigma(bounds);

		// SrcIn оставляет от нарисованного только альфу, заливая её цветом
		// тени. Мягкость уходит в размытие того же слоя: при нулевой
		// фильтра нет вовсе, и тень точно повторяет контуры — как и требуется.
		using var layerPaint = new SKPaint
		{
			IsAntialias = true,
			ColorFilter = SKColorFilter.CreateBlendMode(Shadow.Color, SKBlendMode.SrcIn)
		};

		if (sigma > 0.0001f)
			layerPaint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);

		// Выделение утолщает обводку. У тени это дало бы силуэт толще
		// фигуры — и, что заметнее, он менялся бы от одного клика по фигуре.
		bool wasSelected = IsSelected;
		bool wasFocused = IsFocused;

		IsSelected = false;
		IsFocused = false;
		_suppressEffects = true;

		try
		{
			canvas.SaveLayer(layerPaint);
			canvas.Save();

			// Масштабирование вокруг P1: центр тени стоит там, куда его
			// поставила первая ручка, а размер задаёт вторая.
			canvas.Translate(p1.X, p1.Y);
			canvas.Scale(k, k);
			canvas.Translate(-centre.X, -centre.Y);

			Draw(canvas, mmPerWorldUnit);

			canvas.Restore();
			canvas.Restore();
		}
		finally
		{
			// finally обязателен: исключение внутри Draw иначе оставило бы
			// флаг взведённым и погасило эффекты до конца сеанса.
			_suppressEffects = false;
			IsSelected = wasSelected;
			IsFocused = wasFocused;
		}
	}

	public virtual void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		using var worldPath = BuildWorldPath();
		bool canFill = Fill.Enabled && IsClosedGeometry();

		if (canFill)
		{
			using var fillPaint = Fill.CreatePaint(worldPath.Bounds, GetWorldTransform());
			canvas.DrawPath(worldPath, fillPaint);
		}

		if (Stroke.Enabled)
		{
			using var strokePaint = Stroke.CreatePaint(mmPerWorldUnit, IsSelected);

			// Обводка может рисоваться по укороченному пути, а заливка и
			// попадание — по полному: стрелка на конце требует, чтобы линия
			// не доходила до узла. Пока фигура такого не просит, метод
			// возвращает null и рисуется исходный путь.
			using var trimmed = BuildStrokeWorldPath(worldPath, mmPerWorldUnit);

			canvas.DrawPath(trimmed ?? worldPath, strokePaint);
			DrawStrokeGlyphs(canvas, mmPerWorldUnit, worldPath, strokePaint);
		}
	}

	protected virtual void DrawStrokeGlyphs(SKCanvas canvas, float mmPerWorldUnit, SKPath worldPath, SKPaint strokePaint)
	{
		// базовая фигура глифы не рисует
	}

	#endregion

	#region Изменение фигуры

	public virtual void OnResizeApplied(SKMatrix oldWorld, SKMatrix newWorld)
	{
	}

	/// <summary>
	/// Снимок собственной геометрии для истории отмены.
	///
	/// null означает, что вся геометрия фигуры описывается матрицей
	/// и габаритами, и отдельно хранить нечего. Наследники, у которых есть
	/// свои поля формы, обязаны переопределить оба метода — иначе откат
	/// перетаскивания их ручек вернёт только матрицу.
	/// </summary>
	public virtual IGeometrySnapshot? CaptureGeometrySnapshot() => null;

	/// <summary>Восстанавливает геометрию из снимка. null игнорируется.</summary>
	public virtual void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
	}

	public virtual void MoveByLocal(float dx, float dy)
	{
		LocalTransform = Geometry.Multiply(LocalTransform, SKMatrix.CreateTranslation(dx, dy));
	}

	public void ApplyWorldTransform(SKMatrix selectionWorldTransform)
	{
		var oldWorld = GetWorldTransform();
		var newWorld = Geometry.Multiply(selectionWorldTransform, oldWorld);

		SKMatrix parentWorld = Parent?.GetWorldTransform() ?? SKMatrix.CreateIdentity();
		if (!parentWorld.TryInvert(out var parentInv))
			parentInv = SKMatrix.CreateIdentity();

		LocalTransform = Geometry.Multiply(parentInv, newWorld);
	}

	public virtual void MoveByWorld(float dx, float dy)
	{
		ApplyWorldTransform(SKMatrix.CreateTranslation(dx, dy));

		if (Fill != null && Fill.Enabled && Fill.SupportsInteractiveHandles)
		{
			Fill.StartPoint = new SKPoint(Fill.StartPoint.X + dx, Fill.StartPoint.Y + dy);
			Fill.EndPoint = new SKPoint(Fill.EndPoint.X + dx, Fill.EndPoint.Y + dy);
			Fill.FillTransformInitialized = true;
		}
	}

	#endregion

	#region Вспомогательные методы

	protected static bool StrokeHit(SKPath path, SKPaint strokePaint, SKPoint point)
	{
		using var fillPath = new SKPath();
		bool ok = strokePaint.GetFillPath(path, fillPath);
		if (!ok)
			return false;

		return fillPath.Contains(point.X, point.Y);
	}

	protected static SKRect RectFromPoints(IEnumerable<SKPoint> pts)
	{
		var list = pts.ToList();
		if (list.Count == 0)
			return SKRect.Empty;

		float left = list.Min(p => p.X);
		float top = list.Min(p => p.Y);
		float right = list.Max(p => p.X);
		float bottom = list.Max(p => p.Y);

		return new SKRect(left, top, right, bottom);
	}

	public virtual IEnumerable<Shape> GetChildren()
	{
		return Enumerable.Empty<Shape>();
	}

	public virtual CurveShape? ToCurveShape() => null;

	protected static CurveShape PrepareCurveCloneFromShape(Shape source, CurveShape curve)
	{
		curve.Name = source.Name;
		curve.Layer = source.Layer;
		curve.Stroke = source.Stroke.Clone();
		curve.Fill = source.Fill.Clone();
		curve.IsVisible = source.IsVisible;
		curve.IsLocked = source.IsLocked;
		curve.IsFocused = source.IsFocused;
		curve.LocalTransform = source.LocalTransform;
		return curve;
	}

	public static CurveShape BuildEllipseCurve(SKPoint center, float rx, float ry)
	{
		rx = Math.Max(0.0001f, rx);
		ry = Math.Max(0.0001f, ry);

		const float k = 0.5522847498307936f;

		var curve = new CurveShape();

		var n0 = new CurveNode
		{
			Position = new SKPoint(center.X, center.Y - ry),
			Type = CurveNodeType.Tangent,
			ControlPrev = new SKPoint(center.X - rx * k, center.Y - ry),
			ControlNext = new SKPoint(center.X + rx * k, center.Y - ry),
			BreakAfter = false,
			NewContour = true,
			CloseAfter = false
		};

		var n1 = new CurveNode
		{
			Position = new SKPoint(center.X + rx, center.Y),
			Type = CurveNodeType.Tangent,
			ControlPrev = new SKPoint(center.X + rx, center.Y - ry * k),
			ControlNext = new SKPoint(center.X + rx, center.Y + ry * k),
			BreakAfter = false,
			NewContour = false,
			CloseAfter = false
		};

		var n2 = new CurveNode
		{
			Position = new SKPoint(center.X, center.Y + ry),
			Type = CurveNodeType.Tangent,
			ControlPrev = new SKPoint(center.X + rx * k, center.Y + ry),
			ControlNext = new SKPoint(center.X - rx * k, center.Y + ry),
			BreakAfter = false,
			NewContour = false,
			CloseAfter = false
		};

		var n3 = new CurveNode
		{
			Position = new SKPoint(center.X - rx, center.Y),
			Type = CurveNodeType.Tangent,
			ControlPrev = new SKPoint(center.X - rx, center.Y + ry * k),
			ControlNext = new SKPoint(center.X - rx, center.Y - ry * k),
			BreakAfter = false,
			NewContour = false,
			CloseAfter = true
		};

		curve.Nodes.Add(n0);
		curve.Nodes.Add(n1);
		curve.Nodes.Add(n2);
		curve.Nodes.Add(n3);

		return curve;
	}

	/// <summary>
	/// Пересчёт геометрии при смене единиц страницы.
	///
	/// Принимает миллиметры на единицу, а не сами UoM: у пикселя
	/// коэффициент зависит от DPI страницы, о котором фигура не знает.
	/// Считает его Page и передаёт готовым числом.
	/// </summary>
	public virtual void SetShapeNewUoM(float oldMmPerUom, float newMmPerUom)
	{
		float ratio = oldMmPerUom / MathF.Max(newMmPerUom, 0.0001f);
		LocalTransform = SKMatrix.Concat(SKMatrix.CreateScale(ratio, ratio), LocalTransform);

		// Обводка задана в единицах страницы и обязана меняться вместе
		// с координатами: иначе у фигуры, «выросшей» при переходе на дюймы,
		// линия останется прежней и превратится в волосок.
		Stroke?.SetStrokeNewUoM(ratio);

		// Концы градиента — тоже мировые координаты, и к матрице фигуры
		// они не привязаны. Оставшись на прежних местах, они уходят далеко
		// за габариты выросшей фигуры, и вся она заливается крайним цветом:
		// градиент на вид становится сплошной заливкой.
		Fill?.SetFillNewUoM(ratio);
	}

	public virtual Shape Clone()
	{
		var clone = (Shape)MemberwiseClone();

		// Собственный идентификатор, а не копия чужого.
		//
		// MemberwiseClone копирует Id вместе с остальными полями, и копия
		// выходила неотличимой от оригинала по ключу. Пока обе живут в
		// памяти, это незаметно — ссылки между фигурами держат объекты.
		// Ломается всё при сохранении: в файл уходят ключи, а при загрузке
		// таблица «ключ -> фигура» оставляет из одинаковых только одну,
		// и ВСЕ привязки, записанные на этот ключ, сходятся на ней.
		// Пользователь видит, что размеры, проставленные на разных копиях
		// детали, после открытия файла собрались на одной.
		clone.Id = Guid.NewGuid();

		clone.SetParent(null);
		clone.IsSelected = false;
		clone.IsFocused = false;
		clone.Stroke = Stroke.Clone();
		clone.Fill = Fill.Clone();

		// MemberwiseClone копирует ссылку: без этого копия и оригинал делили
		// бы одну тень, и перетаскивание её ручки у одной фигуры двигало бы
		// тень у второй.
		clone.Shadow = Shadow.Clone();
		clone.Transparency = Transparency.Clone();

		return clone;
	}

	/// <summary>
	/// Путь для обводки, если он должен отличаться от пути фигуры.
	///
	/// null означает «рисовать как есть». Переопределяют те фигуры, у
	/// которых линия обязана заканчиваться не в узле, — сейчас это кривая
	/// со стрелочным глифом на конце.
	/// </summary>
	protected virtual SKPath? BuildStrokeWorldPath(SKPath worldPath, float mmPerWorldUnit) => null;

	/// <summary>
	/// Рисует глиф конца линии.
	///
	/// Само построение вынесено в <see cref="LineGlyphLibrary"/>: там же
	/// живёт список доступных вариантов и построение их превью для панели
	/// свойств. Держать геометрию в двух местах нельзя — набор глифов на
	/// холсте и в выпадающем списке обязан совпадать.
	/// </summary>
	protected static void DrawLineGlyph(
		SKCanvas canvas,
		SKPoint worldPosition,
		SKPoint worldDirection,
		float glyphSizeWorld,
		string glyphName,
		SKPaint baseStrokePaint)
	{
		LineGlyphLibrary.Draw(
			canvas,
			worldPosition,
			worldDirection,
			glyphSizeWorld,
			glyphName,
			baseStrokePaint);
	}

	#endregion
}

[Serializable]
public sealed class GroupShape : Shape
{
	/// <summary>
	/// Содержимое группы.
	///
	/// Сеттер закрытый и нужен только Clone: список надо подменить целиком
	/// на новый, а очистить существующий нельзя — у копии и оригинала после
	/// MemberwiseClone он один и тот же.
	/// </summary>
	public List<Shape> Children { get; private set; } = new();

	public GroupShape()
	{
		Name = "Group";
	}

	/// <summary>
	/// Копия группы вместе с содержимым.
	///
	/// Без этого переопределения работал Shape.Clone, а он делает
	/// MemberwiseClone — то есть копирует ССЫЛКУ на список детей. Копия
	/// и оригинал получали один список на двоих: добавление в одну группу
	/// меняло вторую, а у каждого ребёнка Parent указывал только на одну
	/// из них. Разгруппировка одной копии опустошала обе.
	///
	/// Дети клонируются рекурсивно и получают новые идентификаторы —
	/// это делает Shape.Clone. Матрицы не пересчитываются: SetParent,
	/// а не AddChild, потому что LocalTransform ребёнка уже задана
	/// относительно родителя и менять её при копировании не нужно.
	/// </summary>
	public override Shape Clone()
	{
		var clone = (GroupShape)base.Clone();

		clone.Children = new List<Shape>(Children.Count);

		foreach (var child in Children)
		{
			var childClone = child.Clone();

			childClone.SetParent(clone);
			clone.Children.Add(childClone);
		}

		return clone;
	}

	public void AddChild(Shape child)
	{
		if (child == null)
			return;

		if (ReferenceEquals(child, this))
			return;

		if (Children.Contains(child))
			return;

		child.ReparentPreserveWorld(this);
		Children.Add(child);
		SortChildrenByLayer();
	}

	public bool RemoveChild(Shape child)
	{
		if (child == null)
			return false;

		return Children.Remove(child);
	}

	public bool RemoveChildTo(Shape child, Shape? newParent)
	{
		if (child == null)
			return false;

		if (!Children.Remove(child))
			return false;

		child.ReparentPreserveWorld(newParent);
		return true;
	}

	public void AddChildren(IEnumerable<Shape> children)
	{
		foreach (var child in children)
			AddChild(child);
	}

	public void ClearChildren()
	{
		foreach (var child in Children)
			child.SetParent(null);

		Children.Clear();
	}

	public void SortChildrenByLayer()
	{
		Children.Sort((a, b) => a.Layer.CompareTo(b.Layer));
	}

	/// <summary>
	/// Перенос группы.
	///
	/// Концы градиента у детей заданы в МИРОВЫХ координатах и матрице
	/// группы не подчиняются: геометрия уезжает, а отрезок градиента
	/// остаётся на прежнем месте. Стоит группе отъехать от него дальше
	/// своих габаритов — и заливка на вид становится сплошной, крайним
	/// цветом. Поэтому дети обходятся явно.
	/// </summary>
	public override void MoveByWorld(float dx, float dy)
	{
		base.MoveByWorld(dx, dy);

		foreach (var child in Children)
			MoveFillDeep(child, dx, dy);
	}

	private static void MoveFillDeep(Shape shape, float dx, float dy)
	{
		if (shape.Fill is { Enabled: true, FillTransformInitialized: true } fill
			&& fill.SupportsInteractiveHandles)
		{
			fill.StartPoint = new SKPoint(fill.StartPoint.X + dx, fill.StartPoint.Y + dy);
			fill.EndPoint = new SKPoint(fill.EndPoint.X + dx, fill.EndPoint.Y + dy);
		}

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				MoveFillDeep(child, dx, dy);
		}
	}

	/// <summary>
	/// Смена единиц страницы.
	///
	/// Геометрия детей пересчитывается сама: она задана относительно
	/// матрицы группы, а её база уже отмасштабировала. С обводкой так
	/// не выходит — толщина линии в мировых единицах и к матрице
	/// отношения не имеет, поэтому её приходится обойти явно.
	/// </summary>
	public override void SetShapeNewUoM(float oldMmPerUom, float newMmPerUom)
	{
		base.SetShapeNewUoM(oldMmPerUom, newMmPerUom);

		float ratio = oldMmPerUom / MathF.Max(newMmPerUom, 0.0001f);

		foreach (var child in Children)
			ScaleStrokeDeep(child, ratio);
	}

	private static void ScaleStrokeDeep(Shape shape, float ratio)
	{
		shape.Stroke?.SetStrokeNewUoM(ratio);
		shape.Fill?.SetFillNewUoM(ratio);

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				ScaleStrokeDeep(child, ratio);
		}
	}

	public override SKRect GetLocalBounds()
	{
		if (Children.Count == 0)
			return SKRect.Empty;

		SKRect result = SKRect.Empty;
		bool hasAny = false;

		foreach (var child in Children)
		{
			using var childWorldPath = child.BuildWorldPath();

			SKMatrix myInv;
			if (!TryGetInverseWorldTransform(out myInv))
				myInv = SKMatrix.CreateIdentity();

			using var localPath = new SKPath();
			childWorldPath.Transform(myInv, localPath);

			if (!hasAny)
			{
				result = localPath.Bounds;
				hasAny = true;
			}
			else
			{
				result = Union(result, localPath.Bounds);
			}
		}

		return hasAny ? result : SKRect.Empty;
	}

	public override SKPath BuildLocalPath()
	{
		var path = new SKPath();
		var bounds = GetLocalBounds();

		if (!bounds.IsEmpty)
			path.AddRect(bounds);

		return path;
	}

	protected override bool IsClosedGeometry() => false;

	public override bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		for (int i = Children.Count - 1; i >= 0; i--)
		{
			var child = Children[i];
			if (!child.IsVisible || child.IsLocked)
				continue;

			if (child.HitTest(pagePoint, toleranceUom, mmPerUom))
				return true;
		}

		return false;
	}

	public override void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		// Render, а не Draw: у ребёнка может быть своя тень, и она обязана
		// рисоваться внутри группы. Собственная тень группы при этом остаётся
		// отдельным силуэтом всего содержимого — её ставит Shape.Render выше
		// по стеку, подавляя на время эффекты детей.
		foreach (var child in Children.OrderBy(s => s.Layer))
			child.Render(canvas, mmPerWorldUnit);
	}

	/// <summary>
	/// Габариты группы с эффектами: свои плюс эффекты детей. Ребёнок с
	/// тенью, вылезающей за общий габарит, иначе обрезался бы отсечением
	/// по видимой области.
	/// </summary>
	public override SKRect GetRenderBounds()
	{
		var result = base.GetRenderBounds();

		foreach (var child in Children)
			result = Union(result, child.GetRenderBounds());

		return result;
	}

	public override SKRect GetBounds()
	{
		if (Children.Count == 0)
			return SKRect.Empty;

		SKRect result = SKRect.Empty;
		bool hasAny = false;

		foreach (var child in Children)
		{
			var b = child.GetBounds();

			if (!hasAny)
			{
				result = b;
				hasAny = true;
			}
			else
			{
				result = Union(result, b);
			}
		}

		return hasAny ? result : SKRect.Empty;
	}

	private static SKRect Union(SKRect a, SKRect b)
	{
		if (a.IsEmpty)
			return b;
		if (b.IsEmpty)
			return a;

		return new SKRect(
			MathF.Min(a.Left, b.Left),
			MathF.Min(a.Top, b.Top),
			MathF.Max(a.Right, b.Right),
			MathF.Max(a.Bottom, b.Bottom));
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();

		hash.Add(Children.Count);

		foreach (var child in Children)
			hash.Add(child.GetGeometryHash());

		var m = LocalTransform;

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

}

public static class GroupingHelper
{
	public static GroupShape? GroupSelected(Page page, IList<Shape> selection)
	{
		if (page == null || selection == null || selection.Count < 2)
			return null;

		var items = selection
			.Where(s => s != null)
			.Distinct()
			.ToList();

		if (items.Count < 2)
			return null;

		if (ContainsAncestorAndDescendant(items))
			return null;

		Shape? commonParent = GetCommonParent(items);
		int layer = items.Min(s => s.Layer);

		var group = new GroupShape
		{
			Name = "Group",
			Layer = layer
		};

		AttachToParent(page, group, commonParent);

		foreach (var item in items.OrderBy(s => s.Layer).ToList())
		{
			DetachFromCurrentParent(page, item);
			group.AddChild(item);
		}

		page.SortLayers();
		group.SortChildrenByLayer();

		return group;
	}

	public static List<Shape> UngroupSelected(Page page, IList<Shape> selection)
	{
		var result = new List<Shape>();

		if (page == null || selection == null || selection.Count == 0)
			return result;

		var groups = selection
			.OfType<GroupShape>()
			.Distinct()
			.ToList();

		foreach (var group in groups)
		{
			Shape? targetParent = group.Parent;
			var children = group.Children.OrderBy(s => s.Layer).ToList();

			foreach (var child in children)
			{
				group.RemoveChild(child);
				AttachToParentPreserveWorld(page, child, targetParent);
				result.Add(child);
			}

			DetachFromCurrentParent(page, group);
		}

		page.SortLayers();
		return result;
	}

	private static void AttachToParent(Page page, Shape shape, Shape? parent)
	{
		if (parent is GroupShape group)
			group.AddChild(shape);
		else
			page.AddShape(shape);
	}

	private static void AttachToParentPreserveWorld(Page page, Shape shape, Shape? parent)
	{
		shape.ReparentPreserveWorld(parent);

		if (parent is GroupShape group)
		{
			if (!group.Children.Contains(shape))
				group.Children.Add(shape);

			group.SortChildrenByLayer();
		}
		else
		{
			if (!page.Shapes.Contains(shape))
				page.Shapes.Add(shape);

			page.SortLayers();
		}
	}

	private static void DetachFromCurrentParent(Page page, Shape shape)
	{
		if (shape.Parent is GroupShape parentGroup)
			parentGroup.RemoveChild(shape);
		else
			page.RemoveShape(shape);
	}

	private static Shape? GetCommonParent(IList<Shape> items)
	{
		if (items.Count == 0)
			return null;

		var parent = items[0].Parent;

		for (int i = 1; i < items.Count; i++)
		{
			if (!ReferenceEquals(parent, items[i].Parent))
				return null;
		}

		return parent;
	}

	private static bool ContainsAncestorAndDescendant(IList<Shape> items)
	{
		var set = new HashSet<Shape>(items);

		foreach (var item in items)
		{
			var p = item.Parent;
			while (p != null)
			{
				if (set.Contains(p))
					return true;

				p = p.Parent;
			}
		}

		return false;
	}
}

public class RectangleShape : Shape
{
	public SKRect LocalRect { get; set; }

	public RectangleShape()
	{
		Name = "Rectangle";
	}

	public override SKRect GetLocalBounds() => LocalRect;

	public override SKPath BuildLocalPath()
	{
		var b = new SKPathBuilder();
		b.AddRect(LocalRect);
		return b.Detach();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new RectGeometrySnapshot { Rect = LocalRect };

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is RectGeometrySnapshot s)
			LocalRect = s.Rect;
	}

	public override CurveShape? ToCurveShape()
	{
		var r = LocalRect;

		var curve = new CurveShape();

		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left, r.Top), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = true, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right, r.Top), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right, r.Bottom), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left, r.Bottom), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = true });

		return PrepareCurveCloneFromShape(this, curve);
	}
}

public sealed class RoundedRectangleShape : Shape
{
	public SKRect LocalRect { get; set; }
	public float RadiusX { get; set; } = 10f;
	public float RadiusY { get; set; } = 10f;

	public RoundedRectangleShape()
	{
		Name = "RoundedRectangle";
	}

	public override SKRect GetLocalBounds() => LocalRect;

	public override SKPath BuildLocalPath()
	{
		var b = new SKPathBuilder();
		b.AddRoundRect(LocalRect, RadiusX, RadiusY);
		return b.Detach();
	}

	public override CurveShape? ToCurveShape()
	{
		var r = LocalRect;
		float rx = Math.Clamp(RadiusX, 0f, r.Width * 0.5f);
		float ry = Math.Clamp(RadiusY, 0f, r.Height * 0.5f);

		if (rx <= 0.0001f || ry <= 0.0001f)
		{
			var rectShape = new RectangleShape
			{
				LocalRect = LocalRect,
				LocalTransform = LocalTransform,
				Stroke = Stroke,
				Fill = Fill,
				Layer = Layer,
				Name = Name,
				IsVisible = IsVisible,
				IsLocked = IsLocked,
				IsFocused = IsFocused
			};
			return rectShape.ToCurveShape();
		}

		const float k = 0.5522847498307936f;

		var curve = new CurveShape();

		var n0 = new CurveNode { Position = new SKPoint(r.Left + rx, r.Top), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = true, CloseAfter = false };
		var n1 = new CurveNode { Position = new SKPoint(r.Right - rx, r.Top), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n2 = new CurveNode { Position = new SKPoint(r.Right, r.Top + ry), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n3 = new CurveNode { Position = new SKPoint(r.Right, r.Bottom - ry), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n4 = new CurveNode { Position = new SKPoint(r.Right - rx, r.Bottom), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n5 = new CurveNode { Position = new SKPoint(r.Left + rx, r.Bottom), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n6 = new CurveNode { Position = new SKPoint(r.Left, r.Bottom - ry), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = false };
		var n7 = new CurveNode { Position = new SKPoint(r.Left, r.Top + ry), Type = CurveNodeType.Sharp, BreakAfter = false, NewContour = false, CloseAfter = true };

		n0.ControlPrev = new SKPoint(n0.Position.X - rx * k, n0.Position.Y);
		n0.ControlNext = n0.Position;

		n1.ControlPrev = n1.Position;
		n1.ControlNext = new SKPoint(n1.Position.X + rx * k, n1.Position.Y);

		n2.ControlPrev = new SKPoint(n2.Position.X, n2.Position.Y - ry * k);
		n2.ControlNext = n2.Position;

		n3.ControlPrev = n3.Position;
		n3.ControlNext = new SKPoint(n3.Position.X, n3.Position.Y + ry * k);

		n4.ControlPrev = new SKPoint(n4.Position.X + rx * k, n4.Position.Y);
		n4.ControlNext = n4.Position;

		n5.ControlPrev = n5.Position;
		n5.ControlNext = new SKPoint(n5.Position.X - rx * k, n5.Position.Y);

		n6.ControlPrev = new SKPoint(n6.Position.X, n6.Position.Y + ry * k);
		n6.ControlNext = n6.Position;

		n7.ControlPrev = n7.Position;
		n7.ControlNext = new SKPoint(n7.Position.X, n7.Position.Y - ry * k);

		curve.Nodes.AddRange(new[] { n0, n1, n2, n3, n4, n5, n6, n7 });

		return PrepareCurveCloneFromShape(this, curve);
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new RoundedRectGeometrySnapshot { Rect = LocalRect, RadiusX = RadiusX, RadiusY = RadiusY };

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not RoundedRectGeometrySnapshot s)
			return;

		LocalRect = s.Rect;
		RadiusX = s.RadiusX;
		RadiusY = s.RadiusY;
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(RadiusX);
		hash.Add(RadiusY);
		return hash.ToHashCode();
	}
}

public sealed class ChamferedRectangleShape : Shape
{
	public SKRect LocalRect { get; set; }
	public float ChamferX { get; set; } = 10f;
	public float ChamferY { get; set; } = 10f;

	public ChamferedRectangleShape()
	{
		Name = "ChamferedRectangle";
	}

	public override SKRect GetLocalBounds() => LocalRect;

	public override SKPath BuildLocalPath()
	{
		var r = LocalRect;

		float cx = Math.Clamp(ChamferX, 0f, r.Width * 0.5f);
		float cy = Math.Clamp(ChamferY, 0f, r.Height * 0.5f);

		var pb = new SKPathBuilder();

		pb.MoveTo(r.Left + cx, r.Top);
		pb.LineTo(r.Right - cx, r.Top);
		pb.LineTo(r.Right, r.Top + cy);
		pb.LineTo(r.Right, r.Bottom - cy);
		pb.LineTo(r.Right - cx, r.Bottom);
		pb.LineTo(r.Left + cx, r.Bottom);
		pb.LineTo(r.Left, r.Bottom - cy);
		pb.LineTo(r.Left, r.Top + cy);
		pb.Close();

		return pb.Detach();
	}

	public override CurveShape? ToCurveShape()
	{
		var r = LocalRect;
		float cx = Math.Clamp(ChamferX, 0f, r.Width * 0.5f);
		float cy = Math.Clamp(ChamferY, 0f, r.Height * 0.5f);

		var curve = new CurveShape();

		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left + cx, r.Top), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = true, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right - cx, r.Top), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right, r.Top + cy), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right, r.Bottom - cy), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Right - cx, r.Bottom), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left + cx, r.Bottom), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left, r.Bottom - cy), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = false });
		curve.Nodes.Add(new CurveNode { Position = new SKPoint(r.Left, r.Top + cy), Type = CurveNodeType.Straight, BreakAfter = false, NewContour = false, CloseAfter = true });

		return PrepareCurveCloneFromShape(this, curve);
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new ChamferedRectGeometrySnapshot { Rect = LocalRect, ChamferX = ChamferX, ChamferY = ChamferY };

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not ChamferedRectGeometrySnapshot s)
			return;

		LocalRect = s.Rect;
		ChamferX = s.ChamferX;
		ChamferY = s.ChamferY;
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(ChamferX);
		hash.Add(ChamferY);
		return hash.ToHashCode();
	}
}

public sealed class Circle2PShape : Shape
{
	public SKPoint CenterPoint { get; set; }
	public SKPoint RadiusPoint { get; set; }

	public SKPoint LocalCenter { get; set; } = SKPoint.Empty;
	public float Radius { get; set; }

	public Circle2PShape()
	{
		Name = "Circle2P";
	}

	public static Circle2PShape CreateFromPoints(SKPoint centerWorld, SKPoint radiusWorld)
	{
		float dx = radiusWorld.X - centerWorld.X;
		float dy = radiusWorld.Y - centerWorld.Y;
		float radius = MathF.Sqrt(dx * dx + dy * dy);

		return new Circle2PShape
		{
			LocalTransform = SKMatrix.CreateTranslation(centerWorld.X, centerWorld.Y),
			LocalCenter = SKPoint.Empty,
			Radius = radius,
			CenterPoint = SKPoint.Empty,
			RadiusPoint = new SKPoint(dx, dy)
		};
	}

	public override SKRect GetLocalBounds()
	{
		return new SKRect(
			LocalCenter.X - Radius,
			LocalCenter.Y - Radius,
			LocalCenter.X + Radius,
			LocalCenter.Y + Radius);
	}

	/// <summary>
	/// Габариты считаются по самой кривой, а не по контрольным точкам
	/// коник: у повёрнутой фигуры второе заметно больше первого.
	/// </summary>
	public override SKRect GetBounds() => GetOvalWorldBounds(GetLocalBounds());

	public override SKPath BuildLocalPath()
	{
		var b = new SKPathBuilder();
		if (Radius > 0.0001f)
			b.AddCircle(LocalCenter.X, LocalCenter.Y, Radius);
		return b.Detach();
	}

	/// <summary>
	/// Хеш собственной геометрии.
	///
	/// Базовая реализация считает по габаритам, а у этой фигуры форму
	/// задают опорные точки: правка, не меняющая габаритный прямоугольник,
	/// оставляла бы хеш прежним. Потребители, кэширующие результат по нему
	/// (текст внутри области, инструмент преобразования), не замечали бы
	/// изменения и показывали старую форму.
	/// </summary>
	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(LocalCenter.X);
		hash.Add(LocalCenter.Y);
		hash.Add(Radius);
		hash.Add(CenterPoint.X);
		hash.Add(CenterPoint.Y);
		hash.Add(RadiusPoint.X);
		hash.Add(RadiusPoint.Y);
		return hash.ToHashCode();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new Circle2PGeometrySnapshot
		{
			LocalCenter = LocalCenter,
			Radius = Radius,
			CenterPoint = CenterPoint,
			RadiusPoint = RadiusPoint
		};

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not Circle2PGeometrySnapshot s)
			return;

		LocalCenter = s.LocalCenter;
		Radius = s.Radius;
		CenterPoint = s.CenterPoint;
		RadiusPoint = s.RadiusPoint;
	}

	public override CurveShape? ToCurveShape()
	{
		var curve = BuildEllipseCurve(LocalCenter, Radius, Radius);
		return PrepareCurveCloneFromShape(this, curve);
	}
}

public sealed class Circle3PShape : Shape
{
	public SKPoint P1 { get; set; }
	public SKPoint P2 { get; set; }
	public SKPoint P3 { get; set; }

	public Circle3PShape()
	{
		Name = "Circle3P";
	}

	public static Circle3PShape CreateNormalized(SKPoint w1, SKPoint w2, SKPoint w3)
	{
		float left = MathF.Min(w1.X, MathF.Min(w2.X, w3.X));
		float top = MathF.Min(w1.Y, MathF.Min(w2.Y, w3.Y));

		var origin = new SKPoint(left, top);

		return new Circle3PShape
		{
			P1 = new SKPoint(w1.X - origin.X, w1.Y - origin.Y),
			P2 = new SKPoint(w2.X - origin.X, w2.Y - origin.Y),
			P3 = new SKPoint(w3.X - origin.X, w3.Y - origin.Y),
			LocalTransform = SKMatrix.CreateTranslation(origin.X, origin.Y)
		};
	}

	public override SKRect GetLocalBounds()
	{
		if (!Geometry.TryCircleFrom3Points(P1, P2, P3, out var c, out var r))
			return SKRect.Empty;

		return new SKRect(c.X - r, c.Y - r, c.X + r, c.Y + r);
	}

	/// <summary>
	/// Габариты считаются по самой кривой, а не по контрольным точкам
	/// коник: у повёрнутой фигуры второе заметно больше первого.
	/// </summary>
	public override SKRect GetBounds() => GetOvalWorldBounds(GetLocalBounds());

	public override SKPath BuildLocalPath()
	{
		var b = new SKPathBuilder();

		if (Geometry.TryCircleFrom3Points(P1, P2, P3, out var c, out var r))
			b.AddCircle(c.X, c.Y, r);

		return b.Detach();
	}

	public override void MoveByLocal(float dx, float dy)
	{
		LocalTransform = LocalTransform.PostConcat(SKMatrix.CreateTranslation(dx, dy));
	}

	/// <summary>
	/// Хеш собственной геометрии.
	///
	/// Базовая реализация считает по габаритам, а у этой фигуры форму
	/// задают опорные точки: правка, не меняющая габаритный прямоугольник,
	/// оставляла бы хеш прежним. Потребители, кэширующие результат по нему
	/// (текст внутри области, инструмент преобразования), не замечали бы
	/// изменения и показывали старую форму.
	/// </summary>
	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(P1.X);
		hash.Add(P1.Y);
		hash.Add(P2.X);
		hash.Add(P2.Y);
		hash.Add(P3.X);
		hash.Add(P3.Y);
		return hash.ToHashCode();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new Circle3PGeometrySnapshot { P1 = P1, P2 = P2, P3 = P3 };

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not Circle3PGeometrySnapshot s)
			return;

		P1 = s.P1;
		P2 = s.P2;
		P3 = s.P3;
	}

	public override CurveShape? ToCurveShape()
	{
		if (!Geometry.TryCircleFrom3Points(P1, P2, P3, out var c, out var r))
			return null;

		var curve = BuildEllipseCurve(c, r, r);
		return PrepareCurveCloneFromShape(this, curve);
	}
}

public sealed class Ellipse3PShape : Shape
{
	public SKPoint CenterPoint { get; set; }
	public SKPoint RadiusPoint1 { get; set; }
	public SKPoint RadiusPoint2 { get; set; }

	public SKPoint LocalCenter { get; set; } = SKPoint.Empty;
	public float RadiusX { get; set; }
	public float RadiusY { get; set; }

	public Ellipse3PShape()
	{
		Name = "Ellipse3P";
	}

	public static Ellipse3PShape CreateFromCenterAndAxes(
		SKPoint centerWorld,
		SKPoint axis1PointWorld,
		SKPoint axis2PointWorld)
	{
		float vx = axis1PointWorld.X - centerWorld.X;
		float vy = axis1PointWorld.Y - centerWorld.Y;
		float len1 = MathF.Sqrt(vx * vx + vy * vy);
		if (len1 < 0.0001f)
			len1 = 0.0001f;

		float ux = vx / len1;
		float uy = vy / len1;

		float nx = -uy;
		float ny = ux;

		float wx = axis2PointWorld.X - centerWorld.X;
		float wy = axis2PointWorld.Y - centerWorld.Y;

		float len2Signed = wx * nx + wy * ny;
		float len2 = MathF.Abs(len2Signed);
		if (len2 < 0.0001f)
			len2 = 0.0001f;

		float sign2 = len2Signed >= 0f ? 1f : -1f;

		var rotation = new SKMatrix
		{
			ScaleX = ux,
			SkewX = nx,
			TransX = centerWorld.X,

			SkewY = uy,
			ScaleY = ny,
			TransY = centerWorld.Y,

			Persp0 = 0,
			Persp1 = 0,
			Persp2 = 1
		};

		var shape = new Ellipse3PShape
		{
			LocalCenter = SKPoint.Empty,
			RadiusX = len1,
			RadiusY = len2,
			LocalTransform = rotation
		};

		shape.CenterPoint = SKPoint.Empty;
		shape.RadiusPoint1 = new SKPoint(len1, 0f);
		shape.RadiusPoint2 = new SKPoint(0f, sign2 * len2);

		return shape;
	}

	public override SKRect GetLocalBounds()
	{
		return new SKRect(-RadiusX, -RadiusY, RadiusX, RadiusY);
	}

	/// <summary>
	/// Габариты считаются по самой кривой, а не по контрольным точкам
	/// коник: у повёрнутой фигуры второе заметно больше первого.
	/// </summary>
	public override SKRect GetBounds() => GetOvalWorldBounds(GetLocalBounds());

	public override SKPath BuildLocalPath()
	{
		var pb = new SKPathBuilder();
		if (RadiusX > 0.0001f && RadiusY > 0.0001f)
			pb.AddOval(GetLocalBounds());
		return pb.Detach();
	}

	/// <summary>
	/// Хеш собственной геометрии.
	///
	/// Базовая реализация считает по габаритам, а у этой фигуры форму
	/// задают опорные точки: правка, не меняющая габаритный прямоугольник,
	/// оставляла бы хеш прежним. Потребители, кэширующие результат по нему
	/// (текст внутри области, инструмент преобразования), не замечали бы
	/// изменения и показывали старую форму.
	/// </summary>
	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(LocalCenter.X);
		hash.Add(LocalCenter.Y);
		hash.Add(RadiusX);
		hash.Add(RadiusY);
		hash.Add(CenterPoint.X);
		hash.Add(CenterPoint.Y);
		hash.Add(RadiusPoint1.X);
		hash.Add(RadiusPoint1.Y);
		hash.Add(RadiusPoint2.X);
		hash.Add(RadiusPoint2.Y);
		return hash.ToHashCode();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new Ellipse3PGeometrySnapshot
		{
			LocalCenter = LocalCenter,
			RadiusX = RadiusX,
			RadiusY = RadiusY,
			CenterPoint = CenterPoint,
			RadiusPoint1 = RadiusPoint1,
			RadiusPoint2 = RadiusPoint2
		};

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not Ellipse3PGeometrySnapshot s)
			return;

		LocalCenter = s.LocalCenter;
		RadiusX = s.RadiusX;
		RadiusY = s.RadiusY;
		CenterPoint = s.CenterPoint;
		RadiusPoint1 = s.RadiusPoint1;
		RadiusPoint2 = s.RadiusPoint2;
	}

	public override CurveShape? ToCurveShape()
	{
		var curve = BuildEllipseCurve(LocalCenter, RadiusX, RadiusY);
		return PrepareCurveCloneFromShape(this, curve);
	}
}

public sealed class Ellipse5PShape : Shape
{
	/// <summary>
	/// Точки построения. Сеттер закрытый по той же причине, что
	/// и у многоугольника: список подменяется целиком в Clone.
	/// </summary>
	public List<SKPoint> Points { get; private set; } = new();

	public SKPoint LocalCenter { get; set; } = SKPoint.Empty;
	public float RadiusX { get; set; }
	public float RadiusY { get; set; }

	/// <summary>
	/// Копия с собственным списком точек — иначе копии эллипса делили бы
	/// точки построения с оригиналом, как это было у многоугольника.
	/// </summary>
	public override Shape Clone()
	{
		var clone = (Ellipse5PShape)base.Clone();

		clone.Points = new List<SKPoint>(Points);

		return clone;
	}

	public Ellipse5PShape()
	{
		Name = "Ellipse5P";
	}

	public static Ellipse5PShape? CreateFromFivePoints(IReadOnlyList<SKPoint> worldPoints)
	{
		if (worldPoints == null || worldPoints.Count < 5)
			return null;

		var pts = worldPoints.Take(5).ToArray();

		double[,] m = new double[5, 5];
		double[] rhs = new double[5];

		for (int i = 0; i < 5; i++)
		{
			double x = pts[i].X;
			double y = pts[i].Y;

			m[i, 0] = x * x;
			m[i, 1] = x * y;
			m[i, 2] = y * y;
			m[i, 3] = x;
			m[i, 4] = y;
			rhs[i] = 1.0;
		}

		if (!SolveLinear5x5(m, rhs, out var sol))
			return null;

		double A = sol[0];
		double B = sol[1];
		double C = sol[2];
		double D = sol[3];
		double E = sol[4];
		double F = -1.0;

		double det = B * B - 4.0 * A * C;
		if (det >= 0.0)
			return null;

		double x0 = (2.0 * C * D - B * E) / det;
		double y0 = (2.0 * A * E - B * D) / det;

		double phi = 0.5 * Math.Atan2(B, A - C);

		double cos = Math.Cos(phi);
		double sin = Math.Sin(phi);

		double Ap = A * cos * cos + B * cos * sin + C * sin * sin;
		double Cp = A * sin * sin - B * cos * sin + C * cos * cos;

		double Fc = A * x0 * x0 + B * x0 * y0 + C * y0 * y0 + D * x0 + E * y0 + F;

		if (Math.Abs(Ap) < 1e-12 || Math.Abs(Cp) < 1e-12)
			return null;

		double rx2 = -Fc / Ap;
		double ry2 = -Fc / Cp;

		if (rx2 <= 0.0 || ry2 <= 0.0)
			return null;

		float rx = (float)Math.Sqrt(rx2);
		float ry = (float)Math.Sqrt(ry2);

		var rot = new SKMatrix
		{
			ScaleX = (float)cos,
			SkewX = (float)-sin,
			TransX = (float)x0,

			SkewY = (float)sin,
			ScaleY = (float)cos,
			TransY = (float)y0,

			Persp0 = 0,
			Persp1 = 0,
			Persp2 = 1
		};

		var shape = new Ellipse5PShape
		{
			LocalCenter = SKPoint.Empty,
			RadiusX = rx,
			RadiusY = ry,
			LocalTransform = rot
		};

		foreach (var p in pts)
			shape.Points.Add(shape.WorldToLocal(p));

		return shape;
	}

	public override SKRect GetLocalBounds()
	{
		return new SKRect(-RadiusX, -RadiusY, RadiusX, RadiusY);
	}

	/// <summary>
	/// Габариты считаются по самой кривой, а не по контрольным точкам
	/// коник: у повёрнутой фигуры второе заметно больше первого.
	/// </summary>
	public override SKRect GetBounds() => GetOvalWorldBounds(GetLocalBounds());

	public override SKPath BuildLocalPath()
	{
		var pb = new SKPathBuilder();
		if (RadiusX > 0.0001f && RadiusY > 0.0001f)
			pb.AddOval(GetLocalBounds());
		return pb.Detach();
	}

	private static bool SolveLinear5x5(double[,] a, double[] b, out double[] x)
	{
		const int n = 5;
		x = new double[n];

		double[,] aug = new double[n, n + 1];
		for (int r = 0; r < n; r++)
		{
			for (int c = 0; c < n; c++)
				aug[r, c] = a[r, c];
			aug[r, n] = b[r];
		}

		for (int col = 0; col < n; col++)
		{
			int pivot = col;
			double maxAbs = Math.Abs(aug[pivot, col]);

			for (int r = col + 1; r < n; r++)
			{
				double v = Math.Abs(aug[r, col]);
				if (v > maxAbs)
				{
					maxAbs = v;
					pivot = r;
				}
			}

			if (maxAbs < 1e-12)
				return false;

			if (pivot != col)
			{
				for (int c = col; c <= n; c++)
				{
					double tmp = aug[col, c];
					aug[col, c] = aug[pivot, c];
					aug[pivot, c] = tmp;
				}
			}

			double div = aug[col, col];
			for (int c = col; c <= n; c++)
				aug[col, c] /= div;

			for (int r = 0; r < n; r++)
			{
				if (r == col)
					continue;

				double factor = aug[r, col];
				for (int c = col; c <= n; c++)
					aug[r, c] -= factor * aug[col, c];
			}
		}

		for (int i = 0; i < n; i++)
			x[i] = aug[i, n];

		return true;
	}

	/// <summary>
	/// Хеш собственной геометрии.
	///
	/// Базовая реализация считает по габаритам, а у этой фигуры форму
	/// задают опорные точки: правка, не меняющая габаритный прямоугольник,
	/// оставляла бы хеш прежним. Потребители, кэширующие результат по нему
	/// (текст внутри области, инструмент преобразования), не замечали бы
	/// изменения и показывали старую форму.
	/// </summary>
	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(LocalCenter.X);
		hash.Add(LocalCenter.Y);
		hash.Add(RadiusX);
		hash.Add(RadiusY);
		hash.Add(Points.Count);

		foreach (var pt in Points)
		{
			hash.Add(pt.X);
			hash.Add(pt.Y);
		}

		return hash.ToHashCode();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new Ellipse5PGeometrySnapshot
		{
			LocalCenter = LocalCenter,
			RadiusX = RadiusX,
			RadiusY = RadiusY,
			Points = Points.ToArray()
		};

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not Ellipse5PGeometrySnapshot s)
			return;

		LocalCenter = s.LocalCenter;
		RadiusX = s.RadiusX;
		RadiusY = s.RadiusY;

		Points.Clear();
		Points.AddRange(s.Points);
	}

	public override CurveShape? ToCurveShape()
	{
		var curve = BuildEllipseCurve(LocalCenter, RadiusX, RadiusY);
		return PrepareCurveCloneFromShape(this, curve);
	}
}

public sealed class PolygonShape : Shape
{
	/// <summary>
	/// Вершины в локальных координатах.
	///
	/// Сеттер закрытый и нужен только Clone: список надо подменить целиком
	/// на новый, а очистить существующий нельзя — у копии и оригинала
	/// после MemberwiseClone он один и тот же.
	/// </summary>
	public List<SKPoint> Points { get; private set; } = new();

	public bool UseExplicitPoints { get; set; }
	public SKPoint LocalCenter { get; set; } = SKPoint.Empty;
	public float RadiusX { get; set; } = 10f;
	public float RadiusY { get; set; } = 10f;
	public float RotationRad { get; set; } = 0f;
	public int Sides { get; set; } = 5;

	public PolygonShape()
	{
		Name = "Polygon";
	}

	/// <summary>
	/// Копия многоугольника с собственными вершинами.
	///
	/// Без этого переопределения работал Shape.Clone, а он делает
	/// MemberwiseClone — то есть копирует ССЫЛКУ на список точек. Копия
	/// и оригинал получали один список на двоих, и перетаскивание одной
	/// вершины двигало её сразу во всех копиях фигуры. Заметно это только
	/// на свободном многоугольнике: у правильного вершины пересчитываются
	/// из центра и радиусов, и общий список ничего не решает.
	///
	/// SKPoint — структура, поэтому новый список копирует значения,
	/// и глубже опускаться не нужно.
	/// </summary>
	public override Shape Clone()
	{
		var clone = (PolygonShape)base.Clone();

		clone.Points = new List<SKPoint>(Points);

		return clone;
	}

	public override SKRect GetLocalBounds()
	{
		if (Points.Count == 0)
			return SKRect.Empty;

		return RectFromPoints(Points);
	}

	public override SKPath BuildLocalPath()
	{
		var path = new SKPath();

		if (Points.Count == 0)
			return path;

		if (Points.Count == 1)
		{
			path.MoveTo(Points[0]);
			path.LineTo(Points[0]);
			return path;
		}

		path.MoveTo(Points[0]);

		for (int i = 1; i < Points.Count; i++)
			path.LineTo(Points[i]);

		if (Points.Count >= 3)
			path.Close();

		return path;
	}

	protected override bool IsClosedGeometry() => Points.Count >= 3;

	public void RebuildPoints()
	{
		if (UseExplicitPoints)
			return;

		Points.Clear();

		if (Sides < 3)
			return;

		float step = 2f * MathF.PI / Sides;

		for (int i = 0; i < Sides; i++)
		{
			float a = RotationRad + i * step;
			float x = LocalCenter.X + MathF.Cos(a) * RadiusX;
			float y = LocalCenter.Y + MathF.Sin(a) * RadiusY;
			Points.Add(new SKPoint(x, y));
		}
	}

	public SKPoint GetMainHandleLocal()
	{
		return new SKPoint(
			LocalCenter.X + MathF.Cos(RotationRad) * RadiusX,
			LocalCenter.Y + MathF.Sin(RotationRad) * RadiusY);
	}

	public SKPoint GetMainHandleWorld()
	{
		return LocalToWorld(GetMainHandleLocal());
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();
		hash.Add(base.GetGeometryHash());
		hash.Add(RadiusX);
		hash.Add(RadiusY);
		return hash.ToHashCode();
	}

	public void InitializeFromPointsIfNeeded()
	{
		if (UseExplicitPoints)
			return;

		if (GeometryInitialized)
			return;

		if (Points.Count < 3)
			return;

		var bounds = RectFromPoints(Points);

		LocalCenter = new SKPoint(bounds.MidX, bounds.MidY);
		RadiusX = MathF.Max(0.0001f, bounds.Width * 0.5f);
		RadiusY = MathF.Max(0.0001f, bounds.Height * 0.5f);

		if (Sides < 3)
			Sides = Points.Count;

		var first = Points[0];
		RotationRad = MathF.Atan2(first.Y - LocalCenter.Y, first.X - LocalCenter.X);

		RebuildPoints();
		MarkGeometryInitialized();
	}

	public override IGeometrySnapshot? CaptureGeometrySnapshot()
		=> new PolygonGeometrySnapshot
		{
			LocalCenter = LocalCenter,
			RadiusX = RadiusX,
			RadiusY = RadiusY,
			RotationRad = RotationRad,
			Sides = Sides,
			UseExplicitPoints = UseExplicitPoints,
			Points = Points.ToArray()
		};

	public override void RestoreGeometrySnapshot(IGeometrySnapshot? snapshot)
	{
		if (snapshot is not PolygonGeometrySnapshot s)
			return;

		LocalCenter = s.LocalCenter;
		RadiusX = s.RadiusX;
		RadiusY = s.RadiusY;
		RotationRad = s.RotationRad;
		Sides = s.Sides;
		UseExplicitPoints = s.UseExplicitPoints;

		// Вершины восстанавливаются как есть, а не пересчитываются
		// RebuildPoints: у произвольного многоугольника пересчёта нет вовсе,
		// и восстановить его форму можно только сохранёнными точками.
		Points.Clear();
		Points.AddRange(s.Points);
	}

	public override CurveShape? ToCurveShape()
	{
		if (!UseExplicitPoints && Points.Count == 0)
			RebuildPoints();

		if (Points.Count < 3)
			return null;

		var curve = new CurveShape();

		for (int i = 0; i < Points.Count; i++)
		{
			curve.Nodes.Add(new CurveNode
			{
				Position = Points[i],
				Type = CurveNodeType.Straight,
				BreakAfter = false,
				NewContour = i == 0,

				// Замыкающий сегмент строится от ПОСЛЕДНЕГО узла к первому.
				// Полигон замкнут по определению, поэтому флаг безусловный.
				CloseAfter = i == Points.Count - 1
			});
		}

		return PrepareCurveCloneFromShape(this, curve);
	}
}

/// <summary>
/// Растровое изображение, вписанное в произвольный контур (наследник CurveShape).
///
/// Модель данных:
///   OriginalBitmap  — исходные пиксели (эталон, к которому не применены коррекции).
///   tileDoc         — тайловое хранилище рабочих пикселей; на нём работают кисти.
///   Bitmap          — плоская склейка тайлов, кэш для старого кода и экспорта.
///   Adjustments     — недеструктивная коррекция, применяемая на этапе отрисовки.
///
/// Коррекции НЕ запекаются в пиксели до явного вызова CommitAdjustments().
/// Отрисовка идёт целиком средствами Skia — прямых GL-вызовов в классе нет.
/// </summary>
public sealed class ImageShape : CurveShape
{
	#region Поля и свойства
	public string ImagePath { get; set; } = string.Empty;

	/// <summary>Плоская склейка тайлов. Совместимость со старым кодом и экспортом.</summary>
	public SKBitmap? Bitmap { get; set; }

	/// <summary>Тайловое хранилище рабочих пикселей.</summary>
	public ImageTileDocument? tileDoc { get; set; }

	public int TileSize { get; private set; } = 128;

	private bool _compositeDirty = true;

	public SKRect ImageLocalRect { get; set; } = SKRect.Empty;

	/// <summary>Временный превью-стейт мазка кисти. В документ не входит.</summary>
	public ActiveImageStrokeSession? ActiveStroke { get; internal set; }

	/// <summary>Параметры недеструктивной коррекции.</summary>
	public ImageAdjustmentSettings Adjustments { get; private set; } = new();

	/// <summary>Эталонные пиксели без коррекций.</summary>
	public SKBitmap? OriginalBitmap { get; private set; }

	private SKPaint? _adjustPaint;
	private int _adjustPaintVersion = -1;

	/// <summary>
	/// Показывать изображение в том разрешении, которое у него останется
	/// после приведения к физическому размеру на листе.
	///
	/// Обычная отрисовка масштабирует полный растр под размер фигуры и
	/// сглаживает результат: уменьшенная до 10 мм картинка на увеличении
	/// 1500% выглядит гладкой и детальной, хотя после печати или экспорта
	/// в ней останется четыре десятка пикселей по стороне. Режим показывает
	/// именно этот результат, чтобы потеря деталей была видна во время
	/// работы, а не после вывода.
	/// </summary>
	public bool ActualResolution { get; set; }

	/// <summary>
	/// Разрешение документа, к которому приводится картинка в режиме
	/// ActualResolution.
	///
	/// Это разрешение ВЫВОДА, оно постоянно для документа. Меняется не оно,
	/// а число пикселей: оно считается из физического размера фигуры на
	/// листе, поэтому у маленькой копии картинки пикселей мало, у большой —
	/// много. Ровно так же растр поведёт себя при печати и экспорте.
	///
	/// Выставляется отрисовщиком перед проходом по фигурам: у фигуры нет
	/// ссылки на свою страницу, а тащить контекст рендера через сигнатуру
	/// Shape.Render означало бы править все реализации ради одного случая.
	/// </summary>
	public static float RenderDpi { get; set; } = 96f;

	private SKBitmap? _actualCache;
	private int _actualCacheW;
	private int _actualCacheH;
	private long _actualCacheVersion = -1;

	/// <summary>
	/// Счётчик изменений пикселей. Растёт при любой правке содержимого и
	/// служит ключом кэша уменьшенной копии.
	/// </summary>
	public long PixelVersion { get; private set; }

	public void BumpPixelVersion()
	{
		PixelVersion++;
	}

	#endregion

	#region Конструктор

	public ImageShape()
	{
		Name = "Image";
		ImageLocalRect = SKRect.Empty;
	}

	#endregion

	#region Загрузка и синхронизация битмапов

	public void LoadImage(string path)
	{
		ImagePath = path;

		EndActiveStrokePreview();

		Bitmap?.Dispose();
		Bitmap = SKBitmap.Decode(path);

		SyncOriginalFromCommittedBitmap();
		RebuildTilesFromBitmap();

		Adjustments.Reset();
		InvalidateAdjustmentPaint();

		_compositeDirty = false;
	}

	public void SetOriginalBitmap(SKBitmap bitmap)
	{
		if (bitmap == null)
			throw new ArgumentNullException(nameof(bitmap));

		EndActiveStrokePreview();

		Bitmap?.Dispose();
		OriginalBitmap?.Dispose();

		Bitmap = bitmap.Copy();
		OriginalBitmap = bitmap.Copy();

		RebuildTilesFromBitmap();
		InvalidateAdjustmentPaint();
	}

	/// <summary>Принять текущие рабочие пиксели за новый эталон.</summary>
	public void SyncOriginalFromCommittedBitmap()
	{
		if (Bitmap == null)
			return;

		OriginalBitmap?.Dispose();
		OriginalBitmap = Bitmap.Copy();
	}

	public void EnsureOriginalBitmap()
	{
		if (OriginalBitmap == null && Bitmap != null)
			OriginalBitmap = Bitmap.Copy();
	}

	public bool EnsureBitmapLoaded()
	{
		if (Bitmap != null)
			return true;

		if (!string.IsNullOrWhiteSpace(ImagePath) && System.IO.File.Exists(ImagePath))
		{
			Bitmap = SKBitmap.Decode(ImagePath);

			if (Bitmap != null)
			{
				RebuildTilesFromBitmap();
				_compositeDirty = false;
				return true;
			}
		}

		if (tileDoc != null)
		{
			RebuildBitmapFromTiles();
			return Bitmap != null;
		}

		return false;
	}

	/// <summary>
	/// Восстановление изображения из файла документа.
	///
	/// Отдельно от NotifyBitmapChanged: тот переснимает эталон с рабочих
	/// пикселей, а здесь эталон читается из файла своим. Иначе документ
	/// с применёнными коррекциями терял бы возможность вернуться к исходнику.
	/// </summary>
	public void RestoreFromDocument(SKBitmap working, SKBitmap? original)
	{
		if (working == null)
			return;

		EndActiveStrokePreview();

		Bitmap?.Dispose();
		OriginalBitmap?.Dispose();

		Bitmap = working;
		OriginalBitmap = original ?? working.Copy();

		RebuildTilesFromBitmap();
		InvalidateAdjustmentPaint();

		_compositeDirty = false;
	}

	/// <summary>Вызывать после внешней модификации Bitmap напрямую.</summary>
	public void NotifyBitmapChanged()
	{
		if (Bitmap == null)
			return;

		EndActiveStrokePreview();
		SyncOriginalFromCommittedBitmap();
		RebuildTilesFromBitmap();
		_compositeDirty = false;
	}

	#endregion

	#region Тайлы

	public bool EnsureTileDocument()
	{
		if (tileDoc != null)
			return true;

		if (!EnsureBitmapLoaded() || Bitmap == null)
			return false;

		RebuildTilesFromBitmap();
		return tileDoc != null;
	}

	public void MarkTilesDirty()
	{
		_compositeDirty = true;
		PixelVersion++;
	}

	public void RebuildBitmapFromTiles()
	{
		if (tileDoc == null)
			return;

		Bitmap?.Dispose();
		Bitmap = tileDoc.ComposeFullBitmap();
		_compositeDirty = false;
	}

	public void InvalidateTilePreview(int tx, int ty)
	{
		tileDoc?.GetOrCreateTile(tx, ty).InvalidatePreview();
		_compositeDirty = true;
		PixelVersion++;
	}

	public void InvalidateAllTilePreviews()
	{
		if (tileDoc == null)
			return;

		for (int ty = 0; ty < tileDoc.TileCountY; ty++)
		{
			for (int tx = 0; tx < tileDoc.TileCountX; tx++)
				tileDoc.GetOrCreateTile(tx, ty).InvalidatePreview();
		}

		_compositeDirty = true;
		PixelVersion++;
	}

	public void EndActiveStrokePreview()
	{
		ActiveStroke?.Dispose();
		ActiveStroke = null;
	}

	private void RebuildTilesFromBitmap()
	{
		if (Bitmap == null)
		{
			tileDoc?.Dispose();
			tileDoc = null;
			_compositeDirty = true;
			PixelVersion++;
			return;
		}

		tileDoc?.Dispose();
		tileDoc = ImageTileDocument.FromBitmap(Bitmap, TileSize);
		_compositeDirty = false;
	}

	private static ImageTileDocument CloneTileDocument(ImageTileDocument source)
	{
		using var composed = source.ComposeFullBitmap();
		return ImageTileDocument.FromBitmap(composed, source.TileSize);
	}

	#endregion

	#region Коррекция изображения

	/// <summary>
	/// Закэшированный SKPaint с фильтрами коррекции.
	/// Пересобирается только при изменении Adjustments.Version, а не каждый кадр.
	/// </summary>
	private SKPaint? GetAdjustmentPaint()
	{
		if (_adjustPaintVersion == Adjustments.Version)
			return _adjustPaint;

		_adjustPaint?.Dispose();
		_adjustPaint = ImageAdjustmentPipeline.BuildPaint(Adjustments);
		_adjustPaintVersion = Adjustments.Version;

		return _adjustPaint;
	}

	/// <summary>
	/// Сбросить кэш фильтров. Дёшево: пересчёт произойдёт лениво при отрисовке,
	/// уже внутри контекста Skia. Никаких readback'ов и загрузок текстур.
	/// </summary>
	public void InvalidateAdjustmentPaint()
	{
		_adjustPaintVersion = -1;
	}

	/// <summary>Совместимость с SkPropertyPanelControl.</summary>
	public void UpdateAdjustmentPreview() => InvalidateAdjustmentPaint();

	/// <summary>
	/// Запекает текущие коррекции в пиксели и сбрасывает Adjustments в identity.
	/// Сброс обязателен: без него настройки остаются в слайдерах и применяются
	/// повторно поверх уже запечённого результата.
	/// </summary>
	public void CommitAdjustments()
	{
		if (Adjustments.IsIdentity)
			return;

		EndActiveStrokePreview();

		using var source = tileDoc?.ComposeFullBitmap() ?? Bitmap?.Copy();
		if (source == null)
			return;

		var info = new SKImageInfo(
			source.Width,
			source.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		var result = new SKBitmap(info);
		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		using (var c = new SKCanvas(result))
		{
			c.Clear(SKColors.Transparent);

			using var paint = ImageAdjustmentPipeline.BuildPaint(Adjustments);

			if (paint != null)
			{
				// SaveLayer, а не paint в DrawBitmap: свёртка и блюр должны
				// применяться к готовому слою, иначе на краях появляются артефакты.
				c.SaveLayer(paint);
				c.DrawBitmap(source, 0f, 0f, sampling);
				c.Restore();
			}
			else
			{
				c.DrawBitmap(source, 0f, 0f, sampling);
			}

			c.Flush();
		}

		Bitmap?.Dispose();
		Bitmap = result;

		tileDoc?.Dispose();
		tileDoc = ImageTileDocument.FromBitmap(Bitmap, TileSize);
		_compositeDirty = false;

		SyncOriginalFromCommittedBitmap();

		Adjustments.Reset();
		InvalidateAdjustmentPaint();
	}

	/// <summary>Совместимость с SkPropertyPanelControl.</summary>
	public void ApplyAdjustmentsGpuCommit() => CommitAdjustments();

	/// <summary>Отбросить коррекции и вернуть эталонные пиксели.</summary>
	public void RevertToOriginal()
	{
		if (OriginalBitmap == null)
			return;

		EndActiveStrokePreview();

		Bitmap?.Dispose();
		Bitmap = OriginalBitmap.Copy();

		RebuildTilesFromBitmap();

		Adjustments.Reset();
		InvalidateAdjustmentPaint();
	}

	#endregion

	#region Отрисовка

	public override void Draw(SKCanvas canvas, float mmPerWorldUnit)
	{
		if (!IsVisible)
			return;

		// Путь строится до загрузки битмапа: контур фигуры существует
		// независимо от пикселей, и заливка с обводкой должны рисоваться даже
		// тогда, когда изображение ещё не загружено или уже выгружено.
		using var path = BuildWorldPath();
		if (path == null || path.IsEmpty)
			return;

		// Порядок слоёв: заливка -> изображение -> обводка.
		// Заливка снизу видна там, где у изображения прозрачные пиксели или
		// где контур выходит за пределы ImageLocalRect. Обводка сверху —
		// иначе её внутренняя половина закрывалась бы пикселями изображения.
		if (Fill.Enabled && IsClosedGeometry())
		{
			using var fillPaint = Fill.CreatePaint(path.Bounds, GetWorldTransform());
			canvas.DrawPath(path, fillPaint);
		}

		DrawImageContent(canvas, path, mmPerWorldUnit);

		if (Stroke.Enabled)
		{
			using var strokePaint = Stroke.CreatePaint(mmPerWorldUnit, IsSelected);
			canvas.DrawPath(path, strokePaint);
			DrawStrokeGlyphs(canvas, mmPerWorldUnit, path, strokePaint);
		}
	}

	/// <summary>
	/// Отрисовка пикселей изображения, обрезанных по контуру фигуры.
	/// </summary>
	private void DrawImageContent(SKCanvas canvas, SKPath worldPath, float mmPerWorldUnit)
	{
		if (!EnsureBitmapLoaded())
			return;

		EnsureOriginalBitmap();

		SKPaint? adjustPaint = GetAdjustmentPaint();

		canvas.Save();
		canvas.ClipPath(worldPath, SKClipOperation.Intersect, true);

		// Коррекция применяется к слою целиком, а не к каждому тайлу отдельно:
		// потайловая свёртка и блюр дали бы видимые швы на границах тайлов.
		bool layered = adjustPaint != null;

		if (layered)
			canvas.SaveLayer(adjustPaint);

		if (ActualResolution && TryGetActualSize(mmPerWorldUnit, out int targetW, out int targetH))
			DrawActualResolutionContent(canvas, targetW, targetH);
		else if (tileDoc != null)
			DrawTilesContent(canvas);
		else if (Bitmap != null)
			DrawBitmapContent(canvas, Bitmap);

		if (layered)
			canvas.Restore();

		canvas.Restore();
	}

	/// <summary>
	/// Сколько пикселей останется у картинки после приведения к её
	/// физическому размеру при разрешении документа.
	///
	/// Размер берётся В МИРОВЫХ координатах, а не по ImageLocalRect.
	/// Изменение размера фигуры не трогает локальный прямоугольник — весь
	/// масштаб оседает в LocalTransform, — поэтому по локальным габаритам
	/// маленькая и большая копия одной картинки давали бы одинаковое число
	/// пикселей. Заодно так корректно учитывается поворот: длины сторон
	/// после преобразования остаются верными.
	///
	/// Возвращает false, если приводить не к чему: когда целевой размер не
	/// меньше исходного, растягивать растр вверх незачем — обычная
	/// отрисовка даст тот же результат и без лишнего промежуточного буфера.
	/// </summary>
	private bool TryGetActualSize(float mmPerWorldUnit, out int targetW, out int targetH)
	{
		targetW = 0;
		targetH = 0;

		int sourceW = tileDoc?.Width ?? Bitmap?.Width ?? 0;
		int sourceH = tileDoc?.Height ?? Bitmap?.Height ?? 0;

		if (sourceW <= 0 || sourceH <= 0)
			return false;

		float dpi = MathF.Max(1f, RenderDpi);

		var rect = ImageLocalRect;
		var world = GetWorldTransform();

		var origin = world.MapPoint(rect.Left, rect.Top);
		var alongX = world.MapPoint(rect.Right, rect.Top);
		var alongY = world.MapPoint(rect.Left, rect.Bottom);

		float widthWorld = Distance(origin, alongX);
		float heightWorld = Distance(origin, alongY);

		float widthMm = widthWorld * mmPerWorldUnit;
		float heightMm = heightWorld * mmPerWorldUnit;

		targetW = (int)MathF.Round(widthMm / 25.4f * dpi);
		targetH = (int)MathF.Round(heightMm / 25.4f * dpi);

		// Один пиксель — нижняя граница: нулевой буфер создать нельзя,
		// а фигура размером с волос всё равно должна что-то показывать.
		targetW = Math.Clamp(targetW, 1, sourceW);
		targetH = Math.Clamp(targetH, 1, sourceH);

		return targetW < sourceW || targetH < sourceH;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		return MathF.Sqrt(dx * dx + dy * dy);
	}

	/// <summary>
	/// Рисует картинку через промежуточный буфер целевого разрешения.
	///
	/// Два прохода принципиальны. Сначала уменьшение с честным усреднением
	/// (Linear плюс мипмапы) — так теряются те же детали, что потеряются
	/// при выводе. Затем увеличение до размера фигуры БЕЗ интерполяции,
	/// чтобы на экране были видны настоящие пиксельные блоки, а не гладкая
	/// картинка. Одним проходом это не сделать: любой одиночный фильтр
	/// либо сгладит результат, либо не усреднит исходник.
	/// </summary>
	private void DrawActualResolutionContent(SKCanvas canvas, int targetW, int targetH)
	{
		var cache = EnsureActualCache(targetW, targetH);

		if (cache == null)
			return;

		float sx = ImageLocalRect.Width / MathF.Max(1f, cache.Width);
		float sy = ImageLocalRect.Height / MathF.Max(1f, cache.Height);

		var cacheToLocal =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(ImageLocalRect.Left, ImageLocalRect.Top),
				SKMatrix.CreateScale(sx, sy));

		var finalMatrix = Geometry.Multiply(GetWorldTransform(), cacheToLocal);

		canvas.Save();
		canvas.Concat(ref finalMatrix);

		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
		canvas.DrawBitmap(cache, 0f, 0f, sampling);

		canvas.Restore();
	}

	private SKBitmap? EnsureActualCache(int targetW, int targetH)
	{
		// Во время мазка содержимое меняется на каждое движение мыши,
		// и версия пикселей не поспевает: буфер пересобирается всегда.
		bool painting = ActiveStroke != null;

		if (!painting &&
			_actualCache != null &&
			_actualCacheW == targetW &&
			_actualCacheH == targetH &&
			_actualCacheVersion == PixelVersion)
		{
			return _actualCache;
		}

		if (_actualCache == null || _actualCache.Width != targetW || _actualCache.Height != targetH)
		{
			_actualCache?.Dispose();

			_actualCache = new SKBitmap(
				targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
		}

		int sourceW = tileDoc?.Width ?? Bitmap?.Width ?? 1;
		int sourceH = tileDoc?.Height ?? Bitmap?.Height ?? 1;

		using (var cacheCanvas = new SKCanvas(_actualCache))
		{
			cacheCanvas.Clear(SKColors.Transparent);

			cacheCanvas.Scale(targetW / (float)sourceW, targetH / (float)sourceH);

			var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

			if (tileDoc != null)
				DrawTilesRaw(cacheCanvas, sampling);
			else if (Bitmap != null)
				cacheCanvas.DrawBitmap(Bitmap, 0f, 0f, sampling);
		}

		_actualCacheW = targetW;
		_actualCacheH = targetH;
		_actualCacheVersion = painting ? -1 : PixelVersion;

		return _actualCache;
	}

	/// <summary>
	/// Выкладывает тайлы в пиксельных координатах растра, без привязки
	/// к фигуре. Превью активного мазка учитывается — иначе в режиме
	/// actual dpi мазок не был бы виден до отпускания кнопки.
	/// </summary>
	private void DrawTilesRaw(SKCanvas canvas, SKSamplingOptions sampling)
	{
		if (tileDoc == null)
			return;

		for (int ty = 0; ty < tileDoc.TileCountY; ty++)
		{
			for (int tx = 0; tx < tileDoc.TileCountX; tx++)
			{
				var tile = tileDoc.GetOrCreateTile(tx, ty);
				var bounds = tileDoc.GetTileBounds(tx, ty);
				var key = new ImageTileKey(tx, ty);

				var dst = SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

				if (ActiveStroke != null &&
					ActiveStroke.PreviewTiles.TryGetValue(key, out var previewTile))
				{
					canvas.DrawBitmap(previewTile, dst, sampling, null);
				}
				else
				{
					canvas.DrawImage(tile.GetPreview(), dst, sampling);
				}
			}
		}
	}

	private void DrawBitmapContent(SKCanvas canvas, SKBitmap bitmap)
	{
		float bmpW = MathF.Max(1f, bitmap.Width);
		float bmpH = MathF.Max(1f, bitmap.Height);

		float sx = ImageLocalRect.Width / bmpW;
		float sy = ImageLocalRect.Height / bmpH;

		var bitmapToLocal =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(ImageLocalRect.Left, ImageLocalRect.Top),
				SKMatrix.CreateScale(sx, sy));

		var finalMatrix = Geometry.Multiply(GetWorldTransform(), bitmapToLocal);

		canvas.Save();
		canvas.Concat(ref finalMatrix);

		var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
		canvas.DrawBitmap(bitmap, 0f, 0f, sampling);

		canvas.Restore();
	}

	private void DrawTilesContent(SKCanvas canvas)
	{
		if (tileDoc == null)
			return;

		float docW = MathF.Max(1f, tileDoc.Width);
		float docH = MathF.Max(1f, tileDoc.Height);

		float sx = ImageLocalRect.Width / docW;
		float sy = ImageLocalRect.Height / docH;

		var imageToLocal =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(ImageLocalRect.Left, ImageLocalRect.Top),
				SKMatrix.CreateScale(sx, sy));

		var finalMatrix = Geometry.Multiply(GetWorldTransform(), imageToLocal);

		canvas.Save();
		canvas.Concat(ref finalMatrix);

		var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

		for (int ty = 0; ty < tileDoc.TileCountY; ty++)
		{
			for (int tx = 0; tx < tileDoc.TileCountX; tx++)
			{
				var tile = tileDoc.GetOrCreateTile(tx, ty);
				var bounds = tileDoc.GetTileBounds(tx, ty);
				var key = new ImageTileKey(tx, ty);

				var dst = SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

				if (ActiveStroke != null &&
					ActiveStroke.PreviewTiles.TryGetValue(key, out var previewTile))
				{
					canvas.DrawBitmap(previewTile, dst, sampling, null);
				}
				else
				{
					canvas.DrawImage(tile.GetPreview(), dst, sampling);
				}
			}
		}

		canvas.Restore();
	}

	#endregion

	#region Попадание мышью

	/// <summary>
	/// Клик внутри контура выбирает изображение независимо от заливки.
	///
	/// Базовая реализация пускает клик внутрь фигуры только при Fill.Enabled:
	/// у обычной фигуры без заливки середина пустая, и сквозь неё видно то,
	/// что лежит ниже. У изображения середина занята пикселями, и заливка к
	/// попаданию отношения не имеет. С выключенной заливкой картинка
	/// переставала ловить клики везде, кроме узкой полосы у самого контура.
	/// </summary>
	public override bool HitTest(SKPoint pagePoint, float toleranceUom, float mmPerUom)
	{
		// Считается в МИРОВЫХ координатах.
		//
		// Прежде путь строился локальным, а точка переводилась в локальные,
		// но допуск подставлялся мировой. Пока картинку не растягивали,
		// локальная единица равна мировой, и ошибка не видна. Масштаб
		// от растягивания ручками живёт в LocalTransform, и тогда полоса
		// попадания вдоль контура оказывалась во столько же раз шире.
		using var path = BuildWorldPath();

		if (path == null || path.IsEmpty)
			return false;

		if (path.Contains(pagePoint.X, pagePoint.Y))
			return true;

		// Контур может быть разомкнут после редактирования узлов — тогда
		// остаётся попадание по линии с той же терпимостью, что и у прочих фигур.
		using var strokePaint = Stroke.CreatePaint(mmPerUom);
		strokePaint.StrokeWidth = MathF.Max(strokePaint.StrokeWidth, toleranceUom);

		return StrokeHit(path, strokePaint, pagePoint);
	}

	#endregion

	#region Фабрика и клонирование

	public static ImageShape FromRectangle(RectangleShape rectShape, string imagePath)
	{
		if (rectShape == null)
			throw new ArgumentNullException(nameof(rectShape));

		var curve = rectShape.ToCurveShape();
		if (curve == null)
			throw new InvalidOperationException("RectangleShape.ToCurveShape() returned null.");

		var shape = new ImageShape
		{
			Name = rectShape.Name,
			Layer = rectShape.Layer,
			Stroke = rectShape.Stroke.Clone(),
			Fill = rectShape.Fill.Clone(),
			IsVisible = rectShape.IsVisible,
			IsLocked = rectShape.IsLocked,
			IsFocused = rectShape.IsFocused,
			LocalTransform = curve.LocalTransform,
			ImageLocalRect = rectShape.LocalRect
		};

		foreach (var node in curve.Nodes)
		{
			// Clone, а не пополевое копирование: так сохраняется идентичность
			// узлов, и привязанные к ним размеры не теряют связь при
			// превращении контура в изображение.
			shape.Nodes.Add(node.Clone());
		}

		shape.LoadImage(imagePath);
		return shape;
	}

	protected override CurveShape CreateCloneInstance() => new ImageShape();

	public override Shape Clone()
	{
		// CurveShape.Clone создаёт экземпляр через CreateCloneInstance, поэтому
		// здесь приходит именно ImageShape. Общие поля кривой — узлы, стиль,
		// трансформация, привязки — уже скопированы там.
		var clone = (ImageShape)base.Clone();

		clone.ImagePath = ImagePath;
		clone.ImageLocalRect = ImageLocalRect;
		clone.TileSize = TileSize;
		clone.ActualResolution = ActualResolution;

		// Временное UI-состояние не копируем.
		clone.ActiveStroke = null;

		// Кэш уменьшенной копии не переносим: он собирается заново по
		// первому запросу, а общий буфер на две фигуры означал бы порчу
		// изображения при правке любой из них.
		clone._actualCache = null;
		clone._actualCacheVersion = -1;

		// Коррекции — независимая копия, иначе слайдеры двигали бы обе фигуры сразу.
		clone.Adjustments = Adjustments.Clone();
		clone._adjustPaint = null;
		clone._adjustPaintVersion = -1;

		// Копируем текущие пиксели, а не перечитываем файл: перечитывание
		// потеряло бы всё, что пользователь нарисовал кистью поверх изображения.
		clone.Bitmap = Bitmap?.Copy();

		if (clone.Bitmap == null && tileDoc != null)
		{
			clone.tileDoc = CloneTileDocument(tileDoc);
			clone.Bitmap = clone.tileDoc.ComposeFullBitmap();
		}
		else if (clone.Bitmap != null)
		{
			clone.tileDoc = ImageTileDocument.FromBitmap(clone.Bitmap, clone.TileSize);
		}
		else
		{
			clone.tileDoc = null;
		}

		clone.OriginalBitmap = OriginalBitmap?.Copy() ?? clone.Bitmap?.Copy();
		clone._compositeDirty = clone.tileDoc == null;

		return clone;
	}

	#endregion

	#region Освобождение ресурсов

	/// <summary>
	/// Освободить все неуправляемые ресурсы фигуры.
	/// Shape не реализует IDisposable, поэтому метод вызывается явно —
	/// при удалении фигуры со страницы и при закрытии документа.
	/// </summary>
	public void DisposeImageResources()
	{
		EndActiveStrokePreview();

		_adjustPaint?.Dispose();
		_adjustPaint = null;
		_adjustPaintVersion = -1;

		tileDoc?.Dispose();
		tileDoc = null;

		Bitmap?.Dispose();
		Bitmap = null;

		OriginalBitmap?.Dispose();
		OriginalBitmap = null;

		_actualCache?.Dispose();
		_actualCache = null;
		_actualCacheVersion = -1;

		_compositeDirty = true;
		PixelVersion++;
	}

	#endregion
}