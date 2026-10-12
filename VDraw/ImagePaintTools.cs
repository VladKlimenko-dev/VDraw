using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing.Images;

public enum ImageStrokeMode
{
	Paint,
	Erase,
	Contrast,
	Lighten,
	Darken,
	BW
}

internal enum ImageTransformKind
{
	FlipHorizontal,
	FlipVertical,
	Rotate90CW,
	Rotate90CCW
}

public sealed class ActiveImageStrokeSession : IDisposable
{
	public ImageStrokeMode Mode { get; }
	public SKColor Color { get; }
	/// <summary>Радиус кисти вдоль оси X растра, в пикселях.</summary>
	public float RadiusPx { get; }

	/// <summary>
	/// Во сколько раз пиксель растра «выше» своей ширины на листе.
	///
	/// TryWorldToBitmapPixel переводит мир в пиксели независимыми
	/// множителями по X и по Y, поэтому у растянутой картинки пиксельное
	/// пространство неравномерно. Круг, нарисованный в нём, на экране
	/// выглядит овалом — именно это и происходило с кистью. Отношение
	/// хранится в сессии и применяется преобразованием холста.
	/// </summary>
	public float PixelAspect { get; }

	/// <summary>Радиус вдоль оси Y растра, в пикселях.</summary>
	public float RadiusPxY => RadiusPx * PixelAspect;
	public float Opacity { get; }
	public float SmoothPercent { get; }
	public ImageToolShapeType ShapeType { get; }
	public float Strength { get; }

	public ImagePixelSelection? LimitSelection { get; }

	public Dictionary<ImageTileKey, SKBitmap> BaseTiles { get; } = new();
	public Dictionary<ImageTileKey, SKBitmap> MaskTiles { get; } = new();
	public Dictionary<ImageTileKey, SKBitmap> PreviewTiles { get; } = new();
	public Dictionary<ImageTileKey, SKRectI> DirtyRects { get; } = new();

	public ActiveImageStrokeSession(
		ImageStrokeMode mode,
		SKColor color,
		float radiusPx,
		float opacity,
		float smoothPercent,
		ImageToolShapeType shapeType,
		float strength = 0f,
		ImagePixelSelection? limitSelection = null,
		float pixelAspect = 1f)
	{
		PixelAspect = pixelAspect > 0.0001f ? pixelAspect : 1f;
		Mode = mode;
		Color = color;
		RadiusPx = MathF.Max(0.5f, radiusPx);
		Opacity = Math.Clamp(opacity, 0f, 1f);
		SmoothPercent = Math.Clamp(smoothPercent, 0f, 100f);
		ShapeType = shapeType;
		Strength = Math.Clamp(strength, -1f, 1f);

		if (limitSelection != null &&
			limitSelection.HasSelection &&
			limitSelection.Mask != null)
		{
			LimitSelection = new ImagePixelSelection(
				limitSelection.PathPixels,
				limitSelection.Bounds,
				limitSelection.Mask.Copy());
		}
	}

	public void AddDirtyRect(ImageTileKey key, SKRectI rect)
	{
		if (rect.IsEmpty)
			return;

		if (!DirtyRects.TryGetValue(key, out var existing))
		{
			DirtyRects[key] = rect;
			return;
		}

		DirtyRects[key] = new SKRectI(
			Math.Min(existing.Left, rect.Left),
			Math.Min(existing.Top, rect.Top),
			Math.Max(existing.Right, rect.Right),
			Math.Max(existing.Bottom, rect.Bottom));
	}

	public void Dispose()
	{
		foreach (var bitmap in BaseTiles.Values)
			bitmap.Dispose();

		foreach (var bitmap in MaskTiles.Values)
			bitmap.Dispose();

		foreach (var bitmap in PreviewTiles.Values)
			bitmap.Dispose();

		BaseTiles.Clear();
		MaskTiles.Clear();
		PreviewTiles.Clear();
		DirtyRects.Clear();
		LimitSelection?.Dispose();
	}
}

public enum ImageToolShapeType
{
	Circle = 0,
	Square = 1
}

internal static class ImageToolSettings
{
	public static SKColor GetPaintColor(MouseButtons button)
		=> button == MouseButtons.Right
			? Settings.image_paint_back_color
			: Settings.image_paint_fore_color;

	public static ImageToolShapeType ShapeType
		=> Settings.image_tool_shape;

	public static float SmoothPercent
		=> Settings.image_tool_smooth;

	public static float Strength
		=> Settings.image_tool_strength;

	public static float Tolerance
		=> Settings.image_tool_tolerance;

	/// <summary>
	/// Заданный размер инструмента. В каких единицах он понимается —
	/// в экранных или в пикселях растра — решает SizeLock.
	/// </summary>
	public static float Size
		=> Math.Max(1f, Settings.image_tool_size);

	public static bool SizeLock
		=> Settings.image_tool_size_lock;
}

public enum BrushStampShape
{
	SoftRound,
	HardRound,
	SoftSquare,
	HardSquare,
	Diagonal45,
	Diagonal135,
	Chalk,
	Speckle
}

internal static class ImagePaintEngine
{
	public static ImageShape? GetEditableImage(MainView view, EditorInteractionContext ctx)
	{
		if (view == null || ctx == null)
			return null;

		if (ctx.Selection.Count != 1)
			return null;

		if (ctx.Selection[0] is not ImageShape image)
			return null;

		if (!image.IsVisible || image.IsLocked)
			return null;

		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return null;

		image.EnsureTileDocument();
		return image;
	}

	public static bool TryWorldToBitmapPixel(ImageShape image, SKPoint worldPoint, out SKPoint px)
	{
		px = SKPoint.Empty;

		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return false;

		var local = image.WorldToLocal(worldPoint);
		var r = image.ImageLocalRect;

		if (r.Width <= 0 || r.Height <= 0)
			return false;

		if (local.X < r.Left || local.X > r.Right || local.Y < r.Top || local.Y > r.Bottom)
			return false;

		float u = (local.X - r.Left) / r.Width;
		float v = (local.Y - r.Top) / r.Height;

		px = new SKPoint(
			u * (image.Bitmap.Width - 1),
			v * (image.Bitmap.Height - 1));

		return true;
	}

	public static float UomToBitmapRadiusPx(ImageShape image, float sizeUom)
	{
		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return 0f;

		var r = image.ImageLocalRect;
		if (r.Width <= 0 || r.Height <= 0)
			return 0f;

		float scaleX = image.Bitmap.Width / r.Width;
		float scaleY = image.Bitmap.Height / r.Height;
		float avg = (scaleX + scaleY) * 0.5f;

		return MathF.Max(0.5f, sizeUom * 0.5f * avg);
	}

	public static void StampLine(
		ImageShape image,
		SKPoint from,
		SKPoint to,
		float radiusPx,
		float spacingPx,
		Action<ImageShape, SKPoint, float> stampAction)
	{
		float dx = to.X - from.X;
		float dy = to.Y - from.Y;
		float dist = MathF.Sqrt(dx * dx + dy * dy);

		if (dist < 0.001f)
		{
			stampAction(image, from, radiusPx);
			return;
		}

		int steps = Math.Max(1, (int)MathF.Ceiling(dist / MathF.Max(0.5f, spacingPx)));

		for (int i = 0; i <= steps; i++)
		{
			float t = i / (float)steps;
			var p = new SKPoint(from.X + dx * t, from.Y + dy * t);
			stampAction(image, p, radiusPx);
		}
	}

	public static void ApplyEraser(
		ImageShape image,
		SKPoint center,
		float radiusPx,
		ImageToolShapeType shapeType,
		float smoothPercent)
	{
		if (!image.EnsureTileDocument() || image.tileDoc == null)
			return;

		ProcessStampOnTiles(
			image,
			center,
			radiusPx,
			(tileBitmap, localCenter) =>
				ProcessStamp(
					tileBitmap,
					localCenter,
					radiusPx,
					shapeType,
					smoothPercent,
					(dst, influence) =>
					{
						byte newA = (byte)Math.Clamp(dst.Alpha * (1f - influence), 0f, 255f);
						return new SKColor(dst.Red, dst.Green, dst.Blue, newA);
					}));
	}

	public static void ApplyBrush(
		ImageShape image,
		SKPoint center,
		float radiusPx,
		BrushStampShape brushShape,
		float smoothPercent,
		SKColor color,
		float opacity)
	{
		if (!image.EnsureTileDocument() || image.tileDoc == null)
			return;

		var brushColor = color.WithAlpha((byte)(255f * Math.Clamp(opacity, 0f, 1f)));

		ProcessStampOnTiles(
			image,
			center,
			radiusPx,
			(tileBitmap, localCenter) =>
				ProcessBrushStamp(
					tileBitmap,
					localCenter,
					radiusPx,
					brushShape,
					smoothPercent,
					brushColor,
					(dst, src, influence) => Lerp(dst, src, influence)));
	}

	private static void ProcessStampOnTiles(
		ImageShape image,
		SKPoint center,
		float radiusPx,
		Action<SKBitmap, SKPoint> tileAction)
	{
		var doc = image.tileDoc;
		if (doc == null)
			return;

		var affectedRect = SKRect.Create(
			center.X - radiusPx - 1f,
			center.Y - radiusPx - 1f,
			radiusPx * 2f + 2f,
			radiusPx * 2f + 2f);

		foreach (var tile in doc.GetTilesIntersecting(affectedRect))
		{
			var tileBounds = doc.GetTileBounds(tile.TileX, tile.TileY);
			var localCenter = new SKPoint(center.X - tileBounds.Left, center.Y - tileBounds.Top);

			tileAction(tile.Bitmap, localCenter);
			tile.InvalidatePreview();
		}

		image.MarkTilesDirty();
	}

	private static void ProcessStamp(
		SKBitmap bitmap,
		SKPoint center,
		float radiusPx,
		ImageToolShapeType shapeType,
		float smoothPercent,
		Func<SKColor, float, SKColor> blend)
	{
		int left = Math.Max(0, (int)MathF.Floor(center.X - radiusPx - 1));
		int top = Math.Max(0, (int)MathF.Floor(center.Y - radiusPx - 1));
		int right = Math.Min(bitmap.Width - 1, (int)MathF.Ceiling(center.X + radiusPx + 1));
		int bottom = Math.Min(bitmap.Height - 1, (int)MathF.Ceiling(center.Y + radiusPx + 1));

		for (int y = top; y <= bottom; y++)
		{
			for (int x = left; x <= right; x++)
			{
				float influence = ComputeShapeInfluence(
					x + 0.5f,
					y + 0.5f,
					center,
					radiusPx,
					shapeType,
					smoothPercent);

				if (influence <= 0f)
					continue;

				var dst = bitmap.GetPixel(x, y);
				bitmap.SetPixel(x, y, blend(dst, influence));
			}
		}
	}

	private static void ProcessBrushStamp(
		SKBitmap bitmap,
		SKPoint center,
		float radiusPx,
		BrushStampShape brushShape,
		float smoothPercent,
		SKColor brushColor,
		Func<SKColor, SKColor, float, SKColor> blend)
	{
		int left = Math.Max(0, (int)MathF.Floor(center.X - radiusPx - 1));
		int top = Math.Max(0, (int)MathF.Floor(center.Y - radiusPx - 1));
		int right = Math.Min(bitmap.Width - 1, (int)MathF.Ceiling(center.X + radiusPx + 1));
		int bottom = Math.Min(bitmap.Height - 1, (int)MathF.Ceiling(center.Y + radiusPx + 1));

		for (int y = top; y <= bottom; y++)
		{
			for (int x = left; x <= right; x++)
			{
				float influence = ComputeBrushInfluence(
					x + 0.5f,
					y + 0.5f,
					center,
					radiusPx,
					brushShape,
					smoothPercent);

				if (influence <= 0f)
					continue;

				var dst = bitmap.GetPixel(x, y);
				bitmap.SetPixel(x, y, blend(dst, brushColor, influence * (brushColor.Alpha / 255f)));
			}
		}
	}

	private static float ComputeShapeInfluence(
		float px,
		float py,
		SKPoint center,
		float radiusPx,
		ImageToolShapeType shapeType,
		float smoothPercent)
	{
		float dx = px - center.X;
		float dy = py - center.Y;

		float distanceNorm = shapeType switch
		{
			ImageToolShapeType.Circle => MathF.Sqrt(dx * dx + dy * dy) / MathF.Max(radiusPx, 0.0001f),
			ImageToolShapeType.Square => MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / MathF.Max(radiusPx, 0.0001f),
			_ => 2f
		};

		return ComputeFalloff(distanceNorm, smoothPercent);
	}

	private static float ComputeBrushInfluence(
		float px,
		float py,
		SKPoint center,
		float radiusPx,
		BrushStampShape brushShape,
		float smoothPercent)
	{
		float dx = px - center.X;
		float dy = py - center.Y;

		float influence = brushShape switch
		{
			BrushStampShape.HardRound => ComputeFalloff(MathF.Sqrt(dx * dx + dy * dy) / radiusPx, 0f),
			BrushStampShape.SoftRound => ComputeFalloff(MathF.Sqrt(dx * dx + dy * dy) / radiusPx, smoothPercent),
			BrushStampShape.HardSquare => ComputeFalloff(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / radiusPx, 0f),
			BrushStampShape.SoftSquare => ComputeFalloff(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / radiusPx, smoothPercent),
			BrushStampShape.Diagonal45 => ComputeDiagonalInfluence(dx, dy, radiusPx, smoothPercent, false),
			BrushStampShape.Diagonal135 => ComputeDiagonalInfluence(dx, dy, radiusPx, smoothPercent, true),
			BrushStampShape.Chalk => ComputeNoiseInfluence(dx, dy, radiusPx, smoothPercent, 17),
			BrushStampShape.Speckle => ComputeNoiseInfluence(dx, dy, radiusPx, smoothPercent, 41),
			_ => 0f
		};

		return Math.Clamp(influence, 0f, 1f);
	}

	private static float ComputeDiagonalInfluence(
		float dx,
		float dy,
		float radiusPx,
		float smoothPercent,
		bool invert)
	{
		float u = invert ? (dx - dy) : (dx + dy);
		float v = invert ? (dx + dy) : (dx - dy);

		float major = MathF.Abs(u) / MathF.Max(radiusPx, 0.0001f);
		float minor = MathF.Abs(v) / MathF.Max(radiusPx * 0.35f, 0.0001f);
		float d = MathF.Max(major, minor);

		return ComputeFalloff(d, smoothPercent);
	}

	private static float ComputeNoiseInfluence(
		float dx,
		float dy,
		float radiusPx,
		float smoothPercent,
		int seed)
	{
		float d = MathF.Sqrt(dx * dx + dy * dy) / MathF.Max(radiusPx, 0.0001f);
		float baseValue = ComputeFalloff(d, smoothPercent);
		if (baseValue <= 0f)
			return 0f;

		int hx = (int)MathF.Floor(dx + 2048);
		int hy = (int)MathF.Floor(dy + 2048);
		uint h = (uint)(hx * 73856093) ^ (uint)(hy * 19349663) ^ (uint)seed;
		float noise = (h & 1023) / 1023f;

		float mask = seed == 17
			? 0.65f + noise * 0.35f
			: (noise > 0.55f ? 1f : 0.15f);

		return baseValue * mask;
	}

	private static float ComputeFalloff(float distanceNorm, float smoothPercent)
	{
		if (distanceNorm >= 1f)
			return 0f;

		float s = Math.Clamp(smoothPercent / 100f, 0f, 1f);

		if (s <= 0.0001f)
			return 1f;

		float inner = 1f - s;

		if (distanceNorm <= inner)
			return 1f;

		float t = (distanceNorm - inner) / MathF.Max(0.0001f, 1f - inner);
		t = Math.Clamp(t, 0f, 1f);

		return 1f - t;
	}

	private static SKColor Lerp(SKColor a, SKColor b, float t)
	{
		t = Math.Clamp(t, 0f, 1f);

		byte r = (byte)(a.Red + (b.Red - a.Red) * t);
		byte g = (byte)(a.Green + (b.Green - a.Green) * t);
		byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
		byte al = (byte)(a.Alpha + (b.Alpha - a.Alpha) * t);

		return new SKColor(r, g, bl, al);
	}

	public static void ExitImageEditing(
		MainView view,
		ImageEditSession session,
		ImageShape? image,
		bool commitChanges)
	{
		if (image != null)
		{
			if (commitChanges)
			{
				if (image.ActiveStroke != null)
				{
					CommitImageStroke(image);
				}

				CommitFloatingSelection(session, image);
				image.RebuildBitmapFromTiles();
				image.MarkTilesDirty();
			}
			else
			{
				CancelImageStroke(image);
			}
		}

		session.ClearSelectionAndFloating();
		session.ActiveKind = ImageEditToolKind.None;
		session.mainForm._mainView.ExitImageEditTool();

		if (view.CurrentPage != null && commitChanges)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
	}

	private static void CommitImageStroke(ImageShape image)
	{
		var doc = image.tileDoc;
		var active = image.ActiveStroke;

		if (doc == null || active == null)
			return;

		foreach (var pair in active.DirtyRects)
		{
			var key = pair.Key;

			if (!active.PreviewTiles.TryGetValue(key, out var preview))
				continue;

			var tile = doc.GetOrCreateTile(key.X, key.Y);

			using var canvas = new SKCanvas(tile.Bitmap);
			using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
			var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

			canvas.DrawBitmap(preview, 0f, 0f, sampling, paint);
			tile.InvalidatePreview();
		}

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();

		image.ActiveStroke.Dispose();
		image.ActiveStroke = null;
	}

	private static void CancelImageStroke(ImageShape image)
	{
		image.ActiveStroke?.Dispose();
		image.ActiveStroke = null;
	}

	private static void CommitFloatingSelection(ImageEditSession session, ImageShape image)
	{
		var floating = session.FloatingSelection;
		if (floating == null || image.tileDoc == null)
			return;

		int imageWidth = image.tileDoc.Width;
		int imageHeight = image.tileDoc.Height;
		int tileSize = image.tileDoc.TileSize;

		var touchedTiles = new HashSet<ImageTile>();

		for (int y = 0; y < floating.Height; y++)
		{
			int dstY = floating.Position.Y + y;
			if (dstY < 0 || dstY >= imageHeight)
				continue;

			for (int x = 0; x < floating.Width; x++)
			{
				int dstX = floating.Position.X + x;
				if (dstX < 0 || dstX >= imageWidth)
					continue;

				// По маске, а не по альфе: прозрачный пиксель внутри
				// фрагмента обязан затереть то, что под ним, иначе
				// на новом месте остаётся старое изображение.
				if (!floating.IsInside(x, y))
					continue;

				var src = floating.Bitmap.GetPixel(x, y);

				int tileX = dstX / tileSize;
				int tileY = dstY / tileSize;

				var tile = image.tileDoc.GetOrCreateTile(tileX, tileY);
				int localX = dstX - tileX * tileSize;
				int localY = dstY - tileY * tileSize;

				tile.Bitmap.SetPixel(localX, localY, src);
				touchedTiles.Add(tile);
			}
		}

		foreach (var tile in touchedTiles)
			tile.InvalidatePreview();

		session.ClearFloatingSelection();
	}

	public static void ApplyContrast(
		ImageShape image,
		SKPoint center,
		float radiusPx,
		ImageToolShapeType shapeType,
		float smoothPercent,
		float strength)
	{
		if (!image.EnsureTileDocument() || image.tileDoc == null)
			return;

		strength = Math.Clamp(strength, -1f, 1f);

		ProcessStampOnTiles(
			image,
			center,
			radiusPx,
			(tileBitmap, localCenter) =>
			ProcessStamp(
				tileBitmap,
				localCenter,
				radiusPx,
				shapeType,
				smoothPercent,
				(dst, influence) => ApplyContrastToColor(dst, strength * influence)));
	}

	private static SKColor ApplyContrastToColor(SKColor color, float amount)
	{
		if (color.Alpha == 0)
			return color;

		float a = color.Alpha / 255f;

		float r = color.Red / 255f;
		float g = color.Green / 255f;
		float b = color.Blue / 255f;

		float factor = 1f + amount;

		r = ((r - 0.5f) * factor) + 0.5f;
		g = ((g - 0.5f) * factor) + 0.5f;
		b = ((b - 0.5f) * factor) + 0.5f;

		r = Math.Clamp(r, 0f, 1f);
		g = Math.Clamp(g, 0f, 1f);
		b = Math.Clamp(b, 0f, 1f);

		return new SKColor(
			(byte)MathF.Round(r * 255f),
			(byte)MathF.Round(g * 255f),
			(byte)MathF.Round(b * 255f),
			(byte)MathF.Round(a * 255f));
	}

