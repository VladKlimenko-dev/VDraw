using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.History;

// =====================================================================================
// ФИГУРЫ
// =====================================================================================

/// <summary>Добавление фигур на страницу.</summary>
public sealed class AddShapesAction : UndoableActionBase
{
	private readonly Page _page;
	private readonly List<Shape> _shapes;

	public AddShapesAction(Page page, IEnumerable<Shape> shapes)
	{
		_page = page;
		_shapes = shapes.ToList();
	}

	public override string Name => _shapes.Count == 1 ? "Add shape" : $"Add {_shapes.Count} shapes";

	public override long MemoryFootprint
	{
		get
		{
			long total = 256;
			foreach (var s in _shapes)
				total += UndoMemory.OfShape(s);

			return total;
		}
	}

	public override void Undo()
	{
		foreach (var s in _shapes)
			_page.RemoveShape(s);
	}

	public override void Redo()
	{
		foreach (var s in _shapes)
			_page.AddShape(s);
	}
}

/// <summary>
/// Удаление фигур. Запоминает позицию в списке, чтобы Redo не менял порядок
/// перекрытия внутри одного слоя.
/// </summary>
public sealed class RemoveShapesAction : UndoableActionBase
{
	/// <summary>
	/// Откуда убрана фигура.
	///
	/// Владелец запоминается вместе с позицией, потому что удалять можно
	/// не только фигуры страницы: у вложенной в группу владельцем будет
	/// группа. Прежде такие фигуры отбрасывались проверкой «индекс на
	/// странице неотрицателен», и шаг истории оказывался пустым — отмена
	/// после удаления не возвращала ничего.
	/// </summary>
	private readonly record struct Entry(Shape Shape, GroupShape? Owner, int Index);

	private readonly Page _page;
	private readonly List<Entry> _removed;

	/// <summary>Вызывать ДО удаления: конструктор фиксирует индексы.</summary>
	public static RemoveShapesAction Capture(Page page, IEnumerable<Shape> shapes)
	{
		var list = new List<Entry>();

		foreach (var shape in shapes)
		{
			if (shape == null)
				continue;

			if (shape.Parent is GroupShape owner)
			{
				int nested = owner.Children.IndexOf(shape);

				if (nested >= 0)
					list.Add(new Entry(shape, owner, nested));

				continue;
			}

			int index = page.Shapes.IndexOf(shape);

			if (index >= 0)
				list.Add(new Entry(shape, null, index));
		}

		// По возрастанию индекса: тогда при откате каждая фигура попадает
		// на своё место, а не сдвигает следующие.
		list.Sort((a, b) => a.Index.CompareTo(b.Index));

		return new RemoveShapesAction(page, list);
	}

	private RemoveShapesAction(Page page, List<Entry> removed)
	{
		_page = page;
		_removed = removed;
	}

	public override string Name => _removed.Count == 1 ? "Delete shape" : $"Delete {_removed.Count} shapes";

	public override long MemoryFootprint
	{
		get
		{
			long total = 256;
			foreach (var entry in _removed)
				total += UndoMemory.OfShape(entry.Shape);

			return total;
		}
	}

	public override void Undo()
	{
		foreach (var entry in _removed)
		{
			if (entry.Owner != null)
			{
				if (entry.Owner.Children.Contains(entry.Shape))
					continue;

				int at = Math.Clamp(entry.Index, 0, entry.Owner.Children.Count);

				entry.Owner.Children.Insert(at, entry.Shape);
				entry.Shape.SetParent(entry.Owner);

				entry.Owner.SortChildrenByLayer();
				continue;
			}

			if (_page.Shapes.Contains(entry.Shape))
				continue;

			int index = Math.Clamp(entry.Index, 0, _page.Shapes.Count);

			_page.Shapes.Insert(index, entry.Shape);
			entry.Shape.SetParent(null);
		}

		_page.SortLayers();
	}

	public override void Redo()
	{
		foreach (var entry in _removed)
			_page.RemoveShape(entry.Shape);
	}

	public override void Discard()
	{
		// Вытесненное удаление означает, что фигуры уже не вернуть —
		// освобождаем их растровые ресурсы.
		foreach (var entry in _removed)
		{
			if (entry.Shape is ImageShape img)
				img.DisposeImageResources();
		}

		_removed.Clear();
	}
}

