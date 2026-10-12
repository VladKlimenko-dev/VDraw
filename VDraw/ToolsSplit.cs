using System.ComponentModel;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

/// <summary>
/// Вертикальный разделитель секций панели инструментов.
///
/// Полоска в один пиксель, высотой в 70% высоты панели, по центру
/// по вертикали. Сама по себе не кликается и фокус не принимает: это
/// чисто визуальный элемент, и попадание по нему табуляцией или мышью
/// сбивало бы работу с соседними кнопками.
///
/// Цвет выводится из фона родителя, а не задан константой: панели могут
/// иметь разный фон, и жёстко прошитый серый на тёмной панели оказался бы
/// светлее фона, то есть выглядел бы не разделителем, а бликом.
/// </summary>
[ToolboxItem(false)]
[DesignerCategory("")]
public sealed class ToolsSplit : Control
{
	/// <summary>
	/// Доля высоты панели, которую занимает полоска.
	/// </summary>
	private const float HeightFactor = 0.7f;

	/// <summary>
	/// Полная ширина контрола: сама линия плюс воздух с обеих сторон.
	/// Без отступов разделитель прилипал бы к соседним кнопкам и читался
	/// как их рамка.
	/// </summary>
	private const int DefaultWidthPx = 9;

	public ToolsSplit()
	{
		// UserPaint — рисуем сами; AllPaintingInWmPaint и OptimizedDoubleBuffer
		// убирают мерцание при перерисовке панели.
		SetStyle(
			ControlStyles.UserPaint |
			ControlStyles.AllPaintingInWmPaint |
			ControlStyles.OptimizedDoubleBuffer |
			ControlStyles.ResizeRedraw |
			ControlStyles.SupportsTransparentBackColor,
			true);

		// Разделитель не участвует в навигации и не перехватывает щелчки.
		SetStyle(ControlStyles.Selectable, false);

		TabStop = false;
		Width = DefaultWidthPx;
		Margin = Padding.Empty;
		BackColor = Color.Transparent;
	}

	/// <summary>Толщина линии в пикселях. Обычно 1.</summary>
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public int LineThickness { get; set; } = 1;

	/// <summary>
	/// Насколько цвет линии темнее фона. 0 — вровень с фоном,
	/// 1 — чёрный.
	/// </summary>
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public float Darkening { get; set; } = 0.18f;

	protected override Size DefaultSize => new(DefaultWidthPx, 24);

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		int h = (int)Math.Round(Height * HeightFactor);

		if (h <= 0 || Width <= 0)
			return;

		int thickness = Math.Max(1, LineThickness);

		int x = (Width - thickness) / 2;
		int y = (Height - h) / 2;

		using var brush = new SolidBrush(GetLineColor());
		e.Graphics.FillRectangle(brush, x, y, thickness, h);
	}

	/// <summary>
	/// Цвет линии: фон родителя, затемнённый на заданную долю.
	///
	/// Берётся именно у родителя, а не собственный BackColor: он у нас
	/// прозрачный, и через него всегда виден фон панели.
	/// </summary>
	private Color GetLineColor()
	{
		Color baseColor = Parent?.BackColor ?? SystemColors.Control;

		float k = Math.Clamp(1f - Darkening, 0f, 1f);

		return Color.FromArgb(
			baseColor.A,
			(int)(baseColor.R * k),
			(int)(baseColor.G * k),
			(int)(baseColor.B * k));
	}

	/// <summary>
	/// Смена фона панели должна менять и цвет линии — иначе разделитель
	/// останется прежним и на новом фоне может исчезнуть.
	/// </summary>
	protected override void OnParentBackColorChanged(EventArgs e)
	{
		base.OnParentBackColorChanged(e);
		Invalidate();
	}
}