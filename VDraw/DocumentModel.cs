using SkiaSharp;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

#region Конвертеры типов Skia

/// <summary>
/// Преобразователи структур Skia для System.Text.Json.
///
/// Скалярные структуры пишутся строкой, а не объектом: файл остаётся
/// читаемым глазами, а места занимает втрое меньше — узлов кривых в
/// документе тысячи, и каждый несёт три точки.
///
/// Формат чисел жёстко инвариантный. Без этого документ, сохранённый
/// в локали с запятой в роли разделителя, не открылся бы в другой.
/// </summary>
internal static class SkJson
{
	public static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

	public static float P(string? s)
		=> float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

	public static string[] Split(string? s, int expected)
	{
		var parts = (s ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (parts.Length >= expected)
			return parts;

		// Недостающие поля добиваются нулями: испорченный или укороченный
		// файл должен открыться с потерей одного значения, а не исключением.
		var padded = new string[expected];

		for (int i = 0; i < expected; i++)
			padded[i] = i < parts.Length ? parts[i] : "0";

		return padded;
	}
}

internal sealed class SKPointJsonConverter : JsonConverter<SKPoint>
{
	public override SKPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		var p = SkJson.Split(reader.GetString(), 2);
		return new SKPoint(SkJson.P(p[0]), SkJson.P(p[1]));
	}

	public override void Write(Utf8JsonWriter writer, SKPoint value, JsonSerializerOptions options)
		=> writer.WriteStringValue($"{SkJson.F(value.X)} {SkJson.F(value.Y)}");
}

internal sealed class SKSizeJsonConverter : JsonConverter<SKSize>
{
	public override SKSize Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		var p = SkJson.Split(reader.GetString(), 2);
		return new SKSize(SkJson.P(p[0]), SkJson.P(p[1]));
	}

	public override void Write(Utf8JsonWriter writer, SKSize value, JsonSerializerOptions options)
		=> writer.WriteStringValue($"{SkJson.F(value.Width)} {SkJson.F(value.Height)}");
}

internal sealed class SKRectJsonConverter : JsonConverter<SKRect>
{
	public override SKRect Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		var p = SkJson.Split(reader.GetString(), 4);
		return new SKRect(SkJson.P(p[0]), SkJson.P(p[1]), SkJson.P(p[2]), SkJson.P(p[3]));
	}

	public override void Write(Utf8JsonWriter writer, SKRect value, JsonSerializerOptions options)
		=> writer.WriteStringValue(
			$"{SkJson.F(value.Left)} {SkJson.F(value.Top)} {SkJson.F(value.Right)} {SkJson.F(value.Bottom)}");
}

/// <summary>
/// Матрица пишется всеми девятью полями, включая перспективные.
/// Они всегда 0,0,1 у аффинных преобразований, но додумывать их при чтении
/// значило бы молча чинить файл, записанный будущей версией.
/// </summary>
internal sealed class SKMatrixJsonConverter : JsonConverter<SKMatrix>
{
	public override SKMatrix Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		var p = SkJson.Split(reader.GetString(), 9);

		return new SKMatrix
		{
			ScaleX = SkJson.P(p[0]),
			SkewX = SkJson.P(p[1]),
			TransX = SkJson.P(p[2]),
			SkewY = SkJson.P(p[3]),
			ScaleY = SkJson.P(p[4]),
			TransY = SkJson.P(p[5]),
			Persp0 = SkJson.P(p[6]),
			Persp1 = SkJson.P(p[7]),
			Persp2 = SkJson.P(p[8])
		};
	}

	public override void Write(Utf8JsonWriter writer, SKMatrix value, JsonSerializerOptions options)
		=> writer.WriteStringValue(string.Join(' ', new[]
		{
			SkJson.F(value.ScaleX), SkJson.F(value.SkewX), SkJson.F(value.TransX),
			SkJson.F(value.SkewY), SkJson.F(value.ScaleY), SkJson.F(value.TransY),
			SkJson.F(value.Persp0), SkJson.F(value.Persp1), SkJson.F(value.Persp2)
		}));
}

internal sealed class SKColorJsonConverter : JsonConverter<SKColor>
{
	public override SKColor Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
		=> Geometry.TryParseHexColor(reader.GetString() ?? string.Empty, out var c) ? c : SKColors.Black;

