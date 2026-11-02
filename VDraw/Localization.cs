using System.Text.Json;

namespace VDraw;

public enum AppLanguage
{
	English,
	Russian
}

/// <summary>
/// Локализация интерфейса.
///
/// Ключ — сама английская строка, а не выдуманный идентификатор. Выбор
/// сделан под задачу: в приложении уже несколько тысяч строковых литералов
/// в 78 файлах, и перевод на идентификаторы означал бы разовую правку
/// каждого из них с риском разъезда «ключ есть, строки нет». При ключе-строке
/// правка сводится к обёртке L.T(...), а непереведённое место само
/// показывает английский текст вместо пустоты или имени ключа.
///
/// Словарь встроен в код, а не лежит в файле: пропавший при развёртывании
/// ресурс превратил бы кнопку переключения в кнопку, которая молча ничего
/// не делает. Файл Resources/Lang/ru.json, если он есть, дополняет и
/// перекрывает встроенный словарь — так правки перевода не требуют сборки.
/// </summary>
public static class Localization
{
	private static readonly Dictionary<string, string> _ru =
		new(StringComparer.Ordinal);

	/// <summary>
	/// Исходные английские тексты элементов интерфейса.
	///
	/// Нужны потому, что переключение двустороннее: если брать текущий текст
	/// элемента, то после перехода на русский ключом стал бы русский текст,
	/// и обратно на английский вернуться было бы уже нечем.
	/// </summary>
	private static readonly Dictionary<object, string> _originals = new();

	private static bool _loaded;

	/// <summary>Поддеревья, которые перевод не трогает.</summary>
	private static readonly HashSet<Control> _excluded = new();

	public static AppLanguage Current { get; private set; } = AppLanguage.English;

	/// <summary>Поднимается после смены языка. На него подписаны панели, которые перестраивают себя сами.</summary>
	public static event Action? LanguageChanged;

