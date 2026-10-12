using SkiaSharp;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public sealed class SkMouseEvent
{
	public SKPoint Location { get; set; }
	public MouseButtons Button { get; set; }
	public int Delta { get; set; }
	public bool Ctrl { get; set; }
	public bool Shift { get; set; }
	public bool Alt { get; set; }
}

public sealed class SkKeyEvent
{
	public Keys KeyCode { get; set; }
	public bool Ctrl { get; set; }
	public bool Shift { get; set; }
	public bool Alt { get; set; }
}

public abstract class SkControl
{
	public string Name { get; set; } = "SkControl";
	public SKRect Bounds { get; set; }
	public bool Visible { get; set; } = true;
	public bool Enabled { get; set; } = true;
	public bool Hovered { get; set; }
	public bool Focused { get; set; }
	public SkControl? Parent { get; set; }
	public List<SkControl> Children { get; } = new();

	public bool ClipChildren { get; set; } = false;
	public float ClipRadius { get; set; } = 0f;
	public SKRect ClipPadding { get; set; } = SKRect.Empty;

	public float AbsoluteX => Parent == null ? Bounds.Left : Parent.AbsoluteX + Bounds.Left;
	public float AbsoluteY => Parent == null ? Bounds.Top : Parent.AbsoluteY + Bounds.Top;
	public SKRect AbsoluteBounds => new(AbsoluteX, AbsoluteY, AbsoluteX + Bounds.Width, AbsoluteY + Bounds.Height);

	public virtual void Add(SkControl child)
	{
		child.Parent = this;
		Children.Add(child);
	}

	public virtual void ClearChildren()
	{
		foreach (var c in Children)
			c.Parent = null;

		Children.Clear();
	}

	public virtual void Arrange()
	{
		foreach (var c in Children)
			c.Arrange();
	}

	protected virtual SKRect GetChildrenClipRect()
	{
		var ab = AbsoluteBounds;

		return new SKRect(
			ab.Left + ClipPadding.Left,
			ab.Top + ClipPadding.Top,
			ab.Right - ClipPadding.Right,
			ab.Bottom - ClipPadding.Bottom);
	}

	public virtual void Draw(SKCanvas canvas)
	{
		if (!Visible)
			return;

		DrawSelf(canvas);

		if (Children.Count == 0)
			return;

		if (ClipChildren)
		{
			SKRect clipRect = GetChildrenClipRect();

			if (clipRect.Width <= 0 || clipRect.Height <= 0)
				return;

			canvas.Save();

			if (ClipRadius > 0.01f)
			{
				var rr = new SKRoundRect(clipRect, ClipRadius, ClipRadius);
				canvas.ClipRoundRect(rr, SKClipOperation.Intersect, true);
			}
			else
			{
				canvas.ClipRect(clipRect, SKClipOperation.Intersect, true);
			}

			foreach (var c in Children)
				c.Draw(canvas);

			canvas.Restore();
		}
		else
		{
			foreach (var c in Children)
				c.Draw(canvas);
		}
	}

	protected virtual void DrawSelf(SKCanvas canvas)
	{
	}

	public virtual void DrawOverlay(SKCanvas canvas)
	{
		if (!Visible)
			return;

		foreach (var c in Children)
			c.DrawOverlay(canvas);
	}

	public virtual bool HitTest(SKPoint p) => Visible && Enabled && AbsoluteBounds.Contains(p);

	public virtual SkControl? FindTopMost(SKPoint p)
	{
		if (!Visible || !Enabled)
			return null;

		for (int i = Children.Count - 1; i >= 0; i--)
		{
			var found = Children[i].FindTopMost(p);
			if (found != null)
				return found;
		}

		return HitTest(p) ? this : null;
	}

	public virtual SkControl? FindTopMostOverlay(SKPoint p)
	{
		if (!Visible || !Enabled)
			return null;

		for (int i = Children.Count - 1; i >= 0; i--)
		{
			var found = Children[i].FindTopMostOverlay(p);
			if (found != null)
				return found;
		}

		if (this is SkDropDownBox dd && dd.HitTestOverlay(p))
			return this;

		return null;
	}

	public virtual bool CloseDropDownsOutside(SKPoint p)
	{
		if (!Visible || !Enabled)
			return false;

		bool changed = false;

		for (int i = Children.Count - 1; i >= 0; i--)
		{
			if (Children[i].CloseDropDownsOutside(p))
				changed = true;
		}

		if (this is SkDropDownBox dd && dd.CloseDropDownsOutside(p))
			changed = true;

		return changed;
	}

	public virtual bool OnMouseDown(SkMouseEvent e) => false;
	public virtual bool OnMouseMove(SkMouseEvent e) => false;
	public virtual bool OnMouseUp(SkMouseEvent e) => false;
	public virtual bool OnMouseWheel(SkMouseEvent e) => false;
	public virtual bool OnMouseDoubleClick(SkMouseEvent e) => false;
	public virtual bool OnKeyChar(char c) => false;
	public virtual bool OnKeyDown(SkKeyEvent e) => false;

	protected virtual void Dispose(bool disposing)
	{
	}
}