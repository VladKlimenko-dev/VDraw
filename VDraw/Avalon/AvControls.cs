using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using SkiaSharp;

// О псевдонимах см. пояснение в начале AvTheme.cs.
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Border = Avalonia.Controls.Border;
using Brushes = Avalonia.Media.Brushes;
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Color = Avalonia.Media.Color;
using ComboBox = Avalonia.Controls.ComboBox;
using Cursor = Avalonia.Input.Cursor;
using FontFamily = Avalonia.Media.FontFamily;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using ToolTip = Avalonia.Controls.ToolTip;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;
using Control = Avalonia.Controls.Control;
using Grid = Avalonia.Controls.Grid;
using Orientation = Avalonia.Layout.Orientation;
using Panel = Avalonia.Controls.Panel;
using Pen = Avalonia.Media.Pen;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using TextBox = Avalonia.Controls.TextBox;

namespace VDraw.Ui.Avalon;

#region Раскладка

public enum AvFlowDirection
{
	Vertical,
	Horizontal
}

/// <summary>
/// Контейнер с последовательной раскладкой — замена SkFlowLayout.
///
/// Своя раскладка, а не StackPanel или WrapPanel, по двум причинам. Во-первых,
/// нужен и вертикальный стек, и горизонтальный ряд, и горизонтальный ряд
/// с переносом — переключаемые одним свойством, как было в Skia-версии: иначе
/// перенесённый код пришлось бы в каждом месте переписывать на другой тип
/// контейнера. Во-вторых, в вертикальном режиме дети должны растягиваться
/// по ширине, кроме тех, кому ширина задана явно, — ровно то правило,
/// по которому собраны все строки панели свойств.
/// </summary>
public class AvFlow : Panel
{
	public AvFlowDirection Direction { get; set; } = AvFlowDirection.Vertical;

	/// <summary>Зазор между соседними детьми.</summary>
	public double Gap { get; set; } = AvTheme.Gap;

	/// <summary>Отступ содержимого от края контейнера со всех сторон.</summary>
	public double Pad { get; set; }

	/// <summary>Переносить ли содержимое на следующую строку. Только для Horizontal.</summary>
	public bool WrapContents { get; set; }

	/// <summary>
	/// Совместимость с Skia-версией: там высоту контейнера приходилось
	/// считать вручную, и этот флаг разрешал подгонять её под содержимое.
	/// В Avalonia контейнер и так возвращает высоту содержимого из
	/// MeasureOverride, поэтому свойство ни на что не влияет — оно
	/// оставлено, чтобы не править три десятка мест переноса.
	/// </summary>
	public bool AutoHeight { get; set; } = true;

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public void Add(Control child) => Children.Add(child);

	public void ClearChildren() => Children.Clear();

	private static bool HasExplicitWidth(Control c) => !double.IsNaN(c.Width);

	protected override Size MeasureOverride(Size availableSize)
	{
		double inner = Math.Max(0, availableSize.Width - Pad * 2);

		if (Direction == AvFlowDirection.Vertical)
		{
			double h = 0;
			double w = 0;
			bool first = true;

			foreach (var child in Children)
			{
				if (!child.IsVisible)
					continue;

				child.Measure(new Size(inner, double.PositiveInfinity));

				if (!first)
					h += Gap;

				first = false;
				h += child.DesiredSize.Height;
				w = Math.Max(w, child.DesiredSize.Width);
			}

			return new Size(
				double.IsInfinity(availableSize.Width) ? w + Pad * 2 : availableSize.Width,
				h + Pad * 2);
		}

		if (!WrapContents)
		{
			double w = 0;
			double h = 0;
			bool first = true;

			foreach (var child in Children)
			{
				if (!child.IsVisible)
					continue;

				child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

				if (!first)
					w += Gap;

				first = false;
				w += child.DesiredSize.Width;
				h = Math.Max(h, child.DesiredSize.Height);
			}

			return new Size(w + Pad * 2, h + Pad * 2);
		}

		// Горизонтальный ряд с переносом: высота зависит от доступной ширины,
		// поэтому считается в том же проходе, что и перенос.
		double lineW = 0;
		double lineH = 0;
		double totalH = 0;
		bool firstLine = true;

		foreach (var child in Children)
		{
			if (!child.IsVisible)
				continue;

			child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

			double cw = child.DesiredSize.Width;

			if (lineW > 0 && lineW + Gap + cw > inner)
			{
				totalH += firstLine ? lineH : lineH + Gap;
				firstLine = false;
				lineW = 0;
				lineH = 0;
			}

			lineW += lineW > 0 ? Gap + cw : cw;
			lineH = Math.Max(lineH, child.DesiredSize.Height);
		}

		totalH += firstLine ? lineH : lineH + Gap;

		return new Size(
			double.IsInfinity(availableSize.Width) ? lineW + Pad * 2 : availableSize.Width,
			totalH + Pad * 2);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double inner = Math.Max(0, finalSize.Width - Pad * 2);

		if (Direction == AvFlowDirection.Vertical)
		{
			double y = Pad;

			foreach (var child in Children)
			{
				if (!child.IsVisible)
					continue;

				double cw = HasExplicitWidth(child)
					? Math.Min(child.DesiredSize.Width, inner)
					: inner;

				double ch = child.DesiredSize.Height;

				child.Arrange(new Rect(Pad, y, cw, ch));
				y += ch + Gap;
			}

			return finalSize;
		}

		if (!WrapContents)
		{
			double x = Pad;
			double height = Math.Max(0, finalSize.Height - Pad * 2);

			foreach (var child in Children)
			{
				if (!child.IsVisible)
					continue;

				double cw = child.DesiredSize.Width;
				double ch = HasExplicitHeight(child) ? child.DesiredSize.Height : height;

				child.Arrange(new Rect(x, Pad, cw, ch));
				x += cw + Gap;
			}

			return finalSize;
		}

		double lx = Pad;
		double ly = Pad;
		double rowH = 0;

		foreach (var child in Children)
		{
			if (!child.IsVisible)
				continue;

			double cw = child.DesiredSize.Width;
			double ch = child.DesiredSize.Height;

			if (lx > Pad && lx + cw > Pad + inner)
			{
				lx = Pad;
				ly += rowH + Gap;
				rowH = 0;
			}

			child.Arrange(new Rect(lx, ly, cw, ch));

			lx += cw + Gap;
			rowH = Math.Max(rowH, ch);
		}

		return finalSize;
	}

