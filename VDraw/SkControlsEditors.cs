using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public sealed class SkGradientEditor : SkControl
{
	public List<GradientStopInfo> Stops { get; } = new();
	public int SelectedIndex { get; private set; } = 0;

	public Action<int, GradientStopInfo>? SelectedStopChanged { get; set; }
	public Action? GradientChanged { get; set; }

	private int _dragIndex = -1;

	public SkGradientEditor()
	{
		Bounds = new SKRect(0, 0, 220, 42);
	}

	public void SetStops(IEnumerable<GradientStopInfo> stops)
	{
		Stops.Clear();
		Stops.AddRange(stops.Select(s => s.Clone()).OrderBy(s => s.Offset));
		SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Stops.Count - 1));
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible) return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;
		var bar = GetGradientBarRect();

		using var bg = SkUiTheme.Fill(pal.Surface);
		using var border = SkUiTheme.Stroke(pal.Border);

		canvas.DrawRoundRect(ab, 8, 8, bg);
		canvas.DrawRoundRect(ab, 8, 8, border);

		DrawChecker(canvas, bar, 6f);

		var ordered = Stops.OrderBy(s => s.Offset).ToList();
		if (ordered.Count >= 2)
		{
			using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			fill.Shader = SKShader.CreateLinearGradient(
				new SKPoint(bar.Left, bar.MidY),
				new SKPoint(bar.Right, bar.MidY),
				ordered.Select(x => x.Color).ToArray(),
				ordered.Select(x => x.Offset).ToArray(),
				SKShaderTileMode.Clamp);

			canvas.DrawRect(bar, fill);
		}

		canvas.DrawRect(bar, border);

		for (int i = 0; i < Stops.Count; i++)
			DrawHandle(canvas, bar, i, Stops[i], i == SelectedIndex);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Focused = true;

		int hit = HitTestHandle(e.Location);
		if (hit >= 0)
		{
			SelectedIndex = hit;
			_dragIndex = hit;
			SelectedStopChanged?.Invoke(hit, Stops[hit]);
			GradientChanged?.Invoke();
			return true;
		}

		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool old = Hovered;
		Hovered = HitTest(e.Location);

		if (_dragIndex >= 0 && _dragIndex < Stops.Count)
		{
			var bar = GetGradientBarRect();
			float t = (e.Location.X - bar.Left) / Math.Max(1f, bar.Width);
			t = Math.Clamp(t, 0f, 1f);

			if (_dragIndex == 0) t = 0f;
			if (_dragIndex == Stops.Count - 1) t = 1f;

			Stops[_dragIndex].Offset = t;

			if (_dragIndex > 0 && Stops[_dragIndex].Offset < Stops[_dragIndex - 1].Offset)
				Stops[_dragIndex].Offset = Stops[_dragIndex - 1].Offset;

			if (_dragIndex < Stops.Count - 1 && Stops[_dragIndex].Offset > Stops[_dragIndex + 1].Offset)
				Stops[_dragIndex].Offset = Stops[_dragIndex + 1].Offset;

			SortStopsKeepSelection();
			GradientChanged?.Invoke();
			return true;
		}

		return old != Hovered;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		_dragIndex = -1;
		return Focused;
	}

	public override bool OnMouseDoubleClick(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		int hit = HitTestHandle(e.Location);
		if (hit >= 0)
		{
			if (hit == 0 || hit == Stops.Count - 1)
				return true;

			Stops.RemoveAt(hit);
			SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Stops.Count - 1));
			SelectedStopChanged?.Invoke(SelectedIndex, Stops[SelectedIndex]);
			GradientChanged?.Invoke();
			return true;
		}

		var bar = GetGradientBarRect();
		if (bar.Contains(e.Location))
		{
			float t = (e.Location.X - bar.Left) / Math.Max(1f, bar.Width);
			t = Math.Clamp(t, 0f, 1f);
			var color = SampleColorAt(t);

			Stops.Add(new GradientStopInfo { Offset = t, Color = color });
			SortStopsKeepNearest(t);

			SelectedStopChanged?.Invoke(SelectedIndex, Stops[SelectedIndex]);
			GradientChanged?.Invoke();
			return true;
		}

		return false;
	}

	public void SetSelectedStopColor(SKColor color)
	{
		if (SelectedIndex < 0 || SelectedIndex >= Stops.Count)
			return;

		Stops[SelectedIndex].Color = color;
		GradientChanged?.Invoke();
	}

	private void SortStopsKeepSelection()
	{
		var sel = Stops[_dragIndex];
		Stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
		SelectedIndex = Stops.IndexOf(sel);
		_dragIndex = SelectedIndex;
	}

	private void SortStopsKeepNearest(float offset)
	{
		Stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));

		float best = float.MaxValue;
		int bestIdx = 0;
		for (int i = 0; i < Stops.Count; i++)
		{
			float d = MathF.Abs(Stops[i].Offset - offset);
			if (d < best)
			{
				best = d;
				bestIdx = i;
			}
		}

		SelectedIndex = bestIdx;
	}

	private SKColor SampleColorAt(float t)
	{
		var ordered = Stops.OrderBy(x => x.Offset).ToList();
		if (ordered.Count == 0) return SKColors.White;
		if (ordered.Count == 1) return ordered[0].Color;

		if (t <= ordered[0].Offset) return ordered[0].Color;
		if (t >= ordered[^1].Offset) return ordered[^1].Color;

		for (int i = 0; i < ordered.Count - 1; i++)
		{
			var a = ordered[i];
			var b = ordered[i + 1];
			if (t >= a.Offset && t <= b.Offset)
			{
				float k = (t - a.Offset) / Math.Max(0.0001f, b.Offset - a.Offset);
				return Lerp(a.Color, b.Color, k);
			}
		}

		return ordered[^1].Color;
	}

	private static SKColor Lerp(SKColor a, SKColor b, float t)
	{
		byte r = (byte)(a.Red + (b.Red - a.Red) * t);
		byte g = (byte)(a.Green + (b.Green - a.Green) * t);
		byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
		byte al = (byte)(a.Alpha + (b.Alpha - a.Alpha) * t);
		return new SKColor(r, g, bl, al);
	}

	private int HitTestHandle(SKPoint p)
	{
		var bar = GetGradientBarRect();

		for (int i = 0; i < Stops.Count; i++)
		{
			float x = bar.Left + Stops[i].Offset * bar.Width;
			var r = new SKRect(x - 8, bar.Top - 18, x + 8, bar.Bottom + 16);
			if (r.Contains(p)) return i;
		}

		return -1;
	}

	private void DrawHandle(SKCanvas canvas, SKRect bar, int index, GradientStopInfo stop, bool selected)
	{
		float x = bar.Left + stop.Offset * bar.Width;
		var sq = new SKRect(x - 5, bar.Top - 16, x + 5, bar.Top - 6);

		using var fill = SkUiTheme.Fill(stop.Color);
		using var stroke = SkUiTheme.Stroke(selected ? SkUiTheme.Current.Accent : SkUiTheme.Current.Border, selected ? 2f : 1f);

		canvas.DrawRect(sq, fill);
		canvas.DrawRect(sq, stroke);

		var path = new SKPathBuilder();
		path.MoveTo(x - 6, bar.Bottom + 10);
		path.LineTo(x + 6, bar.Bottom + 10);
		path.LineTo(x, bar.Bottom + 2);
		path.Close();

		using var path1 = path.Detach();
		using var path2 = path.Detach();
		canvas.DrawPath(path1, fill);
		canvas.DrawPath(path2, stroke);
	}

	private SKRect GetGradientBarRect()
	{
		var ab = AbsoluteBounds;
		return new SKRect(ab.Left + 10, ab.Top + 14, ab.Right - 10, ab.Top + 28);
	}

	private static void DrawChecker(SKCanvas canvas, SKRect rect, float cell)
	{
		using var light = new SKPaint { IsAntialias = false, Color = new SKColor(220, 220, 220) };
		using var dark = new SKPaint { IsAntialias = false, Color = new SKColor(180, 180, 180) };

		for (float y = rect.Top; y < rect.Bottom; y += cell)
		{
			for (float x = rect.Left; x < rect.Right; x += cell)
			{
				bool alt = (((int)((x - rect.Left) / cell) + (int)((y - rect.Top) / cell)) % 2 == 0);
				canvas.DrawRect(new SKRect(x, y, MathF.Min(x + cell, rect.Right), MathF.Min(y + cell, rect.Bottom)), alt ? light : dark);
			}
		}
	}
}

