using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

// О псевдонимах см. пояснение в начале AvTheme.cs.
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Border = Avalonia.Controls.Border;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Color = Avalonia.Media.Color;
using ComboBox = Avalonia.Controls.ComboBox;
using Control = Avalonia.Controls.Control;
// Page и GradientStop есть и у Avalonia, и у VDraw. Здесь нужны наши:
// Page — страница документа, GradientStop — узел градиента в модели заливки.
using GradientStop = VDraw.Shapes.GradientStop;
using Page = VDraw.Documents.Page;
using FontFamily = Avalonia.Media.FontFamily;
using UserControl = Avalonia.Controls.UserControl;
using Orientation = Avalonia.Layout.Orientation;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using TextBox = Avalonia.Controls.TextBox;

namespace VDraw.Ui.Avalon;

/// <summary>
/// Панель свойств на Avalonia — замена SkPropertyPanelControl.
///
/// Вся работа с моделью перенесена из Skia-версии без изменений: те же
/// разделы, те же правила множественного выделения, та же запись в историю.
/// Заменён только слой контролов — вместо нарисованных через Skia полей
/// и кнопок используются настоящие контролы Avalonia (см. AvControls.cs
/// и AvEditors.cs, их свойства повторяют Sk-аналоги).
///
/// Что от этого изменилось по сути:
///
/// 1. Раскладку, прокрутку, табуляцию, выделение текста, курсоры и фокус
///    теперь делает Avalonia. Шестьсот строк отрисовки и обработки ввода,
///    которые это воспроизводили, удалены вместе с двойной буферизацией
///    и ручным пересчётом строк.
///
/// 2. Поля ввода стали настоящими: работают системное выделение, перенос
///    по словам, автоповтор у клавиш и ввод с планшета.
///
/// 3. Тему по-прежнему задаёт SkUiTheme — на оба слоя одна палитра
///    (см. AvTheme). Панель при её смене пересобирается, как и раньше.
///
/// Сама панель не является WinForms-контролом. В окно её вставляет
/// AvPropertyPanelHost.
/// </summary>
public sealed class AvPropertyPanel : UserControl
{
	#region Поля и события

	/// <summary>Стопка разделов. Прокрутку обеспечивает _scroll.</summary>
	private readonly AvFlow _root = new()
	{
		Direction = AvFlowDirection.Vertical,
		Gap = 8,
		Pad = 8
	};

	private readonly ScrollViewer _scroll;

	/// <summary>
	/// Область с принудительно заданным вариантом темы.
	///
	/// Без неё встроенные контролы Avalonia — поле ввода, галочка,
	/// выпадающий список — оказывались невидимыми: их шаблоны берут цвета
	/// из словарей темы через DynamicResource, а внутри хоста WinForms
	/// вариант темы приложения до них не доходит, и все кисти получались
	/// пустыми. Отсюда и пустые поля: рамки, текст и значок галочки просто
	/// нечем было нарисовать. Наши собственные контролы (кнопки, числовое
	/// поле, образец цвета) при этом рисовались, потому что цвета им
	/// задаются напрямую из палитры.
	///
	/// Область задаёт вариант явно и ни от чего не зависит.
	/// </summary>
	private readonly ThemeVariantScope _themeScope;

	private Control? _focused;
	private Shape? _boundShape;

	/// <summary>
	/// Идёт ли ввод в одном из полей панели.
	///
	/// Нужно обработчику сочетаний клавиш в MainForm: пока пользователь
	/// набирает число в поле ширины, буква не должна переключать инструмент.
	/// WinForms о контролах Avalonia ничего не знает и видит только хост,
	/// поэтому ответ даёт панель.
	///
	/// AvNumTextBox наследуется от AvTextBox и покрывается этой проверкой.
	/// Выпадающий список сюда не входит: текст в нём не набирают.
	/// </summary>
	public bool IsTextEditing => _focused is AvTextBox;

	/// <summary>
	/// Готова ли панель к работе. Заменяет IsHandleCreated Skia-версии:
	/// смысл тот же — до появления на экране пересобирать нечего.
	/// </summary>
	public bool IsAttached { get; private set; }

	public event Action? RequestCanvasRefresh;
	public event Action<ShapePickTarget>? ShapePickRequested;

	private AvNumericUpDown? _xEditor;
	private AvNumericUpDown? _yEditor;
	private AvNumericUpDown? _wEditor;
	private AvNumericUpDown? _hEditor;
	private AvNumericUpDown? _rotEditor;
	private AvNumericUpDown? _layerEditor;

	private bool _updatingFromShape;

	/// <summary>
	/// Обновление полей с параметрами конкретной фигуры из модели.
	///
	/// Список, а не фиксированные поля: набор параметров зависит от типа
	/// фигуры и меняется при каждой смене выделения. Заполняется при сборке
	/// панели, очищается перед ней.
	/// </summary>
	private readonly List<Action> _shapeParamRefreshers = new();

	private AvTextBox? _nameEditor;

	/// <summary>
	/// Свёрнутость разделов в текущем сеансе.
	///
	/// Статическая и общая на все фигуры: пользователь сворачивает раздел
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

	private AvPatternGridEditor? _strokePatternEditor;
	private AvDropDownBox? _strokeTypeDropDown;
	private AvNumericUpDown? _strokePatternLenEditor;

	#endregion

	#region Создание, подключение и раскладка

	/// <summary>
	/// Оформление берётся от обычного UserControl: иначе Avalonia искала бы
	/// шаблон по имени нашего класса, не нашла бы его и не показала бы
	/// содержимое панели вовсе.
	/// </summary>
	protected override Type StyleKeyOverride => typeof(UserControl);

	public AvPropertyPanel()
	{
		Background = AvTheme.PanelBack;
		Focusable = true;

		_scroll = new ScrollViewer
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = _root
		};

		_themeScope = new ThemeVariantScope
		{
			RequestedThemeVariant = CurrentVariant(),
			Child = _scroll
		};

		Content = _themeScope;