	public override void Write(Utf8JsonWriter writer, SKColor value, JsonSerializerOptions options)
		=> writer.WriteStringValue(Geometry.ColorToHex(value));
}

/// <summary>
/// SKColorF хранится четырьмя вещественными числами, а не как SKColor:
/// перевод в байты потерял бы точность цвета фона страницы при каждом
/// круге сохранения и загрузки.
/// </summary>
internal sealed class SKColorFJsonConverter : JsonConverter<SKColorF>
{
	public override SKColorF Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		var p = SkJson.Split(reader.GetString(), 4);
		return new SKColorF(SkJson.P(p[0]), SkJson.P(p[1]), SkJson.P(p[2]), SkJson.P(p[3]));
	}

	public override void Write(Utf8JsonWriter writer, SKColorF value, JsonSerializerOptions options)
		=> writer.WriteStringValue(
			$"{SkJson.F(value.Red)} {SkJson.F(value.Green)} {SkJson.F(value.Blue)} {SkJson.F(value.Alpha)}");
}

#endregion

#region Модель файла

/// <summary>
/// Корень файла .vdr.
///
/// Version отделён от содержимого намеренно: он читается первым и позволяет
/// понять, что делать с файлом, не разбирая остального. Версия 1 — старый
/// формат, где сохранялись только настройки листа, без страниц и фигур.
/// </summary>
public sealed class DocumentFileDto
{
	/// <summary>
	/// Текущая версия формата.
	///
	/// Версия 1 — только настройки листа, без страниц и фигур.
	/// Версия 2 — страницы, фигуры, встроенные растры.
	/// Версия 3 — толщина обводки и длина цикла узора переехали
	/// из миллиметров в единицы страницы; см. StrokeDto.
	/// Версия 4 — привязка концов измерений к точке НА поверхности
	/// фигуры, в её локальных координатах; см. AnchorDto.Local.
	///
	/// Число увеличивается только тогда, когда меняется СМЫСЛ уже
	/// существующих полей или появляется поле, без которого файл
	/// читается неправильно. Добавление поля, отсутствие которого
	/// безопасно, версию не двигает: иначе номер рос бы на каждую
	/// мелочь и перестал что-либо значить.
	/// </summary>
	public const int CurrentVersion = 4;

	public int Version { get; set; } = CurrentVersion;

	/// <summary>
	/// Версия приложения, записавшего файл, в виде "1.4.2.0".
	///
	/// Отдельно от Version. Номер формата отвечает на вопрос «как читать»,
	/// а этот — на вопрос «кто записал». Второе нужно при разборе жалоб:
	/// по нему сразу видно, той ли сборкой сделан файл, на который
	/// ссылается пользователь, и не тянется ли ошибка из давно
	/// исправленной версии.
	///
	/// Пустая строка означает файл, записанный до появления этого поля.
	/// </summary>
	public string AppVersion { get; set; } = string.Empty;

	/// <summary>
	/// Сборка приложения: дата и время сборки исполняемого файла
	/// в формате "2026-09-10 14:03".
	///
	/// Версия внутри одного релиза не меняется, а сборок за день бывает
	/// несколько, и отличить их можно только по времени.
	/// </summary>
	public string AppBuild { get; set; } = string.Empty;

	public string Title { get; set; } = "Untitled";
	public string FileName { get; set; } = "Untitled.vdr";

	public PaperFormatDto? PaperFormat { get; set; }
	public UoMDto? Uom { get; set; }
	public SKSize PaperSizeUom { get; set; }
	public int DPI { get; set; } = 96;
	public PageOrientation DefaultPageOrientation { get; set; } = PageOrientation.Vertical;

	public int ActivePageIndex { get; set; }
	public List<PageDto> Pages { get; set; } = new();

	/// <summary>
	/// Пользовательские паттерны, использованные в документе.
	///
	/// Сами фигуры и без того несут своё оформление целиком: у заливки это
	/// PatternDefinition, у обводки — строка Pattern, и файл рисуется верно
	/// на любой машине. Эти списки нужны для другого — чтобы у получателя
	/// паттерны появились В СПИСКЕ и ими можно было красить новые фигуры,
	/// а не только смотреть на уже готовые.
	///
	/// В Resources получателя они при этом не попадают: см. IsFromDocument.
	/// </summary>
	public List<FillPatternDto> FillPatterns { get; set; } = new();
	public List<StrokePatternDto> StrokePatterns { get; set; } = new();

