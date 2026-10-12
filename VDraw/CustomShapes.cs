using SkiaSharp;
using System.Text.Json;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

#region Формат файла

/// <summary>
/// Содержимое файла .vdrs — одного пользовательского набора фигур.
///
/// Фигуры описаны теми же ShapeDto, что и в документе. Это не экономия
/// строк, а требование: набор обязан переживать те же вещи, что и файл
/// документа, — заливки с встроенными растрами, эффекты, привязки между
/// фигурами внутри набора. Отдельная урезанная модель разошлась бы
/// с основной на первом же новом свойстве фигуры.
/// </summary>
public sealed class CustomShapeDto
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;

	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Папка внутри Resources\Shapes, где лежит набор. Пустая строка —
	/// корень. Для клипартов Visio сюда попадает имя файла клипарта.
	/// </summary>
	public string Category { get; set; } = string.Empty;

	/// <summary>
	/// Миллиметров в одной единице, в которой записана геометрия.
	///
	/// Без этого числа набор нельзя вставить в страницу с другими
	/// единицами: координаты фигур — это просто числа, и «100» на
	/// миллиметровой странице и на дюймовой означают разный размер.
	/// Храним не сами единицы, а коэффициент: у пикселя он зависит
	/// от DPI страницы, а DPI получателя нам неизвестен.
	/// </summary>
	public float MmPerUnit { get; set; } = 1f;

	public List<ShapeDto> Shapes { get; set; } = new();
}

#endregion

/// <summary>
/// Откуда взялся набор. От этого зависит и место в меню, и то,
/// сохраняется ли набор куда-либо.
/// </summary>
public enum CustomShapeOrigin
{
	/// <summary>Файл в Resources\Shapes. Доступен всегда.</summary>
	Library,

	/// <summary>Пришёл внутри открытого документа. Живёт вместе с ним.</summary>
	Document,

	/// <summary>Клипарт Visio, подгруженный на время сеанса. Никуда не пишется.</summary>
	Stencil
}

/// <summary>
/// Пользовательский набор фигур в памяти.
///
/// Хранит DTO, а не готовые фигуры, намеренно. Готовые пришлось бы
/// клонировать при каждой вставке, а Clone у фигур поверхностный там,
/// где это допустимо внутри одной страницы, — например, привязки конца
/// кривой копируются ссылкой. Для набора, вставляемого многократно
/// и в разные документы, это дало бы связанные между собой копии.
/// Пересборка из DTO проходит тот же двухпроходный путь, что и загрузка
/// документа, и даёт независимый экземпляр.
/// </summary>
public sealed class CustomShapeDefinition
{
	public string Name { get; set; } = string.Empty;
	public string Category { get; set; } = string.Empty;
	public float MmPerUnit { get; set; } = 1f;
	public List<ShapeDto> Shapes { get; set; } = new();

	public CustomShapeOrigin Origin { get; set; } = CustomShapeOrigin.Library;

	/// <summary>Путь к .vdrs. Пуст у наборов из документа и из клипарта.</summary>
	public string FilePath { get; set; } = string.Empty;

	/// <summary>Кэш значка для меню. Строится лениво: наборов бывают сотни.</summary>
	public Image? Preview { get; set; }

	public CustomShapeDto ToDto() => new()
	{
		Version = CustomShapeDto.CurrentVersion,
		Name = Name,
		Category = Category,
		MmPerUnit = MmPerUnit,
		Shapes = Shapes
	};

	public static CustomShapeDefinition FromDto(CustomShapeDto dto, CustomShapeOrigin origin, string filePath = "")
		=> new()
		{
			Name = string.IsNullOrWhiteSpace(dto.Name) ? "Shape" : dto.Name,
			Category = dto.Category ?? string.Empty,
			MmPerUnit = dto.MmPerUnit > 0f ? dto.MmPerUnit : 1f,
			Shapes = dto.Shapes ?? new List<ShapeDto>(),
			Origin = origin,
			FilePath = filePath
		};
}

/// <summary>
/// Библиотека пользовательских наборов.
///
/// Держит только то, что живёт дольше документа: файлы из
/// Resources\Shapes и клипарты, подгруженные на время сеанса. Наборы
/// самого документа лежат в Document.CustomShapes и сюда не копируются —
/// иначе при закрытии документа их пришлось бы вычищать отсюда поимённо,
/// а совпадение имён с библиотечными сделало бы это ненадёжным.
/// </summary>
public static class CustomShapeLibrary
{
	public const string FileExtension = ".vdrs";

	private static readonly List<CustomShapeDefinition> _library = new();
	private static readonly List<CustomShapeDefinition> _stencils = new();

	private static bool _loaded;

