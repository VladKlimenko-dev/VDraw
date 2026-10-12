using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public partial class SColorDialog : Form
{
	private PictureBox[]? spb;
	private int selected_custom_color_index = -1;
	private int colors = 21;
	private int picture_boxes_x = 10;
	private int picture_boxes_y = 8;
	private Size picture_box_sz = new Size(24, 24);
	private int picture_boxes_gap = 4;

	/// <summary>
	/// Defines the Color.
	/// </summary>
	private Color color = Color.White;

	/// <summary>
	/// Defines the hue of SV slider.
	/// </summary>
	private Color hue = Color.Red;

	/// <summary>
	/// Defines the pointer size.
	/// </summary>
	private static int pointerSize = 10;

	/// <summary>
	/// Rectangle for SV pointer.
	/// </summary>
	private RectangleF rec_SV = new RectangleF(0, 0, pointerSize, pointerSize);

	/// <summary>
	/// Rectangle for H pointer.
	/// </summary>
	private RectangleF rec_H = new RectangleF(0, 7.5f, pointerSize, pointerSize);

	/// <summary>
	/// Rectangle for A pointer.
	/// </summary>
	private RectangleF rec_A = new RectangleF(0, 7.5f, pointerSize, pointerSize);

	/// <summary>
	/// Specifies the value change handler.
	/// </summary>
	public event EventHandler ColorChanged;

	/// <summary>
	/// Включён ли режим пипетки — выбора цвета с экрана.
	/// </summary>
	private bool picking_color;

	/// <summary>
	/// Цвет до входа в режим пипетки. Нужен отмене по Escape: во время
	/// наведения цвет меняется на лету, и без запомненного значения
	/// вернуть исходный было бы нечем.
	/// </summary>
	private Color color_before_picking = Color.White;

	public SColorDialog()
	{
		InitializeComponent();
		WinFormsTheme.Register(this);
		ComponentResourceManager resources = new ComponentResourceManager(typeof(SColorDialog));
		spb = new PictureBox[picture_boxes_x * picture_boxes_y];
		for (int j = 0; j < picture_boxes_y; j++)
		{
			for (int i = 0; i < picture_boxes_x; i++)
			{
				spb[i + picture_boxes_x * j] = new PictureBox
				{
					Location = new Point(12 + (picture_box_sz.Width + picture_boxes_gap) * i, 234 + (picture_box_sz.Height + picture_boxes_gap) * j),
					Size = picture_box_sz,
					BackgroundImage = (Image)resources.GetObject("cur_color_pb.BackgroundImage")
				};
				spb[i + picture_boxes_x * j].Paint += CustomColorBox_Paint;
				Controls.Add(spb[i + picture_boxes_x * j]);
				spb[i + picture_boxes_x * j].Click += CustomColorBox_Click;
			}
		}
		spb[0].BackColor = Color.White;
		spb[1].BackColor = Color.LightGray;
		spb[2].BackColor = Color.Gray;
		spb[3].BackColor = Color.DarkGray;
		spb[4].BackColor = Color.Black;
		spb[5].BackColor = Color.Pink;
		spb[6].BackColor = Color.Red;
		spb[7].BackColor = Color.DarkRed;
		spb[8].BackColor = Color.LightGreen;
		spb[9].BackColor = Color.Green;
		spb[10].BackColor = Color.DarkGreen;
		spb[11].BackColor = Color.LightBlue;
		spb[12].BackColor = Color.Blue;
		spb[13].BackColor = Color.DarkBlue;
		spb[14].BackColor = Color.Yellow;
		spb[15].BackColor = Color.LightYellow;
		spb[16].BackColor = Color.SandyBrown;
		spb[17].BackColor = Color.Brown;
		spb[18].BackColor = Color.YellowGreen;
		spb[19].BackColor = Color.Violet;
		spb[20].BackColor = Color.Purple;
		spb[21].BackColor = Color.Indigo;
		spb[22].BackColor = Color.Violet;

		sample_but.Font = MainForm.fSnipit;
		sample_but.Text = "\ue7ea";

		colors = 23;
		for (int i = colors; i < picture_boxes_x * picture_boxes_y; i++)
			spb[i].BackColor = Color.FromArgb(0, 0, 0, 0);
		Refresh();
	}

	private void CustomColorBox_Paint(object? sender, PaintEventArgs e)
	{
		if (sender == null)
			return;
		PictureBox? pb = sender as PictureBox;
		if (pb == null) return;
		int index = (pb.Location.Y - 234) / (picture_box_sz.Height + picture_boxes_gap) * picture_boxes_x + (pb.Location.X - 12) / (picture_box_sz.Width + picture_boxes_gap);
		Pen border = new Pen(Color.Black, 1.0f);
		if (index == selected_custom_color_index)
		{
			border.Width = 3.0f;
			border.Color = Color.DarkOrange;
		}
		Brush fill = new SolidBrush(pb.BackColor);
		e.Graphics.FillRectangle(fill, 0, 0, pb.Width - 1, pb.Height - 1);
		e.Graphics.DrawRectangle(border, 0, 0, pb.Width - 1, pb.Height - 1);
	}

	private void CustomColorBox_Click(object? sender, EventArgs e)
	{
		if (sender == null)
			return;
		PictureBox? pb = sender as PictureBox;
		if (pb == null) return;
		selected_custom_color_index = (pb.Location.Y - 234) / (picture_box_sz.Height + picture_boxes_gap) * picture_boxes_x + (pb.Location.X - 12) / (picture_box_sz.Width + picture_boxes_gap);
		SetColor(spb[selected_custom_color_index].BackColor);
		Refresh();
	}

	public Color GetColor()
	{
		return color;
	}

	public SKColorF GetColorV4()
	{
		return new SKColorF(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
	}

	public SKColor GetSKColor()
	{
		return new SKColor(color.R, color.G, color.B, color.A);
	}

	private void UpdateColor()
	{
		Color oldcolor = color;

		double s = (rec_SV.X / SV_Slider.Width);
		double v = ((SV_Slider.Height - rec_SV.Y) / SV_Slider.Height);
		double h = (rec_H.X / SV_Slider.Width) * 360;
		double a = (rec_A.X / A_Slider.Width);

		color = Color.FromArgb((int)(a * 255.0), HSVRGBConverter.ColorFromHSV(h, s, v));

		if (oldcolor != color)
			ColorChanged?.Invoke(this, null);
	}

	public void SetColorV4(SKColorF _color)
	{
		SetColor(Color.FromArgb((int)(_color.Alpha * 255f), (int)(_color.Red * 255f), (int)(_color.Green * 255f), (int)(_color.Blue * 255f)));
	}

	public void SetSKColor(SKColor _color)
	{
		SetColor(Color.FromArgb(_color.Alpha, _color.Red, _color.Green, _color.Blue));
	}

	private void SetColor(Color _color)
	{
		Color oldcolor = color;

		double h, s, v, a;

		HSVRGBConverter.ColorToHSV(_color, out h, out s, out v);
		a = _color.A / 255.0;

		rec_SV.X = (float)(s * SV_Slider.Width);
		rec_SV.Y = (float)(SV_Slider.Height - (v * SV_Slider.Height));
		rec_H.X = (float)((h / 360) * H_Slider.Width);
		rec_A.X = (float)(a * A_Slider.Width);

		hue = HSVRGBConverter.ColorFromHSV((rec_H.X / H_Slider.Width) * 360, 1, 1);
		color = Color.FromArgb((int)(a * 255.0), _color);

		/*
        SV_Slider.Refresh();
        H_Slider.Refresh();
        A_Slider.Refresh();
        cur_color_pb.Refresh();
        */
		if (oldcolor != color)
		{
			ColorChanged?.Invoke(this, null);
			Refresh();
		}
	}

	private void SV_Slider_Paint(object? sender, PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, SV_Slider.Width, SV_Slider.Height), Color.White, hue, LinearGradientMode.Horizontal))
			e.Graphics.FillRectangle(br, SV_Slider.ClientRectangle);

		using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, SV_Slider.Width, SV_Slider.Height), Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical))
			e.Graphics.FillRectangle(br, SV_Slider.ClientRectangle);

		e.Graphics.DrawRectangle(new Pen(Color.FromArgb(30, 0, 0, 0), 2), 0, 0, SV_Slider.Width, SV_Slider.Height);

		DrawPointer(e.Graphics, rec_SV);
	}

	private void SV_Slider_MouseMove(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			if (e.X < 0)
				rec_SV.X = 0;
			else if (e.X > SV_Slider.Width)
				rec_SV.X = SV_Slider.Width;
			else
				rec_SV.X = e.X;

			if (e.Y < 0)
				rec_SV.Y = 0;
			else if (e.Y > SV_Slider.Height)
				rec_SV.Y = SV_Slider.Height;
			else
				rec_SV.Y = e.Y;

			UpdateColor();
			if (selected_custom_color_index != -1)
			{
				selected_custom_color_index = -1;
				Refresh();
			}
			else
			{
				SV_Slider.Refresh();
				H_Slider.Refresh();
				A_Slider.Refresh();
				cur_color_pb.Refresh();
			}
		}
	}

	private void H_Slider_Paint(object? sender, PaintEventArgs e)
	{

		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using (Brush rainbow_brush = RainbowBrush(new Point(0, H_Slider.Top), new Point(H_Slider.Width, H_Slider.Top)))
		{
			e.Graphics.FillRectangle(rainbow_brush, H_Slider.ClientRectangle);
		}
		e.Graphics.DrawRectangle(new Pen(Color.FromArgb(30, 0, 0, 0), 2), 0, 0, H_Slider.Width + 1, H_Slider.Height + 1);

		DrawPointer(e.Graphics, rec_H);
	}

	private void H_Slider_MouseMove(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			if (e.X < 0)
				rec_H.X = 0;
			else if (e.X > H_Slider.Width)
				rec_H.X = H_Slider.Width;
			else
				rec_H.X = e.X;

			hue = HSVRGBConverter.ColorFromHSV((rec_H.X / H_Slider.Width) * 360, 1, 1);

			UpdateColor();
			if (selected_custom_color_index != -1)
			{
				selected_custom_color_index = -1;
				Refresh();
			}
			else
			{
				SV_Slider.Refresh();
				H_Slider.Refresh();
				A_Slider.Refresh();
				cur_color_pb.Refresh();
			}
		}
	}

	private void A_Slider_Paint(object? sender, PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using (Brush brush = TransparentFillBrush(new Point(0, A_Slider.Top), new Point(A_Slider.Width, A_Slider.Top), color))
		{
			e.Graphics.FillRectangle(brush, A_Slider.ClientRectangle);
		}
		e.Graphics.DrawRectangle(new Pen(Color.FromArgb(30, 0, 0, 0), 2), 0, 0, A_Slider.Width + 1, A_Slider.Height + 1);
		DrawPointer(e.Graphics, rec_A);
	}

	private void A_Slider_MouseMove(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{

			if (e.X < 0)
				rec_A.X = 0;
			else if (e.X > A_Slider.Width)
				rec_A.X = A_Slider.Width;
			else
				rec_A.X = e.X;

			UpdateColor();

			A_Slider.Refresh();
			cur_color_pb.BackColor = color;

			if (selected_custom_color_index != -1)
			{
				selected_custom_color_index = -1;
				Refresh();
			}
			else
			{
				SV_Slider.Refresh();
				H_Slider.Refresh();
				A_Slider.Refresh();
				cur_color_pb.Refresh();
			}
		}
	}

	private void DrawPointer(Graphics g, RectangleF r)
	{
		g.DrawEllipse(new Pen(Color.FromArgb(50, 0, 0, 0), 5), r.X - (r.Width / 2), r.Y - (r.Height / 2), r.Width, r.Height);
		g.DrawEllipse(new Pen(Color.White, 3), r.X - (r.Width / 2), r.Y - (r.Height / 2), r.Width, r.Height);
	}

	public LinearGradientBrush RainbowBrush(Point point1, Point point2)
	{
		LinearGradientBrush rainbow_brush = new LinearGradientBrush(point1, point2, Color.Red, Color.Red);

		ColorBlend color_blend = new ColorBlend();
		color_blend.Colors = new Color[]
			{ Color.Red, Color.Yellow, Color.Lime, Color.Aqua,  Color.Blue, Color.Fuchsia, Color.Red };
		color_blend.Positions = new float[]
			{ 0, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1 };
		rainbow_brush.InterpolationColors = color_blend;

		return rainbow_brush;
	}

	public LinearGradientBrush TransparentFillBrush(Point point1, Point point2, Color _color)
	{
		LinearGradientBrush rainbow_brush = new LinearGradientBrush(point1, point2, Color.Red, Color.Red);

		ColorBlend color_blend = new ColorBlend();
		color_blend.Colors = new Color[]
			{ Color.FromArgb(0, _color), Color.FromArgb(255, _color) };
		color_blend.Positions = new float[]
			{ 0, 1 };
		rainbow_brush.InterpolationColors = color_blend;

		return rainbow_brush;
	}

	private void cur_color_pb_Paint(object? sender, PaintEventArgs e)
	{
		using (Brush b = new SolidBrush(color))
		{
			e.Graphics.FillRectangle(b, 0, 0, cur_color_pb.Width, cur_color_pb.Height);
		}
	}

	private void add_custom_color_but_Click(object? sender, EventArgs e)
	{
		spb[colors].BackColor = color;
		Refresh();
		if (colors < picture_boxes_x * picture_boxes_y - 1)
			colors++;
	}

	private void sample_but_Click(object sender, EventArgs e)
	{
		StartColorPicking();
	}

	/// <summary>
	/// Входит в режим пипетки.
	///
	/// Захват мыши (Capture) обязателен: без него форма перестаёт получать
	/// события мыши, как только курсор уходит за её пределы, а выбирать цвет
	/// надо именно снаружи. С захватом сообщения продолжают приходить сюда,
	/// в какой бы точке экрана ни был курсор.
	/// </summary>
	private void StartColorPicking()
	{
		if (picking_color)
			return;

		picking_color = true;
		color_before_picking = color;

		Capture = true;
		Cursor = Cursors.Cross;
	}

	/// <summary>
	/// Выходит из режима пипетки. При cancel = true возвращает цвет,
	/// который был до входа.
	/// </summary>
	private void StopColorPicking(bool cancel)
	{
		if (!picking_color)
			return;

		picking_color = false;

		Capture = false;
		Cursor = Cursors.Default;

		if (cancel)
			SetColor(color_before_picking);

		Refresh();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		if (!picking_color)
			return;

		ApplyPickedColor(ReadScreenPixelUnderCursor());
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (picking_color)
		{
			// Правая кнопка отменяет — привычный жест для режима выбора.
			if (e.Button == MouseButtons.Right)
			{
				StopColorPicking(cancel: true);
				return;
			}

			ApplyPickedColor(ReadScreenPixelUnderCursor());
			StopColorPicking(cancel: false);
			return;
		}

		base.OnMouseDown(e);
	}

	/// <summary>
	/// Escape отменяет выбор.
	///
	/// Через ProcessCmdKey, а не KeyDown: во время захвата мыши фокус
	/// клавиатуры может быть на кнопке, и обычное событие формы до неё
	/// бы не дошло.
	/// </summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (picking_color && keyData == Keys.Escape)
		{
			StopColorPicking(cancel: true);
			return true;
		}

		return base.ProcessCmdKey(ref msg, keyData);
	}

	/// <summary>
	/// Ставит снятый с экрана цвет, сохраняя текущую прозрачность.
	///
	/// Альфа берётся прежняя, а не из снимка: на экране всё непрозрачно,
	/// и CopyFromScreen всегда вернёт 255 — пипетка сбрасывала бы
	/// выставленную ползунком прозрачность при каждом снятии цвета.
	/// </summary>
	private void ApplyPickedColor(Color screenColor)
	{
		// Пустой цвет означает, что снимок не удался. Без этой проверки
		// Color.Empty дал бы чёрный: у него все составляющие нулевые.
		if (screenColor.IsEmpty)
			return;

		var picked = Color.FromArgb(color.A, screenColor.R, screenColor.G, screenColor.B);

		if (picked == color)
			return;

		// Снятый с экрана цвет не принадлежит палитре образцов: подсветку
		// выбранного образца надо снять, иначе она осталась бы висеть
		// на клетке с другим цветом.
		selected_custom_color_index = -1;

		SetColor(picked);
		Refresh();
	}

	#region Пипетка: чтение пикселя экрана

	/// <summary>Контекст осведомлённости о масштабе: на монитор, версия 2.</summary>
	private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);

	/// <summary>Значение, которое GetPixel возвращает при неудаче.</summary>
	private const uint ClrInvalid = 0xFFFFFFFF;

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int X;
		public int Y;
	}

	/// <summary>
	/// Положение курсора в ФИЗИЧЕСКИХ пикселях экрана, независимо от того,
	/// как процесс объявил свою осведомлённость о масштабе.
	/// </summary>
	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetPhysicalCursorPos(out NativePoint point);

	[DllImport("user32.dll")]
	private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

	[DllImport("gdi32.dll")]
	private static extern uint GetPixel(IntPtr hdc, int x, int y);

	/// <summary>
	/// Цвет пикселя экрана под курсором.
	///
	/// Прежний способ — Cursor.Position плюс Graphics.CopyFromScreen —
	/// промахивался, как только курсор уходил на монитор с другим
	/// масштабом. Приложение объявлено осведомлённым о масштабе СИСТЕМЫ
	/// (ApplicationConfiguration.Initialize без явного HighDpiMode даёт
	/// SystemAware), и на таком мониторе Windows подменяет координаты
	/// процессу: и положение курсора, и снимок экрана пересчитываются
	/// под масштаб основного монитора. Промах тем больше, чем сильнее
	/// расходятся масштабы, и на краю второго монитора достигает сотен
	/// пикселей.
	///
	/// Поэтому на время чтения поток переводится в режим
	/// «осведомлён о масштабе каждого монитора», а положение курсора
	/// берётся функцией, которая физические координаты отдаёт всегда.
	/// В этом режиме и координаты, и содержимое экрана считаются
	/// в одних и тех же физических пикселях, и пипетка попадает туда,
	/// куда показывает.
	///
	/// Читается один пиксель напрямую из контекста экрана, без промежуточного
	/// растра: копировать область 1x1 через Graphics означало лишнюю
	/// пересылку, которая к тому же и подвергалась пересчёту.
	/// </summary>
	private static Color ReadScreenPixelUnderCursor()
	{
		IntPtr previousContext = IntPtr.Zero;
		IntPtr screenDc = IntPtr.Zero;

		try
		{
			// Функция появилась в Windows 10 1607. На более старых системах
			// сработает запасной путь в catch.
			previousContext = SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);

			if (!GetPhysicalCursorPos(out var point))
				return Color.Empty;

			// Нулевой дескриптор окна — контекст всего рабочего стола,
			// включая мониторы с отрицательными координатами: слева
			// и сверху от основного.
			screenDc = GetDC(IntPtr.Zero);

			if (screenDc == IntPtr.Zero)
				return Color.Empty;

			uint bgr = GetPixel(screenDc, point.X, point.Y);

			if (bgr == ClrInvalid)
				return Color.Empty;

			// COLORREF хранит составляющие в обратном порядке: синий старше.
			return Color.FromArgb(
				(int)(bgr & 0xFF),
				(int)((bgr >> 8) & 0xFF),
				(int)((bgr >> 16) & 0xFF));
		}
		catch (EntryPointNotFoundException)
		{
			return ReadScreenPixelLegacy(Cursor.Position);
		}
		catch
		{
			// Чтение может не удаться — например если курсор оказался
			// над защищённым окном. Тогда цвет просто не меняется.
			return Color.Empty;
		}
		finally
		{
			if (screenDc != IntPtr.Zero)
				ReleaseDC(IntPtr.Zero, screenDc);

			// Режим потока возвращается обязательно: оставленный
			// «на монитор» контекст исказил бы разметку окон,
			// построенную в расчёте на масштаб системы.
			if (previousContext != IntPtr.Zero)
				SetThreadDpiAwarenessContext(previousContext);
		}
	}

	/// <summary>
	/// Запасное чтение для систем старше Windows 10 1607. Там мониторы
	/// с разным масштабом не поддерживаются, и прежний способ верен.
	/// </summary>
	private static Color ReadScreenPixelLegacy(Point screenPoint)
	{
		try
		{
			using Bitmap bmp = new Bitmap(1, 1);

			using (Graphics g = Graphics.FromImage(bmp))
				g.CopyFromScreen(screenPoint.X, screenPoint.Y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);

			return bmp.GetPixel(0, 0);
		}
		catch
		{
			return Color.Empty;
		}
	}

	#endregion
}

public class HSVRGBConverter
{
	public static void ColorToHSV(Color color, out double hue, out double saturation, out double value)
	{
		int max = Math.Max(color.R, Math.Max(color.G, color.B));
		int min = Math.Min(color.R, Math.Min(color.G, color.B));

		hue = color.GetHue();
		saturation = (max == 0) ? 0 : 1d - (1d * min / max);
		value = max / 255d;
	}

	public static Color ColorFromHSV(double hue, double saturation, double value)
	{
		int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
		double f = hue / 60 - Math.Floor(hue / 60);

		value = value * 255;
		int v = Convert.ToInt32(value);
		int p = Convert.ToInt32(value * (1 - saturation));
		int q = Convert.ToInt32(value * (1 - f * saturation));
		int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

		if (hi == 0)
			return Color.FromArgb(255, v, t, p);
		else if (hi == 1)
			return Color.FromArgb(255, q, v, p);
		else if (hi == 2)
			return Color.FromArgb(255, p, v, t);
		else if (hi == 3)
			return Color.FromArgb(255, p, q, v);
		else if (hi == 4)
			return Color.FromArgb(255, t, p, v);
		else
			return Color.FromArgb(255, v, p, q);
	}
}