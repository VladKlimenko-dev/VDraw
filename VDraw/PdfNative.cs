using System.Runtime.InteropServices;

namespace VDraw.Documents;

/// <summary>
/// Объявления нужного подмножества C API библиотеки PDFium.
///
/// Биндинги написаны вручную, а не взяты из пакета-обёртки, намеренно:
/// генерируемые обёртки переименовывают функции от версии к версии, и
/// обновление пакета ломает вызовы в местах, к PDF отношения не имеющих.
/// Здесь же контракт зафиксирован ровно тот, что описан в fpdfview.h
/// и fpdf_edit.h, и меняется он только вместе с самим PDFium.
///
/// Требуется нативный pdfium.dll рядом с исполняемым файлом — см.
/// README_PDF_IMPORT.md. Соглашение вызова у всех функций — cdecl.
/// </summary>
internal static unsafe class PdfNative
{
	private const string Lib = "pdfium";

	#region Константы

	public const int FPDF_PAGEOBJ_UNKNOWN = 0;
	public const int FPDF_PAGEOBJ_TEXT = 1;
	public const int FPDF_PAGEOBJ_PATH = 2;
	public const int FPDF_PAGEOBJ_IMAGE = 3;
	public const int FPDF_PAGEOBJ_SHADING = 4;
	public const int FPDF_PAGEOBJ_FORM = 5;

	public const int FPDF_SEGMENT_UNKNOWN = -1;
	public const int FPDF_SEGMENT_LINETO = 0;
	public const int FPDF_SEGMENT_BEZIERTO = 1;
	public const int FPDF_SEGMENT_MOVETO = 2;

	public const int FPDF_FILLMODE_NONE = 0;
	public const int FPDF_FILLMODE_ALTERNATE = 1;
	public const int FPDF_FILLMODE_WINDING = 2;

	public const int FPDFBitmap_Unknown = 0;
	public const int FPDFBitmap_Gray = 1;
	public const int FPDFBitmap_BGR = 2;
	public const int FPDFBitmap_BGRx = 3;
	public const int FPDFBitmap_BGRA = 4;

	public const int FPDF_ANNOT = 0x01;
	public const int FPDF_LCD_TEXT = 0x02;
	public const int FPDF_GRAYSCALE = 0x08;
	public const int FPDF_RENDER_LIMITEDIMAGECACHE = 0x200;
	public const int FPDF_PRINTING = 0x800;
	public const int FPDF_RENDER_NO_SMOOTHTEXT = 0x1000;
	public const int FPDF_RENDER_NO_SMOOTHIMAGE = 0x2000;
	public const int FPDF_RENDER_NO_SMOOTHPATH = 0x4000;

	public const int FPDF_ERR_SUCCESS = 0;
	public const int FPDF_ERR_UNKNOWN = 1;
	public const int FPDF_ERR_FILE = 2;
	public const int FPDF_ERR_FORMAT = 3;
	public const int FPDF_ERR_PASSWORD = 4;
	public const int FPDF_ERR_SECURITY = 5;
	public const int FPDF_ERR_PAGE = 6;

	#endregion

	#region Структуры

	[StructLayout(LayoutKind.Sequential)]
	public struct FS_MATRIX
	{
		public float a, b, c, d, e, f;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct FS_RECTF
	{
		public float left, top, right, bottom;
	}

	#endregion

	#region Жизненный цикл библиотеки

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDF_InitLibrary();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDF_DestroyLibrary();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDF_GetLastError();

	#endregion

	#region Документ

	/// <summary>
	/// Загрузка из памяти, а не из пути: FPDF_LoadDocument принимает
	/// const char*, и путь пришлось бы перекодировать в UTF-8 вручную,
	/// а на нелатинских именах файлов это регулярный источник ошибок.
	/// Буфер обязан жить, пока жив документ — PDFium читает его лениво.
	/// </summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDF_LoadMemDocument64(
		IntPtr dataBuf, long size, [MarshalAs(UnmanagedType.LPStr)] string? password);