	/// <summary>
	/// Радиус инструмента в пикселях растра.
	///
	/// Единственная точка, где решается, что означает введённый размер.
	/// Через неё же считает и рисование, и кружок предпросмотра под
	/// курсором, поэтому они не могут разойтись.
	///
	/// При выключенном локе пересчёта нет вовсе: размер УЖЕ задан
	/// в пикселях изображения, и «5» означает пять его пикселей при любом
	/// масштабе просмотра. При включённом — размер экранный, и его
	/// переводят в пиксели растра по текущему зуму.
	/// </summary>
	public static float ToolRadiusBitmapPx(MainView view, ImageShape image)
	{
		if (!ImageToolSettings.SizeLock)
			return MathF.Max(0.5f, ImageToolSettings.Size * 0.5f);

		return ScreenRadiusToBitmapRadiusPx(view, image, ImageToolSettings.Size);
	}

	public static float ScreenRadiusToBitmapRadiusPx(MainView view, ImageShape image, float screenSizePx)
	{
		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return 0f;

		var rect = image.ImageLocalRect;
		if (rect.Width <= 0 || rect.Height <= 0)
			return 0f;

		float screenRadius = Math.Max(0.5f, screenSizePx * 0.5f);

		var p0Screen = view.PageToScreen(image.LocalToWorld(new SKPoint(rect.Left, rect.Top)));
		var p1Screen = view.PageToScreen(image.LocalToWorld(new SKPoint(rect.Right, rect.Top)));
		var p2Screen = view.PageToScreen(image.LocalToWorld(new SKPoint(rect.Left, rect.Bottom)));

		float screenWidth = MathF.Max(1f, MathF.Abs(p1Screen.X - p0Screen.X));
		float screenHeight = MathF.Max(1f, MathF.Abs(p2Screen.Y - p0Screen.Y));

		float bitmapPerScreenX = image.Bitmap.Width / screenWidth;

		// Раньше здесь бралось среднее из множителей по X и по Y. Среднее
		// прятало неравномерность, но не убирало её: кисть всё равно
		// выходила овальной, просто овал был меньше. Теперь возвращается
		// честный радиус вдоль X, а неравномерность обрабатывает
		// PixelAspect при отрисовке мазка.
		return MathF.Max(0.5f, screenRadius * bitmapPerScreenX);
	}

	/// <summary>
	/// Во сколько раз пиксель растра выше своей ширины в координатах листа.
	/// Единица — квадратный пиксель, больше единицы — растянутая по
	/// горизонтали картинка.
	/// </summary>
	public static float GetPixelAspect(ImageShape image)
	{
		if (!image.EnsureBitmapLoaded() || image.Bitmap == null)
			return 1f;

		var r = image.ImageLocalRect;

		if (r.Width <= 0f || r.Height <= 0f || image.Bitmap.Width <= 0 || image.Bitmap.Height <= 0)
			return 1f;

		float scaleX = image.Bitmap.Width / r.Width;
		float scaleY = image.Bitmap.Height / r.Height;

		if (scaleX <= 0.000001f)
			return 1f;

		return scaleY / scaleX;
	}
}

public sealed class ImagePixelSelection : IDisposable
{
	private SKPath _pathPixels;
	private SKRectI _bounds;
	private SKBitmap? _mask;

	public SKPath PathPixels => _pathPixels;
	public SKRectI Bounds => _bounds;
	public SKBitmap? Mask => _mask;

	public bool HasSelection => !_bounds.IsEmpty && _pathPixels != null && !_pathPixels.IsEmpty;

	public ImagePixelSelection()
	{
		_pathPixels = new SKPath();
		_bounds = SKRectI.Empty;
		_mask = null;
	}

	public ImagePixelSelection(SKPath pathPixels, SKRectI bounds, SKBitmap mask)
	{
		_pathPixels = new SKPath(pathPixels);
		_bounds = bounds;
		_mask = mask;
	}

	public void Clear()
	{
		_pathPixels?.Dispose();
		_pathPixels = new SKPath();

		_mask?.Dispose();
		_mask = null;

		_bounds = SKRectI.Empty;
	}

	public void Set(SKPath pathPixels, SKRectI bounds, SKBitmap mask)
	{
		_pathPixels?.Dispose();
		_pathPixels = new SKPath(pathPixels);

		_mask?.Dispose();
		_mask = mask;

		_bounds = bounds;
	}

	// Совместимость со старым кодом, который зовёт SetRect(...)
	public void SetRect(SKRectI rect, int imageWidth, int imageHeight)
	{
		Clear();

		if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
			return;

		var clipped = SKRectI.Intersect(rect, new SKRectI(0, 0, imageWidth, imageHeight));
		if (clipped.IsEmpty || clipped.Width <= 0 || clipped.Height <= 0)
			return;

		_bounds = clipped;

		_pathPixels.Dispose();
		_pathPixels = new SKPath();
		_pathPixels.AddRect(SKRect.Create(clipped.Left, clipped.Top, clipped.Width, clipped.Height));

		_mask = new SKBitmap(clipped.Width, clipped.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

		using var canvas = new SKCanvas(_mask);
		using var fill = new SKPaint
		{
			Style = SKPaintStyle.Fill,
			Color = SKColors.White,
			BlendMode = SKBlendMode.Src,
			IsAntialias = false
		};

		canvas.Clear(SKColors.Transparent);
		canvas.DrawRect(SKRect.Create(0, 0, clipped.Width, clipped.Height), fill);
	}

	public bool Contains(int x, int y) => ContainsPixel(x, y);

	public bool ContainsPixel(int x, int y)
	{
		if (_mask == null || _bounds.IsEmpty)
			return false;

		if (x < _bounds.Left || x >= _bounds.Right || y < _bounds.Top || y >= _bounds.Bottom)
			return false;

		int lx = x - _bounds.Left;
		int ly = y - _bounds.Top;

		if (lx < 0 || ly < 0 || lx >= _mask.Width || ly >= _mask.Height)
			return false;

		return _mask.GetPixel(lx, ly).Alpha > 0;
	}

	public void Dispose()
	{
		_pathPixels?.Dispose();
		_mask?.Dispose();
	}
}

public sealed class FloatingPixelSelection : IDisposable
{
	public SKBitmap Bitmap { get; set; }
	public SKPointI Position { get; set; }

	/// <summary>
	/// Какие пиксели прямоугольника принадлежат фрагменту. null означает
	/// «все», то есть прямоугольное выделение.
	///
	/// Нужна отдельно от альфы цвета, потому что это РАЗНЫЕ вещи.
	/// Прозрачный пиксель внутри фрагмента обязан затирать то, что лежит
	/// под ним на новом месте: пользователь стёр этот кусок картинки
	/// и перетащил дырку вместе с фрагментом. Пиксель ВНЕ фрагмента —
	/// угол прямоугольника за границей лассо — трогать ничего не должен.
	/// По одной альфе эти два случая неразличимы, и впечатывание,
	/// пропускавшее всё прозрачное, оставляло старое изображение
	/// на новом месте.
	/// </summary>
	public SKBitmap? Mask { get; set; }

	public int Width => Bitmap.Width;
	public int Height => Bitmap.Height;

	public SKRectI Bounds => new(
		Position.X,
		Position.Y,
		Position.X + Width,
		Position.Y + Height);

	public FloatingPixelSelection(SKBitmap bitmap, SKPointI position, SKBitmap? mask = null)
	{
		Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
		Position = position;
		Mask = mask;
	}

	/// <summary>Принадлежит ли пиксель фрагменту.</summary>
	public bool IsInside(int x, int y)
	{
		if (Mask == null)
			return true;

		if (x < 0 || y < 0 || x >= Mask.Width || y >= Mask.Height)
			return false;

		return Mask.GetPixel(x, y).Alpha > 0;
	}

	public void MoveTo(SKPointI position)
	{
		Position = position;
	}

	public void Dispose()
	{
		Bitmap.Dispose();
		Mask?.Dispose();
	}
}

/// <summary>
/// Сессия растрового редактирования.
///
/// Добавлена запись истории через области (scope). Ключевое решение: снимок
/// снимается не в местах мутации, а на открытии и закрытии области. Поэтому
/// инструментам не важно, меняли они выделение через методы сессии
/// (ClearSelectionAndFloating, SetFloatingSelection) или напрямую через
/// Selection.SetRect / Selection.Clear — оба пути покрыты одинаково.
///
/// Заменяет класс ImageEditSession в ImagePaintTools.cs.
/// </summary>
public sealed class ImageEditSession : IDisposable
{
	public MainForm mainForm;
	public ImageEditToolKind ActiveKind { get; set; } = ImageEditToolKind.None;

	// Единый источник истины для raster selection.
	public ImagePixelSelection Selection { get; } = new();

	public FloatingPixelSelection? FloatingSelection { get; private set; }

	public SKBitmap? ClipboardBitmap { get; private set; }

	public bool HasSelection => Selection.HasSelection;
	public bool HasFloatingSelection => FloatingSelection != null;
	public ImageSelectionOverlayStyle OverlayStyle { get; } = new();

	/// <summary>
	/// История, в которую пишутся изменения выделения. Ставится один раз —
	/// например в MainView.SetImageEditTool при создании сессии.
	/// Если null, запись просто не ведётся.
	/// </summary>
	public UndoStack? History { get; set; }

	private ImageSelectionAction? _pendingAction;
	private int _scopeDepth;

	public ImageEditSession(MainForm _mainForm)
	{
		mainForm = _mainForm;
	}

	#region История выделения

	/// <summary>
	/// Открывает область записи. Всё, что изменит выделение до закрытия области,
	/// станет одним шагом истории. Вложенные области допускаются: шаг пишет
	/// только самая внешняя.
	///
	/// Использование:
	///     using var _ = _session.BeginSelectionHistory("Rect select");
	/// либо, когда начало и конец в разных методах, — BeginSelectionHistoryManual
	/// и EndSelectionHistory.
	/// </summary>
	public SelectionHistoryScope BeginSelectionHistory(string name)
	{
		BeginSelectionHistoryManual(name);
		return new SelectionHistoryScope(this);
	}

	/// <summary>
	/// Открыть область без using. Нужно для инструментов, где начало приходится
	/// на OnMouseDown, а конец — на OnMouseUp.
	/// </summary>
	public void BeginSelectionHistoryManual(string name)
	{
		_scopeDepth++;

		if (_scopeDepth > 1)
			return;

		if (History == null || History.IsApplying)
		{
			_pendingAction = null;
			return;
		}

		_pendingAction = ImageSelectionAction.Capture(this);
	}

	/// <summary>Закрыть область и записать шаг, если выделение изменилось.</summary>
	public void EndSelectionHistory()
	{
		if (_scopeDepth > 0)
			_scopeDepth--;

		if (_scopeDepth > 0)
			return;

		var action = _pendingAction;
		_pendingAction = null;

		if (action == null)
			return;

		action.CaptureAfter();

		if (action.HasChanges())
			History?.Push(action);
		else
			action.Discard();
	}

	/// <summary>Отбросить открытую область: изменение отменено.</summary>
	public void AbortSelectionHistory()
	{
		_scopeDepth = 0;
		_pendingAction?.Discard();
		_pendingAction = null;
	}

	/// <summary>true, если область записи уже открыта.</summary>
	public bool IsRecordingSelection => _scopeDepth > 0;

	#endregion

	public void SetSelection(ImagePixelSelection? selection)
	{
		ClearFloatingSelection();

		if (selection == null || !selection.HasSelection || selection.Mask == null)
		{
			Selection.Clear();
			return;
		}

		Selection.Set(
			selection.PathPixels,
			selection.Bounds,
			selection.Mask.Copy());
	}

	public void ClearSelection()
	{
		Selection.Clear();
	}

	public void ClearSelectionAndFloating()
	{
		Selection.Clear();
		ClearFloatingSelection();
	}

	public void SetFloatingSelection(FloatingPixelSelection? floating)
	{
		if (ReferenceEquals(FloatingSelection, floating))
			return;

		FloatingSelection?.Dispose();
		FloatingSelection = floating;
	}

	public void ClearFloatingSelection()
	{
		FloatingSelection?.Dispose();
		FloatingSelection = null;
	}

	public void SetClipboardBitmap(SKBitmap? bitmap)
	{
		ClipboardBitmap?.Dispose();
		ClipboardBitmap = bitmap;
	}

	public void ClearClipboard()
	{
		ClipboardBitmap?.Dispose();
		ClipboardBitmap = null;
	}

	public void Dispose()
	{
		AbortSelectionHistory();
		ClearFloatingSelection();
		ClearClipboard();
		Selection.Clear();
	}
}

/// <summary>
/// Область записи изменений выделения. Закрывается через using.
/// Struct, чтобы не давать нагрузки на сборщик мусора при движении мыши.
/// </summary>
public readonly struct SelectionHistoryScope : IDisposable
{
	private readonly ImageEditSession? _session;

	internal SelectionHistoryScope(ImageEditSession session)
	{
		_session = session;
	}

	public void Dispose() => _session?.EndSelectionHistory();
}

// =====================================================================================
// Дополнение к ImageSelectionAction (файл UndoActions.cs).
//
// Области записи проверяют, изменилось ли выделение, чтобы не плодить пустые
// шаги при простом клике. Добавьте в класс ImageSelectionAction:
//
//	public bool HasChanges()
//	{
//		if (_after == null)
//			return false;
//
//		if (_before.Bounds != _after.Bounds)
//			return true;
//
//		bool maskBefore = _before.Mask != null;
//		bool maskAfter = _after.Mask != null;
//		if (maskBefore != maskAfter)
//			return true;
//
//		bool floatBefore = _before.FloatingBitmap != null;
//		bool floatAfter = _after.FloatingBitmap != null;
//		if (floatBefore != floatAfter)
//			return true;
//
//		if (floatBefore && _before.FloatingPosition != _after.FloatingPosition)
//			return true;
//
//		// Маски одного размера при одинаковых границах считаем совпавшими:
//		// побайтовое сравнение на каждое движение мыши обошлось бы дороже
//		// лишнего шага истории.
//		return false;
//	}
// =====================================================================================

public abstract class ImagePaintToolBase : IEditorTool
{
	protected readonly ImageEditSession _session;

	private bool isPainting;
	private ImageShape? activeImage;
	private SKPoint lastPixel;
	private MouseButtons _activeButton = MouseButtons.None;
	protected virtual string ToolHistoryName => GetStrokeMode().ToString();

	protected ImagePaintToolBase(ImageEditSession session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public abstract ShapeToolKind Kind { get; }

	protected virtual float GetRadiusBitmapPx(MainView view, ImageShape image)
		=> ImagePaintEngine.ToolRadiusBitmapPx(view, image);
	protected virtual ImageToolShapeType GetShapeType()
		=> ImageToolSettings.ShapeType;

	protected abstract ImageStrokeMode GetStrokeMode();

	protected virtual SKColor GetStrokeColor(MouseButtons button)
		=> ImageToolSettings.GetPaintColor(button);

	protected abstract float GetStrokeOpacity(MouseButtons button, SKColor color);

	protected virtual float GetStrokeSmoothPercent()
		=> ImageToolSettings.SmoothPercent;

	protected virtual float GetStrokeStrength()
		=> ImageToolSettings.Strength;

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (!CanStartPainting(button))
			return;

		DiscardActiveStroke();

		activeImage = ImagePaintEngine.GetEditableImage(view, ctx);
		if (activeImage == null || activeImage.tileDoc == null)
			return;

		if (!ImagePaintEngine.TryWorldToBitmapPixel(activeImage, pagePoint, out lastPixel))
		{
			activeImage = null;
			return;
		}

		_activeButton = button;

		var strokeColor = GetStrokeColor(button);
		float strokeOpacity = GetStrokeOpacity(button, strokeColor);

		ImagePixelSelection? limitSelection = null;
		if (_session.Selection.HasSelection && _session.Selection.Mask != null)
		{
			limitSelection = new ImagePixelSelection(
				_session.Selection.PathPixels,
				_session.Selection.Bounds,
				_session.Selection.Mask.Copy());
		}

		activeImage.ActiveStroke = new ActiveImageStrokeSession(
			GetStrokeMode(),
			strokeColor,
			GetRadiusBitmapPx(view, activeImage),
			strokeOpacity,
			GetStrokeSmoothPercent(),
			GetShapeType(),
			GetStrokeStrength(),
			limitSelection,
			ImagePaintEngine.GetPixelAspect(activeImage));

		isPainting = true;

		StampStroke(activeImage, lastPixel, lastPixel);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
	}

	public void OnMouseMove(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!isPainting || activeImage == null || activeImage.ActiveStroke == null)
			return;

		if (!ImagePaintEngine.TryWorldToBitmapPixel(activeImage, pagePoint, out var currentPixel))
			return;

		StampStroke(activeImage, lastPixel, currentPixel);
		lastPixel = currentPixel;

		view.Invalidate();
	}

	public void OnMouseUp(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (!isPainting || button != _activeButton)
			return;

		if (activeImage != null)
		{
			CommitActiveStroke(activeImage);

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.Invalidate();
		}

		isPainting = false;
		activeImage = null;
		_activeButton = MouseButtons.None;
	}

	public void OnKeyDown(
		MainView view,
		EditorInteractionContext ctx,
		Keys keyData)
	{
		if (keyData != Keys.Escape)
			return;

		DiscardActiveStroke();
		isPainting = false;
		activeImage = null;
		_activeButton = MouseButtons.None;

		view.Invalidate();
		view.SetTool(ShapeToolKind.Select);
	}

	public virtual void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		var image = activeImage ?? ImagePaintEngine.GetEditableImage(view, ctx);
		if (image == null || image.Bitmap == null)
			return;

		if (_session.Selection.HasSelection)
		{
			using var screenPath = PixelPathToScreenPath(view, image, _session.Selection.PathPixels);

			using var darkDash = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 1f,
				Color = new SKColor(0, 0, 0, 220),
				PathEffect = SKPathEffect.CreateDash(new float[] { 6f, 4f }, 0f)
			};

