using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.History;

/// <summary>
/// Записывает в историю добавление и удаление фигур, подписавшись на события
/// страницы. Благодаря этому инструменты создания фигур
/// (CreateShapesTools.cs, TextTool, ImageTool, CurveTool) и удаление из
/// MainForm править не нужно вовсе — достаточно, чтобы они вызывали
/// Page.AddShape / Page.RemoveShape, а не мутировали Page.Shapes напрямую.
/// </summary>
public sealed class PageUndoRecorder
{
	private readonly UndoStack _undo;
	private readonly HashSet<Page> _attached = new();

	public PageUndoRecorder(UndoStack undo)
	{
		_undo = undo;
	}

	public void Attach(Page page)
	{
		if (page == null || !_attached.Add(page))
			return;

		page.ShapeAdded += OnShapeAdded;
		page.ShapeRemoving += OnShapeRemoving;
	}

	public void Detach(Page page)
	{
		if (page == null || !_attached.Remove(page))
			return;

		page.ShapeAdded -= OnShapeAdded;
		page.ShapeRemoving -= OnShapeRemoving;
	}

	public void DetachAll()
	{
		foreach (var page in _attached.ToList())
			Detach(page);
	}

	private void OnShapeAdded(Page page, Shape shape)
	{
		if (_undo.IsApplying)
			return;

		_undo.Push(new AddShapesAction(page, new[] { shape }));
	}

	private void OnShapeRemoving(Page page, Shape shape)
	{
		if (_undo.IsApplying)
			return;

		// Событие приходит ДО удаления, поэтому индекс ещё актуален.
		_undo.Push(RemoveShapesAction.Capture(page, new[] { shape }));
	}
}

/// <summary>
/// Единая точка записи истории для всех DragOperation.
///
/// MainView вызывает Begin / Update / End / Cancel у активной операции в трёх
/// местах (StartOperation, OnMouseMove, OnMouseUp плюс Escape в OnKeyDown).
/// Поэтому историю достаточно снимать вокруг этих вызовов — все двадцать две
/// операции покрываются без единой правки в самих классах операций.
///
/// Что именно снимать, определяется видом операции: трансформация фигур,
/// правка узлов кривой, свойства направляющей или выделение.
/// </summary>
public sealed class OperationUndoRecorder
{
	private readonly UndoStack _undo;

	private IUndoableAction? _pending;
	private Action? _captureAfter;
	private Func<bool>? _hasChanges;

	public OperationUndoRecorder(UndoStack undo)
	{
		_undo = undo;
	}

	public bool IsRecording => _pending != null;

	/// <summary>Вызывать перед DragOperation.Begin.</summary>
	public void BeginOperation(DragOperation op, EditorInteractionContext ctx)
	{
		Reset();

		if (op == null || _undo.IsApplying)
			return;

		switch (op.Kind)
		{
			// --- трансформации фигур -------------------------------------------------
			case DragOperationKind.MoveSelection:
			case DragOperationKind.ResizeSelection:
			case DragOperationKind.RotateSelection:
			case DragOperationKind.MoveFillHandle:
			case DragOperationKind.AdjustRoundedRectangleRadius:
			case DragOperationKind.AdjustChamferedRectangleSize:
			case DragOperationKind.MoveShapePoint:
			case DragOperationKind.AdjustTextAlongShapeAx:
			case DragOperationKind.AdjustTextAlongShapeAy:
				CaptureTransform(OperationName(op.Kind), ctx);
				break;

			// --- тень ----------------------------------------------------------------
			// Отдельно от трансформаций: тень не описывается ни матрицей,
			// ни снимком геометрии, и CaptureTransform её бы не заметил.
			case DragOperationKind.MoveShadowHandle:
			case DragOperationKind.AdjustShadowSoftness:
				CaptureShadow(OperationName(op.Kind), ctx);
				break;

			// --- прозрачность --------------------------------------------------------
			case DragOperationKind.MoveTransparencyHandle:
				CaptureTransparency(OperationName(op.Kind), ctx);
				break;

			// --- инструмент преобразования -------------------------------------------
			case DragOperationKind.MoveTransformHandle:
			case DragOperationKind.AdjustTransformBias:
				CaptureTransformTool(OperationName(op.Kind), ctx);
				break;

			// --- подпись размера -----------------------------------------------------
			// Через снимок геометрии: TextOffset и H входят в
			// MeasurementGeometrySnapshot, поэтому отдельного действия
			// заводить не пришлось.
			case DragOperationKind.MoveMeasurementText:
				CaptureTransform(OperationName(op.Kind), ctx);
				break;

			// --- узлы кривой ---------------------------------------------------------
			case DragOperationKind.MoveCurvePoint:
			case DragOperationKind.MoveCurveControlPoint:
			case DragOperationKind.InsertCurvePoint:
			case DragOperationKind.MoveCurveSegment:
				CaptureCurveNodes(OperationName(op.Kind), ctx);
				break;

			// --- направляющие --------------------------------------------------------
			case DragOperationKind.MoveGuide:
			case DragOperationKind.CreateGuide:
			case DragOperationKind.MoveGuideCenter:
				CaptureGuidePoint(OperationName(op.Kind), ctx);
				break;

			case DragOperationKind.RotateGuide:
				CaptureGuideAngle(ctx);
				break;

			// --- выделение рамкой ----------------------------------------------------
			case DragOperationKind.MarqueeSelect:
				CaptureSelection(ctx);
				break;

			// Панорамирование и перенос центра вращения документ не меняют.
			case DragOperationKind.Pan:
			case DragOperationKind.MoveRotationCenter:
			default:
				break;
		}
	}

