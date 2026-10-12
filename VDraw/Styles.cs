using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

public sealed class StrokeStyle
{
	public bool Enabled { get; set; } = true;
	public SKColor Color { get; set; } = Settings.shape_default_line_color;

	/// <summary>
	/// Толщина обводки В ЕДИНИЦАХ СТРАНИЦЫ, а не в миллиметрах.
	///
	/// Раньше здесь лежали миллиметры, и это расходилось с остальной
	/// геометрией: <see cref="Shape.SetShapeNewUoM"/> пересчитывает
	/// координаты так, что фигура сохраняет ЧИСЛА, а не физический размер, —
	/// прямоугольник 100×50 на миллиметровой странице остаётся 100×50 после
	/// перехода на дюймы. Толщина же оставалась прежней в миллиметрах,
	/// и линия у выросшей в 25 раз фигуры превращалась в волосок.
	///
	/// Теперь толщина живёт в тех же единицах, что и координаты, и
	/// пересчитывается вместе с ними — как это давно сделано для кегля
	/// подписи размера (TextStyle.FontSizeMm).
	/// </summary>
	public float Thickness { get; set; } = Settings.DefaultLineThickness;

	public string Pattern { get; set; } = string.Empty;

	/// <summary>Длина полного цикла штрихового узора, В ЕДИНИЦАХ СТРАНИЦЫ.</summary>
	public float PatternLength { get; set; } = Settings.DefaultPatternLength;
	public string StartGlyph { get; set; } = string.Empty;
	public string EndGlyph { get; set; } = string.Empty;

	/// <summary>
	/// Размер глифа начала контура как множитель толщины линии.
	/// Значение 3 соответствует прежнему жёстко заданному размеру.
	/// </summary>
	public float StartGlyphSize { get; set; } = 3f;

	/// <summary>
	/// Размер глифа конца контура как множитель толщины линии.
	/// </summary>
	public float EndGlyphSize { get; set; } = 3f;

	/// <summary>
	/// Пересчёт при смене единиц страницы. Вызывается из
	/// <see cref="Shape.SetShapeNewUoM"/> тем же коэффициентом, что и геометрия.
	/// </summary>
	public void SetStrokeNewUoM(float ratio)
	{
		if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
			return;

		Thickness *= ratio;
		PatternLength *= ratio;
	}

	/// <summary>
	/// Размер глифа начала в мировых единицах.
	///
	/// Параметр больше не участвует в расчёте: толщина и без того задана
	/// в мировых единицах. Он оставлен в сигнатуре, потому что вызовов
	/// по проекту около полусотни, а деления теперь нет ни в одном.
	/// </summary>
	public float GetStartGlyphSizeWorld(float mmPerWorldUnit)
		=> GetGlyphSizeWorld(StartGlyphSize);

	/// <summary>Размер глифа конца в мировых единицах.</summary>
	public float GetEndGlyphSizeWorld(float mmPerWorldUnit)
		=> GetGlyphSizeWorld(EndGlyphSize);

	private float GetGlyphSizeWorld(float multiplier)
	{
		float thickness = MathF.Max(Thickness, 0.0001f);

		// Нулевой множитель означал бы невидимый глиф при выбранном типе —
		// такая же ловушка, как обнуление альфы у обводки. Держим минимум.
		float safeMultiplier = MathF.Max(multiplier, 0.1f);

		return thickness * safeMultiplier;
	}

	/// <summary>
	/// Кисть обводки. Параметр mmPerWorldUnit сохранён ради вызывающих,
	/// но в расчёте не участвует: толщина и длина цикла узора заданы
	/// в мировых единицах и переводить их больше не нужно.
	/// </summary>
	public SKPaint CreatePaint(float mmPerWorldUnit, bool selected = false)
	{
		float strokeWorld = MathF.Max(Thickness, 0.0001f);

		if (selected)
			strokeWorld *= Settings.shape_selected_line_thickness_rate;

		var p = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			Color = Color,
			StrokeWidth = strokeWorld,
			StrokeCap = SKStrokeCap.Round,
			StrokeJoin = SKStrokeJoin.Round
		};

		if (string.IsNullOrWhiteSpace(Pattern))
			return p;

