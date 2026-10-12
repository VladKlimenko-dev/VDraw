using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public sealed class SkPropertyRow : SkControl
{
	public string Label { get; set; } = string.Empty;
	public float LabelWidth { get; set; } = 96f;
	public float EditorGap { get; set; } = 6f;

	/// <summary>
	/// Отступ редакторов слева, В ДОПОЛНЕНИЕ к колонке подписи.
	///
	/// Нужен для вложенных строк: подчинённый параметр смещается вправо,
	/// и по одному взгляду видно, чему он подчинён. Ноль по умолчанию —
	/// строка выглядит ровно так же, как выглядела.
	///
	/// Отдельно от LabelWidth намеренно: та задаёт ширину колонки подписи
	/// и одинакова у всех строк панели, иначе значения перестали бы
	/// выстраиваться в столбец. Отступ же у каждой строки свой.
	/// </summary>
	public float Indent { get; set; } = 0f;

	/// <summary>
	/// Сдвигать ли вместе с редакторами и саму подпись.
	///
	/// По умолчанию да: подпись — часть строки, и оставить её на месте,
	/// сдвинув значение, значит разорвать пару. Выключается там, где
	/// подпись служит заголовком блока, а отступ нужен только значениям.
	/// </summary>
	public bool IndentLabel { get; set; } = true;

	// Список редакторов в строке. Если WidthOverride контрола > 0 — используется он,
	// иначе оставшаяся ширина делится равномерно между контролами без явной ширины.
	public List<SkControl> Editors { get; } = new();

	// Обратная совместимость со старым конструктором (один контрол).
	public SkControl Editor => Editors.Count > 0 ? Editors[0] : null!;

	public SkPropertyRow(string label, SkControl editor)
	{
		Label = label;
		Bounds = new SKRect(0, 0, 260, 30);
		AddEditor(editor);
	}

	public SkPropertyRow(string name, string label, SkControl editor)
	{
		Name = name;
		Label = label;
		Bounds = new SKRect(0, 0, 260, 30);
		AddEditor(editor);
	}

	public SkPropertyRow(string label, params SkControl[] editors)
	{
		Label = label;
		Bounds = new SKRect(0, 0, 260, 30);

		foreach (var e in editors)
			AddEditor(e);
	}

	public void AddEditor(SkControl editor)
	{
		if (editor == null || Editors.Contains(editor))
			return;

		Editors.Add(editor);
		Add(editor);
	}

	// Позволяет задать фиксированную ширину для конкретного редактора через Tag-подобное свойство.
	// Используем словарь, т.к. SkControl не имеет собственного поля ширины-переопределения.
	private readonly Dictionary<SkControl, float> _fixedWidths = new();

	public void SetFixedWidth(SkControl editor, float width)
	{
		_fixedWidths[editor] = width;
	}

	public override void Arrange()
	{
		const float minRowHeight = 30f;

		// Отступ съедает ширину строки, а не наезжает на соседей:
		// иначе редактор с отступом вылезал бы за правый край панели.
		float indent = Math.Max(0f, Indent);
		float editorLeft = indent + LabelWidth + 8f;
		float availableWidth = Math.Max(0f, Bounds.Width - editorLeft);

		if (Editors.Count == 0)
		{
			Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top + minRowHeight);
			return;
		}

		float fixedTotal = 0f;
		int flexCount = 0;

		foreach (var e in Editors)
		{
			if (_fixedWidths.TryGetValue(e, out float fw))
				fixedTotal += fw;
			else
				flexCount++;
		}

		float gapsTotal = EditorGap * (Editors.Count - 1);
		float flexWidthEach = flexCount > 0
			? Math.Max(0f, (availableWidth - fixedTotal - gapsTotal) / flexCount)
			: 0f;

		float x = editorLeft;
		float maxHeight = minRowHeight;

		foreach (var e in Editors)
		{
			float w = _fixedWidths.TryGetValue(e, out float fw) ? fw : flexWidthEach;
			float h = Math.Max(minRowHeight, e.Bounds.Height);

			e.Bounds = new SKRect(x, 0, x + w, h);
			e.Arrange();

			h = Math.Max(h, e.Bounds.Height);
			maxHeight = Math.Max(maxHeight, h);

			x += w + EditorGap;
		}

		Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top + maxHeight);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		float labelLeft = ab.Left + (IndentLabel ? Math.Max(0f, Indent) : 0f);

		SkTextHelper.DrawLeft(canvas, Label, labelLeft, ab.Top + 20f, 12.5f, pal.TextMuted);
	}

	public override SkControl? FindTopMost(SKPoint p)
	{
		for (int i = Editors.Count - 1; i >= 0; i--)
		{
			var found = Editors[i].FindTopMost(p);
			if (found != null)
				return found;
		}

		return HitTest(p) ? this : null;
	}

	public override SkControl? FindTopMostOverlay(SKPoint p)
	{
		foreach (var e in Editors)
		{
			var found = e.FindTopMostOverlay(p);
			if (found != null)
				return found;
		}

		return null;
	}

	public override bool CloseDropDownsOutside(SKPoint p)
	{
		bool changed = false;

		foreach (var e in Editors)
			changed |= e.CloseDropDownsOutside(p);

		return changed;
	}
}

