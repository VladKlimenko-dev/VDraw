using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

/// <summary>
/// Главный холст редактора.
///
/// Архитектура отрисовки:
///   - весь рендер идёт через Skia/Ganesh; прямых GL-вызовов в приложении больше нет,
///     поэтому GRContext.ResetContext не требуется;
///   - кисти (SKPaint) статические и переиспользуются, а не создаются каждый кадр;
///   - фигуры отсекаются по видимой области до вызова Draw;
///   - порядок отрисовки собирается в переиспользуемые буферы без LINQ-аллокаций.
/// </summary>
public sealed partial class MainView : SKGLControl
{
	#region Поля

	public Page? CurrentPage => MainForm.cur_doc?.ActivePage;

	public SKPoint zero_pos = new(150, 150);
	public float scale = 1.0f;

	private bool _panning;
	private Point _panStartMouse;
	private SKPoint _panStartZero;

	private readonly HScrollBar _hScroll;
	private readonly VScrollBar _vScroll;
	/// <summary>
	/// Полосы прокрутки. Создаются здесь, но размещаются снаружи: дочерний
	/// оконный контрол поверх SKGLControl затирается каждым кадром OpenGL.
	/// </summary>
	public HScrollBar HorizontalScrollBar => _hScroll;
	public VScrollBar VerticalScrollBar => _vScroll;

	private const float ViewMarginPx = 800f;

	/// <summary>
	/// Левый и верхний край виртуального поля прокрутки, в тех же пикселях,
	/// в которых живёт zero_pos.
	///
	/// Прежде край был постоянным (минус поле), и обработчики полос знали
	/// его наизусть. Теперь он зависит от того, куда разошлись фигуры,
	/// и его приходится запоминать: по нему значение полосы переводится
	/// в положение листа и обратно.
	/// </summary>
	private float _scrollVirtualLeft = -ViewMarginPx;
	private float _scrollVirtualTop = -ViewMarginPx;

	/// <summary>Габариты всех фигур страницы в единицах страницы, с запасом по времени.</summary>
	private SKRect _contentBoundsCache = SKRect.Empty;
	private bool _contentBoundsValid;
	private long _contentBoundsTicks;
	private Page? _contentBoundsPage;
	private int _contentBoundsShapeCount = -1;

	/// <summary>
	/// Насколько устаревшими габаритами содержимого можно пользоваться.
	///
	/// Полосы пересчитываются и на каждое движение мыши при панорамировании,
	/// и на каждый шаг автопрокрутки, а габариты — это GetBounds у каждой
	/// фигуры листа. Между тем за одну протяжку они меняются мало, и запас
	/// в пятую долю секунды на глаз не виден: диапазон полосы дотянется
	/// следом.
	///
	/// Появление и исчезновение фигур запасом не сглаживается: число фигур
	/// входит в ключ, и вставка, удаление, вставка из буфера и откат
	/// любого из них пересчитывают габариты сразу. Перемещение же
	/// и изменение размера числа фигур не меняют и попадают под общий
	/// запас — на диапазоне полосы это и есть та самая пятая доля секунды.
	/// </summary>
	private const long ContentBoundsTtlMs = 200;

	/// <summary>Полоса у края вида, при заходе в которую начинается автопрокрутка.</summary>
	private const float AutoScrollBandPx = 28f;

	/// <summary>Самый быстрый шаг автопрокрутки за один такт.</summary>
	private const float AutoScrollMaxStepPx = 28f;

	private readonly System.Windows.Forms.Timer _autoScrollTimer = new() { Interval = 30 };

	/// <summary>
	/// Выходил ли курсор из приграничной полосы с начала протяжки —
	/// отдельно по каждой оси.
	///
	/// Пока не выходил, автопрокрутка по этой оси молчит. Иначе протяжка,
	/// НАЧАТАЯ у кромки, уезжала бы сама собой, ещё до единого движения
	/// мыши. Так происходит при вытягивании направляющей с линейки:
	/// операция запускается, когда курсор стоит на самой линейке, то есть
	/// внутри полосы, — и вид уносило бы влево, стоило взяться
	/// за направляющую.
	///
	/// Оси считаются порознь, а не одним признаком на обе. Одним признаком
	/// направляющая, взятая с ЛЕВОЙ линейки, не прокручивала бы вид и вниз,
	/// пока курсор не выйдет из левой полосы, — хотя к нижней кромке он
	/// пришёл своим ходом и прокрутка там законна.
	/// </summary>
	private bool _autoScrollArmedX;
	private bool _autoScrollArmedY;
	private bool _updatingScrollbars = false;
	/// <summary>Минимальный и максимальный масштаб вида.</summary>
	private const float MinFitScale = 0.02f;
	/// <summary>Максимальный и максимальный масштаб вида.</summary>
	private const float MaxFitScale = 50f;
	/// <summary>Доля области просмотра, оставляемая полями вокруг содержимого.</summary>
	private const float FitMarginRatio = 0.05f;
	/// <summary>Поднимается после изменения масштаба вида.</summary>
	public event Action? ScaleChanged;

	private static readonly EditorInteractionContext _interaction = new();
	private static readonly Dictionary<ShapeToolKind, IEditorTool> _tools = new();

	private bool suppressNextLeftMouseUp;
	private long lastLeftMouseDownTicks;
	private Point lastLeftMouseDownPoint;
	private bool _undoGroupOpen;

	private IEditorTool? imageEditTool;
	public bool IsImageEditMode => imageEditTool != null;

	/// <summary>
	/// Включённый инструмент растрового редактирования. None означает,
	/// что режим выключен и контур кисти на картинке не рисуется.
	/// </summary>
	public ImageEditToolKind ActiveImageToolKind =>
		imageEditTool == null ? ImageEditToolKind.None : (ImageEditSession?.ActiveKind ?? ImageEditToolKind.None);

	/// <summary>
	/// Инструменты, работающие ПО пикселям изображения.
	///
	/// Отделены от выделения и курсора: рамку и лассо нормально тянуть,
	/// начиная за краем картинки, а мазок кистью за её пределами
	/// бессмыслен — там нечего красить.
	/// </summary>
	private static bool IsPaintKind(ImageEditToolKind kind) => kind is
		ImageEditToolKind.Brush or
		ImageEditToolKind.Eraser or
		ImageEditToolKind.Fill or
		ImageEditToolKind.Lighten or
		ImageEditToolKind.Darken or
		ImageEditToolKind.Contrast or
		ImageEditToolKind.BW;
	public ImageEditSession? ImageEditSession;

	public ShapeToolKind CurrentToolKind => _interaction.CurrentTool;
	public IReadOnlyList<Shape> Selection => _interaction.Selection;
	public event EventHandler? SelectionChanged;

	public Shape? SelectedShape => _interaction.Selection.Count == 1
		? _interaction.Selection[0]
		: null;

	internal EditorInteractionContext InteractionContext => _interaction;

	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public bool IsCurveEditMode { get; set; }

	private bool _draggingPageOrigin;

	public event Action? SelectionGeometryChanged;

	public MainForm mainForm;

	// Переиспользуемые буферы порядка отрисовки: без аллокаций на каждый кадр.
	private readonly List<Shape> _drawBuffer = new(256);
	private readonly List<GuideLine> _guideBuffer = new(32);

	// Последняя позиция мыши, по которой считался hit-test.
	private Point _lastHitTestPoint = new(int.MinValue, int.MinValue);

	/// <summary>
	/// Экранное положение курсора и признак того, что он над холстом.
	///
	/// Хранится отдельно, а не берётся из ctx.CurrentPage: там страничная
	/// координата, и она остаётся от последнего движения даже когда
	/// курсор давно ушёл с холста — полоски на линейках застывали бы
	/// в случайном месте.
	/// </summary>
	private Point _cursorScreen = new(int.MinValue, int.MinValue);
	private bool _cursorOverCanvas;

	private bool _disposed;

	#endregion

	#region Кэш кистей

	// Кисти неизменны в течение сеанса, поэтому создаются один раз.
	// Раньше каждая из них аллоцировалась и уничтожалась на каждом кадре.
	private static readonly SKPaint RulerBackPaint = new()
	{
		IsAntialias = false,
		Style = SKPaintStyle.Fill,
		Color = new SKColor(245, 245, 245)
	};

	private static readonly SKPaint RulerBorderPaint = new()
	{
		IsAntialias = false,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 1f,
		Color = new SKColor(180, 180, 180)
	};

	private static readonly SKPaint RulerTickPaint = new()
	{
		IsAntialias = false,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 1f,
		Color = new SKColor(90, 90, 90)
	};

	private static readonly SKPaint RulerZeroTickPaint = new()
	{
		IsAntialias = false,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 2f,
		Color = new SKColor(30, 30, 30)
	};

