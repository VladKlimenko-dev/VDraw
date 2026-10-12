using System.ComponentModel;
using System.Drawing.Drawing2D;

using VDraw.Editing;

namespace VDraw.Ui;

/// <summary>Один цвет палитры: сам цвет и подпись для подсказки.</summary>
public sealed class ColorPanelButton
{
	public Color Color { get; set; }

	public string Tip { get; set; }

	public ColorPanelButton(Color color, string tip = "")
	{
		Color = color;
		Tip = tip ?? string.Empty;
	}
}

/// <summary>
/// Полоса быстрого выбора цвета вдоль края окна.
///
/// В свёрнутом виде показывает один столбец, в развёрнутом — столько,
/// сколько нужно, чтобы поместились ВСЕ цвета палитры. Ширина при этом
/// считается от числа цветов и высоты панели, а не берётся из константы:
/// иначе добавленный цвет, попавший в новый столбец, оказывался бы
/// за краем панели и был бы недоступен.
///
/// Левая кнопка мыши назначает цвет заливки, правая — цвет обводки.
/// В режиме правки растра — цвет кисти и цвет фона соответственно.
/// </summary>
public class ColorsPanel : Panel
{
	#region Постоянные величины

	/// <summary>Отступ сетки от края панели.</summary>
	private const int GridPadding = 2;

	/// <summary>Место под кнопку сворачивания внизу панели.</summary>
	private const int FooterHeight = 26;

	private const int MinButtonSize = 8;
	private const int MaxButtonSize = 64;

	#endregion

	#region Поля

	private readonly List<ColorPanelButton> _colors = new();

	private readonly ToolTip _tooltip;

	private Image? _checkerboard;

	private readonly Button _expandButton;

	private int _buttonSize = 22;

	private bool _expanded;

	/// <summary>
	/// Цвет, к которому сейчас относится подсказка.
	///
	/// Нужен, чтобы не переустанавливать подсказку на каждое движение
	/// мыши: SetToolTip перезапускает её показ, и подсказка мигала бы,
	/// пока курсор идёт по одной и той же клетке.
	/// </summary>
	private int _tooltipIndex = -1;

	/// <summary>
	/// Ширина сейчас пересчитывается.
	///
	/// Присваивание Width поднимает Resize, а тот снова считает ширину.
	/// Без этого признака получилась бы бесконечная рекурсия на каждом
	/// изменении размера окна.
	/// </summary>
	private bool _updatingWidth;

	#endregion

	#region Свойства

	/// <summary>Цвета палитры. Правится через AddColor и RemoveColorAt.</summary>
	[Browsable(false)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public IReadOnlyList<ColorPanelButton> Colors => _colors;

	/// <summary>Сторона квадратика цвета в пикселях.</summary>
	[DefaultValue(22)]
	public int ButtonSize
	{
		get => _buttonSize;
		set
		{
			int size = Math.Clamp(value, MinButtonSize, MaxButtonSize);

			if (_buttonSize == size)
				return;

			_buttonSize = size;

			UpdateWidth();
			Invalidate();
		}
	}

	/// <summary>Развёрнута ли панель на все столбцы.</summary>
	[DefaultValue(false)]
	public bool Expanded
	{
		get => _expanded;
		set
		{
			if (_expanded == value)
				return;

			_expanded = value;
			_expandButton.Text = _expanded ? ">>" : "<<";

			UpdateWidth();
			Invalidate();
		}
	}

	/// <summary>Ссылка на активный холст. Задаётся снаружи при создании панели.</summary>
	[Browsable(false)]
	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public MainView? TargetView { get; set; }

	/// <summary>Сколько квадратиков помещается по высоте. Не меньше одного.</summary>
	private int RowCount
		=> Math.Max(1, (ClientSize.Height - GridPadding * 2 - FooterHeight) / _buttonSize);

	/// <summary>
	/// Сколько столбцов нужно, чтобы показать все цвета.
	///
	/// Деление с округлением вверх: последний, неполный столбец — тоже
	/// столбец, и без него крайние цвета остались бы за краем панели.
	/// </summary>
	private int ColumnCount
		=> _colors.Count == 0 ? 1 : (_colors.Count + RowCount - 1) / RowCount;

	private int CollapsedWidth => _buttonSize + GridPadding * 2 + 2;

	private int ExpandedWidth => ColumnCount * _buttonSize + GridPadding * 2 + 2;

	#endregion

	#region Создание

	public ColorsPanel()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
		SetStyle(ControlStyles.AllPaintingInWmPaint, true);
		SetStyle(ControlStyles.UserPaint, true);
		SetStyle(ControlStyles.ResizeRedraw, true);

		AddDefaultPalette();

		_tooltip = new ToolTip
		{
			AutoPopDelay = 2000,
			InitialDelay = 100,
			ReshowDelay = 2000,
			ShowAlways = false
		};

		_checkerboard = CreateCheckerboard();

		_expandButton = new Button
		{
			Text = "<<",
			Dock = DockStyle.Bottom,
			Name = "expand_but",
			Size = new Size(26, FooterHeight - 2),
			TabIndex = 0,
			UseVisualStyleBackColor = true
		};

		_expandButton.Click += (_, _) => Expanded = !Expanded;

		Controls.Add(_expandButton);

		Width = CollapsedWidth;
	}

