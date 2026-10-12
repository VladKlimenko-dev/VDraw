using SkiaSharp;

using System.Globalization;
using System.Text.Json;

using VDraw.Documents;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Настройки приложения.
///
/// Здесь только то, что относится к виду и поведению редактора: цвета
/// холста и сетки, линейки, привязки, значения по умолчанию для новых
/// фигур и размеры маркеров. Всё, что живёт внутри документа — формат
/// листа, единицы, слои конкретных фигур, — сюда не вынесено намеренно:
/// это свойства работы, а не программы, и меняются они в своих местах.
///
/// Разметка лежит в SettingsForm.Designer.cs и правится конструктором форм.
/// </summary>
public sealed partial class SettingsForm : Form
{
	/// <summary>
	/// Цветовые кнопки вместе с доступом к своему полю в Settings.
	///
	/// Кнопка сама по себе цвет не хранит: BackColor у неё — способ
	/// показать значение, а не держать его. Прозрачный цвет в BackColor
	/// не переживает присваивания, а среди настроек есть тень страницы
	/// с альфой.
	/// </summary>
	private readonly List<ColorBinding> _colors = new();

	private sealed class ColorBinding
	{
		public Button Button = null!;
		public Func<SKColor> Read = null!;
		public Action<SKColor> Write = null!;
		public SKColor Value;
	}

	/// <summary>Настройки изменены и применены. По нему перерисовывается холст.</summary>
	public static event Action? SettingsApplied;

	public SettingsForm()
	{
		InitializeComponent();
		WinFormsTheme.Register(this);

		BindColors();

		// Английские подписи расставил дизайнер — здесь они переводятся
		// тем же механизмом, что и в главном окне.
		Localization.Register(this);

		foreach (var binding in _colors)
			binding.Button.Click += OnPickColor;

		applyButton.Click += (_, _) => ApplyToSettings();
		resetButton.Click += OnRestoreDefaults;

		LoadFromSettings();
	}

	/// <summary>
	/// Связывает кнопки с полями. Единственное место, где имя кнопки
	/// встречается вместе с именем настройки, — добавляя параметр,
	/// править нужно только здесь и в дизайнере.
	/// </summary>
	private void BindColors()
	{
		void Bind(Button button, Func<SKColor> read, Action<SKColor> write)
			=> _colors.Add(new ColorBinding { Button = button, Read = read, Write = write });

		Bind(colCanvas, () => ToColor(Settings.background_color), c => Settings.background_color = c);
		Bind(colPageBack, () => ToColor(Settings.page_default_background_color), c => Settings.page_default_background_color = c);
		Bind(colPageBorder, () => ToColor(Settings.page_border_color), c => Settings.page_border_color = c);
		Bind(colPageShadow, () => ToColor(Settings.page_shadow_color), c => Settings.page_shadow_color = c);

		Bind(colGridMinor, () => Settings.grid_minor_color, c => Settings.grid_minor_color = c);
		Bind(colGridMedium, () => Settings.grid_medium_color, c => Settings.grid_medium_color = c);
		Bind(colGridMajor, () => Settings.grid_major_color, c => Settings.grid_major_color = c);
		Bind(colGridZero, () => Settings.grid_zero_color, c => Settings.grid_zero_color = c);
		Bind(colPrintBorder, () => Settings.print_border_color, c => Settings.print_border_color = c);

		Bind(colSnapPoint, () => Settings.snap_point_color, c => Settings.snap_point_color = c);
		Bind(colSnapLine, () => Settings.snap_line_color, c => Settings.snap_line_color = c);

		Bind(colShapeLine, () => Settings.shape_default_line_color, c => Settings.shape_default_line_color = c);
		Bind(colShapeFill, () => Settings.shape_default_fill_color, c => Settings.shape_default_fill_color = c);

		Bind(colHandleFill, () => Settings.handle_fill_color, c => Settings.handle_fill_color = c);
		Bind(colHandleLine, () => Settings.handle_line_color, c => Settings.handle_line_color = c);
	}

	#region Чтение и запись

