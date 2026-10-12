using SkiaSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Animation;

/// <summary>Значки кнопок панели анимации. Рисуются вектором, не шрифтом.</summary>
public enum AnimIcon
{
	None = 0,
	ToStart,
	StepLeft,
	Play,
	Stop,
	StepRight,
	ToEnd,
	Key,
	Zoom,
	ChevronUp,
	ChevronDown
}

/// <summary>
/// Кнопка со значком.
///
/// Не SkButton по двум причинам. Во-первых, SkButton переключает Checked
/// на каждом нажатии, а кнопкам перехода по кадрам залипать не положено —
/// залипают только a_play_but и a_zoom_but, и они делают это сами.
/// Во-вторых, значок нужен векторный: символы вроде ⏮ живут в Segoe UI
/// Symbol, и на машине без него вместо значка вышел бы пустой квадрат.
/// </summary>
public sealed class AnimIconButton : SkControl
{
	public AnimIcon Icon { get; set; } = AnimIcon.None;

	/// <summary>
	/// Символ иконочного шрифта FluentSystemIcons — того же, которым
	/// подписаны кнопки тулбаров MainForm (там он зовётся fSnipit).
	/// Задан — рисуется он, а векторный Icon не используется.
	/// </summary>
	public string Glyph { get; set; } = string.Empty;

	/// <summary>Кегль глифа. Шрифт берётся из общего кэша FontResources.</summary>
	public float GlyphSize { get; set; } = 20f;

	public string Text { get; set; } = string.Empty;
	public float FontSize { get; set; } = 15.6f;
	public float CornerRadius { get; set; } = 5f;

	/// <summary>Кнопка отрисована «нажатой»: режим Play, включённый зум.</summary>
	public bool Checked { get; set; }

	public Action? Click { get; set; }

	private bool _pressed;

	/// <summary>Файл иконочного шрифта в папке Resources.</summary>
	public const string IconFontFile = "FluentSystemIcons-Resizable.ttf";

	public AnimIconButton()
	{
		Bounds = new SKRect(0, 0, 36, 30);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var r = AbsoluteBounds;

		bool down = Checked || _pressed;

		var backColor = !Enabled
			? pal.PanelBack
			: down
				? pal.ButtonPressed
				: Hovered
					? pal.PanelBack2
					: pal.Surface;

		var borderColor = down ? pal.Accent : pal.Border;

		using var back = SkUiTheme.Fill(backColor);
		using var border = SkUiTheme.Stroke(borderColor, down ? 1.5f : 1f);

		canvas.DrawRoundRect(r, CornerRadius, CornerRadius, back);
		canvas.DrawRoundRect(r, CornerRadius, CornerRadius, border);

		var color = !Enabled ? pal.TextMuted : down ? pal.Accent : pal.Text;

		if (!string.IsNullOrEmpty(Glyph))
		{
			var glyphFont = FontResources.GetFont(IconFontFile, GlyphSize);

			// Шрифта в Resources может не оказаться — тогда рисуется
			// векторный значок: кнопка без подписи хуже кнопки с непривычным
			// значком, но лучше пустого прямоугольника.
			if (glyphFont != null)
			{
				SkTextHelper.DrawCenteredInRect(canvas, Glyph, r, glyphFont, color);
				return;
			}
		}

		bool hasText = !string.IsNullOrEmpty(Text);
		float iconSize = MathF.Min(16f, r.Height - 10f);

		if (hasText)
		{
			float textWidth = SkTextHelper.Measure(Text, FontSize);
			float total = iconSize + 6f + textWidth;
			float left = r.MidX - total * 0.5f;

			DrawIcon(canvas, Icon, new SKRect(left, r.MidY - iconSize * 0.5f, left + iconSize, r.MidY + iconSize * 0.5f), color);

			var metrics = SkTextHelper.GetMetrics(FontSize);
			float baseline = r.MidY + (metrics.Ascent - metrics.Descent) * 0.5f;

			SkTextHelper.DrawLeft(canvas, Text, left + iconSize + 6f, baseline, FontSize, color);
			return;
		}

		DrawIcon(canvas, Icon, new SKRect(r.MidX - iconSize * 0.5f, r.MidY - iconSize * 0.5f, r.MidX + iconSize * 0.5f, r.MidY + iconSize * 0.5f), color);
	}

	/// <summary>Значок, вписанный в квадрат box.</summary>
	public static void DrawIcon(SKCanvas canvas, AnimIcon icon, SKRect box, SKColor color)
	{
		if (icon == AnimIcon.None)
			return;

		using var fill = SkUiTheme.Fill(color);
		using var stroke = SkUiTheme.Stroke(color, 1.6f);

		float w = box.Width;
		float h = box.Height;
		float cx = box.MidX;
		float cy = box.MidY;

		switch (icon)
		{
			case AnimIcon.Play:
				FillTriangle(canvas, fill,
					new SKPoint(box.Left + w * 0.18f, box.Top),
					new SKPoint(box.Left + w * 0.18f, box.Bottom),
					new SKPoint(box.Right, cy));
				break;

			case AnimIcon.Stop:
				canvas.DrawRect(new SKRect(box.Left + w * 0.12f, box.Top + h * 0.12f, box.Right - w * 0.12f, box.Bottom - h * 0.12f), fill);
				break;

			case AnimIcon.StepLeft:
				FillTriangle(canvas, fill,
					new SKPoint(box.Right, box.Top),
					new SKPoint(box.Right, box.Bottom),
					new SKPoint(box.Left + w * 0.18f, cy));
				break;

			case AnimIcon.StepRight:
				FillTriangle(canvas, fill,
					new SKPoint(box.Left, box.Top),
					new SKPoint(box.Left, box.Bottom),
					new SKPoint(box.Right - w * 0.18f, cy));
				break;

			case AnimIcon.ToStart:
				canvas.DrawRect(new SKRect(box.Left, box.Top, box.Left + w * 0.18f, box.Bottom), fill);
				FillTriangle(canvas, fill,
					new SKPoint(box.Right, box.Top),
					new SKPoint(box.Right, box.Bottom),
					new SKPoint(box.Left + w * 0.28f, cy));
				break;

			case AnimIcon.ToEnd:
				canvas.DrawRect(new SKRect(box.Right - w * 0.18f, box.Top, box.Right, box.Bottom), fill);
				FillTriangle(canvas, fill,
					new SKPoint(box.Left, box.Top),
					new SKPoint(box.Left, box.Bottom),
					new SKPoint(box.Right - w * 0.28f, cy));
				break;

			case AnimIcon.Key:
				FillDiamond(canvas, fill, new SKPoint(cx, cy), MathF.Min(w, h) * 0.5f);
				break;

			case AnimIcon.Zoom:
			{
				float radius = MathF.Min(w, h) * 0.34f;
				float lensX = cx - w * 0.08f;
				float lensY = cy - h * 0.08f;

				canvas.DrawCircle(lensX, lensY, radius, stroke);
				canvas.DrawLine(
					lensX + radius * 0.75f, lensY + radius * 0.75f,
					box.Right - w * 0.05f, box.Bottom - h * 0.05f, stroke);
				break;
			}

			case AnimIcon.ChevronUp:
				canvas.DrawLine(box.Left + w * 0.2f, cy + h * 0.15f, cx, cy - h * 0.15f, stroke);
				canvas.DrawLine(cx, cy - h * 0.15f, box.Right - w * 0.2f, cy + h * 0.15f, stroke);
				break;

			case AnimIcon.ChevronDown:
				canvas.DrawLine(box.Left + w * 0.2f, cy - h * 0.15f, cx, cy + h * 0.15f, stroke);
				canvas.DrawLine(cx, cy + h * 0.15f, box.Right - w * 0.2f, cy - h * 0.15f, stroke);
				break;
		}
	}

	private static void FillTriangle(SKCanvas canvas, SKPaint paint, SKPoint a, SKPoint b, SKPoint c)
	{
		using var path = new SKPath();
		path.MoveTo(a);
		path.LineTo(b);
		path.LineTo(c);
		path.Close();
		canvas.DrawPath(path, paint);
	}

	public static void FillDiamond(SKCanvas canvas, SKPaint paint, SKPoint center, float radius)
	{
		using var path = new SKPath();
		path.MoveTo(center.X, center.Y - radius);
		path.LineTo(center.X + radius, center.Y);
		path.LineTo(center.X, center.Y + radius);
		path.LineTo(center.X - radius, center.Y);
		path.Close();
		canvas.DrawPath(path, paint);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		_pressed = true;
		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool was = Hovered;
		Hovered = HitTest(e.Location);
		return was != Hovered;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		bool was = _pressed;
		_pressed = false;

		if (!was)
			return false;

		if (HitTest(e.Location))
			Click?.Invoke();

		return true;
	}
}

/// <summary>
/// Контейнер для контролов панели. Сам по себе непрозрачен для нажатий:
/// щелчок по пустому месту тулбара не должен считаться попаданием
/// в контейнер, иначе он забирал бы фокус у полей ввода.
/// </summary>
internal sealed class AnimHost : SkControl
{
	public override bool HitTest(SKPoint p) => false;
}

/// <summary>
/// Панель управления анимацией внизу окна.
///
/// Состав, сверху вниз:
///   - полоса изменения высоты (тянется мышью, двойной щелчок сворачивает);
///   - тулбар: сворачивание, переход в начало, шаг назад, Play/Stop,
///     шаг вперёд, переход в конец, число кадров, кадров в секунду,
///     запись ключа, режим приближения участка;
///   - дочерняя панель дерева параметров выделенных фигур слева,
///     с собственным разделителем ширины;
///   - дочерняя панель ключей: линейка кадров с ручкой прохода по всей
///     анимации, ромбы ключей построчно и горизонтальная полоса прокрутки.
///
/// Видимый участок кадров — отдельное состояние (_viewFirst.._viewLast).
/// Когда кадров сотни, показывать их все разом бессмысленно: ключи
/// сливаются в сплошную линию и попасть мышью в нужный нельзя. Поэтому
/// участок приближается колесом, кнопкой a_zoom_but, полосой прокрутки
/// и перетаскиванием линейки.
///
/// Рисуется через Skia в свой буфер — как SkPropertyPanelControl, ради
/// одинакового вида с остальными панелями. Дочерним контролом MainView
/// панель быть не может: SKGLControl выводит кадр OpenGL на всю свою
/// поверхность и затирает любые оконные контролы поверх себя, поэтому
/// панель — сосед холста по Controls формы, пристыкованный снизу.
/// </summary>
public sealed class AnimationPanel : Control
{
	#region Константы раскладки

	/// <summary>
	/// Общий множитель кегля. Вынесен в одно место: подписи кадров,
	/// имена параметров и надписи кнопок обязаны расти вместе, иначе
	/// панель расползается.
	/// </summary>
	private const float TextScale = 1.3f;

	private const float FontRow = 12f * TextScale;
	private const float FontSmall = 11f * TextScale;
	private const float FontTiny = 10f * TextScale;

	/// <summary>Высота полосы изменения размера сверху.</summary>
	private const float GripHeight = 6f;

	private const float ToolbarHeight = 44f;
	private const float ButtonHeight = 30f;

	/// <summary>Высота линейки кадров и шапки дерева.</summary>
	private const float HeaderHeight = 26f;

	private const float RowHeight = 24f;

	/// <summary>Высота горизонтальной полосы прокрутки кадров.</summary>
	private const float ScrollBarHeight = 12f;

	/// <summary>Ширина разделителя между деревом и линейкой ключей.</summary>
	private const float SplitterWidth = 5f;

	private const float TreeMinWidth = 190f;
	private const float TreeMaxWidth = 700f;

	/// <summary>Отступ дорожки кадров от краёв: под ручку и крайние ключи.</summary>
	private const float TrackPadding = 18f;

	private const float KeyRadius = 5.5f;

	/// <summary>Минимальное расстояние между подписями кадров на линейке.</summary>
	private const float MinLabelSpacing = 42f;

	/// <summary>Предел приближения: ближе двух кадров в окне смысла нет.</summary>
	private const float MinViewSpan = 1f;

	/// <summary>Сдвиг мыши, после которого нажатие считается перетаскиванием.</summary>
	private const float DragThreshold = 3f;

	public static readonly int CollapsedHeight = (int)(GripHeight + ToolbarHeight);
	public const int MinExpandedHeight = 170;
	public const int MaxExpandedHeight = 700;