	private static bool HasExplicitHeight(Control c) => !double.IsNaN(c.Height);
}

/// <summary>
/// Строка свойства — подпись слева, один или несколько редакторов справа.
/// Замена SkPropertyRow.
///
/// Колонка подписей у всех строк одной ширины, поэтому значения выстраиваются
/// в столбик независимо от длины названий. Редакторы без заданной ширины делят
/// остаток строки поровну — так ведут себя пары «ширина/высота» и тройки
/// кнопок, собранные в одну строку.
/// </summary>
public sealed class AvPropertyRow : Panel
{
	private readonly TextBlock _label;
	private readonly Dictionary<Control, double> _fixedWidths = new();

	public string Label
	{
		get => _label.Text ?? string.Empty;
		set => _label.Text = value;
	}

	public double LabelWidth { get; set; } = AvTheme.LabelWidth;

	public double EditorGap { get; set; } = AvTheme.Gap;

	public double Indent { get; set; }

	public bool IndentLabel { get; set; } = true;

	public List<Control> Editors { get; } = new();

	public Control Editor => Editors.Count > 0 ? Editors[0] : null!;

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvPropertyRow(string label, Control editor)
	{
		_label = CreateLabel(label);
		Children.Add(_label);
		AddEditor(editor);
	}

	/// <summary>
	/// Перегрузка с именем строки. Имя в Avalonia-версии служит только
	/// для поиска строки в отладчике, но сохранено, чтобы перенесённый
	/// код не пришлось править.
	/// </summary>
	public AvPropertyRow(string name, string label, Control editor)
		: this(label, editor)
	{
		Name = name;
	}

	public AvPropertyRow(string label, params Control[] editors)
	{
		_label = CreateLabel(label);
		Children.Add(_label);

		foreach (var editor in editors)
			AddEditor(editor);
	}

	private static TextBlock CreateLabel(string text) => new()
	{
		Text = text,
		FontSize = AvTheme.FontSize,
		Foreground = AvTheme.TextMuted,
		VerticalAlignment = VerticalAlignment.Center,
		TextTrimming = TextTrimming.CharacterEllipsis
	};

	public void AddEditor(Control editor)
	{
		if (editor == null)
			return;

		Editors.Add(editor);
		Children.Add(editor);
	}

	public void SetFixedWidth(Control editor, double width)
	{
		_fixedWidths[editor] = width;
	}

	private double WidthOf(Control editor)
	{
		if (_fixedWidths.TryGetValue(editor, out double w))
			return w;

		return double.IsNaN(editor.Width) ? double.NaN : editor.Width;
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		double labelW = Math.Max(0, LabelWidth - (IndentLabel ? Indent : 0));

		_label.Measure(new Size(labelW, double.PositiveInfinity));

		double total = double.IsInfinity(availableSize.Width)
			? LabelWidth + 160
			: availableSize.Width;

		double editorsArea = Math.Max(0, total - Indent - LabelWidth);
		double h = _label.DesiredSize.Height;

		MeasureEditors(editorsArea, ref h);

		return new Size(total, Math.Max(AvTheme.RowHeight, h));
	}

	private void MeasureEditors(double area, ref double h)
	{
		var visible = Editors.Where(e => e.IsVisible).ToList();

		if (visible.Count == 0)
			return;

		double gaps = EditorGap * (visible.Count - 1);
		double fixedSum = 0;
		int flexible = 0;

		foreach (var editor in visible)
		{
			double w = WidthOf(editor);

			if (double.IsNaN(w))
				flexible++;
			else
				fixedSum += w;
		}

		double flexWidth = flexible > 0
			? Math.Max(24, (area - gaps - fixedSum) / flexible)
			: 0;

		foreach (var editor in visible)
		{
			double w = WidthOf(editor);

			editor.Measure(new Size(
				double.IsNaN(w) ? flexWidth : w,
				double.PositiveInfinity));

			h = Math.Max(h, editor.DesiredSize.Height);
		}
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double labelW = Math.Max(0, LabelWidth - (IndentLabel ? Indent : 0));
		double labelX = IndentLabel ? Indent : 0;

		_label.Arrange(new Rect(labelX, 0, labelW, finalSize.Height));

		var visible = Editors.Where(e => e.IsVisible).ToList();

		foreach (var editor in Editors)
		{
			if (!editor.IsVisible)
				editor.Arrange(new Rect(0, 0, 0, 0));
		}

		if (visible.Count == 0)
			return finalSize;

		double area = Math.Max(0, finalSize.Width - Indent - LabelWidth);
		double gaps = EditorGap * (visible.Count - 1);
		double fixedSum = 0;
		int flexible = 0;

		foreach (var editor in visible)
		{
			double w = WidthOf(editor);

			if (double.IsNaN(w))
				flexible++;
			else
				fixedSum += w;
		}

		double flexWidth = flexible > 0
			? Math.Max(24, (area - gaps - fixedSum) / flexible)
			: 0;

		double x = Indent + LabelWidth;

		foreach (var editor in visible)
		{
			double w = WidthOf(editor);

			if (double.IsNaN(w))
				w = flexWidth;

			double h = double.IsNaN(editor.Height)
				? Math.Min(finalSize.Height, Math.Max(editor.DesiredSize.Height, AvTheme.InputHeight))
				: editor.Height;

			double y = (finalSize.Height - h) / 2;

			editor.Arrange(new Rect(x, y, w, h));
			x += w + EditorGap;
		}

		return finalSize;
	}
}