/// <summary>
/// Изменение трансформации выделения: перемещение, масштаб, поворот, отражение.
/// Хранит только матрицы до и после — десятки байт на фигуру.
/// </summary>
public sealed class TransformShapesAction : UndoableActionBase
{
	private readonly struct Entry
	{
		public readonly Shape Shape;
		public readonly SKMatrix Before;
		public SKMatrix After { get; init; }
		public readonly SKPoint FillStartBefore;
		public readonly SKPoint FillEndBefore;

		/// <summary>
		/// Собственная геометрия фигуры: радиус окружности, полуоси эллипса,
		/// вершины многоугольника, скругление прямоугольника, точки размера.
		/// Ручки точек и ручки свойств правят именно её, а не матрицу.
		/// </summary>
		public readonly IGeometrySnapshot? GeometryBefore;
		public IGeometrySnapshot? GeometryAfter { get; init; }

		public Entry(Shape shape)
		{
			Shape = shape;
			Before = shape.LocalTransform;
			After = shape.LocalTransform;
			FillStartBefore = shape.Fill?.StartPoint ?? SKPoint.Empty;
			FillEndBefore = shape.Fill?.EndPoint ?? SKPoint.Empty;
			GeometryBefore = shape.CaptureGeometrySnapshot();
			GeometryAfter = GeometryBefore;
		}
	}

	private readonly List<Entry> _entries;
	private readonly List<(SKPoint Start, SKPoint End)> _fillAfter = new();
	private readonly string _name;
	private readonly object? _mergeKey;

	/// <summary>
	/// Вызывать ДО изменения.
	///
	/// mergeKey — ключ склеивания. По умолчанию null: каждое
	/// перетаскивание — свой шаг истории.
	/// </summary>
	public static TransformShapesAction Capture(string name, IEnumerable<Shape> shapes, object? mergeKey = null)
		=> new TransformShapesAction(name, shapes.Select(s => new Entry(s)).ToList(), mergeKey);

	private TransformShapesAction(string name, List<Entry> entries, object? mergeKey)
	{
		_name = name;
		_entries = entries;
		_mergeKey = mergeKey;
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push в стек.</summary>
	public void CaptureAfter()
	{
		_fillAfter.Clear();

		for (int i = 0; i < _entries.Count; i++)
		{
			var e = _entries[i];

			_entries[i] = e with
			{
				After = e.Shape.LocalTransform,
				GeometryAfter = e.Shape.CaptureGeometrySnapshot()
			};

			_fillAfter.Add((
				e.Shape.Fill?.StartPoint ?? SKPoint.Empty,
				e.Shape.Fill?.EndPoint ?? SKPoint.Empty));
		}
	}

	/// <summary>Изменилось ли хоть что-то. Если нет — в историю писать нечего.</summary>
	public bool HasChanges()
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			var e = _entries[i];

			if (!MatrixEquals(e.Before, e.After))
				return true;

			// Ручка радиуса или вершины матрицу не трогает. Без этой проверки
			// шаг не записывался бы вовсе и такое перетаскивание нельзя было
			// бы отменить.
			if (e.GeometryBefore != null && !e.GeometryBefore.SameAs(e.GeometryAfter))
				return true;
		}

		return false;
	}

	public override string Name => _name;

	public override long MemoryFootprint => 256 + _entries.Count * 128L;

	public override void Undo()
	{
		foreach (var e in _entries)
		{
			e.Shape.LocalTransform = e.Before;
			e.Shape.RestoreGeometrySnapshot(e.GeometryBefore);

			if (e.Shape.Fill != null)
			{
				e.Shape.Fill.StartPoint = e.FillStartBefore;
				e.Shape.Fill.EndPoint = e.FillEndBefore;
			}
		}
	}

	public override void Redo()
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			var e = _entries[i];
			e.Shape.LocalTransform = e.After;
			e.Shape.RestoreGeometrySnapshot(e.GeometryAfter);

			if (e.Shape.Fill != null && i < _fillAfter.Count)
			{
				e.Shape.Fill.StartPoint = _fillAfter[i].Start;
				e.Shape.Fill.EndPoint = _fillAfter[i].End;
			}
		}
	}

	/// <summary>
	/// Слияние только по явному ключу.
	///
	/// Перетаскивание мышью в ключе не нуждается: шаг пишется один
	/// раз, в EndOperation, а не на каждое событие движения. Безусловное
	/// слияние же склеивало два РАЗНЫХ перетаскивания одной фигуры:
	/// отмена возвращала её сразу в исходное место, мимо промежуточного.
	///
	/// Ключ остаётся для сдвига стрелками: там каждое нажатие даёт
	/// отдельную запись, и без склеивания история забилась бы микрошагами.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (_mergeKey == null)
			return false;

		if (next is not TransformShapesAction other)
			return false;

		if (!Equals(_mergeKey, other._mergeKey) || other._entries.Count != _entries.Count)
			return false;

		for (int i = 0; i < _entries.Count; i++)
		{
			if (!ReferenceEquals(_entries[i].Shape, other._entries[i].Shape))
				return false;
		}

		// Берём конечное состояние из нового действия, начальное оставляем своё.
		for (int i = 0; i < _entries.Count; i++)
		{
			_entries[i] = _entries[i] with
			{
				After = other._entries[i].After,
				GeometryAfter = other._entries[i].GeometryAfter
			};
		}

		_fillAfter.Clear();
		_fillAfter.AddRange(other._fillAfter);

		return true;
	}

	private static bool MatrixEquals(SKMatrix a, SKMatrix b)
	{
		const float eps = 1e-6f;

		return MathF.Abs(a.ScaleX - b.ScaleX) < eps &&
			   MathF.Abs(a.ScaleY - b.ScaleY) < eps &&
			   MathF.Abs(a.SkewX - b.SkewX) < eps &&
			   MathF.Abs(a.SkewY - b.SkewY) < eps &&
			   MathF.Abs(a.TransX - b.TransX) < eps &&
			   MathF.Abs(a.TransY - b.TransY) < eps;
	}
}