	#endregion

	#region Контролы тулбара

	private readonly AnimHost _root = new();

	/// <summary>Сворачивание и разворачивание панели.</summary>
	public readonly AnimIconButton a_collapse_but = new() { Name = "a_collapse_but", Icon = AnimIcon.ChevronUp };

	// Значки перемотки — символы иконочного шрифта FluentSystemIcons,
	// того же, которым подписаны кнопки тулбаров MainForm. Коды заданы
	// здесь, а не в MainForm.SetIcons: панель собирает себя целиком сама
	// и не должна зависеть от порядка инициализации формы.
	public readonly AnimIconButton a_to_start_but = new() { Name = "a_to_start_but", Icon = AnimIcon.ToStart, Glyph = "\uedb0" };
	public readonly AnimIconButton a_step_left_but = new() { Name = "a_step_left_but", Icon = AnimIcon.StepLeft, Glyph = "\uedb2" };
	public readonly AnimIconButton a_play_but = new() { Name = "a_play_but", Icon = AnimIcon.Play, Glyph = PlayGlyph };
	public readonly AnimIconButton a_step_right_but = new() { Name = "a_step_right_but", Icon = AnimIcon.StepRight, Glyph = "\ueba1" };
	public readonly AnimIconButton a_to_end_but = new() { Name = "a_to_end_but", Icon = AnimIcon.ToEnd, Glyph = "\ueb9f" };

	/// <summary>Реверсивное воспроизведение: кадры идут от конца к началу.</summary>
	public readonly AnimIconButton a_reverse_but = new() { Name = "a_reverse_but", Icon = AnimIcon.StepLeft, Glyph = "\uee2b" };

	private const string PlayGlyph = "\uf2e8";
	private const string StopGlyph = "\uefc4";

	/// <summary>Текущий кадр. Поле, а не подпись: по нему и переходят.</summary>
	public readonly SkNumericUpDown a_current_frame_nud = new()
	{
		Name = "a_current_frame_nud",
		Minimum = PageAnimation.MinFrameCount,
		Maximum = PageAnimation.MaxFrameCount,
		Increment = 1,
		Decimals = 0,
		AllowNegative = false,
		AllowDecimal = false
	};

	/// <summary>Число кадров анимации.</summary>
	public readonly SkNumericUpDown a_frames_nud = new()
	{
		Name = "a_frames_nud",
		Minimum = PageAnimation.MinFrameCount,
		Maximum = PageAnimation.MaxFrameCount,
		Increment = 1,
		Decimals = 0,
		AllowNegative = false,
		AllowDecimal = false
	};

	/// <summary>Кадров в секунду при воспроизведении.</summary>
	public readonly SkNumericUpDown a_fps_nud = new()
	{
		Name = "a_fps_nud",
		Minimum = PageAnimation.MinFps,
		Maximum = PageAnimation.MaxFps,
		Increment = 1,
		Decimals = 0,
		AllowNegative = false,
		AllowDecimal = false
	};

	/// <summary>Запись текущих параметров выделенных фигур в ключ.</summary>
	public readonly AnimIconButton a_set_key = new() { Name = "a_set_key", Icon = AnimIcon.Key, Text = "Set key" };

	/// <summary>Режим выделения участка кадров для приближения.</summary>
	public readonly AnimIconButton a_zoom_but = new() { Name = "a_zoom_but", Icon = AnimIcon.Zoom };

	#endregion

	#region Состояние

	private Page? _page;
	private UndoStack? _hookedUndo;

	private PageAnimation? Anim => _page?.Animation;
	private UndoStack? Undo => _page?.doc?.Undo;

	public AnimMode Mode { get; private set; } = AnimMode.Stop;

	private readonly System.Windows.Forms.Timer _playTimer;

	/// <summary>
	/// Развёрнута ли панель. В свёрнутом виде остаются полоса и тулбар.
	///
	/// По умолчанию свёрнута: рисуют в VDraw чаще, чем анимируют, и
	/// четверть высоты окна, занятая пустым деревом параметров, при каждом
	/// запуске отнимала бы место у холста. Высота развёрнутого состояния
	/// при этом помнится и возвращается по кнопке.
	/// </summary>
	public bool Expanded { get; private set; }

	private int _expandedHeight = 260;
	private float _treeWidth = 300f;
	private float _scrollY;

	// Видимый участок кадров. Дробный: приближение колесом не обязано
	// попадать в целые кадры, а округление на каждом шаге накапливало бы
	// ошибку, и участок сползал бы сам по себе.
	private float _viewFirst = 1f;
	private float _viewLast = 25f;

	private bool _gripHovered;
	private bool _splitterHovered;

	private SkControl? _focused;
	private SkControl? _captured;

	/// <summary>Подавление обратной записи при программном обновлении полей.</summary>
	private bool _updatingFromModel;

	private SKBitmap? _backBuffer;
	private Bitmap? _gdiBuffer;
	private byte[]? _rowBuffer;

	/// <summary>Идёт ли ввод в одном из числовых полей панели.</summary>
	public bool IsTextEditing => _focused is SkNumericUpDown;

	/// <summary>Поднимается, когда значения фигур изменились и холст надо перерисовать.</summary>
	public event Action? RequestCanvasRefresh;

	#endregion

	#region Перетаскивание

	/// <summary>Что именно тянет мышь. Одно состояние вместо восьми флагов.</summary>
	private enum DragKind
	{
		None = 0,

		/// <summary>Высота панели.</summary>
		Height,

		/// <summary>Ширина дерева параметров.</summary>
		Tree,

		/// <summary>Ручка текущего кадра.</summary>
		Scrub,

		/// <summary>Выделенные ключи.</summary>
		Keys,

		/// <summary>Рамка выделения ключей.</summary>
		Rubber,

		/// <summary>Рамка выбора участка для приближения.</summary>
		ZoomRange,

		/// <summary>Сдвиг видимого участка кадров.</summary>
		PanView,

		/// <summary>Ползунок горизонтальной прокрутки.</summary>
		HScroll
	}

	private DragKind _drag;
	private SKPoint _dragStart;
	private SKPoint _dragNow;
	private bool _dragMoved;

	private int _dragStartHeight;
	private int _dragStartScreenY;
	private float _dragStartTreeWidth;
	private int _dragStartScreenX;

	private float _panStartViewFirst;
	private float _panStartScrollY;
	private float _hscrollGrab;

	/// <summary>Сдвиг перетаскиваемых ключей в кадрах.</summary>
	private int _keyDragDelta;

	#endregion

	#region Строки дерева

	/// <summary>
	/// Строка дерева параметров. Строится заново при каждой смене
	/// выделения и раскрытия узлов, поэтому хранит готовые к отрисовке
	/// данные, а не считает их в цикле рисования.
	/// </summary>
	private sealed class AnimRow
	{
		public Shape Shape = null!;
		public Guid ShapeId;
		public AnimParamNode Node = null!;
		public int Depth;

		/// <summary>Ключ узла в наборах раскрытых и отмеченных: "guid|nodeId".</summary>
		public string Key = string.Empty;

		/// <summary>Параметры поддерева. У строки-параметра — он один.</summary>
		public string[] ParamIds = Array.Empty<string>();

		public bool IsGroup => Node.IsGroup;
		public bool HasChildren => Node.Children.Count > 0;
		public AnimParam? Param => Node.Param;
	}

	/// <summary>Адрес ключа: фигура, параметр, кадр.</summary>
	private readonly record struct KeyRef(Guid ShapeId, string ParamId, int Frame);

	private readonly List<AnimRow> _rows = new();
	private readonly Dictionary<Guid, AnimParamNode> _trees = new();
	private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _checkedParams = new(StringComparer.Ordinal);
	private readonly HashSet<Guid> _knownShapes = new();

	/// <summary>Буфер кадров с ключами при отрисовке строки. Без аллокаций на строку.</summary>
	private readonly HashSet<int> _frameBuffer = new();

	/// <summary>Выделенные ключи. Двигаются и удаляются все разом.</summary>
	private readonly HashSet<KeyRef> _selectedKeys = new();

	/// <summary>Буфер показываемых ключей строки. Без аллокаций на строку.</summary>
	private readonly Dictionary<int, (int Total, int Selected)> _displayKeys = new();

	#endregion

	#region Создание

	public AnimationPanel()
	{
		// StandardDoubleClick обязателен: без него окно не получает
		// WM_LBUTTONDBLCLK, и двойной щелчок по полосе не сворачивал бы панель.
		SetStyle(ControlStyles.AllPaintingInWmPaint |
				 ControlStyles.UserPaint |
				 ControlStyles.OptimizedDoubleBuffer |
				 ControlStyles.ResizeRedraw |
				 ControlStyles.StandardClick |
				 ControlStyles.StandardDoubleClick |
				 ControlStyles.Selectable, true);

		Dock = DockStyle.Bottom;
		Height = CollapsedHeight;
		TabStop = true;
		BackColor = ToColor(SkUiTheme.Current.PanelBack);

		_root.Add(a_collapse_but);
		_root.Add(a_to_start_but);
		_root.Add(a_step_left_but);
		_root.Add(a_play_but);
		_root.Add(a_step_right_but);
		_root.Add(a_to_end_but);
		_root.Add(a_reverse_but);
		_root.Add(a_current_frame_nud);
		_root.Add(a_frames_nud);
		_root.Add(a_fps_nud);
		_root.Add(a_set_key);
		_root.Add(a_zoom_but);

		a_collapse_but.Click = () => SetExpanded(!Expanded);
		a_to_start_but.Click = () => SetCurrentFrame(PageAnimation.MinFrameCount);
		a_step_left_but.Click = () => StepFrame(-1);
		a_play_but.Click = TogglePlay;
		a_step_right_but.Click = () => StepFrame(+1);
		a_to_end_but.Click = () => SetCurrentFrame(Anim?.FrameCount ?? PageAnimation.MinFrameCount);
		a_set_key.Click = SetKeysForSelection;

		a_reverse_but.Click = () =>
		{
			a_reverse_but.Checked = !a_reverse_but.Checked;
			Invalidate();
		};

		a_zoom_but.Click = () =>
		{
			a_zoom_but.Checked = !a_zoom_but.Checked;
			Invalidate();
		};

		a_current_frame_nud.ValueChanged = v =>
		{
			if (_updatingFromModel)
				return;

			SetCurrentFrame((int)MathF.Round(v));
		};

		a_frames_nud.ValueChanged = v =>
		{
			if (_updatingFromModel || Anim == null)
				return;

			Anim.FrameCount = (int)MathF.Round(v);
			EnsureViewValid();
			SyncEditors();
			Invalidate();
		};

		a_fps_nud.ValueChanged = v =>
		{
			if (_updatingFromModel || Anim == null)
				return;

			Anim.Fps = (int)MathF.Round(v);
			ApplyTimerInterval();
			SyncEditors();
			Invalidate();
		};

		_playTimer = new System.Windows.Forms.Timer { Interval = 1000 / PageAnimation.DefaultFps };
		_playTimer.Tick += OnPlayTick;

		MainForm.sel.SelectionChanged += OnSelectionChanged;
		SkUiTheme.ThemeChanged += OnThemeChanged;
	}

	/// <summary>
	/// Привязка к странице. Ключи живут в самой странице.
	///
	/// documentChanged отличает открытие или создание документа от простого
	/// перехода между его страницами: в первом случае панель сворачивается,
	/// как при запуске, во втором — остаётся как была. Схлопывать её на
	/// каждом переключении вкладки значило бы отнимать у пользователя
	/// раскрытое дерево посреди работы.
	/// </summary>
	public void SetPage(Page? page, bool documentChanged = false)
	{
		SetMode(AnimMode.Stop);

		if (documentChanged)
			SetExpanded(false);

		// Отписка от истории прежнего документа: иначе панель продолжит
		// слушать закрытый документ, а он не будет собран сборщиком мусора.
		if (_hookedUndo != null)
			_hookedUndo.Changed -= OnUndoChanged;

		_page = page;
		_hookedUndo = page?.doc?.Undo;

		if (_hookedUndo != null)
			_hookedUndo.Changed += OnUndoChanged;

		_scrollY = 0f;
		_selectedKeys.Clear();
		a_zoom_but.Checked = false;

		ResetView();
		RebuildRows();
		SyncEditors();
		Invalidate();
	}

