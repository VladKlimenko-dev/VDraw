using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public static class SkTextHelper
{
	public static float Measure(string? text, SKFont font)
		=> font == null ? 0f : font.MeasureText(text ?? string.Empty);

	/// <summary>Отрисовка заданным шрифтом от левого края.</summary>
	public static void DrawLeft(SKCanvas canvas, string? text, float x, float baseline, SKFont font, SKColor color)
	{
		if (font == null)
			return;

		using var paint = new SKPaint { IsAntialias = true, Color = color };
		canvas.DrawText(text ?? string.Empty, x, baseline, SKTextAlign.Left, font, paint);
	}

	/// <summary>Отрисовка заданным шрифтом по центру относительно x.</summary>
	public static void DrawCenter(SKCanvas canvas, string? text, float x, float baseline, SKFont font, SKColor color)
	{
		if (font == null)
			return;

		using var paint = new SKPaint { IsAntialias = true, Color = color };
		canvas.DrawText(text ?? string.Empty, x, baseline, SKTextAlign.Center, font, paint);
	}

	/// <summary>
	/// Текст по центру прямоугольника — и по горизонтали, и по вертикали.
	///
	/// Базовая линия считается из метрик шрифта, а не подбором коэффициента.
	/// Прежний код в SkButton использовал MidY + FontSize * 0.36f: для Segoe UI
	/// при кегле 13 это близко к правде, но для другого шрифта или крупного
	/// кегля текст уезжает по вертикали, а у иконочных шрифтов промахивается
	/// заметно — у них другие подъём и спуск.
	/// </summary>
	public static void DrawCenteredInRect(SKCanvas canvas, string? text, SKRect rect, SKFont font, SKColor color)
	{
		if (font == null || string.IsNullOrEmpty(text))
			return;

		var metrics = font.Metrics;

		// Ascent в Skia отрицательный. Середина габарита чернил должна попасть
		// в середину прямоугольника.
		float baseline = rect.MidY - (metrics.Ascent + metrics.Descent) * 0.5f;

		using var paint = new SKPaint { IsAntialias = true, Color = color };
		canvas.DrawText(text, rect.MidX, baseline, SKTextAlign.Center, font, paint);
	}


	public static SKFont CreateFont(float size, bool bold = false)
	{
		var typeface = SKTypeface.FromFamilyName(
			"Segoe UI",
			bold ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
			SKFontStyleWidth.Normal,
			SKFontStyleSlant.Upright);

		return new SKFont(typeface, size);
	}

	public static float Measure(string text, float size, bool bold = false)
	{
		using var font = CreateFont(size, bold);
		return font.MeasureText(text ?? string.Empty);
	}

	public static void DrawLeft(SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold = false)
	{
		using var font = CreateFont(size, bold);
		using var paint = new SKPaint { IsAntialias = true, Color = color };
		canvas.DrawText(text ?? string.Empty, x, y, SKTextAlign.Left, font, paint);
	}

	public static void DrawCenter(SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold = false)
	{
		using var font = CreateFont(size, bold);
		using var paint = new SKPaint { IsAntialias = true, Color = color };
		canvas.DrawText(text ?? string.Empty, x, y, SKTextAlign.Center, font, paint);
	}

	private static readonly Dictionary<(float Size, bool Bold), (float Ascent, float Descent, float LineHeight)> _metricsCache = new();

	/// <summary>
	/// Метрики шрифта: подъём над базовой линией, спуск под ней и полная высота
	/// строки с межстрочным интервалом. Нужны для позиционирования каретки и
	/// многострочной раскладки.
	/// </summary>
	public static (float Ascent, float Descent, float LineHeight) GetMetrics(float size, bool bold = false)
	{
		var key = (size, bold);

		if (_metricsCache.TryGetValue(key, out var cached))
			return cached;

		using var font = CreateFont(size, bold);
		var m = font.Metrics;

		// Ascent в Skia отрицательный (вверх от базовой линии), приводим к
		// положительному расстоянию.
		var result = (-m.Ascent, m.Descent, font.Spacing);

		_metricsCache[key] = result;
		return result;
	}

}