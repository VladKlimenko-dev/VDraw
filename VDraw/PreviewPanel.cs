using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.ComponentModel;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

/// <summary>
/// Область предпросмотра листа для PrintForm.
///
/// Рисует три вещи: белый лист принтера, страницу документа на нём и, по
/// желанию, границу области печати пунктиром. Масштаб задаётся снаружи,
/// размер контрола подгоняется под лист — прокрутку берёт на себя
/// родительская панель с AutoScroll.
///
/// Растр страницы кэшируется: перерисовка при прокрутке и изменении зума
/// не должна каждый раз гонять всю отрисовку фигур заново.
/// </summary>
public sealed class PreviewPanel : Control
{
	#region Константы

	/// <summary>Разрешение растра предпросмотра. Выше экранного ради зума.</summary>
	private const int PreviewDpi = 150;

	#endregion

	#region Поля

	private Page? _page;
	private RectangleF _paperMm;
	private RectangleF _marginsMm;

	private int _scaleMode;
	private float _customScale = 1f;
	private bool _centre = true;
	private bool _background;

	private float _zoom = 1f;

	private Bitmap? _cache;
	private bool _cacheValid;

	#endregion

	#region Свойства

	/// <summary>Показывать ли границу области печати пунктиром.</summary>
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public bool ShowMargins { get; set; } = true;

	#endregion

	#region Создание

	public PreviewPanel()
	{
		SetStyle(
			ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.UserPaint
			| ControlStyles.ResizeRedraw,
			true);
	}

	public void SetPage(
		Page page,
		RectangleF paperMm,
		RectangleF marginsMm,
		int scaleMode,
		float customScale,
		bool centre,
		bool background)
	{
		// Растр переснимается только когда сменилась страница или флаг фона.
		// Масштаб и поля на содержимое растра не влияют — они лишь решают,
		// куда и каким размером его положить.
		bool contentChanged = !ReferenceEquals(_page, page) || _background != background;

		_page = page;
		_paperMm = paperMm;
		_marginsMm = marginsMm;
		_scaleMode = scaleMode;
		_customScale = customScale;
		_centre = centre;
		_background = background;

		if (contentChanged)
			InvalidateCache();

		Invalidate();
	}

	public void SetZoom(float zoom)
	{
		_zoom = Math.Clamp(zoom, 0.05f, 8f);
		UpdateSize();
		Invalidate();
	}

	/// <summary>Масштаб, при котором лист целиком помещается в заданную область.</summary>
	public float ComputeFitZoom(Size available)
	{
		if (_paperMm.Width <= 0f || _paperMm.Height <= 0f)
			return 1f;

		const int Padding = 24;

		float pxPerMm = ScreenPxPerMm;

		float w = Math.Max(1, available.Width - Padding);
		float h = Math.Max(1, available.Height - Padding);

		return Math.Min(w / (_paperMm.Width * pxPerMm), h / (_paperMm.Height * pxPerMm));
	}

	#endregion

	#region Геометрия

	/// <summary>Экранных пикселей на миллиметр при зуме 100 процентов.</summary>
	private static float ScreenPxPerMm => 96f / 25.4f;

	private void UpdateSize()
	{
		if (_paperMm.Width <= 0f || _paperMm.Height <= 0f)
			return;

		float pxPerMm = ScreenPxPerMm * _zoom;

		Size = new Size(
			Math.Max(1, (int)MathF.Round(_paperMm.Width * pxPerMm)),
			Math.Max(1, (int)MathF.Round(_paperMm.Height * pxPerMm)));
	}

	/// <summary>
	/// Прямоугольник страницы на листе, в пикселях контрола.
	/// Повторяет расчёт PrintForm.ComputeTargetRect, но в других единицах.
	/// </summary>
	private RectangleF GetPageRect()
	{
		if (_page == null)
			return RectangleF.Empty;

		var sizeMm = _page.SizeMm;

		if (sizeMm.Width <= 0f || sizeMm.Height <= 0f)
			return RectangleF.Empty;

		float pxPerMm = ScreenPxPerMm * _zoom;

		// Область печати — лист за вычетом полей: именно в неё вписывается
		// страница, и именно её показывает пунктир.
		var area = new RectangleF(
			_marginsMm.X * pxPerMm,
			_marginsMm.Y * pxPerMm,
			(_paperMm.Width - _marginsMm.X - _marginsMm.Width) * pxPerMm,
			(_paperMm.Height - _marginsMm.Y - _marginsMm.Height) * pxPerMm);

		float wantW = sizeMm.Width * pxPerMm;
		float wantH = sizeMm.Height * pxPerMm;

		float scale = _scaleMode switch
		{
			0 => Math.Min(area.Width / wantW, area.Height / wantH),
			1 => 1f,
			_ => _customScale
		};

		float w = wantW * scale;
		float h = wantH * scale;

		float x = _centre ? area.X + (area.Width - w) / 2f : area.X;
		float y = _centre ? area.Y + (area.Height - h) / 2f : area.Y;

		return new RectangleF(x, y, w, h);
	}

	#endregion

	#region Отрисовка

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		var g = e.Graphics;
		g.Clear(Color.FromArgb(110, 110, 110));

		if (_page == null || _paperMm.Width <= 0f)
			return;

		var sheet = new Rectangle(0, 0, Width - 1, Height - 1);

		using (var paper = new SolidBrush(Color.White))
			g.FillRectangle(paper, sheet);

		EnsureCache();

		var pageRect = GetPageRect();

		if (_cache != null && pageRect.Width >= 1f && pageRect.Height >= 1f)
		{
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.DrawImage(_cache, pageRect);
		}

		using (var edge = new Pen(Color.FromArgb(90, 90, 90)))
			g.DrawRectangle(edge, sheet);

		if (ShowMargins)
			DrawMargins(g);
	}

	/// <summary>
	/// Граница области печати пунктиром.
	///
	/// Это подсказка предпросмотра, а не содержимое: на бумагу она не идёт.
	/// Рисуется поверх страницы, потому что смысл её как раз в том, чтобы
	/// показать, что за неё выходит.
	/// </summary>
	private void DrawMargins(Graphics g)
	{
		float pxPerMm = ScreenPxPerMm * _zoom;

		float left = _marginsMm.X * pxPerMm;
		float top = _marginsMm.Y * pxPerMm;
		float right = Width - _marginsMm.Width * pxPerMm;
		float bottom = Height - _marginsMm.Height * pxPerMm;

		if (right - left < 2f || bottom - top < 2f)
			return;

		using var pen = new Pen(Color.FromArgb(200, 40, 40, 40))
		{
			DashStyle = DashStyle.Dash,
			DashPattern = new[] { 4f, 3f }
		};

		g.DrawRectangle(pen, left, top, right - left, bottom - top);
	}

	private void EnsureCache()
	{
		if (_cacheValid || _page == null)
			return;

		_cache?.Dispose();
		_cache = null;

		using var sk = PageRenderer.Render(_page, PreviewDpi, _background);

		if (sk != null)
			_cache = PageRenderer.ToGdiBitmap(sk);

		_cacheValid = true;
	}

	private void InvalidateCache()
	{
		_cacheValid = false;

		_cache?.Dispose();
		_cache = null;
	}

	#endregion

	#region Очистка

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			InvalidateCache();

		base.Dispose(disposing);
	}

	#endregion
}