	protected override void OnCreateControl()
	{
		base.OnCreateControl();
		RebuildRows();
		SyncEditors();
	}

	private void OnSelectionChanged()
	{
		if (!IsHandleCreated)
			return;

		RebuildRows();
		Invalidate();
	}

	/// <summary>
	/// Отмена или повтор шага истории. Ключи могли измениться чужими руками,
	/// поэтому панель перерисовывается, а холст обновляется: сам шаг уже
	/// разложил значения кадра по фигурам.
	/// </summary>
	private void OnUndoChanged()
	{
		if (!IsHandleCreated)
			return;

		Invalidate();
		RequestCanvasRefresh?.Invoke();
	}

	private void OnThemeChanged()
	{
		BackColor = ToColor(SkUiTheme.Current.PanelBack);

		if (IsHandleCreated)
			Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			MainForm.sel.SelectionChanged -= OnSelectionChanged;
			SkUiTheme.ThemeChanged -= OnThemeChanged;

			if (_hookedUndo != null)
			{
				_hookedUndo.Changed -= OnUndoChanged;
				_hookedUndo = null;
			}

			_playTimer.Stop();
			_playTimer.Tick -= OnPlayTick;
			_playTimer.Dispose();

			_backBuffer?.Dispose();
			_gdiBuffer?.Dispose();
		}

