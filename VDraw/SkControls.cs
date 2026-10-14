using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public static class FontResources
{
	private static readonly Dictionary<string, SKTypeface?> _typefaces =
		new(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<(string Key, float Size), SKFont> _fonts = new();

	/// <summary>
	/// Гарнитура из файла в папке Resources. Путь задаётся относительно неё,
	/// например "FluentSystemIcons-Regular.ttf" или "Fonts/MyFont.otf".
	/// Возвращает null, если файла нет или он не читается как шрифт.
	/// </summary>
	public static SKTypeface? GetTypeface(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
			return null;

		if (_typefaces.TryGetValue(relativePath, out var cached))
			return cached;

		SKTypeface? typeface = null;

		try
		{
			string full = ResourcePaths.File(relativePath);

			if (File.Exists(full))
				typeface = SKTypeface.FromFile(full);
		}
		catch
		{
			// Битый или занятый файл не должен ронять отрисовку интерфейса.
		}

		// Отрицательный результат тоже кэшируется: иначе неудачная загрузка
		// повторялась бы на каждый кадр.
		_typefaces[relativePath] = typeface;
		return typeface;
	}

	/// <summary>Шрифт заданного размера из файла. Освобождать НЕ нужно — он общий.</summary>
	public static SKFont? GetFont(string relativePath, float size)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || size <= 0f)
			return null;

		var key = (relativePath, size);

		if (_fonts.TryGetValue(key, out var cached))
			return cached;

		var typeface = GetTypeface(relativePath);

		if (typeface == null)
			return null;

		var font = new SKFont(typeface, size);
		_fonts[key] = font;
		return font;
	}
}

public sealed class SkNumericUpDown : SkControl
{
	public float Value { get; private set; }
	public float Minimum { get; set; } = -100000f;
	public float Maximum { get; set; } = 100000f;
	public float Increment { get; set; } = 1f;
	public int Decimals { get; set; } = 2;
	public Action<float>? ValueChanged { get; set; }

	private readonly SkNumTextBox _box = new();
	// Стрелки — команды, а не выключатели: без этого каждый щелчок
	// оставлял их подсвеченными, и счётчик выглядел как пара флажков.
	private readonly SkButton _up = new() { Text = "▲", Toggles = false, ShowFocus = false };
	private readonly SkButton _down = new() { Text = "▼", Toggles = false, ShowFocus = false };

	private RepeatDirection _repeatDirection = RepeatDirection.None;
	private int initial_timer_repeat_delay = 1000;
	private int timer_repeat_period = 50;
	private enum RepeatDirection
	{
		None,
		Up,
		Down
	}
	private readonly System.Windows.Forms.Timer _repeatTimer;
	private readonly System.Windows.Forms.Timer _initial_repeatTimer;
	public bool AllowNegative { get; set; } = true;
	public bool AllowDecimal { get; set; } = true;

	public SkNumericUpDown()
	{
		Add(_box);
		Add(_up);
		Add(_down);

		// От пустого состояния стрелки отсчитывают от текущего значения:
		// другой опоры нет, а запрещать стрелки при разных значениях
		// значило бы лишить пользователя единственного способа задать
		// их все одним махом.
		_up.Click = () => SetValue(Value + Increment);
		_down.Click = () => SetValue(Value - Increment);

		_box.TextChanged = s =>
		{
		};

		_box.EditingFinished = s =>
		{
			CommitText();
		};

		Bounds = new SKRect(0, 0, 120, 30);
		SyncTextFromValue();
		_initial_repeatTimer = new System.Windows.Forms.Timer();
		_initial_repeatTimer.Interval = initial_timer_repeat_delay;
		_initial_repeatTimer.Tick += _repeatTimer_Tick;
		_initial_repeatTimer.Stop();
		_repeatTimer = new System.Windows.Forms.Timer();
		_repeatTimer.Interval = timer_repeat_period;
		_repeatTimer.Tick += _repeatTimer_Tick;
		_repeatTimer.Stop();
	}

	public override SkControl? FindTopMost(SKPoint p)
	{
		if (!Visible || !Enabled || !AbsoluteBounds.Contains(p))
			return null;

		return this;
	}

	/// <summary>
	/// Попадает ли точка в стрелки, а не в поле ввода.
	///
	/// Нужно панели: FindTopMost возвращает контрол целиком, поэтому
	/// щелчок по стрелке приходил как щелчок по полю и делал его активным.
	/// Пока поле активно, панель считает, что идёт набор текста, и не
	/// обновляет значения из модели — числа в остальных строках
	/// застывали до тех пор, пока пользователь не щёлкнет мимо.
	/// </summary>
	public bool HitsSpinButtons(SKPoint p)
		=> _up.HitTest(p) || _down.HitTest(p);

	private void _repeatTimer_Tick(object? sender, EventArgs e)
	{
		if (_repeatDirection == RepeatDirection.None)
			return;

		_initial_repeatTimer.Stop();
		_repeatTimer.Stop();
		switch (_repeatDirection)
		{
			case RepeatDirection.Up:
				SetValue(Value + Increment);
				break;

			case RepeatDirection.Down:
				SetValue(Value - Increment);
				break;
		}
		_repeatTimer.Start();
	}

	/// <summary>
	/// Значение не задано — поле показывается пустым.
	///
	/// Нужно при нескольких выделенных фигурах: если параметр у них
	/// разный, показать любое из значений значило бы соврать, а показать
	/// ноль — соврать вдвойне. Пустое поле честно говорит «по-разному»
	/// и при этом остаётся рабочим: ввод в него задаёт значение всем.
	/// </summary>
	public bool HasValue { get; private set; } = true;

	public void SetEmpty()
	{
		HasValue = false;
		_box.Text = string.Empty;
	}

	public void SetValue(float v, bool updateText = true)
	{
		v = Math.Clamp(v, Minimum, Maximum);

		// Из пустого состояния выходим всегда, даже если число совпало
		// с прежним: иначе поле осталось бы пустым навсегда.
		bool wasEmpty = !HasValue;

		HasValue = true;

		if (!wasEmpty && MathF.Abs(v - Value) < 0.0001f)
		{
			if (updateText)
				SyncTextFromValue();
			return;
		}

		Value = v;

		if (updateText)
			SyncTextFromValue();

		ValueChanged?.Invoke(Value);
	}

	private void StopRepeat()
	{
		_repeatDirection = RepeatDirection.None;
		_repeatTimer.Stop();
	}

	private void SyncTextFromValue()
	{
		_box.Text = Value.ToString($"F{Decimals}");
	}

	private bool TryParseText(string s, out float value)
	{
		s = (s ?? string.Empty).Trim().Replace(',', '.');
		return float.TryParse(
			s,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out value);
	}

	public void CommitEditing()
	{
		CommitText();
	}

	/// <summary>
	/// Поле ввода счётчика.
	///
	/// Счётчик — не одиночный контрол, а связка из поля и двух стрелок,
	/// и активным должно числиться именно поле: оно хранит каретку
	/// и выделение, и оно же лежит в списке обхода по табуляции. Пока
	/// активным числился сам счётчик, его не удавалось найти в этом списке,
	/// и следующая табуляция уходила на первое поле панели вместо
	/// соседнего.
	/// </summary>
	public SkNumTextBox Editor => _box;

	private void CommitText()
	{
		if (TryParseText(_box.Text, out float v))
		{
			SetValue(v, true);
			return;
		}

		// Пустое поле стирать до нуля нельзя: оно означает «значения
		// разные», и возврат числа туда выглядел бы как ввод.
		if (!HasValue && string.IsNullOrWhiteSpace(_box.Text))
			return;

		SyncTextFromValue();
	}

	public override void Arrange()
	{
		_box.Bounds = new SKRect(0, 0, Bounds.Width - 28, Bounds.Height);
		_up.Bounds = new SKRect(Bounds.Width - 28, 0, Bounds.Width, Bounds.Height / 2f);
		_down.Bounds = new SKRect(Bounds.Width - 28, Bounds.Height / 2f, Bounds.Width, Bounds.Height);
		base.Arrange();
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (_up.HitTest(e.Location))
		{
			_up.OnMouseDown(e);
			_initial_repeatTimer.Start();
			_repeatDirection = RepeatDirection.Up;
			return true;
		}

		if (_down.HitTest(e.Location))
		{
			_down.OnMouseDown(e);
			_initial_repeatTimer.Start();
			_repeatDirection = RepeatDirection.Down;
			return true;
		}

		StopRepeat();
		return _box.OnMouseDown(e);
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool changed = false;

		changed |= _up.OnMouseMove(e);
		changed |= _down.OnMouseMove(e);
		changed |= _box.OnMouseMove(e);

		if (_repeatDirection == RepeatDirection.Up && !_up.HitTest(e.Location))
			StopRepeat();
		else if (_repeatDirection == RepeatDirection.Down && !_down.HitTest(e.Location))
			StopRepeat();

		return changed;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		bool changed = false;

		changed |= _up.OnMouseUp(e);
		changed |= _down.OnMouseUp(e);
		changed |= _box.OnMouseUp(e);

		// Состояние стрелок сбрасывается независимо от Toggles: щелчок
		// приходит и мимо кнопки, и во время автоповтора, и любая из этих
		// дорог не должна оставить стрелку выглядящей нажатой.
		ResetSpinButtons();

		StopRepeat();
		return changed;
	}

	private void ResetSpinButtons()
	{
		_up.Checked = false;
		_up.Focused = false;

		_down.Checked = false;
		_down.Focused = false;
	}

	public override bool OnMouseDoubleClick(SkMouseEvent e)
	{
		return _box.OnMouseDoubleClick(e);
	}

	public override bool OnKeyChar(char c)
	{
		return _box.OnKeyChar(c);
	}

	public override bool OnKeyDown(SkKeyEvent e)
	{
		return _box.OnKeyDown(e);
	}

	protected override void Dispose(bool disposing)
	{
		_repeatTimer.Stop();
		_repeatTimer.Dispose();
	}
}

public sealed class SkDropDownItem
{
	public string Value { get; set; } = string.Empty;
	public string Text { get; set; } = string.Empty;
	public SKBitmap? PreviewBitmap { get; set; }
	/// <summary>
	/// Гарнитура для отрисовки текста этого элемента. null — шрифт интерфейса.
	/// Используется списком шрифтов: каждое название рисуется своим шрифтом.
	/// </summary>
	public string? FontFamily { get; set; }

	public SkDropDownItem()
	{
	}

	public SkDropDownItem(string value, string text, string? fontFamily)
	{
		Value = value ?? string.Empty;
		Text = text ?? string.Empty;
		FontFamily = fontFamily;
	}


	public SkDropDownItem(string value, string text, SKBitmap? previewBitmap = null)
	{
		Value = value ?? string.Empty;
		Text = text ?? string.Empty;
		PreviewBitmap = previewBitmap;
	}

	public override string ToString() => Text;
}

public sealed class SkDropDownBox : SkControl
{
	private readonly List<SkDropDownItem> _items = new();
	private int _hoveredItemIndex = -1;
	/// <summary>Индекс первого видимого элемента списка.</summary>
	private int _scrollIndex = 0;

