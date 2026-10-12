using System.Drawing.Printing;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

/// <summary>Формат бумаги в том виде, в каком он нужен приложению.</summary>
public sealed class PrinterPaperInfo
{
	public string DisplayName { get; init; } = string.Empty;

	/// <summary>Ширина в сотых долях дюйма — родные единицы PaperSize.</summary>
	public int WidthHundredthsInch { get; init; }

	public int HeightHundredthsInch { get; init; }

	public PaperKind Kind { get; init; } = PaperKind.Custom;

	public float WidthMm => WidthHundredthsInch * 0.254f;
	public float HeightMm => HeightHundredthsInch * 0.254f;
}

/// <summary>Принтер и то, что он умеет.</summary>
public sealed class PrinterInfo
{
	public string Name { get; init; } = string.Empty;
	public bool IsDefault { get; init; }

	public List<PrinterPaperInfo> Papers { get; } = new();

}

/// <summary>Поля печати в миллиметрах от краёв листа.</summary>
public sealed class PrinterMarginsMm
{
	public float LeftMm { get; init; }
	public float TopMm { get; init; }
	public float RightMm { get; init; }
	public float BottomMm { get; init; }
}

/// <summary>
/// Список принтеров и поддерживаемых ими форматов, собранный ОДИН РАЗ
/// при запуске.
///
/// Зачем кэш. Перечисление принтеров обращается к спулеру, а чтение
/// PaperSizes и PrintableArea — к драйверу каждого устройства. Сетевой
/// или спящий принтер отвечает не мгновенно, и на машине с десятком
/// очередей опрос занимает секунды. Раньше это происходило при открытии
/// диалога печати, то есть пользователь ждал именно тогда, когда уже
/// хотел печатать. Теперь то же самое делается в фоне на старте, и к
/// моменту первой печати список готов.
///
/// Второе применение — рамка печатаемой области на странице. Её надо
/// рисовать постоянно, и лезть за ней в драйвер на каждый кадр
/// недопустимо.
/// </summary>
public static class PrinterCatalog
{
	private static readonly object _lock = new();

	private static List<PrinterInfo> _printers = new();
	private static bool _ready;
	private static Task? _loading;

	/// <summary>Готов ли список. Пока false, обращения возвращают пустой набор.</summary>
	public static bool IsReady
	{
		get { lock (_lock) return _ready; }
	}

	/// <summary>Поднимается в потоке пула, когда опрос закончен.</summary>
	public static event Action? Loaded;

	public static IReadOnlyList<PrinterInfo> Printers
	{
		get { lock (_lock) return _printers; }
	}

	public static PrinterInfo? Default
	{
		get
		{
			lock (_lock)
				return _printers.FirstOrDefault(p => p.IsDefault) ?? _printers.FirstOrDefault();
		}
	}

	public static PrinterInfo? Find(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return null;

		lock (_lock)
			return _printers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
	}

	/// <summary>
	/// Запускает опрос в фоне. Повторные вызовы безвредны: работа идёт
	/// один раз.
	///
	/// Именно в фоне, а не в конструкторе формы: опрос драйверов способен
	/// подвесить поток интерфейса на секунды, и окно бы не показывалось.
	/// </summary>
	public static void BeginInitialize()
	{
		lock (_lock)
		{
			if (_loading != null)
				return;

			_loading = Task.Run(Load);
		}
	}

	/// <summary>Дождаться готовности. Для диалога печати, которому список нужен сразу.</summary>
	public static void WaitUntilReady(int timeoutMs = 5000)
	{
		Task? task;

		lock (_lock)
		{
			if (_ready)
				return;

			task = _loading;
		}

		if (task == null)
		{
			BeginInitialize();

			lock (_lock)
				task = _loading;
		}

		try
		{
			task?.Wait(timeoutMs);
		}
		catch
		{
			// Опрос не удался — работаем с тем, что успело собраться.
			// Печать в этом случае просто покажет неполный список.
		}
	}

	private static PrinterMarginsMm? _defaultMargins;
	private static bool _defaultMarginsProbed;