		var parts = Pattern.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

		var units = new List<float>();

		foreach (var s in parts)
		{
			if (float.TryParse(
					s,
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture,
					out float v)
				&& v >= 0f)
			{
				units.Add(v);
			}
		}

		if (units.Count < 2)
			return p;

		// Доводим количество интервалов до чётного.
		if ((units.Count % 2) != 0)
			units.Add(0f);

		float totalUnits = units.Sum();
		if (totalUnits <= 0f)
			return p;

		// Длина полного цикла узора — в мировых единицах.
		float cycleWorld = PatternLength > 0f
			? PatternLength
			: MathF.Max(Thickness, 0.0001f) * 4f;

		float scale = cycleWorld / totalUnits;

		var worldIntervals = units.Select(u => u * scale).ToArray();

		// Есть ли хотя бы один положительный ON-интервал?
		bool hasPositiveOn = false;
		for (int i = 0; i < worldIntervals.Length; i += 2)
		{
			if (worldIntervals[i] > 0f)
			{
				hasPositiveOn = true;
				break;
			}
		}

		// Паттерн без видимых сегментов — вырожденный. Прежний код обнулял
		// альфу, и обводка молча исчезала. Возвращаем сплошную линию:
		// выбор типа линии не должен лишать фигуру обриса.
		if (!hasPositiveOn)
			return p;

		try
		{
			p.PathEffect = SKPathEffect.CreateDash(worldIntervals, 0f);
		}
		catch
		{
			p.PathEffect = null;
		}

		return p;
	}

	public StrokeStyle Clone()
	{
		return new StrokeStyle
		{
			Enabled = Enabled,
			Color = Color,
			Thickness = Thickness,
			Pattern = Pattern,
			PatternLength = PatternLength,
			StartGlyph = StartGlyph,
			EndGlyph = EndGlyph,
			StartGlyphSize = StartGlyphSize,
			EndGlyphSize = EndGlyphSize
		};
	}


}

public sealed class GradientStop
{
	public float Offset { get; set; }
	public SKColor Color { get; set; }

	public GradientStop()
	{
	}

	public GradientStop(float offset, SKColor color)
	{
		Offset = Math.Clamp(offset, 0f, 1f);
		Color = color;
	}

	public GradientStop Clone() => new GradientStop(Offset, Color);
}

public sealed class FillStyle
{
	/// <summary>
	/// Вид заливки.
	///
	/// Смена вида сбрасывает FillTransformInitialized: точки Start и End
	/// у разных заливок означают разное. У линейного градиента это концы
	/// отрезка перехода, у плиточных — противоположные углы плитки.
	/// Оставленные от прежнего вида, они дают бессмысленную плитку —
	/// например во всю ширину фигуры от градиента, растянутого по её
	/// габаритам. Сброс заставляет EnsureFillTransformDefaults разложить
	/// ручки заново, уже под новый вид.
	/// </summary>
	public FillKind Kind
	{
		get => _kind;
		set
		{
			if (_kind == value)
				return;

			_kind = value;
			FillTransformInitialized = false;
		}
	}

	private FillKind _kind = FillKind.Solid;
	public bool Enabled { get; set; } = true;
	public SKColor SolidColor { get; set; } = Settings.shape_default_fill_color;
	private const float MinTileDiagonal = 0.5f;
	private const int MaxTileSidePx = 4096;

	/// <summary>
	/// Желаемая диагональ РАСТРА плитки в пикселях.
	///
	/// Разрешение растра не имеет отношения к размеру плитки на листе —
	/// это просто запас чёткости. Раньше эти две вещи были связаны:
	/// растр строился размером «пиксель на мировую единицу», и ручки
	/// заливки меняли не размер узора, а его разрешение. Узор из восьми
	/// клеток на плитке в десять единиц превращался в семь клеток —
	/// часть рядов терялась при пересчёте, — а каждая клетка неизменно
	/// занимала одну мировую единицу и потому выглядела огромной
	/// независимо от того, куда утащили ручку.
	/// </summary>
	private const float TargetTileDiagonalPx = 512f;