			using var lightDash = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 1f,
				Color = new SKColor(255, 255, 255, 230),
				PathEffect = SKPathEffect.CreateDash(new float[] { 6f, 4f }, 5f)
			};

			canvas.DrawPath(screenPath, darkDash);
			canvas.DrawPath(screenPath, lightDash);
		}

		SKPoint px;
		if (isPainting && activeImage != null)
		{
			px = lastPixel;
		}
		else if (!ImagePaintEngine.TryWorldToBitmapPixel(image, ctx.CurrentPage, out px))
		{
			return;
		}

		float radiusBitmapPx = GetRadiusBitmapPx(view, image);

		var local = BitmapPixelToLocal(image, px);
		var centerWorld = image.LocalToWorld(local);
		var centerScreen = view.PageToScreen(centerWorld);

		float localRadiusX = radiusBitmapPx * image.ImageLocalRect.Width / image.Bitmap.Width;
		float localRadiusY = radiusBitmapPx * image.ImageLocalRect.Height / image.Bitmap.Height;

		var worldRight = image.LocalToWorld(new SKPoint(local.X + localRadiusX, local.Y));
		var worldBottom = image.LocalToWorld(new SKPoint(local.X, local.Y + localRadiusY));

		float rxScreen = Distance(view.PageToScreen(centerWorld), view.PageToScreen(worldRight));
		float ryScreen = Distance(view.PageToScreen(centerWorld), view.PageToScreen(worldBottom));

		using var stroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = new SKColor(0, 0, 0, 220)
		};

		using var stroke2 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(255, 255, 255, 220),
			PathEffect = SKPathEffect.CreateDash(new float[] { 4f, 4f }, 0f)
		};

		var rect = SKRect.Create(
			centerScreen.X - rxScreen,
			centerScreen.Y - ryScreen,
			rxScreen * 2f,
			ryScreen * 2f);

		if (GetShapeType() == ImageToolShapeType.Circle)
		{
			canvas.DrawOval(rect, stroke);
			canvas.DrawOval(rect, stroke2);
		}
		else
		{
			canvas.DrawRect(rect, stroke);
			canvas.DrawRect(rect, stroke2);
		}
	}

	private static SKPath PixelPathToScreenPath(MainView view, ImageShape image, SKPath pixelPath)
	{
		var result = new SKPath();

		using var iterator = pixelPath.CreateRawIterator();
		Span<SKPoint> points = stackalloc SKPoint[4];

		while (true)
		{
			var verb = iterator.Next(points);
			if (verb == SKPathVerb.Done)
				break;

			switch (verb)
			{
				case SKPathVerb.Move:
					result.MoveTo(PixelToScreen(view, image, points[0]));
					break;

				case SKPathVerb.Line:
					result.LineTo(PixelToScreen(view, image, points[1]));
					break;

				case SKPathVerb.Quad:
					result.QuadTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]));
					break;

				case SKPathVerb.Cubic:
					result.CubicTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]),
						PixelToScreen(view, image, points[3]));
					break;

				case SKPathVerb.Close:
					result.Close();
					break;
			}
		}

		return result;
	}

	private static SKPoint PixelToScreen(MainView view, ImageShape image, SKPoint pixel)
	{
		var local = BitmapPixelToLocal(image, pixel);
		var world = image.LocalToWorld(local);
		return view.PageToScreen(world);
	}

	protected virtual bool CanStartPainting(MouseButtons button)
	{
		if (GetStrokeMode() == ImageStrokeMode.Erase)
			return button == MouseButtons.Left;

		return button == MouseButtons.Left || button == MouseButtons.Right;
	}

	private static void StampStroke(ImageShape image, SKPoint from, SKPoint to)
	{
		var doc = image.tileDoc;
		var active = image.ActiveStroke;

		if (doc == null || active == null)
			return;

		float radiusX = active.RadiusPx;
		float radiusY = active.RadiusPxY;

		var bounds = new SKRect(
			MathF.Min(from.X, to.X) - radiusX - 2f,
			MathF.Min(from.Y, to.Y) - radiusY - 2f,
			MathF.Max(from.X, to.X) + radiusX + 2f,
			MathF.Max(from.Y, to.Y) + radiusY + 2f);

		var dirty = SKRectI.Intersect(
			SKRectI.Ceiling(bounds),
			new SKRectI(0, 0, doc.Width, doc.Height));

		if (dirty.IsEmpty)
			return;

		int tileX0 = dirty.Left / doc.TileSize;
		int tileY0 = dirty.Top / doc.TileSize;
		int tileX1 = (dirty.Right - 1) / doc.TileSize;
		int tileY1 = (dirty.Bottom - 1) / doc.TileSize;

		for (int ty = tileY0; ty <= tileY1; ty++)
		{
			for (int tx = tileX0; tx <= tileX1; tx++)
			{
				var key = new ImageTileKey(tx, ty);
				EnsureTileBuffers(image, key);

				var tileBounds = doc.GetTileBounds(tx, ty);
				var affectedImageRect = SKRectI.Intersect(tileBounds, dirty);

				if (affectedImageRect.IsEmpty)
					continue;

				var localDirty = new SKRectI(
					affectedImageRect.Left - tileBounds.Left,
					affectedImageRect.Top - tileBounds.Top,
					affectedImageRect.Right - tileBounds.Left,
					affectedImageRect.Bottom - tileBounds.Top);

				var mask = active.MaskTiles[key];

				using (var canvas = new SKCanvas(mask))
				{
					canvas.Save();
					canvas.Translate(-tileBounds.Left, -tileBounds.Top);

					canvas.ClipRect(
						SKRect.Create(
							affectedImageRect.Left,
							affectedImageRect.Top,
							affectedImageRect.Width,
							affectedImageRect.Height),
						SKClipOperation.Intersect,
						true);

					using var paint = CreateMaskPaint(active);

					// Холст сжимается по Y на PixelAspect, а координаты
					// растягиваются на столько же. Отрезок остаётся на месте,
					// но круглый торец кисти шириной 2*RadiusPx превращается
					// в эллипс с полуосями (RadiusPx, RadiusPx*PixelAspect) —
					// то есть в круг на листе и на экране.
					float aspect = active.PixelAspect;

					canvas.Scale(1f, aspect);

					canvas.DrawLine(
						new SKPoint(from.X, from.Y / aspect),
						new SKPoint(to.X, to.Y / aspect),
						paint);

					canvas.Restore();
				}

				if (active.LimitSelection != null &&
					active.LimitSelection.HasSelection &&
					active.LimitSelection.Mask != null)
				{
					ApplySelectionLimitToMask(
						mask,
						tileBounds,
						localDirty,
						active.LimitSelection);
				}

				active.AddDirtyRect(key, localDirty);
				RebuildPreviewTile(active, key, localDirty);
			}
		}
	}

	private static SKPaint CreateMaskPaint(ActiveImageStrokeSession active)
	{
		byte alpha = (byte)Math.Clamp(
			MathF.Round(active.Opacity * 255f),
			0f,
			255f);

		return new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeCap = active.ShapeType == ImageToolShapeType.Circle
				? SKStrokeCap.Round
				: SKStrokeCap.Square,
			StrokeJoin = SKStrokeJoin.Round,
			StrokeWidth = Math.Max(1f, active.RadiusPx * 2f),
			Color = new SKColor(255, 255, 255, alpha),
			BlendMode = SKBlendMode.Src
		};
	}

	private static void ApplySelectionLimitToMask(
		SKBitmap mask,
		SKRectI tileBounds,
		SKRectI localDirty,
		ImagePixelSelection selection)
	{
		if (mask == null || localDirty.IsEmpty || !selection.HasSelection || selection.Mask == null)
			return;

		int width = localDirty.Width;
		int height = localDirty.Height;

		using var selectionChunk = new SKBitmap(
			width,
			height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var selectionCanvas = new SKCanvas(selectionChunk))
		using (var fill = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Fill,
			Color = SKColors.White,
			BlendMode = SKBlendMode.Src
		})
		{
			selectionCanvas.Clear(SKColors.Transparent);

			for (int y = 0; y < height; y++)
			{
				int imageY = tileBounds.Top + localDirty.Top + y;

				for (int x = 0; x < width; x++)
				{
					int imageX = tileBounds.Left + localDirty.Left + x;

					if (selection.ContainsPixel(imageX, imageY))
						selectionCanvas.DrawPoint(x, y, fill);
				}
			}
		}

		using var canvas = new SKCanvas(mask);
		using var clipPaint = new SKPaint
		{
			BlendMode = SKBlendMode.DstIn,
			IsAntialias = false
		};

		canvas.Save();
		canvas.ClipRect(SKRect.Create(
			localDirty.Left,
			localDirty.Top,
			localDirty.Width,
			localDirty.Height));

		canvas.DrawBitmap(
			selectionChunk,
			new SKPoint(localDirty.Left, localDirty.Top),
			new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
			clipPaint);

		canvas.Restore();
	}

	private static void EnsureTileBuffers(ImageShape image, ImageTileKey key)
	{
		var doc = image.tileDoc!;
		var active = image.ActiveStroke!;

		if (active.BaseTiles.ContainsKey(key))
			return;

		var tile = doc.GetOrCreateTile(key.X, key.Y);

		active.BaseTiles[key] = tile.Bitmap.Copy();
		active.MaskTiles[key] = new SKBitmap(
			tile.Bitmap.Width,
			tile.Bitmap.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);
		active.PreviewTiles[key] = tile.Bitmap.Copy();

		using var clearCanvas = new SKCanvas(active.MaskTiles[key]);
		clearCanvas.Clear(SKColors.Transparent);
	}

	private static void RebuildPreviewTile(
		ActiveImageStrokeSession active,
		ImageTileKey key,
		SKRectI localDirty)
	{
		if (localDirty.IsEmpty)
			return;

		var baseTile = active.BaseTiles[key];
		var maskTile = active.MaskTiles[key];
		var previewTile = active.PreviewTiles[key];

		switch (active.Mode)
		{
			case ImageStrokeMode.Paint:
				ApplyPaint(baseTile, maskTile, previewTile, localDirty, active.Color);
				break;

			case ImageStrokeMode.Erase:
				ApplyErase(baseTile, maskTile, previewTile, localDirty);
				break;

			case ImageStrokeMode.Contrast:
				ApplyContrast(baseTile, maskTile, previewTile, localDirty, active.Strength);
				break;

			case ImageStrokeMode.Lighten:
				ApplyLightenDarken(baseTile, maskTile, previewTile, localDirty, Math.Abs(active.Strength));
				break;

			case ImageStrokeMode.Darken:
				ApplyLightenDarken(baseTile, maskTile, previewTile, localDirty, -Math.Abs(active.Strength));
				break;

			case ImageStrokeMode.BW:
				ApplyBlackWhite(baseTile, maskTile, previewTile, localDirty, Math.Abs(active.Strength));
				break;
		}
	}

	private static void ApplyPaint(
		SKBitmap baseTile,
		SKBitmap maskTile,
		SKBitmap previewTile,
		SKRectI localDirty,
		SKColor color)
	{
		if (localDirty.IsEmpty)
			return;

		var rect = SKRect.Create(
			localDirty.Left,
			localDirty.Top,
			localDirty.Width,
			localDirty.Height);

		using var canvas = new SKCanvas(previewTile);
		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		// Режим Src обязателен: превью надо ЗАМЕНИТЬ исходными пикселями,
		// а не положить исходник поверх результата прошлого мазка.
		// Прямоугольники соседних отрезков перекрываются на радиус кисти,
		// и при наложении поверх краска накапливалась бы в местах
		// перекрытия — тем заметнее, чем прозрачнее кисть.
		using (var restore = new SKPaint { BlendMode = SKBlendMode.Src, IsAntialias = false })
			canvas.DrawBitmap(baseTile, rect, rect, sampling, restore);

		canvas.Save();
		canvas.ClipRect(rect, SKClipOperation.Intersect, true);

		using var maskPaint = new SKPaint
		{
			BlendMode = SKBlendMode.DstIn,
			IsAntialias = false
		};

		using var maskChunk = new SKBitmap(
			localDirty.Width,
			localDirty.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var maskCanvas = new SKCanvas(maskChunk))
		{
			maskCanvas.Clear(SKColors.Transparent);
			maskCanvas.DrawBitmap(
				maskTile,
				rect,
				SKRect.Create(0, 0, localDirty.Width, localDirty.Height),
				sampling,
				null);
		}

		using var colorLayer = new SKBitmap(
			localDirty.Width,
			localDirty.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var colorCanvas = new SKCanvas(colorLayer))
		{
			colorCanvas.Clear(SKColors.Transparent);

			using var fill = new SKPaint
			{
				Style = SKPaintStyle.Fill,
				Color = color,
				BlendMode = SKBlendMode.Src
			};

			colorCanvas.DrawRect(
				SKRect.Create(0, 0, localDirty.Width, localDirty.Height),
				fill);

			colorCanvas.DrawBitmap(maskChunk, 0, 0, sampling, maskPaint);
		}

		canvas.DrawBitmap(
			colorLayer,
			new SKPoint(localDirty.Left, localDirty.Top),
			sampling,
			null);

		canvas.Restore();
	}

	private static void ApplyErase(
		SKBitmap baseTile,
		SKBitmap maskTile,
		SKBitmap previewTile,
		SKRectI localDirty)
	{
		if (localDirty.IsEmpty)
			return;

		int width = localDirty.Width;
		int height = localDirty.Height;

		var rect = SKRect.Create(localDirty.Left, localDirty.Top, width, height);
		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		using var canvas = new SKCanvas(previewTile);

		// См. пояснение в ApplyPaint: восстановление базы обязано быть Src.
		using (var restore = new SKPaint { BlendMode = SKBlendMode.Src, IsAntialias = false })
			canvas.DrawBitmap(baseTile, rect, rect, sampling, restore);

		using var maskChunk = new SKBitmap(
			width,
			height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var maskCanvas = new SKCanvas(maskChunk))
		{
			maskCanvas.Clear(SKColors.Transparent);
			maskCanvas.DrawBitmap(
				maskTile,
				rect,
				SKRect.Create(0, 0, width, height),
				sampling,
				null);
		}

		using var erasePaint = new SKPaint
		{
			BlendMode = SKBlendMode.DstOut
		};

		canvas.DrawBitmap(
			maskChunk,
			new SKPoint(localDirty.Left, localDirty.Top),
			sampling,
			erasePaint);
	}

	private static void ApplyContrast(
		SKBitmap baseTile,
		SKBitmap maskTile,
		SKBitmap previewTile,
		SKRectI localDirty,
		float strength)
	{
		if (localDirty.IsEmpty)
			return;

		int left = localDirty.Left;
		int top = localDirty.Top;
		int right = localDirty.Right;
		int bottom = localDirty.Bottom;

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				var src = baseTile.GetPixel(x, y);
				var mask = maskTile.GetPixel(x, y);

				float influence = (mask.Alpha / 255f) * Math.Abs(strength);
				if (influence <= 0f)
				{
					previewTile.SetPixel(x, y, src);
					continue;
				}

				previewTile.SetPixel(
					x,
					y,
					ApplyContrastToColor(src, Math.Sign(strength) * influence));
			}
		}
	}

	private static SKColor ApplyContrastToColor(SKColor color, float amount)
	{
		if (color.Alpha == 0)
			return color;

		float r = color.Red / 255f;
		float g = color.Green / 255f;
		float b = color.Blue / 255f;

		float factor = 1f + amount;

		r = ((r - 0.5f) * factor) + 0.5f;
		g = ((g - 0.5f) * factor) + 0.5f;
		b = ((b - 0.5f) * factor) + 0.5f;

		r = Math.Clamp(r, 0f, 1f);
		g = Math.Clamp(g, 0f, 1f);
		b = Math.Clamp(b, 0f, 1f);

		return new SKColor(
			(byte)MathF.Round(r * 255f),
			(byte)MathF.Round(g * 255f),
			(byte)MathF.Round(b * 255f),
			color.Alpha);
	}

	private static void ApplyLightenDarken(
		SKBitmap baseTile,
		SKBitmap maskTile,
		SKBitmap previewTile,
		SKRectI localDirty,
		float amount)
	{
		if (localDirty.IsEmpty)
			return;

		int left = localDirty.Left;
		int top = localDirty.Top;
		int right = localDirty.Right;
		int bottom = localDirty.Bottom;

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				var src = baseTile.GetPixel(x, y);
				var mask = maskTile.GetPixel(x, y);

				float influence = (mask.Alpha / 255f) * Math.Abs(amount);
				if (influence <= 0f)
				{
					previewTile.SetPixel(x, y, src);
					continue;
				}

				previewTile.SetPixel(
					x,
					y,
					ApplyLightenDarkenToColor(src, Math.Sign(amount) * influence));
			}
		}
	}

	private static SKColor ApplyLightenDarkenToColor(SKColor color, float amount)
	{
		if (color.Alpha == 0)
			return color;

		float r = color.Red / 255f;
		float g = color.Green / 255f;
		float b = color.Blue / 255f;

		if (amount >= 0f)
		{
			r = r + (1f - r) * amount;
			g = g + (1f - g) * amount;
			b = b + (1f - b) * amount;
		}
		else
		{
			float t = 1f + amount;
			r *= t;
			g *= t;
			b *= t;
		}

		r = Math.Clamp(r, 0f, 1f);
		g = Math.Clamp(g, 0f, 1f);
		b = Math.Clamp(b, 0f, 1f);

		return new SKColor(
			(byte)MathF.Round(r * 255f),
			(byte)MathF.Round(g * 255f),
			(byte)MathF.Round(b * 255f),
			color.Alpha);
	}

	private static void ApplyBlackWhite(
		SKBitmap baseTile,
		SKBitmap maskTile,
		SKBitmap previewTile,
		SKRectI localDirty,
		float strength)
	{
		if (localDirty.IsEmpty)
			return;

		int left = localDirty.Left;
		int top = localDirty.Top;
		int right = localDirty.Right;
		int bottom = localDirty.Bottom;

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				var src = baseTile.GetPixel(x, y);
				var mask = maskTile.GetPixel(x, y);

				float influence = (mask.Alpha / 255f) * Math.Abs(strength);
				if (influence <= 0f)
				{
					previewTile.SetPixel(x, y, src);
					continue;
				}

				previewTile.SetPixel(
					x,
					y,
					ApplyBlackWhiteToColor(src, influence));
			}
		}
	}

	private static SKColor ApplyBlackWhiteToColor(SKColor color, float amount)
	{
		if (color.Alpha == 0)
			return color;

		float r = color.Red / 255f;
		float g = color.Green / 255f;
		float b = color.Blue / 255f;

		float gray = 0.299f * r + 0.587f * g + 0.114f * b;

		r = r + (gray - r) * amount;
		g = g + (gray - g) * amount;
		b = b + (gray - b) * amount;

		r = Math.Clamp(r, 0f, 1f);
		g = Math.Clamp(g, 0f, 1f);
		b = Math.Clamp(b, 0f, 1f);

		return new SKColor(
			(byte)MathF.Round(r * 255f),
			(byte)MathF.Round(g * 255f),
			(byte)MathF.Round(b * 255f),
			color.Alpha);
	}

	/// <summary>
	/// Переносит мазок из превью в настоящие тайлы и пишет шаг истории.
	///
	/// Шаг не записывался вовсе — отсюда и неработающая отмена рисования.
	/// Снимок «до» берётся именно здесь, а не на нажатии кнопки: весь мазок
	/// живёт в превью-тайлах, настоящие до этого момента не тронуты. Зато
	/// к концу мазка точно известно, какие тайлы он задел — на нажатии это
	/// было бы ещё неизвестно, и пришлось бы снимать картинку целиком.
	/// </summary>
	private void CommitActiveStroke(ImageShape image)
	{
		var doc = image.tileDoc;
		var active = image.ActiveStroke;

		if (doc == null || active == null)
			return;

		// Два списка вместо одного: мазок адресует тайлы через ImageTileKey,
		// а история — через кортеж (X, Y). Приводить одно к другому ради
		// одного метода значило бы трогать оба слоя.
		var touched = new List<ImageTileKey>();
		var historyKeys = new List<(int X, int Y)>();

		foreach (var pair in active.DirtyRects)
		{
			if (!active.PreviewTiles.ContainsKey(pair.Key))
				continue;

			touched.Add(pair.Key);
			historyKeys.Add((pair.Key.X, pair.Key.Y));
		}

		var undo = _session.History;

		var tilesAction = undo != null && !undo.IsApplying
			? ImageTilesAction.CaptureTiles(ToolHistoryName, image, historyKeys)
			: null;

		// Ключ слияния — сам объект начатого мазка. Пока кнопка мыши
		// не отпущена, ActiveStroke тот же, и отпечатки складываются
		// в один шаг. Отпустил, провёл ещё раз — объект другой, и второй
		// мазок становится отдельным шагом, как и должен.
		if (tilesAction != null)
			tilesAction.MergeKey = active;

		foreach (var key in touched)
		{
			if (!active.PreviewTiles.TryGetValue(key, out var preview))
				continue;

			var tile = doc.GetOrCreateTile(key.X, key.Y);

			using var canvas = new SKCanvas(tile.Bitmap);
			using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
			var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

			canvas.DrawBitmap(preview, 0f, 0f, sampling, paint);
			tile.InvalidatePreview();
		}

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();
		image.InvalidateAdjustmentPaint();

		DiscardActiveStroke(image);

		if (tilesAction == null)
			return;

		tilesAction.CaptureAfter();
		undo!.Push(tilesAction);
	}

	/// <summary>
	/// Бросает незавершённый мазок без фиксации.
	///
	/// Прежний параметр commit убран: его нигде не передавали со значением
	/// true, а фиксация теперь требует сессии ради истории и статической
	/// быть не может.
	/// </summary>
	private static void DiscardActiveStroke(ImageShape image)
	{
		image.ActiveStroke?.Dispose();
		image.ActiveStroke = null;
	}

	private void DiscardActiveStroke()
	{
		if (activeImage == null)
			return;

		DiscardActiveStroke(activeImage);
	}

	/// <summary>
	/// Бросает начатый ввод, не трогая ни выделение, ни активный инструмент.
	///
	/// Нужно отмене: Ctrl+Z посреди мазка обязан сначала избавиться от
	/// незакоммиченного ActiveStroke. Тот держит копию затронутых тайлов
	/// и при следующем движении мыши записал бы её поверх состояния,
	/// которое откат только что восстановил.
	///
	/// От обработки Escape отличается тем, что здесь НЕ переключается
	/// инструмент: пользователь отменяет последний шаг, а не выходит
	/// из рисования, и отбирать у него кисть за это неправильно.
	/// </summary>
	public void CancelActiveInput()
	{
		DiscardActiveStroke();

		isPainting = false;
		activeImage = null;
		_activeButton = MouseButtons.None;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	private static SKPoint BitmapPixelToLocal(ImageShape image, SKPoint px)
	{
		var rect = image.ImageLocalRect;

		float x = rect.Left + px.X / Math.Max(1, image.Bitmap!.Width - 1) * rect.Width;
		float y = rect.Top + px.Y / Math.Max(1, image.Bitmap.Height - 1) * rect.Height;

		return new SKPoint(x, y);
	}
}

/// <summary>
/// Прямоугольное выделение области изображения.
///
/// Изменения: рамка выделения теперь пишется в историю одним шагом на всё
/// перетаскивание — область записи открывается в OnMouseDown и закрывается
/// в OnMouseUp, поэтому промежуточные состояния из OnMouseMove в историю
/// не попадают.
///
/// Отдельно исправлено: сброс плавающего фрагмента при клике вне изображения
/// был скопирован сюда из ImageSelectionClipboardHelper.CommitFloatingIfAny
/// целиком. Теперь вызывается сам хелпер — на тридцать строк меньше и
/// поведение гарантированно одинаковое в обоих местах.
///
/// Заменяет класс ImageRectSelectTool в ImagePaintTools.cs.
/// </summary>
public sealed class ImageRectSelectTool : IEditorTool
{
	private readonly ImageEditSession _session;

	private ImageShape? _image;
	private bool _dragging;
	private SKPointI _startPixel;
	private SKPointI _currentPixel;

	public ImageRectSelectTool(ImageEditSession session)
	{
		_session = session;
	}

	public ShapeToolKind Kind => ShapeToolKind.Select;

