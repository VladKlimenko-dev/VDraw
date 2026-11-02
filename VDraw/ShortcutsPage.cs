using SkiaSharp;

using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Список сочетаний клавиш для окна настроек.
///
/// Сам красит себя из палитры: обход WinFormsTheme пропускает контролы
/// нашей сборки (см. IsOwnDrawnControl) и внутрь них не заходит, поэтому
/// цвета всем вложенным подписям задаются здесь.
/// </summary>
public sealed class ShortcutsPage : Panel
{
	private sealed class Row
	{
		public required ShortcutAction Action { get; init; }
		public required Label Title { get; init; }
		public required Label Keys { get; init; }

		/// <summary>Сочетание повторяется у другого действия.</summary>
		public bool Conflicted { get; set; }
	}

	/// <summary>Строка настраиваемого действия мыши.</summary>
	private sealed class MouseRow
	{
		public required MouseActionBinding Action { get; init; }
		public required Label Title { get; init; }
		public required Label Binding { get; init; }

		/// <summary>Назначение повторяется у другого действия.</summary>
		public bool Conflicted { get; set; }
	}

	private readonly List<Row> _rows = new();
	private readonly List<MouseRow> _mouseRows = new();

	/// <summary>
	/// Подписи неизменяемых действий мыши. Хранятся отдельно, потому что
	/// красятся иначе: они не нажимаются и выглядеть нажимаемыми
	/// не должны.
	/// </summary>
	private readonly List<Label> _mouseGestures = new();

	private readonly TableLayoutPanel _table;
	private readonly Button _resetButton;
	private readonly Button _resetMouseButton;
	private readonly Label _hint;

	/// <summary>Строка, ожидающая нажатия. null — захвата нет.</summary>
	private Row? _capturing;

	/// <summary>Строка мыши, ожидающая щелчка. null — захвата нет.</summary>
	private MouseRow? _capturingMouse;

	public ShortcutsPage()
	{
		Dock = DockStyle.Fill;
		Padding = new Padding(8);

		_resetButton = new Button
		{
			Text = L.T("Reset keys to default"),
			AutoSize = true,
			Dock = DockStyle.Top,
			Height = 30,
			UseVisualStyleBackColor = true
		};

		_resetButton.Click += (_, _) => ResetAll();

		_resetMouseButton = new Button
		{
			Text = L.T("Reset mouse to default"),
			AutoSize = true,
			Dock = DockStyle.Top,
			Height = 30,
			UseVisualStyleBackColor = true
		};

		_resetMouseButton.Click += (_, _) => ResetMouse();

		_hint = new Label
		{
			Dock = DockStyle.Top,
			Height = 72,
			Text = L.T(
				"Click a shortcut and press the new key combination.\r\n"
				+ "Click a mouse assignment, then click the mouse button you want,\r\n"
				+ "holding the modifiers. Esc cancels, Delete clears.\r\n"
				+ "Duplicates are shown in red. Muted lines are a reference and cannot be changed.")
		};

		_table = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			ColumnCount = 2,
			GrowStyle = TableLayoutPanelGrowStyle.AddRows
		};

		_table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
		_table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

		BuildRows();
		BuildMouseRows();

		// Порядок добавления обратный порядку стыковки: добавленное
		// последним встаёт ближе к краю, поэтому список идёт первым,
		// а подсказка и кнопка оказываются над ним.
		Controls.Add(_table);
		Controls.Add(_hint);
		Controls.Add(_resetMouseButton);
		Controls.Add(_resetButton);

		ApplyTheme();
		RefreshAll();

		SkUiTheme.ThemeChanged += ApplyTheme;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			SkUiTheme.ThemeChanged -= ApplyTheme;