	private void LoadFromSettings()
	{
		chkShowRulers.Checked = Settings.show_rulers;
		chkShowGuidelines.Checked = Settings.show_guidelines;
		chkShowGrid.Checked = Settings.show_grid;
		chkShowPrintMargins.Checked = Settings.show_print_margins;

		SetNumber(numRulerThickness, Settings.RulerThickness);
		SetNumber(numRulerZeroMark, Settings.RulerZeroMarkSize);
		SetNumber(numRulerMajorMark, Settings.RulerMajorMarkSize);
		SetNumber(numRulerMinorMark, Settings.RulerMinorMarkSize);

		chkSnapObjects.Checked = Settings.snap_objects;
		chkSnapGuidelines.Checked = Settings.snap_to_guidelines;
		chkDynamicGuides.Checked = Settings.dynamic_guidelines;

		SetNumber(numSnapDistance, Settings.snap_dist);
		SetNumber(numSnapPointSize, Settings.snap_point_size);

		SetNumber(numLineThickness, Settings.shape_default_line_thickness_mm);
		SetNumber(numPatternLength, Settings.shape_default_pattern_length_mm);

		SetNumber(numHandleSize, Settings.handle_size);
		SetNumber(numHandleLineWidth, Settings.handle_line_thickness);
		SetNumber(numGripRadius, Settings.grip_radius);
		SetNumber(numCurvePointSize, Settings.curve_point_size);
		SetNumber(numCurveCpSize, Settings.curve_cp_size);

		foreach (var binding in _colors)
		{
			binding.Value = binding.Read();
			ShowColor(binding);
		}
	}

	private void ApplyToSettings()
	{
		SkUiTheme.SetTheme(SkThemeKind.Light);

		Settings.show_rulers = chkShowRulers.Checked;
		Settings.show_guidelines = chkShowGuidelines.Checked;
		Settings.show_grid = chkShowGrid.Checked;
		Settings.show_print_margins = chkShowPrintMargins.Checked;

		Settings.RulerThickness = (int)numRulerThickness.Value;
		Settings.RulerZeroMarkSize = (int)numRulerZeroMark.Value;
		Settings.RulerMajorMarkSize = (int)numRulerMajorMark.Value;
		Settings.RulerMinorMarkSize = (int)numRulerMinorMark.Value;

		Settings.snap_objects = chkSnapObjects.Checked;
		Settings.snap_to_guidelines = chkSnapGuidelines.Checked;
		Settings.dynamic_guidelines = chkDynamicGuides.Checked;

		Settings.snap_dist = (float)numSnapDistance.Value;
		Settings.snap_point_size = (float)numSnapPointSize.Value;

		Settings.shape_default_line_thickness_mm = (float)numLineThickness.Value;
		Settings.shape_default_pattern_length_mm = (float)numPatternLength.Value;

		Settings.handle_size = (float)numHandleSize.Value;
		Settings.handle_line_thickness = (float)numHandleLineWidth.Value;
		Settings.grip_radius = (float)numGripRadius.Value;
		Settings.curve_point_size = (float)numCurvePointSize.Value;
		Settings.curve_cp_size = (float)numCurveCpSize.Value;

		foreach (var binding in _colors)
			binding.Write(binding.Value);

		SettingsStore.Save();

		SettingsApplied?.Invoke();
	}

