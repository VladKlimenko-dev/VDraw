using System.Globalization;
using System.Text;

namespace VDraw;

/// <summary>
/// Режим, в котором действует сочетание.
///
/// Нужен для проверки совпадений: клавиша N в режиме фигур и та же N
/// в режиме узлов — разные действия и друг другу не мешают. Без понятия
/// режима проверка ругалась бы на пары, которые на деле никогда
/// не встречаются.
/// </summary>
public enum ShortcutScope
{
	/// <summary>Работает в любом режиме.</summary>
	Global,

	/// <summary>Только при работе с фигурами.</summary>
	Shapes,

	/// <summary>Только при правке узлов кривой.</summary>
	Curve
}

/// <summary>Действие, которому можно назначить сочетание клавиш.</summary>
public sealed class ShortcutAction
{
	/// <summary>
	/// Устойчивый ключ для настроек.
	///
	/// Не подпись и не номер: подпись переводится и меняется, номер
	/// поехал бы при вставке нового действия в середину списка, а ключ
	/// переживает и то и другое.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>Английская подпись. На экран идёт через L.T.</summary>
	public required string Title { get; init; }

	/// <summary>Раздел списка. Тоже английский, тоже переводится.</summary>
	public required string Group { get; init; }

	public ShortcutScope Scope { get; init; } = ShortcutScope.Global;

	public Keys Default { get; init; }

	/// <summary>Текущее сочетание.</summary>
	public Keys Keys { get; set; }

	/// <summary>
	/// Дополнительные сочетания, которые пользователь не меняет.
	///
	/// Их два вида: привычные синонимы (Ctrl+Shift+Z для возврата) и
	/// клавиши цифрового блока. Выводить их в список отдельной строкой
	/// значило бы засорить его дублями, а отнимать у пользователя —
	/// сломать привычку без выгоды.
	/// </summary>
	public Keys[] Alternates { get; init; } = Array.Empty<Keys>();

	/// <summary>
	/// Работает ли сочетание во время набора текста.
	///
	/// Разрешены только команды документа: Ctrl+S при правке подписи
	/// должен сохранять документ. Ctrl+C, Ctrl+V и Ctrl+A принадлежат
	/// полю ввода — перехватив их, мы отняли бы у пользователя правку.
	/// </summary>
	public bool AllowedWhileTyping { get; init; }

	public bool IsDefault => Keys == Default;

	public bool Matches(Keys keyData)
	{
		if (Keys != Keys.None && keyData == Keys)
			return true;

		foreach (var alt in Alternates)
		{
			if (keyData == alt)
				return true;
		}

		return false;
	}
}

/// <summary>
/// Реестр сочетаний клавиш.
///
/// Единственное место, где сочетание записано вместе с действием.
/// Прежде они жили в шести switch внутри MainForm: добавить настройку
/// к ним было невозможно, потому что метка case — это константа времени
/// компиляции, а пользовательское сочетание известно только в рантайме.
/// </summary>
public static class Shortcuts
{
	private static readonly List<ShortcutAction> _actions = Build();

	private static readonly Dictionary<string, ShortcutAction> _byId =
		_actions.ToDictionary(a => a.Id, StringComparer.Ordinal);

	public static IReadOnlyList<ShortcutAction> All => _actions;

	public static ShortcutAction? Find(string id)
		=> _byId.TryGetValue(id, out var action) ? action : null;

	public static Keys Of(string id) => Find(id)?.Keys ?? Keys.None;

	/// <summary>
	/// Действие по нажатой комбинации СТРОГО в указанном режиме.
	///
	/// Без запасных вариантов: порядок просмотра режимов задаёт тот, кто
	/// разбирает нажатие, — он один знает, какой режим сейчас включён
	/// и какой из них главнее.
	/// </summary>
	public static ShortcutAction? ResolveInScope(Keys keyData, ShortcutScope scope)
	{
		if (keyData == Keys.None)
			return null;

		foreach (var action in _actions)
		{
			if (action.Scope == scope && action.Matches(keyData))
				return action;
		}

		return null;
	}

