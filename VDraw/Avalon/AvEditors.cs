using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using SkiaSharp;

// FillStyle лежит в VDraw.Shapes: сериализация узора (строковое определение
// клеток) живёт в модели заливки, и редактор пользуется ею, а не своей копией.
using VDraw.Shapes;

// О псевдонимах см. пояснение в начале AvTheme.cs.
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;
// GradientStop есть и в VDraw.Shapes (узел градиента в модели заливки),
// и в Avalonia.Media (узел кисти). Здесь рисуется кисть, поэтому нужен
// второй; узлы модели этот редактор получает как GradientStopInfo.
using GradientStop = Avalonia.Media.GradientStop;
using Control = Avalonia.Controls.Control;
using Pen = Avalonia.Media.Pen;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace VDraw.Ui.Avalon;

/// <summary>
/// Общая отрисовка шахматной подложки прозрачности.
/// </summary>
internal static class AvChecker
{
	public static void Draw(DrawingContext context, Rect rect, double cell = 6)
	{
		var light = AvTheme.Brush(new SKColor(220, 220, 220));
		var dark = AvTheme.Brush(new SKColor(180, 180, 180));

		context.FillRectangle(light, rect);

		for (int row = 0; row * cell < rect.Height; row++)
		{
			for (int col = 0; col * cell < rect.Width; col++)
			{
				if ((row + col) % 2 == 0)
					continue;

				double x = rect.X + col * cell;
				double y = rect.Y + row * cell;

				context.FillRectangle(dark, new Rect(
					x, y,
					Math.Min(cell, rect.Right - x),
					Math.Min(cell, rect.Bottom - y)));
			}
		}
	}
}

/// <summary>
/// Редактор градиента — замена SkGradientEditor.
///
/// Логика узлов перенесена без изменений: перетаскивание меняет смещение,
/// двойной щелчок по полосе добавляет узел с цветом из этой точки, двойной
/// щелчок по узлу его удаляет, а крайние узлы не двигаются и не удаляются —
/// иначе градиент перестал бы покрывать фигуру целиком.
/// </summary>
public sealed class AvGradientEditor : Control
{
	public List<GradientStopInfo> Stops { get; } = new();

	public int SelectedIndex { get; private set; }

	public Action<int, GradientStopInfo>? SelectedStopChanged { get; set; }
	public Action? GradientChanged { get; set; }

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	private int _dragIndex = -1;

	public AvGradientEditor()
	{
		// Минимумы нарочно небольшие: редактор попадает и в узкую строку
		// свойств, и заданная «удобная» ширина выдавила бы его за край.
		MinWidth = 80;
		MinHeight = 32;
		Height = 42;
		Focusable = true;
	}

