using SkiaSharp;

namespace VDraw.Editing;

/// <summary>
/// Каретка и выделение текста поверх холста.
///
/// Рисуется в экранных координатах, как и прочие указатели: надпись
/// может быть повёрнута и масштабирована, и прямоугольник выделения
/// вместе с ней перестаёт быть прямоугольником. Поэтому каждая
/// область переводится из координат надписи в мировые, оттуда
/// в экранные, и рисуется четырёхугольником, а не SKRect.
/// </summary>
public static class TextEditAdorner
{
	private static readonly SKColor SelectionColor = new(80, 150, 255, 90);
	private static readonly SKColor CaretColor = new(20, 20, 20, 255);

	public static void Draw(SKCanvas canvas, MainView view, TextEditSession session)
	{
		var world = session.Shape.GetWorldTransform();

		DrawSelection(canvas, view, session, world);
		DrawCaret(canvas, view, session, world);
	}

	private static void DrawSelection(
		SKCanvas canvas, MainView view, TextEditSession session, SKMatrix world)
	{
		var rects = TextEditGeometry.SelectionRects(session);

		if (rects.Count == 0)
			return;

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SelectionColor
		};

		foreach (var rect in rects)
		{
			using var path = new SKPath();

			path.MoveTo(Screen(view, world, rect.Left, rect.Top));
			path.LineTo(Screen(view, world, rect.Right, rect.Top));
			path.LineTo(Screen(view, world, rect.Right, rect.Bottom));
			path.LineTo(Screen(view, world, rect.Left, rect.Bottom));
			path.Close();

			canvas.DrawPath(path, paint);
		}
	}

	private static void DrawCaret(
		SKCanvas canvas, MainView view, TextEditSession session, SKMatrix world)
	{
		if (!session.CaretVisible)
			return;

		var (top, bottom) = TextEditGeometry.CaretLine(session);

		var a = Screen(view, world, top.X, top.Y);
		var b = Screen(view, world, bottom.X, bottom.Y);

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

	private static SKPoint Screen(MainView view, SKMatrix world, float x, float y)
		=> view.PageToScreen(world.MapPoint(new SKPoint(x, y)));
}
