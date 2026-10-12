using SkiaSharp;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

using VDraw.Shapes;

namespace VDraw.Documents;

public enum PdfImportMode
{
	/// <summary>Объекты страницы становятся полноценными фигурами VDraw.</summary>
	Vector,

	/// <summary>Страница растеризуется целиком и вставляется как ImageShape.</summary>
	Raster,

	/// <summary>Вектор, но при пустом или неразобранном результате — растр.</summary>
	VectorWithRasterFallback
}

public sealed class PdfImportOptions
{
	public PdfImportMode Mode { get; set; } = PdfImportMode.VectorWithRasterFallback;

	/// <summary>Номера страниц PDF с нуля. Пустой список — все страницы.</summary>
	public List<int> Pages { get; } = new();

	public string? Password { get; set; }

	/// <summary>Разрешение растеризации в точках на дюйм.</summary>
	public float RasterDpi { get; set; } = 300f;

	/// <summary>Текст импортируется как TextShape, иначе — как контуры.</summary>
	public bool TextAsText { get; set; } = true;

	/// <summary>
	/// Поднимать программы шрифтов из файла для тех семейств, которых нет
	/// в системе. Выключение оставит подстановку: текст останется на месте,
	/// но начертание и ширина строк изменятся.
	/// </summary>
	public bool UseEmbeddedFonts { get; set; } = true;

	/// <summary>
	/// Сохранять поднятые шрифты в частный кэш приложения, чтобы после
	/// перезапуска документ выглядел так же. В систему не устанавливает.
	/// </summary>
	public bool CacheEmbeddedFonts { get; set; } = true;

	public bool ImportImages { get; set; } = true;

	/// <summary>Раскрывать Form XObject вместо пропуска.</summary>
	public bool FlattenForms { get; set; } = true;

	/// <summary>Складывать фигуры одной страницы PDF в группу.</summary>
	public bool GroupPerPage { get; set; } = true;

	/// <summary>Вписывать страницу PDF в лист VDraw, если она крупнее.</summary>
	public bool FitToPage { get; set; } = true;

	/// <summary>
	/// Отбрасывать невидимые объекты: полностью прозрачные и вырожденные
	/// по габаритам. В печатных PDF таких бывает половина содержимого —
	/// это следы белых подложек и служебной разметки.
	/// </summary>
	public bool SkipInvisible { get; set; } = true;

	/// <summary>
	/// Предел числа фигур. Картографические и приборные PDF содержат
	/// сотни тысяч контуров: без предела импорт выглядит как зависание.
	/// </summary>
	public int MaxShapes { get; set; } = 50_000;
}

public sealed class PdfImportResult
{
	public List<Shape> Shapes { get; } = new();
	public List<string> Warnings { get; } = new();

	/// <summary>Что произошло со шрифтами документа.</summary>
	public PdfFontReport Fonts { get; } = new();

	public int PdfPageCount { get; set; }
	public int ImportedPageCount { get; set; }
	public bool LimitReached { get; set; }
}

/// <summary>
/// Импорт PDF в документ VDraw поверх нативной библиотеки PDFium.
///
/// Единицы. PDF меряет всё в пунктах (1/72 дюйма), начало координат —
/// левый нижний угол, ось Y направлена вверх. VDraw работает в единицах
/// страницы с осью Y вниз и началом в левом верхнем углу. Всё
/// преобразование сведено в одну матрицу <see cref="PdfPageMap"/>:
/// пересчитывать координаты по месту — верный способ получить страницу,
/// перевёрнутую только в половине объектов.
///
/// Потокобезопасность. PDFium не потокобезопасен даже для разных
/// документов: у него общий пул шрифтов и общий аллокатор. Все вызовы
/// идут под одной блокировкой. Импорт поэтому запускается из фоновой
/// задачи целиком, а не распараллеливается по страницам.
/// </summary>
public static class PdfImporter
{
	private static readonly object Sync = new();
	private static bool _initialized;
	private static bool _resolverInstalled;

	#region Инициализация

	private static void EnsureInitialized()
	{
		if (_initialized)
			return;

		InstallResolver();

		PdfNative.FPDF_InitLibrary();
		_initialized = true;
	}

	/// <summary>
	/// Полный путь к загруженной pdfium.dll. null — библиотеку нашёл
	/// не резолвер, а системный поиск, либо она ещё не загружалась.
	///
	/// Нужен сообщениям об ошибках: «файл не является корректным PDF»
	/// на исправном файле означает, что подсунута чужая или несовместимая
	/// сборка библиотеки, и первое, что требуется узнать, — какая именно.
	/// </summary>
	public static string? NativeLibraryPath { get; private set; }