	/// <summary>
	/// Кастомные фигуры, созданные В ЭТОМ документе.
	///
	/// Едут вместе с файлом по той же причине, что и паттерны выше: у
	/// получателя в Resources\Shapes их нет, и без копии в документе меню
	/// «Custom shapes» у него оказалось бы пустым. В свою папку Resources
	/// они при этом не переносятся — библиотека получателя остаётся его.
	///
	/// Наборы, подгруженные из клипартов Visio, сюда НЕ попадают: они живут
	/// только на время сеанса.
	/// </summary>
	public List<CustomShapeDto> CustomShapes { get; set; } = new();
}

public sealed class FillPatternDto
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public int Width { get; set; } = 8;
	public int Height { get; set; } = 8;
	public string Definition { get; set; } = string.Empty;
}

public sealed class StrokePatternDto
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Pattern { get; set; } = string.Empty;
	public int Length { get; set; } = 8;
}

public sealed class PaperFormatDto
{
	public string Title { get; set; } = "A4";
	public float WidthMm { get; set; } = 210f;
	public float HeightMm { get; set; } = 297f;
}

public sealed class UoMDto
{
	public string Title { get; set; } = "mm";
	public float Cmm { get; set; } = 1f;
}

public sealed class PageDto
{
	public string Title { get; set; } = "Page 1";

	public PaperFormatDto? PaperFormat { get; set; }
	public UoMDto? Uom { get; set; }
	public SKSize SizeUom { get; set; }
	public int DPI { get; set; } = 96;
	public PageOrientation Orientation { get; set; } = PageOrientation.Vertical;

	public SKColorF BackgroundColor { get; set; }
	public SKColor PageBgColor { get; set; }
	public bool LockRatio { get; set; }
	public bool GridEnabled { get; set; }
	public bool PrintBordersEnabled { get; set; } = true;

	public SKPoint PageOrigin { get; set; }
	public float LockedRatio { get; set; } = 1f;

	public List<ShapeDto> Shapes { get; set; } = new();

	/// <summary>
	/// Ключи анимации страницы. null в файлах, записанных до появления
	/// панели анимации: свойство необязательное, и старые документы
	/// открываются как есть.
	/// </summary>
	public PageAnimationDto? Animation { get; set; }
}

/// <summary>
/// Анимация страницы в файле.
///
/// Дорожки привязаны к фигуре идентификатором, а параметр назван строкой —
/// тем же Id, что выдаёт AnimParamCatalog. Строка, а не индекс: набор
/// параметров зависит от типа фигуры и растёт от версии к версии,
/// и порядковый номер разъехался бы при первом же добавлении параметра.
/// Неизвестный при чтении Id просто игнорируется раскладкой по кадрам.
/// </summary>
public sealed class PageAnimationDto
{
	public int FrameCount { get; set; } = 25;
	public int CurrentFrame { get; set; } = 1;
	public int Fps { get; set; } = 25;

	public List<ShapeAnimationDto> Shapes { get; set; } = new();
}

public sealed class ShapeAnimationDto
{
	public string ShapeId { get; set; } = string.Empty;
	public List<AnimTrackDto> Tracks { get; set; } = new();
}

public sealed class AnimTrackDto
{
	public string ParamId { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;

	/// <summary>Кадры ключей и их значения, по одной строке на ключ: "12=P 10 20".</summary>
	public List<string> Keys { get; set; } = new();
}

public sealed class StrokeDto
{
	public bool Enabled { get; set; } = true;
	public SKColor Color { get; set; }

	/// <summary>Толщина обводки в единицах страницы. Формат 3 и новее.</summary>
	public float Thickness { get; set; }

	public string Pattern { get; set; } = string.Empty;

	/// <summary>Длина цикла узора в единицах страницы. Формат 3 и новее.</summary>
	public float PatternLength { get; set; } = 10f;

	/// <summary>
	/// Толщина в МИЛЛИМЕТРАХ из файлов формата 2 и старше.
	///
	/// Поле оставлено только для чтения: без него старый документ открылся бы
	/// с нулевой толщиной. При записи оно всегда null и в файл не попадает —
	/// сериализатор настроен пропускать null.
	/// </summary>
	public float? ThicknessMm { get; set; }

