using SkiaSharp;

namespace VDraw;

/// <summary>
/// Перетаскивание файлов на холст.
///
/// Приём тот же, что у импорта из меню: разбор по расширению и те же
/// импортёры. Разница одна — указана точка, и всё, что умеет ложиться
/// в точку (картинки и SVG), ложится туда, где отпустили клавишу мыши.
/// Остальное — PDF, документы, клипарты — вставляется так же, как
/// из меню: место для них определяет сам импортёр.
/// </summary>
public partial class MainForm
{
	/// <summary>
	/// Расширения, которые принимает перетаскивание.
	///
	/// Список, а не «берём всё»: разбор по расширению в ImportFiles
	/// отправляет незнакомый файл в чтение документа VDraw, и брошенный
	/// на холст текстовый файл отвечал бы окном с ошибкой разбора.
	/// Лучше не принять его вовсе — курсор сразу покажет, что файл
	/// не подходит.
	/// </summary>
	private static readonly string[] DroppableExtensions =
	{
		".svg",
		".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
		".pdf",
		".vdr", ".json",
		".vdrs",
		".vsdx", ".vsdm", ".vss", ".vssx", ".vssm", ".vsx", ".vdx"
	};

	/// <summary>
	/// Принимаем ли то, что сейчас тащат над холстом.
	///
	/// Решение принимается однажды, на входе, и дальше только читается:
	/// Windows спрашивает о действии на каждом движении мыши, а перебор
	/// файлов с проверкой их существования на каждое движение — лишняя
	/// работа, тем более на пакете из сотен файлов.
	/// </summary>
	private bool _dropAccepted;

	private void InitFileDrop()
	{
		if (_mainView == null)
			return;

		_mainView.AllowDrop = true;

		_mainView.DragEnter += OnCanvasDragEnter;
		_mainView.DragOver += OnCanvasDragOver;
		_mainView.DragLeave += (_, _) => _dropAccepted = false;
		_mainView.DragDrop += OnCanvasDragDrop;
	}

	private void OnCanvasDragEnter(object? sender, DragEventArgs e)
	{
		_dropAccepted = CurrentPage != null && DroppedFiles(e.Data).Count > 0;

		e.Effect = _dropAccepted ? DragDropEffects.Copy : DragDropEffects.None;
	}

	/// <summary>
	/// Вид курсора над холстом. Без этого обработчика Windows на первом
	/// же движении сбрасывает действие в «нельзя», что бы ни ответил
	/// DragEnter.
	/// </summary>
	private void OnCanvasDragOver(object? sender, DragEventArgs e)
	{
		e.Effect = _dropAccepted ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private void OnCanvasDragDrop(object? sender, DragEventArgs e)
	{
		if (_mainView == null || CurrentPage == null)
			return;

		_dropAccepted = false;

		var files = DroppedFiles(e.Data);

		if (files.Count == 0)
			return;

		// Координаты в событии — экранные, а импорту нужны координаты
		// листа, поэтому сначала в клиентские координаты холста.
		var client = _mainView.PointToClient(new Point(e.X, e.Y));
		var at = _mainView.ScreenToPage(client);

		// Правка текста на листе закрывается до вставки: она держит
		// свой шаг истории и своё выделение, а импорт меняет и то,
		// и другое.
		_mainView.EndTextEdit();

		// Фокус на холст: вставка — шаг истории, и Ctrl+Z сразу после
		// перетаскивания должен отменять именно его, а не уходить
		// в контрол, на котором фокус остался до перетаскивания.
		_mainView.Focus();

		// Импорт откладывается на следующий проход сообщений, а не идёт
		// здесь же. Пока обработчик сброса не вернулся, перетаскивание
		// для системы ещё не закончено, и модальное окно из импорта —
		// выбор страниц PDF, сообщение о нечитаемом файле — повисло бы
		// поверх проводника, подвесив и его, и нас.
		BeginInvoke(() => ImportFiles(files, at));
	}

	/// <summary>
	/// Подходящие файлы из пакета перетаскивания. Пустой список означает
	/// и «это не файлы», и «файлы не того вида» — для вызывающего это
	/// одно и то же.
	/// </summary>
	private static List<string> DroppedFiles(IDataObject? data)
	{
		var result = new List<string>();

		if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
			return result;

		if (data.GetData(DataFormats.FileDrop) is not string[] paths)
			return result;

		foreach (string path in paths)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				continue;

			string extension = Path.GetExtension(path);

			foreach (string allowed in DroppableExtensions)
			{
				if (!string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
					continue;

				result.Add(path);

				break;
			}
		}

		return result;
	}
}
