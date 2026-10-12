using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Тень или свечение фигуры.
///
/// Геометрия задаётся двумя ручками, как у заливки, но хранится
/// ОТНОСИТЕЛЬНО центра габаритов фигуры, а не в абсолютных координатах
/// страницы. Причина: тень обязана ездить вместе с фигурой. Храни мы
/// мировые точки, их пришлось бы править в MoveByWorld, в каждой операции
/// изменения размера и поворота, в отмене каждой из них — ровно те грабли,
/// на которые уже наступила заливка (см. TransformFillPointsWithWorldTransform).
/// Смещение же не требует сопровождения вовсе.
///
///   CenterOffset — вектор от центра фигуры до P1, центра тени;
///   SizeVector   — вектор от P1 до P2, его ДЛИНА задаёт размер тени.
///
/// Размер выражен как доля полудиагонали габаритов: при |SizeVector|,
/// равной полудиагонали, тень повторяет фигуру один к одному. Поэтому
/// значения по умолчанию ставят P2 в угол габаритного прямоугольника.
/// </summary>
public sealed class ShadowEffect
{
	/// <summary>Ниже этой длины вектор считается вырожденным.</summary>
	private const float MinLength = 0.0001f;

	public bool Enabled { get; set; }

	/// <summary>
	/// Цвет тени. Полупрозрачный чёрный даёт тень, светлый и насыщенный —
	/// свечение: инструмент один, разница только в цвете.
	/// </summary>
	public SKColor Color { get; set; } = new SKColor(0, 0, 0, 150);

	/// <summary>
	/// Мягкость: 0 — точное повторение контуров фигуры, 1 — максимальное
	/// размытие. Управляется ползунком между P1 и P2.
	/// </summary>
	public float Softness { get; set; } = 0.35f;

	/// <summary>Смещение центра тени от центра габаритов фигуры.</summary>
	public SKPoint CenterOffset { get; set; }

	/// <summary>Вектор от P1 до P2. Его длина задаёт размер тени.</summary>
	public SKPoint SizeVector { get; set; }

	/// <summary>
	/// Заданы ли смещения. Ровно та же роль, что у
	/// FillStyle.FillTransformInitialized: пока false, ручки ещё ни разу
	/// не считались и их надо вывести из габаритов.
	/// </summary>
	public bool OffsetsInitialized { get; set; }

	/// <summary>
	/// Доля мягкости, уходящая в радиус размытия. Подобрана так, чтобы при
	/// Softness = 1 тень расплывалась заметно, но не теряла форму совсем.
	/// </summary>
	private const float MaxBlurFactor = 0.25f;

	public static float HalfDiagonal(SKRect bounds)
	{
		float w = bounds.Width;
		float h = bounds.Height;
		float hd = 0.5f * MathF.Sqrt(w * w + h * h);
		return MathF.Max(hd, MinLength);
	}

	/// <summary>
	/// Выводит ручки из габаритов, если их ещё не задавали. Повторные
	/// вызовы безвредны.
	/// </summary>
	public void EnsureDefaults(SKRect bounds)
	{
		if (OffsetsInitialized)
			return;

		float w = bounds.Width;
		float h = bounds.Height;

		// Небольшой сдвиг вправо-вниз: тень, точно совпадающая с фигурой,
		// выглядит как отсутствие тени, и пользователь решил бы, что
		// инструмент не работает.
		CenterOffset = new SKPoint(w * 0.06f, h * 0.06f);

		// Длина ровно в полудиагональ — масштаб 1:1.
		SizeVector = new SKPoint(w * 0.5f, h * 0.5f);

		if (Length(SizeVector) < MinLength)
			SizeVector = new SKPoint(MinLength, 0f);

		OffsetsInitialized = true;
	}

	/// <summary>P1 — центр тени в мировых координатах.</summary>
	public SKPoint GetCenterPoint(SKRect bounds)
	{
		return new SKPoint(
			bounds.MidX + CenterOffset.X,
			bounds.MidY + CenterOffset.Y);
	}

	/// <summary>P2 — конец вектора размера в мировых координатах.</summary>
	public SKPoint GetSizePoint(SKRect bounds)
	{
		var p1 = GetCenterPoint(bounds);
		return new SKPoint(p1.X + SizeVector.X, p1.Y + SizeVector.Y);
	}

	/// <summary>Ставит P1 по мировой точке.</summary>
	public void SetCenterPoint(SKRect bounds, SKPoint world)
	{
		CenterOffset = new SKPoint(world.X - bounds.MidX, world.Y - bounds.MidY);
		OffsetsInitialized = true;
	}

