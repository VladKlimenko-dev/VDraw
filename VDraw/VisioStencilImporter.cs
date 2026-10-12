using SkiaSharp;

using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

using VDraw.Shapes;

namespace VDraw.Documents;

/// <summary>
/// Импорт клипартов Visio.
///
/// Расширение .vss носят два несовместимых формата. Visio 2013 и новее
/// пишет ZIP-пакет с разметкой XML внутри — там же живут .vssx и .vsx.
/// Visio 2003 и старше пишет OLE-контейнер с двоичным потоком
/// VisioDocument. Разбор начинается с сигнатуры файла, а не с расширения:
/// на .vss встречается и то, и другое.
///
/// Загруженный набор НИКУДА не сохраняется: ни в документ, ни в
/// Resources\Shapes. Клипарт остаётся внешним файлом пользователя.
/// </summary>
public static class VisioStencilImporter
{
	private static readonly byte[] OleSignature =
		{ 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

	/// <summary>
	/// Читает файл клипарта. Возвращает наборы фигур; note содержит
	/// пояснение, если что-то прочитать не удалось.
	/// </summary>
	public static List<CustomShapeDefinition> Import(string path, out string? note)
		=> Import(path, out note, out _);

	/// <summary>
	/// Читает файл Visio.
	///
	/// isDrawing различает рисунок и трафарет. Различие не косметическое:
	/// рисунок — это готовый чертёж, и место ему на странице документа,
	/// а трафарет — библиотека заготовок, и его фигуры ждут в меню, пока
	/// их оттуда возьмут. Определяется оно по содержимому — есть ли
	/// в файле страницы, — а не по расширению.
	/// </summary>
	public static List<CustomShapeDefinition> Import(string path, out string? note, out bool isDrawing)
	{
		note = null;
		isDrawing = false;

		byte[] head = ReadHead(path, 8);

		if (IsZip(head))
			return VisioXmlStencil.Import(path, out note, out isDrawing);

		if (IsOle(head))
			return VisioBinaryStencil.Import(path, out note);

		// Visio 2003 умел писать трафарет одним XML-файлом без упаковки.
		if (IsXml(head))
			return VisioXmlStencil.ImportFlatFile(path, out note, out isDrawing);

		note = "unknown file format";
		return new List<CustomShapeDefinition>();
	}

	/// <summary>
	/// Похож ли файл на файл Visio.
	///
	/// Проверяется СОДЕРЖИМОЕ, а не расширение. Список расширений у Visio
	/// длинный и продолжает расти, а перепутанное расширение отправляет
	/// файл в разбор JSON, где он спотыкается о первый же байт — сообщение
	/// «'P' is an invalid start of a value» про букву из сигнатуры ZIP
	/// пользователю ничего не объясняет.
	///
	/// Проверка не ограничивается сигнатурой контейнера: ZIP — это ещё
	/// и docx, и сам по себе он ни о чём не говорит. Поэтому внутри
	/// ищется признак именно Visio.
	/// </summary>
	public static bool CanImport(string path)
	{
		try
		{
			byte[] head = ReadHead(path, 8);

			if (IsZip(head))
				return IsVisioPackage(path);

			if (IsOle(head))
				return IsVisioCompoundFile(path);

			return IsXml(head) && IsVisioMarkup(path);
		}
		catch
		{
			// Недоступный или битый файл — не наш; пусть с ним разбирается
			// тот, кому его отдадут дальше.
			return false;
		}
	}

	private static bool IsVisioPackage(string path)
	{
		using var archive = ZipFile.OpenRead(path);

		return archive.Entries.Any(e =>
			e.FullName.StartsWith("visio/", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsVisioCompoundFile(string path)
	{
		var ole = new OleCompoundFile(File.ReadAllBytes(path));

		return ole.GetStream("VisioDocument") != null;
	}

	private static bool IsVisioMarkup(string path)
	{
		using var reader = new StreamReader(path);

		var buffer = new char[2048];
		int read = reader.Read(buffer, 0, buffer.Length);

		string head = new string(buffer, 0, read);

		return head.Contains("VisioDocument", StringComparison.OrdinalIgnoreCase)
			|| head.Contains("schemas.microsoft.com/visio", StringComparison.OrdinalIgnoreCase)
			|| head.Contains("schemas.microsoft.com/office/visio", StringComparison.OrdinalIgnoreCase);
	}

	private static byte[] ReadHead(string path, int count)
	{
		using var stream = File.OpenRead(path);

		var buffer = new byte[count];
		int read = stream.Read(buffer, 0, count);

		return read == count ? buffer : Array.Empty<byte>();
	}

	private static bool IsZip(byte[] head)
		=> head.Length >= 2 && head[0] == 'P' && head[1] == 'K';

	private static bool IsXml(byte[] head)
	{
		foreach (byte c in head)
		{
			if (c == (byte)'<')
				return true;

			// Пробелы и метка порядка байтов до корневого узла допустимы,
			// всё остальное — уже не разметка.
			if (c > 0x20 && c < 0x80)
				return false;
		}

		return false;
	}

	private static bool IsOle(byte[] head)
	{
		if (head.Length < OleSignature.Length)
			return false;

		for (int i = 0; i < OleSignature.Length; i++)
		{
			if (head[i] != OleSignature[i])
				return false;
		}

		return true;
	}
}

#region Контейнер OLE

/// <summary>
/// Чтение составного файла OLE (Compound File Binary).
///
/// Реализовано целиком здесь, а не через System.IO.Packaging: тот умеет
/// OPC, то есть ZIP, а двоичные .vss и .vsd — это совсем другой контейнер.
/// Нужен из него ровно один поток, VisioDocument, поэтому дерево каталога
/// обходится линейно по массиву записей, без разбора красно-чёрного дерева:
/// имена нужны только для поиска, а не для восстановления иерархии.
/// </summary>
internal sealed class OleCompoundFile
{
	private const uint FreeSector = 0xFFFFFFFF;
	private const uint EndOfChain = 0xFFFFFFFE;
	private const uint MaxRegularSector = 0xFFFFFFFA;

	private readonly byte[] _data;
	private readonly int _sectorSize;
	private readonly int _miniSectorSize;
	private readonly uint _miniCutoff;

	private readonly uint[] _fat;
	private readonly uint[] _miniFat;
	private readonly byte[] _miniStream;

	private readonly List<DirectoryEntry> _entries = new();

	private sealed class DirectoryEntry
	{
		public string Name = string.Empty;
		public byte Type;
		public uint Start;
		public ulong Size;
	}

	public OleCompoundFile(byte[] data)
	{
		_data = data;

		_sectorSize = 1 << BitConverter.ToUInt16(data, 0x1E);
		_miniSectorSize = 1 << BitConverter.ToUInt16(data, 0x20);
		_miniCutoff = BitConverter.ToUInt32(data, 0x38);

		uint fatSectorCount = BitConverter.ToUInt32(data, 0x2C);
		uint dirStart = BitConverter.ToUInt32(data, 0x30);
		uint miniFatStart = BitConverter.ToUInt32(data, 0x3C);
		uint difatStart = BitConverter.ToUInt32(data, 0x44);
		uint difatCount = BitConverter.ToUInt32(data, 0x48);

		var difat = new List<uint>(109);

		for (int i = 0; i < 109; i++)
			difat.Add(BitConverter.ToUInt32(data, 0x4C + i * 4));

		uint sector = difatStart;

		for (uint i = 0; i < difatCount && sector < MaxRegularSector; i++)
		{
			int offset = SectorOffset(sector);
			int perSector = _sectorSize / 4;

			for (int k = 0; k < perSector - 1; k++)
				difat.Add(BitConverter.ToUInt32(data, offset + k * 4));

			sector = BitConverter.ToUInt32(data, offset + (perSector - 1) * 4);
		}

		var fat = new List<uint>();

		for (int i = 0; i < fatSectorCount && i < difat.Count; i++)
		{
			if (difat[i] >= MaxRegularSector)
				continue;

			int offset = SectorOffset(difat[i]);

			for (int k = 0; k < _sectorSize / 4; k++)
				fat.Add(BitConverter.ToUInt32(data, offset + k * 4));
		}

		_fat = fat.ToArray();

		var miniFat = new List<uint>();
		sector = miniFatStart;

		while (sector < MaxRegularSector)
		{
			int offset = SectorOffset(sector);

			for (int k = 0; k < _sectorSize / 4; k++)
				miniFat.Add(BitConverter.ToUInt32(data, offset + k * 4));

			sector = NextSector(sector);
		}

		_miniFat = miniFat.ToArray();

		ReadDirectory(dirStart);

		// Мини-поток целиком лежит в потоке корневой записи.
		var root = _entries.Count > 0 ? _entries[0] : null;

		_miniStream = root != null && root.Size > 0
			? ReadChain(root.Start, (int)root.Size)
			: Array.Empty<byte>();
	}

	private int SectorOffset(uint sector) => 512 + (int)sector * _sectorSize;

	private uint NextSector(uint sector)
		=> sector < _fat.Length ? _fat[sector] : EndOfChain;

	private void ReadDirectory(uint start)
	{
		byte[] raw = ReadChain(start, -1);

		for (int i = 0; i + 128 <= raw.Length; i += 128)
		{
			int nameLength = BitConverter.ToUInt16(raw, i + 64);

			string name = nameLength > 2
				? System.Text.Encoding.Unicode.GetString(raw, i, nameLength - 2)
				: string.Empty;

			_entries.Add(new DirectoryEntry
			{
				Name = name,
				Type = raw[i + 66],
				Start = BitConverter.ToUInt32(raw, i + 116),
				Size = BitConverter.ToUInt64(raw, i + 120)
			});
		}
	}

	private byte[] ReadChain(uint start, int size)
	{
		using var output = new MemoryStream();

		uint sector = start;
		var visited = new HashSet<uint>();

		while (sector < MaxRegularSector)
		{
			// Зацикленная цепочка встречается в повреждённых файлах
			// и без этой проверки означала бы бесконечный цикл.
			if (!visited.Add(sector))
				break;

			int offset = SectorOffset(sector);

			if (offset < 0 || offset + _sectorSize > _data.Length)
				break;

			output.Write(_data, offset, _sectorSize);
			sector = NextSector(sector);
		}

		byte[] result = output.ToArray();

		if (size < 0 || size >= result.Length)
			return result;

		var trimmed = new byte[size];
		Array.Copy(result, trimmed, size);

		return trimmed;
	}

	private byte[] ReadMiniChain(uint start, int size)
	{
		using var output = new MemoryStream();

		uint sector = start;
		var visited = new HashSet<uint>();

		while (sector < MaxRegularSector && output.Length < size)
		{
			if (!visited.Add(sector))
				break;

			int offset = (int)sector * _miniSectorSize;

			if (offset < 0 || offset + _miniSectorSize > _miniStream.Length)
				break;

			output.Write(_miniStream, offset, _miniSectorSize);
			sector = sector < _miniFat.Length ? _miniFat[sector] : EndOfChain;
		}

		byte[] result = output.ToArray();

		if (size >= result.Length)
			return result;

		var trimmed = new byte[size];
		Array.Copy(result, trimmed, size);

		return trimmed;
	}

	/// <summary>Содержимое потока по имени. null, если потока нет.</summary>
	public byte[]? GetStream(string name)
	{
		foreach (var entry in _entries)
		{
			if (entry.Type != 2 || !string.Equals(entry.Name, name, StringComparison.Ordinal))
				continue;

			int size = (int)Math.Min(entry.Size, int.MaxValue);

			return entry.Size < _miniCutoff
				? ReadMiniChain(entry.Start, size)
				: ReadChain(entry.Start, size);
		}

		return null;
	}
}

#endregion

#region Двоичный поток Visio

/// <summary>
/// Указатель на поток внутри VisioDocument.
///
/// Раскладка: тип, служебный адрес, смещение, длина, формат — всего
/// восемнадцать байт. Младший бит формата, кроме нулевого, означает
/// сжатие содержимого.
/// </summary>
internal readonly struct VisioPointer
{
	public readonly uint Type;
	public readonly uint Address;
	public readonly uint Offset;
	public readonly uint Length;
	public readonly ushort Format;

	public const int Size = 18;

	public VisioPointer(byte[] data, int at)
	{
		Type = BitConverter.ToUInt32(data, at);
		Address = BitConverter.ToUInt32(data, at + 4);
		Offset = BitConverter.ToUInt32(data, at + 8);
		Length = BitConverter.ToUInt32(data, at + 12);
		Format = BitConverter.ToUInt16(data, at + 16);
	}

	public bool IsCompressed => (Format & 2) == 2;

	public bool IsEmpty => Type == 0 || Offset == 0 || Length == 0;
}

/// <summary>
/// Разбор двоичного потока VisioDocument.
///
/// Формат нигде не опубликован; раскладка ниже выведена из файла и
/// проверена на нём структурно — каждая таблица указателей сходится
/// по длине с точностью до выравнивания.
/// </summary>
internal sealed class VisioBinaryDocument
{
	/// <summary>Смещение указателя на трейлер в заголовке потока.</summary>
	private const int TrailerPointerOffset = 0x24;

	private readonly byte[] _stream;

	public int Version { get; }

	public VisioBinaryDocument(byte[] stream)
	{
		_stream = stream;

		// Заголовок: строка «Visio (TM) Drawing», затем версия.
		Version = stream.Length > 0x1B ? BitConverter.ToUInt16(stream, 0x1A) : 0;
	}

	public VisioPointer Trailer => new(_stream, TrailerPointerOffset);

	/// <summary>Содержимое потока, на который смотрит указатель.</summary>
	public byte[] Read(VisioPointer pointer)
	{
		if (pointer.Offset + pointer.Length > _stream.Length)
			return Array.Empty<byte>();

		var raw = new byte[pointer.Length];
		Array.Copy(_stream, pointer.Offset, raw, 0, pointer.Length);

		return pointer.IsCompressed ? Decompress(raw) : raw;
	}

	#region Распаковка

	/// <summary>
	/// LZSS с окном в 4096 байт.
	///
	/// Флаговый байт описывает восемь следующих элементов, младшим битом
	/// вперёд: взведённый бит — литерал, сброшенный — ссылка из двух байт.
	/// Смещение собирается из старшего полубайта второго байта и первого
	/// целиком, длина — из младшего полубайта плюс три.
	///
	/// Начальная позиция окна — 4078, то есть 4096 минус максимальная длина
	/// совпадения. Это важнее, чем кажется: при нулевой позиции ссылки в
	/// начале потока читают не ту область окна, и таблица указателей
	/// рассыпается на середине, оставаясь при этом внешне правдоподобной.
	/// </summary>
	public static byte[] Decompress(byte[] data)
	{
		var window = new byte[4096];
		int position = 4078;

		using var output = new MemoryStream(data.Length * 3);

		int i = 0;

		while (i < data.Length)
		{
			byte flags = data[i++];

			for (int bit = 0; bit < 8 && i < data.Length; bit++)
			{
				if ((flags & (1 << bit)) != 0)
				{
					byte value = data[i++];

					window[position & 0xFFF] = value;
					position++;
					output.WriteByte(value);

					continue;
				}

				if (i + 1 >= data.Length)
					break;

				byte low = data[i++];
				byte high = data[i++];

				int source = ((high & 0xF0) << 4) | low;
				int length = (high & 0x0F) + 3;

				for (int k = 0; k < length; k++)
				{
					byte value = window[(source + k) & 0xFFF];

					window[position & 0xFFF] = value;
					position++;
					output.WriteByte(value);
				}
			}
		}

		return output.ToArray();
	}

	#endregion

	#region Таблицы указателей

	/// <summary>
	/// Указатели, лежащие внутри потока.
	///
	/// Размер заголовка таблицы зависит от типа потока и в самом потоке
	/// не записан. Вместо таблицы магических чисел на каждый тип он
	/// ВЫЧИСЛЯЕТСЯ: ищется такое смещение H, при котором за двенадцать
	/// байт до него лежит размер списка, за восемь — число указателей,
	/// а H + 18·указателей + 4·список совпадает с длиной потока
	/// с точностью до выравнивания. Условие жёсткое настолько, что
	/// ложных срабатываний на реальном файле не даёт, зато переживает
	/// типы потоков, которых в этом файле не встретилось.
	/// </summary>
	/// <summary>
	/// Указатели таблицы.
	///
	/// keepEmpty сохраняет пустые ячейки на своих местах. Это нужно там,
	/// где важна ПОЗИЦИЯ указателя: фигуры страницы ссылаются друг на
	/// друга её номером, и выброшенная пустая ячейка сдвинула бы всю
	/// нумерацию, развалив дерево групп.
	/// </summary>
	public static List<VisioPointer> ReadPointers(byte[] data, int fileLength, bool keepEmpty = false)
	{
		var result = new List<VisioPointer>();

		if (data.Length < 30)
			return result;

		int bestHeader = -1;
		int bestCount = 0;
		int bestScore = -1;

		for (int header = 12; header + VisioPointer.Size <= data.Length; header++)
		{
			uint listSize = BitConverter.ToUInt32(data, header - 12);
			uint count = BitConverter.ToUInt32(data, header - 8);

			if (count == 0 || count > 1000 || listSize > 1000)
				continue;

			long total = header + (long)VisioPointer.Size * count + 4L * listSize;

			// Хвост выравнивания в несколько байт — норма для этого формата.
			if (total > data.Length || total < data.Length - 8)
				continue;

			int score = 0;

			for (int k = 0; k < count; k++)
			{
				var pointer = new VisioPointer(data, header + k * VisioPointer.Size);

				if (pointer.IsEmpty)
				{
					// Пустые ячейки в таблицах — обычное дело.
					score++;
					continue;
				}

				if (pointer.Offset + pointer.Length <= fileLength && pointer.Format < 0x100)
					score++;
			}

			if (score > bestScore)
			{
				bestScore = score;
				bestHeader = header;
				bestCount = (int)count;
			}
		}

		if (bestHeader < 0)
			return result;

		for (int k = 0; k < bestCount; k++)
		{
			var pointer = new VisioPointer(data, bestHeader + k * VisioPointer.Size);

			if (keepEmpty || !pointer.IsEmpty)
				result.Add(pointer);
		}

		return result;
	}

	#endregion
}

#endregion

#region Точки входа по форматам

/// <summary>
/// Один чанк двоичного потока.
///
/// Заголовок — девятнадцать байт: тип, идентификатор, номер списка,
/// длина данных, уровень вложенности и служебный байт. За данными идёт
/// хвост: двенадцать байт у чанка со списком и четыре у остальных.
/// Правило выведено из файла — при нём цепочка чанков сходится с длиной
/// потока ровно, во всех сорока трёх мастерах разом.
/// </summary>
internal readonly struct VisioChunk
{
	public const int HeaderSize = 19;

	public readonly uint Type;
	public readonly uint Id;
	public readonly uint List;
	public readonly ushort Level;
	public readonly byte[] Data;

	public VisioChunk(uint type, uint id, uint list, ushort level, byte[] data)
	{
		Type = type;
		Id = id;
		List = list;
		Level = level;
		Data = data;
	}

	/// <summary>Значение ячейки: байт признака и следом восемь байт числа.</summary>
	public double Value(int index)
	{
		int offset = index * 9;

		return offset + 9 <= Data.Length
			? BitConverter.ToDouble(Data, offset + 1)
			: 0d;
	}

	public bool HasValues(int count) => Data.Length >= count * 9;
}

/// <summary>Типы чанков, которые нужны импорту.</summary>
internal static class VisioChunkType
{
	public const uint XForm = 0x9B;
	public const uint Line = 0x85;
	public const uint Fill = 0x86;
	public const uint Geometry = 0x89;
	public const uint MoveTo = 0x8A;
	public const uint LineTo = 0x8B;
	public const uint EllipticalArcTo = 0x90;
}

/// <summary>Разбор оформления фигуры.</summary>
internal static class VisioStyle
{
	/// <summary>
	/// Цвет занимает пять байт: номер в палитре, три составляющих и альфа.
	///
	/// Берутся именно составляющие, а не номер: у фигур этого клипарта
	/// номера идут вразнобой (0x88, 0x94, 0x18), а RGB рядом с ними
	/// осмысленный и одинаковый — синий 46 62 d2. Палитра документа при
	/// этом лежит в другом потоке, и полагаться на номер значило бы
	/// тянуть её ради значения, которое уже под рукой.
	/// </summary>
	public static SKColor ReadColour(byte[] data, int offset)
	{
		if (offset + 4 > data.Length)
			return SKColors.Black;

		return new SKColor(data[offset + 1], data[offset + 2], data[offset + 3]);
	}
}

/// <summary>
/// Набор свойств OLE — потоки SummaryInformation и им подобные.
///
/// Нужен ради одной вещи: имён мастеров. В самом VisioDocument они лежат
/// в общей таблице имён вперемешку со служебными («Guide», «TheDoc»,
/// «Page-1», названия стилей), и какое из них принадлежит мастеру,
/// оттуда не видно. А DocumentSummaryInformation хранит их разложенными
/// по разделам: пары «раздел — сколько в нём частей» в свойстве 12
/// и сами названия подряд в свойстве 13. Раздел «Masters» и даёт
/// имена мастеров в том же порядке, в каком идут страницы трафарета.
/// </summary>
internal static class OlePropertySet
{
	private const int HeadingPairProperty = 12;
	private const int PartNamesProperty = 13;
	private const int CodePageProperty = 1;

	private const uint VtI2 = 0x0002;
	private const uint VtI4 = 0x0003;
	private const uint VtLpstr = 0x001E;
	private const uint VtLpwstr = 0x001F;
	private const uint VtVectorVariant = 0x100C;
	private const uint VtVectorLpstr = 0x101E;
	private const uint VtVectorLpwstr = 0x101F;

	/// <summary>
	/// Названия частей заданного раздела. Пустой список, если потока нет
	/// или раздела в нём не оказалось.
	/// </summary>
	public static List<string> ReadPartNames(byte[]? data, string heading)
	{
		var result = new List<string>();

		if (data == null || data.Length < 28)
			return result;

		try
		{
			foreach (int section in Sections(data))
			{
				var properties = Properties(data, section);

				if (!properties.TryGetValue(HeadingPairProperty, out int headingAt) ||
					!properties.TryGetValue(PartNamesProperty, out int namesAt))
					continue;

				int codePage = ReadCodePage(data, properties);

				var pairs = ReadVariantVector(data, headingAt, codePage);
				var names = ReadStringVector(data, namesAt, codePage);

				// Пары идут парами: название раздела и число частей в нём.
				// Смещение раздела в общем списке названий — это сумма
				// длин всех разделов до него.
				int start = 0;

				for (int i = 0; i + 1 < pairs.Count; i += 2)
				{
					string title = pairs[i] as string ?? string.Empty;
					int count = pairs[i + 1] as int? ?? 0;

					if (string.Equals(title, heading, StringComparison.OrdinalIgnoreCase))
					{
						for (int k = start; k < start + count && k < names.Count; k++)
							result.Add(names[k]);

						return result;
					}

					start += count;
				}
			}
		}
		catch
		{
			// Свойства — вещь необязательная: без имён импорт всё равно
			// состоится, просто набор назовётся по имени файла.
			return result;
		}

		return result;
	}

	private static IEnumerable<int> Sections(byte[] data)
	{
		uint count = BitConverter.ToUInt32(data, 24);

		for (uint i = 0; i < count && 28 + i * 20 + 20 <= data.Length; i++)
		{
			int at = (int)(28 + i * 20);

			yield return (int)BitConverter.ToUInt32(data, at + 16);
		}
	}

	private static Dictionary<int, int> Properties(byte[] data, int section)
	{
		var result = new Dictionary<int, int>();

		if (section < 0 || section + 8 > data.Length)
			return result;

		uint count = BitConverter.ToUInt32(data, section + 4);

		for (uint i = 0; i < count && section + 8 + i * 8 + 8 <= data.Length; i++)
		{
			int at = (int)(section + 8 + i * 8);

			int id = (int)BitConverter.ToUInt32(data, at);
			int offset = (int)BitConverter.ToUInt32(data, at + 4);

			result[id] = section + offset;
		}

		return result;
	}

	private static int ReadCodePage(byte[] data, Dictionary<int, int> properties)
	{
		if (!properties.TryGetValue(CodePageProperty, out int at) || at + 6 > data.Length)
			return 1252;

		if (BitConverter.ToUInt32(data, at) != VtI2)
			return 1252;

		// Значение знаковое: кодовые страницы выше 32767 записаны
		// отрицательными числами.
		int page = BitConverter.ToInt16(data, at + 4);

		return page <= 0 ? 1252 : page;
	}

	private static List<object> ReadVariantVector(byte[] data, int at, int codePage)
	{
		var result = new List<object>();

		if (at + 8 > data.Length || BitConverter.ToUInt32(data, at) != VtVectorVariant)
			return result;

		uint count = BitConverter.ToUInt32(data, at + 4);
		int pos = at + 8;

		for (uint i = 0; i < count && pos + 4 <= data.Length; i++)
		{
			uint type = BitConverter.ToUInt32(data, pos);
			pos += 4;

			switch (type)
			{
				case VtLpstr:
				case VtLpwstr:
					result.Add(ReadString(data, ref pos, type == VtLpwstr, codePage));
					break;

				case VtI4:
					if (pos + 4 > data.Length)
						return result;

					result.Add(BitConverter.ToInt32(data, pos));
					pos += 4;
					break;

				default:
					// Неизвестный тип означает, что дальше разбирать нечем:
					// длина значения неизвестна, и шагнуть некуда.
					return result;
			}
		}

		return result;
	}

	private static List<string> ReadStringVector(byte[] data, int at, int codePage)
	{
		var result = new List<string>();

		if (at + 8 > data.Length)
			return result;

		uint type = BitConverter.ToUInt32(data, at);

		if (type != VtVectorLpstr && type != VtVectorLpwstr)
			return result;

		uint count = BitConverter.ToUInt32(data, at + 4);
		int pos = at + 8;

		for (uint i = 0; i < count && pos + 4 <= data.Length; i++)
			result.Add(ReadString(data, ref pos, type == VtVectorLpwstr, codePage));

		return result;
	}

	private static string ReadString(byte[] data, ref int pos, bool wide, int codePage)
	{
		int length = (int)BitConverter.ToUInt32(data, pos);
		pos += 4;

		if (length <= 0)
			return string.Empty;

		int bytes = wide ? length * 2 : length;

		if (pos + bytes > data.Length)
		{
			pos = data.Length;
			return string.Empty;
		}

		string text = wide
			? System.Text.Encoding.Unicode.GetString(data, pos, bytes)
			: Decode(data, pos, bytes, codePage);

		pos += bytes;

		// Длина считает и завершающий ноль, и хвост за ним бывает мусорным.
		int end = text.IndexOf('\0');

		return end >= 0 ? text[..end] : text;
	}

	/// <summary>
	/// Разбор строки в её кодовой странице.
	///
	/// Однобайтовые страницы вроде 1251 в .NET из коробки недоступны —
	/// за ними стоит отдельный пакет System.Text.Encoding.CodePages,
	/// тянуть который ради имён мастеров несоразмерно. Поэтому попытка
	/// делается, а при неудаче берётся Latin1: с латиницей он совпадает
	/// с 1252 байт в байт, а имена мастеров почти всегда латинские.
	/// </summary>
	private static string Decode(byte[] data, int at, int count, int codePage)
	{
		switch (codePage)
		{
			case 1200:
				return System.Text.Encoding.Unicode.GetString(data, at, count);

			case 65001:
				return System.Text.Encoding.UTF8.GetString(data, at, count);
		}

		try
		{
			return System.Text.Encoding.GetEncoding(codePage).GetString(data, at, count);
		}
		catch
		{
			return System.Text.Encoding.Latin1.GetString(data, at, count);
		}
	}
}

/// <summary>
/// Фигура клипарта в разобранном виде: место в дереве, преобразование
/// и собственные чанки.
/// </summary>
internal sealed class VisioShapeRecord
{
	public int Id;
	public int ParentId;
	public bool IsGroup;

	/// <summary>
	/// Есть ли у фигуры узел преобразования.
	///
	/// Признак нужен потому, что VisioTransform — структура: без своего
	/// конструктора она получает нулевые поля, а нулевой косинус означал бы
	/// поворот на прямой угол. Фигуру без узла лучше оставить в координатах
	/// родителя, чем развернуть её на ровном месте.
	/// </summary>
	public bool HasTransform;

	public VisioTransform Transform;
	public List<VisioChunk> Chunks = new();
}

/// <summary>
/// Двоичный клипарт Visio 2003 и старше.
/// </summary>
internal static class VisioBinaryStencil
{
	/// <summary>Тип указателя на список трафаретов.</summary>
	private const uint StencilsPointer = 0x1D;

	/// <summary>
	/// Тип указателя на страницу трафарета.
	///
	/// Проверяется точно: рядом в списке лежат указатели 0x3f, 0x44 и 0xc9 —
	/// служебные таблицы, а не страницы.
	/// </summary>
	private const uint StencilPagePointer = 0x1E;

	/// <summary>Указатель на группу и на обычную фигуру.</summary>
	private const uint GroupPointer = 0x47;
	private const uint ShapePointer = 0x48;

	/// <summary>
	/// Смещение родителя в заголовке фигуры.
	///
	/// Именно родителя, а не собственного номера: у всех детей одной группы
	/// здесь стоит одно и то же число, совпадающее с номером группы,
	/// а у самой группы — номер её собственного родителя. Номер фигуры —
	/// это её позиция в таблице указателей страницы.
	/// </summary>
	private const int ParentIdOffset = 10;

	/// <summary>
	/// Геометрия Visio задана в дюймах, и в них же остаётся в наборе.
	/// Пересчёт в единицы страницы делает вставка — по MmPerUnit.
	/// </summary>
	private const float MmPerInch = 25.4f;

	public static List<CustomShapeDefinition> Import(string path, out string? note)
	{
		var result = new List<CustomShapeDefinition>();

		byte[] file = File.ReadAllBytes(path);

		var ole = new OleCompoundFile(file);
		byte[]? stream = ole.GetStream("VisioDocument");

		if (stream == null)
		{
			note = "the file has no VisioDocument stream";
			return result;
		}

		var document = new VisioBinaryDocument(stream);

		string baseName = Path.GetFileNameWithoutExtension(path);

		// Имена мастеров идут в том же порядке, что и страницы трафарета.
		var names = OlePropertySet.ReadPartNames(
			ole.GetStream("\u0005DocumentSummaryInformation"), "Masters");

		int skipped = 0;
		int index = 0;

		foreach (var page in EnumeratePages(document, stream.Length))
		{
			string name = index < names.Count && !string.IsNullOrWhiteSpace(names[index])
				? names[index]
				: FallbackName(baseName, index);

			index++;

			var def = BuildPage(document, stream.Length, page, name, ref skipped);

			if (def != null)
				result.Add(def);
		}

		note = skipped > 0
			? $"{skipped} shape(s) carried no vector geometry and were skipped"
			: null;

		return result;
	}

	/// <summary>
	/// Имя мастера, когда его нет в свойствах файла. Единственный мастер
	/// зовётся по имени файла, а нескольким приходится добавлять номер.
	/// </summary>
	private static string FallbackName(string baseName, int index)
		=> index == 0 ? baseName : $"{baseName} {index + 1}";

	#region Обход дерева потоков

	private static IEnumerable<VisioPointer> EnumeratePages(VisioBinaryDocument document, int fileLength)
	{
		byte[] trailer = document.Read(document.Trailer);

		if (trailer.Length == 0)
			yield break;

		foreach (var pointer in VisioBinaryDocument.ReadPointers(trailer, fileLength))
		{
			if (pointer.Type != StencilsPointer)
				continue;

			byte[] stencils = document.Read(pointer);

			foreach (var pagePointer in VisioBinaryDocument.ReadPointers(stencils, fileLength))
			{
				if (pagePointer.Type == StencilPagePointer)
					yield return pagePointer;
			}
		}
	}

	/// <summary>
	/// Разбирает поток на чанки.
	///
	/// Заголовок — девятнадцать байт, за данными хвост: двенадцать байт
	/// у чанка со списком и четыре у остальных. Последний чанк потока
	/// обычно не дотягивает хвоста до конца — за ним уже ничего нет,
	/// и обход просто заканчивается.
	/// </summary>
	public static List<VisioChunk> ReadChunks(byte[] data)
	{
		var result = new List<VisioChunk>();

		int pos = 0;

		while (pos + VisioChunk.HeaderSize <= data.Length)
		{
			uint type = BitConverter.ToUInt32(data, pos);
			uint id = BitConverter.ToUInt32(data, pos + 4);
			uint list = BitConverter.ToUInt32(data, pos + 8);
			uint length = BitConverter.ToUInt32(data, pos + 12);
			ushort level = BitConverter.ToUInt16(data, pos + 16);

			int dataStart = pos + VisioChunk.HeaderSize;

			if (length > int.MaxValue || dataStart + (int)length > data.Length)
				break;

			var payload = new byte[length];
			Array.Copy(data, dataStart, payload, 0, (int)length);

			result.Add(new VisioChunk(type, id, list, level, payload));

			pos = dataStart + (int)length + (list != 0 ? 12 : 4);
		}

		return result;
	}

	#endregion

	#region Страница трафарета

	/// <summary>
	/// Собирает страницу трафарета в один набор.
	///
	/// Фигуры страницы — это ЧАСТИ одного изображения, а не отдельные
	/// клипарты: у них общая система координат, и по отдельности они
	/// бессмысленны. Поэтому страница даёт одну группу.
	///
	/// Фигуры лежат в таблице указателей плоским списком, но образуют
	/// дерево: заголовок каждой несёт номер родителя, а группа —
	/// собственный список детей. Дерево обязательно к разбору: признак
	/// отражения стоит у ГРУППЫ, и без композиции преобразований
	/// зеркальная половина сборки выходит незеркальной.
	/// </summary>
	private static CustomShapeDefinition? BuildPage(
		VisioBinaryDocument document,
		int fileLength,
		VisioPointer page,
		string name,
		ref int skipped)
	{
		byte[] pageData = document.Read(page);

		var records = ReadRecords(document, fileLength, pageData);

		if (records.Count == 0)
			return null;

		var shapes = new List<Shape>();

		foreach (var record in records.Values.OrderBy(r => r.Id))
		{
			if (record.IsGroup)
				continue;

			var curve = BuildShape(record, records);

			if (curve == null)
			{
				skipped++;
				continue;
			}

			// Порядок указателей в файле — это порядок отрисовки.
			curve.Layer = shapes.Count;
			shapes.Add(curve);
		}

		if (shapes.Count == 0)
			return null;

		// Центрируется вся страница целиком, а не каждая деталь отдельно:
		// иначе части, собранные в общей системе координат, съехались бы
		// в одну точку.
		Center(shapes);

		Shape root;

		if (shapes.Count == 1)
		{
			root = shapes[0];
		}
		else
		{
			var group = new GroupShape { Name = name };
			group.AddChildren(shapes);
			group.SortChildrenByLayer();

			root = group;
		}

		var def = new CustomShapeDefinition
		{
			Name = name,
			MmPerUnit = MmPerInch,
			Origin = CustomShapeOrigin.Stencil
		};

		def.Shapes.Add(DocumentMapper.ToDto(root));

		return def;
	}

	/// <summary>
	/// Читает фигуры страницы.
	///
	/// Ключ — позиция указателя в таблице страницы: именно на неё
	/// ссылаются и поле родителя, и списки детей у групп.
	/// </summary>
	private static Dictionary<int, VisioShapeRecord> ReadRecords(
		VisioBinaryDocument document,
		int fileLength,
		byte[] pageData)
	{
		var result = new Dictionary<int, VisioShapeRecord>();

		var pointers = VisioBinaryDocument.ReadPointers(pageData, fileLength, keepEmpty: true);

		for (int i = 0; i < pointers.Count; i++)
		{
			var pointer = pointers[i];

			if (pointer.Type != GroupPointer && pointer.Type != ShapePointer)
				continue;

			var chunks = ReadChunks(document.Read(pointer));

			if (chunks.Count == 0)
				continue;

			var header = chunks[0];

			var record = new VisioShapeRecord
			{
				Id = i,
				IsGroup = pointer.Type == GroupPointer,
				Chunks = chunks,
				ParentId = header.Data.Length >= ParentIdOffset + 4
					? (int)BitConverter.ToUInt32(header.Data, ParentIdOffset)
					: -1
			};

			foreach (var chunk in chunks)
			{
				if (chunk.Type != VisioChunkType.XForm || !chunk.HasValues(7))
					continue;

				record.Transform = new VisioTransform(chunk);
				record.HasTransform = true;
			}

			result[i] = record;
		}

		return result;
	}

	private static void Center(List<Shape> shapes)
	{
		SKRect bounds = SKRect.Empty;

		foreach (var shape in shapes)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);
		}

		if (bounds.IsEmpty)
			return;

		foreach (var shape in shapes)
			shape.MoveByWorld(-bounds.MidX, -bounds.MidY);
	}

	#endregion

	#region Отдельная фигура

	private static CurveShape? BuildShape(
		VisioShapeRecord record,
		Dictionary<int, VisioShapeRecord> all)
	{
		float lineWeight = 0.003333f;
		SKColor lineColour = SKColors.Black;
		bool hasLine = true;

		// Отсутствие узла заливки означает не «без заливки», а
		// «как в стиле»: у Visio это сплошной белый. Именно так устроена
		// белая полоса в этом клипарте — узла у неё нет, и без такого
		// значения по умолчанию от полосы оставался один белый обрис.
		SKColor fillColour = SKColors.White;
		bool hasFill = true;
		bool noFillSection = false;

		var builder = new VisioGeometryBuilder();
		bool anyGeometry = false;

		foreach (var chunk in record.Chunks)
		{
			switch (chunk.Type)
			{
				case VisioChunkType.Line when chunk.Data.Length >= 15:
					lineWeight = (float)chunk.Value(0);
					lineColour = VisioStyle.ReadColour(chunk.Data, 9);

					// Нулевой узор линии означает «без обводки».
					hasLine = chunk.Data[14] != 0;
					break;

				case VisioChunkType.Fill when chunk.Data.Length >= 11:
					fillColour = VisioStyle.ReadColour(chunk.Data, 0);
					hasFill = chunk.Data[10] != 0;
					break;

				case VisioChunkType.Geometry:
					builder.BeginSection();
					anyGeometry = true;

					// Первый байт секции — признак «не заливать».
					if (chunk.Data.Length >= 1 && chunk.Data[0] != 0)
						noFillSection = true;

					break;

				case VisioChunkType.MoveTo when chunk.HasValues(2):
					builder.MoveTo(chunk.Value(0), chunk.Value(1));
					break;

				case VisioChunkType.LineTo when chunk.HasValues(2):
					builder.LineTo(chunk.Value(0), chunk.Value(1));
					break;

				case VisioChunkType.EllipticalArcTo when chunk.HasValues(6):
					builder.EllipticalArcTo(
						chunk.Value(0), chunk.Value(1),
						chunk.Value(2), chunk.Value(3),
						chunk.Value(4), chunk.Value(5));
					break;

				default:
					// Неизвестная строка геометрии, у которой первые два
					// значения — координаты конца, вырождается в отрезок.
					// Диапазон узкий намеренно: соседние типы того же уровня
					// вложенности — текст и служебные данные.
					if (chunk.Type >= 0x8A && chunk.Type <= 0x93
						&& chunk.Type != VisioChunkType.EllipticalArcTo
						&& chunk.HasValues(2) && builder.InSection)
					{
						builder.LineTo(chunk.Value(0), chunk.Value(1));
					}

					break;
			}
		}

		if (!anyGeometry)
			return null;

		var curve = builder.Build(BuildChain(record, all));

		if (curve == null)
			return null;

		curve.Stroke.Enabled = hasLine;
		curve.Stroke.Color = lineColour;

		// Толщина в дюймах, и единица набора — тоже дюйм.
		curve.Stroke.Thickness = MathF.Max(lineWeight, 0.0005f);

		curve.Fill.Enabled = hasFill && !noFillSection;
		curve.Fill.Kind = FillKind.Solid;
		curve.Fill.SolidColor = fillColour;

		return curve;
	}

	/// <summary>
	/// Цепочка преобразований от фигуры к странице: своё, затем родителя,
	/// затем родителя родителя. Обход ограничен по длине — испорченный
	/// файл может замкнуть ссылку родителя на самого себя.
	/// </summary>
	private static VisioTransformChain BuildChain(
		VisioShapeRecord record,
		Dictionary<int, VisioShapeRecord> all)
	{
		var chain = new List<VisioTransform>();

		var current = record;
		int guard = 0;

		while (current != null && guard++ < 64)
		{
			if (current.HasTransform)
				chain.Add(current.Transform);

			if (!all.TryGetValue(current.ParentId, out var parent) || parent == current)
				break;

			current = parent;
		}

		return new VisioTransformChain(chain);
	}

	#endregion
}

/// <summary>
/// Преобразование фигуры: положение штифта, размеры, точка штифта внутри
/// фигуры, поворот и отражения. Переводит собственные координаты фигуры
/// в координаты РОДИТЕЛЯ.
/// </summary>
internal readonly struct VisioTransform
{
	private readonly double _pinX;
	private readonly double _pinY;
	private readonly double _width;
	private readonly double _height;
	private readonly double _locPinX;
	private readonly double _locPinY;
	private readonly double _cos;
	private readonly double _sin;
	private readonly bool _flipX;
	private readonly bool _flipY;

	/// <summary>
	/// Признаки отражения лежат сразу за семью ячейками преобразования,
	/// по байту на каждый: семь ячеек по девять байт — это 63, значит
	/// FlipX в 63-м байте, FlipY в 64-м. Отдельными ячейками они не
	/// записаны, поэтому в списке значений их не найти.
	/// </summary>
	private const int FlipXOffset = 63;
	private const int FlipYOffset = 64;

	public VisioTransform(
		double pinX, double pinY,
		double width, double height,
		double locPinX, double locPinY,
		double angle,
		bool flipX, bool flipY)
	{
		_pinX = pinX;
		_pinY = pinY;
		_width = width;
		_height = height;
		_locPinX = locPinX;
		_locPinY = locPinY;

		_cos = Math.Cos(angle);
		_sin = Math.Sin(angle);

		_flipX = flipX;
		_flipY = flipY;
	}

	public VisioTransform(VisioChunk chunk)
	{
		_pinX = chunk.Value(0);
		_pinY = chunk.Value(1);
		_width = chunk.Value(2);
		_height = chunk.Value(3);
		_locPinX = chunk.Value(4);
		_locPinY = chunk.Value(5);

		double angle = chunk.Value(6);

		_cos = Math.Cos(angle);
		_sin = Math.Sin(angle);

		_flipX = chunk.Data.Length > FlipXOffset && chunk.Data[FlipXOffset] != 0;
		_flipY = chunk.Data.Length > FlipYOffset && chunk.Data[FlipYOffset] != 0;
	}

	public (double X, double Y) ToParent(double x, double y)
	{
		// Отражение идёт первым и считается от габаритов фигуры,
		// а не от точки штифта: иначе зеркальная деталь уезжает с места.
		if (_flipX)
			x = _width - x;

		if (_flipY)
			y = _height - y;

		double dx = x - _locPinX;
		double dy = y - _locPinY;

		return (_pinX + dx * _cos - dy * _sin,
				_pinY + dx * _sin + dy * _cos);
	}
}

/// <summary>
/// Составное преобразование: фигура, её группа, группа группы. Последним
/// шагом ось Y меняет знак — в Visio она смотрит вверх, в документе вниз.
/// </summary>
internal sealed class VisioTransformChain
{
	private readonly List<VisioTransform> _chain;

	public VisioTransformChain(List<VisioTransform> chain) => _chain = chain;

	/// <summary>
	/// Цепочка с добавленным впереди преобразованием фигуры.
	///
	/// Возвращается новая цепочка, а не изменённая эта: одну и ту же
	/// родительскую цепочку получают все дети группы, и правка на месте
	/// у первого же ребёнка испортила бы её остальным.
	/// </summary>
	public VisioTransformChain With(VisioTransform transform)
	{
		var chain = new List<VisioTransform>(_chain.Count + 1) { transform };

		chain.AddRange(_chain);

		return new VisioTransformChain(chain);
	}

	public SKPoint Apply(double x, double y)
	{
		foreach (var transform in _chain)
			(x, y) = transform.ToParent(x, y);

		return new SKPoint((float)x, (float)(-y));
	}
}

/// <summary>
/// Собирает узлы кривой из строк геометрии Visio.
///
/// Все секции фигуры складываются в ОДНУ кривую: модель VDraw держит
/// несколько контуров в одном списке узлов, размечая их флагами
/// NewContour и CloseAfter. Дробить фигуру на отдельные объекты значило
/// бы потерять правило чётности при заливке.
/// </summary>
internal sealed class VisioGeometryBuilder
{
	/// <summary>Точка в собственных координатах фигуры, до преобразования.</summary>
	private readonly struct Node
	{
		public readonly SKPoint Position;
		public readonly SKPoint ControlPrev;
		public readonly SKPoint ControlNext;
		public readonly bool HasPrev;
		public readonly bool HasNext;
		public readonly bool NewContour;

		public Node(SKPoint position, bool newContour)
		{
			Position = position;
			ControlPrev = default;
			ControlNext = default;
			HasPrev = false;
			HasNext = false;
			NewContour = newContour;
		}

		public Node(SKPoint position, SKPoint prev, bool hasPrev, SKPoint next, bool hasNext, bool newContour)
		{
			Position = position;
			ControlPrev = prev;
			ControlNext = next;
			HasPrev = hasPrev;
			HasNext = hasNext;
			NewContour = newContour;
		}

		public Node WithNext(SKPoint control)
			=> new(Position, ControlPrev, HasPrev, control, true, NewContour);
	}

	private readonly List<Node> _nodes = new();

	private double _x;
	private double _y;
	private bool _started;

	public bool InSection { get; private set; }

	public void BeginSection()
	{
		InSection = true;
		_started = false;
	}

	public void MoveTo(double x, double y)
	{
		_x = x;
		_y = y;
		_started = true;

		_nodes.Add(new Node(new SKPoint((float)x, (float)y), newContour: true));
	}

	public void LineTo(double x, double y)
	{
		if (!_started)
		{
			MoveTo(x, y);
			return;
		}

		// Совпадающая с предыдущей точка сегмента не даёт, а вреда от неё
		// много: у линии из десяти LineTo в одну и ту же точку — а Visio
		// пишет такие у размерных линий — последний сегмент имеет нулевую
		// длину, касательная в конце вырождается, и наконечник рисуется
		// в никуда. Именно поэтому у выносных линий пропадали стрелки.
		if (SamePosition(_x, _y, x, y) && _nodes.Count > 0 && !_nodes[^1].HasNext)
		{
			_x = x;
			_y = y;
			return;
		}

		_x = x;
		_y = y;

		_nodes.Add(new Node(new SKPoint((float)x, (float)y), newContour: false));
	}

	private static bool SamePosition(double ax, double ay, double bx, double by)
	{
		const double Tolerance = 1e-7d;

		return Math.Abs(ax - bx) < Tolerance && Math.Abs(ay - by) < Tolerance;
	}

	/// <summary>
	/// Дуга эллипса, заданная концом, точкой на дуге, углом главной оси
	/// и отношением полуосей.
	///
	/// Считается в «круговом» пространстве: поворот на минус угол оси
	/// и сжатие по Y превращают эллипс в окружность, где дуга через три
	/// точки строится тривиально. Обратное преобразование аффинно, а
	/// аффинное отображение переводит кубическую кривую в кубическую —
	/// поэтому контрольные точки, посчитанные для окружности, остаются
	/// верными и для эллипса. Ломаной здесь нет: клипарт вставляют
	/// в чертёж и увеличивают.
	/// </summary>
	public void EllipticalArcTo(double x, double y, double a, double b, double c, double d)
	{
		if (!_started)
		{
			MoveTo(x, y);
			return;
		}

		if (d <= 0d || double.IsNaN(d) || double.IsInfinity(d))
		{
			LineTo(x, y);
			return;
		}

		double cosC = Math.Cos(-c);
		double sinC = Math.Sin(-c);

		(double X, double Y) ToCircle(double px, double py)
			=> (px * cosC - py * sinC, (px * sinC + py * cosC) / d);

		var p0 = ToCircle(_x, _y);
		var p1 = ToCircle(a, b);
		var p2 = ToCircle(x, y);

		double det = 2d * (p0.X * (p1.Y - p2.Y) + p1.X * (p2.Y - p0.Y) + p2.X * (p0.Y - p1.Y));

		// Три точки на одной прямой — окружности через них не существует.
		if (Math.Abs(det) < 1e-15)
		{
			LineTo(x, y);
			return;
		}

		double s0 = p0.X * p0.X + p0.Y * p0.Y;
		double s1 = p1.X * p1.X + p1.Y * p1.Y;
		double s2 = p2.X * p2.X + p2.Y * p2.Y;

		double ux = (s0 * (p1.Y - p2.Y) + s1 * (p2.Y - p0.Y) + s2 * (p0.Y - p1.Y)) / det;
		double uy = (s0 * (p2.X - p1.X) + s1 * (p0.X - p2.X) + s2 * (p1.X - p0.X)) / det;

		double radius = Math.Sqrt((p0.X - ux) * (p0.X - ux) + (p0.Y - uy) * (p0.Y - uy));

		double start = Math.Atan2(p0.Y - uy, p0.X - ux);
		double middle = Math.Atan2(p1.Y - uy, p1.X - ux);
		double finish = Math.Atan2(p2.Y - uy, p2.X - ux);

		// Направление обхода задаёт средняя точка: если по ходу против
		// часовой стрелки она встречается раньше конца, дуга идёт туда.
		double sweep = Normalize(middle - start) < Normalize(finish - start)
			? Normalize(finish - start)
			: -Normalize(start - finish);

		int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2d)));
		double step = sweep / steps;

