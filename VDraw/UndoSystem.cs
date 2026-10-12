using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.History;

/// <summary>
/// Одно отменяемое действие пользователя.
///
/// Контракт: к моменту передачи в UndoStack.Push действие уже применено к
/// документу. Стек только откатывает и повторяет его, но не выполняет впервые.
/// </summary>
public interface IUndoableAction
{
	/// <summary>Название для подсказки на кнопках Undo/Redo.</summary>
	string Name { get; }

	/// <summary>
	/// Сколько байт удерживает действие. Для операций с фигурами это единицы
	/// килобайт, для снимков тайлов изображения — мегабайты. По этой величине
	/// стек вытесняет старые шаги.
	/// </summary>
	long MemoryFootprint { get; }

	void Undo();
	void Redo();

	/// <summary>
	/// Освободить удерживаемые ресурсы. Вызывается при вытеснении шага из истории
	/// и при очистке ветки Redo. После этого действие больше не используется.
	/// </summary>
	void Discard();

	/// <summary>
	/// Попытка слить следующее действие в это (перетаскивание ползунка,
	/// непрерывный мазок кистью). Возвращает true, если слияние состоялось —
	/// тогда следующее действие в стек не попадёт.
	/// </summary>
	bool TryMergeWith(IUndoableAction next);
}

/// <summary>Базовая реализация: без слияния, без ресурсов.</summary>
public abstract class UndoableActionBase : IUndoableAction
{
	public abstract string Name { get; }
	public virtual long MemoryFootprint => 512;

	public abstract void Undo();
	public abstract void Redo();

	public virtual void Discard()
	{
	}

	public virtual bool TryMergeWith(IUndoableAction next) => false;
}

/// <summary>
/// Несколько действий как один шаг истории. Удаление пяти фигур или
/// применение операции к группе выделения должно отменяться одним нажатием.
/// </summary>
public sealed class CompositeUndoableAction : IUndoableAction
{
	private readonly List<IUndoableAction> _actions;

	public string Name { get; }

	public CompositeUndoableAction(string name, IEnumerable<IUndoableAction> actions)
	{
		Name = name;
		_actions = actions.ToList();
	}

	public long MemoryFootprint
	{
		get
		{
			long total = 128;
			for (int i = 0; i < _actions.Count; i++)
				total += _actions[i].MemoryFootprint;

			return total;
		}
	}

	public void Undo()
	{
		// Откат в обратном порядке: последнее применённое отменяется первым.
		for (int i = _actions.Count - 1; i >= 0; i--)
			_actions[i].Undo();
	}

	public void Redo()
	{
		for (int i = 0; i < _actions.Count; i++)
			_actions[i].Redo();
	}

	public void Discard()
	{
		foreach (var a in _actions)
			a.Discard();

		_actions.Clear();
	}

	public bool TryMergeWith(IUndoableAction next) => false;

	public bool IsEmpty => _actions.Count == 0;
}

/// <summary>
/// История действий с вытеснением по объёму памяти.
///
/// Ограничение выбрано по суммарному размеру, а не по числу шагов: сто
/// перемещений фигуры стоят десятки килобайт, а десять мазков кистью по
/// 24-мегапиксельному снимку — сотни мегабайт. Считать их одной меркой нельзя.
///
/// Вытесняются самые старые шаги ветки Undo. Ветка Redo при новом действии
/// очищается целиком, её ресурсы освобождаются сразу.
/// </summary>
public sealed class UndoStack
{
	private readonly LinkedList<IUndoableAction> _undo = new();
	private readonly LinkedList<IUndoableAction> _redo = new();

	private long _undoBytes;
	private long _redoBytes;

	/// <summary>Бюджет памяти на всю историю. По умолчанию 256 МБ.</summary>
	public long MemoryBudgetBytes { get; set; } = 256L * 1024 * 1024;

	/// <summary>
	/// Жёсткий потолок на число шагов. Страховка от вырождения: миллион
	/// крошечных действий не должен превратить историю в узкое место.
	/// </summary>
	public int MaxStepsHardLimit { get; set; } = 10_000;

	/// <summary>Открытая группа: всё, что придёт до Commit, станет одним шагом.</summary>
	private List<IUndoableAction>? _openGroup;
	private string _openGroupName = string.Empty;

	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;

