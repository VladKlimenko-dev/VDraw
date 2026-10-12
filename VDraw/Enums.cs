using System;
using System.Collections.Generic;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public enum PageOrientation
{
	Vertical = 0,
	Horizontal = 1
}

public enum EditorMode
{
	Shape,
	Curve,
	Image,
	Text
}

/// <summary>
/// Состояние проигрывателя анимации. Двух значений достаточно: пауза
/// и остановка отличались бы только тем, куда встаёт текущий кадр,
/// а он и так остаётся на месте.
/// </summary>
public enum AnimMode
{
	Stop = 0,
	Play = 1
}

public enum ImageEditToolKind
{
	None,
	Eraser,
	Brush,
	RectSelect,
	LassoSelect,
	Cursor,
	Fill,
	Lighten,
	Darken,
	Contrast,
	BW
}

public enum ImageSelectionHandle
{
	None,
	TopLeft,
	Top,
	TopRight,
	Right,
	BottomRight,
	Bottom,
	BottomLeft,
	Left
}

public enum FillKind
{
	None = 0,
	Solid = 1,
	LinearGradient = 2,
	RadialGradient = 3,
	RectangularGradient = 4,
	Pattern = 5,
	Texture = 6
}

public enum HandleKind
{
	None = 0,
	TopLeft,
	Top,
	TopRight,
	Right,
	BottomRight,
	Bottom,
	BottomLeft,
	Left,
	RotateTopLeft,
	RotateTopRight,
	RotateBottomRight,
	RotateBottomLeft,
	RotationCenter,
	TextAlongAxLeft,
	TextAlongAxRight,
	TextAlongAyTop,
	TextAlongAyBottom
}

public enum ShapeToolKind
{
	Select = 0,
	Line,
	Curve,
	Rectangle,
	RoundedRectangle,
	ChamferedRectangle,
	Circle2P,
	Circle3P,
	Ellipse3P,
	Ellipse5P,
	Polygon,
	FreePolygon,
	Text,
	Image,
	SelectOtherShape,
	ScissorsCurve,
	ScissorsLine,
	Measure,
	MeasureAngle,
	FormatPainter
}

public enum SelectionTransformMode
{
	Resize = 0,
	Rotate
}

public enum GuideOrientation
{
	Vertical,
	Horizontal
}

public enum GuideTransformMode
{
	Move,
	Rotate
}

public enum HitKind
{
	None = 0,
	ShapeBody,
	SelectionHandle,
	RotationHandle,
	RotationPivot,
	CurvePoint,
	CurveSegment,
	GuideBody,
	GuideCenterHandle,
	GuideRotateHandleCW,
	GuideRotateHandleCCW,
	ShapePropertyHandle,

	/// <summary>Подпись размера. Тянется отдельно от самой фигуры.</summary>
	MeasurementText,
	FillHandle,
	FillStartHandle,
	FillEndHandle,
	ShadowStartHandle,
	ShadowEndHandle,
	ShadowSoftnessHandle,
	TransparencyStartHandle,
	TransparencyEndHandle,
	TransformStartHandle,
	TransformEndHandle,
	TransformBiasHandle,
	ShapePoint,
	CurveNode,
	CurveControlPoint,
	TextAlongShapeHandle
}

public enum DragOperationKind
{
	None = 0,
	Pan,
	MarqueeSelect,
	MoveSelection,
	ResizeSelection,
	RotateSelection,
	MoveRotationCenter,
	CreateShape,
	MoveCurvePoint,
	MoveCurveControlPoint,
	InsertCurvePoint,
	MoveCurveSegment,
	MoveMeasurementText,
	CreateGuide,
	MoveGuide,
	RotateGuide,
	MoveGuideCenter,
	AdjustRoundedRectangleRadius,
	AdjustChamferedRectangleSize,
	MoveShapePoint,
	MoveFillHandle,
	MoveShadowHandle,
	AdjustShadowSoftness,
	MoveTransparencyHandle,
	MoveTransformHandle,
	AdjustTransformBias,
	AdjustTextAlongShapeAx,
	AdjustTextAlongShapeAy
}

public enum ShapePropertyHandleKind
{
	None,
	RoundedRectangleRadius,
	ChamferedRectangleSize
}

public enum SnapKind
{
	None = 0,
	PointToPoint = 1,
	PointToLine = 2,
	LineToPoint = 3,

	/// <summary>Привязка к узлу сетки страницы.</summary>
	Grid = 4
}

public enum FillHandleKind
{
	None = 0,
	Start,
	End
}

/// <summary>
/// Ручки инструмента тени. Помимо пары крайних точек есть промежуточный
/// ползунок мягкости, поэтому отдельным перечислением, а не FillHandleKind.
/// </summary>
public enum ShadowHandleKind
{
	None = 0,
	Start,
	End,
	Softness
}

/// <summary>
/// Ручки инструмента прозрачности. Промежуточного ползунка нет: переход
/// между P1 и P2 линейный и дополнительного параметра не имеет.
/// </summary>
public enum TransparencyHandleKind
{
	None = 0,
	Start,
	End
}

/// <summary>
/// Ручки инструмента преобразования: начало и конец цепочки шагов плюс
/// промежуточный ползунок распределения расстояний между ними.
/// </summary>
public enum TransformHandleKind
{
	None = 0,
	Start,
	End,
	Bias
}