	/// <summary>Шахматка, по которой виден прозрачный цвет.</summary>
	private static Image CreateCheckerboard()
	{
		var image = new Bitmap(8, 8);

		using var graphics = Graphics.FromImage(image);
		using var light = new SolidBrush(Color.White);
		using var dark = new SolidBrush(Color.FromArgb(255, 175, 175, 175));

		graphics.FillRectangle(light, 0, 0, 8, 8);
		graphics.FillRectangle(dark, 0, 0, 4, 4);
		graphics.FillRectangle(dark, 4, 4, 4, 4);

		return image;
	}

	#endregion

	#region Палитра

	/// <summary>
	/// Добавляет цвет и при необходимости расширяет панель.
	///
	/// Пересчёт ширины здесь обязателен: цвет, ушедший в новый столбец,
	/// иначе окажется за пределами панели и останется невидимым до тех
	/// пор, пока пользователь не изменит размер окна.
	/// </summary>
	public void AddColor(Color color, string tip = "")
	{
		_colors.Add(new ColorPanelButton(color, tip));

		UpdateWidth();
		Invalidate();
	}

	public void RemoveColorAt(int index)
	{
		if (index < 0 || index >= _colors.Count)
			return;

		_colors.RemoveAt(index);

		UpdateWidth();
		Invalidate();
	}

	public void ClearColors()
	{
		_colors.Clear();

		UpdateWidth();
		Invalidate();
	}

	private void Add(Color color, string tip) => _colors.Add(new ColorPanelButton(color, tip));

