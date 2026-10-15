using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Буфер формата фигуры: обводка и заливка со всеми параметрами.
///
/// Отдельно от буфера фигур (MainForm._clipboardShapes) намеренно. Это
/// разные буферы с разным временем жизни: скопировав фигуру, пользователь
/// не ожидает, что у него пропадёт ранее скопированный формат, и наоборот.
///
/// Хранятся клоны, а не ссылки на стили исходной фигуры. Иначе правка
/// оригинала через панель свойств меняла бы содержимое буфера задним числом,
/// а вставка формата связала бы две фигуры одним объектом стиля.
/// </summary>
public static class ShapeFormatClipboard
{
	#region Состояние

	private static StrokeStyle? _stroke;
	private static FillStyle? _fill;

	/// <summary>
	/// Мировые габариты фигуры, у которой взят формат.
	///
	/// Ручки заливки хранятся в МИРОВЫХ координатах — это видно по
	/// EnsureFillTransformDefaults, куда передаётся shape.GetBounds(). Без габаритов
	/// источника их нечем пересчитать, и градиент на новой фигуре оказался бы
	/// там же, где был на старой — то есть часто вообще за её пределами.
	/// </summary>
	private static SKRect _sourceBounds;

	/// <summary>Есть ли что вставлять.</summary>
	public static bool HasFormat => _stroke != null && _fill != null;

	/// <summary>Поднимается при копировании и очистке — для обновления кнопок.</summary>
	public static event Action? Changed;

	#endregion

	#region Копирование

	/// <summary>
	/// Забирает формат фигуры в буфер.
	///
	/// Группа исключена: собственных обводки и заливки у неё нет, их поля
	/// существуют лишь потому, что объявлены в базовом Shape. Копировать
	/// из группы значило бы взять значения, которые ни на что не влияют.
	/// </summary>
	public static bool Copy(Shape? source)
	{
		if (source == null || source is GroupShape)
			return false;

		_stroke = source.Stroke.Clone();
		_fill = source.Fill.Clone();
		_sourceBounds = source.GetBounds();

		Changed?.Invoke();
		return true;
	}

	public static void Clear()
	{
		if (!HasFormat)
			return;

		_stroke = null;
		_fill = null;

		Changed?.Invoke();
	}

	#endregion

	#region Вставка

	/// <summary>
	/// Применяет формат из буфера к фигурам. Возвращает число изменённых.
	///
	/// Вся вставка — один шаг истории, даже когда фигур много: пользователь
	/// сделал одно действие и отменять ожидает одним нажатием.
	///
	/// В группу формат раздаётся детям рекурсивно. Это единственный способ
	/// покрасить группу: сама она ничего не рисует.
	/// </summary>
	public static int Apply(IEnumerable<Shape> shapes, UndoStack? undo)
	{
		if (!HasFormat)
			return 0;

		var targets = ExpandForStyling(shapes).ToList();

		if (targets.Count == 0)
			return 0;

		var action = ShapeStyleAction.Capture("Paste format", targets);

		foreach (var shape in targets)
		{
			shape.Stroke = _stroke!.Clone();

			var fill = _fill!.Clone();
			RemapFillHandles(fill, _sourceBounds, shape.GetBounds());
			shape.Fill = fill;
		}

		action.CaptureAfter();

		if (action.HasChanges())
			undo?.Push(action);
		else
			action.Discard();

		return targets.Count;
	}

	/// <summary>
	/// Переносит ручки заливки с габаритов источника на габариты приёмника.
	///
	/// Смещение считается от центра: точка, стоявшая у левого края источника,
	/// окажется у левого края приёмника.
	///
	/// Масштаб применяется не всегда. Градиент задаёт ручками свою
	/// протяжённость, и на большей фигуре он обязан растянуться, иначе по краям
	/// останутся однотонные полосы. А вот у паттерна и текстуры вектор между
	/// ручками — это размер плитки, и растягивать его можно только тогда,
	/// когда пользователь сам этого попросил галочкой ScaleWithShape. Штриховка
	/// на плане должна сохранять шаг независимо от размера области.
	/// </summary>
	private static void RemapFillHandles(FillStyle fill, SKRect from, SKRect to)
	{
		// Ручки у источника ещё не задавались — пусть приёмник поставит
		// свои по умолчанию при первой отрисовке.
		if (!fill.FillTransformInitialized)
			return;

		// Вырожденные габариты: пересчитать нечего и не от чего.
		if (from.Width <= 0f || from.Height <= 0f || to.Width <= 0f || to.Height <= 0f)
		{
			fill.FillTransformInitialized = false;
			return;
		}

		bool scale = fill.Kind switch
		{
			FillKind.LinearGradient => true,
			FillKind.RadialGradient => true,
			FillKind.RectangularGradient => true,
			FillKind.Pattern => fill.ScaleWithShape,
			FillKind.Texture => fill.ScaleWithShape,
			_ => false
		};

		float sx = scale ? to.Width / from.Width : 1f;
		float sy = scale ? to.Height / from.Height : 1f;

		fill.StartPoint = Remap(fill.StartPoint, from, to, sx, sy);
		fill.EndPoint = Remap(fill.EndPoint, from, to, sx, sy);
	}

	private static SKPoint Remap(SKPoint p, SKRect from, SKRect to, float sx, float sy)
		=> new(
			to.MidX + (p.X - from.MidX) * sx,
			to.MidY + (p.Y - from.MidY) * sy);

	/// <summary>
	/// Разворачивает выделение в список фигур, которым применимо оформление:
	/// группы заменяются их детьми, запертые и направляющие пропускаются.
	///
	/// Открыто наружу: тем же разворотом пользуется палитра цвета линии
	/// и заливки. У группы собственные обводка и заливка ни на что
	/// не влияют — рисуются дети, каждый своими, — поэтому цвет,
	/// назначенный группе, пропадал без следа: он ложился в поля,
	/// которых никто не читает.
	///
	/// Рекурсивно: группа внутри группы разворачивается до листьев.
	/// </summary>
	public static IEnumerable<Shape> ExpandForStyling(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			if (shape.IsLocked || shape is GuideLine)
				continue;

			if (shape is GroupShape group)
			{
				foreach (var child in ExpandForStyling(group.Children))
					yield return child;

				continue;
			}

			yield return shape;
		}
	}

	#endregion
}