/// <summary>
/// Сворачиваемый раздел панели — замена SkCollapsePanel.
///
/// Содержимое кладётся не в Children, а в Content: сам раздел состоит
/// из заголовка и области содержимого, и добавление строки прямо в Children
/// сломало бы эту пару.
/// </summary>
public sealed class AvCollapseSection : Border
{
	private readonly Border _header;
	private readonly TextBlock _title;
	private readonly TextBlock _chevron;
	private bool _expanded = true;

	public AvFlow Content { get; } = new()
	{
		Direction = AvFlowDirection.Vertical,
		Gap = 4,
		Pad = 6
	};

	public string HeaderText
	{
		get => _title.Text ?? string.Empty;
		set => _title.Text = value;
	}

	public bool Expanded
	{
		get => _expanded;
		set
		{
			if (_expanded == value)
				return;

			_expanded = value;
			Content.IsVisible = value;
			_chevron.Text = value ? "▾" : "▸";

			ExpandedChanged?.Invoke(value);
		}
	}

	public Action<bool>? ExpandedChanged;

	public bool Visible { get => IsVisible; set => IsVisible = value; }

	public AvCollapseSection()
	{
		Background = AvTheme.PanelBack2;
		BorderBrush = AvTheme.BorderLine;
		BorderThickness = new Thickness(1);
		CornerRadius = new CornerRadius(AvTheme.Radius);

		_chevron = new TextBlock
		{
			Text = "▾",
			FontSize = AvTheme.FontSize,
			Foreground = AvTheme.TextMuted,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8, 0, 6, 0)
		};

		_title = new TextBlock
		{
			FontSize = AvTheme.FontSize,
			FontWeight = FontWeight.SemiBold,
			Foreground = AvTheme.Text,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};

		var headerRow = new AvFlow
		{
			Direction = AvFlowDirection.Horizontal,
			Gap = 0
		};

		headerRow.Add(_chevron);
		headerRow.Add(_title);

		_header = new Border
		{
			Height = AvTheme.HeaderHeight,
			Background = AvTheme.Surface2,
			CornerRadius = new CornerRadius(AvTheme.Radius - 1, AvTheme.Radius - 1, 0, 0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = headerRow
		};

		_header.PointerPressed += (_, e) =>
		{
			Expanded = !Expanded;
			e.Handled = true;
		};

		var stack = new AvFlow
		{
			Direction = AvFlowDirection.Vertical,
			Gap = 0
		};

		stack.Add(_header);
		stack.Add(Content);

		Child = stack;
	}

	/// <summary>
	/// Совместимость с перенесённым кодом: в Skia-версии раскладку
	/// приходилось пересчитывать вручную, в Avalonia это делает система.
	/// </summary>
	public void Arrange()
	{
		InvalidateMeasure();
	}
}

#endregion

#region Простые контролы

/// <summary>Подпись — замена SkLabel.</summary>
public sealed class AvLabel : TextBlock
{
	private bool _bold;

	public bool Bold
	{
		get => _bold;
		set
		{
			_bold = value;
			FontWeight = value ? FontWeight.SemiBold : FontWeight.Normal;
		}
	}

	/// <summary>Цвет текста. null — цвет темы.</summary>
	public SKColor? ForeColor
	{
		set => Foreground = value.HasValue ? AvTheme.Brush(value.Value) : AvTheme.Text;
	}

	public bool Visible { get => IsVisible; set => IsVisible = value; }

	public AvLabel()
	{
		FontSize = AvTheme.FontSize;
		Foreground = AvTheme.Text;
		VerticalAlignment = VerticalAlignment.Center;
		TextWrapping = TextWrapping.Wrap;
	}
}

/// <summary>
/// Кнопка — замена SkButton.
///
/// Click объявлен свойством-делегатом, а не событием: перенесённый код
/// присваивает его в инициализаторе объекта (Click = () => ...), как было
/// в Skia-версии. Базовое событие Button.Click при этом скрывается, поэтому
/// внутренняя подписка сделана через AddHandler.
/// </summary>
public class AvButton : Button
{
	private bool _checked;

	public new Action? Click { get; set; }

	/// <summary>Залипает ли кнопка при нажатии.</summary>
	public bool Toggles { get; set; } = true;

