using SkiaSharp;

namespace VDraw.Editing;

/// <summary>
/// Кодировщик анимированного GIF89a.
///
/// Написан вручную, потому что взять готовый неоткуда: SkiaSharp кодирует
/// PNG, JPEG и WEBP, но не GIF, а GDI+ умеет писать только одиночный кадр —
/// многокадровая запись через SaveAdd работает для TIFF и на GIF
/// не распространяется. Оставался выбор между сторонним пакетом и
/// собственной реализацией; формат несложный и полностью описан
/// спецификацией 89a, поэтому реализация своя.
///
/// Ограничения формата, которые видны пользователю:
///   — не более 256 цветов на кадр, поэтому градиенты и фотографии
///     приходится квантовать;
///   — прозрачность булева: пиксель либо полностью виден, либо полностью
///     прозрачен, полутонов у края нет;
///   — задержка кадра хранится в сотых долях секунды, поэтому частота
///     кадров округляется.
/// </summary>
public static class GifEncoder
{
	/// <summary>Один кадр анимации.</summary>
	public sealed class Frame
	{
		public required SKBitmap Bitmap { get; init; }

		/// <summary>Задержка перед следующим кадром, в сотых долях секунды.</summary>
		public required int DelayCentiseconds { get; init; }
	}

	/// <summary>
	/// Порог непрозрачности. Всё ниже становится прозрачным, всё выше —
	/// полностью непрозрачным.
	///
	/// Половина взята намеренно: у сглаженного края альфа распределена
	/// равномерно, и при таком пороге контур остаётся на своём месте,
	/// а не разбухает и не съедается.
	/// </summary>
	private const byte AlphaThreshold = 128;

	#region Запись

	/// <summary>
	/// Пишет анимацию в файл.
	///
	/// <paramref name="loop"/> — число повторов: 0 означает бесконечный
	/// цикл, что для анимации и есть обычное поведение.
	/// </summary>
	public static void Write(
		string path,
		IReadOnlyList<Frame> frames,
		bool transparent,
		int loop = 0)
	{
		if (frames == null || frames.Count == 0)
			throw new ArgumentException("No frames to write.", nameof(frames));

		using var stream = File.Create(path);

		Write(stream, frames, transparent, loop);
	}

	public static void Write(
		Stream stream,
		IReadOnlyList<Frame> frames,
		bool transparent,
		int loop = 0)
	{
		int width = frames[0].Bitmap.Width;
		int height = frames[0].Bitmap.Height;

		WriteHeader(stream, width, height);

		// Расширение NETSCAPE2.0 — единственный способ задать зацикливание.
		// В спецификации его нет, но читают его все.
		WriteLoopExtension(stream, loop);

		foreach (var frame in frames)
		{
			var quantized = Quantize(frame.Bitmap, transparent);

			WriteGraphicControlExtension(
				stream,
				frame.DelayCentiseconds,
				quantized.TransparentIndex);

			WriteImageDescriptor(stream, width, height, quantized.Palette.Count);
			WriteColorTable(stream, quantized.Palette);
			WriteLzwImageData(stream, quantized.Indices, quantized.Palette.Count);
		}

		stream.WriteByte(0x3B);
	}

	private static void WriteHeader(Stream s, int width, int height)
	{
		foreach (char c in "GIF89a")
			s.WriteByte((byte)c);

		WriteShort(s, width);
		WriteShort(s, height);

		// Глобальной таблицы цветов нет: у каждого кадра своя локальная.
		// Общая палитра на всю анимацию экономила бы байты, но заметно
		// портила бы кадры, чьи цвета расходятся с усреднённой.
		s.WriteByte(0x70);
		s.WriteByte(0x00);
		s.WriteByte(0x00);
	}

	private static void WriteLoopExtension(Stream s, int loop)
	{
		s.WriteByte(0x21);
		s.WriteByte(0xFF);
		s.WriteByte(0x0B);

		foreach (char c in "NETSCAPE2.0")
			s.WriteByte((byte)c);

		s.WriteByte(0x03);
		s.WriteByte(0x01);

		WriteShort(s, Math.Clamp(loop, 0, 65535));

		s.WriteByte(0x00);
	}

