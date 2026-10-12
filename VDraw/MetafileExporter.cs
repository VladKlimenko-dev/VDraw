using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using SkiaSharp;

using VDraw.Shapes;

namespace VDraw.Documents;

/// <summary>
/// Экспорт выделенных фигур в метафайлы Windows: WMF и EMF.
///
/// Рисование идёт через GDI+, а не через Skia. Иначе нельзя: метафайл — это
/// запись вызовов графической подсистемы Windows, и записать её умеет только
/// она сама. Skia в файл такого вида не пишет.
///
/// Писать второй отрисовщик для каждого вида фигуры при этом не пришлось.
/// Каждая фигура умеет отдать свой мировой контур — SKPath, — а контур
/// состоит из отрезков и кубических кривых, которые один к одному ложатся
/// на GraphicsPath. Текст до этого превращается в кривые в PrepareShapes,
/// растровые фигуры рисуются отдельной веткой. Остаётся один переходник
/// вместо полутора десятков.
///
/// Чего метафайл не умеет и что при экспорте теряется:
///   градиент — заменяется сплошным цветом (для WMF; EMF рисует настоящий);
///   узор и текстура — заменяются сплошным цветом;
///   полупрозрачность — WMF её не знает вовсе, цвет берётся непрозрачным;
///   тень и размытие — не переносятся.
/// Всё это следствие формата 1990 года, а не упрощение ради скорости.
/// </summary>
public static class MetafileExporter
{
	/// <summary>
	/// Разрешение записи метафайла.
	///
	/// Координаты внутри WMF шестнадцатибитные, предел — 32767. При 600 точках
	/// на дюйм это чуть меньше полутора метров по стороне, чего хватает любому
	/// разумному чертежу, а точность даёт лучшую из достижимых.
	/// </summary>
	private const float ExportDpi = 600f;

	/// <summary>Предел координаты WMF. За ним рисунок обрезается молча.</summary>
	private const float WmfCoordinateLimit = 32000f;

	#region Точка входа

	/// <summary>
	/// Пишет выделенное в метафайл. wmf = true — WMF, иначе EMF.
	///
	/// Возвращает false, если рисовать нечего.
	/// </summary>
	public static bool Export(string path, Page page, IReadOnlyList<Shape> shapes, bool wmf)
	{
		if (page == null || shapes == null || shapes.Count == 0)
			return false;

		var bounds = ShapeExporter.GetExportBounds(page, shapes);

		if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
			return false;

		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);

		// Мировые единицы в точки метафайла.
		float scale = ExportDpi / 25.4f * mmPerWorldUnit;

		float widthDots = bounds.Width * scale;
		float heightDots = bounds.Height * scale;

		// Крупный чертёж не влезает в шестнадцать бит. Уменьшаем разрешение
		// записи, а не обрезаем рисунок: потеря доли точки незаметна,
		// потеря половины чертежа — нет.
		if (wmf && (widthDots > WmfCoordinateLimit || heightDots > WmfCoordinateLimit))
		{
			float fit = MathF.Min(WmfCoordinateLimit / widthDots, WmfCoordinateLimit / heightDots);

			scale *= fit;
			widthDots *= fit;
			heightDots *= fit;
		}

		int pixelWidth = Math.Max(1, (int)MathF.Ceiling(widthDots));
		int pixelHeight = Math.Max(1, (int)MathF.Ceiling(heightDots));

		using var metafile = CreateMetafile(pixelWidth, pixelHeight, out var reference);

		try
		{
			using (var graphics = Graphics.FromImage(metafile))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				// Порядок как в остальных экспортах: точка мира ложится
				// в (p - bounds.TopLeft) * scale. GDI+ применяет
				// преобразования в порядке объявления, поэтому сначала
				// масштаб, потом перенос — обратно канве Skia.
				graphics.ScaleTransform(scale, scale);
				graphics.TranslateTransform(-bounds.Left, -bounds.Top);

				DrawShapes(graphics, shapes, mmPerWorldUnit, wmf);
			}

