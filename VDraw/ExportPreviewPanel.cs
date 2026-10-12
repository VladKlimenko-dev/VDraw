using System.ComponentModel;

namespace VDraw.Editing;

/// <summary>
/// Холст предпросмотра.
///
/// Вынесен в отдельный файл и объявлен public намеренно: конструктор форм
/// размещает этот элемент на поверхности и должен уметь его создать.
/// Внутри файла другой формы он дизайнеру не виден, а internal-тип
/// он размещать отказывается.
///
/// ToolboxItem(false) убирает его из панели элементов: класс служебный,
/// и предлагать его при обычной вёрстке незачем.
///
/// Отдельный элемент, а не PictureBox, из-за шахматной подложки: она должна
/// лежать РОВНО под изображением, а не по всей площади рамки. У PictureBox
/// в режиме Zoom картинка занимает часть площади, и клетка, нарисованная
/// на фоне всего элемента, показывала бы прозрачными в том числе поля
/// вокруг изображения — то есть врала бы о содержимом файла.
/// </summary>
[System.ComponentModel.ToolboxItem(false)]
public sealed class ExportPreviewPanel : Panel
{
	private const int CellSize = 8;

	private const float MinZoom = 0.05f;
	private const float MaxZoom = 40f;

	private static readonly Color CellLight = Color.FromArgb(255, 255, 255);
	private static readonly Color CellDark = Color.FromArgb(214, 214, 214);

	public Image? PreviewImage { get; private set; }

	/// <summary>
	/// Размер экспортируемого изображения в пикселях.
	///
	/// Раскладка считается по нему, а НЕ по размеру растра предпросмотра.
	/// Растр пересобирается в разном разрешении вслед за масштабом, и если
	/// вписывать по нему, получается обратная связь: масштаб меняет
	/// разрешение, разрешение меняет вписанный размер, вписанный размер
	/// сбивает вычисленный сдвиг — изображение при каждой прокрутке
	/// прыгало к курсору.
	/// </summary>
	public Size ContentSize { get; private set; } = new Size(1, 1);

	/// <summary>Задаёт растр и логический размер одной операцией: врозь они рассогласуются.</summary>
	public void SetContent(Image? image, int contentWidth, int contentHeight)
	{
		PreviewImage = image;

		ContentSize = new Size(
			Math.Max(1, contentWidth),
			Math.Max(1, contentHeight));

		Invalidate();
	}

	/// <summary>Рисовать ли шахматную подложку под изображением.</summary>
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public bool ShowCheckerboard { get; set; }

	/// <summary>
	/// Кратность к вписанному размеру. Единица — изображение вписано
	/// в панель целиком.
	/// </summary>
	public float Zoom { get; private set; } = 1f;

	/// <summary>Сдвиг изображения относительно центра панели, в пикселях экрана.</summary>
	private PointF _offset;

	private bool _panning;
	private Point _panStart;
	private PointF _panOrigin;

	/// <summary>
	/// Поднимается после изменения масштаба. Форма пересобирает по нему
	/// растр превью: при увеличении картинка, отрисованная под размер
	/// панели, расплылась бы, и разглядеть детали — ради чего масштаб
	/// и нужен — не вышло бы.
	/// </summary>
	public event Action? ZoomChanged;

	public ExportPreviewPanel()
	{
		DoubleBuffered = true;
		BorderStyle = BorderStyle.FixedSingle;
		BackColor = Color.FromArgb(120, 120, 120);

		// Без этого колесо достаётся форме, а не панели.
		SetStyle(ControlStyles.Selectable, true);
		TabStop = false;
	}

	/// <summary>Возвращает изображение к вписанному положению.</summary>
	public void ResetView()
	{
		bool changed = MathF.Abs(Zoom - 1f) > 0.0001f;

		Zoom = 1f;
		_offset = PointF.Empty;

		Invalidate();

		if (changed)
			ZoomChanged?.Invoke();
	}