	/// <summary>
	/// Возвращает поля к значениям, с которыми программа поставляется.
	///
	/// Не к тем, что были при открытии окна: для этого есть «Отмена».
	/// Кнопка меняет только контролы — до нажатия «Применить» ничего
	/// не происходит, и передумать можно без последствий.
	/// </summary>
	private void OnRestoreDefaults(object? sender, EventArgs e)
	{
		var answer = MessageBox.Show(
			this,
			L.T("Restore all settings on this page to their defaults?"),
			L.T("Settings"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Question);

		if (answer != DialogResult.Yes)
			return;

		SettingsStore.RestoreDefaults();

		LoadFromSettings();
	}

	#endregion

	#region Цвета

	private void OnPickColor(object? sender, EventArgs e)
	{
		if (sender is not Button button)
			return;

		var binding = _colors.FirstOrDefault(b => ReferenceEquals(b.Button, button));

		if (binding == null)
			return;

		using var dialog = new SColorDialog();

		dialog.SetSKColor(binding.Value);

		if (dialog.ShowDialog(this) != DialogResult.OK)
			return;

		binding.Value = dialog.GetSKColor();

		ShowColor(binding);
	}

	/// <summary>
	/// Показывает цвет на кнопке.
	///
	/// Прозрачность в BackColor не передашь, поэтому полупрозрачный цвет
	/// смешивается с белым — так на кнопке видно то же, что получится
	/// на белом листе. Текст на кнопке служит подписью значения альфы:
	/// без неё чёрная тень и полупрозрачная выглядели бы одинаково.
	/// </summary>
	private static void ShowColor(ColorBinding binding)
	{
		var c = binding.Value;

		float a = c.Alpha / 255f;

		binding.Button.BackColor = Color.FromArgb(
			255,
			(byte)(c.Red * a + 255 * (1 - a)),
			(byte)(c.Green * a + 255 * (1 - a)),
			(byte)(c.Blue * a + 255 * (1 - a)));

		binding.Button.Text = c.Alpha == 255
			? string.Empty
			: $"{c.Alpha * 100 / 255}%";

		binding.Button.ForeColor = c.Alpha > 128 && c.Red + c.Green + c.Blue < 380
			? Color.White
			: Color.Black;
	}

	private static SKColor ToColor(SKColorF c) => new(
		(byte)Math.Clamp(c.Red * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Green * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Blue * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Alpha * 255f, 0f, 255f));

	#endregion

	/// <summary>
	/// Ставит значение, не выходя за пределы счётчика.
	///
	/// Без обрезки настройка, сохранённая прежней сборкой с другими
	/// пределами, уронила бы окно исключением при открытии.
	/// </summary>
	private static void SetNumber(NumericUpDown box, float value)
		=> box.Value = Math.Clamp((decimal)value, box.Minimum, box.Maximum);

	private void applyButton_Click(object sender, EventArgs e)
	{
		ApplyToSettings();
	}
}

/// <summary>
/// Хранение настроек между запусками.
///
/// Файл лежит рядом с документами пользователя: каталог программы
/// доступен только на чтение, а настройки — вещь личная и на общей
/// машине у каждого свои.
///
/// Записывается плоский набор «имя — значение», а не слепок класса
/// Settings: в нём есть списки картинок и форматов бумаги, которым
/// в файле настроек делать нечего. Незнакомые имена при чтении
/// пропускаются — файл, сохранённый другой сборкой, не мешает запуску.
/// </summary>
public static class SettingsStore
{
	public static string FilePath =>
		Path.Combine(Document.DefaultDocumentsFolder, "settings.json");

	private sealed record Entry(string Key, Func<string> Read, Action<string> Write);

	private static List<Entry> Build()
	{
		var list = new List<Entry>();

		void Num(string key, Func<float> read, Action<float> write)
			=> list.Add(new Entry(
				key,
				() => read().ToString(CultureInfo.InvariantCulture),
				s => { if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) write(v); }));

