using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

/// <summary>
/// Полоса вкладок страниц документа внизу окна.
///
/// Вкладки трапециевидные, широкой стороной вверх — к холсту. За счёт скоса
/// верхние углы соседей налезают друг на друга, и выбранная вкладка рисуется
/// последней, поэтому перекрывает соседние.
///
/// Контрол рисуется через GDI+, а не через Skia, как панель свойств.
/// Там Skia нужна ради общего вида с холстом и сложных контролов; здесь
/// геометрия — четыре точки и текст, и заводить второй буфер ради этого
/// не стоит.
/// </summary>
public sealed class PageTabsControl : Control
{
	#region Константы

	/// <summary>
	/// Скос трапеции: на столько верхняя сторона шире нижней с каждого края.
	///
	/// Он же задаёт глубину перекрытия соседей: вкладки расставляются
	/// с таким же зазором по нижней стороне, поэтому по верхней они налезают
	/// друг на друга ровно на Slant пикселей.
	/// </summary>
	private const int Slant = 15;

	/// <summary>Отступ полосы от левого края и между полосой и кнопкой «плюс».</summary>
	private const int SideMargin = 8;

	private const int MinTabWidth = 60;
	private const int MaxTabWidth = 180;
	private const int PlusWidth = 34;
	private const int TextPadding = 10;

	/// <summary>Порог, после которого нажатие считается перетаскиванием, а не щелчком.</summary>
	private const int DragThreshold = 4;

	#endregion

	#region Поля

	private Document? _doc;

	private int _hotIndex = -1;
	private bool _plusHot;

	private int _pressIndex = -1;
	private int _pressStartX;

	private bool _dragging;
	private int _dragIndex = -1;

	/// <summary>Место вкладки на момент начала протяжки. -1 — не тянут.</summary>
	private int _dragStartIndex = -1;
	private int _dragGrabOffset;
	private int _dragX;

	private TextBox? _editor;
	private int _editIndex = -1;

	#endregion

	#region События

	/// <summary>Пользователь выбрал страницу.</summary>
	public event Action<Page>? PageActivated;

	/// <summary>Состав, порядок или названия страниц изменились.</summary>
	public event Action? PagesChanged;

	#endregion

	#region Создание

	public PageTabsControl()
	{
		SetStyle(
			ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.UserPaint
			| ControlStyles.ResizeRedraw,
			true);

		Height = 30;
		Dock = DockStyle.Bottom;
		BackColor = SystemColors.Control;
	}

	/// <summary>Привязывает полосу к документу. null — документ закрыт.</summary>
	public void SetDocument(Document? doc)
	{
		CancelEditing();

		_doc = doc;
		_hotIndex = -1;
		_pressIndex = -1;
		_dragging = false;

		Invalidate();
	}

	public void RefreshTabs() => Invalidate();

	#endregion

	#region Раскладка

	private List<Page> Pages => _doc?.pages ?? new List<Page>();

	/// <summary>
	/// Прямоугольники вкладок по нижней стороне трапеций.
	///
	/// Ширина подбирается под текст, но ужимается, если вкладки не влезают:
	/// прокрутка ради редкого случая десятка страниц усложнила бы контрол
	/// заметно сильнее, чем даёт пользы.
	/// </summary>
	private List<Rectangle> GetTabRects()
	{
		var result = new List<Rectangle>();
		var pages = Pages;

		if (pages.Count == 0)
			return result;

		// Зазоры между нижними сторонами тоже занимают место.
		int gaps = Slant * Math.Max(0, pages.Count - 1);
		int available = Math.Max(0, Width - SideMargin * 2 - PlusWidth - SideMargin - gaps);

		using var g = CreateGraphics();

		int total = 0;
		var preferred = new int[pages.Count];

		for (int i = 0; i < pages.Count; i++)
		{
			int text = (int)Math.Ceiling(g.MeasureString(pages[i].title ?? string.Empty, Font).Width);
			preferred[i] = Math.Clamp(text + TextPadding * 2, MinTabWidth, MaxTabWidth);
			total += preferred[i];
		}

		float shrink = total > available && total > 0 ? available / (float)total : 1f;

		int x = SideMargin;

		for (int i = 0; i < pages.Count; i++)
		{
			int w = Math.Max(MinTabWidth / 2, (int)(preferred[i] * shrink));
			result.Add(new Rectangle(x, 0, w, Height));

			// Шаг больше ширины на Slant: нижние стороны разделены зазором,
			// а верхние, широкие, как раз на этот зазор и налезают.
			x += w + Slant;
		}

		return result;
	}

