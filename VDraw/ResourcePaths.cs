using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Единая точка разрешения путей к растровым ресурсам.
///
/// До этого в проекте было четыре несовместимых конвенции:
///   PatternLibrary   -> BaseDirectory\Patterns          (папки не существует)
///   TextureLibrary   -> BaseDirectory\Textures          (папки не существует)
///   Settings         -> AssemblyLocation\Brushes        (папки не существует)
///   csproj копировал -> BaseDirectory\Resources\Brushes (файлы лежат здесь)
///
/// Теперь всё строится от Resources в каталоге сборки, а csproj копирует
/// Resources целиком одним wildcard-правилом.
/// </summary>
public static class ResourcePaths
{
	public static string Root =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

	/// <summary>
	/// Нативные библиотеки: pdfium.dll и всё, что придёт ей на смену.
	///
	/// Рядом с Resources, а не внутри: Resources — это содержимое
	/// рисунка (кисти, текстуры, наборы фигур), его можно чистить и
	/// пополнять руками, а удалённая оттуда dll ломает приложение.
	/// Соседняя папка разделяет эти две судьбы.
	///
	/// Каталог сборки, а не AppData: библиотека — часть поставки,
	/// её версия обязана совпадать с версией исполняемого файла.
	/// </summary>
	public static string Libraries =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libraries");

	public static string Patterns => Path.Combine(Root, "Patterns");
	public static string Textures => Path.Combine(Root, "Textures");
	public static string Brushes => Path.Combine(Root, "Brushes");

	/// <summary>Пользовательские наборы фигур, файлы *.vdrs и их подпапки.</summary>
	public static string Shapes => Path.Combine(Root, "Shapes");

