using System.Runtime.InteropServices;

using SkiaSharp;

using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Правка надписи прямо на листе.
///
/// Вход — двойной щелчок по надписи, как в чертёжных редакторах.
/// Дальше работает всё, чего ждут от текстового поля: набор, перевод
/// строки, выделение клавишами и мышью, буфер обмена, отмена набранного.
/// Выход — Escape или щелчок мимо надписи.
///
/// Правятся все три вида надписей: обычная, разложенная по фигуре
/// с обтеканием препятствий и идущая по кривой.
///
/// Разбор ввода здесь, а правила про символы и строки — в
/// TextEditSession, геометрия каретки — в TextEditGeometry.
/// </summary>
public sealed partial class MainView
{
	private TextEditSession? _textEdit;

	/// <summary>
	/// Мигание каретки. Таймер свой, а не общий с чем-то ещё: он должен
	/// запускаться заново при каждом движении каретки, иначе набирающий
	/// текст время от времени не видит, где он печатает.
	/// </summary>
	private System.Windows.Forms.Timer? _caretTimer;

	/// <summary>Идёт ли правка текста на листе.</summary>
	public bool IsTextEditing => _textEdit != null;

	/// <summary>Надпись, которую правят. null — правки нет.</summary>
	public TextShape? EditedText => _textEdit?.Shape;

	#region Вход и выход

	/// <summary>
	/// Начинает правку надписи и ставит каретку туда, куда показали.
	/// Точка в мировых координатах; null — каретка встаёт в конец текста.
	/// </summary>
	public void BeginTextEdit(TextShape shape, SKPoint? worldPoint = null)
	{
		if (!TextEditSession.CanEdit(shape))
			return;

		if (_textEdit?.Shape == shape)
		{
			if (worldPoint.HasValue)
				PlaceCaret(worldPoint.Value, extend: false);

			return;
		}

		EndTextEdit();

		_textEdit = new TextEditSession(shape);

		if (worldPoint.HasValue)
			PlaceCaret(worldPoint.Value, extend: false);

		StartCaretBlink();

		// Режим ставится через форму: поле режима в ней закрытое, и она же
		// отмечает текущий режим на боковой полосе.
		mainForm?.SetEditorMode(EditorMode.Text);

		Focus();
		InvalidateCanvas();
	}

	/// <summary>
	/// Заканчивает правку и записывает один шаг отмены на весь сеанс.
	///
	/// Один, а не по шагу на букву: в общей истории «отменить» должно
	/// возвращать надпись к тому виду, с каким в неё вошли. Отмена
	/// внутри набора — своя, она живёт в сеансе и кончается вместе с ним.
	/// </summary>
	public void EndTextEdit()
	{
		var session = _textEdit;

		if (session == null)
			return;

		_textEdit = null;

		StopCaretBlink();

		var shape = session.Shape;
		string after = shape.Text ?? string.Empty;

		if (!string.Equals(after, session.TextBefore, StringComparison.Ordinal))
		{
			Undo?.Push(new PropertyChangeAction<string>(
				"Edit text",
				session.TextBefore,
				after,
				value =>
				{
					shape.Text = value;

					// Поле текста в панели свойств держит свою копию
					// строки: без пересборки оно показывало бы отменённое.
					mainForm?.RebuildPropertyPanel();
				}));

			if (CurrentPage != null)
				CurrentPage.doc.need_save = true;
		}

		SelectionGeometryChanged?.Invoke();
		InvalidateCanvas();
	}

	private void StartCaretBlink()
	{
		_caretTimer ??= new System.Windows.Forms.Timer { Interval = 530 };

		_caretTimer.Tick -= OnCaretBlink;
		_caretTimer.Tick += OnCaretBlink;

		if (_textEdit != null)
			_textEdit.CaretVisible = true;

		_caretTimer.Stop();
		_caretTimer.Start();
	}

	private void StopCaretBlink() => _caretTimer?.Stop();

	private void OnCaretBlink(object? sender, EventArgs e)
	{
		if (_textEdit == null)
		{
			StopCaretBlink();
			return;
		}

		_textEdit.CaretVisible = !_textEdit.CaretVisible;
		InvalidateCanvas();
	}

