using SkiaSharp;

using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Где на листе стоит каретка и что закрашено выделением.
///
/// Всё сводится к одному понятию — МЕСТО КАРЕТКИ: отрезок поперёк
/// строки, стоящий перед символом с таким номером. Мест на одно больше,
/// чем символов: каретка стоит и после последнего.
///
/// Через места выражается и остальное. Каретка — место под её номером.
/// Выделение — четырёхугольники между соседними местами. Попадание
/// мыши — ближайшее место. Благодаря этому три совершенно разные
/// раскладки — строки от начала координат, текст по фигуре с обтеканием
/// и текст по кривой — разбираются одним и тем же кодом, и отличаются
/// только тем, как считаются сами места.
///
/// Места считаются в МИРОВЫХ координатах: у текста по кривой буквы
/// раскладываются по контуру чужой фигуры, который живёт в мире,
/// и приводить всё к системе надписи пришлось бы ради одного случая
/// из трёх.
/// </summary>
public static class TextEditGeometry
{
	/// <summary>Место каретки: отрезок сверху вниз и номер строки.</summary>
	public readonly struct Spot
	{
		public readonly SKPoint Top;
		public readonly SKPoint Bottom;

		/// <summary>Строка раскладки. Нужна выделению: оно не склеивает строки.</summary>
		public readonly int Line;

		public readonly bool Valid;

		public Spot(SKPoint top, SKPoint bottom, int line)
		{
			Top = top;
			Bottom = bottom;
			Line = line;
			Valid = true;
		}

		public SKPoint Middle => new((Top.X + Bottom.X) / 2f, (Top.Y + Bottom.Y) / 2f);
	}

	#region Наружу

	public static (SKPoint Top, SKPoint Bottom)? CaretSegment(TextEditSession session)
	{
		var spots = Spots(session);

		if (spots.Length == 0)
			return null;

		var spot = spots[Math.Clamp(session.Caret, 0, spots.Length - 1)];

		return spot.Valid ? (spot.Top, spot.Bottom) : null;
	}

	/// <summary>
	/// Выделение четырёхугольниками: по одному на символ.
	///
	/// По символу, а не по строке: вдоль кривой строка изгибается,
	/// и одним четырёхугольником её не накрыть. На прямых строках
	/// соседние четырёхугольники сходятся край в край и выглядят
	/// сплошной полосой.
	/// </summary>
	public static List<SKPoint[]> SelectionQuads(TextEditSession session)
	{
		var quads = new List<SKPoint[]>();

		if (!session.HasSelection)
			return quads;

		var spots = Spots(session);

		int from = Math.Clamp(session.SelectionStart, 0, spots.Length - 1);
		int to = Math.Clamp(session.SelectionEnd, 0, spots.Length - 1);

		for (int i = from; i < to; i++)
		{
			var a = spots[i];
			var b = spots[i + 1];

			if (!a.Valid || !b.Valid || a.Line != b.Line)
				continue;

			// Нулевой ширины не рисуем: это перевод строки или место,
			// которому на раскладке не нашлось ширины.
			if (SKPoint.Distance(a.Middle, b.Middle) < 0.0001f)
				continue;

			quads.Add(new[] { a.Top, b.Top, b.Bottom, a.Bottom });
		}

		return quads;
	}

	/// <summary>
	/// Номер символа, на который показывает точка в мировых координатах.
	///
	/// Берётся ближайшее МЕСТО каретки, и расстояние считается до отрезка,
	/// а не до его середины: отрезок — это высота строки, и щелчок внутри
	/// строки оказывается ближе к её местам, чем к местам соседней,
	/// независимо от того, насколько строки разнесены.
	/// </summary>
	public static int IndexAt(TextEditSession session, SKPoint world)
	{
		var spots = Spots(session);

		int best = 0;
		float bestDistance = float.MaxValue;

		for (int i = 0; i < spots.Length; i++)
		{
			if (!spots[i].Valid)
				continue;

			float distance = DistanceToSegment(world, spots[i].Top, spots[i].Bottom);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}

		return best;
	}

	#endregion

	#region Места каретки

	public static Spot[] Spots(TextEditSession session)
	{
		var shape = session.Shape;

		if (shape.IsAreaText)
			return AreaSpots(session);

		if (shape.AlongShape && shape.otherShape != null)
			return AlongSpots(session);

		return PlainSpots(session);
	}

	/// <summary>Обычный текст: строки от начала координат надписи вниз.</summary>
	private static Spot[] PlainSpots(TextEditSession session)
	{
		var shape = session.Shape;
		var lines = session.Lines();

		var font = session.Font;

		var metrics = font.Metrics;
		var world = shape.GetWorldTransform();

		var spots = new Spot[session.Text.Length + 1];

		int index = 0;

		for (int line = 0; line < lines.Count; line++)
		{
			float baseline = shape.LocalOrigin.Y + line * shape.EditLineStep;

			for (int column = 0; column <= lines[line].Length; column++)
			{
				float x = shape.LocalOrigin.X + ColumnOffset(font, shape, lines[line], column);

				spots[index + column] = MakeSpot(world, x, baseline, metrics, line);
			}

			index += lines[line].Length + 1;
		}

		return spots;
	}

