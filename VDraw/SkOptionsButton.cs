using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

[DefaultEvent(nameof(MainButtonClick))]
[DefaultProperty(nameof(SelectedImage))]
public class SkOptionsButton : Control
{
	private const int DefaultDropDownWidth = 22;

	private bool _mouseOverMain;
	private bool _mouseOverDropDown;
	private bool _pressedMain;
	private bool _pressedDropDown;
	private bool _menuVisible;

	private ContextMenuStrip? _dropDownMenu;
	private Image? _selectedImage;
	private int _dropDownWidth = DefaultDropDownWidth;

	public SkOptionsButton()
	{
		SetStyle(
			ControlStyles.AllPaintingInWmPaint |
			ControlStyles.UserPaint |
			ControlStyles.OptimizedDoubleBuffer |
			ControlStyles.ResizeRedraw |
			ControlStyles.Selectable |
			ControlStyles.SupportsTransparentBackColor,
			true);

		BackColor = SystemColors.Control;
		ForeColor = SystemColors.ControlText;
		Size = new Size(46, 28);
		TabStop = true;
	}

	[Category("Appearance")]
	[Description("Image shown in the main part of the button.")]
	[DefaultValue(null)]
	public Image? SelectedImage
	{
		get => _selectedImage;
		set
		{
			_selectedImage = value;
			Invalidate();
		}
	}

	[Category("Behavior")]
	[Description("Context menu shown when the dropdown part is clicked.")]
	[DefaultValue(null)]
	public ContextMenuStrip? DropDownMenu
	{
		get => _dropDownMenu;
		set
		{
			if (_dropDownMenu != null)
				_dropDownMenu.Closed -= DropDownMenu_Closed;

			_dropDownMenu = value;

			if (_dropDownMenu != null)
				_dropDownMenu.Closed += DropDownMenu_Closed;

			Invalidate();
		}
	}

	[Category("Layout")]
	[Description("Width of the dropdown arrow button.")]
	[DefaultValue(DefaultDropDownWidth)]
	public int DropDownWidth
	{
		get => _dropDownWidth;
		set
		{
			int newValue = Math.Max(16, value);
			if (_dropDownWidth == newValue)
				return;

			_dropDownWidth = newValue;
			Invalidate();
		}
	}

	[Browsable(false)]
	public Rectangle MainButtonBounds => new Rectangle(0, 0, Math.Max(0, Width - _dropDownWidth), Height);

	[Browsable(false)]
	public Rectangle DropDownButtonBounds => new Rectangle(Math.Max(0, Width - _dropDownWidth), 0, _dropDownWidth, Height);

	[Category("Action")]
	public event EventHandler? MainButtonClick;

	protected override void OnMouseMove(MouseEventArgs e)
	{
		bool overMain = MainButtonBounds.Contains(e.Location);
		bool overDrop = DropDownButtonBounds.Contains(e.Location);

		if (_mouseOverMain != overMain || _mouseOverDropDown != overDrop)
		{
			_mouseOverMain = overMain;
			_mouseOverDropDown = overDrop;
			Invalidate();
		}

		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_mouseOverMain = false;
		_mouseOverDropDown = false;
		_pressedMain = false;
		_pressedDropDown = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			Focus();

			if (DropDownButtonBounds.Contains(e.Location))
			{
				_pressedDropDown = true;
				_pressedMain = false;
			}
			else if (MainButtonBounds.Contains(e.Location))
			{
				_pressedMain = true;
				_pressedDropDown = false;
			}

			Invalidate();
		}

		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		bool wasPressedMain = _pressedMain;
		bool wasPressedDrop = _pressedDropDown;

		_pressedMain = false;
		_pressedDropDown = false;
		Invalidate();

		if (e.Button == MouseButtons.Left)
		{
			if (wasPressedDrop && DropDownButtonBounds.Contains(e.Location))
			{
				ShowDropDownMenu();
				return;
			}

			if (wasPressedMain && MainButtonBounds.Contains(e.Location))
			{
				MainButtonClick?.Invoke(this, EventArgs.Empty);
				return;
			}
		}

		base.OnMouseUp(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
		{
			MainButtonClick?.Invoke(this, EventArgs.Empty);
			e.Handled = true;
			return;
		}

		if (e.KeyCode == Keys.Down)
		{
			ShowDropDownMenu();
			e.Handled = true;
			return;
		}

		base.OnKeyDown(e);
	}

	public void ShowDropDownMenu()
	{
		if (_dropDownMenu == null || _menuVisible)
			return;

		_menuVisible = true;
		Invalidate();
		_dropDownMenu.Show(this, new Point(0, Height));
	}

	private void DropDownMenu_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
	{
		_menuVisible = false;
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _dropDownMenu != null)
			_dropDownMenu.Closed -= DropDownMenu_Closed;

		base.Dispose(disposing);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.Clear(BackColor);

		Rectangle mainRect = MainButtonBounds;
		Rectangle dropRect = DropDownButtonBounds;

		Color mainBack = BackColor;
		Color dropBack = BackColor;
		Color borderColor = SystemColors.ControlDark;
		Color arrowColor = Enabled ? ForeColor : SystemColors.GrayText;

		if (!Enabled)
		{
			mainBack = SystemColors.Control;
			dropBack = SystemColors.Control;
		}
		else
		{
			if (_pressedMain)
				mainBack = ControlPaint.Dark(BackColor);
			else if (_mouseOverMain)
				mainBack = ControlPaint.Light(BackColor);

			if (_pressedDropDown || _menuVisible)
				dropBack = ControlPaint.Dark(BackColor);
			else if (_mouseOverDropDown)
				dropBack = ControlPaint.Light(BackColor);
		}

		using (var b = new SolidBrush(mainBack))
			g.FillRectangle(b, mainRect);

		using (var b = new SolidBrush(dropBack))
			g.FillRectangle(b, dropRect);

		using (var p = new Pen(borderColor))
		{
			g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
			g.DrawLine(p, dropRect.Left, 4, dropRect.Left, Height - 5);
		}

		if (Focused && ShowFocusCues)
		{
			Rectangle focusRect = mainRect;
			focusRect.Inflate(-3, -3);
			ControlPaint.DrawFocusRectangle(g, focusRect);
		}

		if (_selectedImage != null)
		{
			int maxW = Math.Max(1, mainRect.Width - 8);
			int maxH = Math.Max(1, mainRect.Height - 8);

			Size src = _selectedImage.Size;
			float scale = Math.Min(1f, Math.Min((float)maxW / src.Width, (float)maxH / src.Height));

			int drawW = Math.Max(1, (int)(src.Width * scale));
			int drawH = Math.Max(1, (int)(src.Height * scale));

			int x = mainRect.Left + (mainRect.Width - drawW) / 2;
			int y = mainRect.Top + (mainRect.Height - drawH) / 2;

			g.DrawImage(_selectedImage, new Rectangle(x, y, drawW, drawH));
		}

		int cx = dropRect.Left + dropRect.Width / 2;
		int cy = dropRect.Top + dropRect.Height / 2;

		Point[] arrow =
		{
			new Point(cx - 4, cy - 2),
			new Point(cx + 4, cy - 2),
			new Point(cx, cy + 2)
		};

		using (var b = new SolidBrush(arrowColor))
			g.FillPolygon(b, arrow);
	}
}