	public string? NextUndoName => _undo.Last?.Value.Name;
	public string? NextRedoName => _redo.Last?.Value.Name;

	public long UsedBytes => _undoBytes + _redoBytes;
	public int UndoCount => _undo.Count;
	public int RedoCount => _redo.Count;

	/// <summary>Поднимается после любого изменения состояния истории.</summary>
	public event Action? Changed;

	/// <summary>
	/// Пока true, Push игнорируется. Ставится на время самого Undo/Redo,
	/// чтобы откат не записался в историю как новое действие.
	/// </summary>
	public bool IsApplying { get; private set; }

	#region Группировка

	/// <summary>
	/// Начать группу. Вложенные вызовы допускаются и учитываются счётчиком,
	/// группа закрывается последним Commit.
	/// </summary>
	private int _groupDepth;

	public void BeginGroup(string name)
	{
		_groupDepth++;

		if (_openGroup == null)
		{
			_openGroup = new List<IUndoableAction>();
			_openGroupName = name;
		}
	}

	public void CommitGroup()
	{
		if (_groupDepth > 0)
			_groupDepth--;

		if (_groupDepth > 0 || _openGroup == null)
			return;

		var group = _openGroup;
		string name = _openGroupName;

		_openGroup = null;
		_openGroupName = string.Empty;

		if (group.Count == 0)
			return;

		if (group.Count == 1)
			PushCore(group[0]);
		else
			PushCore(new CompositeUndoableAction(name, group));
	}

	/// <summary>Отменить группировку, освободив уже накопленные действия.</summary>
	public void AbortGroup()
	{
		_groupDepth = 0;

		if (_openGroup == null)
			return;

		foreach (var a in _openGroup)
			a.Discard();

		_openGroup = null;
		_openGroupName = string.Empty;
	}

	#endregion

	/// <summary>
	/// Забирает последний шаг обратно из истории.
	///
	/// Нужен панели свойств: при нескольких выделенных фигурах правка
	/// основной успевает попасть в стек раньше, чем становится известно,
	/// что её надо повторить на остальных. Забрав шаг обратно, панель
	/// кладёт его в общую группу вместе с остальными — и всё изменение
	/// отменяется одним нажатием, а не по фигуре за раз.
	///
	/// Внутри открытой группы не работает: там шаги ещё не в стеке.
	/// </summary>
	/// <summary>
	/// Снимает последний шаг с ветки отмены и отдаёт вызывающему.
	///
	/// Учёт памяти правится здесь же. Прежде он не правился, и вес снятого
	/// шага оставался записанным за стеком: после возврата шага обратно
	/// он считался дважды, трижды — и бюджет выбирался тем быстрее, чем
	/// чаще пользователь правил свойства у нескольких выделенных фигур.
	/// Переполненный бюджет вытесняет самые старые шаги, и история
	/// укорачивалась сама по себе.
	/// </summary>
	public IUndoableAction? TakeLast()
	{
		if (_openGroup != null || _undo.Last == null)
			return null;

		var action = _undo.Last.Value;

		_undo.RemoveLast();
		_undoBytes -= action.MemoryFootprint;

		return action;
	}

	/// <summary>
	/// Снимает последний шаг, только если это ИМЕННО тот, что ожидается.
	///
	/// Нужно там, где код сначала выполняет правку, а потом забирает
	/// её шаг обратно, чтобы сложить в группу с другими. Беда в том, что
	/// запись шага могла не произойти: PushCore сливает его с предыдущим,
	/// если совпал ключ слияния. Тогда последним в ветке оказывается
	/// ЧУЖОЙ, более ранний шаг, и обычный TakeLast выдернул бы его
	/// из истории и переложил в чужую группу. Пользователь видит это как
	/// «отменяю правку новой фигуры, а меняется предыдущая».
	///
	/// Сверка по ссылке, а не по имени или содержимому: совпасть может
	/// и то, и другое, а экземпляр — нет.
	/// </summary>
	public IUndoableAction? TakeLastIf(IUndoableAction? expected)
	{
		if (expected == null || _openGroup != null || _undo.Last == null)
			return null;

		if (!ReferenceEquals(_undo.Last.Value, expected))
			return null;

		return TakeLast();
	}