	public bool Tiling { get; set; } = true;
	public bool RotateWithShape { get; set; } = true;
	public bool ScaleWithShape { get; set; } = true;

	public SKColor GradientColor1 { get; set; } = Settings.shape_default_fountain_fill_color1;
	public SKColor GradientColor2 { get; set; } = Settings.shape_default_fountain_fill_color2;

	public List<GradientStop> Stops { get; } = new()
	{
		new GradientStop(0f, Settings.shape_default_fountain_fill_color1),
		new GradientStop(1f, Settings.shape_default_fountain_fill_color2)
	};

	public string GradientMode { get; set; } = "Linear";
	public float Angle { get; set; } = 0f;

	public SKColor PatternFrontColor { get; set; } = Settings.shape_default_fill_pat_forecolor;
	public SKColor PatternBackColor { get; set; } = Settings.shape_default_fill_pat_backcolor;
	public int PatternWidth { get; set; } = 8;
	public int PatternHeight { get; set; } = 8;
	public string PatternId { get; set; } = string.Empty;

	public string PatternDefinition { get; set; } =
		"00000000,00000000,00000000,00000000,FF000000,FF000000,FF000000,FF000000;" +
		"00000000,00000000,00000000,00000000,FF000000,FF000000,FF000000,FF000000;" +
		"FF000000,FF000000,FF000000,FF000000,00000000,00000000,00000000,00000000;" +
		"FF000000,FF000000,FF000000,FF000000,00000000,00000000,00000000,00000000;" +
		"00000000,00000000,00000000,00000000,FF000000,FF000000,FF000000,FF000000;" +
		"00000000,00000000,00000000,00000000,FF000000,FF000000,FF000000,FF000000;" +
		"FF000000,FF000000,FF000000,FF000000,00000000,00000000,00000000,00000000;" +
		"FF000000,FF000000,FF000000,FF000000,00000000,00000000,00000000,00000000";

	public string TextureId { get; set; } = string.Empty;
	public string TextureFilePath { get; set; } = string.Empty;
	public SKBitmap? TextureBitmap { get; set; }

	public SKPoint StartPoint { get; set; } = SKPoint.Empty;
	public SKPoint EndPoint { get; set; } = SKPoint.Empty;
	public bool FillTransformInitialized { get; set; } = false;
	public bool SmoothTexture { get; set; } = true;

	public bool SupportsInteractiveHandles =>
		Enabled && Kind is FillKind.LinearGradient or FillKind.RadialGradient or FillKind.RectangularGradient or FillKind.Pattern or FillKind.Texture;

	/// <summary>
	/// Собственный размер источника заливки. Задаёт пропорции плитки и угол
	/// её внутренней диагонали.
	/// </summary>
	public SKSizeI GetPatternSourceSize()
	{
		switch (Kind)
		{
			case FillKind.Pattern:
				// Те же границы, что в BuildPatternBitmap, иначе пропорции
				// разъедутся на крайних значениях.
				return new SKSizeI(
					Math.Clamp(PatternWidth, 2, 256),
					Math.Clamp(PatternHeight, 2, 256));

			case FillKind.Texture:
				if (TextureBitmap != null &&
					TextureBitmap.Width > 0 &&
					TextureBitmap.Height > 0)
				{
					return new SKSizeI(TextureBitmap.Width, TextureBitmap.Height);
				}

				return new SKSizeI(64, 64);

			default:
				return new SKSizeI(1, 1);
		}
	}

	// <summary>Длина вектора Start -> End, то есть требуемая диагональ плитки.</summary>
	private float GetTileDiagonal()
	{
		float dx = EndPoint.X - StartPoint.X;
		float dy = EndPoint.Y - StartPoint.Y;

		float d = MathF.Sqrt(dx * dx + dy * dy);

		if (float.IsNaN(d) || d < MinTileDiagonal)
			d = MinTileDiagonal;

		return d;
	}