public sealed class SkCollapsePanel : SkControl
{
	public string HeaderText { get; set; } = "Panel";
	public bool Expanded { get; set; } = true;

	/// <summary>
	/// Заголовок панели свёрнут или развёрнут пользователем.
	///
	/// Событие, а не опрос состояния снаружи: панель пересобирается при
	/// каждой смене выделения, и прежний экземпляр к этому моменту уже
	/// выброшен — спросить его о состоянии будет некого.
	/// </summary>
	public Action<bool>? ExpandedChanged;

	public SkFlowLayout Content { get; } = new()
	{
		Direction = SkFlowDirection.Vertical,
		Gap = 8f,
		Padding = 10f,
		AutoHeight = true
	};

	private const float CornerRadius = 12f;
	private const float BottomPadding = 10f;
	private const float ClipInset = 1f;

	public SkCollapsePanel()
	{
		Add(Content);
	}

	public override void Arrange()
	{
		Content.Bounds = new SKRect(0, SkUiTheme.HeaderHeight, Bounds.Width, Bounds.Height);
		Content.Arrange();

		float h = SkUiTheme.HeaderHeight;
		if (Expanded)
			h += Content.Bounds.Height + BottomPadding;

		Bounds = new SKRect(Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Top + h);
	}

	protected override void DrawSelf(SKCanvas canvas)
	{
		if (!Visible)
			return;

		var pal = SkUiTheme.Current;
		var ab = AbsoluteBounds;

		using var fill = SkUiTheme.Fill(pal.PanelBack2);
		using var stroke = SkUiTheme.Stroke(pal.Border);

		canvas.DrawRoundRect(ab, CornerRadius, CornerRadius, fill);
		canvas.DrawRoundRect(ab, CornerRadius, CornerRadius, stroke);

		SkTextHelper.DrawLeft(canvas, HeaderText, ab.Left + 12f, ab.Top + 22f, 14f, pal.Text, true);
		SkTextHelper.DrawLeft(canvas, Expanded ? "-" : "+", ab.Right - 18f, ab.Top + 22f, 20f, pal.TextMuted, true);
	}

	public override void Draw(SKCanvas canvas)
	{
		if (!Visible)
			return;

		DrawSelf(canvas);

		if (!Expanded)
			return;

		var ab = AbsoluteBounds;

		var contentClipRect = new SKRect(
			ab.Left + ClipInset,
			ab.Top + SkUiTheme.HeaderHeight,
			ab.Right - ClipInset,
			ab.Bottom - ClipInset);

		float contentRadius = Math.Max(0f, CornerRadius - ClipInset);
		var contentClipRRect = new SKRoundRect(contentClipRect, contentRadius, contentRadius);

		canvas.Save();
		canvas.ClipRoundRect(contentClipRRect, SKClipOperation.Intersect, true);

		Content.Draw(canvas);

		canvas.Restore();
	}

	public override void DrawOverlay(SKCanvas canvas)
	{
		if (!Visible)
			return;

		if (Expanded)
			Content.DrawOverlay(canvas);
	}

	public override bool OnMouseDown(SkMouseEvent e)
	{
		var ab = AbsoluteBounds;
		var head = new SKRect(ab.Left, ab.Top, ab.Right, ab.Top + SkUiTheme.HeaderHeight);

		if (head.Contains(e.Location))
		{
			Expanded = !Expanded;
			ExpandedChanged?.Invoke(Expanded);
			return true;
		}

		return Expanded && Content.OnMouseDown(e);
	}

	public override SkControl? FindTopMost(SKPoint p)
	{
		if (!Visible || !AbsoluteBounds.Contains(p))
			return null;

		if (Expanded)
		{
			var found = Content.FindTopMost(p);
			if (found != null && found != Content)
				return found;
		}

		return this;
	}

	public override SkControl? FindTopMostOverlay(SKPoint p)
	{
		if (!Visible || !Expanded)
			return null;

		return Content.FindTopMostOverlay(p);
	}

	public override bool CloseDropDownsOutside(SKPoint p)
	{
		if (!Visible || !Expanded)
			return false;

		return Content.CloseDropDownsOutside(p);
	}
}