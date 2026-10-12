using System.Drawing;
using SkiaSharp;
using System.Drawing.Imaging;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

/// <summary>
/// Отрисовка страницы целиком — для печати и предпросмотра.
///
/// Отдельно от ShapeExporter: тот работает с выделением и подбирает габариты
/// под фигуры, а печати нужен лист целиком, включая пустые поля. Общее у них
/// одно — фигуры рисуются тем же Shape.Draw, что и на холсте, поэтому бумага
/// повторяет экран.
/// </summary>
public static class PageRenderer
{
	#region Константы

	/// <summary>Предел стороны растра. Защита от попытки выделить гигабайты.</summary>
	private const int MaxSide = 20000;

	#endregion

	#region Отрисовка

	/// <summary>
	/// Рисует страницу в растр заданного разрешения.
	///
	/// Направляющие пропускаются: это разметка редактора, а не содержимое
	/// документа, и на бумаге ей делать нечего.
	/// </summary>
	public static SKBitmap? Render(Page page, int dpi, bool drawBackground)
	{
		if (page == null)
			return null;

		var sizeMm = page.SizeMm;

		if (sizeMm.Width <= 0f || sizeMm.Height <= 0f)
			return null;

		float pxPerMm = Math.Max(dpi, 1) / 25.4f;

		int width = (int)MathF.Ceiling(sizeMm.Width * pxPerMm);
		int height = (int)MathF.Ceiling(sizeMm.Height * pxPerMm);

		if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
			return null;

		var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

		using (var canvas = new SKCanvas(bitmap))
		{
			canvas.Clear(drawBackground ? GetBackground(page) : SKColors.White);

			// Мировая единица страницы — это uom, а не миллиметр, поэтому
			// масштаб канвы учитывает cmm. После этого фигуры рисуются
			// в своих обычных координатах.
			float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);
			float pxPerWorldUnit = pxPerMm * mmPerWorldUnit;

			canvas.Scale(pxPerWorldUnit);

			DrawShapes(canvas, page, mmPerWorldUnit);
		}

		return bitmap;
	}

	private static SKColor GetBackground(Page page)
	{
		var c = page.background_color;

		return new SKColor(
			(byte)Math.Clamp(c.Red * 255f, 0f, 255f),
			(byte)Math.Clamp(c.Green * 255f, 0f, 255f),
			(byte)Math.Clamp(c.Blue * 255f, 0f, 255f),
			(byte)Math.Clamp(c.Alpha * 255f, 0f, 255f));
	}

	private static void DrawShapes(SKCanvas canvas, Page page, float mmPerWorldUnit)
	{
		// Режим actual dpi приводит картинку к разрешению документа,
		// а оно живёт на странице.
		ImageShape.RenderDpi = page.DPI;

		foreach (var shape in page.Shapes.OrderBy(s => s.Layer))
		{
			if (shape is GuideLine || !shape.IsVisible)
				continue;

			// Выделение не должно влиять на печать: обводка выделенной фигуры
			// рисуется толще, и на бумагу попала бы утолщённая линия.
			bool wasSelected = shape.IsSelected;
			bool wasFocused = shape.IsFocused;

			shape.IsSelected = false;
			shape.IsFocused = false;

			try
			{
				shape.Render(canvas, mmPerWorldUnit);
			}
			finally
			{
				shape.IsSelected = wasSelected;
				shape.IsFocused = wasFocused;
			}
		}
	}

	#endregion

	#region Преобразование в System.Drawing

	/// <summary>
	/// Переводит растр Skia в System.Drawing.Bitmap.
	///
	/// Нужно и печати, и предпросмотру: рисовать на Graphics умеет только
	/// System.Drawing. Раскладка байт у SKColorType.Bgra8888 и
	/// PixelFormat.Format32bppPArgb совпадает на little-endian, поэтому
	/// строки копируются как есть.
	/// </summary>
	public static Bitmap ToGdiBitmap(SKBitmap source)
	{
		int w = source.Width;
		int h = source.Height;

		var result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
		var rect = new Rectangle(0, 0, w, h);
		var locked = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

		try
		{
			IntPtr src = source.GetPixels();
			int srcStride = source.RowBytes;
			int dstStride = locked.Stride;
			int rowBytes = w * 4;

			var row = new byte[rowBytes];

			for (int y = 0; y < h; y++)
			{
				System.Runtime.InteropServices.Marshal.Copy(src + y * srcStride, row, 0, rowBytes);
				System.Runtime.InteropServices.Marshal.Copy(row, 0, locked.Scan0 + y * dstStride, rowBytes);
			}
		}
		finally
		{
			result.UnlockBits(locked);
		}

		return result;
	}

	#endregion
}