	/// <summary>
	/// Полоска курсора на линейке. Полупрозрачная заливка шириной
	/// в пиксель экрана: сплошная линия поверх насечек перекрывала бы
	/// их и мешала читать деления.
	/// </summary>
	/// <summary>Крест на квадрате пересечения линеек.</summary>
	private static readonly SKPaint RulerCornerCrossPaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 1.4f,
		Color = new SKColor(90, 90, 90)
	};

	private static readonly SKPaint RulerCursorPaint = new()
	{
		IsAntialias = false,
		Style = SKPaintStyle.Fill,
		Color = new SKColor(220, 60, 60, 200)
	};

	private static readonly SKPaint MarqueeFillPaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = new SKColor(50, 120, 255, 40)
	};

	private static readonly SKPaint MarqueeStrokePaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 1f,
		Color = new SKColor(50, 120, 255, 180)
	};

	/// <summary>
	/// Рамка-секущая: берёт всё, чего коснулась. Другой цвет и пунктир.
	///
	/// Без этого два режима рамки внешне не отличить, а выбирают они
	/// разное. Пользователь задаёт режим направлением протяжки — значит
	/// должен видеть, какой режим получился, ещё до того, как отпустит
	/// кнопку. Пунктир и зелёный тон — то же различение, что
	/// в чертёжных редакторах.
	/// </summary>
	private static readonly SKPaint MarqueeCrossingFillPaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = new SKColor(70, 190, 120, 40)
	};

	private static readonly SKPaint MarqueeCrossingStrokePaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 1f,
		Color = new SKColor(50, 165, 100, 200),
		PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0f)
	};

	private static readonly SKPaint OriginStrokePaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = 2f,
		Color = new SKColor(220, 80, 80)
	};

	private static readonly SKPaint OriginFillPaint = new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = new SKColor(255, 255, 255, 220)
	};

	// Цвета страницы берутся из Settings и могут меняться, поэтому только Color обновляется.
	private static readonly SKPaint PageShadowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
	private static readonly SKPaint PageFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
	private static readonly SKPaint PageBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

	#endregion

	#region Конструктор
	public MainView()
	{
		Dock = DockStyle.Fill;
		BackColor = Color.White;
		TabStop = true;

		PaintSurface += MainView_PaintSurface;

		_hScroll = new HScrollBar();
		_vScroll = new VScrollBar();

		_hScroll.SmallChange = 20;
		_hScroll.LargeChange = 100;
		_vScroll.SmallChange = 20;
		_vScroll.LargeChange = 100;

		_hScroll.Scroll += HScroll_Scroll;
		_vScroll.Scroll += VScroll_Scroll;

		_autoScrollTimer.Tick += AutoScrollTick;

		RegisterTools();

		SetTool(ShapeToolKind.Select);
	}
	#endregion

	#region Режимы
	internal void NotifySelectionIfChanged(List<Shape> before)
	{
		if (before.Count != _interaction.Selection.Count)
		{
			RaiseSelectionChanged();
			return;
		}

		for (int i = 0; i < before.Count; i++)
		{
			if (!ReferenceEquals(before[i], _interaction.Selection[i]))
			{
				RaiseSelectionChanged();
				return;
			}
		}
	}

	internal void RaiseSelectionChanged()
	{
		SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Выделяет все фигуры страницы. Вызывается по Ctrl+A.
	///
	/// Пропускаются три вида фигур:
	///
	/// направляющие — это не объект рисунка, а разметка, и попав в выделение
	/// они поехали бы вместе с ним при первом же перетаскивании;
	///
	/// скрытые и запертые — их нельзя выбрать мышью, и попадать
	/// в выделение по клавиатуре они тоже не должны: иначе рамка
	/// выделения охватывала бы пустоту, а часть операций молча ничего
	/// бы с ними не делала.
	/// </summary>
	public void SelectAllShapes()
	{
		if (CurrentPage == null)
			return;

		SelectionHelper.Clear(_interaction);

		foreach (var shape in CurrentPage.Shapes)
		{
			if (shape is GuideLine || !shape.IsVisible || shape.IsLocked)
				continue;

			SelectionHelper.Add(_interaction, shape);
		}

		RaiseSelectionChanged();
		InvalidateCanvas();
	}

	public void SetMode(EditorMode mode)
	{
		if (MainForm.Mode == mode)
		{
			mainForm.SetEditorMode(mode);
			return;
		}

		if (mode == EditorMode.Curve)
		{
			if (_interaction.Selection.Count != 1 || _interaction.Selection[0] is not CurveShape curve)
				return;

			curve.ClearNodeSelection();
		}
		else
		{
			if (MainForm.Mode == EditorMode.Curve &&
				_interaction.Selection.Count == 1 &&
				_interaction.Selection[0] is CurveShape prevCurve)
			{
				prevCurve.ClearNodeSelection();
			}
		}

		mainForm.SetEditorMode(mode);
		InvalidateCanvas();
	}

	#endregion

	#region Преобразования координат

	/// <summary>
	/// Миллиметров в одной единице страницы.
	///
	/// Именно это ждёт StrokeStyle.CreatePaint: толщина задана в мм,
	/// а геометрия — в единицах страницы, и делить надо на их отношение.
	/// Для миллиметровой страницы величина равна единице, поэтому прежняя
	/// ошибка в мм не проявлялась и вылезала только на дюймах.
	/// </summary>
	public float MmPerPageUnit() => CurrentPage is Page p && p.MmPerUom > 0.0001f ? p.MmPerUom : 1f;

	/// <summary>
	/// Допуск попадания, переведённый из экранных пикселей в единицы
	/// страницы.
	///
	/// Через масштаб просмотра: пользователь целится курсором по экрану,
	/// и зона захвата должна быть одинаковой в пикселях при любом зуме
	/// и любых единицах.
	/// </summary>
	public float HitToleranceUom(float screenPx = 6f) => ScreenPixelsToPageUnits(screenPx);

	private float PxPerMm(Page page) => page.DPI / 25.4f;

	private float PxPerUom(Page page) => page.DPI / 25.4f * page.MmPerUom;

	private float ViewPxPerUom(Page page) => PxPerUom(page) * scale;

	public SKPoint PageToScreen(SKPoint absolutePagePoint)
	{
		if (CurrentPage == null)
			return SKPoint.Empty;

		float f = ViewPxPerUom(CurrentPage);

		return new SKPoint(
			zero_pos.X + (absolutePagePoint.X - CurrentPage.page_origin.X) * f,
			zero_pos.Y + (absolutePagePoint.Y - CurrentPage.page_origin.Y) * f);
	}

	public SKPoint ScreenToPage(Point p)
	{
		if (CurrentPage == null)
			return SKPoint.Empty;

		float f = ViewPxPerUom(CurrentPage);
		if (Math.Abs(f) < 0.0001f)
			return SKPoint.Empty;

		return new SKPoint(
			CurrentPage.page_origin.X + (p.X - zero_pos.X) / f,
			CurrentPage.page_origin.Y + (p.Y - zero_pos.Y) / f);
	}

	public SKPoint ScreenToPage(SKPoint p) => ScreenToPage(new Point((int)p.X, (int)p.Y));

	public float ScreenPixelsToPageUnits(float px)
	{
		if (CurrentPage == null) return px;
		float pxPerUom = ViewPxPerUom(CurrentPage);
		if (Math.Abs(pxPerUom) < 1e-6f) return px;
		return px / pxPerUom;
	}

	public SKPoint AbsolutePageToScreen(SKPoint absolutePagePoint)
	{
		if (CurrentPage == null)
			return SKPoint.Empty;

		float f = ViewPxPerUom(CurrentPage);

		return new SKPoint(
			zero_pos.X + absolutePagePoint.X * f,
			zero_pos.Y + absolutePagePoint.Y * f);
	}

	public SKPoint ScreenToPageAbsolute(Point p)
	{
		if (CurrentPage == null) return SKPoint.Empty;
		float pxPerUom = ViewPxPerUom(CurrentPage);
		if (Math.Abs(pxPerUom) < 1e-6f) return SKPoint.Empty;
		return new SKPoint(
			(-p.X + zero_pos.X) / pxPerUom,
			(-p.Y + zero_pos.Y) / pxPerUom);
	}

	public float ScreenPixelsToPageMm(float px)
	{
		if (CurrentPage == null) return px;
		float pxPerUom = ViewPxPerUom(CurrentPage);
		if (Math.Abs(pxPerUom) < 1e-6f) return px;
		return (px / pxPerUom) * CurrentPage.MmPerUom;
	}
	#endregion

	#region Перечисление фигур

	public IEnumerable<Shape> GetShapesBackToFront()
	{
		return CurrentPage?.Shapes.OrderBy(s => s.Layer) ?? Enumerable.Empty<Shape>();
	}

	public IEnumerable<Shape> GetShapesFrontToBack()
	{
		return CurrentPage?.Shapes.OrderByDescending(s => s.Layer) ?? Enumerable.Empty<Shape>();
	}

	#endregion

	#region GuideLines

	public IEnumerable<GuideLine> GetGuides()
	{
		if (CurrentPage == null)
			yield break;

		foreach (var g in CurrentPage.Shapes.OfType<GuideLine>().OrderBy(s => s.Layer))
			yield return g;
	}

	public GuideLine CreateGuide(GuideOrientation orientation, SKPoint pagePoint)
	=> CreateGuideUndoable(orientation, pagePoint);

	internal void SelectGuide(GuideLine guide)
	{
		foreach (var s in _interaction.Selection)
			s.IsSelected = false;

		_interaction.Selection.Clear();

		if (_interaction.SelectedGuide != null)
			_interaction.SelectedGuide.IsSelected = false;

		_interaction.SelectedGuide = guide;
		guide.IsSelected = true;
		RaiseSelectionChanged();
	}

	internal void ClearGuideSelection()
	{
		if (_interaction.SelectedGuide != null)
		{
			_interaction.SelectedGuide.IsSelected = false;
			_interaction.SelectedGuide = null;
			RaiseSelectionChanged();
		}
	}

	#endregion

	#region Операции, инструменты, страница
	/// <summary>
	/// Инструменты лежат в статическом словаре, поэтому регистрируются один раз.
	/// Прежний код перезаписывал их при создании каждого MainView.
	/// </summary>
	private static void RegisterTools()
	{
		if (_tools.Count > 0)
			return;

		_tools[ShapeToolKind.Select] = new SelectTool();
		_tools[ShapeToolKind.Line] = new LineTool();
		_tools[ShapeToolKind.Curve] = new CurveTool();
		_tools[ShapeToolKind.Circle2P] = new TwoPointShapeTool(ShapeToolKind.Circle2P);
		_tools[ShapeToolKind.Rectangle] = new TwoPointShapeTool(ShapeToolKind.Rectangle);
		_tools[ShapeToolKind.RoundedRectangle] = new TwoPointShapeTool(ShapeToolKind.RoundedRectangle);
		_tools[ShapeToolKind.ChamferedRectangle] = new TwoPointShapeTool(ShapeToolKind.ChamferedRectangle);
		_tools[ShapeToolKind.Circle3P] = new MultiPointShapeTool(ShapeToolKind.Circle3P);
		_tools[ShapeToolKind.Ellipse3P] = new MultiPointShapeTool(ShapeToolKind.Ellipse3P);
		_tools[ShapeToolKind.Ellipse5P] = new MultiPointShapeTool(ShapeToolKind.Ellipse5P);
		_tools[ShapeToolKind.Polygon] = new RegularPolygonTool(6);
		_tools[ShapeToolKind.FreePolygon] = new MultiPointShapeTool(ShapeToolKind.FreePolygon);
		_tools[ShapeToolKind.Text] = new TextTool();
		_tools[ShapeToolKind.Image] = new ImageTool();
		_tools[ShapeToolKind.SelectOtherShape] = new SelectOtherShapeTool();
		_tools[ShapeToolKind.ScissorsLine] = new ScissorsLineTool();
		_tools[ShapeToolKind.ScissorsCurve] = new ScissorsCurveTool();
		_tools[ShapeToolKind.Measure] = new MeasureTool();
		_tools[ShapeToolKind.MeasureAngle] = new MeasureAngleTool();
		_tools[ShapeToolKind.FormatPainter] = new FormatPainterTool();
	}

	public void StartOperation(DragOperation op, SKPoint pagePoint)
	{
		// Пока включён инструмент растрового редактирования, фигура
		// не двигается и не масштабируется: доступны только узлы и их
		// рычаги. Иначе мазок кистью, начатый чуть мимо, уводил бы
		// саму картинку вместо рисования по ней.
		//
		// Проверка стоит здесь, в единственной точке запуска операций,
		// а не у каждого места, откуда перемещение может начаться:
		// их несколько, и добавление нового пути легко пропустить.
		if (IsImageEditMode && IsShapeTransformOperation(op))
			return;

		BeginOperationUndo(op);

		_interaction.ActiveOperation = op;
		op.Begin(this, _interaction, pagePoint);
		MainForm.sel.NotifyGeometryChanged();

		// Пока идёт протяжка, вид сам доезжает за курсором, ушедшим за край.
		// Такт сам остановится, когда операция закончится.
		_autoScrollArmedX = false;
		_autoScrollArmedY = false;
		_autoScrollTimer.Start();

		InvalidateCanvas();
	}

	public void SetTool(ShapeToolKind toolKind)
	{
		if (!_tools.TryGetValue(toolKind, out var tool))
			throw new InvalidOperationException($"Tool '{toolKind}' is not registered.");

		_interaction.ActiveOperation?.Cancel(this, _interaction);
		_interaction.ActiveOperation = null;
		_interaction.CurrentTool = toolKind;
		_interaction.ToolHandler = tool;

		Cursor = toolKind switch
		{
			ShapeToolKind.Select => Cursors.Default,
			_ => Cursors.Cross
		};

		Focus();
		InvalidateCanvas();
	}

	/// <summary>
	/// Сессия растрового редактирования с уже привязанной историей.
	///
	/// Единственная точка получения сессии. Прямое new ImageEditSession на
	/// стороне оставляло History равным null, и всё, что такая сессия
	/// делала, в историю не попадало.
	/// </summary>
	public ImageEditSession EnsureImageEditSession()
	{
		ImageEditSession ??= new ImageEditSession(mainForm);

		// История текущего документа. Присваивается каждый раз: при открытии
		// другого документа стек меняется.
		ImageEditSession.History = Undo;

		return ImageEditSession;
	}

	/// <summary>
	/// Лежит ли точка на редактируемом изображении.
	///
	/// Через HitTest самой фигуры, а не по габаритам: изображение может
	/// быть обрезано по произвольному контуру, и за его пределами внутри
	/// габаритного прямоугольника рисовать всё равно нечего.
	/// </summary>
	private bool IsPointOnEditableImage(SKPoint pagePoint)
	{
		var image = ImagePaintEngine.GetEditableImage(this, _interaction);

		if (image == null)
			return false;

		return image.HitTest(pagePoint, 0f, MmPerPageUnit());
	}

	/// <summary>
	/// Есть ли под точкой фигура, отличная от редактируемого изображения.
	///
	/// Нужно выходу из режима изображения: щелчок по пустому листу режим
	/// не гасит — там пользователь чаще всего просто начинает выделение
	/// пикселей за краем картинки, — а щелчок по соседней фигуре гасит,
	/// потому что означает переход к ней.
	/// </summary>
	private bool HitsShapeOtherThanEditedImage(SKPoint pagePoint)
	{
		var edited = ImagePaintEngine.GetEditableImage(this, _interaction);
		float tolerance = HitToleranceUom();
		float mmPerUnit = MmPerPageUnit();

		foreach (var shape in GetShapesFrontToBack())
		{
			if (shape is GuideLine)
				continue;

			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (edited != null && ReferenceEquals(shape, edited))
				continue;

			if (shape.HitTest(pagePoint, tolerance, mmPerUnit))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Операции, двигающие или преобразующие саму фигуру.
	///
	/// Перечислены по виду, а не по типу класса: список видов —
	/// это то, что уже поддерживает запись в историю, и он полнее
	/// любого перечисления классов, которое пришлось бы держать руками.
	/// </summary>
	private static bool IsShapeTransformOperation(DragOperation op) => op.Kind is
		DragOperationKind.MoveSelection or
		DragOperationKind.ResizeSelection or
		DragOperationKind.RotateSelection;

	public void SetImageEditTool(ImageEditToolKind kind)
	{
		EnsureImageEditSession();

		// Незакрытая область записи от предыдущего инструмента сделала бы
		// следующий шаг истории некорректным.
		if (ImageEditSession.IsRecordingSelection)
			ImageEditSession.AbortSelectionHistory();

		if (kind == ImageEditToolKind.RectSelect || kind == ImageEditToolKind.LassoSelect)
		{
			// Перемещённый фрагмент сначала впечатывается в пиксели и только
			// потом снимается. Прежний ClearFloatingSelection просто
			// освобождал его растр, и смена инструмента выделения молча
			// стирала уже сделанное перемещение.
			var editable = ImagePaintEngine.GetEditableImage(this, _interaction);

			if (editable != null)
			{
				ImageSelectionClipboardHelper.CommitFloatingIfAny(
					editable,
					ImageEditSession,
					ImageEditSession.History);
			}

			using (ImageEditSession.BeginSelectionHistory("Clear selection"))
			{
				ImageEditSession.Selection.Clear();
				ImageEditSession.ClearFloatingSelection();
			}
		}

		imageEditTool = kind switch
		{
			ImageEditToolKind.Cursor => new ImageCursorTool(ImageEditSession),
			ImageEditToolKind.RectSelect => new ImageRectSelectTool(ImageEditSession),
			ImageEditToolKind.LassoSelect => new ImageLassoTool(ImageEditSession),
			ImageEditToolKind.Eraser => new ImageEraserTool(ImageEditSession),
			ImageEditToolKind.Brush => new ImageBrushTool(ImageEditSession),
			ImageEditToolKind.Fill => new ImageFillTool(ImageEditSession),
			ImageEditToolKind.Lighten => new ImageLightenTool(ImageEditSession),
			ImageEditToolKind.Darken => new ImageDarkenTool(ImageEditSession),
			ImageEditToolKind.Contrast => new ImageContrastTool(ImageEditSession),
			ImageEditToolKind.BW => new ImageBWTool(ImageEditSession),
			_ => null
		};

		ImageEditSession.ActiveKind = kind;
		Cursor = imageEditTool != null ? Cursors.Cross : Cursors.Default;

		if (imageEditTool == null)
		{
			// Выход из режима изображения возвращает в Shape — режим
			// по умолчанию. Прежний возврат в Curve включал редактирование
			// узлов, которого пользователь не просил, да ещё и в обход
			// единственных разрешённых входов в этот режим.
			mainForm.SetEditorMode(EditorMode.Shape);
		}
		else
		{
			Focus();
			InvalidateCanvas();
			mainForm.SetEditorMode(EditorMode.Image);
		}

		// Подсветка кнопок ведётся здесь, а не в вызывающем: сменить
		// инструмент может и сам холст — например выйдя из режима при
		// щелчке мимо картинки.
		mainForm?.UpdateImageToolButtons();
	}

	public void SetPage(Page page)
	{
		if (MainForm.cur_doc != null)
		{
			MainForm.cur_doc.ActivePage = page;
			// Толщина обводки новой фигуры берётся из единиц АКТИВНОЙ страницы,
			// а страницы документа могут быть размечены по-разному.
			Settings.current_mm_per_uom = page.MmPerUom;

			scale = page.scale <= 0 ? 1f : page.scale;
			zero_pos = page.view_zero;

			if (zero_pos == SKPoint.Empty)
				zero_pos = new SKPoint(80, 80);
		}
		else
		{
			_interaction.Selection.Clear();
			_interaction.SelectionSnapshot.Clear();
			_interaction.SelectedGuide = null;
			return;
		}

		UpdateScrollBars();
		InvalidateCanvas();
	}

	public void DocumentChanged()
	{
		if (CurrentPage == null)
			return;

		scale = CurrentPage.scale <= 0 ? 1f : CurrentPage.scale;
		zero_pos = CurrentPage.view_zero == SKPoint.Empty ? new SKPoint(80, 80) : CurrentPage.view_zero;
		InvalidateContentBounds();
		UpdateScrollBars();
		InvalidateCanvas();
	}

	#endregion

	#region View
	/// <summary>
	/// Подгоняет вид так, чтобы прямоугольник в координатах страницы занял
	/// область просмотра целиком.
	///
	/// Масштаб и положение считаются раздельно: сначала выбирается scale,
	/// и только затем zero_pos, потому что перевод координат зависит от
	/// масштаба и до его установки дал бы смещение.
	/// </summary>
	public bool FitToPageRect(SKRect target)
	{
		if (CurrentPage == null || target.IsEmpty)
			return false;

		if (target.Width <= 0f || target.Height <= 0f)
			return false;

		var viewport = GetCanvasViewport();

		if (viewport.Width < 8 || viewport.Height < 8)
			return false;

		// Поля с каждой стороны, чтобы содержимое не упиралось в границы
		// и в линейки.
		float usableWidth = viewport.Width * (1f - FitMarginRatio * 2f);
		float usableHeight = viewport.Height * (1f - FitMarginRatio * 2f);

		// Базовое разрешение страницы: scale = 1 означает натуральную величину.
		float basePxPerUom = PxPerUom(CurrentPage);

		if (basePxPerUom < 1e-6f)
			return false;

		float scaleX = usableWidth / (target.Width * basePxPerUom);
		float scaleY = usableHeight / (target.Height * basePxPerUom);

		scale = Math.Clamp(MathF.Min(scaleX, scaleY), MinFitScale, MaxFitScale);

		// Теперь, когда масштаб известен, ставим центр области в центр холста.
		float f = ViewPxPerUom(CurrentPage);

		float centerX = target.MidX - CurrentPage.page_origin.X;
		float centerY = target.MidY - CurrentPage.page_origin.Y;

		zero_pos = new SKPoint(
			viewport.Left + viewport.Width * 0.5f - centerX * f,
			viewport.Top + viewport.Height * 0.5f - centerY * f);

		CurrentPage.view_zero = zero_pos;

		UpdateScrollBars();
		InvalidateCanvas();

		ScaleChanged?.Invoke();

		return true;
	}

	/// <summary>Вписывает выделенные фигуры.</summary>
	public bool ZoomToSelection()
	{
		if (_interaction.Selection.Count == 0)
			return false;

		var bounds = SKRect.Empty;

		foreach (var shape in _interaction.Selection)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);
		}

		return FitToPageRect(InflateForStroke(bounds));
	}

	/// <summary>Вписывает все фигуры страницы.</summary>
	public bool ZoomToAllShapes()
	{
		if (CurrentPage == null)
			return false;

		var bounds = SKRect.Empty;

		foreach (var shape in CurrentPage.Shapes)
		{
			// Направляющие бесконечны по своей природе и растянули бы область
			// до бессмысленных величин.
			if (!shape.IsVisible || shape is GuideLine)
				continue;

			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);
		}

		// Пустая страница: показываем её саму, а не отказываем в действии.
		return bounds.IsEmpty
			? ZoomToPage()
			: FitToPageRect(InflateForStroke(bounds));
	}

	/// <summary>Вписывает лист целиком.</summary>
	public bool ZoomToPage()
	{
		if (CurrentPage == null)
			return false;

		// Лист начинается в нуле абсолютных координат страницы, а page_origin
		// задаёт лишь смещение начала отсчёта для пользователя.
		var rect = new SKRect(0f, 0f, CurrentPage.size_uom.Width, CurrentPage.size_uom.Height);

		return FitToPageRect(rect);
	}

	/// <summary>
	/// Расширяет границы на половину самой толстой обводки.
	///
	/// GetBounds возвращает габариты геометрии, а обводка рисуется по центру
	/// контура и выходит наружу. Без запаса линия у края области оказалась бы
	/// срезанной ровно наполовину.
	/// </summary>
	private SKRect InflateForStroke(SKRect bounds)
	{
		if (bounds.IsEmpty || CurrentPage == null)
			return bounds;

		float mmPerUom = MathF.Max(CurrentPage.MmPerUom, 0.0001f);
		float maxHalfStroke = 0f;

		foreach (var shape in CurrentPage.Shapes)
		{
			if (shape.Stroke is { Enabled: true })
				maxHalfStroke = MathF.Max(maxHalfStroke, shape.Stroke.Thickness * 0.5f);
		}

		return Geometry.Inflate(bounds, maxHalfStroke, maxHalfStroke);
	}

	/// <summary>
	/// Задаёт масштаб, сохраняя центр области просмотра на месте.
	///
	/// Без пересчёта zero_pos приближение уводило бы содержимое в сторону:
	/// экранная точка привязана к нулю вида, а не к центру.
	/// </summary>
	public bool SetScale(float newScale)
	{
		if (CurrentPage == null)
			return false;

		newScale = Math.Clamp(newScale, MinFitScale, MaxFitScale);

		if (MathF.Abs(newScale - scale) < 1e-4f)
			return false;

		var viewport = GetCanvasViewport();

		var center = new Point(
			viewport.Left + viewport.Width / 2,
			viewport.Top + viewport.Height / 2);

		// Точка страницы под центром экрана до изменения масштаба.
		var anchor = ScreenToPage(center);

		scale = newScale;

		float f = ViewPxPerUom(CurrentPage);

		zero_pos = new SKPoint(
			center.X - (anchor.X - CurrentPage.page_origin.X) * f,
			center.Y - (anchor.Y - CurrentPage.page_origin.Y) * f);

		CurrentPage.view_zero = zero_pos;

		UpdateScrollBars();
		InvalidateCanvas();

		ScaleChanged?.Invoke();

		return true;
	}
	#endregion

	#region Scrollbars

	private void LayoutScrollBars()
	{
		// Размещение выполняет TableLayoutPanel в MainForm.
		_hScroll.Dock = DockStyle.Fill;
		_vScroll.Dock = DockStyle.Fill;
	}

	/// <summary>
	/// Вся поверхность контрола. Линейки рисуются здесь, в отброшенной
	/// холстом полосе сверху и слева.
	/// </summary>
	private Rectangle GetControlRect()
		=> new Rectangle(0, 0, Math.Max(0, Width), Math.Max(0, Height));

	/// <summary>
	/// Область холста: поверхность за вычетом линеек.
	///
	/// Полосы прокрутки больше не вычитаются — они вынесены из вида в сетку
	/// MainForm и в клиентскую область не попадают.
	/// </summary>
	private Rectangle GetCanvasViewport()
	{
		int left = Settings.show_rulers ? Settings.RulerThickness : 0;
		int top = Settings.show_rulers ? Settings.RulerThickness : 0;

		return new Rectangle(
			left,
			top,
			Math.Max(0, Width - left),
			Math.Max(0, Height - top));
	}

	private SKSize GetPageSizePx(Page page)
	{
		float pxPerMm = PxPerMm(page);
		return new SKSize(
			page.SizeMm.Width * pxPerMm * scale,
			page.SizeMm.Height * pxPerMm * scale);
	}

	/// <summary>
	/// Габариты всех фигур страницы в единицах страницы.
	///
	/// Направляющие не в счёт: они бесконечны и растянули бы поле прокрутки
	/// до бессмысленных величин. Невидимые — тоже: их не видно, значит
	/// и доезжать до них незачем.
	/// </summary>
	private SKRect GetShapesContentBounds(Page page)
	{
		long now = Environment.TickCount64;

		if (_contentBoundsValid
			&& ReferenceEquals(_contentBoundsPage, page)
			&& _contentBoundsShapeCount == page.Shapes.Count
			&& now - _contentBoundsTicks < ContentBoundsTtlMs)
		{
			return _contentBoundsCache;
		}

		var result = SKRect.Empty;
		bool any = false;

		foreach (var shape in page.Shapes)
		{
			if (shape is GuideLine || !shape.IsVisible)
				continue;

			var b = shape.GetBounds();

			if (b.Width <= 0f && b.Height <= 0f)
				continue;

			result = any ? Geometry.Union(result, b) : b;
			any = true;
		}

		_contentBoundsCache = any ? result : SKRect.Empty;
		_contentBoundsValid = true;
		_contentBoundsTicks = now;
		_contentBoundsPage = page;
		_contentBoundsShapeCount = page.Shapes.Count;

		return _contentBoundsCache;
	}

	/// <summary>Сбрасывает запомненные габариты содержимого.</summary>
	public void InvalidateContentBounds()
	{
		_contentBoundsValid = false;
	}

	/// <summary>
	/// Поле прокрутки в «виртуальных» пикселях — экранная точка минус zero_pos.
	///
	/// Лист в них занимает от нуля до своего размера, а точка листа p стоит
	/// в (p − page_origin)·масштаб: ровно то, что делает PageToScreen,
	/// за вычетом zero_pos.
	///
	/// Прежде полем был только лист. Фигуры, вынесенные за его край, в поле
	/// не попадали, полоса до них не доезжала, и добраться до такой фигуры
	/// можно было лишь панорамированием средней кнопкой — а по полосе
	/// казалось, что на листе её вовсе нет.
	/// </summary>
	private SKRect GetScrollContentRect(Page page)
	{
		var pageSize = GetPageSizePx(page);

		float left = 0f;
		float top = 0f;
		float right = pageSize.Width;
		float bottom = pageSize.Height;

		var shapes = GetShapesContentBounds(page);

		if (shapes.Width > 0f || shapes.Height > 0f)
		{
			float f = ViewPxPerUom(page);
			var origin = page.page_origin;

			left = MathF.Min(left, (shapes.Left - origin.X) * f);
			top = MathF.Min(top, (shapes.Top - origin.Y) * f);
			right = MathF.Max(right, (shapes.Right - origin.X) * f);
			bottom = MathF.Max(bottom, (shapes.Bottom - origin.Y) * f);
		}

		return new SKRect(left, top, right, bottom);
	}

	private void UpdateScrollBars()
	{
		if (CurrentPage == null)
			return;

		var viewport = GetCanvasViewport();
		var content = GetScrollContentRect(CurrentPage);

		float virtualLeft = content.Left - ViewMarginPx;
		float virtualTop = content.Top - ViewMarginPx;
		float virtualRight = content.Right + ViewMarginPx;
		float virtualBottom = content.Bottom + ViewMarginPx;

		_scrollVirtualLeft = virtualLeft;
		_scrollVirtualTop = virtualTop;

		int contentWidth = (int)MathF.Ceiling(virtualRight - virtualLeft);
		int contentHeight = (int)MathF.Ceiling(virtualBottom - virtualTop);

		int viewWidth = Math.Max(1, viewport.Width);
		int viewHeight = Math.Max(1, viewport.Height);

		int maxH = Math.Max(0, contentWidth - 1);
		int maxV = Math.Max(0, contentHeight - 1);

		int hValue = (int)MathF.Round(-virtualLeft - zero_pos.X);
		int vValue = (int)MathF.Round(-virtualTop - zero_pos.Y);

		hValue = Math.Clamp(hValue, 0, Math.Max(0, maxH - viewWidth + 1));
		vValue = Math.Clamp(vValue, 0, Math.Max(0, maxV - viewHeight + 1));

		_updatingScrollbars = true;
		try
		{
			_hScroll.Minimum = 0;
			_hScroll.Maximum = maxH;
			_hScroll.LargeChange = viewWidth;
			_hScroll.SmallChange = Math.Max(16, viewWidth / 20);
			_hScroll.Value = Math.Min(hValue, Math.Max(_hScroll.Minimum, _hScroll.Maximum - _hScroll.LargeChange + 1));

			_vScroll.Minimum = 0;
			_vScroll.Maximum = maxV;
			_vScroll.LargeChange = viewHeight;
			_vScroll.SmallChange = Math.Max(16, viewHeight / 20);
			_vScroll.Value = Math.Min(vValue, Math.Max(_vScroll.Minimum, _vScroll.Maximum - _vScroll.LargeChange + 1));
		}
		finally
		{
			_updatingScrollbars = false;
		}
	}

	/// <summary>
	/// Доводка вида, когда протяжка увела курсор к краю или за край.
	///
	/// Без неё фигуру нельзя было положить туда, чего в момент начала
	/// протяжки не было видно: курсор упирался в край, вид стоял, и
	/// приходилось бросать фигуру на полпути, прокручивать и брать заново.
	///
	/// Лист едет, курсор стоит — значит мир под курсором уехал, и операция
	/// обязана пересчитаться в новой точке. Иначе фигура осталась бы висеть
	/// на прежнем месте листа, а не шла бы за курсором.
	///
	/// Положение курсора берётся не из события, а напрямую: такт приходит
	/// по времени, а не по движению мыши, и пока пользователь держит курсор
	/// неподвижно у края, движения ему никто не присылает.
	/// </summary>
	private void AutoScrollTick(object? sender, EventArgs e)
	{
		// Кнопка отпущена — протяжки нет, что бы ни осталось в ActiveOperation.
		// Без этой проверки зависшая операция заставляла бы вид уезжать
		// всякий раз, когда курсор просто проходит у края.
		if (CurrentPage == null
			|| _interaction.ActiveOperation == null
			|| _panning
			|| Control.MouseButtons == MouseButtons.None)
		{
			_autoScrollTimer.Stop();
			return;
		}

		var viewport = GetCanvasViewport();

		if (viewport.Width < 8 || viewport.Height < 8)
			return;

		var mouse = PointToClient(MousePosition);

		float dx = EdgePush(mouse.X, viewport.Left, viewport.Right);
		float dy = EdgePush(mouse.Y, viewport.Top, viewport.Bottom);

		// Ось, по которой курсор сейчас вне полосы, считается взведённой:
		// к кромке по ней он придёт своим ходом, а не окажется там
		// с самого начала.
		if (dx == 0f)
			_autoScrollArmedX = true;

		if (dy == 0f)
			_autoScrollArmedY = true;

		if (!_autoScrollArmedX)
			dx = 0f;

		if (!_autoScrollArmedY)
			dy = 0f;

		if (dx == 0f && dy == 0f)
			return;

		zero_pos = new SKPoint(zero_pos.X + dx, zero_pos.Y + dy);
		CurrentPage.view_zero = zero_pos;

		UpdateScrollBars();

		var world = ScreenToPage(mouse);

		_interaction.LastPage = _interaction.CurrentPage;
		_interaction.CurrentPage = world;

		_interaction.ActiveOperation.Update(this, _interaction, world);
		MainForm.sel.NotifyGeometryChanged();

		InvalidateCanvas();
	}

	/// <summary>
	/// Сдвиг листа за один такт по одной оси.
	///
	/// Положительный сдвиг двигает лист вправо или вниз, то есть открывает
	/// то, что лежит левее или выше. Чем дальше курсор зашёл за кромку, тем
	/// быстрее ход, но не быстрее предела: иначе рывок мыши за край окна
	/// уносил бы вид на экраны от нужного места.
	/// </summary>
	private static float EdgePush(int value, int min, int max)
	{
		if (value < min + AutoScrollBandPx)
			return MathF.Min(AutoScrollMaxStepPx, (min + AutoScrollBandPx - value) * 0.5f);

		if (value > max - AutoScrollBandPx)
			return -MathF.Min(AutoScrollMaxStepPx, (value - (max - AutoScrollBandPx)) * 0.5f);

		return 0f;
	}

	private void HScroll_Scroll(object? sender, ScrollEventArgs e)
	{
		if (_updatingScrollbars || CurrentPage == null)
			return;

		zero_pos = new SKPoint(-_scrollVirtualLeft - _hScroll.Value, zero_pos.Y);
		CurrentPage.view_zero = zero_pos;
		InvalidateCanvas();
	}

	private void VScroll_Scroll(object? sender, ScrollEventArgs e)
	{
		if (_updatingScrollbars || CurrentPage == null)
			return;

		zero_pos = new SKPoint(zero_pos.X, -_scrollVirtualTop - _vScroll.Value);
		CurrentPage.view_zero = zero_pos;
		InvalidateCanvas();
	}

	#endregion

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		LayoutScrollBars();
		UpdateScrollBars();
		InvalidateCanvas();
	}

	#region Отрисовка

	private void MainView_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		int save = canvas.Save();
		canvas.Clear(Settings.background_color.ToSKColor());

		var viewport = GetCanvasViewport();
		if (viewport.Width <= 0 || viewport.Height <= 0)
			return;

		if (MainForm.cur_doc == null || CurrentPage == null)
			return;

		int restoreTo = canvas.Save();

		try
		{
			canvas.Save();
			canvas.ClipRect(GetViewportScreenRect());

			DrawPage(canvas, CurrentPage);

			// Сетка и рамка печати лежат ПОД фигурами: это разметка листа,
			// а не содержимое рисунка, и перекрывать нарисованное она не должна.
			DrawGrid(canvas, CurrentPage);
			DrawPrintBorders(canvas, CurrentPage);

			DrawShapes(canvas, CurrentPage);

			// GRContext.ResetContext здесь больше не нужен: сырых GL-вызовов
			// в приложении не осталось, состояние Ganesh никто не портит.

			DrawSelectionOverlay(canvas, CurrentPage);
			imageEditTool?.DrawOverlay(canvas, this, _interaction);
			_interaction.ToolHandler?.DrawOverlay(canvas, this, _interaction);
			DrawPageOrigin(canvas);
			canvas.Restore();
			if (Settings.show_rulers)
				DrawRulers(canvas);
		}
		finally
		{
			// RestoreToCount гарантирует сбалансированный стек даже если
			// какой-нибудь Draw внутри бросит исключение или забудет Restore.
			canvas.RestoreToCount(restoreTo);
		}
	}

	private void DrawPageOrigin(SKCanvas canvas)
	{
		if (CurrentPage == null)
			return;

		var p = AbsolutePageToScreen(new SKPoint(-CurrentPage.page_origin.X, -CurrentPage.page_origin.Y));

		canvas.DrawCircle(p, 6f, OriginFillPaint);
		canvas.DrawCircle(p, 6f, OriginStrokePaint);
		canvas.DrawLine(p.X - 10, p.Y, p.X + 10, p.Y, OriginStrokePaint);
		canvas.DrawLine(p.X, p.Y - 10, p.X, p.Y + 10, OriginStrokePaint);
	}

	private void DrawRulers(SKCanvas canvas)
	{
		if (!Settings.show_rulers)
			return;

		var topRect = GetTopRulerRect();
		var leftRect = GetLeftRulerRect();
		var cornerRect = GetRulerCornerRect();

		canvas.DrawRect(SKRect.Create(topRect.Left, topRect.Top, topRect.Width, topRect.Height), RulerBackPaint);
		canvas.DrawRect(SKRect.Create(leftRect.Left, leftRect.Top, leftRect.Width, leftRect.Height), RulerBackPaint);
		canvas.DrawRect(SKRect.Create(cornerRect.Left, cornerRect.Top, cornerRect.Width, cornerRect.Height), RulerBackPaint);

		canvas.DrawRect(SKRect.Create(topRect.Left, topRect.Top, topRect.Width, topRect.Height), RulerBorderPaint);
		canvas.DrawRect(SKRect.Create(leftRect.Left, leftRect.Top, leftRect.Width, leftRect.Height), RulerBorderPaint);
		canvas.DrawRect(SKRect.Create(cornerRect.Left, cornerRect.Top, cornerRect.Width, cornerRect.Height), RulerBorderPaint);

		// До проверки CurrentPage: квадрат и его назначение существуют
		// независимо от того, открыта ли страница.
		DrawRulerCornerCross(canvas, cornerRect);

		if (CurrentPage == null)
			return;

		float pxPerUom = ViewPxPerUom(CurrentPage);
		if (pxPerUom < 0.0001f)
			return;

		DrawHorizontalRulerTicks(canvas, topRect, pxPerUom);
		DrawVerticalRulerTicks(canvas, leftRect, pxPerUom);

		// Полоски курсора рисуются последними — поверх насечек и подписей.
		DrawRulerCursor(canvas, topRect, leftRect);
	}

	/// <summary>
	/// Крест на квадрате пересечения линеек.
	///
	/// Тот же знак, что и у маркера начала координат на холсте: за этот
	/// квадрат начало координат и перетаскивают, но раньше он был пустым
	/// и о своём назначении никак не сообщал.
	/// </summary>
	private void DrawRulerCornerCross(SKCanvas canvas, Rectangle cornerRect)
	{
		float cx = cornerRect.Left + cornerRect.Width * 0.5f;
		float cy = cornerRect.Top + cornerRect.Height * 0.5f;

		// Радиус от размера самого квадрата, а не константой: толщина
		// линеек берётся из настроек и может отличаться.
		float r = MathF.Max(3f, MathF.Min(cornerRect.Width, cornerRect.Height) * 0.28f);

		canvas.DrawCircle(cx, cy, r, RulerCornerCrossPaint);
		canvas.DrawLine(cx - r - 3f, cy, cx + r + 3f, cy, RulerCornerCrossPaint);
		canvas.DrawLine(cx, cy - r - 3f, cx, cy + r + 3f, RulerCornerCrossPaint);
	}

	/// <summary>
	/// Полоски, показывающие положение курсора на обеих линейках.
	///
	/// Координата берётся ЭКРАННАЯ, без перевода в страничную и обратно:
	/// полоска обязана стоять ровно под курсором, а лишний круг
	/// преобразований дал бы дрожание в полпикселя на дробном масштабе.
	/// </summary>
	private void DrawRulerCursor(SKCanvas canvas, Rectangle topRect, Rectangle leftRect)
	{
		if (!_cursorOverCanvas)
			return;

		var viewport = GetCanvasViewport();

		// Полоска показывается, только пока курсор в области рисования:
		// над самими линейками и полосами прокрутки она смысла не имеет.
		if (_cursorScreen.X >= viewport.Left && _cursorScreen.X <= viewport.Right)
		{
			canvas.DrawRect(
				SKRect.Create(_cursorScreen.X, topRect.Top + 1f, 1f, topRect.Height - 2f),
				RulerCursorPaint);
		}

		if (_cursorScreen.Y >= viewport.Top && _cursorScreen.Y <= viewport.Bottom)
		{
			canvas.DrawRect(
				SKRect.Create(leftRect.Left + 1f, _cursorScreen.Y, leftRect.Width - 2f, 1f),
				RulerCursorPaint);
		}
	}

	/// <summary>
	/// Подбирает шаг делений так, чтобы между рисками было не меньше MinTickSpacingPx.
	/// Прежний код всегда шёл с шагом в одну единицу и на мелком масштабе прокручивал
	/// десятки тысяч итераций, отбрасывая почти все через continue.
	/// </summary>
	private static float ChooseTickStep(float pxPerUom)
	{
		const float MinTickSpacingPx = 5f;

		if (pxPerUom <= 0.0001f)
			return 1f;

		float step = 1f;

		// Ряд 1, 2, 5, 10, 20, 50, ...
		int[] mantissa = { 1, 2, 5 };
		int decade = 0;
		int index = 0;

		while (step * pxPerUom < MinTickSpacingPx && decade < 12)
		{
			index++;
			if (index >= mantissa.Length)
			{
				index = 0;
				decade++;
			}

			step = mantissa[index] * MathF.Pow(10f, decade);
		}

		// Слишком крупный масштаб — дробим шаг.
		while (step > 0.001f && step * 0.5f * pxPerUom >= MinTickSpacingPx * 2f)
			step *= 0.5f;

		return step;
	}

	private void DrawHorizontalRulerTicks(SKCanvas canvas, Rectangle rect, float pxPerUom)
	{
		if (CurrentPage == null || pxPerUom <= 0.0001f)
			return;

		float start = ScreenToPage(new Point(rect.Left, 0)).X;
		float end = ScreenToPage(new Point(rect.Right, 0)).X;

		if (end < start)
			(start, end) = (end, start);

		float step = ChooseTickStep(pxPerUom);
		float major = step * 10f;

		float first = MathF.Floor(start / step) * step;

		for (float u = first; u <= end; u += step)
		{
			float x = PageToScreen(new SKPoint(u, 0)).X;
			if (x < rect.Left || x > rect.Right)
				continue;

			bool isZero = MathF.Abs(u) < step * 0.5f;
			bool isMajor = MathF.Abs(u % major) < step * 0.5f;

			float markHeight = isZero
				? Settings.RulerZeroMarkSize
				: (isMajor ? Settings.RulerMajorMarkSize : Settings.RulerMinorMarkSize);

			canvas.DrawLine(
				x, rect.Bottom - 1,
				x, rect.Bottom - markHeight,
				isZero ? RulerZeroTickPaint : RulerTickPaint);
		}
	}

	private void DrawVerticalRulerTicks(SKCanvas canvas, Rectangle rect, float pxPerUom)
	{
		if (MainForm.cur_doc == null || CurrentPage == null || pxPerUom <= 0.0001f)
			return;
		

		float start = ScreenToPage(new Point(0, rect.Top)).Y;
		float end = ScreenToPage(new Point(0, rect.Bottom)).Y;

		if (end < start)
			(start, end) = (end, start);

		float step = ChooseTickStep(pxPerUom);
		float major = step * 10f;

		float first = MathF.Floor(start / step) * step;

		for (float u = first; u <= end; u += step)
		{
			float y = PageToScreen(new SKPoint(0, u)).Y;
			if (y < rect.Top || y > rect.Bottom)
				continue;

			bool isZero = MathF.Abs(u) < step * 0.5f;
			bool isMajor = MathF.Abs(u % major) < step * 0.5f;

			float markWidth = isZero
				? Settings.RulerZeroMarkSize
				: (isMajor ? Settings.RulerMajorMarkSize : Settings.RulerMinorMarkSize);

			canvas.DrawLine(
				rect.Right - 1, y,
				rect.Right - markWidth, y,
				isZero ? RulerZeroTickPaint : RulerTickPaint);
		}
	}

	private void DrawPage(SKCanvas canvas, Page page)
	{
		var tl = AbsolutePageToScreen(new SKPoint(0, 0));
		var br = AbsolutePageToScreen(new SKPoint(page.size_uom.Width, page.size_uom.Height));

		var pageRect = new SKRect(
			Math.Min(tl.X, br.X),
			Math.Min(tl.Y, br.Y),
			Math.Max(tl.X, br.X),
			Math.Max(tl.Y, br.Y));

		var shadowRect = pageRect;
		shadowRect.Offset(10, 10);

		PageShadowPaint.Color = Settings.page_shadow_color.ToSKColor();

		// Цвет фона страницы. Полностью прозрачное значение считается
		// «не задан»: default(SKColor) — прозрачный чёрный, и без этой ветки
		// страница с неинициализированным page_bg_color выглядела бы чёрной.
		// Если page_bg_color инициализируется белым, ветку можно убрать.
		PageFillPaint.Color = page.page_bg_color.Alpha > 0
			? page.page_bg_color
			: page.background_color.ToSKColor();

		PageBorderPaint.Color = Settings.page_border_color.ToSKColor();

		canvas.DrawRect(shadowRect, PageShadowPaint);
		canvas.DrawRect(pageRect, PageFillPaint);
		canvas.DrawRect(pageRect, PageBorderPaint);
	}

	/// <summary>
	/// Прямоугольник листа в КООРДИНАТАХ ФИГУР.
	///
	/// В этой системе page_zero лежит в (0, 0), а верхний левый угол
	/// листа — в page_origin. Именно её понимает PageToScreen, и в ней же
	/// заданы все фигуры, поэтому разметка обязана строиться здесь, а не
	/// в абсолютных координатах листа.
	/// </summary>
	private static SKRect GetSheetRectPage(Page page)
	{
		return new SKRect(
			page.page_origin.X,
			page.page_origin.Y,
			page.page_origin.X + page.size_uom.Width,
			page.page_origin.Y + page.size_uom.Height);
	}

	/// <summary>Тот же лист, но уже на экране, с нормализованными сторонами.</summary>
	private SKRect GetSheetRectScreen(Page page)
	{
		var r = GetSheetRectPage(page);

		var tl = PageToScreen(new SKPoint(r.Left, r.Top));
		var br = PageToScreen(new SKPoint(r.Right, r.Bottom));

		return new SKRect(
			MathF.Min(tl.X, br.X),
			MathF.Min(tl.Y, br.Y),
			MathF.Max(tl.X, br.X),
			MathF.Max(tl.Y, br.Y));
	}

	/// <summary>
	/// Сетка страницы.
	///
	/// Ноль сетки — page_zero, а не угол листа: сетка нужна для промера
	/// от той же точки, от которой считают линейки и координаты фигур.
	/// Поэтому линии ставятся по кратным шага в координатах фигур, где
	/// page_zero и есть (0, 0).
	///
	/// Только на экране: в PageRenderer и ShapeExporter её нет намеренно —
	/// сетка это разметка для работы, а не часть рисунка, и на бумаге
	/// её быть не должно.
	/// </summary>
	private void DrawGrid(SKCanvas canvas, Page page)
	{
		if (!PageGrid.IsVisible(page))
			return;

		float pxPerUom = ViewPxPerUom(page);

		if (pxPerUom <= 0.0001f)
			return;

		// Прореженный шаг: на отдалении остаётся каждая пятая или десятая
		// линия, но сетка не пропадает.
		float step = PageGrid.GetEffectiveStepUom(page, pxPerUom);
		int factor = PageGrid.GetStepFactor(page, pxPerUom);

		var (medium, major) = PageGrid.GetDivisions(page);

		var sheet = GetSheetRectScreen(page);
		var viewport = GetViewportScreenRect();

		var visible = new SKRect(
			MathF.Max(sheet.Left, viewport.Left),
			MathF.Max(sheet.Top, viewport.Top),
			MathF.Min(sheet.Right, viewport.Right),
			MathF.Min(sheet.Bottom, viewport.Bottom));

		if (visible.Width <= 0f || visible.Height <= 0f)
			return;

		using var minorPaint = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.grid_minor_color
		};

		using var mediumPaint = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.grid_medium_color
		};

		using var majorPaint = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.grid_major_color
		};

		using var zeroPaint = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.grid_zero_color
		};

		canvas.Save();
		canvas.ClipRect(visible);

		// Границы обхода — в координатах фигур: берём видимый кусок листа
		// и переводим его углы обратно.
		var fromPage = ScreenToPage(new SKPoint(visible.Left, visible.Top));
		var toPage = ScreenToPage(new SKPoint(visible.Right, visible.Bottom));

		int firstX = (int)MathF.Floor(MathF.Min(fromPage.X, toPage.X) / step);
		int lastX = (int)MathF.Ceiling(MathF.Max(fromPage.X, toPage.X) / step);

		for (int i = firstX; i <= lastX; i++)
		{
			float x = PageToScreen(new SKPoint(i * step, 0f)).X;

			if (x < visible.Left - 1f || x > visible.Right + 1f)
				continue;

			// Номер линии в ИСХОДНОЙ разметке: только так уцелевшие после
			// прореживания линии сохраняют своё деление на средние и крупные.
			canvas.DrawLine(x, visible.Top, x, visible.Bottom,
				PaintFor(i * factor, medium, major, minorPaint, mediumPaint, majorPaint, zeroPaint));
		}

		int firstY = (int)MathF.Floor(MathF.Min(fromPage.Y, toPage.Y) / step);
		int lastY = (int)MathF.Ceiling(MathF.Max(fromPage.Y, toPage.Y) / step);

		for (int i = firstY; i <= lastY; i++)
		{
			float y = PageToScreen(new SKPoint(0f, i * step)).Y;

			if (y < visible.Top - 1f || y > visible.Bottom + 1f)
				continue;

			canvas.DrawLine(visible.Left, y, visible.Right, y,
				PaintFor(i * factor, medium, major, minorPaint, mediumPaint, majorPaint, zeroPaint));
		}

		canvas.Restore();
	}

	private static SKPaint PaintFor(
		int index,
		int medium,
		int major,
		SKPaint minor,
		SKPaint mid,
		SKPaint big,
		SKPaint zero)
	{
		// Ось проверяется первой: нулевой индекс кратен и major, и medium,
		// и без этой ветки досталась бы кисть крупной линии.
		if (index == 0)
			return zero;

		if (major > 0 && index % major == 0)
			return big;

		if (medium > 0 && index % medium == 0)
			return mid;

		return minor;
	}

	/// <summary>
	/// Рамка печатаемой области принтера по умолчанию.
	///
	/// Поля запрашиваются у PrinterCatalog лениво и кэшируются: обращение
	/// идёт к драйверу, и делать его на каждый кадр нельзя. Пока данных
	/// нет, рамка не рисуется — показывать выдуманные поля хуже, чем
	/// не показывать ничего.
	/// </summary>
	private void DrawPrintBorders(SKCanvas canvas, Page page)
	{
		if (!page.PrintBordersEnabled)
			return;

		var margins = PrinterCatalog.GetDefaultMarginsMm();

		if (margins == null)
			return;

		float mmPerUom = page.MmPerUom;

		if (mmPerUom <= 0.0001f)
			return;

		var sheet = GetSheetRectPage(page);

		float left = sheet.Left + margins.LeftMm / mmPerUom;
		float top = sheet.Top + margins.TopMm / mmPerUom;
		float right = sheet.Right - margins.RightMm / mmPerUom;
		float bottom = sheet.Bottom - margins.BottomMm / mmPerUom;

		if (right <= left || bottom <= top)
			return;

		var tl = PageToScreen(new SKPoint(left, top));
		var br = PageToScreen(new SKPoint(right, bottom));

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = Settings.print_border_color
		};

		canvas.DrawRect(
			new SKRect(
				MathF.Min(tl.X, br.X),
				MathF.Min(tl.Y, br.Y),
				MathF.Max(tl.X, br.X),
				MathF.Max(tl.Y, br.Y)),
			paint);
	}

	private void DrawMarqueeRect(SKCanvas canvas, DragOperation? op)
	{
		SKRect? rect = op switch
		{
			MarqueeSelectionOperation m => m.Rect,
			CurveNodeMarqueeSelectionOperation cm => cm.Rect,
			_ => null
		};

		if (rect == null)
			return;

		var tl = PageToScreen(new SKPoint(rect.Value.Left, rect.Value.Top));
		var br = PageToScreen(new SKPoint(rect.Value.Right, rect.Value.Bottom));

		var sr = new SKRect(
			Math.Min(tl.X, br.X),
			Math.Min(tl.Y, br.Y),
			Math.Max(tl.X, br.X),
			Math.Max(tl.Y, br.Y));

		bool crossing = op is MarqueeSelectionOperation { Crossing: true };

		canvas.DrawRect(sr, crossing ? MarqueeCrossingFillPaint : MarqueeFillPaint);
		canvas.DrawRect(sr, crossing ? MarqueeCrossingStrokePaint : MarqueeStrokePaint);
	}

	/// <summary>
	/// Видимая область в мировых координатах для прилипания.
	///
	/// Динамические направляющие строятся только от того, что пользователь
	/// видит. Линия, протянувшаяся от фигуры за краем экрана, выглядит
	/// пришедшей ниоткуда: совмещаться не с чем, объяснения на экране нет,
	/// а фигура при этом прыгает.
	/// </summary>
	public SKRect GetVisibleWorldRectForSnapping()
		=> CurrentPage == null ? SKRect.Empty : GetVisibleWorldRect(CurrentPage);

	/// <summary>Видимая область в мировых координатах (UoM страницы), с запасом на обводку.</summary>
	private SKRect GetVisibleWorldRect(Page page)
	{
		var vp = GetCanvasViewport();

		var tl = ScreenToPage(new Point(vp.Left, vp.Top));
		var br = ScreenToPage(new Point(vp.Right, vp.Bottom));

		var rect = new SKRect(
			MathF.Min(tl.X, br.X),
			MathF.Min(tl.Y, br.Y),
			MathF.Max(tl.X, br.X),
			MathF.Max(tl.Y, br.Y));

		// Запас: обводка, тени и маркеры выходят за геометрические границы фигуры.
		float pad = ScreenPixelsToPageUnits(64f);
		return Geometry.Inflate(rect, pad, pad);
	}

	private void DrawShapes(SKCanvas canvas, Page page)
	{
		float pxPerWorldUnit = ViewPxPerUom(page);
		if (pxPerWorldUnit <= 0.0001f)
			return;

		float mmPerWorldUnit = page.MmPerUom;

		// Сбор порядка отрисовки в переиспользуемые буферы.
		// Прежний код на каждый кадр делал два LINQ-запроса с OrderBy,
		// то есть две аллокации + две сортировки при каждом Invalidate.
		_drawBuffer.Clear();
		_guideBuffer.Clear();

		var shapes = page.Shapes;
		for (int i = 0; i < shapes.Count; i++)
		{
			var s = shapes[i];

			if (s is GuideLine g)
				_guideBuffer.Add(g);
			else
				_drawBuffer.Add(s);
		}

		_drawBuffer.Sort(static (a, b) => a.Layer.CompareTo(b.Layer));
		_guideBuffer.Sort(static (a, b) => a.Layer.CompareTo(b.Layer));

		var visible = GetVisibleWorldRect(page);

		// Режим actual dpi приводит картинку к разрешению документа.
		ImageShape.RenderDpi = page.DPI;

		canvas.Save();
		canvas.Translate(zero_pos.X, zero_pos.Y);
		canvas.Scale(pxPerWorldUnit, pxPerWorldUnit);
		canvas.Translate(-page.page_origin.X, -page.page_origin.Y);

		for (int i = 0; i < _drawBuffer.Count; i++)
		{
			var shape = _drawBuffer[i];

			if (!shape.IsVisible)
				continue;

			// Отсечение по видимой области: фигуры за экраном не доходят до Draw.
			// Для изображения это особенно важно — иначе Skia строит слой
			// с фильтрами коррекции даже для того, чего не видно.
			// GetRenderBounds, а не GetBounds: тень выходит за габариты
			// фигуры, и по ним она пропадала бы раньше самой фигуры.
			var bounds = shape.GetRenderBounds();
			if (!bounds.IsEmpty && !bounds.IntersectsWith(visible))
				continue;

			shape.Render(canvas, mmPerWorldUnit);
		}

		if (Settings.show_guidelines)
		{
			for (int i = 0; i < _guideBuffer.Count; i++)
				_guideBuffer[i].Draw(canvas, pxPerWorldUnit);
		}

		canvas.Restore();
	}

	private void DrawSelectionOverlay(SKCanvas canvas, Page page)
	{
		if (IsCurveEditMode)
		{
			if (_interaction.Selection.Count == 1 && _interaction.Selection[0] is CurveShape)
				CurveAdorner.Draw(canvas, this, _interaction);
		}

		bool isTextAlongShapeSelected =
			_interaction.Selection.Count == 1 &&
			TextAlongShapeAdorner.IsApplicable(_interaction.Selection[0]);

		if (!IsCurveEditMode)
		{
			SelectionAdorner.Draw(canvas, this, _interaction);

			if (isTextAlongShapeSelected)
				TextAlongShapeAdorner.Draw(canvas, this, _interaction);

			FillAdorner.Draw(canvas, this, _interaction);
			ShadowAdorner.Draw(canvas, this, _interaction);
			TransparencyAdorner.Draw(canvas, this, _interaction);
			TransformAdorner.Draw(canvas, this, _interaction);
		}

		DrawMarqueeRect(canvas, _interaction.ActiveOperation);

		if (_interaction.Selection.Count == 1 && !isTextAlongShapeSelected)
		{
			ShapePropertyAdorner.Draw(canvas, this, _interaction);
			ShapePointAdorner.Draw(canvas, this, _interaction);
		}

		if (_interaction.SelectedGuide != null)
			GuideAdorner.Draw(canvas, this, _interaction.SelectedGuide);

		HoveredNodeAdorner.Draw(canvas, this, _interaction);
		SnapAdorner.Draw(canvas, this, _interaction);
	}

	/// <summary>
	/// Узел под курсором в режиме фигур.
	///
	/// Считается на тех же условиях, что и hit-test, и по той же причине:
	/// перебор узлов всех фигур страницы стоит примерно столько же,
	/// и на панорамировании или во время протяжки он не нужен.
	///
	/// Ищется среди ВСЕХ фигур, а не только выделенных: подсветка
	/// показывает, куда можно прицелиться, и до нажатия выделения может
	/// ещё не быть.
	/// </summary>
	private void UpdateHoveredNode(SKPoint world, bool allowed)
	{
		var previous = _interaction.HoveredNodeWorld;

		if (!allowed || MainForm.Mode != EditorMode.Shape || CurrentPage == null)
		{
			_interaction.HoveredNodeShape = null;
			_interaction.HoveredNodeWorld = null;

			if (previous != null)
				InvalidateCanvas();

			return;
		}

		var found = ShapeNodePicker.FindNearest(
			this,
			GetShapesFrontToBack().Where(s => s.IsVisible && !s.IsLocked),
			world,
			Settings.snap_dist);

		_interaction.HoveredNodeShape = found?.Owner;
		_interaction.HoveredNodeWorld = found?.WorldPoint;

		// Перерисовка только при смене узла: иначе холст обновлялся бы
		// на каждое движение мыши без единого видимого изменения.
		if (!SamePoint(previous, _interaction.HoveredNodeWorld))
			InvalidateCanvas();
	}

	private static bool SamePoint(SKPoint? a, SKPoint? b)
	{
		if (a == null && b == null)
			return true;

		if (a == null || b == null)
			return false;

		return MathF.Abs(a.Value.X - b.Value.X) < 0.0001f
			&& MathF.Abs(a.Value.Y - b.Value.Y) < 0.0001f;
	}

	#endregion

	#region Mouse and Keyboard
	protected override void OnMouseDown(MouseEventArgs e)
	{
		// Группа, оставшаяся от прошлого взаимодействия, закрывается здесь.
		//
		// Нажатие без парного отпускания — обычное дело: модальный диалог,
		// открытый прямо из обработчика нажатия, забирает отпускание себе;
		// окно теряет перехват мыши при переключении задач. Вложенный
		// Begin в такой ситуации увеличил бы глубину группы, и одного
		// Commit в OnMouseUp уже не хватило бы, чтобы её закрыть.
		CloseStaleUndoGroup();

		base.OnMouseDown(e);
		Focus();
		UpdateModifierKeys();

		if (CurrentPage == null)
			return;

		bool secondClickOfDoubleClick = IsSecondClickOfDoubleClick(e);
		if (e.Button == MouseButtons.Left)
		{
			lastLeftMouseDownTicks = Environment.TickCount64;
			lastLeftMouseDownPoint = e.Location;

			if (secondClickOfDoubleClick)
			{
				suppressNextLeftMouseUp = true;
				return;
			}
		}

		var world = ScreenToPage(e.Location);
		_interaction.MouseDownPage = world;
		_interaction.LastPage = world;
		_interaction.CurrentPage = world;

		// Узел под курсором запоминается именно здесь: перетаскивание
		// начнётся позже, когда курсор уйдёт от точки нажатия, и к тому
		// моменту подсвеченным будет уже другой узел.
		//
		// Сначала пересчёт. Подсветка обновляется на движении мыши, а вид
		// меняется и без него — колесом, полосами, вписыванием. После такого
		// изменения запомненный узел остаётся от прежнего масштаба и лежит
		// уже далеко от курсора. Пока он только подсвечивался, это было
		// незаметно; теперь за него берут фигуру, и щелчок по пустому месту
		// уводил бы чужую.
		UpdateHoveredNode(world, _interaction.ActiveOperation == null);

		_interaction.PressedNodeShape = _interaction.HoveredNodeShape;
		_interaction.PressedNodeWorld = _interaction.HoveredNodeWorld;

		// Проверяем imageEditTool, а не _interaction: прежнее условие
		// "_interaction != null" всегда истинно и давало NRE, если режим Image
		// был выставлен, а инструмент ещё не создан.
		if (MainForm.Mode == EditorMode.Image && imageEditTool != null)
		{
			bool onImage = IsPointOnEditableImage(world);

			// Щелчок мимо картинки выключает режим рисования по ней.
			// Только для инструментов, работающих по пикселям: рамку и
			// лассо нормально начинать за краем изображения.
			if (e.Button == MouseButtons.Left
				&& IsPaintKind(ActiveImageToolKind)
				&& !onImage)
			{
				mainForm?.ExitImageToolMode();
				InvalidateCanvas();
				return;
			}

			// Щелчок по ДРУГОЙ фигуре выводит из режима изображения,
			// и этот же щелчок обрабатывается дальше обычным путём.
			//
			// Без этого выхода рамка и лассо перехватывали всё подряд:
			// они по замыслу начинаются и за краем картинки, поэтому
			// проверка выше их не касается, и любой щелчок по соседней
			// фигуре начинал новое выделение пикселей вместо перехода
			// к ней. Со стороны это выглядело как застревание — ничего
			// не выделяется и не двигается, пока не нажмёшь курсор
			// на панели фигур.
			//
			// Возврата здесь нет намеренно: режим уже погашен, imageEditTool
			// обнулён, и остаток обработчика отработает щелчок как обычное
			// выделение. Иначе пользователю пришлось бы щёлкать дважды.
			if (e.Button == MouseButtons.Left && !onImage && HitsShapeOtherThanEditedImage(world))
			{
				mainForm?.ExitImageToolMode();
			}
			else
			{
				_interaction.MouseDownHit = HitResult.None;
				imageEditTool.OnMouseDown(this, _interaction, world, e.Button);
				InvalidateCanvas();
				return;
			}
		}

		#region Rulers and guidelines

		if (Settings.show_rulers)
		{
			Rectangle origin_rect = new Rectangle(0, 0, Settings.RulerThickness, Settings.RulerThickness);
			if (e.Button == MouseButtons.Left && origin_rect.Contains(e.Location))
			{
				_draggingPageOrigin = true;
				Cursor = Cursors.SizeAll;
				InvalidateCanvas();
				return;
			}
		}

		if (e.Button == MouseButtons.Left)
		{
			var topRuler = GetTopRulerRect();
			var leftRuler = GetLeftRulerRect();

			bool fromLeftRuler = leftRuler.Contains(e.Location);
			bool fromTopRuler = !fromLeftRuler && topRuler.Contains(e.Location);

			if (fromLeftRuler || fromTopRuler)
			{
				var world_pos = ScreenToPage(e.Location);
				var orientation = fromLeftRuler ? GuideOrientation.Vertical : GuideOrientation.Horizontal;

				// Создание направляющей, снятие выделения и перетаскивание —
				// один шаг истории. Закрывается в OnMouseUp, поэтому флаг
				// обязателен: без него группа оставалась открытой НАВСЕГДА.
				//
				// Это и была главная поломка истории. Пока группа открыта,
				// Push кладёт действия в неё, а не в список шагов, и после
				// одного вытягивания направляющей с линейки ни одно
				// последующее действие в историю не попадало. Отмена
				// продолжала работать и откатывала последний закрытый шаг —
				// то есть давнюю правку совсем других фигур.
				BeginUndoGroup("Create guide");
				_undoGroupOpen = true;

				GuideLine guide;

				WithSelectionUndo(() =>
				{
					SelectionHelper.Clear(_interaction);
				});

				// CreateGuideUndoable идёт через Page.AddShape, поэтому
				// PageUndoRecorder сам запишет добавление фигуры.
				guide = CreateGuideUndoable(orientation, world_pos);
				SelectGuide(guide);

				_interaction.MouseDownPage = world_pos;
				_interaction.LastPage = world_pos;
				_interaction.CurrentPage = world_pos;
				_interaction.MouseDownHit = new HitResult
				{
					Kind = HitKind.GuideBody,
					Shape = guide,
					PagePoint = world_pos
				};

				StartOperation(new CreateGuideOperation(guide), world_pos);
				InvalidateCanvas();
				return;
			}
		}

		#endregion

		if (e.Button == MouseButtons.Middle)
		{
			_panning = true;
			_panStartMouse = e.Location;
			_panStartZero = zero_pos;
			Cursor = Cursors.Hand;
			return;
		}

		if (_interaction.CurrentTool != ShapeToolKind.Select)
		{
			if (_interaction.CurrentTool == ShapeToolKind.SelectOtherShape)
				_interaction.MouseDownHit = HitTester.HitTest(this, _interaction, world);
			else
				_interaction.MouseDownHit = HitResult.None;

			// Инструменты создания фигур меняют и выделение, и состав страницы.
			// Группа объединяет это в один шаг; добавление фигуры запишет
			// PageUndoRecorder через событие Page.ShapeAdded.
			BeginUndoGroup("Create shape");

			WithSelectionUndo(() =>
				_interaction.ToolHandler?.OnMouseDown(this, _interaction, world, e.Button));

			CommitUndoGroup();

			InvalidateCanvas();
			return;
		}

		if (e.Button == MouseButtons.Left && _interaction.Selection.Count == 1)
		{
			var selectedShape = _interaction.Selection[0];

			if (selectedShape?.Fill != null && selectedShape.Fill.SupportsInteractiveHandles)
			{
				var fillHandle = FillAdorner.HitTest(this, selectedShape, world);

				if (fillHandle != FillHandleKind.None)
				{
					_interaction.MouseDownHit = new HitResult
					{
						Kind = HitKind.FillHandle,
						Shape = selectedShape,
						PagePoint = world
					};

					_interaction.DeferredMoveCandidate = false;
					_interaction.DeferredMoveShape = null;
					_interaction.PendingSelectionCycle = false;
					_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
					_interaction.PendingSelectionCycleStartShape = null;

					StartOperation(new MoveFillHandleOperation(selectedShape, fillHandle), world);
					InvalidateCanvas();
					return;
				}
			}
		}

		_interaction.MouseDownHit = HitTester.HitTest(this, _interaction, world);

		List<Shape>? mouseDownHits = null;
		bool canCycleFromCurrentSelection =
			e.Button == MouseButtons.Left &&
			!_interaction.CtrlPressed &&
			_interaction.Selection.Count == 1 &&
			_interaction.MouseDownHit.Kind == HitKind.ShapeBody;

		if (canCycleFromCurrentSelection)
			mouseDownHits = SelectionCycleHelper.GetHitStack(this, world);

		canCycleFromCurrentSelection =
			mouseDownHits != null &&
			mouseDownHits.Count > 1 &&
			_interaction.Selection[0] != null &&
			mouseDownHits.Any(s => ReferenceEquals(s, _interaction.Selection[0]));

		_interaction.DeferredMoveCandidate = false;
		_interaction.DeferredMoveShape = null;
		_interaction.PendingSelectionCycle = false;
		_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
		_interaction.PendingSelectionCycleStartShape = null;

		if (canCycleFromCurrentSelection)
		{
			_interaction.DeferredMoveCandidate = true;
			_interaction.DeferredMoveShape = _interaction.Selection[0];
			_interaction.PendingSelectionCycle = true;
			_interaction.PendingSelectionCyclePoint = world;
			_interaction.PendingSelectionCycleStartShape = _interaction.Selection[0];
			InvalidateCanvas();
			return;
		}

		// Открываем группу: если за кликом последует перетаскивание, смена
		// выделения и перемещение станут одним шагом. Закрывается в OnMouseUp.
		BeginUndoGroup("Select and move");
		_undoGroupOpen = true;

		WithSelectionUndo(() =>
			_interaction.ToolHandler?.OnMouseDown(this, _interaction, world, e.Button));

		bool isSelectedAfterToolMouseDown =
			!IsCurveEditMode &&
			e.Button == MouseButtons.Left &&
			!_interaction.CtrlPressed &&
			_interaction.MouseDownHit.Kind == HitKind.ShapeBody &&
			_interaction.MouseDownHit.Shape != null &&
			_interaction.Selection.Count == 1 &&
			ReferenceEquals(_interaction.Selection[0], _interaction.MouseDownHit.Shape);

		if (isSelectedAfterToolMouseDown)
		{
			_interaction.DeferredMoveCandidate = true;
			_interaction.DeferredMoveShape = _interaction.MouseDownHit.Shape;
		}
		else
		{
			SelectionCycleHelper.Reset(_interaction);
		}

		InvalidateCanvas();
	}

	/// <summary>
	/// Перехват мыши потерян — взаимодействие прервано не отпусканием
	/// кнопки, а чем-то посторонним: модальным окном, переключением
	/// задач, блокировкой экрана. OnMouseUp в таком случае не придёт,
	/// и закрыть группу истории больше некому.
	/// </summary>
	protected override void OnMouseCaptureChanged(EventArgs e)
	{
		base.OnMouseCaptureChanged(e);

		if (!Capture)
			CloseStaleUndoGroup();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);

		CloseStaleUndoGroup();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		UpdateModifierKeys();

		_cursorScreen = e.Location;
		_cursorOverCanvas = true;

		var world = ScreenToPage(e.Location);
		_interaction.LastPage = _interaction.CurrentPage;
		_interaction.CurrentPage = world;

		bool needHitTest =
			!_panning &&
			!_draggingPageOrigin &&
			_interaction.ActiveOperation == null &&
			!(MainForm.Mode == EditorMode.Image && imageEditTool != null) &&
			e.Location != _lastHitTestPoint;

		if (needHitTest)
		{
			_interaction.HotHit = HitTester.HitTest(this, _interaction, world);
			_lastHitTestPoint = e.Location;
		}

		// Условие своё, а не needHitTest: тот выключается ещё и когда курсор
		// не сдвинулся, и подсветка на этом гасла бы вместо того, чтобы
		// остаться на месте.
		UpdateHoveredNode(
			world,
			!_panning &&
			!_draggingPageOrigin &&
			_interaction.ActiveOperation == null);

		if (MainForm.Mode == EditorMode.Image && imageEditTool != null)
		{
			_interaction.HotHit = HitResult.None;
			imageEditTool.OnMouseMove(this, _interaction, world);
			InvalidateCanvas();
			return;
		}

		if (_draggingPageOrigin)
		{
			if (CurrentPage == null)
				return;

			SKPoint oldOrigin = CurrentPage.page_origin;
			SKPoint newOrigin = ScreenToPageAbsolute(new Point(e.X, e.Y));

			float shiftX = oldOrigin.X - newOrigin.X;
			float shiftY = oldOrigin.Y - newOrigin.Y;

			CurrentPage.page_origin = newOrigin;

			var shapes = CurrentPage.Shapes;
			for (int i = 0; i < shapes.Count; i++)
				shapes[i].MoveByWorld(-shiftX, -shiftY);

			// Поля начала координат ведут за перетаскиванием: иначе они
			// показывали бы прежнее значение до следующей пересборки панели.
			mainForm?.SyncPageZeroEditors();

			InvalidateCanvas();
			return;
		}

		if (_panning)
		{
			float dx = e.X - _panStartMouse.X;
			float dy = e.Y - _panStartMouse.Y;

			zero_pos = new SKPoint(_panStartZero.X + dx, _panStartZero.Y + dy);

			if (CurrentPage != null)
				CurrentPage.view_zero = zero_pos;

			UpdateScrollBars();
			InvalidateCanvas();
			return;
		}

		if (CurrentPage == null)
			return;

		if (_interaction.ActiveOperation == null &&
			_interaction.DeferredMoveCandidate &&
			_interaction.DeferredMoveShape != null &&
			ExceededDragThreshold(_interaction.MouseDownPage, world))
		{
			_interaction.PendingSelectionCycle = false;
			_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
			_interaction.PendingSelectionCycleStartShape = null;
			_interaction.PendingCenterHandleToggle = false;
			_interaction.PendingCenterHandlePoint = SKPoint.Empty;
			_interaction.DeferredMoveCandidate = false;

			StartOperation(new MoveSelectionOperation(), _interaction.MouseDownPage);
		}

		if (_interaction.ActiveOperation == null &&
			_interaction.DeferredGuideCenterMoveCandidate &&
			_interaction.SelectedGuide != null &&
			ExceededDragThreshold(_interaction.MouseDownPage, world))
		{
			_interaction.PendingGuideCenterToggle = false;
			_interaction.PendingGuideCenterTogglePoint = SKPoint.Empty;
			_interaction.DeferredGuideCenterMoveCandidate = false;

			StartOperation(new MoveGuideCenterOperation(), _interaction.MouseDownPage);
		}

		if (_interaction.ActiveOperation == null &&
			_interaction.DeferredGuideMoveCandidate &&
			_interaction.SelectedGuide != null &&
			ExceededDragThreshold(_interaction.MouseDownPage, world))
		{
			_interaction.PendingGuideCenterToggle = false;
			_interaction.PendingGuideCenterTogglePoint = SKPoint.Empty;
			_interaction.DeferredGuideMoveCandidate = false;

			StartOperation(new MoveGuideOperation(), _interaction.MouseDownPage);
		}

		if (_interaction.ActiveOperation != null)
		{
			_interaction.ActiveOperation.Update(this, _interaction, world);
			MainForm.sel.NotifyGeometryChanged();
		}
		else
		{
			_interaction.ToolHandler?.OnMouseMove(this, _interaction, world);
		}

		if (GetRulerCornerRect().Contains(e.Location))
			Cursor = Cursors.SizeAll;

		InvalidateCanvas();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		if (CurrentPage == null)
			return;

		UpdateModifierKeys();

		var world = ScreenToPage(e.Location);

		// Группа закрывается в любом случае, каким бы путём метод ни вышел.
		try
		{
			if (MainForm.Mode == EditorMode.Image && imageEditTool != null)
			{
				imageEditTool.OnMouseUp(this, _interaction, world, e.Button);
				SelectionGeometryChanged?.Invoke();
				InvalidateCanvas();
				return;
			}

			if (e.Button == MouseButtons.Left && suppressNextLeftMouseUp)
			{
				suppressNextLeftMouseUp = false;
				return;
			}

			if (e.Button == MouseButtons.Left && _draggingPageOrigin)
			{
				_draggingPageOrigin = false;
				Cursor = Cursors.Default;

				CurrentPage.doc.need_save = true;
				mainForm?.SyncPageZeroEditors();

				SelectionGeometryChanged?.Invoke();
				UpdateScrollBars();
				InvalidateCanvas();
				return;
			}

			if (e.Button == MouseButtons.Middle)
			{
				_panning = false;
				Cursor = Cursors.Default;
				return;
			}

			// Меню открывается на отпускании, а не на нажатии: правой
			// кнопкой пользуются и инструменты — многоугольник ею
			// завершает контур, — и открытое меню съело бы это движение.
			//
			// Проверка на смещение обязательна: правой кнопкой ещё и
			// панорамируют, и меню, выскочившее в конце протяжки,
			// перекрыло бы то место, куда пользователь только что смотрел.
			// Условие на выделение снято: меню нужно и по пустому месту,
			// потому что вставка в точку курсора — единственный способ
			// положить скопированное туда, куда показал пользователь,
			// а выделения при этом может не быть вовсе. Само меню
			// разбирается, какие пункты в таком случае включать.
			if (e.Button == MouseButtons.Right &&
				MainForm.Mode == EditorMode.Shape &&
				_interaction.ActiveOperation == null &&
				IsClickWithoutDrag(_interaction.MouseDownPage, world))
			{
				mainForm?.ShowShapeContextMenu(e.Location);
				return;
			}

			if (e.Button == MouseButtons.Left &&
				_interaction.PendingCenterHandleToggle &&
				IsClickWithoutDrag(_interaction.MouseDownPage, world))
			{
				_interaction.TransformMode =
					_interaction.TransformMode == SelectionTransformMode.Resize
						? SelectionTransformMode.Rotate
						: SelectionTransformMode.Resize;

				if (_interaction.TransformMode == SelectionTransformMode.Rotate &&
					_interaction.Selection.Count > 0 &&
					!_interaction.HasCustomRotationPivot)
				{
					SelectionHelper.ResetPivotToSelectionCenter(_interaction);
				}

				_interaction.PendingCenterHandleToggle = false;
				_interaction.PendingCenterHandlePoint = SKPoint.Empty;
				_interaction.DeferredMoveCandidate = false;
				_interaction.DeferredMoveShape = null;

				MainForm.sel.NotifyGeometryChanged();
				InvalidateCanvas();
				return;
			}

			if (e.Button == MouseButtons.Left &&
				_interaction.PendingSelectionCycle &&
				_interaction.PendingSelectionCycleStartShape != null &&
				IsClickWithoutDrag(_interaction.MouseDownPage, world))
			{
				var currentSelected = _interaction.PendingSelectionCycleStartShape;

				// Что было выделено до перебора: по нему решается,
				// поднимать ли событие.
				var beforeCycle = _interaction.Selection.ToList();

				WithSelectionUndo(() =>
				{
					var next = SelectionCycleHelper.BeginOrAdvance(this, _interaction, world, currentSelected);

					if (next != null && !ReferenceEquals(next, currentSelected))
						SelectionHelper.SetSingle(_interaction, next);
				});

				_interaction.PendingSelectionCycle = false;
				_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
				_interaction.PendingSelectionCycleStartShape = null;
				_interaction.DeferredMoveCandidate = false;
				_interaction.DeferredMoveShape = null;

				// Перебор перекрывающихся фигур менял ТОЛЬКО список
				// в контексте взаимодействия и никого об этом не извещал.
				// Холст рисуется прямо по этому списку, поэтому новая
				// рамка выделения появлялась сразу, а панель свойств
				// подписана на SelectionManager, который наполняется
				// в обработчике события, — и продолжала показывать
				// прежнюю фигуру.
				//
				// Заметнее всего это было с картинками: перекрытие —
				// их обычное дело, и добраться до фигуры под картинкой
				// или над ней можно только повторным щелчком, то есть
				// ровно через этот перебор.
				NotifySelectionIfChanged(beforeCycle);

				InvalidateCanvas();
				return;
			}

			_interaction.DeferredMoveCandidate = false;
			_interaction.DeferredMoveShape = null;
			_interaction.PendingSelectionCycle = false;
			_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
			_interaction.PendingSelectionCycleStartShape = null;
			_interaction.PendingCenterHandleToggle = false;
			_interaction.PendingCenterHandlePoint = SKPoint.Empty;

			if (e.Button == MouseButtons.Left &&
				_interaction.PendingGuideCenterToggle &&
				_interaction.SelectedGuide != null &&
				IsClickWithoutDrag(_interaction.MouseDownPage, world))
			{
				_interaction.SelectedGuide.TransformMode =
					_interaction.SelectedGuide.TransformMode == GuideTransformMode.Move
						? GuideTransformMode.Rotate
						: GuideTransformMode.Move;

				_interaction.PendingGuideCenterToggle = false;
				_interaction.PendingGuideCenterTogglePoint = SKPoint.Empty;
				_interaction.DeferredGuideMoveCandidate = false;
				_interaction.DeferredGuideCenterMoveCandidate = false;

				SelectionGeometryChanged?.Invoke();
				InvalidateCanvas();
				return;
			}

			if (e.Button == MouseButtons.Left &&
				_interaction.MouseDownHit.Kind == HitKind.GuideBody &&
				_interaction.ActiveOperation == null &&
				IsClickWithoutDrag(_interaction.MouseDownPage, world))
			{
				_interaction.DeferredGuideMoveCandidate = false;
				_interaction.PendingGuideCenterToggle = false;
				_interaction.PendingGuideCenterTogglePoint = SKPoint.Empty;
				_interaction.DeferredGuideCenterMoveCandidate = false;

				InvalidateCanvas();
				return;
			}

			if (_interaction.ActiveOperation != null)
			{
				// Ссылка снимается ДО вызова End.
				//
				// Операции завершают работу переключением инструмента —
				// например создание фигуры возвращает курсор. SetTool
				// отменяет текущую операцию, а ею на этот момент была
				// та самая, которая ещё не вышла из End: она получала
				// Cancel сразу после собственного завершения и убирала
				// со страницы только что созданную фигуру, причём тихо,
				// мимо события удаления. Шаг создания при этом в истории
				// оставался и указывал на фигуру, которой уже нет.
				var operation = _interaction.ActiveOperation;

				_interaction.ActiveOperation = null;
				_interaction.ActiveSnap = null;

				operation.End(this, _interaction, world);

				// Шаг истории для операции перетаскивания.
				EndOperationUndo();

				SelectionGeometryChanged?.Invoke();
			}
			else
			{
				_interaction.ToolHandler?.OnMouseUp(this, _interaction, world, e.Button);
			}

			if (e.Button == MouseButtons.Left && !IsClickWithoutDrag(_interaction.MouseDownPage, world))
				SelectionCycleHelper.Reset(_interaction);

			// Выход из режима узлов по щелчку мимо кривой перенесён
			// в OnMouseClick: см. комментарий там.

			InvalidateCanvas();
		}
		finally
		{
			if (_undoGroupOpen)
			{
				CommitUndoGroup();
				_undoGroupOpen = false;
			}
		}
	}

	private bool IsSecondClickOfDoubleClick(MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
			return false;

		int dt = SystemInformation.DoubleClickTime;
		Size ds = SystemInformation.DoubleClickSize;
		long now = Environment.TickCount64;

		bool near =
			Math.Abs(e.X - lastLeftMouseDownPoint.X) <= ds.Width / 2 &&
			Math.Abs(e.Y - lastLeftMouseDownPoint.Y) <= ds.Height / 2;

		bool inTime = now - lastLeftMouseDownTicks <= dt;

		return inTime && near;
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);

		suppressNextLeftMouseUp = false;
		ResetPendingInteractionState();

		if (CurrentPage == null || e.Button != MouseButtons.Left)
			return;

		var world = ScreenToPage(e.Location);
		var hit = HitTester.HitTest(this, _interaction, world);

		if (hit.Kind == HitKind.ShapeBody && hit.Shape is ImageShape)
		{
			var before = _interaction.Selection.ToList();

			SelectionHelper.SetSingle(_interaction, hit.Shape);
			NotifySelectionIfChanged(before);
			SetMode(EditorMode.Curve);
			InvalidateCanvas();
			return;
		}

		if (_interaction.Selection.Count == 1 && _interaction.Selection[0] is CurveShape curve)
		{
			if (MainForm.Mode != EditorMode.Curve)
			{
				SetMode(EditorMode.Curve);
				InvalidateCanvas();
				return;
			}

			int segIndex = CurveAdorner.HitTestSegment(this, curve, world);
			if (segIndex >= 0)
			{
				StartOperation(new InsertCurvePointOperation(curve, segIndex), world);
				_interaction.ActiveOperation = null;
				SelectionGeometryChanged?.Invoke();
				InvalidateCanvas();
			}
		}
	}

	/// <summary>
	/// Применяет мировое преобразование ко всему выделению одним шагом истории.
	///
	/// Шаг пишется здесь, а не в вызывающих: отражение и поворот на 90°
	/// идут мимо DragOperation, то есть мимо OperationUndoRecorder, и без этого
	/// оставались бы вне отмены вовсе.
	/// </summary>
	private void ApplyTransformToSelection(SKMatrix worldTransform, string historyName)
	{
		if (CurrentPage == null || _interaction.Selection.Count == 0)
			return;

		var action = TransformShapesAction.Capture(historyName, _interaction.Selection);

		foreach (var shape in _interaction.Selection)
			shape.ApplyWorldTransform(worldTransform);

		action.CaptureAfter();

		if (action.HasChanges())
			Undo?.Push(action);
		else
			action.Discard();

		CurrentPage.doc.need_save = true;

		if (!_interaction.HasCustomRotationPivot)
			SelectionHelper.ResetPivotToSelectionCenter(_interaction);

		MainForm.sel.NotifyGeometryChanged();
		InvalidateCanvas();
	}

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData == Keys.Escape)
			return true;

		return base.IsInputKey(keyData);
	}

	private void ResetPendingInteractionState()
	{
		_interaction.DeferredMoveCandidate = false;
		_interaction.DeferredMoveShape = null;
		_interaction.PendingSelectionCycle = false;
		_interaction.PendingSelectionCyclePoint = SKPoint.Empty;
		_interaction.PendingSelectionCycleStartShape = null;
		_interaction.PendingCenterHandleToggle = false;
		_interaction.PendingCenterHandlePoint = SKPoint.Empty;
		_interaction.PendingGuideCenterToggle = false;
		_interaction.PendingGuideCenterTogglePoint = SKPoint.Empty;
		_interaction.DeferredGuideMoveCandidate = false;
		_interaction.DeferredGuideCenterMoveCandidate = false;
		_interaction.ActiveSnap = null;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		if (CurrentPage == null)
			return;

		UpdateModifierKeys();

		switch (e.KeyCode)
		{
			case Keys.Z:
			case Keys.Y:
				if (TryHandleUndoShortcut(e.KeyCode))
				{
					e.Handled = true;
					return;
				}
				break;

			case Keys.Escape:
				if (MainForm.Mode == EditorMode.Image && imageEditTool != null)
				{
					// Сначала даём активному инструменту записать floating selection обратно.
					imageEditTool.OnKeyDown(this, _interaction, e.KeyCode);
					ExitImageEditTool();

					e.Handled = true;
					return;
				}

				if (MainForm.Mode == EditorMode.Curve)
				{
					SetMode(EditorMode.Shape);
					SetTool(ShapeToolKind.Select);
					InvalidateCanvas();
					e.Handled = true;
					return;
				}

				if (_interaction.ActiveOperation != null)
				{
					_interaction.ActiveOperation.Cancel(this, _interaction);
					_interaction.ActiveOperation = null;
					_interaction.ActiveSnap = null;

					CancelOperationUndo();
					ResetPendingInteractionState();

					SelectionGeometryChanged?.Invoke();
					InvalidateCanvas();
					e.Handled = true;
					return;
				}

				var toolBefore = _interaction.CurrentTool;

				_interaction.ToolHandler?.OnKeyDown(this, _interaction, e.KeyCode);

				if (_interaction.CurrentTool == toolBefore &&
					_interaction.CurrentTool != ShapeToolKind.Select)
				{
					SetTool(ShapeToolKind.Select);
				}

				InvalidateCanvas();
				e.Handled = true;
				return;

			case Keys.Delete:
				if (MainForm.Mode == EditorMode.Image && imageEditTool != null)
				{
					imageEditTool.OnKeyDown(this, _interaction, e.KeyCode);
					InvalidateCanvas();
					e.Handled = true;
					return;
				}

				if (DeleteSelectedGuide())
				{
					e.Handled = true;
					return;
				}

				mainForm.DeleteSelectedShapes();
				return;

			case Keys.C:
				if (ModifierKeys == Keys.Control && MainForm.Mode == EditorMode.Image && TryCopyImageSelection())
					return;

				mainForm.CopySelectedShapes();
				return;

			case Keys.V:
				if (ModifierKeys == Keys.Control && MainForm.Mode == EditorMode.Image && TryPasteImageSelection())
					return;

				mainForm.PasteCopiedShapes();
				return;
		}

		_interaction.ToolHandler?.OnKeyDown(this, _interaction, e.KeyCode);
	}

	/// <summary>
	/// Удаляет выделенную направляющую.
	///
	/// Через Page.RemoveShape, а не прямым вычёркиванием из списка:
	/// удаление поднимает ShapeRemoving, и запись в историю делает
	/// PageUndoRecorder — тем же путём, каким записывается создание.
	///
	/// Возвращает признак того, что удалять было что: по нему вызывающий
	/// решает, отдавать ли нажатие дальше.
	/// </summary>
	internal bool DeleteSelectedGuide()
	{
		var guide = _interaction.SelectedGuide;

		if (guide == null || CurrentPage == null)
			return false;

		BeginUndoGroup("Delete guide");

		try
		{
			// Снятие выделения входит в тот же шаг истории: после отмены
			// направляющая должна вернуться выделенной, как была.
			WithSelectionUndo(() =>
			{
				guide.IsSelected = false;
				_interaction.SelectedGuide = null;
			});

			CurrentPage.RemoveShape(guide);
		}
		finally
		{
			CommitUndoGroup();
		}

		RaiseSelectionChanged();
		InvalidateCanvas();

		return true;
	}

	private bool ExceededDragThreshold(SKPoint a, SKPoint b, float thresholdPx = 1f)
	{
		float thresholdUom = ScreenPixelsToPageMm(thresholdPx) / MathF.Max(CurrentPage?.MmPerUom ?? 1f, 0.0001f);

		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return dx * dx + dy * dy > thresholdUom * thresholdUom;
	}

	private bool IsClickWithoutDrag(SKPoint a, SKPoint b, float tolerancePx = 1f)
	{
		float toleranceUom = ScreenPixelsToPageMm(tolerancePx) / MathF.Max(CurrentPage?.MmPerUom ?? 1f, 0.0001f);

		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return dx * dx + dy * dy <= toleranceUom * toleranceUom;
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);

		if (CurrentPage == null)
			return;

		SKPoint pageBefore = ScreenToPage(e.Location);

		float factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
		scale = Math.Clamp(scale * factor, 0.05f, 32f);

		float f = ViewPxPerUom(CurrentPage);

		zero_pos = new SKPoint(
			e.X - (pageBefore.X - CurrentPage.page_origin.X) * f,
			e.Y - (pageBefore.Y - CurrentPage.page_origin.Y) * f);

		CurrentPage.scale = scale;
		CurrentPage.view_zero = zero_pos;

		// Масштаб изменился — прежний hit-test больше не актуален.
		_lastHitTestPoint = new Point(int.MinValue, int.MinValue);

		UpdateScrollBars();
		InvalidateCanvas();
		ScaleChanged?.Invoke();
	}

	/// <summary>
	/// Выход из режима узлов по щелчку мимо редактируемой кривой.
	///
	/// Именно OnMouseClick, а не OnMouseUp и тем более не OnMouseDown:
	/// щелчок — это законченный жест, и до его завершения неизвестно,
	/// щёлкнул пользователь или начал протяжку. Смена режима по нажатию
	/// обрывала бы начатое действие — например рамку выделения узлов,
	/// которая как раз начинается на пустом месте.
	///
	/// Проверка на протяжку всё равно нужна: Click в WinForms приходит
	/// и после перемещения мыши с зажатой кнопкой, лишь бы нажатие
	/// и отпускание случились на одном контроле.
	/// </summary>
	protected override void OnMouseClick(MouseEventArgs e)
	{
		base.OnMouseClick(e);

		if (e.Button != MouseButtons.Left || MainForm.Mode != EditorMode.Curve)
			return;

		if (CurrentPage == null)
			return;

		var world = ScreenToPage(e.Location);

		if (!IsClickWithoutDrag(_interaction.MouseDownPage, world))
			return;

		// Щелчок по самой редактируемой кривой режим не меняет.
		bool clickedSameCurve =
			_interaction.MouseDownHit.Kind == HitKind.ShapeBody &&
			_interaction.Selection.Count == 1 &&
			ReferenceEquals(_interaction.MouseDownHit.Shape, _interaction.Selection[0]);

		// Как и щелчок по её узлам, рычагам или сегментам.
		bool clickedCurveEditablePart =
			_interaction.MouseDownHit.Kind == HitKind.CurveNode ||
			_interaction.MouseDownHit.Kind == HitKind.CurveControlPoint ||
			_interaction.MouseDownHit.Kind == HitKind.CurveSegment;

		if (clickedSameCurve || clickedCurveEditablePart)
			return;

		SetMode(EditorMode.Shape);
		SetTool(ShapeToolKind.Select);
		InvalidateCanvas();
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);

		// Курсор ушёл с холста — полоски на линейках гасим, иначе они
		// застыли бы там, где мышь пересекла край.
		_cursorOverCanvas = false;

		_interaction.HoveredNodeShape = null;
		_interaction.HoveredNodeWorld = null;

		InvalidateCanvas();
	}

	private void UpdateModifierKeys()
	{
		_interaction.CtrlPressed = (ModifierKeys & Keys.Control) == Keys.Control;
		_interaction.AltPressed = (ModifierKeys & Keys.Alt) == Keys.Alt;
		_interaction.ShiftPressed = (ModifierKeys & Keys.Shift) == Keys.Shift;
	}

	/// <summary>
	/// Перерисовывает только область холста, не трогая полосы прокрутки.
	///
	/// Invalidate без аргументов помечает всю клиентскую область, включая место
	/// под дочерними полосами. При UserPaint контрол закрашивает его сам, а
	/// система перерисовывает полосы отдельно и реже — отсюда мигание и
	/// пропадание при непрерывном движении мыши.
	/// </summary>
	public void InvalidateCanvas()
	{
		if (!IsHandleCreated)
			return;

		var viewport = GetCanvasViewport();

		if (viewport.Width <= 0 || viewport.Height <= 0)
		{
			Invalidate();
			return;
		}

		Invalidate(viewport, false);
	}

	#endregion

	#region Shapes methods

	public void FlipSelectionHorizontal()
	{
		if (CurrentPage == null || _interaction.Selection.Count == 0)
			return;

		var bounds = SelectionHelper.GetBounds(_interaction);
		if (bounds.IsEmpty)
			return;

		var pivot = new SKPoint(bounds.MidX, bounds.MidY);

		var transform =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(pivot.X, pivot.Y),
				Geometry.Multiply(
					SKMatrix.CreateScale(-1f, 1f),
					SKMatrix.CreateTranslation(-pivot.X, -pivot.Y)));

		ApplyTransformToSelection(transform, "Flip horizontally");
	}

	public void FlipSelectionVertical()
	{
		if (CurrentPage == null || _interaction.Selection.Count == 0)
			return;

		var bounds = SelectionHelper.GetBounds(_interaction);
		if (bounds.IsEmpty)
			return;

		var pivot = new SKPoint(bounds.MidX, bounds.MidY);

		var transform =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(pivot.X, pivot.Y),
				Geometry.Multiply(
					SKMatrix.CreateScale(1f, -1f),
					SKMatrix.CreateTranslation(-pivot.X, -pivot.Y)));

		ApplyTransformToSelection(transform, "Flip vertically");
	}

	/// <summary>
	/// Поворачивает выделение на прямой угол.
	///
	/// Центр вращения — тот же, что и у поворота мышью: если пользователь
	/// перетащил крестик в сторону, кнопка обязана его уважать — иначе два
	/// способа повернуть одно и то же давали бы разный результат.
	///
	/// Отражение центр не учитывает и идёт от середины габаритов:
	/// там это было так до меня, и менять его заодно я не стал.
	/// </summary>
	public void RotateSelection90(bool clockwise)
	{
		if (CurrentPage == null || _interaction.Selection.Count == 0)
			return;

		var bounds = SelectionHelper.GetBounds(_interaction);
		if (bounds.IsEmpty)
			return;

		var pivot = _interaction.HasCustomRotationPivot
			? _interaction.RotationPivot
			: new SKPoint(bounds.MidX, bounds.MidY);

		var transform = SKMatrix.CreateRotationDegrees(clockwise ? 90f : -90f, pivot.X, pivot.Y);

		ApplyTransformToSelection(transform, clockwise ? "Rotate 90 CW" : "Rotate 90 CCW");
	}

	/// <summary>
	/// Прямоугольник верхней линейки: от угла до правого края.
	///
	/// Толщина полосы прокрутки здесь НЕ вычитается. Полосы вынесены
	/// из вида в сетку MainForm и в клиентскую область не попадают, то есть
	/// ClientSize их уже не включает. Вычитание было остатком от прежней
	/// компоновки и укорачивало линейку ровно на толщину полосы.
	/// </summary>
	private Rectangle GetTopRulerRect()
	{
		return new Rectangle(
			Settings.RulerThickness,
			0,
			Math.Max(0, ClientSize.Width - Settings.RulerThickness),
			Settings.RulerThickness);
	}

	/// <summary>Прямоугольник левой линейки: от угла до нижнего края.</summary>
	private Rectangle GetLeftRulerRect()
	{
		return new Rectangle(
			0,
			Settings.RulerThickness,
			Settings.RulerThickness,
			Math.Max(0, ClientSize.Height - Settings.RulerThickness));
	}

	private Rectangle GetRulerCornerRect()
	{
		return new Rectangle(0, 0, Settings.RulerThickness, Settings.RulerThickness);
	}

	internal SKRect GetViewportScreenRect()
	{
		var r = GetCanvasViewport();
		return new SKRect(r.Left, r.Top, r.Right, r.Bottom);
	}

	public void ExitImageEditTool()
	{
		imageEditTool = null;

		if (ImageEditSession != null)
			ImageEditSession.ActiveKind = ImageEditToolKind.None;

		Cursor = Cursors.Default;

		// В Shape, а не в Curve: выход из режима изображения не должен
		// включать редактирование узлов — оно имеет свои входы.
		mainForm.SetEditorMode(EditorMode.Shape);
		InvalidateCanvas();
	}

	private bool TryCopyImageSelection()
	{
		var imageSession = ImageEditSession;
		if (imageSession == null)
			return false;

		var ctx = _interaction;
		if (ctx.Selection.Count != 1 || ctx.Selection[0] is not ImageShape image)
			return false;

		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return false;

		if (imageSession.FloatingSelection != null)
		{
			imageSession.SetClipboardBitmap(imageSession.FloatingSelection.Bitmap.Copy());
			return true;
		}

		if (!imageSession.Selection.HasSelection)
			return false;

		var bitmap = ImageSelectionClipboardHelper.ExtractSelectionBitmap(
			image,
			imageSession.Selection);

		if (bitmap == null)
			return false;

		imageSession.SetClipboardBitmap(bitmap);
		return true;
	}

	private bool TryPasteImageSelection()
	{
		var imageSession = ImageEditSession;
		if (imageSession?.ClipboardBitmap == null)
			return false;

		var ctx = _interaction;
		if (ctx.Selection.Count != 1 || ctx.Selection[0] is not ImageShape image)
			return false;

		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return false;

		image.EnsureTileDocument();
		if (image.tileDoc == null)
			return false;

		ImageSelectionClipboardHelper.CommitFloatingIfAny(image, imageSession);

		var clip = imageSession.ClipboardBitmap.Copy();

		var targetPos = ImageSelectionClipboardHelper.GetPasteStartPosition(
			image,
			imageSession,
			clip.Width,
			clip.Height);

		imageSession.SetFloatingSelection(
			new FloatingPixelSelection(clip, targetPos));

		imageSession.Selection.SetRect(
			new SKRectI(
				targetPos.X,
				targetPos.Y,
				targetPos.X + clip.Width,
				targetPos.Y + clip.Height),
			image.Bitmap.Width,
			image.Bitmap.Height);

		SetImageEditTool(ImageEditToolKind.Cursor);
		InvalidateCanvas();
		return true;
	}

	#endregion

	#region Освобождение ресурсов

	protected override void Dispose(bool disposing)
	{
		if (disposing && !_disposed)
		{
			_disposed = true;

			PaintSurface -= MainView_PaintSurface;

			_hScroll.Scroll -= HScroll_Scroll;
			_vScroll.Scroll -= VScroll_Scroll;

			_autoScrollTimer.Stop();
			_autoScrollTimer.Tick -= AutoScrollTick;
			_autoScrollTimer.Dispose();

			imageEditTool = null;

			_drawBuffer.Clear();
			_guideBuffer.Clear();

			// Статические кисти живут до конца процесса и здесь не уничтожаются:
			// их может использовать другой экземпляр MainView.
		}

		base.Dispose(disposing);
	}

	#endregion
}

