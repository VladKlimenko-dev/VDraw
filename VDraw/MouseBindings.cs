namespace VDraw;

/// <summary>Чем вызывается действие мыши.</summary>
public enum MouseInput
{
	/// <summary>Кнопка не нужна: действие включается одними модификаторами во время протяжки.</summary>
	None,

	Left,
	Middle,
	Right,
	Wheel
}

/// <summary>Одно настраиваемое действие мыши.</summary>
public sealed class MouseActionBinding
{
	public required string Id { get; init; }

	public required string Group { get; init; }

	/// <summary>Что действие делает. Это же показывается в настройках.</summary>
	public required string Title { get; init; }

	public required MouseInput DefaultInput { get; init; }

	public required Keys DefaultModifiers { get; init; }

	public MouseInput Input { get; set; }

	public Keys Modifiers { get; set; }

	/// <summary>
	/// Действие без кнопки: работает, пока держат модификаторы.
	///
	/// Определяется по умолчанию, а не по текущему значению: пользователь
	/// не должен превратить «отключить привязки на время протяжки»
	/// в действие по кнопке — протяжка уже занята тем, что тащат.
	/// </summary>
	public bool ModifierOnly => DefaultInput == MouseInput.None;

	public bool IsDefault => Input == DefaultInput && Modifiers == DefaultModifiers;
}

/// <summary>
/// Настраиваемые действия мыши.
///
/// Настраивается не всё. Левая кнопка жёстко означает «работать
/// инструментом»: на ней держатся и создание фигур, и выделение,
/// и перетаскивание маркеров, и правка узлов — сотни мест, которые
/// пришлось бы переписать ради возможности, за которой никто
/// не приходил. Настраиваются две вещи, вокруг которых и возникают
/// вопросы: чем панорамируют и вызывают меню, и какие модификаторы
/// что включают во время протяжки.
///
/// Совпадения не запрещаются. Правая кнопка может одновременно
/// панорамировать и вызывать меню: меню открывается только на щелчке
/// без протяжки, и такое сочетание осмысленно — именно так работают
/// чертёжные редакторы. Совпадения показываются в настройках красным,
/// а решает пользователь.
/// </summary>
public static class MouseBindings
{
	public const string Pan = "mouse.pan";
	public const string ContextMenu = "mouse.contextMenu";
	public const string ToolCancel = "mouse.toolCancel";

	public const string AddToSelection = "mouse.addToSelection";
	public const string AxisLock = "mouse.axisLock";
	public const string NoSnapping = "mouse.noSnapping";
	public const string ProportionalResize = "mouse.proportionalResize";
	public const string SymmetricResize = "mouse.symmetricResize";
	public const string MirrorHandle = "mouse.mirrorHandle";
	public const string NoAnchor = "mouse.noAnchor";
	public const string CutAxisLock = "mouse.cutAxisLock";
	public const string AddPixels = "mouse.addPixels";

	private static readonly List<MouseActionBinding> _all = Build();

	private static readonly Dictionary<string, MouseActionBinding> _byId =
		_all.ToDictionary(a => a.Id, StringComparer.Ordinal);

	public static IReadOnlyList<MouseActionBinding> All => _all;

	public static MouseActionBinding? Find(string id) =>
		_byId.TryGetValue(id, out var action) ? action : null;

	#region Проверка во время работы

	/// <summary>
	/// Вызвано ли действие этой кнопкой при этих модификаторах.
	///
	/// Модификаторы сверяются точно, а не «достаточно нажатых»: иначе
	/// Ctrl+средняя кнопка срабатывала бы как просто средняя, и действие
	/// с Ctrl отличить от действия без него было бы нечем.
	/// </summary>
	public static bool Matches(string id, MouseButtons button, Keys modifiers)
	{
		var action = Find(id);

		if (action == null || action.Input == MouseInput.None)
			return false;

		return action.Input == ToInput(button) && Normalize(modifiers) == action.Modifiers;
	}

	/// <summary>
	/// Держат ли модификаторы этого действия.
	///
	/// Здесь сверка нестрогая: достаточно, чтобы нужные клавиши были
	/// нажаты. Во время протяжки модификаторы накладываются — Ctrl держит
	/// ось, Alt одновременно снимает привязки, — и требовать точного
	/// совпадения значило бы запретить их сочетать.
	///
	/// Действие без модификаторов не держат никогда: пустой набор иначе
	/// совпадал бы с любым состоянием клавиш и действие было бы включено
	/// всегда.
	/// </summary>
	public static bool Held(string id, Keys modifiers)
	{
		var action = Find(id);

		if (action == null || action.Modifiers == Keys.None)
			return false;

		return (Normalize(modifiers) & action.Modifiers) == action.Modifiers;
	}

	public static MouseInput ToInput(MouseButtons button) => button switch
	{
		MouseButtons.Left => MouseInput.Left,
		MouseButtons.Middle => MouseInput.Middle,
		MouseButtons.Right => MouseInput.Right,
		_ => MouseInput.None
	};

	private static Keys Normalize(Keys keys) =>
		keys & (Keys.Control | Keys.Shift | Keys.Alt);

	#endregion

	#region Настройка

	public static void ResetToDefaults()
	{
		foreach (var action in _all)
		{
			action.Input = action.DefaultInput;
			action.Modifiers = action.DefaultModifiers;
		}
	}