	/// <summary>
	/// Нажатое состояние.
	///
	/// Присваивание НЕ поднимает CheckedChanged — так же, как в Skia-версии.
	/// Панель свойств задаёт это свойство при сборке (Checked = shape.Bold),
	/// и событие оттуда означало бы правку модели при каждой смене
	/// выделения, да ещё и с записью в историю.
	/// </summary>
	public bool Checked
	{
		get => _checked;
		set
		{
			if (_checked == value)
				return;

			_checked = value;
			UpdateVisual();
		}
	}

	public event Action<bool>? CheckedChanged;

	public string Text
	{
		get => Content as string ?? string.Empty;
		set => Content = value;
	}

	public bool Bold
	{
		set => FontWeight = value ? FontWeight.SemiBold : FontWeight.Normal;
	}

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvButton()
	{
		FontSize = AvTheme.FontSize;
		MinHeight = AvTheme.InputHeight;
		MinWidth = 0;
		Padding = new Thickness(8, 2, 8, 2);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		HorizontalContentAlignment = HorizontalAlignment.Center;
		VerticalContentAlignment = VerticalAlignment.Center;

		AddHandler(Button.ClickEvent, OnButtonClick);

		UpdateVisual();
	}

	private void OnButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (Toggles)
		{
			Checked = !Checked;
			CheckedChanged?.Invoke(Checked);
		}

		Click?.Invoke();
	}

	/// <summary>
	/// Нажатое состояние показывается прямой записью кистей, а не стилем.
	///
	/// Локально заданное значение свойства в Avalonia сильнее стиля, поэтому
	/// подсветка наведения у залипшей кнопки пропадает — и это осознанная
	/// цена: состояние «включено» важнее, а без локальной записи тема Fluent
	/// перекрашивала бы кнопку обратно при каждом движении мыши.
	/// </summary>
	private void UpdateVisual()
	{
		if (_checked)
		{
			Background = AvTheme.Accent;
			Foreground = Brushes.White;
			BorderBrush = AvTheme.Accent;
		}
		else
		{
			Background = AvTheme.Surface;
			Foreground = AvTheme.Text;
			BorderBrush = AvTheme.BorderLine;
		}

		BorderThickness = new Thickness(1);
	}
}

/// <summary>Галочка — замена SkCheckBox.</summary>
public sealed class AvCheckBox : CheckBox
{
	public string Text
	{
		get => Content as string ?? string.Empty;
		set => Content = value;
	}

	/// <summary>
	/// Сквозное свойство к IsChecked. Нужно, чтобы перенесённый код
	/// (Checked = shape.Visible) не пришлось править.
	/// </summary>
	public bool Checked
	{
		get => IsChecked == true;
		set
		{
			// Программная установка не поднимает CheckedChanged: панель
			// задаёт галочки при сборке, и событие оттуда записывало бы
			// в модель и в историю при каждой смене выделения.
			_suppress = true;

			try
			{
				IsChecked = value;
			}
			finally
			{
				_suppress = false;
			}
		}
	}

	private bool _suppress;

	public event Action<bool>? CheckedChanged;

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvCheckBox()
	{
		FontSize = AvTheme.FontSize;
		Foreground = AvTheme.Text;
		MinHeight = AvTheme.InputHeight;
		VerticalAlignment = VerticalAlignment.Center;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == IsCheckedProperty && !_suppress)
			CheckedChanged?.Invoke(IsChecked == true);
	}
}

/// <summary>
/// Текстовое поле — замена SkTextBox.
///
/// EditingFinished поднимается при Enter и при потере фокуса — то же правило,
/// что и в Skia-версии: правка применяется, когда пользователь закончил ввод,
/// а не на каждой букве. Благодаря этому TAB из поля ширины сначала применяет
/// введённое число и только потом переводит фокус.
/// </summary>
public class AvTextBox : TextBox
{
	private string _committed = string.Empty;

	public new Action<string>? TextChanged { get; set; }

	public Action<string>? EditingFinished { get; set; }

	/// <summary>
	/// Высота поля изменилась. Нужно многострочному полю текста: пока его
	/// содержимое растёт, панель должна пересчитать раскладку.
	///
	/// Вызов отложен через Dispatcher: обработчик помечает раскладку
	/// устаревшей, а делать это прямо внутри прохода раскладки нельзя.
	/// </summary>
	public new Action? SizeChanged { get; set; }

	private double _lastArrangedHeight = -1;

	public string Placeholder
	{
		get => Watermark ?? string.Empty;
		set => Watermark = value;
	}

	public bool Multiline
	{
		get => AcceptsReturn;
		set
		{
			AcceptsReturn = value;
			TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
		}
	}

	public bool WordWrap
	{
		get => TextWrapping == TextWrapping.Wrap;
		set => TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
	}

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvTextBox()
	{
		FontSize = AvTheme.FontSize;
		MinHeight = AvTheme.InputHeight;
		Height = AvTheme.InputHeight;
		Padding = new Thickness(6, 0, 6, 0);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		VerticalContentAlignment = VerticalAlignment.Center;

		// Подписка, а не переопределение OnGotFocus/OnLostFocus: у этих
		// методов между версиями Avalonia менялся тип аргумента, а события
		// называются одинаково, и типы в коде называть не нужно.
		//
		// Исходное значение запоминается при входе в поле: без него
		// EditingFinished поднимался бы и тогда, когда пользователь просто
		// прошёл по полю табуляцией, ничего не изменив.
		GotFocus += (_, _) => _committed = Text ?? string.Empty;

		LostFocus += (_, _) => FinishEditing();
	}