/// <summary>
/// Интеграция истории действий в холст.
///
/// Выделена в отдельный файл, чтобы не смешивать её с рендером и обработкой
/// ввода. Для подключения в MainView.cs нужно:
///   1. объявить класс как  public sealed partial class MainView : SKGLControl
///   2. внести пять однострочных правок, перечисленных в UndoWiring.md
/// </summary>
public sealed partial class MainView
{
	/// <summary>История текущего документа. null, если страница не открыта.</summary>
	internal UndoStack? Undo => CurrentPage?.doc?.Undo;

	/// <summary>Диспетчер истории для операций перетаскивания.</summary>
	internal OperationUndoRecorder? OperationRecorder => CurrentPage?.doc?.OperationRecorder;

	#region Операции перетаскивания

	/// <summary>Снять состояние до начала операции. Вызывается из StartOperation.</summary>
	internal void BeginOperationUndo(DragOperation op)
		=> OperationRecorder?.BeginOperation(op, _interaction);

	/// <summary>Записать шаг после завершения операции.</summary>
	internal void EndOperationUndo()
		=> OperationRecorder?.EndOperation();

	/// <summary>Отбросить снимок: операция отменена клавишей Escape.</summary>
	internal void CancelOperationUndo()
		=> OperationRecorder?.CancelOperation();