	/// <summary>
	/// Действия, у которых совпало назначение.
	///
	/// Сравниваются только однородные: кнопочные с кнопочными,
	/// модификаторные с модификаторными. Ctrl у «добавить к выделению»
	/// и Ctrl у «держать ось» не спор: первое решается на щелчке,
	/// второе — на протяжке, и живут они одновременно с самого начала.
	/// А вот два разных действия на одной кнопке с одними модификаторами
	/// — спор: сработает только одно.
	/// </summary>
	public static HashSet<string> FindConflicts()
	{
		var seen = new Dictionary<(MouseInput, Keys), List<string>>();

		foreach (var action in _all)
		{
			if (action.ModifierOnly || action.Input == MouseInput.None)
				continue;

			var key = (action.Input, action.Modifiers);

			if (!seen.TryGetValue(key, out var list))
				seen[key] = list = new List<string>();

			list.Add(action.Id);
		}

		var conflicts = new HashSet<string>(StringComparer.Ordinal);

		foreach (var pair in seen)
		{
			if (pair.Value.Count > 1)
				conflicts.UnionWith(pair.Value);
		}

		return conflicts;
	}

	/// <summary>Подпись назначения: «Ctrl + средняя», «Alt», «нет».</summary>
	public static string Format(MouseActionBinding action)
	{
		string modifiers = FormatModifiers(action.Modifiers);
		string input = FormatInput(action.Input);

		if (input.Length == 0)
			return modifiers.Length > 0 ? modifiers : L.T("none");

		return modifiers.Length > 0 ? modifiers + " + " + input : input;
	}

	private static string FormatInput(MouseInput input) => input switch
	{
		MouseInput.Left => L.T("left button"),
		MouseInput.Middle => L.T("middle button"),
		MouseInput.Right => L.T("right button"),
		MouseInput.Wheel => L.T("wheel"),
		_ => string.Empty
	};

	private static string FormatModifiers(Keys modifiers)
	{
		var parts = new List<string>(3);

		// Названия модификаторов не переводятся: на клавиатуре они
		// подписаны одинаково в любой раскладке.
		if ((modifiers & Keys.Control) == Keys.Control)
			parts.Add("Ctrl");

		if ((modifiers & Keys.Shift) == Keys.Shift)
			parts.Add("Shift");

		if ((modifiers & Keys.Alt) == Keys.Alt)
			parts.Add("Alt");

		return string.Join(" + ", parts);
	}

	#endregion

	#region Сохранение

	/// <summary>
	/// Записываются только изменённые действия: список умолчаний живёт
	/// в коде, и запись его целиком означала бы, что добавленное позже
	/// действие придёт к старому пользователю уже «настроенным».
	/// </summary>
	public static string Serialize()
	{
		var parts = new List<string>();

		foreach (var action in _all)
		{
			if (!action.IsDefault)
				parts.Add($"{action.Id}={(int)action.Input}:{(int)action.Modifiers}");
		}

		return string.Join(";", parts);
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

			var action = Find(part[..eq]);

			if (action == null)
				continue;

			string[] fields = part[(eq + 1)..].Split(':');

			if (fields.Length != 2
				|| !int.TryParse(fields[0], out int input)
				|| !int.TryParse(fields[1], out int modifiers))
			{
				continue;
			}

			if (!Enum.IsDefined(typeof(MouseInput), input))
				continue;

			// Модификаторное действие кнопкой не становится, даже если
			// такое записано в настройках: испорченный или переписанный
			// руками файл не должен ломать протяжку.
			action.Input = action.ModifierOnly ? MouseInput.None : (MouseInput)input;
			action.Modifiers = Normalize((Keys)modifiers);
		}
	}

	#endregion

	#region Список действий

	private static List<MouseActionBinding> Build()
	{
		const string view = "Mouse: view";
		const string select = "Mouse: selection";
		const string move = "Mouse: moving and resizing";
		const string tools = "Mouse: shape tools";
		const string nodes = "Mouse: curve nodes";
		const string image = "Mouse: image editing";

		var list = new List<MouseActionBinding>
		{
			B(Pan, view, "Pan the view", MouseInput.Middle),
			B(ContextMenu, select, "Shape context menu", MouseInput.Right),
			B(ToolCancel, tools, "Cancel the tool, finish the polygon", MouseInput.Right),

			M(AddToSelection, select, "Add to the selection", Keys.Control),
			M(AxisLock, move, "Lock the movement to one axis", Keys.Control),
			M(NoSnapping, move, "Turn snapping off", Keys.Alt),
			M(ProportionalResize, move, "Keep the proportions", Keys.Control),
			M(SymmetricResize, move, "Resize around the centre", Keys.Shift),
			M(MirrorHandle, nodes, "Mirror the opposite control point", Keys.Shift),
			M(NoAnchor, tools, "Do not attach a measurement end", Keys.Control),
			// «По оси или диагонали»: ShapeCutter.ConstrainToAxis прижимает
			// линию и к диагоналям тоже, и подпись «по оси» обещала бы
			// меньше, чем происходит.
			M(CutAxisLock, tools, "Lock the cutting line to an axis or diagonal", Keys.Shift),
			M(AddPixels, image, "Add to the pixel selection", Keys.Control)
		};

		foreach (var action in list)
		{
			action.Input = action.DefaultInput;
			action.Modifiers = action.DefaultModifiers;
		}

		return list;
	}

	private static MouseActionBinding B(
		string id, string group, string title, MouseInput input, Keys modifiers = Keys.None)
		=> new()
		{
			Id = id,
			Group = group,
			Title = title,
			DefaultInput = input,
			DefaultModifiers = modifiers
		};

	private static MouseActionBinding M(string id, string group, string title, Keys modifiers)
		=> new()
		{
			Id = id,
			Group = group,
			Title = title,
			DefaultInput = MouseInput.None,
			DefaultModifiers = modifiers
		};

	#endregion
}