	/// <summary>
	/// Поля печати принтера по умолчанию. Считаются один раз, при первом
	/// обращении, и кэшируются.
	///
	/// Почему не в фоновом опросе вместе со списком принтеров. Печатная
	/// подсистема System.Drawing не обещает потокобезопасности, и
	/// PrintableArea, прочитанная не из потока интерфейса, у части
	/// драйверов возвращает нули — рамка тогда не рисовалась вовсе и
	/// появлялась только после диалога печати, который инициализировал
	/// драйвер по-настоящему. Здесь чтение идёт из потока отрисовки, тем
	/// же путём, что и в диалоге, поэтому результат совпадает.
	///
	/// Одно обращение к драйверу за сеанс: первый кадр с включённой рамкой
	/// может занять чуть больше обычного, дальше берётся из кэша.
	/// </summary>
	public static PrinterMarginsMm? GetDefaultMarginsMm()
	{
		if (_defaultMarginsProbed)
			return _defaultMargins;

		_defaultMarginsProbed = true;

		try
		{
			using var doc = new PrintDocument();

			var page = doc.DefaultPageSettings;
			var paper = page.PaperSize;

			if (paper == null || paper.Width <= 0 || paper.Height <= 0)
				return null;

			var area = page.PrintableArea;

			// PrintableArea и PaperSize — оба в сотых долях дюйма.
			float left = area.Left * 0.254f;
			float top = area.Top * 0.254f;
			float right = MathF.Max(0f, (paper.Width - area.Right) * 0.254f);
			float bottom = MathF.Max(0f, (paper.Height - area.Bottom) * 0.254f);

			// Все нули означают, что драйвер о полях не сообщил. Рисовать
			// рамку по краю листа бессмысленно — она ничего не показывает.
			if (left <= 0.01f && top <= 0.01f && right <= 0.01f && bottom <= 0.01f)
				return null;

			_defaultMargins = new PrinterMarginsMm
			{
				LeftMm = left,
				TopMm = top,
				RightMm = right,
				BottomMm = bottom
			};
		}
		catch
		{
			// Принтеров нет либо спулер недоступен — рамки просто не будет.
			_defaultMargins = null;
		}

		return _defaultMargins;
	}

	/// <summary>Сбросить кэш полей: принтер по умолчанию мог смениться.</summary>
	public static void ResetDefaultMargins()
	{
		_defaultMarginsProbed = false;
		_defaultMargins = null;
	}

	private static void Load()
	{
		var result = new List<PrinterInfo>();

		string defaultName = string.Empty;

		try
		{
			// Имя принтера по умолчанию: у свежего PrinterSettings оно
			// уже подставлено системой.
			defaultName = new PrinterSettings().PrinterName ?? string.Empty;
		}
		catch
		{
			// Спулер недоступен. Не повод падать: приложение должно
			// запускаться и на машине вообще без печати.
		}

		try
		{
			foreach (string name in PrinterSettings.InstalledPrinters)
			{
				var info = Probe(name, string.Equals(name, defaultName, StringComparison.Ordinal));

				if (info != null)
					result.Add(info);
			}
		}
		catch
		{
			// Тот же случай: список останется пустым либо неполным.
		}

		lock (_lock)
		{
			_printers = result;
			_ready = true;
		}

		Loaded?.Invoke();
	}

	/// <summary>
	/// Опрашивает один принтер. Каждый в своём try: неисправный драйвер
	/// не должен лишать пользователя остальных устройств.
	/// </summary>
	private static PrinterInfo? Probe(string name, bool isDefault)
	{
		try
		{
			var settings = new PrinterSettings { PrinterName = name };

			// IsValid отсеивает очереди, оставшиеся в реестре от удалённых
			// устройств: у них чтение PaperSizes бросает исключение.
			if (!settings.IsValid)
				return null;

			// Поля печати здесь НЕ читаются намеренно: см. GetDefaultMarginsMm.
			var info = new PrinterInfo
			{
				Name = name,
				IsDefault = isDefault
			};

			foreach (PaperSize size in settings.PaperSizes)
			{
				info.Papers.Add(new PrinterPaperInfo
				{
					DisplayName = size.PaperName,
					WidthHundredthsInch = size.Width,
					HeightHundredthsInch = size.Height,
					Kind = size.Kind
				});
			}

			return info;
		}
		catch
		{
			return null;
		}
	}
}