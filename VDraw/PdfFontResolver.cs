using System.Text;

namespace VDraw.Documents;

public enum PdfFontSource
{
	/// <summary>Шрифт взят из системы.</summary>
	System,

	/// <summary>Программа шрифта извлечена из PDF и зарегистрирована в процессе.</summary>
	Embedded,

	/// <summary>Ни того, ни другого: текст рисуется чужой гарнитурой.</summary>
	Substituted
}

/// <summary>Одна гарнитура, встреченная при разборе страниц.</summary>
public sealed class PdfFontUsage
{
	/// <summary>Имя как оно записано в PDF, вместе с префиксом подмножества.</summary>
	public string PdfName { get; init; } = string.Empty;

	/// <summary>Имя семейства без префикса и суффиксов начертания.</summary>
	public string Family { get; init; } = string.Empty;

	/// <summary>Гарнитура, которой текст будет нарисован на самом деле.</summary>
	public string ResolvedFamily { get; set; } = string.Empty;

	public bool Bold { get; init; }
	public bool Italic { get; init; }

	public bool IsEmbedded { get; init; }

	/// <summary>
	/// Префикс из шести заглавных букв и плюса означает, что в файл
	/// попали только использованные глифы.
	/// </summary>
	public bool IsSubset { get; init; }

	public bool SystemAvailable { get; init; }

	public PdfFontSource Source { get; set; } = PdfFontSource.Substituted;

	/// <summary>Размер извлечённой программы шрифта в байтах.</summary>
	public int DataSize { get; set; }

	/// <summary>Сколько текстовых объектов им набрано.</summary>
	public int UseCount { get; set; }

	public string? Note { get; set; }
}

/// <summary>
/// Итог по шрифтам импортированного документа. Отдаётся вызывающему
/// вместе с фигурами: решение, показывать ли оповещение, принимает
/// интерфейс, а не импортёр.
/// </summary>
public sealed class PdfFontReport
{
	public List<PdfFontUsage> Fonts { get; } = new();

	/// <summary>Шрифты, которых нет ни в системе, ни в самом файле.</summary>
	public IEnumerable<PdfFontUsage> Missing =>
		Fonts.Where(f => f.Source == PdfFontSource.Substituted);

	/// <summary>Шрифты, поднятые из файла.</summary>
	public IEnumerable<PdfFontUsage> Embedded =>
		Fonts.Where(f => f.Source == PdfFontSource.Embedded);

	public bool HasMissing => Missing.Any();

	/// <summary>
	/// Текст оповещения. Пустая строка означает, что показывать нечего:
	/// все шрифты нашлись, и беспокоить пользователя незачем.
	/// </summary>
	public string BuildMessage()
	{
		var missing = Missing.OrderBy(f => f.Family, StringComparer.CurrentCultureIgnoreCase).ToList();
		var embedded = Embedded.OrderBy(f => f.Family, StringComparer.CurrentCultureIgnoreCase).ToList();

		if (missing.Count == 0 && embedded.Count == 0)
			return string.Empty;

		var sb = new StringBuilder();

		if (missing.Count > 0)
		{
			sb.AppendLine(missing.Count == 1
				? "Шрифт отсутствует в системе и не встроен в файл:"
				: $"Шрифтов отсутствует в системе и не встроено в файл: {missing.Count}");

			sb.AppendLine();

			foreach (var font in missing)
			{
				sb.Append("    • ");
				sb.Append(Describe(font));
				sb.Append(" → ");
				sb.AppendLine(font.ResolvedFamily);
			}

			sb.AppendLine();
			sb.AppendLine("Текст импортирован полностью, но начертание и ширина строк отличаются от исходных.");
		}

		if (embedded.Count > 0)
		{
			if (sb.Length > 0)
				sb.AppendLine();

			sb.AppendLine(embedded.Count == 1
				? "Шрифт взят из самого файла:"
				: $"Шрифтов взято из самого файла: {embedded.Count}");

			sb.AppendLine();

			foreach (var font in embedded)
			{
				sb.Append("    • ");
				sb.Append(Describe(font));

				if (font.IsSubset)
					sb.Append("  (только использованные символы)");

				sb.AppendLine();
			}

			if (embedded.Any(f => f.IsSubset))
			{
				sb.AppendLine();
				sb.AppendLine(
					"Шрифты помечены как подмножества: в файл попали только те символы, " +
					"что встречались на страницах. Существующий текст отображается верно, " +
					"но при наборе новых символов появятся пустые прямоугольники.");
			}
		}

		return sb.ToString().TrimEnd();
	}

