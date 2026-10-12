using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Отрисовка кадров анимации страницы для экспорта.
///
/// Кадр строится тем же путём, что и обычный растровый экспорт: значения
/// дорожек раскладываются по фигурам через PageAnimation.ApplyFrame, после
/// чего сцена рисуется существующим кодом. Отдельного «анимационного»
/// рисования нет и быть не должно — иначе GIF расходился бы с тем, что
/// пользователь видит на холсте.
/// </summary>
public static class AnimationFrameRenderer
{
	/// <summary>
	/// Габариты, общие для всего диапазона кадров.
	///
	/// Считаются по объединению габаритов каждого кадра, а не по первому.
	/// Фигура, уезжающая за пределы стартового положения, иначе обрезалась
	/// бы, причём тем сильнее, чем дальше уехала. Проход по кадрам ради
	/// этого делается лишний раз, но альтернатива — испорченная анимация.
	/// </summary>
	public static SKRect GetAnimationBounds(Page page, IReadOnlyList<Shape> shapes, int firstFrame, int lastFrame)
	{
		var anim = page.Animation;

		int restore = anim.CurrentFrame;

		var union = SKRect.Empty;
		bool any = false;

		try
		{
			for (int frame = firstFrame; frame <= lastFrame; frame++)
			{
				anim.ApplyFrame(page, frame);

				var bounds = ShapeExporter.GetExportBounds(page, shapes);

				if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
					continue;

				if (!any)
				{
					union = bounds;
					any = true;
				}
				else
				{
					union.Union(bounds);
				}
			}
		}
		finally
		{
			// Состояние страницы обязано вернуться: пользователь не просил
			// перематывать анимацию, он просил её сохранить.
			anim.ApplyFrame(page, restore);
		}

		return any ? union : SKRect.Empty;
	}

	/// <summary>
	/// Отрисовывает один кадр в заданный размер.
	///
	/// Габариты передаются извне и одни на всю анимацию: пересчитывай их
	/// на каждом кадре, и содержимое дёргалось бы в кадре при каждом
	/// изменении границ сцены.
	/// </summary>
	public static SKBitmap? RenderFrame(
		Page page,
		IReadOnlyList<Shape> shapes,
		int frame,
		SKRect bounds,
		int pixelWidth,
		int pixelHeight,
		SKColor background,
		SKRectI padding = default)
	{
		page.Animation.ApplyFrame(page, frame);

		return ShapeExporter.RenderToBitmapWithBounds(
			page, shapes, bounds, pixelWidth, pixelHeight, background, padding);
	}

	/// <summary>
	/// Собирает и записывает анимацию целиком.
	///
	/// Кадры готовятся по одному и сразу отдаются кодировщику: держать
	/// в памяти всю анимацию незачем, а на сотне кадров крупного размера
	/// это гигабайты.
	/// </summary>
	public static bool ExportGif(
		string path,
		Page page,
		IReadOnlyList<Shape> shapes,
		int firstFrame,
		int lastFrame,
		int pixelWidth,
		int pixelHeight,
		SKColor background,
		bool transparent,
		int fps,
		SKRect? boundsOverride = null,
		SKRectI padding = default,
		Action<int, int>? progress = null)
	{
		if (page == null || shapes == null || shapes.Count == 0)
			return false;

		int first = Math.Max(1, Math.Min(firstFrame, lastFrame));
		int last = Math.Min(page.Animation.FrameCount, Math.Max(firstFrame, lastFrame));

		// Габариты, заданные извне, важнее вычисленных: пользователь мог
		// выбрать окно на странице, и содержимое обязано обрезаться по нему,
		// а не по своим границам.
		var bounds = boundsOverride ?? GetAnimationBounds(page, shapes, first, last);

		if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
			return false;

		// Задержка в GIF хранится в сотых долях секунды, поэтому частота
		// округляется. Ноль недопустим: часть просмотрщиков трактует его
		// как «максимально быстро», часть — как «одна десятая секунды»,
		// и анимация шла бы с разной скоростью в разных программах.
		int delay = Math.Max(2, (int)MathF.Round(100f / Math.Clamp(fps, 1, 100)));

		int restore = page.Animation.CurrentFrame;

		var frames = new List<GifEncoder.Frame>(last - first + 1);

		try
		{
			for (int frame = first; frame <= last; frame++)
			{
				var bitmap = RenderFrame(
					page, shapes, frame, bounds, pixelWidth, pixelHeight, background, padding);

				if (bitmap == null)
					continue;

				frames.Add(new GifEncoder.Frame
				{
					Bitmap = bitmap,
					DelayCentiseconds = delay
				});

				progress?.Invoke(frame - first + 1, last - first + 1);
			}

			if (frames.Count == 0)
				return false;

			GifEncoder.Write(path, frames, transparent);

			return true;
		}
		finally
		{
			foreach (var frame in frames)
				frame.Bitmap.Dispose();

			page.Animation.ApplyFrame(page, restore);
		}
	}
}
