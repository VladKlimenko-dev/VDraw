using SkiaSharp;

namespace VDraw.Editing;

/// <summary>
/// Каретка и выделение текста поверх холста.
///
/// Геометрию считает TextEditGeometry — и считает её в мировых
/// координатах, уже с поворотом надписи и с изгибом кривой. Здесь
/// остаётся перевести точки в экранные и нарисовать.
/// </summary>
public static class TextEditAdorner
{
	private static readonly SKColor SelectionColor = new(80, 150, 255, 90);
	private static readonly SKColor CaretColor = new(20, 20, 20, 255);

	public static void Draw(SKCanvas canvas, MainView view, TextEditSession session)
	{
		DrawSelection(canvas, view, session);
		DrawCaret(canvas, view, session);
	}

	private static void DrawSelection(SKCanvas canvas, MainView view, TextEditSession session)
	{
		var quads = TextEditGeometry.SelectionQuads(session);

		if (quads.Count == 0)
			return;

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SelectionColor
		};

		foreach (var quad in quads)
		{
			using var path = new SKPath();

			path.MoveTo(view.PageToScreen(quad[0]));

			for (int i = 1; i < quad.Length; i++)
				path.LineTo(view.PageToScreen(quad[i]));

			path.Close();

			canvas.DrawPath(path, paint);
		}
	}

	private static void DrawCaret(SKCanvas canvas, MainView view, TextEditSession session)
	{
		if (!session.CaretVisible)
			return;

		var segment = TextEditGeometry.CaretSegment(session);

		if (segment == null)
			return;

		var a = view.PageToScreen(segment.Value.Top);
		var b = view.PageToScreen(segment.Value.Bottom);

		// Толщина в экранных точках, а не в единицах листа: каретка —
		// указатель, и при мелком масштабе она должна остаться видимой,
		// а при крупном не расползтись в полосу.
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.6f,
			Color = CaretColor,
			StrokeCap = SKStrokeCap.Butt
		};

		canvas.DrawLine(a, b, paint);
	}
}
