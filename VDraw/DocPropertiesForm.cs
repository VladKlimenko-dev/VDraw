using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public partial class DocPropertiesForm : Form
{

	/// <summary>
	/// Разрешение, выбранное в этой форме.
	///
	/// Нужно пересчёту размеров: у пиксельных единиц миллиметры в одном
	/// пикселе зависят от DPI, и брать его надо из поля формы, а не из
	/// страницы — пользователь мог поменять его прямо сейчас, до нажатия OK.
	/// </summary>
	private int CurrentDpi => Math.Max(1, (int)dpi_nud.Value);
	protected PaperFormatData paper;
	public bool portrait = true;
	public bool paper_changed = false;

	public DocPropertiesForm(PaperFormatData? _paper)
	{
		if (_paper == null)
			throw new Exception("DocPropertiesForm: _paper cannot be null.");

		paper = _paper;
		InitializeComponent();
		WinFormsTheme.Register(this);
		SuspendLayout();

		format_cb.SelectedIndexChanged -= format_cb_SelectedIndexChanged;
		orientation_cb.SelectedIndexChanged -= orientation_cb_SelectedIndexChanged;
		unit_cb.SelectedIndexChanged -= unis_cb_SelectedIndexChanged;
		width_nud.ValueChanged -= width_nud_ValueChanged;
		height_nud.ValueChanged -= height_nud_ValueChanged;
		dpi_nud.ValueChanged -= dpi_nud_ValueChanged;

		for (int i = 0; i < Settings.units.Count; i++)
			unit_cb.Items.Add(Settings.units[i].title);
		for (int i = 0; i < Settings.paper_formats.Count; i++)
			format_cb.Items.Add(Settings.paper_formats[i].title);
		orientation_cb.Items.Add("Vertical");
		orientation_cb.Items.Add("Horizontal");

		unit_cb.SelectedIndex = Settings.units.FindIndex(o => o.title == paper.uom.title);
		orientation_cb.SelectedIndex = paper.Orientation == PageOrientation.Vertical ? 0 : 1;
		format_cb.SelectedIndex = Settings.paper_formats.FindIndex(o => o.title == paper.paper_format.title);
		title_tb.Text = paper.title;

		if (paper.Orientation == PageOrientation.Vertical)
		{
			width_nud.Value = (decimal)paper.size_uom.Width;
			height_nud.Value = (decimal)paper.size_uom.Height;
		}
		else
		{
			height_nud.Value = (decimal)paper.size_uom.Width;
			width_nud.Value = (decimal)paper.size_uom.Height;
		}

		dpi_nud.Value = (decimal)paper.DPI;

		dpi_nud.ValueChanged += dpi_nud_ValueChanged;
		width_nud.ValueChanged += width_nud_ValueChanged;
		height_nud.ValueChanged += height_nud_ValueChanged;
		format_cb.SelectedIndexChanged += format_cb_SelectedIndexChanged;
		unit_cb.SelectedIndexChanged += unis_cb_SelectedIndexChanged;
		orientation_cb.SelectedIndexChanged += orientation_cb_SelectedIndexChanged;

		ResumeLayout();
	}

	private void width_nud_ValueChanged(object? sender, EventArgs e)
	{
		UoM uom = Settings.units[unit_cb.SelectedIndex];
		float w = uom.GetMM((float)width_nud.Value, CurrentDpi);
		float h = uom.GetMM((float)height_nud.Value, CurrentDpi);
		if (paper.Orientation == PageOrientation.Horizontal)
		{
			float ww = w;
			w = h;
			h = ww;
		}
		PaperFormat? f = Settings.paper_formats.Find(o => o.width_mm == w && o.height_mm == h);
		if (f != null)
			format_cb.SelectedItem = f.title;
		else
			format_cb.SelectedItem = "Custom";
	}

	private void height_nud_ValueChanged(object? sender, EventArgs e)
	{
		UoM uom = Settings.units[unit_cb.SelectedIndex];
		float w = uom.GetMM((float)width_nud.Value, CurrentDpi);
		float h = uom.GetMM((float)height_nud.Value, CurrentDpi);
		if (paper.Orientation == PageOrientation.Horizontal)
		{
			float ww = w;
			w = h;
			h = ww;
		}
		PaperFormat? f = Settings.paper_formats.Find(o => o.width_mm == w && o.height_mm == h);
		if (f != null)
			format_cb.SelectedItem = f.title;
		else
			format_cb.SelectedItem = "Custom";
	}

	private void orientation_cb_SelectedIndexChanged(object? sender, EventArgs e)
	{
		float w = (float)height_nud.Value;
		float h = (float)width_nud.Value;
		height_nud.ValueChanged -= height_nud_ValueChanged;
		height_nud.Value = (decimal)h;
		height_nud.ValueChanged += height_nud_ValueChanged;

		width_nud.ValueChanged -= width_nud_ValueChanged;
		width_nud.Value = (decimal)w;
		width_nud.ValueChanged += width_nud_ValueChanged;
		paper_changed = true;

	}

	private void format_cb_SelectedIndexChanged(object? sender, EventArgs e)
	{
		UoM uom = Settings.units[unit_cb.SelectedIndex];
		PaperFormat? f = Settings.paper_formats.Find(o => o.title == (format_cb.SelectedItem as string));
		if (f == null)
			return;
		width_nud.ValueChanged -= width_nud_ValueChanged;
		width_nud.Value = (int)(f.width_mm / uom.MmPerUnit(CurrentDpi));
		width_nud.ValueChanged += width_nud_ValueChanged;
		height_nud.ValueChanged -= height_nud_ValueChanged;
		height_nud.Value = (int)(f.height_mm / uom.MmPerUnit(CurrentDpi));
		height_nud.ValueChanged += height_nud_ValueChanged;
	}

	private void unis_cb_SelectedIndexChanged(object? sender, EventArgs e)
	{
		UoM uom = Settings.units[unit_cb.SelectedIndex];
		PaperFormat? f = Settings.paper_formats.Find(o => o.title == (format_cb.SelectedItem as string));
		if (f == null)
			return;
		width_nud.ValueChanged -= width_nud_ValueChanged;
		width_nud.Value = (int)(f.width_mm / uom.MmPerUnit(CurrentDpi));
		width_nud.ValueChanged += width_nud_ValueChanged;
		height_nud.ValueChanged -= height_nud_ValueChanged;
		height_nud.Value = (int)(f.height_mm / uom.MmPerUnit(CurrentDpi));
		height_nud.ValueChanged += height_nud_ValueChanged;
	}

	private void dpi_nud_ValueChanged(object? sender, EventArgs e)
	{
	}

	private void props_ok_but_Click(object sender, EventArgs e)
	{
		if (paper != null)
		{
			UoM uom = Settings.units[unit_cb.SelectedIndex];
			if (paper.uom.title != uom.title)
				paper_changed = true;
			paper.uom = uom;
			if (paper.title != title_tb.Text)
				paper_changed = true;
			paper.title = title_tb.Text;
			PaperFormat? f = Settings.paper_formats.Find(o => o.title == (format_cb.SelectedItem as string));
			if (f == null)
				f = Settings.paper_formats[0];
			if (paper.paper_format != f)
				paper_changed = true;
			paper.paper_format = f;
			if (paper.DPI != (int)dpi_nud.Value)
				paper_changed = true;
			paper.DPI = (int)dpi_nud.Value;
			if ((int)paper.Orientation != orientation_cb.SelectedIndex)
				paper_changed = true;
			paper.Orientation = orientation_cb.SelectedIndex == 0 ? PageOrientation.Vertical : PageOrientation.Horizontal;
			if (paper.size_uom.Width != (float)width_nud.Value)
				paper_changed = true;
			paper.size_uom.Width = (float)width_nud.Value;
			if (paper.size_uom.Height != (float)height_nud.Value)
				paper_changed = true;
			paper.size_uom.Height = (float)height_nud.Value;
			return;
		}
	}
}