	/// <summary>
	/// Поиск pdfium.dll. Основное место — папка Libraries в каталоге
	/// сборки, рядом с Resources и исполняемым файлом.
	///
	/// Остальные пути оставлены запасными: каталог сборки напрямую и
	/// раскладка runtimes/RID/native, которую делают пакеты с нативными
	/// бинарниками. Порядок задаёт приоритет — положенная в Libraries
	/// библиотека перекрывает всё, что могло приехать из пакетов.
	///
	/// Без резолвера приложение падает с DllNotFoundException уже после
	/// выбора файла, когда пользователь считает, что импорт начался.
	/// </summary>
	private static void InstallResolver()
	{
		if (_resolverInstalled)
			return;

		_resolverInstalled = true;

		NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, asm, path) =>
		{
			if (!string.Equals(name, "pdfium", StringComparison.OrdinalIgnoreCase))
				return IntPtr.Zero;

			foreach (var candidate in EnumerateNativeCandidates())
			{
				if (!File.Exists(candidate))
					continue;

				if (!NativeLibrary.TryLoad(candidate, out var handle))
					continue;

				NativeLibraryPath = candidate;
				return handle;
			}

			return IntPtr.Zero;
		});
	}

	/// <summary>
	/// Места, где ищется нативная библиотека, в порядке убывания приоритета.
	///
	/// Подпапка по RID внутри Libraries нужна тем, кто держит рядом сборки
	/// под несколько разрядностей: положить два файла с одним именем
	/// в одну папку нельзя, а перепутать x86 и x64 — легко.
	/// </summary>
	private static IEnumerable<string> EnumerateNativeCandidates()
	{
		string baseDir = AppContext.BaseDirectory;

		string rid = RuntimeInformation.ProcessArchitecture == Architecture.X86
			? "win-x86"
			: RuntimeInformation.ProcessArchitecture == Architecture.Arm64
				? "win-arm64"
				: "win-x64";

		yield return Path.Combine(ResourcePaths.Libraries, "pdfium.dll");
		yield return Path.Combine(ResourcePaths.Libraries, rid, "pdfium.dll");
		yield return Path.Combine(baseDir, "pdfium.dll");
		yield return Path.Combine(baseDir, "runtimes", rid, "native", "pdfium.dll");
	}

	#endregion

	#region Публичные операции

	/// <summary>
	/// Размеры страниц PDF в миллиметрах. Нужны диалогу импорта, чтобы
	/// предложить создать листы под формат исходника, и не требуют
	/// разбора содержимого.
	/// </summary>
	public static List<SKSize> ReadPageSizesMm(string filePath, string? password = null)
	{
		var result = new List<SKSize>();

		lock (Sync)
		{
			EnsureInitialized();

			using var doc = PdfDocument.Open(filePath, password);

			const float MmPerPoint = 25.4f / 72f;

			for (int i = 0; i < doc.PageCount; i++)
			{
				IntPtr page = PdfNative.FPDF_LoadPage(doc.Handle, i);

				if (page == IntPtr.Zero)
				{
					result.Add(SKSize.Empty);
					continue;
				}

				try
				{
					result.Add(new SKSize(
						PdfNative.FPDF_GetPageWidthF(page) * MmPerPoint,
						PdfNative.FPDF_GetPageHeightF(page) * MmPerPoint));
				}
				finally
				{
					PdfNative.FPDF_ClosePage(page);
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Импортирует выбранные страницы PDF в одну страницу VDraw.
	///
	/// Фигуры не добавляются на страницу: вызывающий сам решает, когда
	/// и через какую запись истории их вставить — так же, как это
	/// сделано для импорта документа и вставки из буфера.
	/// </summary>
	public static PdfImportResult Import(string filePath, Page targetPage, PdfImportOptions? options = null)
	{
		if (targetPage == null)
			throw new ArgumentNullException(nameof(targetPage));

		options ??= new PdfImportOptions();

		var result = new PdfImportResult();

		lock (Sync)
		{
			EnsureInitialized();

			using var doc = PdfDocument.Open(filePath, options.Password);

			result.PdfPageCount = doc.PageCount;

			var fonts = new PdfFontResolver(result.Fonts, options.UseEmbeddedFonts);
			var layers = new PdfLayerAllocator(targetPage);

			var indices = options.Pages.Count > 0
				? options.Pages.Where(i => i >= 0 && i < doc.PageCount).Distinct().ToList()
				: Enumerable.Range(0, doc.PageCount).ToList();

			foreach (int index in indices)
			{
				if (result.Shapes.Count >= options.MaxShapes)
				{
					result.LimitReached = true;
					break;
				}

				ImportSinglePage(doc, index, targetPage, options, result, fonts, layers);
				result.ImportedPageCount++;
			}

			// Кэш пишется один раз в конце: запись на каждый шрифт
			// превратила бы импорт многостраничного документа в череду
			// обращений к диску.
			// Общий счётчик слоёв сдвигается за выданный блок: фигуры,
			// созданные после импорта, обязаны лечь поверх импортированных.
			layers.Commit();

			if (options.UseEmbeddedFonts && options.CacheEmbeddedFonts && result.Fonts.Embedded.Any())
				EmbeddedFontRegistry.SaveCache();
		}

		return result;
	}

	/// <summary>
	/// Отрисовывает страницы для предпросмотра, вызывая обратный вызов
	/// по мере готовности каждой.
	///
	/// Пакетом, а не по одной странице за вызов: каждое открытие
	/// документа читает файл целиком, и для сорокастраничного PDF это
	/// сорок чтений вместо одного. Обратный вызов приходит из фонового
	/// потока — вызывающий обязан сам перейти в поток интерфейса.
	///
	/// Владение битмапами передаётся вызывающему.
	/// </summary>
	public static void RenderPages(
		string filePath, IReadOnlyList<int> indices, int maxPixelSize,
		string? password, Action<int, SKBitmap?> onPage, CancellationToken token)
	{
		if (onPage == null)
			throw new ArgumentNullException(nameof(onPage));

		lock (Sync)
		{
			EnsureInitialized();

			using var doc = PdfDocument.Open(filePath, password);

			foreach (int index in indices)
			{
				if (token.IsCancellationRequested)
					return;

				if (index < 0 || index >= doc.PageCount)
				{
					onPage(index, null);
					continue;
				}

				IntPtr page = PdfNative.FPDF_LoadPage(doc.Handle, index);

				if (page == IntPtr.Zero)
				{
					onPage(index, null);
					continue;
				}

				try
				{
					float widthPt = PdfNative.FPDF_GetPageWidthF(page);
					float heightPt = PdfNative.FPDF_GetPageHeightF(page);

					if (widthPt <= 0f || heightPt <= 0f)
					{
						onPage(index, null);
						continue;
					}

					float k = maxPixelSize / MathF.Max(widthPt, heightPt);

					int pxWidth = Math.Max(1, (int)MathF.Round(widthPt * k));
					int pxHeight = Math.Max(1, (int)MathF.Round(heightPt * k));

					onPage(index, RenderToBitmap(page, pxWidth, pxHeight));
				}
				finally
				{
					PdfNative.FPDF_ClosePage(page);
				}
			}
		}
	}

	/// <summary>
	/// Растеризация страницы в новый битмап. PDFium пишет прямо
	/// в пиксели SKBitmap: промежуточный буфер на лист A0 — это лишние
	/// сотни мегабайт и лишнее копирование.
	/// </summary>
	private static SKBitmap? RenderToBitmap(IntPtr page, int pxWidth, int pxHeight)
	{
		var info = new SKImageInfo(pxWidth, pxHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		var bitmap = new SKBitmap(info);

		IntPtr pixels = bitmap.GetPixels();

		if (pixels == IntPtr.Zero)
		{
			bitmap.Dispose();
			return null;
		}

		IntPtr fpdfBitmap = PdfNative.FPDFBitmap_CreateEx(
			pxWidth, pxHeight, PdfNative.FPDFBitmap_BGRA, pixels, bitmap.RowBytes);

		if (fpdfBitmap == IntPtr.Zero)
		{
			bitmap.Dispose();
			return null;
		}

		try
		{
			PdfNative.FPDFBitmap_FillRect(fpdfBitmap, 0, 0, pxWidth, pxHeight, 0xFFFFFFFF);

			int flags = PdfNative.FPDF_ANNOT | PdfNative.FPDF_RENDER_LIMITEDIMAGECACHE;

			PdfNative.FPDF_RenderPageBitmap(fpdfBitmap, page, 0, 0, pxWidth, pxHeight, 0, flags);
		}
		finally
		{
			PdfNative.FPDFBitmap_Destroy(fpdfBitmap);
		}

		bitmap.NotifyPixelsChanged();

		return bitmap;
	}

	#endregion

	#region Импорт одной страницы

	private static void ImportSinglePage(
		PdfDocument doc, int pageIndex, Page targetPage,
		PdfImportOptions options, PdfImportResult result, PdfFontResolver fonts,
		PdfLayerAllocator layers)
	{
		IntPtr page = PdfNative.FPDF_LoadPage(doc.Handle, pageIndex);

		if (page == IntPtr.Zero)
		{
			result.Warnings.Add($"Страница {pageIndex + 1} не открылась (код {PdfNative.FPDF_GetLastError()}).");
			return;
		}

		try
		{
			var map = PdfPageMap.Build(page, targetPage, options.FitToPage);

			List<Shape> shapes;

			if (options.Mode == PdfImportMode.Raster)
			{
				shapes = RasterizePage(page, map, targetPage, options, result, pageIndex, layers);
			}
			else
			{
				shapes = PdfShapeBuilder.BuildPage(doc, page, map, options, result, targetPage, fonts, layers);

				if (shapes.Count == 0 && options.Mode == PdfImportMode.VectorWithRasterFallback)
				{
					result.Warnings.Add(
						$"Страница {pageIndex + 1}: векторное содержимое не разобрано, вставлен растр.");

					shapes = RasterizePage(page, map, targetPage, options, result, pageIndex, layers);
				}
			}

			if (shapes.Count == 0)
				return;

			if (options.GroupPerPage && shapes.Count > 1)
			{
				// Слой группы — слой её первой фигуры, как это делает
				// GroupingHelper при обычной группировке.
				var group = new GroupShape
				{
					Name = $"PDF page {pageIndex + 1}",
					Layer = shapes[0].Layer
				};

				// AddChild на каждом вызове проверяет Contains и пересортирует
				// список целиком. На странице в десять тысяч контуров это
				// квадратичная работа, то есть заметное подвисание. Порядок
				// у фигур уже верный: слои выданы по порядку отрисовки PDF.
				foreach (var shape in shapes)
				{
					shape.ReparentPreserveWorld(group);
					group.Children.Add(shape);
				}

				result.Shapes.Add(group);
			}
			else
			{
				result.Shapes.AddRange(shapes);
			}
		}
		finally
		{
			PdfNative.FPDF_ClosePage(page);
		}
	}

	#endregion

	#region Растровый путь

	private static List<Shape> RasterizePage(
		IntPtr page, in PdfPageMap map, Page targetPage,
		PdfImportOptions options, PdfImportResult result, int pageIndex,
		PdfLayerAllocator layers)
	{
		var shapes = new List<Shape>();

		float widthPt = PdfNative.FPDF_GetPageWidthF(page);
		float heightPt = PdfNative.FPDF_GetPageHeightF(page);

		if (widthPt <= 0f || heightPt <= 0f)
			return shapes;

		float dpi = Math.Clamp(options.RasterDpi, 36f, 1200f);
		float pxPerPoint = dpi / 72f;

		int pxWidth = (int)MathF.Round(widthPt * pxPerPoint);
		int pxHeight = (int)MathF.Round(heightPt * pxPerPoint);

		// Ограничение по площади, а не по стороне: лист A0 при 600 dpi
		// это 1.2 гигапикселя, и SKBitmap на нём падает без внятного текста
		// ошибки.
		const long MaxPixels = 64L * 1024 * 1024;

		if ((long)pxWidth * pxHeight > MaxPixels)
		{
			double k = Math.Sqrt(MaxPixels / (double)((long)pxWidth * pxHeight));

			pxWidth = Math.Max(1, (int)(pxWidth * k));
			pxHeight = Math.Max(1, (int)(pxHeight * k));

			result.Warnings.Add(
				$"Страница {pageIndex + 1}: разрешение растра снижено до {pxWidth}×{pxHeight}.");
		}

		var bitmap = RenderToBitmap(page, pxWidth, pxHeight);

		if (bitmap == null)
		{
			result.Warnings.Add($"Страница {pageIndex + 1}: не удалось создать растровый буфер.");
			return shapes;
		}

		// Габариты берутся из карты страницы, а не из числа пикселей:
		// растр обязан лечь ровно туда же, куда лёг бы вектор.
		var rect = new SKRect(
			-map.DisplaySizeUom.Width * 0.5f,
			-map.DisplaySizeUom.Height * 0.5f,
			map.DisplaySizeUom.Width * 0.5f,
			map.DisplaySizeUom.Height * 0.5f);

		var center = new SKPoint(
			map.Origin.X + map.DisplaySizeUom.Width * 0.5f,
			map.Origin.Y + map.DisplaySizeUom.Height * 0.5f);

		var shape = new ImageShape
		{
			Name = $"PDF page {pageIndex + 1}",
			ImageLocalRect = rect,
			LocalTransform = SKMatrix.CreateTranslation(center.X, center.Y),
			Layer = layers.Next()
		};

		PdfShapeBuilder.AddRectNodes(shape, rect);

		shape.Fill.Enabled = false;
		shape.Stroke.Enabled = false;

		shape.SetOriginalBitmap(bitmap);
		bitmap.Dispose();

		shapes.Add(shape);

		return shapes;
	}

	#endregion
}

/// <summary>
/// Выдаёт номера слоёв импортируемым фигурам.
///
/// Отсчёт начинается за максимальным слоём, уже занятым на листе:
/// импортированное содержимое обязано лечь ПОВЕРХ нарисованного, а не
/// вперемешку с ним. Общий счётчик Settings.layer_start сам по себе
/// такой гарантии не даёт — он синхронизируется при загрузке документа,
/// а лист мог получить фигуры и другими путями.
///
/// Номера выдаются подряд в порядке отрисовки PDF. Это важнее, чем
/// кажется: без явных номеров фигуры остаются с UnassignedLayer, а
/// List.Sort неустойчив и на равных ключах переставляет элементы
/// произвольно — порядок перекрытия внутри страницы оказался бы случайным.
/// </summary>
internal sealed class PdfLayerAllocator
{
	private int _next;

	public PdfLayerAllocator(Page page)
	{
		int max = Settings.layer_start;

		foreach (var shape in EnumerateDeep(page.Shapes))
		{
			if (shape.Layer == Shape.UnassignedLayer || shape.Layer == Shape.PreviewLayer)
				continue;

			max = Math.Max(max, shape.Layer + 1);
		}

		_next = max;
	}

	public int Next() => _next++;

	/// <summary>Сдвигает общий счётчик за выданный блок.</summary>
	public void Commit() => Settings.layer_start = Math.Max(Settings.layer_start, _next);

	private static IEnumerable<Shape> EnumerateDeep(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateDeep(group.Children))
					yield return child;
			}
		}
	}
}

/// <summary>Владение нативным документом и буфером его байтов.</summary>
internal sealed class PdfDocument : IDisposable
{
	public IntPtr Handle { get; private set; }
	public int PageCount { get; }

	private GCHandle _pin;

	private PdfDocument(IntPtr handle, GCHandle pin)
	{
		Handle = handle;
		_pin = pin;
		PageCount = PdfNative.FPDF_GetPageCount(handle);
	}

	/// <summary>
	/// Каким способом открылся последний документ. Нужно для сообщений
	/// и для понимания, работает ли основной путь загрузки вообще.
	/// </summary>
	public static string? LastLoadRoute { get; private set; }

	public static PdfDocument Open(string filePath, string? password)
	{
		if (!File.Exists(filePath))
			throw new FileNotFoundException("PDF-файл не найден.", filePath);

		byte[] bytes = File.ReadAllBytes(filePath);

		// Пустой или обрезанный до неузнаваемости файл — отдельный случай.
		// Без этой проверки он доходил бы до PDFium и возвращался как
		// «неверный формат», уводя разбор в сторону библиотеки, тогда как
		// читать было нечего с самого начала. Так бывает у файлов-заглушек
		// облачных дисков и у прерванных загрузок.
		if (bytes.LongLength < 32)
		{
			throw new InvalidDataException(
				$"Файл пустой или обрезанный: {bytes.LongLength} байт.\r\n" +
				$"Файл: {Path.GetFileName(filePath)}");
		}

		var doc = TryOpen(bytes, filePath, password, out int error, out string route);

		if (doc != null)
		{
			LastLoadRoute = route;
			return doc;
		}

		// Вторая попытка — с отброшенной преамбулой.
		//
		// PDFium ищет заголовок только в первом килобайте файла и всё, что
		// дальше, объявляет неформатом. Между тем файлы с мусором перед
		// %PDF встречаются регулярно: почтовые шлюзы дописывают заголовки
		// письма, часть сканеров и «принтеров в PDF» оставляют служебную
		// шапку. Такой документ исправен целиком, начиная с настоящего
		// заголовка, и открывается, если отдать библиотеке только хвост.
		//
		// Ищем по всему файлу, а не по первому килобайту: иначе эта ветка
		// сработала бы ровно в тех случаях, когда PDFium справился бы и сам.
		int header = FindPdfHeader(bytes, bytes.Length);

		if (header > 0)
		{
			byte[] trimmed = new byte[bytes.Length - header];
			Buffer.BlockCopy(bytes, header, trimmed, 0, trimmed.Length);

			var repaired = TryOpen(trimmed, filePath, password, out error, out route);

			if (repaired != null)
			{
				LastLoadRoute = $"{route}, отброшено {header} байт преамбулы";
				return repaired;
			}
		}

		throw error switch
		{
			PdfNative.FPDF_ERR_PASSWORD => new UnauthorizedAccessException("PDF защищён паролем."),
			PdfNative.FPDF_ERR_SECURITY => new UnauthorizedAccessException("PDF использует неподдерживаемое шифрование."),
			PdfNative.FPDF_ERR_FORMAT => new InvalidDataException(
				Describe("Файл не является корректным PDF.", error, bytes, filePath)),
			_ => new InvalidDataException(Describe("PDF не открылся.", error, bytes, filePath))
		};
	}

	/// <summary>
	/// Пытается открыть документ из готового буфера. null — не вышло,
	/// закрепление буфера при этом снято.
	///
	/// Буфер закрепляется на всё время жизни документа: PDFium читает
	/// его лениво и обращается к нему при загрузке каждой страницы.
	/// </summary>
	private static PdfDocument? TryOpen(
		byte[] bytes,
		string filePath,
		string? password,
		out int error,
		out string route)
	{
		var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);

		IntPtr handle = LoadAnyWay(
			pin.AddrOfPinnedObject(), bytes, filePath, password, out error, out route);

		if (handle != IntPtr.Zero)
			return new PdfDocument(handle, pin);

		pin.Free();
		return null;
	}

	/// <summary>
	/// Открывает документ, перебирая три способа загрузки подряд.
	///
	/// Порядок от лучшего к худшему. Загрузка из памяти с 64-битным
	/// размером — основная: она не зависит от того, как сборка PDFium
	/// трактует путь, и переживает нелатинские имена файлов. Вариант
	/// с int-размером отличается только шириной второго параметра;
	/// если основной путь ломается на несовпадении разметки аргументов,
	/// сработает он. Загрузка по пути не зависит ни от буфера, ни от
	/// размера вовсе — библиотека читает файл сама.
	///
	/// Перебор нужен потому, что сборки PDFium расходятся между собой,
	/// а отличить «файл действительно негодный» от «библиотека не та»
	/// иначе нельзя: и то, и другое приходит одним кодом ошибки.
	/// Способ, которым документ в итоге открылся, запоминается — по нему
	/// видно, что именно в связке не работает.
	/// </summary>
	private static IntPtr LoadAnyWay(
		IntPtr buffer,
		byte[] bytes,
		string filePath,
		string? password,
		out int error,
		out string route)
	{
		error = 0;
		route = "FPDF_LoadMemDocument64";

		IntPtr handle = TryCall(() => PdfNative.FPDF_LoadMemDocument64(buffer, bytes.LongLength, password), ref error);

		if (handle != IntPtr.Zero)
			return handle;

		// int переполнится на файле больше двух гигабайт, и вызов
		// получил бы отрицательный размер.
		if (bytes.LongLength <= int.MaxValue)
		{
			route = "FPDF_LoadMemDocument";
			handle = TryCall(() => PdfNative.FPDF_LoadMemDocument(buffer, bytes.Length, password), ref error);

			if (handle != IntPtr.Zero)
				return handle;
		}

		route = "FPDF_LoadDocument";

		// Ноль на конце добавляется вручную: сюда уходит массив байтов,
		// а не строка, и завершать его маршалер не станет.
		byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(filePath + "\0");

		handle = TryCall(() => PdfNative.FPDF_LoadDocument(pathBytes, password), ref error);

		if (handle != IntPtr.Zero)
			return handle;

		route = "ни один из трёх способов";
		return IntPtr.Zero;
	}

	/// <summary>
	/// Вызов загрузки с запоминанием кода ошибки.
	///
	/// EntryPointNotFoundException гасится: в сборке PDFium может не быть
	/// какой-то из трёх функций, и это не повод прекращать перебор.
	/// </summary>
	private static IntPtr TryCall(Func<IntPtr> load, ref int error)
	{
		try
		{
			IntPtr handle = load();

			if (handle == IntPtr.Zero)
				error = PdfNative.FPDF_GetLastError();

			return handle;
		}
		catch (EntryPointNotFoundException)
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>
	/// Сообщение об ошибке вместе с тем, что нужно для разбора.
	///
	/// Отдельно проверяется сигнатура файла. Если она на месте, а PDFium
	/// всё равно отказывается открывать документ всеми тремя способами,
	/// дело не в файле — значит, загруженная библиотека несовместима
	/// с объявленными здесь сигнатурами, и об этом надо сказать прямо.
	/// Иначе пользователь будет искать причину в своих PDF, а её там нет.
	/// </summary>
	private static string Describe(string message, int error, byte[] bytes, string filePath)
	{
		var text = new System.Text.StringBuilder(message);

		// Порядок строк не случаен: сначала то, что видно в файле, и лишь
		// потом вывод из этого. Сообщение читают в окне ошибки и копируют
		// первые строки, а самое полезное здесь — байты начала файла,
		// и терять их из-за обрезки копирования нельзя.
		text.AppendLine();
		text.Append("Начало файла: ").AppendLine(DescribeHead(bytes));
		text.Append("Файл: ").AppendLine(Path.GetFileName(filePath));
		text.Append("Размер: ").Append(bytes.LongLength).AppendLine(" байт");

		int header = FindPdfHeader(bytes, bytes.Length);

		text.AppendLine();

		if (header < 0)
		{
			text.AppendLine("Сигнатуры %PDF нет НИГДЕ в файле.");
			text.Append("Прочитанные байты — не PDF: либо расширение не соответствует ")
				.AppendLine("содержимому, либо файл отдан приложению не целиком.");
		}
		else if (header > 1024)
		{
			text.Append("Сигнатура %PDF найдена со смещения ").Append(header)
				.AppendLine(", то есть за пределами первого килобайта.");
			text.AppendLine("Отбросить преамбулу и открыть хвост тоже не удалось.");
		}
		else
		{
			text.Append("Сигнатура %PDF найдена со смещения ").Append(header)
				.AppendLine(" — файл прочитан правильно и на PDF похож.");
			text.AppendLine("Значит, дело в содержимом документа либо в самой библиотеке PDFium.");
		}

		text.AppendLine();
		text.Append("Код ошибки PDFium: ").Append(error).AppendLine();
		text.Append("Разрядность процесса: ").AppendLine(Environment.Is64BitProcess ? "x64" : "x86");
		text.Append("Библиотека: ")
			.Append(PdfImporter.NativeLibraryPath ?? "найдена системным поиском, путь неизвестен");

		return text.ToString();
	}

	/// <summary>
	/// Начало файла в печатном виде и шестнадцатеричном.
	///
	/// Оба сразу: печатный вид сразу показывает знакомую подпись вроде
	/// %PDF-1.7 или PK для зазипованного файла, а шестнадцатеричный
	/// не теряет непечатные байты — по ним видно, например, BOM или
	/// что вместо файла прочитан мусор.
	/// </summary>
	private static string DescribeHead(byte[] bytes)
	{
		int count = Math.Min(bytes.Length, 32);

		var ascii = new System.Text.StringBuilder(count);
		var hex = new System.Text.StringBuilder(count * 3);

		for (int i = 0; i < count; i++)
		{
			byte b = bytes[i];

			ascii.Append(b >= 32 && b < 127 ? (char)b : '.');
			hex.Append(b.ToString("X2")).Append(' ');
		}

		return $"{ascii}   [{hex.ToString().TrimEnd()}]";
	}

	/// <summary>
	/// Смещение сигнатуры PDF в пределах limit байт или -1, если её нет.
	///
	/// Спецификация разрешает мусор перед заголовком, и PDFium его тоже
	/// допускает — но только в первом килобайте. Поэтому предел здесь
	/// параметр: для решения «годен ли файл вообще» ищем по всему файлу,
	/// а килобайт остаётся границей того, что библиотека проглотит сама.
	/// </summary>
	private static int FindPdfHeader(byte[] bytes, int limit)
	{
		int last = Math.Min(bytes.Length - 4, limit);

		for (int i = 0; i <= last; i++)
		{
			if (bytes[i] == (byte)'%'
				&& bytes[i + 1] == (byte)'P'
				&& bytes[i + 2] == (byte)'D'
				&& bytes[i + 3] == (byte)'F')
			{
				return i;
			}
		}

		return -1;
	}

	public void Dispose()
	{
		if (Handle != IntPtr.Zero)
		{
			PdfNative.FPDF_CloseDocument(Handle);
			Handle = IntPtr.Zero;
		}

		if (_pin.IsAllocated)
			_pin.Free();
	}
}

/// <summary>
/// Отображение пользовательского пространства PDF в единицы страницы VDraw.
///
/// Учитывает четыре вещи разом: масштаб пункт → единица, обрезку по
/// CropBox, поворот /Rotate и переворот оси Y. Собирается один раз
/// на страницу.
/// </summary>
internal readonly struct PdfPageMap
{
	public readonly SKMatrix ToPage;

	/// <summary>Размер отображаемой страницы PDF в единицах VDraw.</summary>
	public readonly SKSize DisplaySizeUom;

	/// <summary>Левый верхний угол вставки на листе VDraw.</summary>
	public readonly SKPoint Origin;

	/// <summary>Миллиметров в одном пункте PDF после масштабирования.</summary>
	public readonly float MmPerPoint;

	private PdfPageMap(SKMatrix toPage, SKSize displaySize, SKPoint origin, float mmPerPoint)
	{
		ToPage = toPage;
		DisplaySizeUom = displaySize;
		Origin = origin;
		MmPerPoint = mmPerPoint;
	}

	public static PdfPageMap Build(IntPtr page, Page targetPage, bool fitToPage)
	{
		const float MmPerPointBase = 25.4f / 72f;

		float mmPerUom = MathF.Max(targetPage.MmPerUom, 0.0001f);
		float uomPerPoint = MmPerPointBase / mmPerUom;

		if (!PdfNative.FPDFPage_GetCropBox(page, out float l, out float b, out float r, out float t))
		{
			if (!PdfNative.FPDFPage_GetMediaBox(page, out l, out b, out r, out t))
			{
				l = 0f;
				b = 0f;
				r = PdfNative.FPDF_GetPageWidthF(page);
				t = PdfNative.FPDF_GetPageHeightF(page);
			}
		}

		// CropBox допускает любой порядок углов: нормализуем, иначе
		// отрицательная высота даст страницу, вывернутую наизнанку.
		if (r < l) (l, r) = (r, l);
		if (t < b) (b, t) = (t, b);

		float wPt = MathF.Max(r - l, 0.001f);
		float hPt = MathF.Max(t - b, 0.001f);

		int quarter = PdfNative.FPDFPage_GetRotation(page) & 3;

		float displayWpt = (quarter % 2 == 0) ? wPt : hPt;
		float displayHpt = (quarter % 2 == 0) ? hPt : wPt;

		// Вписывание считается до построения матрицы, чтобы масштаб вошёл
		// в неё одним множителем, а не догонялся отдельным преобразованием.
		float s = uomPerPoint;

		if (fitToPage)
		{
			float maxW = targetPage.size_uom.Width;
			float maxH = targetPage.size_uom.Height;

			float fit = MathF.Min(
				maxW / (displayWpt * uomPerPoint),
				maxH / (displayHpt * uomPerPoint));

			if (fit < 1f)
				s = uomPerPoint * fit;
		}

		// Вывод преобразований по четвертям поворота.
		// u = x - l, v = y - b — координаты внутри CropBox, ось v вверх.
		//   0°:   dx = u,        dy = hPt - v
		//  90°:   dx = v,        dy = u
		// 180°:   dx = wPt - u,  dy = v
		// 270°:   dx = hPt - v,  dy = wPt - u
		SKMatrix m = quarter switch
		{
			1 => Make(0f, s, s, 0f, -s * b, -s * l),
			2 => Make(-s, 0f, 0f, s, s * r, -s * b),
			3 => Make(0f, -s, -s, 0f, s * t, s * r),
			_ => Make(s, 0f, 0f, -s, -s * l, s * t)
		};

		var displaySize = new SKSize(displayWpt * s, displayHpt * s);

		// Страница PDF центрируется на листе VDraw. Ставить её в угол
		// хуже: несовпадение форматов тогда выглядит как сбитая привязка.
		var origin = new SKPoint(
			(targetPage.size_uom.Width - displaySize.Width) * 0.5f,
			(targetPage.size_uom.Height - displaySize.Height) * 0.5f);

		m.TransX += origin.X;
		m.TransY += origin.Y;

		return new PdfPageMap(m, displaySize, origin, MmPerPointBase * (s / uomPerPoint));
	}

	/// <summary>
	/// Сборка из коэффициентов в записи PDF [a b c d e f]:
	/// x' = a·x + c·y + e, y' = b·x + d·y + f.
	/// </summary>
	private static SKMatrix Make(float a, float b, float c, float d, float e, float f)
	{
		return new SKMatrix
		{
			ScaleX = a,
			SkewY = b,
			SkewX = c,
			ScaleY = d,
			TransX = e,
			TransY = f,
			Persp0 = 0f,
			Persp1 = 0f,
			Persp2 = 1f
		};
	}
}
