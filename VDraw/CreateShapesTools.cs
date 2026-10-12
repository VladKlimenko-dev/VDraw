using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;


public static class ShapeFactory
{
	public static Shape? CreateInitial(ShapeToolKind kind, SKPoint pagePoint)
	{
		return kind switch
		{
			ShapeToolKind.Rectangle => CreateRectangle(pagePoint),
			ShapeToolKind.RoundedRectangle => CreateRoundedRectangle(pagePoint),
			ShapeToolKind.ChamferedRectangle => CreateChamferedRectangle(pagePoint),
			ShapeToolKind.Circle2P => CreateCircle2P(pagePoint),
			_ => null
		};
	}

	public static TextShape CreateTextAt(SKPoint pagePoint)
	{
		return new TextShape
		{
			Text = "Text",
			LocalOrigin = SKPoint.Empty,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static RectangleShape CreateRectangle(SKPoint pagePoint)
	{
		return new RectangleShape
		{
			LocalRect = new SKRect(0, 0, 0, 0),
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static RoundedRectangleShape CreateRoundedRectangle(SKPoint pagePoint)
	{
		return new RoundedRectangleShape
		{
			LocalRect = new SKRect(0, 0, 0, 0),
			RadiusX = 5f,
			RadiusY = 5f,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static ChamferedRectangleShape CreateChamferedRectangle(SKPoint pagePoint)
	{
		return new ChamferedRectangleShape
		{
			LocalRect = new SKRect(0, 0, 0, 0),
			ChamferX = 10f,
			ChamferY = 10f,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static Circle2PShape CreateCircle2P(SKPoint pagePoint)
	{
		return new Circle2PShape
		{
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y),
			LocalCenter = SKPoint.Empty,
			Radius = 0f,
			CenterPoint = SKPoint.Empty,
			RadiusPoint = SKPoint.Empty
		};
	}
}

public sealed class CreateShapeOperation : DragOperation
{
	private readonly Shape _shape;
	private readonly ShapeToolKind _toolKind;
	private SKPoint _startPage;

	public CreateShapeOperation(Shape shape, ShapeToolKind toolKind)
	{
		_shape = shape;
		_toolKind = toolKind;
	}

	public override DragOperationKind Kind => DragOperationKind.CreateShape;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_startPage = pagePoint;
		UpdateGeometry(pagePoint);
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		UpdateGeometry(pagePoint);
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		UpdateGeometry(pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		if (_shape.GetBounds().IsEmpty && view.CurrentPage != null && _toolKind != ShapeToolKind.Line)
		{
			view.CurrentPage.RemovePreviewShape(_shape);
			SelectionHelper.Clear(ctx);
			view.RaiseSelectionChanged();
			return;
		}

		// Фигура состоялась — теперь она попадает в историю, одним шагом.
		view.CurrentPage?.CommitPreviewShape(_shape);

		view.SetTool(ShapeToolKind.Select);
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (view.CurrentPage == null)
			return;

		view.CurrentPage.RemovePreviewShape(_shape);
		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();
		view.Invalidate();
	}

	private void UpdateGeometry(SKPoint currentPage)
	{
		switch (_shape)
		{
			case RectangleShape rect:
				UpdateRectangle(rect, currentPage);
				break;

			case RoundedRectangleShape rounded:
				UpdateRoundedRectangle(rounded, currentPage);
				break;

			case ChamferedRectangleShape chamfered:
				UpdateChamferedRectangle(chamfered, currentPage);
				break;

			case Circle2PShape circle:
				UpdateCircle(circle, currentPage);
				break;
		}
	}

	private void UpdateRectangle(RectangleShape shape, SKPoint currentPage)
	{
		var local = new SKPoint(currentPage.X - _startPage.X, currentPage.Y - _startPage.Y);
		var rect = NormalizeRect(SKPoint.Empty, local);

		shape.LocalTransform = SKMatrix.CreateTranslation(_startPage.X + rect.Left, _startPage.Y + rect.Top);
		shape.LocalRect = new SKRect(0, 0, rect.Width, rect.Height);
	}

	private void UpdateRoundedRectangle(RoundedRectangleShape shape, SKPoint currentPage)
	{
		var local = new SKPoint(currentPage.X - _startPage.X, currentPage.Y - _startPage.Y);
		var rect = NormalizeRect(SKPoint.Empty, local);

		shape.LocalTransform = SKMatrix.CreateTranslation(_startPage.X + rect.Left, _startPage.Y + rect.Top);
		shape.LocalRect = new SKRect(0, 0, rect.Width, rect.Height);

		float maxRadius = MathF.Min(shape.LocalRect.Width, shape.LocalRect.Height) * 0.5f;
		shape.RadiusX = MathF.Min(shape.RadiusX, maxRadius);
		shape.RadiusY = MathF.Min(shape.RadiusY, maxRadius);
	}

	private void UpdateChamferedRectangle(ChamferedRectangleShape shape, SKPoint currentPage)
	{
		var local = new SKPoint(currentPage.X - _startPage.X, currentPage.Y - _startPage.Y);
		var rect = NormalizeRect(SKPoint.Empty, local);

		shape.LocalTransform = SKMatrix.CreateTranslation(_startPage.X + rect.Left, _startPage.Y + rect.Top);
		shape.LocalRect = new SKRect(0, 0, rect.Width, rect.Height);

		float maxChamfer = MathF.Min(shape.LocalRect.Width, shape.LocalRect.Height) * 0.5f;
		shape.ChamferX = MathF.Min(shape.ChamferX, maxChamfer);
		shape.ChamferY = MathF.Min(shape.ChamferY, maxChamfer);
	}

	private void UpdateCircle(Circle2PShape shape, SKPoint currentPage)
	{
		var rebuilt = Circle2PShape.CreateFromPoints(_startPage, currentPage);

		shape.LocalTransform = rebuilt.LocalTransform;
		shape.LocalCenter = rebuilt.LocalCenter;
		shape.Radius = rebuilt.Radius;
		shape.CenterPoint = rebuilt.CenterPoint;
		shape.RadiusPoint = rebuilt.RadiusPoint;
	}

	private static SKRect NormalizeRect(SKPoint a, SKPoint b)
	{
		float left = MathF.Min(a.X, b.X);
		float top = MathF.Min(a.Y, b.Y);
		float right = MathF.Max(a.X, b.X);
		float bottom = MathF.Max(a.Y, b.Y);
		return new SKRect(left, top, right, bottom);
	}
}

public sealed class CreateShapeTool : IEditorTool
{
	public ShapeToolKind Kind { get; }

	public CreateShapeTool(ShapeToolKind kind)
	{
		Kind = kind;
	}

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || view.CurrentPage == null)
			return;

		if (Kind == ShapeToolKind.Text)
		{
			var text = ShapeFactory.CreateTextAt(pagePoint);
			view.CurrentPage.AddShape(text);

			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, text);
			view.RaiseSelectionChanged();
			view.CurrentPage.doc.need_save = true;

			view.SetTool(ShapeToolKind.Select);
			return;
		}

		var shape = ShapeFactory.CreateInitial(Kind, pagePoint);
		if (shape == null)
			return;

		// Предпросмотр: шаг истории запишет CreateShapeOperation.End,
		// когда станет ясно, что фигура не вырожденная.
		view.CurrentPage.AddPreviewShape(shape);

		SelectionHelper.Clear(ctx);
		SelectionHelper.SetSingle(ctx, shape);
		view.RaiseSelectionChanged();

		view.StartOperation(new CreateShapeOperation(shape, Kind), pagePoint);
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}
}

public sealed class TwoPointShapeTool : IEditorTool
{
	public ShapeToolKind Kind { get; }

	private bool _isCreating;
	private SKPoint _firstPoint;
	private Shape? _previewShape;

	public TwoPointShapeTool(ShapeToolKind kind)
	{
		if (kind != ShapeToolKind.Line &&
			kind != ShapeToolKind.Circle2P &&
			kind != ShapeToolKind.Rectangle &&
			kind != ShapeToolKind.RoundedRectangle &&
			kind != ShapeToolKind.ChamferedRectangle)
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		Kind = kind;
	}

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || view.CurrentPage == null)
			return;

		if (!_isCreating)
		{
			_isCreating = true;
			_firstPoint = pagePoint;
			_previewShape = CreateInitialShape(pagePoint);

			// До второго щелчка фигуры ещё нет — есть предпросмотр.
			// Шаг истории пишется при завершении, иначе отмена
			// построения оставляла бы в истории пару «добавили и убрали».
			view.CurrentPage.AddPreviewShape(_previewShape);

			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _previewShape);
			view.RaiseSelectionChanged();
			view.Invalidate();
			return;
		}

		if (_previewShape == null)
			return;

		UpdateShapeGeometry(_previewShape, _firstPoint, pagePoint);

		bool isEmpty = IsShapeEmpty(_previewShape);
		if (isEmpty)
		{
			view.CurrentPage.RemovePreviewShape(_previewShape);
			SelectionHelper.Clear(ctx);
			view.RaiseSelectionChanged();
		}
		else
		{
			view.CurrentPage.CommitPreviewShape(_previewShape);
			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _previewShape);
			view.RaiseSelectionChanged();
			view.CurrentPage.doc.need_save = true;
		}

		_previewShape = null;
		_isCreating = false;

		view.SetTool(ShapeToolKind.Select);
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!_isCreating || _previewShape == null)
			return;