	/// <summary>
	/// Текст по фигуре: строки берутся у раскладки, а их места
	/// в исходном тексте — у привязки, которую раскладка оставила.
	/// </summary>
	private static Spot[] AreaSpots(TextEditSession session)
	{
		var shape = session.Shape;

		var spots = new Spot[session.Text.Length + 1];
		var layout = shape.BuildAreaLayoutForEdit();

		if (layout == null || layout.Lines.Count == 0)
			return spots;

		var font = session.Font;

		var metrics = font.Metrics;
		var world = shape.GetWorldTransform();

		for (int line = 0; line < layout.Lines.Count; line++)
		{
			var placed = layout.Lines[line];

			foreach (var run in placed.Runs)
			{
				// Место после куска тоже нужно: на конце слова каретка
				// стоит справа от последней буквы, а это уже следующий
				// по счёту номер.
				for (int k = 0; k <= run.Length; k++)
				{
					int source = run.Source + k;

					if (source < 0 || source >= spots.Length)
						continue;

					// Не затирать уже занятое: конец одного куска и начало
					// следующего приходятся на один номер, и верным
					// остаётся первый — он от своего куска.
					if (spots[source].Valid && k == 0)
						continue;

					float x = placed.X + ColumnOffset(font, shape, placed.Text, run.Placed + k);

					spots[source] = MakeSpot(world, x, placed.Baseline, metrics, line);
				}
			}
		}

		FillGaps(spots);

		return spots;
	}

	/// <summary>
	/// Места, которым раскладка ничего не сопоставила, получают место
	/// соседа слева.
	///
	/// Это лишние пробелы между словами и текст, не влезший в фигуру:
	/// на листе их не видно, но каретка по ним ходит, и пропадать ей
	/// нельзя — иначе при наборе пробелов она исчезает, и набирающий
	/// не понимает, куда попадёт следующая буква.
	/// </summary>
	private static void FillGaps(Spot[] spots)
	{
		Spot last = default;

		for (int i = 0; i < spots.Length; i++)
		{
			if (spots[i].Valid)
				last = spots[i];
			else if (last.Valid)
				spots[i] = last;
		}

		// Хвост заполнен, теперь начало: если текст начинается с пробелов,
		// первым местам слева взять нечего.
		Spot first = default;

		for (int i = 0; i < spots.Length; i++)
		{
			if (spots[i].Valid)
			{
				first = spots[i];
				break;
			}
		}

		if (!first.Valid)
			return;

		for (int i = 0; i < spots.Length && !spots[i].Valid; i++)
			spots[i] = first;
	}

	/// <summary>Текст по кривой: места считает сама надпись, они уже мировые.</summary>
	private static Spot[] AlongSpots(TextEditSession session)
	{
		var spots = new Spot[session.Text.Length + 1];
		var along = session.Shape.BuildAlongCaretSpots();

		if (along == null)
			return spots;

		int index = 0;

		var lines = session.Lines();

		for (int line = 0; line < lines.Count; line++)
		{
			for (int column = 0; column <= lines[line].Length; column++)
			{
				int i = index + column;

				if (i >= spots.Length || i >= along.Length || !along[i].Valid)
					continue;

				spots[i] = new Spot(along[i].Top, along[i].Bottom, line);
			}

			index += lines[line].Length + 1;
		}

		return spots;
	}

	private static Spot MakeSpot(SKMatrix world, float x, float baseline, SKFontMetrics metrics, int line)
		=> new(
			world.MapPoint(new SKPoint(x, baseline + metrics.Ascent)),
			world.MapPoint(new SKPoint(x, baseline + metrics.Descent)),
			line);

	#endregion

	#region Меры

	/// <summary>
	/// Смещение начала символа от левого края строки.
	///
	/// Считается ровно так, как текст и рисуется. Без межсимвольного
	/// интервала строка выводится одним вызовом, и ширина берётся
	/// у подстроки целиком — вместе с кернингом, то есть со сближением
	/// отдельных пар букв. С интервалом текст раскладывается посимвольно,
	/// кернинга в нём нет, и ширина складывается из продвижений: мерь
	/// её подстрокой — и каретка уезжала бы тем дальше, чем длиннее
	/// строка.
	/// </summary>
	private static float ColumnOffset(SKFont font, TextShape shape, string line, int column)
	{
		column = Math.Clamp(column, 0, line.Length);

		if (column == 0)
			return 0f;

		float spacing = shape.CharacterSpacingMm;

		if (spacing == 0f)
			return TextGlyphs.Measure(font, line[..column], 0f);

		float offset = 0f;

		for (int i = 0; i < column; i++)
			offset += TextGlyphs.GetCharAdvance(font, line[i], spacing);

		return offset;
	}

	private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		float lengthSquared = dx * dx + dy * dy;

		if (lengthSquared < 1e-9f)
			return SKPoint.Distance(p, a);

		float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;

		t = Math.Clamp(t, 0f, 1f);

		return SKPoint.Distance(p, new SKPoint(a.X + dx * t, a.Y + dy * t));
	}

	#endregion
}
