using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

public sealed class HitResult
{
	public static HitResult None { get; } = new();

	public HitKind Kind { get; init; } = HitKind.None;
	public Shape? Shape { get; init; }
	public HandleKind Handle { get; init; } = HandleKind.None;
	public ShapePropertyHandleKind PropertyHandle { get; init; } = ShapePropertyHandleKind.None;
	public int PointIndex { get; init; } = -1;
	public SKPoint PagePoint { get; init; }
	public bool IsEmpty => Kind == HitKind.None;
	public int CurveNodeIndex { get; init; } = -1;
	public bool CurveControlIsPrev { get; init; }
	public int CurveSegmentIndex { get; init; } = -1;
}

public static class HitTester
{
	public static HitResult? HitTest(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (view == null)
			return HitResult.None;

		// 1. Ручки ВЫДЕЛЕННОЙ направляющей: центр вращения и стрелки поворота.
		//
		// Только ручки. Сама линия направляющей проверяется последней,
		// после корпусов фигур: см. шаг 9.
		var guideHit = HitGuideHandles(view, ctx, pagePoint);
		if (guideHit == null || !guideHit.IsEmpty)
			return guideHit;

		// 2. Режим редактирования кривых
		if (view.IsCurveEditMode)
			return HitCurveEdit(view, ctx, pagePoint);

		// 3. Специальные треугольники для текста вдоль фигуры (ax/ay)
		var textAlongHit = HitTextAlongShapeHandles(view, ctx, pagePoint);
		if (!textAlongHit.IsEmpty)
			return textAlongHit;

		// 4. Хендлы выделения (resize/rotate)
		var selectionHit = HitSelectionHandles(view, ctx, pagePoint);
		if (!selectionHit.IsEmpty)
			return selectionHit;

		// 5. Точки фигур (shape points)
		var pointHit = HitShapePoints(view, ctx, pagePoint);
		if (!pointHit.IsEmpty)
			return pointHit;

		// 6. Хендлы свойств фигур
		var propertyHit = HitShapePropertyHandles(view, ctx, pagePoint);
		if (!propertyHit.IsEmpty)
			return propertyHit;

		// 6c. Подпись размера. Раньше заливки и корпуса: подпись лежит
		// поверх линий фигуры, и щелчок по ней должен тянуть именно её,
		// а не двигать весь размер.
		var measureTextHit = HitMeasurementText(view, ctx, pagePoint);
		if (!measureTextHit.IsEmpty)
			return measureTextHit;

		// 7. Хендлы заливки
		var fillHit = HitFillHandles(view, ctx, pagePoint);
		if (!fillHit.IsEmpty)
			return fillHit;

		// 7a. Хендлы тени. После заливки, а не до: обе пары ручек живут
		// в одном месте экрана, и при включённых сразу двух инструментах
		// порядок должен быть определённым.
		var shadowHit = HitShadowHandles(view, ctx, pagePoint);
		if (!shadowHit.IsEmpty)
			return shadowHit;

		// 7b. Хендлы прозрачности.
		var transparencyHit = HitTransparencyHandles(view, ctx, pagePoint);
		if (!transparencyHit.IsEmpty)
			return transparencyHit;

		// 7c. Хендлы инструмента преобразования.
		var transformHit = HitTransformHandles(view, ctx, pagePoint);
		if (!transformHit.IsEmpty)
			return transformHit;

		// 8. Корпуса фигур (ShapeBody).
		//
		// Узлов и рычагов кривой здесь нет намеренно. Они разбираются
		// в HitCurveEdit — то есть только в режиме узлов, куда управление
		// уходит вторым шагом. Раньше их проверка стояла и тут, перед
		// корпусами, и щелчок по выделенной кривой возвращал CurveSegment —
		// вид попадания, которого в обычном обработчике нет. Управление
		// доходило до ветки default, а та трактует «ничего не задето» как
		// щелчок по пустому месту: снимала выделение и начинала рамку.
		// Отсюда и пропадало выделение при повторном нажатии на кривую,
		// тогда как у прочих фигур, не имеющих адорнера, всё работало.
		var bodyHit = HitShapeBodies(view, pagePoint);
		if (!bodyHit.IsEmpty)
			return bodyHit;

		// 9. Линия направляющей — последней из всего.
		//
		// Прежде она проверялась первой, и направляющая перехватывала
		// щелчок у всего, что оказалось рядом: вершину, лежащую на ней
		// или рядом, выделить было невозможно — выделялась направляющая.
		// А попасть в вершину требуется гораздо чаще: направляющую
		// за тем и ставят, чтобы строить по ней геометрию.
		//
		// Прилипания это не касается: там свой перебор, и приоритет
		// направляющих в нём, наоборот, повышен намеренно.
		return HitGuideBodies(view, pagePoint);
	}

	/// <summary>
	/// Линия направляющей под курсором.
	/// </summary>
	private static HitResult HitGuideBodies(MainView view, SKPoint pagePoint)
	{
		foreach (var shape in view.GetShapesFrontToBack())
		{
			if (shape is not GuideLine guide)
				continue;

			if (!guide.IsVisible || guide.IsLocked)
				continue;

			if (guide.HitTest(pagePoint, view.HitToleranceUom(), view.MmPerPageUnit()))
			{
				return new HitResult
				{
					Kind = HitKind.GuideBody,
					Shape = guide,
					PagePoint = pagePoint
				};
			}
		}

		return HitResult.None;
	}

	/// <summary>
	/// Перебор фигур спереди назад до первого попадания в корпус.
	/// </summary>
	/// <param name="skip">
	/// Фигура, которую пропустить. Нужна режиму узлов: там редактируемая
	/// кривая уже проверена отдельно, вместе с её узлами и рычагами.
	/// </param>
	private static HitResult HitShapeBodies(MainView view, SKPoint pagePoint, Shape? skip = null)
	{
		foreach (var shape in view.GetShapesFrontToBack())
		{
			if (shape is GuideLine)
				continue;
			if (!shape.IsVisible || shape.IsLocked)
				continue;
			if (skip != null && ReferenceEquals(shape, skip))
				continue;

			if (shape.HitTest(pagePoint, view.HitToleranceUom(), view.MmPerPageUnit()))
			{
				return new HitResult
				{
					Kind = HitKind.ShapeBody,
					Shape = shape,
					PagePoint = pagePoint
				};
			}
		}

		return HitResult.None;
	}

	private static HitResult HitTextAlongShapeHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1) return HitResult.None;
		if (ctx.Selection[0] is not TextShape ts) return HitResult.None;
		if (!TextAlongShapeAdorner.IsApplicable(ts)) return HitResult.None;

		var handle = TextAlongShapeAdorner.HitTest(view, ts, pagePoint);
		if (handle == HandleKind.None) return HitResult.None;

		return new HitResult
		{
			Kind = HitKind.TextAlongShapeHandle,
			Shape = ts,
			Handle = handle,
			PagePoint = pagePoint
		};
	}

	/// <summary>
	/// Ручки выделенной направляющей: центр вращения и стрелки поворота.
	///
	/// Линия направляющей сюда не входит — её проверяет HitGuideBodies
	/// в самом конце разбора. Ручки же остаются первыми: они маленькие,
	/// показываются только у выделенной направляющей и ни с чем
	/// не соседствуют.
	/// </summary>
	private static HitResult? HitGuideHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (view == null || view.CurrentPage == null)
			return null;
		float tolMm = view.ScreenPixelsToPageMm(15f) / view.CurrentPage.MmPerUom;

		if (ctx.SelectedGuide != null)
		{
			var center = ctx.SelectedGuide.GetRotationCenterPage();
			if (Geometry.Distance(center, pagePoint) <= tolMm)
			{
				return new HitResult
				{
					Kind = HitKind.GuideCenterHandle,
					Shape = ctx.SelectedGuide,
					PagePoint = pagePoint
				};
			}

			if (ctx.SelectedGuide.TransformMode == GuideTransformMode.Rotate)
			{
				var handles = GuideAdorner.GetRotationHandlesPage(view, ctx.SelectedGuide);

				if (Geometry.Distance(handles.ccw, pagePoint) <= tolMm * 2f)
				{
					return new HitResult
					{
						Kind = HitKind.GuideRotateHandleCCW,
						Shape = ctx.SelectedGuide,
						PagePoint = pagePoint
					};
				}

				if (Geometry.Distance(handles.cw, pagePoint) <= tolMm)
				{
					return new HitResult
					{
						Kind = HitKind.GuideRotateHandleCW,
						Shape = ctx.SelectedGuide,
						PagePoint = pagePoint
					};
				}
			}
		}

		return HitResult.None;
	}

	private static HitResult HitSelectionHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count == 0)
			return HitResult.None;

		var bounds = SelectionHelper.GetBounds(ctx);

		float tolUom = view.ScreenPixelsToPageMm(Settings.handle_size) / view.CurrentPage!.MmPerUom;
		float offset = view.ScreenPixelsToPageMm(Settings.handle_size) / view.CurrentPage!.MmPerUom;

		if (ctx.TransformMode == SelectionTransformMode.Rotate)
		{
			if (Geometry.Distance(ctx.RotationPivot, pagePoint) <= tolUom)
			{
				return new HitResult
				{
					Kind = HitKind.RotationPivot,
					PagePoint = pagePoint
				};
			}
		}

		var modeHandle = SelectionAdorner.GetModeToggleHandle(bounds);
		if (Geometry.Distance(modeHandle, pagePoint) <= tolUom)
		{
			return new HitResult
			{
				Kind = HitKind.SelectionHandle,
				Handle = HandleKind.RotationCenter,
				PagePoint = pagePoint
			};
		}

		var handles = ctx.TransformMode == SelectionTransformMode.Resize
			? SelectionAdorner.GetResizeHandles(bounds, offset)
			: SelectionAdorner.GetRotateHandles(bounds, offset);

		foreach (var h in handles)
		{
			if (Geometry.Distance(h.Position, pagePoint) <= tolUom)
			{
				return new HitResult
				{
					Kind = ctx.TransformMode == SelectionTransformMode.Resize
						? HitKind.SelectionHandle
						: HitKind.RotationHandle,
					Handle = h.Kind,
					PagePoint = pagePoint
				};
			}
		}

		return HitResult.None;
	}

	private static HitResult HitShapePropertyHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return HitResult.None;

		float tolMm = view.ScreenPixelsToPageMm(8f) / view.CurrentPage.MmPerUom;

		foreach (var h in ShapePropertyAdorner.GetHandles(shape))
		{
			if (Geometry.Distance(h.WorldPosition, pagePoint) <= tolMm)
			{
				return new HitResult
				{
					Kind = HitKind.ShapePropertyHandle,
					Shape = shape,
					PropertyHandle = h.Kind,
					PagePoint = pagePoint
				};
			}
		}

		return HitResult.None;
	}

	private static HitResult HitShapePoints(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return HitResult.None;

		float tolMm = view.ScreenPixelsToPageMm(8f) / view.CurrentPage.MmPerUom;

		foreach (var p in ShapePointAdorner.GetPoints(shape))
		{
			if (Geometry.Distance(p.WorldPosition, pagePoint) <= tolMm)
			{
				return new HitResult
				{
					Kind = HitKind.ShapePoint,
					Shape = p.Shape,
					PointIndex = p.PointIndex,
					PagePoint = pagePoint
				};
			}
		}

		return HitResult.None;
	}

	private static HitResult HitFillHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return HitResult.None;

		var fillHandle = FillAdorner.HitTest(view, shape, pagePoint);

		return fillHandle switch
		{
			FillHandleKind.Start => new HitResult
			{
				Kind = HitKind.FillStartHandle,
				Shape = shape,
				PagePoint = shape.Fill.StartPoint
			},

			FillHandleKind.End => new HitResult
			{
				Kind = HitKind.FillEndHandle,
				Shape = shape,
				PagePoint = shape.Fill.EndPoint
			},

			_ => HitResult.None
		};
	}

	private static HitResult HitMeasurementText(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var selected = ctx.Selection[0];

		if (!selected.IsVisible || selected.IsLocked)
			return HitResult.None;

		// Допуск в экранных пикселях: у мелкой подписи попасть точно
		// в глифы иначе почти невозможно.
		float tolerance = view.ScreenPixelsToPageUnits(3f);

		// Обе разновидности размера ведут себя одинаково, поэтому и вид
		// попадания у них общий: разводятся они уже в диспетчере операций,
		// где у каждой своя операция перетаскивания.
		SKPoint center;

		switch (selected)
		{
			case MeasurementShape measurement:
				if (!measurement.HitTestText(pagePoint, tolerance))
					return HitResult.None;

				measurement.TryGetTextCenterWorld(out center);
				break;

			case AngleMeasurementShape angle:
				if (!angle.HitTestText(pagePoint, tolerance))
					return HitResult.None;

				angle.TryGetTextCenterWorld(out center);
				break;

			default:
				return HitResult.None;
		}

		return new HitResult
		{
			Kind = HitKind.MeasurementText,
			Shape = selected,
			PagePoint = center
		};
	}

	private static HitResult HitShadowHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return HitResult.None;

		if (!ShadowAdorner.IsApplicable(shape))
			return HitResult.None;

		var handle = ShadowAdorner.HitTest(view, shape, pagePoint);
		if (handle == ShadowHandleKind.None)
			return HitResult.None;

		var bounds = shape.GetBounds();

		return handle switch
		{
			ShadowHandleKind.Start => new HitResult
			{
				Kind = HitKind.ShadowStartHandle,
				Shape = shape,
				PagePoint = shape.Shadow.GetCenterPoint(bounds)
			},

			ShadowHandleKind.End => new HitResult
			{
				Kind = HitKind.ShadowEndHandle,
				Shape = shape,
				PagePoint = shape.Shadow.GetSizePoint(bounds)
			},

			ShadowHandleKind.Softness => new HitResult
			{
				Kind = HitKind.ShadowSoftnessHandle,
				Shape = shape,
				PagePoint = pagePoint
			},

			_ => HitResult.None
		};
	}

	private static HitResult HitTransparencyHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return HitResult.None;

		if (!TransparencyAdorner.IsApplicable(shape))
			return HitResult.None;

		var handle = TransparencyAdorner.HitTest(view, shape, pagePoint);
		if (handle == TransparencyHandleKind.None)
			return HitResult.None;

		var bounds = shape.GetBounds();

		return handle switch
		{
			TransparencyHandleKind.Start => new HitResult
			{
				Kind = HitKind.TransparencyStartHandle,
				Shape = shape,
				PagePoint = shape.Transparency.GetStartPoint(bounds)
			},

			TransparencyHandleKind.End => new HitResult
			{
				Kind = HitKind.TransparencyEndHandle,
				Shape = shape,
				PagePoint = shape.Transparency.GetEndPoint(bounds)
			},

			_ => HitResult.None
		};
	}

	private static HitResult HitTransformHandles(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitResult.None;

		if (ctx.Selection[0] is not TransformShape tool)
			return HitResult.None;

		if (!TransformAdorner.IsApplicable(tool))
			return HitResult.None;

		var handle = TransformAdorner.HitTest(view, tool, pagePoint);
		if (handle == TransformHandleKind.None)
			return HitResult.None;

		return handle switch
		{
			TransformHandleKind.Start => new HitResult
			{
				Kind = HitKind.TransformStartHandle,
				Shape = tool,
				PagePoint = tool.LocalToWorld(tool.P1)
			},

			TransformHandleKind.End => new HitResult
			{
				Kind = HitKind.TransformEndHandle,
				Shape = tool,
				PagePoint = tool.LocalToWorld(tool.P2)
			},

			TransformHandleKind.Bias => new HitResult
			{
				Kind = HitKind.TransformBiasHandle,
				Shape = tool,
				PagePoint = pagePoint
			},

			_ => HitResult.None
		};
	}

	private static HitResult HitCurveEdit(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.Selection.Count != 1)
			return HitShapeBodies(view, pagePoint);

		if (ctx.Selection[0] is not CurveShape curve)
			return HitShapeBodies(view, pagePoint);

		if (!curve.IsVisible || curve.IsLocked)
			return HitShapeBodies(view, pagePoint);

		var curveHit = CurveAdorner.HitTest(view, curve, pagePoint);
		if (!curveHit.IsEmpty)
			return curveHit;

		if (curve.HitTest(pagePoint, view.HitToleranceUom(), view.MmPerPageUnit()))
		{
			return new HitResult
			{
				Kind = HitKind.ShapeBody,
				Shape = curve,
				PagePoint = pagePoint
			};
		}

		// Клик мимо редактируемой кривой не должен пропадать. SelectTool
		// рассчитывает, что обычное выделение работает в любом режиме, но без
		// этого перебора режим узлов не выдавал ShapeBody ни для одной другой
		// фигуры: клик по соседней картинке возвращал None, только сбрасывал
		// выделение и выходил из режима, и выбрать её удавалось лишь со второго раза.
		return HitShapeBodies(view, pagePoint, curve);
	}
}

public sealed class EditorInteractionContext
{
	public ShapeToolKind CurrentTool { get; set; } = ShapeToolKind.Select;
	public IEditorTool? ToolHandler { get; set; }
	public DragOperation? ActiveOperation { get; set; }

	public SKPoint MouseDownPage { get; set; }
	public SKPoint LastPage { get; set; }
	public SKPoint CurrentPage { get; set; }

	public HitResult MouseDownHit { get; set; } = HitResult.None;
	public HitResult HotHit { get; set; } = HitResult.None;

	public List<Shape> Selection { get; } = new();

	public bool CtrlPressed { get; set; }
	public bool AltPressed { get; set; }
	public bool ShiftPressed { get; set; }
	public List<Shape> SelectionSnapshot { get; } = new();

	public SelectionTransformMode TransformMode { get; set; } = SelectionTransformMode.Resize;
	public SKPoint RotationPivot { get; set; } = SKPoint.Empty;
	public bool HasCustomRotationPivot { get; set; }

	public bool PendingSelectionCycle { get; set; }
	public SKPoint PendingSelectionCyclePoint { get; set; }
	public Shape? PendingSelectionCycleStartShape { get; set; }

	/// <summary>
	/// Узел, выбор которого переключится при отпускании кнопки, если
	/// протяжки не было. null — ничего не отложено.
	///
	/// Отложено потому, что Ctrl занят сразу двумя смыслами: добавить узел
	/// к выбранным и ограничить перемещение одной осью. На нажатии их
	/// не различить — неизвестно, потянут мышь или нет. На отпускании
	/// различие уже видно: сдвинулась мышь — было перемещение, не
	/// сдвинулась — щелчок.
	/// </summary>
	public CurveShape? PendingNodeToggleCurve { get; set; }

	public int PendingNodeToggleIndex { get; set; } = -1;

	public SKPoint PendingNodeTogglePoint { get; set; }

	public SKPoint ActiveSelectionCyclePoint { get; set; }
	public List<Shape> ActiveSelectionCycleHits { get; } = new();
	public int ActiveSelectionCycleIndex { get; set; } = -1;

	public bool DeferredMoveCandidate { get; set; }
	public Shape? DeferredMoveShape { get; set; }

	public bool IsDragging => ActiveOperation != null;

	public bool PendingCenterHandleToggle { get; set; }
	public SKPoint PendingCenterHandlePoint { get; set; }

	public GuideLine? SelectedGuide { get; set; }
	public bool PendingGuideCenterToggle { get; set; }
	public SKPoint PendingGuideCenterTogglePoint { get; set; }
	public bool DeferredGuideMoveCandidate { get; set; }
	public bool DeferredGuideCenterMoveCandidate { get; set; }

	public SnapResult? ActiveSnap { get; set; }

	/// <summary>
	/// Узел под курсором в режиме фигур: его владелец и мировая точка.
	///
	/// Хранится в контексте, а не в MainView, потому что им пользуются
	/// и отрисовка подсветки, и начало перетаскивания — а они живут
	/// в разных классах.
	/// </summary>
	public Shape? HoveredNodeShape { get; set; }

	public SKPoint? HoveredNodeWorld { get; set; }

	/// <summary>
	/// Узел, подсвеченный в момент НАЖАТИЯ.
	///
	/// Отдельно от текущего потому, что перетаскивание начинается не сразу:
	/// сначала курсор должен уйти на несколько пикселей от точки нажатия.
	/// К этому моменту подсвеченным может стать уже соседний узел, и точкой
	/// отсчёта стал бы не тот, за который взялись.
	/// </summary>
	public Shape? PressedNodeShape { get; set; }

	public SKPoint? PressedNodeWorld { get; set; }
}

public static class BehindSelectionHelper
{
	public static Shape? FindNextBehind(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, Shape selectedShape)
	{
		if (view.CurrentPage == null)
			return null;

		var hits = view.GetShapesFrontToBack()
			.Where(s => s.IsVisible && !s.IsLocked)
			.Where(s => s.HitTest(pagePoint, view.HitToleranceUom(), view.MmPerPageUnit()))
			.ToList();

		if (hits.Count == 0)
			return null;

		int index = hits.FindIndex(s => ReferenceEquals(s, selectedShape));
		if (index < 0)
			return null;

		if (index + 1 >= hits.Count)
			return null;

		return hits[index + 1];
	}
}