		UpdateShapeGeometry(_previewShape, _firstPoint, pagePoint);
		view.Invalidate();
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Escape)
			Cancel(view, ctx);
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (!_isCreating)
			return;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(70, 160, 255, 220)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = new SKColor(255, 255, 255, 240)
		};

		var sp = view.PageToScreen(_firstPoint);
		canvas.DrawCircle(sp, 4f, fill);
		canvas.DrawCircle(sp, 4f, stroke);
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		view.CurrentPage?.RemovePreviewShape(_previewShape);

		_previewShape = null;
		_isCreating = false;

		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		view.SetTool(ShapeToolKind.Select);
	}

	private Shape CreateInitialShape(SKPoint pagePoint)
	{
		return Kind switch
		{
			ShapeToolKind.Circle2P => CreateCircle2P(pagePoint),
			ShapeToolKind.Rectangle => CreateRectangle(pagePoint),
			ShapeToolKind.RoundedRectangle => CreateRoundedRectangle(pagePoint),
			ShapeToolKind.ChamferedRectangle => CreateChamferedRectangle(pagePoint),
			_ => throw new NotSupportedException($"Unsupported tool kind '{Kind}'.")
		};
	}

	private static Circle2PShape CreateCircle2P(SKPoint pagePoint)
	{
		return new Circle2PShape
		{
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y),
			LocalCenter = SKPoint.Empty,
			Radius = 0f,
			CenterPoint = SKPoint.Empty,
			RadiusPoint = SKPoint.Empty
		};
	}

	private static RectangleShape CreateRectangle(SKPoint pagePoint)
	{
		return new RectangleShape
		{
			LocalRect = SKRect.Empty,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static RoundedRectangleShape CreateRoundedRectangle(SKPoint pagePoint)
	{
		return new RoundedRectangleShape
		{
			LocalRect = SKRect.Empty,
			RadiusX = 5f,
			RadiusY = 5f,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static ChamferedRectangleShape CreateChamferedRectangle(SKPoint pagePoint)
	{
		return new ChamferedRectangleShape
		{
			LocalRect = SKRect.Empty,
			ChamferX = 10f,
			ChamferY = 10f,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};
	}

	private static void UpdateShapeGeometry(Shape shape, SKPoint firstPoint, SKPoint secondPoint)
	{
		switch (shape)
		{
			case Circle2PShape circle:
				UpdateCircle(circle, firstPoint, secondPoint);
				break;

			case RectangleShape rectangle:
				UpdateRectangle(rectangle, firstPoint, secondPoint);
				break;

			case RoundedRectangleShape rounded:
				UpdateRoundedRectangle(rounded, firstPoint, secondPoint);
				break;

			case ChamferedRectangleShape chamfered:
				UpdateChamferedRectangle(chamfered, firstPoint, secondPoint);
				break;

			default:
				throw new NotSupportedException($"Unsupported shape type '{shape.GetType().Name}'.");
		}
	}

	private static void UpdateCircle(Circle2PShape shape, SKPoint firstPoint, SKPoint secondPoint)
	{
		var rebuilt = Circle2PShape.CreateFromPoints(firstPoint, secondPoint);

		shape.LocalTransform = rebuilt.LocalTransform;
		shape.LocalCenter = rebuilt.LocalCenter;
		shape.Radius = rebuilt.Radius;
		shape.CenterPoint = rebuilt.CenterPoint;
		shape.RadiusPoint = rebuilt.RadiusPoint;
	}

	private static void UpdateRectangle(RectangleShape shape, SKPoint firstPoint, SKPoint secondPoint)
	{
		var rect = Geometry.NormalizeRect(firstPoint, secondPoint);
		shape.LocalTransform = SKMatrix.CreateTranslation(rect.Left, rect.Top);
		shape.LocalRect = new SKRect(0f, 0f, rect.Width, rect.Height);
	}

	private static void UpdateRoundedRectangle(RoundedRectangleShape shape, SKPoint firstPoint, SKPoint secondPoint)
	{
		var rect = Geometry.NormalizeRect(firstPoint, secondPoint);
		shape.LocalTransform = SKMatrix.CreateTranslation(rect.Left, rect.Top);
		shape.LocalRect = new SKRect(0f, 0f, rect.Width, rect.Height);

		float maxRadius = MathF.Min(rect.Width, rect.Height) * 0.5f;
		shape.RadiusX = MathF.Min(5f, maxRadius);
		shape.RadiusY = MathF.Min(5f, maxRadius);
	}

	private static void UpdateChamferedRectangle(ChamferedRectangleShape shape, SKPoint firstPoint, SKPoint secondPoint)
	{
		var rect = Geometry.NormalizeRect(firstPoint, secondPoint);
		shape.LocalTransform = SKMatrix.CreateTranslation(rect.Left, rect.Top);
		shape.LocalRect = new SKRect(0f, 0f, rect.Width, rect.Height);

		float maxChamfer = MathF.Min(rect.Width, rect.Height) * 0.5f;
		shape.ChamferX = MathF.Min(5f, maxChamfer);
		shape.ChamferY = MathF.Min(5f, maxChamfer);
	}

	private static bool IsShapeEmpty(Shape shape)
	{
		return shape switch
		{
			CurveShape curve => curve.Nodes.Count < 2
				|| Geometry.Distance(curve.Nodes[0].Position, curve.Nodes[1].Position) < 0.0001f,
			_ => shape.GetBounds().IsEmpty
		};
	}
}

public sealed class MultiPointShapeTool : IEditorTool
{
	public ShapeToolKind Kind { get; }

	private readonly List<SKPoint> _points = new();
	private Shape? _previewShape;

	public MultiPointShapeTool(ShapeToolKind kind)
	{
		Kind = kind;
	}

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view.CurrentPage == null)
			return;

		if (Kind == ShapeToolKind.Polygon && button == MouseButtons.Right)
		{
			FinishPolygon(view, ctx);
			return;
		}

		if (Kind == ShapeToolKind.FreePolygon && button == MouseButtons.Right)
		{
			FinishPolygon(view, ctx);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		_points.Add(pagePoint);
		RebuildPreview(view, ctx, pagePoint, finalizeIfReady: true);
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (_points.Count == 0 || view.CurrentPage == null)
			return;

		RebuildPreview(view, ctx, pagePoint, finalizeIfReady: false);
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Escape)
		{
			Cancel(view, ctx);
			return;
		}

		if (keyData == Keys.Enter && Kind == ShapeToolKind.Polygon)
		{
			FinishPolygon(view, ctx);
		}

		if (keyData == Keys.Enter && Kind == ShapeToolKind.FreePolygon)
		{
			FinishPolygon(view, ctx);
		}
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (_points.Count == 0)
			return;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(70, 160, 255, 220)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = new SKColor(255, 255, 255, 240)
		};

		foreach (var p in _points)
		{
			var sp = view.PageToScreen(p);
			canvas.DrawCircle(sp, 4f, fill);
			canvas.DrawCircle(sp, 4f, stroke);
		}
	}

	private void RebuildPreview(MainView view, EditorInteractionContext ctx, SKPoint hoverPoint, bool finalizeIfReady)
	{
		if (view.CurrentPage == null)
			return;

		RemovePreview(view);

		var displayPoints = _points.ToList();
		bool isReady = false;
		_previewShape = null;

		switch (Kind)
		{
			case ShapeToolKind.Curve:
				displayPoints.Add(hoverPoint);
				_previewShape = CreateCurve(displayPoints);
				break;

			case ShapeToolKind.Circle3P:
				if (_points.Count >= 2)
				{
					displayPoints.Add(hoverPoint);
					_previewShape = Circle3PShape.CreateNormalized(
						displayPoints[0],
						displayPoints[1],
						displayPoints[2]);
				}
				isReady = _points.Count >= 3;
				break;

			case ShapeToolKind.Ellipse3P:
				while (displayPoints.Count < 3)
					displayPoints.Add(hoverPoint);

				_previewShape = Ellipse3PShape.CreateFromCenterAndAxes(
					displayPoints[0],
					displayPoints[1],
					displayPoints[2]);

				isReady = _points.Count >= 3;
				break;

			case ShapeToolKind.Ellipse5P:
				{
					while (displayPoints.Count < 5)
						displayPoints.Add(hoverPoint);

					_previewShape = Ellipse5PShape.CreateFromFivePoints(displayPoints.Take(5).ToList());
					isReady = _points.Count >= 5;
					break;
				}

			case ShapeToolKind.Polygon:
				displayPoints.Add(hoverPoint);
				_previewShape = CreatePolygon(displayPoints);
				break;

			case ShapeToolKind.FreePolygon:
				displayPoints.Add(hoverPoint);
				_previewShape = CreatePolygon(displayPoints);
				break;
		}

		if (_previewShape != null)
		{
			// Тихое добавление: этот инструмент СОЗДАЁТ предпросмотр заново
			// на каждое движение мыши — геометрия эллипса по трём и пяти
			// точкам пересчитывается целиком. Через AddShape это давало бы
			// по два шага истории на каждый пиксель курсора.
			view.CurrentPage.AddPreviewShape(_previewShape);
			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _previewShape);
			view.RaiseSelectionChanged();
		}

		view.Invalidate();

		if (finalizeIfReady && isReady)
		{
			if (_previewShape != null)
			{
				// Здесь и только здесь пишется шаг создания.
				view.CurrentPage.CommitPreviewShape(_previewShape);

				SelectionHelper.Clear(ctx);
				SelectionHelper.SetSingle(ctx, _previewShape);
				view.RaiseSelectionChanged();
			}

			view.CurrentPage.doc.need_save = true;

			_points.Clear();
			_previewShape = null;

			view.SetTool(ShapeToolKind.Select);
		}
	}

	private void FinishPolygon(MainView view, EditorInteractionContext ctx)
	{
		if ((Kind != ShapeToolKind.Polygon && Kind != ShapeToolKind.FreePolygon) || view.CurrentPage == null)
			return;

		RemovePreview(view);

		if (_points.Count >= 3)
		{
			var poly = CreatePolygon(_points);
			view.CurrentPage.AddShape(poly);
			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, poly);
			view.CurrentPage.doc.need_save = true;
			view.RaiseSelectionChanged();

			_points.Clear();
			_previewShape = null;

			view.SetTool(ShapeToolKind.Select);
			return;
		}

		_points.Clear();
		_previewShape = null;
		view.Invalidate();
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		RemovePreview(view);

		_points.Clear();
		_previewShape = null;

		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		view.SetTool(ShapeToolKind.Select);
	}

	private void RemovePreview(MainView view)
	{
		view.CurrentPage?.RemovePreviewShape(_previewShape);
	}

	private static CurveShape CreateCurve(IReadOnlyList<SKPoint> worldPoints)
	{
		var origin = worldPoints[0];
		var shape = new CurveShape
		{
			LocalTransform = SKMatrix.CreateTranslation(origin.X, origin.Y)
		};

		foreach (var p in worldPoints)
		{
			shape.Nodes.Add(new CurveNode
			{
				Position = new SKPoint(p.X - origin.X, p.Y - origin.Y),
				Type = CurveNodeType.Straight
			});
		}

		return shape;
	}

	private static PolygonShape CreatePolygon(IReadOnlyList<SKPoint> worldPoints)
	{
		float left = worldPoints.Min(p => p.X);
		float top = worldPoints.Min(p => p.Y);

		var shape = new PolygonShape
		{
			LocalTransform = SKMatrix.CreateTranslation(left, top),
			UseExplicitPoints = true
		};

		foreach (var p in worldPoints)
			shape.Points.Add(new SKPoint(p.X - left, p.Y - top));

		return shape;
	}
}