		// Классический множитель приближения дуги кубической кривой.
		double k = 4d / 3d * Math.Tan(step / 4d);

		double cosBack = Math.Cos(c);
		double sinBack = Math.Sin(c);

		SKPoint FromCircle(double px, double py)
		{
			double ey = py * d;

			return new SKPoint(
				(float)(px * cosBack - ey * sinBack),
				(float)(px * sinBack + ey * cosBack));
		}

		double angle = start;

		for (int i = 0; i < steps; i++)
		{
			double next = angle + step;

			double x0 = ux + radius * Math.Cos(angle);
			double y0 = uy + radius * Math.Sin(angle);
			double x1 = ux + radius * Math.Cos(next);
			double y1 = uy + radius * Math.Sin(next);

			// Касательные к окружности в концах сегмента.
			var c1 = FromCircle(x0 - k * radius * Math.Sin(angle), y0 + k * radius * Math.Cos(angle));
			var c2 = FromCircle(x1 + k * radius * Math.Sin(next), y1 - k * radius * Math.Cos(next));

			var end = FromCircle(x1, y1);

			EmitCubic(c1, c2, end);

			angle = next;
		}

		_x = x;
		_y = y;
	}

	/// <summary>
	/// Дуга окружности, заданная концом и высотой сегмента — расстоянием
	/// от середины дуги до середины хорды. Сводится к дуге эллипса через
	/// точку на дуге: у окружности угол оси нулевой, а отношение полуосей
	/// единичное.
	/// </summary>
	public void ArcTo(double x, double y, double bow)
	{
		if (!_started || bow == 0d)
		{
			LineTo(x, y);
			return;
		}

		double midX = (_x + x) * 0.5d;
		double midY = (_y + y) * 0.5d;

		double dx = x - _x;
		double dy = y - _y;

		double length = Math.Sqrt(dx * dx + dy * dy);

		if (length < 1e-12)
		{
			LineTo(x, y);
			return;
		}

		// Нормаль к хорде: высота откладывается от её середины.
		double throughX = midX - dy / length * bow;
		double throughY = midY + dx / length * bow;

		EllipticalArcTo(x, y, throughX, throughY, 0d, 1d);
	}

	/// <summary>
	/// Полный эллипс: центр, точка на одной оси и точка на другой.
	/// Образует собственный контур из четырёх кубических сегментов —
	/// приближение четверти оборота даёт погрешность около сотой доли
	/// процента радиуса, что за пределами разрешения любого чертежа.
	/// </summary>
	public void Ellipse(double cx, double cy, double ax, double ay, double bx, double by)
	{
		double ux = ax - cx;
		double uy = ay - cy;
		double vx = bx - cx;
		double vy = by - cy;

		const double K = 0.5522847498307936d;

		MoveTo(cx + ux, cy + uy);

		EmitEllipseQuarter(cx, cy, ux, uy, vx, vy, K);
		EmitEllipseQuarter(cx, cy, vx, vy, -ux, -uy, K);
		EmitEllipseQuarter(cx, cy, -ux, -uy, -vx, -vy, K);
		EmitEllipseQuarter(cx, cy, -vx, -vy, ux, uy, K);
	}

	/// <summary>Четверть эллипса из точки center+from в точку center+to.</summary>
	private void EmitEllipseQuarter(
		double cx, double cy,
		double fromX, double fromY,
		double toX, double toY,
		double k)
	{
		var c1 = new SKPoint((float)(cx + fromX + k * toX), (float)(cy + fromY + k * toY));
		var c2 = new SKPoint((float)(cx + toX + k * fromX), (float)(cy + toY + k * fromY));
		var end = new SKPoint((float)(cx + toX), (float)(cy + toY));

		EmitCubic(c1, c2, end);
	}

	/// <summary>
	/// Добавляет кубический сегмент: управляющая точка выхода ложится
	/// на последний узел, точка входа — на новый.
	/// </summary>
	private void EmitCubic(SKPoint control1, SKPoint control2, SKPoint end)
	{
		int last = _nodes.Count - 1;

		if (last < 0)
		{
			MoveTo(end.X, end.Y);
			return;
		}

		_nodes[last] = _nodes[last].WithNext(control1);
		_nodes.Add(new Node(end, control2, true, default, false, newContour: false));

		_x = end.X;
		_y = end.Y;
	}

	private static double Normalize(double angle)
	{
		double two = Math.PI * 2d;

		angle %= two;

		return angle < 0d ? angle + two : angle;
	}

	/// <summary>
	/// Строит кривую. Контуры, вернувшиеся в начальную точку, замыкаются,
	/// а дублирующий узел убирается: сегмент замыкания VDraw рисует сам,
	/// от последнего узла к первому, и лишняя точка дала бы нулевой
	/// отрезок с изломом в нём.
	///
	/// Фигура остаётся в координатах страницы Visio. Сдвиг в начало
	/// координат делается один раз для всей страницы: части одного
	/// изображения обязаны сохранить взаимное положение.
	/// </summary>
	public CurveShape? Build(VisioTransformChain transform)
	{
		if (_nodes.Count < 2)
			return null;

		var curve = new CurveShape { Name = "Visio shape" };

		var mapped = new List<CurveNode>(_nodes.Count);

		foreach (var node in _nodes)
		{
			var item = new CurveNode
			{
				Position = transform.Apply(node.Position.X, node.Position.Y),
				Type = node.HasPrev || node.HasNext ? CurveNodeType.Sharp : CurveNodeType.Straight,
				NewContour = node.NewContour
			};

			if (node.HasPrev)
				item.ControlPrev = transform.Apply(node.ControlPrev.X, node.ControlPrev.Y);

			if (node.HasNext)
				item.ControlNext = transform.Apply(node.ControlNext.X, node.ControlNext.Y);

			mapped.Add(item);
		}

		CloseContours(mapped);

		if (mapped.Count < 2)
			return null;

		foreach (var node in mapped)
			curve.Nodes.Add(node);

		return curve;
	}

	private static void CloseContours(List<CurveNode> nodes)
	{
		int start = 0;

		for (int i = 0; i <= nodes.Count; i++)
		{
			bool boundary = i == nodes.Count || (i > start && nodes[i].NewContour);

			if (!boundary)
				continue;

			int end = i - 1;

			bool closed = end - start >= 2 && Near(nodes[start].Position, nodes[end].Position);

			if (closed)
			{
				// Кривизна замыкающего сегмента живёт в контрольных точках
				// убираемого узла: его «предыдущая» становится «предыдущей»
				// первого узла контура.
				if (!float.IsNaN(nodes[end].ControlPrev.X))
				{
					nodes[start].ControlPrev = nodes[end].ControlPrev;
					nodes[start].Type = CurveNodeType.Sharp;
				}

				nodes.RemoveAt(end);
				end--;
				i--;
			}

			// Замыкается ТОЛЬКО контур, который сам вернулся в начальную
			// точку. Раньше флаг ставился каждому, и открытая линия
			// становилась замкнутой: у неё пропадали концы, а вместе с ними
			// и наконечники — рисовать стрелку на замкнутом контуре негде.
			if (closed && end >= start)
				nodes[end].CloseAfter = true;

			start = i;
		}
	}

	private static bool Near(SKPoint a, SKPoint b)
	{
		const float Tolerance = 1e-5f;

		return MathF.Abs(a.X - b.X) < Tolerance && MathF.Abs(a.Y - b.Y) < Tolerance;
	}
}