public interface IEditorTool
{
	ShapeToolKind Kind { get; }

	void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button);
	void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint);
	void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button);
	void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData);
	void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx);
}

public abstract class DragOperation
{
	protected readonly Dictionary<Shape, (SKPoint Start, SKPoint End)> _originalFill = new();
	public abstract DragOperationKind Kind { get; }

	public virtual void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint) { }
	public virtual void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint) { }
	public virtual void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint) { }
	public virtual void Cancel(MainView view, EditorInteractionContext ctx) { }

	#region Ограничение движения по оси

	/// <summary>По какой оси разрешено движение при зажатом Ctrl.</summary>
	private enum AxisLockKind
	{
		None,
		Horizontal,
		Vertical
	}

	private AxisLockKind _axisLock = AxisLockKind.None;

	/// <summary>Сбрасывает ось. Вызывать в Begin.</summary>
	protected void ResetAxisLock()
	{
		_axisLock = AxisLockKind.None;
	}

	/// <summary>
	/// Прижимает точку к оси, выбранной первым движением при зажатом Ctrl.
	///
	/// Ось определяется ОДИН раз — по тому, в какую сторону пользователь
	/// повёл дальше. Пересчитывать её на каждом кадре нельзя: у диагонального
	/// движения преобладающая сторона меняется по дороге, и точка прыгала бы
	/// между горизонталью и вертикалью. Выбранная ось держится до отпускания
	/// Ctrl.
	///
	/// Порог в несколько экранных точек, а не ноль: в первые мгновения
	/// протяжки обе составляющие сравнимы с дрожанием руки, и ось выбиралась
	/// бы случайно.
	/// </summary>
	protected SKPoint ApplyAxisLock(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint start,
		SKPoint current)
	{
		if (!ctx.CtrlPressed)
		{
			// Отпустили Ctrl — ось забывается, и следующее нажатие
			// выбирает её заново по текущему направлению.
			_axisLock = AxisLockKind.None;
			return current;
		}

		float dx = current.X - start.X;
		float dy = current.Y - start.Y;

		if (_axisLock == AxisLockKind.None)
		{
			float threshold = view.ScreenPixelsToPageMm(4f) / MathF.Max(view.MmPerPageUnit(), 1e-6f);

			if (MathF.Abs(dx) < threshold && MathF.Abs(dy) < threshold)
				return current;

			_axisLock = MathF.Abs(dx) >= MathF.Abs(dy)
				? AxisLockKind.Horizontal
				: AxisLockKind.Vertical;
		}

		return _axisLock == AxisLockKind.Horizontal
			? new SKPoint(current.X, start.Y)
			: new SKPoint(start.X, current.Y);
	}

	#endregion

	protected static void RestoreOriginalWorldTransform(Shape shape, SKMatrix originalWorld)
	{
		var parentWorld = shape.Parent?.GetWorldTransform() ?? SKMatrix.CreateIdentity();
		if (!parentWorld.TryInvert(out var parentInv))
			parentInv = SKMatrix.CreateIdentity();

		shape.LocalTransform = Geometry.Multiply(parentInv, originalWorld);
	}

	/// <summary>
	/// Запоминает концы заливки у фигуры И У ВСЕХ ЕЁ ДЕТЕЙ.
	///
	/// Дети группы в выделение не входят, а концы градиента у них заданы
	/// в мировых координатах и матрице группы не подчиняются. Без обхода
	/// вглубь заливка при перетаскивании группы оставалась на месте,
	/// пока сама группа уезжала.
	/// </summary>
	protected static void CaptureFillPointsDeep(
		Shape shape,
		Dictionary<Shape, (SKPoint Start, SKPoint End)> originalFill)
	{
		if (shape.Fill != null)
			originalFill[shape] = (shape.Fill.StartPoint, shape.Fill.EndPoint);

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				CaptureFillPointsDeep(child, originalFill);
		}
	}

	protected static void RestoreOriginalFillPointsDeep(
		Shape shape,
		Dictionary<Shape, (SKPoint Start, SKPoint End)> originalFill)
	{
		RestoreOriginalFillPoints(shape, originalFill);

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				RestoreOriginalFillPointsDeep(child, originalFill);
		}
	}

	protected static void TransformFillPointsDeep(
		Shape shape,
		Dictionary<Shape, (SKPoint Start, SKPoint End)> originalFill,
		SKMatrix worldTransform,
		DragOperationKind operationKind)
	{
		TransformFillPointsWithWorldTransform(shape, originalFill, worldTransform, operationKind);

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				TransformFillPointsDeep(child, originalFill, worldTransform, operationKind);
		}
	}

	protected static void RestoreOriginalFillPoints(Shape shape, Dictionary<Shape, (SKPoint Start, SKPoint End)> originalFill)
	{
		if (!originalFill.TryGetValue(shape, out var fp))
			return;

		shape.Fill.StartPoint = fp.Start;
		shape.Fill.EndPoint = fp.End;
	}

	protected static void TransformFillPointsWithWorldTransform(
		Shape shape,
		Dictionary<Shape, (SKPoint Start, SKPoint End)> originalFill,
		SKMatrix worldTransform,
		DragOperationKind operationKind)
	{
		if (shape.Fill == null || !shape.Fill.Enabled || !shape.Fill.SupportsInteractiveHandles)
			return;

		if (!originalFill.TryGetValue(shape, out var fp))
			return;

		bool applyTransform = operationKind switch
		{
			DragOperationKind.MoveSelection => true,
			DragOperationKind.MoveFillHandle => false,
			DragOperationKind.ResizeSelection => shape.Fill.ScaleWithShape,
			DragOperationKind.RotateSelection => shape.Fill.RotateWithShape,
			_ => true
		};

		if (!applyTransform)
		{
			shape.Fill.StartPoint = fp.Start;
			shape.Fill.EndPoint = fp.End;
			shape.Fill.FillTransformInitialized = true;
			return;
		}

		shape.Fill.StartPoint = worldTransform.MapPoint(fp.Start);
		shape.Fill.EndPoint = worldTransform.MapPoint(fp.End);
		shape.Fill.FillTransformInitialized = true;
	}
}

public static class SelectionHelper
{
	public static void Clear(EditorInteractionContext ctx)
	{
		foreach (var s in ctx.Selection)
			s.IsSelected = false;

		ctx.Selection.Clear();

		if (ctx.SelectedGuide != null)
			ctx.SelectedGuide.IsSelected = false;

		ctx.SelectedGuide = null;
		ctx.PendingGuideCenterToggle = false;
		ctx.PendingGuideCenterTogglePoint = SKPoint.Empty;
		ctx.DeferredGuideMoveCandidate = false;

		ctx.TransformMode = SelectionTransformMode.Resize;
		ctx.HasCustomRotationPivot = false;
		ctx.RotationPivot = SKPoint.Empty;
	}

	public static void SetSingle(EditorInteractionContext ctx, Shape shape)
	{
		Clear(ctx);
		ctx.Selection.Add(shape);
		shape.IsSelected = true;
		ResetPivotToSelectionCenter(ctx);
	}

	public static void Add(EditorInteractionContext ctx, Shape shape)
	{
		if (ctx.Selection.Contains(shape))
			return;

		ctx.Selection.Add(shape);
		shape.IsSelected = true;

		if (!ctx.HasCustomRotationPivot)
			ResetPivotToSelectionCenter(ctx);
	}

	public static void Remove(EditorInteractionContext ctx, Shape shape)
	{
		if (ctx.Selection.Remove(shape))
			shape.IsSelected = false;

		if (ctx.Selection.Count == 0)
		{
			ctx.TransformMode = SelectionTransformMode.Resize;
			ctx.HasCustomRotationPivot = false;
			ctx.RotationPivot = SKPoint.Empty;
		}
		else if (!ctx.HasCustomRotationPivot)
		{
			ResetPivotToSelectionCenter(ctx);
		}
	}

	public static void Toggle(EditorInteractionContext ctx, Shape shape)
	{
		if (ctx.Selection.Contains(shape))
			Remove(ctx, shape);
		else
			Add(ctx, shape);
	}

	public static bool Contains(EditorInteractionContext ctx, Shape shape)
	{
		return ctx.Selection.Contains(shape);
	}

	public static void RestoreSnapshot(EditorInteractionContext ctx)
	{
		foreach (var s in ctx.Selection)
			s.IsSelected = false;

		ctx.Selection.Clear();

		foreach (var s in ctx.SelectionSnapshot)
		{
			ctx.Selection.Add(s);
			s.IsSelected = true;
		}
	}

	public static void ResetPivotToSelectionCenter(EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
		{
			ctx.RotationPivot = SKPoint.Empty;
			ctx.HasCustomRotationPivot = false;
			return;
		}

		var b = GetBounds(ctx);
		ctx.RotationPivot = new SKPoint(b.MidX, b.MidY);
		ctx.HasCustomRotationPivot = false;
	}

	public static void SetCustomPivot(EditorInteractionContext ctx, SKPoint pivot)
	{
		ctx.RotationPivot = pivot;
		ctx.HasCustomRotationPivot = true;
	}

	public static SKRect GetBounds(EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return SKRect.Empty;

		var r = ctx.Selection[0].GetBounds();
		for (int i = 1; i < ctx.Selection.Count; i++)
			r = Union(r, ctx.Selection[i].GetBounds());

		return r;
	}

	private static SKRect Union(SKRect a, SKRect b)
	{
		return new SKRect(
			Math.Min(a.Left, b.Left),
			Math.Min(a.Top, b.Top),
			Math.Max(a.Right, b.Right),
			Math.Max(a.Bottom, b.Bottom));
	}
}

public static class SelectionCycleHelper
{
	public static Shape? BeginOrAdvance(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		Shape currentShape)
	{
		bool continueExisting =
			ctx.ActiveSelectionCycleHits.Count > 0 &&
			IsNear(ctx.ActiveSelectionCyclePoint, pagePoint, 1.5f);

		if (!continueExisting)
		{
			var hits = GetHitStack(view, pagePoint);
			if (hits.Count == 0)
			{
				Reset(ctx);
				return null;
			}

			ctx.ActiveSelectionCycleHits.Clear();
			ctx.ActiveSelectionCycleHits.AddRange(hits);
			ctx.ActiveSelectionCyclePoint = pagePoint;

			int currentIndex = hits.FindIndex(s => ReferenceEquals(s, currentShape));
			if (currentIndex < 0)
				currentIndex = 0;

			ctx.ActiveSelectionCycleIndex = (currentIndex + 1) % hits.Count;
		}

		if (ctx.ActiveSelectionCycleHits.Count == 0)
			return null;

		var result = ctx.ActiveSelectionCycleHits[ctx.ActiveSelectionCycleIndex];
		ctx.ActiveSelectionCycleIndex =
			(ctx.ActiveSelectionCycleIndex + 1) % ctx.ActiveSelectionCycleHits.Count;

		return result;
	}

	public static void Reset(EditorInteractionContext ctx)
	{
		ctx.PendingSelectionCycle = false;
		ctx.PendingSelectionCyclePoint = SKPoint.Empty;
		ctx.PendingSelectionCycleStartShape = null;

		ctx.ActiveSelectionCyclePoint = SKPoint.Empty;
		ctx.ActiveSelectionCycleHits.Clear();
		ctx.ActiveSelectionCycleIndex = -1;
	}

	/// <summary>
	/// Что лежит под точкой, спереди назад, в порядке перебора щелчками.
	///
	/// За группой сразу следует её деталь под курсором, и только потом —
	/// то, что лежит ниже группы. Первый щелчок по группе выделяет группу
	/// целиком, второй — ту самую деталь, по которой щёлкнули, дальше
	/// перебор уходит под группу.
	///
	/// Порядок именно такой, потому что отвечает порядку намерений:
	/// чаще всего нужна группа, реже — деталь внутри неё, и совсем редко —
	/// то, что под ней скрыто. Вложенные группы раскрываются по тому же
	/// правилу, по уровню на щелчок.
	/// </summary>
	public static List<Shape> GetHitStack(MainView view, SKPoint pagePoint)
	{
		float tolerance = view.HitToleranceUom();
		float mmPerUnit = view.MmPerPageUnit();

		var result = new List<Shape>();

		foreach (var shape in view.GetShapesFrontToBack())
		{
			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (!shape.HitTest(pagePoint, tolerance, mmPerUnit))
				continue;

			result.Add(shape);

			if (shape is GroupShape group)
				AppendHitChain(group, pagePoint, tolerance, mmPerUnit, result);
		}

		return result;
	}

	/// <summary>
	/// Добавляет цепочку вложенных деталей под точкой: верхняя деталь
	/// группы, затем, если она сама группа, её верхняя деталь, и так далее.
	///
	/// По одной на уровень, а не все подряд: щелчком указывают на ОДНУ
	/// фигуру, и остальные детали группы под курсором не лежат.
	/// </summary>
	private static void AppendHitChain(
		GroupShape group,
		SKPoint pagePoint,
		float tolerance,
		float mmPerUnit,
		List<Shape> result)
	{
		// С конца: последний в списке детей рисуется поверх остальных,
		// и щелчок достаётся ему, как и при обычном выделении.
		for (int i = group.Children.Count - 1; i >= 0; i--)
		{
			var child = group.Children[i];

			if (!child.IsVisible || child.IsLocked)
				continue;

			if (!child.HitTest(pagePoint, tolerance, mmPerUnit))
				continue;

			result.Add(child);

			if (child is GroupShape inner)
				AppendHitChain(inner, pagePoint, tolerance, mmPerUnit, result);

			return;
		}
	}

	private static bool IsNear(SKPoint a, SKPoint b, float toleranceMm)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return dx * dx + dy * dy <= toleranceMm * toleranceMm;
	}
}

public enum ShapePickTarget
{
	/// <summary>Контур, по которому идёт текст.</summary>
	TextPath,

	/// <summary>Фигура-контейнер, внутри которой располагается текст.</summary>
	TextContainer
}

public sealed class SelectOtherShapeTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.SelectOtherShape;
	/// <summary>
	/// Куда записать выбранную фигуру. Ставится панелью перед включением
	/// инструмента. Статика, а не поле: инструменты лежат в общем статическом
	/// словаре MainView и создаются один раз.
	/// </summary>
	public static ShapePickTarget PickTarget { get; set; } = ShapePickTarget.TextPath;

	private static bool CreatesContainerCycle(TextShape target, Shape candidate)
	{
		const int MaxChain = 32;

		Shape? current = candidate;

		for (int i = 0; i < MaxChain && current != null; i++)
		{
			if (ReferenceEquals(current, target))
				return true;

			current = current is TextShape ts ? ts.InsideShape : null;
		}

		return false;
	}

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view == null)
			return;

		// Правая кнопка и любая другая — отмена выбора без изменений.
		if (button != MouseButtons.Left)
		{
			view.SetTool(ShapeToolKind.Select);
			return;
		}

		var target = view.SelectedShape;
		var hit = ctx.MouseDownHit;

		// Единственный выход из инструмента — в конце метода, чтобы не
		// вызывать SetTool дважды, как это делал прежний код.
		try
		{
			if (target == null)
				return;

			if (hit.Kind != HitKind.ShapeBody || hit.Shape == null)
				return;

			var picked = hit.Shape;

			if (ReferenceEquals(picked, target))
				return;

			if (PickTarget == ShapePickTarget.TextContainer)
			{
				if (target is not TextShape textTarget)
					return;

				// Цикл ссылок даст бесконечную рекурсию в BuildFlowRegions.
				if (CreatesContainerCycle(textTarget, picked))
					return;

				var beforeContainer = textTarget.InsideShape;

				if (ReferenceEquals(beforeContainer, picked))
					return;

				textTarget.InsideShape = picked;

				view.Undo?.Push(new PropertyChangeAction<Shape?>(
					"Text container",
					beforeContainer,
					picked,
					x =>
					{
						textTarget.InsideShape = x;
						view.mainForm?.RebuildPropertyPanel();
					},
					mergeKey: (textTarget, "Text.InsideShape")));
			}
			else
			{
				if (CreatesCycle(target, picked))
					return;

				var before = target.otherShape;

				if (ReferenceEquals(before, picked))
					return;

				target.otherShape = picked;

				view.Undo?.Push(new PropertyChangeAction<Shape?>(
					"Text path",
					before,
					picked,
					x =>
					{
						target.otherShape = x;
						view.mainForm?.RebuildPropertyPanel();
					},
					mergeKey: (target, "Shape.otherShape")));
			}

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.mainForm?.RebuildPropertyPanel();
		}
		finally
		{
			view.SetTool(ShapeToolKind.Select);
			view.Invalidate();
		}
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		// Курсор-указатель подсказывает, что ожидается выбор фигуры.
		view.Cursor = Cursors.Hand;
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Escape)
		{
			view.SetTool(ShapeToolKind.Select);
			view.Invalidate();
		}
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		// Подсветка фигуры под курсором: без неё непонятно, что именно будет
		// выбрано, особенно на перекрывающихся контурах.
		var hot = ctx.HotHit;

		if (hot.Kind != HitKind.ShapeBody || hot.Shape == null)
			return;

		if (ReferenceEquals(hot.Shape, view.SelectedShape))
			return;

		using var worldPath = hot.Shape.BuildWorldPath();
		if (worldPath == null || worldPath.IsEmpty)
			return;

		// Путь в мировых координатах, а канва оверлея — в экранных,
		// поэтому переводим сами.
		using var screenPath = new SKPath();
		using var iter = worldPath.CreateRawIterator();

		var points = new SKPoint[4];
		SKPathVerb verb;

		while ((verb = iter.Next(points)) != SKPathVerb.Done)
		{
			switch (verb)
			{
				case SKPathVerb.Move:
					{
						var p = view.PageToScreen(points[0]);
						screenPath.MoveTo(p.X, p.Y);
						break;
					}

				case SKPathVerb.Line:
					{
						var p = view.PageToScreen(points[1]);
						screenPath.LineTo(p.X, p.Y);
						break;
					}

				case SKPathVerb.Quad:
					{
						var c = view.PageToScreen(points[1]);
						var e = view.PageToScreen(points[2]);
						screenPath.QuadTo(c.X, c.Y, e.X, e.Y);
						break;
					}

				case SKPathVerb.Conic:
					{
						var c = view.PageToScreen(points[1]);
						var e = view.PageToScreen(points[2]);
						screenPath.ConicTo(c.X, c.Y, e.X, e.Y, iter.ConicWeight());
						break;
					}

				case SKPathVerb.Cubic:
					{
						var c1 = view.PageToScreen(points[1]);
						var c2 = view.PageToScreen(points[2]);
						var e = view.PageToScreen(points[3]);
						screenPath.CubicTo(c1.X, c1.Y, c2.X, c2.Y, e.X, e.Y);
						break;
					}

				case SKPathVerb.Close:
					screenPath.Close();
					break;
			}
		}

		using var highlight = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 3f,
			Color = new SKColor(255, 150, 0, 200)
		};

		canvas.DrawPath(screenPath, highlight);
	}

	/// <summary>
	/// Даёт ли присвоение candidate в качестве контура для target цикл ссылок.
	/// Цепочка otherShape обходится с ограничением по длине: даже при
	/// повреждённых данных обход обязан завершиться.
	/// </summary>
	private static bool CreatesCycle(Shape target, Shape candidate)
	{
		const int MaxChainLength = 64;

		var current = candidate;

		for (int i = 0; i < MaxChainLength && current != null; i++)
		{
			if (ReferenceEquals(current, target))
				return true;

			current = current.otherShape;
		}

		return false;
	}
}

public static class SelectionAdorner
{
	public readonly record struct HandleInfo(HandleKind Kind, SKPoint Position);

	/// <summary>Ниже какой толщины габарит считается вырожденным.</summary>
	private const float DegenerateSize = 0.001f;