	#endregion

	#region Выделение фигур

	/// <summary>
	/// Выполняет изменение выделения и записывает его как один шаг истории.
	///
	/// Оборачивать нужно вызывающий код, а не сам SelectionHelper: один клик
	/// мыши может пройти через Clear и SetSingle подряд, и без обёртки это дало
	/// бы два шага вместо одного.
	/// </summary>
	internal void WithSelectionUndo(Action change)
	{
		var undo = Undo;

		if (undo == null || undo.IsApplying)
		{
			change();
			return;
		}

		var action = SelectionChangeAction.Capture(_interaction, RaiseSelectionChanged);

		change();

		action.CaptureAfter();

		if (action.HasChanges())
			undo.Push(action);
	}

	/// <summary>
	/// Группа: смена выделения и последующее перетаскивание становятся одним
	/// шагом. Используется, когда клик по невыделенной фигуре сразу переходит
	/// в её перемещение.
	/// </summary>
	/// <summary>
	/// Закрывает группу истории, оставшуюся открытой от прерванного
	/// взаимодействия.
	///
	/// Закрывает, а не отменяет: накопленные действия уже применены
	/// к документу, и выбросить их значило бы оставить историю без шага
	/// для того, что пользователь на самом деле сделал.
	/// </summary>
	internal void CloseStaleUndoGroup()
	{
		if (!_undoGroupOpen && Undo?.IsGroupOpen != true)
			return;

		Undo?.CloseAllGroups();
		_undoGroupOpen = false;
	}