/// <summary>
/// Одна строка геометрии после слияния с мастером.
/// </summary>
internal sealed class VisioGeometryRow
{
	public string Type = string.Empty;

	/// <summary>
	/// Значения ячеек как есть, строками.
	///
	/// Не числами: в строках градиента цвет записан как #rrggbb, а
	/// в геометрии попадаются «Themed» и формулы. Число получается
	/// из строки по требованию, а строка нужна целиком.
	/// </summary>
	public readonly Dictionary<string, string> Cells = new(StringComparer.OrdinalIgnoreCase);

	public double? Value(string name)
		=> Cells.TryGetValue(name, out string? v) ? VisioXml.Number(v) : null;

	public string? Text(string name)
		=> Cells.TryGetValue(name, out string? v) ? v : null;
}

/// <summary>Секция геометрии после слияния с мастером.</summary>
internal sealed class VisioGeometrySection
{
	public bool NoFill;
	public bool NoLine;
	public bool NoShow;

	public readonly SortedDictionary<int, VisioGeometryRow> Rows = new();
}

/// <summary>
/// Пакет OPC: страницы, мастера и связи между ними.
///
/// Части лежат в архиве по путям, а ссылаются друг на друга по
/// идентификаторам отношений, поэтому каталог отношений читается сразу:
/// без него связь «страница — её содержимое» и «мастер — его содержимое»
/// пришлось бы угадывать по номеру в имени файла, а он ничем не
/// гарантирован.
/// </summary>
internal sealed class VisioXmlPackage : IDisposable
{
	private readonly ZipArchive _archive;

