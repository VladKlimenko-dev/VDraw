using SkiaSharp;

using VDraw.Documents;
using VDraw.Editing;
using VDraw.Shapes;

namespace VDraw;

/// <summary>
/// Контекстное меню фигур и превращение выделения в растр.
///
/// Меню собирается заново на каждый вызов, а не хранится готовым: состав
/// зависит от того, что выделено, и держать один экземпляр значило бы
/// каждый раз обходить его пункты и переставлять им Enabled — той же
/// работы, только с риском забыть новый пункт.
/// </summary>
public partial class MainForm
{
	/// <summary>
	/// Разрешение растра при превращении фигур в изображение.
	///
	/// Триста точек на дюйм — печатное разрешение: полученная картинка
	/// не рассыпается при выводе на бумагу и переживает умеренное
	/// увеличение на экране. Выше поднимать нет смысла: растр хранится
	/// в документе, и каждое удвоение разрешения учетверяет его вес.
	/// </summary>
	private const int RasterizeDpi = 300;

	/// <summary>Предел стороны растра, чтобы крупное выделение не съело память.</summary>
	private const int RasterizeMaxSide = 8192;

	/// <summary>
	/// Показывает меню для правого щелчка по холсту.
	///
	/// Вызывается из MainView: там известно и место щелчка, и попал ли он
	/// в фигуру. Меню открывается и при пустом выделении, если есть что
	/// вставлять: вставка — единственная команда здесь, которой выделение
	/// не нужно, зато нужно место, и указать его можно только щелчком
	/// по пустому листу.
	/// </summary>
	internal void ShowShapeContextMenu(Point screenPoint)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var selected = _mainView.Selection?.ToList() ?? new List<Shape>();

		// Точка щелчка в координатах страницы. Считается здесь, пока вид
		// не прокрутили: обработчик пункта сработает уже после закрытия
		// меню, и брать положение курсора там — значит взять его оттуда,
		// куда мышь успела уехать по дороге к пункту.
		var pastePoint = _mainView.ScreenToPage(screenPoint);

		var menu = new ContextMenuStrip { ShowImageMargin = false };

		// Меню одноразовое: без освобождения на каждый щелчок оставался бы
		// висеть неуправляемый дескриптор окна.
		menu.Closed += (_, _) => menu.BeginInvoke(new Action(menu.Dispose));

		var copy = new ToolStripMenuItem(L.T("Copy"), null, (_, _) => CopySelectedShapes())
		{
			Enabled = selected.Count > 0
		};

		var paste = new ToolStripMenuItem(L.T("Paste"), null, (_, _) => PasteCopiedShapes(pastePoint))
		{
			Enabled = CanPasteShapes()
		};

		var toCurves = new ToolStripMenuItem(
			L.T("Convert to curves"),
			null,
			(_, _) => s_convert_to_curve_but_Click(this, EventArgs.Empty))
		{
			Enabled = selected.Any(CanConvertToCurve)
		};

		var toBitmap = new ToolStripMenuItem(
			L.T("Convert to bitmap"),
			null,
			(_, _) => ConvertSelectionToBitmap())
		{
			Enabled = selected.Count > 0
		};

		var group = new ToolStripMenuItem(
			L.T("Group selected"),
			null,
			(_, _) => s_group_but_Click(this, EventArgs.Empty))
		{
			// Группировать есть что только начиная с двух фигур.
			Enabled = selected.Count >= 2
		};

		var ungroup = new ToolStripMenuItem(
			L.T("Ungroup selected"),
			null,
			(_, _) => s_ungroup_but_Click(this, EventArgs.Empty))
		{
			Enabled = selected.OfType<GroupShape>().Any()
		};

		var delete = new ToolStripMenuItem(L.T("Delete"), null, (_, _) => DeleteSelectedShapes())
		{
			Enabled = selected.Count > 0
		};

		menu.Items.Add(copy);
		menu.Items.Add(paste);
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add(toCurves);
		menu.Items.Add(toBitmap);
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add(group);
		menu.Items.Add(ungroup);
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add(delete);

