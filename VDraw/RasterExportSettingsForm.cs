using SkiaSharp;

using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Настройка растрового экспорта: размер в пикселях, поля, прозрачность,
/// цвет подложки — с предпросмотром результата.
///
/// Предпросмотр рисуется тем же кодом, что и сам экспорт, только в мелком
/// разрешении и с пропорционально уменьшенными полями. Иначе он показывал
/// бы не то, что окажется в файле, и терял бы весь смысл: расхождение
/// обнаруживалось бы уже после сохранения.
///
/// Расположение элементов вынесено в RasterExportSettingsForm.Designer.cs
/// и правится конструктором форм. Здесь остаются только связи и поведение.
/// </summary>
public sealed partial class RasterExportSettingsForm : Form
{
	/// <summary>
	/// Предел стороны растра предпросмотра.
	///
	/// При сильном увеличении растр пересобирается крупнее, чтобы картинка
	/// оставалась резкой. Без предела десятикратный масштаб на большом окне
	/// потребовал бы растр в десятки тысяч точек — то есть ровно ту
	/// отрисовку, которой предпросмотр и должен избегать.
	/// </summary>
	private const int MaxPreviewSide = 3000;

	private readonly Page? _page;
	private readonly IReadOnlyList<Shape> _shapes = Array.Empty<Shape>();

	/// <summary>Соотношение сторон исходных габаритов. Основа для связки размеров.</summary>
	private readonly float _sourceAspect = 1f;

	private SKColor _background = SKColors.White;

	/// <summary>Защита от взаимного пересчёта ширины и высоты.</summary>
	private bool _syncing;

	private Bitmap? _previewImage;

	public RasterExportSettings Settings { get; } = new();

	/// <summary>
	/// Конструктор без параметров — ТОЛЬКО для конструктора форм.
	///
	/// Visual Studio создаёт экземпляр формы, чтобы показать её на холсте,
	/// и умеет вызывать лишь конструктор без аргументов. Без него вместо
	/// разметки в дизайнере появится сообщение об ошибке. В работе программы
	/// этот конструктор не используется.
	/// </summary>
	public RasterExportSettingsForm()
	{
		InitializeComponent();
		WinFormsTheme.Register(this);
	}

	public RasterExportSettingsForm(
		Page page,
		IReadOnlyList<Shape> shapes,
		ShapeExportFormat format,
		int defaultWidth,
		int defaultHeight)
	{
		InitializeComponent();

		_page = page;
		_shapes = shapes;

		_sourceAspect = defaultHeight > 0 ? defaultWidth / (float)defaultHeight : 1f;

		// Английские подписи расставил дизайнер — здесь они переводятся тем
		// же механизмом, что и в главном окне. Своих вызовов L.T для подписей
		// в коде формы больше нет: два источника одной надписи неизбежно
		// разошлись бы при первой же правке в дизайнере.
		Localization.Register(this);

		widthBox.Value = Math.Clamp(defaultWidth, (int)widthBox.Minimum, (int)widthBox.Maximum);
		heightBox.Value = Math.Clamp(defaultHeight, (int)heightBox.Minimum, (int)heightBox.Maximum);

		// JPEG и BMP альфу не хранят: там флажок не выключен, а отсутствует
		// по существу, и показывать его включаемым значило бы обещать
		// невозможное.
		bool supportsAlpha = format == ShapeExportFormat.Png;

		transparent.Enabled = supportsAlpha;
		transparent.Checked = supportsAlpha;

		HookEvents();

		UpdateBackgroundState();
		UpdateInfo();
	}