	public void OnMouseDown(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		// В локальную: обнулив _image, мы потеряли бы картинку, в которую
		// ExitSelection должен впечатать перемещённый фрагмент.
		var resolved = ImagePaintEngine.GetEditableImage(view, ctx);

		if (resolved == null ||
			!ImageEditGeometry.TryPageToBitmapPixel(resolved, pagePoint, out var pixel))
		{
			ExitSelection(view);
			return;
		}

		_image = resolved;

		_dragging = true;
		_startPixel = ImageEditGeometry.ClampPixel(_image, pixel);
		_currentPixel = _startPixel;

		// Область записи закроется в OnMouseUp: всё перетаскивание — один шаг.
		_session.BeginSelectionHistoryManual("Rect select");

		// Впечатать, а не выбросить: начало новой рамки поверх ранее
		// перемещённого фрагмента не должно стирать его пиксели.
		// CommitFloatingIfAny сам снимает плавающий фрагмент после
		// впечатывания, отдельный ClearFloatingSelection не нужен.
		ImageSelectionClipboardHelper.CommitFloatingIfAny(_image, _session, _session.History);

		_session.Selection.Clear();
	}

	public void OnMouseMove(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!_dragging || _image == null)
			return;

		if (!ImageEditGeometry.TryPageToBitmapPixel(_image, pagePoint, out var pixel))
			return;

		_currentPixel = ImageEditGeometry.ClampPixel(_image, pixel);

		ApplySelectionBounds();
		view.Invalidate();
	}

	public void OnMouseUp(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		if (!_dragging)
			return;

		if (_image != null &&
			ImageEditGeometry.TryPageToBitmapPixel(_image, pagePoint, out var pixel))
		{
			_currentPixel = ImageEditGeometry.ClampPixel(_image, pixel);
			ApplySelectionBounds();
		}

		_dragging = false;

		// Записываем шаг до переключения инструмента: SetImageEditTool создаёт
		// новый инструмент и может сбросить выделение.
		_session.EndSelectionHistory();

		if (_session.Selection.HasSelection)
			view.SetImageEditTool(ImageEditToolKind.Cursor);
		else
			view.Invalidate();
	}

	public void OnKeyDown(
		MainView view,
		EditorInteractionContext ctx,
		Keys keyData)
	{
		if (keyData != Keys.Escape)
			return;

		// Прерванное перетаскивание в историю не пишем.
		if (_dragging)
			_session.AbortSelectionHistory();

		_dragging = false;

		using (_session.BeginSelectionHistory("Clear selection"))
		{
			_session.Selection.Clear();
		}

		view.Invalidate();
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		var image = _image ?? ImagePaintEngine.GetEditableImage(view, ctx);

		if (image?.Bitmap == null || !_session.Selection.HasSelection)
			return;

		var screenRect = ImageEditGeometry.BitmapRectToScreen(view, image, _session.Selection.Bounds);

		var style = _session.OverlayStyle;
		using var fill = style.CreateFillPaint();
		using var darkDash = style.CreateDarkDashPaint();
		using var lightDash = style.CreateLightDashPaint();

		canvas.DrawRect(screenRect, fill);
		canvas.DrawRect(screenRect, darkDash);
		canvas.DrawRect(screenRect, lightDash);
	}

	/// <summary>
	/// Клик вне изображения: фиксируем плавающий фрагмент в пикселях, сбрасываем
	/// выделение и выходим из режима растрового редактирования.
	/// </summary>
	private void ExitSelection(MainView view)
	{
		_dragging = false;

		if (_image != null)
		{
			if (_image.ActiveStroke != null)
			{
				_image.RebuildBitmapFromTiles();
				_image.EndActiveStrokePreview();
			}

			// Фиксация плавающего фрагмента меняет пиксели — это отдельный
			// шаг истории, помимо изменения выделения.
			ImageSelectionClipboardHelper.CommitFloatingIfAny(_image, _session, _session.History);
		}

		using (_session.BeginSelectionHistory("Clear selection"))
		{
			_session.ClearSelectionAndFloating();
		}

		_session.ActiveKind = ImageEditToolKind.None;
		_session.mainForm.SetEditorMode(EditorMode.Curve);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
	}

	private void ApplySelectionBounds()
	{
		if (_image?.Bitmap == null)
			return;

		int left = Math.Min(_startPixel.X, _currentPixel.X);
		int top = Math.Min(_startPixel.Y, _currentPixel.Y);
		int right = Math.Max(_startPixel.X, _currentPixel.X);
		int bottom = Math.Max(_startPixel.Y, _currentPixel.Y);

		_session.Selection.SetRect(
			new SKRectI(left, top, right, bottom),
			_image.Bitmap.Width,
			_image.Bitmap.Height);
	}
}

public sealed class ImageLassoTool : IEditorTool
{
	private enum SelectionCombineMode
	{
		Replace,
		Add
	}

	private readonly ImageEditSession _session;

	private ImageShape? _image;
	private bool _tracking;
	private bool _moved;
	private readonly List<SKPointI> _pixels = new();
	private SelectionCombineMode _combineMode;

	public ImageLassoTool(ImageEditSession session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public ShapeToolKind Kind => ShapeToolKind.Select;

	public void OnMouseDown(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		var resolved = ImagePaintEngine.GetEditableImage(view, ctx);

		if (resolved == null ||
			!ImageEditGeometry.TryPageToBitmapPixel(resolved, pagePoint, out var pixel))
		{
			// Клик мимо: перемещённый фрагмент впечатывается в прежнее
			// изображение, а не остаётся висеть до следующего сброса.
			if (_image != null)
				ImageSelectionClipboardHelper.CommitFloatingIfAny(_image, _session, _session.History);

			return;
		}

		_image = resolved;

		_combineMode = (Control.ModifierKeys & Keys.Control) == Keys.Control
			? SelectionCombineMode.Add
			: SelectionCombineMode.Replace;

		_tracking = true;
		_moved = false;

		_pixels.Clear();
		_pixels.Add(ImageEditGeometry.ClampPixel(_image, pixel));

		// Открываем область записи истории. Закроется в OnMouseUp, поэтому вся
		// обводка станет одним шагом, а промежуточные состояния из OnMouseMove
		// в историю не попадут.
		_session.BeginSelectionHistoryManual("Lasso select");

		// Впечатать, а не выбросить: новая обводка поверх ранее
		// перемещённого фрагмента не должна стирать его пиксели.
		ImageSelectionClipboardHelper.CommitFloatingIfAny(_image, _session, _session.History);

		view.Cursor = Cursors.Cross;
		view.Invalidate();
	}


	public void OnMouseMove(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;

		if (!_tracking || _image == null)
			return;

		if (!ImageEditGeometry.TryPageToBitmapPixel(_image, pagePoint, out var pixel))
			return;

		var clamped = ImageEditGeometry.ClampPixel(_image, pixel);

		if (_pixels.Count == 0)
		{
			_pixels.Add(clamped);
			return;
		}

		var previous = _pixels[^1];

		float dx = clamped.X - previous.X;
		float dy = clamped.Y - previous.Y;
		float distanceSquared = dx * dx + dy * dy;

		if (!_moved && distanceSquared >= 1.0f)
			_moved = true;

		if (distanceSquared >= 1.0f)
			_pixels.Add(clamped);

		view.Invalidate();
	}

	public void OnMouseUp(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		if (!_tracking || _image == null)
			return;

		if (ImageEditGeometry.TryPageToBitmapPixel(_image, pagePoint, out var pixel))
		{
			var clamped = ImageEditGeometry.ClampPixel(_image, pixel);

			if (_pixels.Count == 0)
			{
				_pixels.Add(clamped);
			}
			else
			{
				var previous = _pixels[^1];

				float dx = clamped.X - previous.X;
				float dy = clamped.Y - previous.Y;

				if (dx * dx + dy * dy >= 1.0f)
					_pixels.Add(clamped);
			}
		}

		_tracking = false;

		ImagePixelSelection? result = null;

		if (!_moved)
		{
			var seed = _pixels.Count > 0
				? _pixels[^1]
				: SKPointI.Empty;

			result = ImageSmartSelectionRasterizer.BuildSelection(
				_image,
				seed.X,
				seed.Y,
				ImageToolSettings.Tolerance);

			if (result != null &&
				_combineMode == SelectionCombineMode.Add &&
				_session.Selection.HasSelection)
			{
				using var merged = UnionPixelSelections(
					_session.Selection,
					result,
					_image.Bitmap!.Width,
					_image.Bitmap.Height);

				result.Dispose();
				result = merged;
			}

			if (result == null &&
				_combineMode == SelectionCombineMode.Replace)
			{
				_session.Selection.Clear();
			}
		}
		else
		{
			using var lassoSelection = BuildPixelSelection(_image, _pixels);

			if (lassoSelection != null)
			{
				if (_combineMode == SelectionCombineMode.Add &&
					_session.Selection.HasSelection)
				{
					result = UnionPixelSelections(
						_session.Selection,
						lassoSelection,
						_image.Bitmap!.Width,
						_image.Bitmap.Height);
				}
				else if (lassoSelection.Mask != null)
				{
					result = new ImagePixelSelection(
						lassoSelection.PathPixels,
						lassoSelection.Bounds,
						lassoSelection.Mask.Copy());
				}
			}
			else if (_combineMode == SelectionCombineMode.Replace)
			{
				_session.Selection.Clear();
			}
		}

		_session.ClearFloatingSelection();
		_session.SetSelection(result);
		result?.Dispose();

		_pixels.Clear();
		_moved = false;

		// Записываем шаг ДО переключения инструмента: SetImageEditTool создаёт
		// новый инструмент и может сбросить выделение, и тогда в историю попало
		// бы уже пустое состояние.
		_session.EndSelectionHistory();

		if (_session.Selection.HasSelection)
			view.SetImageEditTool(ImageEditToolKind.Cursor);
		else
			view.Invalidate();
	}


	public void OnKeyDown(
		MainView view,
		EditorInteractionContext ctx,
		Keys keyData)
	{
		if (keyData == Keys.Escape)
		{
			// Прерванную обводку в историю не пишем.
			if (_tracking)
				_session.AbortSelectionHistory();

			_tracking = false;
			_moved = false;
			_pixels.Clear();

			using (_session.BeginSelectionHistory("Clear selection"))
			{
				_session.Selection.Clear();
			}

			view.Invalidate();
			return;
		}

		if (keyData == Keys.Delete)
		{
			var image = _image ?? ImagePaintEngine.GetEditableImage(view, ctx);

			if (image == null || !_session.Selection.HasSelection)
				return;

			var bounds = _session.Selection.Bounds;

			// Удаление пикселей и сброс выделения — один шаг истории.
			var undo = _session.History;
			undo?.BeginGroup("Delete selection");

			try
			{
				var tilesAction = ImageTilesAction.Capture(
					"Delete selection",
					image,
					SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height));

				EraseSelection(image, _session.Selection);

				if (tilesAction != null)
				{
					tilesAction.CaptureAfter();
					undo?.Push(tilesAction);
				}

				using (_session.BeginSelectionHistory("Delete selection"))
				{
					_session.Selection.Clear();
				}
			}
			finally
			{
				undo?.CommitGroup();
			}

			image.RebuildBitmapFromTiles();
			image.InvalidateAdjustmentPaint();

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.Invalidate();
		}
	}

	public void DrawOverlay(
		SKCanvas canvas,
		MainView view,
		EditorInteractionContext ctx)
	{
		var image = _image ?? ImagePaintEngine.GetEditableImage(view, ctx);

		if (image?.Bitmap == null)
			return;

		if (_tracking && _moved && _pixels.Count >= 2)
		{
			using var path = PixelPathToScreenPath(view, image, _pixels, false);
			DrawMarchingAnts(canvas, path);
		}

		if (_session.Selection.HasSelection)
		{
			using var path = PixelPathToScreenPath(
				view,
				image,
				_session.Selection.PathPixels);

			DrawMarchingAnts(canvas, path);
		}
	}

	private void DrawMarchingAnts(SKCanvas canvas, SKPath path)
	{
		var style = _session.OverlayStyle;

		using var dark = style.CreateDarkDashPaint();
		using var light = style.CreateLightDashPaint();

		canvas.DrawPath(path, dark);
		canvas.DrawPath(path, light);
	}

	private static ImagePixelSelection? BuildPixelSelection(
		ImageShape image,
		List<SKPointI> pixels)
	{
		if (image.Bitmap == null || pixels == null || pixels.Count < 3)
			return null;

		using var pixelPath = new SKPath();

		pixelPath.MoveTo(pixels[0].X, pixels[0].Y);

		for (int i = 1; i < pixels.Count; i++)
			pixelPath.LineTo(pixels[i].X, pixels[i].Y);

		pixelPath.Close();

		var bounds = SKRectI.Intersect(
			SKRectI.Ceiling(pixelPath.TightBounds),
			new SKRectI(0, 0, image.Bitmap.Width, image.Bitmap.Height));

		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return null;

		var mask = new SKBitmap(
			bounds.Width,
			bounds.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var canvas = new SKCanvas(mask))
		using (var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColors.White,
			BlendMode = SKBlendMode.Src
		})
		{
			canvas.Clear(SKColors.Transparent);
			canvas.Translate(-bounds.Left, -bounds.Top);
			canvas.DrawPath(pixelPath, fill);
		}

		return new ImagePixelSelection(
			new SKPath(pixelPath),
			bounds,
			mask);
	}

	private static ImagePixelSelection? UnionPixelSelections(
		ImagePixelSelection existing,
		ImagePixelSelection added,
		int imageWidth,
		int imageHeight)
	{
		if (added == null || !added.HasSelection || added.Mask == null)
		{
			return existing != null &&
				   existing.HasSelection &&
				   existing.Mask != null
				? new ImagePixelSelection(
					existing.PathPixels,
					existing.Bounds,
					existing.Mask.Copy())
				: null;
		}

		if (existing == null ||
			!existing.HasSelection ||
			existing.Mask == null)
		{
			return new ImagePixelSelection(
				added.PathPixels,
				added.Bounds,
				added.Mask.Copy());
		}

		var bounds = new SKRectI(
			Math.Max(0, Math.Min(existing.Bounds.Left, added.Bounds.Left)),
			Math.Max(0, Math.Min(existing.Bounds.Top, added.Bounds.Top)),
			Math.Min(imageWidth, Math.Max(existing.Bounds.Right, added.Bounds.Right)),
			Math.Min(imageHeight, Math.Max(existing.Bounds.Bottom, added.Bounds.Bottom)));

		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return null;

		var mask = new bool[bounds.Width, bounds.Height];

		for (int y = bounds.Top; y < bounds.Bottom; y++)
		{
			for (int x = bounds.Left; x < bounds.Right; x++)
			{
				mask[x - bounds.Left, y - bounds.Top] =
					existing.ContainsPixel(x, y) ||
					added.ContainsPixel(x, y);
			}
		}

		return ImageSmartSelectionRasterizer.BuildSelectionFromMask(
			mask,
			bounds.Left,
			bounds.Top);
	}

	private static SKPath PixelPathToScreenPath(
		MainView view,
		ImageShape image,
		List<SKPointI> pixels,
		bool close)
	{
		var path = new SKPath();

		if (pixels.Count == 0)
			return path;

		path.MoveTo(PixelToScreen(view, image, pixels[0]));

		for (int i = 1; i < pixels.Count; i++)
			path.LineTo(PixelToScreen(view, image, pixels[i]));

		if (close)
			path.Close();

		return path;
	}

	private static SKPath PixelPathToScreenPath(
		MainView view,
		ImageShape image,
		SKPath pixelPath)
	{
		var result = new SKPath();

		using var iterator = pixelPath.CreateRawIterator();
		Span<SKPoint> points = stackalloc SKPoint[4];

		while (true)
		{
			var verb = iterator.Next(points);

			if (verb == SKPathVerb.Done)
				break;

			switch (verb)
			{
				case SKPathVerb.Move:
					result.MoveTo(PixelToScreen(view, image, points[0]));
					break;

				case SKPathVerb.Line:
					result.LineTo(PixelToScreen(view, image, points[1]));
					break;

				case SKPathVerb.Quad:
					result.QuadTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]));
					break;

				case SKPathVerb.Cubic:
					result.CubicTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]),
						PixelToScreen(view, image, points[3]));
					break;

				case SKPathVerb.Close:
					result.Close();
					break;
			}
		}

		return result;
	}

	private static SKPoint PixelToScreen(
		MainView view,
		ImageShape image,
		SKPointI pixel)
	{
		return PixelToScreen(view, image, new SKPoint(pixel.X, pixel.Y));
	}

	private static SKPoint PixelToScreen(
		MainView view,
		ImageShape image,
		SKPoint pixel)
	{
		var local = BitmapPixelToLocal(image, pixel);
		var world = image.LocalToWorld(local);

		return view.PageToScreen(world);
	}

	private static SKPoint BitmapPixelToLocal(
		ImageShape image,
		SKPoint pixel)
	{
		var rect = image.ImageLocalRect;

		float width = Math.Max(1, image.Bitmap!.Width - 1);
		float height = Math.Max(1, image.Bitmap.Height - 1);

		float x = rect.Left + pixel.X / width * rect.Width;
		float y = rect.Top + pixel.Y / height * rect.Height;

		return new SKPoint(x, y);
	}

	private static void EraseSelection(
		ImageShape image,
		ImagePixelSelection selection)
	{
		if (image.tileDoc == null || !selection.HasSelection)
			return;

		int tileSize = image.tileDoc.TileSize;

		int tileX0 = selection.Bounds.Left / tileSize;
		int tileY0 = selection.Bounds.Top / tileSize;
		int tileX1 = (selection.Bounds.Right - 1) / tileSize;
		int tileY1 = (selection.Bounds.Bottom - 1) / tileSize;

		for (int ty = tileY0; ty <= tileY1; ty++)
		{
			for (int tx = tileX0; tx <= tileX1; tx++)
			{
				var tile = image.tileDoc.GetOrCreateTile(tx, ty);
				var tileBounds = image.tileDoc.GetTileBounds(tx, ty);

				var affected = SKRectI.Intersect(
					tileBounds,
					selection.Bounds);

				if (affected.IsEmpty)
					continue;

				for (int y = affected.Top; y < affected.Bottom; y++)
				{
					for (int x = affected.Left; x < affected.Right; x++)
					{
						if (!selection.ContainsPixel(x, y))
							continue;

						int localX = x - tileBounds.Left;
						int localY = y - tileBounds.Top;

						var color = tile.Bitmap.GetPixel(localX, localY);

						tile.Bitmap.SetPixel(
							localX,
							localY,
							new SKColor(color.Red, color.Green, color.Blue, 0));
					}
				}

				tile.InvalidatePreview();
			}
		}

		image.MarkTilesDirty();
	}
}

internal static class ImageSmartSelectionRasterizer
{
	public static ImagePixelSelection? BuildSelection(
		ImageShape image,
		int seedX,
		int seedY,
		float tolerance,
		ImagePixelSelection? limitSelection = null)
	{
		if (image == null)
			throw new ArgumentNullException(nameof(image));

		if (!image.EnsureTileDocument() ||
			image.tileDoc == null ||
			image.Bitmap == null)
		{
			return null;
		}

		var document = image.tileDoc;

		if (seedX < 0 || seedY < 0 ||
			seedX >= document.Width ||
			seedY >= document.Height)
		{
			return null;
		}

		if (limitSelection != null &&
			limitSelection.HasSelection &&
			!limitSelection.ContainsPixel(seedX, seedY))
		{
			return null;
		}

		tolerance = Math.Clamp(tolerance, 0f, 1f);

		var seedColor = GetPixel(document, seedX, seedY);

		var region = FloodFillRegion(
			document,
			seedX,
			seedY,
			seedColor,
			tolerance,
			limitSelection);

		if (region.Count == 0)
			return null;

		GetRegionBounds(
			region,
			document.Width,
			out int minX,
			out int minY,
			out int maxX,
			out int maxY);

		int width = maxX - minX + 1;
		int height = maxY - minY + 1;

		var mask = new bool[width, height];

		foreach (int index in region)
		{
			int x = index % document.Width;
			int y = index / document.Width;

			mask[x - minX, y - minY] = true;
		}

		return BuildSelectionFromMask(mask, minX, minY);
	}