		menu.Show(_mainView, screenPoint);
	}

	/// <summary>
	/// Есть ли что вставлять. Проверяется и свой буфер, и системный —
	/// в том же порядке, в каком их разбирает сама вставка.
	/// </summary>
	private bool CanPasteShapes()
	{
		if (_clipboardShapes.Count > 0)
			return true;

		try
		{
			return Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
		}
		catch
		{
			// Буфер занят другим приложением — считаем, что вставлять нечего.
			return false;
		}
	}

	#region Превращение в растр

	/// <summary>
	/// Заменяет выделенные фигуры одним изображением.
	///
	/// Растр снимается тем же кодом, что и экспорт в PNG, поэтому картинка
	/// совпадает с тем, что видно на листе, вплоть до теней и полупрозрачных
	/// заливок. Фон прозрачный: непрозрачный залил бы белым всё вокруг
	/// контуров и закрыл бы то, что лежит под ними на странице.
	/// </summary>
	public void ConvertSelectionToBitmap()
	{
		if (_mainView == null || CurrentPage == null)
			return;

		var page = CurrentPage;
		var selected = _mainView.Selection?.ToList() ?? new List<Shape>();

		if (selected.Count == 0)
			return;

		var bounds = ShapeExporter.GetExportBounds(page, selected);

		if (bounds.IsEmpty || bounds.Width <= 0f || bounds.Height <= 0f)
			return;

		var bitmap = ShapeExporter.RenderToBitmap(
			page,
			selected,
			RasterizeDpi,
			SKColors.Transparent,
			RasterizeMaxSide);

		if (bitmap == null)
			return;

		var image = BuildImageShape(bitmap, bounds, selected);

		var undo = page.doc?.Undo;

		// Удаление и добавление — один шаг истории. Иначе отмена возвращала
		// бы фигуры, оставляя картинку поверх них, и только вторым нажатием
		// приводила бы лист в исходный вид.
		undo?.BeginGroup(L.T("Convert to bitmap"));

		try
		{
			page.RemoveShapes(selected);
			page.AddShape(image);
		}
		finally
		{
			undo?.CommitGroup();
		}

		page.SortLayers();
		page.doc.need_save = true;

		var ctx = GetInteractionContext(_mainView);

		if (ctx != null)
		{
			SelectionHelper.Clear(ctx);
			SelectionHelper.Add(ctx, image);
		}

		_mainView.RaiseSelectionChanged();
		_mainView.DocumentChanged();
		RebuildPropertyPanel();
		_mainView.Invalidate();
	}

	/// <summary>
	/// Собирает изображение на месте выделения.
	///
	/// Габариты берутся те же, что у растра, — вместе с выносом обводки
	/// и наконечников. Возьми мы габариты одной геометрии, картинка,
	/// в которую эти выносы вошли, оказалась бы сжата, и толстая линия
	/// сместилась бы внутрь.
	/// </summary>
	private static ImageShape BuildImageShape(SKBitmap bitmap, SKRect bounds, IReadOnlyList<Shape> source)
	{
		var rect = new SKRect(
			-bounds.Width * 0.5f,
			-bounds.Height * 0.5f,
			bounds.Width * 0.5f,
			bounds.Height * 0.5f);

		var image = new ImageShape
		{
			Name = source.Count == 1 ? source[0].Name : "Bitmap",

			// Слой самой верхней из заменяемых фигур: картинка вобрала их
			// все, и лежать она должна там же, где лежала верхняя.
			Layer = source.Max(s => s.Layer),

			ImageLocalRect = rect,
			LocalTransform = SKMatrix.CreateTranslation(bounds.MidX, bounds.MidY)
		};

		ClipboardImporter.AddRectNodes(image, rect);

		image.SetOriginalBitmap(bitmap);

		// Оформление выключается целиком: всё это уже нарисовано ВНУТРИ
		// растра. Оставь обводку — и у картинки появилась бы вторая рамка
		// по краю, а тень легла бы от прямоугольника кадра, а не от фигур.
		image.Stroke.Enabled = false;
		image.Fill.Enabled = false;

		image.Shadow ??= new ShadowEffect();
		image.Shadow.Enabled = false;

		image.Transparency ??= new TransparencyEffect();
		image.Transparency.Enabled = false;

		return image;
	}

	#endregion
}