	/// <summary>Сколько элементов видно одновременно.</summary>
	private int VisibleItemCount
	{
		get
		{
			float h = MathF.Min(DropDownMaxHeight, MathF.Max(_items.Count, 1) * ItemHeight);
			return Math.Max(1, (int)MathF.Floor(h / MathF.Max(ItemHeight, 1f)));
		}
	}

	private int MaxScrollIndex => Math.Max(0, _items.Count - VisibleItemCount);


	public IReadOnlyList<SkDropDownItem> Items => _items;

	public int SelectedIndex { get; private set; } = -1;

	public string SelectedText =>
		SelectedIndex >= 0 && SelectedIndex < _items.Count
			? _items[SelectedIndex].Text
			: string.Empty;

	public string SelectedValue =>
		SelectedIndex >= 0 && SelectedIndex < _items.Count
			? _items[SelectedIndex].Value
			: string.Empty;

	public bool IsOpen { get; private set; }

	public float ItemHeight { get; set; } = 28f;
	public float DropDownMaxHeight { get; set; } = 220f;
	public float PreviewBoxSize { get; set; } = 22f;

	public Action<int, string>? SelectedIndexChanged { get; set; }

	public SkDropDownBox()
	{
		Bounds = new SKRect(0, 0, 160, 28);
	}

	public void SetItems(IEnumerable<string> items)
	{
		_items.Clear();

		foreach (var s in items)
			_items.Add(new SkDropDownItem(s, s));

		NormalizeSelection();
	}

	public void SetItems(IEnumerable<SkDropDownItem> items)
	{
		_items.Clear();
		_items.AddRange(items);
		NormalizeSelection();
	}

	public void SetSelected(string value)
	{
		int idx = _items.FindIndex(i => i.Value == value || i.Text == value);
		if (idx >= 0)
			SelectedIndex = idx;
	}

	private void NormalizeSelection()
	{
		if (_items.Count == 0)
			SelectedIndex = -1;
		else if (SelectedIndex < 0 || SelectedIndex >= _items.Count)
			SelectedIndex = 0;
	}

	public void CloseDropDown()
	{
		IsOpen = false;
		_hoveredItemIndex = -1;
	}

	public override void Draw(SKCanvas canvas)
	{
		var pal = SkUiTheme.Current;
		var r = AbsoluteBounds;

		using var fill = SkUiTheme.Fill(Hovered ? pal.PanelBack2 : pal.PanelBack);
		using var border = SkUiTheme.Stroke(Focused ? pal.Accent : pal.Border, Focused ? 1.5f : 1f);

		canvas.DrawRoundRect(r, 6, 6, fill);
		canvas.DrawRoundRect(r, 6, 6, border);

		using var textPaint = new SKPaint { IsAntialias = true, Color = pal.Text };

		using var mutedPaint = new SKPaint
		{
			IsAntialias = true,
			Color = new SKColor(pal.Text.Red, pal.Text.Green, pal.Text.Blue, 180)
		};

		using var uiFont = new SKFont { Size = 13f };
		using var arrowFont = new SKFont { Size = 12f };

		float left = r.Left + 8f;
		var selected = GetSelectedItem();

		if (selected?.PreviewBitmap != null)
		{
			var pr = new SKRect(
				left,
				r.MidY - PreviewBoxSize / 2f,
				left + PreviewBoxSize,
				r.MidY + PreviewBoxSize / 2f);

			DrawPreviewBitmap(canvas, selected.PreviewBitmap, pr);
			left = pr.Right + 8f;
		}

		string text = selected?.Text ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(text))
		{
			var itemFont = ResolveItemFont(selected, uiFont, 13f);

			// Обрезка: длинное название шрифта не должно заезжать на стрелку.
			canvas.Save();
			canvas.ClipRect(new SKRect(r.Left + 4f, r.Top, r.Right - 20f, r.Bottom));
			canvas.DrawText(text, left, r.MidY + 4.5f, itemFont, textPaint);
			canvas.Restore();
		}

		// Стрелка рисуется всегда, а не только при непустом тексте: иначе
		// пустой элемент выглядел как неактивный контрол.
		canvas.DrawText(IsOpen ? "▲" : "▼", r.Right - 16f, r.MidY + 4.5f, arrowFont, mutedPaint);
	}

	public override void DrawOverlay(SKCanvas canvas)
	{
		if (!IsOpen || _items.Count == 0)
			return;

		var pal = SkUiTheme.Current;
		var listRect = GetDropDownRect();

		using var fill = SkUiTheme.Fill(pal.PanelBack);
		using var border = SkUiTheme.Stroke(pal.Border);

		canvas.DrawRoundRect(listRect, 6, 6, fill);

		using var textPaint = new SKPaint { IsAntialias = true, Color = pal.Text };
		using var uiFont = new SKFont { Size = 13f };

		// Обрезка обязательна: без неё элементы за пределами списка рисовались
		// по всей панели и за её границами.
		canvas.Save();
		canvas.ClipRoundRect(new SKRoundRect(listRect, 6, 6), SKClipOperation.Intersect, true);

		int first = _scrollIndex;
		int last = Math.Min(_items.Count - 1, first + VisibleItemCount - 1);

		for (int i = first; i <= last; i++)
		{
			var itemRect = GetItemRect(i);

			if (i == SelectedIndex || i == _hoveredItemIndex)
			{
				using var itemFill = SkUiTheme.Fill(pal.PanelBack2);
				canvas.DrawRect(itemRect, itemFill);
			}

			float left = itemRect.Left + 8f;

			if (_items[i].PreviewBitmap != null)
			{
				var pr = new SKRect(
					left,
					itemRect.MidY - PreviewBoxSize / 2f,
					left + PreviewBoxSize,
					itemRect.MidY + PreviewBoxSize / 2f);

				DrawPreviewBitmap(canvas, _items[i].PreviewBitmap!, pr);
				left = pr.Right + 8f;
			}

			if (!string.IsNullOrWhiteSpace(_items[i].Text))
			{
				var itemFont = ResolveItemFont(_items[i], uiFont, 13f);
				canvas.DrawText(_items[i].Text, left, itemRect.MidY + 4.5f, itemFont, textPaint);
			}
		}

		canvas.Restore();

		// Индикатор прокрутки: без него непонятно, что список длиннее видимой части.
		if (_items.Count > VisibleItemCount)
		{
			float trackTop = listRect.Top + 2f;
			float trackHeight = listRect.Height - 4f;

			float thumbHeight = MathF.Max(16f, trackHeight * VisibleItemCount / _items.Count);
			float thumbTop = trackTop + (trackHeight - thumbHeight) * _scrollIndex / MathF.Max(1, MaxScrollIndex);

			using var thumb = SkUiTheme.Fill(pal.Border);

			canvas.DrawRoundRect(
				new SKRect(listRect.Right - 6f, thumbTop, listRect.Right - 2f, thumbTop + thumbHeight),
				2f,
				2f,
				thumb);
		}

		// Рамка поверх содержимого, иначе обрезка съедает её внутреннюю кромку.
		canvas.DrawRoundRect(listRect, 6, 6, border);
	}

	private void ScrollToSelected()
	{
		if (SelectedIndex < 0)
		{
			_scrollIndex = 0;
			return;
		}

		int visible = VisibleItemCount;

		if (SelectedIndex < _scrollIndex)
			_scrollIndex = SelectedIndex;
		else if (SelectedIndex >= _scrollIndex + visible)
			_scrollIndex = SelectedIndex - visible + 1;

		_scrollIndex = Math.Clamp(_scrollIndex, 0, MaxScrollIndex);
	}


	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (AbsoluteBounds.Contains(e.Location))
		{
			IsOpen = !IsOpen;
			Focused = true;
			_hoveredItemIndex = -1;

			// При открытии список прокручивается к выбранному элементу.
			// Без этого выбранный тридцатый шрифт открывался бы с первого.
			if (IsOpen)
				ScrollToSelected();

			return true;
		}


		if (IsOpen && GetDropDownRect().Contains(e.Location))
		{
			for (int i = 0; i < _items.Count; i++)
			{
				if (GetItemRect(i).Contains(e.Location))
				{
					SelectedIndex = i;
					IsOpen = false;
					_hoveredItemIndex = -1;
					SelectedIndexChanged?.Invoke(i, _items[i].Value);
					return true;
				}
			}

			return true;
		}

		if (IsOpen)
		{
			IsOpen = false;
			_hoveredItemIndex = -1;
		}

		return false;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool changed = false;

		bool oldHovered = Hovered;
		Hovered = AbsoluteBounds.Contains(e.Location) || (IsOpen && GetDropDownRect().Contains(e.Location));
		if (oldHovered != Hovered)
			changed = true;

		int oldIndex = _hoveredItemIndex;
		_hoveredItemIndex = -1;

		if (IsOpen && GetDropDownRect().Contains(e.Location))
		{
			for (int i = 0; i < _items.Count; i++)
			{
				if (GetItemRect(i).Contains(e.Location))
				{
					_hoveredItemIndex = i;
					break;
				}
			}
		}

		if (oldIndex != _hoveredItemIndex)
			changed = true;

		return changed;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		return AbsoluteBounds.Contains(e.Location) || (IsOpen && GetDropDownRect().Contains(e.Location));
	}

	public override bool OnMouseDoubleClick(SkMouseEvent e)
	{
		return OnMouseDown(e);
	}

	public override bool OnKeyDown(SkKeyEvent e)
	{
		if (_items.Count == 0)
			return false;

		switch (e.KeyCode)
		{
			case System.Windows.Forms.Keys.Enter:
			case System.Windows.Forms.Keys.Space:
				IsOpen = !IsOpen;
				_hoveredItemIndex = SelectedIndex;
				return true;

			case System.Windows.Forms.Keys.Escape:
				if (IsOpen)
				{
					IsOpen = false;
					_hoveredItemIndex = -1;
					return true;
				}
				return false;

			case System.Windows.Forms.Keys.Down:
				if (_items.Count == 0)
					return true;

				if (SelectedIndex < 0)
					SelectedIndex = 0;
				else if (SelectedIndex < _items.Count - 1)
					SelectedIndex++;

				_hoveredItemIndex = SelectedIndex;
				SelectedIndexChanged?.Invoke(SelectedIndex, _items[SelectedIndex].Value);
				return true;

			case System.Windows.Forms.Keys.Up:
				if (_items.Count == 0)
					return true;

				if (SelectedIndex < 0)
					SelectedIndex = 0;
				else if (SelectedIndex > 0)
					SelectedIndex--;

				_hoveredItemIndex = SelectedIndex;
				SelectedIndexChanged?.Invoke(SelectedIndex, _items[SelectedIndex].Value);
				return true;
		}

		return false;
	}

	public override bool OnMouseWheel(SkMouseEvent e)
	{
		if (!IsOpen || !GetDropDownRect().Contains(e.Location))
			return false;

		// Три строки на щелчок — то же, что в списках Windows.
		int step = 3 * Math.Sign(e.Delta);

		int next = Math.Clamp(_scrollIndex - step, 0, MaxScrollIndex);

		if (next == _scrollIndex)
			return true;   // событие всё равно наше: панель прокручивать не надо

		_scrollIndex = next;
		return true;
	}

	public bool HitTestOverlay(SKPoint p)
	{
		return IsOpen && GetDropDownRect().Contains(p);
	}

	public bool CloseDropDownsOutside(SKPoint p)
	{
		if (!IsOpen)
			return false;

		if (AbsoluteBounds.Contains(p) || GetDropDownRect().Contains(p))
			return false;

		IsOpen = false;
		_hoveredItemIndex = -1;
		return true;
	}

	public SKRect GetDropDownRect()
	{
		float h = MathF.Min(DropDownMaxHeight, _items.Count * ItemHeight);

		return new SKRect(
			AbsoluteBounds.Left,
			AbsoluteBounds.Bottom + 2f,
			AbsoluteBounds.Right,
			AbsoluteBounds.Bottom + 2f + h);
	}

	/// <summary>
	/// Прямоугольник элемента с учётом прокрутки. Элементы вне видимой части
	/// получают прямоугольник за границами списка, поэтому не рисуются и не
	/// принимают клики.
	/// </summary>
	private SKRect GetItemRect(int index)
	{
		var dr = GetDropDownRect();

		int row = index - _scrollIndex;

		return new SKRect(
			dr.Left + 1f,
			dr.Top + row * ItemHeight + 1f,
			dr.Right - 1f,
			dr.Top + (row + 1) * ItemHeight);
	}

	private bool IsItemVisible(int index)
		=> index >= _scrollIndex && index < _scrollIndex + VisibleItemCount;

	/// <summary>
	/// Шрифт для отрисовки элемента. Кэшируется в SystemFontCatalog, поэтому
	/// вызывать можно на каждый кадр. Возвращаемый шрифт освобождать НЕЛЬЗЯ —
	/// он общий.
	/// </summary>
	private static SKFont ResolveItemFont(SkDropDownItem? item, SKFont fallback, float size)
	{
		if (item == null || string.IsNullOrWhiteSpace(item.FontFamily))
			return fallback;

		// Символьный шрифт своё название не покажет — рисуем шрифтом интерфейса.
		if (!SystemFontCatalog.CanRenderOwnName(item.FontFamily))
			return fallback;

		return SystemFontCatalog.GetFont(item.FontFamily, size) ?? fallback;
	}


	private SkDropDownItem? GetSelectedItem()
	{
		if (SelectedIndex < 0 || SelectedIndex >= _items.Count)
			return null;

		return _items[SelectedIndex];
	}

	private static void DrawPreviewBitmap(SKCanvas canvas, SKBitmap bitmap, SKRect rect)
	{
		using var back1 = new SKPaint
		{
			Color = new SKColor(235, 235, 235),
			Style = SKPaintStyle.Fill,
			IsAntialias = false
		};

		using var back2 = new SKPaint
		{
			Color = new SKColor(210, 210, 210),
			Style = SKPaintStyle.Fill,
			IsAntialias = false
		};

		using var border = new SKPaint
		{
			Color = new SKColor(120, 120, 120),
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			IsAntialias = false
		};

		const float cell = 4f;

		for (float y = rect.Top; y < rect.Bottom; y += cell)
		{
			for (float x = rect.Left; x < rect.Right; x += cell)
			{
				bool even =
					((((int)((x - rect.Left) / cell)) + ((int)((y - rect.Top) / cell))) & 1) == 0;

				canvas.DrawRect(
					new SKRect(
						x,
						y,
						MathF.Min(x + cell, rect.Right),
						MathF.Min(y + cell, rect.Bottom)),
					even ? back1 : back2);
			}
		}

		var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
		canvas.DrawBitmap(bitmap, rect, sampling);
		canvas.DrawRect(rect, border);
	}
}