	public void SetStops(IEnumerable<GradientStopInfo> stops)
	{
		Stops.Clear();
		Stops.AddRange(stops.Select(s => s.Clone()).OrderBy(s => s.Offset));
		SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Stops.Count - 1));

		InvalidateVisual();
	}

	public void SetSelectedStopColor(SKColor color)
	{
		if (SelectedIndex < 0 || SelectedIndex >= Stops.Count)
			return;

		Stops[SelectedIndex].Color = color;

		InvalidateVisual();
		GradientChanged?.Invoke();
	}

	#region Отрисовка

	private Rect BarRect()
	{
		double w = Bounds.Width;
		double h = Bounds.Height;

		return new Rect(10, h / 2 - 7, Math.Max(1, w - 20), 14);
	}

	public override void Render(DrawingContext context)
	{
		var full = new Rect(0, 0, Bounds.Width, Bounds.Height);

		if (full.Width <= 0 || full.Height <= 0)
			return;

		var bar = BarRect();
		var pen = new Pen(AvTheme.BorderLine, 1);

		context.DrawRectangle(
			AvTheme.Surface, pen,
			new RoundedRect(full.Deflate(0.5), AvTheme.Radius, AvTheme.Radius));

		AvChecker.Draw(context, bar);

		var ordered = Stops.OrderBy(s => s.Offset).ToList();

		if (ordered.Count >= 2)
		{
			var brush = new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
			};

			foreach (var stop in ordered)
			{
				brush.GradientStops.Add(new GradientStop(
					AvTheme.ToAv(stop.Color),
					Math.Clamp(stop.Offset, 0f, 1f)));
			}

			context.FillRectangle(brush, bar);
		}
		else if (ordered.Count == 1)
		{
			context.FillRectangle(AvTheme.Brush(ordered[0].Color), bar);
		}

		context.DrawRectangle(null, pen, bar);

		for (int i = 0; i < Stops.Count; i++)
			DrawHandle(context, bar, Stops[i], i == SelectedIndex);
	}

	private void DrawHandle(DrawingContext context, Rect bar, GradientStopInfo stop, bool selected)
	{
		double x = bar.X + stop.Offset * bar.Width;

		var fill = AvTheme.Brush(stop.Color);
		var pen = new Pen(selected ? AvTheme.Accent : AvTheme.BorderLine, selected ? 2 : 1);

		// Квадрат над полосой — цвет узла, треугольник под полосой —
		// его положение. Две метки на один узел нужны потому, что при
		// тёмном цвете квадрат на тёмной панели почти не виден.
		var square = new Rect(x - 5, bar.Y - 11, 10, 9);

		context.DrawRectangle(fill, pen, square);

		var geometry = new StreamGeometry();

		using (var ctx = geometry.Open())
		{
			ctx.BeginFigure(new Point(x - 6, bar.Bottom + 9), true);
			ctx.LineTo(new Point(x + 6, bar.Bottom + 9));
			ctx.LineTo(new Point(x, bar.Bottom + 1));
			ctx.EndFigure(true);
		}

		context.DrawGeometry(fill, pen, geometry);
	}

	#endregion

	#region Ввод

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var p = e.GetPosition(this);

		Focus();

		if (e.ClickCount >= 2)
		{
			HandleDoubleClick(p);
			e.Handled = true;

			return;
		}

		int hit = HitTestHandle(p);

		if (hit >= 0)
		{
			SelectedIndex = hit;
			_dragIndex = hit;

			e.Pointer.Capture(this);

			SelectedStopChanged?.Invoke(hit, Stops[hit]);
			GradientChanged?.Invoke();

			InvalidateVisual();
		}

		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		if (_dragIndex < 0 || _dragIndex >= Stops.Count)
			return;

		var bar = BarRect();
		var p = e.GetPosition(this);

		float t = (float)((p.X - bar.X) / Math.Max(1, bar.Width));
		t = Math.Clamp(t, 0f, 1f);

		if (_dragIndex == 0)
			t = 0f;

		if (_dragIndex == Stops.Count - 1)
			t = 1f;

		Stops[_dragIndex].Offset = t;

		if (_dragIndex > 0 && Stops[_dragIndex].Offset < Stops[_dragIndex - 1].Offset)
			Stops[_dragIndex].Offset = Stops[_dragIndex - 1].Offset;

		if (_dragIndex < Stops.Count - 1 && Stops[_dragIndex].Offset > Stops[_dragIndex + 1].Offset)
			Stops[_dragIndex].Offset = Stops[_dragIndex + 1].Offset;

		SortStopsKeepSelection();

		InvalidateVisual();
		GradientChanged?.Invoke();

		e.Handled = true;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);

		_dragIndex = -1;
		e.Pointer.Capture(null);
	}

	private void HandleDoubleClick(Point p)
	{
		int hit = HitTestHandle(p);

		if (hit >= 0)
		{
			// Крайние узлы не удаляются: без них градиент не знал бы,
			// где начинаться и где заканчиваться.
			if (hit == 0 || hit == Stops.Count - 1)
				return;

			Stops.RemoveAt(hit);
			SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Stops.Count - 1));

			if (Stops.Count > 0)
				SelectedStopChanged?.Invoke(SelectedIndex, Stops[SelectedIndex]);

			InvalidateVisual();
			GradientChanged?.Invoke();

			return;
		}

		var bar = BarRect();

		if (!bar.Contains(p))
			return;

		float t = (float)((p.X - bar.X) / Math.Max(1, bar.Width));
		t = Math.Clamp(t, 0f, 1f);

		Stops.Add(new GradientStopInfo { Offset = t, Color = SampleColorAt(t) });
		SortStopsKeepNearest(t);

		if (Stops.Count > 0)
			SelectedStopChanged?.Invoke(SelectedIndex, Stops[SelectedIndex]);

		InvalidateVisual();
		GradientChanged?.Invoke();
	}

	private int HitTestHandle(Point p)
	{
		var bar = BarRect();

		for (int i = 0; i < Stops.Count; i++)
		{
			double x = bar.X + Stops[i].Offset * bar.Width;
			var r = new Rect(x - 8, bar.Y - 14, 16, bar.Height + 28);

			if (r.Contains(p))
				return i;
		}

		return -1;
	}

	#endregion

	#region Узлы

	private void SortStopsKeepSelection()
	{
		if (_dragIndex < 0 || _dragIndex >= Stops.Count)
			return;

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

		if (ordered.Count == 0)
			return SKColors.White;

		if (ordered.Count == 1)
			return ordered[0].Color;

		if (t <= ordered[0].Offset)
			return ordered[0].Color;

		if (t >= ordered[^1].Offset)
			return ordered[^1].Color;

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

	#endregion
}