	public static ImagePixelSelection? BuildSelectionFromMask(
		bool[,] mask,
		int offsetX,
		int offsetY)
	{
		if (mask == null)
			return null;

		int width = mask.GetLength(0);
		int height = mask.GetLength(1);

		if (width <= 0 || height <= 0)
			return null;

		var path = BuildOutlinePath(mask, offsetX, offsetY);

		if (path == null || path.IsEmpty)
		{
			path?.Dispose();
			return null;
		}

		var bitmapMask = new SKBitmap(
			width,
			height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var canvas = new SKCanvas(bitmapMask))
		using (var fill = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Fill,
			Color = SKColors.White,
			BlendMode = SKBlendMode.Src
		})
		{
			canvas.Clear(SKColors.Transparent);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (mask[x, y])
						canvas.DrawPoint(x, y, fill);
				}
			}
		}

		var bounds = new SKRectI(
			offsetX,
			offsetY,
			offsetX + width,
			offsetY + height);

		return new ImagePixelSelection(path, bounds, bitmapMask);
	}

	private static SKPath? BuildOutlinePath(
		bool[,] mask,
		int offsetX,
		int offsetY)
	{
		int width = mask.GetLength(0);
		int height = mask.GetLength(1);

		var edges = new Dictionary<SKPointI, List<SKPointI>>();

		void AddEdge(int x1, int y1, int x2, int y2)
		{
			var a = new SKPointI(x1, y1);
			var b = new SKPointI(x2, y2);

			if (!edges.TryGetValue(a, out var aEdges))
			{
				aEdges = new List<SKPointI>();
				edges[a] = aEdges;
			}

			if (!edges.TryGetValue(b, out var bEdges))
			{
				bEdges = new List<SKPointI>();
				edges[b] = bEdges;
			}

			aEdges.Add(b);
			bEdges.Add(a);
		}

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				if (!mask[x, y])
					continue;

				bool hasTop = y > 0 && mask[x, y - 1];
				bool hasBottom = y < height - 1 && mask[x, y + 1];
				bool hasLeft = x > 0 && mask[x - 1, y];
				bool hasRight = x < width - 1 && mask[x + 1, y];

				int px = offsetX + x;
				int py = offsetY + y;

				if (!hasTop) AddEdge(px, py, px + 1, py);
				if (!hasRight) AddEdge(px + 1, py, px + 1, py + 1);
				if (!hasBottom) AddEdge(px + 1, py + 1, px, py + 1);
				if (!hasLeft) AddEdge(px, py + 1, px, py);
			}
		}

		if (edges.Count == 0)
			return null;

		using var builder = new SKPathBuilder();

		var visited = new HashSet<(SKPointI From, SKPointI To)>();

		foreach (var pair in edges)
		{
			var start = pair.Key;

			foreach (var next in pair.Value)
			{
				if (visited.Contains((start, next)))
					continue;

				var contour = TraceContour(
					edges,
					start,
					next,
					visited);

				if (contour.Count < 3)
					continue;

				builder.MoveTo(contour[0].X, contour[0].Y);

				for (int i = 1; i < contour.Count; i++)
					builder.LineTo(contour[i].X, contour[i].Y);

				builder.Close();
			}
		}

		var path = builder.Detach();

		return path.IsEmpty ? null : path;
	}

	private static List<SKPointI> TraceContour(
		Dictionary<SKPointI, List<SKPointI>> edges,
		SKPointI start,
		SKPointI firstNext,
		HashSet<(SKPointI From, SKPointI To)> visited)
	{
		var contour = new List<SKPointI> { start };

		SKPointI previous = start;
		SKPointI current = firstNext;

		visited.Add((start, firstNext));
		visited.Add((firstNext, start));

		contour.Add(current);

		while (current != start)
		{
			if (!edges.TryGetValue(current, out var neighbors))
				break;

			SKPointI? next = null;

			foreach (var candidate in neighbors)
			{
				if (candidate.Equals(previous))
					continue;

				if (visited.Contains((current, candidate)))
					continue;

				next = candidate;
				break;
			}

			if (next == null)
			{
				foreach (var candidate in neighbors)
				{
					if (candidate.Equals(start))
					{
						next = candidate;
						break;
					}
				}
			}

			if (next == null)
				break;

			visited.Add((current, next.Value));
			visited.Add((next.Value, current));

			previous = current;
			current = next.Value;

			if (current != start)
				contour.Add(current);
		}

		return contour;
	}

	private static List<int> FloodFillRegion(
		ImageTileDocument document,
		int seedX,
		int seedY,
		SKColor seedColor,
		float tolerance,
		ImagePixelSelection? limitSelection)
	{
		int width = document.Width;
		int height = document.Height;

		var visited = new HashSet<int>();
		var region = new List<int>();
		var queue = new Queue<int>();

		int seedIndex = seedY * width + seedX;

		visited.Add(seedIndex);
		queue.Enqueue(seedIndex);

		while (queue.Count > 0)
		{
			int index = queue.Dequeue();

			int x = index % width;
			int y = index / width;

			if (limitSelection != null &&
				limitSelection.HasSelection &&
				!limitSelection.ContainsPixel(x, y))
			{
				continue;
			}

			var current = GetPixel(document, x, y);

			if (!IsWithinTolerance(seedColor, current, tolerance))
				continue;

			region.Add(index);

			TryEnqueue(
				document,
				x - 1,
				y,
				visited,
				queue,
				limitSelection);

			TryEnqueue(
				document,
				x + 1,
				y,
				visited,
				queue,
				limitSelection);

			TryEnqueue(
				document,
				x,
				y - 1,
				visited,
				queue,
				limitSelection);

			TryEnqueue(
				document,
				x,
				y + 1,
				visited,
				queue,
				limitSelection);
		}

		return region;
	}

	private static void TryEnqueue(
		ImageTileDocument document,
		int x,
		int y,
		HashSet<int> visited,
		Queue<int> queue,
		ImagePixelSelection? limitSelection)
	{
		if (x < 0 || y < 0 ||
			x >= document.Width ||
			y >= document.Height)
		{
			return;
		}

		if (limitSelection != null &&
			limitSelection.HasSelection &&
			!limitSelection.ContainsPixel(x, y))
		{
			return;
		}

		int index = y * document.Width + x;

		if (visited.Add(index))
			queue.Enqueue(index);
	}

	private static void GetRegionBounds(
		List<int> region,
		int imageWidth,
		out int minX,
		out int minY,
		out int maxX,
		out int maxY)
	{
		minX = int.MaxValue;
		minY = int.MaxValue;
		maxX = int.MinValue;
		maxY = int.MinValue;

		foreach (int index in region)
		{
			int x = index % imageWidth;
			int y = index / imageWidth;

			if (x < minX) minX = x;
			if (y < minY) minY = y;
			if (x > maxX) maxX = x;
			if (y > maxY) maxY = y;
		}
	}

	private static SKColor GetPixel(
		ImageTileDocument document,
		int x,
		int y)
	{
		int tileX = x / document.TileSize;
		int tileY = y / document.TileSize;

		int localX = x - tileX * document.TileSize;
		int localY = y - tileY * document.TileSize;

		var tile = document.GetOrCreateTile(tileX, tileY);

		return tile.Bitmap.GetPixel(localX, localY);
	}

	private static bool IsWithinTolerance(
		SKColor a,
		SKColor b,
		float tolerance)
	{
		float red = (a.Red - b.Red) / 255f;
		float green = (a.Green - b.Green) / 255f;
		float blue = (a.Blue - b.Blue) / 255f;
		float alpha = (a.Alpha - b.Alpha) / 255f;

		float distance = MathF.Sqrt(
			red * red +
			green * green +
			blue * blue +
			alpha * alpha) * 0.5f;

		return distance <= tolerance;
	}
}

public sealed class ImageSelectionOverlayStyle
{
	public SKColor FillColor { get; set; } = new SKColor(70, 135, 255, 38);
	public SKColor DarkDashColor { get; set; } = new SKColor(0, 0, 0, 220);
	public SKColor LightDashColor { get; set; } = new SKColor(255, 255, 255, 230);
	public SKColor HandleFillColor { get; set; } = SKColors.White;
	public SKColor HandleStrokeColor { get; set; } = new SKColor(25, 25, 25, 230);

	public float StrokeWidth { get; set; } = 1f;
	public float[] DashPattern { get; set; } = new[] { 6f, 4f };
	public float DarkDashPhase { get; set; } = 0f;
	public float LightDashPhase { get; set; } = 5f;

	public SKPaint CreateFillPaint() => new SKPaint
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = FillColor
	};

	public SKPaint CreateDarkDashPaint() => new SKPaint
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = StrokeWidth,
		Color = DarkDashColor,
		PathEffect = SKPathEffect.CreateDash(DashPattern, DarkDashPhase)
	};

	public SKPaint CreateLightDashPaint() => new SKPaint
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = StrokeWidth,
		Color = LightDashColor,
		PathEffect = SKPathEffect.CreateDash(DashPattern, LightDashPhase)
	};

	public SKPaint CreateHandleFillPaint() => new SKPaint
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = HandleFillColor
	};

	public SKPaint CreateHandleStrokePaint() => new SKPaint
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		StrokeWidth = StrokeWidth,
		Color = HandleStrokeColor
	};
}

public sealed class ImageCursorTool : IEditorTool
{
	private readonly ImageEditSession _session;

	private ImageShape? _image;
	private bool _dragging;
	private SKPointI _dragOffset;

	private bool _resizing;
	private ImageSelectionHandle _activeHandle = ImageSelectionHandle.None;
	private SKRectI _resizeStartBounds;
	private SKBitmap? _resizeSourceBitmap;

	/// <summary>Маска фрагмента на момент начала растягивания.</summary>
	private SKBitmap? _resizeSourceMask;
	private SKPointI _resizeAnchor;

	private const float HandleScreenSize = 8f;
	private const float HalfHandleScreenSize = HandleScreenSize * 0.5f;
	private bool _historyScopeOpen;

	public ImageCursorTool(ImageEditSession session)
	{
		_session = session;
	}

	public ShapeToolKind Kind => ShapeToolKind.Select;

	public void OnMouseDown(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		// В локальную, а не сразу в _image. GetEditableImage возвращает null,
		// как только выделение перестало быть ровно этим изображением, —
		// и при клике мимо фигуры перезапись поля обнуляла единственную
		// ссылку на картинку, в которую надо впечатать перемещённый
		// фрагмент. Дальше DropFloatingAndExit получал null, пропускал
		// впечатывание и просто выбрасывал фрагмент: перемещённая часть
		// изображения исчезала.
		var resolved = ImagePaintEngine.GetEditableImage(view, ctx);

		// Изображение недоступно — выходим из режима растрового редактирования.
		if (resolved?.Bitmap == null)
		{
			ResetDragState();

			// Прежнее изображение для впечатывания ещё годится: фрагмент
			// вырезан именно из него.
			DropFloatingAndExit(view, _image);
			return;
		}

		_image = resolved;

		// Клик вне области изображения — то же самое.
		if (!ImageEditGeometry.TryPageToBitmapPixel(_image, pagePoint, out var pixel))
		{
			ResetDragState();
			DropFloatingAndExit(view, _image);
			return;
		}

		var point = ImageEditGeometry.ClampPixel(_image, pixel);
		var floating = _session.FloatingSelection;

		if (floating != null)
		{
			// Маркер изменения размера.
			var handle = HitTestHandle(view, _image, floating, pagePoint);
			if (handle != ImageSelectionHandle.None)
			{
				BeginResize(view, floating, handle);
				return;
			}

			// Клик вне фрагмента — фиксируем его в пикселях и выходим.
			if (!floating.Bounds.Contains(point.X, point.Y))
			{
				ResetDragState();
				DropFloatingAndExit(view, _image);
				return;
			}

			// Начинаем перетаскивание фрагмента.
			_dragging = true;
			_resizing = false;
			_activeHandle = ImageSelectionHandle.None;
			_dragOffset = new SKPointI(
				point.X - floating.Position.X,
				point.Y - floating.Position.Y);

			// Область записи закроется в OnMouseUp.
			_session.BeginSelectionHistoryManual("Move selection");
			_historyScopeOpen = true;

			view.Cursor = Cursors.SizeAll;
			view.Invalidate();
			return;
		}

		if (!_session.Selection.HasSelection)
		{
			ResetDragState();
			DropFloatingAndExit(view, _image);
			return;
		}

		// ВАЖНО:
		// Сначала проверяем маркеры, потому что они стоят на bounding box
		// и могут лежать вне реальной пиксельной маски у Lasso/SmartSelect.
		var selectionHandle = HitTestSelectionHandle(view, _image, pagePoint);
		if (selectionHandle != ImageSelectionHandle.None)
		{
			if (!CreateFloatingSelection(_image))
				return;

			floating = _session.FloatingSelection;
			if (floating == null)
				return;

			BeginResize(view, floating, selectionHandle);
			return;
		}

		// Клик вне маски выделения — выходим.
		if (!_session.Selection.ContainsPixel(point.X, point.Y))
		{
			ResetDragState();
			DropFloatingAndExit(view, _image);
			return;
		}

		// Поднимаем выделенные пиксели в плавающий фрагмент и начинаем тащить.
		if (!CreateFloatingSelection(_image))
			return;

		floating = _session.FloatingSelection;
		if (floating == null || !floating.Bounds.Contains(point.X, point.Y))
			return;

		_dragging = true;
		_resizing = false;
		_activeHandle = ImageSelectionHandle.None;
		_dragOffset = new SKPointI(
			point.X - floating.Position.X,
			point.Y - floating.Position.Y);

		_session.BeginSelectionHistoryManual("Move selection");
		_historyScopeOpen = true;

		view.Cursor = Cursors.SizeAll;
		view.Invalidate();
	}


	public void OnMouseMove(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint)
	{
		var image = _image ?? ImagePaintEngine.GetEditableImage(view, ctx);
		if (image?.Bitmap == null)
		{
			view.Cursor = Cursors.Default;
			return;
		}

		if (_resizing)
		{
			if (!ImageEditGeometry.TryPageToBitmapPixel(image, pagePoint, out var resizePixel))
				return;

			var point = ImageEditGeometry.ClampPixel(image, resizePixel);
			UpdateResize(image, point);

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.Cursor = GetCursorForHandle(_activeHandle);
			view.Invalidate();
			return;
		}

		if (_dragging)
		{
			var floating = _session.FloatingSelection;
			if (floating == null ||
				!ImageEditGeometry.TryPageToBitmapPixel(image, pagePoint, out var movePixel))
			{
				return;
			}

			var point = ImageEditGeometry.ClampPixel(image, movePixel);

			floating.MoveTo(new SKPointI(
				point.X - _dragOffset.X,
				point.Y - _dragOffset.Y));

			_session.Selection.SetRect(
				floating.Bounds,
				image.Bitmap.Width,
				image.Bitmap.Height);

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.Cursor = Cursors.SizeAll;
			view.Invalidate();
			return;
		}

		var floatingSelection = _session.FloatingSelection;
		if (floatingSelection != null)
		{
			var handle = HitTestHandle(view, image, floatingSelection, pagePoint);
			view.Cursor = handle != ImageSelectionHandle.None
				? GetCursorForHandle(handle)
				: (IsPointInsideFloating(image, pagePoint, floatingSelection) ? Cursors.SizeAll : Cursors.Default);
			return;
		}

		if (_session.Selection.HasSelection)
		{
			var handle = HitTestSelectionHandle(view, image, pagePoint);
			view.Cursor = handle != ImageSelectionHandle.None
				? GetCursorForHandle(handle)
				: (IsPointInsideSelection(image, pagePoint) ? Cursors.SizeAll : Cursors.Default);
			return;
		}

		view.Cursor = Cursors.Default;
	}

	public void OnMouseUp(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left)
			return;

		_dragging = false;
		_resizing = false;
		_activeHandle = ImageSelectionHandle.None;

		_resizeSourceBitmap?.Dispose();
		_resizeSourceBitmap = null;

		_resizeSourceMask?.Dispose();
		_resizeSourceMask = null;

		// Закрываем область записи, открытую в OnMouseDown или BeginResize.
		// Перемещение и изменение размера фрагмента меняют только выделение,
		// пиксели изображения не затрагиваются — поэтому здесь достаточно
		// ImageSelectionAction, который создаёт сама область.
		if (_historyScopeOpen)
		{
			_session.EndSelectionHistory();
			_historyScopeOpen = false;
		}