/// <summary>
/// Универсальное изменение свойства через геттер и сеттер. Закрывает всю
/// панель свойств: цвет, толщина обводки, тип заливки, параметры паттерна,
/// текст, шрифт. Значение хранится по значению, поэтому подходит для
/// структур и строк, но не для изменяемых ссылочных объектов.
/// </summary>
public sealed class PropertyChangeAction<T> : UndoableActionBase
{
	private readonly Action<T> _setter;
	private readonly T _before;
	private T _after;
	private readonly string _name;
	private readonly object? _mergeKey;

	public PropertyChangeAction(string name, T before, T after, Action<T> setter, object? mergeKey = null)
	{
		_name = name;
		_before = before;
		_after = after;
		_setter = setter;
		_mergeKey = mergeKey;
	}

	public override string Name => _name;
	public override long MemoryFootprint => 256;

	public override void Undo() => _setter(_before);
	public override void Redo() => _setter(_after);

	/// <summary>
	/// Слияние по ключу: перетаскивание одного ползунка — один шаг истории.
	/// В качестве ключа удобно передавать сам объект свойства плюс имя.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (next is not PropertyChangeAction<T> other)
			return false;

		if (_mergeKey == null || !Equals(_mergeKey, other._mergeKey))
			return false;

		_after = other._after;
		return true;
	}
}

// =====================================================================================
// ВЫДЕЛЕНИЕ ФИГУР
// =====================================================================================

/// <summary>
/// Изменение оформления фигур: обводки и заливки.
///
/// Хранятся клоны стилей до и после. Ссылками нельзя: панель свойств и
/// вставка формата правят объекты стиля на месте, и снимок по ссылке
/// менялся бы вместе с оригиналом.
/// </summary>
public sealed class ShapeStyleAction : UndoableActionBase
{
	private readonly struct Entry
	{
		public readonly Shape Shape;
		public readonly StrokeStyle StrokeBefore;
		public readonly FillStyle FillBefore;

		public StrokeStyle StrokeAfter { get; init; }
		public FillStyle FillAfter { get; init; }

		public Entry(Shape shape)
		{
			Shape = shape;
			StrokeBefore = shape.Stroke.Clone();
			FillBefore = shape.Fill.Clone();
			StrokeAfter = StrokeBefore;
			FillAfter = FillBefore;
		}
	}

	private readonly List<Entry> _entries;
	private readonly string _name;

	public override string Name => _name;

	/// <summary>Вызывать ДО изменения стилей.</summary>
	public static ShapeStyleAction Capture(string name, IEnumerable<Shape> shapes)
		=> new ShapeStyleAction(name, shapes.Select(s => new Entry(s)).ToList());

	private ShapeStyleAction(string name, List<Entry> entries)
	{
		_name = name;
		_entries = entries;
	}

	/// <summary>Вызывать ПОСЛЕ изменения стилей.</summary>
	public void CaptureAfter()
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			var e = _entries[i];