	/// <summary>Применить набранное немедленно.</summary>
	public void FinishEditing()
	{
		string text = Text ?? string.Empty;

		if (text == _committed)
			return;

		_committed = text;
		EditingFinished?.Invoke(text);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		// Событие поднимается только когда поле в фокусе, то есть когда
		// текст действительно набирают. Программная запись (заполнение
		// панели из модели, откат истории, Esc) фокуса не требует —
		// и обратной записи в модель из неё быть не должно.
		if (change.Property == TextProperty && IsFocused)
			TextChanged?.Invoke(Text ?? string.Empty);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var result = base.ArrangeOverride(finalSize);

		if (SizeChanged != null && Math.Abs(result.Height - _lastArrangedHeight) > 0.5)
		{
			_lastArrangedHeight = result.Height;

			Avalonia.Threading.Dispatcher.UIThread.Post(() => SizeChanged?.Invoke());
		}

		return result;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Enter && !AcceptsReturn)
		{
			FinishEditing();
			e.Handled = true;

			return;
		}

		if (e.Key == Key.Escape)
		{
			// Esc возвращает значение, с которым вошли в поле: иначе
			// отменить начатую правку было бы нечем.
			Text = _committed;
			e.Handled = true;

			return;
		}

		base.OnKeyDown(e);
	}
}

/// <summary>
/// Текстовое поле только для чисел. Замена SkNumTextBox: буквы в поле
/// размера не попадают вообще, а не отбрасываются при разборе.
/// </summary>
public class AvNumTextBox : AvTextBox
{
	public bool AllowNegative { get; set; } = true;
	public bool AllowDecimal { get; set; } = true;

	protected override void OnTextInput(TextInputEventArgs e)
	{
		string s = e.Text ?? string.Empty;

		foreach (char c in s)
		{
			bool ok = char.IsDigit(c)
				|| (AllowNegative && (c == '-' || c == '+'))
				|| (AllowDecimal && (c == '.' || c == ','));

			if (!ok)
			{
				e.Handled = true;

				return;
			}
		}

		base.OnTextInput(e);
	}
}

#endregion

#region Числовое поле

/// <summary>
/// Числовое поле со стрелками — замена SkNumericUpDown.
///
/// Собрано из полей и кнопок вручную, а не на встроенном NumericUpDown
/// Avalonia, по одной причине: нужно состояние «значение не задано». Панель
/// показывает пустое поле, когда выделено несколько фигур с разными
/// значениями, и подставить туда любое число значило бы соврать о той
/// фигуре, которую пользователь тоже выделил.
/// </summary>
public sealed class AvNumericUpDown : Border
{
	private readonly AvNumTextBox _box;
	private readonly RepeatButton _up;
	private readonly RepeatButton _down;

	private bool _settingText;

	public float Value { get; private set; }

	public float Minimum { get; set; } = -100000f;
	public float Maximum { get; set; } = 100000f;
	public float Increment { get; set; } = 1f;
	public int Decimals { get; set; } = 2;

	public bool AllowNegative
	{
		get => _box.AllowNegative;
		set => _box.AllowNegative = value;
	}

	public bool AllowDecimal
	{
		get => _box.AllowDecimal;
		set => _box.AllowDecimal = value;
	}

	public Action<float>? ValueChanged { get; set; }

	/// <summary>Задано ли значение. false — поле показано пустым.</summary>
	public bool HasValue { get; private set; } = true;

	/// <summary>Поле ввода. Нужно обработчику табуляции и проверке «идёт ли ввод».</summary>
	public AvNumTextBox Editor => _box;

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvNumericUpDown()
	{
		Background = AvTheme.Surface;
		BorderBrush = AvTheme.BorderLine;
		BorderThickness = new Thickness(1);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		Height = AvTheme.InputHeight;

		_box = new AvNumTextBox
		{
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Height = double.NaN,
			MinHeight = 0,
			Padding = new Thickness(6, 0, 2, 0),
			VerticalAlignment = VerticalAlignment.Stretch
		};

		_box.EditingFinished = _ => CommitEditing();

		_up = CreateArrow("▴", +1);
		_down = CreateArrow("▾", -1);

		var arrows = new AvFlow
		{
			Direction = AvFlowDirection.Vertical,
			Gap = 0,
			Width = 16
		};

		arrows.Add(_up);
		arrows.Add(_down);

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto")
		};

		Grid.SetColumn(_box, 0);
		Grid.SetColumn(arrows, 1);

		grid.Children.Add(_box);
		grid.Children.Add(arrows);

		Child = grid;