		view.Cursor = Cursors.Default;
		view.Invalidate();
	}


	public void OnKeyDown(
		MainView view,
		EditorInteractionContext ctx,
		Keys keyData)
	{
		var image = ImagePaintEngine.GetEditableImage(view, ctx);
		if (image?.Bitmap == null)
			return;

		if (keyData == Keys.Delete)
		{
			if (_session.FloatingSelection != null)
			{
				_session.ClearFloatingSelection();
				_session.Selection.Clear();

				if (view.CurrentPage != null)
					view.CurrentPage.doc.need_save = true;

				view.SetImageEditTool(ImageEditToolKind.RectSelect);
				return;
			}

			if (_session.Selection.HasSelection)
			{
				DeleteSelectionPixels(image);
				_session.Selection.Clear();

				if (view.CurrentPage != null)
					view.CurrentPage.doc.need_save = true;

				view.SetImageEditTool(ImageEditToolKind.RectSelect);
				return;
			}
		}

		if (keyData == Keys.Escape)
		{
			CommitFloatingSelection(image);

			if (view.CurrentPage != null)
				view.CurrentPage.doc.need_save = true;

			view.Invalidate();
			return;
		}
	}

	private void BeginResize(
		MainView view,
		FloatingPixelSelection floating,
		ImageSelectionHandle handle)
	{
		_dragging = false;
		_resizing = true;
		_activeHandle = handle;
		_resizeStartBounds = floating.Bounds;

		_resizeSourceBitmap?.Dispose();
		_resizeSourceBitmap = floating.Bitmap.Copy();

		_resizeSourceMask?.Dispose();
		_resizeSourceMask = floating.Mask?.Copy();

		_resizeAnchor = GetAnchorPoint(_resizeStartBounds, handle);

		// Область записи закроется в OnMouseUp. Проверка на уже открытую нужна
		// потому, что BeginResize вызывается и напрямую из OnMouseDown,
		// и после CreateFloatingSelection.
		if (!_historyScopeOpen)
		{
			_session.BeginSelectionHistoryManual("Resize selection");
			_historyScopeOpen = true;
		}

		view.Cursor = GetCursorForHandle(handle);
		view.Invalidate();
	}

	private void UpdateResize(ImageShape image, SKPointI currentPixel)
	{
		var floating = _session.FloatingSelection;
		if (floating == null || _resizeSourceBitmap == null)
			return;

		var newBounds = BuildResizedBounds(
			_resizeStartBounds,
			_resizeAnchor,
			currentPixel,
			_activeHandle,
			image.Bitmap!.Width,
			image.Bitmap.Height);

		if (newBounds.Width < 1 || newBounds.Height < 1)
			return;

		var resized = new SKBitmap(
			newBounds.Width,
			newBounds.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var canvas = new SKCanvas(resized))
		{
			canvas.Clear(SKColors.Transparent);

			// BlendMode.SrcOver поверх очищенного холста эквивалентен Src,
			// поэтому отдельный SKPaint здесь не нужен — на каждое движение
			// мыши это была лишняя аллокация.
			canvas.DrawBitmap(
				_resizeSourceBitmap,
				SKRect.Create(0, 0, _resizeSourceBitmap.Width, _resizeSourceBitmap.Height),
				SKRect.Create(0, 0, newBounds.Width, newBounds.Height),
				new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
				null);
		}

		// Маска растягивается тем же преобразованием и тем же способом
		// выборки: разойдись они хоть на пиксель, по краю фрагмента
		// пошла бы кайма из непритёртых точек.
		var resizedMask = ScaleMask(_resizeSourceMask, newBounds.Width, newBounds.Height);

		floating.Bitmap.Dispose();
		floating.Bitmap = resized;

		floating.Mask?.Dispose();
		floating.Mask = resizedMask;

		floating.Position = new SKPointI(newBounds.Left, newBounds.Top);

		_session.Selection.SetRect(
			newBounds,
			image.Bitmap.Width,
			image.Bitmap.Height);
	}

	/// <summary>
	/// Строит маску фрагмента по выделению.
	///
	/// Alpha8: нужен один бит на пиксель, а не цвет. Полный BGRA занял бы
	/// вчетверо больше на каждый поднятый фрагмент, а читается из него
	/// всё равно только альфа.
	///
	/// Для прямоугольного выделения возвращается null: маска «все пиксели
	/// внутри» ничего не описывает и только тратила бы память и время
	/// на каждое движение мыши при растягивании.
	/// </summary>
	private static SKBitmap? BuildFloatingMask(
		ImagePixelSelection selection,
		int left,
		int top,
		int width,
		int height)
	{
		if (width <= 0 || height <= 0)
			return null;

		bool allInside = true;
		var mask = new SKBitmap(width, height, SKColorType.Alpha8, SKAlphaType.Premul);

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				bool inside = selection.ContainsPixel(left + x, top + y);

				if (!inside)
					allInside = false;

				mask.SetPixel(x, y, inside ? SKColors.White : SKColors.Transparent);
			}
		}

		if (allInside)
		{
			mask.Dispose();
			return null;
		}

		return mask;
	}

	/// <summary>
	/// Растягивает маску под новый размер фрагмента.
	///
	/// Выборка ближайшего соседа — та же, что и у самого фрагмента:
	/// сглаживание дало бы полупрозрачную кайму по краю маски, и по
	/// границе фрагмента появились бы пиксели, которые ни затираются,
	/// ни оставляются.
	/// </summary>
	private static SKBitmap? ScaleMask(SKBitmap? source, int width, int height)
	{
		if (source == null || width <= 0 || height <= 0)
			return null;

		var scaled = new SKBitmap(width, height, SKColorType.Alpha8, SKAlphaType.Premul);

		using (var canvas = new SKCanvas(scaled))
		{
			canvas.Clear(SKColors.Transparent);

			canvas.DrawBitmap(
				source,
				SKRect.Create(0, 0, source.Width, source.Height),
				SKRect.Create(0, 0, width, height),
				new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
				null);
		}

		return scaled;
	}

	/// <summary>Сброс состояния перетаскивания и изменения размера.</summary>
	private void ResetDragState()
	{
		_dragging = false;
		_resizing = false;
		_activeHandle = ImageSelectionHandle.None;

		_resizeSourceBitmap?.Dispose();
		_resizeSourceBitmap = null;

		_resizeSourceMask?.Dispose();
		_resizeSourceMask = null;

		if (_historyScopeOpen)
		{
			_session.AbortSelectionHistory();
			_historyScopeOpen = false;
		}
	}

	/// <summary>
	/// Фиксирует плавающий фрагмент в пикселях, сбрасывает выделение и выходит
	/// из режима растрового редактирования.
	///
	/// Заменяет три идентичные копии этого кода, которые были в OnMouseDown.
	/// </summary>
	private void DropFloatingAndExit(MainView view, ImageShape? image)
	{
		if (image != null)
		{
			if (image.ActiveStroke != null)
			{
				image.RebuildBitmapFromTiles();
				image.EndActiveStrokePreview();
			}

			// Впечатывание фрагмента меняет пиксели — свой шаг истории.
			CommitFloatingSelection(image);
		}

		using (_session.BeginSelectionHistory("Clear selection"))
		{
			_session.ClearSelectionAndFloating();
		}

		_session.ActiveKind = ImageEditToolKind.None;
		_session.mainForm.SetEditorMode(EditorMode.Curve);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Cursor = Cursors.Default;
		view.Invalidate();
	}


	/// <summary>
	/// Стирает пиксели выделения из тайлов, оставляя их прозрачными.
	/// Возвращает false, если стирать нечего.
	/// </summary>
	private bool EraseSelectionFromTiles(ImageShape image)
	{
		var doc = image.tileDoc;
		var selection = _session.Selection;

		if (doc == null || !selection.HasSelection || selection.Mask == null)
			return false;

		var bounds = selection.Bounds;
		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return false;

		int tileSize = doc.TileSize;

		int tileX0 = Math.Max(0, bounds.Left / tileSize);
		int tileY0 = Math.Max(0, bounds.Top / tileSize);
		int tileX1 = Math.Min(doc.TileCountX - 1, (bounds.Right - 1) / tileSize);
		int tileY1 = Math.Min(doc.TileCountY - 1, (bounds.Bottom - 1) / tileSize);

		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		// DstOut: dst = dst * (1 - srcAlpha). Там, где маска непрозрачна,
		// пиксель тайла обнуляется.
		using var erase = new SKPaint
		{
			BlendMode = SKBlendMode.DstOut,
			IsAntialias = false
		};

		for (int ty = tileY0; ty <= tileY1; ty++)
		{
			for (int tx = tileX0; tx <= tileX1; tx++)
			{
				var tile = doc.GetOrCreateTile(tx, ty);
				var tileBounds = doc.GetTileBounds(tx, ty);

				if (SKRectI.Intersect(tileBounds, bounds).IsEmpty)
					continue;

				using var canvas = new SKCanvas(tile.Bitmap);

				canvas.DrawBitmap(
					selection.Mask,
					bounds.Left - tileBounds.Left,
					bounds.Top - tileBounds.Top,
					sampling,
					erase);

				tile.InvalidatePreview();
			}
		}

		image.MarkTilesDirty();
		return true;
	}

	private void DeleteSelectionPixels(ImageShape image)
	{
		if (image.tileDoc == null || !_session.Selection.HasSelection)
			return;

		var bounds = _session.Selection.Bounds;

		var tilesAction = ImageTilesAction.Capture(
			"Delete selection",
			image,
			SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height));

		if (!EraseSelectionFromTiles(image))
		{
			tilesAction?.Discard();
			return;
		}

		image.RebuildBitmapFromTiles();
		image.InvalidateAdjustmentPaint();

		if (tilesAction != null)
		{
			tilesAction.CaptureAfter();
			_session.History?.Push(tilesAction);
		}
	}


	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		var image = _image ?? ImagePaintEngine.GetEditableImage(view, ctx);

		if (image?.Bitmap == null)
			return;

		DrawFloatingBitmap(canvas, view, image);

		var style = _session.OverlayStyle;

		using var darkDash = style.CreateDarkDashPaint();
		using var lightDash = style.CreateLightDashPaint();
		using var handleFill = style.CreateHandleFillPaint();
		using var handleStroke = style.CreateHandleStrokePaint();

		SKRect screenBounds;

		if (_session.FloatingSelection != null)
		{
			// Floating bitmap после move/resize визуально имеет прямоугольную форму.
			var floating = _session.FloatingSelection;

			screenBounds = ImageEditGeometry.BitmapRectToScreen(
				view,
				image,
				floating.Bounds);

			canvas.DrawRect(screenBounds, darkDash);
			canvas.DrawRect(screenBounds, lightDash);
		}
		else if (_session.Selection.HasSelection)
		{
			// Обычный lasso/smart selection отображается по точному path.
			using var screenPath = PixelPathToScreenPath(
				view,
				image,
				_session.Selection.PathPixels);

			canvas.DrawPath(screenPath, darkDash);
			canvas.DrawPath(screenPath, lightDash);

			// Handles всё равно берём от outer bounds.
			screenBounds = ImageEditGeometry.BitmapRectToScreen(
				view,
				image,
				_session.Selection.Bounds);
		}
		else
		{
			return;
		}

		foreach (var handleRect in GetHandleRects(screenBounds).Values)
		{
			canvas.DrawRect(handleRect, handleFill);
			canvas.DrawRect(handleRect, handleStroke);
		}
	}

	private static SKPath PixelPathToScreenPath(MainView view, ImageShape image, SKPath pixelPath)
	{
		var result = new SKPath();

		using var iterator = pixelPath.CreateRawIterator();
		Span<SKPoint> points = stackalloc SKPoint[4];

		while (true)
		{
			var verb = iterator.Next(points);

			if (verb == SKPathVerb.Done)
				break;

			switch (verb)
			{
				case SKPathVerb.Move:
					result.MoveTo(PixelToScreen(view, image, points[0]));
					break;

				case SKPathVerb.Line:
					result.LineTo(PixelToScreen(view, image, points[1]));
					break;

				case SKPathVerb.Quad:
					result.QuadTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]));
					break;

				case SKPathVerb.Cubic:
					result.CubicTo(
						PixelToScreen(view, image, points[1]),
						PixelToScreen(view, image, points[2]),
						PixelToScreen(view, image, points[3]));
					break;

				case SKPathVerb.Close:
					result.Close();
					break;
			}
		}

		return result;
	}

	private static SKPoint PixelToScreen(MainView view, ImageShape image, SKPoint pixel)
	{
		return ImageEditGeometry.BitmapPixelToScreen(view, image, pixel);
	}

	private void DrawFloatingBitmap(
		SKCanvas canvas,
		MainView view,
		ImageShape image)
	{
		var floating = _session.FloatingSelection;
		if (floating == null)
			return;

		var topLeft = ImageEditGeometry.BitmapPixelToScreen(
			view,
			image,
			new SKPoint(floating.Position.X, floating.Position.Y));

		var bottomRight = ImageEditGeometry.BitmapPixelToScreen(
			view,
			image,
			new SKPoint(floating.Position.X + floating.Width, floating.Position.Y + floating.Height));

		var destination = new SKRect(
			MathF.Min(topLeft.X, bottomRight.X),
			MathF.Min(topLeft.Y, bottomRight.Y),
			MathF.Max(topLeft.X, bottomRight.X),
			MathF.Max(topLeft.Y, bottomRight.Y));

		using var paint = new SKPaint
		{
			BlendMode = SKBlendMode.SrcOver,
			IsAntialias = false
		};

		canvas.DrawBitmap(
			floating.Bitmap,
			SKRect.Create(0, 0, floating.Width, floating.Height),
			destination,
			new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
			paint);
	}

	/// <summary>
	/// Поднимает выделенные пиксели из тайлов в плавающий фрагмент.
	/// Меняет и пиксели, и выделение, поэтому оформляется одной группой истории.
	/// </summary>
	private bool CreateFloatingSelection(ImageShape image)
	{
		if (image.tileDoc == null || !_session.Selection.HasSelection)
			return false;

		var selection = _session.Selection.Bounds;
		if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
			return false;

		int imageWidth = image.tileDoc.Width;
		int imageHeight = image.tileDoc.Height;

		int left = Math.Clamp(selection.Left, 0, imageWidth);
		int top = Math.Clamp(selection.Top, 0, imageHeight);
		int right = Math.Clamp(selection.Right, 0, imageWidth);
		int bottom = Math.Clamp(selection.Bottom, 0, imageHeight);

		if (right - left <= 0 || bottom - top <= 0)
			return false;

		var undo = _session.History;
		undo?.BeginGroup("Lift selection");

		try
		{
			var tilesAction = ImageTilesAction.Capture(
				"Lift selection",
				image,
				SKRect.Create(left, top, right - left, bottom - top));

			// Вырезаем содержимое выделения с применением маски. Хелпер читает
			// image.Bitmap, который синхронен тайлам на этот момент.
			var floatingBitmap = ImageSelectionClipboardHelper.ExtractSelectionBitmap(
				image,
				_session.Selection);

			if (floatingBitmap == null)
			{
				tilesAction?.Discard();
				return false;
			}

			// Освобождаем исходное место.
			EraseSelectionFromTiles(image);

			// Маска снимается с того же выделения, которым вырезали:
			// у лассо углы прямоугольника фрагменту не принадлежат.
			var floatingMask = BuildFloatingMask(
				_session.Selection,
				left,
				top,
				right - left,
				bottom - top);

			using (_session.BeginSelectionHistory("Lift selection"))
			{
				_session.SetFloatingSelection(new FloatingPixelSelection(
					floatingBitmap,
					new SKPointI(left, top),
					floatingMask));
			}

			image.RebuildBitmapFromTiles();
			image.InvalidateAdjustmentPaint();

			if (tilesAction != null)
			{
				tilesAction.CaptureAfter();
				undo?.Push(tilesAction);
			}

			return true;
		}
		finally
		{
			undo?.CommitGroup();
		}
	}


	/// <summary>
	/// Впечатывает плавающий фрагмент в тайлы. Логика вынесена в общий хелпер,
	/// который использует и ImageRectSelectTool: раньше это были три отдельные
	/// копии одного и того же кода.
	/// </summary>
	private void CommitFloatingSelection(ImageShape image)
	{
		if (_session.FloatingSelection == null || image.tileDoc == null)
			return;

		ImageSelectionClipboardHelper.CommitFloatingIfAny(
			image,
			_session,
			_session.History);
	}


	private ImageSelectionHandle HitTestSelectionHandle(
		MainView view,
		ImageShape image,
		SKPoint pagePoint)
	{
		if (!_session.Selection.HasSelection)
			return ImageSelectionHandle.None;

		var screenRect = ImageEditGeometry.BitmapRectToScreen(
			view,
			image,
			_session.Selection.Bounds);

		return HitTestHandleRects(view.PageToScreen(pagePoint), screenRect);
	}

	private ImageSelectionHandle HitTestHandle(
		MainView view,
		ImageShape image,
		FloatingPixelSelection floating,
		SKPoint pagePoint)
	{
		var screenRect = ImageEditGeometry.BitmapRectToScreen(
			view,
			image,
			floating.Bounds);

		return HitTestHandleRects(view.PageToScreen(pagePoint), screenRect);
	}

	private ImageSelectionHandle HitTestHandleRects(SKPoint screenPoint, SKRect screenRect)
	{
		var handles = GetHandleRects(screenRect);

		foreach (var pair in handles)
		{
			if (pair.Value.Contains(screenPoint.X, screenPoint.Y))
				return pair.Key;
		}

		return ImageSelectionHandle.None;
	}

	private Dictionary<ImageSelectionHandle, SKRect> GetHandleRects(SKRect rect)
	{
		float left = rect.Left;
		float top = rect.Top;
		float right = rect.Right;
		float bottom = rect.Bottom;
		float midX = rect.MidX;
		float midY = rect.MidY;

		return new Dictionary<ImageSelectionHandle, SKRect>
		{
			[ImageSelectionHandle.TopLeft] = CreateHandleRect(left, top),
			[ImageSelectionHandle.Top] = CreateHandleRect(midX, top),
			[ImageSelectionHandle.TopRight] = CreateHandleRect(right, top),
			[ImageSelectionHandle.Right] = CreateHandleRect(right, midY),
			[ImageSelectionHandle.BottomRight] = CreateHandleRect(right, bottom),
			[ImageSelectionHandle.Bottom] = CreateHandleRect(midX, bottom),
			[ImageSelectionHandle.BottomLeft] = CreateHandleRect(left, bottom),
			[ImageSelectionHandle.Left] = CreateHandleRect(left, midY)
		};
	}

	private static SKRect CreateHandleRect(float cx, float cy)
	{
		return new SKRect(
			cx - HalfHandleScreenSize,
			cy - HalfHandleScreenSize,
			cx + HalfHandleScreenSize,
			cy + HalfHandleScreenSize);
	}

	private static SKPointI GetAnchorPoint(SKRectI bounds, ImageSelectionHandle handle)
	{
		return handle switch
		{
			ImageSelectionHandle.TopLeft => new SKPointI(bounds.Right, bounds.Bottom),
			ImageSelectionHandle.Top => new SKPointI(0, bounds.Bottom),
			ImageSelectionHandle.TopRight => new SKPointI(bounds.Left, bounds.Bottom),
			ImageSelectionHandle.Right => new SKPointI(bounds.Left, 0),
			ImageSelectionHandle.BottomRight => new SKPointI(bounds.Left, bounds.Top),
			ImageSelectionHandle.Bottom => new SKPointI(0, bounds.Top),
			ImageSelectionHandle.BottomLeft => new SKPointI(bounds.Right, bounds.Top),
			ImageSelectionHandle.Left => new SKPointI(bounds.Right, 0),
			_ => new SKPointI(bounds.Left, bounds.Top)
		};
	}

	private static SKRectI BuildResizedBounds(
		SKRectI start,
		SKPointI anchor,
		SKPointI current,
		ImageSelectionHandle handle,
		int bitmapWidth,
		int bitmapHeight)
	{
		int left = start.Left;
		int top = start.Top;
		int right = start.Right;
		int bottom = start.Bottom;

		switch (handle)
		{
			case ImageSelectionHandle.TopLeft:
				left = current.X;
				top = current.Y;
				right = anchor.X;
				bottom = anchor.Y;
				break;

			case ImageSelectionHandle.Top:
				left = start.Left;
				right = start.Right;
				top = current.Y;
				bottom = anchor.Y;
				break;

			case ImageSelectionHandle.TopRight:
				left = anchor.X;
				top = current.Y;
				right = current.X;
				bottom = anchor.Y;
				break;

			case ImageSelectionHandle.Right:
				left = anchor.X;
				right = current.X;
				top = start.Top;
				bottom = start.Bottom;
				break;

			case ImageSelectionHandle.BottomRight:
				left = anchor.X;
				top = anchor.Y;
				right = current.X;
				bottom = current.Y;
				break;

			case ImageSelectionHandle.Bottom:
				left = start.Left;
				right = start.Right;
				top = anchor.Y;
				bottom = current.Y;
				break;

			case ImageSelectionHandle.BottomLeft:
				left = current.X;
				top = anchor.Y;
				right = anchor.X;
				bottom = current.Y;
				break;

			case ImageSelectionHandle.Left:
				left = current.X;
				right = anchor.X;
				top = start.Top;
				bottom = start.Bottom;
				break;
		}

		if (left > right)
			(left, right) = (right, left);

		if (top > bottom)
			(top, bottom) = (bottom, top);

		left = Math.Clamp(left, 0, bitmapWidth - 1);
		top = Math.Clamp(top, 0, bitmapHeight - 1);
		right = Math.Clamp(right, left + 1, bitmapWidth);
		bottom = Math.Clamp(bottom, top + 1, bitmapHeight);

		return new SKRectI(left, top, right, bottom);
	}

	private bool IsPointInsideFloating(
		ImageShape image,
		SKPoint pagePoint,
		FloatingPixelSelection floating)
	{
		if (!ImageEditGeometry.TryPageToBitmapPixel(image, pagePoint, out var pixel))
			return false;

		var p = ImageEditGeometry.ClampPixel(image, pixel);
		return floating.Bounds.Contains(p.X, p.Y);
	}

	private bool IsPointInsideSelection(
		ImageShape image,
		SKPoint pagePoint)
	{
		if (!_session.Selection.HasSelection)
			return false;

		if (!ImageEditGeometry.TryPageToBitmapPixel(image, pagePoint, out var pixel))
			return false;

		var p = ImageEditGeometry.ClampPixel(image, pixel);
		return _session.Selection.Contains(p.X, p.Y);
	}

	private static Cursor GetCursorForHandle(ImageSelectionHandle handle)
	{
		return handle switch
		{
			ImageSelectionHandle.TopLeft => Cursors.SizeNWSE,
			ImageSelectionHandle.BottomRight => Cursors.SizeNWSE,
			ImageSelectionHandle.TopRight => Cursors.SizeNESW,
			ImageSelectionHandle.BottomLeft => Cursors.SizeNESW,
			ImageSelectionHandle.Top => Cursors.SizeNS,
			ImageSelectionHandle.Bottom => Cursors.SizeNS,
			ImageSelectionHandle.Left => Cursors.SizeWE,
			ImageSelectionHandle.Right => Cursors.SizeWE,
			_ => Cursors.Default
		};
	}
}

public sealed class ImageEraserTool : ImagePaintToolBase
{
	public ImageEraserTool(ImageEditSession session) : base(session) { }

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.Erase;

	protected override SKColor GetStrokeColor(MouseButtons button)
		=> SKColors.Transparent;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> 1f;
}

public sealed class ImageBrushTool : ImagePaintToolBase
{
	public ImageBrushTool(ImageEditSession session) : base(session) { }

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.Paint;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> color.Alpha / 255f;
}

public sealed class ImageContrastTool : ImagePaintToolBase
{
	public ImageContrastTool(ImageEditSession session) : base(session) { }

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.Contrast;

	protected override SKColor GetStrokeColor(MouseButtons button)
		=> SKColors.Transparent;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> 1f;
}

public sealed class ImageLightenTool : ImagePaintToolBase
{
	public ImageLightenTool(ImageEditSession session)
		: base(session)
	{
	}

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.Lighten;

	protected override SKColor GetStrokeColor(MouseButtons button)
		=> SKColors.Transparent;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> 1f;
}

public sealed class ImageDarkenTool : ImagePaintToolBase
{
	public ImageDarkenTool(ImageEditSession session)
		: base(session)
	{
	}

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.Darken;

	protected override SKColor GetStrokeColor(MouseButtons button)
		=> SKColors.Transparent;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> 1f;
}

public sealed class ImageBWTool : ImagePaintToolBase
{
	public ImageBWTool(ImageEditSession session)
		: base(session)
	{
	}

	public override ShapeToolKind Kind => ShapeToolKind.Select;

	protected override ImageStrokeMode GetStrokeMode()
		=> ImageStrokeMode.BW;

	protected override SKColor GetStrokeColor(MouseButtons button)
		=> SKColors.Transparent;

	protected override float GetStrokeOpacity(MouseButtons button, SKColor color)
		=> 1f;
}

public sealed class ImageFillTool : IEditorTool
{
	private readonly ImageEditSession session;
	private ImageShape? image;