	/// <summary>
	/// Набор цветов по умолчанию.
	///
	/// Порядок — по кругу основных цветов, внутри группы от светлого
	/// к тёмному, по семь оттенков в группе. Одинаковый размер групп
	/// не для красоты: он ставит группы ровными рядами и позволяет
	/// находить оттенок по месту, а не перечитывая подсказки.
	/// </summary>
	private void AddDefaultPalette()
	{
		Add(Color.Transparent, "Transparent");
		Add(Color.Black, "Black");
		Add(Color.White, "White");
		Add(Color.Red, "Red");
		Add(Color.Green, "Green");
		Add(Color.Blue, "Blue");
		Add(Color.FromArgb((int)(255 * 0.1), (int)(255 * 0.1), (int)(255 * 0.1)), "Gray 10%");
		Add(Color.FromArgb((int)(255 * 0.2), (int)(255 * 0.2), (int)(255 * 0.2)), "Gray 20%");
		Add(Color.FromArgb((int)(255 * 0.25), (int)(255 * 0.25), (int)(255*0.25)), "Gray 25%");
		Add(Color.FromArgb((int)(255 * 0.3), (int)(255 * 0.3), (int)(255 * 0.3)), "Gray 30%");
		Add(Color.FromArgb((int)(255 * 0.4), (int)(255 * 0.4), (int)(255 * 0.4)), "Gray 40%");
		Add(Color.FromArgb((int)(255 * 0.5), (int)(255 * 0.5), (int)(255 * 0.5)), "Gray 50%");
		Add(Color.FromArgb((int)(255 * 0.6), (int)(255 * 0.6), (int)(255 * 0.6)), "Gray 60%");
		Add(Color.FromArgb((int)(255 * 0.7), (int)(255 * 0.7), (int)(255 * 0.7)), "Gray 70%");
		Add(Color.FromArgb((int)(255 * 0.75), (int)(255 * 0.75), (int)(255 * 0.75)), "Gray 75%");
		Add(Color.FromArgb((int)(255 * 0.8), (int)(255 * 0.8), (int)(255 * 0.8)), "Gray 80%");
		Add(Color.FromArgb((int)(255 * 0.9), (int)(255 * 0.9), (int)(255 * 0.9)), "Gray 90%");

		// --- Красные -------------------------------------------------------
		Add(Color.FromArgb(0xFF, 0xC0, 0xCB), "Pink");
		Add(Color.FromArgb(0xF0, 0x80, 0x80), "Light coral");
		Add(Color.FromArgb(0xFA, 0x80, 0x72), "Salmon");
		Add(Color.FromArgb(0xFF, 0x7F, 0x50), "Coral");
		Add(Color.FromArgb(0xDC, 0x14, 0x3C), "Crimson");
		Add(Color.FromArgb(0xB2, 0x22, 0x22), "Firebrick");
		Add(Color.FromArgb(0x8B, 0x00, 0x00), "Dark red");

		// --- Оранжевые и коричневые ----------------------------------------
		Add(Color.FromArgb(0xFF, 0xDA, 0xB9), "Peach");
		Add(Color.FromArgb(0xE9, 0xCF, 0xA1), "Sand");
		Add(Color.FromArgb(0xFF, 0xC0, 0x4D), "Light orange");
		Add(Color.FromArgb(0xFF, 0xA5, 0x00), "Orange");
		Add(Color.FromArgb(0xFF, 0x8C, 0x00), "Dark orange");
		Add(Color.FromArgb(0xD2, 0x69, 0x1E), "Chocolate");
		Add(Color.FromArgb(0x8B, 0x45, 0x13), "Brown");

		// --- Жёлтые и оливковые --------------------------------------------
		Add(Color.FromArgb(0xFF, 0xF3, 0xC4), "Cream");
		Add(Color.FromArgb(0xFF, 0xF9, 0xA0), "Light yellow");
		Add(Color.FromArgb(0xFF, 0xFF, 0x00), "Yellow");
		Add(Color.FromArgb(0xFF, 0xD7, 0x00), "Gold");
		Add(Color.FromArgb(0xFF, 0xBF, 0x00), "Amber");
		Add(Color.FromArgb(0xC9, 0xA2, 0x27), "Dark gold");
		Add(Color.FromArgb(0x80, 0x80, 0x00), "Olive");

		// --- Зелёные --------------------------------------------------------
		Add(Color.FromArgb(0x98, 0xFF, 0x98), "Mint");
		Add(Color.FromArgb(0x90, 0xEE, 0x90), "Light green");
		Add(Color.FromArgb(0x32, 0xCD, 0x32), "Lime");
		Add(Color.FromArgb(0x4C, 0xAF, 0x50), "Grass");
		Add(Color.FromArgb(0x2E, 0x8B, 0x57), "Sea green");
		Add(Color.FromArgb(0x22, 0x8B, 0x22), "Forest green");
		Add(Color.FromArgb(0x00, 0x64, 0x00), "Dark green");

		// --- Бирюзовые ------------------------------------------------------
		Add(Color.FromArgb(0xE0, 0xFF, 0xFF), "Ice");
		Add(Color.FromArgb(0x7F, 0xFF, 0xD4), "Aquamarine");
		Add(Color.FromArgb(0x40, 0xE0, 0xD0), "Turquoise");
		Add(Color.FromArgb(0x00, 0xFF, 0xFF), "Cyan");
		Add(Color.FromArgb(0x20, 0xB2, 0xAA), "Light sea green");
		Add(Color.FromArgb(0x00, 0x80, 0x80), "Teal");
		Add(Color.FromArgb(0x00, 0x56, 0x5B), "Dark teal");

		// --- Синие ----------------------------------------------------------
		Add(Color.FromArgb(0xAD, 0xD8, 0xE6), "Light blue");
		Add(Color.FromArgb(0x87, 0xCE, 0xEB), "Sky blue");
		Add(Color.FromArgb(0x64, 0x95, 0xED), "Cornflower");
		Add(Color.FromArgb(0x46, 0x82, 0xB4), "Steel blue");
		Add(Color.FromArgb(0x00, 0x7F, 0xFF), "Azure");
		Add(Color.FromArgb(0x41, 0x69, 0xE1), "Royal blue");
		Add(Color.FromArgb(0x00, 0x00, 0x80), "Navy");

		// --- Фиолетовые и пурпурные -----------------------------------------
		Add(Color.FromArgb(0xE6, 0xE6, 0xFA), "Lavender");
		Add(Color.FromArgb(0xD8, 0xBF, 0xD8), "Thistle");
		Add(Color.FromArgb(0xDA, 0x70, 0xD6), "Orchid");
		Add(Color.FromArgb(0xEE, 0x82, 0xEE), "Violet");
		Add(Color.FromArgb(0xFF, 0x00, 0xFF), "Magenta");
		Add(Color.FromArgb(0x80, 0x00, 0x80), "Purple");
		Add(Color.FromArgb(0x4B, 0x00, 0x82), "Indigo");
	}

