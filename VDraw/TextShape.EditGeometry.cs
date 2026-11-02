using SkiaSharp;

namespace VDraw.Shapes;

/// <summary>
/// Сведения о раскладке надписи для правки её на листе.
///
/// Здесь только то, что нельзя посчитать снаружи: раскладка текста
/// по фигуре и места букв вдоль кривой живут внутри TextShape, вместе
/// с настройками, областями и обтеканием. Всё остальное — где каретка,
/// что выделено — считает TextEditGeometry по этим сведениям.
/// </summary>
public sealed partial class TextShape
{
	/// <summary>
	/// Раскладка текста по фигуре — та же, по которой он и рисуется.
	///
	/// Строки возвращаются с привязкой к исходному тексту (PlacedLine.Runs):
	/// раскладка сжимает лишние пробелы, убирает переводы строк и рвёт
	/// длинные слова, поэтому место в собранной строке и место в исходном
	/// тексте — разные числа.
	///
	/// null — текст не по фигуре или область не построилась.
	/// </summary>
	internal TextAreaLayout.Result? BuildAreaLayoutForEdit()
	{
		if (!IsAreaText)
			return null;

		if (!TryBuildFlowRegions(out var layoutRegion, out var clipRegion, out var obstacles)
			|| layoutRegion == null)
		{
			DisposeRegions(layoutRegion, clipRegion, obstacles);

			return null;
		}

		try
		{
			using var font = CreateLocalFont();

			return TextAreaLayout.Layout(
				Text, font, layoutRegion, BuildAreaOptions(font, obstacles));
		}
		finally
		{
			DisposeRegions(layoutRegion, clipRegion, obstacles);
		}
	}

	/// <summary>Отрезок каретки: сверху вниз по высоте строки.</summary>
	internal readonly struct CaretSpot
	{
		public readonly SKPoint Top;
		public readonly SKPoint Bottom;
		public readonly bool Valid;

		public CaretSpot(SKPoint top, SKPoint bottom)
		{
			Top = top;
			Bottom = bottom;
			Valid = true;
		}
	}

	/// <summary>
	/// Места каретки для текста по кривой — в МИРОВЫХ координатах.
	///
	/// Мировых, а не местных: буквы раскладывает ShapePathSampler
	/// по контуру другой фигуры, а тот живёт в мире, и переводить его
	/// в систему надписи ради каретки значило бы повторять весь расчёт
	/// во второй системе координат.
	///
	/// Длина массива — на единицу больше длины текста: каретка стоит
	/// между символами, и мест у неё на одно больше. Там, где текста
	/// на контуре не оказалось (кривая кончилась), место помечено
	/// недействительным.
	///
	/// Повторяет ход BuildAlongShapePath: тот же порядок строк, те же
	/// продвижения, тот же отступ от контура. Расходиться им нельзя —
	/// каретка встанет не у той буквы.
	/// </summary>
	internal CaretSpot[]? BuildAlongCaretSpots()
	{
		string text = (Text ?? string.Empty)
			.Replace("\r\n", "\n")
			.Replace('\r', '\n');

		if (!AlongShape || otherShape == null || text.Length == 0)
			return null;

		using var font = CreateLocalFont();
		using var otherWorldPath = otherShape.BuildWorldPath();

		if (otherWorldPath == null || otherWorldPath.IsEmpty)
			return null;

		GetWorldScale(out float scaleX, out float scaleY);

		using var samplePath = new SKPath(otherWorldPath);

		var sampler = ShapePathSampler.FromPath(samplePath, 0.1f);

		if (sampler.TotalLength <= 1e-6f)
			return null;

		var spots = new CaretSpot[text.Length + 1];

		var metrics = font.Metrics;

		float ascent = metrics.Ascent * scaleY;
		float descent = metrics.Descent * scaleY;

		float lineStep = (FontSizeMm * 1.2f + LineSpacingMm) * scaleY;
		float charSpacing = CharacterSpacingMm * scaleX;

		string[] lines = text.Split('\n');

		int index = 0;

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex];

			float normalOffset = ay + lineIndex * lineStep;
			float s = ax;

			for (int i = 0; i <= line.Length; i++)
			{
				spots[index + i] = SpotAt(sampler, s, normalOffset, ascent, descent);

				if (i == line.Length)
					break;

				char ch = line[i];

				s += ch == '\t'
					? TextGlyphs.GetTabWidth(font) * scaleX + charSpacing
					: font.MeasureText(ch.ToString()) * scaleX + charSpacing;
			}

			// Плюс один на перевод строки: он тоже занимает место
			// в исходном тексте, а своего места на кривой не имеет.
			index += line.Length + 1;
		}

		return spots;
	}

	private CaretSpot SpotAt(
		ShapePathSampler sampler, float s, float normalOffset, float ascent, float descent)
	{
		if (!sampler.TryEvaluate(s, out var basePoint, out float tangentAngle))
			return default;

		// Нормаль к контуру: по ней отложен и отступ строки, и высота
		// каретки, — тогда каретка стоит поперёк букв, как им и положено.
		float nx = -MathF.Sin(tangentAngle);
		float ny = MathF.Cos(tangentAngle);

		// Буквы, не повёрнутые по касательной, стоят вертикально,
		// и каретка при них тоже вертикальна.
		if (!tangentAlong)
		{
			nx = 0f;
			ny = 1f;
		}

		var origin = new SKPoint(
			basePoint.X + (-MathF.Sin(tangentAngle)) * normalOffset,
			basePoint.Y + MathF.Cos(tangentAngle) * normalOffset);

		return new CaretSpot(
			new SKPoint(origin.X + nx * ascent, origin.Y + ny * ascent),
			new SKPoint(origin.X + nx * descent, origin.Y + ny * descent));
	}
}