	public ImageFillTool(ImageEditSession session)
	{
		this.session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public ShapeToolKind Kind => ShapeToolKind.Select;

	public void OnMouseDown(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
		if (button != MouseButtons.Left && button != MouseButtons.Right)
			return;

		image = ImagePaintEngine.GetEditableImage(view, ctx);
		if (image?.Bitmap == null || image.tileDoc == null)
			return;

		ImageSelectionClipboardHelper.CommitFloatingIfAny(image, session);

		if (!ImageEditGeometry.TryPageToBitmapPixel(image, pagePoint, out var pixel))
			return;

		var seed = ImageEditGeometry.ClampPixel(image, pixel);

		ImagePixelSelection? limitSelection = null;
		if (session.Selection.HasSelection && session.Selection.ContainsPixel(seed.X, seed.Y))
			limitSelection = session.Selection;

		using var region = ImageSmartSelectionRasterizer.BuildSelection(
			image,
			seed.X,
			seed.Y,
			Settings.image_tool_tolerance,
			limitSelection);

		if (region == null || !region.HasSelection)
			return;

		var fillColor = button == MouseButtons.Right
			? Settings.image_paint_back_color
			: Settings.image_paint_fore_color;

		ApplyFill(image, region, fillColor);

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();
		view.Invalidate();
	}

	public void OnMouseMove(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint)
	{
		view.Cursor = Cursors.Cross;
	}

	public void OnMouseUp(
		MainView view,
		EditorInteractionContext ctx,
		SKPoint pagePoint,
		MouseButtons button)
	{
	}

	public void OnKeyDown(
		MainView view,
		EditorInteractionContext ctx,
		Keys keyData)
	{
		if (keyData == Keys.Escape)
			view.SetTool(ShapeToolKind.Select);
	}

	public void DrawOverlay(
		SKCanvas canvas,
		MainView view,
		EditorInteractionContext ctx)
	{
	}

	private static void ApplyFill(
		ImageShape image,
		ImagePixelSelection selection,
		SKColor fillColor)
	{
		if (image.tileDoc == null || !selection.HasSelection)
			return;

		int tileSize = image.tileDoc.TileSize;
		int tileX0 = selection.Bounds.Left / tileSize;
		int tileY0 = selection.Bounds.Top / tileSize;
		int tileX1 = (selection.Bounds.Right - 1) / tileSize;
		int tileY1 = (selection.Bounds.Bottom - 1) / tileSize;

		for (int ty = tileY0; ty <= tileY1; ty++)
		{
			for (int tx = tileX0; tx <= tileX1; tx++)
			{
				var tile = image.tileDoc.GetOrCreateTile(tx, ty);
				var tileBounds = image.tileDoc.GetTileBounds(tx, ty);
				var affected = SKRectI.Intersect(tileBounds, selection.Bounds);

				if (affected.IsEmpty)
					continue;

				bool changed = false;

				for (int y = affected.Top; y < affected.Bottom; y++)
				{
					for (int x = affected.Left; x < affected.Right; x++)
					{
						if (!selection.ContainsPixel(x, y))
							continue;

						int localX = x - tileBounds.Left;
						int localY = y - tileBounds.Top;

						if (tile.Bitmap.GetPixel(localX, localY) == fillColor)
							continue;

						tile.Bitmap.SetPixel(localX, localY, fillColor);
						changed = true;
					}
				}

				if (changed)
					tile.InvalidatePreview();
			}
		}
	}
}

internal static class ImageEditGeometry
{
	public static bool TryPageToBitmapPixel(
		ImageShape image,
		SKPoint pagePoint,
		out SKPoint pixel)
	{
		return ImagePaintEngine.TryWorldToBitmapPixel(
			image,
			pagePoint,
			out pixel);
	}

	public static SKPoint BitmapPixelToPage(
		ImageShape image,
		SKPoint pixel)
	{
		if (image.Bitmap == null)
			return SKPoint.Empty;

		var rect = image.ImageLocalRect;

		float x = rect.Left +
				  pixel.X / Math.Max(1, image.Bitmap.Width - 1) * rect.Width;

		float y = rect.Top +
				  pixel.Y / Math.Max(1, image.Bitmap.Height - 1) * rect.Height;

		return image.LocalToWorld(new SKPoint(x, y));
	}

	public static SKPoint BitmapPixelToScreen(
		MainView view,
		ImageShape image,
		SKPoint pixel)
	{
		return view.PageToScreen(BitmapPixelToPage(image, pixel));
	}

	public static SKRect BitmapRectToScreen(
		MainView view,
		ImageShape image,
		SKRectI bitmapRect)
	{
		var topLeft = BitmapPixelToScreen(
			view,
			image,
			new SKPoint(bitmapRect.Left, bitmapRect.Top));

		var bottomRight = BitmapPixelToScreen(
			view,
			image,
			new SKPoint(bitmapRect.Right, bitmapRect.Bottom));

		return new SKRect(
			MathF.Min(topLeft.X, bottomRight.X),
			MathF.Min(topLeft.Y, bottomRight.Y),
			MathF.Max(topLeft.X, bottomRight.X),
			MathF.Max(topLeft.Y, bottomRight.Y));
	}

	public static SKPointI ClampPixel(
		ImageShape image,
		SKPoint pixel)
	{
		if (image.Bitmap == null)
			return SKPointI.Empty;

		return new SKPointI(
			Math.Clamp((int)MathF.Round(pixel.X), 0, image.Bitmap.Width),
			Math.Clamp((int)MathF.Round(pixel.Y), 0, image.Bitmap.Height));
	}
}

public sealed class ImageTileSurface
{
	public ImageTileDocument Document { get; }
	public bool IsDirtyComposite { get; private set; }
	private SKBitmap? _composited;

	public ImageTileSurface(SKBitmap source, int tileSize = 128)
	{
		Document = ImageTileDocument.FromBitmap(source, tileSize);
	}

	public void MarkDirty()
	{
		IsDirtyComposite = true;
	}

	public SKBitmap GetCompositeBitmap()
	{
		if (_composited == null || IsDirtyComposite)
		{
			_composited?.Dispose();
			_composited = Document.ComposeFullBitmap();
			IsDirtyComposite = false;
		}

		return _composited;
	}
}

public sealed class ImageTile : IDisposable
{
	public int TileX { get; }
	public int TileY { get; }
	public SKBitmap Bitmap { get; }
	public SKImage? Preview { get; private set; }

	public ImageTile(int tileX, int tileY, int width, int height)
	{
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width),
				$"Tile width must be > 0. tile=({tileX},{tileY}), width={width}, height={height}");

		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height),
				$"Tile height must be > 0. tile=({tileX},{tileY}), width={width}, height={height}");

		TileX = tileX;
		TileY = tileY;

		Bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

		if (Bitmap.IsEmpty)
			throw new InvalidOperationException(
				$"Failed to create tile bitmap. tile=({tileX},{tileY}), width={width}, height={height}");
	}

	public SKImage GetPreview()
	{
		Preview ??= SKImage.FromBitmap(Bitmap);
		return Preview;
	}

	public void InvalidatePreview()
	{
		Preview?.Dispose();
		Preview = null;
	}

	public void Dispose()
	{
		Preview?.Dispose();
		Bitmap.Dispose();
	}
}

public sealed class ImageTilePatch : IDisposable
{
	public int TileX { get; }
	public int TileY { get; }
	public SKRectI LocalRect { get; }
	public SKBitmap Before { get; }
	public SKBitmap After { get; }

	public ImageTilePatch(int tileX, int tileY, SKRectI localRect, SKBitmap before, SKBitmap after)
	{
		TileX = tileX;
		TileY = tileY;
		LocalRect = localRect;
		Before = before ?? throw new ArgumentNullException(nameof(before));
		After = after ?? throw new ArgumentNullException(nameof(after));
	}

	public void ApplyBefore(ImageTileDocument document) => Apply(document, Before);
	public void ApplyAfter(ImageTileDocument document) => Apply(document, After);

	private void Apply(ImageTileDocument document, SKBitmap source)
	{
		if (document == null)
			throw new ArgumentNullException(nameof(document));

		var tile = document.GetOrCreateTile(TileX, TileY);

		using var canvas = new SKCanvas(tile.Bitmap);
		using var paint = new SKPaint { BlendMode = SKBlendMode.Src };

		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		canvas.DrawBitmap(
			source,
			new SKPoint(LocalRect.Left, LocalRect.Top),
			sampling,
			paint);

		tile.InvalidatePreview();
	}

	public void Dispose()
	{
		Before.Dispose();
		After.Dispose();
	}
}

public readonly record struct ImageTileKey(int X, int Y);

public sealed class ImageTileDocument : IDisposable
{
	private readonly Dictionary<ImageTileKey, ImageTile> _tiles = new();

	public int Width { get; }
	public int Height { get; }
	public int TileSize { get; }

	public int TileCountX => (Width + TileSize - 1) / TileSize;
	public int TileCountY => (Height + TileSize - 1) / TileSize;

	public ImageTileDocument(int width, int height, int tileSize)
	{
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height));

		if (tileSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(tileSize));

		Width = width;
		Height = height;
		TileSize = tileSize;
	}

	public static ImageTileDocument FromBitmap(SKBitmap source, int tileSize)
	{
		if (source == null)
			throw new ArgumentNullException(nameof(source));

		var doc = new ImageTileDocument(source.Width, source.Height, tileSize);

		for (int ty = 0; ty < doc.TileCountY; ty++)
		{
			for (int tx = 0; tx < doc.TileCountX; tx++)
			{
				var bounds = doc.GetTileBounds(tx, ty);
				var tile = doc.GetOrCreateTile(tx, ty);

				using var canvas = new SKCanvas(tile.Bitmap);
				canvas.Clear(SKColors.Transparent);

				var srcRect = new SKRect(
					bounds.Left,
					bounds.Top,
					bounds.Right,
					bounds.Bottom);

				var dstRect = new SKRect(0, 0, bounds.Width, bounds.Height);

				canvas.DrawBitmap(
					source,
					srcRect,
					dstRect,
					new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
					null);

				tile.InvalidatePreview();
			}
		}

		return doc;
	}

	public ImageTile GetOrCreateTile(int tx, int ty)
	{
		if (tx < 0 || tx >= TileCountX)
			throw new ArgumentOutOfRangeException(nameof(tx),
				$"Tile X index is out of range. tx={tx}, valid=[0..{TileCountX - 1}]");

		if (ty < 0 || ty >= TileCountY)
			throw new ArgumentOutOfRangeException(nameof(ty),
				$"Tile Y index is out of range. ty={ty}, valid=[0..{TileCountY - 1}]");

		var key = new ImageTileKey(tx, ty);
		if (_tiles.TryGetValue(key, out var existing))
			return existing;

		var rect = GetTileBounds(tx, ty);
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			throw new InvalidOperationException(
				$"Invalid tile bounds for tile ({tx},{ty}): " +
				$"left={rect.Left}, top={rect.Top}, right={rect.Right}, bottom={rect.Bottom}, " +
				$"width={rect.Width}, height={rect.Height}");
		}

		var tile = new ImageTile(tx, ty, rect.Width, rect.Height);
		_tiles[key] = tile;
		return tile;
	}

	public IEnumerable<ImageTile> GetTilesIntersecting(SKRect imageRect)
	{
		if (imageRect.IsEmpty)
			yield break;

		int x0 = Math.Max(0, (int)Math.Floor(imageRect.Left / TileSize));
		int y0 = Math.Max(0, (int)Math.Floor(imageRect.Top / TileSize));
		int x1 = Math.Min(TileCountX - 1, (int)Math.Floor((imageRect.Right - 1) / TileSize));
		int y1 = Math.Min(TileCountY - 1, (int)Math.Floor((imageRect.Bottom - 1) / TileSize));

		if (x1 < x0 || y1 < y0)
			yield break;

		for (int ty = y0; ty <= y1; ty++)
		{
			for (int tx = x0; tx <= x1; tx++)
			{
				yield return GetOrCreateTile(tx, ty);
			}
		}
	}

	public SKRectI GetTileBounds(int tx, int ty)
	{
		if (tx < 0 || tx >= TileCountX)
			throw new ArgumentOutOfRangeException(nameof(tx),
				$"Tile X index is out of range. tx={tx}, valid=[0..{TileCountX - 1}]");

		if (ty < 0 || ty >= TileCountY)
			throw new ArgumentOutOfRangeException(nameof(ty),
				$"Tile Y index is out of range. ty={ty}, valid=[0..{TileCountY - 1}]");

		int left = tx * TileSize;
		int top = ty * TileSize;
		int right = Math.Min(left + TileSize, Width);
		int bottom = Math.Min(top + TileSize, Height);

		return new SKRectI(left, top, right, bottom);
	}

	public SKBitmap ComposeFullBitmap()
	{
		var result = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);

		using var canvas = new SKCanvas(result);
		canvas.Clear(SKColors.Transparent);

		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		foreach (var pair in _tiles.OrderBy(t => t.Key.Y).ThenBy(t => t.Key.X))
		{
			var tile = pair.Value;
			var bounds = GetTileBounds(tile.TileX, tile.TileY);

			canvas.DrawBitmap(
				tile.Bitmap,
				new SKPoint(bounds.Left, bounds.Top),
				sampling,
				null);
		}

		return result;
	}

	public void Dispose()
	{
		foreach (var tile in _tiles.Values)
			tile.Dispose();

		_tiles.Clear();
	}
}

internal static class ImageSelectionClipboardHelper
{
	/// <summary>
	/// Вырезает содержимое выделения в отдельный битмап, применяя маску.
	/// </summary>
	public static SKBitmap? ExtractSelectionBitmap(
		ImageShape image,
		ImagePixelSelection selection)
	{
		if (image.Bitmap == null || !selection.HasSelection)
			return null;

		var bounds = selection.Bounds;
		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return null;

		var result = new SKBitmap(
			bounds.Width,
			bounds.Height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var canvas = new SKCanvas(result))
		{
			canvas.Clear(SKColors.Transparent);

			var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

			// Копируем прямоугольник исходника целиком...
			canvas.DrawBitmap(
				image.Bitmap,
				SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
				SKRect.Create(0, 0, bounds.Width, bounds.Height),
				sampling,
				null);

			// ...затем оставляем только то, что попало в маску.
			if (selection.Mask != null)
			{
				using var maskPaint = new SKPaint
				{
					BlendMode = SKBlendMode.DstIn,
					IsAntialias = false
				};

				canvas.DrawBitmap(
					selection.Mask,
					SKRect.Create(0, 0, selection.Mask.Width, selection.Mask.Height),
					SKRect.Create(0, 0, bounds.Width, bounds.Height),
					sampling,
					maskPaint);
			}
		}

		return result;
	}

	/// <summary>
	/// Впечатывает плавающий фрагмент в тайлы изображения.
	/// Если передана история, изменение пикселей записывается одним шагом.
	/// </summary>
	public static void CommitFloatingIfAny(
		ImageShape image,
		ImageEditSession session,
		UndoStack? undo = null)
	{
		if (session.FloatingSelection == null || image.tileDoc == null)
			return;

		var floating = session.FloatingSelection;

		var affected = SKRect.Create(
			floating.Position.X,
			floating.Position.Y,
			floating.Width,
			floating.Height);

		ImageTilesAction? action = undo != null
			? ImageTilesAction.Capture("Drop floating selection", image, affected)
			: null;

		int imageWidth = image.tileDoc.Width;
		int imageHeight = image.tileDoc.Height;
		int tileSize = image.tileDoc.TileSize;

		// Затрагиваемый диапазон тайлов считаем один раз, вместо того чтобы
		// определять тайл для каждого пикселя по отдельности.
		int tileLeft = Math.Max(0, floating.Position.X / tileSize);
		int tileTop = Math.Max(0, floating.Position.Y / tileSize);
		int tileRight = Math.Min(image.tileDoc.TileCountX - 1,
			(floating.Position.X + floating.Width - 1) / tileSize);
		int tileBottom = Math.Min(image.tileDoc.TileCountY - 1,
			(floating.Position.Y + floating.Height - 1) / tileSize);

		for (int ty = tileTop; ty <= tileBottom; ty++)
		{
			for (int tx = tileLeft; tx <= tileRight; tx++)
			{
				var tile = image.tileDoc.GetOrCreateTile(tx, ty);
				var tileBounds = image.tileDoc.GetTileBounds(tx, ty);

				// Пиксель за пикселем, а не DrawBitmap.
				//
				// Прежняя отрисовка шла обычным смешиванием, и прозрачный
				// пиксель фрагмента просто не менял то, что под ним:
				// на новом месте оставалось старое изображение. Заменить
				// режим смешивания на Src нельзя — вместе с прозрачными
				// точками ВНУТРИ фрагмента затёрлось бы и всё, что лежит
				// в углах прямоугольника ВНЕ контура лассо.
				//
				// Различают эти два случая по маске, а маска читается
				// поточечно. Проход разовый, на отпускание кнопки,
				// и по размеру равен выделению — на скорости работы
				// это не сказывается.
				int startX = Math.Max(tileBounds.Left, floating.Position.X);
				int startY = Math.Max(tileBounds.Top, floating.Position.Y);
				int endX = Math.Min(tileBounds.Right, floating.Position.X + floating.Width);
				int endY = Math.Min(tileBounds.Bottom, floating.Position.Y + floating.Height);

				for (int y = startY; y < endY; y++)
				{
					if (y < 0 || y >= imageHeight)
						continue;

					for (int x = startX; x < endX; x++)
					{
						if (x < 0 || x >= imageWidth)
							continue;

						int srcX = x - floating.Position.X;
						int srcY = y - floating.Position.Y;

						if (!floating.IsInside(srcX, srcY))
							continue;

						tile.Bitmap.SetPixel(
							x - tileBounds.Left,
							y - tileBounds.Top,
							floating.Bitmap.GetPixel(srcX, srcY));
					}
				}

				tile.InvalidatePreview();
			}
		}

		session.Selection.SetRect(floating.Bounds, imageWidth, imageHeight);
		session.ClearFloatingSelection();

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();
		image.InvalidateAdjustmentPaint();

		if (action != null)
		{
			action.CaptureAfter();
			undo!.Push(action);
		}
	}

	public static SKPointI GetPasteStartPosition(
		ImageShape image,
		ImageEditSession session,
		int width,
		int height)
	{
		int x;
		int y;

		if (session.Selection.HasSelection)
		{
			x = session.Selection.Bounds.Left + 10;
			y = session.Selection.Bounds.Top + 10;
		}
		else
		{
			x = 10;
			y = 10;
		}

		if (image.Bitmap != null)
		{
			x = Math.Clamp(x, 0, Math.Max(0, image.Bitmap.Width - width));
			y = Math.Clamp(y, 0, Math.Max(0, image.Bitmap.Height - height));
		}

		return new SKPointI(x, y);
	}
}


internal static class ImageTransformActions
{
	public static bool Apply(MainView view, EditorInteractionContext ctx, ImageEditSession session, ImageTransformKind kind)
	{
		var image = ImagePaintEngine.GetEditableImage(view, ctx);
		if (image?.Bitmap == null)
			return false;

		var undoAction = ImageContentAction.Capture(
			kind switch
			{
				ImageTransformKind.Rotate90CW => "Rotate 90° CW",
				ImageTransformKind.Rotate90CCW => "Rotate 90° CCW",
				ImageTransformKind.FlipHorizontal => "Flip horizontal",
				ImageTransformKind.FlipVertical => "Flip vertical",
				_ => "Transform image"
			},
			image);

		if (image.ActiveStroke != null)
		{
			image.RebuildBitmapFromTiles();
			image.ActiveStroke.Dispose();
			image.ActiveStroke = null;
		}

		if (session.FloatingSelection != null || session.Selection.HasSelection)
			return ApplyToSelection(view, session, image, kind);

		return ApplyToWholeImage(view, image, session, kind);
	}

	private static bool ApplyToSelection(MainView view, ImageEditSession session, ImageShape image, ImageTransformKind kind)
	{
		var undoAction = ImageContentAction.Capture(
			kind switch
			{
				ImageTransformKind.Rotate90CW => "Rotate 90° CW",
				ImageTransformKind.Rotate90CCW => "Rotate 90° CCW",
				ImageTransformKind.FlipHorizontal => "Flip horizontal",
				ImageTransformKind.FlipVertical => "Flip vertical",
				_ => "Transform image"
			},
			image);
		if (session.FloatingSelection == null)
		{
			var cursorTool = new ImageCursorTool(session);
			var createFloatingSelection = typeof(ImageCursorTool)
				.GetMethod("CreateFloatingSelection", BindingFlags.Instance | BindingFlags.NonPublic);

			if (createFloatingSelection == null)
				return false;

			var ok = (bool)(createFloatingSelection.Invoke(cursorTool, new object[] { image }) ?? false);
			if (!ok || session.FloatingSelection == null)
				return false;
		}

		var floating = session.FloatingSelection;
		if (floating == null)
			return false;

		var transformed = TransformBitmap(floating.Bitmap, kind);
		var newPosition = GetTransformedPosition(floating.Position, floating.Bitmap.Width, floating.Bitmap.Height, kind);

		// Маска поворачивается и отражается вместе с фрагментом: иначе
		// после поворота лассо затирало бы соседние пиксели по старому
		// контуру, а по новому — оставляло бы старое изображение.
		var transformedMask = floating.Mask == null ? null : TransformBitmap(floating.Mask, kind);

		floating.Bitmap.Dispose();
		floating.Bitmap = transformed;

		floating.Mask?.Dispose();
		floating.Mask = transformedMask;

		floating.Position = newPosition;

		session.Selection.SetRect(floating.Bounds, image.Bitmap!.Width, image.Bitmap.Height);

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
		if (undoAction != null)
		{
			undoAction.CaptureAfter();
			view.Undo?.Push(undoAction);
		}
		return true;
	}