		SetValue(0f);
	}

	private RepeatButton CreateArrow(string glyph, int sign)
	{
		var button = new RepeatButton
		{
			Content = new TextBlock
			{
				Text = glyph,
				FontSize = 8,
				Foreground = AvTheme.TextMuted,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			},
			Width = 16,
			Height = (AvTheme.InputHeight - 2) / 2,
			MinHeight = 0,
			MinWidth = 0,
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Focusable = false,
			Delay = 400,
			Interval = 60
		};

		button.Click += (_, _) => Step(sign);

		return button;
	}

	private void Step(int sign)
	{
		float start = HasValue ? Value : 0f;

		Apply(start + sign * Increment, raise: true);
	}

	public void SetEmpty()
	{
		HasValue = false;

		_settingText = true;

		try
		{
			_box.Text = string.Empty;
		}
		finally
		{
			_settingText = false;
		}
	}

	/// <summary>
	/// Записывает значение в поле. ValueChanged при этом НЕ поднимается:
	/// метод вызывается при заполнении панели из модели, и обратная запись
	/// превратилась бы в бесконечный круг.
	/// </summary>
	public void SetValue(float v, bool updateText = true)
	{
		Value = Math.Clamp(v, Minimum, Maximum);
		HasValue = true;

		if (!updateText)
			return;

		_settingText = true;

		try
		{
			_box.Text = Format(Value);
		}
		finally
		{
			_settingText = false;
		}
	}

	/// <summary>Разбирает набранное и применяет, если число изменилось.</summary>
	public void CommitEditing()
	{
		if (_settingText)
			return;

		string text = _box.Text ?? string.Empty;

		if (text.Trim().Length == 0)
		{
			// Пустое поле при нескольких выделенных фигурах — не ошибка,
			// а честное «значения разные». Стирание содержимого вручную
			// трактуется так же: применять нечего.
			SetEmpty();

			return;
		}

		if (!TryParse(text, out float parsed))
		{
			// Невалидный ввод возвращается к показанному значению:
			// оставить на экране текст, не соответствующий фигуре,
			// хуже, чем отменить правку.
			SetValue(Value);

			return;
		}

		Apply(parsed, raise: true);
	}

	private void Apply(float v, bool raise)
	{
		float clamped = Math.Clamp(v, Minimum, Maximum);
		bool changed = !HasValue || MathF.Abs(clamped - Value) > 1e-6f;

		SetValue(clamped);

		if (changed && raise)
			ValueChanged?.Invoke(clamped);
	}

	private string Format(float v)
	{
		string format = Decimals <= 0 ? "0" : "0." + new string('#', Decimals);

		return v.ToString(format, CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Запятая принимается наравне с точкой: раскладка клавиатуры у
	/// пользователя русская, и запятая на цифровом блоке — то, что
	/// нажимается само.
	/// </summary>
	private static bool TryParse(string s, out float v)
	{
		s = (s ?? string.Empty).Trim().Replace(',', '.');

		return float.TryParse(
			s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
	}
}

#endregion

#region Выпадающий список

public sealed class AvDropDownItem
{
	public string Value { get; set; } = string.Empty;
	public string Text { get; set; } = string.Empty;

	/// <summary>Превью — узор, текстура, образец линии. null — только текст.</summary>
	public Bitmap? PreviewBitmap { get; set; }

	/// <summary>Шрифт, которым показать текст. Нужно списку шрифтов.</summary>
	public string? FontFamily { get; set; }

	public AvDropDownItem()
	{
	}

	public AvDropDownItem(string value, string text, string? fontFamily)
	{
		Value = value;
		Text = text;
		FontFamily = fontFamily;
	}

	public AvDropDownItem(string value, string text, Bitmap? previewBitmap = null)
	{
		Value = value;
		Text = text;
		PreviewBitmap = previewBitmap;
	}

	/// <summary>Перегрузка для кода, который строит превью через Skia.</summary>
	public AvDropDownItem(string value, string text, SKBitmap? previewBitmap)
		: this(value, text, previewBitmap.ToAvalonia())
	{
	}

	public override string ToString() => Text;
}

/// <summary>
/// Выпадающий список — замена SkDropDownBox.
///
/// Базовое свойство Items скрыто: перенесённый код читает из него свои
/// AvDropDownItem, а штатная коллекция ComboBox отдала бы те же объекты
/// как object.
/// </summary>
public sealed class AvDropDownBox : ComboBox
{
	private readonly List<AvDropDownItem> _items = new();
	private bool _settingItems;

	public new IReadOnlyList<AvDropDownItem> Items => _items;

	public string SelectedText =>
		SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex].Text : string.Empty;

	/// <summary>
	/// Значение выбранного элемента. Скрывает одноимённое свойство
	/// SelectingItemsControl: там это object, выбранный по SelectedValueBinding,
	/// а здесь — строковый идентификатор элемента AvDropDownItem, которым
	/// панель свойств и обменивается с моделью.
	/// </summary>
	public new string SelectedValue =>
		SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex].Value : string.Empty;

	public double ItemHeight { get; set; } = 26;

	public double DropDownMaxHeight
	{
		get => MaxDropDownHeight;
		set => MaxDropDownHeight = value;
	}

	public double PreviewBoxSize { get; set; } = 22;

	public Action<int, string>? SelectedIndexChanged { get; set; }

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvDropDownBox()
	{
		FontSize = AvTheme.FontSize;
		Height = AvTheme.InputHeight;
		MinHeight = AvTheme.InputHeight;
		Padding = new Thickness(6, 0, 6, 0);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		MaxDropDownHeight = 260;
		HorizontalAlignment = HorizontalAlignment.Stretch;

		ItemTemplate = new FuncDataTemplate<AvDropDownItem>(
			(item, _) => BuildItemView(item));
	}

	private Control BuildItemView(AvDropDownItem? item)
	{
		var row = new AvFlow
		{
			Direction = AvFlowDirection.Horizontal,
			Gap = 6
		};

		if (item?.PreviewBitmap != null)
		{
			row.Add(new Avalonia.Controls.Image
			{
				Source = item.PreviewBitmap,
				Width = PreviewBoxSize,
				Height = PreviewBoxSize,
				Stretch = Stretch.Uniform,
				VerticalAlignment = VerticalAlignment.Center
			});
		}

		var text = new TextBlock
		{
			Text = item?.Text ?? string.Empty,
			FontSize = AvTheme.FontSize,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};

		if (!string.IsNullOrWhiteSpace(item?.FontFamily))
		{
			try
			{
				text.FontFamily = new FontFamily(item!.FontFamily!);
			}
			catch
			{
				// Шрифт из списка системных может не открыться — название
				// тогда показывается обычным шрифтом, а не теряется.
			}
		}

		row.Add(text);

		return row;
	}

	public void SetItems(IEnumerable<string> items)
	{
		SetItems(items.Select(s => new AvDropDownItem(s, s)));
	}

	public void SetItems(IEnumerable<AvDropDownItem> items)
	{
		_settingItems = true;

		try
		{
			_items.Clear();
			_items.AddRange(items);

			ItemsSource = null;
			ItemsSource = _items;

			if (SelectedIndex >= _items.Count)
				SelectedIndex = _items.Count > 0 ? 0 : -1;
		}
		finally
		{
			_settingItems = false;
		}
	}

	public void SetSelected(string value)
	{
		int index = _items.FindIndex(i =>
			string.Equals(i.Value, value, StringComparison.Ordinal));

		if (index < 0)
			index = _items.FindIndex(i =>
				string.Equals(i.Text, value, StringComparison.OrdinalIgnoreCase));

		_settingItems = true;

		try
		{
			SelectedIndex = index;
		}
		finally
		{
			_settingItems = false;
		}
	}

	public void CloseDropDown()
	{
		IsDropDownOpen = false;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property != SelectedIndexProperty || _settingItems)
			return;

		SelectedIndexChanged?.Invoke(SelectedIndex, SelectedValue);
	}
}

