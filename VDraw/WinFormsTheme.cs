using System.Runtime.InteropServices;

using SkiaSharp;

using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Перекраска обычных элементов Windows Forms под текущую тему.
///
/// Всё, что рисует SkiaSharp, уже следует SkUiTheme и здесь не трогается:
/// такие элементы узнаются по сборке — они наши, а значит рисуют себя сами.
/// Красить приходится только то, что рисует система: кнопки панелей,
/// подписи, поля ввода, списки и меню.
///
/// Цвета берутся из той же палитры SkUiTheme, что и холст. Отдельная
/// таблица цветов для форм неизбежно разошлась бы с ней при первой же
/// правке оттенка.
/// </summary>
public static class WinFormsTheme
{
	private static readonly List<Form> _forms = new();

	private static bool _hooked;

	/// <summary>
	/// Ставит форму под управление темы и перекрашивает её сейчас же.
	///
	/// Вызывать в конце конструктора формы, после InitializeComponent:
	/// раньше элементов ещё нет, и обходить нечего.
	/// </summary>
	public static void Register(Form form)
	{
		if (form == null || _forms.Contains(form))
			return;

		_forms.Add(form);
		form.FormClosed += (_, _) => _forms.Remove(form);

		if (!_hooked)
		{
			_hooked = true;
			SkUiTheme.ThemeChanged += ApplyToAll;
		}

		Apply(form);
	}

	public static void ApplyToAll()
	{
		foreach (var form in _forms.ToList())
			Apply(form);
	}

	#region Обход дерева

	public static void Apply(Form form)
	{
		var pal = SkUiTheme.Current;

		form.SuspendLayout();

		try
		{
			form.BackColor = ToColor(pal.PanelBack);
			form.ForeColor = ToColor(pal.Text);

			ApplyToChildren(form, pal);
		}
		finally
		{
			form.ResumeLayout(true);
		}

		ApplyTitleBar(form, pal.IsDark);

		form.Invalidate(true);
	}

	private static void ApplyToChildren(Control parent, SkUiPalette pal)
	{
		foreach (Control control in parent.Controls)
		{
			// Наши собственные элементы рисуют себя сами и уже читают
			// SkUiTheme. Перекрашивать их значило бы задать им цвет фона,
			// который они всё равно закрасят, а заодно испортить
			// прозрачные подложки.
			if (IsOwnDrawnControl(control))
				continue;

			ApplyToOne(control, pal);
			ApplyToChildren(control, pal);
		}
	}

	private static bool IsOwnDrawnControl(Control control)
		=> control.GetType().Assembly == typeof(WinFormsTheme).Assembly
			&& control is not Form;

	private static void ApplyToOne(Control control, SkUiPalette pal)
	{
		switch (control)
		{
			case MenuStrip menu:
				ApplyMenu(menu, pal);
				return;

			case ToolStrip strip:
				strip.Renderer = new ThemedToolStripRenderer(pal);
				strip.BackColor = ToColor(pal.PanelBack);
				strip.ForeColor = ToColor(pal.Text);
				return;

			case Button button:
				ApplyButton(button, pal);
				return;

			case CheckBox or RadioButton:
				control.BackColor = Color.Transparent;
				control.ForeColor = ToColor(pal.Text);
				return;

			case Label:
				control.BackColor = Color.Transparent;
				control.ForeColor = ToColor(pal.Text);
				return;

			case TextBox or RichTextBox or ListBox or ListView or TreeView:
				control.BackColor = ToColor(pal.Surface);
				control.ForeColor = ToColor(pal.Text);
				return;

			case ComboBox combo:
				ApplyCombo(combo, pal);
				return;

			case NumericUpDown nud:
				ApplyNumeric(nud, pal);
				return;

			case TabControl tabs:
				tabs.BackColor = ToColor(pal.PanelBack);
				tabs.ForeColor = ToColor(pal.Text);
				return;

			case GroupBox:
				control.BackColor = Color.Transparent;
				control.ForeColor = ToColor(pal.Text);
				return;

			default:
				// Панели, разметки и всё прочее, что служит подложкой.
				control.BackColor = ToColor(pal.PanelBack);
				control.ForeColor = ToColor(pal.Text);
				return;
		}
	}