			_entries[i] = e with
			{
				StrokeAfter = e.Shape.Stroke.Clone(),
				FillAfter = e.Shape.Fill.Clone()
			};
		}
	}

	public bool HasChanges()
	{
		foreach (var e in _entries)
		{
			if (!SameStroke(e.StrokeBefore, e.StrokeAfter) || !SameFill(e.FillBefore, e.FillAfter))
				return true;
		}

		return false;
	}

	public override void Undo()
	{
		foreach (var e in _entries)
		{
			e.Shape.Stroke = e.StrokeBefore.Clone();
			e.Shape.Fill = e.FillBefore.Clone();
		}
	}

	public override void Redo()
	{
		foreach (var e in _entries)
		{
			e.Shape.Stroke = e.StrokeAfter.Clone();
			e.Shape.Fill = e.FillAfter.Clone();
		}
	}

	/// <summary>
	/// Сравнение по значению. Полное сравнение всех полей заливки было бы
	/// длинным и хрупким, поэтому сверяются те, что задают внешний вид;
	/// для текстуры — ссылка на растр, копировать его ради сравнения незачем.
	/// </summary>
	private static bool SameFill(FillStyle a, FillStyle b)
	{
		if (a.Kind != b.Kind
			|| a.Enabled != b.Enabled
			|| a.SolidColor != b.SolidColor
			|| a.Tiling != b.Tiling
			|| a.RotateWithShape != b.RotateWithShape
			|| a.ScaleWithShape != b.ScaleWithShape
			|| a.GradientColor1 != b.GradientColor1
			|| a.GradientColor2 != b.GradientColor2
			|| a.GradientMode != b.GradientMode
			|| a.Angle != b.Angle
			|| a.PatternFrontColor != b.PatternFrontColor
			|| a.PatternBackColor != b.PatternBackColor
			|| a.PatternWidth != b.PatternWidth
			|| a.PatternHeight != b.PatternHeight
			|| a.PatternId != b.PatternId
			|| a.PatternDefinition != b.PatternDefinition
			|| a.TextureId != b.TextureId
			|| a.TextureFilePath != b.TextureFilePath
			|| a.StartPoint != b.StartPoint
			|| a.EndPoint != b.EndPoint
			|| a.SmoothTexture != b.SmoothTexture
			|| a.Stops.Count != b.Stops.Count)
		{
			return false;
		}

		for (int i = 0; i < a.Stops.Count; i++)
		{
			if (a.Stops[i].Offset != b.Stops[i].Offset || a.Stops[i].Color != b.Stops[i].Color)
				return false;
		}

		return true;
	}

	private static bool SameStroke(StrokeStyle a, StrokeStyle b)
		=> a.Enabled == b.Enabled
		&& a.Color == b.Color
		&& a.Thickness == b.Thickness
		&& a.Pattern == b.Pattern
		&& a.PatternLength == b.PatternLength
		&& a.StartGlyph == b.StartGlyph
		&& a.EndGlyph == b.EndGlyph
		&& a.StartGlyphSize == b.StartGlyphSize
		&& a.EndGlyphSize == b.EndGlyphSize;
}

/// <summary>
/// Изменение выделения фигур. Пользователь ожидает, что Undo вернёт и то,
/// что было выделено, а не только геометрию.
/// </summary>
public sealed class SelectionChangeAction : UndoableActionBase
{
	private readonly EditorInteractionContext _ctx;
	private readonly List<Shape> _before;
	private List<Shape> _after = new();
	private readonly GuideLine? _guideBefore;
	private GuideLine? _guideAfter;
	private readonly Action? _notify;

	/// <summary>Вызывать ДО изменения выделения.</summary>
	public static SelectionChangeAction Capture(EditorInteractionContext ctx, Action? notify = null)
		=> new SelectionChangeAction(ctx, notify);

	private SelectionChangeAction(EditorInteractionContext ctx, Action? notify)
	{
		_ctx = ctx;
		_before = ctx.Selection.ToList();
		_guideBefore = ctx.SelectedGuide;
		_notify = notify;
	}

	/// <summary>Вызывать ПОСЛЕ изменения выделения, перед Push.</summary>
	public void CaptureAfter()
	{
		_after = _ctx.Selection.ToList();
		_guideAfter = _ctx.SelectedGuide;
	}

	public bool HasChanges()
	{
		if (!ReferenceEquals(_guideBefore, _guideAfter))
			return true;

		if (_before.Count != _after.Count)
			return true;

		for (int i = 0; i < _before.Count; i++)
		{
			if (!ReferenceEquals(_before[i], _after[i]))
				return true;
		}

		return false;
	}

	public override string Name => "Change selection";

	// Ссылки на фигуры, сами фигуры живут на странице.
	public override long MemoryFootprint => 128 + (_before.Count + _after.Count) * 8L;

	public override void Undo() => ApplySelection(_before, _guideBefore);
	public override void Redo() => ApplySelection(_after, _guideAfter);

