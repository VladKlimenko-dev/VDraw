using SkiaSharp;

using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Правка текста прямо на листе: каретка, выделение, набор.
///
/// Отдельный класс, а не код внутри MainView: здесь только текст
/// и позиции в нём — ни холста, ни мыши, ни клавиш. Благодаря этому
/// правила «куда идёт каретка» и «что выделено» можно читать и править
/// в одном месте, не продираясь через разбор ввода.
///
/// Сеанс не знает, как надпись разложена на листе. Строками от начала
/// координат, по форме фигуры с обтеканием препятствий или по кривой —
/// для правки это одно и то же: символы, строки и номера. Где на листе
/// оказался символ с таким-то номером, отвечает TextEditGeometry.
/// </summary>
public sealed class TextEditSession
{
	/// <summary>Сколько состояний текста помнит сеанс для своей отмены.</summary>
	private const int UndoDepth = 100;

	private readonly List<Snapshot> _undo = new();
	private readonly List<Snapshot> _redo = new();

	private readonly record struct Snapshot(string Text, int Caret, int Anchor);

	public TextShape Shape { get; }

	/// <summary>Текст, каким он был до начала правки. Нужен шагу истории.</summary>
	public string TextBefore { get; private set; }

	/// <summary>
	/// Текст, каким его оставил сам сеанс.
	///
	/// По нему видно, что надпись правили мимо сеанса — из поля в панели
	/// свойств или откатом истории. Своя правка всегда идёт через сеттер
	/// Text, и он это значение обновляет.
	/// </summary>
	private string _lastSeen;

	/// <summary>Положение каретки: число символов слева от неё.</summary>
	public int Caret { get; private set; }

	/// <summary>
	/// Вторая граница выделения. Равна каретке — выделения нет.
	///
	/// Хранится именно граница, а не длина: выделение растят в обе
	/// стороны, и при длине пришлось бы помнить ещё и знак.
	/// </summary>
	public int Anchor { get; private set; }

	public bool HasSelection => Caret != Anchor;

	public int SelectionStart => Math.Min(Caret, Anchor);

	public int SelectionEnd => Math.Max(Caret, Anchor);

	/// <summary>Идёт ли выделение протяжкой мыши.</summary>
	public bool Dragging { get; set; }

	/// <summary>
	/// Колонка, к которой стремится каретка при ходьбе вверх и вниз.
	///
	/// Без неё проход через короткую строку необратимо сдвигал бы
	/// каретку влево: вниз на «ab» — колонка 2, и дальше вниз уже
	/// от двойки. Так ведут себя все редакторы: колонка запоминается
	/// и держится, пока не двинут каретку по горизонтали или не правят
	/// текст. −1 — колонка не загадана.
	/// </summary>
	private int _desiredColumn = -1;

	/// <summary>Видна ли каретка сейчас. Мигание ведёт вызывающий.</summary>
	public bool CaretVisible { get; set; } = true;

	public string Text
	{
		get => Shape.Text ?? string.Empty;

		private set
		{
			Shape.Text = value;
			_lastSeen = value;
		}
	}

	private SKFont? _font;
	private (string Family, float Size, bool Bold, bool Italic) _fontKey;

	/// <summary>
	/// Шрифт надписи, живущий столько же, сколько сеанс.
	///
	/// Создаётся не на каждый запрос: геометрия спрашивает его по три
	/// раза за перерисовку, а мигающая каретка перерисовывает холст
	/// дважды в секунду — каждый такой шрифт тянет за собой начертание,
	/// которое освобождается не сразу.
	///
	/// Настройки шрифта меняют прямо во время набора, из панели свойств,
	/// поэтому хранится и то, из чего он сделан: разошлось — пересоздаём.
	/// </summary>
	internal SKFont Font
	{
		get
		{
			var key = (Shape.FontFamily, Shape.FontSizeMm, Shape.Bold, Shape.Italic);

			if (_font == null || _fontKey != key)
			{
				_font?.Dispose();
				_font = Shape.CreateEditFont();
				_fontKey = key;
			}

			return _font;
		}
	}

	/// <summary>Отпускает шрифт. Зовётся при выходе из правки.</summary>
	public void Release()
	{
		_font?.Dispose();
		_font = null;
	}

	/// <summary>
	/// Можно ли править эту надпись прямо на листе.
	///
	/// Можно любую: и обычную, и ту, что разложена по фигуре с обтеканием,
	/// и ту, что идёт по кривой. Места каретки для всех трёх раскладок
	/// считает TextEditGeometry.
	/// </summary>
	public static bool CanEdit(VDraw.Shapes.Shape? shape) => shape is TextShape;

