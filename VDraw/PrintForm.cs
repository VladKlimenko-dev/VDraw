using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Диалог печати документа: настройки принтера слева, предпросмотр справа.
///
/// Форма собирается кодом, без файла дизайнера. Причина одна: раскладка
/// целиком сеточная и вычисляемая, а дизайнер хранил бы её как сотню
/// абсолютных координат, которые пришлось бы править вручную при каждой
/// правке состава полей.
///
/// Печать идёт через растр: страница рисуется PageRenderer в разрешении
/// принтера, затем кладётся на Graphics. Прямой вывод фигур в Graphics
/// потребовал бы второй реализации всей отрисовки — с градиентами,
/// паттернами и коррекциями изображений, — и бумага разошлась бы с экраном.
/// </summary>
public sealed class PrintForm : Form
{
	#region Элемент списка форматов

	/// <summary>
	/// Обёртка над PaperSize ради читаемой строки в списке.
	///
	/// Собственный ToString у PaperSize возвращает отладочную запись вида
	/// [PaperSize A4 Kind=A4 Height=1169 Width=827] — показывать её пользователю
	/// нельзя. Размер в миллиметрах дописан рядом: названия вроде «Legal»
	/// или «16K» сами по себе ничего не говорят.
	/// </summary>
	private sealed class PaperItem
	{
		public PaperSize Size { get; }

		public PaperItem(PaperSize size) => Size = size;

		public override string ToString()
		{
			float w = Size.Width / 100f * 25.4f;
			float h = Size.Height / 100f * 25.4f;

			return $"{Size.PaperName}  ({w:0} × {h:0} mm)";
		}
	}

	#endregion

	#region Поля

	private readonly Document _doc;

	private readonly PrintDocument _printDoc = new();

	private ComboBox _printerCombo = null!;
	private Button _propertiesBut = null!;
	private ComboBox _paperCombo = null!;

	/// <summary>Пока true, обработчики списков не трогают модель и не перестраивают вид.</summary>
	private bool _suppressUi;

	private RadioButton _rangeAll = null!;
	private RadioButton _rangeCurrent = null!;
	private RadioButton _rangeCustom = null!;
	private TextBox _rangeText = null!;

	private NumericUpDown _copies = null!;
	private CheckBox _collate = null!;

	private RadioButton _orientPortrait = null!;
	private RadioButton _orientLandscape = null!;

	private ComboBox _scaleMode = null!;
	private NumericUpDown _scalePercent = null!;
	private CheckBox _centre = null!;
	private CheckBox _background = null!;

	private CheckBox _showMargins = null!;

	private Panel _previewHost = null!;
	private PreviewPanel _preview = null!;

	private Button _firstBut = null!;
	private Button _prevBut = null!;
	private Button _nextBut = null!;
	private Button _lastBut = null!;
	private Label _pageLabel = null!;

	private ComboBox _zoomCombo = null!;

	private int _previewIndex;
	private int _printIndex;
	private List<int> _printQueue = new();

	#endregion

	#region Создание

	public PrintForm(Document doc)
	{
		_doc = doc ?? throw new ArgumentNullException(nameof(doc));
		WinFormsTheme.Register(this);

		Text = "Print";
		StartPosition = FormStartPosition.CenterParent;
		MinimumSize = new Size(920, 620);
		Size = new Size(1060, 720);

		BuildUi();

		_printDoc.PrintPage += OnPrintPage;
		_printDoc.QueryPageSettings += OnQueryPageSettings;

		LoadPrinters();
		SyncOrientationFromPage();

		_previewIndex = Math.Max(0, _doc.pages.IndexOf(_doc.ActivePage ?? _doc.pages.FirstOrDefault()!));
		UpdatePreview();
	}

	#endregion

	#region Построение интерфейса

	private void BuildUi()
	{
		var root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(10)
		};

		root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

		root.Controls.Add(BuildSettingsColumn(), 0, 0);
		root.Controls.Add(BuildPreviewColumn(), 1, 0);