	/// <summary>
	/// Проецирует произвольную точку на диагональ плитки и возвращает позицию
	/// ручки End. Направление диагонали задано пропорциями источника, поэтому
	/// ручка всегда остаётся точно в углу плитки, а её удаление от Start
	/// задаёт длину диагонали.
	/// </summary>
	public SKPoint ProjectDiagonalHandle(SKPoint start, SKPoint desired)
	{
		var size = GetPatternSourceSize();

		float sw = MathF.Max(1f, size.Width);
		float sh = MathF.Max(1f, size.Height);

		float srcDiagonal = MathF.Sqrt(sw * sw + sh * sh);

		// Единичный вектор вдоль диагонали плитки.
		float ux = sw / srcDiagonal;
		float uy = sh / srcDiagonal;

		// Проекция желаемой точки на луч диагонали.
		float vx = desired.X - start.X;
		float vy = desired.Y - start.Y;

		float projected = vx * ux + vy * uy;

		if (projected < MinTileDiagonal)
			projected = MinTileDiagonal;

		return new SKPoint(
			start.X + ux * projected,
			start.Y + uy * projected);
	}

	/// <summary>
	/// Растеризует источник в размер GetFittedSize. Пропорции источника
	/// сохраняются; ни размер плитки на листе, ни её поворот здесь не
	/// участвуют — и то, и другое задаёт матрица шейдера в BuildTileMatrix.
	///
	/// Сглаживание применяется именно тут, при увеличении растра: для
	/// узора из нескольких клеток разница между ступенчатым и мягким
	/// краем видна как раз на этом переходе.
	/// </summary>
	/// <summary>
	/// Размер растра плитки в пикселях.
	///
	/// Считается только от собственных пропорций источника и запаса
	/// чёткости, но НЕ от размера плитки на листе: за него отвечает
	/// матрица шейдера. Мельче источника растр не бывает — уменьшение
	/// теряло бы клетки узора.
	/// </summary>
	private SKSizeI GetFittedSize()
	{
		var size = GetPatternSourceSize();

		float sw = MathF.Max(1f, size.Width);
		float sh = MathF.Max(1f, size.Height);

		float srcDiagonal = MathF.Sqrt(sw * sw + sh * sh);
		float factor = MathF.Max(1f, TargetTileDiagonalPx / srcDiagonal);

		return new SKSizeI(
			Math.Clamp((int)MathF.Round(sw * factor), 1, MaxTileSidePx),
			Math.Clamp((int)MathF.Round(sh * factor), 1, MaxTileSidePx));
	}

	private SKBitmap BuildFittedBitmap(SKBitmap source)
	{
		var target = GetFittedSize();

		int width = target.Width;
		int height = target.Height;

		var fitted = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

		using var fittedCanvas = new SKCanvas(fitted);
		fittedCanvas.Clear(SKColors.Transparent);

		using var image = SKImage.FromBitmap(source);

		var sampling = SmoothTexture
			? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
			: new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

		var srcRect = new SKRect(0, 0, source.Width, source.Height);
		var dstRect = new SKRect(0, 0, width, height);

		fittedCanvas.DrawImage(image, srcRect, dstRect, sampling);

		return fitted;
	}

	/// <summary>
	/// Локальная матрица шейдера: переносит плитку в StartPoint и поворачивает
	/// так, чтобы её собственная диагональ смотрела вдоль вектора Start -> End.
	///
	/// Проверка: угол внутренней диагонали плитки равен atan2(sh, sw). После
	/// поворота на (angleWorld - angleTile) он становится равен angleWorld, а
	/// длина диагонали уже подогнана в BuildFittedBitmap. Значит дальний угол
	/// плитки после переноса попадает ровно в EndPoint — ручка сидит в углу.
	/// </summary>
	private SKMatrix BuildTileMatrix()
	{
		var fitted = GetFittedSize();

		float sw = MathF.Max(1f, fitted.Width);
		float sh = MathF.Max(1f, fitted.Height);

		float dx = EndPoint.X - StartPoint.X;
		float dy = EndPoint.Y - StartPoint.Y;

		var translate = SKMatrix.CreateTranslation(StartPoint.X, StartPoint.Y);

		// Растр строится в пикселях, а плитка живёт в мировых единицах:
		// перевод между ними — это и есть размер узора на листе. Именно
		// здесь ручка заливки получает власть над масштабом.
		float fittedDiagonal = MathF.Sqrt(sw * sw + sh * sh);
		float scaleFactor = GetTileDiagonal() / fittedDiagonal;

		var scale = SKMatrix.CreateScale(scaleFactor, scaleFactor);

		// Вектор нулевой длины угла не задаёт — оставляем плитку неповёрнутой.
		if (MathF.Abs(dx) < 1e-4f && MathF.Abs(dy) < 1e-4f)
			return Geometry.Multiply(translate, scale);

		float angleWorld = MathF.Atan2(dy, dx);
		float angleTile = MathF.Atan2(sh, sw);

		float rotationDeg = (angleWorld - angleTile) * 180f / MathF.PI;

		var rotate = SKMatrix.CreateRotationDegrees(rotationDeg);

		// Multiply(A, B) применяет сначала B, затем A: сначала растяжение
		// растра до размера плитки, затем поворот вокруг её начала,
		// затем перенос в StartPoint. Дальний угол плитки после этого
		// попадает ровно в EndPoint — ручка сидит в углу.
		return Geometry.Multiply(translate, Geometry.Multiply(rotate, scale));
	}


