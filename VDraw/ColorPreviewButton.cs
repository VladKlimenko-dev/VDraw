using SkiaSharp;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;


public class ColorPreviewButton : Control
{
	private Color _previewColor = Color.FromArgb(128, Color.Red);
	private int _cellSize = 6;

	public ColorPreviewButton()
	{
		SetStyle(
			ControlStyles.AllPaintingInWmPaint |
			ControlStyles.UserPaint |
			ControlStyles.OptimizedDoubleBuffer |
			ControlStyles.ResizeRedraw |
			ControlStyles.SupportsTransparentBackColor,
			true);

		Size = new Size(42, 42);
		BackColor = Color.Transparent;
	}

	[Browsable(true)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public Color PreviewColor
	{
		get => _previewColor;
		set
		{
			// Перерисовка только при действительном изменении: цвет
			// проставляется в том числе из периодического обновления
			// состояния тулбара, и безусловный Invalidate заставлял бы
			// кнопку мигать несколько раз в секунду.
			if (_previewColor == value)
				return;

			_previewColor = value;
			Invalidate();
		}
	}

	[Browsable(true)]
	[DefaultValue(6)]
	public int CheckerCellSize
	{
		get => _cellSize;
		set
		{
			_cellSize = Math.Max(2, value);
			Invalidate();
		}
	}

	/// <summary>
	/// Ставит цвет образца.
	///
	/// Через свойство, а не в поле напрямую. Прямое присваивание не звало
	/// Invalidate, и кнопка оставалась нарисованной прежним цветом до
	/// первой посторонней перерисовки — перекрытия окном, смены размера.
	/// Пользователь выбирал цвет в диалоге, а на кнопке ничего не менялось.
	/// </summary>
	public void SetSKColor(SKColor _color)
	{
		PreviewColor = Color.FromArgb(_color.Alpha, _color.Red, _color.Green, _color.Blue);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		var g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.Half;

		var rect = ClientRectangle;
		rect.Width -= 1;
		rect.Height -= 1;

		DrawChecker(g, rect, _cellSize);

		using (var brush = new SolidBrush(_previewColor))
			g.FillRectangle(brush, rect);

		using (var border = new Pen(Color.FromArgb(120, 120, 120)))
			g.DrawRectangle(border, rect);

		if (Focused)
		{
			rect.Inflate(-2, -2);
			ControlPaint.DrawFocusRectangle(g, rect);
		}
	}

	private static void DrawChecker(Graphics g, Rectangle rect, int cell)
	{
		using var light = new SolidBrush(Color.White);
		using var dark = new SolidBrush(Color.LightGray);

		for (int y = rect.Top; y < rect.Bottom; y += cell)
		{
			for (int x = rect.Left; x < rect.Right; x += cell)
			{
				bool even = ((x - rect.Left) / cell + (y - rect.Top) / cell) % 2 == 0;
				g.FillRectangle(even ? light : dark, x, y, cell, cell);
			}
		}
	}
}