public sealed class LineTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Line;

	/// <summary>Минимальное расстояние между узлами, ниже которого клик считается повторным.</summary>
	private const float MinNodeDistance = 0.001f;

	private CurveShape? _activeCurve;
	private bool _isCreating;

	/// <summary>Последний узел списка — предпросмотр под курсором, ещё не зафиксирован.</summary>
	private CurveNode? PreviewNode =>
		_activeCurve != null && _activeCurve.Nodes.Count > 0
			? _activeCurve.Nodes[^1]
			: null;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view.CurrentPage == null)
			return;

		if (button == MouseButtons.Right)
		{
			Finish(view, ctx);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		if (!_isCreating || _activeCurve == null)
		{
			StartCurve(view, ctx, pagePoint);
			return;
		}

		var local = ToLocal(pagePoint);

		if (_activeCurve.Nodes.Count >= 2)
		{
			var previousCommitted = _activeCurve.Nodes[^2].Position;

			if (Geometry.Distance(previousCommitted, local) < MinNodeDistance)
				return;
		}

		var preview = PreviewNode;

		if (preview != null)
			preview.Position = local;

		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = local,
			Type = CurveNodeType.Straight
		});

		// ОТЛИЧИЕ ОТ CurveTool: сглаживание не вызывается, узлы остаются прямыми.
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!_isCreating || _activeCurve == null)
			return;

		var preview = PreviewNode;

		if (preview == null)
			return;

		preview.Position = ToLocal(pagePoint);
		view.Invalidate();
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button == MouseButtons.Right)
			Finish(view, ctx);
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Enter)
		{
			Finish(view, ctx);
			return;
		}

		if (keyData == Keys.Escape)
			Cancel(view, ctx);
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}

	private void StartCurve(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_activeCurve = new CurveShape
		{
			Name = "Line",
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};

		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = SKPoint.Empty,
			Type = CurveNodeType.Straight,
			NewContour = true
		});

		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = SKPoint.Empty,
			Type = CurveNodeType.Straight
		});

		view.CurrentPage!.AddPreviewShape(_activeCurve);

		SelectionHelper.Clear(ctx);
		SelectionHelper.SetSingle(ctx, _activeCurve);
		view.RaiseSelectionChanged();

		_isCreating = true;
		view.Invalidate();
	}

	private SKPoint ToLocal(SKPoint pagePoint)
		=> new SKPoint(
			pagePoint.X - _activeCurve!.LocalTransform.TransX,
			pagePoint.Y - _activeCurve.LocalTransform.TransY);

	private void Finish(MainView view, EditorInteractionContext ctx)
	{
		if (!_isCreating || _activeCurve == null || view.CurrentPage == null)
			return;

		// Снимаем незафиксированный узел предпросмотра.
		if (_activeCurve.Nodes.Count > 0)
			_activeCurve.Nodes.RemoveAt(_activeCurve.Nodes.Count - 1);

		for (int i = _activeCurve.Nodes.Count - 1; i > 0; i--)
		{
			if (Geometry.Distance(_activeCurve.Nodes[i - 1].Position, _activeCurve.Nodes[i].Position) < MinNodeDistance)
				_activeCurve.Nodes.RemoveAt(i);
		}

		bool hasGeometry = _activeCurve.Nodes.Count >= 2;

		if (!hasGeometry)
		{
			view.CurrentPage.RemovePreviewShape(_activeCurve);
			SelectionHelper.Clear(ctx);
			view.RaiseSelectionChanged();
		}
		else
		{
			view.CurrentPage.CommitPreviewShape(_activeCurve);

			_activeCurve.Nodes[0].NewContour = true;

			// ОТЛИЧИЕ ОТ CurveTool: сглаживание не вызывается, узлы прямые.
			view.CurrentPage.doc.need_save = true;
		}

		_activeCurve = null;
		_isCreating = false;

		view.Invalidate();
		view.SetTool(ShapeToolKind.Select);

		if (hasGeometry)
			view.SetMode(EditorMode.Curve);
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (!_isCreating || _activeCurve == null || view.CurrentPage == null)
			return;

		view.CurrentPage.RemovePreviewShape(_activeCurve);
		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		_activeCurve = null;
		_isCreating = false;

		view.Invalidate();
		view.SetTool(ShapeToolKind.Select);
	}
}