	/// <summary>Ставит P2 по мировой точке, не двигая P1.</summary>
	public void SetSizePoint(SKRect bounds, SKPoint world)
	{
		var p1 = GetCenterPoint(bounds);
		var v = new SKPoint(world.X - p1.X, world.Y - p1.Y);

		// Схлопнутый вектор оставил бы тень нулевого размера и, что хуже,
		// лишил бы ручку направления: вернуть её мышью стало бы невозможно.
		if (Length(v) < MinLength)
			v = new SKPoint(MinLength, 0f);

		SizeVector = v;
		OffsetsInitialized = true;
	}

	/// <summary>Масштаб тени относительно самой фигуры.</summary>
	public float GetScale(SKRect bounds)
	{
		float scale = Length(SizeVector) / HalfDiagonal(bounds);
		return Math.Clamp(scale, 0.01f, 100f);
	}

	/// <summary>Радиус размытия в мировых единицах.</summary>
	public float GetBlurSigma(SKRect bounds)
	{
		if (Softness <= 0.0001f)
			return 0f;

		float span = MathF.Max(bounds.Width, bounds.Height);
		if (span < MinLength)
			span = HalfDiagonal(bounds) * 2f;

		return Math.Clamp(Softness, 0f, 1f) * MaxBlurFactor * span * GetScale(bounds);
	}

	/// <summary>
	/// Габариты самой тени. Нужны отсечению по видимой области: тень
	/// уходит за границы фигуры, и отсекай холст по габаритам фигуры,
	/// тень пропадала бы раньше, чем уезжала за край экрана.
	/// </summary>
	public SKRect GetShadowBounds(SKRect shapeBounds)
	{
		if (!Enabled || shapeBounds.IsEmpty)
			return SKRect.Empty;

		EnsureDefaults(shapeBounds);

		float k = GetScale(shapeBounds);
		var p1 = GetCenterPoint(shapeBounds);

		float hw = shapeBounds.Width * 0.5f * k;
		float hh = shapeBounds.Height * 0.5f * k;

		// Три сигмы покрывают практически всё размытие по Гауссу.
		float pad = GetBlurSigma(shapeBounds) * 3f;

		return new SKRect(
			p1.X - hw - pad,
			p1.Y - hh - pad,
			p1.X + hw + pad,
			p1.Y + hh + pad);
	}

	public ShadowEffect Clone()
	{
		return new ShadowEffect
		{
			Enabled = Enabled,
			Color = Color,
			Softness = Softness,
			CenterOffset = CenterOffset,
			SizeVector = SizeVector,
			OffsetsInitialized = OffsetsInitialized
		};
	}

	/// <summary>Совпадают ли два состояния. Нужно истории: писать шаг без изменений незачем.</summary>
	public bool SameAs(ShadowEffect? other)
	{
		const float eps = 1e-5f;

		return other != null
			&& Enabled == other.Enabled
			&& Color == other.Color
			&& MathF.Abs(Softness - other.Softness) < eps
			&& MathF.Abs(CenterOffset.X - other.CenterOffset.X) < eps
			&& MathF.Abs(CenterOffset.Y - other.CenterOffset.Y) < eps
			&& MathF.Abs(SizeVector.X - other.SizeVector.X) < eps
			&& MathF.Abs(SizeVector.Y - other.SizeVector.Y) < eps;
	}

	/// <summary>Копирует содержимое чужого состояния в это, не меняя ссылку.</summary>
	public void CopyFrom(ShadowEffect source)
	{
		if (source == null)
			return;

		Enabled = source.Enabled;
		Color = source.Color;
		Softness = source.Softness;
		CenterOffset = source.CenterOffset;
		SizeVector = source.SizeVector;
		OffsetsInitialized = source.OffsetsInitialized;
	}

	private static float Length(SKPoint p) => MathF.Sqrt(p.X * p.X + p.Y * p.Y);
}

/// <summary>
/// Ручки инструмента тени на виде выделенной фигуры.
///
/// Устроен по образцу FillAdorner: та же пара круглых ручек, соединённых
/// линией. Добавлена промежуточная полоска-ползунок мягкости — перпендикуляр
/// к вектору P1-P2, который ездит между ними и дальше не уходит.
/// </summary>
public static class ShadowAdorner
{
	private const float HandleRadiusPx = 5f;
	private const float HotHandleRadiusPx = 7f;
	private const float HitRadiusPx = 8f;

	/// <summary>Толщина полоски на экране. Задана условием: 6 пикселей.</summary>
	private const float SliderThicknessPx = 6f;

	/// <summary>Половина длины полоски на экране.</summary>
	private const float SliderHalfLengthPx = 11f;