	private void ApplySelection(List<Shape> shapes, GuideLine? guide)
	{
		foreach (var s in _ctx.Selection)
			s.IsSelected = false;

		_ctx.Selection.Clear();

		foreach (var s in shapes)
		{
			// Фигура могла быть удалена другим шагом истории.
			s.IsSelected = true;
			_ctx.Selection.Add(s);
		}

		if (_ctx.SelectedGuide != null)
			_ctx.SelectedGuide.IsSelected = false;

		_ctx.SelectedGuide = guide;

		if (guide != null)
			guide.IsSelected = true;

		_notify?.Invoke();
	}
}

// =====================================================================================
// УЗЛЫ КРИВОЙ
// =====================================================================================

/// <summary>
/// Изменение набора узлов кривой: добавление, удаление, слияние, разбиение,
/// смена типа узла, перетаскивание узла или контрольной точки.
/// Хранит полные копии списка узлов — они лёгкие, около сотни байт на узел.
/// </summary>
public sealed class CurveNodesAction : UndoableActionBase
{
	private readonly CurveShape _curve;
	private readonly List<CurveNode> _before;
	private List<CurveNode> _after = new();
	private readonly string _name;
	private readonly object? _mergeKey;

	/// <summary>
	/// Вызывать ДО изменения узлов.
	///
	/// mergeKey — ключ склеивания соседних шагов. По умолчанию null,
	/// то есть каждое действие становится отдельным шагом истории.
	/// Ключ нужен только там, где одно действие пользователя порождает
	/// поток записей — например удержание клавиши.
	/// </summary>
	public static CurveNodesAction Capture(string name, CurveShape curve, object? mergeKey = null)
		=> new CurveNodesAction(name, curve, mergeKey);

	private CurveNodesAction(string name, CurveShape curve, object? mergeKey)
	{
		_name = name;
		_curve = curve;
		_mergeKey = mergeKey;
		_before = curve.Nodes.Select(n => n.Clone()).ToList();
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push.</summary>
	public void CaptureAfter()
		=> _after = _curve.Nodes.Select(n => n.Clone()).ToList();

	public bool HasChanges()
	{
		if (_before.Count != _after.Count)
			return true;

		for (int i = 0; i < _before.Count; i++)
		{
			var a = _before[i];
			var b = _after[i];

			if (a.Position != b.Position ||
				a.Type != b.Type ||
				a.ControlPrev != b.ControlPrev ||
				a.ControlNext != b.ControlNext ||
				a.BreakAfter != b.BreakAfter ||
				a.NewContour != b.NewContour ||
				a.CloseAfter != b.CloseAfter)
			{
				return true;
			}
		}

		return false;
	}

	public override string Name => _name;

	public override long MemoryFootprint => 256 + (_before.Count + _after.Count) * 96L;

	public override void Undo() => Restore(_before);
	public override void Redo() => Restore(_after);

	private void Restore(List<CurveNode> snapshot)
	{
		_curve.Nodes.Clear();

		foreach (var n in snapshot)
			_curve.Nodes.Add(n.Clone());
	}

	/// <summary>
	/// Слияние только по явному ключу.
	///
	/// Прежняя версия склеивала любые два соседних действия с одной
	/// кривой и одинаковым именем. Внутри одного перетаскивания это ничего
	/// не давало: OperationUndoRecorder пишет шаг ровно один раз, в EndOperation.
	/// Зато склеивались РАЗНЫЕ перетаскивания: сдвинул три узла по
	/// очереди — одна отмена возвращала все три разом. То же с повторным
	/// удалением узлов и с двумя нажатиями одной кнопки типа узла.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (_mergeKey == null)
			return false;

		if (next is not CurveNodesAction other)
			return false;

		if (!ReferenceEquals(_curve, other._curve) || !Equals(_mergeKey, other._mergeKey))
			return false;

		_after = other._after;
		return true;
	}
}

// =====================================================================================
// РАСТРОВОЕ РЕДАКТИРОВАНИЕ
// =====================================================================================

/// <summary>
/// Снимок затронутых тайлов изображения. Основной потребитель бюджета памяти:
/// хранится не весь битмап, а только изменившиеся тайлы 128x128 — по 64 КБ каждый.
/// Мазок кистью обычно задевает единицы тайлов.
/// </summary>
public sealed class ImageTilesAction : UndoableActionBase
{
	private readonly ImageShape _image;
	private readonly string _name;

	private readonly Dictionary<(int X, int Y), SKBitmap> _before = new();
	private readonly Dictionary<(int X, int Y), SKBitmap> _after = new();

	private bool _discarded;

	/// <summary>
	/// Ключ слияния. Непрерывный мазок кистью складывается в один шаг,
	/// а два разных мазка — нет. Без ключа слияния не происходит вовсе.
	/// </summary>
	public object? MergeKey { get; set; }