	#endregion

	#region Раскладка

	/// <summary>
	/// Подгоняет ширину под текущее состояние и число цветов.
	///
	/// Раскладка живёт ЗДЕСЬ, а не в OnPaint: изменение ширины во время
	/// отрисовки вызывает новую отрисовку, и панель либо мигает, либо
	/// уходит в бесконечный круг.
	/// </summary>
	private void UpdateWidth()
	{
		if (_updatingWidth || !IsHandleCreated)
			return;

		int target = _expanded ? ExpandedWidth : CollapsedWidth;

		if (Width == target)
			return;

		_updatingWidth = true;

		try
		{
			Width = target;
		}
		finally
		{
			_updatingWidth = false;
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);

		// Высота изменилась — в столбец помещается другое число цветов,
		// значит изменилось и число столбцов.
		UpdateWidth();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);

		UpdateWidth();
	}

	#endregion

	#region Отрисовка

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		if (_colors.Count == 0)
			return;

		var grid = new Rectangle(
			GridPadding,
			GridPadding,
			Math.Max(0, Width - GridPadding * 2),
			Math.Max(0, ClientSize.Height - GridPadding * 2 - FooterHeight));

		// Шахматка кладётся только под сетку: она объясняет прозрачный
		// цвет, а под кнопкой сворачивания объяснять нечего.
		if (_checkerboard != null && grid.Width > 0 && grid.Height > 0)
		{
			using var texture = new TextureBrush(_checkerboard) { WrapMode = WrapMode.Tile };
			e.Graphics.FillRectangle(texture, grid);
		}

		int rows = RowCount;
		int columns = _expanded ? ColumnCount : 1;

		using var border = new Pen(Color.Gray, 1f);

		for (int index = 0; index < _colors.Count; index++)
		{
			int column = index / rows;
			int row = index % rows;

			// В свёрнутом виде рисуется только первый столбец: остальные
			// всё равно за краем панели.
			if (column >= columns)
				break;

			var cell = new Rectangle(
				GridPadding + column * _buttonSize,
				GridPadding + row * _buttonSize,
				_buttonSize,
				_buttonSize);

			using var brush = new SolidBrush(_colors[index].Color);

			e.Graphics.FillRectangle(brush, cell);
			e.Graphics.DrawRectangle(border, cell);
		}
	}

	#endregion

	#region Мышь

	/// <summary>
	/// Номер цвета под точкой, или -1.
	///
	/// Проверяются обе оси отдельно. Прежний расчёт делил координаты
	/// без проверки границ, и щелчок правее последнего столбца или ниже
	/// последней строки попадал в чужую клетку — палитра назначала цвет,
	/// по которому никто не щёлкал.
	/// </summary>
	public int GetIndex(Point point)
	{
		int rows = RowCount;
		int columns = _expanded ? ColumnCount : 1;

		int x = point.X - GridPadding;
		int y = point.Y - GridPadding;

		if (x < 0 || y < 0)
			return -1;

		int column = x / _buttonSize;
		int row = y / _buttonSize;

		if (column >= columns || row >= rows)
			return -1;

		int index = column * rows + row;

		return index < _colors.Count ? index : -1;
	}

	/// <summary>Цвет под точкой, или DarkGray, если там пусто.</summary>
	public Color GetColor(Point point)
	{
		int index = GetIndex(point);

		return index >= 0 ? _colors[index].Color : Color.DarkGray;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		int index = GetIndex(e.Location);

		if (index == _tooltipIndex)
			return;

		_tooltipIndex = index;

		_tooltip.SetToolTip(this, index >= 0 ? _colors[index].Tip : string.Empty);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);

		_tooltipIndex = -1;
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		base.OnMouseClick(e);

		if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
			return;

		int index = GetIndex(e.Location);

		if (index < 0)
		{
			// Щелчок по пустому месту сетки — приглашение добавить цвет.
			// Только левой кнопкой: правая на пустом месте почти всегда
			// промах, и диалог в ответ на промах раздражает.
			if (e.Button == MouseButtons.Left)
				AddNewColor();

			return;
		}

		ApplyColor(_colors[index].Color, e.Button == MouseButtons.Left);
	}

	/// <summary>
	/// Отдаёт цвет главному окну.
	///
	/// Палитра относится к ВЫДЕЛЕННЫМ ФИГУРАМ, а режим редактора говорит
	/// лишь о том, чем фигуру правят прямо сейчас. Поэтому вопрос здесь
	/// один: растр это или вектор.
	///
	/// Проверка на один только EditorMode.Shape оставляла текст без
	/// реакции на палитру: выделение TextShape переводит редактор
	/// в EditorMode.Text, и обе ветки молча пропускались. То же самое
	/// было в режиме правки узлов — EditorMode.Curve.
	/// </summary>
	private void ApplyColor(Color color, bool primary)
	{
		var form = FindMainForm();

		if (form == null)
			return;

		bool raster = MainForm.Mode == EditorMode.Image;

		if (primary)
		{
			if (raster)
				form.SetImageDefaultFrontColor(color);
			else
				form.SetShapeDefaultFillColor(color);
		}
		else
		{
			if (raster)
				form.SetImageDefaultBackColor(color);
			else
				form.SetShapeDefaultLineColor(color);
		}
	}

	private MainForm? FindMainForm()
	{
		for (Control? c = Parent; c != null; c = c.Parent)
		{
			if (c is MainForm form)
				return form;
		}

		return null;
	}

	private void AddNewColor()
	{
		using var dialog = new SColorDialog();

		if (dialog.ShowDialog(this) != DialogResult.OK)
			return;

		AddColor(dialog.GetColor());
	}

	#endregion

	#region Освобождение

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_tooltip?.Dispose();

			_checkerboard?.Dispose();
			_checkerboard = null;
		}

		base.Dispose(disposing);
	}

	#endregion
}
