using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Плавная прозрачность фигуры.
///
/// Устроена как заливка: две ручки задают, откуда и докуда идёт переход.
/// Хранятся, как и у тени, смещениями от центра габаритов — тогда ручки
/// ездят вместе с фигурой и их не нужно править в каждой операции
/// перемещения, масштаба и поворота.
///
///   StartOffset  — вектор от центра фигуры до P1;
///   EndOffset    — вектор от центра фигуры до P2;
///   StartOpacity — непрозрачность в точке P1;
///   EndOpacity   — непрозрачность в точке P2.
///
/// За пределами отрезка значения продлеваются постоянными
/// (SKShaderTileMode.Clamp): иначе часть фигуры, лежащая дальше P2,
/// внезапно возвращала бы себе полную непрозрачность.
/// </summary>
public sealed class TransparencyEffect
{
	private const float MinLength = 0.0001f;

	public bool Enabled { get; set; }

	/// <summary>Непрозрачность в P1: 1 — непрозрачно, 0 — полностью прозрачно.</summary>
	public float StartOpacity { get; set; } = 1f;

	/// <summary>Непрозрачность в P2.</summary>
	public float EndOpacity { get; set; }

	public SKPoint StartOffset { get; set; }
	public SKPoint EndOffset { get; set; }

	/// <summary>
	/// Заданы ли смещения. Та же роль, что у
	/// FillStyle.FillTransformInitialized и ShadowEffect.OffsetsInitialized.
	/// </summary>
	public bool OffsetsInitialized { get; set; }

	/// <summary>
	/// Выводит ручки из габаритов, если их ещё не задавали. Значения
	/// повторяют умолчания линейного градиента заливки: слева направо
	/// через всю фигуру — самый ожидаемый вид перехода.
	/// </summary>
	public void EnsureDefaults(SKRect bounds)
	{
		if (OffsetsInitialized)
			return;

		float hw = bounds.Width * 0.5f;

		if (hw < MinLength)
			hw = MathF.Max(bounds.Height * 0.5f, MinLength);

		StartOffset = new SKPoint(-hw, 0f);
		EndOffset = new SKPoint(hw, 0f);

		OffsetsInitialized = true;
	}

	public SKPoint GetStartPoint(SKRect bounds)
		=> new(bounds.MidX + StartOffset.X, bounds.MidY + StartOffset.Y);

	public SKPoint GetEndPoint(SKRect bounds)
		=> new(bounds.MidX + EndOffset.X, bounds.MidY + EndOffset.Y);

	public void SetStartPoint(SKRect bounds, SKPoint world)
	{
		StartOffset = new SKPoint(world.X - bounds.MidX, world.Y - bounds.MidY);
		OffsetsInitialized = true;
	}

	public void SetEndPoint(SKRect bounds, SKPoint world)
	{
		EndOffset = new SKPoint(world.X - bounds.MidX, world.Y - bounds.MidY);
		OffsetsInitialized = true;
	}

	/// <summary>
	/// Маска прозрачности для режима DstIn.
	///
	/// Цвет здесь роли не играет — в DstIn от источника берётся только
	/// альфа. Белый выбран, чтобы отладочная отрисовка маски без блендинга
	/// оставалась читаемой.
	/// </summary>
	public SKShader? CreateMaskShader(SKRect bounds)
	{
		EnsureDefaults(bounds);

		var p1 = GetStartPoint(bounds);
		var p2 = GetEndPoint(bounds);

		byte a1 = (byte)Math.Clamp(MathF.Round(Math.Clamp(StartOpacity, 0f, 1f) * 255f), 0f, 255f);
		byte a2 = (byte)Math.Clamp(MathF.Round(Math.Clamp(EndOpacity, 0f, 1f) * 255f), 0f, 255f);

		// Схлопнутый отрезок градиентом не выразить: Skia вернёт null или
		// мусор. Разводим точки, сохраняя постоянную непрозрачность начала.
		if (Geometry.Distance(p1, p2) < MinLength)
			p2 = new SKPoint(p1.X + MathF.Max(bounds.Width, 1f), p1.Y);

		return SKShader.CreateLinearGradient(
			p1,
			p2,
			new[] { new SKColor(255, 255, 255, a1), new SKColor(255, 255, 255, a2) },
			new[] { 0f, 1f },
			SKShaderTileMode.Clamp);
	}

