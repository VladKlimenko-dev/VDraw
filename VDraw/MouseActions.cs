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
/// Справочник кнопок мыши: какая кнопка за какую операцию отвечает.
///
/// Только справка, настройке не подлежит. Кнопки мыши не лежат таблицей,
/// как сочетания клавиш: каждая разобрана там, где обрабатывается
/// нажатие, вместе с ходом операции — и разобрана по-разному в каждом
/// режиме и в каждом инструменте. Свести их в реестр значило бы
/// переписать весь разбор мыши, а пользователю нужно не это, а ответ
/// на вопрос «чем панорамируют и что делает Ctrl с протяжкой».
///
/// Поэтому список составлен по коду и правится вместе с ним. Чтобы он
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
		// MainView.OnMouseWheel, MainView.OnMouseDown (middle), CreateGuideOperation
		M(View, "Wheel", "Zoom in and out. The point under the pointer stays put"),
		M(View, "Middle button drag", "Pan the view"),
		M(View, "Left drag from a ruler", "Pull out a guide"),

		// SelectTool.OnMouseDown, MarqueeSelectionOperation, MainView.OnMouseUp
		M(Select, "Left click", "Select the shape under the pointer, dropping the rest"),
		M(Select, "Ctrl + left click", "Add the shape to the selection"),
		M(Select, "Left drag on empty space, left to right",
			"Window frame: takes only the shapes that fit inside the frame entirely"),
		M(Select, "Left drag on empty space, right to left",
			"Crossing frame: takes every shape the frame touches"),
		M(Select, "Ctrl + frame", "Keeps the current selection and adds the framed shapes"),
		M(Select, "Left click on the centre handle",
			"Switches the handles between resizing and rotating"),
		M(Select, "Right click", "Shape context menu. Paste puts the content at the pointer"),

		// TransformSelectionOperation, MoveSelectionOperation, DragOperation
		M(Move, "Left drag", "Move the selection"),
		M(Move, "Ctrl + drag", "Locks the movement to one axis"),
		M(Move, "Alt + drag", "Turns snapping off until the button is released"),
		M(Move, "Corner or side handle drag", "Resize"),
		M(Move, "Ctrl + handle drag", "Keeps the proportions"),
		M(Move, "Shift + handle drag", "Resizes around the centre"),
		M(Move, "Rotation handle drag", "Rotate around the pivot"),

		// CreateShapesTools, ShapeCutter, MeasurementTool, AngleMeasurementTool
		M(Tools, "Left click", "Place the next point of the shape being created"),
		M(Tools, "Right click while drawing a polygon", "Finishes the contour"),
		M(Tools, "Right click with any other tool",
			"Cancels what has been started and clears the placed points"),
		M(Tools, "Ctrl while placing an end of a measurement",
			"Does not attach the end to a shape"),
		M(Tools, "Shift while drawing a cutting line", "Locks the line to horizontal or vertical"),

		// MainView.OnMouseDoubleClick, CurveNodeMarqueeSelectionOperation, Curves.cs
		M(Nodes, "Double click on a shape", "Enters node editing"),
		M(Nodes, "Double click on a segment", "Inserts a node, leaving the curve as it is"),
		M(Nodes, "Left click on a node", "Leaves that node alone selected"),
		M(Nodes, "Ctrl + left click on a node", "Adds the node to the selected ones, or drops it"),
		M(Nodes, "Left drag over the curve itself", "Frames the nodes to select them"),
		M(Nodes, "Control point drag", "Bends the curve at the node"),
		M(Nodes, "Shift + control point drag", "Moves the opposite handle the same way, mirrored"),
		M(Nodes, "Alt + drag", "Turns snapping off"),

		// ImageToolSettings.GetPaintColor, ImagePaintEngine, lasso tools
		M(Image, "Double click on an image", "Opens image editing"),
		M(Image, "Left drag with a brush", "Paints with the foreground colour"),
		M(Image, "Right drag with a brush", "Paints with the background colour"),
		M(Image, "Left click with the fill tool", "Fills with the foreground colour"),
		M(Image, "Right click with the fill tool", "Fills with the background colour"),
		M(Image, "Left drag with the eraser", "Erases. The right button does not erase"),
		M(Image, "Left drag with a selection tool", "Selects pixels, dropping the previous selection"),
		M(Image, "Ctrl + selection", "Adds the pixels to the current selection")
	};

	private static MouseAction M(string group, string gesture, string description)
		=> new() { Group = group, Gesture = gesture, Description = description };
}
