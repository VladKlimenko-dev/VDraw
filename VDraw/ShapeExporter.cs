using SkiaSharp;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

public enum ShapeExportFormat
{
	Png,
	Jpeg,
	Bmp,
	Svg,

	/// <summary>Анимированный GIF. Отличается от прочих тем, что кадров много.</summary>
	Gif,

	/// <summary>
	/// Метафайл Windows. Векторный, но формата 1990 года: без прозрачности,
	/// без градиентов, с шестнадцатибитными координатами. Нужен там,
	/// куда SVG не принимают, — в старых чертёжных и станочных программах.
	/// </summary>
	Wmf,

	/// <summary>
	/// Расширенный метафайл. Тот же путь записи, что и WMF, но без его
	/// ограничений. Если принимающая сторона умеет EMF, брать надо его.
	/// </summary>
	Emf
}

/// <summary>
/// Экспорт выделенных фигур в растр и в SVG.
///
/// Фигуры рисуются тем же кодом, что и на холсте — Shape.Render(canvas, mmPerWorldUnit),
/// поэтому вид в файле совпадает с видом в редакторе, включая заливки, паттерны,
/// коррекции изображений и глифы.
///
/// Важное ограничение платформы: Skia кодирует PNG, JPEG и WEBP, но не BMP —
/// формат BMP у неё только на чтение. Поэтому BMP пишется через System.Drawing,
/// который в проекте уже есть как часть WinForms.
/// </summary>
public static class ShapeExporter
{
	/// <summary>Предел стороны растра. Защита от попытки выделить гигабайты.</summary>
	private const int MaxRasterSide = 20000;

	/// <summary>Предел общего числа пикселей — 200 мегапикселей.</summary>
	private const long MaxRasterPixels = 200L * 1000 * 1000;

	/// <summary>Единиц на дюйм для viewBox в SVG. 96 — стандартный CSS-пиксель.</summary>
	private const float SvgDpi = 96f;

	#region Точка входа

	/// <summary>
	/// Показывает диалог сохранения и экспортирует выделенные фигуры.
	/// Возвращает false, если пользователь отменил или экспортировать нечего.
	/// </summary>
	public static bool ExportWithDialog(IWin32Window? owner, Page page, IReadOnlyList<Shape> selection)
	{
		var shapes = PrepareShapes(selection);

		if (page == null || shapes.Count == 0)
		{
			MessageBox.Show(
				owner as IWin32Window is Form f ? f : null,
				"Select at least one shape to export.",
				"Export",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);

			return false;
		}

		using var sfd = new SaveFileDialog
		{
			Title = "Export selection",
			Filter =
				"PNG image (*.png)|*.png|" +
				"JPEG image (*.jpg)|*.jpg;*.jpeg|" +
				"Bitmap (*.bmp)|*.bmp|" +
				"SVG vector (*.svg)|*.svg|" +
				"Animated GIF (*.gif)|*.gif|" +
				"Enhanced metafile (*.emf)|*.emf|" +
				"Windows metafile (*.wmf)|*.wmf",
			FilterIndex = 1,
			AddExtension = true,
			DefaultExt = "png",
			OverwritePrompt = true,
			FileName = BuildSuggestedName(page, shapes)
		};

		if (sfd.ShowDialog(owner) != DialogResult.OK)
			return false;

		var format = DetectFormat(sfd.FileName, sfd.FilterIndex);

		try
		{
			if (format == ShapeExportFormat.Svg)
				return ExportSvg(sfd.FileName, page, shapes);

			// Метафайлы идут мимо диалога параметров: он про размер
			// в пикселях, а у вектора его нет.
			if (format == ShapeExportFormat.Wmf || format == ShapeExportFormat.Emf)
			{
				return MetafileExporter.Export(
					sfd.FileName,
					page,
					shapes,
					wmf: format == ShapeExportFormat.Wmf);
			}

			// У анимации свой диалог: там не только размер, но и диапазон
			// кадров с перемоткой, и втискивать это в общую форму значило бы
			// показывать половину полей неактивными всем остальным.
			if (format == ShapeExportFormat.Gif)
				return ExportGifWithDialog(owner, sfd.FileName, page, shapes);

			// Размер по умолчанию — тот, что дало бы разрешение документа.
			// Пользователь его правит, но начинать с осмысленного значения
			// важнее, чем с круглого.
			if (!TryGetDefaultPixelSize(page, shapes, page.DPI, out int defW, out int defH))
				return false;

			using var settingsForm = new RasterExportSettingsForm(page, shapes, format, defW, defH);

			if (settingsForm.ShowDialog(owner) != DialogResult.OK)
				return false;

			var settings = settingsForm.Settings;

			return ExportRasterExact(
				sfd.FileName,
				format,
				page,
				shapes,
				settings.PixelWidth,
				settings.PixelHeight,
				settings.EffectiveBackground,
				settings.Padding);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to export selection.\r\n\r\n{ex.Message}",
				"Export error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return false;
		}
	}