	private static void WriteGraphicControlExtension(Stream s, int delay, int transparentIndex)
	{
		s.WriteByte(0x21);
		s.WriteByte(0xF9);
		s.WriteByte(0x04);

		bool hasTransparency = transparentIndex >= 0;

		// Способ утилизации 2 — «восстановить фон». Нужен именно при
		// прозрачности: без него прозрачные места показывали бы предыдущий
		// кадр, и движущийся объект оставлял бы за собой шлейф.
		int disposal = hasTransparency ? 2 : 1;

		int packed = (disposal << 2) | (hasTransparency ? 1 : 0);

		s.WriteByte((byte)packed);

		WriteShort(s, Math.Clamp(delay, 0, 65535));

		s.WriteByte((byte)(hasTransparency ? transparentIndex : 0));
		s.WriteByte(0x00);
	}

	private static void WriteImageDescriptor(Stream s, int width, int height, int paletteCount)
	{
		s.WriteByte(0x2C);

		WriteShort(s, 0);
		WriteShort(s, 0);
		WriteShort(s, width);
		WriteShort(s, height);

		int bits = GetPaletteBits(paletteCount);

		// Бит 7 — наличие локальной таблицы, младшие три — её размер.
		s.WriteByte((byte)(0x80 | (bits - 1)));
	}

	private static void WriteColorTable(Stream s, IReadOnlyList<SKColor> palette)
	{
		int bits = GetPaletteBits(palette.Count);
		int size = 1 << bits;

		for (int i = 0; i < size; i++)
		{
			if (i < palette.Count)
			{
				s.WriteByte(palette[i].Red);
				s.WriteByte(palette[i].Green);
				s.WriteByte(palette[i].Blue);
			}
			else
			{
				// Таблица обязана быть степенью двойки. Хвост заполняется
				// чёрным: на эти индексы всё равно никто не ссылается.
				s.WriteByte(0);
				s.WriteByte(0);
				s.WriteByte(0);
			}
		}
	}

	private static int GetPaletteBits(int count)
	{
		int bits = 1;

		while ((1 << bits) < count && bits < 8)
			bits++;

		return bits;
	}

	private static void WriteShort(Stream s, int value)
	{
		s.WriteByte((byte)(value & 0xFF));
		s.WriteByte((byte)((value >> 8) & 0xFF));
	}

	#endregion

	#region Квантование

	private sealed class QuantizedFrame
	{
		public required byte[] Indices { get; init; }
		public required List<SKColor> Palette { get; init; }

		/// <summary>Индекс прозрачного цвета или -1.</summary>
		public required int TransparentIndex { get; init; }
	}

	/// <summary>
	/// Приводит кадр к палитре не более чем из 256 цветов методом медианного
	/// сечения.
	///
	/// Медианное сечение выбрано вместо равномерной сетки цветов потому,
	/// что чертёж — не фотография: в нём обычно немного цветов, но важны
	/// они точно. Равномерная сетка сместила бы каждый на несколько единиц,
	/// и однотонная заливка перестала бы совпадать с исходной.
	/// </summary>
	private static QuantizedFrame Quantize(SKBitmap bitmap, bool transparent)
	{
		int width = bitmap.Width;
		int height = bitmap.Height;

		var pixels = bitmap.Pixels;

		// Прозрачный цвет занимает нулевой индекс, если он нужен.
		int reserved = transparent ? 1 : 0;
		int maxColors = 256 - reserved;

		var opaque = new List<SKColor>(Math.Min(pixels.Length, 4096));
		var seen = new HashSet<uint>();

		foreach (var p in pixels)
		{
			if (transparent && p.Alpha < AlphaThreshold)
				continue;

			uint key = (uint)((p.Red << 16) | (p.Green << 8) | p.Blue);

			if (seen.Add(key))
				opaque.Add(new SKColor(p.Red, p.Green, p.Blue));
		}

		var palette = new List<SKColor>(256);

		if (transparent)
		{
			// Сам цвет прозрачного индекса не виден, но записать его надо.
			palette.Add(new SKColor(0, 0, 0));
		}

		if (opaque.Count == 0)
			opaque.Add(SKColors.White);

		palette.AddRange(opaque.Count <= maxColors
			? opaque
			: MedianCut(opaque, maxColors));

		var lookup = BuildLookup(palette, reserved);

		var indices = new byte[width * height];

		for (int i = 0; i < pixels.Length; i++)
		{
			var p = pixels[i];

			if (transparent && p.Alpha < AlphaThreshold)
			{
				indices[i] = 0;
				continue;
			}

			indices[i] = lookup(p);
		}

		return new QuantizedFrame
		{
			Indices = indices,
			Palette = palette,
			TransparentIndex = transparent ? 0 : -1
		};
	}