	public TextEditSession(TextShape shape)
	{
		Shape = shape;

		// Переводы строки приводятся к одному виду сразу.
		//
		// Иначе индекс каретки пришлось бы считать по-разному для «\r\n»
		// и «\n»: при выводе они равны одному переводу строки, а в строке
		// занимают два символа и один.
		string normalized = (shape.Text ?? string.Empty)
			.Replace("\r\n", "\n")
			.Replace('\r', '\n');

		if (!string.Equals(normalized, shape.Text, StringComparison.Ordinal))
			shape.Text = normalized;

		TextBefore = normalized;
		_lastSeen = normalized;
		Caret = normalized.Length;
		Anchor = Caret;
	}

	/// <summary>
	/// Замечает правку надписи мимо сеанса и подстраивается под неё.
	/// true — текст сменился со стороны.
	///
	/// Так бывает, когда ту же надпись правят в поле панели свойств,
	/// не выходя из набора на листе. Сеанс в этом случае не спорит:
	/// каретка поджимается к новой длине, а его собственная отмена
	/// очищается — её снимки относятся к тексту, которого больше нет.
	///
	/// Заодно сдвигается и точка отсчёта общего шага истории: панель
	/// свою правку уже записала, и записывать её второй раз, поверх,
	/// значило бы при отмене возвращать текст на два шага назад.
	/// </summary>
	public bool NoticeExternalEdit()
	{
		string current = Text;

		if (string.Equals(current, _lastSeen, StringComparison.Ordinal))
			return false;

		_lastSeen = current;
		TextBefore = current;

		Caret = Math.Clamp(Caret, 0, current.Length);
		Anchor = Math.Clamp(Anchor, 0, current.Length);

		_undo.Clear();
		_redo.Clear();

		return true;
	}

	#region Правка

	public void Insert(string chunk)
	{
		if (string.IsNullOrEmpty(chunk))
			return;

		// Перевод строки из буфера обмена приходит в любом виде.
		chunk = chunk.Replace("\r\n", "\n").Replace('\r', '\n');

		Remember();

		string text = Text;
		int from = SelectionStart;
		int to = SelectionEnd;

		Text = text[..from] + chunk + text[to..];

		Caret = from + chunk.Length;
		Anchor = Caret;
	}

	public void Backspace()
	{
		if (HasSelection)
		{
			DeleteSelection();
			return;
		}

		if (Caret <= 0)
			return;

		Remember();

		string text = Text;

		Text = text[..(Caret - 1)] + text[Caret..];
		Caret--;
		Anchor = Caret;
	}

	public void DeleteForward()
	{
		if (HasSelection)
		{
			DeleteSelection();
			return;
		}

		string text = Text;

		if (Caret >= text.Length)
			return;

		Remember();

		Text = text[..Caret] + text[(Caret + 1)..];
		Anchor = Caret;
	}

	public void DeleteSelection()
	{
		if (!HasSelection)
			return;

		Remember();

		string text = Text;
		int from = SelectionStart;
		int to = SelectionEnd;

		Text = text[..from] + text[to..];
		Caret = from;
		Anchor = Caret;
	}

	public string SelectedText()
		=> HasSelection ? Text[SelectionStart..SelectionEnd] : string.Empty;

	#endregion

	#region Своя отмена

	/// <summary>
	/// Отмена внутри сеанса.
	///
	/// Своя, а не общая: пока набирают текст, Ctrl+Z обязан убирать
	/// набранное, а не откатывать то, что делали с документом до входа
	/// в правку. В общую историю сеанс попадает одним шагом — при выходе.
	/// </summary>
	public bool Undo()
	{
		if (_undo.Count == 0)
			return false;

		_redo.Add(Current());

		Apply(_undo[^1]);
		_undo.RemoveAt(_undo.Count - 1);

		return true;
	}

	public bool Redo()
	{
		if (_redo.Count == 0)
			return false;

		_undo.Add(Current());

		Apply(_redo[^1]);
		_redo.RemoveAt(_redo.Count - 1);

		return true;
	}

	private Snapshot Current() => new(Text, Caret, Anchor);

	private void Apply(Snapshot state)
	{
		Text = state.Text;
		Caret = Math.Clamp(state.Caret, 0, Text.Length);
		Anchor = Math.Clamp(state.Anchor, 0, Text.Length);
	}

	private void Remember()
	{
		_undo.Add(Current());

		if (_undo.Count > UndoDepth)
			_undo.RemoveAt(0);

		// Любая новая правка обрывает ветку повтора: вернуться к ней
		// было бы уже некуда.
		_redo.Clear();
	}

	#endregion

	#region Перемещение каретки

	public void SetCaret(int index, bool extend)
	{
		_desiredColumn = -1;

		SetCaretKeepingColumn(index, extend);
	}

	private void SetCaretKeepingColumn(int index, bool extend)
	{
		Caret = Math.Clamp(index, 0, Text.Length);

		if (!extend)
			Anchor = Caret;
	}