	/// <summary>
	/// Ручки растягивания.
	///
	/// У вырожденного габарита — а так выглядит отрезок, вытянутый строго
	/// по оси, — ручки поперёк него не показываются. Растягивать там
	/// нечего, а предложенная ручка, которая ничего не делает, хуже
	/// отсутствующей: пользователь тянет её и решает, что программа
	/// сломалась.
	///
	/// Угловые ручки по той же причине уступают место продольным:
	/// у отрезка угол габаритов совпадает с его концом, и две ручки
	/// в одной точке не различить.
	/// </summary>
	public static IReadOnlyList<HandleInfo> GetResizeHandles(SKRect b, float offset)
	{
		float cx = (b.Left + b.Right) * 0.5f;
		float cy = (b.Top + b.Bottom) * 0.5f;

		bool degenerateX = MathF.Abs(b.Right - b.Left) < DegenerateSize;
		bool degenerateY = MathF.Abs(b.Bottom - b.Top) < DegenerateSize;

		// Точка: тянуть не за что вовсе.
		if (degenerateX && degenerateY)
			return Array.Empty<HandleInfo>();

		if (degenerateX)
		{
			return new[]
			{
				new HandleInfo(HandleKind.Top, new SKPoint(cx, b.Top - offset)),
				new HandleInfo(HandleKind.Bottom, new SKPoint(cx, b.Bottom + offset)),
			};
		}

		if (degenerateY)
		{
			return new[]
			{
				new HandleInfo(HandleKind.Left, new SKPoint(b.Left - offset, cy)),
				new HandleInfo(HandleKind.Right, new SKPoint(b.Right + offset, cy)),
			};
		}

		return new[]
		{
			new HandleInfo(HandleKind.TopLeft, new SKPoint(b.Left - offset, b.Top - offset)),
			new HandleInfo(HandleKind.Top, new SKPoint(cx, b.Top - offset)),
			new HandleInfo(HandleKind.TopRight, new SKPoint(b.Right + offset, b.Top - offset)),
			new HandleInfo(HandleKind.Right, new SKPoint(b.Right + offset, cy)),
			new HandleInfo(HandleKind.BottomRight, new SKPoint(b.Right + offset, b.Bottom + offset)),
			new HandleInfo(HandleKind.Bottom, new SKPoint(cx, b.Bottom + offset)),
			new HandleInfo(HandleKind.BottomLeft, new SKPoint(b.Left - offset, b.Bottom + offset)),
			new HandleInfo(HandleKind.Left, new SKPoint(b.Left - offset, cy)),
		};
	}

	public static IReadOnlyList<HandleInfo> GetRotateHandles(SKRect b, float offset)
	{
		return new[]
		{
			new HandleInfo(HandleKind.TopLeft, new SKPoint(b.Left - offset, b.Top - offset)),
			new HandleInfo(HandleKind.TopRight, new SKPoint(b.Right + offset, b.Top - offset)),
			new HandleInfo(HandleKind.BottomRight, new SKPoint(b.Right + offset, b.Bottom + offset)),
			new HandleInfo(HandleKind.BottomLeft, new SKPoint(b.Left - offset, b.Bottom + offset)),
		};
	}

	public static SKPoint GetModeToggleHandle(SKRect b) => new SKPoint(b.MidX, b.MidY);

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count == 0)
			return;

		// 2 screen pixels of visual gap, converted to page/UoM units for the current page.
		float offset = view.ScreenPixelsToPageMm(Settings.handle_size) / view.CurrentPage!.MmPerUom;

		var b = SelectionHelper.GetBounds(ctx);
		var tl = view.PageToScreen(new SKPoint(b.Left, b.Top));
		var br = view.PageToScreen(new SKPoint(b.Right, b.Bottom));
		var sr = new SKRect(tl.X, tl.Y, br.X, br.Y);

		bool isTextAlongShapeSelected =
			ctx.Selection.Count == 1 &&
			ctx.Selection[0] is TextShape ts &&
			ts.AlongShape &&
			ts.otherShape != null;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Settings.handle_fill_color
		};

		using var selBorder = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(0, 0, 0, 100)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.handle_line_color
		};

		using var pivotStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(220, 80, 80)
		};

		using var pivotLinkStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(100, 100, 100, 150)
		};

		// Draw selection border:
		canvas.DrawRect(sr, selBorder);

		// Draw pivot mode toggle:
		var modeCenter = view.PageToScreen(GetModeToggleHandle(b));
		canvas.DrawCircle(modeCenter, Settings.handle_size, fill);
		canvas.DrawCircle(modeCenter, Settings.handle_size, stroke);

		// Draw handles:
		// Для текста вдоль фигуры всегда рисуем только resize-handles,
		// даже если TransformMode == Rotate.
		if (ctx.TransformMode == SelectionTransformMode.Resize || isTextAlongShapeSelected)
		{
			foreach (var h in GetResizeHandles(b, offset))
			{
				var sp = view.PageToScreen(h.Position);
				var rr = SKRect.Create(
					sp.X - Settings.handle_size,
					sp.Y - Settings.handle_size,
					Settings.handle_size * 2,
					Settings.handle_size * 2);

				canvas.DrawRect(rr, fill);
				canvas.DrawRect(rr, stroke);
			}
		}
		else
		{
			// Rotate-handles и pivot отрисовываем только если это не текст вдоль фигуры
			foreach (var h in GetRotateHandles(b, offset))
			{
				var sp = view.PageToScreen(h.Position);
				canvas.DrawCircle(sp, Settings.handle_size, fill);
				canvas.DrawCircle(sp, Settings.handle_size, stroke);
			}

			var pivot = view.PageToScreen(ctx.RotationPivot);
			canvas.DrawLine(modeCenter.X, modeCenter.Y, pivot.X, pivot.Y, pivotLinkStroke);
			canvas.DrawCircle(pivot, Settings.handle_size, pivotStroke);
			canvas.DrawLine(pivot.X - Settings.handle_size, pivot.Y, pivot.X + Settings.handle_size, pivot.Y, pivotStroke);
			canvas.DrawLine(pivot.X, pivot.Y - Settings.handle_size, pivot.X, pivot.Y + Settings.handle_size, pivotStroke);
		}
	}
}

public sealed class SelectTool : IEditorTool
{
	public ShapeToolKind Kind => ShapeToolKind.Select;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view == null) return;
		if (button != MouseButtons.Left) return;

		var before = ctx.Selection.ToList();
		var hit = ctx.MouseDownHit;

		// Curve mode: only curve-specific editing hits are handled here.
		// All regular shape selection must continue to work in any mode.
		if (view.IsCurveEditMode)
		{
			switch (hit.Kind)
			{
				case HitKind.CurveNode:
					if (hit.Shape is CurveShape curveNodeShape && hit.CurveNodeIndex >= 0)
					{
						view.ClearGuideSelection();

						int selectedCount = curveNodeShape.Nodes.Count(n => n.Selected);
						var node = curveNodeShape.Nodes[hit.CurveNodeIndex];

						foreach (var n in curveNodeShape.Nodes)
							n.Focused = false;

						bool clickedNodeAlreadySelected = node.Selected;

						if (ctx.CtrlPressed)
						{
							// Переключение откладывается до отпускания кнопки.
							//
							// Ctrl здесь означает и «добавить узел к выбранным»,
							// и «вести перемещение по одной оси». Пока кнопка
							// нажата, одно от другого не отличить, поэтому
							// на нажатии не делаем ничего необратимого:
							// переключим на отпускании, если протяжки не было.
							ctx.PendingNodeToggleCurve = curveNodeShape;
							ctx.PendingNodeToggleIndex = hit.CurveNodeIndex;
							ctx.PendingNodeTogglePoint = pagePoint;

							node.Focused = true;

							// Уже выбранный узел можно и потащить: операция
							// запускается сразу, а переключение отменится,
							// как только мышь сдвинется. Невыбранный не тащим —
							// щелчок по нему означает «добавить», и утаскивать
							// фигуру, которую пользователь только намеревается
							// выделить, неправильно.
							if (clickedNodeAlreadySelected)
							{
								view.StartOperation(
									selectedCount > 1
										? new MoveSelectedCurveNodesOperation(curveNodeShape, hit.CurveNodeIndex)
										: new MoveCurveNodeOperation(curveNodeShape, hit.CurveNodeIndex),
									pagePoint);
							}

							return;
						}
						else
						{
							if (!clickedNodeAlreadySelected)
							{
								foreach (var n in curveNodeShape.Nodes)
									n.Selected = false;

								node.Selected = true;
								node.Focused = true;

								view.StartOperation(
									new MoveCurveNodeOperation(curveNodeShape, hit.CurveNodeIndex),
									pagePoint);
								return;
							}
						}

						if (selectedCount > 1)
						{
							view.StartOperation(
								new MoveSelectedCurveNodesOperation(curveNodeShape, hit.CurveNodeIndex),
								pagePoint);
							return;
						}
						else
						{
							view.StartOperation(
								new MoveCurveNodeOperation(curveNodeShape, hit.CurveNodeIndex),
								pagePoint);
							return;
						}
					}
					break;
				case HitKind.ShapeBody:
					// Протяжка по САМОЙ редактируемой кривой — это рамка
					// выделения узлов, а не перемещение фигуры.
					//
					// У замкнутой кривой с заливкой её внутренняя область тоже
					// считается корпусом, поэтому щелчок по видимо пустому месту
					// внутри контура попадал в общий обработчик и уводил всю
					// фигуру. В режиме узлов фигура целиком не двигается —
					// для этого есть режим Shape.
					//
					// Чужие фигуры сюда не попадают: по ним щелчок должен
					// работать как обычно и выводить из режима узлов, поэтому
					// ниже break, а не return.
					if (hit.Shape is CurveShape bodyCurve && SelectionHelper.Contains(ctx, bodyCurve))
					{
						view.ClearGuideSelection();

						// Те же сбросы, что у прочих веток режима: иначе
						// отпускание кнопки досталось бы отложенному
						// перемещению или перебору фигур под курсором.
						ctx.DeferredMoveCandidate = false;
						ctx.DeferredMoveShape = null;
						ctx.PendingSelectionCycle = false;
						ctx.PendingSelectionCyclePoint = SKPoint.Empty;
						ctx.PendingSelectionCycleStartShape = null;

						view.StartOperation(new CurveNodeMarqueeSelectionOperation(bodyCurve), pagePoint);
						return;
					}
					break;

				case HitKind.CurveSegment:
					if (hit.Shape is CurveShape curveSegmentShape && hit.CurveSegmentIndex >= 0)
					{
						int endNode = CurveAdorner.GetSegmentEndNode(curveSegmentShape, hit.CurveSegmentIndex);

						// Оба узла прямые — тянуть нечем: рычагов у них нет.
						// Щелчок оставляем без действия, чтобы не выйти из режима
						// узлов и не начать рамку выделения поверх кривой.
						if (endNode < 0 || !CurveAdorner.CanDragSegment(curveSegmentShape, hit.CurveSegmentIndex))
							return;

						view.ClearGuideSelection();

						if (!SelectionHelper.Contains(ctx, curveSegmentShape))
						{
							SelectionHelper.SetSingle(ctx, curveSegmentShape);
							view.NotifySelectionIfChanged(before);
						}

						view.StartOperation(
							new MoveCurveSegmentOperation(curveSegmentShape, hit.CurveSegmentIndex, endNode),
							pagePoint);
					}
					return;

				case HitKind.CurveControlPoint:
					if (hit.Shape is CurveShape curveCpShape && hit.CurveNodeIndex >= 0)
					{
						view.ClearGuideSelection();

						if (!SelectionHelper.Contains(ctx, curveCpShape))
						{
							SelectionHelper.SetSingle(ctx, curveCpShape);
							view.NotifySelectionIfChanged(before);
						}

						foreach (var n in curveCpShape.Nodes)
							n.Focused = false;

						curveCpShape.Nodes[hit.CurveNodeIndex].Focused = true;

						view.StartOperation(new MoveCurveControlPointOperation(curveCpShape, hit.CurveNodeIndex, hit.CurveControlIsPrev), pagePoint);
					}
					return;
			}
		}

		// Shape mode / common handling for all modes
		switch (hit.Kind)
		{
			case HitKind.ShapePoint:
				if (hit.Shape != null && hit.PointIndex >= 0)
				{
					view.ClearGuideSelection();

					bool needSelectionChange =
						ctx.Selection.Count != 1 ||
						!ReferenceEquals(ctx.Selection[0], hit.Shape);

					if (needSelectionChange)
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					view.StartOperation(new MoveShapePointOperation(hit.Shape, hit.PointIndex), pagePoint);
				}
				break;

			case HitKind.ShapePropertyHandle:
				if (hit.Shape is RoundedRectangleShape &&
					hit.PropertyHandle == ShapePropertyHandleKind.RoundedRectangleRadius)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					view.StartOperation(new AdjustRoundedRectangleRadiusOperation(), pagePoint);
				}
				else if (hit.Shape is ChamferedRectangleShape &&
						 hit.PropertyHandle == ShapePropertyHandleKind.ChamferedRectangleSize)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					view.StartOperation(new AdjustChamferedRectangleSizeOperation(), pagePoint);
				}
				break;

			case HitKind.FillStartHandle:
				if (hit.Shape != null)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					view.StartOperation(new MoveFillHandleOperation(hit.Shape, FillHandleKind.Start), pagePoint);
				}
				break;

			case HitKind.FillEndHandle:
				if (hit.Shape != null)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					view.StartOperation(new MoveFillHandleOperation(hit.Shape, FillHandleKind.End), pagePoint);
				}
				break;

			case HitKind.MeasurementText:
				if (hit.Shape != null)
				{
					DragOperation? textOp = hit.Shape switch
					{
						MeasurementShape m => new MoveMeasurementTextOperation(m),
						AngleMeasurementShape a => new MoveAngleMeasurementTextOperation(a),
						_ => null
					};

					if (textOp == null)
						break;

					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					// Те же сбросы, что у прочих ручек: иначе отпускание
					// кнопки над подписью досталось бы отложенному
					// перемещению всей фигуры.
					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					view.StartOperation(textOp, pagePoint);
				}
				break;

			case HitKind.ShadowStartHandle:
			case HitKind.ShadowEndHandle:
			case HitKind.ShadowSoftnessHandle:
				if (hit.Shape != null)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					// Те же сбросы, что у ручек заливки: иначе отпускание
					// кнопки над ручкой досталось бы отложенному перемещению
					// или перебору фигур под курсором.
					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					if (hit.Kind == HitKind.ShadowSoftnessHandle)
					{
						view.StartOperation(new AdjustShadowSoftnessOperation(hit.Shape), pagePoint);
					}
					else
					{
						var shadowHandle = hit.Kind == HitKind.ShadowStartHandle
							? ShadowHandleKind.Start
							: ShadowHandleKind.End;

						view.StartOperation(new MoveShadowHandleOperation(hit.Shape, shadowHandle), pagePoint);
					}
				}
				break;

			case HitKind.TransparencyStartHandle:
			case HitKind.TransparencyEndHandle:
				if (hit.Shape != null)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, hit.Shape))
					{
						SelectionHelper.SetSingle(ctx, hit.Shape);
						view.NotifySelectionIfChanged(before);
					}

					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					var transparencyHandle = hit.Kind == HitKind.TransparencyStartHandle
						? TransparencyHandleKind.Start
						: TransparencyHandleKind.End;

					view.StartOperation(
						new MoveTransparencyHandleOperation(hit.Shape, transparencyHandle),
						pagePoint);
				}
				break;

			case HitKind.TransformStartHandle:
			case HitKind.TransformEndHandle:
			case HitKind.TransformBiasHandle:
				if (hit.Shape is TransformShape transformTool)
				{
					view.ClearGuideSelection();

					if (!SelectionHelper.Contains(ctx, transformTool))
					{
						SelectionHelper.SetSingle(ctx, transformTool);
						view.NotifySelectionIfChanged(before);
					}

					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					if (hit.Kind == HitKind.TransformBiasHandle)
					{
						view.StartOperation(new AdjustTransformBiasOperation(transformTool), pagePoint);
					}
					else
					{
						var transformHandle = hit.Kind == HitKind.TransformStartHandle
							? TransformHandleKind.Start
							: TransformHandleKind.End;

						view.StartOperation(
							new MoveTransformHandleOperation(transformTool, transformHandle),
							pagePoint);
					}
				}
				break;

			case HitKind.RotationPivot:
				view.ClearGuideSelection();
				view.StartOperation(new MoveRotationPivotOperation(), pagePoint);
				break;

			case HitKind.RotationHandle:
				view.ClearGuideSelection();
				view.StartOperation(new RotateSelectionOperation(), pagePoint);
				break;

			case HitKind.SelectionHandle:
				view.ClearGuideSelection();

				if (hit.Handle == HandleKind.RotationCenter)
				{
					ctx.PendingCenterHandleToggle = true;
					ctx.PendingCenterHandlePoint = pagePoint;
					ctx.DeferredMoveCandidate = true;
					ctx.DeferredMoveShape = ctx.Selection.Count > 0 ? ctx.Selection[0] : null;
				}
				else
				{
					view.StartOperation(new ResizeSelectionOperation(hit.Handle), pagePoint);
				}
				break;

			case HitKind.ShapeBody:
				view.ClearGuideSelection();

				if (hit.Shape != null)
				{
					if (ctx.CtrlPressed)
					{
						if (SelectionHelper.Contains(ctx, hit.Shape))
							SelectionHelper.Remove(ctx, hit.Shape);
						else
							SelectionHelper.Add(ctx, hit.Shape);
					}
					else
					{
						if (!SelectionHelper.Contains(ctx, hit.Shape))
							SelectionHelper.SetSingle(ctx, hit.Shape);
						else
							view.StartOperation(new MoveSelectionOperation(), hit.PagePoint);
					}
					view.mainForm.ApplyEditorModeFromSelection();
					view.NotifySelectionIfChanged(before);
				}
				break;

			case HitKind.GuideCenterHandle:
				if (hit.Shape is GuideLine centerGuide)
				{
					if (!ReferenceEquals(ctx.SelectedGuide, centerGuide))
						view.SelectGuide(centerGuide);

					ctx.PendingGuideCenterToggle = true;
					ctx.PendingGuideCenterTogglePoint = pagePoint;
					ctx.DeferredGuideCenterMoveCandidate = true;
				}
				break;
			case HitKind.GuideRotateHandleCCW:
			case HitKind.GuideRotateHandleCW:
				if (hit.Shape is GuideLine rotateGuide)
				{
					if (!ReferenceEquals(ctx.SelectedGuide, rotateGuide))
						view.SelectGuide(rotateGuide);

					view.StartOperation(new RotateGuideOperation(), pagePoint);
				}
				break;
			case HitKind.GuideBody:
				if (hit.Shape is GuideLine guide)
				{
					if (!ReferenceEquals(ctx.SelectedGuide, guide))
						view.SelectGuide(guide);

					ctx.DeferredGuideMoveCandidate = true;
				}
				break;

			case HitKind.TextAlongShapeHandle:
				if (hit.Shape is TextShape textAlongShape)
				{
					view.ClearGuideSelection();
					if (!SelectionHelper.Contains(ctx, textAlongShape))
						SelectionHelper.SetSingle(ctx, textAlongShape);
					view.NotifySelectionIfChanged(before);

					ctx.DeferredMoveCandidate = false;
					ctx.DeferredMoveShape = null;
					ctx.PendingSelectionCycle = false;
					ctx.PendingSelectionCyclePoint = SKPoint.Empty;
					ctx.PendingSelectionCycleStartShape = null;

					if (hit.Handle == HandleKind.TextAlongAxLeft || hit.Handle == HandleKind.TextAlongAxRight)
						view.StartOperation(new AdjustTextAlongShapeAxOperation(textAlongShape), pagePoint);
					else
						view.StartOperation(new AdjustTextAlongShapeAyOperation(textAlongShape), pagePoint);
				}
				break;

			default:
				view.ClearGuideSelection();
				if (view.IsCurveEditMode &&
					ctx.Selection.Count == 1 &&
					ctx.Selection[0] is CurveShape curve)
				{
					view.StartOperation(new CurveNodeMarqueeSelectionOperation(curve), pagePoint);
					return;
				}

				// Нажатие пришлось на подсвеченную точку — берём фигуру за неё.
				if (TryGrabByHighlightedPoint(view, ctx, pagePoint, before))
					return;

				if (!ctx.CtrlPressed)
					SelectionHelper.Clear(ctx);

				view.NotifySelectionIfChanged(before);
				view.StartOperation(new MarqueeSelectionOperation(), pagePoint);
				break;
		}
	}

	/// <summary>
	/// Захват фигуры за подсвеченную точку, когда под курсором нет ни линии,
	/// ни заливки.
	///
	/// Угол и центр габаритов у кривой, эллипса, текста лежат в пустоте:
	/// геометрии там нет, разбор попадания ничего не находит, и нажатие
	/// уходило в рамку выделения. Получалось, что точку видно, она
	/// подсвечена, а взяться за неё нельзя — тогда как именно за угол
	/// габаритов фигуру и несут, чтобы совместить с чужим углом.
	///
	/// Берётся ровно та точка, которую пользователь видит подсвеченной:
	/// MainView запоминает её в PressedNode* на нажатии. Поэтому и условие
	/// честное — никакого скрытого радиуса, за пределами которого подсветка
	/// гаснет, а захват ещё срабатывает.
	///
	/// Выделяется предок верхнего уровня, а не сама деталь: щелчок по
	/// корпусу группы тоже выделяет группу. Если же выделение уже содержит
	/// деталь или её предка, выделение не трогается вовсе — начинается
	/// перемещение.
	/// </summary>
	private static bool TryGrabByHighlightedPoint(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		List<Shape> before)
	{
		var node = ctx.PressedNodeShape;

		if (node == null || ctx.PressedNodeWorld == null)
			return false;

		if (node is GuideLine || !node.IsVisible || node.IsLocked)
			return false;

		// Уже выделено — своё, тащим как есть.
		for (var current = node; current != null; current = current.Parent)
		{
			if (ctx.Selection.Contains(current))
			{
				if (ctx.CtrlPressed)
					return false;

				view.StartOperation(new MoveSelectionOperation(), pagePoint);
				return true;
			}
		}

		var root = node;
		while (root.Parent != null)
			root = root.Parent;

		if (root.IsLocked || !root.IsVisible)
			return false;

		if (ctx.CtrlPressed)
		{
			SelectionHelper.Add(ctx, root);
			view.mainForm?.ApplyEditorModeFromSelection();
			view.NotifySelectionIfChanged(before);
			return true;
		}

		SelectionHelper.SetSingle(ctx, root);
		view.mainForm?.ApplyEditorModeFromSelection();
		view.NotifySelectionIfChanged(before);

		// Перемещение откладывается до сдвига мыши: щелчок без протяжки
		// должен только выделять, как и щелчок по корпусу фигуры.
		ctx.DeferredMoveCandidate = true;
		ctx.DeferredMoveShape = root;

		return true;
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = ctx.HotHit.Kind switch
		{
			HitKind.ShapePoint => Cursors.Hand,
			HitKind.CurveSegment => Cursors.SizeAll,
			HitKind.ShapePropertyHandle => Cursors.Hand,
			HitKind.MeasurementText => Cursors.SizeAll,
			HitKind.ShadowStartHandle => Cursors.SizeAll,
			HitKind.ShadowEndHandle => Cursors.Hand,
			HitKind.ShadowSoftnessHandle => Cursors.Hand,
			HitKind.TransparencyStartHandle => Cursors.Hand,
			HitKind.TransparencyEndHandle => Cursors.Hand,
			HitKind.TransformStartHandle => Cursors.SizeAll,
			HitKind.TransformEndHandle => Cursors.SizeAll,
			HitKind.TransformBiasHandle => Cursors.Hand,
			HitKind.GuideBody => Cursors.SizeAll,
			HitKind.GuideCenterHandle => Cursors.Hand,
			HitKind.GuideRotateHandleCCW => Cursors.Cross,
			HitKind.GuideRotateHandleCW => Cursors.Cross,
			HitKind.ShapeBody => Cursors.SizeAll,
			HitKind.RotationPivot => Cursors.Hand,
			HitKind.RotationHandle => Cursors.Cross,
			HitKind.SelectionHandle when ctx.HotHit.Handle == HandleKind.RotationCenter => Cursors.SizeAll,
			HitKind.SelectionHandle => Cursors.Cross,
			HitKind.TextAlongShapeHandle when ctx.HotHit.Handle is HandleKind.TextAlongAxLeft or HandleKind.TextAlongAxRight => Cursors.SizeWE,
			HitKind.TextAlongShapeHandle when ctx.HotHit.Handle is HandleKind.TextAlongAyTop or HandleKind.TextAlongAyBottom => Cursors.SizeNS,

			// Подсвеченная точка — угол, середина стороны или центр
			// габаритов — тоже место захвата: нажатие на ней берёт фигуру
			// и несёт её. Курсор обязан об этом говорить, иначе точка
			// выглядит украшением: видно её, а что с ней делать — нет.
			//
			// Последним случаем, а не первым: у ручек выделения и у ручек
			// свойств свои курсоры, и они точнее — там тянут за одну ось
			// или вращают, а не переносят.
			//
			// В режиме узлов — нет: там протяжка по пустому месту обводит
			// узлы рамкой, а фигуру целиком не несёт, и курсор обещал бы
			// то, чего не будет.
			_ => ctx.HoveredNodeShape != null && !view.IsCurveEditMode
				? Cursors.SizeAll
				: Cursors.Default
		};
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		var curve = ctx.PendingNodeToggleCurve;
		int index = ctx.PendingNodeToggleIndex;

		ctx.PendingNodeToggleCurve = null;
		ctx.PendingNodeToggleIndex = -1;

		if (curve == null || index < 0 || index >= curve.Nodes.Count)
			return;

		// Мышь уехала — значит это было перемещение с ограничением по оси,
		// а не выбор узла. Выбор в таком случае не трогаем.
		float dx = pagePoint.X - ctx.PendingNodeTogglePoint.X;
		float dy = pagePoint.Y - ctx.PendingNodeTogglePoint.Y;

		float tolerance = view.HitToleranceUom();

		if (dx * dx + dy * dy > tolerance * tolerance)
			return;

		var node = curve.Nodes[index];

		node.Selected = !node.Selected;
		node.Focused = true;

		view.Invalidate();
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}
}

