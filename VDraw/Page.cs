using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

[Serializable]
public sealed class Page : PaperFormatData
{
	public List<Shape> Shapes { get; } = new();

	/// <summary>
	/// Ключи анимации страницы.
	///
	/// Хранятся здесь, а не в панели управления анимацией: панель — это
	/// представление, её можно свернуть и пересоздать, а ключи принадлежат
	/// документу и переключение страниц переживать обязаны.
	/// </summary>
	public PageAnimation Animation { get; } = new();

	public float scale = 1f;
	public SKPoint view_zero = SKPoint.Empty;
	public SKPoint page_origin { get; set; } = SKPoint.Empty;

	public float locked_ratio = 1.0f;
	public Document doc;
	public SKColor page_bg_color;

	/// <summary>
	/// Поднимается после добавления фигуры. На это событие подписан
	/// PageUndoRecorder, который сам создаёт шаг истории — инструментам
	/// создания фигур ничего дописывать не нужно.
	/// </summary>
	public event Action<Page, Shape>? ShapeAdded;

	/// <summary>
	/// Поднимается ПЕРЕД удалением фигуры. Именно перед: записывающему нужен
	/// индекс фигуры в списке, чтобы Undo вернул её на прежнее место в порядке
	/// перекрытия внутри слоя.
	/// </summary>
	public event Action<Page, Shape>? ShapeRemoving;

	public Page(PaperFormat s, UoM u, int dpi, float width, float height, PageOrientation orientation, Document d)
	{
		paper_format = s;
		uom = u;
		DPI = dpi;
		Orientation = orientation;
		doc = d;

		if (paper_format.title == "Custom")
		{
			size_uom = (Orientation == PageOrientation.Vertical
				? new SKSize(width, height)
				: new SKSize(height, width));
		}
		else
		{
			// MmPerUom, а не uom.cmm: у пиксельных единиц коэффициент
			// зависит от DPI, который присвоен строкой выше.
			float mmPerUom = MmPerUom;

			size_uom = (Orientation == PageOrientation.Vertical
				? new SKSize(paper_format.width_mm / mmPerUom, paper_format.height_mm / mmPerUom)
				: new SKSize(paper_format.height_mm / mmPerUom, paper_format.width_mm / mmPerUom));
		}
	}

	public SKSize SizeMm
	{
		get
		{
			float mmPerUom = MmPerUom;

			if (MathF.Abs(mmPerUom - 1f) < 1e-6f)
				return size_uom;

			return new SKSize(mmPerUom * size_uom.Width, mmPerUom * size_uom.Height);
		}
	}

	public float PxPerMm => DPI / 25.4f;
	/// <summary>
	/// Миллиметров в одной единице страницы.
	///
	/// Единственная точка, где это считается: пиксель зависит от DPI
	/// страницы, и прямое чтение uom.cmm давало бы для него единицу,
	/// то есть миллиметр.
	/// </summary>
	/// <summary>
	/// Единицы страницы сменились, и всё, что показывает числа в них,
	/// обязано пересчитаться.
	///
	/// Событие статическое: подписчик — панель свойств, а она живёт дольше
	/// любой отдельной страницы и не знает заранее, какая из них активна.
	/// Поднимается из SetShapesNewUoM, потому что это единственное место,
	/// через которое проходит смена единиц.
	/// </summary>
	public static event Action? UnitsChanged;

	public float MmPerUom => uom.MmPerUnit(DPI);
	public float PxPerUom => PxPerMm * MmPerUom;

	public SKRect PageRectMm => new SKRect(0, 0, SizeMm.Width, SizeMm.Height);

	public void SetFormatData(PaperFormatData format_data)
	{
		paper_format = format_data.paper_format;
		uom = format_data.uom;
		size_uom = format_data.size_uom;
		DPI = format_data.DPI;
		Orientation = format_data.Orientation;
		background_color = format_data.background_color;
		LockRatio = format_data.LockRatio;
		GridEnabled = format_data.GridEnabled;
		PrintBordersEnabled = format_data.PrintBordersEnabled;
	}

	public SKPoint GetViewSizePx()
	{
		return new SKPoint(
			size_uom.Width * PxPerUom * scale,
			size_uom.Height * PxPerUom * scale);
	}

	public SKPoint GetPrintSizePx()
	{
		return new SKPoint(
			size_uom.Width * PxPerUom,
			size_uom.Height * PxPerUom);
	}

	public void AddShape(Shape shape)
	{
		if (shape == null)
			return;

		if (Shapes.Contains(shape))
			return;

		shape.SetParent(null);
		shape.EnsureLayerAssigned();

		Shapes.Add(shape);
		SortLayers();

		ShapeAdded?.Invoke(this, shape);
	}