	#endregion

	#region Отдельные виды элементов

	/// <summary>
	/// Кнопка.
	///
	/// FlatStyle обязателен: при стандартном стиле кнопку рисует тема
	/// Windows, и заданный BackColor она попросту игнорирует. Заодно
	/// приходится задавать цвета наведения и нажатия — плоская кнопка
	/// без них не отзывается на мышь совсем.
	/// </summary>
	private static void ApplyButton(Button button, SkUiPalette pal)
	{
		button.FlatStyle = FlatStyle.Flat;
		button.UseVisualStyleBackColor = false;

		button.BackColor = ToColor(pal.Surface2);
		button.ForeColor = ToColor(pal.Text);

		button.FlatAppearance.BorderColor = ToColor(pal.Border);
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = ToColor(Blend(pal.Surface2, pal.Hover));
		button.FlatAppearance.MouseDownBackColor = ToColor(pal.ButtonPressed);
	}

	/// <summary>
	/// Цвет кнопки-выключателя в текущей теме.
	///
	/// Отметка «нажато» хранится прямо в BackColor кнопки, поэтому она
	/// обязана браться из палитры — иначе перекраска темы её сотрёт,
	/// а прежний жёлтый на тёмном фоне выглядел бы случайным пятном.
	/// </summary>
	public static Color ButtonChecked => ToColor(SkUiTheme.Current.Selected);

	public static Color ButtonNormal => ToColor(SkUiTheme.Current.Surface2);

	/// <summary>
	/// Выпадающий список.
	///
	/// Поле и стрелку система рисует сама, и BackColor она уважает только
	/// при FlatStyle.Flat. Сам список в раскрытом виде остаётся системным:
	/// его рисует отдельное окно, до которого стили формы не достают.
	/// Перерисовать его можно только владельцем — это отдельная работа,
	/// и без неё список будет светлым в тёмной теме.
	/// </summary>
	private static void ApplyCombo(ComboBox combo, SkUiPalette pal)
	{
		combo.FlatStyle = FlatStyle.Flat;
		combo.BackColor = ToColor(pal.Surface);
		combo.ForeColor = ToColor(pal.Text);
	}

	/// <summary>
	/// Счётчик.
	///
	/// Внутри он состоит из поля ввода и полосы со стрелками; вторая —
	/// отдельный дочерний элемент, и без явной перекраски она остаётся
	/// светлой, оставляя в тёмном поле белый прямоугольник справа.
	/// Сами стрелки система рисует системным цветом, и на это влияния нет.
	/// </summary>
	private static void ApplyNumeric(NumericUpDown nud, SkUiPalette pal)
	{
		nud.BorderStyle = BorderStyle.FixedSingle;
		nud.BackColor = ToColor(pal.Surface);
		nud.ForeColor = ToColor(pal.Text);

		foreach (Control child in nud.Controls)
		{
			child.BackColor = ToColor(pal.Surface);
			child.ForeColor = ToColor(pal.Text);
		}
	}

	private static void ApplyMenu(MenuStrip menu, SkUiPalette pal)
	{
		menu.Renderer = new ThemedToolStripRenderer(pal);
		menu.BackColor = ToColor(pal.PanelBack);
		menu.ForeColor = ToColor(pal.Text);

		ApplyMenuItems(menu.Items, pal);
	}

	private static void ApplyMenuItems(ToolStripItemCollection items, SkUiPalette pal)
	{
		foreach (ToolStripItem item in items)
		{
			item.BackColor = ToColor(pal.PanelBack);
			item.ForeColor = ToColor(pal.Text);

			if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
			{
				menuItem.DropDown.BackColor = ToColor(pal.Surface);
				menuItem.DropDown.ForeColor = ToColor(pal.Text);

				ApplyMenuItems(menuItem.DropDownItems, pal);
			}
		}
	}

	#endregion

	#region Заголовок окна

