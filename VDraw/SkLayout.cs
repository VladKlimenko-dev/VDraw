using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public enum SkFlowDirection
{
	Vertical,
	Horizontal
}

public sealed class SkFlowLayout : SkControl
{
	public SkFlowDirection Direction { get; set; } = SkFlowDirection.Vertical;
	public float Gap { get; set; } = 8f;
	public float Padding { get; set; } = 0f;
	public bool AutoHeight { get; set; } = true;

	/// <summary>
	/// Переносить содержимое на следующую строку, когда очередной элемент
	/// не помещается в ширину.
	///
	/// По умолчанию выключено, и это осознанно. Горизонтальный поток
	/// используется и там, где строка обязана остаться одной — ползунок
	/// со счётчиком, пара кнопок у поля. Раскладка вызывается в том числе
	/// в моменты, когда ширина ещё не установлена, и включённый по умолчанию
	/// перенос разложил бы такие пары в столбик. Ряды, которым перенос
	/// нужен, включают его явно.
	///
	/// Работает только при горизонтальном направлении: у вертикального
	/// переносить нечего.
	/// </summary>
	public bool WrapContents { get; set; } = false;

	public override void Arrange()
	{
		float x = Padding;
		float y = Padding;

		// Высота текущей строки. Именно её не хватало прежней раскладке:
		// без неё следующая строка не знала бы, на сколько опуститься.
		float rowHeight = 0f;

		float rightLimit = Bounds.Width - Padding;

		foreach (var child in Children)
		{
			if (!child.Visible)
			{
				// Скрытый элемент схлопывается. Иначе его Bounds остаются от
				// прошлого прохода, и при включении он оказывается не на месте.
				child.Bounds = new SKRect(Padding, y, Bounds.Width - Padding, y);
				continue;
			}

			if (Direction == SkFlowDirection.Vertical)
			{
				child.Bounds = new SKRect(Padding, y, Bounds.Width - Padding, y + child.Bounds.Height);
				child.Arrange();
				y += child.Bounds.Height + Gap;
				continue;
			}

			// Перенос решается ДО размещения: элемент должен встать уже
			// на новую строку, иначе он успел бы вылезти за край.
			//
			// Условие x > Padding обязательно. Элемент шире всей панели
			// без него переносился бы бесконечно — на пустой строке он
			// тоже не помещается. Такой элемент остаётся в начале строки
			// и выходит за край: это меньшее зло, чем зацикливание.
			if (WrapContents && x > Padding && x + child.Bounds.Width > rightLimit)
			{
				x = Padding;
				y += rowHeight + Gap;
				rowHeight = 0f;
			}

			child.Bounds = new SKRect(x, y, x + child.Bounds.Width, y + child.Bounds.Height);
			child.Arrange();

			rowHeight = MathF.Max(rowHeight, child.Bounds.Height);
			x += child.Bounds.Width + Gap;
		}

		if (!AutoHeight)
			return;

		if (Direction == SkFlowDirection.Vertical)
		{
			Bounds = new SKRect(
				Bounds.Left,
				Bounds.Top,
				Bounds.Right,
				Bounds.Top + MathF.Max(0, y - Gap + Padding));

			return;
		}

		Bounds = new SKRect(
			Bounds.Left,
			Bounds.Top,
			Bounds.Right,
			Bounds.Top + y + rowHeight + Padding);
	}

}
