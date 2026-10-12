using SkiaSharp;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

/// <summary>
/// Шаг сетки страницы и привязка к ней.
///
/// Шаг — одна единица измерения страницы. Каждая N-я линия рисуется темнее:
/// без такой разметки на мелком шаге глаз не может сосчитать клетки и
/// сетка превращается в серый фон.
///
/// Кратность зависит от системы единиц. У десятичных (мм, см, м, px)
/// это 5 и 10. У дюймов и футов десятка бессмысленна: в футе двенадцать
/// дюймов, и делить надо на 6 и 12, иначе крупная линия попадала бы
/// на середину дюйма и разметка противоречила бы самой единице.
/// </summary>
public static class PageGrid
{
	/// <summary>Ниже этого расстояния между линиями сетка не рисуется.</summary>
	private const float MinScreenStepPx = 4f;

	/// <summary>Базовый шаг сетки в единицах страницы. Всегда одна единица.</summary>
	public static float StepUom => 1f;

	/// <summary>
	/// Действующий шаг сетки при текущем масштабе.
	///
	/// При отдалении сетка не исчезает, а ПРОРЕЖИВАЕТСЯ: когда соседние
	/// линии сходятся ближе четырёх пикселей, шаг умножается на кратность
	/// средних линий, затем крупных, и дальше на крупную кратность
	/// столько раз, сколько нужно. Так на любом масштабе на экране
	/// остаётся читаемая сетка, а не серая заливка и не пустота.
	///
	/// Умножается именно на кратность разметки (5 и 10, у дюймов 6 и 12),
	/// а не на произвольное число: тогда оставшиеся линии — это ровно те,
	/// что и раньше рисовались тёмными, и разметка не съезжает.
	/// </summary>
	public static float GetEffectiveStepUom(Page? page, float pxPerUom)
	{
		float step = StepUom;

		if (pxPerUom <= 0.0001f)
			return step;

		if (IsStepVisible(step * pxPerUom))
			return step;

		var (medium, major) = GetDivisions(page);

		if (medium > 1)
		{
			step = StepUom * medium;

			if (IsStepVisible(step * pxPerUom))
				return step;
		}

		if (major <= 1)
			return step;

		step = StepUom * major;

		// Ограничение сверху — страховка от зацикливания при вырожденном
		// масштабе: без него нулевой pxPerUom дал бы бесконечный цикл.
		const float MaxStep = 1e6f;

		while (!IsStepVisible(step * pxPerUom) && step < MaxStep)
			step *= major;

		return step;
	}

	/// <summary>
	/// Во сколько раз действующий шаг крупнее базового. Нужно отрисовке,
	/// чтобы понять, какими по счёту в ИСХОДНОЙ разметке оказались
	/// оставшиеся линии, и сохранить деление на обычные, средние и крупные.
	/// </summary>
	public static int GetStepFactor(Page? page, float pxPerUom)
	{
		float effective = GetEffectiveStepUom(page, pxPerUom);
		return Math.Max(1, (int)MathF.Round(effective / StepUom));
	}

	/// <summary>
	/// Кратность средних и крупных линий для единиц страницы.
	/// </summary>
	public static (int Medium, int Major) GetDivisions(Page? page)
	{
		string title = page?.uom?.title?.Trim().ToLowerInvariant() ?? "mm";

		return title switch
		{
			"in" or "inch" or "inches" or "ft" or "foot" or "feet" => (6, 12),
			_ => (5, 10)
		};
	}

	/// <summary>
	/// Видима ли сетка на странице. Учитывает и флаг страницы, и общий
	/// переключатель в настройках.
	/// </summary>
	public static bool IsVisible(Page? page)
		=> page != null && page.GridEnabled;

	/// <summary>
	/// Достаточно ли крупен шаг, чтобы сетку имело смысл рисовать.
	///
	/// На сильном отдалении линии сливаются в заливку: рисовать тысячи
	/// линий, из которых получится серый прямоугольник, — только тратить
	/// время кадра.
	/// </summary>
	public static bool IsStepVisible(float screenStepPx)
		=> screenStepPx >= MinScreenStepPx;

	/// <summary>
	/// Ближайший узел сетки к точке. Координаты — абсолютные страничные,
	/// в единицах страницы.
	/// </summary>
	public static SKPoint Snap(SKPoint pagePoint, float step)
	{
		if (step <= 0.0001f)
			step = StepUom;

		// Координаты фигур уже отсчитываются от page_zero: там у них (0, 0).
		// Поэтому округления к кратному шагу достаточно, сдвигать на
		// page_origin не нужно.
		return new SKPoint(
			MathF.Round(pagePoint.X / step) * step,
			MathF.Round(pagePoint.Y / step) * step);
	}

	/// <summary>
	/// Привязка точки к сетке, если она включена и узел достаточно близко.
	///
	/// Возвращает null, когда привязываться не к чему: вызывающий тогда
	/// оставляет точку там, где её держит мышь.
	/// </summary>
	public static SnapResult? SnapPoint(Page? page, SKPoint pagePoint, float snapDistPage, float pxPerUom)
	{
		if (!IsVisible(page))
			return null;

		// Тем же шагом, каким сетка НАРИСОВАНА: прилипание к линиям,
		// которых на экране нет, выглядело бы случайным рывком.
		var target = Snap(pagePoint, GetEffectiveStepUom(page, pxPerUom));

		float dx = target.X - pagePoint.X;
		float dy = target.Y - pagePoint.Y;
		float d = MathF.Sqrt(dx * dx + dy * dy);

		if (d > snapDistPage)
			return null;

		return new SnapResult
		{
			Kind = SnapKind.Grid,
			SnappedWorldPoint = pagePoint,
			TargetWorldPoint = target,
			Correction = new SKPoint(dx, dy),
			DistancePage = d,
			SnapX = true,
			SnapY = true
		};
	}

	/// <summary>
	/// Привязка прямоугольника выделения: к сетке подтягивается ближайший
	/// из его углов и середин сторон.
	///
	/// Не только левый верхний угол: при перетаскивании естественно ждать,
	/// что к линии прилипнет та сторона, которую подвели к ней, а не
	/// заранее выбранная.
	/// </summary>
	public static SnapResult? SnapBounds(Page? page, SKRect bounds, float snapDistPage, float pxPerUom)
	{
		if (!IsVisible(page) || bounds.IsEmpty)
			return null;

		Span<SKPoint> probes = stackalloc SKPoint[8]
		{
			new SKPoint(bounds.Left, bounds.Top),
			new SKPoint(bounds.MidX, bounds.Top),
			new SKPoint(bounds.Right, bounds.Top),
			new SKPoint(bounds.Right, bounds.MidY),
			new SKPoint(bounds.Right, bounds.Bottom),
			new SKPoint(bounds.MidX, bounds.Bottom),
			new SKPoint(bounds.Left, bounds.Bottom),
			new SKPoint(bounds.Left, bounds.MidY)
		};

		SnapResult? best = null;

		foreach (var probe in probes)
		{
			var candidate = SnapPoint(page, probe, snapDistPage, pxPerUom);

			if (candidate != null && (best == null || candidate.DistancePage < best.DistancePage))
				best = candidate;
		}

		return best;
	}
}