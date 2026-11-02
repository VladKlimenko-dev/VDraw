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

	private readonly List<Row> _rows = new();

	/// <summary>
	/// Подписи с кнопками мыши. Хранятся отдельно, потому что красятся
	/// иначе, чем сочетания клавиш: они не нажимаются, и выглядеть
	/// нажимаемыми не должны.
	/// </summary>
	private readonly List<Label> _mouseGestures = new();

	private readonly TableLayoutPanel _table;
	private readonly Button _resetButton;
	private readonly Label _hint;

	/// <summary>Строка, ожидающая нажатия. null — захвата нет.</summary>
	private Row? _capturing;

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

		_hint = new Label
		{
			Dock = DockStyle.Top,
			Height = 56,
			Text = L.T(
				"Click a shortcut and press the new key combination.\r\n"
				+ "Esc cancels, Delete clears. Duplicates are shown in red.\r\n"
				+ "The mouse buttons at the end of the list are a reference and cannot be changed.")
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
	/// Справка по кнопкам мыши в конце того же списка.
	///
	/// Отдельной вкладки не заводим: пользователь ищет здесь ответ на один
	/// и тот же вопрос — «чем это делается», — и разводить ответ по двум
	/// местам значило бы заставлять его помнить, клавиша нужна или кнопка,
	/// ещё до того, как он это выяснил.
	///
	/// Столбцы те же, что у сочетаний: справа — то, что нажимают, слева —
	/// что из этого выходит. Строки не нажимаются, поэтому у них нет ни
	/// рамки, ни курсора-руки, а текст приглушён.
	/// </summary>
	private void BuildMouseRows()
	{
		string? lastGroup = null;

		foreach (var action in MouseActions.All)
		{
			if (action.Group != lastGroup)
			{
				lastGroup = action.Group;
				AddGroupHeader(action.Group);
			}

			AddMouseRow(action);
		}
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
		if (_capturing == null)
			return false;

		Keys key = keyData & System.Windows.Forms.Keys.KeyCode;

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

	#region Отрисовка состояния

	private void ResetAll()
	{
		Shortcuts.ResetToDefaults();

		_capturing = null;

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

		_resetButton.FlatStyle = FlatStyle.Flat;
		_resetButton.BackColor = ToColor(pal.Surface);
		_resetButton.ForeColor = ToColor(pal.Text);
		_resetButton.FlatAppearance.BorderColor = ToColor(pal.Border);

		foreach (Control control in _table.Controls)
		{
			if (control is not Label label)
				continue;

			// Поля с сочетаниями красит PaintKeys: у них своё состояние —
			// наведение, захват и совпадение.
			if (_rows.Any(r => ReferenceEquals(r.Keys, label)))
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
	}

	private static Color ToColor(SKColor c)
		=> Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	#endregion
}