	private static string Describe(PdfFontUsage font)
	{
		string style = (font.Bold, font.Italic) switch
		{
			(true, true) => " Bold Italic",
			(true, false) => " Bold",
			(false, true) => " Italic",
			_ => string.Empty
		};

		return font.Family + style;
	}
}

/// <summary>
/// Сопоставление шрифтов PDF гарнитурам, которыми текст будет нарисован.
///
/// Порядок предпочтений: системный шрифт, затем встроенный, затем
/// подстановка. Системный выше встроенного намеренно — встроенный почти
/// всегда подмножество, и для документа, который собираются РЕДАКТИРОВАТЬ,
/// полный системный шрифт того же семейства полезнее урезанного точного.
/// Если же семейства в системе нет, встроенный становится единственным
/// способом показать страницу такой, какой её задумали.
///
/// Живёт один экземпляр на импорт: ключом служит указатель FPDF_FONT,
/// а он действителен только пока открыт документ.
/// </summary>
internal sealed class PdfFontResolver
{
	private const int FlagItalic = 1 << 6;
	private const int FlagForceBold = 1 << 18;

	private readonly Dictionary<IntPtr, PdfFontUsage> _byHandle = new();
	private readonly Dictionary<string, PdfFontUsage> _byKey = new(StringComparer.OrdinalIgnoreCase);

	private readonly PdfFontReport _report;
	private readonly bool _useEmbedded;

	private HashSet<string>? _systemFamilies;

	public PdfFontResolver(PdfFontReport report, bool useEmbedded)
	{
		_report = report;
		_useEmbedded = useEmbedded;
	}

	/// <summary>
	/// Разбирает шрифт текстового объекта и возвращает запись о нём.
	/// Повторные вызовы для того же шрифта бесплатны: программа шрифта
	/// извлекается и регистрируется один раз.
	/// </summary>
	public PdfFontUsage Resolve(IntPtr font)
	{
		if (_byHandle.TryGetValue(font, out var cached))
		{
			cached.UseCount++;
			return cached;
		}

		string pdfName = PdfNative.GetBaseFontName(font);
		int flags = PdfNative.GetFontFlags(font);
		int weight = PdfNative.GetFontWeight(font);

		bool isSubset = HasSubsetPrefix(pdfName);
		string stripped = isSubset ? pdfName[7..] : pdfName;

		string family = CleanFamilyName(stripped, out bool nameBold, out bool nameItalic);

		bool bold = nameBold || weight >= 600 || (flags & FlagForceBold) != 0;
		bool italic = nameItalic || (flags & FlagItalic) != 0;

		// Ключ по имени и начертанию, а не по указателю: один и тот же
		// шрифт на разных страницах — это разные FPDF_FONT, и без такого
		// ключа отчёт распух бы до сотни одинаковых строк.
		string key = $"{family}|{bold}|{italic}";

		if (_byKey.TryGetValue(key, out var existing))
		{
			existing.UseCount++;
			_byHandle[font] = existing;
			return existing;
		}

		var usage = new PdfFontUsage
		{
			PdfName = pdfName,
			Family = family,
			Bold = bold,
			Italic = italic,
			IsSubset = isSubset,
			IsEmbedded = PdfNative.IsFontEmbedded(font),
			SystemAvailable = IsSystemFamily(family),
			UseCount = 1
		};

		Decide(usage, font);

		_byHandle[font] = usage;
		_byKey[key] = usage;
		_report.Fonts.Add(usage);

		return usage;
	}

