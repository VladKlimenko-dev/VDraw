using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui.Avalon;

namespace VDraw.Ui;

/// <summary>
/// WinForms-обёртка панели свойств Avalonia.
///
/// Это единственное место, где два слоя интерфейса встречаются. Снаружи
/// контрол выглядит как прежний SkPropertyPanelControl: тот же Dock = Right,
/// тот же набор событий и метод RebuildForSelection, — поэтому MainForm
/// меняется в одной строке. Внутри — хост Avalonia с AvPropertyPanel.
///
/// Полоса изменения ширины осталась своей, на WinForms: она разделяет
/// WinForms-холст и Avalonia-панель, то есть не принадлежит ни одному
/// из слоёв, и тащить её внутрь Avalonia значило бы тянуть мышь через
/// границу двух систем ввода.
///
/// Если Avalonia не запустилась (см. AvBootstrap.FailureReason), вместо
/// панели показывается надпись с причиной. Молча пустая полоса справа
/// заставила бы искать поломку в выделении фигур.
/// </summary>
public sealed class AvPropertyPanelHost : Panel
{
	private readonly AvPropertyPanel? _panel;
	private readonly Avalonia.Win32.Interoperability.WinFormsAvaloniaControlHost? _host;
	private readonly Label? _failure;

	private readonly Panel _grip;

	private bool _resizing;
	private int _resizeStartWidth;
	private int _resizeStartScreenX;

	/// <summary>Ширина полосы разделителя в пикселях.</summary>
	private const int GripWidth = 6;

	public int MinPanelWidth { get; set; } = 240;
	public int MaxPanelWidth { get; set; } = 560;

	public event Action? RequestCanvasRefresh;
	public event Action<ShapePickTarget>? ShapePickRequested;

	/// <summary>Идёт ли ввод в одном из полей панели. См. MainForm.IsTextEntryFocused.</summary>
	public bool IsTextEditing => _panel?.IsTextEditing == true;

	public AvPropertyPanelHost()
	{
		Dock = DockStyle.Right;
		Width = 300;
		Padding = System.Windows.Forms.Padding.Empty;
		BackColor = ToColor(SkUiTheme.Current.PanelBack);

		_grip = new Panel
		{
			Dock = DockStyle.Left,
			Width = GripWidth,
			Cursor = Cursors.SizeWE,
			BackColor = ToColor(SkUiTheme.Current.Border)
		};

		_grip.MouseDown += OnGripMouseDown;
		_grip.MouseMove += OnGripMouseMove;
		_grip.MouseUp += OnGripMouseUp;

		Controls.Add(_grip);

		AvBootstrap.EnsureStarted();

		if (AvBootstrap.IsStarted)
		{
			_panel = new AvPropertyPanel();

			_panel.RequestCanvasRefresh += () => RequestCanvasRefresh?.Invoke();
			_panel.ShapePickRequested += target => ShapePickRequested?.Invoke(target);

			_host = new Avalonia.Win32.Interoperability.WinFormsAvaloniaControlHost
			{
				Dock = DockStyle.Fill,
				Content = _panel
			};

			Controls.Add(_host);
			_host.BringToFront();
		}
		else
		{
			_failure = new Label
			{
				Dock = DockStyle.Fill,
				Padding = new System.Windows.Forms.Padding(10),
				ForeColor = ToColor(SkUiTheme.Current.Text),
				Text = "Панель свойств не запустилась."
					+ Environment.NewLine + Environment.NewLine
					+ AvBootstrap.FailureReason
			};

			Controls.Add(_failure);
		}

		SkUiTheme.ThemeChanged += OnThemeChanged;
	}

	protected override void OnCreateControl()
	{
		base.OnCreateControl();

		// Подписки панели на выделение ставятся тогда, когда контрол
		// действительно попал в окно: до этого пересобирать нечего.
		_panel?.Attach();
	}

	/// <summary>
	/// Пересобрать панель под текущее выделение.
	/// Поведение то же, что у SkPropertyPanelControl.RebuildForSelection.
	/// </summary>
	public void RebuildForSelection()
	{
		_panel?.RebuildForSelection();
	}

	private void OnThemeChanged()
	{
		BackColor = ToColor(SkUiTheme.Current.PanelBack);
		_grip.BackColor = ToColor(SkUiTheme.Current.Border);

		if (_failure != null)
			_failure.ForeColor = ToColor(SkUiTheme.Current.Text);
	}

	#region Изменение ширины

	private void OnGripMouseDown(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
			return;

		_resizing = true;
		_resizeStartWidth = Width;
		_resizeStartScreenX = Cursor.Position.X;
	}

	private void OnGripMouseMove(object? sender, MouseEventArgs e)
	{
		if (!_resizing)
			return;

		// Отсчёт от экранной координаты, а не от e.X: ползунок во время
		// перетаскивания сам сдвигается вместе с границей панели, и
		// локальная координата считала бы смещение дважды.
		int delta = _resizeStartScreenX - Cursor.Position.X;

		Width = Math.Clamp(_resizeStartWidth + delta, MinPanelWidth, MaxPanelWidth);
	}

	private void OnGripMouseUp(object? sender, MouseEventArgs e)
	{
		_resizing = false;
	}

	#endregion

	private static System.Drawing.Color ToColor(SkiaSharp.SKColor c)
	{
		return System.Drawing.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			SkUiTheme.ThemeChanged -= OnThemeChanged;
			_panel?.Detach();
		}

		base.Dispose(disposing);
	}
}
