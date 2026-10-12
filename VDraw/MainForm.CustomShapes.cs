using SkiaSharp;

using VDraw.Documents;
using VDraw.Editing;
using VDraw.Shapes;

namespace VDraw;

/// <summary>
/// Подменю «Custom shapes» в меню объектов.
///
/// Сам пункт и три его постоянные команды объявлены в дизайнере
/// (customShapesToolStripMenuItem и menu_obj_*). Здесь к ним привязываются
/// обработчики и достраивается изменяемая часть — разделы с наборами:
/// они зависят от папок в Resources\Shapes, от открытого документа
/// и от подгруженных клипартов, и в дизайнере их держать негде.
/// </summary>
public partial class MainForm
{
	/// <summary>
	/// Сколько пунктов подменю созданы дизайнером.
	///
	/// Ровно столько остаётся на месте при пересборке: три команды импорта
	/// и добавления. Всё, что ниже, — наборы фигур, и они перестраиваются
	/// каждый раз заново. Считать по имени было бы надёжнее, но пункты
	/// добавляются подряд, и число здесь честнее описывает связь
	/// с дизайнером, чем поиск по строке.
	/// </summary>
	private const int DesignerCustomShapeItems = 3;

	/// <summary>
	/// Вызывается из конструктора ДО InitLanguage.
	///
	/// Порядок важен: Localization.Register запоминает исходные английские
	/// подписи один раз, обходя дерево меню. Пункты дизайнера к этому
	/// моменту уже на месте, поэтому переводятся штатно; наборы фигур
	/// переводу не подлежат — это имена, данные пользователем.
	/// </summary>
	private void InitCustomShapes()
	{
		menu_obj_add_to_custom_shapes.Click += AddSelectionAsCustomShape_Click;
		menu_obj_import_vdrs.Click += ImportCustomShapes_Click;
		menu_obj_import_visio.Click += ImportVisioStencil_Click;

		// Состав собирается при каждом раскрытии: библиотека могла
		// пополниться из другого места, а документ — смениться.
		customShapesToolStripMenuItem.DropDownOpening += (_, _) => RebuildCustomShapesMenu();

		CustomShapeLibrary.Changed += () =>
		{
			if (customShapesToolStripMenuItem.DropDown.Visible)
				RebuildCustomShapesMenu();
		};
	}

	#region Построение меню

	private void RebuildCustomShapesMenu()
	{
		var items = customShapesToolStripMenuItem.DropDownItems;

		// Пункты дизайнера не пересоздаются: у них свои имена, к ним
		// привязаны обработчики, и на них же держится перевод. Снимается
		// только то, что достроено прошлым разом.
		while (items.Count > DesignerCustomShapeItems)
			items.RemoveAt(items.Count - 1);

		menu_obj_add_to_custom_shapes.Enabled = (_mainView?.Selection?.Count ?? 0) > 0;

		bool any = false;

		var pending = new List<ToolStripItem>();

		any |= AppendGroup(pending, L.T("This document"), DocumentCustomShapes());
		any |= AppendCategories(pending, CustomShapeLibrary.Library);
		any |= AppendCategories(pending, CustomShapeLibrary.Stencils, stencil: true);

		if (!any)
			return;

		// Разделитель добавляется только когда ниже действительно что-то
		// есть: висящая черта под тремя командами читается как обрыв меню.
		items.Add(new ToolStripSeparator());

		foreach (var item in pending)
			items.Add(item);
	}

	private static IReadOnlyList<CustomShapeDefinition> DocumentCustomShapes()
		=> cur_doc?.CustomShapes ?? (IReadOnlyList<CustomShapeDefinition>)Array.Empty<CustomShapeDefinition>();