public sealed class GradientStopInfo
{
	public float Offset { get; set; }
	public SKColor Color { get; set; }

	public GradientStopInfo()
	{
		Offset = 0f;
		Color = SKColors.White;
	}

	public GradientStopInfo(float offset, SKColor color)
	{
		Offset = Math.Clamp(offset, 0f, 1f);
		Color = color;
	}

	public GradientStopInfo Clone()
	{
		return new GradientStopInfo(Offset, Color);
	}
}

public sealed class SkPatternGridEditor : SkControl
{
	private SKColor[,] _cells = new SKColor[8, 8];
	private bool _mouseDown;
	private MouseButtons _paintButton = MouseButtons.Left;
	private int _lastCellX = -1;
	private int _lastCellY = -1;

	public int PatternWidth { get; private set; } = 8;
	public int PatternHeight { get; private set; } = 8;

	// Это текущие цвета рисования, а не глобальная перекраска паттерна.
	public SKColor FrontColor { get; set; } = SKColors.Black;
	public SKColor BackColor { get; set; } = SKColors.White;

	public Action<string, int, int>? PatternChanged { get; set; }

	public SkPatternGridEditor()
	{
		Bounds = new SKRect(0, 0, 220, 220);
		SetSize(8, 8, SKColors.White);
	}