		Controls.Add(root);
		Controls.Add(BuildButtonsRow());
	}

	private Control BuildSettingsColumn()
	{
		var column = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			AutoScroll = true
		};

		column.Controls.Add(BuildPrinterGroup());
		column.Controls.Add(BuildRangeGroup());
		column.Controls.Add(BuildCopiesGroup());
		column.Controls.Add(BuildLayoutGroup());

		return column;
	}

	private Control BuildPrinterGroup()
	{
		var box = new GroupBox { Text = "Printer", Width = 305, Height = 150 };

		_printerCombo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Location = new Point(12, 24),
			Width = 281
		};

		_printerCombo.SelectedIndexChanged += (_, _) =>
		{
			if (_suppressUi || _printerCombo.SelectedItem is not string name)
				return;

			_printDoc.PrinterSettings.PrinterName = name;

			// Список форматов у каждого принтера свой, его надо перечитать.
			LoadPaperSizes();
			UpdatePreview();
		};

		box.Controls.Add(_printerCombo);

		// Формат бумаги — отдельный выбор, а не свойство документа:
		// документ на A4 можно печатать на A3, вписав его в лист.
		box.Controls.Add(new Label { Text = "Paper:", Location = new Point(12, 58), AutoSize = true });

		_paperCombo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Location = new Point(12, 78),
			Width = 281
		};

		_paperCombo.SelectedIndexChanged += (_, _) =>
		{
			if (_suppressUi || _paperCombo.SelectedItem is not PaperItem item)
				return;

			_printDoc.DefaultPageSettings.PaperSize = item.Size;
			UpdatePreview();
		};

		box.Controls.Add(_paperCombo);

		// Высота кнопки поднята до 30: штатные 23 пикселя рядом
		// с двумя выпадающими списками выглядят прижатыми.
		_propertiesBut = new Button
		{
			Text = "Properties...",
			Location = new Point(173, 110),
			Width = 120,
			Height = 30
		};

		_propertiesBut.Click += (_, _) => ShowPrinterProperties();

		box.Controls.Add(_propertiesBut);

		return box;
	}

	private Control BuildRangeGroup()
	{
		var box = new GroupBox { Text = "Page range", Width = 305, Height = 110 };

		_rangeAll = new RadioButton { Text = "All pages", Location = new Point(12, 22), Checked = true, AutoSize = true };
		_rangeCurrent = new RadioButton { Text = "Current page", Location = new Point(12, 46), AutoSize = true };
		_rangeCustom = new RadioButton { Text = "Pages:", Location = new Point(12, 70), AutoSize = true };

		_rangeText = new TextBox
		{
			Location = new Point(90, 68),
			Width = 200,
			Enabled = false,
			PlaceholderText = "1-3, 5"
		};

		_rangeCustom.CheckedChanged += (_, _) => _rangeText.Enabled = _rangeCustom.Checked;

		box.Controls.Add(_rangeAll);
		box.Controls.Add(_rangeCurrent);
		box.Controls.Add(_rangeCustom);
		box.Controls.Add(_rangeText);

		return box;
	}

	private Control BuildCopiesGroup()
	{
		var box = new GroupBox { Text = "Copies", Width = 305, Height = 78 };

		box.Controls.Add(new Label { Text = "Number of copies:", Location = new Point(12, 26), AutoSize = true });

		_copies = new NumericUpDown
		{
			Location = new Point(145, 24),
			Width = 70,
			Minimum = 1,
			Maximum = 999,
			Value = 1
		};

		_collate = new CheckBox { Text = "Collate", Location = new Point(212+10, 25), AutoSize = true, Checked = true };

		box.Controls.Add(_copies);
		box.Controls.Add(_collate);

		return box;
	}

	private Control BuildLayoutGroup()
	{
		var box = new GroupBox { Text = "Layout", Width = 305, Height = 196 };

		_orientPortrait = new RadioButton { Text = "Portrait", Location = new Point(12, 22), Checked = true, AutoSize = true };
		_orientLandscape = new RadioButton { Text = "Landscape", Location = new Point(120, 22), AutoSize = true };

		_orientPortrait.CheckedChanged += (_, _) => UpdatePreview();

		box.Controls.Add(_orientPortrait);
		box.Controls.Add(_orientLandscape);

		box.Controls.Add(new Label { Text = "Scale:", Location = new Point(12, 54), AutoSize = true });

		_scaleMode = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Location = new Point(70, 50),
			Width = 150
		};

		_scaleMode.Items.AddRange(new object[] { "Fit to page", "Actual size", "Custom" });
		_scaleMode.SelectedIndex = 0;

		_scaleMode.SelectedIndexChanged += (_, _) =>
		{
			_scalePercent.Enabled = _scaleMode.SelectedIndex == 2;
			UpdatePreview();
		};

		_scalePercent = new NumericUpDown
		{
			Location = new Point(228, 50),
			Width = 62,
			Minimum = 10,
			Maximum = 400,
			Value = 100,
			Enabled = false
		};

		_scalePercent.ValueChanged += (_, _) => UpdatePreview();

		box.Controls.Add(_scaleMode);
		box.Controls.Add(_scalePercent);

		_centre = new CheckBox { Text = "Centre on page", Location = new Point(12, 84), AutoSize = true, Checked = true };
		_centre.CheckedChanged += (_, _) => UpdatePreview();

		_background = new CheckBox { Text = "Print page background", Location = new Point(12, 110), AutoSize = true };
		_background.CheckedChanged += (_, _) => UpdatePreview();

		// Поля принтера — это физически недоступная для печати кромка листа.
		// Её показ помогает понять, почему рисунок обрезался у самого края,
		// но на бумагу сама рамка не выводится: она подсказка, а не содержимое.
		_showMargins = new CheckBox
		{
			Text = "Show printer margins",
			Location = new Point(12, 136),
			AutoSize = true,
			Checked = true
		};

		_showMargins.CheckedChanged += (_, _) =>
		{
			_preview.ShowMargins = _showMargins.Checked;
			_preview.Invalidate();
		};

		box.Controls.Add(_centre);
		box.Controls.Add(_background);
		box.Controls.Add(_showMargins);

		return box;
	}

	private Control BuildPreviewColumn()
	{
		var column = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = new Padding(10, 0, 0, 0)
		};

		column.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		column.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

		_previewHost = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(110, 110, 110),
			AutoScroll = true
		};

		_preview = new PreviewPanel { ShowMargins = true };
		_previewHost.Controls.Add(_preview);

		column.Controls.Add(_previewHost, 0, 0);
		column.Controls.Add(BuildPreviewToolbar(), 0, 1);

		return column;
	}

	private Control BuildPreviewToolbar()
	{
		var bar = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false
		};

		_firstBut = MakeNavButton("|<", () => GoToPage(0));
		_prevBut = MakeNavButton("<", () => GoToPage(_previewIndex - 1));

		_pageLabel = new Label
		{
			AutoSize = false,
			Width = 110,
			Height = 26,
			TextAlign = ContentAlignment.MiddleCenter,
			Margin = new Padding(4, 4, 4, 4)
		};

		_nextBut = MakeNavButton(">", () => GoToPage(_previewIndex + 1));
		_lastBut = MakeNavButton(">|", () => GoToPage(_doc.pages.Count - 1));

		bar.Controls.Add(_firstBut);
		bar.Controls.Add(_prevBut);
		bar.Controls.Add(_pageLabel);
		bar.Controls.Add(_nextBut);
		bar.Controls.Add(_lastBut);

		bar.Controls.Add(new Label
		{
			Text = "Zoom:",
			AutoSize = false,
			Width = 65,
			Height = 26,
			TextAlign = ContentAlignment.MiddleRight,
			Margin = new Padding(16, 4, 4, 4)
		});

		_zoomCombo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 110,
			Margin = new Padding(0, 5, 4, 4)
		};

		_zoomCombo.Items.AddRange(new object[] { "Fit", "50%", "75%", "100%", "150%", "200%", "400%" });
		_zoomCombo.SelectedIndex = 0;
		_zoomCombo.SelectedIndexChanged += (_, _) => ApplyZoom();

		bar.Controls.Add(_zoomCombo);

		return bar;
	}

	private static Button MakeNavButton(string text, Action onClick)
	{
		var but = new Button
		{
			Text = text,
			Width = 38,
			Height = 32,
			Margin = new Padding(2, 4, 2, 4)
		};

		but.Click += (_, _) => onClick();
		return but;
	}

	private Control BuildButtonsRow()
	{
		var row = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 46,
			Padding = new Padding(10, 8, 10, 8)
		};

		var cancel = new Button { Text = "Cancel", Height = 32, Width = 90, DialogResult = DialogResult.Cancel };
		var print = new Button { Text = "Print", Height = 32, Width = 90 };

		print.Click += (_, _) => DoPrint();

		row.Controls.Add(cancel);
		row.Controls.Add(print);

		CancelButton = cancel;
		AcceptButton = print;

		return row;
	}

	#endregion

	#region Принтер

	private void LoadPrinters()
	{
		_suppressUi = true;

		try
		{
			_printerCombo.Items.Clear();

			// Из каталога, собранного при запуске, а не прямым обращением
			// к спулеру: перечисление опрашивает драйверы, и на сетевых
			// очередях диалог открывался с задержкой. Ожидание здесь
			// нужно лишь на случай, когда печать вызвали раньше, чем
			// фоновый опрос успел закончиться.
			PrinterCatalog.WaitUntilReady();

			foreach (var printer in PrinterCatalog.Printers)
				_printerCombo.Items.Add(printer.Name);

			// Каталог мог не собраться (спулер недоступен). Тогда пробуем
			// напрямую — лучше медленно, чем никак.
			if (_printerCombo.Items.Count == 0)
			{
				foreach (string name in PrinterSettings.InstalledPrinters)
					_printerCombo.Items.Add(name);
			}

			// Принтеров может не быть вовсе — тогда печатать некуда, и честнее
			// заблокировать кнопку, чем падать при попытке.
			if (_printerCombo.Items.Count == 0)
			{
				_printerCombo.Enabled = false;
				_paperCombo.Enabled = false;
				_propertiesBut.Enabled = false;
				AcceptButton = null;
				return;
			}

			string current = _printDoc.PrinterSettings.PrinterName;
			int index = _printerCombo.Items.IndexOf(current);

			_printerCombo.SelectedIndex = index >= 0 ? index : 0;
			_printDoc.PrinterSettings.PrinterName = (string)_printerCombo.SelectedItem!;
		}
		finally
		{
			_suppressUi = false;
		}

		LoadPaperSizes();
	}

	/// <summary>
	/// Заполняет список форматов текущего принтера и выбирает тот,
	/// который соответствует формату страницы документа.
	/// </summary>
	private void LoadPaperSizes()
	{
		_suppressUi = true;

		try
		{
			_paperCombo.Items.Clear();

			foreach (PaperSize size in _printDoc.PrinterSettings.PaperSizes)
				_paperCombo.Items.Add(new PaperItem(size));

			if (_paperCombo.Items.Count == 0)
			{
				_paperCombo.Enabled = false;
				return;
			}

			_paperCombo.Enabled = true;

			int index = FindPaperForDocument();

			_paperCombo.SelectedIndex = index;
			_printDoc.DefaultPageSettings.PaperSize = ((PaperItem)_paperCombo.SelectedItem!).Size;
		}
		finally
		{
			_suppressUi = false;
		}
	}

	/// <summary>
	/// Номер формата принтера, ближайшего к формату страницы документа.
	///
	/// Сначала ищется совпадение по названию («A4» у документа и у
	/// принтера — это точно одно и то же), потом по размерам с допуском
	/// в полтора миллиметра: драйверы округляют дюймы по-разному, и точного
	/// равенства ждать нельзя. Ориентация не важна: альбомный A4 печатается
	/// на том же листе A4, просто повёрнутом.
	///
	/// Ничего не нашлось — берётся текущий формат принтера, а не первый
	/// из списка: первым у многих драйверов идёт Letter.
	/// </summary>
	private int FindPaperForDocument()
	{
		var page = _doc.ActivePage ?? _doc.pages.FirstOrDefault();

		string? title = page?.paper_format?.title;

		if (!string.IsNullOrWhiteSpace(title))
		{
			for (int i = 0; i < _paperCombo.Items.Count; i++)
			{
				if (_paperCombo.Items[i] is PaperItem item
					&& string.Equals(item.Size.PaperName, title, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
		}

		if (page != null)
		{
			var sizeMm = page.SizeMm;

			float wantW = Math.Min(sizeMm.Width, sizeMm.Height);
			float wantH = Math.Max(sizeMm.Width, sizeMm.Height);

			const float Tolerance = 1.5f;

			for (int i = 0; i < _paperCombo.Items.Count; i++)
			{
				if (_paperCombo.Items[i] is not PaperItem item)
					continue;

				float w = item.Size.Width / 100f * 25.4f;
				float h = item.Size.Height / 100f * 25.4f;

				float haveW = Math.Min(w, h);
				float haveH = Math.Max(w, h);

				if (Math.Abs(haveW - wantW) <= Tolerance && Math.Abs(haveH - wantH) <= Tolerance)
					return i;
			}
		}

		var current = _printDoc.DefaultPageSettings.PaperSize;

		for (int i = 0; i < _paperCombo.Items.Count; i++)
		{
			if (_paperCombo.Items[i] is PaperItem item && item.Size.RawKind == current.RawKind)
				return i;
		}

		return 0;
	}

	private void ShowPrinterProperties()
	{
		using var dialog = new PrintDialog
		{
			Document = _printDoc,
			UseEXDialog = true,
			AllowSomePages = false,
			AllowSelection = false
		};

		if (dialog.ShowDialog(this) != DialogResult.OK)
			return;

		// Диалог мог сменить и принтер, и ориентацию — подтягиваем обратно.
		int index = _printerCombo.Items.IndexOf(_printDoc.PrinterSettings.PrinterName);

		if (index >= 0)
			_printerCombo.SelectedIndex = index;

		if (_printDoc.DefaultPageSettings.Landscape)
			_orientLandscape.Checked = true;
		else
			_orientPortrait.Checked = true;

		// Диалог мог сменить и формат листа — показываем его в списке.
		SelectCurrentPaper();

		UpdatePreview();
	}

	/// <summary>
	/// Подсвечивает в списке тот формат, который сейчас стоит у документа
	/// печати. Нужно после системного диалога свойств: там формат могли сменить.
	/// </summary>
	private void SelectCurrentPaper()
	{
		var current = _printDoc.DefaultPageSettings.PaperSize;

		_suppressUi = true;

		try
		{
			for (int i = 0; i < _paperCombo.Items.Count; i++)
			{
				if (_paperCombo.Items[i] is PaperItem item && item.Size.RawKind == current.RawKind)
				{
					_paperCombo.SelectedIndex = i;
					return;
				}
			}
		}
		finally
		{
			_suppressUi = false;
		}
	}

	/// <summary>
	/// Ориентация по умолчанию берётся у страницы документа: лист альбомной
	/// страницы логично печатать альбомно, и заставлять пользователя
	/// переключать это вручную незачем.
	/// </summary>
	private void SyncOrientationFromPage()
	{
		var page = _doc.ActivePage ?? _doc.pages.FirstOrDefault();

		if (page == null)
			return;

		bool landscape = page.SizeMm.Width > page.SizeMm.Height;

		if (landscape)
			_orientLandscape.Checked = true;
		else
			_orientPortrait.Checked = true;
	}

	#endregion

	#region Диапазон страниц

	/// <summary>
	/// Номера страниц к печати, от нуля. Пустой список — печатать нечего.
	/// </summary>
	private List<int> BuildPrintQueue()
	{
		var result = new List<int>();

		if (_rangeCurrent.Checked)
		{
			result.Add(_previewIndex);
			return result;
		}

		if (_rangeAll.Checked)
		{
			for (int i = 0; i < _doc.pages.Count; i++)
				result.Add(i);

			return result;
		}

		// Разбор «1-3, 5». Мусор молча пропускается: ругаться на каждую
		// опечатку в поле, которое правят по символу, назойливо.
		foreach (string part in _rangeText.Text.Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			string token = part.Trim();

			if (token.Length == 0)
				continue;

			int dash = token.IndexOf('-');

			if (dash < 0)
			{
				if (int.TryParse(token, out int single))
					AddPage(result, single - 1);

				continue;
			}

			if (int.TryParse(token[..dash].Trim(), out int from)
				&& int.TryParse(token[(dash + 1)..].Trim(), out int to))
			{
				if (from > to)
					(from, to) = (to, from);

				for (int i = from; i <= to; i++)
					AddPage(result, i - 1);
			}
		}

		return result;
	}

	private void AddPage(List<int> list, int index)
	{
		if (index >= 0 && index < _doc.pages.Count && !list.Contains(index))
			list.Add(index);
	}

	#endregion

	#region Предпросмотр

	private void GoToPage(int index)
	{
		index = Math.Clamp(index, 0, Math.Max(0, _doc.pages.Count - 1));

		if (index == _previewIndex)
			return;

		_previewIndex = index;
		UpdatePreview();
	}

	private void UpdatePreview()
	{
		if (_doc.pages.Count == 0)
			return;

		_previewIndex = Math.Clamp(_previewIndex, 0, _doc.pages.Count - 1);

		var page = _doc.pages[_previewIndex];
		var paper = GetPaperRectMm(out var marginsMm);

		_preview.SetPage(
			page,
			paper,
			marginsMm,
			_scaleMode.SelectedIndex,
			(float)_scalePercent.Value / 100f,
			_centre.Checked,
			_background.Checked);

		_pageLabel.Text = $"Page {_previewIndex + 1} of {_doc.pages.Count}";

		_firstBut.Enabled = _previewIndex > 0;
		_prevBut.Enabled = _previewIndex > 0;
		_nextBut.Enabled = _previewIndex < _doc.pages.Count - 1;
		_lastBut.Enabled = _previewIndex < _doc.pages.Count - 1;

		ApplyZoom();
	}

	private void ApplyZoom()
	{
		float zoom;

		if (_zoomCombo.SelectedIndex <= 0)
		{
			zoom = _preview.ComputeFitZoom(_previewHost.ClientSize);
		}
		else
		{
			string text = (_zoomCombo.SelectedItem as string ?? "100%").TrimEnd('%');
			zoom = float.TryParse(text, out float percent) ? percent / 100f : 1f;
		}

		_preview.SetZoom(zoom);
		CentrePreview();
	}

	private void CentrePreview()
	{
		int x = Math.Max(0, (_previewHost.ClientSize.Width - _preview.Width) / 2);
		int y = Math.Max(0, (_previewHost.ClientSize.Height - _preview.Height) / 2);

		_preview.Location = new Point(x, y);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);

		if (_preview == null)
			return;

		if (_zoomCombo.SelectedIndex <= 0)
			ApplyZoom();
		else
			CentrePreview();
	}

	#endregion

	#region Геометрия листа

	/// <summary>
	/// Размер листа принтера и его поля в миллиметрах.
	///
	/// PageSettings отдаёт сотые доли дюйма — отсюда деление на 100 и
	/// умножение на 25.4. Ориентация применяется здесь же: у альбомной
	/// стороны меняются местами.
	/// </summary>
	private RectangleF GetPaperRectMm(out RectangleF marginsMm)
	{
		var settings = _printDoc.DefaultPageSettings;

		float widthMm = settings.PaperSize.Width / 100f * 25.4f;
		float heightMm = settings.PaperSize.Height / 100f * 25.4f;

		var area = settings.PrintableArea;

		float leftMm = area.Left / 100f * 25.4f;
		float topMm = area.Top / 100f * 25.4f;
		float rightMm = widthMm - (area.Left + area.Width) / 100f * 25.4f;
		float bottomMm = heightMm - (area.Top + area.Height) / 100f * 25.4f;

		if (_orientLandscape.Checked)
		{
			(widthMm, heightMm) = (heightMm, widthMm);
			(leftMm, topMm, rightMm, bottomMm) = (topMm, rightMm, bottomMm, leftMm);
		}

		// Отрицательные поля означают, что драйвер не сообщил область печати.
		marginsMm = new RectangleF(
			Math.Max(0f, leftMm),
			Math.Max(0f, topMm),
			Math.Max(0f, rightMm),
			Math.Max(0f, bottomMm));

		return new RectangleF(0f, 0f, widthMm, heightMm);
	}

	#endregion

	#region Печать

	private void DoPrint()
	{
		if (_printerCombo.Items.Count == 0)
			return;

		_printQueue = BuildPrintQueue();

		if (_printQueue.Count == 0)
		{
			MessageBox.Show(this, "Nothing to print: the page range is empty.", "Print");
			return;
		}

		_printDoc.DefaultPageSettings.Landscape = _orientLandscape.Checked;
		_printDoc.PrinterSettings.Copies = (short)_copies.Value;
		_printDoc.PrinterSettings.Collate = _collate.Checked;
		_printDoc.DocumentName = _doc.title;

		_printIndex = 0;

		try
		{
			_printDoc.Print();
			DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				$"Failed to print.\r\n\r\n{ex.Message}",
				"Print error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
		}
	}

	private void OnQueryPageSettings(object sender, QueryPageSettingsEventArgs e)
	{
		e.PageSettings.Landscape = _orientLandscape.Checked;
	}

	private void OnPrintPage(object sender, PrintPageEventArgs e)
	{
		if (_printIndex >= _printQueue.Count || e.Graphics == null)
		{
			e.HasMorePages = false;
			return;
		}

		var page = _doc.pages[_printQueue[_printIndex]];

		// Разрешение растра берётся у принтера, а не у документа: печатать
		// экранные 96 dpi на 600-точечный принтер значит терять качество зря.
		int dpi = Math.Clamp((int)e.Graphics.DpiX, 96, 600);

		using var sk = PageRenderer.Render(page, dpi, _background.Checked);

		if (sk != null)
		{
			using var bitmap = PageRenderer.ToGdiBitmap(sk);

			var target = ComputeTargetRect(
				page,
				new RectangleF(
					e.MarginBounds.X,
					e.MarginBounds.Y,
					e.MarginBounds.Width,
					e.MarginBounds.Height),
				e.Graphics.DpiX);

			e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			e.Graphics.DrawImage(bitmap, target);
		}

		_printIndex++;
		e.HasMorePages = _printIndex < _printQueue.Count;
	}

	/// <summary>
	/// Куда лечь странице на листе, в сотых долях дюйма (единицы Graphics
	/// при печати).
	/// </summary>
	private RectangleF ComputeTargetRect(Page page, RectangleF areaHundredths, float dpi)
	{
		var sizeMm = page.SizeMm;

		if (sizeMm.Width <= 0f || sizeMm.Height <= 0f)
			return areaHundredths;

		float hundredthsPerMm = 100f / 25.4f;

		float wantW = sizeMm.Width * hundredthsPerMm;
		float wantH = sizeMm.Height * hundredthsPerMm;

		float scale = _scaleMode.SelectedIndex switch
		{
			0 => Math.Min(areaHundredths.Width / wantW, areaHundredths.Height / wantH),
			1 => 1f,
			_ => (float)_scalePercent.Value / 100f
		};

		float w = wantW * scale;
		float h = wantH * scale;

		float x = _centre.Checked ? areaHundredths.X + (areaHundredths.Width - w) / 2f : areaHundredths.X;
		float y = _centre.Checked ? areaHundredths.Y + (areaHundredths.Height - h) / 2f : areaHundredths.Y;

		return new RectangleF(x, y, w, h);
	}

	#endregion

	#region Очистка

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_printDoc.PrintPage -= OnPrintPage;
			_printDoc.QueryPageSettings -= OnQueryPageSettings;
			_printDoc.Dispose();
		}

		base.Dispose(disposing);
	}

	#endregion
}