public static class ComboPreviewBinder
{
	public static void BindPatternCombo(ComboBox combo)
	{
		combo.DrawMode = DrawMode.OwnerDrawFixed;
		combo.DropDownStyle = ComboBoxStyle.DropDownList;
		combo.ItemHeight = 28;

		combo.DrawItem -= PatternCombo_DrawItem;
		combo.DrawItem += PatternCombo_DrawItem;
	}

	public static void BindTextureCombo(ComboBox combo)
	{
		combo.DrawMode = DrawMode.OwnerDrawFixed;
		combo.DropDownStyle = ComboBoxStyle.DropDownList;
		combo.ItemHeight = 40;

		combo.DrawItem -= TextureCombo_DrawItem;
		combo.DrawItem += TextureCombo_DrawItem;
	}

	private static void PatternCombo_DrawItem(object? sender, DrawItemEventArgs e)
	{
		if (sender is not ComboBox combo) return;
		e.DrawBackground();

		if (e.Index < 0 || e.Index >= combo.Items.Count)
			return;

		if (combo.Items[e.Index] is not PatternInfo item)
			return;

		var g = e.Graphics;
		var bounds = e.Bounds;

		g.FillRectangle(SystemBrushes.Window, bounds);

		Rectangle previewRect = new Rectangle(bounds.Left + 4, bounds.Top + 4, bounds.Width - 8, bounds.Height - 8);

		if (item.Bitmap != null)
		{
			using var bmp = ToDrawingBitmap(item.Bitmap);
			using var texBrush = new TextureBrush(bmp, System.Drawing.Drawing2D.WrapMode.Tile);
			g.FillRectangle(texBrush, previewRect);
			g.DrawRectangle(Pens.Gray, previewRect);
		}

		if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
			g.DrawRectangle(Pens.DarkOrange, bounds.Left + 1, bounds.Top + 1, bounds.Width - 3, bounds.Height - 3);

		e.DrawFocusRectangle();
	}

	private static void TextureCombo_DrawItem(object? sender, DrawItemEventArgs e)
	{
		if (sender is not ComboBox combo) return;
		e.DrawBackground();

		if (e.Index < 0 || e.Index >= combo.Items.Count)
			return;

		if (combo.Items[e.Index] is not TextureInfo item)
			return;

		var g = e.Graphics;
		var bounds = e.Bounds;

		g.FillRectangle(SystemBrushes.Window, bounds);

		Rectangle previewRect = new Rectangle(bounds.Left + 4, bounds.Top + 4, bounds.Width - 8, bounds.Height - 8);

		if (item.Bitmap != null)
		{
			using var bmp = ToDrawingBitmap(item.Bitmap);
			g.DrawImage(bmp, previewRect);
			g.DrawRectangle(Pens.Gray, previewRect);
		}

		if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
			g.DrawRectangle(Pens.DarkOrange, bounds.Left + 1, bounds.Top + 1, bounds.Width - 3, bounds.Height - 3);

		e.DrawFocusRectangle();
	}

	private static Bitmap ToDrawingBitmap(SKBitmap skBitmap)
	{
		using var image = SKImage.FromBitmap(skBitmap);
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		using var ms = new MemoryStream(data.ToArray());
		return new Bitmap(ms);
	}
}

public sealed class SkCheckBox : SkControl
{
	public string Text { get; set; } = string.Empty;
	public bool Checked { get; set; }

	public event Action<bool>? CheckedChanged;

	public SkCheckBox()
	{
		Bounds = new SKRect(0, 0, 160, 28);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		var box = new SKRect(ab.Left, ab.Top + 5, ab.Left + 18, ab.Top + 23);

		using var fill = SkUiTheme.Fill(pal.PanelBack);
		using var border = SkUiTheme.Stroke(pal.Border);
		canvas.DrawRoundRect(box, 4, 4, fill);
		canvas.DrawRoundRect(box, 4, 4, border);

		if (Checked)
		{
			using var mark = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 2f,
				Color = pal.Accent
			};

			canvas.DrawLine(box.Left + 4, box.MidY, box.Left + 8, box.Bottom - 4, mark);
			canvas.DrawLine(box.Left + 8, box.Bottom - 4, box.Right - 4, box.Top + 4, mark);
		}

		SkTextHelper.DrawLeft(canvas, Text, ab.Left + 26, ab.Top + 18, 12.5f, pal.Text);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Checked = !Checked;
		CheckedChanged?.Invoke(Checked);
		return true;
	}
}

public sealed class SkSlideBar : SkControl
{
	public float Value { get; private set; }
	public float Minimum { get; set; } = 0f;
	public float Maximum { get; set; } = 100f;
	public float SmallChange { get; set; } = 1f;

	public bool IntegerMode { get; set; } = false;
	public int Decimals { get; set; } = 2;

	public bool SnapToStep { get; set; } = false;
	public float Step { get; set; } = 1f;

	public bool ShowValueLabel { get; set; } = false;
	public string? PrefixText { get; set; }
	public string? SuffixText { get; set; }

	public Action<float>? ValueChanged { get; set; }

	public SKColor TrackColor { get; set; } = new SKColor(85, 85, 85);
	public SKColor FillColor { get; set; } = new SKColor(70, 135, 255);
	public SKColor ThumbColor { get; set; } = SKColors.White;
	public SKColor BorderColor { get; set; } = new SKColor(30, 30, 30);
	public SKColor DisabledColor { get; set; } = new SKColor(130, 130, 130);
	public SKColor TextColor { get; set; } = SKColors.White;

	public float TrackHeight { get; set; } = 4f;
	public float ThumbRadius { get; set; } = 8f;

	public bool IsDragging { get; private set; }

	public int IntValue => (int)MathF.Round(Value);

	public SkSlideBar()
	{
		Bounds = new SKRect(0, 0, 140, 24);
		Name = "SkSlideBar";
	}

	public void SetValue(float value, bool raiseChanged = true)
	{
		float v = NormalizeValue(value);

		if (MathF.Abs(v - Value) < 0.0001f)
			return;

		Value = v;

		if (raiseChanged)
			ValueChanged?.Invoke(Value);
	}

	private float NormalizeValue(float value)
	{
		float min = Math.Min(Minimum, Maximum);
		float max = Math.Max(Minimum, Maximum);

		value = Math.Clamp(value, min, max);

		if (SnapToStep && Step > 0.0001f)
			value = min + MathF.Round((value - min) / Step) * Step;

		if (IntegerMode)
			value = MathF.Round(value);

		return Math.Clamp(value, min, max);
	}

	private float GetNormalizedT()
	{
		float range = Maximum - Minimum;
		if (MathF.Abs(range) < 0.0001f)
			return 0f;

		return Math.Clamp((Value - Minimum) / range, 0f, 1f);
	}

	private float ValueFromPoint(SKPoint p)
	{
		SKRect track = GetTrackRectAbsolute();
		if (track.Width <= 0.0001f)
			return Minimum;

		float t = (p.X - track.Left) / track.Width;
		t = Math.Clamp(t, 0f, 1f);
		return Minimum + (Maximum - Minimum) * t;
	}

	private SKRect GetTrackRectLocal()
	{
		float left = ThumbRadius;
		float right = Bounds.Width - ThumbRadius;
		float cy = Bounds.Height * 0.5f;
		float top = cy - TrackHeight * 0.5f;
		float bottom = cy + TrackHeight * 0.5f;

		return new SKRect(left, top, right, bottom);
	}

	private SKRect GetTrackRectAbsolute()
	{
		SKRect local = GetTrackRectLocal();
		return new SKRect(
			AbsoluteX + local.Left,
			AbsoluteY + local.Top,
			AbsoluteX + local.Right,
			AbsoluteY + local.Bottom);
	}

	private SKPoint GetThumbCenterLocal()
	{
		SKRect track = GetTrackRectLocal();
		float t = GetNormalizedT();
		return new SKPoint(track.Left + track.Width * t, track.MidY);
	}