	public void SetSize(int width, int height, SKColor? newCellDefaultColor = null)
	{
		width = Math.Clamp(width, 1, 256);
		height = Math.Clamp(height, 1, 256);

		SKColor defaultColor = newCellDefaultColor ?? BackColor;

		var old = _cells;
		int oldW = PatternWidth;
		int oldH = PatternHeight;

		PatternWidth = width;
		PatternHeight = height;
		_cells = new SKColor[PatternHeight, PatternWidth];

		for (int y = 0; y < PatternHeight; y++)
			for (int x = 0; x < PatternWidth; x++)
				_cells[y, x] = defaultColor;

		for (int y = 0; y < Math.Min(oldH, PatternHeight); y++)
		{
			for (int x = 0; x < Math.Min(oldW, PatternWidth); x++)
				_cells[y, x] = old[y, x];
		}

		RaiseChanged();
	}

	public void LoadFromDefinition(string definition, int width, int height, SKColor fallbackColor)
	{
		PatternWidth = Math.Clamp(width, 1, 256);
		PatternHeight = Math.Clamp(height, 1, 256);
		_cells = FillStyle.DeserializePatternColors(definition, PatternWidth, PatternHeight, fallbackColor);
	}

	public void LoadFromBitmap(SKBitmap bitmap, SKColor frontColor, SKColor backColor)
	{
		if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
		{
			SetSize(1, 1, backColor);
			FrontColor = frontColor;
			BackColor = backColor;
			Clear(backColor);
			return;
		}

		FrontColor = frontColor;
		BackColor = backColor;

		PatternWidth = Math.Clamp(bitmap.Width, 1, 256);
		PatternHeight = Math.Clamp(bitmap.Height, 1, 256);
		_cells = new SKColor[PatternHeight, PatternWidth];

		for (int y = 0; y < PatternHeight; y++)
		{
			for (int x = 0; x < PatternWidth; x++)
			{
				var c = bitmap.GetPixel(x, y);
				_cells[y, x] = c.Alpha > 0 ? FrontColor : BackColor;
			}
		}
	}

	public string GetDefinition()
	{
		return FillStyle.SerializePatternColors(_cells, PatternWidth, PatternHeight);
	}

	public void Clear(SKColor? color = null)
	{
		SKColor c = color ?? BackColor;

		for (int y = 0; y < PatternHeight; y++)
			for (int x = 0; x < PatternWidth; x++)
				_cells[y, x] = c;

		RaiseChanged();
	}

	public SKBitmap BuildBitmap()
	{
		var bmp = new SKBitmap(PatternWidth, PatternHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < PatternHeight; y++)
		{
			for (int x = 0; x < PatternWidth; x++)
				bmp.SetPixel(x, y, _cells[y, x]);
		}

		return bmp;
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		using var panelFill = SkUiTheme.Fill(pal.Surface);
		using var panelBorder = SkUiTheme.Stroke(Focused ? pal.Accent : pal.Border, Focused ? 1.5f : 1f);

		canvas.DrawRoundRect(ab, 8, 8, panelFill);
		canvas.DrawRoundRect(ab, 8, 8, panelBorder);

		var gridRect = GetSquareGridRect(ab, PatternWidth, PatternHeight);

		using var bg = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Fill,
			Color = pal.PanelBack
		};
		canvas.DrawRect(gridRect, bg);

