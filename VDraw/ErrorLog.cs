using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

using VDraw.Documents;

namespace VDraw;

/// <summary>
/// Журнал ошибок.
///
/// Пишется в папку пользователя, а не рядом с программой: каталог
/// Program Files доступен только на чтение, и запись туда упала бы ровно
/// в тот момент, когда журнал нужнее всего. Соседство с документами
/// вдобавок избавляет от объяснений, где искать файл: рядом с работой.
///
/// Формат — обычный текст, а не JSON или XML. Файл читает человек,
/// а не программа, и разбирать его будут в блокноте, пересылая куском
/// в переписке.
///
/// Пишется НЕ только исключение. Само по себе оно почти никогда не
/// объясняет, что случилось: одна и та же строка «Object reference not
/// set» приходит из десятка мест. Поэтому рядом ложится обстановка —
/// версия, система, память, открытый документ, активный инструмент.
/// </summary>
public static class ErrorLog
{
	/// <summary>Файл журнала рядом с папкой документов пользователя.</summary>
	public static string FilePath =>
		Path.Combine(Document.DefaultDocumentsFolder, "Logs", "vdraw-errors.log");

	/// <summary>
	/// Предел размера файла.
	///
	/// Журнал не должен расти без конца: у пользователя, который месяцами
	/// ловит одну и ту же ошибку, он иначе дорастёт до сотен мегабайт.
	/// При переполнении старый файл откладывается в .1, а новый начинается
	/// с чистого листа — так последние события всегда под рукой, а предыдущий
	/// сеанс не пропадает совсем.
	/// </summary>
	private const long MaxSizeBytes = 2 * 1024 * 1024;

	private static readonly object _lock = new();

	private static bool _hooked;

	/// <summary>
	/// Ставит перехватчики на все три источника необработанных исключений.
	///
	/// Их именно три, и каждый ловит своё:
	///   ThreadException      — исключения в обработчиках Windows Forms,
	///   UnhandledException   — всё остальное, включая фоновые потоки,
	///   UnobservedTaskException — задачи, чей результат никто не спросил.
	///
	/// Без первого пользователь увидит стандартное окно .NET и продолжит
	/// работу, а мы не узнаем ничего. Без второго программа просто исчезнет
	/// с экрана.
	/// </summary>
	public static void Install()
	{
		if (_hooked)
			return;

		_hooked = true;

		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

		Application.ThreadException += (_, e) =>
		{
			Write("Ошибка в потоке интерфейса", e.Exception, fatal: false);
			Notify(e.Exception);
		};

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			Write(
				e.IsTerminating ? "Неперехваченная ошибка, работа прекращается" : "Неперехваченная ошибка",
				e.ExceptionObject as Exception,
				fatal: e.IsTerminating);

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Write("Ошибка в фоновой задаче", e.Exception, fatal: false);

			// Иначе задача утащит процесс за собой при сборке мусора.
			e.SetObserved();
		};

		FirstChanceLogging = false;