	private bool HitThumb(SKPoint p)
	{
		var center = GetThumbCenterLocal();
		var abs = new SKPoint(AbsoluteX + center.X, AbsoluteY + center.Y);

		float dx = p.X - abs.X;
		float dy = p.Y - abs.Y;
		float r = ThumbRadius + 3f;
		return dx * dx + dy * dy <= r * r;
	}

	public override SkControl? FindTopMost(SKPoint p)
	{
		if (!Visible || !Enabled || !AbsoluteBounds.Contains(p))
			return null;

		return this;
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!Enabled || !Visible || !HitTest(e.Location) || e.Button != MouseButtons.Left)
			return false;

		Focused = true;
		IsDragging = true;

		SetValue(ValueFromPoint(e.Location), true);
		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		if (!Enabled || !Visible)
			return false;

		Hovered = HitTest(e.Location) || HitThumb(e.Location);

		if (!IsDragging)
			return Hovered;

		SetValue(ValueFromPoint(e.Location), true);
		return true;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		if (!Enabled || !Visible)
			return false;

		bool wasDragging = IsDragging;
		IsDragging = false;
		return wasDragging;
	}

	public override bool OnKeyDown(SkKeyEvent e)
	{
		if (!Enabled || !Visible || !Focused)
			return false;

		switch (e.KeyCode)
		{
			case Keys.Left:
			case Keys.Down:
				SetValue(Value - SmallChange, true);
				return true;

			case Keys.Right:
			case Keys.Up:
				SetValue(Value + SmallChange, true);
				return true;

			case Keys.Home:
				SetValue(Minimum, true);
				return true;

			case Keys.End:
				SetValue(Maximum, true);
				return true;
		}

		return false;
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		var ab = AbsoluteBounds;
		float cy = ab.MidY;

		SKRect track = new SKRect(
			ab.Left + ThumbRadius,
			cy - TrackHeight * 0.5f,
			ab.Right - ThumbRadius,
			cy + TrackHeight * 0.5f);

		float t = GetNormalizedT();

		SKRect fill = new SKRect(
			track.Left,
			track.Top,
			track.Left + track.Width * t,
			track.Bottom);

		SKPoint thumb = new SKPoint(
			track.Left + track.Width * t,
			cy);

		SKColor trackColor = Enabled ? TrackColor : DisabledColor;
		SKColor fillColor = Enabled ? FillColor : DisabledColor;
		SKColor thumbColor = Enabled ? ThumbColor : DisabledColor;

		using var trackPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = trackColor
		};

		using var fillPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = fillColor
		};

		using var thumbPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = thumbColor
		};

		using var thumbStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = BorderColor
		};

		canvas.DrawRoundRect(track, TrackHeight * 0.5f, TrackHeight * 0.5f, trackPaint);

		if (fill.Width > 0.5f)
			canvas.DrawRoundRect(fill, TrackHeight * 0.5f, TrackHeight * 0.5f, fillPaint);

		float thumbR = ThumbRadius + (IsDragging ? 1f : Hovered ? 0.5f : 0f);
		canvas.DrawCircle(thumb, thumbR, thumbPaint);
		canvas.DrawCircle(thumb, thumbR, thumbStroke);

		if (Focused)
		{
			using var focusPaint = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = 1f,
				Color = new SKColor(255, 255, 255, 110)
			};

			var focusRect = new SKRoundRect(
				new SKRect(ab.Left, ab.Top, ab.Right, ab.Bottom),
				6f, 6f);

			canvas.DrawRoundRect(focusRect, focusPaint);
		}

		if (ShowValueLabel)
		{
			string valueText = IntegerMode
				? IntValue.ToString()
				: Value.ToString($"F{Decimals}", System.Globalization.CultureInfo.InvariantCulture);

			if (!string.IsNullOrEmpty(PrefixText))
				valueText = PrefixText + valueText;

			if (!string.IsNullOrEmpty(SuffixText))
				valueText += SuffixText;

			using var textPaint = new SKPaint
			{
				IsAntialias = true,
				Color = TextColor
			};

			using var textFont = new SKFont
			{
				Size = 12f
			};

			float tw = textFont.MeasureText(valueText, textPaint);
			float x = ab.Right - tw;
			float y = ab.Top - 4f + textFont.Size;

			canvas.DrawText(valueText, new SKPoint(x, y), SKTextAlign.Left, textFont, textPaint);
		}
	}
}

public sealed class SkPictureButton : SkControl
{
	private bool _pressed;

	public SKBitmap? Image { get; set; }
	public string ToolTipText { get; set; } = string.Empty;
	public bool Selected { get; set; }
	public Action<SkPictureButton>? Click { get; set; }

	public SkPictureButton()
	{
		Bounds = new SKRect(0, 0, 36, 36);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var r = AbsoluteBounds;

		SKColor back =
			Selected ? pal.Accent.WithAlpha(48) :
			_pressed ? pal.PanelBack2 :
			Hovered ? pal.Surface :
			pal.PanelBack;

		SKColor borderColor =
			Selected ? pal.Accent :
			Focused ? pal.Accent :
			Hovered ? pal.Text.WithAlpha(96) :
			pal.Border;

		float borderWidth = Selected || Focused ? 1.5f : 1f;

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = back
		};

		using var border = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = borderWidth,
			Color = borderColor
		};

		canvas.DrawRoundRect(r, 6, 6, fill);
		canvas.DrawRoundRect(r, 6, 6, border);

		if (Image != null)
		{
			float pad = 2f;
			var imgRect = new SKRect(
				r.Left + pad,
				r.Top + pad,
				r.Right - pad,
				r.Bottom - pad);

			canvas.DrawBitmap(Image, imgRect);
		}
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Focused = true;
		_pressed = true;
		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool changed = false;

		bool oldHovered = Hovered;
		Hovered = HitTest(e.Location);
		if (oldHovered != Hovered)
			changed = true;

		return changed;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		bool wasPressed = _pressed;
		_pressed = false;

		if (wasPressed && HitTest(e.Location))
		{
			Click?.Invoke(this);
			return true;
		}

		return wasPressed;
	}
}

public sealed class SkColorButton : SkControl
{
	private static SKBitmap? _checkerBitmap;

	public string Text { get; set; } = string.Empty; // совместимость со старым кодом
	public SKColor Color { get; set; } = SKColors.White;

	public Action<SkColorButton>? Click { get; set; }
	public event EventHandler<SKColor>? ColorChanged;

	public SkColorButton()
	{
		Bounds = new SKRect(0, 0, 42, 24);
		EnsureCheckerBitmap();
	}

	public void SetColor(SKColor color)
	{
		Color = color;
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible) return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		DrawCheckerBackground(canvas, ab);

		using var fill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Color
		};

		using var border = SkUiTheme.Stroke(
			Focused ? pal.Accent : pal.Border,
			Focused ? 1.5f : 1f);

		canvas.DrawRoundRect(ab, 6, 6, fill);
		canvas.DrawRoundRect(ab, 6, 6, border);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Focused = true;
		ShowColorDialog();
		Click?.Invoke(this);
		return true;
	}

	private void ShowColorDialog()
	{
		using var dlg = new SColorDialog();
		dlg.SetColorV4(new SKColorF(
			Color.Red / 255f,
			Color.Green / 255f,
			Color.Blue / 255f,
			Color.Alpha / 255f));

		if (dlg.ShowDialog() == DialogResult.OK)
		{
			var c = dlg.GetColor();
			Color = new SKColor(c.R, c.G, c.B, c.A);
			ColorChanged?.Invoke(this, Color);
		}
	}

	private static void EnsureCheckerBitmap()
	{
		if (_checkerBitmap != null) return;

		string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "transparentbg_mini-2.jpg");
		if (File.Exists(file))
			_checkerBitmap = SKBitmap.Decode(file);
	}

	private static void DrawCheckerBackground(SKCanvas canvas, SKRect rect)
	{
		if (_checkerBitmap == null)
		{
			using var light = new SKPaint { Color = new SKColor(220, 220, 220), IsAntialias = false };
			using var dark = new SKPaint { Color = new SKColor(180, 180, 180), IsAntialias = false };

			const float cell = 6f;
			for (float y = rect.Top; y < rect.Bottom; y += cell)
			{
				for (float x = rect.Left; x < rect.Right; x += cell)
				{
					bool alt = ((((int)((x - rect.Left) / cell)) + ((int)((y - rect.Top) / cell))) % 2) == 0;
					canvas.DrawRect(
						new SKRect(x, y, MathF.Min(x + cell, rect.Right), MathF.Min(y + cell, rect.Bottom)),
						alt ? light : dark);
				}
			}
			return;
		}

		using var image = SKImage.FromBitmap(_checkerBitmap);
		using var paint = new SKPaint
		{
			IsAntialias = false,
			Shader = SKShader.CreateImage(
				image,
				SKShaderTileMode.Repeat,
				SKShaderTileMode.Repeat)
		};

		canvas.DrawRoundRect(rect, 6, 6, paint);
	}
}

public sealed class SkLabel : SkControl
{
	public string Text { get; set; } = string.Empty;
	public float FontSize { get; set; } = 13f;
	public SKColor? ForeColor { get; set; }
	public bool Bold { get; set; }

	public SkLabel()
	{
		Bounds = new SKRect(0, 0, 120, 22);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;
		float baseline = ab.Top + FontSize + 4f;
		SkTextHelper.DrawLeft(canvas, Text, ab.Left, baseline, FontSize, ForeColor ?? pal.TextMuted, Bold);
	}

	public override bool HitTest(SKPoint p) => false;
}

public enum SkTextAlign
{
	Left,
	Center,
	Right
}

// Обычная кнопка в виде скруглённого прямоугольника с текстом посередине.
// Checked = true отображает кнопку как "нажатую" (вдавленную/подсвеченную).
public sealed class SkButton : SkControl
{
	public string Text { get; set; } = string.Empty;
	public float FontSize { get; set; } = 13f;
	public float CornerRadius { get; set; } = 6f;
	public bool Checked { get; set; } = false;

	/// <summary>
	/// Переключается ли кнопка при щелчке.
	///
	/// По умолчанию да — на этом держатся Ж, К, Ч в панели текста и другие
	/// кнопки-выключатели. Но кнопке-КОМАНДЕ такое поведение вредит:
	/// она остаётся подсвеченной после нажатия и читается как включённый
	/// режим. Стрелки счётчика именно такие — их щелчок меняет число
	/// и на этом заканчивается.
	/// </summary>
	public bool Toggles { get; set; } = true;

	/// <summary>
	/// Рисовать ли рамку фокуса. Кнопка, которая не становится активным
	/// контролом, не должна и выглядеть таковой.
	/// </summary>
	public bool ShowFocus { get; set; } = true;

	/// <summary>Полужирное начертание. Учитывается только когда Font не задан.</summary>
	public bool Bold { get; set; } = false;

	/// <summary>
	/// Шрифт надписи. Если задан, FontSize и Bold игнорируются: размер и
	/// начертание берутся из самого шрифта.
	///
	/// Кнопка НЕ освобождает этот объект. Передавайте шрифт из кэша —
	/// FontResources.GetFont для файлов из Resources либо
	/// SystemFontCatalog.GetFont для системных семейств. Создавать SKFont
	/// на каждую кнопку не нужно: одна гарнитура обслуживает их все.
	/// </summary>
	public SKFont? Font { get; set; }