	/// <summary>
	/// Подписки собраны здесь, а не расставлены дизайнером.
	///
	/// Дизайнер пишет их внутрь InitializeComponent, и тогда обработчики
	/// начинают срабатывать раньше, чем форма получила страницу и фигуры:
	/// присваивание начальных значений в счётчики подняло бы ValueChanged
	/// и полезло бы строить предпросмотр по пустым полям.
	/// </summary>
	private void HookEvents()
	{
		// Пересборка откладывается таймером. Ввод в счётчик поднимает
		// событие на каждую цифру и на каждый щелчок по стрелке, а полная
		// отрисовка сцены на каждое из них превратила бы набор числа
		// в череду подвисаний.
		previewTimer.Tick += (_, _) => { previewTimer.Stop(); RebuildPreview(); };

		widthBox.ValueChanged += OnWidthChanged;
		heightBox.ValueChanged += OnHeightChanged;

		foreach (var pad in new[] { padLeft, padTop, padRight, padBottom })
			pad.ValueChanged += (_, _) => { UpdateInfo(); SchedulePreview(); };

		transparent.CheckedChanged += (_, _) => { UpdateBackgroundState(); SchedulePreview(); };
		backgroundButton.Click += OnPickBackground;

		// Размер панели задаёт разрешение растра превью, масштаб — его
		// кратность. Изменилось любое из двух — пересобираем, иначе
		// картинка останется в прежнем разрешении и расплывётся.
		preview.Resize += (_, _) => SchedulePreview();
		preview.ZoomChanged += SchedulePreview;

		okButton.Click += (_, _) => Apply();
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);

		RebuildPreview();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		previewTimer.Stop();

		preview.SetContent(null, 1, 1);

		_previewImage?.Dispose();
		_previewImage = null;