		WriteSessionHeader();
	}

	/// <summary>
	/// Писать ли ВСЕ исключения, включая перехваченные.
	///
	/// По умолчанию выключено: программа ловит и глушит десятки исключений
	/// штатно — не открылся буфер обмена, не прочиталась чужая картинка, —
	/// и в обычной работе они только зашумят журнал. Включается на время
	/// разбора, когда ошибка проглатывается где-то по дороге и до
	/// перехватчиков не доходит.
	/// </summary>
	public static bool FirstChanceLogging
	{
		get => _firstChance;
		set
		{
			if (_firstChance == value)
				return;

			_firstChance = value;

			if (value)
				AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
			else
				AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
		}
	}

	private static bool _firstChance;

	private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
		=> Write("Перехваченное исключение", e.Exception, fatal: false, includeContext: false);

	/// <summary>
	/// Записывает ошибку с обстановкой.
	///
	/// Сам метод исключений не выпускает ни при каких условиях: журнал,
	/// падающий при записи об ошибке, превращает разбираемую неполадку
	/// в неразбираемую.
	/// </summary>
	public static void Write(string title, Exception? error, bool fatal = false, bool includeContext = true)
	{
		try
		{
			var text = new StringBuilder();

			text.AppendLine();
			text.AppendLine(new string('-', 72));
			text.Append(Timestamp()).Append("  ").Append(fatal ? "СБОЙ" : "ОШИБКА").Append("  ").AppendLine(title);
			text.AppendLine(new string('-', 72));

			if (error != null)
				AppendException(text, error, depth: 0);
			else
				text.AppendLine("Объект исключения не передан.");

			if (includeContext)
				AppendContext(text);

			Append(text.ToString());
		}
		catch
		{
			// Писать некуда — молчим. Показать это пользователю нечем:
			// окно с сообщением поверх уже случившегося сбоя только
			// собьёт его с толку.
		}
	}

	/// <summary>Запись без исключения — для мест, где важен сам факт.</summary>
	public static void Note(string message)
	{
		try
		{
			Append($"{Timestamp()}  {message}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	#region Составление записи

	/// <summary>
	/// Разворачивает цепочку вложенных исключений.
	///
	/// Внешнее исключение обычно называет место, внутреннее — причину.
	/// Без разворачивания в журнале остаётся бесполезное «Произошла одна
	/// или несколько ошибок».
	/// </summary>
	private static void AppendException(StringBuilder text, Exception error, int depth)
	{
		string pad = new string(' ', depth * 2);

		text.Append(pad).Append(error.GetType().FullName).Append(": ").AppendLine(error.Message);

		if (error.TargetSite != null)
			text.Append(pad).Append("  Метод: ").AppendLine(Describe(error.TargetSite));

		if (!string.IsNullOrWhiteSpace(error.StackTrace))
		{
			text.Append(pad).AppendLine("  Стек:");

			foreach (var line in error.StackTrace.Split('\n'))
				text.Append(pad).Append("    ").AppendLine(line.TrimEnd('\r'));
		}

		if (error is AggregateException aggregate)
		{
			foreach (var inner in aggregate.InnerExceptions)
			{
				text.Append(pad).AppendLine("  --- вложенное ---");
				AppendException(text, inner, depth + 1);
			}

			return;
		}

		if (error.InnerException != null)
		{
			text.Append(pad).AppendLine("  --- причина ---");
			AppendException(text, error.InnerException, depth + 1);
		}
	}

	private static string Describe(MethodBase method)
		=> method.DeclaringType == null
			? method.Name
			: $"{method.DeclaringType.FullName}.{method.Name}";

	/// <summary>
	/// Обстановка на момент ошибки.
	///
	/// Каждый пункт здесь отвечает на вопрос, который иначе пришлось бы
	/// задавать пользователю письмом и ждать ответа сутки.
	/// </summary>
	private static void AppendContext(StringBuilder text)
	{
		text.AppendLine("Обстановка:");

		Line(text, "Версия", Version());
		Line(text, "Система", Environment.OSVersion.VersionString);
		Line(text, "Разрядность", Environment.Is64BitProcess ? "64 бита" : "32 бита");
		Line(text, "Процессоров", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
		Line(text, "Язык системы", CultureInfo.CurrentUICulture.Name);

		try
		{
			using var process = Process.GetCurrentProcess();

			Line(text, "Память процесса", $"{process.WorkingSet64 / (1024 * 1024)} МБ");
			Line(text, "Время работы", (DateTime.Now - process.StartTime).ToString(@"hh\:mm\:ss"));
		}
		catch
		{
			// Счётчики процесса могут быть недоступны при урезанных правах.
		}

		Line(text, "Управляемая память", $"{GC.GetTotalMemory(false) / (1024 * 1024)} МБ");

		AppendDocumentContext(text);
	}

	/// <summary>
	/// Что было открыто и чем пользователь занимался.
	///
	/// Имя файла НЕ пишется целиком: в нём бывает и фамилия заказчика,
	/// и номер договора. Достаточно расширения, числа страниц и фигур —
	/// они и объясняют, на чём споткнулась программа.
	/// </summary>
	private static void AppendDocumentContext(StringBuilder text)
	{
		try
		{
			var doc = MainForm.cur_doc;

			if (doc == null)
			{
				Line(text, "Документ", "не открыт");
				return;
			}

			Line(text, "Страниц", doc.pages.Count.ToString(CultureInfo.InvariantCulture));

			var page = MainForm.CurrentPage;

			if (page != null)
			{
				Line(text, "Фигур на странице", page.Shapes.Count.ToString(CultureInfo.InvariantCulture));
				Line(text, "Единицы страницы", page.uom?.title ?? "неизвестно");
			}

			Line(text, "Есть несохранённое", doc.need_save ? "да" : "нет");
			Line(text, "Режим", MainForm.Mode.ToString());
			Line(text, "Выделено фигур", MainForm.sel?.Selected.Count.ToString(CultureInfo.InvariantCulture) ?? "0");
		}
		catch
		{
			// Обстановка снимается уже после сбоя, и часть состояния может
			// быть разрушена. Отсутствие этих строк лучше второго падения.
			Line(text, "Документ", "состояние снять не удалось");
		}
	}

	private static void Line(StringBuilder text, string name, string value)
		=> text.Append("  ").Append(name).Append(": ").AppendLine(value);

	private static string Version()
	{
		var assembly = Assembly.GetExecutingAssembly();

		return assembly.GetName().Version?.ToString() ?? "неизвестна";
	}

	private static string Timestamp()
		=> DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

	#endregion

	#region Запись в файл

	private static void WriteSessionHeader()
	{
		var text = new StringBuilder();

		text.AppendLine();
		text.AppendLine(new string('=', 72));
		text.Append(Timestamp()).Append("  ЗАПУСК  VDraw ").AppendLine(Version());
		text.Append("  Система: ").AppendLine(Environment.OSVersion.VersionString);
		text.AppendLine(new string('=', 72));

		try
		{
			Append(text.ToString());
		}
		catch
		{
		}
	}

	/// <summary>
	/// Дописывает текст в файл.
	///
	/// Под замком: исключения приходят и из фоновых задач, а две записи
	/// внахлёст дали бы перемешанные строки — ровно там, где нужна ясность.
	/// </summary>
	private static void Append(string text)
	{
		lock (_lock)
		{
			string path = FilePath;
			string? folder = Path.GetDirectoryName(path);

			if (!string.IsNullOrEmpty(folder))
				Directory.CreateDirectory(folder);

			RotateIfTooBig(path);

			File.AppendAllText(path, text, Encoding.UTF8);
		}
	}

	private static void RotateIfTooBig(string path)
	{
		try
		{
			var info = new FileInfo(path);

			if (!info.Exists || info.Length < MaxSizeBytes)
				return;

			string previous = path + ".1";

			if (File.Exists(previous))
				File.Delete(previous);

			File.Move(path, previous);
		}
		catch
		{
			// Не вышло переложить — пишем дальше в тот же файл.
			// Разросшийся журнал лучше потерянного.
		}
	}

	#endregion

	#region Сообщение пользователю

	/// <summary>
	/// Показывать ли окно при ошибке в интерфейсе.
	///
	/// Молчаливая запись в журнал хуже, чем кажется: пользователь видит,
	/// что кнопка не сработала, пожимает плечами и работает дальше,
	/// а к нам файл так и не попадает. Одно короткое сообщение с путём
	/// к журналу превращает молчание в отчёт.
	/// </summary>
	public static bool ShowDialogs { get; set; } = true;

	/// <summary>
	/// Об одной и той же ошибке сообщаем один раз за сеанс.
	///
	/// Иначе неполадка в отрисовке или в обработчике движения мыши выдаст
	/// сотню одинаковых окон подряд, и закрыть программу станет нельзя.
	/// </summary>
	private static readonly HashSet<string> _reported = new(StringComparer.Ordinal);

	private static void Notify(Exception error)
	{
		if (!ShowDialogs)
			return;

		try
		{
			string signature = error.GetType().FullName + "|" + error.Message;

			lock (_lock)
			{
				if (!_reported.Add(signature))
					return;
			}

			MessageBox.Show(
				"В работе VDraw произошла ошибка. Программа продолжит работу, "
					+ "но сделанное только что действие могло не выполниться."
					+ Environment.NewLine + Environment.NewLine
					+ error.Message
					+ Environment.NewLine + Environment.NewLine
					+ "Подробности записаны в файл:"
					+ Environment.NewLine + FilePath
					+ Environment.NewLine + Environment.NewLine
					+ "Повторно об этой же ошибке сообщено не будет.",
				"VDraw",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}
		catch
		{
		}
	}

	#endregion

	/// <summary>Открывает журнал в программе по умолчанию.</summary>
	public static void ShowInEditor()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				Note("Журнал открыт пользователем, записей об ошибках нет.");
			}

			Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
		}
		catch
		{
		}
	}
}