	/// <summary>Наборы из Resources\Shapes.</summary>
	public static IReadOnlyList<CustomShapeDefinition> Library
	{
		get { EnsureLoaded(); return _library; }
	}

	/// <summary>Наборы из клипартов Visio. Только текущий сеанс.</summary>
	public static IReadOnlyList<CustomShapeDefinition> Stencils => _stencils;

	/// <summary>Поднимается, когда состав наборов изменился и меню нужно пересобрать.</summary>
	public static event Action? Changed;

	public static void NotifyChanged() => Changed?.Invoke();

	#region Загрузка библиотеки

	private static void EnsureLoaded()
	{
		if (_loaded)
			return;

		_loaded = true;
		Reload();
	}

	/// <summary>
	/// Перечитывает Resources\Shapes целиком, включая подпапки.
	/// Имя подпапки становится разделом меню.
	/// </summary>
	public static void Reload()
	{
		_loaded = true;
		_library.Clear();

		string root = ResourcePaths.Shapes;

		if (!Directory.Exists(root))
		{
			try { Directory.CreateDirectory(root); }
			catch { /* права на запись — не повод падать при старте */ }

			return;
		}

		IEnumerable<string> files;

		try
		{
			files = Directory.EnumerateFiles(root, "*" + FileExtension, SearchOption.AllDirectories);
		}
		catch
		{
			return;
		}

		foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			var def = TryRead(file);

			if (def == null)
				continue;

			// Раздел определяется положением файла на диске, а не полем внутри:
			// пользователь раскладывает файлы по папкам руками, и меню обязано
			// отражать то, что он видит в проводнике.
			def.Category = RelativeFolder(root, file);
			_library.Add(def);
		}
	}

	private static string RelativeFolder(string root, string file)
	{
		string? dir = Path.GetDirectoryName(file);

		if (string.IsNullOrEmpty(dir))
			return string.Empty;

		string rel = Path.GetRelativePath(root, dir);

		return rel == "." ? string.Empty : rel.Replace('\\', '/');
	}

	public static CustomShapeDefinition? TryRead(string file)
	{
		try
		{
			string json = File.ReadAllText(file);

			var dto = JsonSerializer.Deserialize<CustomShapeDto>(json, DocumentJson.CreateOptions());

			if (dto == null || dto.Shapes.Count == 0)
				return null;

			if (string.IsNullOrWhiteSpace(dto.Name))
				dto.Name = Path.GetFileNameWithoutExtension(file);

			return CustomShapeDefinition.FromDto(dto, CustomShapeOrigin.Library, file);
		}
		catch
		{
			// Битый или чужой файл пропускаем: одна испорченная фигура
			// не должна лишать пользователя всей библиотеки.
			return null;
		}
	}

	#endregion

	#region Создание набора

	/// <summary>
	/// Собирает набор из выделенных фигур.
	///
	/// Фигуры клонируются перед разбором в DTO и сдвигаются так, чтобы
	/// центр габаритов оказался в начале координат. Без этого набор нёс
	/// бы в себе координаты того места страницы, где его создали, и при
	/// вставке улетал бы туда же.
	/// </summary>
	public static CustomShapeDefinition Create(string name, IReadOnlyList<Shape> shapes, Page page)
	{
		var def = new CustomShapeDefinition
		{
			Name = name,
			MmPerUnit = MathF.Max(page.MmPerUom, 0.0001f),
			Origin = CustomShapeOrigin.Library
		};

		var copies = new List<Shape>(shapes.Count);

		foreach (var shape in shapes)
		{
			if (shape is GuideLine)
				continue;

			copies.Add(shape.Clone());
		}

		if (copies.Count == 0)
			return def;

		var bounds = UnionBounds(copies);

		if (!bounds.IsEmpty)
		{
			foreach (var shape in copies)
				shape.MoveByWorld(-bounds.MidX, -bounds.MidY);
		}

		foreach (var shape in copies)
			def.Shapes.Add(DocumentMapper.ToDto(shape));

		return def;
	}

	private static SKRect UnionBounds(IEnumerable<Shape> shapes)
	{
		SKRect result = SKRect.Empty;

		foreach (var shape in shapes)
		{
			var b = shape.GetBounds();

			if (b.IsEmpty)
				continue;

			result = result.IsEmpty ? b : Geometry.Union(result, b);
		}

		return result;
	}

	#endregion

	#region Запись в Resources\Shapes

	/// <summary>
	/// Пишет набор в Resources\Shapes и добавляет его в библиотеку.
	/// Возвращает путь к файлу.
	/// </summary>
	public static string Save(CustomShapeDefinition def)
	{
		string folder = ResourcePaths.Shapes;

		if (!string.IsNullOrWhiteSpace(def.Category))
			folder = Path.Combine(folder, def.Category);

		Directory.CreateDirectory(folder);

		string baseName = SanitizeFileName(def.Name);
		string path = Path.Combine(folder, baseName + FileExtension);

		int i = 1;
		while (File.Exists(path))
		{
			path = Path.Combine(folder, $"{baseName}_{i}{FileExtension}");
			i++;
		}

		string json = JsonSerializer.Serialize(def.ToDto(), DocumentJson.CreateOptions());
		File.WriteAllText(path, json);

		def.FilePath = path;
		def.Origin = CustomShapeOrigin.Library;

		EnsureLoaded();
		_library.Add(def);

		return path;
	}

	/// <summary>Удаляет набор из библиотеки вместе с файлом.</summary>
	public static bool Delete(CustomShapeDefinition def)
	{
		if (def.Origin != CustomShapeOrigin.Library)
			return false;

		try
		{
			if (!string.IsNullOrWhiteSpace(def.FilePath) && File.Exists(def.FilePath))
				File.Delete(def.FilePath);
		}
		catch
		{
			return false;
		}

		_library.Remove(def);
		def.Preview?.Dispose();
		def.Preview = null;

		return true;
	}

	public static string SanitizeFileName(string name)
	{
		string result = string.IsNullOrWhiteSpace(name) ? "shape" : name.Trim();

		foreach (char c in Path.GetInvalidFileNameChars())
			result = result.Replace(c, '_');

		return string.IsNullOrWhiteSpace(result) ? "shape" : result;
	}

	#endregion

	#region Импорт

	/// <summary>
	/// Импортирует файлы .vdrs в библиотеку. Файлы КОПИРУЮТСЯ в
	/// Resources\Shapes: импорт должен переживать перемещение исходника.
	/// Возвращает число добавленных наборов.
	/// </summary>
	public static int ImportFiles(IEnumerable<string> files, out List<string> errors)
	{
		errors = new List<string>();
		int added = 0;

		EnsureLoaded();

		foreach (var file in files)
		{
			var def = TryRead(file);

			if (def == null)
			{
				errors.Add(Path.GetFileName(file));
				continue;
			}

			try
			{
				Save(def);
				added++;
			}
			catch (Exception ex)
			{
				errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
			}
		}

		return added;
	}

	/// <summary>
	/// Добавляет наборы из клипарта на время сеанса.
	///
	/// Ничего не пишет на диск и не кладёт в документ: подгруженный
	/// клипарт остаётся внешним файлом, а не частью работы пользователя.
	/// Повторная загрузка того же раздела заменяет прежний, иначе меню
	/// обрастало бы дублями при каждом нажатии.
	/// </summary>
	public static void AddStencil(string category, IEnumerable<CustomShapeDefinition> shapes)
	{
		_stencils.RemoveAll(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));

		foreach (var def in shapes)
		{
			def.Origin = CustomShapeOrigin.Stencil;
			def.Category = category;
			_stencils.Add(def);
		}
	}

	public static void RemoveStencil(string category)
	{
		foreach (var def in _stencils.Where(x =>
			string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)))
		{
			def.Preview?.Dispose();
			def.Preview = null;
		}

		_stencils.RemoveAll(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
	}

	#endregion

	#region Вставка на страницу

	/// <summary>
	/// Пересобирает фигуры набора для вставки в страницу.
	///
	/// Проход тот же, что и при загрузке документа: временный документ,
	/// временная страница, DocumentMapper. Своего пути разбора здесь нет
	/// намеренно — второй проход с восстановлением ссылок между фигурами
	/// пришлось бы повторить целиком, и он немедленно разошёлся бы
	/// с основным.
	/// </summary>
	public static List<Shape> Materialize(CustomShapeDefinition def, Page target)
	{
		var result = new List<Shape>();

		if (def.Shapes.Count == 0)
			return result;

		var pageDto = new PageDto
		{
			Title = def.Name,
			DPI = target.DPI,
			SizeUom = target.size_uom,
			Shapes = def.Shapes
		};

		var temp = new Document();

		foreach (var page in temp.pages.ToList())
			temp.PageRecorder.Detach(page);

		temp.pages.Clear();
		temp.ActivePage = null;

		// Единицы временной страницы совпадают с единицами НАБОРА: именно
		// в них записана его геометрия, и пересчёт делается ниже, одним
		// понятным шагом, а не спрятанным в чтение.
		temp.uom = target.uom;
		temp.DPI = target.DPI;

		var built = DocumentMapper.FromDto(pageDto, temp);

		foreach (var shape in built.Shapes)
			result.Add(shape);

		built.Shapes.Clear();

		// Идентификаторы выдаются заново: набор вставляют многократно,
		// и вторая копия с теми же ключами сделала бы ссылки на фигуру
		// неоднозначными — привязка размера нашла бы чужой экземпляр.
		ReassignIds(result);

		// Единицы набора могут отличаться от единиц страницы.
		float mmPerUnit = MathF.Max(def.MmPerUnit, 0.0001f);
		float pageMmPerUom = MathF.Max(target.MmPerUom, 0.0001f);

		if (MathF.Abs(mmPerUnit - pageMmPerUom) > 0.000001f)
		{
			foreach (var shape in result)
				shape.SetShapeNewUoM(mmPerUnit, pageMmPerUom);
		}

		return result;
	}

	/// <summary>
	/// Выдаёт новые идентификаторы всему дереву и чинит ссылки, которые
	/// хранятся ключами, а не объектами. Сейчас такие только у инструмента
	/// пошагового преобразования; привязки концов держат ссылку и переживают
	/// смену ключа сами.
	/// </summary>
	private static void ReassignIds(IEnumerable<Shape> shapes)
	{
		var map = new Dictionary<Guid, Guid>();

		foreach (var shape in EnumerateDeep(shapes))
		{
			var fresh = Guid.NewGuid();
			map[shape.Id] = fresh;
			shape.Id = fresh;
		}

		foreach (var shape in EnumerateDeep(shapes))
		{
			if (shape is not TransformShape transform)
				continue;

			if (map.TryGetValue(transform.ShapeAId, out var a))
				transform.ShapeAId = a;

			if (map.TryGetValue(transform.ShapeBId, out var b))
				transform.ShapeBId = b;
		}
	}

	public static IEnumerable<Shape> EnumerateDeep(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateDeep(group.Children))
					yield return child;
			}
		}
	}

	#endregion

	#region Значок для меню

	/// <summary>
	/// Строит значок набора. Рисует те же фигуры тем же кодом, что и холст,
	/// — отдельного «упрощённого» рендера здесь нет, иначе значок начал бы
	/// врать про заливки и эффекты.
	/// </summary>
	public static Image? BuildPreview(CustomShapeDefinition def, int size = 32)
	{
		if (def.Preview != null)
			return def.Preview;

		try
		{
			def.Preview = RenderPreview(def, size);
		}
		catch
		{
			def.Preview = null;
		}

		return def.Preview;
	}

	private static Image? RenderPreview(CustomShapeDefinition def, int size)
	{
		var shapes = MaterializeForPreview(def);

		if (shapes.Count == 0)
			return null;

		SKRect bounds = SKRect.Empty;
		float maxHalfStroke = 0f;

		foreach (var shape in shapes)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);

			if (shape.Stroke is { Enabled: true })
				maxHalfStroke = MathF.Max(maxHalfStroke, shape.Stroke.Thickness * 0.5f);
		}

		if (bounds.IsEmpty)
			return null;

		bounds = Geometry.Inflate(bounds, maxHalfStroke, maxHalfStroke);

		float side = MathF.Max(MathF.Max(bounds.Width, bounds.Height), 0.0001f);
		float scale = (size - 2) / side;

		var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);

		using var surface = SKSurface.Create(info);

		if (surface == null)
			return null;

		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Transparent);

		// Порядок обратен объявлению: сначала фигура становится в начало
		// координат, затем ужимается, затем центрируется в значке.
		canvas.Translate(
			(size - bounds.Width * scale) * 0.5f,
			(size - bounds.Height * scale) * 0.5f);

		canvas.Scale(scale);
		canvas.Translate(-bounds.Left, -bounds.Top);

		float mmPerWorldUnit = MathF.Max(def.MmPerUnit, 0.0001f);

		foreach (var shape in shapes)
		{
			shape.IsSelected = false;
			shape.IsFocused = false;
			shape.Render(canvas, mmPerWorldUnit);
		}

		canvas.Flush();

		using var image = surface.Snapshot();
		using var data = image.Encode(SKEncodedImageFormat.Png, 100);
		using var stream = new MemoryStream(data.ToArray());

		return new Bitmap(stream);
	}

	/// <summary>
	/// Фигуры набора в его собственных единицах — для значка пересчёт
	/// не нужен, значок безразмерный.
	/// </summary>
	private static List<Shape> MaterializeForPreview(CustomShapeDefinition def)
	{
		var pageDto = new PageDto { Title = def.Name, Shapes = def.Shapes };

		var temp = new Document();

		foreach (var page in temp.pages.ToList())
			temp.PageRecorder.Detach(page);

		temp.pages.Clear();
		temp.ActivePage = null;

		var built = DocumentMapper.FromDto(pageDto, temp);

		var result = built.Shapes.ToList();
		built.Shapes.Clear();

		return result;
	}

	#endregion
}
