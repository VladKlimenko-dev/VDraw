using System.Reflection;

namespace VDraw;

/// <summary>
/// Версия и сборка приложения.
///
/// Отдельный класс, а не чтение атрибутов по месту: сведения нужны
/// в нескольких местах — в записываемом файле, в окне «О программе»,
/// в журнале ошибок, — и посчитанные заново в каждом они рано или
/// поздно разойдутся по формату.
///
/// Значения читаются один раз и запоминаются: сборка за время работы
/// приложения не меняется, а отражение стоит заметно дороже обращения
/// к полю.
/// </summary>
public static class AppInfo
{
	private static string? _version;
	private static string? _build;

	/// <summary>
	/// Версия в виде "1.4.2.0". Пустая строка, если атрибут недоступен —
	/// например у сборки, собранной без указания версии.
	/// </summary>
	public static string Version => _version ??= ReadVersion();

	/// <summary>
	/// Время сборки исполняемого файла в виде "2026-09-10 14:03",
	/// по местному времени машины, где файл читают.
	///
	/// Версия внутри одного релиза не меняется, а сборок за день бывает
	/// несколько, и отличить их можно только по времени.
	/// </summary>
	public static string Build => _build ??= ReadBuild();

	/// <summary>Строка "1.4.2.0 (2026-09-10 14:03)" для показа человеку.</summary>
	public static string VersionWithBuild
	{
		get
		{
			if (Version.Length == 0)
				return Build;

			return Build.Length == 0 ? Version : $"{Version} ({Build})";
		}
	}

	private static string ReadVersion()
	{
		var assembly = Assembly.GetExecutingAssembly();

		// Информационная версия первой: в неё попадает то, что задано
		// в свойствах проекта, включая суффиксы вроде "-beta". Числовая
		// версия сборки суффиксы теряет.
		string? informational = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;

		if (!string.IsNullOrWhiteSpace(informational))
		{
			// Сборщик дописывает к информационной версии хеш коммита
			// через плюс. Для показа он лишний.
			int plus = informational.IndexOf('+');
			return plus >= 0 ? informational[..plus] : informational;
		}

		return assembly.GetName().Version?.ToString() ?? string.Empty;
	}

	private static string ReadBuild()
	{
		try
		{
			string path = Assembly.GetExecutingAssembly().Location;

			// У сборки, упакованной в один файл, Location пуст: читать
			// время сборки неоткуда, и это не повод падать.
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return string.Empty;

			return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
		}
		catch (IOException)
		{
			return string.Empty;
		}
		catch (UnauthorizedAccessException)
		{
			return string.Empty;
		}
	}
}