public sealed class CurveTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Curve;

	/// <summary>Минимальное расстояние между узлами, ниже которого клик считается повторным.</summary>
	private const float MinNodeDistance = 0.001f;

	private CurveShape? _activeCurve;
	private bool _isCreating;

	/// <summary>Последний узел списка — предпросмотр под курсором, ещё не зафиксирован.</summary>
	private CurveNode? PreviewNode =>
		_activeCurve != null && _activeCurve.Nodes.Count > 0
			? _activeCurve.Nodes[^1]
			: null;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view.CurrentPage == null)
			return;

		if (button == MouseButtons.Right)
		{
			Finish(view, ctx);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		if (!_isCreating || _activeCurve == null)
		{
			StartCurve(view, ctx, pagePoint);
			return;
		}

		var local = ToLocal(pagePoint);

		// Клик в ту же точку, что и предыдущий зафиксированный узел — игнорируем,
		// иначе в контуре появится нулевой сегмент.
		if (_activeCurve.Nodes.Count >= 2)
		{
			var previousCommitted = _activeCurve.Nodes[^2].Position;
			if (Geometry.Distance(previousCommitted, local) < MinNodeDistance)
				return;
		}

		// Фиксируем предпросмотр в кликнутой точке и заводим новый предпросмотр.
		var preview = PreviewNode;
		if (preview != null)
			preview.Position = local;

		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = local,
			Type = CurveNodeType.Straight
		});

		CurveAutoSmooth.Apply(_activeCurve);
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!_isCreating || _activeCurve == null)
			return;

		var preview = PreviewNode;
		if (preview == null)
			return;

		preview.Position = ToLocal(pagePoint);

		CurveAutoSmooth.Apply(_activeCurve);
		view.Invalidate();
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button == MouseButtons.Right)
			Finish(view, ctx);
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Enter)
		{
			Finish(view, ctx);
			return;
		}

		if (keyData == Keys.Escape)
			Cancel(view, ctx);
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}

	private void StartCurve(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_activeCurve = new CurveShape
		{
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};

		// Узел 0 — первая точка контура, узел 1 — предпросмотр под курсором.
		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = SKPoint.Empty,
			Type = CurveNodeType.Straight,
			NewContour = true
		});

		_activeCurve.Nodes.Add(new CurveNode
		{
			Position = SKPoint.Empty,
			Type = CurveNodeType.Straight
		});

		view.CurrentPage!.AddPreviewShape(_activeCurve);

		SelectionHelper.Clear(ctx);
		SelectionHelper.SetSingle(ctx, _activeCurve);
		view.RaiseSelectionChanged();

		_isCreating = true;
		view.Invalidate();
	}

	private SKPoint ToLocal(SKPoint pagePoint)
	{
		// LocalTransform на этапе создания — чистый перенос, поэтому обратное
		// преобразование сводится к вычитанию. Если в будущем инструмент начнёт
		// создавать кривую с поворотом, замените на _activeCurve.WorldToLocal(pagePoint).
		return new SKPoint(
			pagePoint.X - _activeCurve!.LocalTransform.TransX,
			pagePoint.Y - _activeCurve.LocalTransform.TransY);
	}

	private void Finish(MainView view, EditorInteractionContext ctx)
	{
		if (!_isCreating || _activeCurve == null || view.CurrentPage == null)
			return;

		// Снимаем незафиксированный узел предпросмотра.
		if (_activeCurve.Nodes.Count > 0)
			_activeCurve.Nodes.RemoveAt(_activeCurve.Nodes.Count - 1);

		// Схлопываем совпавшие соседние узлы (двойные клики в одну точку).
		for (int i = _activeCurve.Nodes.Count - 1; i > 0; i--)
		{
			if (Geometry.Distance(_activeCurve.Nodes[i - 1].Position, _activeCurve.Nodes[i].Position) < MinNodeDistance)
				_activeCurve.Nodes.RemoveAt(i);
		}

		bool hasGeometry = _activeCurve.Nodes.Count >= 2;

		if (!hasGeometry)
		{
			view.CurrentPage.RemovePreviewShape(_activeCurve);
			SelectionHelper.Clear(ctx);
			view.RaiseSelectionChanged();
		}
		else
		{
			view.CurrentPage.CommitPreviewShape(_activeCurve);

			_activeCurve.Nodes[0].NewContour = true;
			CurveAutoSmooth.Apply(_activeCurve);
			view.CurrentPage.doc.need_save = true;
		}

		_activeCurve = null;
		_isCreating = false;

		view.Invalidate();
		view.SetTool(ShapeToolKind.Select);

		// Созданная кривая открывается в режиме узлов: только что расставленные
		// точки чаще всего хочется поправить. Вырожденная кривая удалена и
		// выделения больше нет, поэтому переключаться не на что.
		if (hasGeometry)
			view.SetMode(EditorMode.Curve);
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (!_isCreating || _activeCurve == null || view.CurrentPage == null)
			return;

		view.CurrentPage.RemovePreviewShape(_activeCurve);
		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		_activeCurve = null;
		_isCreating = false;
		view.Invalidate();
		view.SetTool(ShapeToolKind.Select);
	}
}