	public static bool IsApplicable(Shape? shape)
	{
		return shape != null
			&& shape.Shadow != null
			&& shape.Shadow.Enabled
			&& shape.IsVisible
			&& !shape.IsLocked;
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext context)
	{
		if (context.Selection.Count != 1)
			return;

		var shape = context.Selection[0];
		if (!IsApplicable(shape))
			return;

		var bounds = shape.GetBounds();
		if (bounds.IsEmpty)
			return;

		var shadow = shape.Shadow;
		shadow.EnsureDefaults(bounds);

		var p1 = view.PageToScreen(shadow.GetCenterPoint(bounds));
		var p2 = view.PageToScreen(shadow.GetSizePoint(bounds));

		bool startHot = IsHot(context, shape, HitKind.ShadowStartHandle);
		bool endHot = IsHot(context, shape, HitKind.ShadowEndHandle);
		bool sliderHot = IsHot(context, shape, HitKind.ShadowSoftnessHandle);

		using var line = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(40, 40, 40, 220)
		};

		canvas.DrawLine(p1, p2, line);

		DrawSlider(canvas, p1, p2, Math.Clamp(shadow.Softness, 0f, 1f), sliderHot);

		DrawHandle(canvas, p1, startHot);
		DrawHandle(canvas, p2, endHot);
	}

	private static bool IsHot(EditorInteractionContext context, Shape shape, HitKind kind)
	{
		return context.HotHit.Kind == kind && ReferenceEquals(context.HotHit.Shape, shape);
	}

	private static void DrawHandle(SKCanvas canvas, SKPoint centre, bool hot)
	{
		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = hot ? new SKColor(255, 180, 60) : new SKColor(90, 170, 255)
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

	/// <summary>
	/// Полоска мягкости: чёрный перпендикуляр с белой обводкой. Белая
	/// подложка рисуется первой и шире — иначе на тёмной фигуре чёрная
	/// полоска сливалась бы с фоном и ручку нельзя было бы найти.
	/// </summary>
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

	/// <summary>
	/// Концы полоски в экранных координатах для доли t вдоль P1-P2.
	/// Возвращает false для вырожденного вектора: перпендикуляр к точке
	/// не определён.
	/// </summary>
	private static bool TryGetSliderSegment(SKPoint p1, SKPoint p2, float t, out SKPoint a, out SKPoint b)
	{
		a = SKPoint.Empty;
		b = SKPoint.Empty;

		float dx = p2.X - p1.X;
		float dy = p2.Y - p1.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 0.5f)
			return false;

		float ux = dx / len;
		float uy = dy / len;

		// Перпендикуляр к направлению P1-P2.
		float px = -uy;
		float py = ux;

		t = Math.Clamp(t, 0f, 1f);

		var centre = new SKPoint(p1.X + dx * t, p1.Y + dy * t);

		a = new SKPoint(centre.X - px * SliderHalfLengthPx, centre.Y - py * SliderHalfLengthPx);
		b = new SKPoint(centre.X + px * SliderHalfLengthPx, centre.Y + py * SliderHalfLengthPx);
		return true;
	}

	/// <summary>
	/// Что под курсором. Ручки P1 и P2 проверяются раньше полоски: при
	/// мягкости 0 или 1 полоска стоит ровно на ручке, и выиграй она,
	/// сдвинуть саму ручку стало бы невозможно.
	/// </summary>
	public static ShadowHandleKind HitTest(MainView view, Shape shape, SKPoint pagePoint)
	{
		if (!IsApplicable(shape))
			return ShadowHandleKind.None;

		var bounds = shape.GetBounds();
		if (bounds.IsEmpty)
			return ShadowHandleKind.None;

		var shadow = shape.Shadow;
		shadow.EnsureDefaults(bounds);

		var p1 = view.PageToScreen(shadow.GetCenterPoint(bounds));
		var p2 = view.PageToScreen(shadow.GetSizePoint(bounds));
		var mouse = view.PageToScreen(pagePoint);

		if (Geometry.Distance(mouse, p1) <= HitRadiusPx)
			return ShadowHandleKind.Start;

		if (Geometry.Distance(mouse, p2) <= HitRadiusPx)
			return ShadowHandleKind.End;

		if (TryGetSliderSegment(p1, p2, Math.Clamp(shadow.Softness, 0f, 1f), out var a, out var b)
			&& DistanceToSegment(mouse, a, b) <= SliderThicknessPx)
		{
			return ShadowHandleKind.Softness;
		}

		return ShadowHandleKind.None;
	}

	/// <summary>
	/// Доля вдоль P1-P2, соответствующая мировой точке. Именно проекция,
	/// а не расстояние: ползунок обязан ходить по своей оси и не убегать
	/// за курсором вбок.
	/// </summary>
	public static float ProjectToSoftness(SKRect bounds, ShadowEffect shadow, SKPoint pagePoint)
	{
		var p1 = shadow.GetCenterPoint(bounds);
		var p2 = shadow.GetSizePoint(bounds);

		float dx = p2.X - p1.X;
		float dy = p2.Y - p1.Y;
		float lenSq = dx * dx + dy * dy;

		if (lenSq < 1e-8f)
			return Math.Clamp(shadow.Softness, 0f, 1f);

		float t = ((pagePoint.X - p1.X) * dx + (pagePoint.Y - p1.Y) * dy) / lenSq;

		// Условие прямо требует: не дальше P1 и не дальше P2.
		return Math.Clamp(t, 0f, 1f);
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		float lenSq = dx * dx + dy * dy;

		if (lenSq < 1e-6f)
			return Geometry.Distance(p, a);

		float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
		t = Math.Clamp(t, 0f, 1f);

		return Geometry.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
	}
}