	private static bool ApplyToWholeImage(MainView view, ImageShape image, ImageEditSession session, ImageTransformKind kind)
	{
		var undoAction = ImageContentAction.Capture(
			kind switch
			{
				ImageTransformKind.Rotate90CW => "Rotate 90° CW",
				ImageTransformKind.Rotate90CCW => "Rotate 90° CCW",
				ImageTransformKind.FlipHorizontal => "Flip horizontal",
				ImageTransformKind.FlipVertical => "Flip vertical",
				_ => "Transform image"
			},
			image);
		using var source = image.Bitmap!.Copy();
		var transformed = TransformBitmap(source, kind);

		image.Bitmap.Dispose();
		image.Bitmap = transformed;
		image.tileDoc?.Dispose();
		image.tileDoc = ImageTileDocument.FromBitmap(transformed, 128);

		session.ClearSelectionAndFloating();

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		view.Invalidate();
		if (undoAction != null)
		{
			undoAction.CaptureAfter();
			view.Undo?.Push(undoAction);
		}
		return true;
	}

	private static SKBitmap TransformBitmap(SKBitmap source, ImageTransformKind kind)
	{
		return kind switch
		{
			ImageTransformKind.FlipHorizontal => FlipHorizontal(source),
			ImageTransformKind.FlipVertical => FlipVertical(source),
			ImageTransformKind.Rotate90CW => Rotate90CW(source),
			ImageTransformKind.Rotate90CCW => Rotate90CCW(source),
			_ => source.Copy()
		};
	}

	private static SKPointI GetTransformedPosition(SKPointI position, int width, int height, ImageTransformKind kind)
	{
		return kind switch
		{
			ImageTransformKind.Rotate90CW => position,
			ImageTransformKind.Rotate90CCW => position,
			ImageTransformKind.FlipHorizontal => position,
			ImageTransformKind.FlipVertical => position,
			_ => position
		};
	}

	private static SKBitmap FlipHorizontal(SKBitmap source)
	{
		var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < source.Height; y++)
			for (int x = 0; x < source.Width; x++)
				result.SetPixel(source.Width - 1 - x, y, source.GetPixel(x, y));

		return result;
	}

	private static SKBitmap FlipVertical(SKBitmap source)
	{
		var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < source.Height; y++)
			for (int x = 0; x < source.Width; x++)
				result.SetPixel(x, source.Height - 1 - y, source.GetPixel(x, y));

		return result;
	}

	private static SKBitmap Rotate90CW(SKBitmap source)
	{
		var result = new SKBitmap(source.Height, source.Width, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < source.Height; y++)
			for (int x = 0; x < source.Width; x++)
				result.SetPixel(source.Height - 1 - y, x, source.GetPixel(x, y));

		return result;
	}

	private static SKBitmap Rotate90CCW(SKBitmap source)
	{
		var result = new SKBitmap(source.Height, source.Width, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < source.Height; y++)
			for (int x = 0; x < source.Width; x++)
				result.SetPixel(y, source.Width - 1 - x, source.GetPixel(x, y));

		return result;
	}
}

internal static class ImageSelectionTransformHelper
{
	public static bool EnsureFloatingSelection(ImageShape image, ImageEditSession session)
	{
		if (image.tileDoc == null || !session.Selection.HasSelection)
			return false;

		var selection = session.Selection.Bounds;
		if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
			return false;

		int imageWidth = image.tileDoc.Width;
		int imageHeight = image.tileDoc.Height;
		int tileSize = image.tileDoc.TileSize;

		int left = Math.Clamp(selection.Left, 0, imageWidth);
		int top = Math.Clamp(selection.Top, 0, imageHeight);
		int right = Math.Clamp(selection.Right, 0, imageWidth);
		int bottom = Math.Clamp(selection.Bottom, 0, imageHeight);

		int width = right - left;
		int height = bottom - top;
		if (width <= 0 || height <= 0)
			return false;

		var floatingBitmap = new SKBitmap(
			width,
			height,
			SKColorType.Bgra8888,
			SKAlphaType.Premul);

		using (var clearCanvas = new SKCanvas(floatingBitmap))
			clearCanvas.Clear(SKColors.Transparent);

		var touchedTiles = new HashSet<ImageTile>();

		for (int y = 0; y < height; y++)
		{
			int srcY = top + y;

			for (int x = 0; x < width; x++)
			{
				int srcX = left + x;

				if (!session.Selection.ContainsPixel(srcX, srcY))
					continue;

				int tileX = srcX / tileSize;
				int tileY = srcY / tileSize;

				var tile = image.tileDoc.GetOrCreateTile(tileX, tileY);
				var tileBounds = image.tileDoc.GetTileBounds(tileX, tileY);

				int localX = srcX - tileBounds.Left;
				int localY = srcY - tileBounds.Top;

				var pixel = tile.Bitmap.GetPixel(localX, localY);
				floatingBitmap.SetPixel(x, y, pixel);

				if (pixel.Alpha != 0)
				{
					tile.Bitmap.SetPixel(localX, localY, SKColors.Transparent);
					touchedTiles.Add(tile);
				}
			}
		}

		foreach (var tile in touchedTiles)
			tile.InvalidatePreview();

		session.SetFloatingSelection(new FloatingPixelSelection(
			floatingBitmap,
			new SKPointI(left, top)));

		image.MarkTilesDirty();
		image.RebuildBitmapFromTiles();

		return true;
	}
}

/// <summary>
/// Параметры недеструктивной коррекции изображения.
/// Хранится в ImageShape, сериализуется вместе с документом.
/// </summary>
public sealed class ImageAdjustmentSettings
{
	private const float Eps = 1e-4f;

	private float _brightness;      // -1..1
	private float _contrast;        // -1..1
	private float _exposure;        // -2..2
	private float _sharpness;       //  0..1
	private float _blackWhite;      //  0..1
	private float _shadows;         // -1..1
	private float _hue;             // -180..180
	private float _temperature;     // -1..1
	private float _tint;            // -1..1
	private float _vibrancy;        // -1..1
	private float _noiseReduction;  //  0..1
	private bool _invert;           //  негатив

	/// <summary>
	/// Счётчик изменений. Используется для инвалидации закэшированного SKPaint
	/// без пересоздания фильтров на каждом кадре.
	/// </summary>
	public int Version { get; private set; }

	private void Set(ref float field, float value, float min, float max)
	{
		value = Math.Clamp(value, min, max);

		if (MathF.Abs(field - value) < 1e-6f)
			return;

		field = value;
		Version++;
	}

	public float Brightness { get => _brightness; set => Set(ref _brightness, value, -1f, 1f); }
	public float Contrast { get => _contrast; set => Set(ref _contrast, value, -1f, 1f); }
	public float Exposure { get => _exposure; set => Set(ref _exposure, value, -2f, 2f); }
	public float Sharpness { get => _sharpness; set => Set(ref _sharpness, value, 0f, 1f); }
	public float BlackWhite { get => _blackWhite; set => Set(ref _blackWhite, value, 0f, 1f); }
	public float Shadows { get => _shadows; set => Set(ref _shadows, value, -1f, 1f); }
	public float Hue { get => _hue; set => Set(ref _hue, value, -180f, 180f); }
	public float Temperature { get => _temperature; set => Set(ref _temperature, value, -1f, 1f); }
	public float Tint { get => _tint; set => Set(ref _tint, value, -1f, 1f); }
	public float Vibrancy { get => _vibrancy; set => Set(ref _vibrancy, value, -1f, 1f); }
	public float NoiseReduction { get => _noiseReduction; set => Set(ref _noiseReduction, value, 0f, 1f); }

	/// <summary>
	/// Негатив: цвет каждого пикселя заменяется дополнительным.
	///
	/// Прозрачность при этом не трогается — прозрачное остаётся прозрачным.
	/// Это не мелочь: обращение вместе с альфой превратило бы пустой фон
	/// вокруг вырезанной детали в сплошную заливку, то есть уничтожило бы
	/// вырезку.
	/// </summary>
	public bool Invert
	{
		get => _invert;
		set
		{
			if (_invert == value)
				return;

			_invert = value;
			Version++;
		}
	}

	/// <summary>Есть ли тональная/цветовая коррекция (без учёта resample-фильтров).</summary>
	public bool HasColorWork =>
		MathF.Abs(_brightness) > Eps ||
		MathF.Abs(_contrast) > Eps ||
		MathF.Abs(_exposure) > Eps ||
		MathF.Abs(_shadows) > Eps ||
		MathF.Abs(_hue) > Eps ||
		MathF.Abs(_temperature) > Eps ||
		MathF.Abs(_tint) > Eps ||
		MathF.Abs(_vibrancy) > Eps ||
		_blackWhite > Eps ||
		_invert;

	/// <summary>Есть ли пространственная коррекция (резкость / шумоподавление).</summary>
	public bool HasSpatialWork =>
		_sharpness > Eps ||
		_noiseReduction > Eps;

	public bool IsIdentity => !HasColorWork && !HasSpatialWork;

	public void Reset()
	{
		_brightness = _contrast = _exposure = 0f;
		_sharpness = _blackWhite = 0f;
		_shadows = _hue = _temperature = _tint = _vibrancy = 0f;
		_noiseReduction = 0f;
		_invert = false;
		Version++;
	}

	public void CopyFrom(ImageAdjustmentSettings other)
	{
		if (other == null)
			return;

		_brightness = other._brightness;
		_contrast = other._contrast;
		_exposure = other._exposure;
		_sharpness = other._sharpness;
		_blackWhite = other._blackWhite;
		_shadows = other._shadows;
		_hue = other._hue;
		_temperature = other._temperature;
		_tint = other._tint;
		_vibrancy = other._vibrancy;
		_noiseReduction = other._noiseReduction;
		_invert = other._invert;

		Version++;
	}

	public ImageAdjustmentSettings Clone()
	{
		var copy = new ImageAdjustmentSettings();
		copy.CopyFrom(this);
		return copy;
	}
}

/// <summary>
/// Сборка SKPaint из ImageAdjustmentSettings.
///
/// Принципиальное отличие от прежнего GpuImageAdjustmentEngine: здесь нет ни одного
/// прямого GL-вызова. Всё выражено штатными примитивами Skia, поэтому работа идёт
/// внутри GRContext, состояние Ganesh не портится, и readback (glReadPixels) не нужен.
///
/// Конвейер (порядок применения к пикселю):
///   1. blur                (noise reduction)   — SKImageFilter
///   2. convolution         (sharpness)         — SKImageFilter
///   3. тоновая кривая LUT  (exposure -> brightness -> contrast -> shadows)
///   4. цветовая матрица    (hue -> temp/tint -> vibrancy -> b/w)
/// </summary>
public static class ImageAdjustmentPipeline
{
	// Коэффициенты яркости по Rec.709 — те же, что использовались в старом шейдере.
	private const float LumR = 0.2126f;
	private const float LumG = 0.7152f;
	private const float LumB = 0.0722f;

	/// <summary>
	/// Возвращает SKPaint с настроенными фильтрами, либо null, если коррекция пустая.
	/// Вызывающий обязан сделать Dispose (или закэшировать — см. ImageShape).
	/// </summary>
	public static SKPaint? BuildPaint(ImageAdjustmentSettings s)
	{
		if (s == null || s.IsIdentity)
			return null;

		SKColorFilter? colorFilter = BuildColorFilter(s);
		SKImageFilter? imageFilter = BuildImageFilter(s);

		if (colorFilter == null && imageFilter == null)
			return null;

		return new SKPaint
		{
			IsAntialias = true,
			ColorFilter = colorFilter,
			ImageFilter = imageFilter
		};
	}

	#region Color filter

	private static SKColorFilter? BuildColorFilter(ImageAdjustmentSettings s)
	{
		if (!s.HasColorWork)
			return null;

		SKColorFilter? tone = BuildToneCurveFilter(s);
		SKColorFilter? color = BuildColorMatrixFilter(s);

		// Compose(outer, inner): сначала inner (тон), затем outer (цвет).
		var result = Compose(color, tone);

		// Негатив накладывается ПОСЛЕДНИМ, поверх всей коррекции.
		//
		// Порядок важен и выбран не случайно. Обращение до коррекции
		// заставило бы все остальные ползунки работать наоборот:
		// поднятие яркости темнило бы картинку, тени становились бы
		// светами. После коррекции негатив остаётся тем, чем его считают
		// в любом редакторе, — последним действием над готовым
		// изображением.
		return Compose(BuildInvertFilter(s), result);
	}

	/// <summary>
	/// Складывает два фильтра. Compose(outer, inner) применяет сначала
	/// inner, затем outer. Любой из двух может отсутствовать.
	///
	/// Исходные фильтры освобождаются: Compose держит собственные ссылки,
	/// а вызывающему они больше не нужны.
	/// </summary>
	private static SKColorFilter? Compose(SKColorFilter? outer, SKColorFilter? inner)
	{
		if (outer == null)
			return inner;

		if (inner == null)
			return outer;

		var composed = SKColorFilter.CreateCompose(outer, inner);

		outer.Dispose();
		inner.Dispose();

		return composed;
	}

	/// <summary>
	/// Негатив таблицей, а не цветовой матрицей.
	///
	/// Табличный фильтр Skia работает с непремультиплицированными
	/// значениями и имеет отдельную таблицу на альфу. Значит,
	/// прозрачность можно оставить тождественной явно, одной строкой,
	/// и не разбираться, на каком шаге конвейера цвет умножен на альфу,
	/// а на каком нет. С матрицей этот вопрос пришлось бы держать в уме.
	/// </summary>
	private static SKColorFilter? BuildInvertFilter(ImageAdjustmentSettings s)
	{
		if (!s.Invert)
			return null;

		byte[] inverted = new byte[256];
		byte[] identity = new byte[256];

		for (int i = 0; i < 256; i++)
		{
			identity[i] = (byte)i;
			inverted[i] = (byte)(255 - i);
		}

		// Первая таблица — альфа: она тождественная, прозрачное остаётся
		// прозрачным.
		return SKColorFilter.CreateTable(identity, inverted, inverted, inverted);
	}

	/// <summary>
	/// Тоновая кривая через 256-точечный LUT.
	/// Table color filter в Skia работает с непремультиплицированными значениями,
	/// поэтому здесь не нужны деления на альфу, которые в старом шейдере давали NaN.
	/// </summary>
	private static SKColorFilter? BuildToneCurveFilter(ImageAdjustmentSettings s)
	{
		bool needed =
			MathF.Abs(s.Exposure) > 1e-4f ||
			MathF.Abs(s.Brightness) > 1e-4f ||
			MathF.Abs(s.Contrast) > 1e-4f ||
			MathF.Abs(s.Shadows) > 1e-4f;

		if (!needed)
			return null;

		float exposureMul = MathF.Pow(2f, s.Exposure);
		float contrast = 1f + s.Contrast * 0.75f;

		byte[] rgb = new byte[256];
		byte[] identity = new byte[256];

		for (int i = 0; i < 256; i++)
		{
			identity[i] = (byte)i;

			float v = i / 255f;

			v *= exposureMul;
			v += s.Brightness * 0.25f;
			v = (v - 0.5f) * contrast + 0.5f;

			// Подъём/затемнение теней: вес тем выше, чем темнее тон.
			float clamped = Math.Clamp(v, 0f, 1f);
			v += (1f - clamped) * s.Shadows * 0.20f;

			v = Math.Clamp(v, 0f, 1f);

			// Защита от NaN на случай экзотических значений uniform'ов.
			if (float.IsNaN(v))
				v = 0f;

			rgb[i] = (byte)MathF.Round(v * 255f);
		}

		// Альфа-канал не трогаем.
		return SKColorFilter.CreateTable(identity, rgb, rgb, rgb);
	}

	private static SKColorFilter? BuildColorMatrixFilter(ImageAdjustmentSettings s)
	{
		bool needed =
			MathF.Abs(s.Hue) > 1e-4f ||
			MathF.Abs(s.Temperature) > 1e-4f ||
			MathF.Abs(s.Tint) > 1e-4f ||
			MathF.Abs(s.Vibrancy) > 1e-4f ||
			s.BlackWhite > 1e-4f;

		if (!needed)
			return null;

		float[] m = Identity();

		if (MathF.Abs(s.Hue) > 1e-4f)
			m = Concat(HueRotation(s.Hue), m);

		if (MathF.Abs(s.Temperature) > 1e-4f || MathF.Abs(s.Tint) > 1e-4f)
			m = Concat(ColorBalance(s.Temperature, s.Tint), m);

		// Vibrancy и BlackWhite сводятся к одной операции насыщенности.
		float saturation = (1f + s.Vibrancy * 0.5f) * (1f - Math.Clamp(s.BlackWhite, 0f, 1f));

		if (MathF.Abs(saturation - 1f) > 1e-4f)
			m = Concat(Saturation(saturation), m);

		return SKColorFilter.CreateColorMatrix(m);
	}

	#endregion

	#region Image filter

	private static SKImageFilter? BuildImageFilter(ImageAdjustmentSettings s)
	{
		if (!s.HasSpatialWork)
			return null;

		SKImageFilter? filter = null;

		if (s.NoiseReduction > 1e-4f)
		{
			// 0..1 -> сигма 0..2.5 px. Мягкое подавление, без потери мелких деталей на малых значениях.
			float sigma = s.NoiseReduction * 2.5f;
			filter = SKImageFilter.CreateBlur(sigma, sigma, filter);
		}

		if (s.Sharpness > 1e-4f)
		{
			float a = s.Sharpness;

			// Крестообразное нерезкое маскирование. Сумма ядра = 1, поэтому
			// средняя яркость сохраняется и плоские области не уходят в чёрный.
			float[] kernel =
			{
				0f,     -a,        0f,
				-a,      1f + 4f * a, -a,
				0f,     -a,        0f
			};

			var previous = filter;

			filter = SKImageFilter.CreateMatrixConvolution(
				new SKSizeI(3, 3),
				kernel,
				gain: 1f,
				bias: 0f,
				kernelOffset: new SKPointI(1, 1),
				tileMode: SKShaderTileMode.Clamp,
				convolveAlpha: false,
				input: previous);

			previous?.Dispose();
		}

		return filter;
	}

	#endregion

	#region Матричные помощники (5x4, row-major, как в SKColorFilter.CreateColorMatrix)

	private static float[] Identity() => new float[]
	{
		1f, 0f, 0f, 0f, 0f,
		0f, 1f, 0f, 0f, 0f,
		0f, 0f, 1f, 0f, 0f,
		0f, 0f, 0f, 1f, 0f
	};

	/// <summary>Результат = сначала <paramref name="first"/>, затем <paramref name="second"/>.</summary>
	private static float[] Concat(float[] second, float[] first)
	{
		float[] r = new float[20];

		for (int row = 0; row < 4; row++)
		{
			for (int col = 0; col < 4; col++)
			{
				r[row * 5 + col] =
					second[row * 5 + 0] * first[0 * 5 + col] +
					second[row * 5 + 1] * first[1 * 5 + col] +
					second[row * 5 + 2] * first[2 * 5 + col] +
					second[row * 5 + 3] * first[3 * 5 + col];
			}

			r[row * 5 + 4] =
				second[row * 5 + 0] * first[0 * 5 + 4] +
				second[row * 5 + 1] * first[1 * 5 + 4] +
				second[row * 5 + 2] * first[2 * 5 + 4] +
				second[row * 5 + 3] * first[3 * 5 + 4] +
				second[row * 5 + 4];
		}

		return r;
	}

	private static float[] Saturation(float s)
	{
		float ir = LumR * (1f - s);
		float ig = LumG * (1f - s);
		float ib = LumB * (1f - s);

		return new float[]
		{
			ir + s, ig,     ib,     0f, 0f,
			ir,     ig + s, ib,     0f, 0f,
			ir,     ig,     ib + s, 0f, 0f,
			0f,     0f,     0f,     1f, 0f
		};
	}

	/// <summary>Поворот оттенка вокруг оси яркости.</summary>
	private static float[] HueRotation(float degrees)
	{
		float rad = degrees * MathF.PI / 180f;
		float c = MathF.Cos(rad);
		float s = MathF.Sin(rad);

		return new float[]
		{
			LumR + c * (1f - LumR) + s * (-LumR),
			LumG + c * (-LumG)     + s * (-LumG),
			LumB + c * (-LumB)     + s * (1f - LumB),
			0f, 0f,

			LumR + c * (-LumR)     + s * (0.143f),
			LumG + c * (1f - LumG) + s * (0.140f),
			LumB + c * (-LumB)     + s * (-0.283f),
			0f, 0f,

			LumR + c * (-LumR)     + s * (-(1f - LumR)),
			LumG + c * (-LumG)     + s * (LumG),
			LumB + c * (1f - LumB) + s * (LumB),
			0f, 0f,

			0f, 0f, 0f, 1f, 0f
		};
	}

	/// <summary>Температура и оттенок как аддитивные сдвиги каналов.</summary>
	private static float[] ColorBalance(float temperature, float tint)
	{
		float offR = temperature * 0.08f - tint * 0.025f;
		float offG = tint * 0.05f;
		float offB = -temperature * 0.08f - tint * 0.025f;

		return new float[]
		{
			1f, 0f, 0f, 0f, offR,
			0f, 1f, 0f, 0f, offG,
			0f, 0f, 1f, 0f, offB,
			0f, 0f, 0f, 1f, 0f
		};
	}

	#endregion
}