	private const int DwmwaUseImmersiveDarkMode = 20;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	/// <summary>
	/// Полоса заголовка окна принадлежит системе, а не программе, и
	/// красится отдельным вызовом. Без него тёмное окно остаётся
	/// со светлым заголовком, и это первое, что бросается в глаза.
	/// </summary>
	private static void ApplyTitleBar(Form form, bool dark)
	{
		if (!form.IsHandleCreated)
		{
			// До создания дескриптора красить нечего; повторим, когда окно
			// появится на экране.
			form.HandleCreated += (_, _) => ApplyTitleBar(form, SkUiTheme.Current.IsDark);
			return;
		}

		try
		{
			int value = dark ? 1 : 0;
			DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
		}
		catch
		{
			// На старых сборках Windows атрибута нет. Светлый заголовок
			// неприятен, но не повод падать.
		}
	}

	#endregion

	#region Цвета

	public static Color ToColor(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

	/// <summary>Накладывает полупрозрачный цвет на непрозрачный.</summary>
	private static SKColor Blend(SKColor back, SKColor over)
	{
		float a = over.Alpha / 255f;

		return new SKColor(
			(byte)(over.Red * a + back.Red * (1 - a)),
			(byte)(over.Green * a + back.Green * (1 - a)),
			(byte)(over.Blue * a + back.Blue * (1 - a)));
	}

	#endregion
}

/// <summary>
/// Отрисовщик меню и панелей.
///
/// Одного BackColor меню мало: подложку под значками, рамку выпадающего
/// списка, подсветку пункта и разделители рисует отрисовщик, и по умолчанию
/// он берёт цвета системной темы. В тёмном окне это видно сразу — светлая
/// полоса слева от пунктов и белая рамка вокруг списка.
/// </summary>
internal sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
	private readonly SkUiPalette _pal;

	public ThemedToolStripRenderer(SkUiPalette pal)
		: base(new ThemedColorTable(pal))
	{
		_pal = pal;
		RoundedEdges = false;
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = e.Item.Enabled
			? WinFormsTheme.ToColor(_pal.Text)
			: WinFormsTheme.ToColor(_pal.TextMuted);

		base.OnRenderItemText(e);
	}

	protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
	{
		e.ArrowColor = WinFormsTheme.ToColor(_pal.Text);
		base.OnRenderArrow(e);
	}
}

internal sealed class ThemedColorTable : ProfessionalColorTable
{
	private readonly SkUiPalette _pal;

	public ThemedColorTable(SkUiPalette pal)
	{
		_pal = pal;
		UseSystemColors = false;
	}

	private Color Panel => WinFormsTheme.ToColor(_pal.PanelBack);
	private Color Surface => WinFormsTheme.ToColor(_pal.Surface);
	private Color Selected => WinFormsTheme.ToColor(_pal.Selected);
	private Color Border => WinFormsTheme.ToColor(_pal.Border);

	public override Color MenuStripGradientBegin => Panel;
	public override Color MenuStripGradientEnd => Panel;
	public override Color ToolStripGradientBegin => Panel;
	public override Color ToolStripGradientMiddle => Panel;
	public override Color ToolStripGradientEnd => Panel;
	public override Color ToolStripDropDownBackground => Surface;

	public override Color ImageMarginGradientBegin => Surface;
	public override Color ImageMarginGradientMiddle => Surface;
	public override Color ImageMarginGradientEnd => Surface;

	public override Color MenuItemSelected => Selected;
	public override Color MenuItemSelectedGradientBegin => Selected;
	public override Color MenuItemSelectedGradientEnd => Selected;
	public override Color MenuItemPressedGradientBegin => Surface;
	public override Color MenuItemPressedGradientEnd => Surface;
	public override Color MenuItemBorder => Border;
	public override Color MenuBorder => Border;

	public override Color ButtonSelectedHighlight => Selected;
	public override Color ButtonSelectedGradientBegin => Selected;
	public override Color ButtonSelectedGradientEnd => Selected;
	public override Color ButtonPressedGradientBegin => WinFormsTheme.ToColor(_pal.ButtonPressed);
	public override Color ButtonPressedGradientEnd => WinFormsTheme.ToColor(_pal.ButtonPressed);

	public override Color SeparatorDark => Border;
	public override Color SeparatorLight => Border;
}