	/// <summary>
	/// Каретка показалась и отсчёт мигания пошёл заново.
	///
	/// Текста не касается: перемещение каретки — не правка, и помечать
	/// из-за него документ изменённым было бы неправдой.
	/// </summary>
	private void TouchCaret()
	{
		if (_textEdit == null)
			return;

		_textEdit.CaretVisible = true;

		_caretTimer?.Stop();
		_caretTimer?.Start();

		InvalidateCanvas();
	}

	private void TextChangedDuringEdit()
	{
		if (_textEdit == null)
			return;

		// Пути надписи строятся при каждой отрисовке, сбрасывать нечего:
		// достаточно позвать перерисовку и сообщить о смене геометрии —
		// по ней панель свойств обновляет размеры.
		if (CurrentPage != null)
			CurrentPage.doc.need_save = true;

		SelectionGeometryChanged?.Invoke();
		InvalidateCanvas();
	}

	#endregion

	#region Мышь

	/// <summary>
	/// Попадает ли точка в текст, который правят.
	///
	/// Меряется расстояние до ближайшего места каретки, а не попадание
	/// в габаритную коробку. Коробка не годится: у текста по кривой она
	/// накрывает всю фигуру, вокруг которой он идёт, и щелчок в середине
	/// окружности считался бы щелчком по надписи. А у текста по фигуре
	/// с обтеканием в коробку попадает и то место, откуда текст
	/// вытеснен препятствием.
	///
	/// Порог берётся от высоты строки на экране — она же длина отрезка
	/// каретки: так он сам подстраивается и под кегль, и под масштаб.
	/// </summary>
	private bool HitsEditedText(SKPoint world)
	{
		var session = _textEdit;

		if (session == null)
			return false;

		var spots = TextEditGeometry.Spots(session);

		float best = float.MaxValue;
		float lineHeight = 0f;

		foreach (var spot in spots)
		{
			if (!spot.Valid)
				continue;

			lineHeight = MathF.Max(lineHeight, SKPoint.Distance(spot.Top, spot.Bottom));

			float distance = DistanceToSegment(world, spot.Top, spot.Bottom);

			if (distance < best)
				best = distance;
		}

		if (best == float.MaxValue)
			return false;

		return best <= MathF.Max(lineHeight, 1f);
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		float lengthSquared = dx * dx + dy * dy;

		if (lengthSquared < 1e-9f)
			return SKPoint.Distance(p, a);

		float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;

		t = Math.Clamp(t, 0f, 1f);

		return SKPoint.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
	}

	private void PlaceCaret(SKPoint world, bool extend)
	{
		var session = _textEdit;

		if (session == null)
			return;

		session.SetCaret(TextEditGeometry.IndexAt(session, world), extend);

		TouchCaret();
		InvalidateCanvas();
	}

	/// <summary>
	/// Нажатие кнопки во время правки. true — холсту дальше делать
	/// нечего, нажатие ушло в текст.
	/// </summary>
	private bool TextEditMouseDown(MouseEventArgs e, SKPoint world)
	{
		if (_textEdit == null || e.Button != MouseButtons.Left)
			return false;

		if (!HitsEditedText(world))
		{
			// Щелчок мимо надписи заканчивает правку, но само нажатие
			// отдаётся холсту: пользователь уже показал, куда хочет
			// попасть, и заставлять его щёлкать второй раз незачем.
			EndTextEdit();

			return false;
		}

		PlaceCaret(world, extend: (ModifierKeys & Keys.Shift) == Keys.Shift);

		_textEdit.Dragging = true;

		return true;
	}

	private bool TextEditMouseMove(SKPoint world)
	{
		if (_textEdit is not { Dragging: true })
			return false;

		PlaceCaret(world, extend: true);

		return true;
	}

	private bool TextEditMouseUp()
	{
		if (_textEdit is not { Dragging: true })
			return false;

		_textEdit.Dragging = false;

		return true;
	}

	/// <summary>Двойной щелчок во время правки выделяет слово.</summary>
	private bool TextEditDoubleClick(SKPoint world)
	{
		var session = _textEdit;

		if (session == null || !HitsEditedText(world))
			return false;

		session.SelectWordAt(TextEditGeometry.IndexAt(session, world));

		TouchCaret();
		InvalidateCanvas();

		return true;
	}