	// Совместимо со стилем остальных контролов (Action-делегаты).
	public Action? Click { get; set; }
	public event Action<bool>? CheckedChanged;

	private bool _pressed;

	public SkButton()
	{
		Bounds = new SKRect(0, 0, 90, 28);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		var pal = SkUiTheme.Current;
		var r = AbsoluteBounds;

		bool isDown = Checked || _pressed;

		var fillColor = !Enabled
			? pal.PanelBack
			: isDown
				? pal.ButtonPressed
				: Hovered
					? pal.PanelBack2
					: pal.PanelBack;

		bool focusRing = Focused && ShowFocus;

		var borderColor = isDown || focusRing ? pal.Accent : pal.Border;
		float borderWidth = isDown || focusRing ? 1.5f : 1f;

		using var fill = SkUiTheme.Fill(fillColor);
		using var border = SkUiTheme.Stroke(borderColor, borderWidth);

		// Небольшое «вдавливание»: содержимое смещается на пиксель вниз.
		var drawRect = isDown
			? new SKRect(r.Left, r.Top + 1f, r.Right, r.Bottom + 1f)
			: r;

		canvas.DrawRoundRect(drawRect, CornerRadius, CornerRadius, fill);
		canvas.DrawRoundRect(drawRect, CornerRadius, CornerRadius, border);

		if (string.IsNullOrEmpty(Text))
			return;

		var textColor = isDown ? pal.Accent : (Enabled ? pal.Text : pal.TextMuted);

		// Надпись обрезается по самой кнопке.
		//
		// Отсечка идёт по прямоугольнику, вжатому внутрь на толщину рамки
		// с небольшим запасом: иначе длинный текст ложился бы поверх
		// собственной обводки и кнопка выглядела бы продавленной сбоку.
		// Скругление отсечки уменьшается на ту же величину, чтобы её угол
		// шёл вдоль угла рамки, а не срезал его.
		float inset = borderWidth + 2f;

		var clipRect = new SKRect(
			drawRect.Left + inset,
			drawRect.Top + inset,
			drawRect.Right - inset,
			drawRect.Bottom - inset);

		// Кнопка уже рамки с запасом — рисовать текст негде.
		if (clipRect.Width <= 0f || clipRect.Height <= 0f)
			return;

		float clipRadius = MathF.Max(0f, CornerRadius - inset);

		canvas.Save();

		try
		{
			canvas.ClipRoundRect(
				new SKRoundRect(clipRect, clipRadius, clipRadius),
				SKClipOperation.Intersect,
				antialias: true);

			// Центрирование считается по ПОЛНОМУ прямоугольнику кнопки,
			// а не по обрезанному: иначе текст, который помещается,
			// заметно сдвигался бы при изменении толщины рамки.
			if (Font != null)
			{
				// Заданный шрифт: базовая линия считается из его метрик.
				SkTextHelper.DrawCenteredInRect(canvas, Text, drawRect, Font, textColor);
			}
			else
			{
				// Шрифт по умолчанию. Создаётся на время отрисовки, поэтому
				// освобождается здесь же.
				using var defaultFont = SkTextHelper.CreateFont(FontSize, Bold);
				SkTextHelper.DrawCenteredInRect(canvas, Text, drawRect, defaultFont, textColor);
			}
		}
		finally
		{
			// Восстановление в finally: без него ранний выход из ветки
			// оставил бы отсечку включённой, и всё, что рисуется после
			// кнопки, обрезалось бы по ней.
			canvas.Restore();
		}
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		_pressed = true;
		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool oldHovered = Hovered;
		Hovered = HitTest(e.Location);
		return oldHovered != Hovered;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		bool wasPressed = _pressed;
		_pressed = false;

		if (!wasPressed)
			return false;

		if (HitTest(e.Location))
		{
			if (Toggles)
			{
				Checked = !Checked;
				CheckedChanged?.Invoke(Checked);
			}

			Click?.Invoke();
		}

		return true;
	}
}


/// <summary>
/// Многострочное поле ввода.
///
/// Добавлено: вертикальная полоса прокрутки, прокрутка колесом, ручка изменения
/// высоты в правом нижнем углу и отображение табуляции.
///
/// Табуляция рисуется как один пробел шириной в четыре пробела текущего шрифта,
/// а не заменяется четырьмя пробелами: символ в тексте остаётся одним, поэтому
/// каретка перескакивает его целиком, а копирование в буфер возвращает именно
/// табуляцию. Прежде '\t' уходил в DrawText как обычный символ и шрифт рисовал
/// на его месте .notdef — тот самый квадратик.
/// </summary>
public class SkTextBox : SkControl
{
	#region Константы

	private const float PaddingX = 10f;
	private const float PaddingY = 4f;

	private const float ScrollBarWidth = 9f;
	private const float ScrollBarMinThumb = 18f;

	private const float ResizeGripSize = 12f;
	private const float MinBoxHeight = 30f;
	private const float MaxBoxHeight = 600f;

	/// <summary>Ширина табуляции в пробелах текущего шрифта.</summary>
	private const int TabSpaces = 4;

	#endregion

	#region Текст и состояние

	private string _text = string.Empty;

	public string Text
	{
		get => _text;
		set
		{
			string v = value ?? string.Empty;

			if (!Multiline)
				v = v.Replace("\r", "").Replace("\n", " ");

			if (_text == v)
				return;

			_text = v;

			// Разбиение на строки и позиция каретки должны пережить внешнее
			// присваивание: панель свойств делает это при откате истории.
			RebuildLines();
			CaretIndex = Math.Min(_caretIndex, _text.Length);
			ClampSelection();
			_scrollToCaret = true;
		}
	}

	public string Placeholder { get; set; } = string.Empty;

	private bool _multiline;

	public bool Multiline
	{
		get => _multiline;
		set
		{
			if (_multiline == value)
				return;

			_multiline = value;

			if (!_multiline)
				_text = _text.Replace("\r", "").Replace("\n", " ");

			RebuildLines();
		}
	}

	private bool _wordWrap = true;

	/// <summary>
	/// Переносить длинные строки по словам.
	///
	/// Осмысленно только вместе с Multiline: у однострочного поля перенос
	/// некуда делать, там длинный текст доступен горизонтальной прокруткой.
	///
	/// Включено по умолчанию. Без переноса конец длинной строки уезжает
	/// за правый край поля, и добраться до него можно только каретой —
	/// прочитать текст целиком нельзя вообще.
	/// </summary>
	public bool WordWrap
	{
		get => _wordWrap;
		set
		{
			if (_wordWrap == value)
				return;

			_wordWrap = value;
			RebuildLines();
		}
	}

	/// <summary>Действует ли перенос: он требует и режима, и разрешения.</summary>
	private bool WrapEnabled => Multiline && _wordWrap;

	public Action<string>? TextChanged;
	public Action<string>? EditingFinished;

	/// <summary>Поднимается после изменения высоты ручкой в углу.</summary>
	public Action? SizeChanged;

	protected virtual void RaiseTextChanged() => TextChanged?.Invoke(Text);
	protected virtual void RaiseEditingFinished() => EditingFinished?.Invoke(Text);

	/// <summary>
	/// Завершает правку: набранное применяется так же, как по Enter
	/// или при уходе фокуса.
	///
	/// Был только у числового поля, из-за чего имя фигуры при переходе
	/// по табуляции не применялось вовсе: панель умела завершить правку
	/// числа и не умела — строки.
	/// </summary>
	public void FinishEditing()
	{
		RaiseEditingFinished();
	}

	private int _caretIndex = 0;
	private int _selectionStart = 0;
	private int _selectionLength = 0;
	private bool _mouseSelecting = false;

	private float _fontSize = 13f;

	/// <summary>Начала строк: индексы в Text. Всегда хотя бы один элемент.</summary>
	private readonly List<int> _lineStarts = new() { 0 };

	private int _scrollLine = 0;
	private float _scrollX = 0f;

	/// <summary>
	/// Каретку нужно показать. Флаг, а не безусловная прокрутка на каждый кадр:
	/// иначе прокрутка колесом мгновенно отматывалась бы обратно к каретке.
	/// </summary>
	private bool _scrollToCaret = true;

	private bool _scrollDragging;
	private float _scrollDragOffset;

	private bool _resizingBox;
	private float _resizeStartMouseY;
	private float _resizeStartHeight;

	public int CaretIndex
	{
		get => _caretIndex;
		set => _caretIndex = Math.Clamp(value, 0, Text.Length);
	}

	/// <summary>
	/// Выделяет весь текст и ставит каретку в конец.
	///
	/// Вынесено из обработки Ctrl+A, потому что тем же нужно пользоваться
	/// снаружи: при переходе по табуляции поле должно быть готово к набору
	/// нового значения, а не к правке прежнего.
	/// </summary>
	public void SelectAll()
	{
		_selectionStart = 0;
		_selectionLength = _text.Length;
		CaretIndex = _text.Length;
		_scrollToCaret = true;
	}

	public bool HasSelection => _selectionLength != 0;
	public int SelectionStart => _selectionLength >= 0 ? _selectionStart : _selectionStart + _selectionLength;
	public int SelectionEnd => _selectionLength >= 0 ? _selectionStart + _selectionLength : _selectionStart;
	public int SelectionLengthAbs => Math.Abs(_selectionLength);

	public SkTextBox()
	{
		Bounds = new SKRect(0, 0, 160, 30);
	}

	#endregion

	#region Метрики и табуляция

	private float LineHeight => SkTextHelper.GetMetrics(_fontSize).LineHeight;
	private float Ascent => SkTextHelper.GetMetrics(_fontSize).Ascent;

	private float _cachedSpaceWidth = -1f;
	private float _cachedSpaceWidthFor = -1f;

	/// <summary>
	/// Ширина табуляции. Кэшируется: SkTextHelper.Measure создаёт SKTypeface и
	/// SKFont на каждый вызов, а ширина нужна при каждом измерении строки.
	/// </summary>
	private float TabWidth
	{
		get
		{
			if (_cachedSpaceWidthFor != _fontSize)
			{
				_cachedSpaceWidth = SkTextHelper.Measure(" ", _fontSize);
				_cachedSpaceWidthFor = _fontSize;
			}

			return _cachedSpaceWidth * TabSpaces;
		}
	}

	/// <summary>
	/// Ширина участка текста с учётом табуляций. Текст разбивается на отрезки
	/// по символам '\t', отрезки измеряются шрифтом, табуляции добавляются
	/// фиксированной шириной.
	///
	/// Отрисовка использует то же разбиение, поэтому позиция каретки всегда
	/// совпадает с тем, что нарисовано. Кернинг через границу отрезка теряется,
	/// но он теряется одинаково при измерении и при рисовании.
	/// </summary>
	private float MeasureSpan(string text, int start, int count)
	{
		start = Math.Clamp(start, 0, text.Length);
		int end = Math.Clamp(start + count, start, text.Length);

		float width = 0f;
		int segmentStart = start;

		for (int i = start; i < end; i++)
		{
			if (text[i] != '\t')
				continue;

			if (i > segmentStart)
				width += SkTextHelper.Measure(text[segmentStart..i], _fontSize);

			width += TabWidth;
			segmentStart = i + 1;
		}

		if (end > segmentStart)
			width += SkTextHelper.Measure(text[segmentStart..end], _fontSize);

		return width;
	}