public sealed class CurveNodeMarqueeSelectionOperation : DragOperation
{
	private readonly CurveShape shape;
	private readonly HashSet<int> initialSelected = new();
	private int initialFocusedIndex = -1;
	public SKRect Rect { get; private set; }

	public CurveNodeMarqueeSelectionOperation(CurveShape shape)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override DragOperationKind Kind => DragOperationKind.MarqueeSelect;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = new SKRect(pagePoint.X, pagePoint.Y, pagePoint.X, pagePoint.Y);

		initialSelected.Clear();
		initialFocusedIndex = -1;

		for (int i = 0; i < shape.Nodes.Count; i++)
		{
			shape.Nodes[i].Selected = false;
			shape.Nodes[i].Focused = false;
		}
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = Normalize(ctx.MouseDownPage, pagePoint);

		int lastHitIndex = -1;

		for (int i = 0; i < shape.Nodes.Count; i++)
		{
			var wp = shape.LocalToWorld(shape.Nodes[i].Position);
			if (Rect.Contains(wp.X, wp.Y))
			{
				shape.Nodes[i].Selected = true;
				lastHitIndex = i;
			}
		}

		if (lastHitIndex >= 0)
		{
			for (int i = 0; i < shape.Nodes.Count; i++)
				shape.Nodes[i].Focused = false;

			shape.Nodes[lastHitIndex].Focused = true;
		}

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = Normalize(ctx.MouseDownPage, pagePoint);
	}

	private static SKRect Normalize(SKPoint a, SKPoint b)
	{
		return new SKRect(
			Math.Min(a.X, b.X),
			Math.Min(a.Y, b.Y),
			Math.Max(a.X, b.X),
			Math.Max(a.Y, b.Y));
	}
}

public sealed class MarqueeSelectionOperation : DragOperation
{
	public SKRect Rect { get; private set; }

	/// <summary>
	/// Режим рамки: true — «задел, значит выбран», false — «только целиком».
	///
	/// Определяется направлением протяжки по горизонтали: слева направо —
	/// рамка-окно, берёт лишь то, что вошло в неё полностью; справа налево —
	/// рамка-секущая, берёт всё, чего коснулась.
	///
	/// Решает одна горизонталь, а не обе оси сразу. Пользователь тянет
	/// рамку по диагонали, и вертикальная составляющая у одного и того же
	/// по смыслу движения бывает любой: вниз-вправо и вверх-вправо — это
	/// одно и то же «вправо». Потребуй мы совпадения обеих осей, две
	/// из четырёх диагоналей остались бы без определённого режима,
	/// и рамка вела бы себя по-разному при одинаковом, на взгляд
	/// пользователя, движении. Правило по горизонтали — то же, что
	/// в чертёжных редакторах, и привыкать к нему не нужно.
	/// </summary>
	public bool Crossing { get; private set; }

	public override DragOperationKind Kind => DragOperationKind.MarqueeSelect;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = new SKRect(pagePoint.X, pagePoint.Y, pagePoint.X, pagePoint.Y);
		Crossing = false;

		ctx.SelectionSnapshot.Clear();
		ctx.SelectionSnapshot.AddRange(ctx.Selection);
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = Normalize(ctx.MouseDownPage, pagePoint);
		Crossing = pagePoint.X < ctx.MouseDownPage.X;

		if (ctx.CtrlPressed)
			SelectionHelper.RestoreSnapshot(ctx);
		else
			SelectionHelper.Clear(ctx);

		foreach (var s in view.GetShapesBackToFront())
		{
			// Направляющие рамкой не берутся.
			//
			// Их габариты — бесконечная полоса через весь лист, и секущая
			// рамка задевала бы каждую направляющую поблизости, а рамка-окно
			// не смогла бы взять ни одну: полоса в неё не вмещается никогда.
			// Направляющую выделяют щелчком по ней — там для этого есть
			// отдельный разбор попадания.
			if (s is GuideLine)
				continue;

			// Скрытое и запертое не выделяется — так же, как при щелчке.
			// Прежде проверка центра брала и то и другое: при рамке-секущей
			// это стало бы заметно сразу.
			if (!s.IsVisible || s.IsLocked)
				continue;

			var b = s.GetBounds();

			bool hit = Crossing
				? IntersectsRect(Rect, b)
				: ContainsRect(Rect, b);

			if (hit)
				SelectionHelper.Add(ctx, s);
		}
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Rect = Normalize(ctx.MouseDownPage, pagePoint);
		Crossing = pagePoint.X < ctx.MouseDownPage.X;
		ctx.SelectionSnapshot.Clear();

		// Выделение менялось в Update напрямую через SelectionHelper, минуя
		// событие. Панель свойств подписана именно на него, поэтому после
		// рамки она оставалась в состоянии «ничего не выбрано», хотя фигуры
		// были выделены и обведены на холсте.
		//
		// Оповещение идёт здесь, а не в Update: там оно приходило бы на
		// каждое движение мыши и перестраивало бы панель десятки раз
		// за одну протяжку.
		view.mainForm?.ApplyEditorModeFromSelection();
		view.RaiseSelectionChanged();
	}

	/// <summary>
	/// Коробка inner целиком внутри outer.
	///
	/// Своя проверка, а не SKRect.Contains: та отказывает вырожденным
	/// коробкам. Вертикальный отрезок имеет нулевую ширину, и строгие
	/// сравнения выбрасывали бы его из рамки, в которой он на самом
	/// деле лежит. Границы считаются входящими: фигура, край которой
	/// совпал с краем рамки, пользователем воспринимается как попавшая.
	/// </summary>
	private static bool ContainsRect(SKRect outer, SKRect inner)
	{
		return inner.Left >= outer.Left
			&& inner.Right <= outer.Right
			&& inner.Top >= outer.Top
			&& inner.Bottom <= outer.Bottom;
	}

	/// <summary>
	/// Коробки пересекаются или касаются. Вырожденные — тоже.
	/// </summary>
	private static bool IntersectsRect(SKRect a, SKRect b)
	{
		return a.Left <= b.Right
			&& a.Right >= b.Left
			&& a.Top <= b.Bottom
			&& a.Bottom >= b.Top;
	}

	private static SKRect Normalize(SKPoint a, SKPoint b)
	{
		return new SKRect(
			Math.Min(a.X, b.X),
			Math.Min(a.Y, b.Y),
			Math.Max(a.X, b.X),
			Math.Max(a.Y, b.Y));
	}
}

public sealed class MoveSelectionOperation : DragOperation
{
	private SKPoint _start;
	private SKRect _startBounds;
	private readonly Dictionary<Shape, SKMatrix> _originalWorld = new();

	/// <summary>
	/// Узел, за который взялись, в положении на момент начала протяжки.
	///
	/// Прилипание считается ТОЛЬКО для него, а не для габаритов выделения.
	/// Габариты дают девять точек сразу, и к ближайшей цели притягивалась
	/// та из них, что оказалась рядом, — а пользователь тянул фигуру
	/// за конкретный угол и ждал совмещения именно его. Точка нужна одна,
	/// и выбирает её сам пользователь тем, где нажал.
	/// </summary>
	private SKPoint _anchorStart;

	public override DragOperationKind Kind => DragOperationKind.MoveSelection;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ResetAxisLock();

		_start = pagePoint;
		_startBounds = SelectionHelper.GetBounds(ctx);
		_originalWorld.Clear();
		_originalFill.Clear();
		ctx.ActiveSnap = null;

		foreach (var s in ctx.Selection)
		{
			_originalWorld[s] = s.GetWorldTransform();
			CaptureFillPointsDeep(s, _originalFill);
		}

		_anchorStart = PickAnchor(ctx, pagePoint);
	}

	/// <summary>
	/// Точка отсчёта прилипания.
	///
	/// Сначала — узел, подсвеченный под курсором: раз пользователь целился
	/// в него, тянуть фигуру он будет именно за него. Узел чужой фигуры
	/// не годится, поэтому проверяется принадлежность выделению.
	///
	/// Если под курсором узла не было, берётся ближайший к нему узел
	/// выделения — без ограничения по расстоянию: точка отсчёта нужна
	/// всегда, даже когда фигуру потянули за середину.
	/// </summary>
	private static SKPoint PickAnchor(EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (ctx.PressedNodeWorld != null &&
			ctx.PressedNodeShape != null &&
			BelongsToSelection(ctx, ctx.PressedNodeShape))
		{
			return ctx.PressedNodeWorld.Value;
		}

		var nearest = ShapeNodePicker.FindNearestUnbounded(ctx.Selection, pagePoint);

		return nearest?.WorldPoint ?? pagePoint;
	}

	/// <summary>
	/// Принадлежит ли фигура выделению — сама или как потомок выделенного.
	///
	/// Прямого Contains недостаточно: подсвеченный узел принадлежит
	/// КОНКРЕТНОЙ детали внутри группы, а в выделении лежит группа.
	/// Без подъёма по цепочке родителей проверка не проходила, узел
	/// отбрасывался как чужой, и точкой отсчёта становился ближайший
	/// угол габаритов — то есть ровно то, чего пользователь не выбирал.
	///
	/// Подъём от узла вверх, а не спуск по выделению вниз: глубина группы
	/// заведомо мала, а содержимое может быть большим.
	/// </summary>
	private static bool BelongsToSelection(EditorInteractionContext ctx, Shape shape)
	{
		for (var current = shape; current != null; current = current.Parent)
		{
			if (ctx.Selection.Contains(current))
				return true;
		}

		return false;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		// Ограничение по оси применяется ДО прилипания.
		//
		// Порядок важен: прижав точку к оси после прилипания, мы бы отменили
		// найденную поправку по второй координате, и фигура соскакивала бы
		// с цели. Наоборот — прилипание получает уже прижатую точку и ищет
		// цель вдоль разрешённого направления.
		var constrained = ApplyAxisLock(view, ctx, _start, pagePoint);

		float dx = constrained.X - _start.X;
		float dy = constrained.Y - _start.Y;

		if (!ctx.AltPressed)
		{
			// Прилипает одна точка — взятый узел в своём новом положении.
			var movedAnchor = new SKPoint(_anchorStart.X + dx, _anchorStart.Y + dy);

			var snap = ObjectSnapEngine.SnapSinglePoint(
				view,
				ctx.Selection.Cast<Shape>(),
				movedAnchor,
				Settings.snap_dist);

			ctx.ActiveSnap = snap;

			if (snap != null && snap.IsValid)
			{
				dx += snap.Correction.X;
				dy += snap.Correction.Y;

				// Поправка прилипания тоже прижимается к оси: цель могла
				// найтись в стороне, и без этого Ctrl переставал держать
				// направление ровно в тот момент, когда сработало прилипание.
				var corrected = ApplyAxisLock(
					view, ctx, _start, new SKPoint(_start.X + dx, _start.Y + dy));

				dx = corrected.X - _start.X;
				dy = corrected.Y - _start.Y;
			}
		}
		else
		{
			ctx.ActiveSnap = null;
		}

		var worldMove = SKMatrix.CreateTranslation(dx, dy);

		foreach (var pair in _originalWorld)
		{
			var shape = pair.Key;
			var originalWorld = pair.Value;

			RestoreOriginalWorldTransform(shape, originalWorld);
			RestoreOriginalFillPointsDeep(shape, _originalFill);

			shape.ApplyWorldTransform(worldMove);
			TransformFillPointsDeep(shape, _originalFill, worldMove, Kind);
		}

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		foreach (var pair in _originalWorld)
		{
			RestoreOriginalWorldTransform(pair.Key, pair.Value);
			RestoreOriginalFillPointsDeep(pair.Key, _originalFill);
		}

		ctx.ActiveSnap = null;
		view.Invalidate();
	}
}

public sealed class ResizeSelectionOperation : DragOperation
{
	private readonly HandleKind _handle;
	private readonly Dictionary<Shape, SKMatrix> _originalWorld = new();
	private SKRect _selectionStartBounds;

	public ResizeSelectionOperation(HandleKind handle)
	{
		_handle = handle;
	}

	public override DragOperationKind Kind => DragOperationKind.ResizeSelection;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_selectionStartBounds = SelectionHelper.GetBounds(ctx);
		_originalWorld.Clear();
		_originalFill.Clear();
		ctx.ActiveSnap = null;