	public TransparencyEffect Clone()
	{
		return new TransparencyEffect
		{
			Enabled = Enabled,
			StartOpacity = StartOpacity,
			EndOpacity = EndOpacity,
			StartOffset = StartOffset,
			EndOffset = EndOffset,
			OffsetsInitialized = OffsetsInitialized
		};
	}

	public bool SameAs(TransparencyEffect? other)
	{
		const float eps = 1e-5f;

		return other != null
			&& Enabled == other.Enabled
			&& MathF.Abs(StartOpacity - other.StartOpacity) < eps
			&& MathF.Abs(EndOpacity - other.EndOpacity) < eps
			&& MathF.Abs(StartOffset.X - other.StartOffset.X) < eps
			&& MathF.Abs(StartOffset.Y - other.StartOffset.Y) < eps
			&& MathF.Abs(EndOffset.X - other.EndOffset.X) < eps
			&& MathF.Abs(EndOffset.Y - other.EndOffset.Y) < eps;
	}

	public void CopyFrom(TransparencyEffect source)
	{
		if (source == null)
			return;

		Enabled = source.Enabled;
		StartOpacity = source.StartOpacity;
		EndOpacity = source.EndOpacity;
		StartOffset = source.StartOffset;
		EndOffset = source.EndOffset;
		OffsetsInitialized = source.OffsetsInitialized;
	}
}

/// <summary>
/// Ручки инструмента прозрачности. Пара точек без промежуточного
/// ползунка — переход между ними линейный и дополнительного параметра
/// не имеет.
/// </summary>
public static class TransparencyAdorner
{
	private const float HandleRadiusPx = 5f;
	private const float HotHandleRadiusPx = 7f;
	private const float HitRadiusPx = 8f;

	public static bool IsApplicable(Shape? shape)
	{
		return shape != null
			&& shape.Transparency != null
			&& shape.Transparency.Enabled
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

		var tr = shape.Transparency;
		tr.EnsureDefaults(bounds);

		var p1 = view.PageToScreen(tr.GetStartPoint(bounds));
		var p2 = view.PageToScreen(tr.GetEndPoint(bounds));

		bool startHot = IsHot(context, shape, HitKind.TransparencyStartHandle);
		bool endHot = IsHot(context, shape, HitKind.TransparencyEndHandle);

		using var line = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(40, 40, 40, 220),

			// Пунктир отличает ось прозрачности от сплошных осей заливки
			// и тени: при трёх включённых эффектах их иначе не различить.
			PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0f)
		};

		canvas.DrawLine(p1, p2, line);

		DrawHandle(canvas, p1, startHot, shape.Transparency.StartOpacity);
		DrawHandle(canvas, p2, endHot, shape.Transparency.EndOpacity);
	}

	private static bool IsHot(EditorInteractionContext context, Shape shape, HitKind kind)
	{
		return context.HotHit.Kind == kind && ReferenceEquals(context.HotHit.Shape, shape);
	}

	/// <summary>
	/// Ручка залита в меру своей непрозрачности поверх шахматной подложки:
	/// по одному взгляду видно, какой конец прозрачный, а какой нет.
	/// </summary>
	private static void DrawHandle(SKCanvas canvas, SKPoint centre, bool hot, float opacity)
	{
		float r = hot ? HotHandleRadiusPx : HandleRadiusPx;

		using var back = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColors.White
		};

		byte a = (byte)Math.Clamp(MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f), 0f, 255f);

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = hot ? new SKColor(255, 180, 60, a) : new SKColor(60, 200, 160, a)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = hot ? 2.5f : 1.5f,
			Color = SKColors.Black
		};

		canvas.DrawCircle(centre, r, back);
		canvas.DrawCircle(centre, r, fill);
		canvas.DrawCircle(centre, r, stroke);
	}

	public static TransparencyHandleKind HitTest(MainView view, Shape shape, SKPoint pagePoint)
	{
		if (!IsApplicable(shape))
			return TransparencyHandleKind.None;

		var bounds = shape.GetBounds();
		if (bounds.IsEmpty)
			return TransparencyHandleKind.None;

		var tr = shape.Transparency;
		tr.EnsureDefaults(bounds);

		var p1 = view.PageToScreen(tr.GetStartPoint(bounds));
		var p2 = view.PageToScreen(tr.GetEndPoint(bounds));
		var mouse = view.PageToScreen(pagePoint);

		if (Geometry.Distance(mouse, p1) <= HitRadiusPx)
			return TransparencyHandleKind.Start;

		if (Geometry.Distance(mouse, p2) <= HitRadiusPx)
			return TransparencyHandleKind.End;

		return TransparencyHandleKind.None;
	}
}