	/// <summary>
	/// Медианное сечение: множество цветов делится по самой широкой оси
	/// пополам, пока число групп не достигнет предела. Цвет группы —
	/// среднее по ней.
	/// </summary>
	private static List<SKColor> MedianCut(List<SKColor> colors, int maxColors)
	{
		var buckets = new List<List<SKColor>> { colors };

		while (buckets.Count < maxColors)
		{
			int index = -1;
			int widest = 0;

			for (int i = 0; i < buckets.Count; i++)
			{
				if (buckets[i].Count < 2)
					continue;

				int range = GetWidestRange(buckets[i], out _);

				if (range <= widest)
					continue;

				widest = range;
				index = i;
			}

			// Делить больше нечего: все группы одноцветные.
			if (index < 0)
				break;

			var bucket = buckets[index];

			GetWidestRange(bucket, out int axis);

			bucket.Sort((a, b) => Channel(a, axis).CompareTo(Channel(b, axis)));

			int half = bucket.Count / 2;

			buckets[index] = bucket.GetRange(0, half);
			buckets.Add(bucket.GetRange(half, bucket.Count - half));
		}

		var result = new List<SKColor>(buckets.Count);

		foreach (var bucket in buckets)
		{
			if (bucket.Count == 0)
				continue;

			long r = 0, g = 0, b = 0;

			foreach (var c in bucket)
			{
				r += c.Red;
				g += c.Green;
				b += c.Blue;
			}

			result.Add(new SKColor(
				(byte)(r / bucket.Count),
				(byte)(g / bucket.Count),
				(byte)(b / bucket.Count)));
		}

		return result;
	}

	private static int GetWidestRange(List<SKColor> colors, out int axis)
	{
		int minR = 255, minG = 255, minB = 255;
		int maxR = 0, maxG = 0, maxB = 0;

		foreach (var c in colors)
		{
			minR = Math.Min(minR, c.Red);
			minG = Math.Min(minG, c.Green);
			minB = Math.Min(minB, c.Blue);

			maxR = Math.Max(maxR, c.Red);
			maxG = Math.Max(maxG, c.Green);
			maxB = Math.Max(maxB, c.Blue);
		}

		int dr = maxR - minR;
		int dg = maxG - minG;
		int db = maxB - minB;

		if (dg >= dr && dg >= db)
		{
			axis = 1;
			return dg;
		}

		if (dr >= db)
		{
			axis = 0;
			return dr;
		}

		axis = 2;
		return db;
	}

	private static int Channel(SKColor c, int axis) => axis switch
	{
		0 => c.Red,
		1 => c.Green,
		_ => c.Blue
	};

	/// <summary>
	/// Сопоставление цвета ближайшему индексу палитры, с запоминанием
	/// результата.
	///
	/// Кэш здесь не оптимизация, а необходимость: без него на каждый пиксель
	/// кадра пришлось бы до 256 сравнений, то есть для растра в мегапиксель —
	/// четверть миллиарда операций на кадр.
	/// </summary>
	private static Func<SKColor, byte> BuildLookup(List<SKColor> palette, int firstIndex)
	{
		var cache = new Dictionary<uint, byte>();

		return color =>
		{
			uint key = (uint)((color.Red << 16) | (color.Green << 8) | color.Blue);

			if (cache.TryGetValue(key, out byte cached))
				return cached;

			int best = firstIndex;
			int bestDistance = int.MaxValue;

			for (int i = firstIndex; i < palette.Count; i++)
			{
				int dr = palette[i].Red - color.Red;
				int dg = palette[i].Green - color.Green;
				int db = palette[i].Blue - color.Blue;

				int distance = dr * dr + dg * dg + db * db;

				if (distance >= bestDistance)
					continue;

				bestDistance = distance;
				best = i;

				if (distance == 0)
					break;
			}

			byte result = (byte)best;
			cache[key] = result;

			return result;
		};
	}