	private float MeasureSubstring(string text, int length) => MeasureSpan(text, 0, length);

	/// <summary>
	/// Рисует текст, пропуская табуляции: на их месте оставляется пустое место
	/// нужной ширины. Без этого шрифт рисует на месте '\t' символ .notdef.
	/// </summary>
	private void DrawTextWithTabs(SKCanvas canvas, string text, float x, float baseline, SKColor color)
	{
		if (string.IsNullOrEmpty(text))
			return;

		if (text.IndexOf('\t') < 0)
		{
			SkTextHelper.DrawLeft(canvas, text, x, baseline, _fontSize, color);
			return;
		}

		int segmentStart = 0;
		float cursor = x;

		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != '\t')
				continue;

			if (i > segmentStart)
			{
				string segment = text[segmentStart..i];
				SkTextHelper.DrawLeft(canvas, segment, cursor, baseline, _fontSize, color);
				cursor += SkTextHelper.Measure(segment, _fontSize);
			}

			cursor += TabWidth;
			segmentStart = i + 1;
		}

		if (segmentStart < text.Length)
			SkTextHelper.DrawLeft(canvas, text[segmentStart..], cursor, baseline, _fontSize, color);
	}

	#endregion

	#region Раскладка строк

	private int LineCount => _lineStarts.Count;

	/// <summary>
	/// Заканчивается ли строка настоящим символом перевода.
	///
	/// Нужен потому, что у строки, разорванной переносом, разделяющего
	/// символа нет. Без этого различия LineRange отрезал бы от каждой
	/// перенесённой строки последнюю букву — ту, которую он вычитает
	/// как '\n'.
	/// </summary>
	private readonly List<bool> _lineHardBreak = new() { false };

	/// <summary>
	/// Ширина, на которую разложен текст. По ней видно, что поле
	/// изменило размер и раскладку надо считать заново.
	/// </summary>
	private float _wrappedForWidth = -1f;

	/// <summary>
	/// Ширина области, в которую укладывается текст при переносе.
	///
	/// Полоса прокрутки вычитается ВСЕГДА, а не только когда она видна.
	/// Иначе получается круг: перенос рождает лишние строки, от них
	/// появляется полоса, она сужает поле, от этого строк становится
	/// ещё больше. Постоянный запас в девять пикселей дешевле такой
	/// неустойчивости.
	/// </summary>
	private float WrapWidth
		=> MathF.Max(1f, Bounds.Width - PaddingX * 2f - ScrollBarWidth);

	private void RebuildLines()
	{
		_lineStarts.Clear();
		_lineHardBreak.Clear();

		_lineStarts.Add(0);
		_lineHardBreak.Add(false);

		if (!Multiline)
			return;

		_wrappedForWidth = Bounds.Width;

		float limit = WrapWidth;

		int lineStart = 0;

		for (int i = 0; i < _text.Length; i++)
		{
			if (_text[i] != '\n')
				continue;

			if (WrapEnabled)
				WrapSegment(lineStart, i, limit);

			_lineStarts.Add(i + 1);
			_lineHardBreak.Add(false);

			// Признак ставится ПРЕДЫДУЩЕЙ строке: это она оканчивается
			// на найденный символ перевода.
			_lineHardBreak[^2] = true;

			lineStart = i + 1;
		}

		if (WrapEnabled)
			WrapSegment(lineStart, _text.Length, limit);
	}

	/// <summary>
	/// Разбивает один абзац на строки по ширине.
	///
	/// Начало абзаца уже лежит в списке, поэтому добавляются только точки
	/// разрыва внутри него. Разрыв ищется по последнему пробелу, а если
	/// слово в поле не помещается целиком — рвётся само слово: длинный
	/// адрес или число иначе остались бы недоступными, а ради них перенос
	/// и делался.
	/// </summary>
	private void WrapSegment(int start, int end, float limit)
	{
		int lineStart = start;
		int pos = start;

		float width = 0f;

		while (pos < end)
		{
			// Кусок — это слово вместе с идущими за ним пробелами. Измеряем
			// кусками, а не буквами: измерение строит шрифт на каждый вызов,
			// и посимвольный проход по абзацу обошёлся бы в сотни таких
			// построений на каждую перекладку.
			int wordEnd = pos;

			while (wordEnd < end && _text[wordEnd] != ' ' && _text[wordEnd] != '\t')
				wordEnd++;

			int chunkEnd = wordEnd;

			while (chunkEnd < end && (_text[chunkEnd] == ' ' || _text[chunkEnd] == '\t'))
				chunkEnd++;

			float chunkWidth = MeasureSpan(_text, pos, chunkEnd - pos);

			if (width > 0f && width + chunkWidth > limit)
			{
				_lineStarts.Add(pos);
				_lineHardBreak.Add(false);

				lineStart = pos;
				width = 0f;
			}

			// Слово шире поля целиком — рвём его по буквам. Иначе длинный
			// адрес или число уехали бы за край, а ради них перенос
			// и затевался.
			if (chunkWidth > limit)
			{
				int i = pos;

				while (i < chunkEnd)
				{
					float charWidth = MeasureSpan(_text, i, 1);

					if (width > 0f && width + charWidth > limit)
					{
						_lineStarts.Add(i);
						_lineHardBreak.Add(false);

						lineStart = i;
						width = 0f;
					}

					width += charWidth;
					i++;
				}

				pos = chunkEnd;
				continue;
			}

			width += chunkWidth;
			pos = chunkEnd;
		}
	}

	/// <summary>
	/// Перекладывает текст, когда поле изменило ширину.
	///
	/// Раскладка зависит от ширины, а ширину контрол получает уже после
	/// того, как в него положили текст. Без этой проверки поле оставалось бы
	/// разложенным по ширине, которая была у него в момент присваивания —
	/// то есть по случайной.
	/// </summary>
	public override void Arrange()
	{
		base.Arrange();

		if (!Multiline)
			return;

		if (MathF.Abs(Bounds.Width - _wrappedForWidth) < 0.5f)
			return;

		RebuildLines();

		// Строк стало больше или меньше — прежняя прокрутка может указывать
		// за конец текста.
		_scrollLine = Math.Clamp(_scrollLine, 0, MaxScrollLine);
		_scrollToCaret = true;
	}

	private int LineOfIndex(int index)
	{
		index = Math.Clamp(index, 0, _text.Length);

		for (int line = LineCount - 1; line >= 0; line--)
		{
			if (index >= _lineStarts[line])
				return line;
		}

		return 0;
	}

	private (int Start, int Length) LineRange(int line)
	{
		line = Math.Clamp(line, 0, LineCount - 1);

		int start = _lineStarts[line];

		// У жёсткого разрыва вычитается сам символ '\n', у мягкого
		// вычитать нечего — там разделяющего символа нет.
		int end = line + 1 < LineCount
			? _lineStarts[line + 1] - (line < _lineHardBreak.Count && _lineHardBreak[line] ? 1 : 0)
			: _text.Length;

		if (end < start)
			end = start;

		return (start, end - start);
	}

	private string LineText(int line)
	{
		var (start, length) = LineRange(line);
		return _text.Substring(start, length);
	}

	private int VisibleLineCount
	{
		get
		{
			if (!Multiline)
				return 1;

			float usable = Bounds.Height - PaddingY * 2f;
			return Math.Max(1, (int)MathF.Floor(usable / MathF.Max(LineHeight, 1f)));
		}
	}

	private int MaxScrollLine => Math.Max(0, LineCount - VisibleLineCount);

	private bool ScrollBarVisible => Multiline && LineCount > VisibleLineCount;

	private bool ResizeGripVisible => Multiline;

	private float LineTop(SKRect ab, int line)
	{
		if (!Multiline)
			return ab.Top + (ab.Height - LineHeight) * 0.5f;

		return ab.Top + PaddingY + (line - _scrollLine) * LineHeight;
	}

	private float LineBaseline(SKRect ab, int line) => LineTop(ab, line) + Ascent;

	private float TextLeft(SKRect ab) => ab.Left + PaddingX - (Multiline ? 0f : _scrollX);

	/// <summary>Правая граница текста: полоса прокрутки не должна его перекрывать.</summary>
	private float TextRight(SKRect ab)
		=> ab.Right - PaddingX - (ScrollBarVisible ? ScrollBarWidth : 0f);

	private SKRect ScrollBarRect(SKRect ab)
	{
		float bottom = ab.Bottom - 2f - (ResizeGripVisible ? ResizeGripSize : 0f);

		return new SKRect(ab.Right - ScrollBarWidth - 2f, ab.Top + 2f, ab.Right - 2f, bottom);
	}

	private SKRect ScrollThumbRect(SKRect ab)
	{
		var track = ScrollBarRect(ab);

		if (track.Height <= 0f || LineCount <= 0)
			return SKRect.Empty;

		float ratio = Math.Clamp((float)VisibleLineCount / LineCount, 0f, 1f);
		float thumbHeight = MathF.Max(ScrollBarMinThumb, track.Height * ratio);

		float t = MaxScrollLine > 0 ? (float)_scrollLine / MaxScrollLine : 0f;
		float top = track.Top + (track.Height - thumbHeight) * t;

		return new SKRect(track.Left, top, track.Right, top + thumbHeight);
	}

	private SKRect ResizeGripRect(SKRect ab)
		=> new SKRect(ab.Right - ResizeGripSize, ab.Bottom - ResizeGripSize, ab.Right, ab.Bottom);

	#endregion

	#region Отрисовка

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		using var fill = SkUiTheme.Fill(pal.Surface);
		using var stroke = SkUiTheme.Stroke(Focused ? pal.Accent : pal.Border, Focused ? 1.5f : 1f);

		canvas.DrawRoundRect(ab, SkUiTheme.Radius, SkUiTheme.Radius, fill);
		canvas.DrawRoundRect(ab, SkUiTheme.Radius, SkUiTheme.Radius, stroke);

		// Прокрутка к каретке — только когда каретка действительно двигалась.
		if (_scrollToCaret)
		{
			EnsureCaretVisible();
			_scrollToCaret = false;
		}

		_scrollLine = Math.Clamp(_scrollLine, 0, MaxScrollLine);

		bool placeholder = string.IsNullOrEmpty(_text);
		SKColor textColor = placeholder ? pal.TextMuted : pal.Text;

		var contentRect = new SKRect(
			ab.Left + 4f,
			ab.Top + 2f,
			TextRight(ab) + PaddingX,
			ab.Bottom - 2f);

		canvas.Save();
		canvas.ClipRect(contentRect, SKClipOperation.Intersect, true);

		if (placeholder)
		{
			DrawPlaceholder(canvas, ab, pal);
		}
		else
		{
			// Выделение под текстом, каретка поверх.
			DrawSelectionHighlight(canvas, ab, pal);
			DrawTextLines(canvas, ab, textColor);
			DrawCaret(canvas, ab, pal.Text);
		}

		canvas.Restore();

		if (ScrollBarVisible)
			DrawScrollBar(canvas, ab, pal);

		if (ResizeGripVisible)
			DrawResizeGrip(canvas, ab, pal);
	}

	private void DrawPlaceholder(SKCanvas canvas, SKRect ab, SkUiPalette pal)
	{
		if (!string.IsNullOrEmpty(Placeholder))
		{
			SkTextHelper.DrawLeft(
				canvas, Placeholder, ab.Left + PaddingX, LineBaseline(ab, 0), _fontSize, pal.TextMuted);
		}

		if (Focused)
			DrawCaretLine(canvas, ab, 0, ab.Left + PaddingX, pal.TextMuted);
	}

	private void DrawTextLines(SKCanvas canvas, SKRect ab, SKColor textColor)
	{
		int first = Multiline ? _scrollLine : 0;
		int last = Math.Min(LineCount - 1, first + VisibleLineCount - 1);

		for (int line = first; line <= last; line++)
			DrawTextWithTabs(canvas, LineText(line), TextLeft(ab), LineBaseline(ab, line), textColor);
	}

	private void DrawSelectionHighlight(SKCanvas canvas, SKRect ab, SkUiPalette pal)
	{
		if (!HasSelection)
			return;

		int selStart = SelectionStart;
		int selEnd = SelectionEnd;

		int first = Multiline ? _scrollLine : 0;
		int last = Math.Min(LineCount - 1, first + VisibleLineCount - 1);

		using var sel = SkUiTheme.Fill(pal.Selected);

		for (int line = first; line <= last; line++)
		{
			var (lineStart, lineLength) = LineRange(line);
			int lineEnd = lineStart + lineLength;

			int from = Math.Max(selStart, lineStart);
			int to = Math.Min(selEnd, lineEnd);

			if (to < from)
				continue;

			// Пустая строка внутри выделения: без узкой полоски не видно,
			// что перевод строки тоже выделен.
			bool crossesLineBreak = selEnd > lineEnd && line + 1 < LineCount;

			string text = LineText(line);
			float x1 = TextLeft(ab) + MeasureSubstring(text, from - lineStart);
			float x2 = TextLeft(ab) + MeasureSubstring(text, to - lineStart);

			if (crossesLineBreak)
				x2 += _fontSize * 0.4f;

			if (x2 - x1 < 1f)
			{
				if (!crossesLineBreak)
					continue;

				x2 = x1 + _fontSize * 0.4f;
			}

			float top = LineTop(ab, line);
			canvas.DrawRoundRect(new SKRect(x1, top, x2, top + LineHeight), 3f, 3f, sel);
		}
	}

	private void DrawCaret(SKCanvas canvas, SKRect ab, SKColor color)
	{
		if (!Focused)
			return;

		int line = LineOfIndex(_caretIndex);

		if (Multiline && (line < _scrollLine || line >= _scrollLine + VisibleLineCount))
			return;

		var (lineStart, _) = LineRange(line);
		float x = TextLeft(ab) + MeasureSubstring(LineText(line), _caretIndex - lineStart);

		DrawCaretLine(canvas, ab, line, x, color);
	}

	private void DrawCaretLine(SKCanvas canvas, SKRect ab, int line, float x, SKColor color)
	{
		float top = LineTop(ab, line);

		using var caret = SkUiTheme.Stroke(color, 1.2f);
		canvas.DrawLine(x, top + 1f, x, top + LineHeight - 1f, caret);
	}

	private void DrawScrollBar(SKCanvas canvas, SKRect ab, SkUiPalette pal)
	{
		var track = ScrollBarRect(ab);

		if (track.Height <= 0f)
			return;

		using var trackPaint = SkUiTheme.Fill(pal.Surface2);
		using var thumbPaint = SkUiTheme.Fill(_scrollDragging ? pal.Accent : pal.Border);

		canvas.DrawRoundRect(track, 4f, 4f, trackPaint);

		var thumb = ScrollThumbRect(ab);

		if (!thumb.IsEmpty)
			canvas.DrawRoundRect(thumb, 4f, 4f, thumbPaint);
	}

	private void DrawResizeGrip(SKCanvas canvas, SKRect ab, SkUiPalette pal)
	{
		var grip = ResizeGripRect(ab);

		using var line = SkUiTheme.Stroke(_resizingBox ? pal.Accent : pal.TextMuted, 1.2f);

		// Три диагональные насечки — общепринятый вид ручки изменения размера.
		for (int i = 1; i <= 3; i++)
		{
			float offset = i * 3f;

			canvas.DrawLine(
				grip.Right - offset, grip.Bottom - 2f,
				grip.Right - 2f, grip.Bottom - offset,
				line);
		}
	}

	#endregion

	#region Прокрутка

	private void EnsureCaretVisible()
	{
		if (Multiline)
		{
			int line = LineOfIndex(_caretIndex);
			int visible = VisibleLineCount;

			if (line < _scrollLine)
				_scrollLine = line;
			else if (line >= _scrollLine + visible)
				_scrollLine = line - visible + 1;

			_scrollLine = Math.Clamp(_scrollLine, 0, MaxScrollLine);
			_scrollX = 0f;
			return;
		}

		_scrollLine = 0;

		float caretX = MeasureSubstring(_text, _caretIndex);
		float usable = MathF.Max(1f, Bounds.Width - PaddingX * 2f);

		if (caretX - _scrollX < 0f)
			_scrollX = caretX;
		else if (caretX - _scrollX > usable)
			_scrollX = caretX - usable;

		float total = MeasureSubstring(_text, _text.Length);
		_scrollX = Math.Clamp(_scrollX, 0f, MathF.Max(0f, total - usable));
	}

	/// <summary>
	/// Прокрутка колесом. Работает, когда поле в фокусе или под курсором.
	///
	/// Панель свойств забирает колесо себе и не передаёт его дочерним контролам,
	/// поэтому нужна правка в SkPropertyPanelControl.OnMouseWheel — см. конец файла.
	/// </summary>
	public override bool OnMouseWheel(SkMouseEvent e)
	{
		if (!Multiline || !ScrollBarVisible)
			return false;

		if (!Focused && !HitTest(e.Location))
			return false;

		// Три строки на щелчок — то же, что в списках Windows.
		int step = 3 * Math.Sign(e.Delta);
		int next = Math.Clamp(_scrollLine - step, 0, MaxScrollLine);

		if (next == _scrollLine)
		{
			// Событие всё равно наше: панель прокручивать не надо, иначе поле
			// уедет вместе с ней на границах текста.
			return true;
		}

		_scrollLine = next;

		// Возврат к каретке отменяем: пользователь смотрит в другое место.
		_scrollToCaret = false;
		return true;
	}

	#endregion

	#region Мышь

	public override bool OnMouseDown(SkMouseEvent e)
	{
		bool wasFocused = Focused;
		Focused = HitTest(e.Location);

		if (!Focused)
		{
			_mouseSelecting = false;
			return wasFocused;
		}

		var ab = AbsoluteBounds;

		// Ручка изменения размера перехватывает клик первой: она лежит поверх
		// полосы прокрутки в углу.
		if (ResizeGripVisible && ResizeGripRect(ab).Contains(e.Location))
		{
			_resizingBox = true;
			_resizeStartMouseY = e.Location.Y;
			_resizeStartHeight = Bounds.Height;
			return true;
		}

		if (ScrollBarVisible && ScrollBarRect(ab).Contains(e.Location))
		{
			var thumb = ScrollThumbRect(ab);

			if (thumb.Contains(e.Location))
			{
				_scrollDragging = true;
				_scrollDragOffset = e.Location.Y - thumb.Top;
			}
			else
			{
				// Клик мимо ползунка — страница вверх или вниз.
				int step = e.Location.Y < thumb.Top ? -VisibleLineCount : VisibleLineCount;
				_scrollLine = Math.Clamp(_scrollLine + step, 0, MaxScrollLine);
				_scrollToCaret = false;
			}

			return true;
		}

		int idx = GetCaretIndexFromPoint(e.Location);

		_selectionStart = idx;
		_selectionLength = 0;
		CaretIndex = idx;
		_mouseSelecting = true;
		_scrollToCaret = true;

		return true;
	}

	public override bool OnMouseMove(SkMouseEvent e)
	{
		bool old = Hovered;
		Hovered = HitTest(e.Location);

		if (_resizingBox)
		{
			float delta = e.Location.Y - _resizeStartMouseY;
			float height = Math.Clamp(_resizeStartHeight + delta, MinBoxHeight, MaxBoxHeight);

			if (MathF.Abs(height - Bounds.Height) > 0.5f)
			{
				Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top + height);

				// Высота строки в панели зависит от высоты редактора, поэтому
				// нужен пересчёт раскладки всей панели, а не только перерисовка.
				SizeChanged?.Invoke();
			}

			return true;
		}

		if (_scrollDragging)
		{
			var ab = AbsoluteBounds;
			var track = ScrollBarRect(ab);
			var thumb = ScrollThumbRect(ab);

			float free = track.Height - thumb.Height;

			if (free > 0.01f)
			{
				float t = Math.Clamp((e.Location.Y - _scrollDragOffset - track.Top) / free, 0f, 1f);
				_scrollLine = (int)MathF.Round(t * MaxScrollLine);
				_scrollToCaret = false;
			}

			return true;
		}

		if (Focused && _mouseSelecting)
		{
			int idx = GetCaretIndexFromPoint(e.Location);
			_selectionLength = idx - _selectionStart;
			CaretIndex = idx;
			_scrollToCaret = true;
			return true;
		}

		return old != Hovered;
	}

	public override bool OnMouseUp(SkMouseEvent e)
	{
		bool wasActive = _mouseSelecting || _scrollDragging || _resizingBox;

		_mouseSelecting = false;
		_scrollDragging = false;
		_resizingBox = false;

		return Focused || wasActive;
	}

	public override bool OnMouseDoubleClick(SkMouseEvent e)
	{
		if (!HitTest(e.Location))
			return false;

		Focused = true;

		if (string.IsNullOrEmpty(_text))
		{
			_selectionStart = 0;
			_selectionLength = 0;
			CaretIndex = 0;
			return true;
		}

		int idx = Math.Clamp(GetCaretIndexFromPoint(e.Location), 0, _text.Length);

		if (idx == _text.Length && idx > 0)
			idx--;

		SelectWordAt(idx);
		_mouseSelecting = false;
		return true;
	}

	/// <summary>
	/// Позиция каретки по точке. В многострочном режиме сначала определяется
	/// строка по Y, потом столбец по X внутри этой строки.
	/// </summary>
	private int GetCaretIndexFromPoint(SKPoint p)
	{
		var ab = AbsoluteBounds;

		int line = 0;

		if (Multiline)
		{
			float rel = p.Y - (ab.Top + PaddingY);
			line = _scrollLine + (int)MathF.Floor(rel / MathF.Max(LineHeight, 1f));
			line = Math.Clamp(line, 0, LineCount - 1);
		}

		var (lineStart, lineLength) = LineRange(line);
		string text = LineText(line);

		float localX = p.X - TextLeft(ab);

		if (localX <= 0f)
			return lineStart;

		for (int i = 0; i <= lineLength; i++)
		{
			float w = MeasureSubstring(text, i);

			if (localX <= w)
			{
				if (i == 0)
					return lineStart;

				float prev = MeasureSubstring(text, i - 1);
				return lineStart + ((localX - prev) < (w - localX) ? i - 1 : i);
			}
		}

		return lineStart + lineLength;
	}

	#endregion

	#region Клавиатура

	public override bool OnKeyChar(char c)
	{
		if (!Focused)
			return false;

		// Enter приходит как '\r'. В многострочном поле это перевод строки,
		// в однострочном — завершение ввода.
		if (c == '\r' || c == '\n')
		{
			if (Multiline)
			{
				ReplaceSelection("\n");
				return true;
			}

			RaiseEditingFinished();
			return true;
		}

		// Табуляция — управляющий символ, поэтому её нужно пропустить до
		// проверки IsControl, иначе она отсеется вместе с остальными.
		if (c == '\t')
		{
			if (!Multiline)
				return false;

			ReplaceSelection("\t");
			return true;
		}

		if (char.IsControl(c))
			return false;

		ReplaceSelection(c.ToString());
		return true;
	}

	public override bool OnKeyDown(SkKeyEvent e)
	{
		if (!Focused)
			return false;

		switch (e.KeyCode)
		{
			case Keys.Left:
				MoveCaretTo(_caretIndex - 1, e.Shift);
				return true;

			case Keys.Right:
				MoveCaretTo(_caretIndex + 1, e.Shift);
				return true;

			case Keys.Up:
				if (!Multiline) return false;
				MoveCaretVertical(-1, e.Shift);
				return true;

			case Keys.Down:
				if (!Multiline) return false;
				MoveCaretVertical(1, e.Shift);
				return true;

			case Keys.PageUp:
				if (!Multiline) return false;
				MoveCaretVertical(-VisibleLineCount, e.Shift);
				return true;

			case Keys.PageDown:
				if (!Multiline) return false;
				MoveCaretVertical(VisibleLineCount, e.Shift);
				return true;

			case Keys.Home:
				// Home — в начало строки, Ctrl+Home — в начало текста.
				MoveCaretTo(e.Ctrl ? 0 : LineRange(LineOfIndex(_caretIndex)).Start, e.Shift);
				return true;

			case Keys.End:
				if (e.Ctrl)
				{
					MoveCaretTo(_text.Length, e.Shift);
				}
				else
				{
					var (start, length) = LineRange(LineOfIndex(_caretIndex));
					MoveCaretTo(start + length, e.Shift);
				}
				return true;

			case Keys.Tab:
				// В многострочном поле табуляция вводится, а не переводит фокус.
				if (!Multiline)
					return false;

				ReplaceSelection("\t");
				return true;

			case Keys.Back:
				Backspace();
				return true;

			case Keys.Delete:
				DeleteForward();
				return true;

			case Keys.Escape:
				// Выход из многострочного поля: Enter там занят переводом строки.
				RaiseEditingFinished();
				Focused = false;
				return true;

			case Keys.A:
				if (e.Ctrl)
				{
					SelectAll();
					return true;
				}
				break;

			case Keys.C:
				if (e.Ctrl && HasSelection)
				{
					TrySetClipboard(GetSelectedText());
					return true;
				}
				break;

			case Keys.X:
				if (e.Ctrl && HasSelection)
				{
					TrySetClipboard(GetSelectedText());
					DeleteSelection();
					return true;
				}
				break;

			case Keys.V:
				if (e.Ctrl)
				{
					string clip = TryGetClipboard();

					if (!string.IsNullOrEmpty(clip))
					{
						// Однострочное поле не должно получить переводы строк
						// и табуляции из буфера: они сломали бы раскладку.
						clip = Multiline
							? clip.Replace("\r\n", "\n").Replace("\r", "\n")
							: clip.Replace("\r", "").Replace("\n", " ").Replace("\t", " ");

						ReplaceSelection(clip);
					}

					return true;
				}
				break;
		}

		return false;
	}

	private static void TrySetClipboard(string text)
	{
		try
		{
			if (!string.IsNullOrEmpty(text))
				Clipboard.SetText(text);
		}
		catch
		{
			// Буфер может быть занят другим процессом — это не причина падать.
		}
	}

	private static string TryGetClipboard()
	{
		try
		{
			return Clipboard.GetText();
		}
		catch
		{
			return string.Empty;
		}
	}

	#endregion

	#region Перемещение каретки

	private void MoveCaretTo(int pos, bool shift)
	{
		pos = Math.Clamp(pos, 0, _text.Length);

		if (shift)
		{
			if (!HasSelection)
				_selectionStart = _caretIndex;

			_selectionLength = pos - _selectionStart;
		}
		else
		{
			_selectionStart = pos;
			_selectionLength = 0;
		}

		CaretIndex = pos;
		_scrollToCaret = true;
	}

	/// <summary>
	/// Перемещение каретки на строку вверх или вниз с сохранением столбца.
	/// Если в целевой строке символов меньше, каретка встаёт в её конец.
	/// </summary>
	private void MoveCaretVertical(int delta, bool shift)
	{
		int line = LineOfIndex(_caretIndex);
		int targetLine = Math.Clamp(line + delta, 0, LineCount - 1);

		if (targetLine == line)
			return;

		var (currentStart, _) = LineRange(line);
		int column = _caretIndex - currentStart;

		var (targetStart, targetLength) = LineRange(targetLine);

		MoveCaretTo(targetStart + Math.Min(column, targetLength), shift);
	}

	private void ClampSelection()
	{
		_selectionStart = Math.Clamp(_selectionStart, 0, _text.Length);

		int end = Math.Clamp(_selectionStart + _selectionLength, 0, _text.Length);
		_selectionLength = end - _selectionStart;
	}

	#endregion

	#region Правка текста

	private void Backspace()
	{
		if (HasSelection)
		{
			DeleteSelection();
			return;
		}

		if (_caretIndex <= 0)
			return;

		SetTextInternal(_text.Remove(_caretIndex - 1, 1), _caretIndex - 1);
	}

	private void DeleteForward()
	{
		if (HasSelection)
		{
			DeleteSelection();
			return;
		}

		if (_caretIndex >= _text.Length)
			return;

		SetTextInternal(_text.Remove(_caretIndex, 1), _caretIndex);
	}

	private void ReplaceSelection(string insertText)
	{
		int start = SelectionStart;
		int len = SelectionLengthAbs;

		string next = _text;

		if (len > 0)
			next = next.Remove(start, len);

		next = next.Insert(start, insertText);

		SetTextInternal(next, start + insertText.Length);
	}

	private void DeleteSelection()
	{
		if (!HasSelection)
			return;

		int start = SelectionStart;
		int len = SelectionLengthAbs;

		SetTextInternal(_text.Remove(start, len), start);
	}

	/// <summary>
	/// Единая точка изменения текста изнутри контрола: обновляет строки,
	/// каретку, сбрасывает выделение и поднимает событие.
	/// </summary>
	private void SetTextInternal(string newText, int caret)
	{
		_text = newText ?? string.Empty;

		RebuildLines();

		CaretIndex = caret;
		_selectionStart = _caretIndex;
		_selectionLength = 0;
		_scrollToCaret = true;

		RaiseTextChanged();
	}

	private string GetSelectedText()
		=> HasSelection ? _text.Substring(SelectionStart, SelectionLengthAbs) : string.Empty;

	private void SelectWordAt(int index)
	{
		if (string.IsNullOrEmpty(_text))
		{
			_selectionStart = 0;
			_selectionLength = 0;
			CaretIndex = 0;
			return;
		}

		index = Math.Clamp(index, 0, _text.Length - 1);

		if (_text[index] == '\n')
			return;

		if (!IsWordChar(_text[index]))
		{
			_selectionStart = index;
			_selectionLength = 1;
			CaretIndex = index + 1;
			return;
		}

		int start = index;
		int end = index;

		while (start > 0 && IsWordChar(_text[start - 1]))
			start--;

		while (end < _text.Length - 1 && IsWordChar(_text[end + 1]))
			end++;

		_selectionStart = start;
		_selectionLength = end - start + 1;
		CaretIndex = end + 1;
	}

	private static bool IsWordChar(char c)
		=> char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '+' || c == '.' || c == ',';

	#endregion
}