	/// <summary>
	/// Действия с повторяющимся сочетанием.
	///
	/// Режим при сравнении не учитывается, и это не упрощение. Действия
	/// фигур работают во всех режимах, а в режиме узлов к ним добавляются
	/// узловые — то есть одновременно живыми бывают все три набора сразу,
	/// и непересекающихся пар просто нет. Режим задаёт не изоляцию,
	/// а старшинство при разборе нажатия.
	/// </summary>
	public static HashSet<string> FindConflicts()
	{
		var conflicts = new HashSet<string>(StringComparer.Ordinal);

		for (int i = 0; i < _actions.Count; i++)
		{
			var a = _actions[i];

			if (a.Keys == Keys.None)
				continue;

			for (int j = i + 1; j < _actions.Count; j++)
			{
				if (_actions[j].Keys != a.Keys)
					continue;

				conflicts.Add(a.Id);
				conflicts.Add(_actions[j].Id);
			}
		}

		return conflicts;
	}

	public static void ResetToDefaults()
	{
		foreach (var action in _actions)
			action.Keys = action.Default;
	}

	#region Сохранение

	/// <summary>
	/// Строка для файла настроек: «id=код;id=код».
	///
	/// Записываются только отличия от исходных значений. Так файл
	/// не распухает от полусотни строк, в которых ничего не изменено,
	/// а добавленное позже действие приходит со своим сочетанием,
	/// а не с пустым.
	///
	/// Код — целое число Keys, а не текст сочетания: текст зависит
	/// от языка системы и разбирается неоднозначно.
	/// </summary>
	public static string Serialize()
	{
		var sb = new StringBuilder();

		foreach (var action in _actions)
		{
			if (action.IsDefault)
				continue;

			if (sb.Length > 0)
				sb.Append(';');

			sb.Append(action.Id).Append('=').Append((int)action.Keys);
		}

		return sb.ToString();
	}