	/// <summary>
	/// Пересчёт при смене единиц страницы.
	///
	/// Точки градиента и плитки заданы в мировых координатах и живут
	/// отдельно от матрицы фигуры, поэтому масштабировать их приходится
	/// явно — тем же коэффициентом, что и геометрию.
	/// </summary>
	public void SetFillNewUoM(float ratio)
	{
		if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
			return;

		if (!FillTransformInitialized)
			return;

		StartPoint = new SKPoint(StartPoint.X * ratio, StartPoint.Y * ratio);
		EndPoint = new SKPoint(EndPoint.X * ratio, EndPoint.Y * ratio);
	}

	public void EnsureFillTransformDefaults(SKRect bounds)
	{
		if (FillTransformInitialized)
			return;

		switch (Kind)
		{
			case FillKind.LinearGradient:
				StartPoint = new SKPoint(bounds.Left, bounds.MidY);
				EndPoint = new SKPoint(bounds.Right, bounds.MidY);
				break;

			case FillKind.RadialGradient:
			case FillKind.RectangularGradient:
				StartPoint = new SKPoint(bounds.MidX, bounds.MidY);
				EndPoint = new SKPoint(bounds.Right, bounds.Top);
				break;

			case FillKind.Pattern:
			case FillKind.Texture:
				{
					// Размер плитки считается ОТ ФИГУРЫ, а не от растра.
					//
					// GetPatternSourceSize отдаёт пиксели источника: у текстуры
					// это её собственный размер, нередко пятьсот и больше.
					// Прежний код прибавлял их прямо к координатам страницы,
					// и вторая ручка оказывалась в пятистах единицах от центра
					// фигуры — далеко за её пределами и часто за краем листа.
					// Найти её можно было только отматывая вид.
					//
					// Пропорции растра при этом сохраняются: плитка вписывается
					// в четверть меньшей стороны фигуры, поэтому узор виден
					// сразу несколькими повторами и понятно, что тянуть.
					var size = GetPatternSourceSize();

					float sw = MathF.Max(1f, size.Width);
					float sh = MathF.Max(1f, size.Height);

					float shortSide = MathF.Min(MathF.Abs(bounds.Width), MathF.Abs(bounds.Height));

					// Вырожденные габариты: у только что созданной фигуры
					// ширина и высота могут быть ещё нулевыми. Плитка в ноль
					// схлопнула бы обе ручки в одну точку, и взяться было бы
					// не за что.
					float target = shortSide > MinTileDiagonal
						? shortSide * 0.25f
						: MinTileDiagonal;

					float scale = target / MathF.Max(sw, sh);

					StartPoint = new SKPoint(bounds.Left, bounds.Top);
					EndPoint = new SKPoint(
						bounds.Left + sw * scale,
						bounds.Top + sh * scale);
					break;
				}

			default:
				StartPoint = SKPoint.Empty;
				EndPoint = SKPoint.Empty;
				break;
		}

		FillTransformInitialized = true;
	}

	public SKPaint CreatePaint(SKRect bounds, SKMatrix shapeWorld)
	{
		if (!FillTransformInitialized)
			EnsureFillTransformDefaults(bounds);

		var p = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SolidColor
		};

