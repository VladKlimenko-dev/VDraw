using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

[Serializable]
public class PaperFormatData
{
	public PaperFormat? paper_format;
	public UoM? uom;
	public string title = "Untitled";

	/// <summary>Document size in current UoM. No scaling applied.</summary>
	public SKSize size_uom = new SKSize(100, 100);

	public int DPI { get; set; } = 96;
	public PageOrientation Orientation { get; set; } = PageOrientation.Vertical;
	public SKColorF background_color = Settings.page_default_background_color;
	public bool LockRatio = false;
	public bool PrintBordersEnabled = false;
	public bool GridEnabled = false;

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

	public PaperFormatData ClonePaperFormatData()
	{
		return new PaperFormatData
		{
			paper_format = paper_format,
			uom = uom,
			size_uom = size_uom,
			DPI = DPI,
			Orientation = Orientation,
			background_color = background_color,
			LockRatio = LockRatio,
			GridEnabled = GridEnabled,
			PrintBordersEnabled = PrintBordersEnabled
		};
	}
}

[Serializable]
public sealed partial class Document : PaperFormatData
{
	public List<Page> pages = new();
	public string file_path_name = string.Empty;
	public string FileName { get; set; } = "Untitled.vdr";
	public int PagesQuantity => pages.Count;
	public Page? ActivePage { get; set; }

	/// <summary>If true - the document require saving before closing.</summary>
	public bool need_save = false;

	/// <summary>
	/// Кастомные фигуры, созданные в этом документе.
	///
	/// Живут вместе с файлом: у получателя в Resources\Shapes их нет,
	/// и без копии внутри документа меню «Custom shapes» у него было бы
	/// пустым. В его собственную библиотеку они при этом не переносятся.
	///
	/// Наборы из клипартов Visio сюда не попадают — они внешние и
	/// существуют только на время сеанса.
	/// </summary>
	public List<CustomShapeDefinition> CustomShapes { get; } = new();

	#region История действий

	/// <summary>
	/// История отмены на документ. Не сериализуется: после загрузки файла
	/// откатывать нечего, а старые шаги ссылались бы на фигуры закрытого документа.
	/// </summary>
	[JsonIgnore]
	public UndoStack Undo { get; } = new UndoStack
	{
		MemoryBudgetBytes = 256L * 1024 * 1024
	};

	/// <summary>Записывает добавление и удаление фигур по событиям страниц.</summary>
	[JsonIgnore]
	private PageUndoRecorder? _pageRecorder;

	[JsonIgnore]
	public PageUndoRecorder PageRecorder => _pageRecorder ??= new PageUndoRecorder(Undo);

	/// <summary>Диспетчер истории для операций перетаскивания. Используется MainView.</summary>
	[JsonIgnore]
	private OperationUndoRecorder? _operationRecorder;

	[JsonIgnore]
	public OperationUndoRecorder OperationRecorder => _operationRecorder ??= new OperationUndoRecorder(Undo);

	#endregion

