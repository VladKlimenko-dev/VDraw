using VDraw.Documents;

namespace VDraw.History;

/// <summary>
/// Добавление страницы.
///
/// Хранит саму страницу, а не её описание: отменённая страница из документа
/// вынута, но никуда не делась, и повтор возвращает тот же объект со всем
/// содержимым. Восстанавливать её из слепка значило бы терять всё, что
/// на ней нарисовали между отменой и повтором.
/// </summary>
public sealed class AddPageAction : UndoableActionBase
{
	private readonly Document _doc;
	private readonly Page _page;
	private readonly int _index;
	private readonly Action? _refreshUi;

	public override string Name => "Add page";

	public AddPageAction(Document doc, Page page, int index, Action? refreshUi = null)
	{
		_doc = doc;
		_page = page;
		_index = index;
		_refreshUi = refreshUi;
	}

	/// <summary>
	/// Страница держит фигуры, а те — растровые изображения. Отражать это
	/// в оценке памяти важнее, чем кажется: стек истории ограничен
	/// по объёму, и шаг, весящий по учёту полкилобайта, а на деле
	/// десятки мегабайт, вытеснил бы из него все остальные.
	/// </summary>
	public override long MemoryFootprint => 4096;

	public override void Undo()
	{
		_doc.RemovePage(_page);
		_refreshUi?.Invoke();
	}

	public override void Redo()
	{
		_doc.InsertPage(_index, _page);
		_doc.ActivePage = _page;
		_refreshUi?.Invoke();
	}
}

/// <summary>
/// Удаление страницы.
///
/// Зеркально добавлению. Индекс запоминается, чтобы возврат ставил
/// страницу на прежнее место, а не в конец: порядок вкладок — часть
/// документа, и восстановленная не там страница выглядела бы как вторая
/// ошибка вдогонку первой.
/// </summary>
public sealed class RemovePageAction : UndoableActionBase
{
	private readonly Document _doc;
	private readonly Page _page;
	private readonly int _index;
	private readonly bool _wasActive;
	private readonly Action? _refreshUi;

	public override string Name => "Delete page";

	public RemovePageAction(Document doc, Page page, int index, bool wasActive, Action? refreshUi = null)
	{
		_doc = doc;
		_page = page;
		_index = index;
		_wasActive = wasActive;
		_refreshUi = refreshUi;
	}

	public override long MemoryFootprint => 4096;

	public override void Undo()
	{
		_doc.InsertPage(_index, _page);

		// Возвращённая страница открывается, только если была открыта
		// в момент удаления. Иначе отмена уводила бы пользователя
		// с того листа, где он работает.
		if (_wasActive)
			_doc.ActivePage = _page;

		_refreshUi?.Invoke();
	}

	public override void Redo()
	{
		_doc.RemovePage(_page);
		_refreshUi?.Invoke();
	}
}

/// <summary>
/// Переименование страницы.
///
/// Слияние подряд идущих переименований одной страницы: правка названия
/// идёт в поле ввода, и без слияния каждое нажатие клавиши оставляло бы
/// свой шаг.
/// </summary>
public sealed class RenamePageAction : UndoableActionBase
{
	private readonly Page _page;
	private readonly string _before;
	private string _after;
	private readonly Action? _refreshUi;

	public override string Name => "Rename page";

	public RenamePageAction(Page page, string before, string after, Action? refreshUi = null)
	{
		_page = page;
		_before = before;
		_after = after;
		_refreshUi = refreshUi;
	}

	public override void Undo()
	{
		_page.title = _before;
		_refreshUi?.Invoke();
	}

	public override void Redo()
	{
		_page.title = _after;
		_refreshUi?.Invoke();
	}

	public override bool TryMergeWith(IUndoableAction next)
	{
		if (next is not RenamePageAction other || !ReferenceEquals(_page, other._page))
			return false;

		_after = other._after;
		return true;
	}
}

/// <summary>
/// Перестановка страницы в порядке вкладок.
///
/// Слияние обязательно: порядок меняется прямо во время протяжки вкладки,
/// на каждом пересечении соседа, и один перенос через три позиции без
/// слияния оставил бы три шага. Склеенный шаг ведёт от исходного места
/// к конечному.
/// </summary>
public sealed class MovePageAction : UndoableActionBase
{
	private readonly Document _doc;
	private readonly Page _page;
	private readonly int _from;
	private int _to;
	private readonly Action? _refreshUi;

	public override string Name => "Reorder pages";

	public MovePageAction(Document doc, Page page, int from, int to, Action? refreshUi = null)
	{
		_doc = doc;
		_page = page;
		_from = from;
		_to = to;
		_refreshUi = refreshUi;
	}

	public override void Undo() => MoveTo(_from);

	public override void Redo() => MoveTo(_to);

	private void MoveTo(int index)
	{
		int current = _doc.pages.IndexOf(_page);

		if (current < 0)
			return;

		_doc.pages.RemoveAt(current);
		_doc.pages.Insert(Math.Clamp(index, 0, _doc.pages.Count), _page);

		_refreshUi?.Invoke();
	}

	public override bool TryMergeWith(IUndoableAction next)
	{
		if (next is not MovePageAction other || !ReferenceEquals(_page, other._page))
			return false;

		_to = other._to;
		return true;
	}

	/// <summary>Перенос на прежнее место изменением не считается.</summary>
	public bool IsNoOp => _from == _to;
}