/// <summary>
/// Редактор узора по клеткам — замена SkPatternGridEditor.
///
/// Левая кнопка кладёт цвет переднего плана, правая — цвет фона: так узор
/// правится и стирается одной рукой, без переключения инструмента.
/// </summary>
public sealed class AvPatternGridEditor : Control
{
	private SKColor[,] _cells = new SKColor[8, 8];

	private bool _mouseDown;
	private bool _paintWithBack;
	private int _lastCellX = -1;
	private int _lastCellY = -1;

	public int PatternWidth { get; private set; } = 8;
	public int PatternHeight { get; private set; } = 8;

	/// <summary>Текущие цвета рисования, а не глобальная перекраска узора.</summary>
	public SKColor FrontColor { get; set; } = SKColors.Black;

	public SKColor BackColor { get; set; } = SKColors.White;

	public Action<string, int, int>? PatternChanged { get; set; }

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvPatternGridEditor()
	{
		// Редактор узора служит двум задачам: сетка заливки размером
		// до 64x64 и однострочный штрих обводки высотой в 34 пикселя.
		// Поэтому минимум низкий — высоту в каждом случае задаёт панель.
		MinWidth = 60;
		MinHeight = 20;
		Focusable = true;

		SetSize(8, 8, SKColors.White);
	}

	#region Модель узора

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

