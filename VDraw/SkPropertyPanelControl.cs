using SkiaSharp;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public sealed class SkPropertyPanelControl : Control
{
	#region Поля и события

	private readonly SkFlowLayout _root = new();

	private SkControl? _focused;
	private SkControl? _captured;
	private Shape? _boundShape;

	/// <summary>
	/// Идёт ли ввод в одном из полей панели.
	///
	/// Нужно обработчику сочетаний клавиш в MainForm. Панель рисует
	/// свои контролы сама, поэтому WinForms о её текстовых полях не знает
	/// и ActiveControl указывает на панель целиком. Без этой проверки буква,
	/// набранная в поле имени шрифта, переключала бы инструмент.
	///
	/// SkNumTextBox наследуется от SkTextBox и покрывается первой проверкой.
	/// SkDropDownBox сюда не входит: текст в нём не набирают, а клавиши
	/// он использует только для перебора элементов.
	/// </summary>
	public bool IsTextEditing => _focused is SkTextBox || _focused is SkNumericUpDown;

	private float _scrollY = 0f;

	/// <summary>Ширина полосы разделителя в пикселях.</summary>
	private const float ResizeGrip = 6f;

	/// <summary>Длина насечки посередине разделителя.</summary>
	private const float GripMarkHeight = 36f;

	private bool _resizing = false;

	/// <summary>Курсор над полосой разделителя.</summary>
	private bool _gripHovered;

	private int _resizeStartWidth;
	private int _resizeStartScreenX;

	public float PanelWidth { get; private set; } = 300f;
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public float MinPanelWidth { get; set; } = 240f;
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public float MaxPanelWidth { get; set; } = 560f;

	public event Action? RequestCanvasRefresh;
	public event Action<float>? PanelWidthChanged;
	public event Action<ShapePickTarget>? ShapePickRequested;

	private SKBitmap? _backBuffer;
	private Bitmap? _gdiBuffer;
	private byte[]? _pixelBuffer;

	private SkNumericUpDown? _xEditor;
	private SkNumericUpDown? _yEditor;
	private SkNumericUpDown? _wEditor;
	private SkNumericUpDown? _hEditor;
	private SkNumericUpDown? _rotEditor;
	private SkNumericUpDown? _layerEditor;

	private bool _updatingFromShape;

	/// <summary>
	/// Обновление полей с параметрами конкретной фигуры из модели.
	///
	/// Список, а не фиксированные поля: набор параметров зависит от типа
	/// фигуры и меняется при каждой смене выделения. Заполняется при сборке
	/// панели, очищается перед ней.
	/// </summary>
	private readonly List<Action> _shapeParamRefreshers = new();

	private SkTextBox? _nameEditor;

	/// <summary>
	/// Свёрнутость панелей в текущем сеансе.
	///
	/// Статический и общий на все фигуры: пользователь сворачивает раздел
	/// не у конкретного объекта, а насовсем — «заливка мне сейчас не нужна».
	/// Хранить состояние у фигуры значило бы каждый раз при выделении
	/// соседнего объекта возвращать раскладку к исходной.
	///
	/// Ключ — необработанное английское название раздела, а не подпись
	/// на экране: перевод меняется вместе с языком, и раскладка сбрасывалась
	/// бы при его переключении. В документ не сохраняется намеренно: это
	/// состояние рабочего места, а не свойство чертежа.
	/// </summary>
	private static readonly Dictionary<string, bool> _panelExpanded = new(StringComparer.Ordinal);

	private SkPatternGridEditor? _strokePatternEditor;
	private SkDropDownBox? _strokeTypeDropDown;
	private SkNumericUpDown? _strokePatternLenEditor;
	/// <summary>
	/// Идёт программное заполнение редактора паттерна. Пока true, обратная
	/// запись из редактора в модель подавляется: при заполнении редактор
	/// проходит через промежуточное пустое состояние, и запись из него
	/// затирала бы правильный паттерн.
	/// </summary>
	private bool _suppressStrokePatternWriteback;

	#endregion

	#region Создание и жизненный цикл

	public SkPropertyPanelControl()
	{
		SetStyle(ControlStyles.AllPaintingInWmPaint |
				 ControlStyles.UserPaint |
				 ControlStyles.OptimizedDoubleBuffer |
				 ControlStyles.ResizeRedraw |
				 ControlStyles.Selectable, true);

		Dock = DockStyle.Right;
		Width = (int)PanelWidth;
		TabStop = true;
		BackColor = ToColor(SkUiTheme.Current.PanelBack);

		_root.Direction = SkFlowDirection.Vertical;
		_root.Gap = 10f;
		_root.Padding = 10f;
		_root.AutoHeight = true;
		_root.Bounds = new SKRect(8, 8, 280, 0);

		MainForm.sel.SelectionChanged += OnSelectionChanged;
		MainForm.sel.SelectionGeometryChanged += OnSelectionGeometryChanged;
		SkUiTheme.ThemeChanged += OnThemeChanged;

		// Смена единиц меняет ЧИСЛА почти в каждой строке панели —
		// координаты, размеры, толщину линии, отступы. Пересчёт по месту
		// пришлось бы дописывать в каждый редактор и не забывать при
		// добавлении новых, поэтому панель просто пересобирается целиком.
		Page.UnitsChanged += OnUnitsChanged;
	}

	private void EnsureBuffers()
	{
		if (Width <= 0 || Height <= 0)
			return;

		bool needRecreate =
			_backBuffer == null ||
			_gdiBuffer == null ||
			_backBuffer.Width != Width ||
			_backBuffer.Height != Height ||
			_gdiBuffer.Width != Width ||
			_gdiBuffer.Height != Height;

		if (!needRecreate)
			return;

		_gdiBuffer?.Dispose();
		_backBuffer?.Dispose();

		_backBuffer = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		_gdiBuffer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
		_pixelBuffer = null;
	}

	protected override void OnCreateControl()
	{
		base.OnCreateControl();
		RebuildForSelection();
	}

	private void OnSelectionChanged()
	{
		if (!IsHandleCreated)
			return;

		RebuildForSelection();
	}

	private void OnThemeChanged()
	{
		BackColor = ToColor(SkUiTheme.Current.PanelBack);

		if (!IsHandleCreated)
			return;

		RebuildForSelection();
		Invalidate();
	}

	private static Color ToColor(SKColor c)
	{
		return Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
	}

	private static void EnsureShapeInitialized(Shape? shape)
	{
		if (shape == null)
			return;

		shape.Stroke ??= new StrokeStyle();
		shape.Fill ??= new FillStyle();
		shape.Shadow ??= new ShadowEffect();
		shape.Transparency ??= new TransparencyEffect();
	}

	public void RebuildForSelection()
	{
		_xEditor = null;
		_yEditor = null;
		_wEditor = null;
		_hEditor = null;
		_rotEditor = null;
		_layerEditor = null;
		_nameEditor = null;
		_shapeParamRefreshers.Clear();

		_boundShape = MainForm.sel.Primary;
		EnsureShapeInitialized(_boundShape);

		_root.ClearChildren();

		if (_boundShape == null)
		{
			var p = CreatePanel("General");
			p.Content.Add(new SkLabel
			{
				Text = "No object selected",
				Bounds = new SKRect(0, 0, 250, 20)
			});
			_root.Add(p);
		}
		// Группа — только General.
		//
		// Собственных обводки и заливки у неё нет: рисуются дети, каждый
		// своими стилями. Поля Stroke и Fill у GroupShape существуют лишь потому,
		// что объявлены в базовом Shape, и ни на что не влияют. Включённые
		// контролы, которые ничего не меняют, читаются как неработающая функция.
		else if (_boundShape is GroupShape)
		{
			_root.Add(BuildGeneralPanel(_boundShape));

			// Тень у группы своя, в отличие от обводки и заливки: силуэт
			// строится по всему содержимому разом, и это осмысленный
			// самостоятельный эффект, а не бесполезное дублирование
			// свойств детей.
			_root.Add(BuildShadowPanel(_boundShape));

			// Прозрачность у группы применяется ко всему содержимому разом —
			// ради этого случая эффект и живёт на уровне Shape, а не стиля.
			_root.Add(BuildTransparencyPanel(_boundShape));
		}
		else
		{
			// Порядок панелей: General, затем панель типа фигуры (Text/Image),
			// затем Stroke и Fill. Специфичная панель стоит выше общих стилей,
			// потому что при работе с текстом и изображением обращаются прежде
			// всего к ней.
			_root.Add(BuildGeneralPanel(_boundShape));

			// Инструмент преобразования: собственных обводки и заливки
			// у него нет, каждый шаг берёт промежуточные между стилями
			// привязанных фигур. Показывать Stroke и Fill значило бы
			// обещать управление, которого нет.
			if (_boundShape is TransformShape transformTool)
			{
				_root.Add(BuildTransformPanel(transformTool));
				_root.Add(BuildShadowPanel(_boundShape));
				_root.Add(BuildTransparencyPanel(_boundShape));

				ArrangeUi();

				if (IsHandleCreated)
					Invalidate();

				return;
			}

			if (_boundShape is AngleMeasurementShape angleMeasurement)
				_root.Add(BuildAngleMeasurementTextPanel(angleMeasurement));
			else if (_boundShape is MeasurementShape measurement)
				_root.Add(BuildMeasurementTextPanel(measurement));
			else if (_boundShape is TextShape textShape)
				_root.Add(BuildTextPanel(textShape));
			else if (_boundShape is ImageShape imageShape)
				_root.Add(BuildImagePanel(imageShape));

			_root.Add(BuildStrokePanel(_boundShape));
			_root.Add(BuildFillPanel(_boundShape));
			_root.Add(BuildShadowPanel(_boundShape));
			_root.Add(BuildTransparencyPanel(_boundShape));

		}

		// У размера привязываются измеряемые точки, поэтому строки названы
		// P1 и P2: «First node» и «Last node» здесь ни о чём не говорят — узлов у размера нет.
		if (_boundShape is MeasurementShape measurementLink)
		{
			_root.Add(BuildLinkPanel(measurementLink, "P1", "P2"));
		}
		// Изображение исключено: ImageShape наследуется от CurveShape, но его
		// узлы задают замкнутый контур с картинкой, а не линию, и привязывать
		// их не к чему.
		else if (_boundShape is CurveShape linkCurve
			&& linkCurve is not ImageShape
			&& linkCurve.Nodes.Count >= 2)
		{
			_root.Add(BuildLinkPanel(linkCurve));
		}

		ArrangeUi();

		if (IsHandleCreated)
			Invalidate();
	}

	private void OnSelectionGeometryChanged()
	{
		if (!IsHandleCreated)
			return;

		RebuildForSelection();
	}

	/// <summary>
	/// Создаёт раздел панели, восстанавливая его свёрнутость из сеанса.
	/// Идентификатор служит и ключом состояния, и строкой для перевода.
	/// </summary>
	private static SkCollapsePanel CreatePanel(string id, bool expandedByDefault = true)
	{
		bool expanded = _panelExpanded.TryGetValue(id, out bool stored)
			? stored
			: expandedByDefault;

		var panel = new SkCollapsePanel
		{
			HeaderText = L.T(id),
			Expanded = expanded
		};

		panel.ExpandedChanged = v => _panelExpanded[id] = v;

		return panel;
	}

	#region Множественное выделение

	/// <summary>Все выделенные фигуры. Первая из них — та, по которой построена панель.</summary>
	private static IReadOnlyList<Shape> Targets => MainForm.sel.Selected;

	private static bool IsMultiSelection => Targets.Count > 1;

	/// <summary>
	/// Выделенные фигуры нужного вида, кроме основной.
	///
	/// Основная исключена потому, что её изменил сам редактор — вместе
	/// с записью в историю. Здесь остаётся дописать то же самое остальным.
	/// Отбор по виду и есть правило «меняется только у тех, у кого этот
	/// параметр есть»: радиус получат окружности, текст — надписи,
	/// а положение и размер — все, потому что для них вид Shape.
	/// </summary>
	private IEnumerable<T> OtherTargets<T>() where T : class
	{
		foreach (var shape in Targets)
		{
			if (ReferenceEquals(shape, _boundShape))
				continue;

			if (shape is T typed)
				yield return typed;
		}
	}

	/// <summary>
	/// Правка ГЕОМЕТРИИ всех выделенных фигур нужного вида.
	///
	/// Снимок берётся сразу по всем, поэтому отмена возвращает выделение
	/// целиком одним нажатием. Основная фигура входит в тот же список:
	/// у полей положения и размера своей записи в историю не было вовсе,
	/// и заводить её отдельно значило бы получить два шага на одно действие.
	/// </summary>
	private void ApplyGeometryToSelection<T>(string title, Action<T> apply)
		where T : class
	{
		var affected = new List<Shape>();

		foreach (var shape in Targets)
		{
			if (shape is T)
				affected.Add(shape);
		}

		if (affected.Count == 0)
			return;

		var action = TransformShapesAction.Capture(title, affected);

		foreach (var shape in affected)
			apply((T)(object)shape);

		action.CaptureAfter();

		if (action.HasChanges())
			UndoStackForShape()?.Push(action);
	}

	/// <summary>
	/// Повторяет правку ОФОРМЛЕНИЯ на остальных выделенных фигурах.
	///
	/// Основная исключена: её изменил сам редактор, вместе со своей
	/// записью в историю. Обе половины попадают в один шаг отмены:
	/// группа открывается ЗДЕСЬ и закрывается сразу же, а запись основной
	/// фигуры к этому моменту уже лежит в стеке — поэтому она забирается
	/// из него обратно и кладётся в ту же группу.
	///
	/// Отбор allows и есть правило «меняется только там, где применимо»:
	/// цвет обводки получают лишь фигуры с включённой обводкой.
	/// </summary>
	private void ApplyStyleToOthers(
		string title,
		Func<Shape, bool> allows,
		Action<Shape> apply,
		IUndoableAction? primaryStep)
	{
		if (!IsMultiSelection)
			return;

		var others = new List<Shape>();

		foreach (var shape in OtherTargets<Shape>())
		{
			if (allows(shape))
				others.Add(shape);
		}

		if (others.Count == 0)
			return;

		var action = ShapeStyleAction.Capture(title, others);

		foreach (var shape in others)
			apply(shape);

		action.CaptureAfter();

		if (!action.HasChanges())
			return;

		var undo = UndoStackForShape();

		// Шаг основной фигуры забирается обратно ТОЛЬКО если он записан
		// этой же правкой. Иначе в группу попал бы посторонний прошлый
		// шаг, и одна отмена откатывала бы два разных действия.
		var taken = undo?.TakeLastIf(primaryStep);

		undo?.BeginGroup(title);

		try
		{
			if (taken != null)
				undo?.Push(taken);

			undo?.Push(action);
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	/// <summary>
	/// Одинаково ли значение у всех выделенных фигур нужного вида.
	/// Если нет — редактор показывается пустым.
	/// </summary>
	private bool SameAcrossSelection<T, TValue>(Func<T, TValue> read, out TValue value)
		where T : class
	{
		value = default!;

		bool first = true;

		foreach (var shape in Targets)
		{
			if (shape is not T typed)
				continue;

			var current = read(typed);

			if (first)
			{
				value = current;
				first = false;
				continue;
			}

			if (!EqualityComparer<TValue>.Default.Equals(value, current))
				return false;
		}

		return !first;
	}

	/// <summary>
	/// Повторяет правку на остальных целях и складывает всё в один шаг
	/// отмены вместе с правкой основной фигуры.
	///
	/// Цель — не обязательно фигура: у надписи это TextShape, лежащий
	/// внутри размера, у тени и прозрачности — сама фигура, у привязок —
	/// узел-хозяин. Поэтому список целей передаётся снаружи, а не
	/// вычисляется здесь по типу.
	///
	/// Цели, у которых значение уже такое, пропускаются: пустой шаг
	/// истории заставлял бы жать отмену дважды без видимого результата.
	/// </summary>
	private void ApplyToOthers<TTarget, TValue>(
		string title,
		string key,
		IEnumerable<TTarget> targets,
		Func<TTarget, TValue> read,
		Action<TTarget, TValue> write,
		TValue value,
		IUndoableAction? primaryStep)
		where TTarget : class
	{
		var steps = new List<IUndoableAction>();

		foreach (var target in targets)
		{
			var before = read(target);

			if (EqualityComparer<TValue>.Default.Equals(before, value))
				continue;

			write(target, value);

			var captured = target;

			steps.Add(new PropertyChangeAction<TValue>(
				title,
				before,
				value,
				v =>
				{
					write(captured, v);

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (captured, key)));
		}

		if (steps.Count == 0)
			return;

		var undo = UndoStackForShape();

		// Шаг основной фигуры забирается обратно ТОЛЬКО если он записан
		// этой же правкой: иначе в группу попал бы посторонний прошлый шаг.
		var taken = undo?.TakeLastIf(primaryStep);

		undo?.BeginGroup(title);

		try
		{
			if (taken != null)
				undo?.Push(taken);

			foreach (var step in steps)
				undo?.Push(step);
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	/// <summary>
	/// Надписи остальных выделенных объектов.
	///
	/// Надпись живёт и самостоятельной фигурой, и внутри размера — панель
	/// текста в обоих случаях строится по одному и тому же TextShape.
	/// Поэтому цели собираются не по типу выделенного объекта, а по тому,
	/// есть ли у него надпись.
	/// </summary>
	private IEnumerable<TextShape> OtherTextTargets(TextShape primary)
	{
		foreach (var shape in Targets)
		{
			TextShape? text = shape switch
			{
				TextShape own => own,
				MeasurementShape measurement => measurement.TextStyle,
				AngleMeasurementShape angle => angle.TextStyle,
				_ => null
			};

			if (text != null && !ReferenceEquals(text, primary))
				yield return text;
		}
	}

	/// <summary>Ставит в поле общее значение или оставляет его пустым.</summary>
	private void BindNumeric<T>(SkNumericUpDown editor, Func<T, float> read, float scale = 1f)
		where T : class
	{
		if (SameAcrossSelection<T, float>(s => read(s), out float common))
			editor.SetValue(common * scale);
		else
			editor.SetEmpty();
	}

	#endregion

	private void OnUnitsChanged()
	{
		if (!IsHandleCreated)
			return;

		RebuildForSelection();
	}

	private void ArrangeUi()
	{
		_root.Bounds = new SKRect(8, 8 - _scrollY, Width - 8, Height - 8);
		_root.Arrange();

		// Второй проход обязателен из-за строк с переносом.
		//
		// Кнопки выбора типа заливки лежат в SkFlowLayout с WrapContents,
		// и его высота зависит от ширины, которую он получает В ХОДЕ этого
		// же прохода. На первом проходе ширина ещё исходная, семь кнопок
		// в неё не помещаются и переносятся на вторую строку — панель
		// резервирует под них двойную высоту. К концу прохода ширина уже
		// настоящая, кнопки встают в один ряд, а пустое место под ними
		// остаётся до следующей перерисовки.
		//
		// Второй проход считает высоту по уже известной ширине, и разрыв
		// не успевает появиться на экране.
		_root.Arrange();
	}

	#endregion

	#region Отрисовка и ввод

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ArrangeUi();
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		if (Width <= 0 || Height <= 0)
			return;

		EnsureBuffers();

		if (_backBuffer == null || _gdiBuffer == null)
			return;

		var pal = SkUiTheme.Current;

		using (var canvas = new SKCanvas(_backBuffer))
		{
			canvas.Clear(pal.PanelBack);

			using var fill = SkUiTheme.Fill(pal.PanelBack2);
			using var border = SkUiTheme.Stroke(pal.Border);

			canvas.DrawRect(new SKRect(0, 0, Width, Height), fill);
			canvas.DrawLine(0, 0, 0, Height, border);

			canvas.Save();
			canvas.ClipRect(new SKRect(0, 0, Width, Height));
			_root.Draw(canvas);
			_root.DrawOverlay(canvas);
			canvas.Restore();

			DrawResizeGrip(canvas, pal);
		}

		Rectangle rect = new Rectangle(0, 0, _gdiBuffer.Width, _gdiBuffer.Height);
		BitmapData data = _gdiBuffer.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

		try
		{
			int srcStride = _backBuffer.RowBytes;
			int dstStride = Math.Abs(data.Stride);
			int rowBytes = Math.Min(srcStride, dstStride);
			int height = Math.Min(_backBuffer.Height, data.Height);

			if (_pixelBuffer == null || _pixelBuffer.Length != rowBytes)
				_pixelBuffer = new byte[rowBytes];

			IntPtr srcBase = _backBuffer.GetPixels();
			IntPtr dstBase = data.Scan0;

			for (int y = 0; y < height; y++)
			{
				IntPtr srcPtr = IntPtr.Add(srcBase, y * srcStride);
				IntPtr dstPtr = IntPtr.Add(dstBase, y * data.Stride);

				Marshal.Copy(srcPtr, _pixelBuffer, 0, rowBytes);
				Marshal.Copy(_pixelBuffer, 0, dstPtr, rowBytes);
			}
		}
		finally
		{
			_gdiBuffer.UnlockBits(data);
		}

		e.Graphics.DrawImageUnscaled(_gdiBuffer, 0, 0);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);

		Focus();

		if (IsOverGrip(e.X))
		{
			_resizing = true;
			_resizeStartWidth = Width;
			_resizeStartScreenX = MousePosition.X;
			Capture = true;
			return;
		}

		var ev = ToSk(e);

		bool closedAny = _root.CloseDropDownsOutside(ev.Location);
		var target = _root.FindTopMostOverlay(ev.Location) ?? _root.FindTopMost(ev.Location);

		// Щелчок по стрелкам счётчика не делает его активным контролом.
		// Иначе панель считает, что идёт набор текста: RefreshFromBoundShape
		// молчит, а сочетания клавиш уходят в поле вместо инструментов —
		// притом что курсора в поле пользователь не видит.
		SetFocused(target is SkNumericUpDown spinner && spinner.HitsSpinButtons(ev.Location)
			? null
			: target);

		_captured = target;
		_captured?.OnMouseDown(ev);

		ArrangeUi();

		if (closedAny || target != null)
			Invalidate();
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);

		Focus();

		var ev = ToSk(e);
		var target = _root.FindTopMostOverlay(ev.Location) ?? _root.FindTopMost(ev.Location);

		if (target != null)
		{
			SetFocused(target);
			if (target.OnMouseDoubleClick(ev))
			{
				Invalidate();
				NotifyShapeChanged();
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		UpdateGripState(e.X);

		if (_resizing)
		{
			int dx = MousePosition.X - _resizeStartScreenX;
			int newWidth = (int)Math.Clamp(_resizeStartWidth - dx, MinPanelWidth, MaxPanelWidth);

			if (newWidth != Width)
			{
				PanelWidth = newWidth;
				Width = newWidth;
				PanelWidthChanged?.Invoke(newWidth);
			}

			return;
		}

		var ev = ToSk(e);
		bool changed = false;

		if (_captured != null)
		{
			changed = _captured.OnMouseMove(ev);
		}
		else
		{
			var target = _root.FindTopMostOverlay(ev.Location) ?? _root.FindTopMost(ev.Location);
			if (target != null)
				changed = target.OnMouseMove(ev);
		}

		if (changed)
			Invalidate();
	}

	/// <summary>
	/// Сброс подсветки разделителя.
	///
	/// Без этого полоса оставалась бы подсвеченной после того, как курсор
	/// ушёл на холст: OnMouseMove больше не приходит, и состояние замирает.
	///
	/// Во время перетаскивания сброса нет: мышь закономерно уходит
	/// за пределы панели, пока граница едет за ней.
	/// </summary>
	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);

		if (_resizing || !_gripHovered)
			return;

		_gripHovered = false;
		Cursor = Cursors.Default;
		Invalidate();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		if (_resizing)
		{
			_resizing = false;
			Capture = false;

			// Курсор и подсветка пересчитываются по фактическому положению
			// мыши: после отпускания она может быть уже вне полосы.
			UpdateGripState(e.X);

			Invalidate();
			return;
		}

		var ev = ToSk(e);
		_captured?.OnMouseUp(ev);
		_captured = null;

		ArrangeUi();
		Invalidate();
		NotifyShapeChanged();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);

		var point = new SKPoint(e.X, e.Y);
		var ev = new SkMouseEvent { Location = point, Delta = e.Delta };

		// Открытый выпадающий список рисуется поверх панели и забирает колесо.
		var overlayTarget = _root.FindTopMostOverlay(point);

		if (overlayTarget != null && overlayTarget.OnMouseWheel(ev))
		{
			Invalidate();
			return;
		}

		// Контрол в фокусе — например многострочное текстовое поле.
		if (_focused != null && _focused.OnMouseWheel(ev))
		{
			Invalidate();
			return;
		}

		float contentHeight = _root.Bounds.Height;
		float visible = Height - 16f;
		float max = MathF.Max(0, contentHeight - visible);

		_scrollY -= Math.Sign(e.Delta) * 30f;
		_scrollY = Math.Clamp(_scrollY, 0, max);

		ArrangeUi();
		Invalidate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		// Табуляция переводит ввод на следующее поле панели.
		//
		// Раньше её никто не обрабатывал: числовое поле возвращало
		// «не моё», и WinForms уводил ввод из панели целиком — к соседнему
		// элементу окна. Набранное при этом значение оставалось
		// не применённым, потому что применяется оно при потере фокуса
		// внутри панели, а такой потери не происходило.
		//
		// Многострочное текстовое поле — исключение: там табуляция
		// вводится как символ.
		if ((e.KeyCode == Keys.Tab) && _focused is not SkTextBox { Multiline: true })
		{
			if (MoveFocusToNextEditor(backwards: e.Shift))
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Invalidate();
				NotifyShapeChanged();
				return;
			}
		}

		if (_focused != null)
		{
			bool handled = _focused.OnKeyDown(new SkKeyEvent
			{
				KeyCode = e.KeyCode,
				Ctrl = e.Control,
				Shift = e.Shift,
				Alt = e.Alt
			});

			if (handled)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Invalidate();
				NotifyShapeChanged();
			}
		}
	}

	protected override bool IsInputKey(Keys keyData)
	{
		// Tab панель забирает себе всегда.
		//
		// В многострочном поле это ввод символа табуляции, во всех
		// остальных — переход к следующему полю ПАНЕЛИ. Прежде условие
		// ограничивалось многострочным полем, и в остальных случаях
		// WinForms считал Tab своей навигацией: OnKeyDown его не видел,
		// а фокус уходил из панели в соседний элемент окна вместе
		// с ненабранным значением.
		if ((keyData & Keys.KeyCode) == Keys.Tab)
			return true;

		Keys key = keyData & Keys.KeyCode;

		return key == Keys.Left ||
			   key == Keys.Right ||
			   key == Keys.Up ||
			   key == Keys.Down ||
			   key == Keys.Home ||
			   key == Keys.End ||
			   key == Keys.Delete ||
			   key == Keys.Tab
			   ? true
			   : base.IsInputKey(keyData);
	}

	protected override void OnKeyPress(KeyPressEventArgs e)
	{
		base.OnKeyPress(e);

		if (_focused != null && _focused.OnKeyChar(e.KeyChar))
		{
			Invalidate();
			NotifyShapeChanged();
		}
	}

	/// <summary>
	/// Полоса разделителя между холстом и панелью.
	///
	/// Раньше это был прямоугольник с прозрачностью 24 из 255 — почти
	/// невидимый, и о возможности тянуть границу приходилось догадываться.
	///
	/// Теперь у полосы три состояния и насечка посередине — привычный
	/// знак того, что границу можно двигать.
	/// </summary>
	private void DrawResizeGrip(SKCanvas canvas, SkUiPalette pal)
	{
		var area = new SKRect(0, 0, ResizeGrip, Height);

		if (_resizing)
		{
			using var active = SkUiTheme.Fill(pal.Accent);
			canvas.DrawRect(area, active);
		}
		else if (_gripHovered)
		{
			using var hover = SkUiTheme.Fill(pal.Hover);
			canvas.DrawRect(area, hover);
		}
		else
		{
			using var idle = SkUiTheme.Fill(WithAlpha(pal.Text, 16));
			canvas.DrawRect(area, idle);
		}

		// Насечка: три точки по центру высоты. На низкой панели
		// она не рисуется: три точки на всю высоту читались бы как сор.
		if (Height < GripMarkHeight * 2f)
			return;

		var markColor = _resizing || _gripHovered
			? WithAlpha(pal.Text, 200)
			: WithAlpha(pal.Text, 90);

		using var mark = SkUiTheme.Fill(markColor);

		float cx = ResizeGrip * 0.5f;
		float cy = Height * 0.5f;
		float step = 5f;
		float r = 1.2f;

		for (int i = -1; i <= 1; i++)
			canvas.DrawCircle(cx, cy + i * step, r, mark);
	}

	private static SKColor WithAlpha(SKColor color, byte alpha)
		=> new SKColor(color.Red, color.Green, color.Blue, alpha);

	/// <summary>Лежит ли точка на полосе разделителя.</summary>
	private static bool IsOverGrip(int x) => x <= ResizeGrip;

	/// <summary>
	/// Обновляет подсветку и курсор разделителя.
	///
	/// Курсор ставится и во время перетаскивания: при быстром движении
	/// мышь убегает с полосы, а захват удерживается, и стрелка не должна
	/// мелькать.
	/// </summary>
	private void UpdateGripState(int mouseX)
	{
		bool hovered = _resizing || IsOverGrip(mouseX);

		Cursor = hovered ? Cursors.SizeWE : Cursors.Default;

		if (hovered == _gripHovered)
			return;

		_gripHovered = hovered;
		Invalidate();
	}

	/// <summary>
	/// Переводит ввод на следующее поле панели. Возвращает false, если
	/// переводить не на что.
	///
	/// Значение текущего поля применяется само: SetFocused завершает
	/// правку предыдущего контрола. Отдельно вызывать применение здесь
	/// было бы вторым местом, где это делается, — и однажды они разошлись
	/// бы.
	///
	/// По кругу: с последнего поля ввод возвращается на первое. Панель
	/// короткая, и упереться в её конец, перебирая поля табуляцией,
	/// неприятнее, чем пройти второй круг.
	/// </summary>
	private bool MoveFocusToNextEditor(bool backwards)
	{
		var editors = new List<SkControl>();
		CollectEditors(_root, editors);

		if (editors.Count == 0)
			return false;

		int current = _focused == null ? -1 : editors.IndexOf(_focused);

		int next = current < 0
			? (backwards ? editors.Count - 1 : 0)
			: (current + (backwards ? -1 : 1) + editors.Count) % editors.Count;

		SetFocused(editors[next]);
		ScrollIntoView(editors[next]);

		if (editors[next] is SkNumericUpDown spinner)
			spinner.SelectAllForEditing();

		return true;
	}

	/// <summary>
	/// Собирает поля ввода в порядке обхода дерева.
	///
	/// Порядок обхода совпадает с порядком, в котором строки добавлялись
	/// в панель, то есть с тем, что видит пользователь сверху вниз.
	/// Сортировать по координатам не нужно и вредно: у свёрнутой группы
	/// координаты не обновляются, и поля из неё перемешались бы с чужими.
	/// </summary>
	private static void CollectEditors(SkControl control, List<SkControl> result)
	{
		foreach (var child in control.Children)
		{
			if (!child.Visible || !child.Enabled)
				continue;

			if (child is SkNumericUpDown or SkTextBox)
				result.Add(child);

			CollectEditors(child, result);
		}
	}

	/// <summary>
	/// Подкручивает панель так, чтобы поле оказалось на виду: иначе
	/// табуляция уводила бы ввод за нижний край.
	/// </summary>
	private void ScrollIntoView(SkControl control)
	{
		var bounds = control.AbsoluteBounds;

		float visible = Height - 16f;
		float margin = 8f;

		float contentHeight = _root.Bounds.Height;
		float max = MathF.Max(0, contentHeight - visible);

		if (bounds.Top < margin)
			_scrollY = Math.Clamp(bounds.Top + _scrollY - margin, 0, max);
		else if (bounds.Bottom > visible - margin)
			_scrollY = Math.Clamp(bounds.Bottom + _scrollY - visible + margin, 0, max);
		else
			return;

		ArrangeUi();
	}

	private void SetFocused(SkControl? control)
	{
		if (!ReferenceEquals(_focused, control))
		{
			if (_focused is SkNumericUpDown oldNud)
				oldNud.CommitEditing();
			else if (_focused is SkNumTextBox oldNumBox)
				oldNumBox.FinishEditing();
		}

		ClearFocus(_root);
		_focused = control;

		if (_focused != null)
			_focused.Focused = true;
	}

	private void ClearFocus(SkControl c)
	{
		c.Focused = false;
		foreach (var ch in c.Children)
			ClearFocus(ch);
	}

	private static SkMouseEvent ToSk(MouseEventArgs e)
	{
		return new SkMouseEvent
		{
			Location = new SKPoint(e.X, e.Y),
			Button = e.Button,
			Delta = e.Delta,
			Ctrl = (ModifierKeys & Keys.Control) != 0,
			Shift = (ModifierKeys & Keys.Shift) != 0,
			Alt = (ModifierKeys & Keys.Alt) != 0
		};
	}

	#endregion

	#region Панель General

	private SkCollapsePanel BuildGeneralPanel(Shape shape)
	{
		var p = CreatePanel("General");

		var worldBounds = shape.GetBounds();

		// X и Y — это левый верхний угол габаритов НА СТРАНИЦЕ, а не поля
		// TransX и TransY матрицы. Прежде показывались именно они, и у
		// масштабированной или повёрнутой фигуры число не имело отношения
		// к тому, где она видна: после сжатия по высоте панель показывала
		// сдвиг матрицы в сотни тысяч, хотя фигура стояла на месте.
		// При нескольких выделенных фигурах поле пустует, если значения
		// разошлись: показать чужое число значило бы соврать о фигуре,
		// которую пользователь тоже выделил.
		_xEditor = new SkNumericUpDown() { AllowDecimal = true, Decimals = 3 };
		BindNumeric<Shape>(_xEditor, s => s.GetBounds().Left);

		_yEditor = new SkNumericUpDown() { AllowDecimal = true, Decimals = 3 };
		BindNumeric<Shape>(_yEditor, s => s.GetBounds().Top);

		_wEditor = new SkNumericUpDown { Minimum = 0, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_wEditor, s => s.GetBounds().Width);

		_hEditor = new SkNumericUpDown { Minimum = 0, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_hEditor, s => s.GetBounds().Height);

		_rotEditor = new SkNumericUpDown { Minimum = -360, Maximum = 360, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_rotEditor, s => GetRotationDegrees(s.LocalTransform));

		// Слой у нескольких фигур один быть не может: он и задаёт их
		// порядок друг относительно друга. Поле остаётся видимым, но
		// выключенным — так понятнее, чем спрятать строку совсем.
		_layerEditor = new SkNumericUpDown
		{
			Increment = 1,
			Decimals = 0,
			Enabled = !IsMultiSelection
		};

		_layerEditor.SetValue(shape.Layer);

		// Задаётся положение, а применяется СМЕЩЕНИЕ на разницу. Присваивать
		// TransX напрямую нельзя: у повёрнутой или масштабированной фигуры
		// это поле не равно её координате на странице, и ввод числа
		// перебрасывал бы фигуру в непредсказуемое место.
		// Каждая выделенная фигура встаёт левым краем на введённое число:
		// смещение считается по её собственным габаритам, а не общее
		// на всех — иначе одинаковым стал бы сдвиг, а не координата.
		_xEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;

			ApplyGeometryToSelection<Shape>("Position X", s =>
			{
				float dx = v - s.GetBounds().Left;

				if (MathF.Abs(dx) < 0.0001f)
					return;

				s.LocalTransform = s.LocalTransform.PostConcat(
					SKMatrix.CreateTranslation(dx, 0f));
			});

			NotifyShapeChanged(rebuildPanel: true);
		};

		_yEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;

			ApplyGeometryToSelection<Shape>("Position Y", s =>
			{
				float dy = v - s.GetBounds().Top;

				if (MathF.Abs(dy) < 0.0001f)
					return;

				s.LocalTransform = s.LocalTransform.PostConcat(
					SKMatrix.CreateTranslation(0f, dy));
			});

			NotifyShapeChanged(rebuildPanel: true);
		};

		_wEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;

			ApplyGeometryToSelection<Shape>("Width", s =>
			{
				var oldWorld = s.GetBounds();

				// Из нулевого размера восстановить нечего: у сплющенной фигуры
				// все точки по этой оси совпали, и умножение на любой множитель
				// оставит их совпавшими. Прежний код делил на подставное
				// 0.0001 и получал множитель в тысячи — фигура не менялась,
				// зато TransX улетал в сотни тысяч.
				if (!TryGetScale(oldWorld.Width, v, out float sx))
					return;

				// Опора — центр самой фигуры: иначе соседние объекты
				// разъехались бы от общего центра выделения.
				var scale = CreateScaleAt(sx, 1f, GetWorldCenter(s));

				s.LocalTransform = s.LocalTransform.PostConcat(scale);
			});

			NotifyShapeChanged(rebuildPanel: true);
		};

		_hEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;

			ApplyGeometryToSelection<Shape>("Height", s =>
			{
				var oldWorld = s.GetBounds();

				if (!TryGetScale(oldWorld.Height, v, out float sy))
					return;

				var scale = CreateScaleAt(1f, sy, GetWorldCenter(s));

				s.LocalTransform = s.LocalTransform.PostConcat(scale);
			});

			NotifyShapeChanged(rebuildPanel: true);
		};

		_rotEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;

			ApplyGeometryToSelection<Shape>("Rotation", s =>
			{
				float current = GetRotationDegrees(s.LocalTransform);
				float delta = v - current;

				if (MathF.Abs(delta) < 0.0001f)
					return;

				var rotate = CreateRotationAt(delta, GetWorldCenter(s));

				s.LocalTransform = s.LocalTransform.PostConcat(rotate);
			});

			NotifyShapeChanged(rebuildPanel: true);
		};

		_layerEditor.ValueChanged = v =>
		{
			if (_updatingFromShape) return;
			shape.Layer = (int)v;
			NotifyShapeChanged(rebuildPanel: false);
		};

		// Имя стоит первым: это единственная строка, отвечающая на вопрос
		// «что это за объект», а остальные — на вопрос «где он и какой».
		// У группы она особенно нужна: по габаритам и слою одну группу
		// от другой не отличить.
		_nameEditor = new SkTextBox
		{
			Text = SameAcrossSelection<Shape, string>(s => s.Name ?? string.Empty, out string commonName)
				? commonName
				: string.Empty
		};

		_nameEditor.EditingFinished = s =>
		{
			if (_updatingFromShape)
				return;

			string before = shape.Name ?? string.Empty;
			string after = (s ?? string.Empty).Trim();

			// Безымянная фигура читается в панели слоёв как пустая строка,
			// поэтому пустой ввод трактуется как отказ от переименования.
			// Присваивание Text событий не поднимает — обратной записи
			// в модель отсюда не случится.
			if (after.Length == 0 || after == before)
			{
				if (_nameEditor != null)
					_nameEditor.Text = before;

				return;
			}

			shape.Name = after;

			foreach (var other in OtherTargets<Shape>())
				other.Name = after;

			UndoStackForShape()?.Push(new PropertyChangeAction<string>(
				"Shape name",
				before,
				after,
				v =>
				{
					_updatingFromShape = true;

					try
					{
						shape.Name = v;

						if (_nameEditor != null)
							_nameEditor.Text = v;
					}
					finally
					{
						_updatingFromShape = false;
					}

					NotifyShapeChanged();
					Invalidate();
				},
				mergeKey: (shape, "Shape.Name")));

			NotifyShapeChanged(rebuildPanel: false);
		};

		p.Content.Add(new SkPropertyRow(L.T("Name"), _nameEditor));
		p.Content.Add(new SkPropertyRow(L.T("X"), _xEditor));
		p.Content.Add(new SkPropertyRow(L.T("Y"), _yEditor));
		p.Content.Add(new SkPropertyRow(L.T("Width"), _wEditor));
		p.Content.Add(new SkPropertyRow(L.T("Height"), _hEditor));
		p.Content.Add(new SkPropertyRow(L.T("Rotation"), _rotEditor));
		p.Content.Add(new SkPropertyRow(L.T("Layer"), _layerEditor));

		AppendShapeSpecificRows(p, shape);

		p.Arrange();

		return p;
	}

	public void RefreshFromBoundShape()
	{
		if (!IsHandleCreated)
			return;

		if (_boundShape == null)
			return;

		if (_focused is SkTextBox || _focused is SkNumTextBox || _focused is SkNumericUpDown)
			return;

		var worldBounds = _boundShape.GetBounds();
		var m = _boundShape.LocalTransform;

		_updatingFromShape = true;
		try
		{
			if (_xEditor != null) BindNumeric<Shape>(_xEditor, s => s.GetBounds().Left);
			if (_yEditor != null) BindNumeric<Shape>(_yEditor, s => s.GetBounds().Top);
			if (_wEditor != null) BindNumeric<Shape>(_wEditor, s => s.GetBounds().Width);
			if (_hEditor != null) BindNumeric<Shape>(_hEditor, s => s.GetBounds().Height);
			if (_rotEditor != null) BindNumeric<Shape>(_rotEditor, s => GetRotationDegrees(s.LocalTransform));

			_layerEditor?.SetValue(_boundShape.Layer);
			if (_nameEditor != null)
				_nameEditor.Text = _boundShape.Name ?? string.Empty;

			foreach (var refresh in _shapeParamRefreshers)
				refresh();
		}
		finally
		{
			_updatingFromShape = false;
		}

		Invalidate();
	}

	private static SKPoint GetLocalCenter(Shape shape)
	{
		var b = shape.GetLocalBounds();
		return new SKPoint(b.MidX, b.MidY);
	}

	/// <summary>
	/// Центр фигуры в МИРОВЫХ координатах.
	///
	/// Именно он служит опорой для масштабирования и поворота из панели.
	/// Прежде брался локальный центр, хотя преобразование дописывается
	/// через PostConcat, то есть действует уже в мировом пространстве.
	/// Для фигуры, чья локальная система смещена относительно страницы —
	/// а у линии начало обычно в первом узле, — опора уезжала, и при
	/// сжатии по высоте линия не сплющивалась на месте, а перелетала
	/// к нулевой координате страницы.
	/// </summary>
	private static SKPoint GetWorldCenter(Shape shape)
	{
		var b = shape.GetBounds();
		return new SKPoint(b.MidX, b.MidY);
	}

	/// <summary>
	/// Множитель для перехода от текущего размера к заданному.
	///
	/// Ноль в поле размера — законный способ выровнять почти горизонтальную
	/// линию точно по горизонтали, и запрещать его не стоит. Но множитель
	/// ровно ноль сделал бы преобразование фигуры НЕОБРАТИМЫМ: у матрицы
	/// с нулевым определителем нет обратной, а на ней держатся попадание
	/// курсора, правка узлов и прилипание.
	///
	/// Поэтому множитель ограничен снизу исчезающе малым значением.
	/// Фигура выглядит схлопнутой так же — при размере 0.12 остаётся
	/// 0.00012, что в поле с двумя знаками показывается как 0.00, — зато
	/// матрица остаётся обратимой, и прежний размер возвращается обычным
	/// вводом, а не только отменой.
	/// </summary>
	private static bool TryGetScale(float current, float target, out float scale)
	{
		const float Epsilon = 1e-7f;
		const float MinScale = 1e-3f;

		scale = 1f;

		if (MathF.Abs(current) < Epsilon)
			return false;

		if (MathF.Abs(target - current) < Epsilon)
			return false;

		scale = target / current;

		if (MathF.Abs(scale) < MinScale)
			scale = scale < 0f ? -MinScale : MinScale;

		return true;
	}

	private static float GetRotationDegrees(SKMatrix m)
	{
		return MathF.Atan2(m.SkewY, m.ScaleX) * 180f / MathF.PI;
	}

	private static SKMatrix CreateScaleAt(float sx, float sy, SKPoint pivot)
	{
		var m = SKMatrix.CreateIdentity();
		m = m.PostConcat(SKMatrix.CreateTranslation(-pivot.X, -pivot.Y));
		m = m.PostConcat(SKMatrix.CreateScale(sx, sy));
		m = m.PostConcat(SKMatrix.CreateTranslation(pivot.X, pivot.Y));
		return m;
	}

	private static SKMatrix CreateRotationAt(float degrees, SKPoint pivot)
	{
		var m = SKMatrix.CreateIdentity();
		m = m.PostConcat(SKMatrix.CreateTranslation(-pivot.X, -pivot.Y));
		m = m.PostConcat(SKMatrix.CreateRotationDegrees(degrees));
		m = m.PostConcat(SKMatrix.CreateTranslation(pivot.X, pivot.Y));
		return m;
	}

	private static string GridRowToPattern(SkPatternGridEditor editor, SKColor frontColor)
	{
		int width = editor.PatternWidth;
		if (width <= 0)
			return "";

		using var bmp = editor.BuildBitmap();

		var backColor = editor.BackColor;

		int[] bits = new int[width];
		for (int x = 0; x < width; x++)
		{
			var c = bmp.GetPixel(x, 0);

			// Закрашенной считается любая непрозрачная ячейка, отличная от фона.
			// Точное сравнение с frontColor не годится: премультипликация
			// съедает единицу в канале, и ячейка читалась как пустая.
			bool isOn = c.Alpha > 8 && !ColorsClose(c, backColor);

			bits[x] = isOn ? 1 : 0;
		}

		// Все единицы — сплошная линия, dash не нужен.
		if (bits.All(b => b == 1))
			return "";

		// Все нули — рисовать нечего. Возвращаем пустой паттерн, то есть
		// сплошную линию, вместо "0 width": прежнее значение приводило
		// к невидимой обводке.
		if (bits.All(b => b == 0))
			return "";

		var runs = new List<float>();

		// Первый run всегда описывает линию (ON). Если сетка начинается
		// с пробелов, первая длина будет нулевой.
		bool currentOn = true;
		int i = 0;

		while (i < width)
		{
			int run = 0;

			while (i < width && bits[i] == (currentOn ? 1 : 0))
			{
				run++;
				i++;
			}

			runs.Add(run);
			currentOn = !currentOn;
		}

		// Количество интервалов должно быть чётным.
		if ((runs.Count % 2) != 0)
			runs.Add(0f);

		return string.Join(
			" ",
			runs.Select(r => r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
	}

	private static bool ColorsClose(SKColor a, SKColor b, int tolerance = 8)
		=> Math.Abs(a.Red - b.Red) <= tolerance
		&& Math.Abs(a.Green - b.Green) <= tolerance
		&& Math.Abs(a.Blue - b.Blue) <= tolerance;

	private static void PatternToGridRow(
		SkPatternGridEditor editor,
		string pattern,
		int width,
		SKColor frontColor,
		SKColor backColor)
	{
		editor.SetSize(width, 1, backColor);
		editor.Clear(backColor);

		// Пустой паттерн трактуем как "сплошная линия",
		// значит в редакторе показываем всю строку закрашенной.
		if (string.IsNullOrWhiteSpace(pattern))
		{
			using var mask = new SKBitmap(width, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
			for (int x = 0; x < width; x++)
				mask.SetPixel(x, 0, SKColors.Black);

			editor.LoadFromBitmap(mask, frontColor, backColor);
			return;
		}

		var intervals = pattern
			.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(p => float.TryParse(
							p,
							System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture,
							out var f)
						? f
						: 0f)
			.ToArray();

		if (intervals.Length == 0)
			return;

		using var bmp = new SKBitmap(width, 1, SKColorType.Bgra8888, SKAlphaType.Premul);

		int xPos = 0;
		bool isOn = true; // первый run — "линия"

		foreach (var len in intervals)
		{
			int cellsForRun = (int)MathF.Round(len);
			for (int i = 0; i < cellsForRun && xPos < width; i++, xPos++)
			{
				bmp.SetPixel(xPos, 0, isOn ? SKColors.Black : SKColors.Transparent);
			}
			isOn = !isOn;
		}

		for (; xPos < width; xPos++)
			bmp.SetPixel(xPos, 0, SKColors.Transparent);

		editor.LoadFromBitmap(bmp, frontColor, backColor);
	}

	#endregion

	#region Панель Line

	private SkCollapsePanel BuildStrokePanel(Shape shape)
	{
		shape.Stroke ??= new StrokeStyle();

		var p = CreatePanel("Line");

		// ------------------------------------------------------------------------
		// Общая машинерия истории
		// ------------------------------------------------------------------------

		// Пока true, обработчики контролов не пишут ни в модель, ни в историю.
		// Ставится в двух случаях: при программном заполнении редактора сетки
		// (он проходит через промежуточное пустое состояние) и при откате
		// истории, когда мы сами обновляем значения контролов.
		bool suppressUi = false;

		// Записывает шаг истории. Сеттер обновляет и модель, и контрол — иначе
		// после Undo панель показывала бы старые значения, пока её не перестроят.
		// mergeKey склеивает непрерывное изменение одного свойства в один шаг,
		// иначе каждый щелчок стрелкой давал бы отдельную отмену.
		// Возвращает признак того, что шаг действительно записан: по нему
		// ApplyStyleToOthers решает, забирать ли его обратно в общую группу.
		IUndoableAction? PushChange<T>(string name, T before, T after, Action<T> apply, string key)
		{
			if (suppressUi)
				return null;

			if (EqualityComparer<T>.Default.Equals(before, after))
				return null;

			var step = new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						// finally обязательно: исключение внутри apply иначе
						// навсегда отключило бы панель от модели.
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (shape, key));

			UndoStackForShape()?.Push(step);

			// Возвращается сам шаг, а не признак «записано»: Push мог
			// слить его с предыдущим по совпадению ключа, и тогда
			// последним в истории остаётся ЧУЖОЙ, более ранний шаг.
			return step;
		}

		// ------------------------------------------------------------------------
		// Enabled
		// ------------------------------------------------------------------------

		var enabled = new SkCheckBox { Checked = shape.Stroke.Enabled };
		SkPropertyRow enabledRow = new SkPropertyRow("enabled_row", L.T("Enabled"), enabled);
		p.Content.Add(enabledRow);

		// ------------------------------------------------------------------------
		// Цвет контура
		// ------------------------------------------------------------------------

		// Строка добавляется здесь, а не в блоке компоновки ниже: порядок
		// строк равен порядку добавления, и цвет должен идти сразу за Enabled.
		// Под UpdateRowsVisibility она попадает автоматически: имя у неё
		// не enabled_row, значит при выключенной обводке она скроется вместе
		// со всеми остальными.
		var strokeColor = new SkColorButton
		{
			Text = string.Empty,
			Color = shape.Stroke.Color
		};

		strokeColor.Click = btn =>
		{
			if (suppressUi)
				return;

			var before = shape.Stroke.Color;
			shape.Stroke.Color = btn.Color;

			var recorded = PushChange(
				"Stroke colour",
				before,
				btn.Color,
				x =>
				{
					shape.Stroke.Color = x;
					strokeColor.Color = x;
				},
				"Stroke.Color");

			// Цвет достаётся только тем, у кого обводка включена: у
			// остальных он не виден, а тихая правка невидимого свойства
			// всплыла бы потом как неожиданность.
			ApplyStyleToOthers(
				"Stroke colour",
				s => s.Stroke.Enabled,
				s => s.Stroke.Color = btn.Color,
				recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		p.Content.Add(new SkPropertyRow("stroke_color_row", L.T("Color"), strokeColor));

		// Вынесено в функцию: то же самое нужно вызвать при откате истории.
		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "enabled_row")
					p.Content.Children[i].Visible = shape.Stroke.Enabled;
			}
		}

		// ------------------------------------------------------------------------
		// Толщина
		// ------------------------------------------------------------------------

		var thick = new SkNumericUpDown
		{
			Increment = 0.1f,
			Decimals = 2,
			Minimum = 0,
			Maximum = 1000
		};
		thick.SetValue(shape.Stroke.Thickness);

		// ------------------------------------------------------------------------
		// Тип линии
		// ------------------------------------------------------------------------

		var strokeType = new SkDropDownBox();
		_strokeTypeDropDown = strokeType;

		void RefreshStrokeTypeItems()
		{
			strokeType.SetItems(
				StrokePatternLibrary.Items.Select(x => new SkDropDownItem(
					x.Id,
					x.DisplayName,
					x.PreviewBitmap)));

			var current = StrokePatternLibrary.FindByPattern(shape.Stroke.Pattern);
			strokeType.SetSelected(current?.Id ?? StrokePatternLibrary.Items[0].Id);
		}

		const int StrokePatternMinLen = 2;
		const int StrokePatternMaxLen = 64;

		var strokePatternLen = new SkNumericUpDown
		{
			Minimum = StrokePatternMinLen,
			Maximum = StrokePatternMaxLen,
			Increment = 1,
			Decimals = 0
		};
		_strokePatternLenEditor = strokePatternLen;

		var strokePattern = new SkPatternGridEditor
		{
			Bounds = new SKRect(0, 0, 260, 34),
			FrontColor = SKColors.Black,
			BackColor = SKColors.White
		};
		_strokePatternEditor = strokePattern;

		var patternLenUoM = new SkNumericUpDown
		{
			Minimum = 0.1f,
			Maximum = 1000f,
			Increment = 0.5f,
			Decimals = 2
		};
		patternLenUoM.SetValue(shape.Stroke.PatternLength);

		// Программное заполнение редактора сетки. Подавление обязательно:
		// PatternToGridRow вызывает SetSize и Clear, каждый из которых поднимает
		// PatternChanged, и обработчик записал бы в модель пустую сетку.
		// Именно это и приводило к исчезновению обводки при выборе типа линии.
		void LoadPatternIntoEditor(string patternText, int len)
		{
			bool previous = suppressUi;
			suppressUi = true;

			try
			{
				strokePatternLen.SetValue(len);
				PatternToGridRow(strokePattern, patternText, len, strokePattern.FrontColor, strokePattern.BackColor);
			}
			finally
			{
				suppressUi = previous;
			}
		}

		{
			var initial = StrokePatternLibrary.FindByPattern(shape.Stroke.Pattern);
			int initialLen = initial?.Length ?? Math.Max(StrokePatternMinLen, 8);
			LoadPatternIntoEditor(shape.Stroke.Pattern, initialLen);
		}

		RefreshStrokeTypeItems();

		// ------------------------------------------------------------------------
		// Глифы и их размеры
		// ------------------------------------------------------------------------

		const float GlyphSizeMin = 0.5f;
		const float GlyphSizeMax = 50f;

		var startSize = new SkNumericUpDown
		{
			Minimum = GlyphSizeMin,
			Maximum = GlyphSizeMax,
			Increment = 0.5f,
			Decimals = 2
		};

		var endSize = new SkNumericUpDown
		{
			Minimum = GlyphSizeMin,
			Maximum = GlyphSizeMax,
			Increment = 0.5f,
			Decimals = 2
		};

		startSize.SetValue(shape.Stroke.StartGlyphSize);
		endSize.SetValue(shape.Stroke.EndGlyphSize);

		// Размер имеет смысл только при выбранном глифе: включённое поле,
		// которое ни на что не влияет, читается как неработающая функция.
		startSize.Enabled = !string.IsNullOrEmpty(shape.Stroke.StartGlyph);
		endSize.Enabled = !string.IsNullOrEmpty(shape.Stroke.EndGlyph);

		// Список глифов графический: каждый вариант показан отрезком
		// линии с наконечником. Названия вроде «Triangle arrow» и «Engineer arrow»
		// различаются только формой, и по одному тексту выбрать нужный
		// было невозможно.
		//
		// Высота строки увеличена под превью: при штатных 28 пикселях
		// картинка вплотную прижимается к границам элемента.
		var start = new SkDropDownBox
		{
			ItemHeight = 32f,
			PreviewBoxSize = 26f,
			DropDownMaxHeight = 340f,
			Bounds = new SKRect(0, 0, 160, 32)
		};
		start.SetItems(LineGlyphLibrary.ToDropDownItems());
		start.SetSelected(shape.Stroke.StartGlyph);

		var end = new SkDropDownBox
		{
			ItemHeight = 32f,
			PreviewBoxSize = 26f,
			DropDownMaxHeight = 340f,
			Bounds = new SKRect(0, 0, 160, 32)
		};
		end.SetItems(LineGlyphLibrary.ToDropDownItems());
		end.SetSelected(shape.Stroke.EndGlyph);

		var addPatternBut = new SkButton { Text = L.T("Add pattern"), Bounds = new SKRect(0, 0, 90, 30) };
		var deletePatternBut = new SkButton { Text = L.T("Delete pattern"), Bounds = new SKRect(0, 0, 90, 30) };

		// ------------------------------------------------------------------------
		// Обработчики
		// ------------------------------------------------------------------------

		enabled.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			bool before = shape.Stroke.Enabled;
			shape.Stroke.Enabled = v;

			var recorded = PushChange(
				"Stroke enabled",
				before,
				v,
				x =>
				{
					shape.Stroke.Enabled = x;
					enabled.Checked = x;
					UpdateRowsVisibility();
					ArrangeUi();
				},
				"Stroke.Enabled");

			// Включение обводки применяется ко всем выделенным без отбора:
			// обводка есть у любой фигуры.
			ApplyStyleToOthers("Stroke enabled", _ => true, s => s.Stroke.Enabled = v, recorded);

			UpdateRowsVisibility();
			ArrangeUi();

			NotifyShapeChanged();
			Invalidate();
		};


		thick.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			float before = shape.Stroke.Thickness;
			shape.Stroke.Thickness = v;

			var recorded = PushChange(
				"Stroke width",
				before,
				v,
				x =>
				{
					shape.Stroke.Thickness = x;
					thick.SetValue(x);
				},
				"Stroke.Thickness");

			// Толщина задаётся всем выделенным, независимо от того,
			// включена ли у них обводка: выключенная обводка — это режим
			// показа, а толщина остаётся её свойством и пригодится,
			// когда обводку включат.
			ApplyStyleToOthers("Stroke width", _ => true, s => s.Stroke.Thickness = v, recorded);

			NotifyShapeChanged();
		};

		strokeType.SelectedIndexChanged = (_, text) =>
		{
			if (suppressUi)
				return;

			var info = StrokePatternLibrary.Find(text);
			if (info == null)
				return;

			string before = shape.Stroke.Pattern;

			shape.Stroke.Pattern = info.Pattern;
			LoadPatternIntoEditor(info.Pattern, info.Length);

			var recorded = PushChange(
				"Stroke type",
				before,
				info.Pattern,
				x =>
				{
					shape.Stroke.Pattern = x;

					var restored = StrokePatternLibrary.FindByPattern(x);
					int len = restored?.Length ?? Math.Max(StrokePatternMinLen, 8);

					LoadPatternIntoEditor(x, len);

					if (restored != null)
						strokeType.SetSelected(restored.Id);
				},
				"Stroke.Pattern");

			ApplyStyleToOthers(
				"Stroke type",
				s => s.Stroke.Enabled,
				s => s.Stroke.Pattern = info.Pattern,
				recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		strokePatternLen.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			int len = (int)Math.Clamp(v, StrokePatternMinLen, StrokePatternMaxLen);

			// Число ячеек — параметр редактора, а не модели: он меняет только то,
			// как паттерн показан в сетке. Поэтому в историю не пишется,
			// но подавление нужно, чтобы заполнение сетки не ушло в модель.
			bool previous = suppressUi;
			suppressUi = true;

			try
			{
				PatternToGridRow(strokePattern, shape.Stroke.Pattern, len, strokePattern.FrontColor, strokePattern.BackColor);
			}
			finally
			{
				suppressUi = previous;
			}

			Invalidate();
		};

		patternLenUoM.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			float before = shape.Stroke.PatternLength;
			shape.Stroke.PatternLength = v;

			var recorded = PushChange(
				"Pattern length",
				before,
				v,
				x =>
				{
					shape.Stroke.PatternLength = x;
					patternLenUoM.SetValue(x);
				},
				"Stroke.PatternLength");

			ApplyStyleToOthers(
				"Pattern length",
				s => s.Stroke.Enabled,
				s => s.Stroke.PatternLength = v,
				recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		strokePattern.PatternChanged = (_, w, _) =>
		{
			// Событие от программного заполнения — не правка пользователя.
			if (suppressUi)
				return;

			string before = shape.Stroke.Pattern;
			string newPattern = GridRowToPattern(strokePattern, strokePattern.FrontColor);

			shape.Stroke.Pattern = newPattern;

			var recorded = PushChange(
				"Edit pattern",
				before,
				newPattern,
				x =>
				{
					shape.Stroke.Pattern = x;

					var restored = StrokePatternLibrary.FindByPattern(x);
					int len = restored?.Length ?? strokePattern.PatternWidth;

					LoadPatternIntoEditor(x, len);
				},
				"Stroke.PatternGrid");

			// Нарисованный пользователем узор получают все выделенные:
			// он задан явно и ни от каких свойств фигуры не зависит.
			ApplyStyleToOthers("Edit pattern", _ => true, s => s.Stroke.Pattern = newPattern, recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		// В модель пишется SelectedValue, а не показанный текст: теперь это
		// разные строки — идентификатор глифа против читаемого названия.
		// От идентификатора зависит и отрисовка, и совместимость со старыми
		// документами.
		start.SelectedIndexChanged = (_, _) =>
		{
			if (suppressUi)
				return;

			string before = shape.Stroke.StartGlyph;
			string after = start.SelectedValue;

			shape.Stroke.StartGlyph = after;
			startSize.Enabled = !string.IsNullOrEmpty(after);

			PushChange(
				"Start glyph",
				before,
				after,
				x =>
				{
					shape.Stroke.StartGlyph = x;
					start.SetSelected(x);
					startSize.Enabled = !string.IsNullOrEmpty(x);
				},
				"Stroke.StartGlyph");

			NotifyShapeChanged();
			Invalidate();
		};

		end.SelectedIndexChanged = (_, _) =>
		{
			if (suppressUi)
				return;

			string before = shape.Stroke.EndGlyph;
			string after = end.SelectedValue;

			shape.Stroke.EndGlyph = after;
			endSize.Enabled = !string.IsNullOrEmpty(after);

			PushChange(
				"End glyph",
				before,
				after,
				x =>
				{
					shape.Stroke.EndGlyph = x;
					end.SetSelected(x);
					endSize.Enabled = !string.IsNullOrEmpty(x);
				},
				"Stroke.EndGlyph");

			NotifyShapeChanged();
			Invalidate();
		};

		startSize.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			float before = shape.Stroke.StartGlyphSize;
			shape.Stroke.StartGlyphSize = v;

			PushChange(
				"Start glyph size",
				before,
				v,
				x =>
				{
					shape.Stroke.StartGlyphSize = x;
					startSize.SetValue(x);
				},
				"Stroke.StartGlyphSize");

			NotifyShapeChanged();
			Invalidate();
		};

		endSize.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			float before = shape.Stroke.EndGlyphSize;
			shape.Stroke.EndGlyphSize = v;

			PushChange(
				"End glyph size",
				before,
				v,
				x =>
				{
					shape.Stroke.EndGlyphSize = x;
					endSize.SetValue(x);
				},
				"Stroke.EndGlyphSize");

			NotifyShapeChanged();
			Invalidate();
		};

		addPatternBut.Click = () =>
		{
			string currentPattern = GridRowToPattern(strokePattern, strokePattern.FrontColor);

			if (StrokePatternLibrary.ContainsPattern(currentPattern))
			{
				var existing = StrokePatternLibrary.FindByPattern(currentPattern);
				if (existing != null)
					strokeType.SetSelected(existing.Id);

				return;
			}

			string before = shape.Stroke.Pattern;

			var added = StrokePatternLibrary.AddCustomPattern(currentPattern, strokePattern.PatternWidth);
			shape.Stroke.Pattern = added.Pattern;

			RefreshStrokeTypeItems();
			strokeType.SetSelected(added.Id);

			// В историю пишется только изменение свойства фигуры. Пополнение
			// библиотеки паттернов отмене не подлежит: это состояние приложения,
			// а не документа, и Undo не должен удалять сохранённый паттерн.
			PushChange(
				"Stroke pattern",
				before,
				added.Pattern,
				x =>
				{
					shape.Stroke.Pattern = x;

					var restored = StrokePatternLibrary.FindByPattern(x);
					int len = restored?.Length ?? strokePattern.PatternWidth;

					LoadPatternIntoEditor(x, len);

					if (restored != null)
						strokeType.SetSelected(restored.Id);
				},
				"Stroke.PatternLibrary");

			NotifyShapeChanged();
			Invalidate();
		};

		deletePatternBut.Click = () =>
		{
			var current = StrokePatternLibrary.FindByPattern(shape.Stroke.Pattern)
				?? StrokePatternLibrary.FindByPattern(GridRowToPattern(strokePattern, strokePattern.FrontColor));

			if (current == null || current.IsBuiltIn)
				return;

			string before = shape.Stroke.Pattern;

			StrokePatternLibrary.RemovePattern(current.Id);
			RefreshStrokeTypeItems();

			var first = StrokePatternLibrary.Items[0];
			shape.Stroke.Pattern = first.Pattern;
			strokeType.SetSelected(first.Id);
			LoadPatternIntoEditor(first.Pattern, first.Length);

			// Удалённый паттерн Undo не вернёт — вернётся только значение
			// у фигуры. Если это неприемлемо, удаление стоит спрашивать
			// подтверждением, а не откатом.
			PushChange(
				"Stroke pattern",
				before,
				first.Pattern,
				x =>
				{
					shape.Stroke.Pattern = x;

					var restored = StrokePatternLibrary.FindByPattern(x);
					int len = restored?.Length ?? strokePattern.PatternWidth;

					LoadPatternIntoEditor(x, len);

					if (restored != null)
						strokeType.SetSelected(restored.Id);
				},
				"Stroke.PatternLibrary");

			NotifyShapeChanged();
			Invalidate();
			RequestCanvasRefresh?.Invoke();
		};

		var patternButtonsRow = new SkFlowLayout
		{
			Direction = SkFlowDirection.Horizontal,
			Gap = 8f,
			AutoHeight = true,
			Bounds = new SKRect(0, 0, 260, 30),
		};
		patternButtonsRow.Add(addPatternBut);
		patternButtonsRow.Add(deletePatternBut);

		// ------------------------------------------------------------------------
		// Компоновка
		// ------------------------------------------------------------------------

		// Подпись несёт единицы страницы: с переходом толщины из миллиметров
		// в единицы документа число в поле стало зависеть от разметки листа,
		// и не назвать единицу здесь означало бы оставить его без смысла.
		string strokeUnit = CurrentPageUnitTitle();

		p.Content.Add(new SkPropertyRow(
			L.T("Thickness") + strokeUnit, thick));
		p.Content.Add(new SkPropertyRow(L.T("Stroke"), strokeType) { Bounds = new SKRect(0, 0, 260, 34) });

		// Заголовок группы: ниже идут четыре строки, которые по отдельности
		// читаются как случайный набор — «Size», «Length», сетка и кнопки.
		// Жирная подпись связывает их в один блок и заодно объясняет,
		// к чему относятся короткие названия.
		// Подпись кладётся прямо в содержимое, а не в SkPropertyRow:
		// строка отвела бы под название левую колонку в 96 пикселей,
		// и заголовок блока встал бы с отступом наравне со значениями,
		// вместо того чтобы начинаться от края.
		p.Content.Add(new SkLabel
		{
			Text = L.T("Pattern"),
			Bold = true,
			FontSize = 14f,
			ForeColor = SkUiTheme.Current.Text
		});

		// Уточнение контекста: в панели Text «Size» — это кегль шрифта,
		// а здесь размер сетки штриха, и по-русски это разные слова.
		p.Content.Add(new SkPropertyRow(L.TIn("pattern", "Size"), strokePatternLen));
		p.Content.Add(new SkPropertyRow(
			L.T("Length") + strokeUnit, patternLenUoM));   // длина цикла в единицах страницы
		p.Content.Add(new SkPropertyRow(string.Empty, strokePattern) { Bounds = new SKRect(0, 0, 260, 34) });
		p.Content.Add(new SkPropertyRow(string.Empty, patternButtonsRow) { Indent = 0f, LabelWidth = 0f, IndentLabel = false });
		p.Content.Add(new SkPropertyRow(L.T("Start"), start) { Bounds = new SKRect(0, 0, 260, 34) });
		p.Content.Add(new SkPropertyRow(L.T("Start size"), startSize));
		p.Content.Add(new SkPropertyRow(L.T("End"), end) { Bounds = new SKRect(0, 0, 260, 34) });
		p.Content.Add(new SkPropertyRow(L.T("End size"), endSize));

		UpdateRowsVisibility();

		p.Arrange();

		return p;
	}


	#endregion

	#region Панель Fill

	private static void InitializeFillWithFirstPattern(FillStyle fill)
	{
		var first = PatternLibrary.Items.FirstOrDefault(x => x.Bitmap != null);
		if (first == null)
			return;

		fill.PatternId = first.Id;
		fill.PatternWidth = first.Bitmap!.Width;
		fill.PatternHeight = first.Bitmap.Height;

		var tempEditor = new SkPatternGridEditor
		{
			FrontColor = fill.PatternFrontColor,
			BackColor = fill.PatternBackColor
		};

		tempEditor.LoadFromBitmap(first.Bitmap, fill.PatternFrontColor, fill.PatternBackColor);
		fill.PatternDefinition = tempEditor.GetDefinition();
	}

	/// <summary>
	/// Панель тени и свечения.
	///
	/// Цвет и мягкость дублируют то, чем управляют ручки на холсте:
	/// ползунок здесь — тот же самый параметр, что и промежуточная полоска
	/// между P1 и P2. Держать их согласованными обязан вызывающий —
	/// операция перетаскивания полоски пересобирает панель.
	/// </summary>
	private SkCollapsePanel BuildShadowPanel(Shape shape)
	{
		shape.Shadow ??= new ShadowEffect();

		var shadow = shape.Shadow;
		var p = CreatePanel("Shadow / Glow");

		// Пока true, обработчики не пишут ни в модель, ни в историю.
		// Ставится на время отката: иначе присваивание значения контролу
		// из Undo породило бы новый шаг истории.
		bool suppressUi = false;

		var undo = UndoStackForShape();

		// Возвращает признак того, что шаг записан: по нему решается,
		// забирать ли его обратно в общую группу истории.
		IUndoableAction? PushChange<T>(string name, T before, T after, Action<T> apply, string key)
		{
			if (suppressUi)
				return null;

			if (EqualityComparer<T>.Default.Equals(before, after))
				return null;

			var step = new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						// finally обязательно: исключение внутри apply иначе
						// навсегда отключило бы панель от модели.
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (shape, key));

			UndoStackForShape()?.Push(step);

			// Возвращается сам шаг, а не признак «записано»: Push мог
			// слить его с предыдущим по совпадению ключа, и тогда
			// последним в истории остаётся ЧУЖОЙ, более ранний шаг.
			return step;
		}

		// ------------------------------------------------------------------------
		// Enabled
		// ------------------------------------------------------------------------

		var enabled = new SkCheckBox { Checked = shadow.Enabled };
		p.Content.Add(new SkPropertyRow("shadow_enabled_row", L.T("Enabled"), enabled));

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "shadow_enabled_row")
					p.Content.Children[i].Visible = shadow.Enabled;
			}
		}

		enabled.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			bool before = shadow.Enabled;
			shadow.Enabled = v;

			// Ручки считаются от габаритов, а до включения их никто не
			// считал. Без этого первая же отрисовка получила бы нулевой
			// вектор размера и тень схлопнулась бы в точку.
			if (v)
				shadow.EnsureDefaults(shape.GetBounds());

			var recorded = PushChange(
				"Shadow enabled",
				before,
				v,
				x =>
				{
					shadow.Enabled = x;
					enabled.Checked = x;
					UpdateRowsVisibility();

					// ArrangeUi, а не p.Arrange: меняется высота панели,
					// и соседние панели надо переставить.
					ArrangeUi();
				},
				"Shadow.Enabled");

			// Тень есть у любой фигуры, отбора не нужно. У тех, у кого
			// её ещё не было, она создаётся по их собственным габаритам:
			// смещение и мягкость по умолчанию считаются от размера.
			ApplyToOthers(
				"Shadow enabled",
				"Shadow.Enabled",
				OtherTargets<Shape>(),
				s => s.Shadow?.Enabled ?? false,
				(s, x) =>
				{
					s.Shadow ??= new ShadowEffect();

					if (x)
						s.Shadow.EnsureDefaults(s.GetBounds());

					s.Shadow.Enabled = x;
				},
				v,
				recorded);

			UpdateRowsVisibility();
			ArrangeUi();

			NotifyShapeChanged();
			Invalidate();
		};

		// ------------------------------------------------------------------------
		// Цвет
		// ------------------------------------------------------------------------

		var color = new SkColorButton
		{
			Text = "",
			Color = shadow.Color
		};

		color.Click = btn =>
		{
			if (suppressUi)
				return;

			var before = shadow.Color;
			shadow.Color = btn.Color;

			var recorded = PushChange(
				"Shadow colour",
				before,
				btn.Color,
				x =>
				{
					shadow.Color = x;
					color.Color = x;
				},
				"Shadow.Color");

			ApplyToOthers(
				"Shadow colour",
				"Shadow.Color",
				OtherTargets<Shape>(),
				s => s.Shadow?.Color ?? SKColors.Transparent,
				(s, x) =>
				{
					s.Shadow ??= new ShadowEffect();
					s.Shadow.Color = x;
				},
				btn.Color,
				recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		p.Content.Add(new SkPropertyRow("shadow_color_row", L.T("Colour"), color));

		// ------------------------------------------------------------------------
		// Мягкость
		// ------------------------------------------------------------------------

		var softness = new SkSlideBar
		{
			Minimum = 0f,
			Maximum = 1f,
			SmallChange = 0.01f,
			Decimals = 2,
			ShowValueLabel = true
		};

		softness.SetValue(Math.Clamp(shadow.Softness, 0f, 1f), raiseChanged: false);

		// Значение до начала перетаскивания. Ползунок шлёт событие на
		// каждое движение, и брать «до» из самого события нельзя — там уже
		// новое. Слияние по ключу схлопнет серию в один шаг истории.
		float softnessBefore = shadow.Softness;

		softness.ValueChanged += v =>
		{
			if (suppressUi)
				return;

			float before = softnessBefore;
			shadow.Softness = v;
			softnessBefore = v;

			var recorded = PushChange(
				"Shadow softness",
				before,
				v,
				x =>
				{
					shadow.Softness = x;
					softnessBefore = x;
					softness.SetValue(x, raiseChanged: false);
				},
				"Shadow.Softness");

			ApplyToOthers(
				"Shadow softness",
				"Shadow.Softness",
				OtherTargets<Shape>(),
				s => s.Shadow?.Softness ?? 0f,
				(s, x) =>
				{
					s.Shadow ??= new ShadowEffect();
					s.Shadow.Softness = x;
				},
				v,
				recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		p.Content.Add(new SkPropertyRow("shadow_softness_row", L.T("Softness"), softness));

		UpdateRowsVisibility();

		return p;
	}

	/// <summary>
	/// Панель плавной прозрачности.
	///
	/// Положение перехода задают ручки на холсте, здесь — только крайние
	/// значения непрозрачности. Пара ползунков, а не один: переход бывает
	/// нужен и от полупрозрачного к прозрачному, и в обратную сторону.
	/// </summary>
	private SkCollapsePanel BuildTransparencyPanel(Shape shape)
	{
		shape.Transparency ??= new TransparencyEffect();

		var tr = shape.Transparency;
		var p = CreatePanel("Transparency");

		bool suppressUi = false;

		var undo = UndoStackForShape();

		// Возвращает признак того, что шаг записан: по нему решается,
		// забирать ли его обратно в общую группу истории.
		IUndoableAction? PushChange<T>(string name, T before, T after, Action<T> apply, string key)
		{
			if (suppressUi)
				return null;

			if (EqualityComparer<T>.Default.Equals(before, after))
				return null;

			var step = new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (shape, key));

			UndoStackForShape()?.Push(step);

			// Возвращается сам шаг, а не признак «записано»: Push мог
			// слить его с предыдущим по совпадению ключа, и тогда
			// последним в истории остаётся ЧУЖОЙ, более ранний шаг.
			return step;
		}

		// ------------------------------------------------------------------------
		// Enabled
		// ------------------------------------------------------------------------

		var enabled = new SkCheckBox { Checked = tr.Enabled };
		p.Content.Add(new SkPropertyRow("transparency_enabled_row", L.T("Enabled"), enabled));

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "transparency_enabled_row")
					p.Content.Children[i].Visible = tr.Enabled;
			}
		}

		enabled.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			bool before = tr.Enabled;
			tr.Enabled = v;

			// Ручки считаются от габаритов, а до включения их никто не считал.
			if (v)
				tr.EnsureDefaults(shape.GetBounds());

			var recorded = PushChange(
				"Transparency enabled",
				before,
				v,
				x =>
				{
					tr.Enabled = x;
					enabled.Checked = x;
					UpdateRowsVisibility();

					// ArrangeUi, а не p.Arrange: меняется высота панели,
					// и соседние панели надо переставить.
					ArrangeUi();
				},
				"Transparency.Enabled");

			// Прозрачность есть у любой фигуры. У тех, у кого её ещё
			// не было, ручки считаются по их собственным габаритам.
			ApplyToOthers(
				"Transparency enabled",
				"Transparency.Enabled",
				OtherTargets<Shape>(),
				s => s.Transparency?.Enabled ?? false,
				(s, x) =>
				{
					s.Transparency ??= new TransparencyEffect();

					if (x)
						s.Transparency.EnsureDefaults(s.GetBounds());

					s.Transparency.Enabled = x;
				},
				v,
				recorded);

			UpdateRowsVisibility();
			ArrangeUi();

			NotifyShapeChanged();
			Invalidate();
		};

		// ------------------------------------------------------------------------
		// Крайние значения непрозрачности
		// ------------------------------------------------------------------------

		// Геттер и сеттер принимают TransparencyEffect, а не замыкаются
		// на эффекте основной фигуры: тот же сеттер нужно применить
		// и к остальным выделенным.
		void AddOpacitySlider(
			string rowName,
			string label,
			string historyName,
			string key,
			Func<TransparencyEffect, float> get,
			Action<TransparencyEffect, float> set)
		{
			var slider = new SkSlideBar
			{
				Minimum = 0f,
				Maximum = 1f,
				SmallChange = 0.01f,
				Decimals = 2,
				ShowValueLabel = true
			};

			slider.SetValue(Math.Clamp(get(tr), 0f, 1f), raiseChanged: false);

			// Значение до начала перетаскивания: ползунок шлёт событие на
			// каждое движение, и брать «до» из самого события нельзя — там
			// уже новое. Слияние по ключу схлопнет серию в один шаг истории.
			float before = get(tr);

			slider.ValueChanged += v =>
			{
				if (suppressUi)
					return;

				float wasValue = before;
				set(tr, v);
				before = v;

				var recorded = PushChange(
					historyName,
					wasValue,
					v,
					x =>
					{
						set(tr, x);
						before = x;
						slider.SetValue(x, raiseChanged: false);
					},
					key);

				ApplyToOthers(
					historyName,
					key,
					OtherTargets<Shape>(),
					s => s.Transparency == null ? 1f : get(s.Transparency),
					(s, x) =>
					{
						s.Transparency ??= new TransparencyEffect();
						set(s.Transparency, x);
					},
					v,
					recorded);

				NotifyShapeChanged();
				Invalidate();
			};

			p.Content.Add(new SkPropertyRow(rowName, L.T(label), slider));
		}

		AddOpacitySlider(
			"transparency_start_row",
			"From (P1)",
			"Transparency start",
			"Transparency.StartOpacity",
			e => e.StartOpacity,
			(e, x) => e.StartOpacity = x);

		AddOpacitySlider(
			"transparency_end_row",
			"To (P2)",
			"Transparency end",
			"Transparency.EndOpacity",
			e => e.EndOpacity,
			(e, x) => e.EndOpacity = x);

		UpdateRowsVisibility();

		return p;
	}

	/// <summary>
	/// Панель инструмента преобразования.
	///
	/// Ползунок повторяет промежуточную полоску между P1 и P2 на холсте:
	/// это один и тот же параметр, и менять его удобно как мышью на виде,
	/// так и точным значением здесь.
	/// </summary>
	private SkCollapsePanel BuildTransformPanel(TransformShape tool)
	{
		var p = CreatePanel("Transform");

		bool suppressUi = false;

		var undo = UndoStackForShape();

		void PushChange<T>(string name, T before, T after, Action<T> apply, string key)
		{
			if (suppressUi)
				return;

			if (EqualityComparer<T>.Default.Equals(before, after))
				return;

			undo?.Push(new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (tool, key)));
		}

		// ------------------------------------------------------------------------
		// Число шагов
		// ------------------------------------------------------------------------

		var steps = new SkNumericUpDown
		{
			Minimum = 1,
			Maximum = TransformShape.MaxSteps,
			Increment = 1,
			Decimals = 0,
			AllowDecimal = false,
			AllowNegative = false
		};

		steps.SetValue(tool.Steps);

		int stepsBefore = tool.Steps;

		steps.ValueChanged = v =>
		{
			if (suppressUi)
				return;

			int after = (int)MathF.Round(v);
			int before = stepsBefore;

			if (after == before)
				return;

			tool.Steps = after;
			stepsBefore = after;

			PushChange(
				"Transform steps",
				before,
				after,
				x =>
				{
					tool.Steps = x;
					stepsBefore = x;
					steps.SetValue(x);
				},
				"Transform.Steps");

			NotifyShapeChanged();
			Invalidate();
		};

		p.Content.Add(new SkPropertyRow("transform_steps_row", L.T("Steps"), steps));

		// ------------------------------------------------------------------------
		// Распределение расстояний между шагами
		// ------------------------------------------------------------------------

		var bias = new SkSlideBar
		{
			Minimum = 0f,
			Maximum = 1f,
			SmallChange = 0.01f,
			Decimals = 2,
			ShowValueLabel = true
		};

		bias.SetValue(Math.Clamp(tool.Bias, 0f, 1f), raiseChanged: false);

		// Значение до начала перетаскивания: ползунок шлёт событие на
		// каждое движение, и брать «до» из самого события нельзя — там уже
		// новое. Слияние по ключу схлопнет серию в один шаг истории.
		float biasBefore = tool.Bias;

		bias.ValueChanged += v =>
		{
			if (suppressUi)
				return;

			float before = biasBefore;
			tool.Bias = v;
			biasBefore = v;

			PushChange(
				"Transform spacing",
				before,
				v,
				x =>
				{
					tool.Bias = x;
					biasBefore = x;
					bias.SetValue(x, raiseChanged: false);
				},
				"Transform.Bias");

			NotifyShapeChanged();
			Invalidate();
		};

		p.Content.Add(new SkPropertyRow("transform_bias_row", L.T("Spacing"), bias));

		// ------------------------------------------------------------------------
		// Привязанные фигуры
		// ------------------------------------------------------------------------

		// Только для сведения: пересобрать привязку можно, создав
		// инструмент заново, а перевыбор одной из фигур на месте потребовал
		// бы отдельного режима указания и своего шага истории.
		p.Content.Add(new SkPropertyRow("transform_from_row", L.T("From"), new SkLabel
			{
				Text = tool.ShapeA?.Name ?? "-",
				Bounds = new SKRect(0, 0, 150, 20)
			}));

		p.Content.Add(new SkPropertyRow("transform_to_row", L.T("To"), new SkLabel
			{
				Text = tool.ShapeB?.Name ?? "-",
				Bounds = new SKRect(0, 0, 150, 20)
			}));

		return p;
	}

	private SkCollapsePanel BuildFillPanel(Shape shape)
	{
		shape.Fill ??= new FillStyle();

		var fill = shape.Fill;
		var p = CreatePanel("Fill");

		// ------------------------------------------------------------------------
		// Машинерия истории
		// ------------------------------------------------------------------------

		// Пока true, обработчики не пишут ни в модель, ни в историю. Ставится
		// при программном заполнении редактора паттерна и при откате истории.
		bool suppressUi = false;

		var undo = UndoStackForShape();

		// Возвращает признак того, что шаг записан: по нему FanOut решает,
		// забирать ли его обратно в общую группу истории.
		IUndoableAction? PushChange<T>(string name, T before, T after, Action<T> apply, string key, bool checkEquality = true)
		{
			if (suppressUi)
				return null;

			if (checkEquality && EqualityComparer<T>.Default.Equals(before, after))
				return null;

			var step = new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (shape, key));

			UndoStackForShape()?.Push(step);

			// Возвращается сам шаг, а не признак «записано»: Push мог
			// слить его с предыдущим по совпадению ключа, и тогда
			// последним в истории остаётся ЧУЖОЙ, более ранний шаг.
			return step;
		}

		// ------------------------------------------------------------------------
		// Enabled
		// ------------------------------------------------------------------------

		// Повторяет правку заливки на остальных выделенных фигурах.
		// allows — правило «у кого этот параметр есть»: цвет сплошной
		// заливки достаётся только тем, у кого заливка сплошная,
		// а остановки градиента — только градиентным.
		static bool IsGradient(FillStyle f)
			=> f.Kind is FillKind.LinearGradient
				or FillKind.RadialGradient
				or FillKind.RectangularGradient;

		void FanOut(string title, Func<FillStyle, bool> allows, Action<FillStyle> apply, IUndoableAction? recorded)
			=> ApplyStyleToOthers(
				title,
				s => allows(s.Fill),
				s => apply(s.Fill),
				recorded);

		var enabled = new SkCheckBox { Checked = fill.Enabled };
		SkPropertyRow enabledRow = new SkPropertyRow("enabled_row", L.T("Enabled"), enabled);

		p.Content.Add(enabledRow);

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "enabled_row")
					p.Content.Children[i].Visible = fill.Enabled;
			}
		}

		enabled.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			bool before = fill.Enabled;
			fill.Enabled = v;

			var recorded = PushChange(
				"Fill enabled",
				before,
				v,
				x =>
				{
					fill.Enabled = x;
					enabled.Checked = x;
					UpdateRowsVisibility();

					// ArrangeUi, а не p.Arrange: меняется высота панели,
					// и соседние панели надо переставить.
					ArrangeUi();
				},
				"Fill.Enabled");

			// Заливка есть у любой фигуры, отбора не нужно.
			FanOut("Fill enabled", _ => true, f => f.Enabled = v, recorded);

			UpdateRowsVisibility();

			// Пересчёт раскладки обязателен. SkFlowLayout.Arrange расставляет
			// только видимые элементы, поэтому у скрытых строк Bounds остаются
			// от прошлого прохода. Без Arrange включённые строки лежат друг на
			// друге за границами панели и обрезаются при отрисовке.
			ArrangeUi();

			NotifyShapeChanged();
			Invalidate();
		};


		// ------------------------------------------------------------------------
		// Тип заливки
		// ------------------------------------------------------------------------

		var fillTypeButtons = BuildFillTypeButtons(fill);
		p.Content.Add(new SkPropertyRow("fill_type_row", L.T("Type"), fillTypeButtons) { Bounds = new SKRect(0, 0, 260, 38) });

		// ------------------------------------------------------------------------
		// Правило заполнения (только для кривых)
		// ------------------------------------------------------------------------

		if (shape is CurveShape cs)
		{
			var fillGaps = new SkCheckBox
			{
				Text = "Fill intersection gaps",
				Checked = cs.FillType == SKPathFillType.Winding
			};

			fillGaps.CheckedChanged += v =>
			{
				if (suppressUi)
					return;

				var before = cs.FillType;
				var after = v ? SKPathFillType.Winding : SKPathFillType.EvenOdd;
				cs.FillType = after;

				PushChange(
					"Fill rule",
					before,
					after,
					x =>
					{
						cs.FillType = x;
						fillGaps.Checked = x == SKPathFillType.Winding;
					},
					"Curve.FillType");

				// Правило заполнения есть только у кривых: у прямоугольника
				// или окружности пересечений контура не бывает.
				ApplyGeometryToSelection<CurveShape>("Fill rule", c => c.FillType = after);

				NotifyShapeChanged();
				Invalidate();
			};

			p.Content.Add(new SkPropertyRow(string.Empty, fillGaps));
		}

		// ------------------------------------------------------------------------
		// Общие переключатели: вынесены в фабрику, потому что три из четырёх
		// повторяются в ветках Pattern, Texture и градиентов дословно.
		// ------------------------------------------------------------------------

		// Геттер и сеттер принимают FillStyle, а не работают с замыканием
		// на заливке основной фигуры: тот же самый сеттер нужно применить
		// и к остальным выделенным. Через эту фабрику проходят все
		// переключатели заливки разом — «Tiling», «Rotate with shape»,
		// «Scale with shape», «Smooth texture».
		SkCheckBox MakeToggle(
			string text,
			string historyName,
			string key,
			Func<FillStyle, bool> get,
			Action<FillStyle, bool> set)
		{
			var box = new SkCheckBox { Text = text, Checked = get(fill) };

			box.CheckedChanged += v =>
			{
				if (suppressUi)
					return;

				bool before = get(fill);
				set(fill, v);

				var recorded = PushChange(
					historyName,
					before,
					v,
					x =>
					{
						set(fill, x);
						box.Checked = x;
					},
					key);

				// Переключатель имеет смысл только там, где заливка того же
				// рода: «Tiling» у сплошной заливки ничего не значит.
				FanOut(historyName, f => f.Kind == fill.Kind, f => set(f, v), recorded);

				NotifyShapeChanged();
				Invalidate();
			};

			return box;
		}

		switch (fill.Kind)
		{
			case FillKind.Solid:
				{
					var color = new SkColorButton
					{
						Text = "",
						Color = fill.SolidColor
					};

					color.Click = btn =>
					{
						if (suppressUi)
							return;

						var before = fill.SolidColor;
						fill.SolidColor = btn.Color;

						var recorded = PushChange(
							"Fill colour",
							before,
							btn.Color,
							x =>
							{
								fill.SolidColor = x;
								color.Color = x;
							},
							"Fill.SolidColor");

						// Только фигурам со сплошной заливкой: у градиентной
						// или узорной этот цвет не виден, и тихая правка
						// невидимого свойства всплыла бы потом неожиданностью.
						FanOut(
							"Fill colour",
							f => f.Kind == FillKind.Solid,
							f => f.SolidColor = btn.Color,
							recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					p.Content.Add(new SkPropertyRow("fill_color_row", L.T("Color"), color));
					break;
				}

			case FillKind.LinearGradient:
			case FillKind.RadialGradient:
			case FillKind.RectangularGradient:
				{
					var stopInfos = fill.Stops
						.Select(s => new GradientStopInfo(s.Offset, s.Color))
						.ToList();

					var color = new SkColorButton
					{
						Text = "Stop color",
						Color = stopInfos.Count > 0 ? stopInfos[0].Color : SKColors.White
					};

					var grad = new SkGradientEditor();
					grad.SetStops(stopInfos);

					// Снимок точек градиента. Список изменяемый, поэтому в историю
					// кладутся копии: иначе Undo вернул бы ссылку на тот же список,
					// который уже изменён.
					List<GradientStop> SnapshotStops()
						=> fill.Stops.Select(s => new GradientStop(s.Offset, s.Color)).ToList();

					void ApplyStops(List<GradientStop> src)
					{
						fill.Stops.Clear();
						fill.Stops.AddRange(src.Select(s => new GradientStop(s.Offset, s.Color)));

						grad.SetStops(src.Select(s => new GradientStopInfo(s.Offset, s.Color)));

						if (grad.Stops.Count > 0)
							color.Color = grad.Stops[Math.Clamp(grad.SelectedIndex, 0, grad.Stops.Count - 1)].Color;
					}

					grad.SelectedStopChanged = (idx, stop) =>
					{
						color.Color = stop.Color;
						Invalidate();
					};

					grad.GradientChanged = () =>
					{
						if (suppressUi)
							return;

						var before = SnapshotStops();

						fill.Stops.Clear();
						fill.Stops.AddRange(grad.Stops.Select(s => new GradientStop(s.Offset, s.Color)));

						var after = SnapshotStops();

						// checkEquality отключён: сравнение списков по ссылке
						// всегда дало бы false, а поэлементное здесь избыточно —
						// mergeKey всё равно склеит серию перетаскивания в один шаг.
						var recorded = PushChange(
							"Gradient stops",
							before,
							after,
							ApplyStops,
							"Fill.Stops",
							checkEquality: false);

						// Только градиентным заливкам: остальным набор точек
						// присвоить можно, но виден он не будет.
						FanOut(
							"Gradient stops",
							IsGradient,
							f =>
							{
								f.Stops.Clear();
								f.Stops.AddRange(after.Select(s => new GradientStop(s.Offset, s.Color)));
							},
							recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					color.Click = btn =>
					{
						if (suppressUi || grad.Stops.Count == 0)
							return;

						var before = SnapshotStops();

						grad.SetSelectedStopColor(btn.Color);

						fill.Stops.Clear();
						fill.Stops.AddRange(grad.Stops.Select(s => new GradientStop(s.Offset, s.Color)));

						var after = SnapshotStops();

						var recorded = PushChange(
							"Gradient stop colour",
							before,
							after,
							ApplyStops,
							"Fill.Stops",
							checkEquality: false);

						FanOut(
							"Gradient stop colour",
							IsGradient,
							f =>
							{
								f.Stops.Clear();
								f.Stops.AddRange(after.Select(s => new GradientStop(s.Offset, s.Color)));
							},
							recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					var rotateWithShape = MakeToggle(
						"Rotate with shape", "Rotate fill with shape", "Fill.RotateWithShape",
						f => f.RotateWithShape, (f, x) => f.RotateWithShape = x);

					var scaleWithShape = MakeToggle(
						"Scale with shape", "Scale fill with shape", "Fill.ScaleWithShape",
						f => f.ScaleWithShape, (f, x) => f.ScaleWithShape = x);

					p.Content.Add(new SkPropertyRow(L.T("Color"), color));
					p.Content.Add(new SkPropertyRow(L.T("Stops"), grad) { Bounds = new SKRect(0, 0, 260, 42) });
					p.Content.Add(new SkPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new SkPropertyRow(L.T("Scale"), scaleWithShape));
					break;
				}

			case FillKind.Pattern:
				{
					var patternDrop = new SkDropDownBox();
					patternDrop.SetItems(
						PatternLibrary.Items.Select(x => new SkDropDownItem(
							x.Id,
							string.Empty,
							x.Bitmap)));

					var editor = new SkPatternGridEditor
					{
						Bounds = new SKRect(0, 0, 260, 140),
						FrontColor = fill.PatternFrontColor,
						BackColor = fill.PatternBackColor
					};

					// Все программные обращения к редактору идут через эту обёртку.
					// Без подавления PatternChanged запишет в модель промежуточное
					// состояние сетки — так же, как это ломало обводку.
					void EditorAction(Action action)
					{
						bool previous = suppressUi;
						suppressUi = true;

						try
						{
							action();
						}
						finally
						{
							suppressUi = previous;
						}
					}

					EditorAction(() =>
					{
						if (!string.IsNullOrWhiteSpace(fill.PatternDefinition))
						{
							editor.LoadFromDefinition(
								fill.PatternDefinition,
								fill.PatternWidth,
								fill.PatternHeight,
								fill.PatternBackColor);
						}
						else
						{
							editor.SetSize(fill.PatternWidth, fill.PatternHeight, fill.PatternBackColor);
							editor.Clear(fill.PatternBackColor);
						}
					});

					// Снимок всех связанных полей паттерна. Они меняются группой,
					// и откатывать их по отдельности бессмысленно: определение
					// без своих размеров не имеет смысла.
					(string Id, string Definition, int W, int H) SnapshotPattern()
						=> (fill.PatternId, fill.PatternDefinition, fill.PatternWidth, fill.PatternHeight);

					// Узор копируется остальным ЦЕЛИКОМ, а не по одному полю.
					// Описание решётки хранит цвет каждой клетки готовым
					// значением и рассчитано на свой размер: перенести одну
					// ширину или один цвет — значит получить у соседней фигуры
					// решётку, не соответствующую собственному описанию.
					void FanOutPattern(string title, string key, IUndoableAction? recorded)
						=> FanOut(
							title,
							f => f.Kind == FillKind.Pattern,
							f =>
							{
								f.PatternId = fill.PatternId;
								f.PatternDefinition = fill.PatternDefinition;
								f.PatternWidth = fill.PatternWidth;
								f.PatternHeight = fill.PatternHeight;
								f.PatternFrontColor = fill.PatternFrontColor;
								f.PatternBackColor = fill.PatternBackColor;
							},
							recorded);

					void ApplyPattern((string Id, string Definition, int W, int H) s)
					{
						fill.PatternId = s.Id;
						fill.PatternDefinition = s.Definition;
						fill.PatternWidth = s.W;
						fill.PatternHeight = s.H;

						patternDrop.SetSelected(s.Id);

						EditorAction(() =>
							editor.LoadFromDefinition(s.Definition, s.W, s.H, fill.PatternBackColor));
					}

					editor.PatternChanged = (definition, w, h) =>
					{
						if (suppressUi)
							return;

						var before = SnapshotPattern();

						fill.PatternDefinition = definition;
						fill.PatternWidth = w;
						fill.PatternHeight = h;

						var recorded = PushChange(
							"Edit pattern",
							before,
							SnapshotPattern(),
							ApplyPattern,
							"Fill.PatternGrid");

						FanOutPattern("Edit pattern", "Fill.PatternGrid", recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					patternDrop.SetSelected(fill.PatternId);
					patternDrop.SelectedIndexChanged = (_, text) =>
					{
						if (suppressUi)
							return;

						var before = SnapshotPattern();

						fill.PatternId = text;

						var info = PatternLibrary.Find(text);
						if (info?.Bitmap != null)
						{
							fill.PatternWidth = info.Bitmap.Width;
							fill.PatternHeight = info.Bitmap.Height;

							EditorAction(() => editor.LoadFromBitmap(
								info.Bitmap,
								fill.PatternFrontColor,
								fill.PatternBackColor));

							fill.PatternDefinition = editor.GetDefinition();
						}

						var recorded = PushChange(
							"Pattern",
							before,
							SnapshotPattern(),
							ApplyPattern,
							"Fill.PatternId");

						FanOutPattern("Pattern", "Fill.PatternId", recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					var fw = new SkNumericUpDown { Minimum = 2, Maximum = 256, Increment = 1, Decimals = 0 };
					fw.SetValue(fill.PatternWidth);

					var fh = new SkNumericUpDown { Minimum = 2, Maximum = 256, Increment = 1, Decimals = 0 };
					fh.SetValue(fill.PatternHeight);

					void ResizePattern(int newWidth, int newHeight, string historyName)
					{
						if (suppressUi)
							return;

						var before = SnapshotPattern();

						fill.PatternWidth = newWidth;
						fill.PatternHeight = newHeight;

						EditorAction(() =>
						{
							editor.SetSize(newWidth, newHeight, fill.PatternBackColor);
							editor.LoadFromDefinition(before.Definition, newWidth, newHeight, fill.PatternBackColor);
						});

						// Определение перечитывается из редактора: старое описывало
						// решётку прежнего размера, и при смене размера раскладка
						// сдвигается. Прежний код оставлял в модели старую строку.
						fill.PatternDefinition = editor.GetDefinition();

						var recorded = PushChange(
							historyName,
							before,
							SnapshotPattern(),
							x =>
							{
								ApplyPattern(x);
								fw.SetValue(x.W);
								fh.SetValue(x.H);
							},
							"Fill.PatternSize");

						FanOutPattern(historyName, "Fill.PatternSize", recorded);

						NotifyShapeChanged();
						Invalidate();
					}

					fw.ValueChanged = v => ResizePattern((int)v, fill.PatternHeight, "Pattern width");
					fh.ValueChanged = v => ResizePattern(fill.PatternWidth, (int)v, "Pattern height");

					var front = new SkColorButton { Text = "Front", Color = fill.PatternFrontColor };
					var back = new SkColorButton { Text = "Back", Color = fill.PatternBackColor };

					front.Click = btn =>
					{
						if (suppressUi)
							return;

						var before = fill.PatternFrontColor;

						fill.PatternFrontColor = btn.Color;
						editor.FrontColor = btn.Color;

						EditorAction(() => editor.LoadFromDefinition(
							fill.PatternDefinition, fill.PatternWidth, fill.PatternHeight, fill.PatternBackColor));

						var recorded = PushChange(
							"Pattern front colour",
							before,
							btn.Color,
							x =>
							{
								fill.PatternFrontColor = x;
								front.Color = x;
								editor.FrontColor = x;

								EditorAction(() => editor.LoadFromDefinition(
									fill.PatternDefinition, fill.PatternWidth, fill.PatternHeight, fill.PatternBackColor));
							},
							"Fill.PatternFrontColor");

						FanOutPattern("Pattern front colour", "Fill.PatternFrontColor", recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					back.Click = btn =>
					{
						if (suppressUi)
							return;

						var before = fill.PatternBackColor;

						fill.PatternBackColor = btn.Color;
						editor.BackColor = btn.Color;

						EditorAction(() => editor.LoadFromDefinition(
							fill.PatternDefinition, fill.PatternWidth, fill.PatternHeight, fill.PatternBackColor));

						var recorded = PushChange(
							"Pattern back colour",
							before,
							btn.Color,
							x =>
							{
								fill.PatternBackColor = x;
								back.Color = x;
								editor.BackColor = x;

								EditorAction(() => editor.LoadFromDefinition(
									fill.PatternDefinition, fill.PatternWidth, fill.PatternHeight, fill.PatternBackColor));
							},
							"Fill.PatternBackColor");

						FanOutPattern("Pattern back colour", "Fill.PatternBackColor", recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					var save = new SkButton { Text = "Save" };
					save.Click = () =>
					{
						var before = SnapshotPattern();

						fill.PatternDefinition = editor.GetDefinition();
						fill.PatternWidth = editor.PatternWidth;
						fill.PatternHeight = editor.PatternHeight;

						var recorded = PushChange(
							"Save pattern",
							before,
							SnapshotPattern(),
							ApplyPattern,
							"Fill.PatternSave");

						FanOutPattern("Save pattern", "Fill.PatternSave", recorded);

						if (string.IsNullOrWhiteSpace(fill.PatternId))
						{
							NotifyShapeChanged();
							Invalidate();
							return;
						}

						var info = PatternLibrary.Find(fill.PatternId);
						if (info != null)
						{
							// Прежний битмап освобождается: без этого каждое
							// сохранение паттерна оставляло утечку.
							var oldBitmap = info.Bitmap;
							info.Bitmap = editor.BuildBitmap();
							oldBitmap?.Dispose();
						}

						NotifyShapeChanged();
						Invalidate();
					};

					var clear = new SkButton { Text = "Clear" };
					clear.Click = () =>
					{
						var before = SnapshotPattern();

						EditorAction(() => editor.Clear(fill.PatternBackColor));

						fill.PatternDefinition = editor.GetDefinition();
						fill.PatternWidth = editor.PatternWidth;
						fill.PatternHeight = editor.PatternHeight;

						PushChange(
							"Clear pattern",
							before,
							SnapshotPattern(),
							ApplyPattern,
							"Fill.PatternClear");

						NotifyShapeChanged();
						Invalidate();
					};

					var buttons = new SkFlowLayout
					{
						Direction = SkFlowDirection.Horizontal,
						Gap = 8f,
						AutoHeight = true,
						Bounds = new SKRect(0, 0, 260, 30)
					};

					save.Bounds = new SKRect(0, 0, 80, 30);
					clear.Bounds = new SKRect(0, 0, 80, 30);
					buttons.Add(save);
					buttons.Add(clear);

					var tiling = MakeToggle(
						"Tiling", "Fill tiling", "Fill.Tiling",
						f => f.Tiling, (f, x) => f.Tiling = x);

					var rotateWithShape = MakeToggle(
						"Rotate with shape", "Rotate fill with shape", "Fill.RotateWithShape",
						f => f.RotateWithShape, (f, x) => f.RotateWithShape = x);

					var scaleWithShape = MakeToggle(
						"Scale with shape", "Scale fill with shape", "Fill.ScaleWithShape",
						f => f.ScaleWithShape, (f, x) => f.ScaleWithShape = x);

					var smoothTexture = MakeToggle(
						"Smooth texture", "Smooth fill", "Fill.SmoothTexture",
						f => f.SmoothTexture, (f, x) => f.SmoothTexture = x);

					p.Content.Add(new SkPropertyRow(L.T("Pattern"), patternDrop));
					p.Content.Add(new SkPropertyRow(L.T("Width"), fw));
					p.Content.Add(new SkPropertyRow(L.T("Height"), fh));
					p.Content.Add(new SkPropertyRow(L.T("Front"), front));
					p.Content.Add(new SkPropertyRow(L.T("Back"), back));
					p.Content.Add(new SkPropertyRow(L.T("Edit"), editor) { Bounds = new SKRect(0, 0, 260, 140) });
					p.Content.Add(new SkPropertyRow(string.Empty, buttons));
					p.Content.Add(new SkPropertyRow(L.T("Tiling"), tiling));
					p.Content.Add(new SkPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new SkPropertyRow(L.T("Scale"), scaleWithShape));
					p.Content.Add(new SkPropertyRow(L.T("Smooth"), smoothTexture));

					break;
				}

			case FillKind.Texture:
				{
					var texDrop = new SkDropDownBox();
					texDrop.SetItems(
						TextureLibrary.Items.Select(x => new SkDropDownItem(
							x.Id,
							string.Empty,
							x.Bitmap)));

					// Идентификатор, битмап и путь меняются вместе — снимок группой.
					(string Id, SKBitmap? Bitmap, string Path) SnapshotTexture()
						=> (fill.TextureId, fill.TextureBitmap, fill.TextureFilePath);

					// Текстура тоже переносится группой: идентификатор без
					// картинки и пути оставил бы соседнюю фигуру со ссылкой
					// в никуда.
					void FanOutTexture(string title, string key, IUndoableAction? recorded)
						=> FanOut(
							title,
							f => f.Kind == FillKind.Texture,
							f =>
							{
								f.TextureId = fill.TextureId;
								f.TextureBitmap = fill.TextureBitmap;
								f.TextureFilePath = fill.TextureFilePath;
							},
							recorded);

					void ApplyTexture((string Id, SKBitmap? Bitmap, string Path) s)
					{
						fill.TextureId = s.Id;
						fill.TextureBitmap = s.Bitmap;
						fill.TextureFilePath = s.Path;

						texDrop.SetSelected(s.Id);
					}

					texDrop.SetSelected(fill.TextureId);
					texDrop.SelectedIndexChanged = (_, text) =>
					{
						if (suppressUi)
							return;

						var before = SnapshotTexture();

						fill.TextureId = text;

						var t = TextureLibrary.Find(text);
						fill.TextureBitmap = t?.Bitmap;
						fill.TextureFilePath = t?.FileName ?? string.Empty;

						var recorded = PushChange(
							"Texture",
							before,
							SnapshotTexture(),
							ApplyTexture,
							"Fill.TextureId");

						FanOutTexture("Texture", "Fill.TextureId", recorded);

						NotifyShapeChanged();
						Invalidate();
					};

					var load = new SkButton { Text = "Load texture" };
					load.Click = () =>
					{
						using var ofd = new OpenFileDialog();
						ofd.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp";

						if (ofd.ShowDialog() != DialogResult.OK)
							return;

						var before = SnapshotTexture();

						string id = TextureLibrary.ImportTexture(ofd.FileName);
						var t = TextureLibrary.Find(id);

						fill.TextureId = id;
						fill.TextureBitmap = t?.Bitmap;
						fill.TextureFilePath = t?.FileName ?? string.Empty;

						// В историю идёт только выбор текстуры у фигуры. Импорт
						// файла в Resources\Textures отмене не подлежит: это
						// состояние приложения, а не документа.
						var recorded = PushChange(
							"Texture",
							before,
							SnapshotTexture(),
							ApplyTexture,
							"Fill.TextureId");

						FanOutTexture("Texture", "Fill.TextureId", recorded);

						RebuildForSelection();
						NotifyShapeChanged();
					};

					var buttons = new SkFlowLayout
					{
						Direction = SkFlowDirection.Horizontal,
						Gap = 8f,
						AutoHeight = true,
						Bounds = new SKRect(0, 0, 260, 30)
					};

					load.Bounds = new SKRect(0, 0, 120, 30);
					buttons.Add(load);

					var tiling = MakeToggle(
						"Tiling", "Fill tiling", "Fill.Tiling",
						f => f.Tiling, (f, x) => f.Tiling = x);

					var rotateWithShape = MakeToggle(
						"Rotate with shape", "Rotate fill with shape", "Fill.RotateWithShape",
						f => f.RotateWithShape, (f, x) => f.RotateWithShape = x);

					var scaleWithShape = MakeToggle(
						"Scale with shape", "Scale fill with shape", "Fill.ScaleWithShape",
						f => f.ScaleWithShape, (f, x) => f.ScaleWithShape = x);

					var smoothTexture = MakeToggle(
						"Smooth texture", "Smooth fill", "Fill.SmoothTexture",
						f => f.SmoothTexture, (f, x) => f.SmoothTexture = x);

					p.Content.Add(new SkPropertyRow(L.T("Texture"), texDrop));
					p.Content.Add(new SkPropertyRow(string.Empty, buttons));
					p.Content.Add(new SkPropertyRow(L.T("Tiling"), tiling));
					p.Content.Add(new SkPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new SkPropertyRow(L.T("Scale"), scaleWithShape));
					p.Content.Add(new SkPropertyRow(L.T("Smooth"), smoothTexture));

					break;
				}
		}

		UpdateRowsVisibility();

		p.Arrange();
		return p;
	}

	#endregion

	#region Панель Text

	private SkCollapsePanel BuildTextPanel(TextShape shape)
	{
		var p = CreatePanel("Text");

		// ------------------------------------------------------------------------
		// Машинерия истории
		// ------------------------------------------------------------------------

		// Пока true, обработчики не пишут ни в модель, ни в историю. Ставится
		// на время откатa, когда мы сами обновляем значения контролов.
		bool suppressUi = false;

		var undo = UndoStackForShape();

		// Возвращает признак того, что шаг записан: по нему решается,
		// забирать ли его обратно в общую группу истории.
		IUndoableAction? PushChange<T>(string name, T before, T after, Action<T> apply, string key)
		{
			if (suppressUi)
				return null;

			if (EqualityComparer<T>.Default.Equals(before, after))
				return null;

			var step = new PropertyChangeAction<T>(
				name,
				before,
				after,
				v =>
				{
					suppressUi = true;

					try
					{
						apply(v);
					}
					finally
					{
						// finally обязательно: исключение внутри apply иначе
						// навсегда отключило бы панель от модели.
						suppressUi = false;
					}

					NotifyShapeChanged();
					RequestCanvasRefresh?.Invoke();
					Invalidate();
				},
				mergeKey: (shape, key));

			UndoStackForShape()?.Push(step);

			// Возвращается сам шаг, а не признак «записано»: Push мог
			// слить его с предыдущим по совпадению ключа, и тогда
			// последним в истории остаётся ЧУЖОЙ, более ранний шаг.
			return step;
		}

		// ------------------------------------------------------------------------
		// Текст, шрифт, оформление
		// ------------------------------------------------------------------------

		var tb = new SkTextBox
		{
			Multiline = true,
			Text = shape.Text,
			Bounds = new SKRect(0, 0, 160, 72)
		};

		tb.SizeChanged = () =>
		{
			ArrangeUi();
			Invalidate();
		};


		var font = new SkDropDownBox
		{
			DropDownMaxHeight = 320f,
			ItemHeight = 26f
		};

		font.SetItems(SystemFontCatalog.Families
			.Select(f => new SkDropDownItem(f, f, f)));

		font.SetSelected(shape.FontFamily);

		var boldBut = new SkButton { Text = "B", Checked = shape.Bold, Bounds = new SKRect(0, 0, 30, 26) };
		var italicBut = new SkButton { Text = "I", Checked = shape.Italic, Bounds = new SKRect(0, 0, 30, 26) };
		var underlineBut = new SkButton { Text = "U", Checked = shape.Underline, Bounds = new SKRect(0, 0, 30, 26) };
		var strikeBut = new SkButton { Text = "S", Checked = shape.Strikethrough, Bounds = new SKRect(0, 0, 30, 26) };

		var styleRow = new SkFlowLayout
		{
			Direction = SkFlowDirection.Horizontal,
			Gap = 4f,
			AutoHeight = true,
			Bounds = new SKRect(0, 0, 260, 28)
		};

		styleRow.Add(boldBut);
		styleRow.Add(italicBut);
		styleRow.Add(underlineBut);
		styleRow.Add(strikeBut);

		var fs = new SkNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = 0.1f, Maximum = 1000f };
		fs.SetValue(shape.FontSizeMm);

		var charSpace = new SkNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = -100f, Maximum = 1000f };
		charSpace.SetValue(shape.CharacterSpacingMm);

		var lineSpace = new SkNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = -100f, Maximum = 1000f };
		lineSpace.SetValue(shape.LineSpacingMm);

		// ------------------------------------------------------------------------
		// Текст внутри фигуры
		// ------------------------------------------------------------------------

		var insideOtherShape = new SkCheckBox { Checked = shape.InsideOtherShape };

		var containerBut = new SkButton
		{
			Text = shape.InsideShape != null ? shape.InsideShape.Name : "Select",
			Checked = shape.InsideShape != null
		};
		containerBut.Click += OnSelectContainerBut_Click;

		// Сетка выравнивания 3x3. Индекс = (int)VAlign * 3 + (int)HAlign,
		// поэтому порядок значений в перечислениях совпадает с порядком кнопок.
		var alignButtons = new SkButton[9];

		string[] alignGlyphs =
		{
			"\uf29b", "\uf299", "\uf29d",
			"\uf291", "\uf28d", "\uf293",
			"\uf287", "\uf285", "\uf289"
		};

		var alignGrid = new SkFlowLayout
		{
			Direction = SkFlowDirection.Vertical,
			Gap = 3f,
			AutoHeight = true,
			Bounds = new SKRect(0, 0, 120, 96)
		};

		for (int row = 0; row < 3; row++)
		{
			var line = new SkFlowLayout
			{
				Direction = SkFlowDirection.Horizontal,
				Gap = 3f,
				AutoHeight = true,
				Bounds = new SKRect(0, 0, 120, 30)
			};

			for (int col = 0; col < 3; col++)
			{
				int index = row * 3 + col;

				var btn = new SkButton
				{
					Text = alignGlyphs[index],
					Bounds = new SKRect(0, 0, 30, 28),
					Font = MainForm.skFont
				};

				alignButtons[index] = btn;
				line.Add(btn);
			}

			alignGrid.Add(line);
		}

		void RefreshAlignButtons()
		{
			int current = (int)shape.AreaVAlign * 3 + (int)shape.AreaHAlign;

			for (int k = 0; k < 9; k++)
				alignButtons[k].Checked = k == current;
		}

		RefreshAlignButtons();

		var wrapInsideShape = new SkCheckBox { Checked = shape.WrapInsideShape };
		var clipByShape = new SkCheckBox { Checked = shape.ClipByShape };
		var wrapOtherShapes = new SkCheckBox { Checked = shape.WrapOtherShapes };

		var columns = new SkNumericUpDown { Minimum = 0, Maximum = 10, Increment = 1, Decimals = 0 };
		columns.SetValue(shape.Columns);

		var columnGap = new SkNumericUpDown { Minimum = 0, Maximum = 1000, Increment = 0.5f, Decimals = 2 };
		columnGap.SetValue(shape.DistanceBetweenColumns);

		var padding = new SkNumericUpDown { Minimum = 0, Maximum = 1000, Increment = 0.5f, Decimals = 2 };
		padding.SetValue(shape.Padding);

		// ------------------------------------------------------------------------
		// Текст по контуру
		// ------------------------------------------------------------------------

		var alongShape = new SkCheckBox { Checked = shape.AlongShape };

		var shapeAlongBut = new SkButton
		{
			Text = shape.otherShape != null ? shape.otherShape.Name : "Select",
			Checked = shape.otherShape != null
		};
		shapeAlongBut.Click += OnSelectOtherShapeBut_Click;

		var ax = new SkNumericUpDown { Minimum = -1000000, Maximum = 1000000, Increment = 1, Decimals = 2 };
		ax.SetValue(shape.ax);

		// Decimals = 2 добавлен: без него вертикальное смещение округлялось
		// до целых, а горизонтальное — нет.
		var ay = new SkNumericUpDown { Minimum = -1000000, Maximum = 1000000, Increment = 1, Decimals = 2 };
		ay.SetValue(shape.ay);

		var tangent = new SkCheckBox { Checked = shape.tangentAlong };

		// ------------------------------------------------------------------------
		// Строки, видимость которых зависит от режима
		// ------------------------------------------------------------------------

		var containerRow = new SkPropertyRow(L.T("Inside shape"), new SkControl[] { insideOtherShape, containerBut });
		var alignRow = new SkPropertyRow(L.T("Alignment"), alignGrid) { Bounds = new SKRect(0, 0, 260, 100) };
		var wrapInsideRow = new SkPropertyRow(L.T("Wrapping"), wrapInsideShape);
		var clipByShapeRow = new SkPropertyRow(L.T("Clipping"), clipByShape);
		var wrapOtherRow = new SkPropertyRow(L.T("Wrap others"), wrapOtherShapes);
		var columnsRow = new SkPropertyRow(L.T("Columns"), columns);
		var columnGapRow = new SkPropertyRow(L.T("Column gap"), columnGap);
		var paddingRow = new SkPropertyRow(L.T("Padding"), padding);

		var alongRow = new SkPropertyRow(L.T("Along path"), new SkControl[] { alongShape, shapeAlongBut });
		var axRow = new SkPropertyRow(L.T("Horizontal"), ax);
		var ayRow = new SkPropertyRow(L.T("Vertical"), ay);
		var tangentRow = new SkPropertyRow(L.T("Tangent to curve"), tangent);

		// Видимость обеих групп зависит от обоих флагов: включение одного режима
		// выключает другой, поэтому обновлять их нужно вместе.
		void UpdateModeRows()
		{
			bool inside = shape.InsideOtherShape;
			bool along = shape.AlongShape;

			insideOtherShape.Checked = inside;
			alongShape.Checked = along;

			containerBut.Enabled = inside;
			alignRow.Visible = inside;
			wrapInsideRow.Visible = inside;
			wrapOtherRow.Visible = inside;
			columnsRow.Visible = inside;
			columnGapRow.Visible = inside;
			paddingRow.Visible = inside;
			clipByShapeRow.Visible = inside;

			shapeAlongBut.Enabled = along;
			axRow.Visible = along;
			ayRow.Visible = along;
			tangentRow.Visible = along;
		}

		// Пара (AlongShape, InsideOtherShape) пишется одним шагом: нажатие одной
		// галочки меняет оба значения, и откат должен вернуть оба. Запись только
		// изменённого свойства означала бы, что Undo не восстановит выключенный
		// режим.
		void PushModeChange((bool Along, bool Inside) before)
		{
			var after = (shape.AlongShape, shape.InsideOtherShape);

			PushChange(
				"Text mode",
				before,
				after,
				x =>
				{
					// Порядок важен: сеттеры взаимно гасят друг друга, поэтому
					// сначала выключаем, потом включаем.
					if (!x.Item1) shape.AlongShape = false;
					if (!x.Item2) shape.InsideOtherShape = false;
					if (x.Item1) shape.AlongShape = true;
					if (x.Item2) shape.InsideOtherShape = true;

					UpdateModeRows();
					ArrangeUi();
				},
				"Text.Mode");
		}

		// ------------------------------------------------------------------------
		// Обработчики: текст и шрифт
		// ------------------------------------------------------------------------

		tb.TextChanged = s =>
		{
			if (suppressUi)
				return;

			string before = shape.Text;
			shape.Text = s;

			// mergeKey склеивает набор текста в один шаг истории: без него
			// каждое нажатие клавиши давало бы отдельную отмену.
			var recorded = PushChange("Text", before, s, x => { shape.Text = x; tb.Text = x; }, "Text.Text");

			// Надписи получают текст, всё остальное в выделении — нет:
			// у кривой или окружности своего текста не существует.
			ApplyToOthers(
				"Text", "Text.Text", OtherTextTargets(shape),
				x => x.Text, (x, value) => x.Text = value, s, recorded);

			NotifyShapeChanged();
		};

		font.SelectedIndexChanged = (_, family) =>
		{
			if (suppressUi || string.IsNullOrWhiteSpace(family))
				return;

			string before = shape.FontFamily;
			shape.FontFamily = family;

			var recorded = PushChange("Font", before, family,
				x => { shape.FontFamily = x; font.SetSelected(x); }, "Text.FontFamily");

			ApplyToOthers(
				"Font", "Text.FontFamily", OtherTextTargets(shape),
				x => x.FontFamily, (x, value) => x.FontFamily = value, family, recorded);

			NotifyShapeChanged();
			Invalidate();
		};

		// Геттер и сеттер принимают TextShape: тот же самый сеттер нужно
		// применить и к надписям остальных выделенных объектов.
		void SetupStyleButton(
			SkButton button,
			string name,
			string key,
			Func<TextShape, bool> get,
			Action<TextShape, bool> set)
		{
			button.Click = () =>
			{
				if (suppressUi)
					return;

				bool before = get(shape);
				bool after = !before;

				set(shape, after);
				button.Checked = after;

				var recorded = PushChange(name, before, after,
					x => { set(shape, x); button.Checked = x; }, key);

				ApplyToOthers(
					name, key, OtherTextTargets(shape),
					get, set, after, recorded);

				NotifyShapeChanged();
				Invalidate();
			};
		}

		SetupStyleButton(boldBut, "Bold", "Text.Bold", x => x.Bold, (x, v) => x.Bold = v);
		SetupStyleButton(italicBut, "Italic", "Text.Italic", x => x.Italic, (x, v) => x.Italic = v);
		SetupStyleButton(underlineBut, "Underline", "Text.Underline", x => x.Underline, (x, v) => x.Underline = v);
		SetupStyleButton(strikeBut, "Strikethrough", "Text.Strikethrough", x => x.Strikethrough, (x, v) => x.Strikethrough = v);

		fs.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.FontSizeMm;
			shape.FontSizeMm = v;

			var recorded = PushChange("Font size", before, v,
				x => { shape.FontSizeMm = x; fs.SetValue(x); }, "Text.FontSizeMm");

			ApplyToOthers(
				"Font size", "Text.FontSizeMm", OtherTextTargets(shape),
				x => x.FontSizeMm, (x, value) => x.FontSizeMm = value, v, recorded);

			NotifyShapeChanged();
		};

		charSpace.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.CharacterSpacingMm;
			shape.CharacterSpacingMm = v;

			var recorded = PushChange("Character spacing", before, v,
				x => { shape.CharacterSpacingMm = x; charSpace.SetValue(x); }, "Text.CharacterSpacingMm");

			ApplyToOthers(
				"Character spacing", "Text.CharacterSpacingMm", OtherTextTargets(shape),
				x => x.CharacterSpacingMm, (x, value) => x.CharacterSpacingMm = value, v, recorded);

			NotifyShapeChanged();
		};

		lineSpace.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.LineSpacingMm;
			shape.LineSpacingMm = v;

			var recorded = PushChange("Line spacing", before, v,
				x => { shape.LineSpacingMm = x; lineSpace.SetValue(x); }, "Text.LineSpacingMm");

			ApplyToOthers(
				"Line spacing", "Text.LineSpacingMm", OtherTextTargets(shape),
				x => x.LineSpacingMm, (x, value) => x.LineSpacingMm = value, v, recorded);

			NotifyShapeChanged();
		};

		// ------------------------------------------------------------------------
		// Обработчики: режимы
		// ------------------------------------------------------------------------

		insideOtherShape.CheckedChanged += v =>
		{
			if (suppressUi) return;

			var before = (shape.AlongShape, shape.InsideOtherShape);

			// Сеттер сам выключит AlongShape.
			shape.InsideOtherShape = v;

			UpdateModeRows();
			PushModeChange(before);

			// ArrangeUi, а не p.Arrange: меняется высота панели, и соседние
			// панели надо переставить. SkFlowLayout расставляет только видимые
			// элементы, поэтому без пересчёта включённые строки лежат друг
			// на друге за границами панели.
			ArrangeUi();
			NotifyShapeChanged();
			Invalidate();
		};

		alongShape.CheckedChanged += v =>
		{
			if (suppressUi) return;

			var before = (shape.AlongShape, shape.InsideOtherShape);

			shape.AlongShape = v;

			UpdateModeRows();
			PushModeChange(before);

			ArrangeUi();
			NotifyShapeChanged();
			Invalidate();
		};

		// ------------------------------------------------------------------------
		// Обработчики: параметры области
		// ------------------------------------------------------------------------

		for (int i = 0; i < 9; i++)
		{
			int index = i;

			alignButtons[index].Click = () =>
			{
				if (suppressUi) return;

				var before = (shape.AreaHAlign, shape.AreaVAlign);

				shape.AreaVAlign = (TextAreaVAlign)(index / 3);
				shape.AreaHAlign = (TextAreaHAlign)(index % 3);

				RefreshAlignButtons();

				// Горизонталь и вертикаль меняются одной кнопкой, поэтому шаг
				// истории один: пара упакована в кортеж.
				PushChange(
					"Text alignment",
					before,
					(shape.AreaHAlign, shape.AreaVAlign),
					x =>
					{
						shape.AreaHAlign = x.Item1;
						shape.AreaVAlign = x.Item2;
						RefreshAlignButtons();
					},
					"Text.AreaAlign");

				NotifyShapeChanged();
				Invalidate();
			};
		}

		void SetupAreaToggle(SkCheckBox box, string name, string key, Func<bool> get, Action<bool> set)
		{
			box.CheckedChanged += v =>
			{
				if (suppressUi) return;

				bool before = get();
				set(v);

				PushChange(name, before, v,
					x => { set(x); box.Checked = x; }, key);

				NotifyShapeChanged();
				Invalidate();
			};
		}

		SetupAreaToggle(wrapInsideShape, "Wrap inside shape", "Text.WrapInsideShape",
			() => shape.WrapInsideShape, x => shape.WrapInsideShape = x);

		SetupAreaToggle(clipByShape, "Clip by shape", "Text.ClipByShape",
			() => shape.ClipByShape, x => shape.ClipByShape = x);

		SetupAreaToggle(wrapOtherShapes, "Wrap other shapes", "Text.WrapOtherShapes",
			() => shape.WrapOtherShapes, x => shape.WrapOtherShapes = x);

		columns.ValueChanged = v =>
		{
			if (suppressUi) return;

			int before = shape.Columns;
			int after = (int)Math.Clamp(v, 0, 10);

			shape.Columns = after;

			PushChange("Columns", before, after,
				x => { shape.Columns = x; columns.SetValue(x); }, "Text.Columns");

			NotifyShapeChanged();
			Invalidate();
		};

		columnGap.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.DistanceBetweenColumns;
			shape.DistanceBetweenColumns = v;

			PushChange("Column gap", before, v,
				x => { shape.DistanceBetweenColumns = x; columnGap.SetValue(x); }, "Text.DistanceBetweenColumns");

			NotifyShapeChanged();
			Invalidate();
		};

		padding.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.Padding;
			shape.Padding = v;

			PushChange("Padding", before, v,
				x => { shape.Padding = x; padding.SetValue(x); }, "Text.Padding");

			NotifyShapeChanged();
			Invalidate();
		};


		// ------------------------------------------------------------------------
		// Обработчики: параметры контура
		// ------------------------------------------------------------------------

		ax.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.ax;
			shape.ax = v;

			PushChange("Text offset X", before, v,
				x => { shape.ax = x; ax.SetValue(x); }, "Text.ax");

			NotifyShapeChanged();
			Invalidate();
		};

		ay.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.ay;
			shape.ay = v;

			PushChange("Text offset Y", before, v,
				x => { shape.ay = x; ay.SetValue(x); }, "Text.ay");

			NotifyShapeChanged();
			Invalidate();
		};

		tangent.CheckedChanged += v =>
		{
			if (suppressUi) return;

			bool before = shape.tangentAlong;
			shape.tangentAlong = v;

			PushChange("Tangent to curve", before, v,
				x => { shape.tangentAlong = x; tangent.Checked = x; }, "Text.tangentAlong");

			NotifyShapeChanged();
			Invalidate();
		};

		// ------------------------------------------------------------------------
		// Компоновка
		// ------------------------------------------------------------------------

		p.Content.Add(new SkPropertyRow("text_row", L.T("Text"), tb));
		p.Content.Add(new SkPropertyRow("font_row", L.T("Font"), font) { Bounds = new SKRect(0, 0, 260, 32) });
		p.Content.Add(new SkPropertyRow("style_row", L.T("Style"), styleRow) { Bounds = new SKRect(0, 0, 260, 30) });
		p.Content.Add(new SkPropertyRow("size_row", L.T("Size"), fs));
		p.Content.Add(new SkPropertyRow("char_spacing_row", L.T("Char spacing"), charSpace));
		p.Content.Add(new SkPropertyRow("line_spacing_row", L.T("Line spacing"), lineSpace));

		p.Content.Add(new SkLabel { Text = "Text inside shape", Bold = true, FontSize = 16 });

		p.Content.Add(containerRow);
		p.Content.Add(alignRow);
		p.Content.Add(paddingRow);
		p.Content.Add(wrapInsideRow);
		p.Content.Add(clipByShapeRow);
		p.Content.Add(wrapOtherRow);
		p.Content.Add(columnsRow);
		p.Content.Add(columnGapRow);

		p.Content.Add(new SkLabel { Text = "Text along path", Bold = true, FontSize = 16 });

		p.Content.Add(alongRow);
		p.Content.Add(axRow);
		p.Content.Add(ayRow);
		p.Content.Add(tangentRow);

		// Начальное состояние. Без этого вызова строки обеих групп показывались
		// бы всегда, независимо от включённого режима.
		UpdateModeRows();

		p.Arrange();

		return p;
	}

	#endregion

	#region Панель Link

	/// <summary>
	/// Привязка концов кривой к другим фигурам — связи во flowchart.
	///
	/// Показывается для кривых с двумя и более узлами. Изображение исключено:
	/// ImageShape наследуется от CurveShape, но его узлы задают рамку картинки,
	/// а не линию, и привязывать их не к чему.
	/// </summary>
	private SkCollapsePanel BuildLinkPanel(IShapeAnchorHost curve, string firstLabel = "First node", string lastLabel = "Last node")
	{
		var p = CreatePanel("Link");

		// Пока true, обработчики не пишут ни в модель, ни в историю.
		bool suppressUi = false;

		var undo = UndoStackForShape();

		// Привязки первого и последнего конца пишутся одним действием:
		// CurveAnchorAction снимает обе разом, и разделять их незачем.
		void PushAnchorChange(CurveAnchorAction action)
		{
			if (suppressUi)
			{
				action.Discard();
				return;
			}

			action.CaptureAfter();

			if (action.HasChanges())
				undo?.Push(action);
			else
				action.Discard();
		}

		/// <summary>
		/// Повторяет правку привязок на остальных выделенных объектах,
		/// у которых привязки есть.
		///
		/// Снимок берётся своим CurveAnchorAction на каждый объект:
		/// привязка — это не одно значение, а пара «фигура плюс режим»,
		/// и откат простой подстановкой булева поля вернул бы галочку,
		/// но не саму связь.
		/// </summary>
		void FanOutAnchors(string title, Action<IShapeAnchorHost> apply)
		{
			if (!IsMultiSelection)
				return;

			var steps = new List<IUndoableAction>();

			foreach (var host in OtherTargets<IShapeAnchorHost>())
			{
				var hostAction = CurveAnchorAction.Capture(host);

				apply(host);

				hostAction.CaptureAfter();

				if (hostAction.HasChanges())
					steps.Add(hostAction);
				else
					hostAction.Discard();
			}

			if (steps.Count == 0)
				return;

			// Шаг основной фигуры уже в стеке — забираем его в общую группу,
			// чтобы всё выделение откатывалось одним нажатием.
			var primaryStep = undo?.TakeLast();

			undo?.BeginGroup(title);

			try
			{
				if (primaryStep != null)
					undo?.Push(primaryStep);

				foreach (var step in steps)
					undo?.Push(step);
			}
			finally
			{
				undo?.CommitGroup();
			}
		}

		// ------------------------------------------------------------------------
		// Контролы
		// ------------------------------------------------------------------------

		var anchorFirstNode = new SkCheckBox { Checked = curve.FirstAnchor.Enabled };

		var firstNodeTangentToEdge = new SkCheckBox
		{
			Checked = curve.FirstAnchor.TangentToEdge,
			Enabled = curve.FirstAnchor.Enabled,
			Text = "Tangent"
		};

		var anchorLastNode = new SkCheckBox { Checked = curve.LastAnchor.Enabled };

		var lastNodeTangentToEdge = new SkCheckBox
		{
			Checked = curve.LastAnchor.TangentToEdge,
			Enabled = curve.LastAnchor.Enabled,
			Text = "Tangent"
		};

		var firstNodeAnchorRow = new SkPropertyRow(
			firstLabel,
			new SkControl[] { anchorFirstNode, firstNodeTangentToEdge });

		var lastNodeAnchorRow = new SkPropertyRow(
			lastLabel,
			new SkControl[] { anchorLastNode, lastNodeTangentToEdge });

		// Состояние контролов зависит от модели, поэтому вынесено в функцию:
		// её же вызывает откат истории через перестроение панели.
		void UpdateRowStates()
		{
			anchorFirstNode.Checked = curve.FirstAnchor.Enabled;
			anchorLastNode.Checked = curve.LastAnchor.Enabled;

			firstNodeTangentToEdge.Checked = curve.FirstAnchor.TangentToEdge;
			lastNodeTangentToEdge.Checked = curve.LastAnchor.TangentToEdge;

			// Направление выхода имеет смысл только при разрешённой привязке:
			// включённая галочка, которая ни на что не влияет, читается
			// как неработающая функция.
			firstNodeTangentToEdge.Enabled = curve.FirstAnchor.Enabled;
			lastNodeTangentToEdge.Enabled = curve.LastAnchor.Enabled;
		}

		// ------------------------------------------------------------------------
		// Обработчики
		// ------------------------------------------------------------------------

		anchorFirstNode.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			var action = CurveAnchorAction.Capture(curve);

			curve.FirstAnchor.Enabled = v;

			// Снятие галочки снимает и саму привязку. Иначе конец остался бы
			// приклеенным к фигуре, а управления им в панели больше нет:
			// ApplyAnchors продолжал бы тянуть его на место при каждой
			// отрисовке, и пользователь не смог бы его сдвинуть.
			if (!v)
				curve.ClearAnchor(first: true);

			UpdateRowStates();
			PushAnchorChange(action);

			FanOutAnchors("Anchor first node", host =>
			{
				host.FirstAnchor.Enabled = v;

				if (!v)
					host.ClearAnchor(first: true);
			});

			NotifyShapeChanged();
			Invalidate();
		};

		anchorLastNode.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			var action = CurveAnchorAction.Capture(curve);

			curve.LastAnchor.Enabled = v;

			if (!v)
				curve.ClearAnchor(first: false);

			UpdateRowStates();
			PushAnchorChange(action);

			FanOutAnchors("Anchor last node", host =>
			{
				host.LastAnchor.Enabled = v;

				if (!v)
					host.ClearAnchor(first: false);
			});

			NotifyShapeChanged();
			Invalidate();
		};

		firstNodeTangentToEdge.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			var action = CurveAnchorAction.Capture(curve);

			curve.FirstAnchor.TangentToEdge = v;

			// Направление выхода задаётся в ApplyAnchors, а её результат
			// закэширован подписью — без сброса изменение не проявится.
			curve.InvalidateAnchors();

			PushAnchorChange(action);

			FanOutAnchors("Anchor tangent", host =>
			{
				host.FirstAnchor.TangentToEdge = v;
				host.InvalidateAnchors();
			});

			NotifyShapeChanged();
			Invalidate();
		};

		lastNodeTangentToEdge.CheckedChanged += v =>
		{
			if (suppressUi)
				return;

			var action = CurveAnchorAction.Capture(curve);

			curve.LastAnchor.TangentToEdge = v;
			curve.InvalidateAnchors();

			PushAnchorChange(action);

			FanOutAnchors("Anchor tangent", host =>
			{
				host.LastAnchor.TangentToEdge = v;
				host.InvalidateAnchors();
			});

			NotifyShapeChanged();
			Invalidate();
		};

		// ------------------------------------------------------------------------
		// Компоновка
		// ------------------------------------------------------------------------

		p.Content.Add(firstNodeAnchorRow);
		p.Content.Add(lastNodeAnchorRow);

		UpdateRowStates();

		p.Arrange();

		return p;
	}

	#endregion

	#region История

	private static UndoStack? UndoStackForShape()
	=> MainForm.CurrentPage?.doc?.Undo;

	/// <summary>
	/// Единицы активной страницы в виде готового суффикса подписи: «, mm».
	///
	/// Пустая строка, когда страницы нет: подпись «Толщина» без единиц
	/// честнее, чем «Толщина, mm» про страницу, размеченную в дюймах.
	/// </summary>
	private static string CurrentPageUnitTitle()
	{
		string? title = MainForm.CurrentPage?.uom?.title;

		return string.IsNullOrWhiteSpace(title) ? string.Empty : ", " + title;
	}
	#endregion

	#region Параметры конкретных фигур

	/// <summary>
	/// Добавляет в General строки с параметрами, которые есть только у
	/// конкретного типа фигуры: радиусы скругления, размер фаски, радиус
	/// окружности, полуоси эллипса, число сторон многоугольника.
	///
	/// Значения показываются В МИРОВЫХ единицах, а не в локальных. Причина:
	/// растягивание фигуры мышью не трогает её собственные поля — масштаб
	/// уходит в LocalTransform. Показывай панель локальное поле, число при
	/// растягивании не менялось бы, хотя скругление на экране растёт. При
	/// записи значение делится на тот же масштаб, поэтому на холсте
	/// получается ровно то, что ввёл пользователь.
	/// </summary>
	private void AppendShapeSpecificRows(SkCollapsePanel p, Shape shape)
	{
		// Множители берутся у каждой фигуры свои — здесь только функции,
		// а не числа: строка может править несколько выделенных объектов
		// с разным растяжением.
		static float ScaleX(Shape s) { GetShapeScale(s, out float x, out _); return x; }
		static float ScaleY(Shape s) { GetShapeScale(s, out _, out float y); return y; }

		switch (shape)
		{
			case RoundedRectangleShape:
				{
					// Названия «Round X» и «Round Y», а не «Radius»: у
					// прямоугольника это скругление угла, и одно имя
					// с полуосями эллипса сбивало с толку — рядом в панели
					// они означают разное.
					//
					// Предел скругления считается у каждой фигуры свой:
					// половина её собственной стороны, дальше углы наезжают
					// друг на друга.
					AddShapeParamRow<RoundedRectangleShape>(
						p,
						"Round X",
						s => s.RadiusX,
						(s, v) => s.RadiusX = Math.Clamp(v, 0f, s.LocalRect.Width * 0.5f),
						ScaleX,
						0f);

					AddShapeParamRow<RoundedRectangleShape>(
						p,
						"Round Y",
						s => s.RadiusY,
						(s, v) => s.RadiusY = Math.Clamp(v, 0f, s.LocalRect.Height * 0.5f),
						ScaleY,
						0f);

					break;
				}

			case ChamferedRectangleShape:
				{
					AddShapeParamRow<ChamferedRectangleShape>(
						p,
						"Chamfer X",
						s => s.ChamferX,
						(s, v) => s.ChamferX = Math.Clamp(v, 0f, s.LocalRect.Width * 0.5f),
						ScaleX,
						0f);

					AddShapeParamRow<ChamferedRectangleShape>(
						p,
						"Chamfer Y",
						s => s.ChamferY,
						(s, v) => s.ChamferY = Math.Clamp(v, 0f, s.LocalRect.Height * 0.5f),
						ScaleY,
						0f);

					break;
				}

			case Circle2PShape:
				{
					AddShapeParamRow<Circle2PShape>(
						p,
						"Radius",
						s => s.Radius,
						(s, v) =>
						{
							s.Radius = MathF.Max(0.0001f, v);
							SyncCircleRadiusPoint(s);
						},
						ScaleX,
						0.0001f);

					break;
				}

			case Ellipse3PShape:
				{
					AddShapeParamRow<Ellipse3PShape>(
						p,
						"Radius X",
						s => s.RadiusX,
						(s, v) =>
						{
							s.RadiusX = MathF.Max(0.0001f, v);
							SyncEllipseRadiusPoints(s);
						},
						ScaleX,
						0.0001f);

					AddShapeParamRow<Ellipse3PShape>(
						p,
						"Radius Y",
						s => s.RadiusY,
						(s, v) =>
						{
							s.RadiusY = MathF.Max(0.0001f, v);
							SyncEllipseRadiusPoints(s);
						},
						ScaleY,
						0.0001f);

					break;
				}

			case PolygonShape polygon:
				{
					// У произвольного многоугольника вершины заданы явно:
					// числа сторон и полуосей у него просто нет, а RebuildPoints
					// для него ничего не делает. Показывать неработающие поля
					// хуже, чем не показывать их вовсе.
					if (polygon.UseExplicitPoints)
						break;

					polygon.InitializeFromPointsIfNeeded();

					var sides = new SkNumericUpDown
					{
						Minimum = 3,
						Maximum = 200,
						Increment = 1,
						Decimals = 0,
						AllowNegative = false,
						AllowDecimal = false
					};

					void BindSides()
					{
						if (SameAcrossSelection<PolygonShape, int>(s => s.Sides, out int common))
							sides.SetValue(common);
						else
							sides.SetEmpty();
					}

					BindSides();

					sides.ValueChanged = v =>
					{
						if (_updatingFromShape)
							return;

						ApplyGeometryToSelection<PolygonShape>("Sides", s =>
						{
							if (s.UseExplicitPoints)
								return;

							s.Sides = Math.Clamp((int)MathF.Round(v), 3, 200);
							s.RebuildPoints();
						});

						NotifyShapeChanged();
					};

					_shapeParamRefreshers.Add(BindSides);
					p.Content.Add(new SkPropertyRow(L.T("Sides"), sides));

					AddShapeParamRow<PolygonShape>(
						p,
						"Radius X",
						s => s.RadiusX,
						(s, v) =>
						{
							if (s.UseExplicitPoints)
								return;

							s.RadiusX = MathF.Max(0.0001f, v);
							s.RebuildPoints();
						},
						ScaleX,
						0.0001f);

					AddShapeParamRow<PolygonShape>(
						p,
						"Radius Y",
						s => s.RadiusY,
						(s, v) =>
						{
							if (s.UseExplicitPoints)
								return;

							s.RadiusY = MathF.Max(0.0001f, v);
							s.RebuildPoints();
						},
						ScaleY,
						0.0001f);

					break;
				}
		}
	}

	/// <summary>
	/// Строка редактора собственного параметра фигуры.
	///
	/// Тип T задаёт круг фигур, которых касается правка: радиус получат
	/// только окружности, полуоси — только эллипсы и многоугольники.
	/// Остальные выделенные объекты остаются нетронутыми — у них такого
	/// параметра просто нет.
	///
	/// Множитель перехода в мировые единицы берётся У КАЖДОЙ фигуры свой:
	/// две одинаковые окружности могут быть по-разному растянуты, и общий
	/// множитель задал бы им разные радиусы.
	/// </summary>
	private void AddShapeParamRow<T>(
		SkCollapsePanel p,
		string label,
		Func<T, float> read,
		Action<T, float> write,
		Func<Shape, float> scaleOf,
		float minimum)
		where T : class
	{
		float SafeScale(Shape s)
		{
			float scale = scaleOf(s);

			return MathF.Abs(scale) < 0.0001f ? 1f : MathF.Abs(scale);
		}

		var editor = new SkNumericUpDown
		{
			Minimum = minimum,
			Maximum = 1000000f,
			Increment = 0.5f,
			Decimals = 2
		};

		void Bind()
		{
			if (SameAcrossSelection<T, float>(
					s => read(s) * SafeScale((Shape)(object)s),
					out float common))
			{
				editor.SetValue(common);
			}
			else
			{
				editor.SetEmpty();
			}
		}

		Bind();

		editor.ValueChanged = v =>
		{
			if (_updatingFromShape)
				return;

			ApplyGeometryToSelection<T>(label, s => write(s, v / SafeScale((Shape)(object)s)));

			NotifyShapeChanged();
		};

		// Обратная связь: после растягивания фигуры на холсте панель обязана
		// показать новое значение, иначе поле врёт до следующего выделения.
		_shapeParamRefreshers.Add(Bind);

		p.Content.Add(new SkPropertyRow(L.T(label), editor));
	}

	/// <summary>
	/// Масштаб фигуры по осям, полученный из её мировой трансформации.
	/// Используется длина столбцов матрицы, поэтому поворот на результат
	/// не влияет — в отличие от чтения ScaleX и ScaleY напрямую.
	/// </summary>
	private static void GetShapeScale(Shape shape, out float scaleX, out float scaleY)
	{
		var m = shape.GetWorldTransform();

		scaleX = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
		scaleY = MathF.Sqrt(m.SkewX * m.SkewX + m.ScaleY * m.ScaleY);

		if (scaleX < 0.0001f)
			scaleX = 1f;

		if (scaleY < 0.0001f)
			scaleY = 1f;
	}

	/// <summary>
	/// Держит опорную точку радиуса согласованной с самим радиусом.
	/// Точка служит маркером перетаскивания: если её не подвинуть, маркер
	/// останется на прежнем месте, а окружность уже другого размера.
	/// </summary>
	private static void SyncCircleRadiusPoint(Circle2PShape shape)
	{
		float dx = shape.RadiusPoint.X - shape.CenterPoint.X;
		float dy = shape.RadiusPoint.Y - shape.CenterPoint.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		// Направление потеряно (точки совпали) — берём горизонтальное.
		if (len < 0.0001f)
		{
			shape.RadiusPoint = new SKPoint(shape.CenterPoint.X + shape.Radius, shape.CenterPoint.Y);
			return;
		}

		shape.RadiusPoint = new SKPoint(
			shape.CenterPoint.X + dx / len * shape.Radius,
			shape.CenterPoint.Y + dy / len * shape.Radius);
	}

	/// <summary>
	/// То же для эллипса. Знак смещения сохраняется: он задаёт сторону, с
	/// которой пользователь построил вторую полуось.
	/// </summary>
	private static void SyncEllipseRadiusPoints(Ellipse3PShape shape)
	{
		float signX = shape.RadiusPoint1.X < 0f ? -1f : 1f;
		float signY = shape.RadiusPoint2.Y < 0f ? -1f : 1f;

		shape.RadiusPoint1 = new SKPoint(signX * shape.RadiusX, 0f);
		shape.RadiusPoint2 = new SKPoint(0f, signY * shape.RadiusY);
	}

	#endregion

	#region Панели размера
	/// <summary>
	/// Параметры шрифта размера.
	///
	/// Переиспользуется готовая панель текста: MeasurementShape держит
	/// TextShape именно ради этого. Лишние строки скрываются — у размера нет
	/// ни собственной строки текста (значение измеряется), ни текста внутри
	/// фигуры, ни текста по контуру.
	/// </summary>
	private SkCollapsePanel BuildMeasurementTextPanel(MeasurementShape shape)
	{
		var p = BuildTextPanel(shape.TextStyle);
		p.HeaderText = L.T("Text");

		KeepOnlyRows(p, "font_row", "size_row", "style_row");

		// Строка добавляется ПОСЛЕ KeepOnlyRows: тот скрывает всё, чего нет
		// в списке имён, и добавленная раньше строка исчезла бы вместе
		// с чужими режимами.
		AddMeasurementFactorRow(p, shape);

		p.Arrange();

		return p;
	}

	/// <summary>
	/// Панель текста для размера угла.
	///
	/// Собирается тем же способом, что и у линейного размера: берётся
	/// готовая панель текста и из неё оставляются только строки шрифта.
	/// Отличие одно — вместо множителя значения здесь инверсия угла.
	/// </summary>
	private SkCollapsePanel BuildAngleMeasurementTextPanel(AngleMeasurementShape shape)
	{
		var p = BuildTextPanel(shape.TextStyle);
		p.HeaderText = L.T("Text");

		KeepOnlyRows(p, "font_row", "size_row", "style_row");

		// Строка добавляется ПОСЛЕ KeepOnlyRows: тот скрывает всё, чего нет
		// в списке имён, и добавленная раньше строка исчезла бы вместе
		// с чужими режимами.
		AddInvertAngleRow(p, shape);

		p.Arrange();

		return p;
	}

	/// <summary>
	/// Переключение между внутренним и внешним углом.
	///
	/// Внутренний — тот, внутри которого лежат оба выбранных вектора;
	/// внешний дополняет его до полного оборота. Какой из них нужен,
	/// из щелчков вывести нельзя, поэтому выбор отдан пользователю.
	/// </summary>
	private void AddInvertAngleRow(SkCollapsePanel p, AngleMeasurementShape shape)
	{
		var check = new SkCheckBox { Checked = shape.InvertAngle };

		check.CheckedChanged += value =>
		{
			if (_updatingFromShape)
				return;

			bool before = shape.InvertAngle;

			if (before == value)
				return;

			shape.InvertAngle = value;

			UndoStackForShape()?.Push(
				new PropertyChangeAction<bool>(
					L.T("Invert angle"),
					before,
					value,
					v =>
					{
						shape.InvertAngle = v;
						RequestCanvasRefresh?.Invoke();
					}));

			// Меняется измеренное значение и направление дуги, но не состав
			// строк панели: достаточно перерисовать холст.
			RequestCanvasRefresh?.Invoke();
			NotifyShapeChanged();
		};

		_shapeParamRefreshers.Add(() => check.Checked = shape.InvertAngle);

		p.Content.Add(new SkPropertyRow("invert_angle_row", L.T("Invert angle"), check));
	}

	/// <summary>
	/// Множитель отображаемого значения размера.
	///
	/// Три знака после запятой, потому что типовые масштабные множители —
	/// это 0.001, 0.01 и 0.1: с двумя знаками первый из них не набрать.
	/// </summary>
	private void AddMeasurementFactorRow(SkCollapsePanel p, MeasurementShape shape)
	{
		var editor = new SkNumericUpDown
		{
			Minimum = 0.001f,
			Maximum = 1000000f,
			Increment = 0.1f,
			Decimals = 3,
			AllowNegative = false
		};

		editor.SetValue(shape.Factor);

		editor.ValueChanged = v =>
		{
			if (_updatingFromShape)
				return;

			shape.Factor = v;

			// Перестраивать панель не нужно: меняется только подпись
			// на холсте, состав строк остаётся прежним.
			NotifyShapeChanged();
		};

		_shapeParamRefreshers.Add(() => editor.SetValue(shape.Factor));

		p.Content.Add(new SkPropertyRow("factor_row", L.T("Factor"), editor));

		// Строка знаков идёт под строкой значения и получает её редактор
		// напрямую. Через _shapeParamRefreshers связать их нельзя:
		// RefreshFromBoundShape выходит сразу, пока фокус в SkNumericUpDown,
		// а перерисовать значение надо ровно в тот момент, когда пользователь
		// щёлкает стрелку счётчика знаков — то есть когда фокус в нём.
		var valueEditor = AddMeasurementValueRow(p, shape);

		AddMeasurementDigitsRow(p, shape, valueEditor);
	}

	/// <summary>
	/// Поле подписываемого значения.
	///
	/// Показывает то, что стоит в подписи: измеренное или введённое.
	/// Введённое держится ровно до тех пор, пока размер не растянут
	/// и не сдвинута привязанная фигура, — дальше поле само возвращается
	/// к измеренному, потому что читает его у фигуры при каждом обновлении.
	///
	/// Возвращает редактор: строке знаков он нужен, чтобы менять его
	/// точность вместе с точностью подписи.
	/// </summary>
	private SkNumericUpDown AddMeasurementValueRow(SkCollapsePanel p, MeasurementShape shape)
	{
		var editor = new SkNumericUpDown
		{
			Minimum = -1000000f,
			Maximum = 1000000f,
			Increment = 1f,
			Decimals = Math.Clamp(shape.Decimals, 0, 6),
			AllowNegative = true
		};

		editor.SetValue(shape.GetDisplayValue());

		editor.ValueChanged = v =>
		{
			if (_updatingFromShape)
				return;

			// Совпадение с измеренным трактуется как отказ от подмены:
			// иначе значение осталось бы «введённым» и перестало следить
			// за геометрией, ничем себя не выдавая.
			if (MathF.Abs(v - shape.GetMeasuredValue() * shape.Factor) < 0.000001f)
				shape.ClearValueOverride();
			else
				shape.SetValueOverride(v);

			NotifyShapeChanged();
		};

		_shapeParamRefreshers.Add(() =>
		{
			editor.Decimals = Math.Clamp(shape.Decimals, 0, 6);
			editor.SetValue(shape.GetDisplayValue());
		});

		p.Content.Add(new SkPropertyRow("value_row", L.T("Value"), editor));

		return editor;
	}

	/// <summary>
	/// Число знаков после запятой в подписи размера.
	///
	/// Одно поле правит и подпись на холсте, и поле значения в панели:
	/// расходиться им нельзя, иначе панель показывала бы одно число,
	/// а чертёж — другое.
	///
	/// Предел 6 — тот же, что и в MeasurementShape.GetDisplayText: дальше
	/// float не хранит значащих цифр, и лишние знаки были бы не точностью,
	/// а мусором округления. Дробных знаков у самого счётчика нет
	/// (AllowDecimal = false): «2.5 знака» смысла не имеет.
	/// </summary>
	private void AddMeasurementDigitsRow(
		SkCollapsePanel p,
		MeasurementShape shape,
		SkNumericUpDown valueEditor)
	{
		var editor = new SkNumericUpDown
		{
			Minimum = 0f,
			Maximum = 6f,
			Increment = 1f,
			Decimals = 0,
			AllowNegative = false,
			AllowDecimal = false
		};

		editor.SetValue(shape.Decimals);

		// Применение вынесено отдельно, потому что тем же путём ходит отмена:
		// иначе после Undo фигура получила бы прежнее число знаков, а поля
		// панели остались бы с новым.
		void Apply(int digits)
		{
			shape.Decimals = digits;

			// Значение перечитывается у фигуры, а не берётся из поля: в поле
			// оно уже округлено до прежнего числа знаков, и при увеличении
			// точности вместо восстановленных цифр появились бы нули.
			//
			// Флаг сохраняется и возвращается, а не сбрасывается в false:
			// Apply вызывается и из обработчика отмены, который сам может
			// идти изнутри обновления панели.
			bool wasUpdating = _updatingFromShape;
			_updatingFromShape = true;

			try
			{
				editor.SetValue(digits);

				valueEditor.Decimals = digits;
				valueEditor.SetValue(shape.GetDisplayValue());
			}
			finally
			{
				_updatingFromShape = wasUpdating;
			}

			RequestCanvasRefresh?.Invoke();
		}

		editor.ValueChanged = v =>
		{
			if (_updatingFromShape)
				return;

			int after = Math.Clamp((int)MathF.Round(v), 0, 6);
			int before = shape.Decimals;

			// Ввод «3.4» округляется до тех же 3 знаков — считать это
			// изменением значило бы засорять историю пустыми шагами.
			if (before == after)
				return;

			Apply(after);

			UndoStackForShape()?.Push(
				new PropertyChangeAction<int>(
					L.T("Digits"),
					before,
					after,
					Apply));

			// Состав строк панели не меняется — только подпись на холсте.
			NotifyShapeChanged();
		};

		_shapeParamRefreshers.Add(() => editor.SetValue(shape.Decimals));

		p.Content.Add(new SkPropertyRow("digits_row", L.T("Digits"), editor));
	}

	/// <summary>
	/// Оставляет видимыми только строки с перечисленными именами.
	/// Безымянные элементы (заголовки-разделители, строки без Name) скрываются:
	/// они относятся к режимам, которых у этой фигуры нет.
	/// </summary>
	private static void KeepOnlyRows(SkCollapsePanel panel, params string[] keepNames)
	{
		foreach (var child in panel.Content.Children)
			child.Visible = Array.IndexOf(keepNames, child.Name) >= 0;
	}

	#endregion

	#region Панель Image

	private SkCollapsePanel BuildImagePanel(ImageShape shape)
	{
		var p = CreatePanel("Image");

		void RefreshPreviewOnly()
		{
			shape.EnsureOriginalBitmap();
			shape.InvalidateAdjustmentPaint();

			// Перерисовать нужно холст, а не панель свойств.
			RequestCanvasRefresh?.Invoke();
		}

		void CommitAdjustments()
		{
			// Запекание меняет пиксели и размер эталона — нужен снимок содержимого.
			var action = ImageContentAction.Capture("Apply", shape);

			shape.CommitAdjustments();

			if (action != null)
			{
				action.CaptureAfter();
				UndoStackForShape()?.Push(action);
			}

			NotifyShapeChanged();
			Invalidate();
		}

		void ResetAdjustments()
		{
			if (shape.Adjustments.IsIdentity)
				return;

			var action = ImageAdjustmentsAction.Capture(shape);

			shape.Adjustments.Reset();
			shape.InvalidateAdjustmentPaint();

			action.CaptureAfter();
			UndoStackForShape()?.Push(action);

			RefreshPreviewOnly();
		}

		SkPropertyRow CreateSliderRow(
			string title,
			float min,
			float max,
			float value,
			float increment,
			int decimals,
			Action<float> setter,
			bool integerMode = false,
			float? step = null,
			string? suffix = null)
		{
			var slider = new SkSlideBar
			{
				Minimum = min,
				Maximum = max,
				SmallChange = increment,
				IntegerMode = integerMode,
				SnapToStep = true,
				Step = step ?? increment,
				Decimals = decimals,
				ShowValueLabel = false,
				Bounds = new SKRect(0, 0, 150, 24)
			};

			var numeric = new SkNumericUpDown
			{
				Minimum = min,
				Maximum = max,
				Increment = increment,
				Decimals = decimals,
				Bounds = new SKRect(0, 0, 76, 24)
			};

			bool updating = false;

			void SetBoth(float v, bool fromSlider)
			{
				if (updating)
					return;

				updating = true;

				try
				{
					if (integerMode)
						v = MathF.Round(v);

					if (step.HasValue && step.Value > 0.0001f)
						v = min + MathF.Round((v - min) / step.Value) * step.Value;

					v = Math.Clamp(v, min, max);

					slider.SetValue(v, false);
					numeric.SetValue(v, true);

					// Шаг истории на каждое изменение; серия по ОДНОМУ
					// ползунку склеится в один. Ключом служит заголовок
					// строки: без него сливались бы и разные ползунки,
					// и отмена яркости откатывала бы заодно контраст.
					var action = ImageAdjustmentsAction.Capture(shape, (shape, title));

					setter(v);

					action.CaptureAfter();
					UndoStackForShape()?.Push(action);

					RefreshPreviewOnly();
				}
				finally
				{
					// finally, иначе исключение внутри setter навсегда заблокировало бы
					// ползунок флагом updating.
					updating = false;
				}
			}

			slider.SetValue(value, false);
			numeric.SetValue(value, true);

			slider.ValueChanged = v => SetBoth(v, true);
			numeric.ValueChanged = v => SetBoth(v, false);

			return new SkPropertyRow(L.T(title), new SkControl[] { slider, numeric });
		}

		SkPropertyRow CreateActionRow(params SkControl[] controls)
			=> new SkPropertyRow(string.Empty, controls);

		SkPropertyRow BuildBrightnessRow() => CreateSliderRow(
			"Brightness", -100f, 100f, shape.Adjustments.Brightness * 100f, 1f, 0,
			v => shape.Adjustments.Brightness = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildContrastRow() => CreateSliderRow(
			"Contrast", -100f, 100f, shape.Adjustments.Contrast * 100f, 1f, 0,
			v => shape.Adjustments.Contrast = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildExposureRow() => CreateSliderRow(
			"Exposure", -2f, 2f, shape.Adjustments.Exposure, 0.05f, 2,
			v => shape.Adjustments.Exposure = v, integerMode: false, step: 0.05f);

		SkPropertyRow BuildSharpnessRow() => CreateSliderRow(
			"Sharpness", 0f, 100f, shape.Adjustments.Sharpness * 100f, 1f, 0,
			v => shape.Adjustments.Sharpness = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildBwRow() => CreateSliderRow(
			"B/W", 0f, 100f, shape.Adjustments.BlackWhite * 100f, 1f, 0,
			v => shape.Adjustments.BlackWhite = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildShadowsRow() => CreateSliderRow(
			"Shadows", -100f, 100f, shape.Adjustments.Shadows * 100f, 1f, 0,
			v => shape.Adjustments.Shadows = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildHueRow() => CreateSliderRow(
			"Hue", -180f, 180f, shape.Adjustments.Hue, 1f, 0,
			v => shape.Adjustments.Hue = v, integerMode: true, step: 1f);

		SkPropertyRow BuildTemperatureRow() => CreateSliderRow(
			"Temperature", -100f, 100f, shape.Adjustments.Temperature * 100f, 1f, 0,
			v => shape.Adjustments.Temperature = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildTintRow() => CreateSliderRow(
			"Tint", -100f, 100f, shape.Adjustments.Tint * 100f, 1f, 0,
			v => shape.Adjustments.Tint = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildVibrancyRow() => CreateSliderRow(
			"Vibrancy", -100f, 100f, shape.Adjustments.Vibrancy * 100f, 1f, 0,
			v => shape.Adjustments.Vibrancy = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildNoiseReductionRow() => CreateSliderRow(
			"Noise reduction", 0f, 100f, shape.Adjustments.NoiseReduction * 100f, 1f, 0,
			v => shape.Adjustments.NoiseReduction = v / 100f, integerMode: true, step: 1f);

		SkPropertyRow BuildInvertRow()
		{
			var check = new SkCheckBox { Checked = shape.Adjustments.Invert };

			check.CheckedChanged += value =>
			{
				if (_updatingFromShape)
					return;

				bool before = shape.Adjustments.Invert;

				if (before == value)
					return;

				Apply(value);

				UndoStackForShape()?.Push(
					new PropertyChangeAction<bool>(
						L.T("Invert"),
						before,
						value,
						Apply));

				NotifyShapeChanged();
			};

			// Применение вынесено отдельно: тем же путём ходит отмена.
			// Без сброса закэшированного SKPaint картинка на холсте
			// осталась бы прежней — коррекция пересобирается только
			// по изменению версии настроек.
			void Apply(bool value)
			{
				shape.Adjustments.Invert = value;
				shape.InvalidateAdjustmentPaint();

				_updatingFromShape = true;

				try
				{
					check.Checked = value;
				}
				finally
				{
					_updatingFromShape = false;
				}

				RequestCanvasRefresh?.Invoke();
			}

			_shapeParamRefreshers.Add(() => check.Checked = shape.Adjustments.Invert);

			return new SkPropertyRow("invert_row", L.T("Invert"), check);
		}

		SkPropertyRow BuildActualResolutionRow()
		{
			var check = new SkCheckBox { Checked = shape.ActualResolution };

			// CheckedChanged — событие, а не поле-делегат: присваивание
			// через = здесь запрещено, только подписка.
			check.CheckedChanged += value =>
			{
				if (_updatingFromShape)
					return;

				bool before = shape.ActualResolution;

				if (before == value)
					return;

				shape.ActualResolution = value;

				UndoStackForShape()?.Push(
					new PropertyChangeAction<bool>(
						L.T("Actual dpi"),
						before,
						value,
						v =>
						{
							shape.ActualResolution = v;
							RequestCanvasRefresh?.Invoke();
						}));

				// Меняется только показ: пиксели растра те же, перестраивать
				// панель незачем, достаточно перерисовать холст.
				RequestCanvasRefresh?.Invoke();
				NotifyShapeChanged();
			};

			_shapeParamRefreshers.Add(() => check.Checked = shape.ActualResolution);

			return new SkPropertyRow("actual_dpi_row", L.T("Actual dpi"), check);
		}

		void RebuildPanelContent()
		{
			p.Content.ClearChildren();

			p.Content.Add(BuildActualResolutionRow());
			p.Content.Add(BuildInvertRow());
			p.Content.Add(BuildBrightnessRow());
			p.Content.Add(BuildContrastRow());
			p.Content.Add(BuildExposureRow());
			p.Content.Add(BuildSharpnessRow());
			p.Content.Add(BuildBwRow());
			p.Content.Add(BuildShadowsRow());
			p.Content.Add(BuildHueRow());
			p.Content.Add(BuildTemperatureRow());
			p.Content.Add(BuildTintRow());
			p.Content.Add(BuildVibrancyRow());
			p.Content.Add(BuildNoiseReductionRow());

			var applyBut = new SkButton
			{
				Text = "Apply",
				Bounds = new SKRect(0, 0, 220, 28)
			};

			var resetBut = new SkButton
			{
				Text = "Reset",
				Bounds = new SKRect(0, 0, 220, 28)
			};

			applyBut.Click += () =>
			{
				CommitAdjustments();
				RebuildPanelContent();
			};

			resetBut.Click += () =>
			{
				ResetAdjustments();
				RebuildPanelContent();
			};


			p.Content.Add(CreateActionRow(applyBut, resetBut));
			p.Arrange();
		}

		RebuildPanelContent();
		return p;
	}

	#endregion

	#region Служебные методы

	private void OnSelectOtherShapeBut_Click()
	{
		ShapePickRequested?.Invoke(ShapePickTarget.TextPath);
	}

	private void OnSelectContainerBut_Click()
	{
		ShapePickRequested?.Invoke(ShapePickTarget.TextContainer);
	}

	private void NotifyShapeChanged(bool rebuildPanel = false, bool refreshPanel = true)
	{
		if (rebuildPanel)
			RebuildForSelection();
		else if (refreshPanel && IsHandleCreated)
			Invalidate();

		RequestCanvasRefresh?.Invoke();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			MainForm.sel.SelectionChanged -= OnSelectionChanged;
			MainForm.sel.SelectionGeometryChanged -= OnSelectionGeometryChanged;
			SkUiTheme.ThemeChanged -= OnThemeChanged;

			// Событие статическое, поэтому подписка удерживала бы панель
			// в памяти до конца работы программы.
			Page.UnitsChanged -= OnUnitsChanged;

			_gdiBuffer?.Dispose();
			_gdiBuffer = null;

			_backBuffer?.Dispose();
			_backBuffer = null;

			_pixelBuffer = null;
		}

		base.Dispose(disposing);
	}

	private static SKBitmap? LoadUiBitmap(string fileName)
	{
		try
		{
			string baseDir = AppDomain.CurrentDomain.BaseDirectory;
			string path = Path.Combine(baseDir, "Resources", fileName);

			if (!System.IO.File.Exists(path))
				return null;

			return SKBitmap.Decode(path);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Переключает род заливки и сбрасывает то, что от него зависит:
	/// концы градиента считаются заново по габаритам фигуры, а узор
	/// подставляется первый из библиотеки, если своего ещё нет.
	/// </summary>
	private static void SetFillKind(FillStyle fill, FillKind kind)
	{
		fill.Kind = kind;
		fill.FillTransformInitialized = false;
		fill.StartPoint = SKPoint.Empty;
		fill.EndPoint = SKPoint.Empty;

		if (kind != FillKind.Pattern)
			return;

		bool needInit = string.IsNullOrWhiteSpace(fill.PatternId)
			|| PatternLibrary.Find(fill.PatternId)?.Bitmap == null;

		if (needInit)
			InitializeFillWithFirstPattern(fill);
	}

	private SkFlowLayout BuildFillTypeButtons(FillStyle fill)
	{
		var row = new SkFlowLayout
		{
			Direction = SkFlowDirection.Horizontal,
			Gap = 6f,
			AutoHeight = true,

			// Семь кнопок по 36 точек с зазорами требуют около 290 точек,
			// а панель свойств бывает и уже. Перенос — единственный способ
			// не потерять последние кнопки: сжимать их означало бы делать
			// значки нечитаемыми, а горизонтальная прокрутка внутри строки
			// свойств неуместна.
			WrapContents = true,

			Bounds = new SKRect(0, 0, 260, 38)
		};

		void addButton(string png, string tip, FillKind kind)
		{
			var btn = new SkPictureButton
			{
				Bounds = new SKRect(0, 0, 36, 36),
				Image = LoadUiBitmap(png),
				ToolTipText = tip,
				Selected = fill.Kind == kind
			};

			btn.Click += _ =>
			{
				// Род заливки — общий параметр: он есть у любой фигуры,
				// поэтому применяется ко всему выделению без отбора.
				var action = ShapeStyleAction.Capture("Fill type", Targets);

				foreach (var target in Targets)
					SetFillKind(target.Fill, kind);

				action.CaptureAfter();

				if (action.HasChanges())
					UndoStackForShape()?.Push(action);

				RebuildForSelection();
				NotifyShapeChanged();
			};

			row.Add(btn);
		}

		addButton("fill_none.png", "No fill", FillKind.None);
		addButton("fill_uniform.png", "Solid fill", FillKind.Solid);
		addButton("fountain_fill_linear.png", "Linear gradient", FillKind.LinearGradient);
		addButton("fountain_fill_radial.png", "Radial gradient", FillKind.RadialGradient);
		addButton("fountain_fill_square.png", "Rectangular gradient", FillKind.RectangularGradient);
		addButton("fill_pattern.png", "Pattern fill", FillKind.Pattern);
		addButton("fill_texture.png", "Texture fill", FillKind.Texture);

		return row;
	}

	#endregion
}

public static class ColorExtensions
{
	public static SKColor ToSKColor(this SKColorF c)
	{
		byte r = (byte)Math.Clamp((int)MathF.Round(c.Red * 255f), 0, 255);
		byte g = (byte)Math.Clamp((int)MathF.Round(c.Green * 255f), 0, 255);
		byte b = (byte)Math.Clamp((int)MathF.Round(c.Blue * 255f), 0, 255);
		byte a = (byte)Math.Clamp((int)MathF.Round(c.Alpha * 255f), 0, 255);
		return new SKColor(r, g, b, a);
	}
}