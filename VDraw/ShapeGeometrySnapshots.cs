using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

/// <summary>
/// Снимок собственной геометрии фигуры для истории отмены.
///
/// Зачем отдельно от матрицы. TransformShapesAction хранил только
/// LocalTransform, и этого хватало, пока перетаскивание меняло лишь её.
/// Но ручки точек фигуры и ручки свойств правят собственные поля:
/// радиус окружности, полуоси эллипса, вершины многоугольника, скругление
/// и фаску прямоугольника, измеряемые точки размера. Для них матрица
/// не менялась вовсе — и шаг истории либо не записывался (HasChanges
/// возвращал false), либо записывался и при откате возвращал матрицу,
/// оставляя поля новыми: фигура оказывалась в состоянии, которого никогда
/// не было.
///
/// SameAs, а не Equals: сравнение нужно только истории, и держать его
/// в самом типе понятнее, чем разбираться, у какого снимка перекрыт Equals,
/// а у какого сработало сравнение по ссылке.
/// </summary>
public interface IGeometrySnapshot
{
	bool SameAs(IGeometrySnapshot? other);
}

/// <summary>Прямоугольник: только габариты.</summary>
public sealed class RectGeometrySnapshot : IGeometrySnapshot
{
	public SKRect Rect { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is RectGeometrySnapshot o && Rect == o.Rect;
}

/// <summary>Скруглённый прямоугольник: габариты и радиусы углов.</summary>
public sealed class RoundedRectGeometrySnapshot : IGeometrySnapshot
{
	public SKRect Rect { get; init; }
	public float RadiusX { get; init; }
	public float RadiusY { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is RoundedRectGeometrySnapshot o
		&& Rect == o.Rect
		&& RadiusX == o.RadiusX
		&& RadiusY == o.RadiusY;
}

/// <summary>Срезанный прямоугольник: габариты и размеры фаски.</summary>
public sealed class ChamferedRectGeometrySnapshot : IGeometrySnapshot
{
	public SKRect Rect { get; init; }
	public float ChamferX { get; init; }
	public float ChamferY { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is ChamferedRectGeometrySnapshot o
		&& Rect == o.Rect
		&& ChamferX == o.ChamferX
		&& ChamferY == o.ChamferY;
}

/// <summary>Окружность по двум точкам: центр, радиус и обе опорные точки.</summary>
public sealed class Circle2PGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint LocalCenter { get; init; }
	public float Radius { get; init; }
	public SKPoint CenterPoint { get; init; }
	public SKPoint RadiusPoint { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is Circle2PGeometrySnapshot o
		&& LocalCenter == o.LocalCenter
		&& Radius == o.Radius
		&& CenterPoint == o.CenterPoint
		&& RadiusPoint == o.RadiusPoint;
}

/// <summary>Окружность по трём точкам.</summary>
public sealed class Circle3PGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint P1 { get; init; }
	public SKPoint P2 { get; init; }
	public SKPoint P3 { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is Circle3PGeometrySnapshot o
		&& P1 == o.P1
		&& P2 == o.P2
		&& P3 == o.P3;
}

/// <summary>Эллипс по трём точкам: полуоси и опорные точки.</summary>
public sealed class Ellipse3PGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint LocalCenter { get; init; }
	public float RadiusX { get; init; }
	public float RadiusY { get; init; }
	public SKPoint CenterPoint { get; init; }
	public SKPoint RadiusPoint1 { get; init; }
	public SKPoint RadiusPoint2 { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is Ellipse3PGeometrySnapshot o
		&& LocalCenter == o.LocalCenter
		&& RadiusX == o.RadiusX
		&& RadiusY == o.RadiusY
		&& CenterPoint == o.CenterPoint
		&& RadiusPoint1 == o.RadiusPoint1
		&& RadiusPoint2 == o.RadiusPoint2;
}

/// <summary>Эллипс по пяти точкам: полуоси и сами пять точек.</summary>
public sealed class Ellipse5PGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint LocalCenter { get; init; }
	public float RadiusX { get; init; }
	public float RadiusY { get; init; }
	public SKPoint[] Points { get; init; } = Array.Empty<SKPoint>();

	public bool SameAs(IGeometrySnapshot? other)
		=> other is Ellipse5PGeometrySnapshot o
		&& LocalCenter == o.LocalCenter
		&& RadiusX == o.RadiusX
		&& RadiusY == o.RadiusY
		&& GeometrySnapshotHelper.SamePoints(Points, o.Points);
}

/// <summary>Многоугольник: параметры построения и сами вершины.</summary>
public sealed class PolygonGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint LocalCenter { get; init; }
	public float RadiusX { get; init; }
	public float RadiusY { get; init; }
	public float RotationRad { get; init; }
	public int Sides { get; init; }
	public bool UseExplicitPoints { get; init; }
	public SKPoint[] Points { get; init; } = Array.Empty<SKPoint>();

	public bool SameAs(IGeometrySnapshot? other)
		=> other is PolygonGeometrySnapshot o
		&& LocalCenter == o.LocalCenter
		&& RadiusX == o.RadiusX
		&& RadiusY == o.RadiusY
		&& RotationRad == o.RotationRad
		&& Sides == o.Sides
		&& UseExplicitPoints == o.UseExplicitPoints
		&& GeometrySnapshotHelper.SamePoints(Points, o.Points);
}

/// <summary>Размер: измеряемые точки и вынос размерной линии.</summary>
public sealed class MeasurementGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint P1 { get; init; }
	public SKPoint P2 { get; init; }
	public float H { get; init; }

	/// <summary>
	/// Положение подписи вдоль линии. Часть геометрии, а не оформления:
	/// без него отмена перетаскивания подписи ничего бы не вернула.
	/// </summary>
	public float TextOffset { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is MeasurementGeometrySnapshot o
		&& P1 == o.P1
		&& P2 == o.P2
		&& H == o.H
		&& TextOffset == o.TextOffset;
}

/// <summary>
/// Снимок геометрии угла.
///
/// Радиус и угловой сдвиг подписи входят в снимок наравне с точками:
/// без них отмена перетаскивания подписи ничего бы не вернула.
/// </summary>
public sealed class AngleMeasurementGeometrySnapshot : IGeometrySnapshot
{
	public SKPoint S1P1 { get; init; }
	public SKPoint S1P2 { get; init; }
	public SKPoint S2P1 { get; init; }
	public SKPoint S2P2 { get; init; }

	public float Radius { get; init; }
	public float TextAngleOffset { get; init; }

	public bool SameAs(IGeometrySnapshot? other)
		=> other is AngleMeasurementGeometrySnapshot o
		&& S1P1 == o.S1P1
		&& S1P2 == o.S1P2
		&& S2P1 == o.S2P1
		&& S2P2 == o.S2P2
		&& Radius == o.Radius
		&& TextAngleOffset == o.TextAngleOffset;
}

internal static class GeometrySnapshotHelper
{
	public static bool SamePoints(SKPoint[] a, SKPoint[] b)
	{
		if (ReferenceEquals(a, b))
			return true;

		if (a.Length != b.Length)
			return false;

		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
				return false;
		}

		return true;
	}
}