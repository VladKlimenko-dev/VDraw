using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Экспорт анимации документа в GIF.
///
/// Предпросмотр показывает кадр так, как он попадёт в файл: тем же
/// кодом отрисовки, с тем же фоном и в тех же пропорциях. Перемотка
/// и воспроизведение меняют кадр НА САМОЙ СТРАНИЦЕ через
/// PageAnimation.ApplyFrame — иначе пришлось бы держать вторую,
/// параллельную реализацию анимации, которая неизбежно разошлась бы
/// с первой.
///
/// Исходный кадр документа запоминается при открытии и возвращается
/// при закрытии: пользователь просил экспортировать анимацию, а не
/// перематывать её.
/// </summary>
public partial class GifExportForm : Form
{
	private const int PreviewDelayMs = 200;
	private const int MaxPreviewSide = 2000;

	private readonly Page _page;
	private readonly IReadOnlyList<Shape> _shapes;

	/// <summary>
	/// Соотношение сторон снимаемой области. Меняется вместе с ней,
	/// поэтому не readonly: при переходе на область страницы пропорции
	/// становятся другими, и связка ширины с высотой обязана это учесть.
	/// </summary>
	private float _sourceAspect;

	private readonly int _restoreFrame;

	/// <summary>Габариты содержимого, вычисленные по всем кадрам анимации.</summary>
	private SKRect _contentBounds;

	private readonly System.Windows.Forms.Timer _previewTimer;

	private SKColor _background = SKColors.White;
	private SKRect _animationBounds;

	private bool _syncing;
	private bool _playing;

	private Bitmap? _previewImage;

	/// <summary>
	/// Конструктор без параметров — ТОЛЬКО для конструктора форм.
	///
	/// Дизайнер создаёт экземпляр designed-класса, чтобы выполнить
	/// InitializeComponent и показать поверхность. Без такого конструктора
	/// он сообщает, что не может создать тип, и форму открыть не удаётся.
	/// В работе приложения этот конструктор не используется: страницы
	/// у формы нет, и делать ей нечего.
	/// </summary>
	public GifExportForm()
	{
		InitializeComponent();

		_page = null!;
		_shapes = Array.Empty<Shape>();
		_previewTimer = new System.Windows.Forms.Timer();
		_sourceAspect = 1f;
		_restoreFrame = 1;
		WinFormsTheme.Register(this);
	}

	public GifExportForm(Page page, IReadOnlyList<Shape> shapes)
	{
		_page = page;
		_shapes = shapes;

		InitializeComponent();

		var anim = _page.Animation;

		_restoreFrame = anim.CurrentFrame;

		// Габариты считаются один раз по всей анимации: содержимое не должно
		// дёргаться в кадре вслед за меняющимися границами сцены.
		_contentBounds = AnimationFrameRenderer.GetAnimationBounds(
			_page, _shapes, 1, anim.FrameCount);

		_animationBounds = _contentBounds;

		_sourceAspect = _animationBounds.Height > 0f
			? _animationBounds.Width / _animationBounds.Height
			: 1f;

		ApplyTranslations();
		SetupRanges();
		SetupHandlers();

		_previewTimer = new System.Windows.Forms.Timer { Interval = PreviewDelayMs };
		_previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RebuildPreview(); };