	/// <summary>Содержимое мастеров по их идентификатору.</summary>
	private readonly Dictionary<string, XElement> _masters = new(StringComparer.Ordinal);

	public List<(string Name, XElement Contents)> Pages { get; } = new();

	public List<(string Name, XElement Contents)> Masters { get; } = new();

	/// <summary>
	/// Палитра документа.
	///
	/// В ячейках цвет чаще записан НОМЕРОМ, а не как #rrggbb: в этом
	/// чертеже литерал стоит лишь у трёх десятков фигур из пяти сотен,
	/// у остальных номера. Первые двадцать четыре номера — стандартная
	/// палитра Visio, она одинакова во всех файлах и потому зашита;
	/// дальше идут цвета документа из раздела Colors.
	///
	/// Без этой таблицы почти весь чертёж приходил белым — цвет не
	/// разбирался, и подставлялось значение по умолчанию.
	/// </summary>
	public List<SKColor> Palette { get; } = new()
	{
		new SKColor(0x00, 0x00, 0x00), new SKColor(0xFF, 0xFF, 0xFF),
		new SKColor(0xFF, 0x00, 0x00), new SKColor(0x00, 0xFF, 0x00),
		new SKColor(0x00, 0x00, 0xFF), new SKColor(0xFF, 0xFF, 0x00),
		new SKColor(0xFF, 0x00, 0xFF), new SKColor(0x00, 0xFF, 0xFF),
		new SKColor(0x80, 0x00, 0x00), new SKColor(0x00, 0x80, 0x00),
		new SKColor(0x00, 0x00, 0x80), new SKColor(0x80, 0x80, 0x00),
		new SKColor(0x80, 0x00, 0x80), new SKColor(0x00, 0x80, 0x80),
		new SKColor(0xC0, 0xC0, 0xC0), new SKColor(0x80, 0x80, 0x80),
		new SKColor(0x99, 0x99, 0xFF), new SKColor(0x99, 0x33, 0x66),
		new SKColor(0xFF, 0xFF, 0xCC), new SKColor(0xCC, 0xFF, 0xFF),
		new SKColor(0x66, 0x00, 0x66), new SKColor(0xFF, 0x80, 0x80),
		new SKColor(0x00, 0x66, 0xCC), new SKColor(0xCC, 0xCC, 0xFF)
	};