	private static string SettingsPath =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"VDraw", "language.txt");

	#region Перевод

	/// <summary>
	/// Перевод строки. Отсутствующий перевод возвращает исходный текст:
	/// непокрытое место выглядит как английская надпись, а не как дыра.
	/// </summary>
	public static string T(string? english)
	{
		if (string.IsNullOrEmpty(english))
			return string.Empty;

		if (Current == AppLanguage.English)
			return english;

		EnsureLoaded();

		return _ru.TryGetValue(english, out string? value) && !string.IsNullOrEmpty(value)
			? value
			: english;
	}

	/// <summary>Перевод с подстановкой, для сообщений с данными.</summary>
	public static string T(string english, params object?[] args) =>
		string.Format(T(english), args);

	/// <summary>
	/// Перевод с уточнением, где именно стоит подпись.
	///
	/// Нужен там, где одно английское слово в разных местах означает
	/// разное. «Size» у шрифта — это кегль, а у сетки штриха — размер
	/// в ячейках; словарь же построен на английской строке как на ключе,
	/// и второе значение затирало бы первое.
	///
	/// Ключ уточнённого перевода — «контекст|строка». Если его в словаре
	/// нет, берётся обычный перевод: уточнять приходится редко, и
	/// заводить пару ключей на каждую подпись незачем.
	/// </summary>
	public static string TIn(string context, string? english)
	{
		if (string.IsNullOrEmpty(english))
			return string.Empty;

		if (Current == AppLanguage.English)
			return english;

		EnsureLoaded();

		return _ru.TryGetValue(context + "|" + english, out string? value) && !string.IsNullOrEmpty(value)
			? value
			: T(english);
	}

	/// <summary>
	/// Перевод строки фильтра файлового диалога.
	///
	/// Строка фильтра — это пары «подпись|маски», разделённые вертикальной
	/// чертой. Переводится только подпись, то есть каждая вторая часть
	/// через одну: маска вроде «*.vdr» — данные для диалога, и перевод
	/// сломал бы отбор файлов.
	///
	/// Ключом служит подпись целиком, вместе со списком расширений
	/// в скобках: он часть видимого текста, и выдумывать для него
	/// отдельный ключ значило бы расходиться с тем, что на экране.
	/// </summary>
	public static string Filter(string? filter)
	{
		if (string.IsNullOrEmpty(filter))
			return string.Empty;

		if (Current == AppLanguage.English)
			return filter;

		string[] parts = filter.Split('|');

		for (int i = 0; i < parts.Length; i += 2)
			parts[i] = T(parts[i]);

		return string.Join('|', parts);
	}

	#endregion

	#region Переключение

	public static void SetLanguage(AppLanguage language)
	{
		if (language == Current)
			return;

		Current = language;

		SaveChoice();

		LanguageChanged?.Invoke();
	}

	public static void Toggle() =>
		SetLanguage(Current == AppLanguage.English ? AppLanguage.Russian : AppLanguage.English);

	/// <summary>Двухбуквенный код для подписи на кнопке.</summary>
	public static string CurrentCode => Current == AppLanguage.Russian ? "RU" : "EN";

	/// <summary>Код языка, на который переключит следующее нажатие.</summary>
	public static string NextCode => Current == AppLanguage.Russian ? "EN" : "RU";

	#endregion

	#region Применение к элементам интерфейса

	/// <summary>
	/// Запоминает исходные тексты дерева элементов и сразу применяет
	/// текущий язык. Вызывается один раз из конструктора формы.
	///
	/// Обходятся и обычные элементы, и пункты меню: у ToolStripItem нет
	/// общего с Control базового типа с текстом, поэтому ветки разные.
	/// </summary>
	public static void Register(Control root)
	{
		if (root == null)
			return;

		Capture(root);
		Apply(root);
		Fit(root);
	}

	/// <summary>
	/// Перевод меню, созданного в коде.
	///
	/// Общий обход до таких меню не доходит: ContextMenuStrip не лежит
	/// в дереве Controls формы, и Apply его не видит. Подписи запоминаются
	/// по-английски и расставляются заново при каждом открытии: меню живёт
	/// столько же, сколько форма, а открывается на долю секунды — дешевле
	/// перевести его на показе, чем держать подписку на смену языка.
	///
	/// Вызывается после того, как пункты добавлены и подписаны
	/// по-английски.
	/// </summary>
	public static void RegisterMenu(ToolStripDropDown menu)
	{
		if (menu == null)
			return;

		foreach (ToolStripItem item in menu.Items)
			CaptureItem(item);

		ApplyMenu(menu);

		menu.Opening += (_, _) => ApplyMenu(menu);
	}

	private static void ApplyMenu(ToolStripDropDown menu)
	{
		foreach (ToolStripItem item in menu.Items)
			ApplyItem(item);
	}

	/// <summary>
	/// Забывает исходный текст элемента: при смене языка он останется
	/// таким, какой есть.
	///
	/// Нужно тем, у кого подпись убрали уже после обхода — кнопкам,
	/// которым раздали значки вместо текста.
	/// </summary>
	public static void Forget(object item)
	{
		if (item != null)
			_originals.Remove(item);
	}

	/// <summary>
	/// Исключает элемент и всё его содержимое из перевода. Вызывается
	/// до Register.
	/// </summary>
	public static void Exclude(params Control[] controls)
	{
		foreach (var control in controls)
		{
			if (control != null)
				_excluded.Add(control);
		}
	}

	/// <summary>
	/// Можно ли переводить свойство Text этого элемента.
	///
	/// Разрешительный список, а не запретительный. У NumericUpDown,
	/// ComboBox, TextBox и им подобных Text — это ДАННЫЕ, а не подпись:
	/// присваивание туда переведённой строки сбрасывает значение и поднимает
	/// ValueChanged, то есть меняет документ. Запретительный список пропустил
	/// бы каждый новый тип контрола, добавленный позже; разрешительный
	/// по умолчанию не трогает ничего незнакомого.
	/// </summary>
	private static bool IsCaptionControl(Control control) => control switch
	{
		Form => true,
		Label => true,        // и LinkLabel — он наследник Label
		ButtonBase => true,   // Button, CheckBox, RadioButton
		GroupBox => true,
		TabPage => true,
		_ => false
	};

	public static void Apply(Control root)
	{
		if (root == null || _excluded.Contains(root))
			return;

		if (IsCaptionControl(root) && _originals.TryGetValue(root, out string? original))
		{
			string translated = T(original);

			// Сравнение перед присваиванием: лишняя запись в Text поднимает
			// события даже когда строка не изменилась.
			if (!string.Equals(root.Text, translated, StringComparison.Ordinal))
				root.Text = translated;
		}

		if (root is ToolStrip strip)
		{
			foreach (ToolStripItem item in strip.Items)
				ApplyItem(item);
		}

		foreach (Control child in root.Controls)
			Apply(child);
	}

	#region Подгонка размеров под перевод

	/// <summary>
	/// Подсказки для подписей, которым текст всё равно не влез. Своя, а не
	/// общая с окном: та раздаёт пояснения к кнопкам, и затирать их
	/// техническим «вот полный текст» нельзя.
	/// </summary>
	private static readonly ToolTip _overflowTips = new() { AutoPopDelay = 20000 };

	/// <summary>
	/// Раздвигает подписи под переведённый текст, а что не раздвинулось —
	/// помечает многоточием и показывает целиком в подсказке.
	///
	/// Русский текст в среднем на пятую часть длиннее английского, и
	/// подписи, подогнанные в дизайнере под английский, обрезали бы его.
	/// Проверять каждую руками бессмысленно: их несколько сотен, и
	/// перевод будет правиться дальше.
	///
	/// Раздвигается только вправо и только до ближайшего соседа: сдвигать
	/// соседей значило бы перекладывать форму заново, а это уже не подгонка,
	/// а переразметка, и предсказать её на глаз нельзя. Где места нет,
	/// обрезка остаётся, но видимой — многоточие и подсказка честнее
	/// молча срезанного слова.
	/// </summary>
	public static void Fit(Control root)
	{
		if (root == null || _excluded.Contains(root))
			return;

		if (root is not Form && root is not TabPage)
			FitOne(root);

		foreach (Control child in root.Controls)
			Fit(child);
	}

	private static void FitOne(Control control)
	{
		if (!IsCaptionControl(control) || control.AutoSize || string.IsNullOrEmpty(control.Text))
			return;

		int needed = MeasureWidth(control);

		if (needed <= control.Width)
			return;

		// В потоковой панели ширину можно не считать вовсе: достаточно
		// разрешить подписи расти самой, а панель переложит соседей.
		// Для этого потоковая панель и нужна, и русский текст, который
		// иначе переносился бы на вторую строку посреди слова, встаёт
		// в одну.
		if (control.Parent is FlowLayoutPanel)
		{
			control.AutoSize = true;

			return;
		}

		// Внутри остальных раскладочных панелей ширину задаёт панель:
		// присваивание в Width там либо ничего не даст, либо собьёт
		// разметку.
		bool ownWidth =
			control.Dock == DockStyle.None &&
			control.Parent is not TableLayoutPanel;

		if (ownWidth)
			control.Width += Math.Min(needed - control.Width, FreeSpaceRight(control));

		if (needed <= control.Width)
			return;

		switch (control)
		{
			case Label label:
				label.AutoEllipsis = true;
				break;

			case ButtonBase button:
				button.AutoEllipsis = true;
				break;
		}

		_overflowTips.SetToolTip(control, control.Text);
	}

	/// <summary>Ширина, нужная тексту вместе с отступами и глифом.</summary>
	private static int MeasureWidth(Control control)
	{
		int widest = 0;

		// Многострочная подпись мерится по самой длинной строке: Graphics
		// меряет перенос по уже заданной ширине, а нам нужна та ширина,
		// которой не хватает.
		foreach (string line in control.Text.Split('\n'))
			widest = Math.Max(widest, TextRenderer.MeasureText(line.TrimEnd('\r'), control.Font).Width);

		int extra = control.Padding.Horizontal + control switch
		{
			CheckBox or RadioButton => 22,   // квадратик или кружок слева
			Button => 12,                    // поля внутри кнопки
			_ => 2
		};

		return widest + extra;
	}

	/// <summary>
	/// Свободное место справа: до ближайшего соседа, который стоит правее
	/// и пересекается по высоте, иначе до края родителя.
	/// </summary>
	private static int FreeSpaceRight(Control control)
	{
		const int gap = 6;

		var parent = control.Parent;

		if (parent == null)
			return 0;

		int limit = parent.ClientSize.Width - gap;

		foreach (Control sibling in parent.Controls)
		{
			if (ReferenceEquals(sibling, control) || !sibling.Visible)
				continue;

			if (sibling.Left < control.Right)
				continue;

			bool overlapsVertically =
				sibling.Top < control.Bottom && control.Top < sibling.Bottom;

			if (overlapsVertically)
				limit = Math.Min(limit, sibling.Left - gap);
		}

		return Math.Max(0, limit - control.Right);
	}

	#endregion

	private static void Capture(Control control)
	{
		if (_excluded.Contains(control))
			return;

		// Пустые тексты не запоминаются: у панелей и контейнеров их нет,
		// а хранить пустую строку значило бы засорять словарь.
		if (IsCaptionControl(control) && !string.IsNullOrEmpty(control.Text) && !_originals.ContainsKey(control))
			_originals[control] = control.Text;

		if (control is ToolStrip strip)
		{
			foreach (ToolStripItem item in strip.Items)
				CaptureItem(item);
		}

		foreach (Control child in control.Controls)
			Capture(child);
	}

	private static void CaptureItem(ToolStripItem item)
	{
		if (!string.IsNullOrEmpty(item.Text) && !_originals.ContainsKey(item))
			_originals[item] = item.Text;

		if (item is ToolStripDropDownItem drop)
		{
			foreach (ToolStripItem child in drop.DropDownItems)
				CaptureItem(child);
		}
	}

	private static void ApplyItem(ToolStripItem item)
	{
		if (_originals.TryGetValue(item, out string? original))
			item.Text = T(original);

		if (item is ToolStripDropDownItem drop)
		{
			foreach (ToolStripItem child in drop.DropDownItems)
				ApplyItem(child);
		}
	}

	#endregion

	#region Загрузка словаря

	private static void EnsureLoaded()
	{
		if (_loaded)
			return;

		_loaded = true;

		foreach (var pair in LocalizationRu.Table)
			_ru[pair.Key] = pair.Value;

		LoadOverrides();
	}

	/// <summary>
	/// Необязательный файл Resources/Lang/ru.json. Позволяет править перевод
	/// без пересборки; отсутствие файла — обычное дело, а не ошибка.
	/// </summary>
	private static void LoadOverrides()
	{
		try
		{
			string path = Path.Combine(ResourcePaths.Root, "Lang", "ru.json");

			if (!File.Exists(path))
				return;

			var table = JsonSerializer.Deserialize<Dictionary<string, string>>(
				File.ReadAllText(path));

			if (table == null)
				return;

			foreach (var pair in table)
			{
				if (!string.IsNullOrEmpty(pair.Value))
					_ru[pair.Key] = pair.Value;
			}
		}
		catch
		{
			// Испорченный файл перевода не должен мешать работе: остаётся
			// встроенный словарь.
		}
	}

	#endregion

	#region Сохранение выбора

	public static void LoadChoice()
	{
		try
		{
			if (!File.Exists(SettingsPath))
				return;

			string value = File.ReadAllText(SettingsPath).Trim();

			if (string.Equals(value, "ru", StringComparison.OrdinalIgnoreCase))
				Current = AppLanguage.Russian;
		}
		catch
		{
		}
	}

	private static void SaveChoice()
	{
		try
		{
			string? folder = Path.GetDirectoryName(SettingsPath);

			if (!string.IsNullOrEmpty(folder))
				Directory.CreateDirectory(folder);

			File.WriteAllText(SettingsPath, Current == AppLanguage.Russian ? "ru" : "en");
		}
		catch
		{
		}
	}

	#endregion
}

/// <summary>Короткое имя для мест, где обёртка встречается десятками.</summary>
public static class L
{
	public static string T(string? english) => Localization.T(english);

	public static string T(string english, params object?[] args) => Localization.T(english, args);

	public static string TIn(string context, string? english) => Localization.TIn(context, english);

	public static string Filter(string? filter) => Localization.Filter(filter);
}