		// Фокус отслеживается на уровне панели: события GotFocus и LostFocus
		// всплывают от любого вложенного поля, поэтому одной подписки хватает
		// на все редакторы, сколько бы их ни пересобиралось.
		GotFocus += (_, e) => _focused = e.Source as Control;

		LostFocus += (_, e) =>
		{
			if (ReferenceEquals(_focused, e.Source))
				_focused = null;
		};
	}

	/// <summary>
	/// Подписывается на события модели и собирает панель впервые.
	///
	/// Отдельным методом, а не в конструкторе: хост создаёт панель заранее,
	/// а подписки должны появиться тогда, когда панель действительно попала
	/// в окно — иначе она держала бы на себе статические события приложения,
	/// ничего не показывая.
	/// </summary>
	public void Attach()
	{
		if (IsAttached)
			return;

		IsAttached = true;

		MainForm.sel.SelectionChanged += OnSelectionChanged;
		MainForm.sel.SelectionGeometryChanged += OnSelectionGeometryChanged;
		SkUiTheme.ThemeChanged += OnThemeChanged;

		// Смена единиц меняет ЧИСЛА почти в каждой строке панели —
		// координаты, размеры, толщину линии, отступы. Пересчёт по месту
		// пришлось бы дописывать в каждый редактор и не забывать при
		// добавлении новых, поэтому панель просто пересобирается целиком.
		Page.UnitsChanged += OnUnitsChanged;

		RebuildForSelection();
	}

	/// <summary>
	/// Снимает подписки. Вызывается из Dispose хоста: события статические,
	/// и без этого панель осталась бы в памяти до конца работы программы.
	/// </summary>
	public void Detach()
	{
		if (!IsAttached)
			return;

		IsAttached = false;

		MainForm.sel.SelectionChanged -= OnSelectionChanged;
		MainForm.sel.SelectionGeometryChanged -= OnSelectionGeometryChanged;
		SkUiTheme.ThemeChanged -= OnThemeChanged;
		Page.UnitsChanged -= OnUnitsChanged;
	}

	/// <summary>
	/// Пересчёт раскладки. Заменяет одноимённый метод Skia-версии, где
	/// приходилось дважды обходить дерево и вручную считать высоты;
	/// в Avalonia достаточно пометить раскладку устаревшей.
	///
	/// Метод сохранён, а не вычищен из трёх десятков мест вызова, потому что
	/// смысл у них прежний: «высота содержимого изменилась, пересчитай».
	/// </summary>
	private void ArrangeUi()
	{
		_root.InvalidateMeasure();
		InvalidateMeasure();
	}

	#endregion


	#region Создание и жизненный цикл




	private void OnSelectionChanged()
	{
		if (!IsAttached)
			return;

		RebuildForSelection();
	}

	private static ThemeVariant CurrentVariant()
		=> SkUiTheme.Current.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

	private void OnThemeChanged()
	{
		Background = AvTheme.PanelBack;
		_themeScope.RequestedThemeVariant = CurrentVariant();

		if (!IsAttached)
			return;

		RebuildForSelection();
		InvalidateVisual();
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
		// Ссылка на поле с вводом сбрасывается вместе с деревом.
		//
		// Пересборка выбрасывает все контролы, и прежняя ссылка указывала
		// бы на мёртвый. Для табуляции это значило бы «поле не найдено»
		// и прыжок на самое первое вместо следующего; саму же правку
		// такого контрола применить было бы уже некуда.
		_focused = null;

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
			p.Content.Add(new AvLabel
			{
				Text = "No object selected",
				Height = 20
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

				if (IsAttached)
					InvalidateVisual();

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

		if (IsAttached)
			InvalidateVisual();
	}

	private void OnSelectionGeometryChanged()
	{
		if (!IsAttached)
			return;

		RebuildForSelection();
	}

	/// <summary>
	/// Создаёт раздел панели, восстанавливая его свёрнутость из сеанса.
	/// Идентификатор служит и ключом состояния, и строкой для перевода.
	/// </summary>
	private static AvCollapseSection CreatePanel(string id, bool expandedByDefault = true)
	{
		bool expanded = _panelExpanded.TryGetValue(id, out bool stored)
			? stored
			: expandedByDefault;

		var panel = new AvCollapseSection
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

		// Группа разворачивается до детей: оформление у неё своё, но
		// рисуется оно детьми, и назначенное самой группе никуда
		// не попадало.
		foreach (var shape in ShapeFormatClipboard.ExpandForStyling(OtherTargets<Shape>()))
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

		// Запас на случай, когда панель построена, а список выделения пуст.
		// Такое расхождение возможно: панель строится по sel.Primary,
		// а значения читает по sel.Selected, и обновляются они не одним
		// присваиванием. Без запасного пути все поля оказывались пустыми
		// при явно выделенной фигуре.
		var targets = Targets.Count > 0
			? Targets
			: (_boundShape != null ? new[] { _boundShape } : Array.Empty<Shape>());

		foreach (var shape in targets)
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
					InvalidateVisual();
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
	private void BindNumeric<T>(AvNumericUpDown editor, Func<T, float> read, float scale = 1f)
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
		if (!IsAttached)
			return;

		RebuildForSelection();
	}


	#endregion


	#region Панель General

	private AvCollapseSection BuildGeneralPanel(Shape shape)
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
		_xEditor = new AvNumericUpDown() { AllowDecimal = true, Decimals = 3 };
		BindNumeric<Shape>(_xEditor, s => s.GetBounds().Left);

		_yEditor = new AvNumericUpDown() { AllowDecimal = true, Decimals = 3 };
		BindNumeric<Shape>(_yEditor, s => s.GetBounds().Top);

		_wEditor = new AvNumericUpDown { Minimum = 0, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_wEditor, s => s.GetBounds().Width);

		_hEditor = new AvNumericUpDown { Minimum = 0, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_hEditor, s => s.GetBounds().Height);

		_rotEditor = new AvNumericUpDown { Minimum = -360, Maximum = 360, AllowDecimal = true, Decimals = 3};
		BindNumeric<Shape>(_rotEditor, s => GetRotationDegrees(s.LocalTransform));

		// Слой у нескольких фигур один быть не может: он и задаёт их
		// порядок друг относительно друга. Поле остаётся видимым, но
		// выключенным — так понятнее, чем спрятать строку совсем.
		_layerEditor = new AvNumericUpDown
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
		_nameEditor = new AvTextBox
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
					InvalidateVisual();
				},
				mergeKey: (shape, "Shape.Name")));

			NotifyShapeChanged(rebuildPanel: false);
		};

		p.Content.Add(new AvPropertyRow(L.T("Name"), _nameEditor));
		p.Content.Add(new AvPropertyRow(L.T("X"), _xEditor));
		p.Content.Add(new AvPropertyRow(L.T("Y"), _yEditor));
		p.Content.Add(new AvPropertyRow(L.T("Width"), _wEditor));
		p.Content.Add(new AvPropertyRow(L.T("Height"), _hEditor));
		p.Content.Add(new AvPropertyRow(L.T("Rotation"), _rotEditor));
		p.Content.Add(new AvPropertyRow(L.T("Layer"), _layerEditor));

		AppendShapeSpecificRows(p, shape);

		p.Arrange();

		return p;
	}

	public void RefreshFromBoundShape()
	{
		if (!IsAttached)
			return;

		if (_boundShape == null)
			return;

		if (_focused is AvTextBox || _focused is AvNumTextBox || _focused is AvNumericUpDown)
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

		InvalidateVisual();
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

	private static string GridRowToPattern(AvPatternGridEditor editor, SKColor frontColor)
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
		AvPatternGridEditor editor,
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

	private AvCollapseSection BuildStrokePanel(Shape shape)
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
					InvalidateVisual();
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

		var enabled = new AvCheckBox { Checked = shape.Stroke.Enabled };
		AvPropertyRow enabledRow = new AvPropertyRow("enabled_row", L.T("Enabled"), enabled);
		p.Content.Add(enabledRow);

		// ------------------------------------------------------------------------
		// Цвет контура
		// ------------------------------------------------------------------------

		// Строка добавляется здесь, а не в блоке компоновки ниже: порядок
		// строк равен порядку добавления, и цвет должен идти сразу за Enabled.
		// Под UpdateRowsVisibility она попадает автоматически: имя у неё
		// не enabled_row, значит при выключенной обводке она скроется вместе
		// со всеми остальными.
		var strokeColor = new AvColorButton
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
			InvalidateVisual();
		};

		p.Content.Add(new AvPropertyRow("stroke_color_row", L.T("Color"), strokeColor));

		// Вынесено в функцию: то же самое нужно вызвать при откате истории.
		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "enabled_row")
					p.Content.Children[i].IsVisible = shape.Stroke.Enabled;
			}
		}

		// ------------------------------------------------------------------------
		// Толщина
		// ------------------------------------------------------------------------

		var thick = new AvNumericUpDown
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

		var strokeType = new AvDropDownBox();
		_strokeTypeDropDown = strokeType;

		void RefreshStrokeTypeItems()
		{
			strokeType.SetItems(
				StrokePatternLibrary.Items.Select(x => new AvDropDownItem(
					x.Id,
					x.DisplayName,
					x.PreviewBitmap)));

			var current = StrokePatternLibrary.FindByPattern(shape.Stroke.Pattern);
			strokeType.SetSelected(current?.Id ?? StrokePatternLibrary.Items[0].Id);
		}

		const int StrokePatternMinLen = 2;
		const int StrokePatternMaxLen = 64;

		var strokePatternLen = new AvNumericUpDown
		{
			Minimum = StrokePatternMinLen,
			Maximum = StrokePatternMaxLen,
			Increment = 1,
			Decimals = 0
		};
		_strokePatternLenEditor = strokePatternLen;

		var strokePattern = new AvPatternGridEditor
		{
			Height = 34,
			FrontColor = SKColors.Black,
			BackColor = SKColors.White
		};
		_strokePatternEditor = strokePattern;

		var patternLenUoM = new AvNumericUpDown
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

		var startSize = new AvNumericUpDown
		{
			Minimum = GlyphSizeMin,
			Maximum = GlyphSizeMax,
			Increment = 0.5f,
			Decimals = 2
		};

		var endSize = new AvNumericUpDown
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
		var start = new AvDropDownBox
		{
			ItemHeight = 32f,
			PreviewBoxSize = 26f,
			DropDownMaxHeight = 340f,
			Width = 160, Height = 32
		};
		start.SetItems(LineGlyphLibrary.ToAvDropDownItems());
		start.SetSelected(shape.Stroke.StartGlyph);

		var end = new AvDropDownBox
		{
			ItemHeight = 32f,
			PreviewBoxSize = 26f,
			DropDownMaxHeight = 340f,
			Width = 160, Height = 32
		};
		end.SetItems(LineGlyphLibrary.ToAvDropDownItems());
		end.SetSelected(shape.Stroke.EndGlyph);

		var addPatternBut = new AvButton { Text = L.T("Add pattern"), Width = 90, Height = 30 };
		var deletePatternBut = new AvButton { Text = L.T("Delete pattern"), Width = 90, Height = 30 };

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
			InvalidateVisual();
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
			InvalidateVisual();
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

			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
			RequestCanvasRefresh?.Invoke();
		};

		var patternButtonsRow = new AvFlow
		{
			Direction = AvFlowDirection.Horizontal,
			Gap = 8f,
			AutoHeight = true
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

		p.Content.Add(new AvPropertyRow(
			L.T("Thickness") + strokeUnit, thick));
		p.Content.Add(new AvPropertyRow(L.T("Stroke"), strokeType) { MinHeight = 34 });

		// Заголовок группы: ниже идут четыре строки, которые по отдельности
		// читаются как случайный набор — «Size», «Length», сетка и кнопки.
		// Жирная подпись связывает их в один блок и заодно объясняет,
		// к чему относятся короткие названия.
		// Подпись кладётся прямо в содержимое, а не в AvPropertyRow:
		// строка отвела бы под название левую колонку в 96 пикселей,
		// и заголовок блока встал бы с отступом наравне со значениями,
		// вместо того чтобы начинаться от края.
		p.Content.Add(new AvLabel
		{
			Text = L.T("Pattern"),
			Bold = true,
			FontSize = 14f,
			ForeColor = SkUiTheme.Current.Text
		});

		// Уточнение контекста: в панели Text «Size» — это кегль шрифта,
		// а здесь размер сетки штриха, и по-русски это разные слова.
		p.Content.Add(new AvPropertyRow(L.TIn("pattern", "Size"), strokePatternLen));
		p.Content.Add(new AvPropertyRow(
			L.T("Length") + strokeUnit, patternLenUoM));   // длина цикла в единицах страницы
		p.Content.Add(new AvPropertyRow(string.Empty, strokePattern) { MinHeight = 34 });
		p.Content.Add(new AvPropertyRow(string.Empty, patternButtonsRow) { Indent = 0f, LabelWidth = 0f, IndentLabel = false });
		p.Content.Add(new AvPropertyRow(L.T("Start"), start) { MinHeight = 34 });
		p.Content.Add(new AvPropertyRow(L.T("Start size"), startSize));
		p.Content.Add(new AvPropertyRow(L.T("End"), end) { MinHeight = 34 });
		p.Content.Add(new AvPropertyRow(L.T("End size"), endSize));

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

		var tempEditor = new AvPatternGridEditor
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
	private AvCollapseSection BuildShadowPanel(Shape shape)
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
					InvalidateVisual();
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

		var enabled = new AvCheckBox { Checked = shadow.Enabled };
		p.Content.Add(new AvPropertyRow("shadow_enabled_row", L.T("Enabled"), enabled));

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "shadow_enabled_row")
					p.Content.Children[i].IsVisible = shadow.Enabled;
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
			InvalidateVisual();
		};

		// ------------------------------------------------------------------------
		// Цвет
		// ------------------------------------------------------------------------

		var color = new AvColorButton
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
			InvalidateVisual();
		};

		p.Content.Add(new AvPropertyRow("shadow_color_row", L.T("Colour"), color));

		// ------------------------------------------------------------------------
		// Мягкость
		// ------------------------------------------------------------------------

		var softness = new AvSlideBar
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
			InvalidateVisual();
		};

		p.Content.Add(new AvPropertyRow("shadow_softness_row", L.T("Softness"), softness));

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
	private AvCollapseSection BuildTransparencyPanel(Shape shape)
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
					InvalidateVisual();
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

		var enabled = new AvCheckBox { Checked = tr.Enabled };
		p.Content.Add(new AvPropertyRow("transparency_enabled_row", L.T("Enabled"), enabled));

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "transparency_enabled_row")
					p.Content.Children[i].IsVisible = tr.Enabled;
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
			InvalidateVisual();
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
			var slider = new AvSlideBar
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
				InvalidateVisual();
			};

			p.Content.Add(new AvPropertyRow(rowName, L.T(label), slider));
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
	private AvCollapseSection BuildTransformPanel(TransformShape tool)
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
					InvalidateVisual();
				},
				mergeKey: (tool, key)));
		}

		// ------------------------------------------------------------------------
		// Число шагов
		// ------------------------------------------------------------------------

		var steps = new AvNumericUpDown
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
			InvalidateVisual();
		};

		p.Content.Add(new AvPropertyRow("transform_steps_row", L.T("Steps"), steps));

		// ------------------------------------------------------------------------
		// Распределение расстояний между шагами
		// ------------------------------------------------------------------------

		var bias = new AvSlideBar
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
			InvalidateVisual();
		};

		p.Content.Add(new AvPropertyRow("transform_bias_row", L.T("Spacing"), bias));

		// ------------------------------------------------------------------------
		// Привязанные фигуры
		// ------------------------------------------------------------------------

		// Только для сведения: пересобрать привязку можно, создав
		// инструмент заново, а перевыбор одной из фигур на месте потребовал
		// бы отдельного режима указания и своего шага истории.
		p.Content.Add(new AvPropertyRow("transform_from_row", L.T("From"), new AvLabel
			{
				Text = tool.ShapeA?.Name ?? "-",
				Width = 150, Height = 20
			}));

		p.Content.Add(new AvPropertyRow("transform_to_row", L.T("To"), new AvLabel
			{
				Text = tool.ShapeB?.Name ?? "-",
				Width = 150, Height = 20
			}));

		return p;
	}

	private AvCollapseSection BuildFillPanel(Shape shape)
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
					InvalidateVisual();
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

		var enabled = new AvCheckBox { Checked = fill.Enabled };
		AvPropertyRow enabledRow = new AvPropertyRow("enabled_row", L.T("Enabled"), enabled);

		p.Content.Add(enabledRow);

		void UpdateRowsVisibility()
		{
			for (int i = 0; i < p.Content.Children.Count; i++)
			{
				if (p.Content.Children[i].Name != "enabled_row")
					p.Content.Children[i].IsVisible = fill.Enabled;
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

			// Пересчёт раскладки обязателен. AvFlow.Arrange расставляет
			// только видимые элементы, поэтому у скрытых строк Bounds остаются
			// от прошлого прохода. Без Arrange включённые строки лежат друг на
			// друге за границами панели и обрезаются при отрисовке.
			ArrangeUi();

			NotifyShapeChanged();
			InvalidateVisual();
		};


		// ------------------------------------------------------------------------
		// Тип заливки
		// ------------------------------------------------------------------------

		var fillTypeButtons = BuildFillTypeButtons(fill);
		p.Content.Add(new AvPropertyRow("fill_type_row", L.T("Type"), fillTypeButtons) { MinHeight = 38 });

		// ------------------------------------------------------------------------
		// Правило заполнения (только для кривых)
		// ------------------------------------------------------------------------

		if (shape is CurveShape cs)
		{
			var fillGaps = new AvCheckBox
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
				InvalidateVisual();
			};

			p.Content.Add(new AvPropertyRow(string.Empty, fillGaps));
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
		AvCheckBox MakeToggle(
			string text,
			string historyName,
			string key,
			Func<FillStyle, bool> get,
			Action<FillStyle, bool> set)
		{
			var box = new AvCheckBox { Text = text, Checked = get(fill) };

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
				InvalidateVisual();
			};

			return box;
		}

		switch (fill.Kind)
		{
			case FillKind.Solid:
				{
					var color = new AvColorButton
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
						InvalidateVisual();
					};

					p.Content.Add(new AvPropertyRow("fill_color_row", L.T("Color"), color));
					break;
				}

			case FillKind.LinearGradient:
			case FillKind.RadialGradient:
			case FillKind.RectangularGradient:
				{
					var stopInfos = fill.Stops
						.Select(s => new GradientStopInfo(s.Offset, s.Color))
						.ToList();

					var color = new AvColorButton
					{
						Text = "Stop color",
						Color = stopInfos.Count > 0 ? stopInfos[0].Color : SKColors.White
					};

					var grad = new AvGradientEditor();
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
						InvalidateVisual();
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
						InvalidateVisual();
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
						InvalidateVisual();
					};

					var rotateWithShape = MakeToggle(
						"Rotate with shape", "Rotate fill with shape", "Fill.RotateWithShape",
						f => f.RotateWithShape, (f, x) => f.RotateWithShape = x);

					var scaleWithShape = MakeToggle(
						"Scale with shape", "Scale fill with shape", "Fill.ScaleWithShape",
						f => f.ScaleWithShape, (f, x) => f.ScaleWithShape = x);

					p.Content.Add(new AvPropertyRow(L.T("Color"), color));
					p.Content.Add(new AvPropertyRow(L.T("Stops"), grad) { MinHeight = 42 });
					p.Content.Add(new AvPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new AvPropertyRow(L.T("Scale"), scaleWithShape));
					break;
				}

			case FillKind.Pattern:
				{
					var patternDrop = new AvDropDownBox();
					patternDrop.SetItems(
						PatternLibrary.Items.Select(x => new AvDropDownItem(
							x.Id,
							string.Empty,
							x.Bitmap)));

					var editor = new AvPatternGridEditor
					{
						Height = 140,
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
						InvalidateVisual();
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
						InvalidateVisual();
					};

					var fw = new AvNumericUpDown { Minimum = 2, Maximum = 256, Increment = 1, Decimals = 0 };
					fw.SetValue(fill.PatternWidth);

					var fh = new AvNumericUpDown { Minimum = 2, Maximum = 256, Increment = 1, Decimals = 0 };
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
						InvalidateVisual();
					}

					fw.ValueChanged = v => ResizePattern((int)v, fill.PatternHeight, "Pattern width");
					fh.ValueChanged = v => ResizePattern(fill.PatternWidth, (int)v, "Pattern height");

					var front = new AvColorButton { Text = "Front", Color = fill.PatternFrontColor };
					var back = new AvColorButton { Text = "Back", Color = fill.PatternBackColor };

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
						InvalidateVisual();
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
						InvalidateVisual();
					};

					var save = new AvButton { Text = "Save" };
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
							InvalidateVisual();
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
						InvalidateVisual();
					};

					var clear = new AvButton { Text = "Clear" };
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
						InvalidateVisual();
					};

					var buttons = new AvFlow
					{
						Direction = AvFlowDirection.Horizontal,
						Gap = 8f,
						AutoHeight = true
					};

					save.Width = 80;
					save.Height = 30;
					clear.Width = 80;
					clear.Height = 30;
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

					p.Content.Add(new AvPropertyRow(L.T("Pattern"), patternDrop));
					p.Content.Add(new AvPropertyRow(L.T("Width"), fw));
					p.Content.Add(new AvPropertyRow(L.T("Height"), fh));
					p.Content.Add(new AvPropertyRow(L.T("Front"), front));
					p.Content.Add(new AvPropertyRow(L.T("Back"), back));
					p.Content.Add(new AvPropertyRow(L.T("Edit"), editor) { MinHeight = 140 });
					p.Content.Add(new AvPropertyRow(string.Empty, buttons));
					p.Content.Add(new AvPropertyRow(L.T("Tiling"), tiling));
					p.Content.Add(new AvPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new AvPropertyRow(L.T("Scale"), scaleWithShape));
					p.Content.Add(new AvPropertyRow(L.T("Smooth"), smoothTexture));

					break;
				}

			case FillKind.Texture:
				{
					var texDrop = new AvDropDownBox();
					texDrop.SetItems(
						TextureLibrary.Items.Select(x => new AvDropDownItem(
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
						InvalidateVisual();
					};

					var load = new AvButton { Text = "Load texture" };
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

					var buttons = new AvFlow
					{
						Direction = AvFlowDirection.Horizontal,
						Gap = 8f,
						AutoHeight = true
					};

					load.Width = 120;
					load.Height = 30;
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

					p.Content.Add(new AvPropertyRow(L.T("Texture"), texDrop));
					p.Content.Add(new AvPropertyRow(string.Empty, buttons));
					p.Content.Add(new AvPropertyRow(L.T("Tiling"), tiling));
					p.Content.Add(new AvPropertyRow(L.T("Rotate"), rotateWithShape));
					p.Content.Add(new AvPropertyRow(L.T("Scale"), scaleWithShape));
					p.Content.Add(new AvPropertyRow(L.T("Smooth"), smoothTexture));

					break;
				}
		}

		UpdateRowsVisibility();

		p.Arrange();
		return p;
	}

	#endregion

	#region Панель Text

	private AvCollapseSection BuildTextPanel(TextShape shape)
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
					InvalidateVisual();
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

		var tb = new AvTextBox
		{
			Multiline = true,
			Text = shape.Text,
			Width = 160, Height = 72
		};

		tb.SizeChanged = () =>
		{
			ArrangeUi();
			InvalidateVisual();
		};


		var font = new AvDropDownBox
		{
			DropDownMaxHeight = 320f,
			ItemHeight = 26f
		};

		font.SetItems(SystemFontCatalog.Families
			.Select(f => new AvDropDownItem(f, f, f)));

		font.SetSelected(shape.FontFamily);

		var boldBut = new AvButton { Text = "B", Checked = shape.Bold, Width = 30, Height = 26 };
		var italicBut = new AvButton { Text = "I", Checked = shape.Italic, Width = 30, Height = 26 };
		var underlineBut = new AvButton { Text = "U", Checked = shape.Underline, Width = 30, Height = 26 };
		var strikeBut = new AvButton { Text = "S", Checked = shape.Strikethrough, Width = 30, Height = 26 };

		var styleRow = new AvFlow
		{
			Direction = AvFlowDirection.Horizontal,
			Gap = 4f,
			AutoHeight = true
		};

		styleRow.Add(boldBut);
		styleRow.Add(italicBut);
		styleRow.Add(underlineBut);
		styleRow.Add(strikeBut);

		var fs = new AvNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = 0.1f, Maximum = 1000f };
		fs.SetValue(shape.FontSizeMm);

		var charSpace = new AvNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = -100f, Maximum = 1000f };
		charSpace.SetValue(shape.CharacterSpacingMm);

		var lineSpace = new AvNumericUpDown { Increment = 0.5f, Decimals = 2, Minimum = -100f, Maximum = 1000f };
		lineSpace.SetValue(shape.LineSpacingMm);

		// ------------------------------------------------------------------------
		// Текст внутри фигуры
		// ------------------------------------------------------------------------

		var insideOtherShape = new AvCheckBox { Checked = shape.InsideOtherShape };

		var containerBut = new AvButton
		{
			Text = shape.InsideShape != null ? shape.InsideShape.Name : "Select",
			Checked = shape.InsideShape != null
		};
		containerBut.Click += OnSelectContainerBut_Click;

		// Сетка выравнивания 3x3. Индекс = (int)VAlign * 3 + (int)HAlign,
		// поэтому порядок значений в перечислениях совпадает с порядком кнопок.
		var alignButtons = new AvButton[9];

		// Значки выравнивания: тот же порядок, что и у индексов сетки.
		string[] alignIcons =
		{
			"align-top-left", "align-top-center", "align-top-right",
			"align-middle-left", "align-middle-center", "align-middle-right",
			"align-bottom-left", "align-bottom-center", "align-bottom-right"
		};

		var alignGrid = new AvFlow
		{
			Direction = AvFlowDirection.Vertical,
			Gap = 3f,
			AutoHeight = true,
			Width = 120
		};

		for (int row = 0; row < 3; row++)
		{
			var line = new AvFlow
			{
				Direction = AvFlowDirection.Horizontal,
				Gap = 3f,
				AutoHeight = true,
				Width = 120
			};

			for (int col = 0; col < 3; col++)
			{
				int index = row * 3 + col;

				var btn = new AvButton
				{
					Width = 30,
					Height = 28,
					Padding = new Thickness(2),
					Content = new Avalonia.Controls.Image
					{
						Source = AvInterop.LoadUiBitmap(alignIcons[index], 24),
						Stretch = Stretch.Uniform
					}
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

		var wrapInsideShape = new AvCheckBox { Checked = shape.WrapInsideShape };
		var clipByShape = new AvCheckBox { Checked = shape.ClipByShape };
		var wrapOtherShapes = new AvCheckBox { Checked = shape.WrapOtherShapes };

		var columns = new AvNumericUpDown { Minimum = 0, Maximum = 10, Increment = 1, Decimals = 0 };
		columns.SetValue(shape.Columns);

		var columnGap = new AvNumericUpDown { Minimum = 0, Maximum = 1000, Increment = 0.5f, Decimals = 2 };
		columnGap.SetValue(shape.DistanceBetweenColumns);

		var padding = new AvNumericUpDown { Minimum = 0, Maximum = 1000, Increment = 0.5f, Decimals = 2 };
		padding.SetValue(shape.Padding);

		// ------------------------------------------------------------------------
		// Текст по контуру
		// ------------------------------------------------------------------------

		var alongShape = new AvCheckBox { Checked = shape.AlongShape };

		var shapeAlongBut = new AvButton
		{
			Text = shape.otherShape != null ? shape.otherShape.Name : "Select",
			Checked = shape.otherShape != null
		};
		shapeAlongBut.Click += OnSelectOtherShapeBut_Click;

		var ax = new AvNumericUpDown { Minimum = -1000000, Maximum = 1000000, Increment = 1, Decimals = 2 };
		ax.SetValue(shape.ax);

		// Decimals = 2 добавлен: без него вертикальное смещение округлялось
		// до целых, а горизонтальное — нет.
		var ay = new AvNumericUpDown { Minimum = -1000000, Maximum = 1000000, Increment = 1, Decimals = 2 };
		ay.SetValue(shape.ay);

		var tangent = new AvCheckBox { Checked = shape.tangentAlong };

		// ------------------------------------------------------------------------
		// Строки, видимость которых зависит от режима
		// ------------------------------------------------------------------------

		var containerRow = new AvPropertyRow(L.T("Inside shape"), new Control[] { insideOtherShape, containerBut });
		var alignRow = new AvPropertyRow(L.T("Alignment"), alignGrid) { MinHeight = 100 };
		var wrapInsideRow = new AvPropertyRow(L.T("Wrapping"), wrapInsideShape);
		var clipByShapeRow = new AvPropertyRow(L.T("Clipping"), clipByShape);
		var wrapOtherRow = new AvPropertyRow(L.T("Wrap others"), wrapOtherShapes);
		var columnsRow = new AvPropertyRow(L.T("Columns"), columns);
		var columnGapRow = new AvPropertyRow(L.T("Column gap"), columnGap);
		var paddingRow = new AvPropertyRow(L.T("Padding"), padding);

		var alongRow = new AvPropertyRow(L.T("Along path"), new Control[] { alongShape, shapeAlongBut });
		var axRow = new AvPropertyRow(L.T("Horizontal"), ax);
		var ayRow = new AvPropertyRow(L.T("Vertical"), ay);
		var tangentRow = new AvPropertyRow(L.T("Tangent to curve"), tangent);

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
			InvalidateVisual();
		};

		// Геттер и сеттер принимают TextShape: тот же самый сеттер нужно
		// применить и к надписям остальных выделенных объектов.
		void SetupStyleButton(
			AvButton button,
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
				InvalidateVisual();
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
			// панели надо переставить. AvFlow расставляет только видимые
			// элементы, поэтому без пересчёта включённые строки лежат друг
			// на друге за границами панели.
			ArrangeUi();
			NotifyShapeChanged();
			InvalidateVisual();
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
			InvalidateVisual();
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
				InvalidateVisual();
			};
		}

		void SetupAreaToggle(AvCheckBox box, string name, string key, Func<bool> get, Action<bool> set)
		{
			box.CheckedChanged += v =>
			{
				if (suppressUi) return;

				bool before = get();
				set(v);

				PushChange(name, before, v,
					x => { set(x); box.Checked = x; }, key);

				NotifyShapeChanged();
				InvalidateVisual();
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
			InvalidateVisual();
		};

		columnGap.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.DistanceBetweenColumns;
			shape.DistanceBetweenColumns = v;

			PushChange("Column gap", before, v,
				x => { shape.DistanceBetweenColumns = x; columnGap.SetValue(x); }, "Text.DistanceBetweenColumns");

			NotifyShapeChanged();
			InvalidateVisual();
		};

		padding.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.Padding;
			shape.Padding = v;

			PushChange("Padding", before, v,
				x => { shape.Padding = x; padding.SetValue(x); }, "Text.Padding");

			NotifyShapeChanged();
			InvalidateVisual();
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
			InvalidateVisual();
		};

		ay.ValueChanged = v =>
		{
			if (suppressUi) return;

			float before = shape.ay;
			shape.ay = v;

			PushChange("Text offset Y", before, v,
				x => { shape.ay = x; ay.SetValue(x); }, "Text.ay");

			NotifyShapeChanged();
			InvalidateVisual();
		};

		tangent.CheckedChanged += v =>
		{
			if (suppressUi) return;

			bool before = shape.tangentAlong;
			shape.tangentAlong = v;

			PushChange("Tangent to curve", before, v,
				x => { shape.tangentAlong = x; tangent.Checked = x; }, "Text.tangentAlong");

			NotifyShapeChanged();
			InvalidateVisual();
		};

		// ------------------------------------------------------------------------
		// Компоновка
		// ------------------------------------------------------------------------

		p.Content.Add(new AvPropertyRow("text_row", L.T("Text"), tb));
		p.Content.Add(new AvPropertyRow("font_row", L.T("Font"), font) { MinHeight = 32 });
		p.Content.Add(new AvPropertyRow("style_row", L.T("Style"), styleRow) { MinHeight = 30 });
		p.Content.Add(new AvPropertyRow("size_row", L.T("Size"), fs));
		p.Content.Add(new AvPropertyRow("char_spacing_row", L.T("Char spacing"), charSpace));
		p.Content.Add(new AvPropertyRow("line_spacing_row", L.T("Line spacing"), lineSpace));

		p.Content.Add(new AvLabel { Text = "Text inside shape", Bold = true, FontSize = 16 });

		p.Content.Add(containerRow);
		p.Content.Add(alignRow);
		p.Content.Add(paddingRow);
		p.Content.Add(wrapInsideRow);
		p.Content.Add(clipByShapeRow);
		p.Content.Add(wrapOtherRow);
		p.Content.Add(columnsRow);
		p.Content.Add(columnGapRow);

		p.Content.Add(new AvLabel { Text = "Text along path", Bold = true, FontSize = 16 });

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
	private AvCollapseSection BuildLinkPanel(IShapeAnchorHost curve, string firstLabel = "First node", string lastLabel = "Last node")
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

		var anchorFirstNode = new AvCheckBox { Checked = curve.FirstAnchor.Enabled };

		var firstNodeTangentToEdge = new AvCheckBox
		{
			Checked = curve.FirstAnchor.TangentToEdge,
			Enabled = curve.FirstAnchor.Enabled,
			Text = "Tangent"
		};

		var anchorLastNode = new AvCheckBox { Checked = curve.LastAnchor.Enabled };

		var lastNodeTangentToEdge = new AvCheckBox
		{
			Checked = curve.LastAnchor.TangentToEdge,
			Enabled = curve.LastAnchor.Enabled,
			Text = "Tangent"
		};

		var firstNodeAnchorRow = new AvPropertyRow(
			firstLabel,
			new Control[] { anchorFirstNode, firstNodeTangentToEdge });

		var lastNodeAnchorRow = new AvPropertyRow(
			lastLabel,
			new Control[] { anchorLastNode, lastNodeTangentToEdge });

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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
			InvalidateVisual();
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
	private void AppendShapeSpecificRows(AvCollapseSection p, Shape shape)
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

					var sides = new AvNumericUpDown
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
					p.Content.Add(new AvPropertyRow(L.T("Sides"), sides));

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
		AvCollapseSection p,
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

		var editor = new AvNumericUpDown
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

		p.Content.Add(new AvPropertyRow(L.T(label), editor));
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
	private AvCollapseSection BuildMeasurementTextPanel(MeasurementShape shape)
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
	private AvCollapseSection BuildAngleMeasurementTextPanel(AngleMeasurementShape shape)
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
	private void AddInvertAngleRow(AvCollapseSection p, AngleMeasurementShape shape)
	{
		var check = new AvCheckBox { Checked = shape.InvertAngle };

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

		p.Content.Add(new AvPropertyRow("invert_angle_row", L.T("Invert angle"), check));
	}

	/// <summary>
	/// Множитель отображаемого значения размера.
	///
	/// Три знака после запятой, потому что типовые масштабные множители —
	/// это 0.001, 0.01 и 0.1: с двумя знаками первый из них не набрать.
	/// </summary>
	private void AddMeasurementFactorRow(AvCollapseSection p, MeasurementShape shape)
	{
		var editor = new AvNumericUpDown
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

		p.Content.Add(new AvPropertyRow("factor_row", L.T("Factor"), editor));

		// Строка знаков идёт под строкой значения и получает её редактор
		// напрямую. Через _shapeParamRefreshers связать их нельзя:
		// RefreshFromBoundShape выходит сразу, пока фокус в AvNumericUpDown,
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
	private AvNumericUpDown AddMeasurementValueRow(AvCollapseSection p, MeasurementShape shape)
	{
		var editor = new AvNumericUpDown
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

		p.Content.Add(new AvPropertyRow("value_row", L.T("Value"), editor));

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
		AvCollapseSection p,
		MeasurementShape shape,
		AvNumericUpDown valueEditor)
	{
		var editor = new AvNumericUpDown
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

		p.Content.Add(new AvPropertyRow("digits_row", L.T("Digits"), editor));
	}

	/// <summary>
	/// Оставляет видимыми только строки с перечисленными именами.
	/// Безымянные элементы (заголовки-разделители, строки без Name) скрываются:
	/// они относятся к режимам, которых у этой фигуры нет.
	/// </summary>
	private static void KeepOnlyRows(AvCollapseSection panel, params string[] keepNames)
	{
		foreach (var child in panel.Content.Children)
			child.IsVisible = Array.IndexOf(keepNames, child.Name) >= 0;
	}

	#endregion

	#region Панель Image

	private AvCollapseSection BuildImagePanel(ImageShape shape)
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
			InvalidateVisual();
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

		AvPropertyRow CreateSliderRow(
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
			var slider = new AvSlideBar
			{
				Minimum = min,
				Maximum = max,
				SmallChange = increment,
				IntegerMode = integerMode,
				SnapToStep = true,
				Step = step ?? increment,
				Decimals = decimals,
				ShowValueLabel = false,
				Width = 150, Height = 24
			};

			var numeric = new AvNumericUpDown
			{
				Minimum = min,
				Maximum = max,
				Increment = increment,
				Decimals = decimals,
				Width = 76, Height = 24
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

			return new AvPropertyRow(L.T(title), new Control[] { slider, numeric });
		}

		AvPropertyRow CreateActionRow(params Control[] controls)
			=> new AvPropertyRow(string.Empty, controls);

		AvPropertyRow BuildBrightnessRow() => CreateSliderRow(
			"Brightness", -100f, 100f, shape.Adjustments.Brightness * 100f, 1f, 0,
			v => shape.Adjustments.Brightness = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildContrastRow() => CreateSliderRow(
			"Contrast", -100f, 100f, shape.Adjustments.Contrast * 100f, 1f, 0,
			v => shape.Adjustments.Contrast = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildExposureRow() => CreateSliderRow(
			"Exposure", -2f, 2f, shape.Adjustments.Exposure, 0.05f, 2,
			v => shape.Adjustments.Exposure = v, integerMode: false, step: 0.05f);

		AvPropertyRow BuildSharpnessRow() => CreateSliderRow(
			"Sharpness", 0f, 100f, shape.Adjustments.Sharpness * 100f, 1f, 0,
			v => shape.Adjustments.Sharpness = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildBwRow() => CreateSliderRow(
			"B/W", 0f, 100f, shape.Adjustments.BlackWhite * 100f, 1f, 0,
			v => shape.Adjustments.BlackWhite = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildShadowsRow() => CreateSliderRow(
			"Shadows", -100f, 100f, shape.Adjustments.Shadows * 100f, 1f, 0,
			v => shape.Adjustments.Shadows = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildHueRow() => CreateSliderRow(
			"Hue", -180f, 180f, shape.Adjustments.Hue, 1f, 0,
			v => shape.Adjustments.Hue = v, integerMode: true, step: 1f);

		AvPropertyRow BuildTemperatureRow() => CreateSliderRow(
			"Temperature", -100f, 100f, shape.Adjustments.Temperature * 100f, 1f, 0,
			v => shape.Adjustments.Temperature = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildTintRow() => CreateSliderRow(
			"Tint", -100f, 100f, shape.Adjustments.Tint * 100f, 1f, 0,
			v => shape.Adjustments.Tint = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildVibrancyRow() => CreateSliderRow(
			"Vibrancy", -100f, 100f, shape.Adjustments.Vibrancy * 100f, 1f, 0,
			v => shape.Adjustments.Vibrancy = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildNoiseReductionRow() => CreateSliderRow(
			"Noise reduction", 0f, 100f, shape.Adjustments.NoiseReduction * 100f, 1f, 0,
			v => shape.Adjustments.NoiseReduction = v / 100f, integerMode: true, step: 1f);

		AvPropertyRow BuildInvertRow()
		{
			var check = new AvCheckBox { Checked = shape.Adjustments.Invert };

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

			return new AvPropertyRow("invert_row", L.T("Invert"), check);
		}

		AvPropertyRow BuildActualResolutionRow()
		{
			var check = new AvCheckBox { Checked = shape.ActualResolution };

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

			return new AvPropertyRow("actual_dpi_row", L.T("Actual dpi"), check);
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

			var applyBut = new AvButton
			{
				Text = "Apply",
				Width = 220, Height = 28
			};

			var resetBut = new AvButton
			{
				Text = "Reset",
				Width = 220, Height = 28
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
		else if (refreshPanel && IsAttached)
			InvalidateVisual();

		RequestCanvasRefresh?.Invoke();
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

	private AvFlow BuildFillTypeButtons(FillStyle fill)
	{
		var row = new AvFlow
		{
			Direction = AvFlowDirection.Horizontal,
			Gap = 6f,
			AutoHeight = true,

			// Семь кнопок по 36 точек с зазорами требуют около 290 точек,
			// а панель свойств бывает и уже. Перенос — единственный способ
			// не потерять последние кнопки: сжимать их означало бы делать
			// значки нечитаемыми, а горизонтальная прокрутка внутри строки
			// свойств неуместна.
			WrapContents = true

		};

		void addButton(string png, string tip, FillKind kind)
		{
			var btn = new AvPictureButton
			{
				Width = 36, Height = 36,
				Image = AvInterop.LoadUiBitmap(png),
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
