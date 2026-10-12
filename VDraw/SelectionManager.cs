using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

public sealed class SelectionManager
{
	private readonly List<Shape> _selected = new();

	public IReadOnlyList<Shape> Selected => _selected;
	public Shape? Primary => _selected.Count > 0 ? _selected[0] : null;
	public bool HasSelection => _selected.Count > 0;

	public event Action? SelectionChanged;
	public event Action? SelectionGeometryChanged;
	public void NotifyGeometryChanged() => SelectionGeometryChanged?.Invoke();

	public void Clear()
	{
		foreach (var s in _selected)
			s.IsSelected = false;

		_selected.Clear();
		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Заменяет выделение целиком.
	///
	/// Отдельно от SetSingle потому, что панель свойств смотрит на этот
	/// список: пока в него попадала только одиночная фигура, при выделении
	/// нескольких объектов панель считала, что не выделено ничего.
	/// </summary>
	public void Set(IEnumerable<Shape> shapes)
	{
		foreach (var s in _selected)
			s.IsSelected = false;

		_selected.Clear();

		foreach (var shape in shapes)
		{
			if (shape == null || _selected.Contains(shape))
				continue;

			shape.IsSelected = true;
			_selected.Add(shape);
		}

		SelectionChanged?.Invoke();
	}

	public void SetSingle(Shape? shape)
	{
		foreach (var s in _selected)
			s.IsSelected = false;

		_selected.Clear();

		if (shape != null)
		{
			shape.IsSelected = true;
			_selected.Add(shape);
		}

		SelectionChanged?.Invoke();
	}

	public void Toggle(Shape shape)
	{
		if (_selected.Contains(shape))
		{
			shape.IsSelected = false;
			_selected.Remove(shape);
		}
		else
		{
			shape.IsSelected = true;
			_selected.Add(shape);
		}

		SelectionChanged?.Invoke();
	}

	public SKRect GetSelectionBounds()
	{
		if (_selected.Count == 0)
			return SKRect.Empty;

		SKRect r = _selected[0].GetBounds();
		for (int i = 1; i < _selected.Count; i++)
			r = Geometry.Union(r, _selected[i].GetBounds());

		return r;
	}

	public void DeleteFrom(Page page)
	{
		foreach (var s in _selected.ToList())
			page.RemoveShape(s);

		_selected.Clear();
		SelectionChanged?.Invoke();
	}
}

public sealed class HandleInfo
{
	public HandleKind Kind { get; set; }
	public SKRect ScreenRect { get; set; }
	public SKPoint PagePoint { get; set; }
}

public static class HandleFactory
{
	public static List<HandleInfo> CreateResizeHandles(SKRect pageBounds, Func<SKPoint, SKPoint> pageToScreen)
	{
		var points = new Dictionary<HandleKind, SKPoint>
		{
			[HandleKind.TopLeft] = new(pageBounds.Left, pageBounds.Top),
			[HandleKind.Top] = new(pageBounds.MidX, pageBounds.Top),
			[HandleKind.TopRight] = new(pageBounds.Right, pageBounds.Top),
			[HandleKind.Right] = new(pageBounds.Right, pageBounds.MidY),
			[HandleKind.BottomRight] = new(pageBounds.Right, pageBounds.Bottom),
			[HandleKind.Bottom] = new(pageBounds.MidX, pageBounds.Bottom),
			[HandleKind.BottomLeft] = new(pageBounds.Left, pageBounds.Bottom),
			[HandleKind.Left] = new(pageBounds.Left, pageBounds.MidY)
		};

		return points.Select(p =>
		{
			var s = pageToScreen(p.Value);
			return new HandleInfo
			{
				Kind = p.Key,
				PagePoint = p.Value,
				ScreenRect = SKRect.Create(s.X - 10, s.Y - 10, 20, 20)
			};
		}).ToList();
	}

	public static List<HandleInfo> CreateRotateHandles(SKRect pageBounds, SKPoint rotationCenter, Func<SKPoint, SKPoint> pageToScreen)
	{
		var points = new Dictionary<HandleKind, SKPoint>
		{
			[HandleKind.RotateTopLeft] = new(pageBounds.Left, pageBounds.Top),
			[HandleKind.RotateTopRight] = new(pageBounds.Right, pageBounds.Top),
			[HandleKind.RotateBottomRight] = new(pageBounds.Right, pageBounds.Bottom),
			[HandleKind.RotateBottomLeft] = new(pageBounds.Left, pageBounds.Bottom),
			[HandleKind.RotationCenter] = rotationCenter
		};

		return points.Select(p =>
		{
			var s = pageToScreen(p.Value);
			float size = p.Key == HandleKind.RotationCenter ? 25f : 20f;
			return new HandleInfo
			{
				Kind = p.Key,
				PagePoint = p.Value,
				ScreenRect = SKRect.Create(s.X - size / 2f, s.Y - size / 2f, size, size)
			};
		}).ToList();
	}
}