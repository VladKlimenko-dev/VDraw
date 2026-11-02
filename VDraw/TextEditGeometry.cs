using SkiaSharp;

using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Где на листе стоит каретка и что закрашено выделением.
///
/// Вынесено из TextEditSession: там правила про символы и строки,
/// здесь — пересчёт их в точки. Смешай одно с другим, и ни то,
/// ни другое не прочитать.
///
/// Считается по тем же продвижениям, какими текст и рисуется
/// (TextGlyphs), иначе каретка вставала бы не туда, где потом
/// появляется буква.
/// </summary>
public static class TextEditGeometry
{
	/// <summary>Отрезок каретки в локальных координатах надписи.</summary>
	public static (SKPoint Top, SKPoint Bottom) CaretLine(TextEditSession session)
	{
		var shape = session.Shape;
		var (line, column) = session.LineAndColumn(session.Caret);

		using var font = shape.CreateEditFont();

		float x = shape.LocalOrigin.X + ColumnOffset(font, shape, session.Lines()[line], column);
		float baseline = shape.LocalOrigin.Y + line * shape.EditLineStep;

		var metrics = font.Metrics;

		return (
			new SKPoint(x, baseline + metrics.Ascent),
			new SKPoint(x, baseline + metrics.Descent));
	}

	/// <summary>
	/// Прямоугольники выделения — по одному на каждую задетую строку.
	///
	/// Пустая строка внутри выделения получает прямоугольник шириной
	/// в полпробела: иначе выделенный перевод строки выглядел бы
	/// пропуском, и было бы непонятно, что он тоже удалится.
	/// </summary>
	public static List<SKRect> SelectionRects(TextEditSession session)
	{
		var rects = new List<SKRect>();

		if (!session.HasSelection)
			return rects;

		var shape = session.Shape;
		var lines = session.Lines();

		using var font = shape.CreateEditFont();

		var metrics = font.Metrics;

		int from = session.SelectionStart;
		int to = session.SelectionEnd;

		var (firstLine, firstColumn) = session.LineAndColumn(from);
		var (lastLine, lastColumn) = session.LineAndColumn(to);

		for (int line = firstLine; line <= lastLine; line++)
		{
			string text = lines[line];

			int start = line == firstLine ? firstColumn : 0;
			int end = line == lastLine ? lastColumn : text.Length;

			float left = shape.LocalOrigin.X + ColumnOffset(font, shape, text, start);
			float right = shape.LocalOrigin.X + ColumnOffset(font, shape, text, end);

			// Перевод строки сам по себе ширины не имеет, но в выделение
			// входит: показываем его узкой полоской за концом строки.
			if (line != lastLine)
				right += font.Size * 0.3f;

			float baseline = shape.LocalOrigin.Y + line * shape.EditLineStep;

			rects.Add(new SKRect(
				left,
				baseline + metrics.Ascent,
				right,
				baseline + metrics.Descent));
		}

		return rects;
	}

	/// <summary>
	/// Номер символа, на который показывает точка в локальных координатах.
	///
	/// Считается ближайшая ГРАНИЦА между символами, а не символ под
	/// точкой: каретка стоит между буквами, и щелчок в правую половину
	/// буквы должен ставить её справа от этой буквы.
	/// </summary>
	public static int IndexAt(TextEditSession session, SKPoint local)
	{
		var shape = session.Shape;
		var lines = session.Lines();

		using var font = shape.CreateEditFont();

		var metrics = font.Metrics;
		float step = shape.EditLineStep;

		// Верх первой строки: от базовой линии вверх на подъём шрифта
		// (Ascent в Skia отрицателен).
		float top = shape.LocalOrigin.Y + metrics.Ascent;

		int line = step > 0f ? (int)MathF.Floor((local.Y - top) / step) : 0;

		line = Math.Clamp(line, 0, lines.Count - 1);

		string text = lines[line];
		float x = local.X - shape.LocalOrigin.X;

		int column = 0;
		float previous = 0f;

		while (column < text.Length)
		{
			float next = ColumnOffset(font, shape, text, column + 1);

			// Через ту же меру, что и у каретки, а не через продвижение
			// каждого символа по отдельности: шрифт сближает иные пары
			// букв, и две меры разошлись бы — щелчок ставил бы каретку
			// не туда, где она потом нарисуется.
			if (x < (previous + next) / 2f)
				break;

			previous = next;
			column++;
		}

		return session.LineStart(line) + column;
	}

	/// <summary>Смещение начала символа от левого края строки.</summary>
	private static float ColumnOffset(SKFont font, TextShape shape, string line, int column)
	{
		column = Math.Clamp(column, 0, line.Length);

		if (column == 0)
			return 0f;

		// Measure не добавляет интервал после последнего символа,
		// а каретка стоит именно после него: без этой добавки при
		// заданном межсимвольном интервале она налезала бы на букву.
		return TextGlyphs.Measure(font, line[..column], shape.CharacterSpacingMm)
			+ shape.CharacterSpacingMm;
	}
}