		base.Dispose(disposing);
	}

	#region Построение списка

	private void BuildRows()
	{
		string? lastGroup = null;

		foreach (var action in Shortcuts.All)
		{
			if (action.Group != lastGroup)
			{
				lastGroup = action.Group;
				AddGroupHeader(action.Group);
			}

			AddActionRow(action);
		}
	}

	private void AddGroupHeader(string group)
	{
		var header = new Label
		{
			Text = L.T(group),
			Dock = DockStyle.Fill,
			Height = 30,
			Padding = new Padding(0, 10, 0, 2),
			Font = new Font(Font, FontStyle.Bold),
			TextAlign = ContentAlignment.BottomLeft
		};

		int row = _table.RowCount;

		_table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		_table.Controls.Add(header, 0, row);
		_table.SetColumnSpan(header, 2);
		_table.RowCount = row + 1;
	}

	private void AddActionRow(ShortcutAction action)
	{
		var title = new Label
		{
			Text = L.T(action.Title),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(12, 0, 0, 0)
		};

		var keys = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(6, 0, 0, 0),
			Cursor = Cursors.Hand,
			BorderStyle = BorderStyle.FixedSingle
		};

		var row = new Row { Action = action, Title = title, Keys = keys };

		keys.MouseEnter += (_, _) => PaintKeys(row, hovered: true);
		keys.MouseLeave += (_, _) => PaintKeys(row, hovered: false);
		keys.Click += (_, _) => BeginCapture(row);

		int index = _table.RowCount;

		_table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		_table.Controls.Add(title, 0, index);
		_table.Controls.Add(keys, 1, index);
		_table.RowCount = index + 1;

		_rows.Add(row);
	}

	/// <summary>
	/// Действия мыши в конце того же списка.
	///
	/// Отдельной вкладки не заводим: пользователь ищет здесь ответ на один
	/// и тот же вопрос — «чем это делается», — и разводить ответ по двум
	/// местам значило бы заставлять его помнить, клавиша нужна или кнопка,
	/// ещё до того, как он это выяснил.
	///
	/// В каждой группе сначала настраиваемые действия, потом неизменяемые.
	/// Настраиваемые выглядят как строки сочетаний — рамка и курсор-рука,
	/// — а справочные приглушены и рамки не имеют: разница в виде отвечает
	/// на вопрос «это можно поменять?» раньше, чем пользователь щёлкнет.
	///
	/// Столбцы те же, что у сочетаний: справа — то, что нажимают, слева —
	/// что из этого выходит.
	/// </summary>
	private void BuildMouseRows()
	{
		foreach (string group in MouseGroupsInOrder())
		{
			AddGroupHeader(group);

			foreach (var action in MouseBindings.All)
			{
				if (action.Group == group)
					AddMouseBindingRow(action);
			}

			foreach (var action in MouseActions.All)
			{
				if (action.Group == group)
					AddMouseRow(action);
			}
		}
	}

	/// <summary>
	/// Группы действий мыши в порядке появления, без повторов.
	///
	/// Берутся из обоих списков: в одной группе может не быть
	/// настраиваемых действий, в другой — справочных.
	/// </summary>
	private static List<string> MouseGroupsInOrder()
	{
		var groups = new List<string>();

		void Add(string group)
		{
			if (!groups.Contains(group, StringComparer.Ordinal))
				groups.Add(group);
		}

		foreach (var action in MouseActions.All)
			Add(action.Group);

		foreach (var action in MouseBindings.All)
			Add(action.Group);

		return groups;
	}

	private void AddMouseBindingRow(MouseActionBinding action)
	{
		var title = new Label
		{
			Text = L.T(action.Title),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(12, 0, 0, 0)
		};

		var binding = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(6, 0, 0, 0),
			Cursor = Cursors.Hand,
			BorderStyle = BorderStyle.FixedSingle
		};

		var row = new MouseRow { Action = action, Title = title, Binding = binding };

		binding.MouseEnter += (_, _) => PaintMouseBinding(row, hovered: true);
		binding.MouseLeave += (_, _) => PaintMouseBinding(row, hovered: false);

		// Щелчок разбирается на нажатии, а не на Click: назначить нужно
		// именно кнопку, а Click приходит только от левой.
		binding.MouseDown += (_, e) => OnMouseBindingPressed(row, e);

		int index = _table.RowCount;

		_table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		_table.Controls.Add(title, 0, index);
		_table.Controls.Add(binding, 1, index);
		_table.RowCount = index + 1;

		_mouseRows.Add(row);
	}

	private void AddMouseRow(MouseAction action)
	{
		var description = new Label
		{
			Text = L.T(action.Description),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(12, 0, 0, 0)
		};

		var gesture = new Label
		{
			Text = L.T(action.Gesture),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(6, 0, 0, 0)
		};

		int index = _table.RowCount;

		_table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		_table.Controls.Add(description, 0, index);
		_table.Controls.Add(gesture, 1, index);
		_table.RowCount = index + 1;

		_mouseGestures.Add(gesture);
	}

	#endregion

	#region Захват нажатия

	private void BeginCapture(Row row)
	{
		// Прежний захват отменяется молча: пользователь передумал и выбрал
		// другую строку, и спрашивать его об этом не о чем.
		if (_capturing != null && _capturing != row)
			CancelCapture();

		_capturing = row;
		row.Keys.Text = L.T("Press keys...");

		PaintKeys(row, hovered: true);
	}

	private void CancelCapture()
	{
		var row = _capturing;

		_capturing = null;

		if (row != null)
			RefreshRow(row);
	}

	/// <summary>
	/// Нажатие во время захвата. true — клавиша занята и дальше не идёт.
	///
	/// Вызывается из ProcessCmdKey окна настроек: только там видны
	/// сочетания целиком, включая Tab и стрелки, которые контролы
	/// считают своими.
	/// </summary>
	public bool HandleCaptureKey(Keys keyData)
	{
		Keys pressed = keyData & System.Windows.Forms.Keys.KeyCode;

		// Захват мыши тоже отменяют и очищают с клавиатуры: кнопок у мыши
		// три, и занимать одну из них под «передумал» значило бы отнять
		// её у назначений.
		if (_capturingMouse != null)
		{
			if (pressed == System.Windows.Forms.Keys.Escape)
			{
				CancelMouseCapture();

				return true;
			}

			if (pressed == System.Windows.Forms.Keys.Delete
				|| pressed == System.Windows.Forms.Keys.Back)
			{
				AssignMouse(_capturingMouse, MouseInput.None, System.Windows.Forms.Keys.None);

				return true;
			}

			// Остальные нажатия при захвате мыши съедаются: это
			// модификаторы, которые пользователь набирает перед щелчком.
			return true;
		}

		if (_capturing == null)
			return false;

		Keys key = pressed;

		if (key == System.Windows.Forms.Keys.Escape)
		{
			CancelCapture();

			return true;
		}

		if (key == System.Windows.Forms.Keys.Delete || key == System.Windows.Forms.Keys.Back)
		{
			Assign(_capturing, System.Windows.Forms.Keys.None);

			return true;
		}

		// Одни модификаторы приходят отдельными нажатиями, пока
		// пользователь набирает сочетание. Это не выбор, а его начало.
		if (!Shortcuts.IsAssignable(keyData))
			return true;

		Assign(_capturing, keyData);

		return true;
	}

	private void Assign(Row row, Keys keys)
	{
		row.Action.Keys = keys;
		_capturing = null;

		// Перерисовывается весь список, а не одна строка: назначенное
		// сочетание могло освободить или, наоборот, создать совпадение
		// в другом месте, и показать это нужно сразу.
		RefreshAll();
	}

	#endregion

	#region Захват щелчка мыши

	/// <summary>
	/// Щелчок по назначению: первый начинает захват, следующий назначает.
	///
	/// Иначе было бы нечем назначить саму левую кнопку: щелчок, которым
	/// строку выбирают, и щелчок, который назначают, — один и тот же.
	/// </summary>
	private void OnMouseBindingPressed(MouseRow row, MouseEventArgs e)
	{
		if (_capturingMouse != row)
		{
			BeginMouseCapture(row);

			return;
		}

		Keys modifiers = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);

		// У действия без кнопки назначаются одни модификаторы: протяжка
		// уже занята тем, что тащат, и кнопке там места нет.
		MouseInput input = row.Action.ModifierOnly
			? MouseInput.None
			: MouseBindings.ToInput(e.Button);

		AssignMouse(row, input, modifiers);
	}

	private void BeginMouseCapture(MouseRow row)
	{
		CancelCapture();

		if (_capturingMouse != null && _capturingMouse != row)
			CancelMouseCapture();

		_capturingMouse = row;

		row.Binding.Text = row.Action.ModifierOnly
			? L.T("Hold the keys and click...")
			: L.T("Click a mouse button...");

		PaintMouseBinding(row, hovered: true);
	}

	private void CancelMouseCapture()
	{
		var row = _capturingMouse;

		_capturingMouse = null;

		if (row != null)
			RefreshMouseRow(row);
	}

	private void AssignMouse(MouseRow row, MouseInput input, Keys modifiers)
	{
		row.Action.Input = input;
		row.Action.Modifiers = modifiers;

		_capturingMouse = null;

		RefreshAll();
	}

	#endregion

	#region Отрисовка состояния

	private void ResetAll()
	{
		Shortcuts.ResetToDefaults();

		_capturing = null;

		RefreshAll();
	}

	private void ResetMouse()
	{
		MouseBindings.ResetToDefaults();

		_capturingMouse = null;

		RefreshAll();
	}

	public void RefreshAll()
	{
		var conflicts = Shortcuts.FindConflicts();

		foreach (var row in _rows)
		{
			row.Conflicted = conflicts.Contains(row.Action.Id);
			RefreshRow(row);
		}

		var mouseConflicts = MouseBindings.FindConflicts();

		foreach (var row in _mouseRows)
		{
			row.Conflicted = mouseConflicts.Contains(row.Action.Id);
			RefreshMouseRow(row);
		}
	}

	private void RefreshMouseRow(MouseRow row)
	{
		if (_capturingMouse == row)
			return;

		row.Binding.Text = MouseBindings.Format(row.Action);

		PaintMouseBinding(row, hovered: false);
	}

	private void PaintMouseBinding(MouseRow row, bool hovered)
	{
		var pal = SkUiTheme.Current;

		bool capturing = _capturingMouse == row;

		row.Binding.BackColor = ToColor(
			capturing ? pal.Selected : hovered ? pal.Surface2 : pal.Surface);

		row.Binding.ForeColor = row.Conflicted
			? Color.FromArgb(220, 60, 50)
			: ToColor(pal.Text);
	}

	private void RefreshRow(Row row)
	{
		if (_capturing == row)
			return;

		string text = Shortcuts.Format(row.Action.Keys);

		row.Keys.Text = text.Length > 0 ? text : L.T("none");

		PaintKeys(row, hovered: false);
	}

	private void PaintKeys(Row row, bool hovered)
	{
		var pal = SkUiTheme.Current;

		bool capturing = _capturing == row;

		row.Keys.BackColor = ToColor(
			capturing ? pal.Selected : hovered ? pal.Surface2 : pal.Surface);

		row.Keys.ForeColor = row.Conflicted
			? Color.FromArgb(220, 60, 50)
			: ToColor(pal.Text);
	}

	private void ApplyTheme()
	{
		var pal = SkUiTheme.Current;

		BackColor = ToColor(pal.PanelBack);
		_table.BackColor = ToColor(pal.PanelBack);

		_hint.BackColor = Color.Transparent;
		_hint.ForeColor = ToColor(pal.TextMuted);

		foreach (var button in new[] { _resetButton, _resetMouseButton })
		{
			button.FlatStyle = FlatStyle.Flat;
			button.BackColor = ToColor(pal.Surface);
			button.ForeColor = ToColor(pal.Text);
			button.FlatAppearance.BorderColor = ToColor(pal.Border);
		}

		foreach (Control control in _table.Controls)
		{
			if (control is not Label label)
				continue;

			// Поля с сочетаниями и назначениями мыши красятся отдельно:
			// у них своё состояние — наведение, захват и совпадение.
			if (_rows.Any(r => ReferenceEquals(r.Keys, label)))
				continue;

			if (_mouseRows.Any(r => ReferenceEquals(r.Binding, label)))
				continue;

			label.BackColor = Color.Transparent;

			// Кнопки мыши приглушены: они справка, а не настройка, и ровный
			// с сочетаниями цвет читался бы как «это тоже можно нажать».
			label.ForeColor = _mouseGestures.Contains(label)
				? ToColor(pal.TextMuted)
				: ToColor(pal.Text);
		}

		foreach (var row in _rows)
			PaintKeys(row, hovered: false);

		foreach (var row in _mouseRows)
			PaintMouseBinding(row, hovered: false);
	}

	private static Color ToColor(SKColor c)
		=> Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	#endregion
}