		foreach (var s in ctx.Selection)
		{
			_originalWorld[s] = s.GetWorldTransform();
			CaptureFillPointsDeep(s, _originalFill);
		}
	}

	/// <summary>
	/// Ниже какой толщины габарит считается вырожденным.
	///
	/// Тысячная доля единицы листа: при миллиметрах это микрон, при дюймах
	/// двадцать пять микрон. Настолько тонких фигур не бывает — так выглядит
	/// только отрезок, вытянутый строго по оси.
	/// </summary>
	private const float DegenerateSize = 0.001f;

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		var start = _selectionStartBounds;

		float l0 = start.Left;
		float t0 = start.Top;
		float r0 = start.Right;
		float b0 = start.Bottom;

		const float minSize = 0.0001f;

		float w0 = r0 - l0;
		float h0 = b0 - t0;

		// Вырожденная ось исключается из растягивания.
		//
		// У строго вертикального отрезка ширина габаритов равна нулю,
		// и множитель по горизонтали считался делением на подставную
		// тысячную долю. Сдвиг ручки на десять единиц давал множитель
		// в сто тысяч. Точки, лежащие точно на оси, он бы не тронул,
		// но точно на оси они лежат только в теории: координаты дробные,
		// и остаток в последнем разряде, умноженный на сто тысяч,
		// уносил отрезок за пределы листа.
		//
		// Поэтому такая ось не растягивается вовсе. Тянуть отрезок
		// за боковую ручку бессмысленно — растягивать в нём по этой оси
		// нечего, — а вдоль он по-прежнему тянется как раньше.
		bool degenerateX = MathF.Abs(w0) < DegenerateSize;
		bool degenerateY = MathF.Abs(h0) < DegenerateSize;

		// Точка не растягивается ни в какую сторону. Прежде сюда же
		// приводила проверка start.IsEmpty, из-за которой у вертикального
		// отрезка не работало и растягивание вдоль — единственное,
		// которое для него осмысленно.
		if (degenerateX && degenerateY)
			return;

		bool proportionalResize = ctx.CtrlPressed;
		bool symmetricResize = ctx.ShiftPressed;

		float safeW0 = MathF.Abs(w0) < minSize ? (w0 < 0 ? -minSize : minSize) : w0;
		float safeH0 = MathF.Abs(h0) < minSize ? (h0 < 0 ? -minSize : minSize) : h0;

		SKPoint anchor;
		SKPoint effectivePagePoint = pagePoint;

		if (symmetricResize)
			anchor = new SKPoint(start.MidX, start.MidY);
		else
			anchor = GetAnchorForHandle(_handle, start);

		var snap = ObjectSnapEngine.SnapResizeSelectionHandle(
			view,
			ctx.Selection.Cast<Shape>(),
			_handle,
			_selectionStartBounds,
			effectivePagePoint,
			Settings.snap_dist);

		ctx.ActiveSnap = snap;

		if (snap != null && snap.IsValid)
			effectivePagePoint = Add(effectivePagePoint, snap.Correction);

		float sx = 1f;
		float sy = 1f;

		if (symmetricResize)
		{
			switch (_handle)
			{
				case HandleKind.Right:
					sx = SafeDiv(effectivePagePoint.X - start.MidX, w0 * 0.5f);
					sy = 1f;
					break;

				case HandleKind.Left:
					sx = SafeDiv(start.MidX - effectivePagePoint.X, w0 * 0.5f);
					sy = 1f;
					break;

				case HandleKind.Bottom:
					sx = 1f;
					sy = SafeDiv(effectivePagePoint.Y - start.MidY, h0 * 0.5f);
					break;

				case HandleKind.Top:
					sx = 1f;
					sy = SafeDiv(start.MidY - effectivePagePoint.Y, h0 * 0.5f);
					break;

				case HandleKind.BottomRight:
					sx = SafeDiv(effectivePagePoint.X - start.MidX, w0 * 0.5f);
					sy = SafeDiv(effectivePagePoint.Y - start.MidY, h0 * 0.5f);
					break;

				case HandleKind.BottomLeft:
					sx = SafeDiv(start.MidX - effectivePagePoint.X, w0 * 0.5f);
					sy = SafeDiv(effectivePagePoint.Y - start.MidY, h0 * 0.5f);
					break;

				case HandleKind.TopRight:
					sx = SafeDiv(effectivePagePoint.X - start.MidX, w0 * 0.5f);
					sy = SafeDiv(start.MidY - effectivePagePoint.Y, h0 * 0.5f);
					break;

				case HandleKind.TopLeft:
					sx = SafeDiv(start.MidX - effectivePagePoint.X, w0 * 0.5f);
					sy = SafeDiv(start.MidY - effectivePagePoint.Y, h0 * 0.5f);
					break;

				default:
					return;
			}
		}
		else
		{
			switch (_handle)
			{
				case HandleKind.Right:
					sx = (effectivePagePoint.X - l0) / safeW0;
					sy = 1f;
					break;

				case HandleKind.Left:
					sx = (r0 - effectivePagePoint.X) / safeW0;
					sy = 1f;
					break;

				case HandleKind.Bottom:
					sx = 1f;
					sy = (effectivePagePoint.Y - t0) / safeH0;
					break;

				case HandleKind.Top:
					sx = 1f;
					sy = (b0 - effectivePagePoint.Y) / safeH0;
					break;

				case HandleKind.BottomRight:
					sx = (effectivePagePoint.X - l0) / safeW0;
					sy = (effectivePagePoint.Y - t0) / safeH0;
					break;

				case HandleKind.BottomLeft:
					sx = (r0 - effectivePagePoint.X) / safeW0;
					sy = (effectivePagePoint.Y - t0) / safeH0;
					break;

				case HandleKind.TopRight:
					sx = (effectivePagePoint.X - l0) / safeW0;
					sy = (b0 - effectivePagePoint.Y) / safeH0;
					break;

				case HandleKind.TopLeft:
					sx = (r0 - effectivePagePoint.X) / safeW0;
					sy = (b0 - effectivePagePoint.Y) / safeH0;
					break;

				default:
					return;
			}
		}

		// Пропорциональное растягивание при вырожденной оси не считается:
		// оно выводит общий множитель из обеих сторон, а одна из них
		// здесь нулевая. Остаётся обычный множитель по живой оси.
		if (proportionalResize && !degenerateX && !degenerateY)
		{
			var uniform = ComputeProportionalScaleFromHandle(
				_handle,
				start,
				anchor,
				effectivePagePoint,
				symmetricResize);

			sx = uniform.X;
			sy = uniform.Y;
		}

		// Пересчёт по вырожденной оси отбрасывается последним, уже после
		// всех ветвей: так ни одна из них не может его обойти.
		if (degenerateX)
			sx = 1f;

		if (degenerateY)
			sy = 1f;

		// Ничего не меняется — и applying нечего: лишний проход по фигурам
		// сбрасывал бы и восстанавливал их матрицы на каждое движение мыши.
		if (sx == 1f && sy == 1f)
			return;

		if (MathF.Abs(sx) < minSize)
			sx = sx < 0 ? -minSize : minSize;

		if (MathF.Abs(sy) < minSize)
			sy = sy < 0 ? -minSize : minSize;

		var resizeWorld = CreateScaleAroundPivot(sx, sy, anchor);

		foreach (var pair in _originalWorld)
		{
			var shape = pair.Key;
			var originalWorld = pair.Value;

			RestoreOriginalWorldTransform(shape, originalWorld);
			RestoreOriginalFillPointsDeep(shape, _originalFill);

			var oldWorld = shape.GetWorldTransform();
			shape.ApplyWorldTransform(resizeWorld);
			var newWorld = shape.GetWorldTransform();
			shape.OnResizeApplied(oldWorld, newWorld);

			TransformFillPointsDeep(shape, _originalFill, resizeWorld, Kind);
		}

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		foreach (var pair in _originalWorld)
		{
			RestoreOriginalWorldTransform(pair.Key, pair.Value);
			RestoreOriginalFillPointsDeep(pair.Key, _originalFill);
		}

		ctx.ActiveSnap = null;
		view.Invalidate();
	}

	private static SKPoint GetAnchorForHandle(HandleKind handle, SKRect start)
	{
		return handle switch
		{
			HandleKind.Right => new SKPoint(start.Left, start.MidY),
			HandleKind.Left => new SKPoint(start.Right, start.MidY),
			HandleKind.Bottom => new SKPoint(start.MidX, start.Top),
			HandleKind.Top => new SKPoint(start.MidX, start.Bottom),

			HandleKind.BottomRight => new SKPoint(start.Left, start.Top),
			HandleKind.BottomLeft => new SKPoint(start.Right, start.Top),
			HandleKind.TopRight => new SKPoint(start.Left, start.Bottom),
			HandleKind.TopLeft => new SKPoint(start.Right, start.Bottom),

			_ => new SKPoint(start.MidX, start.MidY)
		};
	}

	private static SKPoint ComputeProportionalScaleFromHandle(
		HandleKind handle,
		SKRect bounds,
		SKPoint pivot,
		SKPoint currentPage,
		bool symmetric)
	{
		float startDxLeft = bounds.Left - pivot.X;
		float startDxRight = bounds.Right - pivot.X;
		float startDyTop = bounds.Top - pivot.Y;
		float startDyBottom = bounds.Bottom - pivot.Y;

		float curDx = currentPage.X - pivot.X;
		float curDy = currentPage.Y - pivot.Y;

		float sx;
		float sy;

		switch (handle)
		{
			case HandleKind.Left:
				sx = SafeDiv(curDx, startDxLeft);
				return new SKPoint(sx, sx);

			case HandleKind.Right:
				sx = SafeDiv(curDx, startDxRight);
				return new SKPoint(sx, sx);

			case HandleKind.Top:
				sy = SafeDiv(curDy, startDyTop);
				return new SKPoint(sy, sy);

			case HandleKind.Bottom:
				sy = SafeDiv(curDy, startDyBottom);
				return new SKPoint(sy, sy);

			case HandleKind.TopLeft:
				{
					sx = SafeDiv(curDx, startDxLeft);
					sy = SafeDiv(curDy, startDyTop);
					float s = MathF.Abs(sx) >= MathF.Abs(sy) ? sx : sy;
					return new SKPoint(s, s);
				}

			case HandleKind.TopRight:
				{
					sx = SafeDiv(curDx, startDxRight);
					sy = SafeDiv(curDy, startDyTop);
					float s = MathF.Abs(sx) >= MathF.Abs(sy) ? sx : sy;
					return new SKPoint(s, s);
				}

			case HandleKind.BottomRight:
				{
					sx = SafeDiv(curDx, startDxRight);
					sy = SafeDiv(curDy, startDyBottom);
					float s = MathF.Abs(sx) >= MathF.Abs(sy) ? sx : sy;
					return new SKPoint(s, s);
				}

			case HandleKind.BottomLeft:
				{
					sx = SafeDiv(curDx, startDxLeft);
					sy = SafeDiv(curDy, startDyBottom);
					float s = MathF.Abs(sx) >= MathF.Abs(sy) ? sx : sy;
					return new SKPoint(s, s);
				}

			default:
				return new SKPoint(1f, 1f);
		}
	}

	private static float SafeDiv(float a, float b, float fallback = 1f)
	{
		return MathF.Abs(b) < 0.000001f ? fallback : a / b;
	}

	private static SKMatrix CreateScaleAroundPivot(float sx, float sy, SKPoint pivot)
	{
		var toOrigin = SKMatrix.CreateTranslation(-pivot.X, -pivot.Y);
		var scale = SKMatrix.CreateScale(sx, sy);
		var fromOrigin = SKMatrix.CreateTranslation(pivot.X, pivot.Y);

		return Geometry.Multiply(fromOrigin, Geometry.Multiply(scale, toOrigin));
	}

	private static SKPoint Add(SKPoint a, SKPoint b)
	{
		return new SKPoint(a.X + b.X, a.Y + b.Y);
	}
}

public sealed class RotateSelectionOperation : DragOperation
{
	private float _startAngle;
	private readonly Dictionary<Shape, SKMatrix> _originalWorld = new();

	public override DragOperationKind Kind => DragOperationKind.RotateSelection;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_originalWorld.Clear();
		_originalFill.Clear();

		foreach (var s in ctx.Selection)
		{
			_originalWorld[s] = s.GetWorldTransform();
			CaptureFillPointsDeep(s, _originalFill);
		}

		_startAngle = GetAngle(ctx.RotationPivot, pagePoint);
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		float currentAngle = GetAngle(ctx.RotationPivot, pagePoint);
		float delta = currentAngle - _startAngle;

		var rotation = CreateRotationAround(ctx.RotationPivot, delta);

		foreach (var pair in _originalWorld)
		{
			var shape = pair.Key;
			var originalWorld = pair.Value;

			RestoreOriginalWorldTransform(shape, originalWorld);
			RestoreOriginalFillPointsDeep(shape, _originalFill);

			shape.ApplyWorldTransform(rotation);
			TransformFillPointsDeep(shape, _originalFill, rotation, Kind);
		}

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		foreach (var pair in _originalWorld)
		{
			RestoreOriginalWorldTransform(pair.Key, pair.Value);
			RestoreOriginalFillPointsDeep(pair.Key, _originalFill);
		}

		view.Invalidate();
	}

	private static float GetAngle(SKPoint pivot, SKPoint p)
	{
		return MathF.Atan2(p.Y - pivot.Y, p.X - pivot.X);
	}

	private static SKMatrix CreateRotationAround(SKPoint pivot, float radians)
	{
		var t1 = SKMatrix.CreateTranslation(-pivot.X, -pivot.Y);
		var r = SKMatrix.CreateRotation(radians);
		var t2 = SKMatrix.CreateTranslation(pivot.X, pivot.Y);

		return Geometry.Multiply(t2, Geometry.Multiply(r, t1));
	}
}

public sealed class MoveRotationPivotOperation : DragOperation
{
	public override DragOperationKind Kind => DragOperationKind.MoveRotationCenter;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		SelectionHelper.SetCustomPivot(ctx, pagePoint);
	}
}

public sealed class MoveCurvePointOperation : DragOperation
{
	private readonly Shape? _shape;
	private readonly int _pointIndex;

	public MoveCurvePointOperation(Shape? shape, int pointIndex)
	{
		_shape = shape;
		_pointIndex = pointIndex;
	}

	public override DragOperationKind Kind => DragOperationKind.MoveCurvePoint;
}

public static class LayerArrangeHelper
{
	public static bool BringToFront(Page page, IList<Shape> selection)
	{
		if (page == null || selection == null || selection.Count == 0)
			return false;

		var items = selection
			.Where(s => s != null)
			.Distinct()
			.ToList();

		if (items.Count == 0)
			return false;

		if (!HaveSameParent(items))
			return false;

		var parent = items[0].Parent;
		var container = GetContainer(page, parent);
		if (container == null || container.Count == 0)
			return false;

		var selectedSet = new HashSet<Shape>(items);

		var selectedInOrder = container
			.Where(selectedSet.Contains)
			.ToList();

		if (selectedInOrder.Count == 0)
			return false;

		var unselected = container
			.Where(s => !selectedSet.Contains(s))
			.ToList();

		container.Clear();
		container.AddRange(unselected);
		container.AddRange(selectedInOrder);

		NormalizeLayers(container);
		SortContainer(page, parent);

		return true;
	}

	public static bool SendToBack(Page page, IList<Shape> selection)
	{
		if (page == null || selection == null || selection.Count == 0)
			return false;

		var items = selection
			.Where(s => s != null)
			.Distinct()
			.ToList();

		if (items.Count == 0)
			return false;

		if (!HaveSameParent(items))
			return false;

		var parent = items[0].Parent;
		var container = GetContainer(page, parent);
		if (container == null || container.Count == 0)
			return false;

		var selectedSet = new HashSet<Shape>(items);

		var selectedInOrder = container
			.Where(selectedSet.Contains)
			.ToList();

		if (selectedInOrder.Count == 0)
			return false;

		var unselected = container
			.Where(s => !selectedSet.Contains(s))
			.ToList();

		container.Clear();
		container.AddRange(selectedInOrder);
		container.AddRange(unselected);

		NormalizeLayers(container);
		SortContainer(page, parent);

		return true;
	}

	private static bool HaveSameParent(IList<Shape> items)
	{
		if (items.Count <= 1)
			return true;

		var parent = items[0].Parent;

		for (int i = 1; i < items.Count; i++)
		{
			if (!ReferenceEquals(parent, items[i].Parent))
				return false;
		}

		return true;
	}

	private static List<Shape>? GetContainer(Page page, Shape? parent)
	{
		if (parent == null)
			return page.Shapes;

		if (parent is GroupShape group)
			return group.Children;

		return null;
	}

	private static void NormalizeLayers(List<Shape> container)
	{
		for (int i = 0; i < container.Count; i++)
			container[i].Layer = i;
	}

	private static void SortContainer(Page page, Shape? parent)
	{
		if (parent == null)
			page.SortLayers();
		else if (parent is GroupShape group)
			group.SortChildrenByLayer();
	}
}

public static class ShapePropertyAdorner
{
	public readonly record struct PropertyHandleInfo(
		Shape Shape,
		ShapePropertyHandleKind Kind,
		SKPoint WorldPosition);

	public static IReadOnlyList<PropertyHandleInfo> GetHandles(Shape shape)
	{
		if (shape is RoundedRectangleShape rr && rr.IsSelected)
		{
			float rx = GetEffectiveRadiusX(rr);
			float ry = GetEffectiveRadiusY(rr);

			var local = new SKPoint(rx, ry);
			var world = rr.LocalToWorld(local);

			return new[]
			{
			new PropertyHandleInfo(rr, ShapePropertyHandleKind.RoundedRectangleRadius, world)
		};
		}

		if (shape is ChamferedRectangleShape cr && cr.IsSelected)
		{
			float cx = GetEffectiveChamferX(cr);
			float cy = GetEffectiveChamferY(cr);
			var local = new SKPoint(cx, cy);
			var world = cr.LocalToWorld(local);

			return new[]
			{
			new PropertyHandleInfo(cr, ShapePropertyHandleKind.ChamferedRectangleSize, world)
		};
		}

		return Array.Empty<PropertyHandleInfo>();
	}

	public static float GetEffectiveChamferX(ChamferedRectangleShape cr)
	{
		float maxChamferX = cr.LocalRect.Width * 0.5f;
		return Math.Clamp(cr.ChamferX, 0f, maxChamferX);
	}

	public static float GetEffectiveChamferY(ChamferedRectangleShape cr)
	{
		float maxChamferY = cr.LocalRect.Height * 0.5f;
		return Math.Clamp(cr.ChamferY, 0f, maxChamferY);
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count != 1)
			return;

		var shape = ctx.Selection[0];

		foreach (var h in GetHandles(shape))
		{
			var sp = view.PageToScreen(h.WorldPosition);

			using var fill = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Color = new SKColor(255, 255, 255)
			};

			using var stroke = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 2f,
				Color = new SKColor(40, 180, 255)
			};

			using var center = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Color = new SKColor(40, 180, 255)
			};

			canvas.DrawCircle(sp, 7f, fill);
			canvas.DrawCircle(sp, 7f, stroke);
			canvas.DrawCircle(sp, 2.5f, center);
		}
	}

	public static float GetEffectiveRadiusX(RoundedRectangleShape rr)
	{
		float maxRadiusX = rr.LocalRect.Width * 0.5f;
		return Math.Clamp(rr.RadiusX, 0f, maxRadiusX);
	}

	public static float GetEffectiveRadiusY(RoundedRectangleShape rr)
	{
		float maxRadiusY = rr.LocalRect.Height * 0.5f;
		return Math.Clamp(rr.RadiusY, 0f, maxRadiusY);
	}
}

public sealed class AdjustRoundedRectangleRadiusOperation : DragOperation
{
	private RoundedRectangleShape? _shape;

	public override DragOperationKind Kind => DragOperationKind.AdjustRoundedRectangleRadius;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_shape = ctx.Selection.Count == 1
			? ctx.Selection[0] as RoundedRectangleShape
			: null;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (_shape == null)
			return;

		var local = _shape.WorldToLocal(pagePoint);

		float maxRadiusX = _shape.LocalRect.Width * 0.5f;
		float maxRadiusY = _shape.LocalRect.Height * 0.5f;

		_shape.RadiusX = Math.Clamp(local.X, 0f, maxRadiusX);
		_shape.RadiusY = Math.Clamp(local.Y, 0f, maxRadiusY);
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}
}

public sealed class AdjustChamferedRectangleSizeOperation : DragOperation
{
	private ChamferedRectangleShape? _shape;

	public override DragOperationKind Kind => DragOperationKind.AdjustChamferedRectangleSize;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_shape = ctx.Selection.Count == 1
			? ctx.Selection[0] as ChamferedRectangleShape
			: null;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (_shape == null)
			return;

		var local = _shape.WorldToLocal(pagePoint);

		float maxChamferX = _shape.LocalRect.Width * 0.5f;
		float maxChamferY = _shape.LocalRect.Height * 0.5f;

		_shape.ChamferX = Math.Clamp(local.X, 0f, maxChamferX);
		_shape.ChamferY = Math.Clamp(local.Y, 0f, maxChamferY);
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}
}

public static class ShapePointAdorner
{
	public readonly record struct ShapePointInfo(Shape Shape, int PointIndex, SKPoint WorldPosition);

	public static IReadOnlyList<ShapePointInfo> GetPoints(Shape shape)
	{
		if (shape == null)
			return Array.Empty<ShapePointInfo>();

		var result = new List<ShapePointInfo>();

		switch (shape)
		{
			case Circle2PShape c2:
				result.Add(new ShapePointInfo(c2, 0, c2.LocalToWorld(c2.CenterPoint)));
				result.Add(new ShapePointInfo(c2, 1, c2.LocalToWorld(c2.RadiusPoint)));
				break;

			case Circle3PShape c3:
				result.Add(new ShapePointInfo(c3, 0, c3.LocalToWorld(c3.P1)));
				result.Add(new ShapePointInfo(c3, 1, c3.LocalToWorld(c3.P2)));
				result.Add(new ShapePointInfo(c3, 2, c3.LocalToWorld(c3.P3)));
				break;

			case Ellipse3PShape e3:
				result.Add(new ShapePointInfo(e3, 0, e3.LocalToWorld(e3.CenterPoint)));
				result.Add(new ShapePointInfo(e3, 1, e3.LocalToWorld(e3.RadiusPoint1)));
				result.Add(new ShapePointInfo(e3, 2, e3.LocalToWorld(e3.RadiusPoint2)));
				break;

			case Ellipse5PShape e5:
				for (int i = 0; i < e5.Points.Count; i++)
					result.Add(new ShapePointInfo(e5, i, e5.LocalToWorld(e5.Points[i])));
				break;

			case PolygonShape polygon:
				if (polygon.UseExplicitPoints)
				{
					for (int i = 0; i < polygon.Points.Count; i++)
						result.Add(new ShapePointInfo(polygon, i, polygon.LocalToWorld(polygon.Points[i])));
				}
				else
				{
					polygon.InitializeFromPointsIfNeeded();
					result.Add(new ShapePointInfo(polygon, 0, polygon.GetMainHandleWorld()));
				}
				break;

			// Измеряемые точки размера. Вынос подписи своего маркера
			// не имеет: он задаётся третьим щелчком при построении и дальше
			// меняется растягиванием габаритной рамки.
			case MeasurementShape measurement:
				result.Add(new ShapePointInfo(measurement, 0, measurement.LocalToWorld(measurement.P1)));
				result.Add(new ShapePointInfo(measurement, 1, measurement.LocalToWorld(measurement.P2)));
				break;
		}

		return result;
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count != 1)
			return;

		var shape = ctx.Selection[0];
		if (shape == null || !shape.IsVisible || shape.IsLocked)
			return;

		var points = GetPoints(shape);
		if (points.Count == 0)
			return;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(255, 255, 255)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(255, 140, 0)
		};

		using var center = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(255, 140, 0)
		};

		foreach (var p in points)
		{
			var sp = view.PageToScreen(p.WorldPosition);
			canvas.DrawCircle(sp, 6f, fill);
			canvas.DrawCircle(sp, 6f, stroke);
			canvas.DrawCircle(sp, 2.5f, center);
		}
	}
}

public sealed class MoveShapePointOperation : DragOperation
{
	private readonly Shape _shape;
	private readonly int _pointIndex;

	private bool _polygonCaptured;
	private SKPoint _polygonStartCenterLocal;
	private float _polygonStartRadiusX;
	private float _polygonStartRadiusY;
	private float _polygonAspectRatio;

	public MoveShapePointOperation(Shape shape, int pointIndex)
	{
		_shape = shape;
		_pointIndex = pointIndex;
	}

	public override DragOperationKind Kind => DragOperationKind.MoveShapePoint;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ctx.ActiveSnap = null;

		if (_shape is PolygonShape polygon && !polygon.UseExplicitPoints)
		{
			polygon.InitializeFromPointsIfNeeded();

			_polygonStartCenterLocal = polygon.LocalCenter;
			_polygonStartRadiusX = MathF.Max(0.0001f, polygon.RadiusX);
			_polygonStartRadiusY = MathF.Max(0.0001f, polygon.RadiusY);
			_polygonAspectRatio = _polygonStartRadiusY / _polygonStartRadiusX;
			_polygonCaptured = true;
		}
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		SKPoint targetPoint = pagePoint;
		ctx.ActiveSnap = null;