	internal void BeginUndoGroup(string name) => Undo?.BeginGroup(name);

	internal void CommitUndoGroup() => Undo?.CommitGroup();

	internal void AbortUndoGroup() => Undo?.AbortGroup();

	#endregion

	#region Отмена и повтор

	internal bool PerformUndo()
	{
		var undo = Undo;
		if (undo == null || !undo.CanUndo)
			return false;

		// Незавершённая операция перетаскивания сделала бы откат
		// непредсказуемым — сначала снимаем её.
		AbortActiveOperation();

		if (!undo.Undo())
			return false;

		AfterHistoryChange();
		return true;
	}

	internal bool PerformRedo()
	{
		var undo = Undo;
		if (undo == null || !undo.CanRedo)
			return false;

		AbortActiveOperation();

		if (!undo.Redo())
			return false;

		AfterHistoryChange();
		return true;
	}

	private void AbortActiveOperation()
	{
		if (_interaction.ActiveOperation == null)
			return;

		_interaction.ActiveOperation.Cancel(this, _interaction);
		_interaction.ActiveOperation = null;
		_interaction.ActiveSnap = null;

		CancelOperationUndo();
		ResetPendingInteractionState();
	}

	/// <summary>
	/// Приведение состояния в согласованный вид после отката или повтора.
	/// </summary>
	private void AfterHistoryChange()
	{
		// Выделение могло указывать на фигуру, которой больше нет на странице.
		PruneSelection();

		// Кэш hit-test устарел.
		_lastHitTestPoint = new System.Drawing.Point(int.MinValue, int.MinValue);

		if (CurrentPage != null)
			CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		RaiseSelectionChanged();
		SelectionGeometryChanged?.Invoke();

		InvalidateCanvas();
	}