public static class CurveAutoSmooth
{
	/// <summary>Доля длины сегмента, уходящая в ручку. 1/3 даёт классический Catmull-Rom.</summary>
	public const float DefaultTension = 1f / 3f;

	/// <summary>Сглаживает все контуры фигуры.</summary>
	public static void Apply(CurveShape curve, float tension = DefaultTension)
	{
		if (curve == null || curve.Nodes.Count < 2)
			return;

		int i = 0;
		while (i < curve.Nodes.Count)
		{
			int start = i;
			int end = start;
			bool closed = false;

			// Ищем конец контура.
			while (end < curve.Nodes.Count)
			{
				if (curve.Nodes[end].CloseAfter)
				{
					closed = true;
					break;
				}

				if (curve.Nodes[end].BreakAfter)
					break;

				if (end + 1 < curve.Nodes.Count && curve.Nodes[end + 1].NewContour)
					break;

				end++;
			}

			if (end >= curve.Nodes.Count)
				end = curve.Nodes.Count - 1;

			ApplyToRange(curve, start, end, closed, tension);
			i = end + 1;
		}
	}

	/// <summary>Сглаживает один контур [start..end].</summary>
	public static void ApplyToRange(CurveShape curve, int start, int end, bool closed, float tension = DefaultTension)
	{
		int count = end - start + 1;
		if (count < 2)
			return;

		var nodes = curve.Nodes;

		for (int i = start; i <= end; i++)
		{
			var node = nodes[i];

			int prevIdx = i - 1;
			int nextIdx = i + 1;

			if (prevIdx < start)
				prevIdx = closed ? end : -1;

			if (nextIdx > end)
				nextIdx = closed ? start : -1;

			SKPoint p = node.Position;

			// Направление касательной — хорда между соседями.
			SKPoint before = prevIdx >= 0 ? nodes[prevIdx].Position : p;
			SKPoint after = nextIdx >= 0 ? nodes[nextIdx].Position : p;

			float dirX = after.X - before.X;
			float dirY = after.Y - before.Y;
			float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);

			if (dirLen < 1e-6f)
			{
				// Соседи совпали — касательную задать нечем, оставляем узел прямым.
				node.SetStraight();
				continue;
			}

			dirX /= dirLen;
			dirY /= dirLen;

			float lenPrev = prevIdx >= 0 ? Dist(before, p) * tension : 0f;
			float lenNext = nextIdx >= 0 ? Dist(p, after) * tension : 0f;

			// Для концов разомкнутого контура неиспользуемая ручка кладётся
			// в саму точку — AppendSegment её всё равно не читает.
			node.ControlPrev = new SKPoint(p.X - dirX * lenPrev, p.Y - dirY * lenPrev);
			node.ControlNext = new SKPoint(p.X + dirX * lenNext, p.Y + dirY * lenNext);
			node.Type = CurveNodeType.Tangent;
		}
	}

	private static float Dist(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}
}