	private Rectangle GetPlusRect()
	{
		var rects = GetTabRects();
		int x = rects.Count > 0 ? rects[^1].Right + SideMargin : SideMargin;

		return new Rectangle(x, 0, PlusWidth, Height);
	}

	/// <summary>
	/// Трапеция вкладки. Нижняя сторона равна rect, верхняя шире на Slant
	/// с каждой стороны — отсюда и перекрытие соседей.
	/// </summary>
	private static GraphicsPath BuildTabPath(Rectangle rect)
	{
		var path = new GraphicsPath();

		path.AddPolygon(new[]
		{
			new Point(rect.Left - Slant, rect.Top),
			new Point(rect.Right + Slant, rect.Top),
			new Point(rect.Right, rect.Bottom),
			new Point(rect.Left, rect.Bottom)
		});

		return path;
	}

	#endregion

	#region Отрисовка

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		var g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(BackColor);

		var pages = Pages;
		var rects = GetTabRects();

		int active = _doc?.ActivePage != null ? pages.IndexOf(_doc.ActivePage) : -1;

		// Сначала невыбранные, слева направо, потом выбранная и, если идёт
		// перетаскивание, перетаскиваемая. Порядок отрисовки и есть порядок
		// перекрытия: верхние углы последней ложатся поверх соседей.
		for (int i = 0; i < rects.Count; i++)
		{
			if (i == active || (_dragging && i == _dragIndex))
				continue;

			DrawTab(g, rects[i], pages[i].title, selected: false, hot: i == _hotIndex);
		}

		if (active >= 0 && active < rects.Count && !(_dragging && active == _dragIndex))
			DrawTab(g, rects[active], pages[active].title, selected: true, hot: active == _hotIndex);

		if (_dragging && _dragIndex >= 0 && _dragIndex < rects.Count)
		{
			var moving = rects[_dragIndex];
			moving.X = _dragX;

			DrawTab(g, moving, pages[_dragIndex].title, selected: _dragIndex == active, hot: true);
		}