	/// <summary>
	/// Показывает диалог анимации и пишет GIF.
	/// </summary>
	private static bool ExportGifWithDialog(
		IWin32Window? owner, string path, Page page, IReadOnlyList<Shape> shapes)
	{
		using var form = new GifExportForm(page, shapes);

		if (form.ShowDialog(owner) != DialogResult.OK)
			return false;

		return AnimationFrameRenderer.ExportGif(
			path,
			page,
			shapes,
			form.FirstFrame,
			form.LastFrame,
			form.PixelWidth,
			form.PixelHeight,
			form.Background,
			form.Transparent,
			form.Fps,
			form.ExportBounds);
	}

	/// <summary>
	/// Размер растра, который дало бы указанное разрешение.
	///
	/// Тот же расчёт, что и в RenderToBitmap, вынесенный отдельно: диалогу
	/// параметров нужны числа ДО отрисовки, чтобы показать их в полях.
	/// </summary>
	public static bool TryGetDefaultPixelSize(
		Page page, IReadOnlyList<Shape> shapes, int dpi, out int width, out int height)
	{
		width = 0;
		height = 0;

		if (page == null || shapes == null || shapes.Count == 0)
			return false;

		var bounds = GetExportBounds(page, shapes);

		if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
			return false;

		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);
		float pxPerWorldUnit = MathF.Max(dpi, 1) / 25.4f * mmPerWorldUnit;

		width = Math.Clamp((int)MathF.Ceiling(bounds.Width * pxPerWorldUnit), 1, MaxRasterSide);
		height = Math.Clamp((int)MathF.Ceiling(bounds.Height * pxPerWorldUnit), 1, MaxRasterSide);