		base.OnFormClosed(e);
	}

	#region Размеры

	private void OnWidthChanged(object? sender, EventArgs e)
	{
		if (!_syncing && keepAspect.Checked)
		{
			_syncing = true;

			try
			{
				int h = (int)MathF.Round((float)widthBox.Value / MathF.Max(_sourceAspect, 0.0001f));
				heightBox.Value = Math.Clamp(h, (int)heightBox.Minimum, (int)heightBox.Maximum);
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
		if (!_syncing && keepAspect.Checked)
		{
			_syncing = true;

			try
			{
				int w = (int)MathF.Round((float)heightBox.Value * _sourceAspect);
				widthBox.Value = Math.Clamp(w, (int)widthBox.Minimum, (int)widthBox.Maximum);
			}
			finally
			{
				_syncing = false;
			}
		}

		UpdateInfo();
		SchedulePreview();
	}

	/// <summary>
	/// Подсказка о последствиях выбора. Размер файла не считается — он
	/// зависит от содержимого и степени сжатия, и любая оценка тут была бы
	/// выдумкой.
	/// </summary>
	private void UpdateInfo()
	{
		long pixels = (long)widthBox.Value * (long)heightBox.Value;

		int innerW = (int)widthBox.Value - (int)padLeft.Value - (int)padRight.Value;
		int innerH = (int)heightBox.Value - (int)padTop.Value - (int)padBottom.Value;

		string text = L.T("Pixels: {0:N0}", pixels);

		if (innerW < 1 || innerH < 1)
		{
			text += Environment.NewLine + L.T("Padding leaves no room for content.");
		}
		else
		{
			text += Environment.NewLine + L.T("Content: {0} x {1}", innerW, innerH);

			float aspect = innerW / MathF.Max(innerH, 1f);

			if (MathF.Abs(aspect - _sourceAspect) > 0.01f)
				text += Environment.NewLine + L.T("Proportions differ from the original.");
		}

		info.Text = text;
	}

	private SKRectI GetPadding() => new(
		(int)padLeft.Value,
		(int)padTop.Value,
		(int)padRight.Value,
		(int)padBottom.Value);

	#endregion

	#region Фон

	private void UpdateBackgroundState()
	{
		bool useColor = !transparent.Checked || !transparent.Enabled;

		backgroundButton.Enabled = useColor;
		backgroundSwatch.Visible = useColor;

		backgroundSwatch.BackColor = Color.FromArgb(
			255, _background.Red, _background.Green, _background.Blue);

		preview.ShowCheckerboard = !useColor;
		preview.Invalidate();
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
		previewTimer.Stop();
		previewTimer.Start();
	}

	/// <summary>
	/// Перерисовывает предпросмотр.
	///
	/// Рисуется то же самое изображение, что уйдёт в файл, только уменьшенное
	/// в целое число раз по обеим осям сразу. Поля пересчитываются тем же
	/// множителем — иначе при мелком превью они выглядели бы непропорционально
	/// широкими и вводили бы в заблуждение.
	///
	/// Экспортный размер напрямую не берётся: он может доходить до двадцати
	/// тысяч пикселей по стороне, и отрисовка такого растра ради картинки
	/// в триста точек — это секунды ожидания на каждое нажатие стрелки.
	/// </summary>
	private void RebuildPreview()
	{
		// У формы, созданной конструктором форм, страницы нет: рисовать
		// нечего, а обращение к ней уронило бы дизайнер.
		if (_page == null)
			return;

		try
		{
			int width = (int)widthBox.Value;
			int height = (int)heightBox.Value;

			float fit = Math.Min(
				preview.ClientSize.Width / (float)width,
				preview.ClientSize.Height / (float)height);

			float k = Math.Min(fit, 1f) * MathF.Max(preview.Zoom, 1f);

			// Крупнее оригинала растр не нужен: в файле больше пикселей
			// всё равно не будет, и увеличение сверх этого показывало бы
			// сглаженную выдумку вместо настоящих пикселей.
			k = Math.Min(k, 1f);

			int previewWidth = Math.Max(1, (int)MathF.Round(width * k));
			int previewHeight = Math.Max(1, (int)MathF.Round(height * k));

			if (previewWidth > MaxPreviewSide || previewHeight > MaxPreviewSide)
			{
				float limit = Math.Min(
					MaxPreviewSide / (float)previewWidth,
					MaxPreviewSide / (float)previewHeight);

				k *= limit;

				previewWidth = Math.Max(1, (int)MathF.Round(previewWidth * limit));
				previewHeight = Math.Max(1, (int)MathF.Round(previewHeight * limit));
			}

			var pad = GetPadding();

			var scaledPad = new SKRectI(
				(int)MathF.Round(pad.Left * k),
				(int)MathF.Round(pad.Top * k),
				(int)MathF.Round(pad.Right * k),
				(int)MathF.Round(pad.Bottom * k));

			bool isTransparent = transparent.Checked && transparent.Enabled;

			var background = isTransparent ? SKColors.Transparent : _background.WithAlpha(255);

			using var bitmap = ShapeExporter.RenderToBitmapExact(
				_page, _shapes, previewWidth, previewHeight, background, scaledPad);

			preview.SetContent(null, width, height);

			_previewImage?.Dispose();
			_previewImage = bitmap == null ? null : ToDrawingBitmap(bitmap);

			// Логический размер — тот, что уйдёт в файл, а не разрешение
			// растра: по нему панель вписывает и масштабирует.
			preview.SetContent(_previewImage, width, height);
		}
		catch
		{
			// Предпросмотр — удобство. Его неудача не должна мешать
			// настроить экспорт и сохранить файл.
			preview.SetContent(null, 1, 1);
		}

		preview.Invalidate();
	}

	/// <summary>
	/// Переводит растр Skia в System.Drawing через PNG.
	///
	/// Через кодирование, а не копированием пикселей: PNG сохраняет альфу,
	/// и прозрачный фон остаётся прозрачным, а не становится чёрным.
	/// </summary>
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

	private void Apply()
	{
		Settings.PixelWidth = (int)widthBox.Value;
		Settings.PixelHeight = (int)heightBox.Value;
		Settings.Padding = GetPadding();
		Settings.Transparent = transparent.Checked && transparent.Enabled;
		Settings.Background = _background;
	}
}

/// <summary>
/// Параметры растрового экспорта, выбранные пользователем.
/// </summary>
public sealed class RasterExportSettings
{
	public int PixelWidth { get; set; }
	public int PixelHeight { get; set; }

	/// <summary>
	/// Поля внутри изображения, в пикселях. Общий размер файла остаётся
	/// PixelWidth на PixelHeight, содержимое ужимается.
	/// </summary>
	public SKRectI Padding { get; set; }

	/// <summary>Прозрачный фон. Осмысленно только там, где формат хранит альфу.</summary>
	public bool Transparent { get; set; }

	/// <summary>Цвет подложки. Используется, когда прозрачность выключена.</summary>
	public SKColor Background { get; set; } = SKColors.White;

	/// <summary>Итоговый цвет фона с учётом флажка прозрачности.</summary>
	public SKColor EffectiveBackground =>
		Transparent ? SKColors.Transparent : Background.WithAlpha(255);
}