	/// <summary>
	/// Убирает из выделения фигуры, отсутствующие на текущей странице.
	/// Без этого адорнеры продолжали бы рисовать рамку вокруг удалённой фигуры.
	/// </summary>
	private void PruneSelection()
	{
		if (CurrentPage == null)
		{
			_interaction.Selection.Clear();
			_interaction.SelectedGuide = null;
			return;
		}

		for (int i = _interaction.Selection.Count - 1; i >= 0; i--)
		{
			var shape = _interaction.Selection[i];

			if (!CurrentPage.Shapes.Contains(shape))
			{
				shape.IsSelected = false;
				_interaction.Selection.RemoveAt(i);
			}
		}

		if (_interaction.SelectedGuide != null &&
			!CurrentPage.Shapes.Contains(_interaction.SelectedGuide))
		{
			_interaction.SelectedGuide.IsSelected = false;
			_interaction.SelectedGuide = null;
		}

		if (_interaction.DeferredMoveShape != null &&
			!CurrentPage.Shapes.Contains(_interaction.DeferredMoveShape))
		{
			_interaction.DeferredMoveShape = null;
			_interaction.DeferredMoveCandidate = false;
		}

		if (_interaction.PendingSelectionCycleStartShape != null &&
			!CurrentPage.Shapes.Contains(_interaction.PendingSelectionCycleStartShape))
		{
			_interaction.PendingSelectionCycleStartShape = null;
			_interaction.PendingSelectionCycle = false;
		}
	}