	#endregion

	#region Клавиатура

	/// <summary>
	/// Клавиши правки текста. true — клавиша израсходована.
	///
	/// Вызывается из OnKeyDown раньше всего остального: пока правят
	/// надпись, стрелки двигают каретку, а не фигуру, и Delete убирает
	/// символ, а не объект.
	/// </summary>
	private bool TextEditKeyDown(KeyEventArgs e)
	{
		var session = _textEdit;

		if (session == null)
			return false;

		bool shift = (e.Modifiers & Keys.Shift) == Keys.Shift;
		bool ctrl = (e.Modifiers & Keys.Control) == Keys.Control;

		switch (e.KeyCode)
		{
			case Keys.Escape:
				EndTextEdit();
				break;

			case Keys.Left:
				session.MoveHorizontal(-1, shift, ctrl);
				break;

			case Keys.Right:
				session.MoveHorizontal(+1, shift, ctrl);
				break;

			case Keys.Up:
				session.MoveVertical(-1, shift);
				break;

			case Keys.Down:
				session.MoveVertical(+1, shift);
				break;

			case Keys.Home:
				if (ctrl)
					session.MoveToTextEdge(toStart: true, shift);
				else
					session.MoveToLineEdge(toStart: true, shift);

				break;

			case Keys.End:
				if (ctrl)
					session.MoveToTextEdge(toStart: false, shift);
				else
					session.MoveToLineEdge(toStart: false, shift);

				break;

			case Keys.Back:
				session.Backspace();
				TextChangedDuringEdit();
				break;

			case Keys.Delete:
				session.DeleteForward();
				TextChangedDuringEdit();
				break;

			case Keys.Enter:
				session.Insert("\n");
				TextChangedDuringEdit();
				break;

			case Keys.Tab:
				session.Insert("\t");
				TextChangedDuringEdit();
				break;

			case Keys.A when ctrl:
				session.SelectAll();
				break;

			case Keys.C when ctrl:
				CopySelectedText(session);
				break;

			case Keys.X when ctrl:
				CopySelectedText(session);
				session.DeleteSelection();
				TextChangedDuringEdit();
				break;

			case Keys.V when ctrl:
				PasteIntoText(session);
				break;

			case Keys.Z when ctrl:
				session.Undo();
				TextChangedDuringEdit();
				break;

			case Keys.Y when ctrl:
				session.Redo();
				TextChangedDuringEdit();
				break;

			default:
				return false;
		}

		TouchCaret();

		e.Handled = true;
		e.SuppressKeyPress = true;

		return true;
	}

	/// <summary>
	/// Набранный символ. Возвращает true, если он ушёл в текст.
	///
	/// Управляющие коды сюда тоже приходят — перевод строки, забой,
	/// табуляция, — но их уже разобрал TextEditKeyDown, и вставлять
	/// их второй раз нельзя.
	/// </summary>
	private bool TextEditKeyPress(KeyPressEventArgs e)
	{
		var session = _textEdit;

		if (session == null || char.IsControl(e.KeyChar))
			return false;

		session.Insert(e.KeyChar.ToString());

		TouchCaret();
		TextChangedDuringEdit();

		e.Handled = true;

		return true;
	}

	private static void CopySelectedText(TextEditSession session)
	{
		string selected = session.SelectedText();

		if (selected.Length == 0)
			return;

		try
		{
			Clipboard.SetText(selected);
		}
		catch (ExternalException)
		{
			// Буфером обмена в этот миг владеет другая программа.
			// Потерянная копия — не повод прерывать правку.
		}
	}

	private void PasteIntoText(TextEditSession session)
	{
		string text;

		try
		{
			text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
		}
		catch (ExternalException)
		{
			return;
		}

		if (text.Length == 0)
			return;

		session.Insert(text);
		TextChangedDuringEdit();
	}

	#endregion

	#region Отрисовка

	private void DrawTextEdit(SKCanvas canvas)
	{
		if (_textEdit != null)
			TextEditAdorner.Draw(canvas, this, _textEdit);
	}

	#endregion
}
