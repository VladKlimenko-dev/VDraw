using SkiaSharp;

namespace VDraw;

/// <summary>
/// Гарнитуры, зарегистрированные из байтов, а не взятые из системы.
///
/// Сюда попадают шрифты, встроенные в импортированный PDF. Установка их
/// в систему была бы неверной по двум причинам. Во-первых, в PDF почти
/// всегда лежит ПОДМНОЖЕСТВО шрифта: только те глифы, что встретились на
/// страницах. Установленный в систему, он появится в списке гарнитур и
/// на любом непредусмотренном символе выдаст пустой прямоугольник.
/// Во-вторых, право на встраивание (поле fsType в самом шрифте) и право
/// на установку — разные вещи, и просмотрщики PDF намеренно ограничиваются
/// первым.
///
/// Поэтому шрифт живёт в процессе, а на диск попадает только в частный
/// кэш приложения, чтобы текст выглядел так же после перезапуска.
/// </summary>
public static class EmbeddedFontRegistry
{
	private readonly record struct Key(string Family, bool Bold, bool Italic);

	private static readonly Dictionary<Key, SKTypeface> _typefaces = new();

	private static readonly Dictionary<string, byte[]> _data =
		new(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> _families =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Частный кэш приложения. Не системная папка шрифтов.</summary>
	public static string CacheFolder =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"VDraw", "EmbeddedFonts");

	/// <summary>Семейства, доступные только через этот реестр.</summary>
	public static IReadOnlyCollection<string> Families => _families;

	public static bool Contains(string? family) =>
		!string.IsNullOrWhiteSpace(family) && _families.Contains(family);

	#region Регистрация

	/// <summary>
	/// Регистрирует программу шрифта. Возвращает false, если формат
	/// не поддерживается Skia — тогда вызывающий обязан подставить
	/// системный шрифт, а не считать, что всё получилось.
	/// </summary>
	public static bool Register(string family, bool bold, bool italic, byte[] data)
	{
		if (string.IsNullOrWhiteSpace(family) || data == null || data.Length < 16)
			return false;

		var key = new Key(family, bold, italic);

		if (_typefaces.ContainsKey(key))
			return true;

		if (!IsSupportedFormat(data))
			return false;

		var typeface = CreateTypeface(data);

		if (typeface == null)
			return false;

		_typefaces[key] = typeface;
		_families.Add(family);

		string dataKey = $"{family}##{(bold ? 'b' : '-')}{(italic ? 'i' : '-')}";
		_data[dataKey] = data;

		return true;
	}

	/// <summary>
	/// Skia принимает TrueType и OpenType. Type 1 (PFB) и голый CFF без
	/// оболочки OpenType она не читает, а исключение из FromStream
	/// приходит не всегда: часть сборок возвращает null, часть —
	/// нерабочую гарнитуру, у которой нет ни одного глифа. Проверка по
	/// сигнатуре надёжнее.
	/// </summary>
	public static bool IsSupportedFormat(byte[] data)
	{
		if (data.Length < 4)
			return false;

		uint tag = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];

		return tag switch
		{
			0x00010000 => true,        // TrueType
			0x74727565 => true,        // 'true'
			0x74746366 => true,        // 'ttcf', коллекция
			0x4F54544F => true,        // 'OTTO', OpenType с контурами CFF
			_ => false                 // '%!PS', 0x8001 (Type 1), 'wOFF', голый CFF
		};
	}

	private static SKTypeface? CreateTypeface(byte[] data)
	{
		// Порядок вызовов — от более нового API к более старому:
		// сигнатуры SKTypeface менялись между мажорными версиями
		// SkiaSharp, и жёсткая привязка к одной ломала бы обновление.
		try
		{
			using var stream = new SKMemoryStream(data);

			var typeface = SKFontManager.Default.CreateTypeface(stream);

			if (typeface != null)
				return typeface;
		}
		catch
		{
		}

		try
		{
			using var stream = new MemoryStream(data, writable: false);

			return SKTypeface.FromStream(stream);
		}
		catch
		{
			return null;
		}
	}

	#endregion

	#region Поиск

	/// <summary>
	/// Гарнитура для семейства и начертания. Точное совпадение по
	/// начертанию, затем любое: PDF нередко встраивает только прямое
	/// начертание, а курсив рисует наклоном матрицы.
	/// </summary>
	public static SKTypeface? Resolve(string? family, bool bold, bool italic)
	{
		if (string.IsNullOrWhiteSpace(family) || !_families.Contains(family))
			return null;

		if (_typefaces.TryGetValue(new Key(family, bold, italic), out var exact))
			return exact;

		foreach (var pair in _typefaces)
		{
			if (string.Equals(pair.Key.Family, family, StringComparison.OrdinalIgnoreCase))
				return pair.Value;
		}

		return null;
	}

	#endregion

	#region Кэш на диске

	/// <summary>
	/// Сохраняет зарегистрированные шрифты в частный кэш. Без этого
	/// документ, сохранённый после импорта, при следующем открытии
	/// покажет текст подставленным шрифтом: сам .vdr программы шрифтов
	/// не хранит.
	/// </summary>
	public static void SaveCache()
	{
		try
		{
			Directory.CreateDirectory(CacheFolder);

			foreach (var pair in _data)
			{
				string path = Path.Combine(CacheFolder, SafeFileName(pair.Key) + ".fnt");

				if (!File.Exists(path))
					File.WriteAllBytes(path, pair.Value);
			}
		}
		catch
		{
			// Кэш — удобство, а не условие работы.
		}
	}

	/// <summary>Вызывается один раз при запуске приложения.</summary>
	public static void LoadCache()
	{
		try
		{
			if (!Directory.Exists(CacheFolder))
				return;

			foreach (string path in Directory.EnumerateFiles(CacheFolder, "*.fnt"))
			{
				string name = Path.GetFileNameWithoutExtension(path);

				if (!TryParseFileName(name, out string family, out bool bold, out bool italic))
					continue;

				Register(family, bold, italic, File.ReadAllBytes(path));
			}
		}
		catch
		{
		}
	}

	public static void ClearCache()
	{
		try
		{
			if (Directory.Exists(CacheFolder))
				Directory.Delete(CacheFolder, recursive: true);
		}
		catch
		{
		}
	}

	private static string SafeFileName(string key)
	{
		var chars = key.ToCharArray();

		for (int i = 0; i < chars.Length; i++)
		{
			if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0)
				chars[i] = '_';
		}

		return new string(chars);
	}

	private static bool TryParseFileName(string name, out string family, out bool bold, out bool italic)
	{
		family = string.Empty;
		bold = false;
		italic = false;

		// Разделитель «##» выбран из символов, допустимых в имени файла:
		// вертикальную черту SafeFileName заменила бы подчёркиванием,
		// и разобрать имя обратно стало бы нечем.
		int sep = name.LastIndexOf("##", StringComparison.Ordinal);

		if (sep <= 0 || name.Length - sep != 4)
			return false;

		family = name[..sep];
		bold = name[sep + 2] == 'b';
		italic = name[sep + 3] == 'i';

		return family.Length > 0;
	}

	#endregion
}