/// <summary>Перетаскивание ручки P1 или P2 инструмента прозрачности.</summary>
public sealed class MoveTransparencyHandleOperation : DragOperation
{
	private readonly Shape _shape;
	private readonly TransparencyHandleKind _kind;

	private TransparencyEffect? _startState;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveTransparencyHandle;

	public MoveTransparencyHandleOperation(Shape shape, TransparencyHandleKind kind)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
		_kind = kind;
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (_shape.Transparency == null)
			return;

		// Значения по умолчанию должны быть рассчитаны до того, как мы их запомним.
		_shape.Transparency.EnsureDefaults(_shape.GetBounds());

		_startState = _shape.Transparency.Clone();
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun || _shape.Transparency == null)
			return;

		var bounds = _shape.GetBounds();
		if (bounds.IsEmpty)
			return;

		switch (_kind)
		{
			case TransparencyHandleKind.Start:
				_shape.Transparency.SetStartPoint(bounds, pagePoint);
				break;

			case TransparencyHandleKind.End:
				_shape.Transparency.SetEndPoint(bounds, pagePoint);
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
		if (_begun && _startState != null && _shape.Transparency != null)
			_shape.Transparency.CopyFrom(_startState);

		view.Invalidate();
	}
}

/// <summary>
/// Изменение состояния прозрачности у набора фигур. Отдельное действие
/// по той же причине, что и у тени: параметры эффекта не описываются ни
/// матрицей, ни снимком геометрии.
/// </summary>
public sealed class TransparencyEffectAction : UndoableActionBase
{
	private readonly struct Entry
	{
		public readonly Shape Shape;
		public readonly TransparencyEffect Before;
		public TransparencyEffect After { get; init; }

		public Entry(Shape shape)
		{
			Shape = shape;
			Before = shape.Transparency.Clone();
			After = Before;
		}
	}

	private readonly List<Entry> _entries;
	private readonly string _name;

	public override string Name => _name;
	public override long MemoryFootprint => 256 + _entries.Count * 96L;

	/// <summary>Вызывать ДО изменения.</summary>
	public static TransparencyEffectAction Capture(string name, IEnumerable<Shape> shapes)
		=> new TransparencyEffectAction(
			name,
			shapes.Where(s => s?.Transparency != null).Select(s => new Entry(s)).ToList());

	private TransparencyEffectAction(string name, List<Entry> entries)
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
			_entries[i] = e with { After = e.Shape.Transparency.Clone() };
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
			e.Shape.Transparency.CopyFrom(e.Before);
	}

	public override void Redo()
	{
		foreach (var e in _entries)
			e.Shape.Transparency.CopyFrom(e.After);
	}
}