	[JsonIgnore]
	public static string DefaultDocumentsFolder =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			"VDraw");

	[JsonIgnore]
	public bool IsNewDocument => string.IsNullOrWhiteSpace(file_path_name);

	public Document()
	{
		CreateDefaultPage();
	}

	public void AddPage(Page page)
	{
		if (!pages.Contains(page))
			pages.Add(page);

		// Подписка на события страницы — с этого момента добавление и удаление
		// фигур попадает в историю автоматически.
		PageRecorder.Attach(page);

		if (ActivePage == null)
			ActivePage = page;
	}

	/// <summary>
	/// Вставляет страницу на заданное место.
	///
	/// Нужна отмене удаления и перестановки: AddPage всегда добавляет
	/// в конец, а возвращённая не на своё место страница выглядела бы
	/// как вторая ошибка вдогонку первой.
	/// </summary>
	public void InsertPage(int index, Page page)
	{
		if (page == null || pages.Contains(page))
			return;

		pages.Insert(Math.Clamp(index, 0, pages.Count), page);

		// Подписка восстанавливается вместе со страницей: RemovePage её
		// сняла, и без этого добавление фигур на возвращённую страницу
		// перестало бы попадать в историю.
		PageRecorder.Attach(page);

		ActivePage ??= page;
	}

	public bool RemovePage(Page page)
	{
		if (page == null)
			return false;

		PageRecorder.Detach(page);

		bool removed = pages.Remove(page);

		if (removed && ReferenceEquals(ActivePage, page))
			ActivePage = pages.Count > 0 ? pages[0] : null;

		return removed;
	}

	public Page CreateDefaultPage()
	{
		paper_format ??= Settings.paper_formats.FirstOrDefault(p => p.title == "A4")
			?? new PaperFormat("A4", 210, 297);

		uom ??= Settings.units.FirstOrDefault(u => u.title == "mm")
			?? new UoM("mm", 1f);

		var page = new Page(paper_format, uom, DPI, 0, 0, PageOrientation.Vertical, this)
		{
			title = $"Page {pages.Count + 1}",
			Orientation = Orientation
		};

		AddPage(page);
		return page;
	}

	public bool Save()
	{
		if (IsNewDocument)
			return SaveAs();

		try
		{
			string? dir = Path.GetDirectoryName(file_path_name);
			if (string.IsNullOrWhiteSpace(dir))
				dir = DefaultDocumentsFolder;

			Directory.CreateDirectory(dir);

			NormalizeFileNames();

			string json = Serialize();
			File.WriteAllText(file_path_name, json);

			need_save = false;
			return true;
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to save document.\r\n\r\n{ex.Message}",
				"Save error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return false;
		}
	}

	public bool SaveAs()
	{
		try
		{
			Directory.CreateDirectory(DefaultDocumentsFolder);

			using SaveFileDialog sfd = new SaveFileDialog();
			sfd.Title = "Save VDraw document";
			sfd.InitialDirectory = DefaultDocumentsFolder;
			sfd.Filter = "VDraw document (*.vdr)|*.vdr|JSON files (*.json)|*.json|All files (*.*)|*.*";
			sfd.DefaultExt = "vdr";
			sfd.AddExtension = true;
			sfd.OverwritePrompt = true;
			sfd.FileName = BuildSuggestedFileName();

			if (sfd.ShowDialog() != DialogResult.OK)
				return false;

			file_path_name = sfd.FileName;
			FileName = Path.GetFileName(file_path_name);
			title = Path.GetFileNameWithoutExtension(file_path_name);

			string? dir = Path.GetDirectoryName(file_path_name);
			if (!string.IsNullOrWhiteSpace(dir))
				Directory.CreateDirectory(dir);

			string json = Serialize();
			File.WriteAllText(file_path_name, json);

			need_save = false;
			return true;
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to save document.\r\n\r\n{ex.Message}",
				"Save error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);

			return false;
		}
	}

	public bool Close()
	{
		if (!need_save)
		{
			ReleaseResources();
			return true;
		}

		DialogResult dr = MessageBox.Show(
			"The document has unsaved changes.\r\nDo you want to save it before closing?",
			"Save document",
			MessageBoxButtons.YesNoCancel,
			MessageBoxIcon.Question);

		if (dr == DialogResult.Cancel)
			return false;

		if (dr == DialogResult.No)
		{
			ReleaseResources();
			return true;
		}

		if (!Save())
			return false;

		ReleaseResources();
		return true;
	}

	/// <summary>
	/// Освобождает всё, что удерживает документ: историю (в ней лежат снимки
	/// тайлов и удалённые фигуры) и растровые ресурсы изображений на страницах.
	/// </summary>
	public void ReleaseResources()
	{
		Undo.Clear();
		PageRecorder.DetachAll();

		foreach (var page in pages)
		{
			foreach (var shape in page.Shapes)
			{
				if (shape is ImageShape img)
					img.DisposeImageResources();
			}
		}
	}

	/// <summary>
	/// Полное содержимое документа в JSON: настройки листа, страницы, фигуры
	/// и встроенные растры.
	///
	/// Путь к файлу в содержимое не входит. Он у файла и так есть, а записанный
	/// внутрь начинает врать, как только документ переименуют или перенесут.
	/// </summary>
	public string Serialize()
	{
		NormalizeFileNames();

		var dto = DocumentMapper.ToDto(this);
		return JsonSerializer.Serialize(dto, DocumentJson.CreateOptions());
	}

	public static Document Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("File path is empty.", nameof(path));

		string json = File.ReadAllText(path);

		var dto = JsonSerializer.Deserialize<DocumentFileDto>(json, DocumentJson.CreateOptions());
		if (dto == null)
			throw new InvalidOperationException("Failed to deserialize document.");

		Document doc = new Document();

		// Страницы, созданные конструктором, отписываем от истории,
		// иначе PageUndoRecorder останется подписан на выброшенные объекты.
		foreach (var page in doc.pages.ToList())
			doc.PageRecorder.Detach(page);

		doc.pages.Clear();
		doc.ActivePage = null;

		DocumentMapper.Populate(doc, dto);

		if (string.IsNullOrWhiteSpace(doc.title))
			doc.title = Path.GetFileNameWithoutExtension(path);

		// Путь и имя берутся у файла, а не из его содержимого: документ могли
		// переименовать или скопировать, и записанный внутрь путь указывал бы
		// на чужое место.
		doc.file_path_name = path;
		doc.FileName = Path.GetFileName(path);

		doc.need_save = false;

		// Загрузка не должна оказаться отменяемой: история начинается с чистого листа.
		doc.Undo.Clear();

		return doc;
	}

	/// <summary>
	/// Читает файл документа, не создавая документа. Нужен импорту:
	/// он берёт из файла только фигуры.
	/// </summary>
	public static DocumentFileDto ReadFile(string path)
	{
		string json = File.ReadAllText(path);

		return JsonSerializer.Deserialize<DocumentFileDto>(json, DocumentJson.CreateOptions())
			?? throw new InvalidOperationException("Failed to deserialize document.");
	}

	private void NormalizeFileNames()
	{
		if (string.IsNullOrWhiteSpace(title))
			title = "Untitled";

		if (!string.IsNullOrWhiteSpace(file_path_name))
		{
			FileName = Path.GetFileName(file_path_name);
			title = Path.GetFileNameWithoutExtension(file_path_name);
		}
		else if (string.IsNullOrWhiteSpace(FileName))
		{
			FileName = title + ".vdr";
		}
	}

	private string BuildSuggestedFileName()
	{
		string name = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();

		foreach (char c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');

		if (string.IsNullOrWhiteSpace(name))
			name = "Untitled";

		return name + ".vdr";
	}

}