	#endregion

	#region Сжатие LZW

	/// <summary>
	/// Сжатие индексов алгоритмом LZW в том виде, в каком его требует GIF:
	/// переменная разрядность кода, коды очистки и конца, запись блоками
	/// не длиннее 255 байт.
	/// </summary>
	private static void WriteLzwImageData(Stream stream, byte[] indices, int paletteCount)
	{
		int bits = Math.Max(2, GetPaletteBits(paletteCount));

		stream.WriteByte((byte)bits);

		int clearCode = 1 << bits;
		int endCode = clearCode + 1;

		var dictionary = new Dictionary<(int Prefix, byte Suffix), int>();

		int nextCode = endCode + 1;
		int codeSize = bits + 1;

		using var output = new BlockWriter(stream);

		output.Write(clearCode, codeSize);

		if (indices.Length == 0)
		{
			output.Write(endCode, codeSize);
			output.Flush();
			return;
		}

		int prefix = indices[0];

		for (int i = 1; i < indices.Length; i++)
		{
			byte suffix = indices[i];
			var key = (prefix, suffix);

			if (dictionary.TryGetValue(key, out int code))
			{
				prefix = code;
				continue;
			}

			output.Write(prefix, codeSize);

			if (nextCode < 4096)
			{
				dictionary[key] = nextCode++;

				// Разрядность растёт ровно тогда, когда следующий код
				// перестаёт помещаться. Декодер считает так же, поэтому
				// договорённость обязана совпадать до кода.
				if (nextCode > (1 << codeSize) && codeSize < 12)
					codeSize++;
			}
			else
			{
				output.Write(clearCode, codeSize);

				dictionary.Clear();
				nextCode = endCode + 1;
				codeSize = bits + 1;
			}

			prefix = suffix;
		}

		output.Write(prefix, codeSize);
		output.Write(endCode, codeSize);
		output.Flush();
	}

	/// <summary>
	/// Побитовая запись кодов с разбиением на блоки GIF.
	///
	/// Коды идут младшими битами вперёд и не выравниваются по границе
	/// байта, поэтому накапливаются в буфере; данные изображения пишутся
	/// блоками, каждому предшествует байт длины, завершает всё нулевая
	/// длина.
	/// </summary>
	private sealed class BlockWriter : IDisposable
	{
		private readonly Stream _stream;
		private readonly byte[] _block = new byte[255];

		private int _blockLength;
		private int _bitBuffer;
		private int _bitCount;

		public BlockWriter(Stream stream) => _stream = stream;

		public void Write(int code, int codeSize)
		{
			_bitBuffer |= code << _bitCount;
			_bitCount += codeSize;

			while (_bitCount >= 8)
			{
				AddByte((byte)(_bitBuffer & 0xFF));

				_bitBuffer >>= 8;
				_bitCount -= 8;
			}
		}

		public void Flush()
		{
			if (_bitCount > 0)
			{
				AddByte((byte)(_bitBuffer & 0xFF));

				_bitBuffer = 0;
				_bitCount = 0;
			}

			FlushBlock();

			_stream.WriteByte(0x00);
		}

		private void AddByte(byte value)
		{
			_block[_blockLength++] = value;

			if (_blockLength == 255)
				FlushBlock();
		}

		private void FlushBlock()
		{
			if (_blockLength == 0)
				return;

			_stream.WriteByte((byte)_blockLength);
			_stream.Write(_block, 0, _blockLength);

			_blockLength = 0;
		}

		public void Dispose()
		{
		}
	}

	#endregion
}