	/// <summary>Стили документа по идентификатору.</summary>
	private readonly Dictionary<string, XElement> _styles = new(StringComparer.Ordinal);

	private VisioXmlPackage(ZipArchive archive) => _archive = archive;

	public static VisioXmlPackage? Open(string path)
	{
		var archive = ZipFile.OpenRead(path);

		var package = new VisioXmlPackage(archive);

		package.ReadDocument();
		package.ReadMasters();
		package.ReadPages();

		return package;
	}

	public void Dispose() => _archive.Dispose();

	#region Чтение частей

	/// <summary>Палитра и стили документа.</summary>
	private void ReadDocument()
	{
		var entry = FindEntry("visio/document.xml");

		if (entry == null)
			return;

		var document = Load(entry);

		if (document?.Root == null)
			return;

		foreach (var colours in VisioXml.Children(document.Root, "Colors"))
		{
			foreach (var colour in VisioXml.Children(colours, "ColorEntry"))
			{
				int index = (int)(VisioXml.Number(VisioXml.Attribute(colour, "IX")) ?? -1d);

				if (index < 0)
					continue;

				// Номера начинаются там, где кончается стандартная палитра,
				// но встречаются файлы, переопределяющие и её начало.
				while (Palette.Count <= index)
					Palette.Add(SKColors.Black);

				Palette[index] = VisioXml.Colour(VisioXml.Attribute(colour, "RGB"), SKColors.Black);
			}
		}

		foreach (var sheets in VisioXml.Children(document.Root, "StyleSheets"))
		{
			foreach (var sheet in VisioXml.Children(sheets, "StyleSheet"))
			{
				string? id = VisioXml.Attribute(sheet, "ID");

				if (id != null)
					_styles[id] = sheet;
			}
		}
	}