	#region Навигация

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);

		if (e.Button != MouseButtons.Left || PreviewImage == null)
			return;

		_panning = true;
		_panStart = e.Location;
		_panOrigin = _offset;

		Cursor = Cursors.SizeAll;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		if (!_panning)
			return;

		_offset = new PointF(
			_panOrigin.X + (e.X - _panStart.X),
			_panOrigin.Y + (e.Y - _panStart.Y));

		Invalidate();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		if (e.Button != MouseButtons.Left)
			return;

		_panning = false;
		Cursor = Cursors.Default;
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);

		ResetView();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);

		if (PreviewImage == null)
			return;

		float factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;

		float target = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);

		if (MathF.Abs(target - Zoom) < 0.0001f)
			return;

		// Масштабирование у курсора: точка изображения под указателем
		// должна остаться на месте. Иначе при увеличении интересующая
		// деталь уезжает за край и её приходится догонять перетаскиванием.
		var rect = GetImageRect();

		if (rect.Width <= 0 || rect.Height <= 0)
		{
			Zoom = target;
			Invalidate();
			ZoomChanged?.Invoke();
			return;
		}

		// Приведение к float обязательно. Rectangle хранит целые числа,
		// и без него деление было бы целочисленным: доля обращалась бы
		// в ноль при любом положении курсора, кроме правого края, а нулевая
		// доля означает левый верхний угол изображения — картинка на каждой
		// прокрутке перескакивала бы этим углом под курсор.
		float ax = (e.X - rect.X) / (float)rect.Width;
		float ay = (e.Y - rect.Y) / (float)rect.Height;

		Zoom = target;

		var after = GetImageRect();

		_offset = new PointF(
			_offset.X + (e.X - (after.X + ax * after.Width)),
			_offset.Y + (e.Y - (after.Y + ay * after.Height)));

		Invalidate();

		ZoomChanged?.Invoke();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);

		Invalidate();
	}

	#endregion

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		if (PreviewImage == null)
			return;

		var rect = GetImageRect();

		if (rect.Width <= 0 || rect.Height <= 0)
			return;

		if (ShowCheckerboard)
			DrawCheckerboard(e.Graphics, rect);

		// При сильном увеличении сглаживание превращает пиксели в кашу,
		// а показать надо именно их. Порог — двукратное превышение
		// исходного размера растра превью.
		bool magnified = rect.Width > PreviewImage.Width * 2;


		e.Graphics.InterpolationMode = magnified
			? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
			: System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

		e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

		e.Graphics.DrawImage(PreviewImage, rect);
	}

	/// <summary>
	/// Прямоугольник изображения: вписан в панель с сохранением пропорций,
	/// затем увеличен на Zoom и сдвинут на _offset.
	/// </summary>
	private Rectangle GetImageRect()
	{
		if (PreviewImage == null)
			return Rectangle.Empty;

		float fit = Math.Min(
			ClientSize.Width / (float)ContentSize.Width,
			ClientSize.Height / (float)ContentSize.Height);

		fit = Math.Min(fit, 1f);

		float k = fit * Zoom;

		int w = Math.Max(1, (int)MathF.Round(ContentSize.Width * k));
		int h = Math.Max(1, (int)MathF.Round(ContentSize.Height * k));

		int x = (int)MathF.Round((ClientSize.Width - w) * 0.5f + _offset.X);
		int y = (int)MathF.Round((ClientSize.Height - h) * 0.5f + _offset.Y);

		return new Rectangle(x, y, w, h);
	}

	private static void DrawCheckerboard(Graphics g, Rectangle rect)
	{
		using var light = new SolidBrush(CellLight);
		using var dark = new SolidBrush(CellDark);

		var clip = g.Clip;

		// Клетка обрезается прямоугольником изображения: при сдвиге
		// панорамированием она не должна выползать на серое поле вокруг.
		g.SetClip(rect);

		g.FillRectangle(light, rect);

		for (int y = 0; y * CellSize < rect.Height; y++)
		{
			for (int x = 0; x * CellSize < rect.Width; x++)
			{
				if (((x + y) & 1) == 0)
					continue;

				g.FillRectangle(
					dark,
					rect.X + x * CellSize,
					rect.Y + y * CellSize,
					CellSize,
					CellSize);
			}
		}

		g.Clip = clip;
	}
}