	/// <summary>Длина цикла узора в МИЛЛИМЕТРАХ из файлов формата 2 и старше.</summary>
	public float? PatternLengthMm { get; set; }
	public string StartGlyph { get; set; } = string.Empty;
	public string EndGlyph { get; set; } = string.Empty;
	public float StartGlyphSize { get; set; } = 3f;
	public float EndGlyphSize { get; set; } = 3f;
}

public sealed class GradientStopDto
{
	public float Offset { get; set; }
	public SKColor Color { get; set; }
}

public sealed class FillDto
{
	public FillKind Kind { get; set; } = FillKind.Solid;
	public bool Enabled { get; set; } = true;
	public SKColor SolidColor { get; set; }

	public bool Tiling { get; set; } = true;
	public bool RotateWithShape { get; set; }
	public bool ScaleWithShape { get; set; }

	public SKColor GradientColor1 { get; set; }
	public SKColor GradientColor2 { get; set; }
	public List<GradientStopDto> Stops { get; set; } = new();
	public string GradientMode { get; set; } = "Linear";
	public float Angle { get; set; }

	public SKColor PatternFrontColor { get; set; }
	public SKColor PatternBackColor { get; set; }
	public int PatternWidth { get; set; } = 8;
	public int PatternHeight { get; set; } = 8;
	public string PatternId { get; set; } = string.Empty;
	public string PatternDefinition { get; set; } = string.Empty;

	public string TextureId { get; set; } = string.Empty;
	public string TextureFilePath { get; set; } = string.Empty;

	/// <summary>
	/// Текстура встроена в файл как PNG в base64.
	///
	/// Путь TextureFilePath сохраняется тоже, но полагаться на него нельзя:
	/// документ уезжает на другую машину, а библиотека текстур остаётся.
	/// </summary>
	public string? TexturePng { get; set; }

	public SKPoint StartPoint { get; set; }
	public SKPoint EndPoint { get; set; }
	public bool FillTransformInitialized { get; set; }
	public bool SmoothTexture { get; set; } = true;
}

public sealed class CurveNodeDto
{
	/// <summary>
	/// Идентификатор узла. На него ссылаются привязки концов,
	/// поэтому он обязан пережить сохранение. Пустой в файле от
	/// старой версии — узел получит новый при чтении.
	/// </summary>
	public string Id { get; set; } = string.Empty;

	public SKPoint Position { get; set; }
	public CurveNodeType Type { get; set; }
	public SKPoint ControlPrev { get; set; } = new(float.NaN, float.NaN);
	public SKPoint ControlNext { get; set; } = new(float.NaN, float.NaN);
	public bool BreakAfter { get; set; }
	public bool NewContour { get; set; }
	public bool CloseAfter { get; set; }
}

/// <summary>
/// Привязка конца. Цель хранится идентификатором, а не ссылкой: JSON
/// не умеет циклы объектов, а привязки их образуют — две фигуры могут
/// смотреть друг на друга. Ссылки восстанавливаются вторым проходом.
/// </summary>
public sealed class AnchorDto
{
	public bool Enabled { get; set; }
	public bool TangentToEdge { get; set; } = true;
	public ShapeAnchorKind Kind { get; set; }
	public int Index { get; set; }
	public string? TargetId { get; set; }

	/// <summary>
	/// Узел цели, к которому привязан конец. Пустой — привязка
	/// к точке габаритов или очертания, там достаточно Index.
	/// </summary>
	public string? NodeId { get; set; }

	/// <summary>
	/// Доли смещения от центра локальных габаритов цели для привязки
	/// вида Surface. Ноль — центр, единица — край.
	///
	/// Поля появились в версии 4 формата. В файлах прежних версий их нет,
	/// но там нет и привязок этого вида, поэтому нули по умолчанию
	/// ничему не мешают: загрузчик до них просто не дойдёт.
	/// </summary>
	public float LocalU { get; set; }

	public float LocalV { get; set; }
}

public sealed class ImageAdjustmentsDto
{
	public float Brightness { get; set; }
	public float Contrast { get; set; }
	public float Exposure { get; set; }
	public float Sharpness { get; set; }
	public float BlackWhite { get; set; }
	public float Shadows { get; set; }
	public float Hue { get; set; }
	public float Temperature { get; set; }
	public float Tint { get; set; }
	public float Vibrancy { get; set; }
	public float NoiseReduction { get; set; }