#endregion

#region Кнопки с картинкой и цветом

/// <summary>
/// Образец цвета поверх шахматной подложки.
///
/// Подложка нужна для полупрозрачных цветов: без неё светло-серая заливка
/// с альфой 20 и белая с альфой 255 на панели выглядели бы одинаково.
/// </summary>
internal sealed class AvColorSwatch : Control
{
	private SKColor _color = SKColors.White;

	public AvColorSwatch()
	{
		// Без минимального размера образец внутри кнопки остался бы
		// нулевой высоты: своего желаемого размера у Control нет.
		MinWidth = 20;
		MinHeight = 14;
	}

	public SKColor Color
	{
		get => _color;
		set
		{
			_color = value;
			InvalidateVisual();
		}
	}

	public override void Render(DrawingContext context)
	{
		var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);

		if (rect.Width <= 0 || rect.Height <= 0)
			return;

		const double cell = 5;

		var light = AvTheme.Brush(new SKColor(255, 255, 255));
		var dark = AvTheme.Brush(new SKColor(205, 205, 205));

		context.FillRectangle(light, rect);

		for (int row = 0; row * cell < rect.Height; row++)
		{
			for (int col = 0; col * cell < rect.Width; col++)
			{
				if ((row + col) % 2 == 0)
					continue;

				double x = col * cell;
				double y = row * cell;

				context.FillRectangle(dark, new Rect(
					x, y,
					Math.Min(cell, rect.Width - x),
					Math.Min(cell, rect.Height - y)));
			}
		}

		context.FillRectangle(AvTheme.Brush(_color), rect);

		context.DrawRectangle(
			null,
			new Pen(AvTheme.BorderLine, 1),
			new RoundedRect(rect.Deflate(0.5), AvTheme.RadiusSmall, AvTheme.RadiusSmall));
	}
}

/// <summary>Кнопка выбора цвета — замена SkColorButton.</summary>
public sealed class AvColorButton : Button
{
	private readonly AvColorSwatch _swatch = new();

	public new Action<AvColorButton>? Click { get; set; }

	public event EventHandler<SKColor>? ColorChanged;

	/// <summary>Совместимость со старым кодом: подпись на кнопке не рисуется.</summary>
	public string Text { get; set; } = string.Empty;

	public SKColor Color
	{
		get => _swatch.Color;
		set => _swatch.Color = value;
	}

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvColorButton()
	{
		Height = AvTheme.InputHeight;
		MinWidth = 0;
		MinHeight = 0;
		Padding = new Thickness(2);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		Background = AvTheme.Surface;
		BorderBrush = AvTheme.BorderLine;
		BorderThickness = new Thickness(1);
		Content = _swatch;

		AddHandler(Button.ClickEvent, (_, _) =>
		{
			// Порядок важен и повторяет Skia-версию: диалог сначала меняет
			// Color, и только потом поднимается Click. Весь перенесённый код
			// панели читает в обработчике btn.Color как УЖЕ новый цвет.
			ShowColorDialog();

			Click?.Invoke(this);
		});
	}

	/// <summary>
	/// Записывает цвет без события — как SkColorButton.SetColor.
	/// ColorChanged поднимается только диалогом выбора цвета.
	/// </summary>
	public void SetColor(SKColor color)
	{
		Color = color;
	}