	public void Push(IUndoableAction action)
	{
		if (action == null)
			return;

		if (IsApplying)
		{
			// Действие, порождённое самим откатом, в историю не идёт.
			action.Discard();
			return;
		}

		if (_openGroup != null)
		{
			_openGroup.Add(action);
			return;
		}

		PushCore(action);
	}

	private void PushCore(IUndoableAction action)
	{
		// Попытка слить с предыдущим шагом: непрерывное перетаскивание ползунка
		// не должно оставлять по шагу на каждый пиксель.
		var last = _undo.Last;
		if (last != null)
		{
			long before = last.Value.MemoryFootprint;

			if (last.Value.TryMergeWith(action))
			{
				_undoBytes += last.Value.MemoryFootprint - before;
				action.Discard();
				ClearRedo();
				EnforceBudget();
				Changed?.Invoke();
				return;
			}
		}

		ClearRedo();

		_undo.AddLast(action);
		_undoBytes += action.MemoryFootprint;

		EnforceBudget();
		Changed?.Invoke();
	}

	public bool Undo()
	{
		if (_undo.Last == null)
			return false;

		var node = _undo.Last;
		var action = node.Value;

		_undo.RemoveLast();
		_undoBytes -= action.MemoryFootprint;

		IsApplying = true;
		try
		{
			action.Undo();
		}
		finally
		{
			IsApplying = false;
		}

		_redo.AddLast(action);
		_redoBytes += action.MemoryFootprint;

		EnforceBudget();
		Changed?.Invoke();
		return true;
	}

	public bool Redo()
	{
		if (_redo.Last == null)
			return false;

		var action = _redo.Last.Value;

		_redo.RemoveLast();
		_redoBytes -= action.MemoryFootprint;

		IsApplying = true;
		try
		{
			action.Redo();
		}
		finally
		{
			IsApplying = false;
		}

		_undo.AddLast(action);
		_undoBytes += action.MemoryFootprint;

		EnforceBudget();
		Changed?.Invoke();
		return true;
	}

	public void Clear()
	{
		AbortGroup();

		foreach (var a in _undo)
			a.Discard();

		foreach (var a in _redo)
			a.Discard();

		_undo.Clear();
		_redo.Clear();
		_undoBytes = 0;
		_redoBytes = 0;

		Changed?.Invoke();
	}

	private void ClearRedo()
	{
		if (_redo.Count == 0)
			return;

		foreach (var a in _redo)
			a.Discard();

		_redo.Clear();
		_redoBytes = 0;
	}

	/// <summary>
	/// Вытесняет самые старые шаги, пока история не уложится в бюджет.
	/// Ветка Redo при этом не трогается: она уже урезана до нуля любым новым
	/// действием, а во время серии Undo её содержимое нужно пользователю.
	/// </summary>
	private void EnforceBudget()
	{
		while (_undo.First != null &&
			   (UsedBytes > MemoryBudgetBytes || _undo.Count > MaxStepsHardLimit))
		{
			var oldest = _undo.First.Value;

			_undo.RemoveFirst();
			_undoBytes -= oldest.MemoryFootprint;
			oldest.Discard();

			// Один шаг может быть тяжелее всего бюджета (снимок огромного
			// изображения). В этом случае история опустеет — это лучше, чем
			// бесконечный рост потребления памяти.
			if (_undo.Count == 0)
				break;
		}
	}
}

/// <summary>Подсчёт занимаемой памяти для типовых объектов.</summary>
public static class UndoMemory
{
	public static long Of(SKBitmap? bitmap)
		=> bitmap == null ? 0 : (long)bitmap.ByteCount;

	public static long Of(SKPath? path)
		=> path == null ? 0 : 64 + path.PointCount * 8L;

	public static long OfShape(Shape? shape)
	{
		if (shape == null)
			return 0;

		// Геометрия фигуры целиком — единицы килобайт. Изображение внутри
		// ImageShape считаем отдельно, оно доминирует.
		long total = 1024;

		if (shape is ImageShape img)
		{
			total += Of(img.Bitmap);
			total += Of(img.OriginalBitmap);
		}

		if (shape is CurveShape curve)
			total += curve.Nodes.Count * 96L;

		return total;
	}
}