	/// <summary>
	/// Негатив. Поле появилось позже остальных; в файлах прежних версий
	/// его нет, и false по умолчанию означает «без негатива» — ровно то,
	/// что там и было.
	/// </summary>
	public bool Invert { get; set; }
}

public sealed class ImageDto
{
	public string ImagePath { get; set; } = string.Empty;
	public SKRect ImageLocalRect { get; set; }

	/// <summary>Рабочие пиксели со всеми мазками кистью, PNG в base64.</summary>
	public string? Png { get; set; }

	/// <summary>
	/// Эталонные пиксели до коррекций, PNG в base64. Пишется только когда
	/// отличается от рабочих: у нетронутой картинки это была бы точная
	/// копия и файл раздулся бы вдвое ни за что.
	/// </summary>
	public string? OriginalPng { get; set; }

	/// <summary>
	/// Показ в разрешении документа. Значение по умолчанию читают файлы,
	/// записанные до появления режима.
	/// </summary>
	public bool ActualResolution { get; set; }

	public ImageAdjustmentsDto? Adjustments { get; set; }
}

public sealed class TextDto
{
	public string Text { get; set; } = string.Empty;
	public SKPoint LocalOrigin { get; set; }
	public float FontSizeMm { get; set; } = 5f;
	public string FontFamily { get; set; } = "Arial";
	public bool Bold { get; set; }
	public bool Italic { get; set; }
	public bool Underline { get; set; }
	public bool Strikethrough { get; set; }
	public float CharacterSpacingMm { get; set; }
	public float LineSpacingMm { get; set; }

	public float Ax { get; set; }
	public float Ay { get; set; }
	public bool TangentAlong { get; set; } = true;
	public bool AlongShape { get; set; }

	public bool InsideOtherShape { get; set; }
	public string? InsideShapeId { get; set; }
	public TextAreaHAlign AreaHAlign { get; set; }
	public TextAreaVAlign AreaVAlign { get; set; }
	public bool WrapInsideShape { get; set; }
	public bool WrapOtherShapes { get; set; }
	public bool ClipByShape { get; set; }
	public int Columns { get; set; } = 1;
	public float DistanceBetweenColumns { get; set; }
	public float Padding { get; set; }
}

/// <summary>Размер угла между двумя векторами.</summary>
public sealed class AngleMeasurementDto
{
	public SKPoint S1P1 { get; set; }
	public SKPoint S1P2 { get; set; }
	public SKPoint S2P1 { get; set; }
	public SKPoint S2P2 { get; set; }

	public float Radius { get; set; } = 10f;
	public float TextAngleOffset { get; set; }

	public bool InvertAngle { get; set; }

	public int Decimals { get; set; } = 1;
	public bool ShowUnits { get; set; } = true;
	public string TextOverride { get; set; } = string.Empty;
	public float ArrowSizeFactor { get; set; } = 0.8f;

	public TextDto? TextStyle { get; set; }

	/// <summary>
	/// Привязки четырёх опорных точек. Ровно четыре элемента; отсутствие
	/// массива означает измерение, записанное до появления привязок.
	/// </summary>
	public AnchorDto[]? Anchors { get; set; }
}

public sealed class MeasurementDto
{
	public SKPoint P1 { get; set; }
	public SKPoint P2 { get; set; }
	public float H { get; set; }
	public float TextOffset { get; set; }
	public int Decimals { get; set; } = 2;
	public bool ShowUnits { get; set; } = true;
	public string TextOverride { get; set; } = string.Empty;
	public float ArrowSizeFactor { get; set; } = 0.8f;
	public TextDto? TextStyle { get; set; }
}

public sealed class GuideDto
{
	public GuideOrientation Orientation { get; set; }
	public GuideTransformMode TransformMode { get; set; }
	public SKPoint GuidePoint { get; set; }
	public float AngleDeg { get; set; }
}

/// <summary>
/// Фигура любого типа. Плоская запись с дискриминатором Kind вместо
/// иерархии наследников: System.Text.Json без своего конвертера не умеет
/// полиморфизм, а разбирать один известный набор полей проще и надёжнее,
/// чем поддерживать конвертер на каждый класс фигуры.
///
/// Неприменимые к типу поля не пишутся — они объявлены как nullable,
/// а сериализатор настроен пропускать null.
/// </summary>
/// <summary>
/// Инструмент пошагового преобразования. Ссылки на привязанные фигуры
/// хранятся идентификаторами и расставляются вторым проходом чтения:
/// цель может стоять в файле позже ссылки на неё.
/// </summary>
public sealed class TransformToolDto
{
	public string? ShapeAId { get; set; }
	public string? ShapeBId { get; set; }
	public int Steps { get; set; }
	public float Bias { get; set; }
	public SKPoint P1 { get; set; }
	public SKPoint P2 { get; set; }
	public bool PointsInitialized { get; set; }
}

/// <summary>Тень или свечение фигуры.</summary>
public sealed class ShadowDto
{
	public bool Enabled { get; set; }
	public SKColor Color { get; set; }
	public float Softness { get; set; }
	public SKPoint CenterOffset { get; set; }
	public SKPoint SizeVector { get; set; }
	public bool OffsetsInitialized { get; set; }
}

/// <summary>Плавная прозрачность фигуры.</summary>
public sealed class TransparencyDto
{
	public bool Enabled { get; set; }
	public float StartOpacity { get; set; }
	public float EndOpacity { get; set; }
	public SKPoint StartOffset { get; set; }
	public SKPoint EndOffset { get; set; }
	public bool OffsetsInitialized { get; set; }
}

public sealed class ShapeDto
{
	public string Kind { get; set; } = string.Empty;
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int Layer { get; set; }
	public bool IsVisible { get; set; } = true;
	public bool IsLocked { get; set; }
	public SKMatrix LocalTransform { get; set; }