	public void MoveHorizontal(int delta, bool extend, bool byWord)
	{
		// Стрелка при выделении и без него ведёт себя по-разному:
		// без Shift она не двигает каретку на символ, а снимает выделение,
		// ставя каретку к ближнему его краю. Так делают все редакторы,
		// и иначе край выделения терялся бы из виду.
		if (!extend && HasSelection && !byWord)
		{
			Caret = delta < 0 ? SelectionStart : SelectionEnd;
			Anchor = Caret;

			return;
		}

		int index = byWord
			? (delta < 0 ? WordStartBefore(Caret) : WordEndAfter(Caret))
			: Caret + delta;

		SetCaret(index, extend);
	}

	public void MoveVertical(int delta, bool extend)
	{
		var (line, column) = LineAndColumn(Caret);

		if (_desiredColumn < 0)
			_desiredColumn = column;

		var lines = Lines();
		int target = Math.Clamp(line + delta, 0, lines.Count - 1);

		if (target == line)
		{
			// Выше первой строки каретка идёт в начало текста, ниже
			// последней — в конец: привычнее, чем «ничего не произошло».
			SetCaret(delta < 0 ? 0 : Text.Length, extend);

			return;
		}

		int wanted = Math.Min(_desiredColumn, lines[target].Length);
		int keep = _desiredColumn;

		SetCaretKeepingColumn(LineStart(target) + wanted, extend);

		_desiredColumn = keep;
	}

	public void MoveToLineEdge(bool toStart, bool extend)
	{
		var (line, _) = LineAndColumn(Caret);

		SetCaret(toStart ? LineStart(line) : LineStart(line) + Lines()[line].Length, extend);
	}

	public void MoveToTextEdge(bool toStart, bool extend)
		=> SetCaret(toStart ? 0 : Text.Length, extend);

	public void SelectAll()
	{
		Anchor = 0;
		Caret = Text.Length;
	}

	/// <summary>Выделяет слово под указанным местом. Для двойного щелчка.</summary>
	public void SelectWordAt(int index)
	{
		string text = Text;

		if (text.Length == 0)
			return;

		index = Math.Clamp(index, 0, text.Length - 1);

		// Щелчок в правую половину последней буквы слова даёт границу
		// ЗА словом, то есть номер пробела. Без поправки двойной щелчок
		// по концу слова пропускал бы пробелы в обе стороны и забирал
		// два слова разом — а это самый частый способ попасть по слову.
		if (!IsWordChar(text[index]) && index > 0 && IsWordChar(text[index - 1]))
			index--;

		// Щелчок по пробелам выделяет сами пробелы, а не слова по обе
		// стороны от них: иначе двойной щелчок в промежутке между словами
		// забирал бы оба слова разом, чего никто не просил.
		if (IsSpace(text[index]))
		{
			int from = index;
			int to = index;

			while (from > 0 && IsSpace(text[from - 1]))
				from--;

			while (to < text.Length && IsSpace(text[to]))
				to++;

			Anchor = from;
			Caret = to;

			return;
		}

		Anchor = WordStartBefore(index + 1);
		Caret = WordEndAfter(index);
	}

	private int WordStartBefore(int index)
	{
		string text = Text;
		int i = Math.Clamp(index, 0, text.Length);

		while (i > 0 && IsSpace(text[i - 1]))
			i--;

		if (i > 0 && !IsWordChar(text[i - 1]))
			return i - 1;

		while (i > 0 && IsWordChar(text[i - 1]))
			i--;

		return i;
	}

	private int WordEndAfter(int index)
	{
		string text = Text;
		int i = Math.Clamp(index, 0, text.Length);

		while (i < text.Length && IsSpace(text[i]))
			i++;

		if (i < text.Length && !IsWordChar(text[i]))
			return i + 1;

		while (i < text.Length && IsWordChar(text[i]))
			i++;

		return i;
	}

	private static bool IsSpace(char c) => c is ' ' or '\t';

	private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

	#endregion

	#region Строки

	/// <summary>
	/// Строки текста. Считаются при каждом обращении, а не хранятся:
	/// текст правится на каждую нажатую клавишу, и запомненный список
	/// пришлось бы сбрасывать в десятке мест, забыв в одиннадцатом.
	/// </summary>
	public List<string> Lines()
	{
		var lines = new List<string>(Text.Split('\n'));

		if (lines.Count == 0)
			lines.Add(string.Empty);

		return lines;
	}

	public int LineStart(int line)
	{
		var lines = Lines();
		int index = 0;

		for (int i = 0; i < line && i < lines.Count; i++)
			index += lines[i].Length + 1;

		return index;
	}

	public (int Line, int Column) LineAndColumn(int index)
	{
		var lines = Lines();
		int remaining = Math.Clamp(index, 0, Text.Length);

		for (int i = 0; i < lines.Count; i++)
		{
			if (remaining <= lines[i].Length)
				return (i, remaining);

			remaining -= lines[i].Length + 1;
		}

		return (lines.Count - 1, lines[^1].Length);
	}

	#endregion
}
