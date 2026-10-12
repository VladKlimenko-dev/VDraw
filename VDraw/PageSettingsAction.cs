using SkiaSharp;

using VDraw.Documents;

namespace VDraw.History;

/// <summary>
/// Изменение настроек листа: размер, ориентация, единицы, начало координат,
/// сетка, поля печати, цвет фона.
///
/// Снимком целиком, а не по одному полю. Настройки переплетены: смена
/// ширины подбирает формат бумаги и вместе с ним ориентацию, смена единиц
/// пересчитывает размер. Разложить это на независимые шаги значило бы
/// откатывать половину изменения — и получать лист, которого никогда
/// не существовало.
///
/// Геометрия фигур сюда не входит намеренно. Единственная настройка,
/// которая её трогает, — единицы измерения, и пересчёт там чистое
/// умножение на отношение: вернув прежние единицы тем же путём, каким
/// их меняли, получаем и прежнюю геометрию. Хранить ради этого снимок
/// всех фигур было бы расточительством на ровном месте.
/// </summary>
public sealed class PageSettingsAction : UndoableActionBase
{
	private readonly Page _page;
	private readonly PageSettingsSnapshot _before;
	private readonly Action? _refreshUi;
	private PageSettingsSnapshot _after;

	private readonly string _name;

	/// <summary>
	/// Имя шага. Свойством, а не через конструктор основы: у
	/// UndoableActionBase конструктора с именем нет, Name объявлено
	/// абстрактным и переопределяется наследником.
	/// </summary>
	public override string Name => _name;

	private PageSettingsAction(string name, Page page, PageSettingsSnapshot before, Action? refreshUi)
	{
		_name = name;
		_page = page;
		_before = before;
		_after = before;
		_refreshUi = refreshUi;
	}

	/// <summary>
	/// Снимает состояние ДО изменения.
	///
	/// refreshUi вызывается после отката и повтора. Без него поля панели
	/// листа остались бы показывать откаченные значения: они заполняются
	/// из страницы один раз и сами за ней не следят.
	/// </summary>
	public static PageSettingsAction Capture(string name, Page page, Action? refreshUi = null)
		=> new(name, page, PageSettingsSnapshot.From(page), refreshUi);

	/// <summary>Снимает состояние ПОСЛЕ. Вызывать перед помещением в историю.</summary>
	public void CaptureAfter()
	{
		_after = PageSettingsSnapshot.From(_page);
	}

	public bool HasChanges() => !_before.Equals(_after);

	public override void Undo() => Apply(_before);

	public override void Redo() => Apply(_after);

	private void Apply(PageSettingsSnapshot snapshot)
	{
		snapshot.ApplyTo(_page);
		_refreshUi?.Invoke();
	}

	/// <summary>
	/// Сливает подряд идущие изменения одной и той же настройки.
	///
	/// Поле ввода поднимает событие на каждое нажатие, и набранное
	/// «210» оставило бы в истории три шага: «2», «21», «210». Слияние
	/// оставляет один — от того, что было до набора, к тому, что вышло.
	///
	/// Условие на совпадение имени, а не только страницы: менять ширину,
	/// потом цвет фона, потом снова ширину — три разных намерения,
	/// и склеивать их в одно неправильно.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (next is not PageSettingsAction other)
			return false;

		if (!ReferenceEquals(_page, other._page) || Name != other.Name)
			return false;

		_after = other._after;
		return true;
	}
}

/// <summary>
/// Слепок настроек листа. Структура, а не класс: сравнивается по значению,
/// и сравнение полей нужно здесь ради выяснения «а было ли изменение».
/// </summary>
public readonly struct PageSettingsSnapshot : IEquatable<PageSettingsSnapshot>
{
	private readonly PaperFormat? _paperFormat;
	private readonly UoM? _uom;
	private readonly SKSize _sizeUom;
	private readonly int _dpi;
	private readonly PageOrientation _orientation;
	private readonly bool _lockRatio;
	private readonly float _lockedRatio;
	private readonly bool _printBorders;
	private readonly bool _grid;
	private readonly SKPoint _origin;
	private readonly SKColor _background;

	private PageSettingsSnapshot(Page page)
	{
		_paperFormat = page.paper_format;
		_uom = page.uom;
		_sizeUom = page.size_uom;
		_dpi = page.DPI;
		_orientation = page.Orientation;
		_lockRatio = page.LockRatio;
		_lockedRatio = page.locked_ratio;
		_printBorders = page.PrintBordersEnabled;
		_grid = page.GridEnabled;
		_origin = page.page_origin;
		_background = page.page_bg_color;
	}

	public static PageSettingsSnapshot From(Page page) => new(page);

	public void ApplyTo(Page page)
	{
		// Единицы возвращаются тем же путём, каким менялись: SetShapesNewUoM
		// пересчитывает геометрию на отношение старых к новым, и обратное
		// отношение возвращает её на место. Присвоить uom напрямую нельзя —
		// фигуры остались бы в прежнем масштабе.
		//
		// Порядок важен: пересчёт берёт DPI страницы, поэтому делается
		// до того, как снимок вернёт прежнее разрешение.
		var currentUom = page.uom;

		if (currentUom != null && _uom != null && currentUom.title != _uom.title)
			page.SetShapesNewUoM(currentUom, _uom);

		page.uom = _uom;
		page.paper_format = _paperFormat;
		page.size_uom = _sizeUom;
		page.DPI = _dpi;
		page.Orientation = _orientation;
		page.LockRatio = _lockRatio;
		page.locked_ratio = _lockedRatio;
		page.PrintBordersEnabled = _printBorders;
		page.GridEnabled = _grid;
		page.page_origin = _origin;
		page.page_bg_color = _background;
	}

	public bool Equals(PageSettingsSnapshot other)
	{
		return Equals(_paperFormat, other._paperFormat)
			&& Equals(_uom, other._uom)
			&& _sizeUom == other._sizeUom
			&& _dpi == other._dpi
			&& _orientation == other._orientation
			&& _lockRatio == other._lockRatio
			&& _lockedRatio.Equals(other._lockedRatio)
			&& _printBorders == other._printBorders
			&& _grid == other._grid
			&& _origin == other._origin
			&& _background == other._background;
	}

	public override bool Equals(object? obj) => obj is PageSettingsSnapshot other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(_sizeUom, _dpi, _orientation, _origin, _background);
}