	/// <summary>
	/// Обработка Ctrl+Z и Ctrl+Y. Вызывается из OnKeyDown.
	/// Возвращает true, если клавиша обработана.
	/// </summary>
	internal bool TryHandleUndoShortcut(Keys keyCode)
	{
		if ((ModifierKeys & Keys.Control) != Keys.Control)
			return false;

		// Незакоммиченный мазок сбрасывается перед откатом.
		//
		// Прежде здесь стоял выход с false: отмену «отдавали растровому
		// инструменту», но ни один из них Ctrl+Z не обрабатывает, и в
		// режиме изображения отмена не работала вовсе — ни для мазков,
		// ни для выделения, ни для фигур.
		//
		// Бросить надо только начатый ввод: ActiveStroke держит копию
		// затронутых тайлов и при следующем движении мыши записал бы её
		// поверх состояния, которое откат восстановил. Выделение и сам
		// инструмент остаются на месте — пользователь отменяет шаг,
		// а не выходит из рисования.
		if (MainForm.Mode == EditorMode.Image && imageEditTool is ImagePaintToolBase paintTool)
			paintTool.CancelActiveInput();

		switch (keyCode)
		{
			case Keys.Z:
				if ((ModifierKeys & Keys.Shift) == Keys.Shift)
					PerformRedo();
				else
					PerformUndo();
				return true;

			case Keys.Y:
				PerformRedo();
				return true;

			default:
				return false;
		}
	}

	#endregion

	#region Направляющие

	/// <summary>
	/// Создание направляющей через страницу, а не прямой вставкой в Page.Shapes.
	/// Прежний CreateGuide писал напрямую в список, поэтому событие ShapeAdded
	/// не поднималось и создание направляющей в историю не попадало.
	/// </summary>
	internal GuideLine CreateGuideUndoable(GuideOrientation orientation, SKPoint pagePoint)
	{
		if (CurrentPage == null)
			throw new InvalidOperationException("No current page.");

		var guide = new GuideLine
		{
			Name = orientation == GuideOrientation.Vertical ? "Vertical Guide" : "Horizontal Guide",
			Orientation = orientation,
			GuidePoint = pagePoint,
			AngleDeg = 0f
		};

		CurrentPage.AddShape(guide);
		return guide;
	}

	#endregion
}