		// У размера концы ловятся только на точки привязки, без обычного
		// прилипания к объектам.
		//
		// Причина в цене. SnapMovingPoint собирает точки и линии со всей
		// страницы на каждое движение мыши, а у фигур с текстом это тянет
		// за собой раскладку и построение контуров глифов. На странице с текстом
		// это десятки тысяч точек на кадр, и перетаскивание начинает заметно
		// тормозить.
		//
		// Потери нет: точки привязки уже покрывают вершины, середины рёбер,
		// стороны света и центры всех фигур — то есть ровно то, между чем имеет
		// смысл измерять расстояние. Считаются они по габаритам и узлам,
		// к глифам не обращаются.
		if (_shape is MeasurementShape anchored)
		{
			if (TryAnchorMeasurement(view, ctx, anchored, pagePoint))
			{
				view.Invalidate();
				return;
			}

			UpdateMeasurement(anchored, pagePoint);
			view.Invalidate();
			return;
		}

		// Alt, а не Ctrl: отключение прилипания на время протяжки повсюду
		// на Alt. Ctrl в этой операции не участвует вовсе.
		if (!ctx.AltPressed && ObjectSnapEngine.AnySnappingEnabled)
		{
			var snap = ObjectSnapEngine.SnapMovingPoint(
				view,
				new[] { _shape },
				pagePoint,
				Settings.snap_dist);

			ctx.ActiveSnap = snap;

			if (snap != null && snap.IsValid)
				targetPoint = Add(pagePoint, snap.Correction);
		}

		switch (_shape)
		{
			case Circle2PShape c2:
				UpdateCircle2P(c2, targetPoint);
				break;

			case Circle3PShape c3:
				UpdateCircle3P(c3, targetPoint);
				break;

			case Ellipse3PShape e3:
				UpdateEllipse3P(e3, targetPoint);
				break;

			case Ellipse5PShape e5:
				UpdateEllipse5P(e5, targetPoint);
				break;

			case PolygonShape polygon:
				UpdatePolygon(polygon, targetPoint);
				break;

			case CurveShape curve:
				UpdateCurve(curve, targetPoint);
				break;
		}

		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
		ctx.ActiveSnap = null;

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		ctx.ActiveSnap = null;
		view.Invalidate();
	}

	private void UpdateCurve(CurveShape shape, SKPoint worldPoint)
	{
		if (_pointIndex < 0 || _pointIndex >= shape.Nodes.Count)
			return;

		shape.MoveNode(_pointIndex, shape.WorldToLocal(worldPoint));
	}

	private void UpdateCircle2P(Circle2PShape shape, SKPoint worldPoint)
	{
		var pts = new[]
		{
			shape.LocalToWorld(shape.CenterPoint),
			shape.LocalToWorld(shape.RadiusPoint)
		};

		if (_pointIndex < 0 || _pointIndex >= pts.Length)
			return;

		pts[_pointIndex] = worldPoint;

		var rebuilt = Circle2PShape.CreateFromPoints(pts[0], pts[1]);

		shape.LocalTransform = rebuilt.LocalTransform;
		shape.LocalCenter = rebuilt.LocalCenter;
		shape.Radius = rebuilt.Radius;
		shape.CenterPoint = rebuilt.CenterPoint;
		shape.RadiusPoint = rebuilt.RadiusPoint;
	}

	private void UpdateCircle3P(Circle3PShape shape, SKPoint worldPoint)
	{
		var pts = new[]
		{
			shape.LocalToWorld(shape.P1),
			shape.LocalToWorld(shape.P2),
			shape.LocalToWorld(shape.P3)
		};

		if (_pointIndex < 0 || _pointIndex >= pts.Length)
			return;

		pts[_pointIndex] = worldPoint;

		var rebuilt = Circle3PShape.CreateNormalized(pts[0], pts[1], pts[2]);
		shape.LocalTransform = rebuilt.LocalTransform;
		shape.P1 = rebuilt.P1;
		shape.P2 = rebuilt.P2;
		shape.P3 = rebuilt.P3;
	}

	private void UpdateEllipse3P(Ellipse3PShape shape, SKPoint worldPoint)
	{
		var center = shape.LocalToWorld(shape.CenterPoint);
		var p1 = shape.LocalToWorld(shape.RadiusPoint1);
		var p2 = shape.LocalToWorld(shape.RadiusPoint2);

		switch (_pointIndex)
		{
			case 0:
				{
					SKPoint d1 = new SKPoint(p1.X - center.X, p1.Y - center.Y);
					SKPoint d2 = new SKPoint(p2.X - center.X, p2.Y - center.Y);

					center = worldPoint;
					p1 = new SKPoint(center.X + d1.X, center.Y + d1.Y);
					p2 = new SKPoint(center.X + d2.X, center.Y + d2.Y);
					break;
				}

			case 1:
				{
					p1 = worldPoint;

					float vx = p1.X - center.X;
					float vy = p1.Y - center.Y;
					float len = MathF.Sqrt(vx * vx + vy * vy);
					if (len < 0.0001f)
						return;

					float ux = vx / len;
					float uy = vy / len;
					float nx = -uy;
					float ny = ux;

					float w2x = p2.X - center.X;
					float w2y = p2.Y - center.Y;
					float signed = w2x * nx + w2y * ny;
					p2 = new SKPoint(center.X + nx * signed, center.Y + ny * signed);
					break;
				}

			case 2:
				{
					float vx = p1.X - center.X;
					float vy = p1.Y - center.Y;
					float len = MathF.Sqrt(vx * vx + vy * vy);
					if (len < 0.0001f)
						return;

					float ux = vx / len;
					float uy = vy / len;
					float nx = -uy;
					float ny = ux;

					float wx = worldPoint.X - center.X;
					float wy = worldPoint.Y - center.Y;
					float signed = wx * nx + wy * ny;

					p2 = new SKPoint(center.X + nx * signed, center.Y + ny * signed);
					break;
				}

			default:
				return;
		}

		var rebuilt = Ellipse3PShape.CreateFromCenterAndAxes(center, p1, p2);

		shape.LocalTransform = rebuilt.LocalTransform;
		shape.LocalCenter = rebuilt.LocalCenter;
		shape.RadiusX = rebuilt.RadiusX;
		shape.RadiusY = rebuilt.RadiusY;
		shape.CenterPoint = rebuilt.CenterPoint;
		shape.RadiusPoint1 = rebuilt.RadiusPoint1;
		shape.RadiusPoint2 = rebuilt.RadiusPoint2;
	}

	private void UpdateEllipse5P(Ellipse5PShape shape, SKPoint worldPoint)
	{
		var pts = shape.Points.Select(p => shape.LocalToWorld(p)).ToList();
		if (_pointIndex < 0 || _pointIndex >= pts.Count)
			return;

		pts[_pointIndex] = worldPoint;

		var rebuilt = Ellipse5PShape.CreateFromFivePoints(pts);
		if (rebuilt == null)
			return;

		shape.LocalTransform = rebuilt.LocalTransform;
		shape.LocalCenter = rebuilt.LocalCenter;
		shape.RadiusX = rebuilt.RadiusX;
		shape.RadiusY = rebuilt.RadiusY;

		shape.Points.Clear();
		foreach (var p in pts)
			shape.Points.Add(rebuilt.WorldToLocal(p));
	}

	private void UpdatePolygon(PolygonShape shape, SKPoint worldPoint)
	{
		if (shape.UseExplicitPoints)
		{
			if (_pointIndex < 0 || _pointIndex >= shape.Points.Count)
				return;

			shape.Points[_pointIndex] = shape.WorldToLocal(worldPoint);
			return;
		}

		if (_pointIndex != 0)
			return;

		if (!_polygonCaptured)
		{
			shape.InitializeFromPointsIfNeeded();

			_polygonStartCenterLocal = shape.LocalCenter;
			_polygonStartRadiusX = MathF.Max(0.0001f, shape.RadiusX);
			_polygonStartRadiusY = MathF.Max(0.0001f, shape.RadiusY);
			_polygonAspectRatio = _polygonStartRadiusY / _polygonStartRadiusX;
			_polygonCaptured = true;
		}

		var localPoint = shape.WorldToLocal(worldPoint);

		float dx = localPoint.X - _polygonStartCenterLocal.X;
		float dy = localPoint.Y - _polygonStartCenterLocal.Y;

		float len = MathF.Sqrt(dx * dx + dy * dy);
		if (len < 0.0001f)
			return;

		float angle = MathF.Atan2(dy, dx);
		float ca = MathF.Cos(angle);
		float sa = MathF.Sin(angle);

		float aspect = MathF.Max(0.0001f, _polygonAspectRatio);
		float denom = MathF.Sqrt(ca * ca + (aspect * sa) * (aspect * sa));
		if (denom < 0.0001f)
			return;

		float radiusX = len / denom;
		float radiusY = radiusX * aspect;

		shape.LocalCenter = _polygonStartCenterLocal;
		shape.RotationRad = angle;
		shape.RadiusX = MathF.Max(0.0001f, radiusX);
		shape.RadiusY = MathF.Max(0.0001f, radiusY);
		shape.RebuildPoints();
	}

	/// <summary>
	/// Перенос измеряемой точки размера.
	///
	/// Матрица фигуры не перестраивается, меняется только сама точка:
	/// вынос H задан в локальных единицах вдоль нормали к вектору P1-P2,
	/// и при сдвиге конца размерная линия сама поворачивается вместе с ним,
	/// сохраняя расстояние до измеряемого отрезка. Пересборка матрицы, как
	/// у окружности или эллипса, здесь только сбивала бы показания X и Y
	/// в панели свойств посередине перетаскивания.
	/// </summary>
	private void UpdateMeasurement(MeasurementShape shape, SKPoint worldPoint)
	{
		if (_pointIndex < 0 || _pointIndex > 1)
			return;

		shape.SetMeasurePoint(_pointIndex == 0, shape.WorldToLocal(worldPoint));
	}

	/// <summary>
	/// Привязывает перетаскиваемый конец размера к ближайшей точке измерения.
	///
	/// Возвращает true, если привязка найдена и точка уже расставлена —
	/// тогда обычное прилипание пропускается.
	///
	/// Settings.snap_objects здесь сознательно не проверяется, как и у связей
	/// во flowchart: привязка конца размера нужна и тогда, когда прилипание
	/// выключено. Alt временно отключает и её.
	/// </summary>
	private bool TryAnchorMeasurement(
		MainView view,
		EditorInteractionContext ctx,
		MeasurementShape shape,
		SKPoint pagePoint)
	{
		if (_pointIndex < 0 || _pointIndex > 1)
			return false;

		bool first = _pointIndex == 0;
		var anchor = first ? shape.FirstAnchor : shape.LastAnchor;

		// Alt, а не Ctrl. Протяжка конца размера проходит через эту ветку
		// И через обычное прилипание ниже, и пока они слушали разные
		// клавиши, Alt гасил одну половину, Ctrl — другую, а отпустить
		// точку целиком не удавалось ни тем, ни другим.
		if (ctx.AltPressed || !anchor.Enabled)
			return false;

		// includeCenter: у размера центр фигуры — полноценная точка измерения.
		// includeGroupChildren: мерить надо детали внутри группы, а не рамку
		// вокруг них.
		if (ObjectSnapEngine.TryFindAnchor(
				view, shape, pagePoint, Settings.snap_dist,
				out var target, out var kind, out int index, out var anchorPoint,
				out var nodeId,
				includeCenter: true,
				includeGroupChildren: true))
		{
			shape.SetAnchor(first, target, kind, index, nodeId);
			shape.SetMeasurePoint(first, shape.WorldToLocal(anchorPoint));

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			MainForm.sel.NotifyGeometryChanged();
			return true;
		}

		// Увели конец от всех точек привязки — связь снимается, дальше
		// точка ставится обычным путём.
		shape.ClearAnchor(first);
		return false;
	}

	private static SKPoint Add(SKPoint a, SKPoint b)
	{
		return new SKPoint(a.X + b.X, a.Y + b.Y);
	}
}

public static class FillAdorner
{
	private const float HandleRadiusPx = 5f;

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext context)
	{
		if (context.Selection.Count != 1)
			return;

		var shape = context.Selection[0];
		if (shape == null || shape.Fill == null || !shape.Fill.SupportsInteractiveHandles)
			return;

		var bounds = shape.GetBounds();
		shape.Fill.EnsureFillTransformDefaults(bounds);

		var p1 = view.PageToScreen(shape.Fill.StartPoint);
		var p2 = view.PageToScreen(shape.Fill.EndPoint);

		bool startHot =
			context.HotHit.Kind == HitKind.FillStartHandle &&
			ReferenceEquals(context.HotHit.Shape, shape);

		bool endHot =
			context.HotHit.Kind == HitKind.FillEndHandle &&
			ReferenceEquals(context.HotHit.Shape, shape);

		using var line = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			Color = new SKColor(40, 40, 40, 220)
		};

		using var startFill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = startHot ? new SKColor(255, 180, 60) : new SKColor(160, 90, 255)
		};

		using var endFill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = endHot ? new SKColor(255, 180, 60) : new SKColor(160, 90, 255)
		};

		using var startStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = startHot ? 2.5f : 1.5f,
			Color = SKColors.Black
		};

		using var endStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = endHot ? 2.5f : 1.5f,
			Color = SKColors.Black
		};

		float startRadius = startHot ? 7f : HandleRadiusPx;
		float endRadius = endHot ? 7f : HandleRadiusPx;

		canvas.DrawLine(p1, p2, line);

		if (shape.Fill.Kind == FillKind.Pattern || shape.Fill.Kind == FillKind.Texture)
		{
			var size = shape.Fill.GetPatternSourceSize();

			float sw = MathF.Max(1f, size.Width);
			float sh = MathF.Max(1f, size.Height);

			var start = shape.Fill.StartPoint;
			var end = shape.Fill.EndPoint;

			float dx = end.X - start.X;
			float dy = end.Y - start.Y;
			float diagonal = MathF.Sqrt(dx * dx + dy * dy);

			if (diagonal > 0.001f)
			{
				float srcDiagonal = MathF.Sqrt(sw * sw + sh * sh);
				float scale = diagonal / srcDiagonal;

				float angle = MathF.Atan2(dy, dx) - MathF.Atan2(sh, sw);
				float cos = MathF.Cos(angle);
				float sin = MathF.Sin(angle);

				// Углы плитки в её собственных координатах.
				SKPoint Corner(float lx, float ly) => view.PageToScreen(new SKPoint(
					start.X + (lx * cos - ly * sin) * scale,
					start.Y + (lx * sin + ly * cos) * scale));

				var c00 = Corner(0f, 0f);
				var c10 = Corner(sw, 0f);
				var c11 = Corner(sw, sh);
				var c01 = Corner(0f, sh);

				using var tileOutline = new SKPaint
				{
					IsAntialias = true,
					Style = SKPaintStyle.Stroke,
					StrokeWidth = 1f,
					Color = new SKColor(160, 90, 255, 200),
					PathEffect = SKPathEffect.CreateDash(new float[] { 4f, 3f }, 0f)
				};

				canvas.DrawLine(c00, c10, tileOutline);
				canvas.DrawLine(c10, c11, tileOutline);
				canvas.DrawLine(c11, c01, tileOutline);
				canvas.DrawLine(c01, c00, tileOutline);
			}
		}


		canvas.DrawCircle(p1, startRadius, startFill);
		canvas.DrawCircle(p1, startRadius, startStroke);

		canvas.DrawCircle(p2, endRadius, endFill);
		canvas.DrawCircle(p2, endRadius, endStroke);
	}

	public static FillHandleKind HitTest(MainView view, Shape shape, SKPoint pagePoint, float hitRadiusPx = 8f)
	{
		if (shape.Fill == null || !shape.Fill.SupportsInteractiveHandles)
			return FillHandleKind.None;

		var bounds = shape.GetBounds();
		shape.Fill.EnsureFillTransformDefaults(bounds);

		var startScreen = view.PageToScreen(shape.Fill.StartPoint);
		var endScreen = view.PageToScreen(shape.Fill.EndPoint);
		var mouseScreen = view.PageToScreen(pagePoint);

		if (Geometry.Distance(mouseScreen, startScreen) <= hitRadiusPx)
			return FillHandleKind.Start;

		if (Geometry.Distance(mouseScreen, endScreen) <= hitRadiusPx)
			return FillHandleKind.End;

		return FillHandleKind.None;
	}
}

public sealed class MoveFillHandleOperation : DragOperation
{
	private readonly Shape _shape;
	private readonly FillHandleKind _kind;

	private SKPoint _startFillStartPoint;
	private SKPoint _startFillEndPoint;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveFillHandle;

	public MoveFillHandleOperation(Shape shape, FillHandleKind kind)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
		_kind = kind;
	}

	public override void Begin(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (_shape.Fill == null)
			return;

		// Значения по умолчанию должны быть рассчитаны до того, как мы их запомним.
		_shape.Fill.EnsureFillTransformDefaults(_shape.GetBounds());

		_startFillStartPoint = _shape.Fill.StartPoint;
		_startFillEndPoint = _shape.Fill.EndPoint;
		_shape.Fill.FillTransformInitialized = true;
		_begun = true;
	}

	public override void Update(MainView view, EditorInteractionContext context, SKPoint pagePoint)
	{
		if (!_begun || _shape.Fill == null)
			return;

		var fill = _shape.Fill;
		fill.FillTransformInitialized = true;

		switch (_kind)
		{
			case FillHandleKind.Start:
				// Свободное перемещение. Меняется и начало плитки, и вектор,
				// то есть заодно размер с углом — это и есть двухточечное
				// управление заливкой.
				fill.StartPoint = pagePoint;
				fill.EndPoint = _startFillEndPoint;
				break;

			case FillHandleKind.End:
				fill.StartPoint = _startFillStartPoint;
				fill.EndPoint = pagePoint;
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
		if (_shape.Fill != null && _begun)
		{
			_shape.Fill.StartPoint = _startFillStartPoint;
			_shape.Fill.EndPoint = _startFillEndPoint;
			_shape.Fill.FillTransformInitialized = true;
		}

		view.Invalidate();
	}
}

public static class TextAlongShapeAdorner
{
	public readonly record struct TextAlongHandleInfo(HandleKind Kind, SKPoint WorldPosition, float RotationRad);

	public static bool IsApplicable(Shape? shape)
	{
		return shape is TextShape ts && ts.AlongShape && ts.otherShape != null;
	}

	public static IReadOnlyList<TextAlongHandleInfo> GetHandles(MainView view, TextShape shape)
	{
		// bounds текста в мировых координатах
		var worldBounds = shape.GetBounds();
		if (worldBounds.IsEmpty)
			return Array.Empty<TextAlongHandleInfo>();

		// базовый оффсет для стандартных квадратных handle'ов (как в SelectionAdorner)
		float handleOffsetUom = view.ScreenPixelsToPageMm(Settings.handle_size);
		// треугольники рисуем дальше, чем resize-handles
		float triOffsetUom = handleOffsetUom * 2f;

		var left = new SKPoint(worldBounds.Left - triOffsetUom, worldBounds.MidY);
		var right = new SKPoint(worldBounds.Right + triOffsetUom, worldBounds.MidY);
		var top = new SKPoint(worldBounds.MidX, worldBounds.Top - triOffsetUom);
		var bottom = new SKPoint(worldBounds.MidX, worldBounds.Bottom + triOffsetUom);

		return new[]
		{
		new TextAlongHandleInfo(HandleKind.TextAlongAxLeft,   left,   -MathF.PI / 2f),
		new TextAlongHandleInfo(HandleKind.TextAlongAxRight,  right,   MathF.PI / 2f),
		new TextAlongHandleInfo(HandleKind.TextAlongAyTop,    top,     0f),
		new TextAlongHandleInfo(HandleKind.TextAlongAyBottom, bottom,  MathF.PI)
	};
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count != 1 || ctx.Selection[0] is not TextShape ts || !IsApplicable(ts))
			return;

		using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Settings.text_handle_fill_color };
		using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Settings.handle_line_thickness, Color = Settings.text_handle_line_color };

		float triSize = Settings.handle_size;

		foreach (var h in GetHandles(view, ts))
		{
			var sp = view.PageToScreen(h.WorldPosition);
			DrawTriangle(canvas, sp, triSize, h.RotationRad, fill, stroke);
		}
	}

	private static void DrawTriangle(SKCanvas canvas, SKPoint center, float size, float rotationRad, SKPaint fill, SKPaint stroke)
	{
		var p0 = RotatePoint(new SKPoint(0, -size * 2.5f), rotationRad) + center; // top corner
		var p1 = RotatePoint(new SKPoint(-size * 0.85f, -size * 0.5f), rotationRad) + center; // Left corner
		var p2 = RotatePoint(new SKPoint(size * 0.85f, -size * 0.5f), rotationRad) + center; // Right corner

		using var path = new SKPath();
		path.MoveTo(p0);
		path.LineTo(p1);
		path.LineTo(p2);
		path.Close();

		canvas.DrawPath(path, fill);
		canvas.DrawPath(path, stroke);
	}

	private static SKPoint RotatePoint(SKPoint p, float radians)
	{
		float cos = MathF.Cos(radians);
		float sin = MathF.Sin(radians);
		return new SKPoint(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
	}

	public static HandleKind HitTest(MainView view, TextShape shape, SKPoint pagePoint, float hitRadiusPx = 10f)
	{
		if (!IsApplicable(shape))
			return HandleKind.None;

		var mouseScreen = view.PageToScreen(pagePoint);

		foreach (var h in GetHandles(view, shape))
		{
			var sp = view.PageToScreen(h.WorldPosition);
			if (Geometry.Distance(mouseScreen, sp) <= hitRadiusPx)
				return h.Kind;
		}

		return HandleKind.None;
	}
}