	/// <summary>
	/// Цвет темы по умолчанию — акцент 1 стандартной темы Office.
	///
	/// Значение «Themed» означает «взять из темы», но тема хранится
	/// отдельной частью пакета, и в этом чертеже её просто нет: Visio
	/// в таком случае берёт встроенную тему. Повторить её целиком нечем,
	/// поэтому берётся один цвет — тот, которым Office красит первый
	/// акцент. Иначе половина чертежа приходит белой, а белая деталь
	/// не перекрывает то, что за ней, и наружу лезет внутренняя обвязка.
	/// </summary>
	private static readonly SKColor ThemedFallback = new(0x5B, 0x9B, 0xD5);

	/// <summary>
	/// Цвет из ячейки: литерал #rrggbb либо номер в палитре документа.
	/// Для «Themed» возвращается запасной цвет темы, для всего
	/// остального — null, и решение остаётся за вызывающим.
	/// </summary>
	public SKColor? ResolveColour(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return null;

		if (text[0] == '#')
		{
			var literal = VisioXml.Colour(text, SKColors.Transparent);

			return literal == SKColors.Transparent ? null : literal;
		}

		if (string.Equals(text, "Themed", StringComparison.OrdinalIgnoreCase))
			return ThemedFallback;

		if (VisioXml.Number(text) is not { } number)
			return null;

		int index = (int)number;

		return index >= 0 && index < Palette.Count ? Palette[index] : null;
	}

	/// <summary>
	/// Значение ячейки из стиля документа.
	///
	/// Стили выстроены в свою цепочку наследования: у каждого листа стиля
	/// есть родительские LineStyle, FillStyle и TextStyle. Разряд ячейки
	/// определяет, по какой из трёх цепочек идти, — искать по всем сразу
	/// нельзя, лист стиля хранит ячейки всех трёх разрядов, и заливка
	/// пришла бы из стиля линии.
	/// </summary>
	public string? FindStyleCell(XElement owner, string cellName)
	{
		string attribute = StyleAttributeFor(cellName);
		string? id = VisioXml.Attribute(owner, attribute);

		int guard = 0;

		while (id != null && guard++ < 32 && _styles.TryGetValue(id, out var sheet))
		{
			string? value = VisioXml.FindCell(sheet, cellName);

			if (value != null)
				return value;

			id = VisioXml.Attribute(sheet, attribute);
		}

		return null;
	}

	private static string StyleAttributeFor(string cellName)
	{
		if (cellName.StartsWith("Fill", StringComparison.Ordinal)
			|| cellName.StartsWith("Shdw", StringComparison.Ordinal)
			|| cellName.StartsWith("ShapeShdw", StringComparison.Ordinal)
			|| cellName is "UseGroupGradient" or "RotateGradientWithShape")
		{
			return "FillStyle";
		}

		if (cellName.StartsWith("Line", StringComparison.Ordinal)
			|| cellName.StartsWith("Begin", StringComparison.Ordinal)
			|| cellName.StartsWith("End", StringComparison.Ordinal)
			|| cellName is "Rounding" or "CompoundType")
		{
			return "LineStyle";
		}

		return "TextStyle";
	}

	private void ReadMasters()
	{
		var index = FindEntry("visio/masters/masters.xml");

		if (index == null)
			return;

		var document = Load(index);

		if (document?.Root == null)
			return;

		var relations = ReadRelations("visio/masters/_rels/masters.xml.rels");

		foreach (var master in VisioXml.Children(document.Root, "Master"))
		{
			var contents = LoadRelated(master, relations, "visio/masters/");

			if (contents == null)
				continue;

			string name = VisioXml.Attribute(master, "NameU")
				?? VisioXml.Attribute(master, "Name")
				?? "Shape";

			Masters.Add((name, contents));

			string? id = VisioXml.Attribute(master, "ID");

			if (id != null)
				_masters[id] = contents;
		}
	}

	private void ReadPages()
	{
		var index = FindEntry("visio/pages/pages.xml");

		if (index == null)
			return;

		var document = Load(index);

		if (document?.Root == null)
			return;

		var relations = ReadRelations("visio/pages/_rels/pages.xml.rels");

		foreach (var page in VisioXml.Children(document.Root, "Page"))
		{
			// Фоновые страницы пропускаются: они рисуются под другими
			// и сами по себе отдельным рисунком не являются.
			if (VisioXml.Attribute(page, "Background") == "1")
				continue;

			var contents = LoadRelated(page, relations, "visio/pages/");

			if (contents == null)
				continue;

			string name = VisioXml.Attribute(page, "NameU")
				?? VisioXml.Attribute(page, "Name")
				?? "Page";

			Pages.Add((name, contents));
		}
	}

	private XElement? LoadRelated(
		XElement owner,
		Dictionary<string, string> relations,
		string folder)
	{
		string? relationId = VisioXml.Children(owner, "Rel")
			.Select(r => r.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value)
			.FirstOrDefault(v => !string.IsNullOrEmpty(v));

		if (relationId == null || !relations.TryGetValue(relationId, out string? target))
			return null;

		var part = FindEntry(folder + target);

		return part == null ? null : Load(part)?.Root;
	}

	private Dictionary<string, string> ReadRelations(string path)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);

		var entry = FindEntry(path);

		if (entry == null)
			return result;

		var document = Load(entry);

		if (document?.Root == null)
			return result;

		foreach (var relation in VisioXml.Children(document.Root, "Relationship"))
		{
			string? id = VisioXml.Attribute(relation, "Id");
			string? target = VisioXml.Attribute(relation, "Target");

			if (id != null && target != null)
				result[id] = target.TrimStart('/');
		}

		return result;
	}

	private ZipArchiveEntry? FindEntry(string path)
		=> _archive.Entries.FirstOrDefault(e =>
			string.Equals(e.FullName.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));

	private static XDocument? Load(ZipArchiveEntry entry)
	{
		try
		{
			using var stream = entry.Open();

			return XDocument.Load(stream, LoadOptions.None);
		}
		catch
		{
			// Повреждённая часть пакета не должна ронять импорт целиком.
			return null;
		}
	}

	#endregion

	#region Наследование от мастера

	/// <summary>Корневая фигура мастера — от неё наследуется экземпляр на странице.</summary>
	public XElement? MasterRootShape(string masterId)
	{
		if (!_masters.TryGetValue(masterId, out var contents))
			return null;

		return VisioXml.Children(contents, "Shapes")
			.SelectMany(s => VisioXml.Children(s, "Shape"))
			.FirstOrDefault();
	}

	/// <summary>Фигура мастера по её идентификатору, на любой глубине.</summary>
	public XElement? MasterShape(string masterId, string shapeId)
	{
		if (!_masters.TryGetValue(masterId, out var contents))
			return null;

		foreach (var container in VisioXml.Children(contents, "Shapes"))
		{
			var found = FindShape(container, shapeId);

			if (found != null)
				return found;
		}

		return null;
	}

	private static XElement? FindShape(XElement container, string shapeId)
	{
		foreach (var shape in VisioXml.Children(container, "Shape"))
		{
			if (VisioXml.Attribute(shape, "ID") == shapeId)
				return shape;

			foreach (var nested in VisioXml.Children(shape, "Shapes"))
			{
				var found = FindShape(nested, shapeId);

				if (found != null)
					return found;
			}
		}

		return null;
	}

	#endregion
}

/// <summary>
/// Фигура вместе с фигурой мастера, от которой она наследует.
///
/// Экземпляр на странице записывает только то, чем ОТЛИЧАЕТСЯ от мастера:
/// у прямоугольника это могут быть две координаты из десяти строк
/// геометрии, а MoveTo и вовсе остаться в мастере. Читать такую фигуру
/// в лоб — значит получить обрывок контура или пустоту, поэтому всякое
/// обращение к ячейке и к геометрии идёт через слияние.
/// </summary>
internal sealed class VisioXmlShape
{
	private readonly XElement _element;
	private readonly XElement? _master;
	private readonly VisioXmlPackage? _package;

	public VisioXmlShape(XElement element, XElement? master, VisioXmlPackage? package = null)
	{
		_element = element;
		_master = master;
		_package = package;
	}

	public XElement Element => _element;

	public string? Name
		=> VisioXml.Attribute(_element, "NameU") ?? VisioXml.Attribute(_element, "Name");

	/// <summary>
	/// Подпись фигуры.
	///
	/// Берётся только собственная: текст мастера — это образец, который
	/// пользователь на странице заменил или стёр, и подставлять его
	/// экземпляру значило бы вернуть на чертёж давно убранное слово.
	/// </summary>
	public string? Text()
	{
		var node = VisioXml.Children(_element, "Text").FirstOrDefault();

		return node?.Value;
	}

	#region Ячейки

	public double? Cell(string name) => VisioXml.Number(CellText(name));

	/// <summary>
	/// Значение ячейки с полным разбором наследования.
	///
	/// Порядок: собственная ячейка, ячейка мастера, стиль документа.
	/// Третья ступень нужна не для полноты: в этом чертеже у 237 фигур
	/// цвета заливки нет ни у себя, ни у мастера — он весь в стилях,
	/// и без него они приходили белыми.
	/// </summary>
	public string? CellText(string name)
	{
		string? own = VisioXml.FindCell(_element, name);

		if (own != null)
			return own;

		if (_master != null)
		{
			string? inherited = VisioXml.FindCell(_master, name);

			if (inherited != null)
				return inherited;
		}

		if (_package == null)
			return null;

		// Стиль берётся с самой фигуры, а при его отсутствии — с мастера:
		// экземпляр обычно наследует и оформление, и ссылку на стиль.
		return _package.FindStyleCell(_element, name)
			?? (_master == null ? null : _package.FindStyleCell(_master, name));
	}

	/// <summary>Цвет ячейки: литерал, номер в палитре или значение по умолчанию.</summary>
	public SKColor Colour(string name, SKColor fallback)
		=> ResolveColour(CellText(name)) ?? fallback;

	/// <summary>Разбор произвольной цветовой записи в контексте документа.</summary>
	public SKColor? ResolveColour(string? text)
		=> _package != null
			? _package.ResolveColour(text)
			: (VisioXml.Colour(text, SKColors.Transparent) is var c && c != SKColors.Transparent ? c : null);

	#endregion

	#region Геометрия

	/// <summary>
	/// Секции геометрии после слияния с мастером.
	///
	/// Порядок наложения — мастер, затем экземпляр: строки сопоставляются
	/// по номеру IX, ячейки внутри строки — по имени. Признак Del удаляет
	/// унаследованное; без него удалённая в экземпляре строка вернулась бы
	/// из мастера.
	/// </summary>
	public IEnumerable<VisioGeometrySection> Geometry() => Sections("Geometry");

	/// <summary>
	/// Строки именованной секции после слияния с мастером.
	///
	/// Кроме геометрии так же устроены FillGradient и Character: у них
	/// та же схема наследования, и отдельного чтения они не требуют.
	/// </summary>
	public IEnumerable<VisioGeometrySection> Sections(string name)
	{
		var sections = new SortedDictionary<int, VisioGeometrySection>();

		if (_master != null)
			Merge(sections, _master, name);

		Merge(sections, _element, name);

		return sections.Values;
	}

	/// <summary>Строки первой секции с таким именем, или пустой список.</summary>
	public IReadOnlyCollection<VisioGeometryRow> SectionRows(string name)
	{
		var section = Sections(name).FirstOrDefault();

		return section == null
			? Array.Empty<VisioGeometryRow>()
			: (IReadOnlyCollection<VisioGeometryRow>)section.Rows.Values;
	}

	private static void Merge(
		SortedDictionary<int, VisioGeometrySection> sections,
		XElement owner,
		string sectionName)
	{
		foreach (var element in owner.Elements())
		{
			if (element.Name.LocalName != "Section")
				continue;

			if (VisioXml.Attribute(element, "N") != sectionName)
				continue;

			int index = (int)(VisioXml.Number(VisioXml.Attribute(element, "IX")) ?? 0d);

			if (VisioXml.Attribute(element, "Del") == "1")
			{
				sections.Remove(index);
				continue;
			}

			if (!sections.TryGetValue(index, out var section))
			{
				section = new VisioGeometrySection();
				sections[index] = section;
			}

			foreach (var cell in VisioXml.Children(element, "Cell"))
			{
				bool on = (VisioXml.Number(VisioXml.Attribute(cell, "V")) ?? 0d) != 0d;

				switch (VisioXml.Attribute(cell, "N"))
				{
					case "NoFill": section.NoFill = on; break;
					case "NoLine": section.NoLine = on; break;
					case "NoShow": section.NoShow = on; break;
				}
			}

			foreach (var rowElement in VisioXml.Children(element, "Row"))
			{
				int rowIndex = (int)(VisioXml.Number(VisioXml.Attribute(rowElement, "IX")) ?? 0d);

				if (VisioXml.Attribute(rowElement, "Del") == "1")
				{
					section.Rows.Remove(rowIndex);
					continue;
				}

				if (!section.Rows.TryGetValue(rowIndex, out var row))
				{
					row = new VisioGeometryRow();
					section.Rows[rowIndex] = row;
				}

				string? type = VisioXml.Attribute(rowElement, "T");

				if (!string.IsNullOrEmpty(type))
					row.Type = type;

				foreach (var cell in VisioXml.Children(rowElement, "Cell"))
				{
					string? name = VisioXml.Attribute(cell, "N");
					string? value = VisioXml.Attribute(cell, "V");

					if (name != null && value != null)
						row.Cells[name] = value;
				}
			}
		}
	}