	private object? _mergeKey => MergeKey;

	private ImageTilesAction(string name, ImageShape image)
	{
		_name = name;
		_image = image;
	}

	/// <summary>
	/// Вызывать ДО изменения пикселей, передав прямоугольник в координатах
	/// пикселей изображения, который собирается затронуть инструмент.
	/// </summary>
	public static ImageTilesAction? Capture(string name, ImageShape image, SKRect affectedImageRect)
	{
		if (image.tileDoc == null)
			return null;

		var action = new ImageTilesAction(name, image);

		foreach (var tile in image.tileDoc.GetTilesIntersecting(affectedImageRect))
			action._before[(tile.TileX, tile.TileY)] = tile.Bitmap.Copy();

		return action._before.Count == 0 ? null : action;
	}

	/// <summary>
	/// Снимок точно перечисленных тайлов.
	///
	/// Для мазка кистью: затронутые тайлы известны поимённо, а вот
	/// общий прямоугольник по ним бывает огромен: диагональный мазок
	/// через всю картинку задевает десяток тайлов, а его охватывающий
	/// прямоугольник — все. При бюджете памяти в 64 КБ на тайл разница
	/// между десятком и сотней тайлов решает, сколько шагов поместится
	/// в историю.
	/// </summary>
	public static ImageTilesAction? CaptureTiles(
		string name,
		ImageShape image,
		IEnumerable<(int X, int Y)> tileKeys)
	{
		if (image.tileDoc == null)
			return null;

		var action = new ImageTilesAction(name, image);

		foreach (var key in tileKeys)
		{
			if (action._before.ContainsKey(key))
				continue;

			var tile = image.tileDoc.GetOrCreateTile(key.X, key.Y);
			action._before[key] = tile.Bitmap.Copy();
		}

		return action._before.Count == 0 ? null : action;
	}

	/// <summary>Снимок всех тайлов — для операций над изображением целиком.</summary>
	public static ImageTilesAction? CaptureAll(string name, ImageShape image)
	{
		if (image.tileDoc == null)
			return null;

		return Capture(
			name,
			image,
			SKRect.Create(0, 0, image.tileDoc.Width, image.tileDoc.Height));
	}

	/// <summary>Вызывать ПОСЛЕ изменения, перед Push.</summary>
	public void CaptureAfter()
	{
		if (_image.tileDoc == null)
			return;

		foreach (var key in _before.Keys)
		{
			var tile = _image.tileDoc.GetOrCreateTile(key.X, key.Y);
			_after[key] = tile.Bitmap.Copy();
		}
	}

	public override string Name => _name;

	public override long MemoryFootprint
	{
		get
		{
			long total = 512;

			foreach (var b in _before.Values)
				total += UndoMemory.Of(b);

			foreach (var b in _after.Values)
				total += UndoMemory.Of(b);

			return total;
		}
	}

	public override void Undo() => Restore(_before);
	public override void Redo() => Restore(_after);

	private void Restore(Dictionary<(int X, int Y), SKBitmap> snapshot)
	{
		if (_discarded || _image.tileDoc == null)
			return;

		_image.EndActiveStrokePreview();

		foreach (var kv in snapshot)
		{
			var tile = _image.tileDoc.GetOrCreateTile(kv.Key.X, kv.Key.Y);

			using var canvas = new SKCanvas(tile.Bitmap);
			canvas.Clear(SKColors.Transparent);
			canvas.DrawBitmap(kv.Value, 0f, 0f,
				new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

			tile.InvalidatePreview();
		}

		_image.MarkTilesDirty();
		_image.RebuildBitmapFromTiles();
		_image.InvalidateAdjustmentPaint();
	}

	public override void Discard()
	{
		_discarded = true;

		foreach (var b in _before.Values)
			b.Dispose();

		foreach (var b in _after.Values)
			b.Dispose();

		_before.Clear();
		_after.Clear();
	}

	/// <summary>
	/// Слияние непрерывного мазка: пока кнопка мыши не отпущена, все отпечатки
	/// складываются в один шаг. Начальное состояние тайлов берётся самое раннее.
	///
	/// Ключом служит сам мазок, а не имя инструмента. По имени склеивались
	/// ДВА РАЗНЫХ мазка той же кистью: отпустил кнопку, провёл ещё раз —
	/// и одна отмена убирала оба. Та же ошибка была у перемещения фигур
	/// и у правки узлов, там её уже исправили тем же способом.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (_mergeKey == null)
			return false;

		if (next is not ImageTilesAction other)
			return false;

		if (!ReferenceEquals(_image, other._image) || !Equals(_mergeKey, other._mergeKey))
			return false;

		// Тайлы, которых ещё не было в нашем снимке, забираем целиком.
		foreach (var kv in other._before)
		{
			if (!_before.ContainsKey(kv.Key))
			{
				_before[kv.Key] = kv.Value.Copy();
				continue;
			}
		}

		// Конечное состояние — из нового действия.
		foreach (var kv in other._after)
		{
			if (_after.TryGetValue(kv.Key, out var existing))
				existing.Dispose();

			_after[kv.Key] = kv.Value.Copy();
		}

		return true;
	}

