using SkiaSharp;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

/// <summary>
/// Вставка из системного буфера обмена.
///
/// Буфер почти всегда содержит одни и те же данные в нескольких видах сразу:
/// Word при копировании фигуры кладёт Art::GVML ClipFormat, EnhancedMetafile,
/// PNG, Bitmap и ещё несколько. Поэтому порядок разбора идёт от самого
/// содержательного к запасному, и первый удавшийся выигрывает.
///
/// Растр из PNG предпочтительнее Bitmap: формат Bitmap в буфере обмена
/// теряет альфа-канал, и вставленный логотип получил бы чёрный фон.
/// </summary>
public static class ClipboardImporter
{
	/// <summary>Формат буфера обмена, которым Office передаёт фигуры.</summary>
	public const string OfficeDrawingFormat = "Art::GVML ClipFormat";

	private static readonly string[] ImageExtensions =
		{ ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };

	/// <summary>
	/// Вставляет содержимое буфера на страницу. Возвращает вставленные фигуры.
	/// </summary>
	public static List<Shape> Paste(MainView view, Page page)
	{
		var result = new List<Shape>();

		if (view == null || page == null)
			return result;

		IDataObject? data;

		try
		{
			data = Clipboard.GetDataObject();
		}
		catch
		{
			// Буфер может быть занят другим процессом — это не причина падать.
			return result;
		}

		if (data == null)
			return result;

		var origin = GetPasteOrigin(view);

		// Порядок важен: у одних и тех же данных бывает несколько представлений,
		// и векторное всегда лучше растрового.
		if (TryPasteOfficeShapes(data, page, origin, result))
			return result;

		if (TryPasteFiles(data, page, origin, result))
			return result;

		if (TryPasteImage(data, page, origin, result))
			return result;

		if (TryPasteText(data, page, origin, result))
			return result;

		return result;
	}

	/// <summary>
	/// Точка вставки — центр видимой области. Вставлять в начало страницы
	/// неудобно: при работе с крупным чертежом фигура окажется за экраном.
	/// </summary>
	private static SKPoint GetPasteOrigin(MainView view)
	{
		var viewport = view.GetViewportScreenRect();

		return view.ScreenToPage(new System.Drawing.Point(
			(int)viewport.MidX,
			(int)viewport.MidY));
	}

	#region Фигуры Office

	private static bool TryPasteOfficeShapes(IDataObject data, Page page, SKPoint origin, List<Shape> result)
	{
		if (!data.GetDataPresent(OfficeDrawingFormat))
			return false;

		try
		{
			if (data.GetData(OfficeDrawingFormat) is not MemoryStream stream)
				return false;

			var shapes = OfficeShapeImporter.Import(stream, page, origin);

			if (shapes.Count == 0)
				return false;

			foreach (var shape in shapes)
				result.Add(shape);

			return true;
		}
		catch
		{
			// Неизвестная разновидность пакета — уходим на растровый запасной путь.
			return false;
		}
	}

	#endregion

	#region Файлы

	private static bool TryPasteFiles(IDataObject data, Page page, SKPoint origin, List<Shape> result)
	{
		if (!data.GetDataPresent(DataFormats.FileDrop))
			return false;

		if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
			return false;

		float offset = 0f;
		bool any = false;

		foreach (var file in files)
		{
			if (!IsImageFile(file) || !File.Exists(file))
				continue;

			SKBitmap? bitmap = null;

			try
			{
				bitmap = SKBitmap.Decode(file);
			}
			catch
			{
			}

			if (bitmap == null)
				continue;

			// Каскад: несколько файлов не должны лечь друг на друга.
			var shape = CreateImageShape(
				bitmap,
				page,
				new SKPoint(origin.X + offset, origin.Y + offset),
				file);

			bitmap.Dispose();

			if (shape == null)
				continue;

			result.Add(shape);
			offset += 5f;
			any = true;
		}

		return any;
	}