		if (!Enabled)
			return p;

		switch (Kind)
		{
			case FillKind.None:
				break;

			case FillKind.Solid:
				p.Color = SolidColor;
				break;

			case FillKind.LinearGradient:
			case FillKind.RadialGradient:
			case FillKind.RectangularGradient:
				ApplyGradient(p, bounds);
				break;

			case FillKind.Pattern:
				{
					using var source = BuildPatternBitmap();
					if (source != null)
					{
						using var fitted = BuildFittedBitmap(source);

						p.Shader = SKShader.CreateBitmap(
							fitted,
							GetTileMode(Tiling),
							GetTileMode(Tiling),
							BuildTileMatrix());
					}
				}
				break;

			case FillKind.Texture:
				if (TextureBitmap != null)
				{
					using var fitted = BuildFittedBitmap(TextureBitmap);

					p.Shader = SKShader.CreateBitmap(
						fitted,
						GetTileMode(Tiling),
						GetTileMode(Tiling),
						BuildTileMatrix());
				}
				break;

		}

		return p;
	}

	private void ApplyGradient(SKPaint paint, SKRect bounds)
	{
		if (Stops.Count == 0)
		{
			paint.Color = SolidColor;
			return;
		}

		EnsureFillTransformDefaults(bounds);

		var ordered = Stops.OrderBy(s => s.Offset).ToList();
		SKColor[] colors = ordered.Select(s => s.Color).ToArray();
		float[] pos = ordered.Select(s => Math.Clamp(s.Offset, 0f, 1f)).ToArray();

		string mode = string.IsNullOrWhiteSpace(GradientMode) ? "Linear" : GradientMode;
		var start = StartPoint;
		var end = EndPoint;

		if (Geometry.Distance(start, end) < 0.0001f)
			end = new SKPoint(start.X + MathF.Max(bounds.Width, 1f), start.Y);

		if (Kind == FillKind.RadialGradient || mode.Equals("Radial", StringComparison.OrdinalIgnoreCase))
		{
			float radius = Geometry.Distance(start, end);
			paint.Shader = SKShader.CreateRadialGradient(start, MathF.Max(radius, 0.0001f), colors, pos, SKShaderTileMode.Clamp);
			return;
		}

		if (Kind == FillKind.RectangularGradient || mode.Equals("Square", StringComparison.OrdinalIgnoreCase))
		{
			paint.Shader = CreateRectangularGradientShader(start, end, colors, pos);
			return;
		}

		paint.Shader = SKShader.CreateLinearGradient(start, end, colors, pos, SKShaderTileMode.Clamp);
	}

	private static SKShader CreateRectangularGradientShader(SKPoint start, SKPoint end, SKColor[] colors, float[] pos)
	{
		float halfW = MathF.Abs(end.X - start.X);
		float halfH = MathF.Abs(end.Y - start.Y);

		if (halfW < 0.0001f)
			halfW = 1f;

		if (halfH < 0.0001f)
			halfH = 1f;

		int bmpW = Math.Max(2, (int)MathF.Ceiling(halfW * 2f));
		int bmpH = Math.Max(2, (int)MathF.Ceiling(halfH * 2f));

		var bmp = new SKBitmap(bmpW, bmpH, SKColorType.Bgra8888, SKAlphaType.Premul);

		float cx = (bmpW - 1) * 0.5f;
		float cy = (bmpH - 1) * 0.5f;

		for (int y = 0; y < bmpH; y++)
		{
			for (int x = 0; x < bmpW; x++)
			{
				float nx = MathF.Abs((x - cx) / MathF.Max(cx, 1f));
				float ny = MathF.Abs((y - cy) / MathF.Max(cy, 1f));

				float t = MathF.Max(nx, ny);
				t = Math.Clamp(t, 0f, 1f);

				bmp.SetPixel(x, y, SampleGradient(colors, pos, t));
			}
		}

		var localMatrix =
			Geometry.Multiply(
				SKMatrix.CreateTranslation(start.X, start.Y),
				SKMatrix.CreateTranslation(-cx, -cy));

		return SKShader.CreateBitmap(
			bmp,
			SKShaderTileMode.Clamp,
			SKShaderTileMode.Clamp,
			localMatrix);
	}

	private static SKColor SampleGradient(SKColor[] colors, float[] pos, float t)
	{
		if (colors == null || colors.Length == 0)
			return SKColors.Transparent;

		if (colors.Length == 1 || pos == null || pos.Length != colors.Length)
			return colors[0];

		t = Math.Clamp(t, 0f, 1f);

		for (int i = 0; i < pos.Length - 1; i++)
		{
			float p0 = pos[i];
			float p1 = pos[i + 1];

			if (t >= p0 && t <= p1)
			{
				float span = p1 - p0;
				float u = span <= 0.000001f ? 0f : (t - p0) / span;
				return Geometry.LerpColor(colors[i], colors[i + 1], u);
			}
		}

		if (t <= pos[0])
			return colors[0];

		return colors[^1];
	}

	private static SKShaderTileMode GetTileMode(bool tiling)
		=> tiling ? SKShaderTileMode.Repeat : SKShaderTileMode.Decal;

	public SKBitmap BuildPatternBitmap()
	{
		int w = Math.Clamp(PatternWidth, 2, 256);
		int h = Math.Clamp(PatternHeight, 2, 256);

		var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
		var colors = DeserializePatternColors(PatternDefinition, w, h, PatternBackColor);

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				bmp.SetPixel(x, y, colors[y, x]);

		return bmp;
	}

	public static string SerializePatternColors(SKColor[,] colors, int width, int height)
	{
		var rows = new string[height];

		for (int y = 0; y < height; y++)
		{
			var cols = new string[width];
			for (int x = 0; x < width; x++)
				cols[x] = Geometry.ColorToHex(colors[y, x]);

			rows[y] = string.Join(",", cols);
		}

		return string.Join(";", rows);
	}

	public static SKColor[,] DeserializePatternColors(string definition, int width, int height, SKColor defaultColor)
	{
		width = Math.Clamp(width, 2, 256);
		height = Math.Clamp(height, 2, 256);

		var result = new SKColor[height, width];

		for (int y = 0; y < height; y++)
			for (int x = 0; x < width; x++)
				result[y, x] = defaultColor;

		if (string.IsNullOrWhiteSpace(definition))
			return result;

		string[] rows = definition.Split(';', StringSplitOptions.RemoveEmptyEntries);

		for (int y = 0; y < Math.Min(height, rows.Length); y++)
		{
			string[] cols = rows[y].Split(',', StringSplitOptions.RemoveEmptyEntries);
			for (int x = 0; x < Math.Min(width, cols.Length); x++)
			{
				if (Geometry.TryParseHexColor(cols[x], out var c))
					result[y, x] = c;
			}
		}

		return result;
	}

	public FillStyle Clone()
	{
		var clone = new FillStyle
		{
			Kind = Kind,
			Enabled = Enabled,
			SolidColor = SolidColor,

			Tiling = Tiling,
			RotateWithShape = RotateWithShape,
			ScaleWithShape = ScaleWithShape,

			GradientColor1 = GradientColor1,
			GradientColor2 = GradientColor2,

			GradientMode = GradientMode,
			Angle = Angle,

			PatternFrontColor = PatternFrontColor,
			PatternBackColor = PatternBackColor,
			PatternWidth = PatternWidth,
			PatternHeight = PatternHeight,
			PatternId = PatternId,
			PatternDefinition = PatternDefinition,

			TextureId = TextureId,
			TextureFilePath = TextureFilePath,
			TextureBitmap = TextureBitmap != null ? new SKBitmap(TextureBitmap.Info) : null,

			StartPoint = StartPoint,
			EndPoint = EndPoint,
			FillTransformInitialized = FillTransformInitialized,
			SmoothTexture = SmoothTexture
		};

		if (TextureBitmap != null && clone.TextureBitmap != null)
			TextureBitmap.CopyTo(clone.TextureBitmap);

		clone.Stops.Clear();
		foreach (var stop in Stops)
			clone.Stops.Add(stop.Clone());

		return clone;
	}
}