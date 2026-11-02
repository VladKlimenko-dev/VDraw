using SkiaSharp;

using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw;

/// <summary>
/// Импорт SVG. Разбор файла живёт в SvgImporter, здесь — только то,
/// что относится к документу: куда положить, как уместить на листе
/// и о чём сообщить пользователю.
/// </summary>
public partial class MainForm
{
	/// <summary>Промежуток между соседними файлами в ряду, мм.</summary>
	private const float SvgRowGapMm = 5f;

	/// <summary>
	/// Самая тонкая обводка в единицах листа. Тот же порог, с которым
	/// импортёр принимает толщину из файла: ужатый чертёж не должен
	/// получать линию тоньше той, которую импорт считает допустимой.
	/// </summary>
	private const float MinStrokeThickness = 0.01f;

	/// <summary>
	/// Доля листа, оставляемая по краям, когда ряд приходится ужимать.
	/// Иначе крайние контуры ложились бы вплотную к обрезу.
	/// </summary>
	private const float SvgPageMargin = 0.05f;

	/// <summary>
	/// Вставляет файлы SVG на текущий лист.
	///
	/// Каждый файл — одна фигура: содержимое файла собирается в группу,
	/// потому что рисунок переносят, поворачивают и масштабируют целиком,
	/// а не по контуру. Файл из единственного контура группой не
	/// оборачивается: пустая обёртка только мешала бы правке узлов.
	///
	/// Файлы выстраиваются в ряд по горизонтали с промежутком: при выборе
	/// десятка значков иначе все легли бы в одну точку, и разбирать эту
	/// кучу пришлось бы перетаскиванием вслепую.
	///
	/// Всё добавляется одним вызовом AddShapes, то есть одним шагом
	/// истории: выбрал пять файлов — отменяется одним Ctrl+Z.
	/// </summary>
	private async void ImportSvgFiles(IReadOnlyList<string> files)
	{
		if (files.Count == 0 || CurrentPage == null || _mainView == null)
			return;

		var page = CurrentPage;

		var roots = new List<Shape>();
		var warnings = new List<string>();
		var failed = new List<string>();

		try
		{
			Cursor = Cursors.WaitCursor;

			foreach (string file in files)
			{
				// Разбор в фоне: карта или чертёж в SVG — это десятки тысяч
				// контуров, и на потоке интерфейса это выглядит зависанием.
				var result = await Task.Run(() => SvgImporter.Import(file, page));

				warnings.AddRange(result.Warnings);

				if (result.Shapes.Count == 0)
				{
					// В список «нечего вставить» файл попадает только
					// молча: если разбор уже сказал, почему файл пустой,
					// второе окно о том же самом — лишнее.
					if (result.Warnings.Count == 0)
						failed.Add(Path.GetFileName(file));

					continue;
				}

				roots.Add(BuildSvgRoot(result.Shapes, Path.GetFileNameWithoutExtension(file)));
			}

			if (roots.Count > 0)
			{
				LayOutSvgRow(page, roots);

				// Добавление на лист — тоже внутри try: это шаг истории
				// и событие ShapeAdded, то есть чужой код, и падать от него
				// в async void значит уронить приложение вместо окна
				// с ошибкой.
				page.AddShapes(roots);

				sel.Set(roots);
				page.doc.need_save = true;

				sel.NotifyGeometryChanged();
				_mainView?.Invalidate();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				L.T("Failed to import SVG.\r\n\r\n{0}", ex.Message),
				L.T("Import error"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return;
		}
		finally
		{
			Cursor = Cursors.Default;
		}

		if (failed.Count > 0)
		{
			MessageBox.Show(
				this,
				L.T("These files contain nothing importable:") + Environment.NewLine +
				string.Join(Environment.NewLine, failed.Take(10)),
				L.T("Import SVG"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}

		if (warnings.Count > 0)
		{
			MessageBox.Show(
				this,
				string.Join(Environment.NewLine, warnings.Distinct().Take(10)),
				L.T("Import notes"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
		}
	}

	/// <summary>Содержимое одного файла как одна фигура документа.</summary>
	private static Shape BuildSvgRoot(List<Shape> shapes, string name)
	{
		if (shapes.Count == 1)
		{
			shapes[0].Name = name;

			return shapes[0];
		}

		// Слой группе не назначается: он ничейный у всего, что пришло
		// из файла, и выдаст его Page.AddShape через EnsureLayerAssigned —
		// уже с учётом того, что на листе лежит.
		var group = new GroupShape { Name = name };

		// Children.Add напрямую, а не AddChild: тот на каждом вызове
		// проверяет Contains и пересортировывает список целиком, то есть
		// на файле в десять тысяч контуров делает квадратичную работу.
		// Порядок у фигур уже верный — он задан порядком в файле.
		//
		// Но номера слоёв расставить всё равно надо. Page.AddShape выдаёт
		// их только корню, а SortChildrenByLayer — обычный List.Sort,
		// то есть сортировка неустойчивая: у детей с одинаковым (ничейным)
		// слоем первая же отмена или преобразование внутри группы
		// перемешали бы порядок перекрытия рисунка.
		for (int i = 0; i < shapes.Count; i++)
		{
			var shape = shapes[i];

			shape.ReparentPreserveWorld(group);
			shape.Layer = i;
			group.Children.Add(shape);
		}

		return group;
	}

	/// <summary>
	/// Расставляет файлы в ряд по горизонтали, центром ряда — в центре листа.
	///
	/// Если ряд в лист не влезает, он ужимается целиком и с одним
	/// коэффициентом на все файлы: пропорции между рисунками при этом
	/// сохраняются, а разный масштаб у соседних значков выглядел бы
	/// ошибкой импорта.
	/// </summary>
	private static void LayOutSvgRow(Page page, List<Shape> roots)
	{
		float availableWidth = page.size_uom.Width * (1f - SvgPageMargin * 2f);
		float availableHeight = page.size_uom.Height * (1f - SvgPageMargin * 2f);

		// Габариты снимаются по одному разу на проход и запоминаются:
		// у кривой GetBounds каждый раз собирает мировой путь заново
		// и считает по нему точную коробку, и на карте в десятки тысяч
		// узлов лишний проход заметен.
		var bounds = new SKRect[roots.Count];

		float contentWidth = 0f;
		float contentHeight = 0f;

		for (int i = 0; i < roots.Count; i++)
		{
			bounds[i] = MeasureSvgRoot(roots[i]);

			contentWidth += bounds[i].Width;
			contentHeight = MathF.Max(contentHeight, bounds[i].Height);
		}

		float gap = SvgRowGapMm / MathF.Max(page.MmPerUom, 0.0001f);
		int gaps = roots.Count - 1;

		// Промежутки считаются до рисунков и из их доли вычитаются,
		// потому что сами они не ужимаются: это расстояние между
		// отдельными объектами на листе, а не часть рисунка. Иначе
		// на двух десятках файлов промежутки заняли бы ползаголовка
		// листа, и ряд вылез бы за край, сколько бы ни ужимали сами
		// рисунки. Больше половины ширины промежуткам не отдаётся.
		if (gaps > 0 && gap * gaps > availableWidth * 0.5f)
			gap = availableWidth * 0.5f / gaps;

		float forShapes = availableWidth - gap * gaps;

		float scale = 1f;

		if (contentWidth > forShapes && contentWidth > 0f && forShapes > 0f)
			scale = forShapes / contentWidth;

		if (contentHeight * scale > availableHeight && contentHeight > 0f)
			scale = availableHeight / contentHeight;

		float rowWidth = gap * gaps;

		if (scale < 1f)
		{
			// После ужатия коробки берутся заново, а не умножаются
			// на коэффициент: у фигуры они считаются по самой кривой,
			// и приблизительное умножение сдвинуло бы ряд.
			for (int i = 0; i < roots.Count; i++)
			{
				ScaleSvgRoot(roots[i], scale);

				bounds[i] = MeasureSvgRoot(roots[i]);
			}
		}

		foreach (var b in bounds)
			rowWidth += b.Width;

		float x = page.page_origin.X + (page.size_uom.Width - rowWidth) * 0.5f;
		float middle = page.page_origin.Y + page.size_uom.Height * 0.5f;

		for (int i = 0; i < roots.Count; i++)
		{
			roots[i].MoveByWorld(x - bounds[i].Left, middle - bounds[i].MidY);

			x += bounds[i].Width + gap;
		}
	}

	/// <summary>
	/// Ужимает рисунок вместе с толщиной его линий.
	///
	/// Толщина обводки живёт отдельно от матрицы фигуры (StrokeStyle.Thickness
	/// в единицах листа) и преобразованием не затрагивается. Без явного
	/// пересчёта ужатый в десять раз чертёж превратился бы в кляксу: линии
	/// остались бы прежними, а расстояния между ними сократились.
	/// </summary>
	/// <summary>
	/// Габариты фигуры, считающиеся и по плоским детям.
	///
	/// Своя мерка, а не GroupShape.GetBounds, из-за значков вроде «строку
	/// вверх»: там четыре строго горизонтальные линии и одна стрелка.
	/// У горизонтальной линии высота ровно ноль, и в зависимости от того,
	/// что считает пустым прямоугольником SKRect.IsEmpty, общая коробка
	/// группы могла бы свестись к одной стрелке — тогда линии вылезли бы
	/// за ряд влево. Здесь отбрасывается только то, у чего нет ни ширины,
	/// ни высоты, то есть фигуры вовсе без геометрии.
	/// </summary>
	private static SKRect MeasureSvgRoot(Shape shape)
	{
		if (shape is not GroupShape group)
			return shape.GetBounds();

		SKRect result = SKRect.Empty;
		bool any = false;

		foreach (var child in group.Children)
		{
			var b = MeasureSvgRoot(child);

			if (b.Width <= 0f && b.Height <= 0f)
				continue;

			result = any
				? new SKRect(
					MathF.Min(result.Left, b.Left),
					MathF.Min(result.Top, b.Top),
					MathF.Max(result.Right, b.Right),
					MathF.Max(result.Bottom, b.Bottom))
				: b;

			any = true;
		}

		return any ? result : SKRect.Empty;
	}

	private static void ScaleSvgRoot(Shape shape, float scale)
	{
		// ApplyWorldTransform, а не правка LocalTransform напрямую:
		// он сам учитывает матрицу родителя и пересчитывает местную
		// матрицу относительно неё.
		shape.ApplyWorldTransform(SKMatrix.CreateScale(scale, scale));

		ScaleStrokes(shape, scale);
	}

	private static void ScaleStrokes(Shape shape, float scale)
	{
		shape.Stroke.Thickness = MathF.Max(shape.Stroke.Thickness * scale, MinStrokeThickness);

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
				ScaleStrokes(child, scale);
		}
	}
}