	/// <summary>
	/// Выбор цвета идёт в прежнем окне SColorDialog — это форма WinForms.
	///
	/// Своего диалога у Avalonia-панели нет и не нужно: окно работает, оно
	/// привычно пользователю, и открывается оно в том же потоке, в котором
	/// живёт и WinForms, и встроенный в него Avalonia. Переписывать подбор
	/// цвета заодно с панелью значило бы менять две вещи разом и не знать,
	/// которая из них сломалась.
	/// </summary>
	private void ShowColorDialog()
	{
		using var dlg = new SColorDialog();

		dlg.SetColorV4(new SKColorF(
			Color.Red / 255f,
			Color.Green / 255f,
			Color.Blue / 255f,
			Color.Alpha / 255f));

		if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
			return;

		var c = dlg.GetColor();

		Color = new SKColor(c.R, c.G, c.B, c.A);

		ColorChanged?.Invoke(this, Color);
	}
}

/// <summary>Кнопка со значком — замена SkPictureButton.</summary>
public sealed class AvPictureButton : Button
{
	private readonly Avalonia.Controls.Image _image = new()
	{
		Stretch = Stretch.Uniform,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center
	};

	private bool _selected;

	public new Action<AvPictureButton>? Click { get; set; }

	public Bitmap? Image
	{
		get => _image.Source as Bitmap;
		set => _image.Source = value;
	}

	public string ToolTipText
	{
		set => ToolTip.SetTip(this, string.IsNullOrWhiteSpace(value) ? null : value);
	}

	public bool Selected
	{
		get => _selected;
		set
		{
			_selected = value;
			UpdateVisual();
		}
	}

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvPictureButton()
	{
		Width = 34;
		Height = 34;
		MinWidth = 0;
		MinHeight = 0;
		Padding = new Thickness(3);
		CornerRadius = new CornerRadius(AvTheme.RadiusSmall);
		Content = _image;

		AddHandler(Button.ClickEvent, (_, _) => Click?.Invoke(this));

		UpdateVisual();
	}

	private void UpdateVisual()
	{
		Background = _selected ? AvTheme.Selected : AvTheme.Surface;
		BorderBrush = _selected ? AvTheme.Accent : AvTheme.BorderLine;
		BorderThickness = new Thickness(_selected ? 2 : 1);
	}
}

#endregion

#region Движок

/// <summary>
/// Движок — замена SkSlideBar.
///
/// Составной контрол, а не унаследованный Slider: рядом с ползунком нужна
/// подпись с текущим значением (ShowValueLabel), а у штатного Slider её нет.
/// Заодно свойства остаются своими — Value отдаётся как float, и
/// перенесённому коду не нужны приведения типа в каждом выражении.
/// </summary>
public sealed class AvSlideBar : Grid
{
	private readonly Slider _slider;
	private readonly TextBlock _label;

	private bool _suppress;

	public double Minimum
	{
		get => _slider.Minimum;
		set => _slider.Minimum = value;
	}

	public double Maximum
	{
		get => _slider.Maximum;
		set => _slider.Maximum = value;
	}

	public double SmallChange
	{
		get => _slider.SmallChange;
		set => _slider.SmallChange = value;
	}

	public bool SnapToStep
	{
		get => _slider.IsSnapToTickEnabled;
		set => _slider.IsSnapToTickEnabled = value;
	}

	public double Step
	{
		get => _slider.TickFrequency;
		set => _slider.TickFrequency = value;
	}

	public int Decimals { get; set; } = 2;

	/// <summary>Округлять ли значение до целого.</summary>
	public bool IntegerMode { get; set; }

	public bool ShowValueLabel
	{
		get => _label.IsVisible;
		set => _label.IsVisible = value;
	}

	public string? PrefixText { get; set; }
	public string? SuffixText { get; set; }

	public float Value => (float)_slider.Value;

	public int IntValue => (int)MathF.Round(Value);

	public Action<float>? ValueChanged { get; set; }

	public bool Visible { get => IsVisible; set => IsVisible = value; }
	public bool Enabled { get => IsEnabled; set => IsEnabled = value; }

	public AvSlideBar()
	{
		ColumnDefinitions = new ColumnDefinitions("*,Auto");
		Height = AvTheme.InputHeight;

		_slider = new Slider
		{
			Minimum = 0,
			Maximum = 100,
			SmallChange = 1,
			MinHeight = 0,
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};

		_label = new TextBlock
		{
			FontSize = AvTheme.FontSize,
			Foreground = AvTheme.TextMuted,
			MinWidth = 38,
			Margin = new Thickness(6, 0, 0, 0),
			TextAlignment = TextAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			IsVisible = false
		};

		Grid.SetColumn(_slider, 0);
		Grid.SetColumn(_label, 1);

		Children.Add(_slider);
		Children.Add(_label);

		_slider.PropertyChanged += OnSliderPropertyChanged;

		UpdateLabel();
	}

	private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property != RangeBase.ValueProperty)
			return;

		UpdateLabel();

		if (_suppress)
			return;

		ValueChanged?.Invoke(Value);
	}

	/// <summary>
	/// Записывает значение. raiseChanged = false нужен при заполнении
	/// панели из модели: обратная запись оттуда замкнула бы круг.
	/// </summary>
	public void SetValue(float value, bool raiseChanged = true)
	{
		_suppress = !raiseChanged;

		try
		{
			_slider.Value = IntegerMode ? MathF.Round(value) : value;
		}
		finally
		{
			_suppress = false;
		}
	}

	private void UpdateLabel()
	{
		string format = Decimals <= 0 ? "0" : "0." + new string('#', Decimals);

		_label.Text = (PrefixText ?? string.Empty)
			+ Value.ToString(format, CultureInfo.InvariantCulture)
			+ (SuffixText ?? string.Empty);
	}
}

#endregion