public sealed class TextTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Text;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || view.CurrentPage == null)
			return;

		var shape = new TextShape
		{
			Text = "Text",
			LocalOrigin = SKPoint.Empty,
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y)
		};

		view.CurrentPage.AddShape(shape);
		SelectionHelper.Clear(ctx);
		SelectionHelper.SetSingle(ctx, shape);
		view.RaiseSelectionChanged();
		view.CurrentPage.doc.need_save = true;

		view.SetTool(ShapeToolKind.Select);
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint) => view.Cursor = Cursors.Cross;
	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button) { }
	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData) { }
	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx) { }
}

public sealed class ImageTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Image;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left || view.CurrentPage == null)
			return;

		using var ofd = new OpenFileDialog
		{
			Title = "Select image",
			Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All Files|*.*"
		};

		if (ofd.ShowDialog() != DialogResult.OK)
			return;

		float w = 40f;
		float h = 30f;

		using (var probe = SKBitmap.Decode(ofd.FileName))
		{
			if (probe != null && probe.Width > 0 && probe.Height > 0)
			{
				w = 40f;
				h = w * probe.Height / probe.Width;
			}
		}

		var rectShape = new RectangleShape
		{
			Name = "Image",
			LocalTransform = SKMatrix.CreateTranslation(pagePoint.X, pagePoint.Y),
			LocalRect = new SKRect(0f, 0f, w, h)
		};

		var shape = ImageShape.FromRectangle(rectShape, ofd.FileName);

		view.CurrentPage.AddShape(shape);
		SelectionHelper.Clear(ctx);
		SelectionHelper.SetSingle(ctx, shape);
		view.RaiseSelectionChanged();
		view.CurrentPage.doc.need_save = true;
		view.SetTool(ShapeToolKind.Select);
	}
	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint) => view.Cursor = Cursors.Cross;
	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button) { }
	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData) { }
	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx) { }
}