		UpdateBackgroundState();
		UpdateAreaState();
		UpdateInfo();
	}

	#region Область страницы

	private void UpdateAreaState()
	{
		bool enabled = use_page_space_check.Checked;

		area_label.Enabled = enabled;
		area_left_label.Enabled = enabled;
		area_top_label.Enabled = enabled;
		area_right_label.Enabled = enabled;
		area_bottom_label.Enabled = enabled;

		area_left_nud.Enabled = enabled;
		area_top_nud.Enabled = enabled;
		area_right_nud.Enabled = enabled;
		area_bottom_nud.Enabled = enabled;
	}

	private void OnAreaChanged(object? sender, EventArgs e)
	{
		if (_syncing)
			return;

		_syncing = true;

		try
		{
			// Правая граница не может оказаться левее левой. Вместо запрета
			// подтягиваем вторую: так пользователю не приходится угадывать
			// порядок ввода, а перевёрнутый прямоугольник дал бы пустой кадр.
			if (sender == area_left_nud && area_left_nud.Value > area_right_nud.Value)
				area_right_nud.Value = area_left_nud.Value;

			if (sender == area_right_nud && area_right_nud.Value < area_left_nud.Value)
				area_left_nud.Value = area_right_nud.Value;

			if (sender == area_top_nud && area_top_nud.Value > area_bottom_nud.Value)
				area_bottom_nud.Value = area_top_nud.Value;

			if (sender == area_bottom_nud && area_bottom_nud.Value < area_top_nud.Value)
				area_top_nud.Value = area_bottom_nud.Value;
		}
		finally
		{
			_syncing = false;
		}

		ApplyBounds();
	}

	/// <summary>
	/// Выбирает габариты кадра и подстраивает под них пропорции.
	///
	/// Снимаемая область — это ОКНО, через которое видно сцену, а не рамка
	/// вокруг содержимого. В этом весь смысл режима: объект может влететь
	/// в кадр снаружи и улететь за его пределы, и обрезка по краю окна —
	/// правильное поведение, а не потеря.
	/// </summary>
	private void ApplyBounds()
	{
		if (use_page_space_check.Checked)
		{
			float left = (float)area_left_nud.Value;
			float top = (float)area_top_nud.Value;
			float right = (float)area_right_nud.Value;
			float bottom = (float)area_bottom_nud.Value;

			// Вырожденное окно рисовать нечем: оставляем прежние габариты,
			// пока пользователь не введёт осмысленные.
			if (right - left > 0.0001f && bottom - top > 0.0001f)
				_animationBounds = new SKRect(left, top, right, bottom);
		}
		else
		{
			_animationBounds = _contentBounds;
		}

		if (_animationBounds.Height > 0f)
			_sourceAspect = _animationBounds.Width / _animationBounds.Height;

		if (keep_aspect_check.Checked)
		{
			_syncing = true;

			try
			{
				int h = (int)MathF.Round((float)width_nud.Value / MathF.Max(_sourceAspect, 0.0001f));
				height_nud.Value = Math.Clamp(h, (int)height_nud.Minimum, (int)height_nud.Maximum);
			}
			finally
			{
				_syncing = false;
			}
		}

		UpdateInfo();
		SchedulePreview();
	}

	#endregion

	#region Настройка

	private void ApplyTranslations()
	{
		Text = L.T("Export animated GIF");

		width_label.Text = L.T("Width (px)");
		height_label.Text = L.T("Height (px)");
		keep_aspect_check.Text = L.T("Keep aspect ratio");
		transparent_check.Text = L.T("Transparent background");
		background_but.Text = L.T("Background...");
		fps_label.Text = L.T("Frames per second");
		range_label.Text = L.T("Exported range");
		first_frame_label.Text = L.T("First frame");
		last_frame_label.Text = L.T("Last frame");
		use_page_space_check.Text = L.T("Use page space for export");
		area_label.Text = L.T("Captured area, page units");
		area_left_label.Text = L.T("Left");
		area_top_label.Text = L.T("Top");
		area_right_label.Text = L.T("Right");
		area_bottom_label.Text = L.T("Bottom");

		hint_label.Text = L.T("Drag to pan, wheel to zoom, double-click to fit.");
		ok_but.Text = L.T("OK");
		cancel_but.Text = L.T("Cancel");
	}

	private void SetupRanges()
	{
		var anim = _page.Animation;

		int defaultWidth = Math.Clamp((int)MathF.Round(_animationBounds.Width), 1, 4000);
		int defaultHeight = Math.Clamp((int)MathF.Round(_animationBounds.Height), 1, 4000);

		// Пропорции важнее конкретного числа: если габариты сцены дают
		// нецелое отношение, высота подгоняется под ширину.
		defaultHeight = Math.Clamp(
			(int)MathF.Round(defaultWidth / MathF.Max(_sourceAspect, 0.0001f)), 1, 4000);

		_syncing = true;

		try
		{
			width_nud.Value = defaultWidth;
			height_nud.Value = defaultHeight;

			fps_nud.Value = Math.Clamp(anim.Fps, (int)fps_nud.Minimum, (int)fps_nud.Maximum);

			first_frame_nud.Maximum = anim.FrameCount;
			last_frame_nud.Maximum = anim.FrameCount;

			first_frame_nud.Value = 1;
			last_frame_nud.Value = anim.FrameCount;
		}
		finally
		{
			_syncing = false;
		}

		var keys = new HashSet<int>();

		for (int frame = 1; frame <= anim.FrameCount; frame++)
		{
			if (anim.HasAnyKeyAt(frame))
				keys.Add(frame);
		}

		// По умолчанию — весь лист. Именно он и есть «пространство страницы»,
		// а содержимое анимации может выходить за него в обе стороны.
		_syncing = true;

		try
		{
			area_left_nud.Value = 0M;
			area_top_nud.Value = 0M;
			area_right_nud.Value = (decimal)_page.size_uom.Width;
			area_bottom_nud.Value = (decimal)_page.size_uom.Height;
		}
		finally
		{
			_syncing = false;
		}

		timeline.SetAnimation(anim.FrameCount, keys);
		timeline.SetRange(1, anim.FrameCount);
		timeline.CurrentFrame = anim.CurrentFrame;
	}

	private void SetupHandlers()
	{
		width_nud.ValueChanged += OnWidthChanged;
		height_nud.ValueChanged += OnHeightChanged;

		transparent_check.CheckedChanged += (_, _) => { UpdateBackgroundState(); SchedulePreview(); };
		background_but.Click += OnPickBackground;

		first_frame_nud.ValueChanged += OnRangeChanged;
		last_frame_nud.ValueChanged += OnRangeChanged;
		fps_nud.ValueChanged += (_, _) => UpdateInfo();

		timeline.FrameChanged += GoToFrame;

		rewind_but.Click += (_, _) => GoToFrame((int)first_frame_nud.Value);
		forward_but.Click += (_, _) => GoToFrame((int)last_frame_nud.Value);
		step_back_but.Click += (_, _) => GoToFrame(timeline.CurrentFrame - 1);
		step_forward_but.Click += (_, _) => GoToFrame(timeline.CurrentFrame + 1);
		play_but.Click += (_, _) => TogglePlay();

		play_timer.Tick += OnPlayTick;

		use_page_space_check.CheckedChanged += (_, _) => { UpdateAreaState(); ApplyBounds(); };

		foreach (var nud in new[] { area_left_nud, area_top_nud, area_right_nud, area_bottom_nud })
			nud.ValueChanged += OnAreaChanged;

		preview_panel.Resize += (_, _) => SchedulePreview();
		preview_panel.ZoomChanged += SchedulePreview;
	}

	#endregion

	#region Жизненный цикл

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);

		GoToFrame(timeline.CurrentFrame);
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		StopPlay();

		_previewTimer.Stop();
		_previewTimer.Dispose();

		preview_panel.SetContent(null, 1, 1);

		_previewImage?.Dispose();
		_previewImage = null;

		// Документ возвращается на тот кадр, с которого пользователь начал.
		_page.Animation.ApplyFrame(_page, _restoreFrame);
		_page.Animation.CurrentFrame = _restoreFrame;

		base.OnFormClosed(e);
	}

	#endregion

	#region Воспроизведение

	private void TogglePlay()
	{
		if (_playing)
			StopPlay();
		else
			StartPlay();
	}

	private void StartPlay()
	{
		int fps = Math.Clamp((int)fps_nud.Value, 1, 50);

		play_timer.Interval = Math.Max(20, 1000 / fps);
		play_timer.Start();

		_playing = true;
		play_but.Text = L.T("Stop");
	}

	private void StopPlay()
	{
		play_timer.Stop();

		_playing = false;
		play_but.Text = L.T("Play");
	}

	private void OnPlayTick(object? sender, EventArgs e)
	{
		int first = (int)first_frame_nud.Value;
		int last = (int)last_frame_nud.Value;

		int next = timeline.CurrentFrame + 1;

		// Проигрывается только экспортируемый диапазон и по кругу: смысл
		// предпросмотра в том, чтобы увидеть будущий файл, а он зациклен.
		if (next > last || next < first)
			next = first;

		GoToFrame(next);
	}

	private void GoToFrame(int frame)
	{
		var anim = _page.Animation;

		int clamped = Math.Clamp(frame, 1, anim.FrameCount);

		anim.CurrentFrame = clamped;
		anim.ApplyFrame(_page, clamped);

		timeline.CurrentFrame = clamped;

		frame_label.Text = L.T("Frame {0} / {1}", clamped, anim.FrameCount);

		// При воспроизведении откладывать нечего: кадры и так идут
		// по таймеру, а задержка добавила бы к ним вторую и сбила ритм.
		if (_playing)
			RebuildPreview();
		else
			SchedulePreview();
	}

	#endregion

	#region Размеры и диапазон

	private void OnWidthChanged(object? sender, EventArgs e)
	{
		if (!_syncing && keep_aspect_check.Checked)
		{
			_syncing = true;

			try
			{
				int h = (int)MathF.Round((float)width_nud.Value / MathF.Max(_sourceAspect, 0.0001f));
				height_nud.Value = Math.Clamp(h, (int)height_nud.Minimum, (int)height_nud.Maximum);
			}
			finally
			{
				_syncing = false;
			}
		}

		UpdateInfo();
		SchedulePreview();
	}

	private void OnHeightChanged(object? sender, EventArgs e)
	{
		if (!_syncing && keep_aspect_check.Checked)
		{
			_syncing = true;

			try
			{
				int w = (int)MathF.Round((float)height_nud.Value * _sourceAspect);
				width_nud.Value = Math.Clamp(w, (int)width_nud.Minimum, (int)width_nud.Maximum);
			}
			finally
			{
				_syncing = false;
			}
		}

		UpdateInfo();
		SchedulePreview();
	}

	private void OnRangeChanged(object? sender, EventArgs e)
	{
		if (_syncing)
			return;

		_syncing = true;

		try
		{
			// Конец не может оказаться раньше начала: вместо запрета
			// подтягиваем второе поле — так пользователю не приходится
			// угадывать порядок ввода.
			if (sender == first_frame_nud && first_frame_nud.Value > last_frame_nud.Value)
				last_frame_nud.Value = first_frame_nud.Value;

			if (sender == last_frame_nud && last_frame_nud.Value < first_frame_nud.Value)
				first_frame_nud.Value = last_frame_nud.Value;
		}
		finally
		{
			_syncing = false;
		}

		timeline.SetRange((int)first_frame_nud.Value, (int)last_frame_nud.Value);

		UpdateInfo();
	}

	private void UpdateInfo()
	{
		int first = (int)first_frame_nud.Value;
		int last = (int)last_frame_nud.Value;
		int fps = Math.Max(1, (int)fps_nud.Value);

		int count = Math.Max(0, last - first + 1);

		float seconds = count / (float)fps;

		string area = use_page_space_check.Checked
			? L.T("Area: {0:0.##} x {1:0.##} page units", _animationBounds.Width, _animationBounds.Height)
			: L.T("Area: content bounds across all frames");

		info_label.Text =
			L.T("Frames: {0}", count) + Environment.NewLine +
			L.T("Duration: {0:0.0} s", seconds) + Environment.NewLine +
			area + Environment.NewLine +
			L.T("GIF stores at most 256 colours per frame.");
	}

	#endregion

	#region Фон

	private void UpdateBackgroundState()
	{
		bool useColor = !transparent_check.Checked;

		background_but.Enabled = useColor;
		background_swatch.Visible = useColor;

		background_swatch.BackColor = Color.FromArgb(
			255, _background.Red, _background.Green, _background.Blue);

		preview_panel.ShowCheckerboard = !useColor;
		preview_panel.Invalidate();
	}

	private void OnPickBackground(object? sender, EventArgs e)
	{
		using var dialog = new SColorDialog();

		dialog.SetSKColor(_background);

		if (dialog.ShowDialog(this) != DialogResult.OK)
			return;

		_background = dialog.GetSKColor();

		UpdateBackgroundState();
		RebuildPreview();
	}

	#endregion

	#region Предпросмотр

	private void SchedulePreview()
	{
		_previewTimer.Stop();
		_previewTimer.Start();
	}

	private void RebuildPreview()
	{
		try
		{
			int width = (int)width_nud.Value;
			int height = (int)height_nud.Value;

			float fit = Math.Min(
				preview_panel.ClientSize.Width / (float)width,
				preview_panel.ClientSize.Height / (float)height);

			float k = Math.Min(Math.Min(fit, 1f) * MathF.Max(preview_panel.Zoom, 1f), 1f);

			int previewWidth = Math.Max(1, (int)MathF.Round(width * k));
			int previewHeight = Math.Max(1, (int)MathF.Round(height * k));

			if (previewWidth > MaxPreviewSide || previewHeight > MaxPreviewSide)
			{
				float limit = Math.Min(
					MaxPreviewSide / (float)previewWidth,
					MaxPreviewSide / (float)previewHeight);

				previewWidth = Math.Max(1, (int)(previewWidth * limit));
				previewHeight = Math.Max(1, (int)(previewHeight * limit));
			}

			var background = transparent_check.Checked
				? SKColors.Transparent
				: _background.WithAlpha(255);

			using var bitmap = ShapeExporter.RenderToBitmapWithBounds(
				_page, _shapes, _animationBounds, previewWidth, previewHeight, background);

			preview_panel.SetContent(null, width, height);

			_previewImage?.Dispose();
			_previewImage = bitmap == null ? null : ToDrawingBitmap(bitmap);

			preview_panel.SetContent(_previewImage, width, height);
		}
		catch
		{
			preview_panel.SetContent(null, 1, 1);
		}

		preview_panel.Invalidate();
	}

	private static Bitmap? ToDrawingBitmap(SKBitmap bitmap)
	{
		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);

		if (data == null)
			return null;

		using var stream = new MemoryStream(data.ToArray());

		return new Bitmap(stream);
	}

	#endregion

	#region Результат

	public int PixelWidth => (int)width_nud.Value;
	public int PixelHeight => (int)height_nud.Value;
	public int FirstFrame => (int)first_frame_nud.Value;
	public int LastFrame => (int)last_frame_nud.Value;
	public int Fps => (int)fps_nud.Value;
	public bool Transparent => transparent_check.Checked;

	public SKColor Background =>
		transparent_check.Checked ? SKColors.Transparent : _background.WithAlpha(255);

	/// <summary>
	/// Габариты снимаемой области. Отдаются наружу, чтобы запись файла шла
	/// по тем же границам, что показывал предпросмотр.
	/// </summary>
	public SKRect ExportBounds => _animationBounds;

	#endregion
}