/// <summary>Перетаскивание ручки P1 или P2 инструмента тени.</summary>
public sealed class MoveShadowHandleOperation : DragOperation
{
	private readonly Shape _shape;
	private readonly ShadowHandleKind _kind;

	private ShadowEffect? _startState;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveShadowHandle;

	public MoveShadowHandleOperation(Shape shape, ShadowHandleKind kind)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
		_kind = kind;
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (_shape.Shadow == null)
			return;

		// Значения по умолчанию должны быть рассчитаны до того, как мы их запомним.
		_shape.Shadow.EnsureDefaults(_shape.GetBounds());

		_startState = _shape.Shadow.Clone();
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun || _shape.Shadow == null)
			return;

		var bounds = _shape.GetBounds();
		if (bounds.IsEmpty)
			return;

		var shadow = _shape.Shadow;

		switch (_kind)
		{
			case ShadowHandleKind.Start:
				// P1 тащит за собой P2: первая ручка отвечает за положение
				// тени, и менять заодно её размер она не должна.
				shadow.SetCenterPoint(bounds, pagePoint);
				break;

			case ShadowHandleKind.End:
				shadow.SetSizePoint(bounds, pagePoint);
				break;
		}

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		Update(view, context, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext context)
	{
		if (_begun && _startState != null && _shape.Shadow != null)
			_shape.Shadow.CopyFrom(_startState);

		view.Invalidate();
	}
}

/// <summary>Перетаскивание промежуточной полоски: мягкость тени.</summary>
public sealed class AdjustShadowSoftnessOperation : DragOperation
{
	private readonly Shape _shape;

	private ShadowEffect? _startState;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.AdjustShadowSoftness;

	public AdjustShadowSoftnessOperation(Shape shape)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (_shape.Shadow == null)
			return;

		_shape.Shadow.EnsureDefaults(_shape.GetBounds());

		_startState = _shape.Shadow.Clone();
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun || _shape.Shadow == null)
			return;

		var bounds = _shape.GetBounds();
		if (bounds.IsEmpty)
			return;

		_shape.Shadow.Softness = ShadowAdorner.ProjectToSoftness(bounds, _shape.Shadow, pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		// Панель свойств показывает ту же величину слайдером, и её надо
		// подтянуть за холстом.
		view.mainForm?.RebuildPropertyPanel();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		Update(view, context, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext context)
	{
		if (_begun && _startState != null && _shape.Shadow != null)
			_shape.Shadow.CopyFrom(_startState);

		view.mainForm?.RebuildPropertyPanel();
		view.Invalidate();
	}
}

/// <summary>
/// Изменение состояния тени у набора фигур.
///
/// Отдельное действие, а не TransformShapesAction: тень не описывается ни
/// матрицей, ни снимком геометрии, и универсальный шаг вернул бы фигуру
/// на место, оставив тень сдвинутой.
/// </summary>
public sealed class ShadowEffectAction : UndoableActionBase
{
	private readonly struct Entry
	{
		public readonly Shape Shape;
		public readonly ShadowEffect Before;
		public ShadowEffect After { get; init; }

		public Entry(Shape shape)
		{
			Shape = shape;

			// Именно клон: панель и операции правят объект тени на месте,
			// и снимок по ссылке менялся бы вместе с оригиналом.
			Before = shape.Shadow.Clone();
			After = Before;
		}
	}

	private readonly List<Entry> _entries;
	private readonly string _name;

	public override string Name => _name;
	public override long MemoryFootprint => 256 + _entries.Count * 96L;

	/// <summary>Вызывать ДО изменения.</summary>
	public static ShadowEffectAction Capture(string name, IEnumerable<Shape> shapes)
		=> new ShadowEffectAction(name, shapes.Where(s => s?.Shadow != null).Select(s => new Entry(s)).ToList());

	private ShadowEffectAction(string name, List<Entry> entries)
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
			_entries[i] = e with { After = e.Shape.Shadow.Clone() };
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
			e.Shape.Shadow.CopyFrom(e.Before);
	}

	public override void Redo()
	{
		foreach (var e in _entries)
			e.Shape.Shadow.CopyFrom(e.After);
	}
}
