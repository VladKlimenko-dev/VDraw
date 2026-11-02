using VDraw.Editing;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Кнопка отбора объектов для выделения.
///
/// Отбор влияет на два действия: «выделить всё» (Ctrl+A) и рамку выделения.
/// Щелчок по самой фигуре ему не подчиняется намеренно — если пользователь
/// попал курсором точно в объект, он хочет именно его, а не объяснений,
/// почему объект не берётся.
/// </summary>
public partial class MainForm
{
	private Button _sel_filter_but = null!;
	private ContextMenuStrip _selFilterMenu = null!;

	/// <summary>Пункт меню и виды объектов, которыми он управляет.</summary>
	private readonly List<(ToolStripMenuItem Item, SelectionKinds Kinds)> _selFilterItems = new();

	/// <summary>
	/// Английская подпись пункта и его отступ.
	///
	/// Нужны потому, что меню строится один раз, а язык меняется на ходу.
	/// Общий обход Localization.Apply сюда не доходит: меню не лежит
	/// в дереве Controls формы. Подписи расставляются заново при каждом
	/// открытии — это дешевле, чем подписываться на смену языка ради
	/// меню, которое видно долю секунды.
	/// </summary>
	private readonly List<(ToolStripMenuItem Item, string Caption, int Indent)> _selFilterCaptions = new();

	/// <summary>
	/// Создаёт кнопку и её меню.
	///
	/// В коде, а не в дизайнере, по той же причине, что кнопка смены темы:
	/// файл дизайнера правится пользователем, и добавленное туда пропало бы
	/// при первой же перегенерации.
	/// </summary>
	private void InitSelectionFilterButton()
	{
		_sel_filter_but = new Button
		{
			Name = "sel_filter_but",
			MinimumSize = v_scale_page_but.MinimumSize,
			Size = v_scale_page_but.Size,
			Margin = v_scale_page_but.Margin,
			Padding = v_scale_page_but.Padding,
			UseVisualStyleBackColor = true,
			TabStop = false,
			Text = string.Empty
		};

		BuildSelectionFilterMenu();

		_sel_filter_but.Click += (_, _) =>
			_selFilterMenu.Show(_sel_filter_but, new Point(0, _sel_filter_but.Height));

		toolTip.SetToolTip(_sel_filter_but, L.T("Selection filter"));

		view_flow.Controls.Add(_sel_filter_but);

		// Полосе добавляется ширина под новую кнопку.
		//
		// Без этого кнопка никуда не девалась, но и не показывалась:
		// FlowLayoutPanel переносит не поместившееся на следующий ряд,
		// а высоты у полосы ровно на один ряд — кнопка уезжала под
		// нижний край и пропадала с глаз.
		view_flow.Width += _sel_filter_but.Width + _sel_filter_but.Margin.Horizontal;

		// Сразу за кнопкой «вписать страницу»: отбор относится к работе
		// с выделением, а не к масштабу, и место у края полосы для него
		// нагляднее, чем в середине.
		view_flow.Controls.SetChildIndex(
			_sel_filter_but,
			view_flow.Controls.GetChildIndex(v_scale_page_but) + 1);
	}

	private void BuildSelectionFilterMenu()
	{
		_selFilterMenu = new ContextMenuStrip { ShowImageMargin = false };

		WinFormsTheme.ApplyToolStrip(_selFilterMenu);

		// Меню не закрывается от щелчка по пункту: отбор почти всегда
		// настраивают несколькими галочками подряд, и открывать меню
		// заново после каждой — работа вместо настройки.
		_selFilterMenu.Closing += (_, e) =>
		{
			if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
				e.Cancel = true;
		};

		// Состояние галочек читается из документа при каждом открытии:
		// документ могли сменить, а подписываться на это ради меню,
		// которое видно долю секунды, незачем.
		_selFilterMenu.Opening += (_, _) =>
		{
			ApplySelectionFilterTexts();
			RefreshSelectionFilterChecks();
		};

		AddSelFilterItem("All", SelectionKinds.All, 0);
		_selFilterMenu.Items.Add(new ToolStripSeparator());

		AddSelFilterItem("All shapes", SelectionKinds.AllShapes, 0);
		AddSelFilterItem("Circles", SelectionKinds.Circles, 1);
		AddSelFilterItem("Ellipses", SelectionKinds.Ellipses, 1);
		AddSelFilterItem("Rectangles", SelectionKinds.Rectangles, 1);
		AddSelFilterItem("Rounded Rectangles", SelectionKinds.RoundedRectangles, 1);
		AddSelFilterItem("Chamfered Rectangles", SelectionKinds.ChamferedRectangles, 1);

		// Многоугольника в заказанном списке не было, но без своего пункта
		// он остался бы доступен только вместе со всеми фигурами разом —
		// отобрать одни многоугольники стало бы нечем.
		AddSelFilterItem("Polygons", SelectionKinds.Polygons, 1);

		_selFilterMenu.Items.Add(new ToolStripSeparator());

		AddSelFilterItem("Curves", SelectionKinds.Curves, 0);
		AddSelFilterItem("Images", SelectionKinds.Images, 0);
		AddSelFilterItem("Text", SelectionKinds.Text, 0);

		_selFilterMenu.Items.Add(new ToolStripSeparator());

		AddSelFilterItem("Measurements", SelectionKinds.Measurements, 0);
		AddSelFilterItem("Distance Measurements", SelectionKinds.DistanceMeasurements, 1);
		AddSelFilterItem("Angle Measurements", SelectionKinds.AngleMeasurements, 1);

		ApplySelectionFilterTexts();
	}

	private void AddSelFilterItem(string text, SelectionKinds kinds, int indent)
	{
		var item = new ToolStripMenuItem { CheckOnClick = false };

		item.Click += (_, _) => ToggleSelectionFilter(kinds);

		_selFilterMenu.Items.Add(item);
		_selFilterItems.Add((item, kinds));
		_selFilterCaptions.Add((item, text, indent));
	}

	/// <summary>Расставляет подписи пунктов на текущем языке.</summary>
	private void ApplySelectionFilterTexts()
	{
		foreach (var (item, caption, indent) in _selFilterCaptions)
			item.Text = new string(' ', indent * 4) + L.T(caption);
	}

	/// <summary>
	/// Включает или выключает набор видов.
	///
	/// «All» работает в одну сторону: щелчок по нему всегда включает всё.
	/// Если бы он выключался, как остальные, одним нажатием получалось бы
	/// состояние «не выделяется ничего» — рамка и Ctrl+A молчат, а причина
	/// спрятана в меню, которое пользователь только что закрыл.
	/// </summary>
	private void ToggleSelectionFilter(SelectionKinds kinds)
	{
		if (cur_doc == null)
			return;

		var current = cur_doc.SelectionFilter;

		if (kinds == SelectionKinds.All)
		{
			if (current == SelectionKinds.All)
				return;

			current = SelectionKinds.All;
		}
		else
		{
			bool allSet = (current & kinds) == kinds;

			current = allSet
				? current & ~kinds
				: current | kinds;
		}

		if (current == cur_doc.SelectionFilter)
			return;

		cur_doc.SelectionFilter = current;
		cur_doc.need_save = true;

		RefreshSelectionFilterChecks();
		UpdateWindowTitle();
	}

	private void RefreshSelectionFilterChecks()
	{
		var filter = SelectionFilter.Of(cur_doc);

		foreach (var (item, kinds) in _selFilterItems)
		{
			// Групповой пункт отмечен, только когда включено всё, что
			// в него входит: наполовину включённая группа не «включена».
			item.Checked = (filter & kinds) == kinds;
		}
	}
}
