using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public sealed class StrokePatternInfo
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Pattern { get; set; } = string.Empty; // "5 3" style, как shape.Stroke.Pattern
	public int Length { get; set; } = 8;                // ширина сетки (кол-во ячеек)
	public bool IsBuiltIn { get; set; }
	public SKBitmap? PreviewBitmap { get; set; }
}

public static class StrokePatternLibrary
{
	public static readonly List<StrokePatternInfo> Items = new();

	static StrokePatternLibrary()
	{
		AddBuiltIn("solid", "Solid", "", 8);
		AddBuiltIn("dash", "Dash", "5 3", 8);
		AddBuiltIn("dot", "Dot", "1 2", 8);
		AddBuiltIn("dashdot", "Dash Dot", "5 2 1 2", 10);
	}

	private static void AddBuiltIn(string id, string name, string pattern, int length)
	{
		var info = new StrokePatternInfo
		{
			Id = id,
			DisplayName = name,
			Pattern = pattern,
			Length = length,
			IsBuiltIn = true
		};
		info.PreviewBitmap = BuildPreviewBitmap(pattern, length);
		Items.Add(info);
	}

	public static StrokePatternInfo? Find(string id)
	{
		return Items.FirstOrDefault(x => x.Id == id);
	}

	public static StrokePatternInfo? FindByPattern(string pattern)
	{
		string norm = NormalizePattern(pattern);
		return Items.FirstOrDefault(x => NormalizePattern(x.Pattern) == norm);
	}

	// Возвращает true, если такой паттерн (по значению) уже есть в списке
	public static bool ContainsPattern(string pattern)
	{
		return FindByPattern(pattern) != null;
	}

	public static StrokePatternInfo AddCustomPattern(string pattern, int length)
	{
		var existing = FindByPattern(pattern);
		if (existing != null)
			return existing;

		int index = Items.Count(x => !x.IsBuiltIn) + 1;
		var info = new StrokePatternInfo
		{
			Id = $"custom_{System.Guid.NewGuid():N}",
			DisplayName = $"Custom {index}",
			Pattern = pattern,
			Length = length,
			IsBuiltIn = false,
			PreviewBitmap = BuildPreviewBitmap(pattern, length)
		};

		Items.Add(info);
		return info;
	}

	public static bool RemovePattern(string id)
	{
		var info = Find(id);
		if (info == null || info.IsBuiltIn)
			return false;

		info.PreviewBitmap?.Dispose();
		Items.Remove(info);
		return true;
	}

	public static string NormalizePattern(string pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
			return "";

		var parts = pattern
			.Split(new[] { ' ', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
			.Select(p => float.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f)
			.ToArray();

		return string.Join(" ", parts.Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
	}

	// Строит превью паттерна как SKBitmap — крупная, отчётливая линия
	public static SKBitmap BuildPreviewBitmap(string pattern, int lengthCells, int width = 120, int height = 28)
	{
		var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.Transparent);

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 4f,
			Color = SKColors.Black,
			StrokeCap = SKStrokeCap.Butt
		};

		float y = height / 2f;
		float margin = 8f;
		float lineWidth = width - margin * 2f;

		if (!string.IsNullOrWhiteSpace(pattern))
		{
			var intervals = pattern
				.Split(new[] { ' ', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
				.Select(p => float.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f)
				.ToArray();

			if (intervals.Length >= 2 && intervals.Length % 2 == 0 && intervals.Sum() > 0)
			{
				float cellPx = lineWidth / MathF.Max(1, lengthCells);
				float scale = MathF.Max(0.5f, cellPx); // масштабируем визуально, чтобы паттерн был отчётлив

				paint.PathEffect = SKPathEffect.CreateDash(
					intervals.Select(v => v * scale).ToArray(),
					0f);
			}
		}

		canvas.DrawLine(margin, y, margin + lineWidth, y, paint);
		return bmp;
	}
}