		DrawPlus(g, GetPlusRect());
	}

	private void DrawTab(Graphics g, Rectangle rect, string? title, bool selected, bool hot)
	{
		using var path = BuildTabPath(rect);

		Color back = selected
			? Color.FromArgb(240, 240, 240)
			: hot
				? Color.FromArgb(215, 215, 215)
				: Color.FromArgb(196, 196, 196);

		using var fill = new SolidBrush(back);
		using var border = new Pen(Color.FromArgb(140, 140, 140));

		g.FillPath(fill, path);
		g.DrawPath(border, path);

		// Текст ограничивается нижней, узкой стороной: у верхней есть свесы,
		// налезающие на соседей, и попавшая туда буква читалась бы как чужая.
		var textRect = new Rectangle(
			rect.Left + TextPadding / 2,
			rect.Top,
			Math.Max(0, rect.Width - TextPadding),
			rect.Height);

		TextRenderer.DrawText(
			g,
			title ?? string.Empty,
			Font,
			textRect,
			selected ? Color.Black : Color.FromArgb(60, 60, 60),
			TextFormatFlags.HorizontalCenter
			| TextFormatFlags.VerticalCenter
			| TextFormatFlags.EndEllipsis
			| TextFormatFlags.NoPrefix);
	}

	private void DrawPlus(Graphics g, Rectangle rect)
	{
		using var path = BuildTabPath(rect);

		using var fill = new SolidBrush(_plusHot ? Color.FromArgb(215, 215, 215) : Color.FromArgb(196, 196, 196));
		using var border = new Pen(Color.FromArgb(140, 140, 140));

		g.FillPath(fill, path);
		g.DrawPath(border, path);

		TextRenderer.DrawText(
			g,
			"+",
			Font,
			rect,
			Color.FromArgb(60, 60, 60),
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
	}

	#endregion

	#region Попадание мышью

	/// <summary>
	/// Индекс вкладки под точкой, либо -1.
	///
	/// Перебор идёт в обратном порядке отрисовки: сверху лежит та, что
	/// нарисована последней, и она же должна ловить щелчок.
	/// </summary>
	private int HitTestTab(Point location)
	{
		var rects = GetTabRects();
		int active = _doc?.ActivePage != null ? Pages.IndexOf(_doc.ActivePage) : -1;

		if (active >= 0 && active < rects.Count && Contains(rects[active], location))
			return active;

		for (int i = rects.Count - 1; i >= 0; i--)
		{
			if (Contains(rects[i], location))
				return i;
		}

		return -1;
	}

	private static bool Contains(Rectangle rect, Point location)
	{
		using var path = BuildTabPath(rect);
		return path.IsVisible(location);
	}

	#endregion

	#region Мышь

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		if (_dragging)
		{
			UpdateDrag(e.X);
			return;
		}

		// Начало перетаскивания: кнопка удерживается и курсор ушёл дальше порога.
		if (_pressIndex >= 0 && e.Button == MouseButtons.Left
			&& Math.Abs(e.X - _pressStartX) > DragThreshold)
		{
			BeginDrag(e.X);
			return;
		}

		int hot = HitTestTab(e.Location);
		bool plusHot = Contains(GetPlusRect(), e.Location);

		if (hot == _hotIndex && plusHot == _plusHot)
			return;

		_hotIndex = hot;
		_plusHot = plusHot;
		Invalidate();
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);

		if (_hotIndex < 0 && !_plusHot)
			return;

		_hotIndex = -1;
		_plusHot = false;
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);

		CommitEditing();

		if (e.Button != MouseButtons.Left || _doc == null)
			return;

		if (Contains(GetPlusRect(), e.Location))
		{
			AddPage();
			return;
		}

		int index = HitTestTab(e.Location);

		if (index < 0)
			return;

		_pressIndex = index;
		_pressStartX = e.X;

		ActivatePage(index);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		if (_dragging)
			EndDrag();

		_pressIndex = -1;
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		base.OnMouseDoubleClick(e);

		if (e.Button != MouseButtons.Left)
			return;

		int index = HitTestTab(e.Location);

		if (index >= 0)
			BeginEditing(index);
	}

	#endregion

	#region Перетаскивание

	private void BeginDrag(int mouseX)
	{
		var rects = GetTabRects();

		if (_pressIndex < 0 || _pressIndex >= rects.Count)
			return;

		_dragging = true;
		_dragIndex = _pressIndex;

		// Исходное место запоминается на старте, а не берётся из шага
		// истории: порядок меняется много раз за протяжку, и «откуда
		// начали» известно только здесь.
		_dragStartIndex = _pressIndex;

		_dragGrabOffset = mouseX - rects[_dragIndex].Left;
		_dragX = rects[_dragIndex].Left;

		Invalidate();
	}

	/// <summary>
	/// Ведёт перетаскиваемую вкладку за курсором и меняет порядок, когда её
	/// середина переходит середину соседа.
	///
	/// Порядок меняется прямо в документе, а не в отдельном списке: соседи
	/// тогда разъезжаются сами при следующей отрисовке, и отдельный код
	/// «показать, куда встанет» не нужен.
	/// </summary>
	private void UpdateDrag(int mouseX)
	{
		if (_doc == null || _dragIndex < 0)
			return;

		_dragX = mouseX - _dragGrabOffset;

		var rects = GetTabRects();

		if (_dragIndex >= rects.Count)
			return;

		int centre = _dragX + rects[_dragIndex].Width / 2;
		int target = _dragIndex;

		if (_dragIndex > 0 && centre < rects[_dragIndex - 1].Left + rects[_dragIndex - 1].Width / 2)
			target = _dragIndex - 1;
		else if (_dragIndex < rects.Count - 1 && centre > rects[_dragIndex + 1].Left + rects[_dragIndex + 1].Width / 2)
			target = _dragIndex + 1;

		if (target != _dragIndex)
		{
			var page = _doc.pages[_dragIndex];
			_doc.pages.RemoveAt(_dragIndex);
			_doc.pages.Insert(target, page);

			_dragIndex = target;
			_doc.need_save = true;

			PagesChanged?.Invoke();
		}

		Invalidate();
	}

	/// <summary>
	/// Завершает протяжку и записывает перенос одним шагом истории.
	///
	/// Шаг создаётся здесь, а не в UpdateDrag: порядок там меняется
	/// на каждом пересечении соседа, и перенос через три позиции дал бы
	/// три записи. Слияние в MovePageAction их бы склеило, но проще
	/// не плодить их вовсе.
	/// </summary>
	private void EndDrag()
	{
		if (_doc != null && _dragging && _dragStartIndex >= 0 && _dragIndex >= 0
			&& _dragIndex != _dragStartIndex
			&& _dragIndex < _doc.pages.Count)
		{
			_doc.Undo.Push(new MovePageAction(
				_doc,
				_doc.pages[_dragIndex],
				_dragStartIndex,
				_dragIndex,
				RefreshAfterHistory));
		}

		_dragging = false;
		_dragIndex = -1;
		_dragStartIndex = -1;

		Invalidate();
	}

	#endregion

	#region Страницы

	private void ActivatePage(int index)
	{
		if (_doc == null || index < 0 || index >= _doc.pages.Count)
			return;

		var page = _doc.pages[index];

		if (ReferenceEquals(_doc.ActivePage, page))
		{
			Invalidate();
			return;
		}

		_doc.ActivePage = page;

		Invalidate();
		PageActivated?.Invoke(page);
	}

	/// <summary>
	/// Добавляет страницу и сразу открывает её.
	/// </summary>
	private void AddPage()
	{
		if (_doc == null)
			return;

		var page = _doc.CreateDefaultPage();
		_doc.need_save = true;

		_doc.Undo.Push(new AddPageAction(_doc, page, _doc.pages.IndexOf(page), RefreshAfterHistory));

		PagesChanged?.Invoke();

		ActivatePage(_doc.pages.IndexOf(page));
	}

	/// <summary>
	/// Удаляет страницу с возможностью отката.
	///
	/// Последнюю не удаляем: документ без страниц нечем показать,
	/// а заводить ради этого состояние «пустой документ» незачем.
	/// </summary>
	public bool DeletePage(Page page)
	{
		if (_doc == null || page == null || _doc.pages.Count <= 1)
			return false;

		int index = _doc.pages.IndexOf(page);

		if (index < 0)
			return false;

		bool wasActive = ReferenceEquals(_doc.ActivePage, page);

		// Действие создаётся ДО удаления: ему нужно и место страницы
		// в списке, и то, была ли она открыта.
		var action = new RemovePageAction(_doc, page, index, wasActive, RefreshAfterHistory);

		if (!_doc.RemovePage(page))
			return false;

		_doc.need_save = true;
		_doc.Undo.Push(action);

		PagesChanged?.Invoke();
		ActivatePage(Math.Clamp(index, 0, _doc.pages.Count - 1));

		return true;
	}

	/// <summary>
	/// Приводит вкладки в соответствие с документом после отката и повтора.
	///
	/// Открытая страница могла быть вынута из документа — тогда открывается
	/// первая: вкладка, указывающая в никуда, хуже любой другой.
	/// </summary>
	private void RefreshAfterHistory()
	{
		if (_doc == null)
			return;

		if (_doc.ActivePage == null || !_doc.pages.Contains(_doc.ActivePage))
			_doc.ActivePage = _doc.pages.FirstOrDefault();

		CommitEditing();

		PagesChanged?.Invoke();

		if (_doc.ActivePage != null)
			PageActivated?.Invoke(_doc.ActivePage);

		Invalidate();
	}

	#endregion

	#region Переименование

	/// <summary>
	/// Правка названия прямо на вкладке.
	///
	/// Настоящий TextBox поверх вкладки, а не своя отрисовка каретки:
	/// выделение, буфер обмена и раскладка достаются готовыми.
	/// </summary>
	private void BeginEditing(int index)
	{
		if (_doc == null || index < 0 || index >= _doc.pages.Count)
			return;

		CommitEditing();

		var rects = GetTabRects();

		if (index >= rects.Count)
			return;

		var rect = rects[index];

		_editIndex = index;

		_editor = new TextBox
		{
			Text = _doc.pages[index].title ?? string.Empty,
			BorderStyle = BorderStyle.FixedSingle,
			TextAlign = HorizontalAlignment.Center,
			Font = Font,
			Bounds = new Rectangle(rect.Left + 2, 3, Math.Max(40, rect.Width - 4), Height - 6)
		};

		_editor.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				CommitEditing();
			}
			else if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				CancelEditing();
			}
		};

		// Потеря фокуса приравнивается к Enter: щелчок мимо — это обычный
		// способ закончить правку, и терять набранное на нём нельзя.
		_editor.LostFocus += (_, _) => CommitEditing();

		Controls.Add(_editor);
		_editor.BringToFront();
		_editor.Focus();
		_editor.SelectAll();
	}

	private void CommitEditing()
	{
		if (_editor == null)
			return;

		var editor = _editor;
		int index = _editIndex;

		// Ссылка снимается до удаления контрола: удаление поднимет LostFocus,
		// и без этого метод вошёл бы сам в себя.
		_editor = null;
		_editIndex = -1;

		string title = editor.Text.Trim();

		Controls.Remove(editor);
		editor.Dispose();

		if (_doc != null && index >= 0 && index < _doc.pages.Count && title.Length > 0)
		{
			var page = _doc.pages[index];

			if (page.title != title)
			{
				string before = page.title ?? string.Empty;

				page.title = title;
				_doc.need_save = true;

				_doc.Undo.Push(new RenamePageAction(page, before, title, RefreshAfterHistory));

				PagesChanged?.Invoke();
			}
		}

		Invalidate();
	}

	private void CancelEditing()
	{
		if (_editor == null)
			return;

		var editor = _editor;

		_editor = null;
		_editIndex = -1;

		Controls.Remove(editor);
		editor.Dispose();

		Invalidate();
	}

	#endregion
}