	public void AddShapes(IEnumerable<Shape> shapes)
	{
		// Группа: добавление набора фигур должно отменяться одним нажатием.
		var undo = doc?.Undo;
		undo?.BeginGroup("Add shapes");

		try
		{
			foreach (var shape in shapes)
				AddShape(shape);
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	#region Предпросмотр инструментов

	/// <summary>
	/// Добавляет фигуру на страницу без события ShapeAdded.
	///
	/// Для предпросмотра во время построения. На ShapeAdded подписан
	/// PageUndoRecorder, и обычное AddShape пишет шаг истории. Инструменты,
	/// пересобирающие предпросмотр на каждое движение мыши, таким образом
	/// оставляли в истории по паре шагов на каждый пиксель курсора.
	/// </summary>
	public void AddPreviewShape(Shape shape)
	{
		if (shape == null || Shapes.Contains(shape))
			return;

		shape.SetParent(null);

		if (shape.Layer == Shape.UnassignedLayer)
			shape.Layer = GetTopLayer();

		Shapes.Add(shape);
		SortLayers();
	}

	/// <summary>Убирает предпросмотр без события ShapeRemoving.</summary>
	public bool RemovePreviewShape(Shape? shape)
	{
		if (shape == null || !Shapes.Remove(shape))
			return false;

		shape.SetParent(null);
		return true;
	}

	/// <summary>
	/// Превращает предпросмотр в настоящую фигуру: снимает тихо и добавляет
	/// заново обычным путём. В историю попадает ровно один шаг — создание
	/// фигуры в её окончательном виде.
	/// </summary>
	public void CommitPreviewShape(Shape? shape)
	{
		if (shape == null)
			return;

		RemovePreviewShape(shape);
		AddShape(shape);
	}

	#endregion

	public bool RemoveShape(Shape shape)
	{
		if (shape == null)
			return false;

		// Фигура внутри группы на странице не лежит, но удалять её можно
		// и нужно. Раньше метод молча отказывал: вложенная фигура
		// не удалялась вовсе, а событие для истории не поднималось —
		// и отменять было нечего.
		if (shape.Parent is GroupShape owner)
		{
			if (!owner.Children.Contains(shape))
				return false;

			ShapeRemoving?.Invoke(this, shape);

			bool detached = owner.RemoveChild(shape);

			if (detached)
				shape.SetParent(null);

			return detached;
		}

		if (!Shapes.Contains(shape))
			return false;

		ShapeRemoving?.Invoke(this, shape);

		bool removed = Shapes.Remove(shape);
		if (removed)
			shape.SetParent(null);

		return removed;
	}

	public void RemoveShapes(IEnumerable<Shape> shapes)
	{
		var undo = doc?.Undo;
		undo?.BeginGroup("Delete shapes");

		try
		{
			// Копия: RemoveShape меняет Shapes во время перебора.
			foreach (var shape in shapes.ToList())
				RemoveShape(shape);
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	public void ClearShapes()
	{
		var undo = doc?.Undo;
		undo?.BeginGroup("Clear page");

		try
		{
			foreach (var shape in Shapes.ToList())
				RemoveShape(shape);
		}
		finally
		{
			undo?.CommitGroup();
		}
	}

	/// <summary>
	/// Вставка фигуры на конкретную позицию без события. Используется только
	/// откатом истории: шаг уже записан, повторно писать его нельзя.
	/// </summary>
	internal void InsertShapeSilent(Shape shape, int index)
	{
		if (shape == null || Shapes.Contains(shape))
			return;

		int at = Math.Clamp(index, 0, Shapes.Count);
		Shapes.Insert(at, shape);
		shape.SetParent(null);
		SortLayers();
	}

	/// <summary>Удаление без события. Используется только откатом истории.</summary>
	internal bool RemoveShapeSilent(Shape shape)
	{
		if (shape == null)
			return false;

		bool removed = Shapes.Remove(shape);
		if (removed)
			shape.SetParent(null);

		return removed;
	}

	public void SortLayers()
	{
		Shapes.Sort((a, b) => a.Layer.CompareTo(b.Layer));
	}

	public IEnumerable<Shape> GetShapesBackToFront()
	{
		return Shapes;
	}

	public IEnumerable<Shape> GetShapesFrontToBack()
	{
		for (int i = Shapes.Count - 1; i >= 0; i--)
			yield return Shapes[i];
	}

	/// <summary>
	/// Верхняя фигура под точкой. Допуск — в единицах страницы, как и
	/// у Shape.HitTest.
	/// </summary>
	public Shape? HitTest(SKPoint pagePoint, float toleranceUom)
	{
		for (int i = Shapes.Count - 1; i >= 0; i--)
		{
			var shape = Shapes[i];

			if (!shape.IsVisible || shape.IsLocked)
				continue;

			if (shape.HitTest(pagePoint, toleranceUom, MmPerUom))
				return shape;
		}

		return null;
	}

	public SKRect GetContentBounds()
	{
		if (Shapes.Count == 0)
			return SKRect.Empty;

		SKRect bounds = Shapes[0].GetBounds();

		for (int i = 1; i < Shapes.Count; i++)
			bounds = Geometry.Union(bounds, Shapes[i].GetBounds());

		return bounds;
	}

	public void SetShapesNewUoM(UoM old_uom, UoM new_uom)
	{
		if (old_uom.title == new_uom.title)
			return;

		// Коэффициенты считаются здесь, при известном DPI страницы,
		// и передаются числами: сама фигура о разрешении не знает.
		float oldMmPerUom = old_uom.MmPerUnit(DPI);
		float newMmPerUom = new_uom.MmPerUnit(DPI);

		for (int i = 0; i < Shapes.Count; i++)
			Shapes[i].SetShapeNewUoM(oldMmPerUom, newMmPerUom);

		// Новые фигуры должны получать толщину обводки уже в новых единицах.
		Settings.current_mm_per_uom = newMmPerUom;

		UnitsChanged?.Invoke();
	}

	/// <summary>
	/// Номер слоя выше всех существующих на странице.
	///
	/// Общий для предпросмотра и вставки из буфера: обоим нужно лечь поверх
	/// уже нарисованного, и считать это в двух местах по-разному не стоит.
	/// </summary>
	public int GetTopLayer()
	{
		int top = Shape.UnassignedLayer;

		foreach (var shape in Shapes)
		{
			if (shape.Layer > top)
				top = shape.Layer;
		}

		return top == Shape.UnassignedLayer ? 0 : top + 1;
	}

}