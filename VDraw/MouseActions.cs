namespace VDraw;

/// <summary>Одно действие мыши и то, что оно делает.</summary>
public sealed class MouseAction
{
	public required string Group { get; init; }

	/// <summary>Кнопка или кнопка с клавишей: «Middle button drag», «Ctrl + left click».</summary>
	public required string Gesture { get; init; }

	public required string Description { get; init; }
}

/// <summary>
/// Справочник неизменяемых действий мыши.
///
/// Здесь то, что настройке не подлежит и показывается только как
/// справка; настраиваемое живёт в MouseBindings и показывается
/// в тех же группах выше.
///
/// Не настраивается левая кнопка и всё, что на ней держится. Левая
/// кнопка означает «работать инструментом»: создание фигур, выделение,
/// перетаскивание маркеров, правка узлов — сотни мест, которые пришлось
/// бы переписать ради возможности, за которой никто не приходил.
/// Не настраивается и колесо: другого колеса у мыши нет, а масштаб
/// по нему ожидают все.
///
/// Список составлен по коду и правится вместе с ним. Чтобы он
/// не разошёлся с действительностью, над каждой группой указано, где
/// её проверять.
/// </summary>
public static class MouseActions
{
	private const string View = "Mouse: view";
	private const string Select = "Mouse: selection";
	private const string Move = "Mouse: moving and resizing";
	private const string Tools = "Mouse: shape tools";
	private const string Nodes = "Mouse: curve nodes";
	private const string Image = "Mouse: image editing";

	public static IReadOnlyList<MouseAction> All { get; } = new List<MouseAction>
	{
		// Жесты названы коротко: столбец под них узкий, а подробность
		// всё равно уходит в описание. «Протяжка» в русском длиннее
		// английского drag, и длинные жесты там обрезались бы.

		// MainView.OnMouseWheel, CreateGuideOperation
		M(View, "Wheel", "Zoom in and out around the pointer"),
		M(View, "Drag from a ruler", "Pulls out a guide"),

		// SelectTool.OnMouseDown, MarqueeSelectionOperation, MainView.OnMouseUp
		M(Select, "Left button", "Selects the shape under the pointer"),
		M(Select, "Frame, left to right", "Takes only what fits inside entirely"),
		M(Select, "Frame, right to left", "Takes everything the frame touches"),
		M(Select, "Centre handle", "Switches resizing and rotating"),

		// TransformSelectionOperation, MoveSelectionOperation, DragOperation
		M(Move, "Left drag", "Moves the selection"),
		M(Move, "Handle drag", "Resizes"),
		M(Move, "Rotation handle", "Rotates around the pivot"),

		// CreateShapesTools, ShapeCutter, MeasurementTool, AngleMeasurementTool
		M(Tools, "Left button", "Places the next point"),

		// MainView.OnMouseDoubleClick, CurveNodeMarqueeSelectionOperation, Curves.cs
		M(Nodes, "Double click", "Enters node editing"),
		M(Nodes, "Double click on a segment", "Inserts a node, keeping the curve"),
		M(Nodes, "Left button on a node", "Leaves that node alone selected"),
		M(Nodes, "Drag over the curve", "Frames the nodes"),
		M(Nodes, "Control point", "Bends the curve at the node"),

		// ImageToolSettings.GetPaintColor, ImagePaintEngine, lasso tools
		M(Image, "Double click", "Opens image editing"),
		M(Image, "Left with a brush", "Paints with the foreground colour"),
		M(Image, "Right with a brush", "Paints with the background colour"),
		M(Image, "Left with the fill", "Fills with the foreground colour"),
		M(Image, "Right with the fill", "Fills with the background colour"),
		M(Image, "Left with the eraser", "Erases. The right button does not"),
		M(Image, "Left, selection", "Selects pixels, dropping the previous ones")
	};

	private static MouseAction M(string group, string gesture, string description)
		=> new() { Group = group, Gesture = gesture, Description = description };
}