public class SkNumTextBox : SkTextBox
{
	public bool AllowNegative { get; set; } = true;
	public bool AllowDecimal { get; set; } = true;

	public override bool OnKeyChar(char c)
	{
		if (!Focused)
			return false;

		if (c == '\r')
		{
			RaiseEditingFinished();
			return true;
		}

		if (char.IsControl(c))
			return false;

		if (!IsAllowedChar(c))
			return true;

		string next = BuildTextAfterInsertion(c.ToString());
		if (!IsPotentiallyValidNumericText(next))
			return true;

		return base.OnKeyChar(c);
	}

	public override bool OnKeyDown(SkKeyEvent e)
	{
		if (!Focused)
			return false;

		switch (e.KeyCode)
		{
			case Keys.Enter:
				RaiseEditingFinished();
				return true;

			case Keys.V:
				if (e.Ctrl)
				{
					try
					{
						string clip = Clipboard.GetText();
						if (string.IsNullOrWhiteSpace(clip))
							return true;

						clip = NormalizeNumericText(clip);
						if (!IsPotentiallyValidNumericText(BuildTextAfterInsertion(clip)))
							return true;

						InsertReplacementText(clip);
					}
					catch
					{
					}

					return true;
				}
				break;
		}

		return base.OnKeyDown(e);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		bool wasFocused = Focused;
		bool handled = base.OnMouseDown(e);

		if (wasFocused && !HitTest(e.Location))
			RaiseEditingFinished();

		return handled;
	}