	private void Decide(PdfFontUsage usage, IntPtr font)
	{
		if (usage.SystemAvailable)
		{
			usage.Source = PdfFontSource.System;
			usage.ResolvedFamily = usage.Family;

			if (usage.IsEmbedded)
				usage.Note = "Встроенная копия не понадобилась: семейство есть в системе.";

			return;
		}

		if (_useEmbedded && usage.IsEmbedded)
		{
			byte[]? data = PdfNative.GetFontData(font);

			if (data != null && EmbeddedFontRegistry.Register(usage.Family, usage.Bold, usage.Italic, data))
			{
				usage.Source = PdfFontSource.Embedded;
				usage.ResolvedFamily = usage.Family;
				usage.DataSize = data.Length;

				return;
			}

			usage.Note = data == null
				? "Программа шрифта из файла не читается."
				: "Формат встроенного шрифта Skia не поддерживает (Type 1 или голый CFF).";
		}

		usage.Source = PdfFontSource.Substituted;
		usage.ResolvedFamily = PickSubstitute(usage);
	}

	/// <summary>
	/// Подстановка по признакам из FontDescriptor, а не «всё в Arial»:
	/// засечки и моноширинность заметны сильнее, чем конкретная гарнитура.
	/// </summary>
	private string PickSubstitute(PdfFontUsage usage)
	{
		string lower = usage.Family.ToLowerInvariant();

		bool serif =
			lower.Contains("times") ||
			lower.Contains("georgia") ||
			lower.Contains("garamond") ||
			lower.Contains("book") ||
			lower.Contains("minion") ||
			lower.Contains("serif") && !lower.Contains("sans");

		bool mono =
			lower.Contains("courier") ||
			lower.Contains("mono") ||
			lower.Contains("consol");

		string candidate = mono
			? "Consolas"
			: serif
				? "Times New Roman"
				: "Arial";

		return IsSystemFamily(candidate) ? candidate : "Arial";
	}

	private bool IsSystemFamily(string family)
	{
		if (string.IsNullOrWhiteSpace(family))
			return false;

		_systemFamilies ??= new HashSet<string>(
			SystemFontCatalog.Families, StringComparer.OrdinalIgnoreCase);

		return _systemFamilies.Contains(family);
	}

	#region Разбор имени

	/// <summary>Шесть заглавных букв и плюс — признак подмножества по спецификации.</summary>
	private static bool HasSubsetPrefix(string name)
	{
		if (name.Length < 8 || name[6] != '+')
			return false;

		for (int i = 0; i < 6; i++)
		{
			if (name[i] < 'A' || name[i] > 'Z')
				return false;
		}

		return true;
	}

	/// <summary>
	/// «Arial-BoldMT» → «Arial» плюс признак жирного. Имена базовых
	/// четырнадцати шрифтов приводятся к системным аналогам: Helvetica
	/// в Windows нет, и без сопоставления весь текст ушёл бы
	/// в отсутствующие.
	/// </summary>
	private static string CleanFamilyName(string name, out bool bold, out bool italic)
	{
		string lower = name.ToLowerInvariant();

		bold = lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy") || lower.Contains("semibold");
		italic = lower.Contains("italic") || lower.Contains("oblique");

		int dash = name.IndexOf('-');

		if (dash > 0)
			name = name[..dash];

		if (name.Length == 0)
			return "Arial";

		return name switch
		{
			"ArialMT" or "Arial" or "Helvetica" or "ArialUnicodeMS" => "Arial",
			"TimesNewRomanPSMT" or "TimesNewRomanPS" or "TimesNewRoman" or "Times" => "Times New Roman",
			"CourierNewPSMT" or "CourierNew" or "Courier" => "Courier New",
			"SymbolMT" => "Symbol",
			"ZapfDingbats" => "Wingdings",
			_ => name
		};
	}

	#endregion
}