	private static bool IsImageFile(string path)
	{
		string ext = Path.GetExtension(path);

		foreach (var e in ImageExtensions)
		{
			if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	#endregion

	#region Растр

	private static bool TryPasteImage(IDataObject data, Page page, SKPoint origin, List<Shape> result)
	{
		var bitmap = ReadBitmap(data);

		if (bitmap == null)
			return false;

		try
		{
			var shape = CreateImageShape(bitmap, page, origin, string.Empty);

			if (shape == null)
				return false;

			result.Add(shape);
			return true;
		}
		finally
		{
			bitmap.Dispose();
		}
	}

	/// <summary>
	/// Читает растр, предпочитая форматы с прозрачностью.
	///
	/// Формат Bitmap в буфере обмена альфа-канал теряет: вставленный из
	/// браузера логотип с прозрачным фоном получил бы чёрный прямоугольник.
	/// PNG и Format17 (DIBv5) прозрачность сохраняют, поэтому проверяются
	/// первыми.
	/// </summary>
	private static SKBitmap? ReadBitmap(IDataObject data)
	{
		foreach (var format in new[] { "PNG", "image/png", "Format17" })
		{
			if (!data.GetDataPresent(format))
				continue;

			try
			{
				if (data.GetData(format) is MemoryStream stream)
				{
					stream.Position = 0;

					var decoded = SKBitmap.Decode(stream);

					if (decoded != null)
						return decoded;
				}
			}
			catch
			{
			}
		}

		try
		{
			if (data.GetDataPresent(DataFormats.Bitmap) &&
				data.GetData(DataFormats.Bitmap) is System.Drawing.Image image)
			{
				return FromGdiImage(image);
			}
		}
		catch
		{
		}

		return null;
	}

	private static SKBitmap? FromGdiImage(System.Drawing.Image image)
	{
		using var memory = new MemoryStream();

		// Пересохранение в PNG — самый короткий надёжный путь между
		// System.Drawing и Skia: побитовое копирование потребовало бы
		// разбора всех вариантов PixelFormat.
		image.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
		memory.Position = 0;

		return SKBitmap.Decode(memory);
	}

	/// <summary>
	/// Создаёт изображение с размером по фактическому разрешению растра.
	/// </summary>
	private static ImageShape? CreateImageShape(SKBitmap bitmap, Page page, SKPoint center, string path)
	{
		if (bitmap.Width <= 0 || bitmap.Height <= 0)
			return null;

		// Пиксели переводятся в единицы страницы через её разрешение:
		// снимок 1920 точек при 96 DPI даёт 508 мм, а не 1920.
		float pxPerUom = MathF.Max(page.PxPerUom, 0.0001f);

		float width = bitmap.Width / pxPerUom;
		float height = bitmap.Height / pxPerUom;

		// Слишком крупная картинка вписывается в страницу, иначе она
		// вылезет далеко за лист и её придётся искать.
		float maxWidth = page.size_uom.Width * 0.9f;
		float maxHeight = page.size_uom.Height * 0.9f;

		float scale = MathF.Min(1f, MathF.Min(maxWidth / width, maxHeight / height));

		width *= scale;
		height *= scale;

		var rect = new SKRect(-width * 0.5f, -height * 0.5f, width * 0.5f, height * 0.5f);

		var shape = new ImageShape
		{
			Name = string.IsNullOrEmpty(path) ? "Image" : Path.GetFileNameWithoutExtension(path),
			ImagePath = path,
			ImageLocalRect = rect,
			LocalTransform = SKMatrix.CreateTranslation(center.X, center.Y)
		};

		AddRectNodes(shape, rect);

		shape.SetOriginalBitmap(bitmap);

		return shape;
	}

	internal static void AddRectNodes(CurveShape shape, SKRect r)
	{
		var corners = new[]
		{
			new SKPoint(r.Left, r.Top),
			new SKPoint(r.Right, r.Top),
			new SKPoint(r.Right, r.Bottom),
			new SKPoint(r.Left, r.Bottom)
		};

		for (int i = 0; i < 4; i++)
		{
			shape.Nodes.Add(new CurveNode
			{
				Position = corners[i],
				Type = CurveNodeType.Straight,
				NewContour = i == 0,
				CloseAfter = i == 3
			});
		}
	}

	#endregion

	#region Текст

	private static bool TryPasteText(IDataObject data, Page page, SKPoint origin, List<Shape> result)
	{
		// RTF содержит шрифт, кегль, цвет и начертание. Простой текст —
		// только символы, поэтому сначала пробуем его.
		if (data.GetDataPresent(DataFormats.Rtf) &&
			data.GetData(DataFormats.Rtf) is string rtf &&
			!string.IsNullOrWhiteSpace(rtf))
		{
			var shape = CreateTextFromRtf(rtf, page, origin);

			if (shape != null)
			{
				result.Add(shape);
				return true;
			}
		}

		string? plain = null;

		if (data.GetDataPresent(DataFormats.UnicodeText))
			plain = data.GetData(DataFormats.UnicodeText) as string;
		else if (data.GetDataPresent(DataFormats.Text))
			plain = data.GetData(DataFormats.Text) as string;

		if (string.IsNullOrWhiteSpace(plain))
			return false;

		result.Add(CreateTextShape(plain, page, origin, null));
		return true;
	}

	/// <summary>
	/// Разбирает RTF через скрытый RichTextBox.
	///
	/// Свой разборщик RTF писать не нужно: RichTextBox уже умеет читать
	/// таблицы шрифтов и цветов, а нам достаточно спросить у него атрибуты
	/// первого символа. Это надёжнее ручного разбора управляющих слов,
	/// которые Word расставляет далеко не так предсказуемо, как хотелось бы.
	/// </summary>
	private static TextShape? CreateTextFromRtf(string rtf, Page page, SKPoint origin)
	{
		try
		{
			using var box = new RichTextBox();

			box.Rtf = rtf;

			string text = box.Text;

			if (string.IsNullOrWhiteSpace(text))
				return null;

			// Атрибуты берутся у первого символа: вставка сохраняет один
			// стиль на всю фигуру, разноформатный текст в одной TextShape
			// не поддерживается.
			box.SelectionStart = 0;
			box.SelectionLength = Math.Min(1, box.TextLength);

			var font = box.SelectionFont;
			var color = box.SelectionColor;

			var format = new TextFormatInfo
			{
				FontFamily = font?.Name,

				// Кегль в RTF задан в пунктах, у нас в миллиметрах.
				FontSizeMm = font != null ? font.SizeInPoints * 25.4f / 72f : null,

				Bold = font?.Bold,
				Italic = font?.Italic,
				Underline = font?.Underline,
				Strikethrough = font?.Strikeout,

				Color = color.A == 0
					? null
					: new SKColor(color.R, color.G, color.B, color.A)
			};

			return CreateTextShape(text, page, origin, format);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Атрибуты, вычитанные из форматированного текста.</summary>
	private sealed class TextFormatInfo
	{
		public string? FontFamily;
		public float? FontSizeMm;
		public bool? Bold;
		public bool? Italic;
		public bool? Underline;
		public bool? Strikethrough;
		public SKColor? Color;
	}

	private static TextShape CreateTextShape(string text, Page page, SKPoint origin, TextFormatInfo? format)
	{
		var shape = new TextShape
		{
			Text = text.Replace("\r\n", "\n").Replace("\r", "\n"),
			LocalTransform = SKMatrix.CreateTranslation(origin.X, origin.Y)
		};

		if (format == null)
			return shape;

		// Шрифт применяется только если он есть в системе: иначе Skia молча
		// подставит свой, и запись в свойствах фигуры будет врать.
		if (!string.IsNullOrWhiteSpace(format.FontFamily) &&
			SystemFontCatalog.GetTypeface(format.FontFamily) != null)
		{
			shape.FontFamily = format.FontFamily;
		}

		if (format.FontSizeMm is > 0.1f and < 1000f)
			shape.FontSizeMm = format.FontSizeMm.Value;

		if (format.Bold.HasValue) shape.Bold = format.Bold.Value;
		if (format.Italic.HasValue) shape.Italic = format.Italic.Value;
		if (format.Underline.HasValue) shape.Underline = format.Underline.Value;
		if (format.Strikethrough.HasValue) shape.Strikethrough = format.Strikethrough.Value;

		if (format.Color.HasValue)
		{
			shape.Fill.Enabled = true;
			shape.Fill.Kind = FillKind.Solid;
			shape.Fill.SolidColor = format.Color.Value;
		}

		return shape;
	}

	#endregion
}

/// <summary>
/// Копирование выделения в системный буфер обмена.
///
/// В буфер кладётся несколько представлений одних и тех же данных: каждая
/// программа берёт то, что понимает. Word и Paint возьмут Bitmap, редакторы
/// с поддержкой прозрачности — PNG, текстовые поля — простой текст.
///
/// Порядок добавления в DataObject значения не имеет: принимающая сторона
/// сама выбирает формат по своему списку предпочтений.
/// </summary>
public static class ClipboardExporter
{
	/// <summary>
	/// Разрешение растра для буфера.
	///
	/// Выше разрешения страницы намеренно: вставленная в Word картинка
	/// печатается, и 96 точек на дюйм на бумаге выглядят рвано. При 300
	/// изображение остаётся приемлемым и в печати.
	/// </summary>
	private const int ClipboardDpi = 300;

	/// <summary>Предел стороны растра, чтобы крупное выделение не съело память.</summary>
	private const int MaxSide = 8000;

	/// <summary>
	/// Кладёт выделенные фигуры в буфер обмена.
	///
	/// Ошибки не проглатываются: возвращается описание неудачи, иначе отладка
	/// сводится к угадыванию — ровно то, с чем мы столкнулись.
	/// </summary>
	public static bool CopyShapes(Page page, IReadOnlyList<Shape> shapes, out string error)
	{
		error = string.Empty;

		if (page == null || shapes == null || shapes.Count == 0)
		{
			error = "Нечего копировать: пустое выделение.";
			return false;
		}

		SKBitmap? transparent = null;
		SKBitmap? opaque = null;

		try
		{
			transparent = ShapeExporter.RenderToBitmap(
				page, shapes, ClipboardDpi, SKColors.Transparent, MaxSide);

			opaque = ShapeExporter.RenderToBitmap(
				page, shapes, ClipboardDpi, SKColors.White, MaxSide);
		}
		catch (Exception ex)
		{
			error = $"Отрисовка не удалась: {ex.GetType().Name}: {ex.Message}";
			transparent?.Dispose();
			opaque?.Dispose();
			return false;
		}

		if (transparent == null && opaque == null)
		{
			error = "RenderToBitmap вернул null для обоих вариантов — "
				  + "проверьте PrepareShapes и GetExportBounds.";
			return false;
		}

		var data = new DataObject();
		int formats = 0;

		try
		{
			if (transparent != null)
			{
				var png = EncodePng(transparent);

				if (png != null)
				{
					data.SetData("PNG", false, png);
					formats++;
				}
			}

			if (opaque != null)
			{
				var gdi = ToGdiBitmap(opaque, ClipboardDpi);

				if (gdi != null)
				{
					// Владение переходит буферу: освобождать нельзя.
					data.SetData(DataFormats.Bitmap, true, gdi);
					formats++;
				}
			}

			string? text = ExtractText(shapes);

			if (!string.IsNullOrEmpty(text))
			{
				data.SetData(DataFormats.UnicodeText, true, text);
				formats++;
			}
		}
		finally
		{
			transparent?.Dispose();
			opaque?.Dispose();
		}

		if (formats == 0)
		{
			error = "Ни один формат не удалось подготовить.";
			return false;
		}

		// Сначала без флага копирования. Он вызывает OleFlushClipboard, который
		// требует немедленной отрисовки ВСЕХ форматов, и отказ одного из них
		// отменяет всю операцию — буфер тогда остаётся с прежним содержимым,
		// что и наблюдалось.
		try
		{
			Clipboard.SetDataObject(data, false);
		}
		catch (Exception ex)
		{
			error = $"SetDataObject: {ex.GetType().Name}: {ex.Message}";
			return false;
		}

		// Отдельной попыткой закрепляем содержимое, чтобы оно пережило закрытие
		// приложения. Неудача здесь уже не страшна: в буфере лежит нужное.
		try
		{
			Clipboard.SetDataObject(data, true);
		}
		catch
		{
		}

		return true;
	}

	private static MemoryStream? EncodePng(SKBitmap bitmap)
	{
		try
		{
			using var image = SKImage.FromBitmap(bitmap);
			using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

			if (encoded == null)
				return null;

			var stream = new MemoryStream();
			encoded.SaveTo(stream);
			stream.Position = 0;

			return stream;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Переносит растр в System.Drawing.Bitmap и проставляет разрешение.
	///
	/// Разрешение важно: Word вычисляет физический размер вставленной картинки
	/// по нему. Без вызова SetResolution он примет умолчание в 96 точек,
	/// и фигура на листе окажется втрое крупнее исходной.
	/// </summary>
	private static System.Drawing.Bitmap? ToGdiBitmap(SKBitmap bitmap, int dpi)
	{
		try
		{
			using var stream = EncodePng(bitmap);

			if (stream == null)
				return null;

			// Промежуточный PNG короче побитового переноса: тот потребовал бы
			// разбора всех вариантов PixelFormat и учёта выравнивания строк.
			var gdi = new System.Drawing.Bitmap(stream);

			gdi.SetResolution(dpi, dpi);

			return gdi;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Текст выделенных текстовых фигур. Если текста нет — null.
	/// </summary>
	private static string? ExtractText(IReadOnlyList<Shape> shapes)
	{
		var parts = new List<string>();

		foreach (var shape in shapes)
		{
			if (shape is TextShape text && !string.IsNullOrWhiteSpace(text.Text))
				parts.Add(text.Text);
		}

		return parts.Count == 0 ? null : string.Join("\r\n", parts);
	}
}