	private bool IsAllowedChar(char c)
	{
		if (char.IsDigit(c))
			return true;

		if (AllowNegative && c == '-')
			return true;

		if (AllowDecimal && (c == '.' || c == ','))
			return true;

		return false;
	}

	private string NormalizeNumericText(string s)
	{
		return (s ?? string.Empty).Trim().Replace(',', '.');
	}

	private bool IsPotentiallyValidNumericText(string s)
	{
		s = NormalizeNumericText(s);

		if (string.IsNullOrEmpty(s))
			return true;

		if (AllowNegative && s == "-")
			return true;

		if (AllowDecimal)
		{
			if (s == ".")
				return true;

			if (AllowNegative && s == "-.")
				return true;
		}

		int minusCount = s.Count(ch => ch == '-');
		if (minusCount > 1)
			return false;

		if (minusCount == 1 && (!AllowNegative || s[0] != '-'))
			return false;

		int dotCount = s.Count(ch => ch == '.');
		if (dotCount > 1)
			return false;

		if (!AllowDecimal && dotCount > 0)
			return false;

		foreach (char ch in s)
		{
			if (!(char.IsDigit(ch) || ch == '-' || ch == '.'))
				return false;
		}

		return true;
	}

	private string BuildTextAfterInsertion(string insertText)
	{
		string current = Text ?? string.Empty;

		int selStart = SelectionStart;
		int selLen = SelectionLengthAbs;

		if (selLen > 0)
			current = current.Remove(selStart, selLen);

		return current.Insert(selStart, insertText);
	}

	private void InsertReplacementText(string insertText)
	{
		string current = Text ?? string.Empty;

		int selStart = SelectionStart;
		int selLen = SelectionLengthAbs;

		if (selLen > 0)
			current = current.Remove(selStart, selLen);

		current = current.Insert(selStart, insertText);

		Text = current;
		CaretIndex = selStart + insertText.Length;
		TextChanged?.Invoke(Text);
	}
}