	/// <summary>
	/// Раскладывает наборы по разделам. Набор без раздела попадает
	/// в корень подменю, а не в раздел «Прочее»: лишний уровень ради
	/// одной-двух фигур только удлиняет путь мыши.
	/// </summary>
	private bool AppendCategories(
		List<ToolStripItem> items,
		IReadOnlyList<CustomShapeDefinition> source,
		bool stencil = false)
	{
		if (source.Count == 0)
			return false;

		var root = source.Where(x => string.IsNullOrWhiteSpace(x.Category)).ToList();

		foreach (var def in root)
			items.Add(BuildShapeItem(def));

		var groups = source
			.Where(x => !string.IsNullOrWhiteSpace(x.Category))
			.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
			.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

		foreach (var group in groups)
		{
			var sub = new ToolStripMenuItem(group.Key);

			foreach (var def in group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
				sub.DropDownItems.Add(BuildShapeItem(def));

			if (stencil)
			{
				sub.DropDownItems.Add(new ToolStripSeparator());

				string category = group.Key;
				var unload = new ToolStripMenuItem(L.T("Unload this stencil"));
				unload.Click += (_, _) =>
				{
					CustomShapeLibrary.RemoveStencil(category);
					RebuildCustomShapesMenu();
				};

				sub.DropDownItems.Add(unload);
			}

			items.Add(sub);
		}

		return true;
	}

	private bool AppendGroup(
		List<ToolStripItem> items,
		string title,
		IReadOnlyList<CustomShapeDefinition> source)
	{
		if (source.Count == 0)
			return false;

		var sub = new ToolStripMenuItem(title);

		foreach (var def in source.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
			sub.DropDownItems.Add(BuildShapeItem(def));

		items.Add(sub);
		return true;
	}

	private ToolStripMenuItem BuildShapeItem(CustomShapeDefinition def)
	{
		var item = new ToolStripMenuItem(def.Name)
		{
			Tag = def,
			Image = CustomShapeLibrary.BuildPreview(def),
			ImageScaling = ToolStripItemImageScaling.None
		};

		item.Click += CustomShapeItem_Click;

		// Удаление — правой кнопкой, с подтверждением. Отдельного окна
		// управления пока нет, а держать набор, который больше не нужен,
		// в меню навсегда — хуже, чем один вопрос в диалоге.
		item.MouseUp += (_, e) =>
		{
			if (e.Button != MouseButtons.Right)
				return;

			DeleteCustomShape(def);
		};

		return item;
	}

	#endregion

	#region Команды

	private void CustomShapeItem_Click(object? sender, EventArgs e)
	{
		if (sender is not ToolStripMenuItem item || item.Tag is not CustomShapeDefinition def)
			return;

		InsertCustomShape(def);
	}

	/// <summary>
	/// Вставляет набор в центр видимой области — туда же, куда ложится
	/// вставка из буфера. В начало страницы вставлять неудобно: при работе
	/// с крупным чертежом фигура окажется за экраном.
	/// </summary>
	private void InsertCustomShape(CustomShapeDefinition def)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var page = CurrentPage;
		var undo = page.doc?.Undo;

		List<Shape> shapes;

		try
		{
			shapes = CustomShapeLibrary.Materialize(def, page);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				L.T("Failed to insert the custom shape.") + "\r\n\r\n" + ex.Message,
				L.T("Custom shapes"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return;
		}

		if (shapes.Count == 0)
			return;

		var viewport = _mainView.GetViewportScreenRect();

		var origin = _mainView.ScreenToPage(new Point(
			(int)viewport.MidX,
			(int)viewport.MidY));

		foreach (var shape in shapes)
			shape.MoveByWorld(origin.X, origin.Y);

		undo?.BeginGroup("Insert custom shape");

		try
		{
			int topLayer = page.GetTopLayer();

			foreach (var shape in shapes)
				shape.Layer = topLayer++;

			page.AddShapes(shapes);
			page.SortLayers();
			page.doc.need_save = true;
		}
		finally
		{
			undo?.CommitGroup();
		}

		var ctx = GetInteractionContext(_mainView);

		if (ctx != null)
		{
			SelectionHelper.Clear(ctx);

			foreach (var shape in shapes)
				SelectionHelper.Add(ctx, shape);
		}

		_mainView.RaiseSelectionChanged();
		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	private void AddSelectionAsCustomShape_Click(object? sender, EventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var selected = _mainView.Selection?.ToList() ?? new List<Shape>();

		if (selected.Count == 0)
		{
			MessageBox.Show(
				this,
				L.T("Select at least one shape first."),
				L.T("Custom shapes"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);

			return;
		}

		string? name = PromptForName(L.T("Name of the custom shape:"), BuildDefaultName(selected));

		if (string.IsNullOrWhiteSpace(name))
			return;

		var def = CustomShapeLibrary.Create(name.Trim(), selected, CurrentPage);

		if (def.Shapes.Count == 0)
			return;

		try
		{
			CustomShapeLibrary.Save(def);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				this,
				L.T("Failed to save the custom shape to the library.") + "\r\n\r\n" + ex.Message,
				L.T("Custom shapes"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return;
		}

		// Вторая копия — в документ. Именно она уедет вместе с файлом
		// к получателю, у которого своей библиотеки с этим набором нет.
		var inDocument = CustomShapeDefinition.FromDto(def.ToDto(), CustomShapeOrigin.Document);

		cur_doc?.CustomShapes.Add(inDocument);

		if (cur_doc != null)
			cur_doc.need_save = true;

		CustomShapeLibrary.NotifyChanged();
	}

	private static string BuildDefaultName(IReadOnlyList<Shape> selected)
	{
		if (selected.Count == 1 && !string.IsNullOrWhiteSpace(selected[0].Name))
			return selected[0].Name;

		return "Shape";
	}

	private void DeleteCustomShape(CustomShapeDefinition def)
	{
		if (def.Origin == CustomShapeOrigin.Stencil)
			return;

		var dr = MessageBox.Show(
			this,
			L.T("Delete the custom shape \"{0}\"?", def.Name),
			L.T("Custom shapes"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Question);

		if (dr != DialogResult.Yes)
			return;

		if (def.Origin == CustomShapeOrigin.Document)
		{
			cur_doc?.CustomShapes.Remove(def);

			if (cur_doc != null)
				cur_doc.need_save = true;
		}
		else
		{
			CustomShapeLibrary.Delete(def);
		}

		RebuildCustomShapesMenu();
	}

	/// <summary>Фильтр диалога для наборов фигур.</summary>
	public const string CustomShapeFilter = "VDraw shapes (*.vdrs)|*.vdrs";

	/// <summary>
	/// Фильтр диалога для файлов Visio — и рисунков, и клипартов.
	///
	/// Расширения перечислены все сразу: под .vss скрываются два
	/// несовместимых формата, а .vsdx, .vssx и .vsx — это те же данные
	/// в другой упаковке. Разбирается файл по сигнатуре, поэтому делить
	/// их в диалоге было бы обманом — пользователю пришлось бы угадывать,
	/// какого поколения у него файл.
	/// </summary>
	public const string StencilFilter =
		"Visio drawing or stencil (*.vsdx;*.vsdm;*.vss;*.vssx;*.vssm;*.vsx;*.vdx)" +
		"|*.vsdx;*.vsdm;*.vss;*.vssx;*.vssm;*.vsx;*.vdx";

	private void ImportCustomShapes_Click(object? sender, EventArgs e)
	{
		using var ofd = new OpenFileDialog
		{
			Title = L.T("Import custom shapes"),
			Filter = CustomShapeFilter + "|All files (*.*)|*.*",
			Multiselect = true,
			CheckFileExists = true
		};

		if (ofd.ShowDialog(this) != DialogResult.OK)
			return;

		ImportCustomShapeFiles(ofd.FileNames);
	}

	/// <summary>
	/// Импорт наборов .vdrs. Вынесен отдельно от обработчика меню,
	/// потому что то же самое вызывает кнопка панели инструментов.
	/// </summary>
	internal void ImportCustomShapeFiles(IReadOnlyList<string> files)
	{
		if (files.Count == 0)
			return;

		CustomShapeLibrary.ImportFiles(files, out var errors);

		CustomShapeLibrary.NotifyChanged();
		RebuildCustomShapesMenu();

		if (errors.Count == 0)
			return;

		MessageBox.Show(
			this,
			L.T("These files could not be read:") + "\r\n" + string.Join("\r\n", errors),
			L.T("Custom shapes"),
			MessageBoxButtons.OK,
			MessageBoxIcon.Warning);
	}

	private void ImportVisioStencil_Click(object? sender, EventArgs e)
	{
		using var ofd = new OpenFileDialog
		{
			Title = L.T("Import Visio file"),
			Filter = StencilFilter + "|All files (*.*)|*.*",
			Multiselect = true,
			CheckFileExists = true
		};

		if (ofd.ShowDialog(this) != DialogResult.OK)
			return;

		ImportVisioStencilFiles(ofd.FileNames);
	}

	/// <summary>
	/// Импорт клипартов Visio. Вынесен отдельно от обработчика меню,
	/// потому что то же самое вызывает кнопка панели инструментов.
	/// </summary>
	internal void ImportVisioStencilFiles(IReadOnlyList<string> files)
	{
		if (files.Count == 0)
			return;

		var errors = new List<string>();

		foreach (var file in files)
		{
			try
			{
				var shapes = VisioStencilImporter.Import(file, out string? note, out bool isDrawing);

				if (shapes.Count == 0)
				{
					errors.Add($"{Path.GetFileName(file)}: {note ?? L.T("no shapes found")}");
					continue;
				}

				// Рисунок ложится на страницу, трафарет — в меню фигур.
				// Чертёж в библиотеке заготовок пришлось бы оттуда ещё
				// доставать, а заготовки на странице оказались бы кучей
				// поверх работы.
				if (isDrawing)
					PlaceOnCurrentPage(shapes);
				else
					CustomShapeLibrary.AddStencil(Path.GetFileNameWithoutExtension(file), shapes);

				if (!string.IsNullOrWhiteSpace(note))
					errors.Add($"{Path.GetFileName(file)}: {note}");
			}
			catch (Exception ex)
			{
				errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
			}
		}

		CustomShapeLibrary.NotifyChanged();
		RebuildCustomShapesMenu();

		// Об удачном импорте говорит сам результат: рисунок на странице
		// или новый раздел в меню фигур. Окно с числом фигур пришлось бы
		// закрывать после каждого файла, ничего к увиденному не добавляя.
		if (errors.Count == 0)
			return;

		MessageBox.Show(
			this,
			string.Join("\r\n", errors),
			L.T("Custom shapes"),
			MessageBoxButtons.OK,
			MessageBoxIcon.Warning);
	}

	/// <summary>
	/// Кладёт разобранные рисунки на текущую страницу.
	///
	/// Проход тот же, что и у вставки кастомной фигуры: пересборка через
	/// DocumentMapper, пересчёт единиц, слои поверх имеющихся, одна запись
	/// в историю на всю вставку.
	/// </summary>
	private void PlaceOnCurrentPage(IReadOnlyList<CustomShapeDefinition> drawings)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var page = CurrentPage;
		var undo = page.doc?.Undo;

		var placed = new List<Shape>();

		undo?.BeginGroup("Import Visio drawing");

		try
		{
			int topLayer = page.GetTopLayer();

			foreach (var drawing in drawings)
			{
				var shapes = CustomShapeLibrary.Materialize(drawing, page);

				if (shapes.Count == 0)
					continue;

				foreach (var shape in shapes)
					shape.Layer = topLayer++;

				page.AddShapes(shapes);
				placed.AddRange(shapes);
			}

			if (placed.Count > 0)
			{
				page.SortLayers();
				page.doc.need_save = true;
			}
		}
		finally
		{
			undo?.CommitGroup();
		}

		if (placed.Count == 0)
			return;

		// Рисунок приходит с центром в начале координат — ставим его
		// в середину видимой области, как и вставку из буфера.
		var viewport = _mainView.GetViewportScreenRect();

		var origin = _mainView.ScreenToPage(new Point(
			(int)viewport.MidX,
			(int)viewport.MidY));

		foreach (var shape in placed)
			shape.MoveByWorld(origin.X, origin.Y);

		var ctx = GetInteractionContext(_mainView);

		if (ctx != null)
		{
			SelectionHelper.Clear(ctx);

			foreach (var shape in placed)
				SelectionHelper.Add(ctx, shape);
		}

		_mainView.RaiseSelectionChanged();
		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	#endregion

	#region Ввод имени

	/// <summary>
	/// Маленький диалог ввода строки. В WinForms своего InputBox нет,
	/// а тянуть Microsoft.VisualBasic ради одного окна не стоит.
	/// </summary>
	private string? PromptForName(string prompt, string initial)
	{
		using var form = new Form
		{
			Text = L.T("Custom shapes"),
			FormBorderStyle = FormBorderStyle.FixedDialog,
			StartPosition = FormStartPosition.CenterParent,
			MinimizeBox = false,
			MaximizeBox = false,
			ShowInTaskbar = false,
			ClientSize = new Size(360, 116)
		};

		var label = new Label
		{
			Text = prompt,
			AutoSize = true,
			Location = new Point(12, 14)
		};

		var box = new TextBox
		{
			Text = initial,
			Location = new Point(12, 40),
			Width = 336
		};

		var ok = new Button
		{
			Text = "OK",
			DialogResult = DialogResult.OK,
			Location = new Point(192, 76),
			Width = 75
		};

		var cancel = new Button
		{
			Text = L.T("Cancel"),
			DialogResult = DialogResult.Cancel,
			Location = new Point(273, 76),
			Width = 75
		};

		form.Controls.AddRange(new Control[] { label, box, ok, cancel });
		form.AcceptButton = ok;
		form.CancelButton = cancel;

		box.SelectAll();

		return form.ShowDialog(this) == DialogResult.OK ? box.Text : null;
	}

	#endregion
}