	public StrokeDto? Stroke { get; set; }
	public FillDto? Fill { get; set; }

	// --- эффекты ---
	// null означает «эффекта нет»: выключенный по умолчанию объект есть
	// у каждой фигуры, и писать его в файл для всех было бы напрасным
	// раздуванием документа. Файлы прежних версий читаются как есть —
	// отсутствие узла даёт выключенный эффект.
	public ShadowDto? Shadow { get; set; }
	public TransparencyDto? Transparency { get; set; }

	/// <summary>Ссылка Shape.otherShape — текст вдоль контура.</summary>
	public string? OtherShapeId { get; set; }

	// --- прямоугольники ---
	public SKRect? Rect { get; set; }
	public float? RadiusX { get; set; }
	public float? RadiusY { get; set; }
	public float? ChamferX { get; set; }
	public float? ChamferY { get; set; }

	// --- окружности и эллипсы ---
	public SKPoint? LocalCenter { get; set; }
	public float? Radius { get; set; }
	public SKPoint? CenterPoint { get; set; }
	public SKPoint? RadiusPoint { get; set; }
	public SKPoint? RadiusPoint1 { get; set; }
	public SKPoint? RadiusPoint2 { get; set; }
	public SKPoint? P1 { get; set; }
	public SKPoint? P2 { get; set; }
	public SKPoint? P3 { get; set; }

	// --- многоугольник и эллипс по пяти точкам ---
	public float? RotationRad { get; set; }
	public int? Sides { get; set; }
	public bool? UseExplicitPoints { get; set; }
	public List<SKPoint>? Points { get; set; }

	// --- кривая ---
	public List<CurveNodeDto>? Nodes { get; set; }
	public SKPathFillType? FillType { get; set; }
	public float? AnchorStubFactor { get; set; }
	public AnchorDto? FirstAnchor { get; set; }
	public AnchorDto? LastAnchor { get; set; }

	// --- специализация по типу ---
	public ImageDto? Image { get; set; }
	public TextDto? Text { get; set; }
	public MeasurementDto? Measurement { get; set; }
	public AngleMeasurementDto? AngleMeasurement { get; set; }
	public TransformToolDto? Transform { get; set; }
	public GuideDto? Guide { get; set; }
	public List<ShapeDto>? Children { get; set; }
}

#endregion

/// <summary>
/// Настройки сериализатора документа. Вынесены отдельно, потому что нужны
/// и записи, и чтению, и импорту — и должны совпадать во всех трёх местах.
/// </summary>
public static class DocumentJson
{
	public static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			IncludeFields = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		options.Converters.Add(new JsonStringEnumConverter());
		options.Converters.Add(new SKPointJsonConverter());
		options.Converters.Add(new SKSizeJsonConverter());
		options.Converters.Add(new SKRectJsonConverter());
		options.Converters.Add(new SKMatrixJsonConverter());
		options.Converters.Add(new SKColorJsonConverter());
		options.Converters.Add(new SKColorFJsonConverter());

		return options;
	}
}