	/// <summary>Расширения, которые считаем растровым ресурсом.</summary>
	public static readonly string[] ImageExtensions =
		{ ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

	public static bool IsImageFile(string path)
	{
		string ext = Path.GetExtension(path);

		foreach (var e in ImageExtensions)
		{
			if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Перечисляет растровые файлы в папке. Возвращает пустую последовательность,
	/// если папки нет — вместо исключения из Directory.GetFiles.
	/// </summary>
	public static IEnumerable<string> EnumerateImages(string folder)
	{
		if (!Directory.Exists(folder))
			return Array.Empty<string>();

		return Directory
			.EnumerateFiles(folder)
			.Where(IsImageFile)
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Путь к одиночному файлу внутри Resources.</summary>
	public static string File(string relativePath) => Path.Combine(Root, relativePath);

	/// <summary>Создаёт папки, в которые приложение записывает пользовательские ресурсы.</summary>
	public static void EnsureWritableFolders()
	{
		Directory.CreateDirectory(Patterns);
		Directory.CreateDirectory(Textures);
		Directory.CreateDirectory(Shapes);
	}
}

/// <summary>
/// Библиотека паттернов заливки.
///
/// Исправления: путь берётся из ResourcePaths, поддерживаются все растровые
/// расширения (раньше только *.png), при отсутствии папки не падаем,
/// и — главное — если своя папка Patterns пуста, подхватываются заготовки
/// из Resources\Brushes, где физически лежат pat_brush_image_*.png.
/// Именно из-за их отсутствия выпадающий список паттернов был пуст.
/// </summary>
public static class PatternLibrary
{
	public static string FolderPath => ResourcePaths.Patterns;

	private static readonly List<PatternInfo> _items = new();
	public static IReadOnlyList<PatternInfo> Items => _items;

	/// <summary>Префикс имён файлов-заготовок в Resources\Brushes.</summary>
	private const string LegacyPatternPrefix = "pat_brush_image_";

	public static void Initialize()
	{
		ResourcePaths.EnsureWritableFolders();
		Reload();
	}

	public static void Reload()
	{
		foreach (var p in _items)
			p.Bitmap?.Dispose();

		_items.Clear();

		foreach (var file in ResourcePaths.EnumerateImages(FolderPath))
			TryAdd(file);

		// Обратная совместимость: заготовки из Resources\Brushes.
		foreach (var file in ResourcePaths.EnumerateImages(ResourcePaths.Brushes))
		{
			string name = Path.GetFileNameWithoutExtension(file);

			if (!name.StartsWith(LegacyPatternPrefix, StringComparison.OrdinalIgnoreCase))
				continue;

			if (_items.Any(x => x.Id.Equals(name, StringComparison.OrdinalIgnoreCase)))
				continue;

			TryAdd(file);
		}
	}

	private static void TryAdd(string file)
	{
		try
		{
			var bmp = SKBitmap.Decode(file);
			if (bmp == null)
				return;

			string name = Path.GetFileNameWithoutExtension(file);

			_items.Add(new PatternInfo
			{
				Id = name,
				FileName = file,
				DisplayName = name,
				Bitmap = bmp
			});
		}
		catch
		{
			// Битый файл просто пропускаем — библиотека не должна ронять запуск.
		}
	}

	public static PatternInfo? Find(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return _items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
	}

	public static string SaveIfNotExists(SKBitmap bmp, string baseId)
	{
		Directory.CreateDirectory(FolderPath);

		using var image = SKImage.FromBitmap(bmp);
		using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
		byte[] newBytes = encoded.ToArray();

		foreach (var item in _items)
		{
			try
			{
				if (System.IO.File.ReadAllBytes(item.FileName).AsSpan().SequenceEqual(newBytes))
					return item.Id;
			}
			catch
			{
			}
		}

		string id = NormalizeId(baseId);
		if (string.IsNullOrWhiteSpace(id))
			id = "pattern";

		string file = Path.Combine(FolderPath, id + ".png");
		int i = 1;
		while (System.IO.File.Exists(file))
		{
			file = Path.Combine(FolderPath, $"{id}_{i}.png");
			i++;
		}

		System.IO.File.WriteAllBytes(file, newBytes);
		Reload();

		return Path.GetFileNameWithoutExtension(file);
	}

	private static string NormalizeId(string s)
	{
		var chars = (s ?? string.Empty)
			.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
			.ToArray();

		return new string(chars).Trim();
	}
}

/// <summary>
/// Библиотека текстур. Те же исправления путей, плюс подхват заготовок
/// tex_image_*.png из Resources\Brushes.
/// </summary>
public static class TextureLibrary
{
	public static string FolderPath => ResourcePaths.Textures;

	private static readonly List<TextureInfo> _items = new();
	public static IReadOnlyList<TextureInfo> Items => _items;

	private const string LegacyTexturePrefix = "tex_image_";

	public static void Initialize()
	{
		ResourcePaths.EnsureWritableFolders();
		Reload();
	}

	public static void Reload()
	{
		foreach (var t in _items)
			t.Bitmap?.Dispose();

		_items.Clear();

		foreach (var file in ResourcePaths.EnumerateImages(FolderPath))
			TryAdd(file);

		foreach (var file in ResourcePaths.EnumerateImages(ResourcePaths.Brushes))
		{
			string name = Path.GetFileNameWithoutExtension(file);

			if (!name.StartsWith(LegacyTexturePrefix, StringComparison.OrdinalIgnoreCase))
				continue;

			if (_items.Any(x => x.Id.Equals(name, StringComparison.OrdinalIgnoreCase)))
				continue;

			TryAdd(file);
		}
	}

	private static void TryAdd(string file)
	{
		try
		{
			var bmp = SKBitmap.Decode(file);
			if (bmp == null)
				return;

			string name = Path.GetFileNameWithoutExtension(file);

			_items.Add(new TextureInfo
			{
				Id = name,
				FileName = file,
				DisplayName = name,
				Bitmap = bmp
			});
		}
		catch
		{
		}
	}

	public static TextureInfo? Find(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return _items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
	}

	public static string ImportTexture(string sourceFile)
	{
		Directory.CreateDirectory(FolderPath);

		string fileName = Path.GetFileName(sourceFile);
		string dest = Path.Combine(FolderPath, fileName);

		if (System.IO.File.Exists(dest))
		{
			string name = Path.GetFileNameWithoutExtension(fileName);
			string ext = Path.GetExtension(fileName);
			int i = 1;

			while (System.IO.File.Exists(dest))
			{
				dest = Path.Combine(FolderPath, $"{name}_{i}{ext}");
				i++;
			}
		}

		System.IO.File.Copy(sourceFile, dest, false);
		Reload();
		return Path.GetFileNameWithoutExtension(dest);
	}
}