	/// <summary>Вызывать после DragOperation.End.</summary>
	public void EndOperation()
	{
		if (_pending == null)
		{
			Reset();
			return;
		}

		_captureAfter?.Invoke();

		bool changed = _hasChanges?.Invoke() ?? true;

		if (changed)
			_undo.Push(_pending);
		else
			_pending.Discard();

		Reset();
	}

	/// <summary>Вызывать после DragOperation.Cancel — записывать нечего.</summary>
	public void CancelOperation()
	{
		_pending?.Discard();
		Reset();
	}

	private void Reset()
	{
		_pending = null;
		_captureAfter = null;
		_hasChanges = null;
	}

	#region Снимки по видам операций

	private void CaptureTransform(string name, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return;

		var action = TransformShapesAction.Capture(name, ctx.Selection);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private void CaptureShadow(string name, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return;

		var action = ShadowEffectAction.Capture(name, ctx.Selection);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private void CaptureTransparency(string name, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return;

		var action = TransparencyEffectAction.Capture(name, ctx.Selection);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private void CaptureTransformTool(string name, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return;

		var action = TransformToolStateAction.Capture(name, ctx.Selection);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private void CaptureCurveNodes(string name, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count != 1 || ctx.Selection[0] is not CurveShape curve)
			return;

		var action = CurveNodesAction.Capture(name, curve);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private void CaptureGuidePoint(string name, EditorInteractionContext ctx)
	{
		var guide = ctx.SelectedGuide;
		if (guide == null)
			return;

		var before = guide.GuidePoint;
		SKPoint after = before;

		// PropertyChangeAction фиксирует конечное значение в конструкторе, а здесь
		// оно известно только к завершению перетаскивания — поэтому DeferredProperty.
		_captureAfter = () => after = guide.GuidePoint;
		_hasChanges = () => Distance(before, after) > 1e-5f;
		_pending = new DeferredProperty<SKPoint>(
			name,
			before,
			() => after,
			p => guide.GuidePoint = p,
			(guide, nameof(guide.GuidePoint)));
	}

	private void CaptureGuideAngle(EditorInteractionContext ctx)
	{
		var guide = ctx.SelectedGuide;
		if (guide == null)
			return;

		float before = guide.AngleDeg;
		float after = before;

		_captureAfter = () => after = guide.AngleDeg;
		_hasChanges = () => MathF.Abs(before - after) > 1e-4f;
		_pending = new DeferredProperty<float>(
			"Rotate guide",
			before,
			() => after,
			v => guide.AngleDeg = v,
			(guide, nameof(guide.AngleDeg)));
	}

	private void CaptureSelection(EditorInteractionContext ctx)
	{
		var action = SelectionChangeAction.Capture(ctx);

		_pending = action;
		_captureAfter = action.CaptureAfter;
		_hasChanges = action.HasChanges;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	private static string OperationName(DragOperationKind kind) => kind switch
	{
		DragOperationKind.MoveSelection => "Move",
		DragOperationKind.ResizeSelection => "Resize",
		DragOperationKind.RotateSelection => "Rotate",
		DragOperationKind.MoveFillHandle => "Move fill handle",
		DragOperationKind.MoveShadowHandle => "Move shadow handle",
		DragOperationKind.AdjustShadowSoftness => "Shadow softness",
		DragOperationKind.MoveTransparencyHandle => "Move transparency handle",
		DragOperationKind.MoveTransformHandle => "Move transform handle",
		DragOperationKind.AdjustTransformBias => "Transform spacing",
		DragOperationKind.AdjustRoundedRectangleRadius => "Corner radius",
		DragOperationKind.AdjustChamferedRectangleSize => "Chamfer size",
		DragOperationKind.MoveShapePoint => "Move point",
		DragOperationKind.AdjustTextAlongShapeAx => "Text offset X",
		DragOperationKind.AdjustTextAlongShapeAy => "Text offset Y",
		DragOperationKind.MoveCurvePoint => "Move node",
		DragOperationKind.MoveCurveControlPoint => "Move control point",
		DragOperationKind.InsertCurvePoint => "Insert node",
		DragOperationKind.MoveCurveSegment => "Move segment",
		DragOperationKind.MoveMeasurementText => "Move dimension text",
		DragOperationKind.MoveGuide => "Move guide",
		DragOperationKind.CreateGuide => "Create guide",
		DragOperationKind.MoveGuideCenter => "Move guide centre",
		DragOperationKind.RotateGuide => "Rotate guide",
		_ => kind.ToString()
	};

	#endregion
}

/// <summary>
/// Изменение свойства, конечное значение которого известно только к моменту
/// завершения операции. Нужен там, где перетаскивание меняет значение
/// непрерывно: направляющая, угол поворота, точка привязки.
/// </summary>
public sealed class DeferredProperty<T> : UndoableActionBase
{
	private readonly string _name;
	private readonly T _before;
	private readonly Func<T> _readAfter;
	private readonly Action<T> _setter;
	private readonly object? _mergeKey;

	private bool _resolved;
	private T? _after;

	public DeferredProperty(string name, T before, Func<T> readAfter, Action<T> setter, object? mergeKey = null)
	{
		_name = name;
		_before = before;
		_readAfter = readAfter;
		_setter = setter;
		_mergeKey = mergeKey;
	}

	public override string Name => _name;
	public override long MemoryFootprint => 256;

	private T After
	{
		get
		{
			if (!_resolved)
			{
				_after = _readAfter();
				_resolved = true;
			}

			return _after!;
		}
	}

	public override void Undo() => _setter(_before);
	public override void Redo() => _setter(After);

	public override bool TryMergeWith(IUndoableAction next)
	{
		if (next is not DeferredProperty<T> other)
			return false;

		if (_mergeKey == null || !Equals(_mergeKey, other._mergeKey))
			return false;

		_after = other.After;
		_resolved = true;
		return true;
	}
}