	/// <summary>
	/// Прежний вариант загрузки из памяти, с размером int.
	///
	/// Оставлен как запасной путь: сборки PDFium различаются, и если
	/// 64-битный вариант в конкретной библиотеке ведёт себя не так, как
	/// описано в fpdfview.h, документ должен открыться хотя бы этим
	/// способом. Файлы больше двух гигабайт им не берутся — там int
	/// переполняется, и вызывать его для таких нельзя.
	/// </summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDF_LoadMemDocument(
		IntPtr dataBuf, int size, [MarshalAs(UnmanagedType.LPStr)] string? password);

	/// <summary>
	/// Загрузка по пути. Второй запасной путь: он не зависит ни от
	/// разметки буфера, ни от трактовки размера — файл читает сама
	/// библиотека.
	///
	/// Путь передаётся байтами: FPDF_STRING — это const char*, и что
	/// именно в него класть, зависит от сборки. Байты UTF-8 с нулём
	/// на конце подходят для латинских путей всегда, а для остальных
	/// это лучшее, что можно сделать без знания сборки.
	/// </summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDF_LoadDocument(
		byte[] filePath, [MarshalAs(UnmanagedType.LPStr)] string? password);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDF_CloseDocument(IntPtr document);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDF_GetPageCount(IntPtr document);

	#endregion

	#region Страница

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDF_ClosePage(IntPtr page);

	/// <summary>Ширина в пунктах С УЧЁТОМ /Rotate.</summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern float FPDF_GetPageWidthF(IntPtr page);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern float FPDF_GetPageHeightF(IntPtr page);

	/// <summary>0, 1, 2, 3 — то есть 0°, 90°, 180°, 270° по часовой.</summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPage_GetRotation(IntPtr page);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPage_GetCropBox(
		IntPtr page, out float left, out float bottom, out float right, out float top);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPage_GetMediaBox(
		IntPtr page, out float left, out float bottom, out float right, out float top);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPage_CountObjects(IntPtr page);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPage_HasTransparency(IntPtr page);

	#endregion

	#region Растеризация

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFBitmap_CreateEx(
		int width, int height, int format, IntPtr firstScan, int stride);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDFBitmap_FillRect(
		IntPtr bitmap, int left, int top, int width, int height, uint color);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFBitmap_GetWidth(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFBitmap_GetHeight(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFBitmap_GetFormat(IntPtr bitmap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDF_RenderPageBitmap(
		IntPtr bitmap, IntPtr page,
		int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

	#endregion

	#region Объекты страницы: общее

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPageObj_GetType(IntPtr pageObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetMatrix(IntPtr pageObject, out FS_MATRIX matrix);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetBounds(
		IntPtr pageObject, out float left, out float bottom, out float right, out float top);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetFillColor(
		IntPtr pageObject, out uint r, out uint g, out uint b, out uint a);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetStrokeColor(
		IntPtr pageObject, out uint r, out uint g, out uint b, out uint a);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetStrokeWidth(IntPtr pageObject, out float width);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPageObj_GetLineCap(IntPtr pageObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPageObj_GetLineJoin(IntPtr pageObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPageObj_GetDashCount(IntPtr pageObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetDashArray(
		IntPtr pageObject, float* dashArray, nuint dashCount);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPageObj_GetDashPhase(IntPtr pageObject, out float phase);

	#endregion

	#region Контуры

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPath_CountSegments(IntPtr path);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFPath_GetPathSegment(IntPtr path, int index);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPath_GetDrawMode(
		IntPtr path, out int fillMode, [MarshalAs(UnmanagedType.Bool)] out bool stroke);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPathSegment_GetPoint(IntPtr segment, out float x, out float y);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFPathSegment_GetType(IntPtr segment);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFPathSegment_GetClose(IntPtr segment);

	#endregion

	#region Form XObject

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFFormObj_CountObjects(IntPtr formObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFFormObj_GetObject(IntPtr formObject, uint index);

	#endregion

	#region Растровые объекты

	/// <summary>
	/// Отрисованный вариант картинки: маски, SMask и цветовое
	/// преобразование уже применены. Сырой FPDFImageObj_GetBitmap
	/// вернул бы поток до применения маски — с чёрным фоном вместо
	/// прозрачности на каждой второй картинке.
	/// </summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFImageObj_GetRenderedBitmap(
		IntPtr document, IntPtr page, IntPtr imageObject);

	#endregion

	#region Текст

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFText_LoadPage(IntPtr page);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void FPDFText_ClosePage(IntPtr textPage);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern uint FPDFTextObj_GetText(
		IntPtr textObject, IntPtr textPage, char* buffer, uint length);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFTextObj_GetFontSize(IntPtr textObject, out float size);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFTextObj_GetTextRenderMode(IntPtr textObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFTextObj_GetFont(IntPtr textObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern nuint FPDFFont_GetBaseFontName(IntPtr font, byte* buffer, nuint length);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern nuint FPDFFont_GetFamilyName(IntPtr font, byte* buffer, nuint length);

	/// <summary>1 — встроен, 0 — нет, -1 — определить не удалось.</summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFFont_GetIsEmbedded(IntPtr font);

	/// <summary>Биты /Flags из FontDescriptor.</summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFFont_GetFlags(IntPtr font);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFFont_GetWeight(IntPtr font);

	/// <summary>
	/// Сама программа шрифта: TrueType, OpenType или Type 1, ровно те
	/// байты, что лежат в потоке FontFile/FontFile2/FontFile3.
	/// </summary>
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool FPDFFont_GetFontData(
		IntPtr font, byte* buffer, nuint bufLen, out nuint outBufLen);

	#endregion

	#region Отсечение

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFPageObj_GetClipPath(IntPtr pageObject);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFClipPath_CountPaths(IntPtr clipPath);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int FPDFClipPath_CountPathSegments(IntPtr clipPath, int pathIndex);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr FPDFClipPath_GetPathSegment(
		IntPtr clipPath, int pathIndex, int segmentIndex);

	#endregion

	#region Обёртки над строковыми вызовами

	/// <summary>
	/// Базовое имя шрифта. Функция появилась не во всех сборках PDFium,
	/// поэтому отсутствие точки входа — не ошибка: текст импортируется
	/// со шрифтом по умолчанию, а не теряется целиком.
	/// </summary>
	public static string GetBaseFontName(IntPtr font)
	{
		if (font == IntPtr.Zero)
			return string.Empty;

		try
		{
			Span<byte> buffer = stackalloc byte[256];

			fixed (byte* p = buffer)
			{
				nuint written = FPDFFont_GetBaseFontName(font, p, (nuint)buffer.Length);

				if (written <= 1)
					return string.Empty;

				// Возвращается длина вместе с завершающим нулём.
				return System.Text.Encoding.ASCII.GetString(p, (int)written - 1);
			}
		}
		catch (EntryPointNotFoundException)
		{
			return string.Empty;
		}
	}

	/// <summary>
	/// Байты встроенной программы шрифта или null, если шрифт не встроен
	/// либо сборка PDFium этот вызов не экспортирует.
	/// </summary>
	public static byte[]? GetFontData(IntPtr font)
	{
		if (font == IntPtr.Zero)
			return null;

		try
		{
			// Первый вызов с нулевым буфером сообщает нужный размер.
			if (!FPDFFont_GetFontData(font, null, 0, out nuint size) || size == 0)
				return null;

			// Программа шрифта крупнее пары мегабайт — это почти наверняка
			// не шрифт, а разбор ушёл не туда. Читать такое в память
			// на каждый текстовый объект незачем.
			if (size > 16 * 1024 * 1024)
				return null;

			var buffer = new byte[(int)size];

			fixed (byte* p = buffer)
			{
				if (!FPDFFont_GetFontData(font, p, size, out nuint written) || written == 0)
					return null;

				if ((int)written < buffer.Length)
					Array.Resize(ref buffer, (int)written);
			}

			return buffer;
		}
		catch (EntryPointNotFoundException)
		{
			return null;
		}
	}

	public static bool IsFontEmbedded(IntPtr font)
	{
		if (font == IntPtr.Zero)
			return false;

		try
		{
			return FPDFFont_GetIsEmbedded(font) == 1;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
	}

	public static int GetFontFlags(IntPtr font)
	{
		if (font == IntPtr.Zero)
			return 0;

		try
		{
			return FPDFFont_GetFlags(font);
		}
		catch (EntryPointNotFoundException)
		{
			return 0;
		}
	}

	public static int GetFontWeight(IntPtr font)
	{
		if (font == IntPtr.Zero)
			return 0;

		try
		{
			return FPDFFont_GetWeight(font);
		}
		catch (EntryPointNotFoundException)
		{
			return 0;
		}
	}

	public static string GetObjectText(IntPtr textObject, IntPtr textPage)
	{
		if (textObject == IntPtr.Zero || textPage == IntPtr.Zero)
			return string.Empty;

		// Первый вызов с нулевым буфером сообщает нужный размер В БАЙТАХ.
		uint bytes = FPDFTextObj_GetText(textObject, textPage, null, 0);

		if (bytes <= 2)
			return string.Empty;

		int chars = (int)(bytes / sizeof(char));
		var buffer = new char[chars];

		fixed (char* p = buffer)
			FPDFTextObj_GetText(textObject, textPage, p, bytes);

		// Последний символ — завершающий ноль.
		return new string(buffer, 0, chars - 1);
	}

	public static float[] GetDashArray(IntPtr pageObject)
	{
		int count = FPDFPageObj_GetDashCount(pageObject);

		if (count <= 0)
			return Array.Empty<float>();

		var result = new float[count];

		fixed (float* p = result)
		{
			if (!FPDFPageObj_GetDashArray(pageObject, p, (nuint)count))
				return Array.Empty<float>();
		}

		return result;
	}

	#endregion
}