		InvalidateVisual();
		RaiseChanged();
	}

	public void LoadFromDefinition(string definition, int width, int height, SKColor fallbackColor)
	{
		PatternWidth = Math.Clamp(width, 1, 256);
		PatternHeight = Math.Clamp(height, 1, 256);
		_cells = FillStyle.DeserializePatternColors(
			definition, PatternWidth, PatternHeight, fallbackColor);

		InvalidateVisual();
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

		InvalidateVisual();
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

		InvalidateVisual();
		RaiseChanged();
	}

	public SKBitmap BuildBitmap()
	{
		var bmp = new SKBitmap(
			PatternWidth, PatternHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

		for (int y = 0; y < PatternHeight; y++)
		{
			for (int x = 0; x < PatternWidth; x++)
				bmp.SetPixel(x, y, _cells[y, x]);
		}

		return bmp;
	}

	private void RaiseChanged()
	{
		PatternChanged?.Invoke(GetDefinition(), PatternWidth, PatternHeight);
	}

	#endregion

	#region Отрисовка

	public override void Render(DrawingContext context)
	{
		var full = new Rect(0, 0, Bounds.Width, Bounds.Height);

		if (full.Width <= 0 || full.Height <= 0)
			return;

		context.DrawRectangle(
			AvTheme.Surface,
			new Pen(AvTheme.BorderLine, 1),
			new RoundedRect(full.Deflate(0.5), AvTheme.Radius, AvTheme.Radius));

		var grid = GridRect();

		if (grid.Width <= 0 || grid.Height <= 0)
			return;

		context.FillRectangle(AvTheme.PanelBack, grid);

		double cell = CellSize(grid);

		for (int y = 0; y < PatternHeight; y++)
		{
			for (int x = 0; x < PatternWidth; x++)
			{
				context.FillRectangle(
					AvTheme.Brush(_cells[y, x]),
					new Rect(grid.X + x * cell, grid.Y + y * cell, cell, cell));
			}
		}

		// Сетка рисуется только когда клетка достаточно крупная: на узоре
		// 64x64 линии сетки заняли бы больше места, чем сами клетки,
		// и узор превратился бы в серое поле.
		if (cell < 5)
			return;

		var pen = new Pen(
			AvTheme.Brush(new SKColor(
				SkUiTheme.Current.Text.Red,
				SkUiTheme.Current.Text.Green,
				SkUiTheme.Current.Text.Blue,
				72)),
			1);

		for (int x = 0; x <= PatternWidth; x++)
		{
			double gx = Math.Round(grid.X + x * cell) + 0.5;

			context.DrawLine(pen, new Point(gx, grid.Y), new Point(gx, grid.Bottom));
		}

		for (int y = 0; y <= PatternHeight; y++)
		{
			double gy = Math.Round(grid.Y + y * cell) + 0.5;

			context.DrawLine(pen, new Point(grid.X, gy), new Point(grid.Right, gy));
		}
	}

	/// <summary>
	/// Поле клеток — квадратное и по центру: при неквадратном узоре
	/// растянутые клетки читались бы как другой рисунок.
	/// </summary>
	private Rect GridRect()
	{
		const double pad = 8;

		double innerW = Math.Max(0, Bounds.Width - pad * 2);
		double innerH = Math.Max(0, Bounds.Height - pad * 2);

		if (innerW <= 0 || innerH <= 0 || PatternWidth <= 0 || PatternHeight <= 0)
			return default;

		double cell = Math.Min(innerW / PatternWidth, innerH / PatternHeight);
		double gridW = cell * PatternWidth;
		double gridH = cell * PatternHeight;

		return new Rect(
			pad + (innerW - gridW) / 2,
			pad + (innerH - gridH) / 2,
			gridW,
			gridH);
	}

	private double CellSize(Rect grid)
	{
		return Math.Min(grid.Width / PatternWidth, grid.Height / PatternHeight);
	}

	#endregion

	#region Ввод

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);
		var cell = HitCell(point.Position);

		Focus();

		if (cell == null)
			return;

		_mouseDown = true;
		_paintWithBack = point.Properties.IsRightButtonPressed;

		e.Pointer.Capture(this);

		PaintCell(cell.Value.x, cell.Value.y);

		_lastCellX = cell.Value.x;
		_lastCellY = cell.Value.y;

		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		if (!_mouseDown)
			return;

		var cell = HitCell(e.GetPosition(this));

		if (cell == null)
			return;

		if (cell.Value.x == _lastCellX && cell.Value.y == _lastCellY)
			return;

		PaintCell(cell.Value.x, cell.Value.y);

		_lastCellX = cell.Value.x;
		_lastCellY = cell.Value.y;

		e.Handled = true;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);

		_mouseDown = false;
		_lastCellX = -1;
		_lastCellY = -1;

		e.Pointer.Capture(null);
	}

	private void PaintCell(int x, int y)
	{
		if (x < 0 || x >= PatternWidth || y < 0 || y >= PatternHeight)
			return;

		SKColor color = _paintWithBack ? BackColor : FrontColor;

		if (_cells[y, x] == color)
			return;

		_cells[y, x] = color;

		InvalidateVisual();
		RaiseChanged();
	}

	private (int x, int y)? HitCell(Point p)
	{
		var grid = GridRect();

		if (grid.Width <= 0 || !grid.Contains(p))
			return null;

		double cell = CellSize(grid);

		int x = (int)((p.X - grid.X) / cell);
		int y = (int)((p.Y - grid.Y) / cell);

		if (x < 0 || x >= PatternWidth || y < 0 || y >= PatternHeight)
			return null;

		return (x, y);
	}

	#endregion
}