public sealed class RegularPolygonTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Polygon;

	private readonly int _sideCount;
	private bool _hasCenter;
	private SKPoint _center;
	private PolygonShape? _previewShape;

	public RegularPolygonTool(int sideCount = 6)
	{
		_sideCount = Math.Max(3, sideCount);
	}

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view == null)
			return;
		if (view.CurrentPage == null)
			return;

		if (button != MouseButtons.Left || view.CurrentPage == null)
			return;

		if (!_hasCenter)
		{
			_hasCenter = true;
			_center = pagePoint;

			_previewShape = CreateRegularPolygon(_center, pagePoint, _sideCount);
			if (_previewShape == null)
				throw new Exception("RegularPolygonToo.OnMouseDown error: _previewShape = null");

			view.CurrentPage.AddPreviewShape(_previewShape);

			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _previewShape);
			view.RaiseSelectionChanged();
			view.Invalidate();
			return;
		}

		UpdateRegularPolygon(_previewShape, _center, pagePoint, _sideCount);

		// Шаг истории пишется вторым щелчком, когда размер уже выбран.
		view.CurrentPage.CommitPreviewShape(_previewShape);
		view.CurrentPage.doc.need_save = true;

		_previewShape = null;
		_hasCenter = false;

		view.SetTool(ShapeToolKind.Select);
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (view == null)
			return;
		if (view.CurrentPage == null)
			return;

		view.Cursor = Cursors.Cross;

		if (!_hasCenter || _previewShape == null)
			return;

		UpdateRegularPolygon(_previewShape, _center, pagePoint, _sideCount);
		view.Invalidate();
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Escape)
			Cancel(view, ctx);
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (view == null)
			return;
		if (view.CurrentPage == null)
			return;

		view.CurrentPage.RemovePreviewShape(_previewShape);

		_previewShape = null;
		_hasCenter = false;

		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();
		view.Invalidate();
	}

	private static PolygonShape CreateRegularPolygon(SKPoint center, SKPoint radiusPoint, int sideCount)
	{
		var shape = new PolygonShape();
		UpdateRegularPolygon(shape, center, radiusPoint, sideCount);
		return shape;
	}

	private static void UpdateRegularPolygon(PolygonShape shape, SKPoint center, SKPoint radiusPoint, int sideCount)
	{
		float dx = radiusPoint.X - center.X;
		float dy = radiusPoint.Y - center.Y;
		float radius = MathF.Sqrt(dx * dx + dy * dy);
		float startAngle = MathF.Atan2(dy, dx);

		shape.Sides = Math.Max(3, sideCount);
		shape.LocalTransform = SKMatrix.CreateTranslation(center.X, center.Y);
		shape.LocalCenter = SKPoint.Empty;
		shape.RadiusX = MathF.Max(0.0001f, radius);
		shape.RadiusY = MathF.Max(0.0001f, radius);
		shape.RotationRad = startAngle;

		shape.RebuildPoints();
		shape.MarkGeometryInitialized();
	}
}