	#endregion
}

/// <summary>Мелочи разбора разметки, общие для всех поколений формата.</summary>
internal static class VisioXml
{
	/// <summary>
	/// Дети с заданным именем без учёта пространства имён: у пакета и
	/// у DatadiagramML они разные, а имена узлов совпадают.
	/// </summary>
	public static IEnumerable<XElement> Children(XElement parent, string local)
		=> parent.Elements().Where(e => e.Name.LocalName == local);

	public static string? Attribute(XElement element, string local)
		=> element.Attributes().FirstOrDefault(a => a.Name.LocalName == local)?.Value;

	/// <summary>
	/// Цвет ячейки. Разбирается запись вида #rrggbb; всё остальное —
	/// «Themed», формулы, номера в теме — сюда не подходит и отдаётся
	/// вызывающему нетронутым через значение по умолчанию.
	/// </summary>
	public static SKColor Colour(string? text, SKColor fallback)
	{
		if (text == null || text.Length != 7 || text[0] != '#')
			return fallback;

		return uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
			? new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
			: fallback;
	}

	public static double? Number(string? text)
		=> double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			? value
			: null;

	/// <summary>
	/// Значение ячейки фигуры.
	///
	/// Ищется и как узел Cell с признаком N — так пишет пакет, — и как
	/// узел с именем самой ячейки внутри раздела вроде XForm или Line:
	/// так писал DatadiagramML. Разделы Shapes и Section при этом
	/// пропускаются, иначе фигура забрала бы значение у своего ребёнка
	/// или у строки геометрии.
	/// </summary>
	public static string? FindCell(XElement shape, string name)
	{
		foreach (var cell in Children(shape, "Cell"))
		{
			if (Attribute(cell, "N") == name)
				return Attribute(cell, "V");
		}

		foreach (var section in shape.Elements())
		{
			if (section.Name.LocalName is "Shapes" or "Section")
				continue;

			foreach (var cell in section.Elements())
			{
				if (cell.Name.LocalName == name)
					return cell.Value;
			}
		}

		return null;
	}
}

/// <summary>
/// Visio в разметке XML: рисунки .vsdx и .vsdm, трафареты .vssx и .vssm,
/// а также одиночные файлы DatadiagramML (.vsx, .vdx) времён Visio 2003.
///
/// Рисунок и трафарет отличаются лишь тем, где лежат фигуры: у рисунка
/// это страницы, у трафарета — мастера. Разбор фигуры один и тот же.
/// </summary>
internal static class VisioXmlStencil
{
	/// <summary>Внутренние единицы длины Visio — дюймы.</summary>
	private const float MmPerInch = 25.4f;

	#region Точки входа

	/// <summary>Пакет OPC: .vsdx, .vssx и родственные.</summary>
	public static List<CustomShapeDefinition> Import(string path, out string? note, out bool isDrawing)
	{
		var result = new List<CustomShapeDefinition>();

		isDrawing = false;

		using var package = VisioXmlPackage.Open(path);

		if (package == null)
		{
			note = "the package could not be read";
			return result;
		}

		string baseName = Path.GetFileNameWithoutExtension(path);
		int skipped = 0;

		// Страницы важнее мастеров: если в файле есть рисунок, пользователь
		// хочет именно его, а мастера в рисунке — это заготовки, из которых
		// он собран, и отдельными фигурами в меню они только мешают.
		isDrawing = package.Pages.Count > 0;

		var sources = isDrawing ? package.Pages : package.Masters;

		foreach (var (name, contents) in sources)
		{
			var def = BuildDrawing(package, contents, name, baseName, ref skipped);

			if (def != null)
				result.Add(def);
		}

		if (result.Count == 0)
		{
			note = "the package has no drawable shapes";
			return result;
		}

		note = skipped > 0
			? $"{skipped} shape(s) carried no vector geometry and were skipped"
			: null;

		return result;
	}

	/// <summary>Одиночный файл DatadiagramML: .vsx и старые .vdx.</summary>
	public static List<CustomShapeDefinition> ImportFlatFile(string path, out string? note, out bool isDrawing)
	{
		var result = new List<CustomShapeDefinition>();

		isDrawing = false;

		XDocument document;

		try
		{
			document = XDocument.Load(path, LoadOptions.None);
		}
		catch (Exception ex)
		{
			note = ex.Message;
			return result;
		}

		if (document.Root == null)
		{
			note = "the file has no root element";
			return result;
		}

		string baseName = Path.GetFileNameWithoutExtension(path);
		int skipped = 0;

		var sources = VisioXml.Children(document.Root, "Masters")
			.SelectMany(m => VisioXml.Children(m, "Master"))
			.ToList();

		if (sources.Count == 0)
		{
			sources = VisioXml.Children(document.Root, "Pages")
				.SelectMany(p => VisioXml.Children(p, "Page"))
				.ToList();

			isDrawing = sources.Count > 0;
		}

		foreach (var source in sources)
		{
			string name = VisioXml.Attribute(source, "NameU")
				?? VisioXml.Attribute(source, "Name")
				?? baseName;

			var def = BuildDrawing(null, source, name, baseName, ref skipped);

			if (def != null)
				result.Add(def);
		}

		if (result.Count == 0)
		{
			note = "the file has no drawable shapes";
			return result;
		}

		note = skipped > 0
			? $"{skipped} shape(s) carried no vector geometry and were skipped"
			: null;

		return result;
	}

	#endregion

	#region Страница или мастер

	private static CustomShapeDefinition? BuildDrawing(
		VisioXmlPackage? package,
		XElement contents,
		string name,
		string baseName,
		ref int skipped)
	{
		var roots = VisioXml.Children(contents, "Shapes")
			.SelectMany(s => VisioXml.Children(s, "Shape"))
			.ToList();

		if (roots.Count == 0)
			return null;

		var shapes = new List<Shape>();
		var empty = new VisioTransformChain(new List<VisioTransform>());

		foreach (var root in roots)
			Collect(package, root, null, empty, shapes, ref skipped);

		if (shapes.Count == 0)
			return null;

		Center(shapes);

		Shape result;

		if (shapes.Count == 1)
		{
			result = shapes[0];
		}
		else
		{
			var group = new GroupShape { Name = name };
			group.AddChildren(shapes);
			group.SortChildrenByLayer();

			result = group;
		}

		return new CustomShapeDefinition
		{
			Name = string.IsNullOrWhiteSpace(name) ? baseName : name,
			MmPerUnit = MmPerInch,
			Origin = CustomShapeOrigin.Stencil,
			Shapes = { DocumentMapper.ToDto(result) }
		};
	}

	/// <summary>
	/// Обходит дерево фигур.
	///
	/// Вместе с фигурой по дереву идёт номер мастера: его объявляет
	/// верхняя фигура экземпляра, а вложенные ссылаются на фигуры ВНУТРИ
	/// него по MasterShape. Поэтому номер передаётся вниз по рекурсии,
	/// а не ищется заново на каждом уровне.
	/// </summary>
	private static void Collect(
		VisioXmlPackage? package,
		XElement element,
		string? masterId,
		VisioTransformChain parent,
		List<Shape> output,
		ref int skipped)
	{
		XElement? masterShape = null;

		if (package != null)
		{
			string? ownMaster = VisioXml.Attribute(element, "Master");

			if (ownMaster != null)
			{
				masterId = ownMaster;
				masterShape = package.MasterRootShape(ownMaster);
			}
			else
			{
				string? masterShapeId = VisioXml.Attribute(element, "MasterShape");

				if (masterShapeId != null && masterId != null)
					masterShape = package.MasterShape(masterId, masterShapeId);
			}
		}

		var shape = new VisioXmlShape(element, masterShape, package);

		var chain = parent.With(ReadTransform(shape));

		// Внедрённая картинка — метафайл или растр — рисуется не геометрией,
		// а содержимым, которого мы не читаем. Геометрия у такой фигуры
		// всё равно есть: это рамка кадра. Нарисовав её, мы добавили бы
		// к чертежу пустой прямоугольник на месте картинки.
		bool foreign = VisioXml.Children(element, "ForeignData").Any()
			|| VisioXml.Attribute(element, "Type") == "Foreign";

		var curve = foreign ? null : BuildShape(package, shape, chain);

		if (curve != null)
		{
			curve.Layer = output.Count;
			output.Add(curve);
		}

		var label = BuildText(shape, chain);

		if (label != null)
		{
			label.Layer = output.Count;
			output.Add(label);
		}

		var children = VisioXml.Children(element, "Shapes")
			.SelectMany(s => VisioXml.Children(s, "Shape"))
			.ToList();

		if (curve == null && label == null && children.Count == 0)
			skipped++;

		foreach (var child in children)
			Collect(package, child, masterId, chain, output, ref skipped);
	}

	private static void Center(List<Shape> shapes)
	{
		SKRect bounds = SKRect.Empty;

		foreach (var shape in shapes)
		{
			var b = shape.GetBounds();

			if (!b.IsEmpty)
				bounds = bounds.IsEmpty ? b : Geometry.Union(bounds, b);
		}

		if (bounds.IsEmpty)
			return;

		foreach (var shape in shapes)
			shape.MoveByWorld(-bounds.MidX, -bounds.MidY);
	}

	#endregion

	#region Фигура

	private static VisioTransform ReadTransform(VisioXmlShape shape)
		=> new(
			shape.Cell("PinX") ?? 0d,
			shape.Cell("PinY") ?? 0d,
			shape.Cell("Width") ?? 0d,
			shape.Cell("Height") ?? 0d,
			shape.Cell("LocPinX") ?? 0d,
			shape.Cell("LocPinY") ?? 0d,
			shape.Cell("Angle") ?? 0d,
			(shape.Cell("FlipX") ?? 0d) != 0d,
			(shape.Cell("FlipY") ?? 0d) != 0d);

	private static CurveShape? BuildShape(
		VisioXmlPackage? package,
		VisioXmlShape shape,
		VisioTransformChain chain)
	{
		var builder = new VisioGeometryBuilder();

		double width = shape.Cell("Width") ?? 0d;
		double height = shape.Cell("Height") ?? 0d;

		bool anyGeometry = false;
		int visibleSections = 0;
		int noFillSections = 0;
		int noLineSections = 0;

		foreach (var section in shape.Geometry())
		{
			// Скрытая секция не рисуется вовсе: обычно это вспомогательный
			// контур для привязки соединителей.
			if (section.NoShow || section.Rows.Count == 0)
				continue;

			builder.BeginSection();

			anyGeometry = true;
			visibleSections++;

			if (section.NoFill)
				noFillSections++;

			if (section.NoLine)
				noLineSections++;

			foreach (var row in section.Rows.Values)
				EmitRow(builder, row, width, height);
		}

		if (!anyGeometry)
			return null;

		var curve = builder.Build(chain);

		if (curve == null)
			return null;

		double linePattern = shape.Cell("LinePattern") ?? 1d;
		double fillPattern = shape.Cell("FillPattern") ?? 1d;

		// Признаки секций учитываются только когда их объявили ВСЕ секции.
		// У фигуры одна заливка и одна обводка на все контуры, и отключить
		// их из-за единственной вспомогательной секции значило бы стереть
		// оформление всей фигуры.
		bool allNoFill = visibleSections > 0 && noFillSections == visibleSections;
		bool allNoLine = visibleSections > 0 && noLineSections == visibleSections;

		curve.Stroke.Enabled = linePattern != 0d && !allNoLine;
		curve.Stroke.Color = shape.Colour("LineColor", SKColors.Black);

		// Толщина в дюймах, и единица набора — тоже дюйм.
		curve.Stroke.Thickness = MathF.Max((float)(shape.Cell("LineWeight") ?? 0.003333d), 0.0005f);

		curve.Fill.Enabled = fillPattern != 0d && !allNoFill;
		curve.Fill.Kind = FillKind.Solid;
		curve.Fill.SolidColor = shape.Colour("FillForegnd", SKColors.White);

		// Заливка обязана следовать за фигурой: чертёж после вставки
		// двигают, поворачивают и растягивают, а заливка, оставшаяся
		// на месте, сразу выдаёт себя — блик уезжает поперёк детали.
		// Ставится всем, а не только градиентным: узор ведёт себя так же.
		curve.Fill.RotateWithShape = true;
		curve.Fill.ScaleWithShape = true;

		ApplyHatch(shape, curve, fillPattern);

		ApplyGradient(package, shape, curve, chain, width, height);
		ApplyArrows(shape, curve);

		// Ни обводки, ни заливки — рисовать нечего. Такие контуры Visio
		// держит под подписи: у текстового блока «№4» есть рамка, но она
		// невидима, и в документе от неё остался бы пустой прямоугольник
		// поверх чертежа, который мешает выделять то, что под ним.
		if (!curve.Stroke.Enabled && !curve.Fill.Enabled)
			return null;

		string? name = shape.Name;

		if (!string.IsNullOrWhiteSpace(name))
			curve.Name = name;

		return curve;
	}