		void Int(string key, Func<int> read, Action<int> write)
			=> list.Add(new Entry(
				key,
				() => read().ToString(CultureInfo.InvariantCulture),
				s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) write(v); }));

		void Flag(string key, Func<bool> read, Action<bool> write)
			=> list.Add(new Entry(
				key,
				() => read() ? "1" : "0",
				s => write(s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))));

		void Col(string key, Func<SKColor> read, Action<SKColor> write)
			=> list.Add(new Entry(
				key,
				() => Geometry.ColorToHex(read()),
				s => { if (Geometry.TryParseHexColor(s, out var c)) write(c); }));

		Int("theme", () => (int)SkUiTheme.CurrentKind, v => SkUiTheme.SetTheme((SkThemeKind)v));

		Flag("show_rulers", () => Settings.show_rulers, v => Settings.show_rulers = v);
		Flag("show_guidelines", () => Settings.show_guidelines, v => Settings.show_guidelines = v);
		Flag("show_grid", () => Settings.show_grid, v => Settings.show_grid = v);
		Flag("show_print_margins", () => Settings.show_print_margins, v => Settings.show_print_margins = v);

		Col("background_color", () => ToColor(Settings.background_color), c => Settings.background_color = c);
		Col("page_background", () => ToColor(Settings.page_default_background_color), c => Settings.page_default_background_color = c);
		Col("page_border", () => ToColor(Settings.page_border_color), c => Settings.page_border_color = c);
		Col("page_shadow", () => ToColor(Settings.page_shadow_color), c => Settings.page_shadow_color = c);

		Col("grid_minor", () => Settings.grid_minor_color, c => Settings.grid_minor_color = c);
		Col("grid_medium", () => Settings.grid_medium_color, c => Settings.grid_medium_color = c);
		Col("grid_major", () => Settings.grid_major_color, c => Settings.grid_major_color = c);
		Col("grid_zero", () => Settings.grid_zero_color, c => Settings.grid_zero_color = c);
		Col("print_border", () => Settings.print_border_color, c => Settings.print_border_color = c);

		Int("ruler_thickness", () => Settings.RulerThickness, v => Settings.RulerThickness = v);
		Int("ruler_zero_mark", () => Settings.RulerZeroMarkSize, v => Settings.RulerZeroMarkSize = v);
		Int("ruler_major_mark", () => Settings.RulerMajorMarkSize, v => Settings.RulerMajorMarkSize = v);
		Int("ruler_minor_mark", () => Settings.RulerMinorMarkSize, v => Settings.RulerMinorMarkSize = v);

		Flag("snap_objects", () => Settings.snap_objects, v => Settings.snap_objects = v);
		Flag("snap_guidelines", () => Settings.snap_to_guidelines, v => Settings.snap_to_guidelines = v);
		Flag("dynamic_guidelines", () => Settings.dynamic_guidelines, v => Settings.dynamic_guidelines = v);

		Num("snap_dist", () => Settings.snap_dist, v => Settings.snap_dist = v);
		Num("snap_point_size", () => Settings.snap_point_size, v => Settings.snap_point_size = v);

		Col("snap_point_color", () => Settings.snap_point_color, c => Settings.snap_point_color = c);
		Col("snap_line_color", () => Settings.snap_line_color, c => Settings.snap_line_color = c);

		Col("shape_line_color", () => Settings.shape_default_line_color, c => Settings.shape_default_line_color = c);
		Col("shape_fill_color", () => Settings.shape_default_fill_color, c => Settings.shape_default_fill_color = c);

		Num("shape_line_thickness_mm", () => Settings.shape_default_line_thickness_mm, v => Settings.shape_default_line_thickness_mm = v);
		Num("shape_pattern_length_mm", () => Settings.shape_default_pattern_length_mm, v => Settings.shape_default_pattern_length_mm = v);

		Num("handle_size", () => Settings.handle_size, v => Settings.handle_size = v);
		Num("handle_line_thickness", () => Settings.handle_line_thickness, v => Settings.handle_line_thickness = v);
		Col("handle_fill", () => Settings.handle_fill_color, c => Settings.handle_fill_color = c);
		Col("handle_line", () => Settings.handle_line_color, c => Settings.handle_line_color = c);

		Num("grip_radius", () => Settings.grip_radius, v => Settings.grip_radius = v);
		Num("curve_point_size", () => Settings.curve_point_size, v => Settings.curve_point_size = v);
		Num("curve_cp_size", () => Settings.curve_cp_size, v => Settings.curve_cp_size = v);

		return list;
	}

	/// <summary>
	/// Значения, с которыми программа поставляется.
	///
	/// Снимаются один раз при первом обращении — ДО чтения файла, поэтому
	/// это именно заводские значения, а не то, что пользователь сохранил
	/// в прошлый раз.
	/// </summary>
	private static Dictionary<string, string>? _defaults;

	private static void CaptureDefaults()
	{
		if (_defaults != null)
			return;

		_defaults = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var entry in Build())
			_defaults[entry.Key] = entry.Read();
	}

	/// <summary>Читает файл настроек. Вызывается при запуске, до создания окна.</summary>
	public static void Load()
	{
		CaptureDefaults();

		try
		{
			if (!File.Exists(FilePath))
				return;

			var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));

			if (saved == null)
				return;

			foreach (var entry in Build())
			{
				if (saved.TryGetValue(entry.Key, out string? value) && value != null)
					entry.Write(value);
			}
		}
		catch (Exception ex)
		{
			// Испорченный файл настроек не повод не запускаться: работаем
			// на значениях по умолчанию, а причину оставляем в журнале.
			ErrorLog.Write("Не удалось прочитать настройки", ex);
		}
	}

	public static void Save()
	{
		try
		{
			var data = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var entry in Build())
				data[entry.Key] = entry.Read();

			string? folder = Path.GetDirectoryName(FilePath);

			if (!string.IsNullOrEmpty(folder))
				Directory.CreateDirectory(folder);

			File.WriteAllText(
				FilePath,
				JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex)
		{
			ErrorLog.Write("Не удалось сохранить настройки", ex);
		}
	}

	public static void RestoreDefaults()
	{
		CaptureDefaults();

		if (_defaults == null)
			return;

		foreach (var entry in Build())
		{
			if (_defaults.TryGetValue(entry.Key, out string? value))
				entry.Write(value);
		}
	}

	private static SKColor ToColor(SKColorF c) => new(
		(byte)Math.Clamp(c.Red * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Green * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Blue * 255f, 0f, 255f),
		(byte)Math.Clamp(c.Alpha * 255f, 0f, 255f));
}