public sealed class AdjustTextAlongShapeAxOperation : DragOperation
{
	private readonly TextShape shape;
	private SKPoint startMousePage;
	private float startAx;

	public AdjustTextAlongShapeAxOperation(TextShape shape)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override DragOperationKind Kind => DragOperationKind.AdjustTextAlongShapeAx;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		startMousePage = pagePoint;
		startAx = shape.ax;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		float dx = pagePoint.X - startMousePage.X;
		shape.ax = startAx + dx;
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		shape.ax = startAx;
		view.Invalidate();
	}
}

public sealed class AdjustTextAlongShapeAyOperation : DragOperation
{
	private readonly TextShape shape;
	private SKPoint startMousePage;
	private float startAy;

	public AdjustTextAlongShapeAyOperation(TextShape shape)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
	}

	public override DragOperationKind Kind => DragOperationKind.AdjustTextAlongShapeAy;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		startMousePage = pagePoint;
		startAy = shape.ay;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		float dy = pagePoint.Y - startMousePage.Y;
		shape.ay = startAy + dy;
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		shape.ay = startAy;
		view.Invalidate();
	}
}

/// <summary>
/// Инструмент измерения расстояний на странице в единицах Page.uom.
///
/// Работает в три щелчка:
///   1) откуда измерять;
///   2) до какой точки измерять;
///   3) на какой высоте расположить подпись с измеренным значением.
///
/// После первого щелчка фигура уже добавлена на страницу и обновляется вслед
/// за курсором. Так пользователь видит результат до фиксации, а не после —
/// как и у остальных инструментов создания фигур в проекте.
/// </summary>
public sealed class MeasureTool : IEditorTool
{
	#region Константы

	/// <summary>Вынос подписи по умолчанию, пока третий щелчок не сделан.</summary>
	private const float DefaultOffset = 10f;

	#endregion

	#region Поля

	private MeasurementShape? _shape;
	private SKPoint _p1;
	private SKPoint _p2;
	private int _clickCount;

	// Найденные привязки концов. Хранятся в инструменте, а не сразу в фигуре:
	// первая точка выбирается до того, как фигура вообще создана.
	private Shape? _target1;
	private ShapeAnchorKind _kind1;
	private int _index1;
	private Guid _nodeId1;

	/// <summary>
	/// Мировая точка привязки к поверхности, если Kind оказался Surface.
	///
	/// Хранится точкой, а не долями: доли считает сама привязка, и делать
	/// это дважды — в инструменте и в фигуре — значит завести два места,
	/// где формула может разойтись.
	/// </summary>
	private SKPoint _surface1;
	private SKPoint _surface2;

	private Shape? _target2;
	private ShapeAnchorKind _kind2;
	private int _index2;
	private Guid _nodeId2;

	/// <summary>
	/// Точка привязки под курсором. Пересчитывается на каждое движение
	/// и показывает, куда именно встанет конец при щелчке: без индикации
	/// прилипание срабатывает незаметно, и промах виден только по готовой
	/// фигуре.
	/// </summary>
	private bool _hoverHasAnchor;
	private SKPoint _hoverAnchor;
	private ShapeAnchorKind _hoverAnchorKind;
	private Shape? _hoverTarget;
	private int _hoverIndex;

	#endregion

	#region Свойства

	public ShapeToolKind Kind => ShapeToolKind.Measure;

	#endregion

	#region Обработка ввода

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view.CurrentPage == null)
			return;

		if (button == MouseButtons.Right)
		{
			Cancel(view, ctx);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		if (_clickCount == 0)
		{
			_p1 = ResolveAnchor(view, ctx, pagePoint, out _target1, out _kind1, out _index1, out _nodeId1);

			// Точка привязки к поверхности — это сам конец: он остаётся
			// там, куда его привели, и ни к чему не притягивается.
			_surface1 = _p1;

			_p2 = _p1;
			_surface2 = _p1;
			_clickCount = 1;
		}
		else if (_clickCount == 1)
		{
			_p2 = ResolveAnchor(view, ctx, pagePoint, out _target2, out _kind2, out _index2, out _nodeId2);
			_surface2 = _p2;
			_clickCount = 2;
		}
		else
		{
			CreateOrUpdateShape(view, ctx, pagePoint);
			Finish(view, ctx);
			return;
		}

		CreateOrUpdateShape(view, ctx, pagePoint);
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (view.CurrentPage == null)
			return;

		if (_clickCount == 1)
		{
			_p2 = ResolveAnchor(view, ctx, pagePoint, out _target2, out _kind2, out _index2, out _nodeId2);
			_surface2 = _p2;
		}
		else
		{
			// До первого щелчка и после второго фигура не строится, но
			// подсветка обязана жить: она показывает, куда встанет конец.
			ResolveAnchor(view, ctx, pagePoint, out _, out _, out _, out _);
		}

		if (_clickCount == 0)
		{
			view.Invalidate();
			return;
		}

		CreateOrUpdateShape(view, ctx, pagePoint);
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

	#endregion

	#region Отрисовка подсказок

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		// Подсветка рисуется всегда, в том числе до первого щелчка:
		// именно она показывает, что инструмент готов прилипнуть к точке.
		DrawHoverAnchor(canvas, view);

		if (_clickCount == 0)
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

		DrawMarker(canvas, view, _p1, fill, stroke);

		if (_clickCount >= 2)
			DrawMarker(canvas, view, _p2, fill, stroke);
	}

	private static void DrawMarker(SKCanvas canvas, MainView view, SKPoint pagePoint, SKPaint fill, SKPaint stroke)
	{
		var sp = view.PageToScreen(pagePoint);
		canvas.DrawCircle(sp, 4f, fill);
		canvas.DrawCircle(sp, 4f, stroke);
	}

	/// <summary>
	/// Подсветка точки, к которой прилипнет конец размера.
	///
	/// Рисуется в ЭКРАННЫХ координатах: размер маркера должен быть
	/// одинаковым на любом масштабе, а в мировых единицах он таял бы
	/// при отдалении.
	///
	/// Форма кодирует вид точки, как принято в объектных привязках
	/// чертёжных редакторов: квадрат — узел, треугольник — середина
	/// ребра, круг — центр и стороны света. У середины ребра вдобавок
	/// подсвечивается само ребро: иначе непонятно, какого из соседних
	/// сегментов эта середина.
	/// </summary>
	private void DrawHoverAnchor(SKCanvas canvas, MainView view)
	{
		if (!_hoverHasAnchor)
			return;

		if (_hoverAnchorKind == ShapeAnchorKind.EdgeMidpoint)
			DrawHoveredEdge(canvas, view);

		var sp = view.PageToScreen(_hoverAnchor);

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(255, 200, 60, 230)
		};

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = new SKColor(40, 40, 40, 230)
		};

		switch (_hoverAnchorKind)
		{
			case ShapeAnchorKind.Vertex:
				{
					var rect = new SKRect(sp.X - 4.5f, sp.Y - 4.5f, sp.X + 4.5f, sp.Y + 4.5f);

					canvas.DrawRect(rect, fill);
					canvas.DrawRect(rect, stroke);
					break;
				}

			case ShapeAnchorKind.EdgeMidpoint:
				{
					using var tri = new SKPath();

					tri.MoveTo(sp.X, sp.Y - 3.6f);
					tri.LineTo(sp.X + 3.3f, sp.Y + 2.7f);
					tri.LineTo(sp.X - 3.3f, sp.Y + 2.7f);
					tri.Close();

					canvas.DrawPath(tri, fill);
					canvas.DrawPath(tri, stroke);
					break;
				}

			default:
				canvas.DrawCircle(sp, 4.5f, fill);
				canvas.DrawCircle(sp, 4.5f, stroke);
				break;
		}
	}

	/// <summary>
	/// Ребро, серединой которого является точка привязки.
	///
	/// Концы ребра берутся из того же перечисления точек фигуры, что
	/// использовала привязка: узел с тем же номером и следующий за ним.
	/// </summary>
	private void DrawHoveredEdge(SKCanvas canvas, MainView view)
	{
		if (_hoverTarget == null)
			return;

		// Концы ребра берутся из того же перечисления, что использовала
		// сама привязка: LocalA и LocalB у точки EdgeMidpoint — это и есть
		// концы ребра. Считать их заново по габаритам значило бы
		// продублировать логику, которая для кривых, прямоугольников
		// и эллипсов разная.
		var anchors = ShapeAnchorGeometry.GetLocalAnchors(_hoverTarget);

		SKPoint a = SKPoint.Empty;
		SKPoint b = SKPoint.Empty;
		bool found = false;

		foreach (var candidate in anchors)
		{
			if (candidate.Kind != ShapeAnchorKind.EdgeMidpoint || candidate.Index != _hoverIndex)
				continue;

			a = _hoverTarget.LocalToWorld(candidate.LocalA);
			b = _hoverTarget.LocalToWorld(candidate.LocalB);
			found = true;
			break;
		}

		if (!found)
			return;

		using var glow = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 6f,
			StrokeCap = SKStrokeCap.Round,
			Color = new SKColor(255, 200, 60, 90)
		};

		using var core = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 2f,
			StrokeCap = SKStrokeCap.Round,
			Color = new SKColor(255, 200, 60, 210)
		};

		var sa = view.PageToScreen(a);
		var sb = view.PageToScreen(b);

		canvas.DrawLine(sa, sb, glow);
		canvas.DrawLine(sa, sb, core);
	}


	#endregion

	#region Жизненный цикл фигуры

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		RemovePreview(view);
		Reset();

		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		view.SetTool(ShapeToolKind.Select);
		view.Invalidate();
	}

	/// <summary>
	/// Создаёт фигуру при первом обращении и далее только обновляет её.
	/// Пересоздание на каждое движение мыши сбрасывало бы выделение и
	/// плодило бы шаги истории через событие Page.ShapeAdded.
	/// </summary>
	private void CreateOrUpdateShape(MainView view, EditorInteractionContext ctx, SKPoint hoverPoint)
	{
		if (view.CurrentPage == null)
			return;

		if (_shape == null)
		{
			_shape = MeasurementShape.CreateFromWorldPoints(_p1, _p2, GetDefaultOffsetPoint(), view.MmPerPageUnit());
			ApplyAnchors(_shape);

			// Предпросмотр: до третьего щелчка размера ещё нет, и отмена
			// построения не должна оставлять следов в истории.
			view.CurrentPage.AddPreviewShape(_shape);

			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _shape);
			view.RaiseSelectionChanged();
			return;
		}

		_shape.LocalTransform = SKMatrix.CreateTranslation(_p1.X, _p1.Y);
		_shape.P1 = SKPoint.Empty;
		_shape.P2 = new SKPoint(_p2.X - _p1.X, _p2.Y - _p1.Y);

		ApplyAnchors(_shape);

		// До второго щелчка вынос ещё не выбирается пользователем: третья
		// точка появится только потом, и до неё держим значение по умолчанию.
		_shape.SetOffsetFromWorldPoint(_clickCount >= 2 ? hoverPoint : GetDefaultOffsetPoint());
	}

	private void Finish(MainView view, EditorInteractionContext ctx)
	{
		if (view.CurrentPage != null)
		{
			view.CurrentPage.CommitPreviewShape(_shape);
			view.CurrentPage.doc.need_save = true;
		}

		if (_shape != null)
		{
			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _shape);
			view.RaiseSelectionChanged();
		}

		Reset();

		view.SetTool(ShapeToolKind.Select);
		view.Invalidate();
	}

	private SKPoint GetDefaultOffsetPoint()
	{
		float dx = _p2.X - _p1.X;
		float dy = _p2.Y - _p1.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 0.0001f)
			return new SKPoint(_p1.X, _p1.Y + DefaultOffset);

		float nx = -dy / len;
		float ny = dx / len;

		float midX = (_p1.X + _p2.X) * 0.5f;
		float midY = (_p1.Y + _p2.Y) * 0.5f;

		return new SKPoint(midX + nx * DefaultOffset, midY + ny * DefaultOffset);
	}

	private void RemovePreview(MainView view)
	{
		view.CurrentPage?.RemovePreviewShape(_shape);
	}

	private void Reset()
	{
		_shape = null;
		_clickCount = 0;
		_p1 = SKPoint.Empty;
		_p2 = SKPoint.Empty;

		_target1 = null;
		_kind1 = ShapeAnchorKind.None;
		_index1 = 0;
		_nodeId1 = Guid.Empty;
		_surface1 = SKPoint.Empty;
		_surface2 = SKPoint.Empty;

		_target2 = null;
		_kind2 = ShapeAnchorKind.None;
		_index2 = 0;
		_nodeId2 = Guid.Empty;
	}

	/// <summary>Переносит найденные привязки в фигуру.</summary>
	private void ApplyAnchors(MeasurementShape shape)
	{
		if (_kind1 == ShapeAnchorKind.Surface)
			shape.SetSurfaceAnchor(true, _target1, _surface1);
		else
			shape.SetAnchor(true, _target1, _kind1, _index1, _nodeId1);

		if (_kind2 == ShapeAnchorKind.Surface)
			shape.SetSurfaceAnchor(false, _target2, _surface2);
		else
			shape.SetAnchor(false, _target2, _kind2, _index2, _nodeId2);
	}

	/// <summary>
	/// Ищет точку измерения рядом с курсором.
	///
	/// Нашлась — возвращает её и заполняет привязку, иначе возвращает точку
	/// под курсором без привязки. Оба конца могут указать на одну и ту же
	/// фигуру — именно так меряют сторону или диагональ одного объекта.
	/// </summary>
	private SKPoint ResolveAnchor(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		out Shape? target,
		out ShapeAnchorKind kind,
		out int index,
		out Guid nodeId)
	{
		target = null;
		kind = ShapeAnchorKind.None;
		index = 0;
		nodeId = Guid.Empty;

		_hoverHasAnchor = false;
		_hoverAnchorKind = ShapeAnchorKind.None;
		_hoverTarget = null;
		_hoverIndex = 0;

		if (ctx.CtrlPressed)
			return pagePoint;

		if (ObjectSnapEngine.TryFindAnchor(
				view, null, pagePoint, Settings.snap_dist,
				out var found, out var foundKind, out int foundIndex, out var anchorPoint,
				out var foundNodeId,
				includeCenter: true,
				includeGroupChildren: true))
		{
			target = found;
			kind = foundKind;
			index = foundIndex;
			nodeId = foundNodeId;

			// Поиск и подсветка — один и тот же вызов. Ищи они порознь,
			// на границе допуска подсвечивалась бы одна точка, а бралась
			// другая.
			_hoverHasAnchor = true;
			_hoverAnchor = anchorPoint;
			_hoverAnchorKind = foundKind;
			_hoverTarget = found;
			_hoverIndex = foundIndex;

			return anchorPoint;
		}

		// Особой точки рядом нет. Если курсор стоит ВНУТРИ геометрии
		// какой-нибудь фигуры, конец привязывается к этому месту её
		// поверхности: потом, когда фигуру подвинут, повернут или
		// растянут, конец останется на том же месте детали, и измеренная
		// величина будет меняться вместе с ней.
		var surfaceTarget = FindSurfaceTarget(view, pagePoint);

		if (surfaceTarget != null)
		{
			target = surfaceTarget;
			kind = ShapeAnchorKind.Surface;
			index = 0;
			nodeId = Guid.Empty;

			// Подсветка та же, что и у остальных привязок, но точка своя:
			// она не притягивается к особому месту, а остаётся там,
			// куда пользователь привёл курсор.
			_hoverHasAnchor = true;
			_hoverAnchor = pagePoint;
			_hoverAnchorKind = ShapeAnchorKind.Surface;
			_hoverTarget = surfaceTarget;
			_hoverIndex = 0;

			return pagePoint;
		}

		return pagePoint;
	}

	/// <summary>
	/// Фигура, внутри геометрии которой лежит точка. null — точка на пустом
	/// месте либо только внутри габаритов, но вне контура.
	///
	/// Проверяется именно контур, а не габаритный прямоугольник: у кольца,
	/// буквы с вырезом и любой вогнутой детали внутри габаритов полно
	/// пустоты, и привязывать к ней нечего.
	///
	/// Измерения исключены: мерить расстояние от поверхности другого
	/// размера бессмысленно, а перехватывать точку они бы стали охотно —
	/// подпись размера лежит поверх чертежа.
	///
	/// Обход спереди назад: привязка достаётся верхней фигуре, как и при
	/// обычном выделении.
	/// </summary>
	private static Shape? FindSurfaceTarget(MainView view, SKPoint pagePoint)
	{
		foreach (var shape in view.GetShapesFrontToBack())
		{
			if (shape is GuideLine || shape is MeasurementShape || shape is AngleMeasurementShape)
				continue;

			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (shape is GroupShape group)
			{
				var inner = FindSurfaceTargetDeep(group.Children, pagePoint);

				if (inner != null)
					return inner;

				continue;
			}

			if (ContainsPoint(shape, pagePoint))
				return shape;
		}

		return null;
	}

	/// <summary>
	/// То же внутри группы: привязка идёт к КОНКРЕТНОЙ детали, а не
	/// к группе целиком — иначе при растягивании группы конец поехал бы
	/// по её общим габаритам, а не по той детали, которую меряли.
	/// </summary>
	private static Shape? FindSurfaceTargetDeep(IReadOnlyList<Shape> shapes, SKPoint pagePoint)
	{
		for (int i = shapes.Count - 1; i >= 0; i--)
		{
			var shape = shapes[i];

			if (shape is GuideLine || shape is MeasurementShape || shape is AngleMeasurementShape)
				continue;

			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (shape is GroupShape group)
			{
				var inner = FindSurfaceTargetDeep(group.Children, pagePoint);

				if (inner != null)
					return inner;

				continue;
			}

			if (ContainsPoint(shape, pagePoint))
				return shape;
		}

		return null;
	}

	/// <summary>Лежит ли точка внутри контура фигуры.</summary>
	private static bool ContainsPoint(Shape shape, SKPoint pagePoint)
	{
		using var path = shape.BuildWorldPath();

		if (path.IsEmpty)
			return false;

		return path.Contains(pagePoint.X, pagePoint.Y);
	}

	#endregion
}

/// <summary>
/// Построение размера угла.
///
/// Вектор задаётся одним из двух способов, и они смешиваются свободно:
/// щелчок по прямому сегменту любой фигуры считывает его концы целиком,
/// щелчок по пустому месту задаёт одну точку, и вектор достраивается
/// вторым щелчком. Поэтому состояние — это не номер щелчка, а число
/// готовых векторов плюс возможная незавершённая точка.
///
/// Ctrl подавляет захват сегмента: без него нельзя было бы поставить
/// точку рядом с чужой линией, а такое нужно постоянно.
/// </summary>
public sealed class MeasureAngleTool : IEditorTool
{
	#region Константы

	/// <summary>Радиус дуги по умолчанию, пока последний щелчок не сделан.</summary>
	private const float DefaultRadius = 20f;

	#endregion

	#region Поля

	private AngleMeasurementShape? _shape;

	private SKPoint _v1a, _v1b;
	private SKPoint _v2a, _v2b;

	/// <summary>Сколько векторов уже задано: 0, 1 или 2.</summary>
	private int _vectorsDone;

	/// <summary>Первая точка текущего вектора, если она уже поставлена.</summary>
	private bool _hasPendingPoint;
	private SKPoint _pendingPoint;

	/// <summary>
	/// Что находится под курсором прямо сейчас.
	///
	/// Пересчитывается на каждое движение мыши и служит ЕДИНСТВЕННЫМ
	/// источником для щелчка: если бы подсветка и обработка щелчка искали
	/// цель порознь, они разошлись бы на границе допуска — подсвечен один
	/// сегмент, а взят соседний.
	/// </summary>
	private bool _hoverHasSegment;
	private SKPoint _hoverSegA, _hoverSegB;

	/// <summary>
	/// Привязки концов подсвеченного сегмента.
	///
	/// Два отрезка нельзя задать двумя точками — нужны четыре, и каждая
	/// обязана иметь собственную привязку. Поэтому сегмент выбирается
	/// не по геометрии контура, а по списку точек привязки цели: тогда
	/// оба его конца сразу известны как узлы, с номером и идентификатором.
	/// </summary>
	private Shape? _hoverSegTarget;
	private ShapeAnchorKind _hoverSegKindA, _hoverSegKindB;
	private int _hoverSegIndexA, _hoverSegIndexB;
	private Guid _hoverSegNodeA, _hoverSegNodeB;

	private bool _hoverHasAnchor;
	private SKPoint _hoverAnchor;
	private ShapeAnchorKind _hoverAnchorKind;
	private Shape? _hoverAnchorTarget;
	private int _hoverAnchorIndex;
	private Guid _hoverAnchorNodeId;