			if (wmf)
				WriteWmf(metafile, path, pixelWidth, pixelHeight);
			else
				WriteEmf(metafile, path);
		}
		finally
		{
			reference?.Dispose();
		}

		return true;
	}

	/// <summary>
	/// Создаёт метафайл в памяти.
	///
	/// Нужен опорный контекст устройства: метафайл записывает вызовы
	/// в единицах устройства, и без образца GDI+ не знает, каких.
	/// Берём контекст экрана через временный растр 1x1 — заводить ради
	/// этого окно незачем.
	/// </summary>
	private static Metafile CreateMetafile(int width, int height, out Bitmap? reference)
	{
		reference = new Bitmap(1, 1);

		using var referenceGraphics = Graphics.FromImage(reference);

		IntPtr hdc = referenceGraphics.GetHdc();

		try
		{
			// EmfOnly, а не EmfPlusDual: двойная запись вкладывает внутрь
			// и команды GDI+, и их GDI-перевод, отчего файл вдвое толще,
			// а перевод в WMF всё равно берёт только вторую половину.
			return new Metafile(
				hdc,
				new Rectangle(0, 0, width, height),
				MetafileFrameUnit.Pixel,
				EmfType.EmfOnly);
		}
		finally
		{
			referenceGraphics.ReleaseHdc(hdc);
		}
	}

	#endregion

	#region Отрисовка

	private static void DrawShapes(
		Graphics graphics,
		IReadOnlyList<Shape> shapes,
		float mmPerWorldUnit,
		bool wmf)
	{
		foreach (var shape in shapes)
		{
			if (shape is GuideLine || !shape.IsVisible)
				continue;

			if (shape is ImageShape image)
			{
				DrawImageShape(graphics, image);
				continue;
			}

			DrawPathShape(graphics, shape, mmPerWorldUnit, wmf);
		}
	}

	private static void DrawPathShape(Graphics graphics, Shape shape, float mmPerWorldUnit, bool wmf)
	{
		using var skPath = shape.BuildWorldPath();

		if (skPath == null || skPath.IsEmpty)
			return;

		using var path = ToGraphicsPath(skPath);

		if (path.PointCount == 0)
			return;

		if (shape.Fill != null && shape.Fill.Enabled && shape.Fill.Kind != FillKind.None)
		{
			using var brush = CreateBrush(shape.Fill, path, wmf);

			if (brush != null)
				graphics.FillPath(brush, path);
		}

		if (shape.Stroke != null && shape.Stroke.Enabled && shape.Stroke.Thickness > 0f)
		{
			using var pen = CreatePen(shape.Stroke, wmf);
			graphics.DrawPath(pen, path);
		}
	}

	/// <summary>
	/// Переносит контур Skia в контур GDI+.
	///
	/// Оба хранят одно и то же — последовательность отрезков и кубических
	/// кривых, — поэтому перенос дословный. Квадратичная кривая переводится
	/// в кубическую: у GraphicsPath квадратичных нет, а подъём степени
	/// точен и общеизвестен.
	///
	/// Коническая кривая (дуга окружности в записи Skia) разбивается
	/// самим Skia на квадратичные вызовом ConvertConicToQuads — писать
	/// своё разбиение дуг незачем.
	/// </summary>
	private static GraphicsPath ToGraphicsPath(SKPath source)
	{
		var path = new GraphicsPath
		{
			FillMode = source.FillType == SKPathFillType.EvenOdd
				? FillMode.Alternate
				: FillMode.Winding
		};

		using var iterator = source.CreateRawIterator();

		var points = new SKPoint[4];
		var current = new PointF();
		bool figureOpen = false;

		SKPathVerb verb;

		while ((verb = iterator.Next(points)) != SKPathVerb.Done)
		{
			switch (verb)
			{
				case SKPathVerb.Move:
					if (figureOpen)
						path.StartFigure();

					current = ToPointF(points[0]);
					figureOpen = true;
					break;

				case SKPathVerb.Line:
					path.AddLine(current, ToPointF(points[1]));
					current = ToPointF(points[1]);
					break;

				case SKPathVerb.Quad:
					{
						var c1 = Lerp(points[0], points[1], 2f / 3f);
						var c2 = Lerp(points[2], points[1], 2f / 3f);

						path.AddBezier(current, ToPointF(c1), ToPointF(c2), ToPointF(points[2]));
						current = ToPointF(points[2]);
						break;
					}

				case SKPathVerb.Conic:
					{
						// Вес конической кривой лежит в отдельном поле итератора.
						var quads = SKPath.ConvertConicToQuads(
							points[0], points[1], points[2], iterator.ConicWeight(), 2);

						for (int i = 0; i + 2 < quads.Length; i += 2)
						{
							var c1 = Lerp(quads[i], quads[i + 1], 2f / 3f);
							var c2 = Lerp(quads[i + 2], quads[i + 1], 2f / 3f);

							path.AddBezier(current, ToPointF(c1), ToPointF(c2), ToPointF(quads[i + 2]));
							current = ToPointF(quads[i + 2]);
						}

						break;
					}

				case SKPathVerb.Cubic:
					path.AddBezier(
						current,
						ToPointF(points[1]),
						ToPointF(points[2]),
						ToPointF(points[3]));

					current = ToPointF(points[3]);
					break;

				case SKPathVerb.Close:
					path.CloseFigure();
					figureOpen = false;
					break;
			}
		}

		return path;
	}

	private static PointF ToPointF(SKPoint p) => new(p.X, p.Y);

	private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
		=> new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	#endregion

	#region Кисти и перья

	/// <summary>
	/// Кисть по заливке фигуры.
	///
	/// В EMF настоящий градиент рисуется как есть. В WMF градиента нет,
	/// и он заменяется сплошным цветом — средним между крайними точками,
	/// а не первым из них: заменить растяжку от белого к чёрному белым
	/// значило бы потерять фигуру на белом листе.
	///
	/// Узор и текстура заменяются сплошным цветом всегда. Перенести их
	/// можно было бы растром, но растр внутри метафайла теряет то
	/// единственное, ради чего метафайл и берут, — разрешение печати.
	/// </summary>
	private static Brush? CreateBrush(FillStyle fill, GraphicsPath path, bool wmf)
	{
		switch (fill.Kind)
		{
			case FillKind.Solid:
				return new SolidBrush(ToColor(fill.SolidColor, wmf));

			case FillKind.LinearGradient when !wmf:
				{
					var box = path.GetBounds();

					if (box.Width <= 0f || box.Height <= 0f)
						return new SolidBrush(ToColor(fill.GradientColor1, wmf));

					return new LinearGradientBrush(
						box,
						ToColor(fill.GradientColor1, wmf),
						ToColor(fill.GradientColor2, wmf),
						fill.Angle);
				}

			case FillKind.LinearGradient:
			case FillKind.RadialGradient:
			case FillKind.RectangularGradient:
				return new SolidBrush(Blend(fill.GradientColor1, fill.GradientColor2, wmf));

			case FillKind.Pattern:
				return new SolidBrush(ToColor(fill.PatternFrontColor, wmf));

			case FillKind.Texture:
				return new SolidBrush(ToColor(fill.SolidColor, wmf));

			default:
				return null;
		}
	}

	private static Pen CreatePen(StrokeStyle stroke, bool wmf)
	{
		var pen = new Pen(ToColor(stroke.Color, wmf), MathF.Max(stroke.Thickness, 0.0001f))
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};

		return pen;
	}

	/// <summary>
	/// Цвет для метафайла.
	///
	/// В WMF полупрозрачности нет: альфа отбрасывается, а цвет смешивается
	/// с белым по своей прозрачности. Иначе полупрозрачная серая заливка
	/// пришла бы в файл насыщенно-серой, то есть темнее, чем на экране.
	/// </summary>
	private static Color ToColor(SKColor color, bool wmf)
	{
		if (!wmf || color.Alpha == 255)
			return Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

		float a = color.Alpha / 255f;

		return Color.FromArgb(
			255,
			(int)(color.Red * a + 255 * (1 - a)),
			(int)(color.Green * a + 255 * (1 - a)),
			(int)(color.Blue * a + 255 * (1 - a)));
	}

	private static Color Blend(SKColor a, SKColor b, bool wmf)
	{
		var ca = ToColor(a, wmf);
		var cb = ToColor(b, wmf);

		return Color.FromArgb(
			(ca.A + cb.A) / 2,
			(ca.R + cb.R) / 2,
			(ca.G + cb.G) / 2,
			(ca.B + cb.B) / 2);
	}

	#endregion

	#region Растровые фигуры

	/// <summary>
	/// Рисует изображение по четырём углам его рамки.
	///
	/// Через параллелограмм, а не через прямоугольник: фигура могла быть
	/// повёрнута или скошена, и прямоугольная отрисовка потеряла бы поворот.
	/// GDI+ принимает три угла — четвёртый он достраивает сам.
	/// </summary>
	private static void DrawImageShape(Graphics graphics, ImageShape image)
	{
		using var bitmap = ToGdiBitmap(image);

		if (bitmap == null)
			return;

		var box = image.GetLocalBounds();

		var corners = new[]
		{
			ToPointF(image.LocalToWorld(new SKPoint(box.Left, box.Top))),
			ToPointF(image.LocalToWorld(new SKPoint(box.Right, box.Top))),
			ToPointF(image.LocalToWorld(new SKPoint(box.Left, box.Bottom)))
		};

		graphics.DrawImage(bitmap, corners);
	}

	/// <summary>
	/// Переводит растр фигуры в растр GDI+.
	///
	/// Через кодирование в PNG и обратное чтение: у SKBitmap и Bitmap
	/// разный порядок составляющих цвета и разное обращение с умноженной
	/// альфой, и копирование памяти напрямую давало бы то синий вместо
	/// красного, то тёмную кайму по краю полупрозрачных мест.
	/// </summary>
	private static Bitmap? ToGdiBitmap(ImageShape image)
	{
		// Плоская склейка тайлов пересобирается перед чтением: рисование
		// по картинке правит тайлы, а Bitmap остаётся прежним до явной
		// пересборки. Без неё в файл ушло бы изображение до последних
		// мазков кистью.
		if (image.tileDoc != null)
			image.RebuildBitmapFromTiles();

		var source = image.Bitmap;

		if (source == null)
			return null;

		using var data = source.Encode(SKEncodedImageFormat.Png, 100);

		if (data == null)
			return null;

		using var stream = new MemoryStream(data.ToArray());

		// Копия, а не сам Bitmap из потока: Bitmap держит поток открытым
		// всё время жизни, а поток здесь временный.
		using var decoded = new Bitmap(stream);

		return new Bitmap(decoded);
	}

	#endregion

	#region Запись файла

	private static void WriteEmf(Metafile metafile, string path)
	{
		// Дескриптор у метафайла забирается один раз и потом принадлежит
		// вызывающему — уничтожать его обязаны мы.
		IntPtr handle = metafile.GetHenhmetafile();

		try
		{
			uint size = GetEnhMetaFileBits(handle, 0, null!);

			if (size == 0)
				throw new InvalidOperationException("Не удалось получить содержимое метафайла.");

			var buffer = new byte[size];

			if (GetEnhMetaFileBits(handle, size, buffer) == 0)
				throw new InvalidOperationException("Не удалось прочитать содержимое метафайла.");

			File.WriteAllBytes(path, buffer);
		}
		finally
		{
			DeleteEnhMetaFile(handle);
		}
	}

	/// <summary>
	/// Пишет WMF с размещаемым заголовком.
	///
	/// GDI+ записывает только EMF, поэтому путь двухступенчатый: сначала
	/// EMF переводится в набор записей WMF функцией GdipEmfToWmfBits,
	/// затем перед ними дописывается placeable-заголовок. Без него файл
	/// формально верен, но не содержит своих размеров, и почти всё, куда
	/// такой файл вставляют, показывает его величиной с почтовую марку
	/// либо не показывает вовсе.
	/// </summary>
	private static void WriteWmf(Metafile metafile, string path, int width, int height)
	{
		IntPtr handle = metafile.GetHenhmetafile();

		try
		{
			uint size = GdipEmfToWmfBits(handle, 0, null!, MmAnisotropic, EmfToWmfBitsFlagsDefault);

			if (size == 0)
				throw new InvalidOperationException("Не удалось перевести метафайл в WMF.");

			var bits = new byte[size];

			if (GdipEmfToWmfBits(handle, size, bits, MmAnisotropic, EmfToWmfBitsFlagsDefault) == 0)
				throw new InvalidOperationException("Не удалось перевести метафайл в WMF.");

			using var file = File.Create(path);
			using var writer = new BinaryWriter(file);

			WritePlaceableHeader(writer, width, height);
			writer.Write(bits);
		}
		finally
		{
			DeleteEnhMetaFile(handle);
		}
	}

	/// <summary>
	/// Размещаемый заголовок WMF — двадцать два байта перед записями.
	///
	/// Описывает рамку рисунка в твипах и число единиц на дюйм. Контрольная
	/// сумма — исключающее ИЛИ по всем словам заголовка, кроме неё самой;
	/// без верной суммы часть программ файл отвергает.
	/// </summary>
	private static void WritePlaceableHeader(BinaryWriter writer, int width, int height)
	{
		const uint Key = 0x9AC6CDD7;

		// Единиц на дюйм. Совпадает с разрешением, в котором писались
		// координаты, — иначе рисунок вставится в неверном масштабе.
		const short UnitsPerInch = (short)ExportDpi;

		short left = 0;
		short top = 0;
		short right = (short)Math.Min(width, short.MaxValue);
		short bottom = (short)Math.Min(height, short.MaxValue);

		var header = new short[10];

		header[0] = unchecked((short)(Key & 0xFFFF));
		header[1] = unchecked((short)(Key >> 16));
		header[2] = 0;
		header[3] = left;
		header[4] = top;
		header[5] = right;
		header[6] = bottom;
		header[7] = UnitsPerInch;
		header[8] = 0;
		header[9] = 0;

		short checksum = 0;

		for (int i = 0; i < header.Length; i++)
			checksum ^= header[i];

		foreach (short value in header)
			writer.Write(value);

		writer.Write(checksum);
	}

	#endregion

	#region Системные вызовы

	private const int MmAnisotropic = 8;

	/// <summary>
	/// Перевод без подмены записей: GDI+ по умолчанию пытается заменить
	/// команды, которых в WMF нет, набором простых. Это и нужно.
	/// </summary>
	private const int EmfToWmfBitsFlagsDefault = 0;

	[DllImport("gdiplus.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
	private static extern uint GdipEmfToWmfBits(
		IntPtr hEmf,
		uint bufferSize,
		byte[] buffer,
		int mappingMode,
		int flags);

	[DllImport("gdi32.dll")]
	private static extern uint GetEnhMetaFileBits(IntPtr hemf, uint size, byte[] buffer);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteEnhMetaFile(IntPtr hemf);

	#endregion
}