	/// <summary>
	/// Снимок из активного мазка: BaseTiles сессии — это состояние тайлов
	/// до первого отпечатка. Вызывать ДО CommitActiveStroke.
	/// </summary>
	public static ImageTilesAction? FromStroke(string name, ImageShape image, ActiveImageStrokeSession stroke)
	{
		if (image.tileDoc == null || stroke.BaseTiles.Count == 0)
			return null;

		var action = new ImageTilesAction(name, image);

		foreach (var kv in stroke.BaseTiles)
			action._before[(kv.Key.X, kv.Key.Y)] = kv.Value.Copy();

		return action;
	}
}

/// <summary>
/// Изменение выделения области изображения, включая плавающий фрагмент.
/// Требование заказчика: выделения областей тоже отменяются.
/// </summary>
public sealed class ImageSelectionAction : UndoableActionBase
{
	private sealed class Snapshot : IDisposable
	{
		public SKPath? Path;
		public SKRectI Bounds;
		public SKBitmap? Mask;

		public SKBitmap? FloatingBitmap;
		public SKPointI FloatingPosition;

		public long Bytes =>
			UndoMemory.Of(Mask) + UndoMemory.Of(FloatingBitmap) + UndoMemory.Of(Path) + 128;

		public void Dispose()
		{
			Path?.Dispose();
			Mask?.Dispose();
			FloatingBitmap?.Dispose();

			Path = null;
			Mask = null;
			FloatingBitmap = null;
		}
	}

	private readonly ImageEditSession _session;
	private readonly Snapshot _before;
	private Snapshot? _after;
	private bool _discarded;

	/// <summary>Вызывать ДО изменения выделения.</summary>
	public static ImageSelectionAction Capture(ImageEditSession session)
		=> new ImageSelectionAction(session);

	private ImageSelectionAction(ImageEditSession session)
	{
		_session = session;
		_before = Take(session);
	}

	/// <summary>
	/// Изменилось ли выделение. Сравнение поверхностное — границы, наличие маски
	/// и позиция плавающего фрагмента. Побайтовое сравнение масок на каждое
	/// движение мыши обошлось бы дороже, чем изредка записанный лишний шаг.
	/// </summary>
	public bool HasChanges()
	{
		if (_after == null)
			return false;

		if (_before.Bounds != _after.Bounds)
			return true;

		if ((_before.Mask != null) != (_after.Mask != null))
			return true;

		bool floatBefore = _before.FloatingBitmap != null;
		bool floatAfter = _after.FloatingBitmap != null;

		if (floatBefore != floatAfter)
			return true;

		if (floatBefore)
		{
			if (_before.FloatingPosition != _after.FloatingPosition)
				return true;

			if (_before.FloatingBitmap!.Width != _after.FloatingBitmap!.Width ||
				_before.FloatingBitmap.Height != _after.FloatingBitmap.Height)
			{
				return true;
			}
		}

		return false;
	}


	/// <summary>Вызывать ПОСЛЕ изменения, перед Push.</summary>
	public void CaptureAfter() => _after = Take(_session);

	private static Snapshot Take(ImageEditSession session)
	{
		var sel = session.Selection;
		var floating = session.FloatingSelection;

		return new Snapshot
		{
			Path = sel.HasSelection && sel.PathPixels != null ? new SKPath(sel.PathPixels) : null,
			Bounds = sel.Bounds,
			Mask = sel.Mask?.Copy(),
			FloatingBitmap = floating?.Bitmap.Copy(),
			FloatingPosition = floating?.Position ?? SKPointI.Empty
		};
	}

	public override string Name => "Change image selection";

	public override long MemoryFootprint => _before.Bytes + (_after?.Bytes ?? 0);

	public override void Undo() => Apply(_before);
	public override void Redo() => Apply(_after);

	private void Apply(Snapshot? s)
	{
		if (_discarded || s == null)
			return;

		_session.ClearSelectionAndFloating();

		if (s.Path != null && s.Mask != null && !s.Bounds.IsEmpty)
			_session.Selection.Set(s.Path, s.Bounds, s.Mask.Copy());

		if (s.FloatingBitmap != null)
			_session.SetFloatingSelection(
				new FloatingPixelSelection(s.FloatingBitmap.Copy(), s.FloatingPosition));
	}