	/// <summary>
	/// Найденные привязки четырёх опорных точек. Хранятся в инструменте,
	/// а не сразу в фигуре: первые точки выбираются до того, как фигура
	/// вообще создана.
	/// </summary>
	private readonly Shape?[] _anchorTargets = new Shape?[4];
	private readonly ShapeAnchorKind[] _anchorKinds = new ShapeAnchorKind[4];
	private readonly int[] _anchorIndices = new int[4];
	private readonly Guid[] _anchorNodeIds = new Guid[4];

	#endregion

	public ShapeToolKind Kind => ShapeToolKind.MeasureAngle;

	#region Обработка ввода

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view.CurrentPage == null)
			return;

		if (button == MouseButtons.Right)
		{
			Cancel(view, ctx);
			return;
		}

		if (button != MouseButtons.Left)
			return;

		if (_vectorsDone >= 2)
		{
			CreateOrUpdateShape(view, ctx, pagePoint);
			Finish(view, ctx);
			return;
		}

		// Подсветка пересчитывается до разбора щелчка: мышь могла
		// сместиться между последним движением и нажатием, и брать нужно
		// то, что подсвечено в момент нажатия.
		UpdateHover(view, ctx, pagePoint);

		if (!_hasPendingPoint && _hoverHasSegment)
		{
			int slot = _vectorsDone * 2;

			// Привязки обоих концов приходят вместе с сегментом: он и
			// выбирается по списку точек привязки цели, поэтому его концы —
			// это заведомо узлы с известными номером и идентификатором.
			// Отдельный поиск по координате мог бы не найти ничего и оставить
			// вектор вовсе без привязок.
			StoreSegmentAnchor(slot, _hoverSegTarget, _hoverSegKindA, _hoverSegIndexA, _hoverSegNodeA);
			StoreSegmentAnchor(slot + 1, _hoverSegTarget, _hoverSegKindB, _hoverSegIndexB, _hoverSegNodeB);

			StoreVector(_hoverSegA, _hoverSegB);
		}
		else if (!_hasPendingPoint)
		{
			_pendingPoint = _hoverHasAnchor ? _hoverAnchor : pagePoint;
			_hasPendingPoint = true;

			StoreHoverAnchor(_vectorsDone * 2);
		}
		else
		{
			var second = _hoverHasAnchor ? _hoverAnchor : pagePoint;

			StoreHoverAnchor(_vectorsDone * 2 + 1);
			StoreVector(_pendingPoint, second);

			_hasPendingPoint = false;
		}

		CreateOrUpdateShape(view, ctx, pagePoint);
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (view.CurrentPage == null)
			return;

		UpdateHover(view, ctx, pagePoint);

		// Незавершённый вектор тянется за курсором, чтобы было видно,
		// что именно измеряется.
		if (_hasPendingPoint)
		{
			var live = _hoverHasAnchor ? _hoverAnchor : pagePoint;

			if (_vectorsDone == 0)
			{
				_v1a = _pendingPoint;
				_v1b = live;
			}
			else
			{
				_v2a = _pendingPoint;
				_v2b = live;
			}
		}

		if (_vectorsDone == 0 && !_hasPendingPoint)
		{
			// Фигуры ещё нет, но подсветка уже меняется — перерисовать надо.
			view.Invalidate();
			return;
		}

		CreateOrUpdateShape(view, ctx, pagePoint);
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

	#endregion

	#region Отрисовка подсказок

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		// Подсветка рисуется всегда, в том числе до первого щелчка:
		// именно она показывает, что инструмент готов взять сегмент
		// или узел, ещё до того как пользователь нажал кнопку.
		DrawHover(canvas, view);

		if (_vectorsDone == 0 && !_hasPendingPoint)
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

		if (_vectorsDone >= 1)
		{
			DrawMarker(canvas, view, _v1a, fill, stroke);
			DrawMarker(canvas, view, _v1b, fill, stroke);
		}

		if (_vectorsDone >= 2)
		{
			DrawMarker(canvas, view, _v2a, fill, stroke);
			DrawMarker(canvas, view, _v2b, fill, stroke);
		}

		if (_hasPendingPoint)
			DrawMarker(canvas, view, _pendingPoint, fill, stroke);
	}

	private static void DrawMarker(SKCanvas canvas, MainView view, SKPoint pagePoint, SKPaint fill, SKPaint stroke)
	{
		var sp = view.PageToScreen(pagePoint);
		canvas.DrawCircle(sp, 4f, fill);
		canvas.DrawCircle(sp, 4f, stroke);
	}

	/// <summary>
	/// Подсветка того, что будет взято щелчком.
	///
	/// Рисуется в ЭКРАННЫХ координатах: толщина подсветки должна быть
	/// одинаковой на любом масштабе, а в мировых единицах она таяла бы
	/// при отдалении и превращалась в полосу при приближении.
	/// </summary>
	private void DrawHover(SKCanvas canvas, MainView view)
	{
		if (_hoverHasSegment)
		{
			using var glow = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 6f,
				StrokeCap = SKStrokeCap.Round,
				Color = new SKColor(70, 160, 255, 90)
			};

			using var core = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 2f,
				StrokeCap = SKStrokeCap.Round,
				Color = new SKColor(70, 160, 255, 230)
			};

			var a = view.PageToScreen(_hoverSegA);
			var b = view.PageToScreen(_hoverSegB);

			canvas.DrawLine(a, b, glow);
			canvas.DrawLine(a, b, core);

			// Концы сегмента: показывают, какой именно вектор будет взят,
			// когда рядом лежит несколько коротких звеньев.
			DrawHoverEnd(canvas, a);
			DrawHoverEnd(canvas, b);

			return;
		}

		if (!_hoverHasAnchor)
			return;

		var sp = view.PageToScreen(_hoverAnchor);

		using var anchorFill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(255, 200, 60, 230)
		};

		using var anchorStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = new SKColor(40, 40, 40, 230)
		};

		// Форма маркера кодирует вид точки — так же, как это принято
		// в объектных привязках чертёжных редакторов: квадрат у узла,
		// треугольник у середины ребра, круг у всего остального.
		switch (_hoverAnchorKind)
		{
			case ShapeAnchorKind.Vertex:
				DrawSquare(canvas, sp, 4.5f, anchorFill, anchorStroke);
				break;

			case ShapeAnchorKind.EdgeMidpoint:
				DrawTriangle(canvas, sp, 5.5f, anchorFill, anchorStroke);
				break;

			default:
				canvas.DrawCircle(sp, 4.5f, anchorFill);
				canvas.DrawCircle(sp, 4.5f, anchorStroke);
				break;
		}
	}

	private static void DrawHoverEnd(SKCanvas canvas, SKPoint screenPoint)
	{
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(70, 160, 255, 230)
		};

		canvas.DrawCircle(screenPoint, 3f, paint);
	}

	private static void DrawSquare(SKCanvas canvas, SKPoint c, float half, SKPaint fill, SKPaint stroke)
	{
		var rect = new SKRect(c.X - half, c.Y - half, c.X + half, c.Y + half);

		canvas.DrawRect(rect, fill);
		canvas.DrawRect(rect, stroke);
	}

	private static void DrawTriangle(SKCanvas canvas, SKPoint c, float size, SKPaint fill, SKPaint stroke)
	{
		using var path = new SKPath();

		path.MoveTo(c.X, c.Y - size * 0.6f);
		path.LineTo(c.X + size * 0.55f, c.Y + size * 0.45f);
		path.LineTo(c.X - size * 0.55f, c.Y + size * 0.45f);
		path.Close();

		canvas.DrawPath(path, fill);
		canvas.DrawPath(path, stroke);
	}

	#endregion

	#region Жизненный цикл фигуры

	private void StoreVector(SKPoint a, SKPoint b)
	{
		if (_vectorsDone == 0)
		{
			_v1a = a;
			_v1b = b;
			_vectorsDone = 1;
		}
		else
		{
			_v2a = a;
			_v2b = b;
			_vectorsDone = 2;
		}
	}

	/// <summary>
	/// Создаёт фигуру при первом обращении и далее только обновляет её.
	/// Пересоздание на каждое движение мыши сбрасывало бы выделение и
	/// плодило бы шаги истории через событие Page.ShapeAdded.
	/// </summary>
	private void CreateOrUpdateShape(MainView view, EditorInteractionContext ctx, SKPoint hoverPoint)
	{
		if (view.CurrentPage == null)
			return;

		// До второго вектора угла ещё нет. Чтобы предпросмотр не мигал
		// вырожденной фигурой, второй вектор временно берётся от курсора.
		var v2a = _vectorsDone >= 2 ? _v2a : _v1a;
		var v2b = _vectorsDone >= 2 ? _v2b : hoverPoint;

		if (_shape == null)
		{
			if (_vectorsDone == 0)
				return;

			_shape = AngleMeasurementShape.CreateFromWorldPoints(
				_v1a, _v1b, v2a, v2b, GetDefaultTextPoint(), view.MmPerPageUnit());

			ApplyAnchorsToShape(_shape);

			// Предпросмотр: до последнего щелчка размера ещё нет, и отмена
			// построения не должна оставлять следов в истории.
			view.CurrentPage.AddPreviewShape(_shape);

			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _shape);
			view.RaiseSelectionChanged();
			return;
		}

		_shape.LocalTransform = SKMatrix.CreateTranslation(_v1a.X, _v1a.Y);
		_shape.S1P1 = SKPoint.Empty;
		_shape.S1P2 = new SKPoint(_v1b.X - _v1a.X, _v1b.Y - _v1a.Y);
		_shape.S2P1 = new SKPoint(v2a.X - _v1a.X, v2a.Y - _v1a.Y);
		_shape.S2P2 = new SKPoint(v2b.X - _v1a.X, v2b.Y - _v1a.Y);

		// Привязки переносятся на каждом обновлении: слоты заполняются
		// по мере щелчков, и фигура должна узнать о новых сразу.
		ApplyAnchorsToShape(_shape);

		// Радиус выбирается пользователем только последним щелчком:
		// до него держим значение по умолчанию.
		_shape.SetTextPositionFromWorldPoint(
			_vectorsDone >= 2 ? hoverPoint : GetDefaultTextPoint());
	}

	private void Finish(MainView view, EditorInteractionContext ctx)
	{
		if (view.CurrentPage != null)
		{
			view.CurrentPage.CommitPreviewShape(_shape);
			view.CurrentPage.doc.need_save = true;
		}

		if (_shape != null)
		{
			SelectionHelper.Clear(ctx);
			SelectionHelper.SetSingle(ctx, _shape);
			view.RaiseSelectionChanged();
		}

		Reset();

		view.SetTool(ShapeToolKind.Select);
		view.Invalidate();
	}

	private void Cancel(MainView view, EditorInteractionContext ctx)
	{
		RemovePreview(view);
		Reset();

		SelectionHelper.Clear(ctx);
		view.RaiseSelectionChanged();

		view.SetTool(ShapeToolKind.Select);
		view.Invalidate();
	}

	private SKPoint GetDefaultTextPoint()
	{
		float midX = (_v1a.X + _v1b.X) * 0.5f;
		float midY = (_v1a.Y + _v1b.Y) * 0.5f;

		return new SKPoint(midX, midY + DefaultRadius);
	}

	private void RemovePreview(MainView view)
	{
		view.CurrentPage?.RemovePreviewShape(_shape);
	}

	private void Reset()
	{
		_shape = null;
		_vectorsDone = 0;
		_hasPendingPoint = false;

		_v1a = SKPoint.Empty;
		_v1b = SKPoint.Empty;
		_v2a = SKPoint.Empty;
		_v2b = SKPoint.Empty;
		_pendingPoint = SKPoint.Empty;

		for (int i = 0; i < 4; i++)
			ClearAnchorSlot(i);
	}

	#endregion

	#region Выбор точки и сегмента

	/// <summary>
	/// Пересчитывает, что находится под курсором.
	///
	/// Приоритет зависит от стадии. Пока вектор не начат, сегмент важнее
	/// узла: щелчок по сегменту задаёт вектор целиком, и перехватывать его
	/// узлом на конце того же сегмента значило бы отбирать у пользователя
	/// более быстрый способ. Когда первая точка уже поставлена, сегмент
	/// брать некуда — остаётся только привязка к точке.
	///
	/// Ctrl отключает и то, и другое: без этого нельзя было бы поставить
	/// точку рядом с чужой линией, а такое нужно постоянно.
	/// </summary>
	private void UpdateHover(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		_hoverHasSegment = false;
		_hoverSegTarget = null;
		_hoverSegKindA = ShapeAnchorKind.None;
		_hoverSegKindB = ShapeAnchorKind.None;
		_hoverSegIndexA = 0;
		_hoverSegIndexB = 0;
		_hoverSegNodeA = Guid.Empty;
		_hoverSegNodeB = Guid.Empty;

		_hoverHasAnchor = false;
		_hoverAnchorKind = ShapeAnchorKind.None;
		_hoverAnchorTarget = null;
		_hoverAnchorIndex = 0;
		_hoverAnchorNodeId = Guid.Empty;

		if (ctx.CtrlPressed || _vectorsDone >= 2)
			return;

		if (!_hasPendingPoint && TryPickAnchoredSegment(view, pagePoint))
			return;

		if (ObjectSnapEngine.TryFindAnchor(
				view, null, pagePoint, Settings.snap_dist,
				out var target, out var kind, out int index, out var anchorPoint,
				out var nodeId,
				includeCenter: true,
				includeGroupChildren: true))
		{
			_hoverHasAnchor = true;
			_hoverAnchor = anchorPoint;
			_hoverAnchorKind = kind;
			_hoverAnchorTarget = target;
			_hoverAnchorIndex = index;
			_hoverAnchorNodeId = nodeId;
		}
	}

	/// <summary>Переносит найденную под курсором привязку в слот точки.</summary>
	private void StoreHoverAnchor(int slot)
	{
		if (slot < 0 || slot >= 4)
			return;

		if (!_hoverHasAnchor)
		{
			ClearAnchorSlot(slot);
			return;
		}

		_anchorTargets[slot] = _hoverAnchorTarget;
		_anchorKinds[slot] = _hoverAnchorKind;
		_anchorIndices[slot] = _hoverAnchorIndex;
		_anchorNodeIds[slot] = _hoverAnchorNodeId;
	}

	/// <summary>Кладёт привязку конца сегмента в слот.</summary>
	private void StoreSegmentAnchor(int slot, Shape? target, ShapeAnchorKind kind, int index, Guid nodeId)
	{
		if (slot < 0 || slot >= 4)
			return;

		if (target == null || kind == ShapeAnchorKind.None)
		{
			ClearAnchorSlot(slot);
			return;
		}

		_anchorTargets[slot] = target;
		_anchorKinds[slot] = kind;
		_anchorIndices[slot] = index;
		_anchorNodeIds[slot] = nodeId;
	}

	private void ClearAnchorSlot(int slot)
	{
		_anchorTargets[slot] = null;
		_anchorKinds[slot] = ShapeAnchorKind.None;
		_anchorIndices[slot] = 0;
		_anchorNodeIds[slot] = Guid.Empty;
	}

	/// <summary>Переносит найденные привязки в фигуру.</summary>
	private void ApplyAnchorsToShape(AngleMeasurementShape shape)
	{
		for (int i = 0; i < 4; i++)
			shape.SetAnchor(i, _anchorTargets[i], _anchorKinds[i], _anchorIndices[i], _anchorNodeIds[i]);
	}

	/// <summary>
	/// Ищет ребро фигуры под курсором и запоминает его вместе с привязками
	/// обоих концов.
	///
	/// Поиск идёт по СПИСКУ ТОЧЕК ПРИВЯЗКИ цели, а не по её контуру.
	/// Причина в требовании задачи: два отрезка задаются четырьмя точками,
	/// и каждая обязана иметь собственную привязку. Ребро из перечисления
	/// привязок приходит вместе со своими концами — это узлы с готовыми
	/// номером и идентификатором. Ребро, найденное по контуру, дало бы
	/// произвольные точки, для которых привязку пришлось бы искать
	/// повторным поиском по координате: на кривой он не нашёл бы ничего,
	/// и вектор остался бы вовсе не привязанным.
	///
	/// Побочное следствие: у кривой берётся хорда между узлами, а не
	/// касательная. Для измерения угла это правильнее — угол меряют между
	/// гранями, а грань задана своими вершинами.
	/// </summary>
	private bool TryPickAnchoredSegment(MainView view, SKPoint pagePoint)
	{
		var page = view.CurrentPage;

		if (page == null)
			return false;

		float tolerance = view.ScreenPixelsToPageUnits(6f);
		float best = tolerance;
		bool found = false;

		// Обход с конца: верхние фигуры перехватывают щелчок первыми,
		// как и при обычном выделении. Внутри группы порядок тот же:
		// дети идут на месте самой группы, а при равном расстоянии
		// побеждает найденный последним — то есть более точная цель.
		var candidates = new List<Shape>();
		CollectSegmentTargets(page.Shapes, candidates);

		for (int i = candidates.Count - 1; i >= 0; i--)
		{
			var shape = candidates[i];

			// Измерения не являются частью чертежа: мерить угол между
			// размерными линиями бессмысленно, а перехватывать щелчок
			// они бы стали охотно.
			if (shape is MeasurementShape || shape is AngleMeasurementShape)
				continue;

			var anchors = ShapeAnchorGeometry.GetLocalAnchors(shape);

			foreach (var edge in anchors)
			{
				if (edge.Kind != ShapeAnchorKind.EdgeMidpoint)
					continue;

				var a = shape.LocalToWorld(edge.LocalA);
				var b = shape.LocalToWorld(edge.LocalB);

				float d = DistanceToSegment(pagePoint, a, b);

				if (d > best)
					continue;

				// Концы ребра как точки привязки. Номер ребра равен номеру
				// его начальной вершины — на этом и держится сопоставление.
				if (!TryFindVertex(anchors, edge.Index, out var kindA, out int indexA, out var nodeA))
					continue;

				if (!TryFindNextVertex(anchors, edge.Index, out var kindB, out int indexB, out var nodeB))
					continue;

				best = d;
				found = true;

				_hoverHasSegment = true;
				_hoverSegA = a;
				_hoverSegB = b;
				_hoverSegTarget = shape;

				_hoverSegKindA = kindA;
				_hoverSegIndexA = indexA;
				_hoverSegNodeA = nodeA;

				_hoverSegKindB = kindB;
				_hoverSegIndexB = indexB;
				_hoverSegNodeB = nodeB;
			}
		}

		return found;
	}

	/// <summary>
	/// Плоский список целей для захвата сегмента: фигуры страницы вместе
	/// с содержимым групп, в порядке отрисовки.
	///
	/// Сама группа в список не попадает: её габаритная рамка сегментов
	/// не образует — грани, которые видит пользователь, принадлежат детям,
	/// и предлагать вместо них стороны рамки значило бы мерить угол
	/// не того объекта.
	/// </summary>
	private static void CollectSegmentTargets(IReadOnlyList<Shape> shapes, List<Shape> result)
	{
		foreach (var shape in shapes)
		{
			if (shape is GuideLine || !shape.IsVisible || shape.IsLocked)
				continue;

			if (shape is GroupShape group)
			{
				CollectSegmentTargets(group.Children, result);
				continue;
			}

			result.Add(shape);
		}
	}

	private static bool TryFindVertex(
		List<ShapeAnchorPoint> anchors, int index,
		out ShapeAnchorKind kind, out int foundIndex, out Guid nodeId)
	{
		foreach (var candidate in anchors)
		{
			if (candidate.Kind != ShapeAnchorKind.Vertex || candidate.Index != index)
				continue;

			kind = candidate.Kind;
			foundIndex = candidate.Index;
			nodeId = candidate.NodeId;
			return true;
		}

		kind = ShapeAnchorKind.None;
		foundIndex = 0;
		nodeId = Guid.Empty;
		return false;
	}

	/// <summary>
	/// Вершина, следующая за началом ребра. У замкнутого контура последнее
	/// ребро возвращается к нулевой вершине, поэтому номер берётся по кругу.
	/// </summary>
	private static bool TryFindNextVertex(
		List<ShapeAnchorPoint> anchors, int edgeIndex,
		out ShapeAnchorKind kind, out int foundIndex, out Guid nodeId)
	{
		int vertexCount = 0;

		foreach (var candidate in anchors)
		{
			if (candidate.Kind == ShapeAnchorKind.Vertex)
				vertexCount++;
		}

		if (vertexCount == 0)
		{
			kind = ShapeAnchorKind.None;
			foundIndex = 0;
			nodeId = Guid.Empty;
			return false;
		}

		return TryFindVertex(anchors, (edgeIndex + 1) % vertexCount, out kind, out foundIndex, out nodeId);
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		float lenSq = dx * dx + dy * dy;

		if (lenSq < 0.000001f)
			return MathF.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

		float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);

		float cx = a.X + dx * t;
		float cy = a.Y + dy * t;

		return MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
	}


	#endregion
}
