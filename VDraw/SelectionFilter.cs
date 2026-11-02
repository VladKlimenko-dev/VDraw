using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw.Editing;

/// <summary>
/// Виды объектов, доступные для выделения.
///
/// Флаги, а не список: набор проверяется на каждую фигуру при рамке
/// выделения, то есть десятки раз за движение мыши, и побитовая проверка
/// здесь дешевле любого перебора.
///
/// Групповые значения (AllShapes, Measurements, All) — это не отдельные
/// виды, а объединения: пункт меню «All shapes» включает и выключает сразу
/// все виды фигур, а своего бита у него нет. Иначе пришлось бы хранить
/// состояние, которое может разойтись с состоянием детей.
/// </summary>
[Flags]
public enum SelectionKinds
{
	None = 0,

	Circles = 1 << 0,
	Ellipses = 1 << 1,
	Rectangles = 1 << 2,
	RoundedRectangles = 1 << 3,
	ChamferedRectangles = 1 << 4,
	Polygons = 1 << 5,

	Curves = 1 << 6,
	Images = 1 << 7,
	Text = 1 << 8,

	DistanceMeasurements = 1 << 9,
	AngleMeasurements = 1 << 10,

	/// <summary>Все геометрические фигуры.</summary>
	AllShapes = Circles | Ellipses | Rectangles | RoundedRectangles
		| ChamferedRectangles | Polygons,

	/// <summary>Оба вида размеров.</summary>
	Measurements = DistanceMeasurements | AngleMeasurements,

	All = AllShapes | Curves | Images | Text | Measurements
}

/// <summary>
/// Отбор объектов для выделения.
///
/// Фильтр хранится в документе, а не в настройках приложения: он относится
/// к работе над конкретным чертежом. Отключив на время всё, кроме размеров,
/// пользователь не ждёт, что завтра в другом файле выделение по-прежнему
/// будет брать только их.
/// </summary>
public static class SelectionFilter
{
	/// <summary>
	/// Вид фигуры одним флагом.
	///
	/// Порядок проверок важен: ImageShape наследуется от CurveShape, и
	/// без проверки на изображение ПЕРЕД кривой картинка считалась бы
	/// кривой и попадала бы под чужой пункт меню.
	/// </summary>
	public static SelectionKinds KindOf(Shape shape)
	{
		return shape switch
		{
			Circle2PShape or Circle3PShape => SelectionKinds.Circles,
			Ellipse3PShape or Ellipse5PShape => SelectionKinds.Ellipses,
			RoundedRectangleShape => SelectionKinds.RoundedRectangles,
			ChamferedRectangleShape => SelectionKinds.ChamferedRectangles,
			RectangleShape => SelectionKinds.Rectangles,
			PolygonShape => SelectionKinds.Polygons,
			ImageShape => SelectionKinds.Images,
			TextShape => SelectionKinds.Text,
			AngleMeasurementShape => SelectionKinds.AngleMeasurements,
			MeasurementShape => SelectionKinds.DistanceMeasurements,
			CurveShape => SelectionKinds.Curves,
			_ => SelectionKinds.None
		};
	}

	/// <summary>
	/// Доступна ли фигура для выделения при заданном наборе видов.
	///
	/// Группа доступна, если доступен хоть один объект внутри неё. Иначе
	/// отбор по надписям не дал бы добраться до надписи, лежащей в группе,
	/// а именно так чаще всего и устроены чертежи.
	///
	/// Вид, которому не досталось своего флага (направляющая, инструмент
	/// преобразования), фильтром не отсеивается: его отбирают другие
	/// проверки на месте, и прятать его здесь значило бы сломать работу
	/// с ним незаметно для пользователя.
	/// </summary>
	public static bool Allows(Shape shape, SelectionKinds filter)
	{
		if (filter == SelectionKinds.All)
			return true;

		if (shape is GroupShape group)
		{
			foreach (var child in group.Children)
			{
				if (Allows(child, filter))
					return true;
			}

			return false;
		}

		var kind = KindOf(shape);

		if (kind == SelectionKinds.None)
			return true;

		return (filter & kind) != 0;
	}

	/// <summary>Набор видов активного документа. All, если документа нет.</summary>
	public static SelectionKinds Of(Document? doc)
		=> doc?.SelectionFilter ?? SelectionKinds.All;

	/// <summary>Доступна ли фигура при фильтре её документа.</summary>
	public static bool Allows(Shape shape, Document? doc)
		=> Allows(shape, Of(doc));
}