	public static void Deserialize(string? value)
	{
		ResetToDefaults();

		if (string.IsNullOrWhiteSpace(value))
			return;

		foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			int eq = part.IndexOf('=');

			if (eq <= 0)
				continue;

			string id = part.Substring(0, eq);
			string raw = part.Substring(eq + 1);

			if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
				continue;

			if (_byId.TryGetValue(id, out var action))
				action.Keys = (Keys)code;
		}
	}

	#endregion

	#region Вид сочетания на экране

	/// <summary>
	/// Человеческая запись сочетания.
	///
	/// Своя, а не KeysConverter: тот зовёт клавиши именами из перечисления
	/// («Oemcomma», «D1», «Oemtilde»), и в списке настроек они читаются
	/// как мусор.
	/// </summary>
	public static string Format(Keys keys)
	{
		if (keys == Keys.None)
			return string.Empty;

		var sb = new StringBuilder();

		if ((keys & Keys.Control) != 0)
			sb.Append("Ctrl+");

		if ((keys & Keys.Alt) != 0)
			sb.Append("Alt+");

		if ((keys & Keys.Shift) != 0)
			sb.Append("Shift+");

		sb.Append(KeyName(keys & Keys.KeyCode));

		return sb.ToString();
	}

	private static string KeyName(Keys key)
	{
		switch (key)
		{
			case Keys.None: return string.Empty;

			case Keys.Oemcomma: return ",";
			case Keys.OemPeriod: return ".";
			case Keys.OemMinus: return "-";
			case Keys.Oemplus: return "=";
			case Keys.OemQuestion: return "/";
			case Keys.Oemtilde: return "`";
			case Keys.OemSemicolon: return ";";
			case Keys.OemQuotes: return "'";
			case Keys.OemOpenBrackets: return "[";
			case Keys.Oem6: return "]";
			case Keys.Oem5: return "\\";

			case Keys.Add: return "Num +";
			case Keys.Subtract: return "Num -";
			case Keys.Multiply: return "Num *";
			case Keys.Divide: return "Num /";
			case Keys.Decimal: return "Num .";

			case Keys.Prior: return "Page Up";
			case Keys.Next: return "Page Down";
			case Keys.Back: return "Backspace";
			case Keys.Return: return "Enter";
			case Keys.Capital: return "Caps Lock";
			case Keys.Escape: return "Esc";
			case Keys.Space: return "Space";
		}

		if (key >= Keys.D0 && key <= Keys.D9)
			return ((char)('0' + (key - Keys.D0))).ToString();

		if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
			return "Num " + (char)('0' + (key - Keys.NumPad0));

		return key.ToString();
	}

	/// <summary>
	/// Годится ли нажатие как сочетание.
	///
	/// Одни модификаторы не годятся: Ctrl сам по себе не действие.
	/// </summary>
	public static bool IsAssignable(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;

		return key != Keys.None
			&& key != Keys.ControlKey
			&& key != Keys.ShiftKey
			&& key != Keys.Menu
			&& key != Keys.LWin
			&& key != Keys.RWin;
	}

	#endregion

	#region Исходный набор

	private static ShortcutAction A(
		string id,
		string group,
		string title,
		Keys keys,
		ShortcutScope scope = ShortcutScope.Global,
		bool whileTyping = false,
		params Keys[] alternates)
	{
		return new ShortcutAction
		{
			Id = id,
			Group = group,
			Title = title,
			Default = keys,
			Keys = keys,
			Scope = scope,
			AllowedWhileTyping = whileTyping,
			Alternates = alternates
		};
	}

	private static List<ShortcutAction> Build()
	{
		const string file = "File";
		const string edit = "Edit";
		const string tools = "Tools";
		const string arrange = "Arrange";
		const string boolean = "Boolean operations";
		const string nudge = "Nudge";
		const string nodes = "Curve nodes";
		const string view = "View";

		return new List<ShortcutAction>
		{
			A("file.new", file, "New document", Keys.Control | Keys.N, whileTyping: true),
			A("file.open", file, "Open document", Keys.Control | Keys.O, whileTyping: true),
			A("file.save", file, "Save", Keys.Control | Keys.S, whileTyping: true),
			A("file.saveAs", file, "Save as", Keys.Control | Keys.Shift | Keys.S, whileTyping: true),
			A("file.import", file, "Import", Keys.Control | Keys.I, whileTyping: true),
			A("file.export", file, "Export selection", Keys.Control | Keys.E, whileTyping: true),
			A("file.print", file, "Print", Keys.Control | Keys.P, whileTyping: true),
			A("file.help", file, "Help", Keys.F1, whileTyping: true),

			A("edit.undo", edit, "Undo", Keys.Control | Keys.Z),
			A("edit.redo", edit, "Redo", Keys.Control | Keys.Y,
				alternates: Keys.Control | Keys.Shift | Keys.Z),
			A("edit.copyFormat", edit, "Copy formatting", Keys.Control | Keys.Shift | Keys.C),
			A("edit.pasteFormat", edit, "Paste formatting", Keys.Control | Keys.Shift | Keys.V),
			A("edit.selectAll", edit, "Select all", Keys.Control | Keys.A),
			A("edit.delete", edit, "Delete selected", Keys.Delete),

			A("tool.select", tools, "Select", Keys.Escape, ShortcutScope.Shapes),
			A("tool.line", tools, "Straight lines", Keys.L, ShortcutScope.Shapes),
			A("tool.curve", tools, "Curve", Keys.B, ShortcutScope.Shapes),
			A("tool.text", tools, "Text", Keys.T, ShortcutScope.Shapes),
			A("tool.measureDistance", tools, "Measure distance", Keys.M, ShortcutScope.Shapes),
			A("tool.nodes", tools, "Edit nodes", Keys.N, ShortcutScope.Shapes),
			A("tool.polygon", tools, "Regular polygon", Keys.P, ShortcutScope.Shapes),
			A("tool.freePolygon", tools, "Free polygon", Keys.Shift | Keys.P, ShortcutScope.Shapes),
			A("tool.cutLine", tools, "Cut with a straight line", Keys.K, ShortcutScope.Shapes),
			A("tool.cutCurve", tools, "Cut with a curve", Keys.Shift | Keys.K, ShortcutScope.Shapes),
			A("tool.rectangle", tools, "Rectangle", Keys.R, ShortcutScope.Shapes),
			A("tool.circle", tools, "Circle", Keys.C, ShortcutScope.Shapes),
			A("tool.ellipse", tools, "Ellipse", Keys.E, ShortcutScope.Shapes),
			A("tool.image", tools, "Image", Keys.Control | Keys.Shift | Keys.I),
			A("tool.imageMode", tools, "Image editing mode", Keys.Control | Keys.M),

			A("arrange.group", arrange, "Group", Keys.Control | Keys.G),
			A("arrange.ungroup", arrange, "Ungroup", Keys.Control | Keys.Shift | Keys.G),
			A("arrange.bringToFront", arrange, "Bring to front", Keys.Control | Keys.Shift | Keys.Up),
			A("arrange.sendToBack", arrange, "Send to back", Keys.Control | Keys.Shift | Keys.Down),
			A("arrange.flipHorizontal", arrange, "Flip horizontally", Keys.Shift | Keys.H),
			A("arrange.flipVertical", arrange, "Flip vertically", Keys.Shift | Keys.V),
			A("arrange.rotateCW", arrange, "Rotate 90 clockwise", Keys.Control | Keys.Right),
			A("arrange.rotateCCW", arrange, "Rotate 90 counter-clockwise", Keys.Control | Keys.Left),

			A("bool.union", boolean, "Union", Keys.Control | Keys.Alt | Keys.U),
			A("bool.subtract", boolean, "Subtract", Keys.Control | Keys.Alt | Keys.S),
			A("bool.intersect", boolean, "Intersect", Keys.Control | Keys.Alt | Keys.I),
			A("bool.exclude", boolean, "Exclude", Keys.Control | Keys.Alt | Keys.X),
			A("bool.combine", boolean, "Combine", Keys.Control | Keys.Alt | Keys.C),
			A("bool.convertToCurve", boolean, "Convert to curve", Keys.Control | Keys.Q),

			A("nudge.left", nudge, "Nudge left", Keys.Left),
			A("nudge.right", nudge, "Nudge right", Keys.Right),
			A("nudge.up", nudge, "Nudge up", Keys.Up),
			A("nudge.down", nudge, "Nudge down", Keys.Down),
			A("nudge.leftBig", nudge, "Nudge left, large step", Keys.Shift | Keys.Left),
			A("nudge.rightBig", nudge, "Nudge right, large step", Keys.Shift | Keys.Right),
			A("nudge.upBig", nudge, "Nudge up, large step", Keys.Shift | Keys.Up),
			A("nudge.downBig", nudge, "Nudge down, large step", Keys.Shift | Keys.Down),

			A("node.straight", nodes, "Straight node", Keys.D1, ShortcutScope.Curve,
				alternates: Keys.NumPad1),
			A("node.tangent", nodes, "Smooth node", Keys.D2, ShortcutScope.Curve,
				alternates: Keys.NumPad2),
			A("node.sharp", nodes, "Cusp node", Keys.D3, ShortcutScope.Curve,
				alternates: Keys.NumPad3),
			A("node.merge", nodes, "Merge nodes", Keys.Control | Keys.J, ShortcutScope.Curve),
			A("node.split", nodes, "Split node", Keys.Control | Keys.Shift | Keys.J, ShortcutScope.Curve),

			A("view.zoomAll", view, "Zoom to all shapes", Keys.Control | Keys.D0),
			A("view.zoomPage", view, "Fit page", Keys.Control | Keys.D1),
			A("view.zoomSelection", view, "Zoom to selection", Keys.Control | Keys.Shift | Keys.D0),
			A("view.rulers", view, "Rulers", Keys.Control | Keys.R),
			A("view.guides", view, "Guides", Keys.Control | Keys.Oemcomma),
			A("view.grid", view, "Grid", Keys.Control | Keys.D3),
			A("view.snapObjects", view, "Snap to objects", Keys.Control | Keys.Shift | Keys.N),
			A("view.dynamicGuides", view, "Dynamic guides", Keys.Control | Keys.Shift | Keys.D)
		};
	}

	#endregion
}