		base.Dispose(disposing);
	}

	private static Color ToColor(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	private static SKColor WithAlpha(SKColor c, byte alpha) => new(c.Red, c.Green, c.Blue, alpha);

	/// <summary>Осветление цвета подмешиванием белого.</summary>
	private static SKColor Lighten(SKColor c, float amount)
	{
		float t = Math.Clamp(amount, 0f, 1f);

		return new SKColor(
			(byte)(c.Red + (255 - c.Red) * t),
			(byte)(c.Green + (255 - c.Green) * t),
			(byte)(c.Blue + (255 - c.Blue) * t),
			c.Alpha);
	}

	#endregion

	#region Сворачивание и размеры

	public void SetExpanded(bool expanded)
	{
		if (Expanded == expanded)
			return;

		Expanded = expanded;
		a_collapse_but.Icon = expanded ? AnimIcon.ChevronDown : AnimIcon.ChevronUp;

		Height = expanded ? _expandedHeight : CollapsedHeight;

		ArrangeUi();
		Invalidate();
	}

	private void SetPanelHeight(int height)
	{
		int clamped = Math.Clamp(height, MinExpandedHeight, MaxExpandedHeight);

		_expandedHeight = clamped;

		if (!Expanded)
		{
			// Тянуть свёрнутую панель за полосу естественно означает
			// «разверни её»: иначе полоса выглядит нерабочей.
			Expanded = true;
			a_collapse_but.Icon = AnimIcon.ChevronDown;
		}

		if (Height != clamped)
			Height = clamped;

		ArrangeUi();
		Invalidate();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ArrangeUi();
		ClampScroll();
		Invalidate();
	}

	#endregion

	#region Геометрия областей

	private float ContentTop => GripHeight + ToolbarHeight;
	private float HeaderTop => ContentTop;
	private float RowsTop => ContentTop + HeaderHeight;
	private float ScrollBarTop => Height - ScrollBarHeight;
	private float RowsBottom => MathF.Max(RowsTop, ScrollBarTop);
	private float TimelineLeft => _treeWidth + SplitterWidth;
	private float TrackLeft => TimelineLeft + TrackPadding;
	private float TrackRight => MathF.Max(TrackLeft + 1f, Width - TrackPadding);

	private bool HasContentArea => Expanded && Height > ContentTop + HeaderHeight + ScrollBarHeight;

	private SKRect TreeRect => new(0, ContentTop, _treeWidth, Height);
	private SKRect SplitterRect => new(_treeWidth, ContentTop, _treeWidth + SplitterWidth, Height);
	private SKRect RulerRect => new(TimelineLeft, HeaderTop, Width, HeaderTop + HeaderHeight);
	private SKRect ScrollBarRect => new(TimelineLeft + 2f, ScrollBarTop + 1f, MathF.Max(TimelineLeft + 3f, Width - 2f), Height - 1f);

	private int FrameCount => Anim?.FrameCount ?? 1;

	/// <summary>Экранная координата кадра в текущем окне просмотра.</summary>
	private float FrameX(float frame)
	{
		float span = MathF.Max(MinViewSpan, _viewLast - _viewFirst);
		return TrackLeft + (frame - _viewFirst) / span * (TrackRight - TrackLeft);
	}

	private float FrameFromXExact(float x)
	{
		float width = TrackRight - TrackLeft;

		if (width < 0.01f)
			return _viewFirst;

		float span = MathF.Max(MinViewSpan, _viewLast - _viewFirst);
		return _viewFirst + (x - TrackLeft) / width * span;
	}

	private int FrameFromX(float x)
		=> Math.Clamp((int)MathF.Round(FrameFromXExact(x)), PageAnimation.MinFrameCount, FrameCount);

	private float RowY(int index) => RowsTop + index * RowHeight - _scrollY;

	private float RowsContentHeight => _rows.Count * RowHeight;

	private void ClampScroll()
	{
		float visible = MathF.Max(0f, RowsBottom - RowsTop);
		float max = MathF.Max(0f, RowsContentHeight - visible);
		_scrollY = Math.Clamp(_scrollY, 0f, max);
	}

	#endregion

	#region Окно видимых кадров

	private void ResetView()
	{
		_viewFirst = PageAnimation.MinFrameCount;
		_viewLast = MathF.Max(FrameCount, PageAnimation.MinFrameCount + MinViewSpan);
	}

	/// <summary>
	/// Приводит окно просмотра в допустимое состояние: не уже двух кадров,
	/// не шире всей анимации и не выходящее за её границы.
	/// </summary>
	private void EnsureViewValid()
	{
		int count = FrameCount;
		float full = MathF.Max(MinViewSpan, count - 1);

		float span = Math.Clamp(_viewLast - _viewFirst, MinViewSpan, full);
		float first = Math.Clamp(_viewFirst, 1f, MathF.Max(1f, count - span));

		_viewFirst = first;
		_viewLast = first + span;
	}

	/// <summary>
	/// Подтягивает окно просмотра так, чтобы кадр оказался в нём.
	///
	/// Сдвиг минимальный, с небольшим запасом от края: перескок на кадр
	/// за границей окна не должен переставлять вид целиком, иначе при
	/// пошаговом движении картинка прыгала бы на каждом шаге.
	/// Масштаб при этом не меняется — приближенный участок остаётся
	/// приближенным.
	/// </summary>
	private void EnsureFrameVisible(int frame)
	{
		float span = _viewLast - _viewFirst;
		float margin = MathF.Min(span * 0.1f, 2f);

		if (frame < _viewFirst + margin)
			_viewFirst = frame - margin;
		else if (frame > _viewLast - margin)
			_viewFirst = frame + margin - span;
		else
			return;

		_viewLast = _viewFirst + span;
		EnsureViewValid();
	}

	/// <summary>Приближение к кадру под курсором: точка под мышью остаётся на месте.</summary>
	private void ZoomView(float anchorFrame, float factor)
	{
		float span = _viewLast - _viewFirst;
		float newSpan = Math.Clamp(span * factor, MinViewSpan, MathF.Max(MinViewSpan, FrameCount - 1));

		if (MathF.Abs(newSpan - span) < 0.0001f)
			return;

		float t = span < 0.0001f ? 0.5f : (anchorFrame - _viewFirst) / span;

		_viewFirst = anchorFrame - t * newSpan;
		_viewLast = _viewFirst + newSpan;

		EnsureViewValid();
		Invalidate();
	}

	/// <summary>Приближение к выбранному участку. Участок короче двух кадров расширяется.</summary>
	private void ZoomToRange(float a, float b)
	{
		float first = MathF.Min(a, b);
		float last = MathF.Max(a, b);

		if (last - first < MinViewSpan)
		{
			float mid = (first + last) * 0.5f;
			first = mid - MinViewSpan * 0.5f;
			last = mid + MinViewSpan * 0.5f;
		}

		_viewFirst = first;
		_viewLast = last;

		EnsureViewValid();
		Invalidate();
	}

	#endregion

	#region Раскладка контролов

	private float _frameLabelX;
	private float _framesLabelX;
	private float _fpsLabelX;

	private void ArrangeUi()
	{
		_root.Bounds = new SKRect(0, 0, Width, Height);

		float y = GripHeight + (ToolbarHeight - ButtonHeight) * 0.5f;
		float x = 8f;

		a_collapse_but.Bounds = new SKRect(x, y, x + 30f, y + ButtonHeight);
		x += 30f + 12f;

		foreach (var b in new[] { a_to_start_but, a_step_left_but, a_play_but, a_step_right_but, a_to_end_but, a_reverse_but })
		{
			b.Bounds = new SKRect(x, y, x + 36f, y + ButtonHeight);
			x += 36f + 3f;
		}

		x += 14f;
		_frameLabelX = x;
		x += SkTextHelper.Measure("Frame", FontRow) + 8f;

		a_current_frame_nud.Bounds = new SKRect(x, y, x + 90f, y + ButtonHeight);
		x += 90f + 12f;

		_framesLabelX = x;
		x += SkTextHelper.Measure("Frames", FontRow) + 8f;

		a_frames_nud.Bounds = new SKRect(x, y, x + 90f, y + ButtonHeight);
		x += 90f + 12f;

		_fpsLabelX = x;
		x += SkTextHelper.Measure("FPS", FontRow) + 8f;

		a_fps_nud.Bounds = new SKRect(x, y, x + 84f, y + ButtonHeight);
		x += 84f + 14f;

		a_set_key.Bounds = new SKRect(x, y, x + 120f, y + ButtonHeight);
		x += 120f + 6f;

		a_zoom_but.Bounds = new SKRect(x, y, x + 36f, y + ButtonHeight);

		_root.Arrange();
	}

	#endregion

	#region Проигрывание и кадры

	private void TogglePlay() => SetMode(Mode == AnimMode.Play ? AnimMode.Stop : AnimMode.Play);

	private void ApplyTimerInterval()
	{
		int fps = Anim?.Fps ?? PageAnimation.DefaultFps;
		_playTimer.Interval = Math.Max(5, 1000 / Math.Max(1, fps));
	}

	/// <summary>
	/// Переключение режима. Значок кнопки меняется здесь, а не в обработчике
	/// нажатия: режим сбрасывается ещё и при смене страницы, и значок
	/// обязан следовать за ним из любой точки.
	///
	/// В историю отмены пуск и остановка не пишутся: это просмотр,
	/// а не правка документа.
	/// </summary>
	public void SetMode(AnimMode mode)
	{
		if (Mode == mode)
			return;

		Mode = mode;

		a_play_but.Icon = mode == AnimMode.Play ? AnimIcon.Stop : AnimIcon.Play;
		a_play_but.Glyph = mode == AnimMode.Play ? StopGlyph : PlayGlyph;
		a_play_but.Checked = mode == AnimMode.Play;

		if (mode == AnimMode.Play)
		{
			ApplyTimerInterval();
			_playTimer.Start();
		}
		else
		{
			_playTimer.Stop();
		}

		Invalidate();
	}

	private void OnPlayTick(object? sender, EventArgs e)
	{
		if (Anim == null)
		{
			SetMode(AnimMode.Stop);
			return;
		}

		bool reverse = a_reverse_but.Checked;
		int next = Anim.CurrentFrame + (reverse ? -1 : +1);

		// Зацикливание, а не остановка в конце: проверять движение
		// удобнее по кругу, а остановиться можно кнопкой. В реверсе
		// цикл замыкается через противоположный край.
		if (next > Anim.FrameCount)
			next = PageAnimation.MinFrameCount;
		else if (next < PageAnimation.MinFrameCount)
			next = Anim.FrameCount;

		SetCurrentFrame(next);
	}

	private void StepFrame(int delta)
	{
		if (Anim == null)
			return;

		SetCurrentFrame(Anim.CurrentFrame + delta);
	}

	/// <summary>
	/// Переход на кадр: значения дорожек раскладываются по фигурам
	/// и холст перерисовывается.
	///
	/// Шага истории отмены при этом не создаётся. Так задумано: иначе
	/// секунда воспроизведения набивала бы историю двумя десятками шагов
	/// и вытесняла из неё настоящую работу пользователя.
	/// </summary>
	public void SetCurrentFrame(int frame)
	{
		if (Anim == null)
			return;

		int clamped = Math.Clamp(frame, PageAnimation.MinFrameCount, Anim.FrameCount);

		Anim.CurrentFrame = clamped;
		Anim.ApplyFrame(_page, clamped);

		EnsureFrameVisible(clamped);
		SyncEditors();

		// Панель свойств пересобирается целиком на каждое уведомление
		// о геометрии, поэтому при воспроизведении её не трогаем: двадцать
		// пять полных пересборок в секунду сжигали бы кадры на UI,
		// который в это время всё равно никто не правит. При ручной
		// перемотке значения в полях обновить наоборот нужно.
		if (Mode == AnimMode.Stop)
			MainForm.sel.NotifyGeometryChanged();

		RequestCanvasRefresh?.Invoke();

		Invalidate();
	}

	private void SyncEditors()
	{
		_updatingFromModel = true;

		try
		{
			a_current_frame_nud.Maximum = FrameCount;

			// Поле, в котором прямо сейчас идёт ввод, не перезаписывается:
			// иначе тик воспроизведения затирал бы набираемое значение
			// на полуслове.
			if (!ReferenceEquals(_focused, a_current_frame_nud))
				a_current_frame_nud.SetValue(Anim?.CurrentFrame ?? PageAnimation.MinFrameCount);

			if (!ReferenceEquals(_focused, a_frames_nud))
				a_frames_nud.SetValue(FrameCount);

			if (!ReferenceEquals(_focused, a_fps_nud))
				a_fps_nud.SetValue(Anim?.Fps ?? PageAnimation.DefaultFps);
		}
		finally
		{
			_updatingFromModel = false;
		}
	}

	#endregion

	#region Дерево параметров

	public void RefreshTree()
	{
		RebuildRows();
		Invalidate();
	}

	private void RebuildRows()
	{
		_rows.Clear();
		_trees.Clear();

		foreach (var shape in MainForm.sel.Selected)
		{
			// Направляющие — разметка листа, а не рисунок: анимировать
			// их нечего.
			if (shape is GuideLine)
				continue;

			var root = AnimParamCatalog.Build(shape);
			_trees[shape.Id] = root;

			string rootKey = RowKey(shape.Id, root.Id);

			// Впервые увиденная фигура раскрывается до групп: пустое
			// дерево при выделении читалось бы как «параметров нет».
			if (_knownShapes.Add(shape.Id))
				_expandedKeys.Add(rootKey);

			AddRow(shape, root, 0);
		}

		ClampScroll();
	}

	private void AddRow(Shape shape, AnimParamNode node, int depth)
	{
		var row = new AnimRow
		{
			Shape = shape,
			ShapeId = shape.Id,
			Node = node,
			Depth = depth,
			Key = RowKey(shape.Id, node.Id),
			ParamIds = node.EnumerateParams().Select(p => p.Id).ToArray()
		};

		_rows.Add(row);

		if (!_expandedKeys.Contains(row.Key))
			return;

		foreach (var child in node.Children)
			AddRow(shape, child, depth + 1);
	}

	private static string RowKey(Guid shapeId, string nodeId) => shapeId.ToString("N") + "|" + nodeId;

	private static string ParamKey(Guid shapeId, string paramId) => shapeId.ToString("N") + "|" + paramId;

	private void ToggleExpanded(AnimRow row)
	{
		if (!_expandedKeys.Remove(row.Key))
			_expandedKeys.Add(row.Key);

		RebuildRows();
		Invalidate();
	}

	/// <summary>
	/// Состояние галочки строки: 0 — снята, 1 — стоит, 2 — частично
	/// (у группы отмечена часть параметров).
	/// </summary>
	private int CheckStateOf(AnimRow row)
	{
		int total = row.ParamIds.Length;

		if (total == 0)
			return 0;

		int marked = 0;

		foreach (var id in row.ParamIds)
		{
			if (_checkedParams.Contains(ParamKey(row.ShapeId, id)))
				marked++;
		}

		if (marked == 0)
			return 0;

		return marked == total ? 1 : 2;
	}

	private void ToggleCheck(AnimRow row)
	{
		bool turnOn = CheckStateOf(row) != 1;

		foreach (var id in row.ParamIds)
		{
			string key = ParamKey(row.ShapeId, id);

			if (turnOn)
				_checkedParams.Add(key);
			else
				_checkedParams.Remove(key);
		}

		Invalidate();
	}

	/// <summary>Название параметра для шага истории: из дорожки, иначе из дерева.</summary>
	private string TitleOf(Guid shapeId, string paramId)
	{
		var track = Anim?.Find(shapeId)?.Find(paramId);

		if (track != null && !string.IsNullOrEmpty(track.Title))
			return track.Title;

		if (_trees.TryGetValue(shapeId, out var root))
		{
			foreach (var p in root.EnumerateParams())
			{
				if (p.Id == paramId)
					return p.Title;
			}
		}

		return paramId;
	}

	#endregion

	#region Правка ключей

	/// <summary>
	/// Применяет правки и кладёт их в историю одним шагом.
	///
	/// Создание, удаление и перенос ключей проходят только здесь: иначе
	/// какой-нибудь путь однажды окажется вне истории, и Ctrl+Z начнёт
	/// работать через раз.
	/// </summary>
	private void PushEdits(string name, List<AnimKeyEdit> edits)
	{
		var anim = Anim;

		if (anim == null || edits.Count == 0)
			return;

		foreach (var e in edits)
			anim.ApplyEdit(e.ShapeId, e.ParamId, e.Title, e.Frame, e.After);

		Undo?.Push(new AnimKeysAction(anim, _page, name, edits));

		anim.ApplyFrame(_page, anim.CurrentFrame);
		RequestCanvasRefresh?.Invoke();
		Invalidate();
	}

	/// <summary>
	/// Записывает ключ по кнопке a_set_key.
	///
	/// Если в дереве не отмечено ничего, пишутся ВСЕ параметры всех
	/// выделенных фигур — самый частый случай «запомни как есть».
	/// Как только пользователь отметил хотя бы один параметр, пишутся
	/// только отмеченные: это и есть обещанный выбор между всеми
	/// и отдельными параметрами.
	/// </summary>
	public void SetKeysForSelection()
	{
		var anim = Anim;

		if (anim == null || _trees.Count == 0)
			return;

		bool anyChecked = false;

		foreach (var pair in _trees)
		{
			foreach (var p in pair.Value.EnumerateParams())
			{
				if (_checkedParams.Contains(ParamKey(pair.Key, p.Id)))
				{
					anyChecked = true;
					break;
				}
			}

			if (anyChecked)
				break;
		}

		int frame = anim.CurrentFrame;
		var edits = new List<AnimKeyEdit>();

		foreach (var shape in MainForm.sel.Selected)
		{
			if (!_trees.TryGetValue(shape.Id, out var root))
				continue;

			foreach (var p in root.EnumerateParams())
			{
				if (anyChecked && !_checkedParams.Contains(ParamKey(shape.Id, p.Id)))
					continue;

				edits.Add(new AnimKeyEdit
				{
					ShapeId = shape.Id,
					ParamId = p.Id,
					Title = p.Title,
					Frame = frame,
					Before = anim.GetKeyValue(shape.Id, p.Id, frame),
					After = p.Get()
				});
			}
		}

		PushEdits(edits.Count == 1 ? "Set key" : $"Set {edits.Count} keys", edits);
	}

	private void SetKeyForParam(AnimRow row, AnimParam param, int frame)
	{
		var anim = Anim;

		if (anim == null)
			return;

		PushEdits("Set key", new List<AnimKeyEdit>
		{
			new()
			{
				ShapeId = row.ShapeId,
				ParamId = param.Id,
				Title = param.Title,
				Frame = frame,
				Before = anim.GetKeyValue(row.ShapeId, param.Id, frame),
				After = param.Get()
			}
		});
	}

	private void DeleteSelectedKeys()
	{
		var anim = Anim;

		if (anim == null || _selectedKeys.Count == 0)
			return;

		var edits = new List<AnimKeyEdit>();

		foreach (var key in _selectedKeys)
		{
			var before = anim.GetKeyValue(key.ShapeId, key.ParamId, key.Frame);

			if (!before.HasValue)
				continue;

			edits.Add(new AnimKeyEdit
			{
				ShapeId = key.ShapeId,
				ParamId = key.ParamId,
				Title = TitleOf(key.ShapeId, key.ParamId),
				Frame = key.Frame,
				Before = before,
				After = null
			});
		}

		_selectedKeys.Clear();
		PushEdits(edits.Count == 1 ? "Delete key" : $"Delete {edits.Count} keys", edits);
	}

	/// <summary>
	/// Перенос выделенных ключей на delta кадров.
	///
	/// Правки собираются как «состояние ячейки до всей операции» и «после
	/// неё», а не как последовательность шагов. Иначе перенос группы ключей
	/// вправо на один кадр, где каждый следующий встаёт на место соседа,
	/// откатывался бы неверно: в историю попали бы промежуточные состояния
	/// ячеек вперемешку с исходными.
	/// </summary>
	private void MoveSelectedKeys(int delta)
	{
		var anim = Anim;

		if (anim == null || delta == 0 || _selectedKeys.Count == 0)
			return;

		var sources = _selectedKeys.ToList();

		// Сдвиг ограничивается так, чтобы за границы анимации не вышел
		// ни один ключ: разъезжаться выделение при упоре в край не должно.
		int minFrame = sources.Min(k => k.Frame);
		int maxFrame = sources.Max(k => k.Frame);

		delta = Math.Clamp(delta, PageAnimation.MinFrameCount - minFrame, anim.FrameCount - maxFrame);

		if (delta == 0)
			return;

		var before = new Dictionary<KeyRef, AnimValue?>();
		var after = new Dictionary<KeyRef, AnimValue?>();

		void Remember(KeyRef cell)
		{
			if (!before.ContainsKey(cell))
				before[cell] = anim.GetKeyValue(cell.ShapeId, cell.ParamId, cell.Frame);
		}

		foreach (var src in sources)
		{
			Remember(src);
			Remember(src with { Frame = src.Frame + delta });
		}

		// Итоговое состояние: сначала источники пустеют, затем в цели
		// ложатся перенесённые значения.
		foreach (var cell in before.Keys)
			after[cell] = before[cell];

		foreach (var src in sources)
			after[src] = null;

		foreach (var src in sources)
			after[src with { Frame = src.Frame + delta }] = before[src];

		var edits = new List<AnimKeyEdit>();

		foreach (var cell in before.Keys)
		{
			var b = before[cell];
			var a = after[cell];

			bool same =
				(!b.HasValue && !a.HasValue) ||
				(b.HasValue && a.HasValue && b.Value.Serialize() == a.Value.Serialize());

			if (same)
				continue;

			edits.Add(new AnimKeyEdit
			{
				ShapeId = cell.ShapeId,
				ParamId = cell.ParamId,
				Title = TitleOf(cell.ShapeId, cell.ParamId),
				Frame = cell.Frame,
				Before = b,
				After = a
			});
		}

		_selectedKeys.Clear();

		foreach (var src in sources)
			_selectedKeys.Add(src with { Frame = src.Frame + delta });

		PushEdits(sources.Count == 1 ? "Move key" : $"Move {sources.Count} keys", edits);
	}

	/// <summary>Кадры, на которых у поддерева строки есть ключи.</summary>
	private HashSet<int> CollectKeyFrames(AnimRow row)
	{
		_frameBuffer.Clear();

		var anim = Anim?.Find(row.ShapeId);

		if (anim == null)
			return _frameBuffer;

		foreach (var id in row.ParamIds)
		{
			var track = anim.Find(id);

			if (track == null)
				continue;

			foreach (var key in track.Keys)
				_frameBuffer.Add(key.Frame);
		}

		return _frameBuffer;
	}

	private bool IsKeySelected(Guid shapeId, string paramId, int frame)
		=> _selectedKeys.Contains(new KeyRef(shapeId, paramId, frame));

	/// <summary>
	/// Ключи строки, как они должны быть НАРИСОВАНЫ: кадр, сколько ключей
	/// параметров стоит на нём и сколько из них выделено.
	///
	/// Общий для строки-параметра и строки-группы. У группы на одном кадре
	/// сходятся ключи нескольких параметров, и один ромб обозначает их все;
	/// счётчики нужны, чтобы отличить «выделены все» от «выделена часть» —
	/// при переносе выделенная часть уезжает, а невыделенная остаётся
	/// на месте, и оба ромба обязаны быть видны.
	///
	/// delta — сдвиг перетаскивания, применяемый только к выделенным.
	/// </summary>
	private Dictionary<int, (int Total, int Selected)> CollectDisplayKeys(AnimRow row, int delta)
	{
		_displayKeys.Clear();

		var anim = Anim?.Find(row.ShapeId);

		if (anim == null)
			return _displayKeys;

		foreach (var paramId in row.ParamIds)
		{
			var track = anim.Find(paramId);

			if (track == null)
				continue;

			foreach (var key in track.Keys)
			{
				bool selected = IsKeySelected(row.ShapeId, paramId, key.Frame);

				int frame = selected
					? Math.Clamp(key.Frame + delta, PageAnimation.MinFrameCount, FrameCount)
					: key.Frame;

				_displayKeys.TryGetValue(frame, out var cell);
				_displayKeys[frame] = (cell.Total + 1, cell.Selected + (selected ? 1 : 0));
			}
		}

		return _displayKeys;
	}

	/// <summary>Адреса ключей строки, стоящих на этом кадре.</summary>
	private List<KeyRef> KeysAt(AnimRow row, int frame)
	{
		var result = new List<KeyRef>();
		var anim = Anim?.Find(row.ShapeId);

		if (anim == null)
			return result;

		foreach (var paramId in row.ParamIds)
		{
			// Параметр без ключа на этом кадре не попадает в выделение
			// и переносом не затрагивается: общий ключ группы — это ровно
			// то, что уже записано, а не обещание записать.
			if (anim.Find(paramId)?.Find(frame) == null)
				continue;

			result.Add(new KeyRef(row.ShapeId, paramId, frame));
		}

		return result;
	}

	#endregion

	#region Отрисовка

	private void EnsureBuffers()
	{
		if (Width <= 0 || Height <= 0)
			return;

		bool recreate =
			_backBuffer == null ||
			_gdiBuffer == null ||
			_backBuffer.Width != Width ||
			_backBuffer.Height != Height ||
			_gdiBuffer.Width != Width ||
			_gdiBuffer.Height != Height;

		if (!recreate)
			return;

		_gdiBuffer?.Dispose();
		_backBuffer?.Dispose();

		_backBuffer = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		_gdiBuffer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
		_rowBuffer = null;
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

		ArrangeUi();
		EnsureViewValid();

		using (var canvas = new SKCanvas(_backBuffer))
		{
			canvas.Clear(pal.PanelBack2);

			DrawGrip(canvas, pal);
			DrawToolbar(canvas, pal);

			if (HasContentArea)
			{
				DrawTreePanel(canvas, pal);
				DrawTimelinePanel(canvas, pal);
				DrawSplitter(canvas, pal);
			}
		}

		Blit(e);
	}

	private void Blit(PaintEventArgs e)
	{
		if (_backBuffer == null || _gdiBuffer == null)
			return;

		var rect = new System.Drawing.Rectangle(0, 0, _gdiBuffer.Width, _gdiBuffer.Height);
		BitmapData data = _gdiBuffer.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

		try
		{
			int srcStride = _backBuffer.RowBytes;
			int dstStride = Math.Abs(data.Stride);
			int rowBytes = Math.Min(srcStride, dstStride);
			int height = Math.Min(_backBuffer.Height, data.Height);

			if (_rowBuffer == null || _rowBuffer.Length != rowBytes)
				_rowBuffer = new byte[rowBytes];

			IntPtr srcBase = _backBuffer.GetPixels();
			IntPtr dstBase = data.Scan0;

			for (int y = 0; y < height; y++)
			{
				IntPtr srcPtr = IntPtr.Add(srcBase, y * srcStride);
				IntPtr dstPtr = IntPtr.Add(dstBase, y * data.Stride);

				Marshal.Copy(srcPtr, _rowBuffer, 0, rowBytes);
				Marshal.Copy(_rowBuffer, 0, dstPtr, rowBytes);
			}
		}
		finally
		{
			_gdiBuffer.UnlockBits(data);
		}

		e.Graphics.DrawImageUnscaled(_gdiBuffer, 0, 0);
	}

	/// <summary>
	/// Полоса изменения высоты. Три состояния и насечка посередине — тот же
	/// приём, что у разделителя панели свойств: почти невидимая полоса
	/// не подсказывает, что её можно тянуть.
	/// </summary>
	private void DrawGrip(SKCanvas canvas, SkUiPalette pal)
	{
		var area = new SKRect(0, 0, Width, GripHeight);
		bool active = _drag == DragKind.Height;

		if (active)
		{
			using var accent = SkUiTheme.Fill(pal.Accent);
			canvas.DrawRect(area, accent);
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

		var markColor = active || _gripHovered
			? WithAlpha(pal.Text, 200)
			: WithAlpha(pal.Text, 90);

		using var mark = SkUiTheme.Fill(markColor);

		float cx = Width * 0.5f;
		float cy = GripHeight * 0.5f;

		for (int i = -1; i <= 1; i++)
			canvas.DrawCircle(cx + i * 5f, cy, 1.2f, mark);
	}

	private void DrawToolbar(SKCanvas canvas, SkUiPalette pal)
	{
		var area = new SKRect(0, GripHeight, Width, ContentTop);

		using var back = SkUiTheme.Fill(pal.PanelBack);
		using var border = SkUiTheme.Stroke(pal.Border);

		canvas.DrawRect(area, back);
		canvas.DrawLine(0, ContentTop, Width, ContentTop, border);

		_root.Draw(canvas);
		_root.DrawOverlay(canvas);

		var metrics = SkTextHelper.GetMetrics(FontRow);
		float baseline = GripHeight + ToolbarHeight * 0.5f + (metrics.Ascent - metrics.Descent) * 0.5f;

		SkTextHelper.DrawLeft(canvas, "Frame", _frameLabelX, baseline, FontRow, pal.TextMuted);
		SkTextHelper.DrawLeft(canvas, "Frames", _framesLabelX, baseline, FontRow, pal.TextMuted);
		SkTextHelper.DrawLeft(canvas, "FPS", _fpsLabelX, baseline, FontRow, pal.TextMuted);
	}

	private void DrawSplitter(SKCanvas canvas, SkUiPalette pal)
	{
		var area = SplitterRect;
		bool active = _drag == DragKind.Tree;

		var color = active
			? pal.Accent
			: _splitterHovered
				? WithAlpha(pal.Text, 70)
				: WithAlpha(pal.Text, 28);

		using var fill = SkUiTheme.Fill(color);
		canvas.DrawRect(area, fill);

		if (area.Height < 40f)
			return;

		var markColor = active || _splitterHovered
			? WithAlpha(pal.Text, 200)
			: WithAlpha(pal.Text, 90);

		using var mark = SkUiTheme.Fill(markColor);

		for (int i = -1; i <= 1; i++)
			canvas.DrawCircle(area.MidX, area.MidY + i * 5f, 1.2f, mark);
	}

	private void DrawTreePanel(SKCanvas canvas, SkUiPalette pal)
	{
		using var back = SkUiTheme.Fill(pal.PanelBack);
		using var border = SkUiTheme.Stroke(pal.Border);
		using var headerBack = SkUiTheme.Fill(pal.Surface2);

		canvas.DrawRect(TreeRect, back);
		canvas.DrawRect(new SKRect(0, HeaderTop, _treeWidth, HeaderTop + HeaderHeight), headerBack);
		canvas.DrawLine(0, RowsTop, _treeWidth, RowsTop, border);

		var hm = SkTextHelper.GetMetrics(FontSmall, true);
		float headerBaseline = HeaderTop + HeaderHeight * 0.5f + (hm.Ascent - hm.Descent) * 0.5f;

		SkTextHelper.DrawLeft(canvas, "Parameter", 8f, headerBaseline, FontSmall, pal.TextMuted, true);

		if (_treeWidth > 240f)
		{
			float valueLabelWidth = SkTextHelper.Measure("Value", FontSmall, true);
			SkTextHelper.DrawLeft(canvas, "Value", _treeWidth - 8f - valueLabelWidth, headerBaseline, FontSmall, pal.TextMuted, true);
		}

		if (_rows.Count == 0)
		{
			SkTextHelper.DrawLeft(canvas, "No object selected", 8f, RowsTop + RowHeight, FontRow, pal.TextMuted);
			return;
		}

		canvas.Save();
		canvas.ClipRect(new SKRect(0, RowsTop, _treeWidth, RowsBottom));

		for (int i = 0; i < _rows.Count; i++)
		{
			float y = RowY(i);

			if (y + RowHeight < RowsTop || y > RowsBottom)
				continue;

			DrawTreeRow(canvas, pal, _rows[i], y);
		}

		canvas.Restore();
	}

	private void DrawTreeRow(SKCanvas canvas, SkUiPalette pal, AnimRow row, float y)
	{
		var rowRect = new SKRect(0, y, _treeWidth, y + RowHeight);

		if (row.Depth == 0)
		{
			using var shapeBack = SkUiTheme.Fill(pal.Surface2);
			canvas.DrawRect(rowRect, shapeBack);
		}
		else if (row.IsGroup)
		{
			using var groupBack = SkUiTheme.Fill(WithAlpha(pal.Text, 8));
			canvas.DrawRect(rowRect, groupBack);
		}

		float x = 6f + row.Depth * 14f;

		// Треугольник раскрытия.
		if (row.HasChildren)
		{
			var box = new SKRect(x, y + RowHeight * 0.5f - 5f, x + 10f, y + RowHeight * 0.5f + 5f);
			bool open = _expandedKeys.Contains(row.Key);

			using var tri = SkUiTheme.Fill(pal.TextMuted);
			using var path = new SKPath();

			if (open)
			{
				path.MoveTo(box.Left, box.Top + 1f);
				path.LineTo(box.Right, box.Top + 1f);
				path.LineTo(box.MidX, box.Bottom - 1f);
			}
			else
			{
				path.MoveTo(box.Left + 1f, box.Top);
				path.LineTo(box.Right - 1f, box.MidY);
				path.LineTo(box.Left + 1f, box.Bottom);
			}

			path.Close();
			canvas.DrawPath(path, tri);
		}

		x += 14f;

		// Галочка отбора параметров для записи ключа.
		var check = new SKRect(x, y + RowHeight * 0.5f - 6f, x + 12f, y + RowHeight * 0.5f + 6f);
		int state = CheckStateOf(row);

		using (var checkBack = SkUiTheme.Fill(state == 0 ? pal.Surface : pal.Accent))
		using (var checkBorder = SkUiTheme.Stroke(pal.Border))
		{
			canvas.DrawRoundRect(check, 3f, 3f, checkBack);
			canvas.DrawRoundRect(check, 3f, 3f, checkBorder);
		}

		if (state == 1)
		{
			using var tick = SkUiTheme.Stroke(SKColors.White, 1.8f);
			canvas.DrawLine(check.Left + 2.5f, check.MidY, check.MidX - 0.5f, check.Bottom - 3f, tick);
			canvas.DrawLine(check.MidX - 0.5f, check.Bottom - 3f, check.Right - 2.5f, check.Top + 3f, tick);
		}
		else if (state == 2)
		{
			using var dash = SkUiTheme.Stroke(SKColors.White, 1.8f);
			canvas.DrawLine(check.Left + 3f, check.MidY, check.Right - 3f, check.MidY, dash);
		}

		x += 18f;

		var metrics = SkTextHelper.GetMetrics(FontRow);
		float baseline = y + RowHeight * 0.5f + (metrics.Ascent - metrics.Descent) * 0.5f;

		var titleColor = row.Depth == 0 || row.IsGroup ? pal.Text : pal.TextMuted;

		var param = row.Param;
		bool isColor = param != null && param.Kind == AnimValueKind.Color;

		// Цвет показывается образцом, а не строкой #AARRGGBB: восемь
		// шестнадцатеричных цифр в строке таблицы читаются заметно
		// медленнее, чем сам цвет виден глазом.
		float valueWidth = 0f;
		string valueText = string.Empty;

		if (isColor)
		{
			valueWidth = 26f;
		}
		else if (param != null)
		{
			valueText = param.Get().Format();
			valueWidth = SkTextHelper.Measure(valueText, FontSmall);
		}

		float titleLimit = _treeWidth - x - valueWidth - 16f;

		SkTextHelper.DrawLeft(canvas, Ellipsize(row.Node.Title, FontRow, titleLimit, row.Depth == 0), x, baseline, FontRow, titleColor, row.Depth == 0);

		if (titleLimit <= 20f || param == null)
			return;

		if (isColor)
		{
			var swatch = new SKRect(
				_treeWidth - 8f - valueWidth,
				y + RowHeight * 0.5f - 7f,
				_treeWidth - 8f,
				y + RowHeight * 0.5f + 7f);

			DrawColorSwatch(canvas, pal, swatch, param.Get().AsColor);
			return;
		}

		SkTextHelper.DrawLeft(canvas, valueText, _treeWidth - 8f - valueWidth, baseline, FontSmall, pal.TextMuted);
	}

	/// <summary>
	/// Образец цвета со скруглёнными углами.
	///
	/// Под цветом лежит шахматка: полупрозрачная заливка на однотонном фоне
	/// панели выглядела бы просто более светлым цветом, и отличить её
	/// от непрозрачной было бы нельзя.
	/// </summary>
	private static void DrawColorSwatch(SKCanvas canvas, SkUiPalette pal, SKRect rect, SKColor color)
	{
		using var light = SkUiTheme.Fill(new SKColor(255, 255, 255));
		using var dark = SkUiTheme.Fill(new SKColor(205, 205, 205));

		canvas.Save();

		var clip = new SKRoundRect(rect, 3f, 3f);
		canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);

		canvas.DrawRect(rect, light);

		float cell = 5f;
		int rows = (int)MathF.Ceiling(rect.Height / cell);
		int cols = (int)MathF.Ceiling(rect.Width / cell);

		for (int r = 0; r < rows; r++)
		{
			for (int c = 0; c < cols; c++)
			{
				if ((r + c) % 2 == 0)
					continue;

				canvas.DrawRect(
					new SKRect(
						rect.Left + c * cell,
						rect.Top + r * cell,
						rect.Left + (c + 1) * cell,
						rect.Top + (r + 1) * cell),
					dark);
			}
		}

		using var fill = SkUiTheme.Fill(color);
		canvas.DrawRect(rect, fill);

		canvas.Restore();

		using var border = SkUiTheme.Stroke(pal.Border);
		canvas.DrawRoundRect(rect, 3f, 3f, border);
	}

	private static string Ellipsize(string text, float fontSize, float maxWidth, bool bold)
	{
		if (maxWidth <= 8f || text.Length == 0)
			return string.Empty;

		if (SkTextHelper.Measure(text, fontSize, bold) <= maxWidth)
			return text;

		for (int len = text.Length - 1; len > 1; len--)
		{
			string candidate = text.Substring(0, len) + "…";

			if (SkTextHelper.Measure(candidate, fontSize, bold) <= maxWidth)
				return candidate;
		}

		return "…";
	}

	private void DrawTimelinePanel(SKCanvas canvas, SkUiPalette pal)
	{
		float left = TimelineLeft;

		using var back = SkUiTheme.Fill(pal.PanelBack2);
		using var headerBack = SkUiTheme.Fill(pal.Surface2);
		using var border = SkUiTheme.Stroke(pal.Border);

		canvas.DrawRect(new SKRect(left, ContentTop, Width, Height), back);
		canvas.DrawRect(RulerRect, headerBack);
		canvas.DrawLine(left, RowsTop, Width, RowsTop, border);

		DrawRuler(canvas, pal);

		canvas.Save();
		canvas.ClipRect(new SKRect(left, RowsTop, Width, RowsBottom));

		DrawFrameGrid(canvas, pal);

		// Три прохода, а не один: фон строк и полоски интерполяции ложатся
		// первыми, поверх них идёт линия текущего кадра, и только затем
		// ромбы ключей. Иначе фон следующей строки затирал бы линию,
		// а сама линия перечёркивала бы ключи — а ключ здесь главное.
		for (int i = 0; i < _rows.Count; i++)
		{
			float y = RowY(i);

			if (y + RowHeight < RowsTop || y > RowsBottom)
				continue;

			DrawKeyRowBackground(canvas, pal, _rows[i], y);
		}

		DrawPlayheadLine(canvas, pal);

		for (int i = 0; i < _rows.Count; i++)
		{
			float y = RowY(i);

			if (y + RowHeight < RowsTop || y > RowsBottom)
				continue;

			DrawKeyRowDiamonds(canvas, pal, _rows[i], y);
		}

		DrawDragRect(canvas, pal);

		canvas.Restore();

		DrawHScrollBar(canvas, pal);
		DrawPlayheadHandle(canvas, pal);
	}

	/// <summary>
	/// Еле заметные вертикальные линии по кадрам: по ним видно расстояние
	/// между ключами в кадрах, а не только на глаз.
	///
	/// При сильном отдалении линии не рисуются вовсе: на полутора пикселях
	/// на кадр они сливаются в ровную заливку, которая ничего не показывает,
	/// зато стоит тысячи вызовов отрисовки на каждый кадр.
	/// </summary>
	private void DrawFrameGrid(SKCanvas canvas, SkUiPalette pal)
	{
		float pxPerFrame = (TrackRight - TrackLeft) / MathF.Max(MinViewSpan, _viewLast - _viewFirst);

		if (pxPerFrame < 4f)
			return;

		using var faint = SkUiTheme.Stroke(WithAlpha(pal.Text, 16), 1f);

		int first = Math.Max(1, (int)MathF.Floor(_viewFirst));
		int last = Math.Min(FrameCount, (int)MathF.Ceiling(_viewLast));

		for (int f = first; f <= last; f++)
		{
			// Полупиксельный сдвиг: иначе сглаживание размывает
			// однопиксельную линию на две и сетка выглядит грязной.
			float x = MathF.Round(FrameX(f)) + 0.5f;
			canvas.DrawLine(x, RowsTop, x, RowsBottom, faint);
		}
	}

	private void DrawRuler(SKCanvas canvas, SkUiPalette pal)
	{
		int count = FrameCount;

		using var tick = SkUiTheme.Stroke(WithAlpha(pal.Text, 70));
		using var strongTick = SkUiTheme.Stroke(WithAlpha(pal.Text, 150));

		float pxPerFrame = (TrackRight - TrackLeft) / MathF.Max(MinViewSpan, _viewLast - _viewFirst);

		int labelStep = 1;

		while (labelStep * MathF.Max(pxPerFrame, 0.01f) < MinLabelSpacing)
			labelStep = labelStep < 5 ? labelStep + 1 : labelStep + 5;

		var metrics = SkTextHelper.GetMetrics(FontTiny);
		float baseline = RulerRect.Top + HeaderHeight * 0.5f + (metrics.Ascent - metrics.Descent) * 0.5f;

		bool drawEveryFrame = pxPerFrame >= 6f;

		int first = Math.Max(1, (int)MathF.Floor(_viewFirst));
		int last = Math.Min(count, (int)MathF.Ceiling(_viewLast));

		canvas.Save();
		canvas.ClipRect(new SKRect(TimelineLeft, RulerRect.Top, Width, RulerRect.Bottom));

		for (int f = first; f <= last; f++)
		{
			float x = FrameX(f);
			bool labelled = f == 1 || f == count || (f - 1) % labelStep == 0;

			if (labelled)
			{
				canvas.DrawLine(x, RulerRect.Bottom - 8f, x, RulerRect.Bottom, strongTick);
				SkTextHelper.DrawCenter(canvas, f.ToString(), x, baseline - 2f, FontTiny, pal.TextMuted);
			}
			else if (drawEveryFrame)
			{
				canvas.DrawLine(x, RulerRect.Bottom - 4f, x, RulerRect.Bottom, tick);
			}
		}

		canvas.Restore();
	}

	/// <summary>Фон строки, её дорожка и полоски интерполяции.</summary>
	private void DrawKeyRowBackground(SKCanvas canvas, SkUiPalette pal, AnimRow row, float y)
	{
		float centerY = y + RowHeight * 0.5f;

		if (row.Depth == 0)
		{
			using var shapeBack = SkUiTheme.Fill(WithAlpha(pal.Text, 10));
			canvas.DrawRect(new SKRect(TimelineLeft, y, Width, y + RowHeight), shapeBack);
		}

		using var line = SkUiTheme.Stroke(WithAlpha(pal.Text, 20));
		canvas.DrawLine(TrackLeft, centerY, TrackRight, centerY, line);

		string paramId = row.ParamIds.Length > 0 ? row.ParamIds[0] : string.Empty;

		// Серая полоска между соседними ключами: на этом промежутке
		// значение считается интерполяцией, а не берётся из фигуры.
		if (!row.IsGroup && paramId.Length > 0)
			DrawInterpolationBars(canvas, pal, row, paramId, centerY);
	}

	/// <summary>Сами ключи. Отдельным проходом — поверх линии текущего кадра.</summary>
	private void DrawKeyRowDiamonds(SKCanvas canvas, SkUiPalette pal, AnimRow row, float y)
	{
		float centerY = y + RowHeight * 0.5f;

		int delta = _drag == DragKind.Keys ? _keyDragDelta : 0;
		var display = CollectDisplayKeys(row, delta);

		if (display.Count == 0)
			return;

		bool group = row.IsGroup;
		float radius = group ? KeyRadius * 0.72f : KeyRadius;

		using var normal = SkUiTheme.Fill(group ? WithAlpha(pal.Accent, 150) : pal.Accent);
		using var selected = SkUiTheme.Fill(pal.IsDark ? new SKColor(255, 205, 90) : new SKColor(224, 132, 0));
		using var selectedRing = SkUiTheme.Stroke(pal.IsDark ? SKColors.White : new SKColor(70, 45, 0), 1.6f);

		foreach (var pair in display)
		{
			float x = FrameX(pair.Key);

			if (x < TimelineLeft - radius || x > Width + radius)
				continue;

			var cell = pair.Value;
			bool allSelected = cell.Selected > 0 && cell.Selected == cell.Total;

			AnimIconButton.FillDiamond(canvas, allSelected ? selected : normal, new SKPoint(x, centerY), radius);

			if (cell.Selected == 0)
				continue;

			// Обводка ставится и при частичном выделении: иначе ромб группы,
			// половина ключей которой выделена, выглядел бы невыделенным.
			using var path = new SKPath();
			path.MoveTo(x, centerY - radius - 2f);
			path.LineTo(x + radius + 2f, centerY);
			path.LineTo(x, centerY + radius + 2f);
			path.LineTo(x - radius - 2f, centerY);
			path.Close();
			canvas.DrawPath(path, selectedRing);
		}
	}

	/// <summary>
	/// Полоски между соседними ключами дорожки.
	///
	/// У логических параметров перехода нет — значение держится
	/// до следующего ключа ступенькой, поэтому их полоска тоньше и тусклее:
	/// одинаковая ввела бы в заблуждение насчёт плавности.
	/// </summary>
	private void DrawInterpolationBars(SKCanvas canvas, SkUiPalette pal, AnimRow row, string paramId, float centerY)
	{
		var track = Anim?.Find(row.ShapeId)?.Find(paramId);

		if (track == null || track.Keys.Count < 2)
			return;

		bool stepped = track.Keys[0].Value.Kind == AnimValueKind.Bool;

		using var bar = SkUiTheme.Fill(WithAlpha(pal.Text, stepped ? (byte)45 : (byte)85));

		float half = stepped ? 1.2f : 2f;
		int delta = _drag == DragKind.Keys ? _keyDragDelta : 0;

		for (int i = 0; i < track.Keys.Count - 1; i++)
		{
			int a = track.Keys[i].Frame;
			int b = track.Keys[i + 1].Frame;

			if (IsKeySelected(row.ShapeId, paramId, a))
				a = Math.Clamp(a + delta, PageAnimation.MinFrameCount, FrameCount);

			if (IsKeySelected(row.ShapeId, paramId, b))
				b = Math.Clamp(b + delta, PageAnimation.MinFrameCount, FrameCount);

			float x1 = FrameX(MathF.Min(a, b)) + KeyRadius;
			float x2 = FrameX(MathF.Max(a, b)) - KeyRadius;

			if (x2 <= x1 || x2 < TimelineLeft || x1 > Width)
				continue;

			canvas.DrawRoundRect(new SKRect(x1, centerY - half, x2, centerY + half), half, half, bar);
		}
	}

	/// <summary>
	/// Вертикальная черта текущего кадра: ровно один пиксель и цветом
	/// светлее ключей, чтобы не спорить с ними за внимание.
	/// </summary>
	private void DrawPlayheadLine(SKCanvas canvas, SkUiPalette pal)
	{
		if (Anim == null)
			return;

		// Полупиксельный сдвиг: линия шириной в пиксель, проведённая
		// по целой координате, размазывается сглаживанием на два.
		float x = MathF.Round(FrameX(Anim.CurrentFrame)) + 0.5f;

		using var paint = SkUiTheme.Stroke(Lighten(pal.Accent, 0.5f), 1f);
		canvas.DrawLine(x, RowsTop, x, RowsBottom, paint);
	}

	/// <summary>Ручка прохода по всей анимации: язычок на линейке кадров.</summary>
	private void DrawPlayheadHandle(SKCanvas canvas, SkUiPalette pal)
	{
		if (Anim == null)
			return;

		float x = FrameX(Anim.CurrentFrame);

		if (x < TimelineLeft || x > Width)
			return;

		string label = Anim.CurrentFrame.ToString();
		float labelWidth = SkTextHelper.Measure(label, FontTiny, true);
		float handleWidth = MathF.Max(26f, labelWidth + 14f);

		var handle = new SKRect(
			x - handleWidth * 0.5f,
			RulerRect.Top + 2f,
			x + handleWidth * 0.5f,
			RulerRect.Bottom - 2f);

		using var fill = SkUiTheme.Fill(_drag == DragKind.Scrub ? pal.Accent : WithAlpha(pal.Accent, 225));
		using var border = SkUiTheme.Stroke(pal.Accent, 1f);

		canvas.DrawRoundRect(handle, 4f, 4f, fill);
		canvas.DrawRoundRect(handle, 4f, 4f, border);

		var metrics = SkTextHelper.GetMetrics(FontTiny, true);
		float baseline = handle.MidY + (metrics.Ascent - metrics.Descent) * 0.5f;

		SkTextHelper.DrawCenter(canvas, label, handle.MidX, baseline, FontTiny, SKColors.White, true);
	}

	/// <summary>Рамка выделения ключей и рамка выбора участка для приближения.</summary>
	private void DrawDragRect(SKCanvas canvas, SkUiPalette pal)
	{
		if (!_dragMoved)
			return;

		if (_drag == DragKind.Rubber)
		{
			var rect = Normalize(_dragStart, _dragNow);

			using var fill = SkUiTheme.Fill(WithAlpha(pal.Accent, 40));
			using var border = SkUiTheme.Stroke(pal.Accent, 1f);

			canvas.DrawRect(rect, fill);
			canvas.DrawRect(rect, border);
			return;
		}

		if (_drag != DragKind.ZoomRange)
			return;

		// Участок приближения выделяется по всей высоте: он относится
		// к кадрам, а не к строкам, и рамка по вертикали вводила бы
		// в заблуждение.
		var band = new SKRect(MathF.Min(_dragStart.X, _dragNow.X), RowsTop, MathF.Max(_dragStart.X, _dragNow.X), RowsBottom);

		using var bandFill = SkUiTheme.Fill(WithAlpha(pal.Accent, 45));
		using var bandBorder = SkUiTheme.Stroke(pal.Accent, 1f);

		canvas.DrawRect(band, bandFill);
		canvas.DrawLine(band.Left, RowsTop, band.Left, RowsBottom, bandBorder);
		canvas.DrawLine(band.Right, RowsTop, band.Right, RowsBottom, bandBorder);
	}

	/// <summary>
	/// Горизонтальная полоса прокрутки кадров.
	///
	/// Рисуется своими руками, а не системным HScrollBar: тот — оконный
	/// контрол, а панель целиком выводится одной картинкой, и вставка
	/// системного элемента внутрь неё дала бы чужеродную рамку и вечную
	/// возню с координатами.
	/// </summary>
	private void DrawHScrollBar(SKCanvas canvas, SkUiPalette pal)
	{
		var area = ScrollBarRect;

		if (area.Width <= 2f)
			return;

		using var trackFill = SkUiTheme.Fill(WithAlpha(pal.Text, 14));
		canvas.DrawRoundRect(area, area.Height * 0.4f, area.Height * 0.4f, trackFill);

		var thumb = ThumbRect();

		using var thumbFill = SkUiTheme.Fill(_drag == DragKind.HScroll ? pal.Accent : WithAlpha(pal.Text, 90));
		canvas.DrawRoundRect(thumb, thumb.Height * 0.4f, thumb.Height * 0.4f, thumbFill);
	}

	private SKRect ThumbRect()
	{
		var area = ScrollBarRect;

		float full = MathF.Max(1f, FrameCount - 1);

		float from = Math.Clamp((_viewFirst - 1f) / full, 0f, 1f);
		float to = Math.Clamp((_viewLast - 1f) / full, 0f, 1f);

		float left = area.Left + from * area.Width;
		float right = area.Left + to * area.Width;

		// Минимальная ширина ползунка: на анимации в тысячу кадров
		// приближенный участок дал бы полоску в один пиксель.
		if (right - left < 24f)
		{
			float mid = (left + right) * 0.5f;
			left = Math.Clamp(mid - 12f, area.Left, MathF.Max(area.Left, area.Right - 24f));
			right = left + 24f;
		}

		return new SKRect(left, area.Top, right, area.Bottom);
	}

	private static SKRect Normalize(SKPoint a, SKPoint b)
		=> new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

	#endregion

	#region Ввод

	private bool IsOverGrip(float y) => y <= GripHeight;

	private bool IsOverSplitter(float x, float y)
		=> HasContentArea && y > ContentTop && x >= _treeWidth && x <= _treeWidth + SplitterWidth;

	private bool IsInToolbar(float y) => y > GripHeight && y < ContentTop;

	private bool IsInRuler(float x, float y)
		=> HasContentArea && x > TimelineLeft && y >= HeaderTop && y < RowsTop;

	private bool IsInScrollBar(float x, float y)
		=> HasContentArea && x > TimelineLeft && y >= ScrollBarTop;

	private bool IsInKeyArea(float x, float y)
		=> HasContentArea && x > TimelineLeft && y >= RowsTop && y < RowsBottom;

	private bool IsOnPlayheadHandle(float x, float y)
	{
		if (Anim == null || !IsInRuler(x, y))
			return false;

		return MathF.Abs(FrameX(Anim.CurrentFrame) - x) <= 14f;
	}

	private int RowIndexAt(float y)
	{
		if (y < RowsTop || y > RowsBottom)
			return -1;

		int index = (int)((y - RowsTop + _scrollY) / RowHeight);
		return index >= 0 && index < _rows.Count ? index : -1;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);

		Focus();

		_dragStart = new SKPoint(e.X, e.Y);
		_dragNow = _dragStart;
		_dragMoved = false;

		// Щелчок мимо тулбара снимает фокус с числовых полей: иначе
		// каретка продолжала бы мигать в поле, которое уже никто не правит,
		// а рамка фокуса вводила бы в заблуждение, куда пойдут клавиши.
		if (!IsInToolbar(e.Y))
			SetFocused(null);

		if (IsOverGrip(e.Y))
		{
			BeginDrag(DragKind.Height);
			_dragStartHeight = Expanded ? Height : _expandedHeight;
			_dragStartScreenY = MousePosition.Y;
			return;
		}

		if (IsInToolbar(e.Y))
		{
			RouteMouseDown(e);
			return;
		}

		if (IsOverSplitter(e.X, e.Y))
		{
			BeginDrag(DragKind.Tree);
			_dragStartTreeWidth = _treeWidth;
			_dragStartScreenX = MousePosition.X;
			return;
		}

		if (!HasContentArea)
			return;

		// Средняя кнопка в правой части панели двигает вид: и по кадрам,
		// и по строкам. Привычка из графических редакторов.
		if (e.Button == MouseButtons.Middle && e.X > _treeWidth)
		{
			BeginDrag(DragKind.PanView);
			_panStartViewFirst = _viewFirst;
			_panStartScrollY = _scrollY;
			Cursor = Cursors.SizeAll;
			return;
		}

		if (IsInScrollBar(e.X, e.Y))
		{
			BeginScrollBarDrag(e.X);
			return;
		}

		if (IsInRuler(e.X, e.Y))
		{
			if (a_zoom_but.Checked)
			{
				BeginDrag(DragKind.ZoomRange);
				return;
			}

			// Ручка тянет кадр, остальная линейка тянет сам вид.
			// Одиночный щелчок по линейке при этом всё равно ставит кадр —
			// см. завершение перетаскивания.
			if (IsOnPlayheadHandle(e.X, e.Y))
			{
				BeginDrag(DragKind.Scrub);
				SetCurrentFrame(FrameFromX(e.X));
				return;
			}

			BeginDrag(DragKind.PanView);
			_panStartViewFirst = _viewFirst;
			_panStartScrollY = _scrollY;
			return;
		}

		if (e.Y < RowsTop)
			return;

		if (e.X < _treeWidth)
		{
			int treeIndex = RowIndexAt(e.Y);

			if (treeIndex >= 0)
				HandleTreeClick(_rows[treeIndex], e.X);

			return;
		}

		if (!IsInKeyArea(e.X, e.Y))
			return;

		if (a_zoom_but.Checked)
		{
			BeginDrag(DragKind.ZoomRange);
			return;
		}

		int index = RowIndexAt(e.Y);
		AnimRow? row = index >= 0 ? _rows[index] : null;

		if (row != null && TryHitKey(row, e.X, out int hitFrame))
		{
			HandleKeyPress(row, hitFrame);
			return;
		}

		// Пустое место: рамка выделения. Если мышь не сдвинулась,
		// это окажется обычным щелчком — он снимает выделение
		// и переводит текущий кадр.
		if ((ModifierKeys & Keys.Shift) == 0)
			_selectedKeys.Clear();

		BeginDrag(DragKind.Rubber);
		Invalidate();
	}

	private void BeginDrag(DragKind kind)
	{
		_drag = kind;
		Capture = true;
	}

	private void BeginScrollBarDrag(float x)
	{
		var thumb = ThumbRect();

		if (x < thumb.Left || x > thumb.Right)
		{
			// Щелчок мимо ползунка ставит его серединой под курсор:
			// прыжок на страницу тут менее предсказуем, чем прямое
			// попадание в нужное место анимации.
			MoveViewByScrollBar(x, thumb.Width * 0.5f);
			thumb = ThumbRect();
		}

		_hscrollGrab = x - thumb.Left;
		BeginDrag(DragKind.HScroll);
		Invalidate();
	}

	/// <summary>Ставит левый край окна просмотра по положению ползунка.</summary>
	private void MoveViewByScrollBar(float x, float grab)
	{
		var area = ScrollBarRect;

		if (area.Width < 1f)
			return;

		float span = _viewLast - _viewFirst;
		float t = Math.Clamp((x - grab - area.Left) / area.Width, 0f, 1f);

		_viewFirst = 1f + t * MathF.Max(1f, FrameCount - 1);
		_viewLast = _viewFirst + span;

		EnsureViewValid();
	}

	private bool TryHitKey(AnimRow row, float x, out int frame)
	{
		frame = -1;

		var frames = CollectKeyFrames(row);

		foreach (int f in frames)
		{
			if (MathF.Abs(FrameX(f) - x) <= KeyRadius + 3f)
			{
				frame = f;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Нажатие на ромб. Для строки-группы (фигура целиком, Stroke, Fill…)
	/// берутся ВСЕ ключи её параметров, стоящие на этом кадре: щелчок
	/// по общему ромбу выделяет и двигает их разом. Параметры, у которых
	/// ключа на этом кадре нет, не затрагиваются — новых ключей общий
	/// ромб не создаёт.
	/// </summary>
	private void HandleKeyPress(AnimRow row, int frame)
	{
		var keys = KeysAt(row, frame);

		if (keys.Count == 0)
			return;

		bool shift = (ModifierKeys & Keys.Shift) != 0;

		if (shift)
		{
			// Повторный Shift-щелчок по уже выделенному ромбу снимает
			// выделение целиком, а не по одному ключу за раз.
			bool allSelected = keys.All(_selectedKeys.Contains);

			foreach (var key in keys)
			{
				if (allSelected)
					_selectedKeys.Remove(key);
				else
					_selectedKeys.Add(key);
			}
		}
		else if (!keys.All(_selectedKeys.Contains))
		{
			_selectedKeys.Clear();

			foreach (var key in keys)
				_selectedKeys.Add(key);
		}

		_keyDragDelta = 0;
		BeginDrag(DragKind.Keys);
		Invalidate();
	}

	private void HandleTreeClick(AnimRow row, float x)
	{
		float expanderX = 6f + row.Depth * 14f;

		if (row.HasChildren && x >= expanderX - 2f && x <= expanderX + 14f)
		{
			ToggleExpanded(row);
			return;
		}

		float checkX = expanderX + 14f;

		if (x >= checkX - 2f && x <= checkX + 16f)
		{
			ToggleCheck(row);
			return;
		}

		// Щелчок по имени тоже раскрывает узел: попадать в треугольник
		// шириной десять пикселей на каждом шаге — лишняя точность.
		if (row.HasChildren)
			ToggleExpanded(row);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		_dragNow = new SKPoint(e.X, e.Y);

		if (_drag != DragKind.None &&
			(MathF.Abs(_dragNow.X - _dragStart.X) > DragThreshold || MathF.Abs(_dragNow.Y - _dragStart.Y) > DragThreshold))
		{
			_dragMoved = true;
		}

		UpdateHoverState(e.X, e.Y);

		switch (_drag)
		{
			case DragKind.Height:
				SetPanelHeight(_dragStartHeight - (MousePosition.Y - _dragStartScreenY));
				return;

			case DragKind.Tree:
			{
				int dx = MousePosition.X - _dragStartScreenX;
				float max = MathF.Min(TreeMaxWidth, MathF.Max(TreeMinWidth, Width - 260f));
				float target = Math.Clamp(_dragStartTreeWidth + dx, TreeMinWidth, max);

				if (MathF.Abs(target - _treeWidth) > 0.5f)
				{
					_treeWidth = target;
					Invalidate();
				}

				return;
			}

			case DragKind.Scrub:
				SetCurrentFrame(FrameFromX(e.X));
				return;

			case DragKind.PanView:
			{
				float span = _viewLast - _viewFirst;
				float framesPerPixel = span / MathF.Max(1f, TrackRight - TrackLeft);

				_viewFirst = _panStartViewFirst - (_dragNow.X - _dragStart.X) * framesPerPixel;
				_viewLast = _viewFirst + span;

				EnsureViewValid();

				_scrollY = _panStartScrollY - (_dragNow.Y - _dragStart.Y);
				ClampScroll();

				Invalidate();
				return;
			}

			case DragKind.HScroll:
				MoveViewByScrollBar(e.X, _hscrollGrab);
				Invalidate();
				return;

			case DragKind.Keys:
			{
				int delta = FrameFromX(e.X) - FrameFromX(_dragStart.X);

				if (delta != _keyDragDelta)
				{
					_keyDragDelta = delta;
					Invalidate();
				}

				return;
			}

			case DragKind.Rubber:
			case DragKind.ZoomRange:
				Invalidate();
				return;
		}

		if (IsInToolbar(e.Y) || _captured != null)
		{
			var ev = ToSk(e);
			bool changed = _captured != null
				? _captured.OnMouseMove(ev)
				: (_root.FindTopMost(ev.Location)?.OnMouseMove(ev) ?? false);

			if (changed)
				Invalidate();
		}
	}

	private void UpdateHoverState(float x, float y)
	{
		bool grip = _drag == DragKind.Height || IsOverGrip(y);
		bool splitter = _drag == DragKind.Tree || IsOverSplitter(x, y);

		Cursor =
			grip ? Cursors.SizeNS :
			splitter ? Cursors.SizeWE :
			_drag == DragKind.PanView ? Cursors.SizeAll :
			a_zoom_but.Checked && (IsInKeyArea(x, y) || IsInRuler(x, y)) ? Cursors.Cross :
			_drag == DragKind.Scrub || IsOnPlayheadHandle(x, y) ? Cursors.Hand :
			IsInRuler(x, y) ? Cursors.SizeWE :
			Cursors.Default;

		if (grip != _gripHovered || splitter != _splitterHovered)
		{
			_gripHovered = grip;
			_splitterHovered = splitter;
			Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);

		if (_drag != DragKind.None)
			return;

		if (!_gripHovered && !_splitterHovered)
			return;

		_gripHovered = false;
		_splitterHovered = false;
		Cursor = Cursors.Default;
		Invalidate();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		var kind = _drag;
		_drag = DragKind.None;

		if (kind != DragKind.None)
			Capture = false;

		switch (kind)
		{
			case DragKind.Height:
			case DragKind.Tree:
			case DragKind.HScroll:
			case DragKind.Scrub:
				UpdateHoverState(e.X, e.Y);
				Invalidate();
				return;

			case DragKind.PanView:
				UpdateHoverState(e.X, e.Y);

				// Линейку можно просто щёлкнуть: раз вид не сдвинулся,
				// человек метил в кадр, а не в перетаскивание.
				if (!_dragMoved && IsInRuler(e.X, e.Y))
					SetCurrentFrame(FrameFromX(e.X));

				Invalidate();
				return;

			case DragKind.Keys:
				if (_dragMoved && _keyDragDelta != 0)
					MoveSelectedKeys(_keyDragDelta);

				_keyDragDelta = 0;
				Invalidate();
				return;

			case DragKind.Rubber:
				if (_dragMoved)
					SelectKeysInRect(Normalize(_dragStart, new SKPoint(e.X, e.Y)));
				else
					SetCurrentFrame(FrameFromX(e.X));

				Invalidate();
				return;

			case DragKind.ZoomRange:
				if (_dragMoved)
				{
					ZoomToRange(FrameFromXExact(_dragStart.X), FrameFromXExact(e.X));
				}
				else
				{
					// Щелчок без протяжки в режиме зума показывает анимацию
					// целиком: обратное действие должно быть таким же
					// доступным, как само приближение.
					ResetView();
				}

				a_zoom_but.Checked = false;
				UpdateHoverState(e.X, e.Y);
				Invalidate();
				return;
		}

		if (_captured != null)
		{
			_captured.OnMouseUp(ToSk(e));
			_captured = null;
			ArrangeUi();
			SyncEditors();
			Invalidate();
		}
	}

	/// <summary>Выделение ключей рамкой. Shift добавляет к уже выделенным.</summary>
	private void SelectKeysInRect(SKRect rect)
	{
		if ((ModifierKeys & Keys.Shift) == 0)
			_selectedKeys.Clear();

		for (int i = 0; i < _rows.Count; i++)
		{
			var row = _rows[i];

			if (row.ParamIds.Length == 0)
				continue;

			float centerY = RowY(i) + RowHeight * 0.5f;

			if (centerY < rect.Top || centerY > rect.Bottom)
				continue;

			// Строки-группы тоже участвуют: рамка, накрывшая свёрнутую
			// группу, обязана взять ключи её параметров — иначе выделение
			// зависело бы от того, раскрыт узел или нет.
			var frames = CollectKeyFrames(row);

			foreach (int frame in frames)
			{
				float x = FrameX(frame);

				if (x < rect.Left || x > rect.Right)
					continue;

				foreach (var key in KeysAt(row, frame))
					_selectedKeys.Add(key);
			}
		}
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);

		// Двойной щелчок по полосе — привычный способ свернуть панель.
		if (IsOverGrip(e.Y))
		{
			SetExpanded(!Expanded);
			return;
		}

		if (IsInToolbar(e.Y))
		{
			var ev = ToSk(e);
			var target = _root.FindTopMost(ev.Location);

			if (target != null && target.OnMouseDoubleClick(ev))
				Invalidate();

			return;
		}

		if (!HasContentArea)
			return;

		// Двойной щелчок по линейке возвращает показ всей анимации.
		if (IsInRuler(e.X, e.Y))
		{
			ResetView();
			Invalidate();
			return;
		}

		if (!IsInKeyArea(e.X, e.Y))
			return;

		// Двойной щелчок по свободному месту строки параметра пишет ключ
		// на этом кадре из текущего значения фигуры.
		int index = RowIndexAt(e.Y);

		if (index < 0)
			return;

		var row = _rows[index];
		var param = row.Param;

		if (param == null)
			return;

		SetKeyForParam(row, param, FrameFromX(e.X));
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);

		var point = new SKPoint(e.X, e.Y);
		var ev = new SkMouseEvent { Location = point, Delta = e.Delta };

		if (IsInToolbar(e.Y))
		{
			var target = _root.FindTopMost(point);

			if (target != null && target.OnMouseWheel(ev))
			{
				SyncEditors();
				Invalidate();
				return;
			}
		}

		// Колесо над ключами меняет масштаб показа кадров, над деревом —
		// прокручивает строки. Ctrl всюду означает прокрутку: приближать
		// вслепую, не видя нужной строки, бессмысленно.
		bool overTimeline = HasContentArea && e.X > TimelineLeft && e.Y >= ContentTop;

		if (overTimeline && (ModifierKeys & Keys.Control) == 0)
		{
			float factor = e.Delta > 0 ? 1f / 1.2f : 1.2f;
			ZoomView(FrameFromXExact(e.X), factor);
			return;
		}

		_scrollY -= Math.Sign(e.Delta) * RowHeight * 2f;
		ClampScroll();
		Invalidate();
	}

	private void RouteMouseDown(MouseEventArgs e)
	{
		var ev = ToSk(e);

		bool closed = _root.CloseDropDownsOutside(ev.Location);
		var target = _root.FindTopMostOverlay(ev.Location) ?? _root.FindTopMost(ev.Location);

		SetFocused(target);
		_captured = target;
		_captured?.OnMouseDown(ev);

		ArrangeUi();

		if (closed || target != null)
			Invalidate();
	}

	private void SetFocused(SkControl? control)
	{
		if (!ReferenceEquals(_focused, control) && _focused is SkNumericUpDown nud)
			nud.CommitEditing();

		ClearFocus(_root);
		_focused = control;

		if (_focused != null)
			_focused.Focused = true;
	}

	private static void ClearFocus(SkControl c)
	{
		c.Focused = false;

		foreach (var child in c.Children)
			ClearFocus(child);
	}

	private static SkMouseEvent ToSk(MouseEventArgs e) => new()
	{
		Location = new SKPoint(e.X, e.Y),
		Button = e.Button,
		Delta = e.Delta,
		Ctrl = (ModifierKeys & Keys.Control) != 0,
		Shift = (ModifierKeys & Keys.Shift) != 0,
		Alt = (ModifierKeys & Keys.Alt) != 0
	};

	protected override bool IsInputKey(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;

		return key is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Delete or Keys.Space or Keys.Enter
			? true
			: base.IsInputKey(keyData);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		// Поле ввода забирает клавиши себе: иначе цифра в числе кадров
		// перескакивала бы по кадрам.
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
				// Enter завершает ввод: значение уже принято полем, и держать
				// на нём фокус с кареткой больше не за чем.
				if (e.KeyCode is Keys.Enter or Keys.Escape)
					SetFocused(null);

				e.Handled = true;
				e.SuppressKeyPress = true;
				SyncEditors();
				Invalidate();
				return;
			}
		}

		switch (e.KeyCode)
		{
			case Keys.Space:
				TogglePlay();
				break;

			case Keys.Left:
				StepFrame(-1);
				break;

			case Keys.Right:
				StepFrame(+1);
				break;

			case Keys.Home:
				SetCurrentFrame(PageAnimation.MinFrameCount);
				break;

			case Keys.End:
				SetCurrentFrame(FrameCount);
				break;

			case Keys.Delete:
				DeleteSelectedKeys();
				break;

			default:
				return;
		}

		e.Handled = true;
		e.SuppressKeyPress = true;
	}

	protected override void OnKeyPress(KeyPressEventArgs e)
	{
		base.OnKeyPress(e);

		if (_focused != null && _focused.OnKeyChar(e.KeyChar))
			Invalidate();
	}

	#endregion
}