	public override void Discard()
	{
		_discarded = true;
		_before.Dispose();
		_after?.Dispose();
		_after = null;
	}
}

/// <summary>
/// Изменение параметров недеструктивной коррекции изображения.
/// Дешёвый шаг: хранятся только одиннадцать чисел до и после.
/// </summary>
public sealed class ImageAdjustmentsAction : UndoableActionBase
{
	private readonly ImageShape _image;
	private readonly ImageAdjustmentSettings _before;
	private ImageAdjustmentSettings _after;

	private readonly object? _mergeKey;

	/// <summary>
	/// mergeKey склеивает одно непрерывное перетаскивание ползунка.
	/// Передавать имя параметра: без ключа слияния не будет вовсе,
	/// и каждый щелчок стрелкой оставит свой шаг.
	/// </summary>
	public static ImageAdjustmentsAction Capture(ImageShape image, object? mergeKey = null)
		=> new ImageAdjustmentsAction(image, mergeKey);

	private ImageAdjustmentsAction(ImageShape image, object? mergeKey)
	{
		_image = image;
		_mergeKey = mergeKey;
		_before = image.Adjustments.Clone();
		_after = image.Adjustments.Clone();
	}

	public void CaptureAfter() => _after = _image.Adjustments.Clone();

	public override string Name => "Image adjustments";
	public override long MemoryFootprint => 256;

	public override void Undo() => Apply(_before);
	public override void Redo() => Apply(_after);

	private void Apply(ImageAdjustmentSettings s)
	{
		_image.Adjustments.CopyFrom(s);
		_image.InvalidateAdjustmentPaint();
	}

	/// <summary>
	/// Слияние только внутри одного перетаскивания ползунка.
	///
	/// Прежде склеивались любые два соседних изменения коррекции одной
	/// картинки, независимо от того, какой ползунок двигали. Подняв
	/// яркость, а затем контраст, пользователь получал один шаг на оба:
	/// отмена откатывала и то, чего он отменять не просил. Ключ —
	/// имя параметра, поэтому разные ползунки больше не сливаются.
	/// </summary>
	public override bool TryMergeWith(IUndoableAction next)
	{
		if (_mergeKey == null)
			return false;

		if (next is not ImageAdjustmentsAction other)
			return false;

		if (!ReferenceEquals(_image, other._image) || !Equals(_mergeKey, other._mergeKey))
			return false;

		_after = other._after;
		return true;
	}
}

/// <summary>
/// Замена пиксельного содержимого изображения целиком: поворот на 90°,
/// отражение, запекание коррекций. Тайловый снимок здесь не подходит, потому
/// что меняются и размеры документа.
/// </summary>
public sealed class ImageContentAction : UndoableActionBase
{
	private readonly ImageShape _image;
	private readonly string _name;

	private SKBitmap? _before;
	private SKBitmap? _after;
	private readonly ImageAdjustmentSettings _adjustmentsBefore;
	private ImageAdjustmentSettings? _adjustmentsAfter;

	private bool _discarded;

	public static ImageContentAction? Capture(string name, ImageShape image)
	{
		if (image.Bitmap == null)
			return null;

		return new ImageContentAction(name, image);
	}

	private ImageContentAction(string name, ImageShape image)
	{
		_name = name;
		_image = image;
		_before = image.Bitmap!.Copy();
		_adjustmentsBefore = image.Adjustments.Clone();
	}

	public void CaptureAfter()
	{
		_after = _image.Bitmap?.Copy();
		_adjustmentsAfter = _image.Adjustments.Clone();
	}

	public override string Name => _name;

	public override long MemoryFootprint
		=> 512 + UndoMemory.Of(_before) + UndoMemory.Of(_after);

	public override void Undo() => Apply(_before, _adjustmentsBefore);
	public override void Redo() => Apply(_after, _adjustmentsAfter);

	private void Apply(SKBitmap? snapshot, ImageAdjustmentSettings? adjustments)
	{
		if (_discarded || snapshot == null)
			return;

		_image.EndActiveStrokePreview();
		_image.SetOriginalBitmap(snapshot);

		if (adjustments != null)
			_image.Adjustments.CopyFrom(adjustments);

		_image.InvalidateAdjustmentPaint();
	}

	public override void Discard()
	{
		_discarded = true;

		_before?.Dispose();
		_after?.Dispose();
		_before = null;
		_after = null;
	}
}