		float cellSize = GetCellSize(gridRect, PatternWidth, PatternHeight);

		for (int y = 0; y < PatternHeight; y++)
		{
			for (int x = 0; x < PatternWidth; x++)
			{
				var r = new SKRect(
					gridRect.Left + x * cellSize,
					gridRect.Top + y * cellSize,
					gridRect.Left + (x + 1) * cellSize,
					gridRect.Top + (y + 1) * cellSize);

				using var fill = new SKPaint
				{
					IsAntialias = false,
					Style = SKPaintStyle.Fill,
					Color = _cells[y, x]
				};

				canvas.DrawRect(r, fill);
			}
		}

		using var gridPaint = new SKPaint
		{
			IsAntialias = false,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(
				pal.Text.Red,
				pal.Text.Green,
				pal.Text.Blue,
				72)
		};

		for (int x = 0; x <= PatternWidth; x++)
		{
			float gx = gridRect.Left + x * cellSize;
			canvas.DrawLine(gx, gridRect.Top, gx, gridRect.Bottom, gridPaint);
		}

		for (int y = 0; y <= PatternHeight; y++)
		{
			float gy = gridRect.Top + y * cellSize;
			canvas.DrawLine(gridRect.Left, gy, gridRect.Right, gy, gridPaint);
		}
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Focused = true;

		var cell = HitCell(e.Location);
		if (cell == null)
			return true;

		_mouseDown = true;
		_paintButton = e.Button;
		PaintCell(cell.Value.x, cell.Value.y, _paintButton);
		_lastCellX = cell.Value.x;
		_lastCellY = cell.Value.y;
		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool changed = false;

		bool oldHovered = Hovered;
		Hovered = HitTest(e.Location);
		if (oldHovered != Hovered)
			changed = true;

		if (!_mouseDown)
			return changed;

		var cell = HitCell(e.Location);
		if (cell == null)
			return changed;

		if (cell.Value.x == _lastCellX && cell.Value.y == _lastCellY)
			return changed;

		PaintCell(cell.Value.x, cell.Value.y, _paintButton);
		_lastCellX = cell.Value.x;
		_lastCellY = cell.Value.y;
		return true;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		_mouseDown = false;
		_lastCellX = -1;
		_lastCellY = -1;
		return Focused;
	}

	private void PaintCell(int x, int y, MouseButtons button)
	{
		if (x < 0 || x >= PatternWidth || y < 0 || y >= PatternHeight)
			return;

		SKColor color = button == MouseButtons.Right ? BackColor : FrontColor;

		if (_cells[y, x] == color)
			return;

		_cells[y, x] = color;
		RaiseChanged();
	}

	private (int x, int y)? HitCell(SKPoint p)
	{
		var gridRect = GetSquareGridRect(AbsoluteBounds, PatternWidth, PatternHeight);
		if (!gridRect.Contains(p))
			return null;

		float cellSize = GetCellSize(gridRect, PatternWidth, PatternHeight);

		int x = (int)((p.X - gridRect.Left) / cellSize);
		int y = (int)((p.Y - gridRect.Top) / cellSize);

		if (x < 0 || x >= PatternWidth || y < 0 || y >= PatternHeight)
			return null;

		return (x, y);
	}

	private static SKRect GetSquareGridRect(SKRect bounds, int cols, int rows)
	{
		const float pad = 8f;

		var inner = new SKRect(
			bounds.Left + pad,
			bounds.Top + pad,
			bounds.Right - pad,
			bounds.Bottom - pad);

		float cell = MathF.Min(inner.Width / cols, inner.Height / rows);
		float gridW = cell * cols;
		float gridH = cell * rows;

		float left = inner.Left + (inner.Width - gridW) * 0.5f;
		float top = inner.Top + (inner.Height - gridH) * 0.5f;

		return new SKRect(left, top, left + gridW, top + gridH);
	}

	private static float GetCellSize(SKRect gridRect, int cols, int rows)
	{
		return MathF.Min(gridRect.Width / cols, gridRect.Height / rows);
	}

	private void RaiseChanged()
	{
		PatternChanged?.Invoke(GetDefinition(), PatternWidth, PatternHeight);
	}
}

public sealed class PatternInfo
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public SKBitmap? Bitmap { get; set; }

	public PatternInfo()
	{
	}

	public PatternInfo(string id, string displayName, string fileName, SKBitmap? bitmap)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		FileName = fileName ?? string.Empty;
		Bitmap = bitmap;
	}
}