		return true;
	}

	/// <summary>
	/// Формат определяется по расширению, а не по индексу фильтра: пользователь
	/// может ввести имя с расширением вручную, не меняя выпадающий список.
	/// </summary>
	private static ShapeExportFormat DetectFormat(string path, int filterIndex)
	{
		string ext = Path.GetExtension(path).ToLowerInvariant();

		return ext switch
		{
			".png" => ShapeExportFormat.Png,
			".jpg" => ShapeExportFormat.Jpeg,
			".jpeg" => ShapeExportFormat.Jpeg,
			".bmp" => ShapeExportFormat.Bmp,
			".svg" => ShapeExportFormat.Svg,
			".gif" => ShapeExportFormat.Gif,
			".emf" => ShapeExportFormat.Emf,
			".wmf" => ShapeExportFormat.Wmf,

			// Расширения нет или оно незнакомое — идём по выбранному фильтру.
			_ => filterIndex switch
			{
				2 => ShapeExportFormat.Jpeg,
				3 => ShapeExportFormat.Bmp,
				4 => ShapeExportFormat.Svg,
				5 => ShapeExportFormat.Gif,
				6 => ShapeExportFormat.Emf,
				7 => ShapeExportFormat.Wmf,
				_ => ShapeExportFormat.Png
			}
		};
	}

	private static string BuildSuggestedName(Page page, List<Shape> shapes)
	{
		string baseName = shapes.Count == 1
			? shapes[0].Name
			: page.title;

		if (string.IsNullOrWhiteSpace(baseName))
			baseName = "export";

		foreach (char c in Path.GetInvalidFileNameChars())
			baseName = baseName.Replace(c, '_');

		return baseName.Trim();
	}

	#endregion

	#region Подготовка

	/// <summary>
	/// Готовит список фигур к отрисовке: разворачивает группы, конвертирует
	/// текст в кривые, отбрасывает невидимое и направляющие, сортирует по слою.
	/// </summary>
	private static List<Shape> PrepareShapes(IReadOnlyList<Shape>? selection)
	{
		var flat = new List<Shape>();

		if (selection != null)
		{
			foreach (var shape in selection)
				CollectLeaves(shape, flat);
		}

		// Текст заменяется на кривые. Если конвертация не удалась (пустая строка,
		// шрифт без глифов), фигура остаётся как есть — хуже, чем кривые, но
		// лучше, чем пропасть из экспорта.
		var result = new List<Shape>(flat.Count);

		foreach (var shape in flat)
		{
			if (shape is TextShape text)
			{
				var curve = ConvertTextToCurves(text);
				result.Add(curve ?? shape);
				continue;
			}

			result.Add(shape);
		}

		result.Sort((a, b) => a.Layer.CompareTo(b.Layer));
		return result;
	}

	/// <summary>
	/// Разворачивает группы в отдельные фигуры.
	///
	/// Это допустимо потому, что GroupShape.Draw просто перебирает детей и
	/// вызывает child.Draw — ни обрезки, ни групповой прозрачности там нет,
	/// поэтому нарисовать листья по отдельности даст тот же результат.
	/// Заодно это единственный способ добраться до текста внутри группы:
	/// подменить ребёнка в самой группе нельзя, AddChild вызывает
	/// ReparentPreserveWorld и изменил бы трансформацию реальной фигуры.
	/// </summary>
	private static void CollectLeaves(Shape? shape, List<Shape> target)
	{
		if (shape == null || !shape.IsVisible)
			return;

		if (shape is GuideLine)
			return;

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				CollectLeaves(child, target);

			return;
		}

		if (!target.Contains(shape))
			target.Add(shape);
	}

	/// <summary>
	/// Превращает текст в кривые. Возвращает null, если превращать нечего.
	///
	/// Исходная фигура не изменяется: ToCurveShape строит новый объект,
	/// а экспорт работает с копией списка.
	/// </summary>
	private static CurveShape? ConvertTextToCurves(TextShape text)
	{
		var curve = text.ToCurveShape();

		if (curve == null || curve.Nodes.Count == 0)
			return null;

		// Контуры глифов рассчитаны на правило Winding: у буквы с дыркой
		// внешний контур идёт в одну сторону, внутренний в другую. У жирных и
		// рукописных начертаний контуры внутри одного глифа перекрываются, и
		// EvenOdd выел бы дырки там, где их быть не должно.
		//
		// CurveShape.FillType по умолчанию EvenOdd, а ToCurveShape его не
		// задаёт — поэтому выставляем явно.
		curve.FillType = SKPathFillType.Winding;

		// Текст рисуется заливкой глифов; если у исходной фигуры заливка
		// выключена, кривые тоже останутся только с обводкой — как и было.
		return curve;
	}


	/// <summary>
	/// Границы экспорта в мировых единицах.
	///
	/// Shape.GetBounds возвращает границы ГЕОМЕТРИИ, без обводки. Обводка рисуется
	/// по центру контура, поэтому наружу выходит на половину толщины, а глифы
	/// начала и конца — ещё дальше. Без запаса края обрезались бы.
	/// </summary>
	public static SKRect GetExportBounds(Page page, IReadOnlyList<Shape> shapes)
	{
		SKRect bounds = SKRect.Empty;
		float maxOverhang = 0f;

		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);

		foreach (var shape in shapes)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);

			if (shape.Stroke == null || !shape.Stroke.Enabled)
				continue;

			float halfStrokeWorld = shape.Stroke.Thickness * 0.5f;

			// Глиф выступает за конец контура на свой размер.
			float glyphWorld = 0f;

			if (!string.IsNullOrEmpty(shape.Stroke.StartGlyph))
				glyphWorld = MathF.Max(glyphWorld, shape.Stroke.GetStartGlyphSizeWorld(mmPerWorldUnit));

			if (!string.IsNullOrEmpty(shape.Stroke.EndGlyph))
				glyphWorld = MathF.Max(glyphWorld, shape.Stroke.GetEndGlyphSizeWorld(mmPerWorldUnit));

			maxOverhang = MathF.Max(maxOverhang, halfStrokeWorld + glyphWorld);
		}

		if (bounds.IsEmpty)
			return SKRect.Empty;

		// Плюс один процент на скругления соединений и сглаживание.
		float padding = maxOverhang + MathF.Max(bounds.Width, bounds.Height) * 0.01f;

		return Geometry.Inflate(bounds, padding, padding);
	}

	/// <summary>Рисует фигуры в канву, уже настроенную на мировые координаты.</summary>
	private static void DrawShapes(SKCanvas canvas, Page page, IReadOnlyList<Shape> shapes)
	{
		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);

		foreach (var shape in shapes)
		{
			// Выделение не должно влиять на экспорт: обводка выделенной фигуры
			// рисуется толще, и в файл попала бы утолщённая линия.
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

	#region Растр
	public static SKBitmap? RenderToBitmap(
		Page page,
		IReadOnlyList<Shape> selection,
		int dpi,
		SKColor background,
		int maxSide)
	{
		var shapes = PrepareShapes(selection);

		if (page == null || shapes.Count == 0)
			return null;

		var bounds = GetExportBounds(page, shapes);

		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return null;

		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);
		float pxPerWorldUnit = MathF.Max(dpi, 1) / 25.4f * mmPerWorldUnit;

		int width = (int)MathF.Ceiling(bounds.Width * pxPerWorldUnit);
		int height = (int)MathF.Ceiling(bounds.Height * pxPerWorldUnit);

		// Крупное выделение при высоком разрешении легко даёт растр
		// на сотни мегабайт. Вписываем в предел, сохраняя пропорции.
		if (width > maxSide || height > maxSide)
		{
			float scale = MathF.Min((float)maxSide / width, (float)maxSide / height);

			width = Math.Max(1, (int)(width * scale));
			height = Math.Max(1, (int)(height * scale));

			pxPerWorldUnit *= scale;
		}

		width = Math.Clamp(width, 1, maxSide);
		height = Math.Clamp(height, 1, maxSide);

		bool opaque = background.Alpha == 255;

		var info = new SKImageInfo(
			width,
			height,
			SKColorType.Bgra8888,
			opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);

		var surface = SKSurface.Create(info);

		if (surface == null)
			return null;

		using (surface)
		{
			var canvas = surface.Canvas;
			canvas.Clear(background);

			// Порядок важен: точка мира отображается как
			// (p - bounds.TopLeft) * scale, а канва применяет преобразования
			// в обратном порядке объявления.
			canvas.Scale(pxPerWorldUnit);
			canvas.Translate(-bounds.Left, -bounds.Top);

			DrawShapes(canvas, page, shapes);

			canvas.Flush();

			using var image = surface.Snapshot();

			return SKBitmap.FromImage(image);
		}
	}

	/// <summary>
	/// Отрисовка в ТОЧНО заданный размер в пикселях.
	///
	/// Отличается от RenderToBitmap тем, что размер задаёт пользователь,
	/// а не разрешение. Если запрошенные пропорции расходятся с исходными,
	/// изображение растягивается — это осознанный выбор пользователя,
	/// снявшего галочку связки сторон, и подправлять его молча нельзя.
	/// </summary>
	public static SKBitmap? RenderToBitmapExact(
		Page page,
		IReadOnlyList<Shape> selection,
		int pixelWidth,
		int pixelHeight,
		SKColor background,
		SKRectI padding = default)
	{
		var shapes = PrepareShapes(selection);

		if (page == null || shapes.Count == 0)
			return null;

		return RenderToBitmapWithBounds(
			page, shapes, GetExportBounds(page, shapes), pixelWidth, pixelHeight, background, padding);
	}

	/// <summary>
	/// То же, но с габаритами, заданными извне.
	///
	/// Нужно анимации: там границы считаются один раз по объединению всех
	/// кадров, иначе содержимое дёргалось бы в кадре вслед за меняющимися
	/// границами сцены.
	/// </summary>
	public static SKBitmap? RenderToBitmapWithBounds(
		Page page,
		IReadOnlyList<Shape> selection,
		SKRect bounds,
		int pixelWidth,
		int pixelHeight,
		SKColor background,
		SKRectI padding = default)
	{
		var shapes = PrepareShapes(selection);

		if (page == null || shapes.Count == 0)
			return null;

		if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
			return null;

		int width = Math.Clamp(pixelWidth, 1, MaxRasterSide);
		int height = Math.Clamp(pixelHeight, 1, MaxRasterSide);

		// Предел по площади проверяется отдельно от предела по стороне:
		// растр 20000 на 20000 укладывается в оба ограничения по сторонам,
		// но это четыреста мегапикселей и полтора гигабайта памяти.
		if ((long)width * height > MaxRasterPixels)
		{
			double k = Math.Sqrt(MaxRasterPixels / (double)((long)width * height));

			width = Math.Max(1, (int)(width * k));
			height = Math.Max(1, (int)(height * k));
		}

		// Отступы отсчитываются ВНУТРЬ: общий размер файла остаётся тем,
		// что задал пользователь, а содержимое ужимается. Обратный вариант —
		// добавлять поля снаружи — менял бы размер файла вслед за отступами,
		// и два поля в диалоге спорили бы друг с другом.
		int padLeft = Math.Max(0, padding.Left);
		int padTop = Math.Max(0, padding.Top);
		int padRight = Math.Max(0, padding.Right);
		int padBottom = Math.Max(0, padding.Bottom);

		// Отступы шире самой картинки оставили бы нулевую область
		// содержимого. Ужимаем их пропорционально, а не обрезаем один:
		// так сохраняется заданное соотношение полей.
		int innerWidth = width - padLeft - padRight;
		int innerHeight = height - padTop - padBottom;

		if (innerWidth < 1)
		{
			float k = (width - 1) / MathF.Max(1f, padLeft + padRight);
			padLeft = (int)(padLeft * k);
			padRight = (int)(padRight * k);
			innerWidth = Math.Max(1, width - padLeft - padRight);
		}

		if (innerHeight < 1)
		{
			float k = (height - 1) / MathF.Max(1f, padTop + padBottom);
			padTop = (int)(padTop * k);
			padBottom = (int)(padBottom * k);
			innerHeight = Math.Max(1, height - padTop - padBottom);
		}

		bool opaque = background.Alpha == 255;

		var info = new SKImageInfo(
			width,
			height,
			SKColorType.Bgra8888,
			opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);

		var surface = SKSurface.Create(info);

		if (surface == null)
			return null;

		using (surface)
		{
			var canvas = surface.Canvas;
			canvas.Clear(background);

			// Порядок обратный объявлению: сначала перенос содержимого
			// в начало координат, затем масштаб, затем сдвиг на отступ.
			canvas.Translate(padLeft, padTop);

			// Множители по осям считаются независимо: при снятой связке
			// сторон они и должны различаться.
			canvas.Scale(innerWidth / bounds.Width, innerHeight / bounds.Height);
			canvas.Translate(-bounds.Left, -bounds.Top);

			DrawShapes(canvas, page, shapes);

			canvas.Flush();

			using var image = surface.Snapshot();

			return SKBitmap.FromImage(image);
		}
	}

	/// <summary>
	/// Экспорт растра в заданный размер. Отличается от ExportRaster только
	/// способом задать размер — пикселями вместо разрешения.
	/// </summary>
	public static bool ExportRasterExact(
		string path,
		ShapeExportFormat format,
		Page page,
		IReadOnlyList<Shape> shapes,
		int pixelWidth,
		int pixelHeight,
		SKColor background,
		SKRectI padding = default,
		int jpegQuality = 92)
	{
		if (format == ShapeExportFormat.Svg)
			throw new ArgumentException("Use ExportSvg for vector output.", nameof(format));

		// Альфу хранит только PNG. Для остальных форматов прозрачность
		// заменяется белым, иначе фон вышел бы чёрным: незаданные пиксели
		// при отбрасывании альфы становятся нулями.
		var bg = format == ShapeExportFormat.Png
			? background
			: (background.Alpha == 255 ? background : SKColors.White);

		using var bitmap = RenderToBitmapExact(page, shapes, pixelWidth, pixelHeight, bg, padding);

		if (bitmap == null)
			return false;

		return EncodeAndSave(bitmap, path, format, bg, jpegQuality);
	}

	/// <summary>
	/// Экспорт в PNG, JPEG или BMP.
	/// PNG сохраняет прозрачность, JPEG и BMP её не поддерживают и получают
	/// подложку указанного цвета.
	/// </summary>
	public static bool ExportRaster(
		string path,
		ShapeExportFormat format,
		Page page,
		IReadOnlyList<Shape> shapes,
		int dpi,
		SKColor? background = null,
		int jpegQuality = 92)
	{
		if (format == ShapeExportFormat.Svg)
			throw new ArgumentException("Use ExportSvg for vector output.", nameof(format));

		bool supportsAlpha = format == ShapeExportFormat.Png;

		var bg = background
			?? (supportsAlpha ? SKColors.Transparent : SKColors.White);

		using var bitmap = RenderToBitmap(page, shapes, dpi, bg, MaxRasterSide);

		if (bitmap == null)
			return false;

		return EncodeAndSave(bitmap, path, format, bg, jpegQuality);
	}

	/// <summary>Запись готового растра в файл выбранного формата.</summary>
	private static bool EncodeAndSave(
		SKBitmap bitmap, string path, ShapeExportFormat format, SKColor background, int jpegQuality)
	{
		if (format == ShapeExportFormat.Bmp)
		{
			// Skia кодирует PNG, JPEG и WEBP; BMP у неё только на чтение.
			SaveBmp(bitmap, path, background);
			return true;
		}

		var skFormat = format == ShapeExportFormat.Jpeg
			? SKEncodedImageFormat.Jpeg
			: SKEncodedImageFormat.Png;

		int quality = format == ShapeExportFormat.Jpeg
			? Math.Clamp(jpegQuality, 1, 100)
			: 100;

		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(skFormat, quality);

		if (data == null)
			throw new InvalidOperationException($"Failed to encode {format}.");

		using var stream = File.Create(path);
		data.SaveTo(stream);

		return true;
	}


	/// <summary>
	/// Записывает BMP через System.Drawing. Результат 24-битный: 32-битный BMP
	/// с альфа-каналом читают далеко не все программы.
	///
	/// Через System.Drawing, а не через Skia, потому что Skia кодирует PNG,
	/// JPEG и WEBP — BMP у неё только на чтение.
	/// </summary>
	private static void SaveBmp(SKBitmap skBitmap, string path, SKColor background)
	{
		if (skBitmap == null)
			throw new InvalidOperationException("Failed to read pixels for BMP export.");

		int w = skBitmap.Width;
		int h = skBitmap.Height;

		using var argb = new System.Drawing.Bitmap(w, h, PixelFormat.Format32bppArgb);

		var rect = new System.Drawing.Rectangle(0, 0, w, h);
		var locked = argb.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

		try
		{
			// SKColorType.Bgra8888 и PixelFormat.Format32bppArgb совпадают
			// по раскладке байт на little-endian: B, G, R, A.
			IntPtr src = skBitmap.GetPixels();
			int srcStride = skBitmap.RowBytes;
			int dstStride = locked.Stride;
			int rowBytes = w * 4;

			var row = new byte[rowBytes];

			for (int y = 0; y < h; y++)
			{
				Marshal.Copy(src + y * srcStride, row, 0, rowBytes);
				Marshal.Copy(row, 0, locked.Scan0 + y * dstStride, rowBytes);
			}
		}
		finally
		{
			argb.UnlockBits(locked);
		}

		using var rgb = new System.Drawing.Bitmap(w, h, PixelFormat.Format24bppRgb);

		using (var g = System.Drawing.Graphics.FromImage(rgb))
		{
			g.Clear(System.Drawing.Color.FromArgb(255, background.Red, background.Green, background.Blue));
			g.DrawImageUnscaled(argb, 0, 0);
		}

		rgb.Save(path, ImageFormat.Bmp);
	}

	#endregion

	#region SVG

	/// <summary>
	/// Экспорт в SVG. Геометрия остаётся векторной; растровые фигуры
	/// (ImageShape) Skia встраивает внутрь SVG как base64-PNG.
	/// </summary>
	public static bool ExportSvg(string path, Page page, IReadOnlyList<Shape> shapes)
	{
		var bounds = GetExportBounds(page, shapes);
		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
			return false;

		float mmPerWorldUnit = MathF.Max(page.MmPerUom, 0.0001f);

		// viewBox задаётся в CSS-пикселях при 96 DPI: это то, что ожидают
		// браузеры и векторные редакторы при импорте без указания единиц.
		float pxPerWorldUnit = SvgDpi / 25.4f * mmPerWorldUnit;

		float width = bounds.Width * pxPerWorldUnit;
		float height = bounds.Height * pxPerWorldUnit;

		// Порядок using важен: канва должна освободиться раньше потока.
		// SKSvgCanvas буферизует часть вызовов, и до Dispose файл неполный.
		using var stream = File.Create(path);

		using (var canvas = SKSvgCanvas.Create(SKRect.Create(0, 0, width, height), stream))
		{
			canvas.Scale(pxPerWorldUnit);
			canvas.Translate(-bounds.Left, -bounds.Top);

			DrawShapes(canvas, page, shapes);
		}

		return true;
	}

	#endregion
}