	private static void EmitRow(
		VisioGeometryBuilder builder,
		VisioGeometryRow row,
		double width,
		double height)
	{
		double x = row.Value("X") ?? 0d;
		double y = row.Value("Y") ?? 0d;

		switch (row.Type)
		{
			case "MoveTo":
				builder.MoveTo(x, y);
				break;

			case "LineTo":
				builder.LineTo(x, y);
				break;

			// Относительные строки задают долю от габаритов фигуры,
			// а не длину: 0.5 означает середину, а не полдюйма.
			case "RelMoveTo":
				builder.MoveTo(x * width, y * height);
				break;

			case "RelLineTo":
				builder.LineTo(x * width, y * height);
				break;

			case "ArcTo":
				builder.ArcTo(x, y, row.Value("A") ?? 0d);
				break;

			case "EllipticalArcTo":
				builder.EllipticalArcTo(
					x, y,
					row.Value("A") ?? 0d, row.Value("B") ?? 0d,
					row.Value("C") ?? 0d, row.Value("D") ?? 1d);
				break;

			case "Ellipse":
				builder.Ellipse(
					x, y,
					row.Value("A") ?? 0d, row.Value("B") ?? 0d,
					row.Value("C") ?? 0d, row.Value("D") ?? 0d);
				break;

			default:
				// Сплайны описаны формулой в отдельной ячейке, разбор
				// которой — отдельная работа. Конец сегмента при этом
				// лежит в тех же X и Y, и отрезок до него даёт узнаваемый
				// контур вместо разрыва.
				builder.LineTo(x, y);
				break;
		}
	}

	/// <summary>
	/// Фонтанная заливка.
	///
	/// Остановки лежат в секции FillGradient и наследуются от мастера
	/// наравне с геометрией. Цвет остановки записан либо как #rrggbb,
	/// либо номером в цветовой схеме темы — второй случай и есть тот
	/// белый блик в середине трубы, который иначе теряется.
	///
	/// Направление задаёт FillGradientAngle, и отсчитывается он В СИСТЕМЕ
	/// САМОЙ ФИГУРЫ, а не страницы. Поэтому концы градиента строятся
	/// в её собственных координатах и переносятся тем же преобразованием,
	/// что и геометрия: поворот фигуры, отражения и повороты групп
	/// применяются к ним заодно.
	///
	/// Прежняя правка на прямой угол была не исправлением, а подгонкой:
	/// в том чертеже трубы стояли повёрнутыми, и поворот фигуры, который
	/// не учитывался, выглядел как постоянная ошибка формата. На чертеже
	/// с вертикальными деталями та же подгонка развернула градиент
	/// в другую сторону.
	/// </summary>
	private static void ApplyGradient(
		VisioXmlPackage? package,
		VisioXmlShape shape,
		CurveShape curve,
		VisioTransformChain chain,
		double width,
		double height)
	{
		if ((shape.Cell("FillGradientEnabled") ?? 0d) == 0d)
			return;

		var stops = ReadStops(package, shape, curve.Fill.SolidColor);

		if (stops.Count < 2)
			return;

		curve.Fill.Kind = FillKind.LinearGradient;
		curve.Fill.Stops.Clear();
		curve.Fill.Stops.AddRange(stops);

		curve.Fill.GradientColor1 = stops[0].Color;
		curve.Fill.GradientColor2 = stops[^1].Color;

		double angle = shape.Cell("FillGradientAngle") ?? 0d;

		if (width <= 0d || height <= 0d)
			return;

		double dx = Math.Cos(angle);
		double dy = Math.Sin(angle);

		// Половина протяжённости фигуры вдоль направления градиента:
		// проекция её полуразмеров на ось направления. Всё в собственных
		// координатах фигуры, где габариты — это Width и Height.
		double half = Math.Abs(dx) * width * 0.5d + Math.Abs(dy) * height * 0.5d;

		double cx = width * 0.5d;
		double cy = height * 0.5d;

		curve.Fill.StartPoint = chain.Apply(cx - dx * half, cy - dy * half);
		curve.Fill.EndPoint = chain.Apply(cx + dx * half, cy + dy * half);

		// Иначе точки будут перезаписаны значениями по умолчанию —
		// горизонтальным градиентом по габаритам.
		curve.Fill.FillTransformInitialized = true;
	}

	/// <summary>
	/// Штриховка.
	///
	/// У Visio номера узора со второго по двадцать четвёртый — это
	/// готовые узоры: штрихи, клетка, точки. Двадцать пятый и дальше —
	/// градиенты, их разбирает ApplyGradient, а первый — сплошная заливка.
	///
	/// Рисунок узора берётся не из файла: Visio хранит его во внутренней
	/// библиотеке, которой в пакете нет. Поэтому все штриховые номера
	/// сводятся к диагональной штриховке цветом переднего плана — это
	/// заметно ближе к правде, чем сплошная заливка, которой такие
	/// фигуры заливались раньше, но точным соответствием не является.
	/// </summary>
	private static void ApplyHatch(VisioXmlShape shape, CurveShape curve, double fillPattern)
	{
		int pattern = (int)fillPattern;

		if (pattern < 2 || pattern > 24)
			return;

		var front = shape.Colour("FillForegnd", SKColors.Black);
		var back = shape.Colour("FillBkgnd", SKColors.White);

		curve.Fill.Kind = FillKind.Pattern;
		curve.Fill.PatternFrontColor = front;
		curve.Fill.PatternBackColor = back;
		curve.Fill.PatternWidth = HatchSize;
		curve.Fill.PatternHeight = HatchSize;
		curve.Fill.PatternDefinition = BuildDiagonalHatch(front, back);
		curve.Fill.Tiling = true;
	}

	private const int HatchSize = 8;

	/// <summary>Диагональ через клетку: по одной закрашенной точке в строке.</summary>
	private static string BuildDiagonalHatch(SKColor front, SKColor back)
	{
		string frontHex = Geometry.ColorToHex(front);
		string backHex = Geometry.ColorToHex(back);

		var rows = new string[HatchSize];

		for (int y = 0; y < HatchSize; y++)
		{
			var cells = new string[HatchSize];

			for (int x = 0; x < HatchSize; x++)
				cells[x] = x == HatchSize - 1 - y ? frontHex : backHex;

			rows[y] = string.Join(",", cells);
		}

		return string.Join(";", rows);
	}

	/// <summary>
	/// Остановки градиента.
	///
	/// Своя секция FillGradient есть далеко не у каждой фигуры: в этом
	/// чертеже признак градиента стоит у двух сотен, а остановки записаны
	/// у четырёх десятков — остальные наследуют их из стиля, где все
	/// значения помечены «Themed». Разобрать такие нечем, поэтому для них
	/// собирается блик по образцу, который Visio и рисует: цвет фигуры
	/// по краям и светлая полоса посередине.
	/// </summary>
	private static List<GradientStop> ReadStops(
		VisioXmlPackage? package,
		VisioXmlShape shape,
		SKColor solid)
	{
		var result = new List<GradientStop>();

		foreach (var row in shape.SectionRows("FillGradient"))
		{
			var colour = package?.ResolveColour(row.Text("GradientStopColor"));

			// Остановка без разбираемого цвета обесценивает весь набор:
			// подставив в неё цвет фигуры, мы получим не блик, а ступеньку.
			if (colour == null)
			{
				result.Clear();
				break;
			}

			float position = (float)(row.Value("GradientStopPosition") ?? 0d);

			result.Add(new GradientStop(Math.Clamp(position, 0f, 1f), colour.Value));
		}

		if (result.Count >= 2)
			return result;

		result.Clear();

		result.Add(new GradientStop(0f, solid));
		result.Add(new GradientStop(0.5f, Lighten(solid)));
		result.Add(new GradientStop(1f, solid));

		return result;
	}

	/// <summary>Осветление к белому наполовину — светлая полоса блика.</summary>
	private static SKColor Lighten(SKColor colour)
		=> new(
			(byte)((colour.Red + 255) / 2),
			(byte)((colour.Green + 255) / 2),
			(byte)((colour.Blue + 255) / 2),
			colour.Alpha);

	/// <summary>
	/// Наконечники линии.
	///
	/// Размер у Visio задан номером от нуля до шести, а у документа —
	/// множителем к толщине линии. Пересчёт грубый и намеренно такой:
	/// точное соответствие потребовало бы таблицы размеров Visio, а на
	/// вид важно, чтобы наконечник размерной линии был заметен рядом
	/// с волосяной линией толщиной в четверть точки.
	/// </summary>
	private static void ApplyArrows(VisioXmlShape shape, CurveShape curve)
	{
		double begin = shape.Cell("BeginArrow") ?? 0d;
		double end = shape.Cell("EndArrow") ?? 0d;

		if (begin != 0d)
		{
			curve.Stroke.StartGlyph = ArrowGlyph(begin);
			curve.Stroke.StartGlyphSize = ArrowSize(shape.Cell("BeginArrowSize"));
		}

		if (end != 0d)
		{
			curve.Stroke.EndGlyph = ArrowGlyph(end);
			curve.Stroke.EndGlyphSize = ArrowSize(shape.Cell("EndArrowSize"));
		}
	}

	/// <summary>
	/// Вид наконечника по его номеру у Visio.
	///
	/// Первый номер — открытая «птичка» из двух отрезков, а не залитый
	/// треугольник; именно её Visio ставит на размерные и выносные линии.
	/// Полного соответствия здесь нет: у Visio наконечников больше сорока,
	/// и разбирать их поимённо без файла, где видно результат, — гадание.
	/// Остальные сводятся к залитому треугольнику как к ближайшему общему.
	/// </summary>
	private static string ArrowGlyph(double visioArrow)
		=> (int)visioArrow == 1 ? LineGlyphLibrary.EmptyArrow : LineGlyphLibrary.Arrow;

	private static float ArrowSize(double? visioSize)
	{
		// Второй номер — размер по умолчанию у Visio.
		double size = visioSize ?? 2d;

		return (float)Math.Clamp(6d + 3d * size, 6d, 30d);
	}

	/// <summary>
	/// Подпись фигуры.
	///
	/// Visio держит текст внутри той же фигуры, у документа это отдельный
	/// объект, поэтому подпись рождается рядом с контуром, а не вместо него.
	/// Ставится по центру габаритов фигуры: собственные поля текстового
	/// блока Visio описывает отдельной матрицей TextXForm, но у подписей
	/// вроде «№4» она совпадает с центром, а разбор её ради редкого случая
	/// добавил бы ещё один слой наследования.
	/// </summary>
	private static TextShape? BuildText(VisioXmlShape shape, VisioTransformChain chain)
	{
		string? text = shape.Text();

		if (string.IsNullOrWhiteSpace(text))
			return null;

		var label = new TextShape
		{
			Text = text.TrimEnd('\r', '\n'),
			Name = text.Trim()
		};

		// Кегль Visio задаёт в дюймах, и единица набора — тоже дюйм.
		double size = shape.SectionRows("Character")
			.Select(r => r.Value("Size"))
			.FirstOrDefault(v => v.HasValue) ?? 0.1667d;

		label.FontSizeMm = (float)size;

		label.Fill.Enabled = true;
		label.Fill.Kind = FillKind.Solid;
		label.Fill.SolidColor = CharacterColour(shape);
		label.Stroke.Enabled = false;

		double width = shape.Cell("Width") ?? 0d;
		double height = shape.Cell("Height") ?? 0d;

		var centre = chain.Apply(width * 0.5d, height * 0.5d);
		var local = label.GetLocalBounds();

		label.LocalTransform = SKMatrix.CreateTranslation(
			centre.X - local.MidX,
			centre.Y - local.MidY);

		return label;
	}

	private static SKColor CharacterColour(VisioXmlShape shape)
	{
		foreach (var row in shape.SectionRows("Character"))
		{
			var colour = shape.ResolveColour(row.Text("Color"));

			if (colour.HasValue)
				return colour.Value;
		}

		return SKColors.Black;
	}

	#endregion
}

#endregion
