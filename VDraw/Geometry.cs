using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public static class Geometry
{
	public static float DegToRad(float deg) => deg * MathF.PI / 180f;
	public static float RadToDeg(float rad) => rad * 180f / MathF.PI;

	public static float Distance(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	public static SKPoint Mid(SKPoint a, SKPoint b)
		=> new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

	public static SKRect RectFromTwoPoints(SKPoint a, SKPoint b)
	{
		float left = MathF.Min(a.X, b.X);
		float top = MathF.Min(a.Y, b.Y);
		float right = MathF.Max(a.X, b.X);
		float bottom = MathF.Max(a.Y, b.Y);
		return new SKRect(left, top, right, bottom);
	}

	public static SKPoint RotatePoint(SKPoint p, SKPoint center, float angleDeg)
	{
		float rad = DegToRad(angleDeg);
		float cos = MathF.Cos(rad);
		float sin = MathF.Sin(rad);
		float dx = p.X - center.X;
		float dy = p.Y - center.Y;
		return new SKPoint(
			center.X + dx * cos - dy * sin,
			center.Y + dx * sin + dy * cos);
	}

	public static SKRect Inflate(SKRect r, float dx, float dy)
		=> new(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

	public static SKRect NormalizeRect(SKPoint a, SKPoint b)
	{
		return new SKRect(
			MathF.Min(a.X, b.X),
			MathF.Min(a.Y, b.Y),
			MathF.Max(a.X, b.X),
			MathF.Max(a.Y, b.Y));
	}

	public static bool TryCircleFrom3Points(SKPoint a, SKPoint b, SKPoint c, out SKPoint center, out float radius)
	{
		float d = 2f * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
		if (MathF.Abs(d) < 0.0001f)
		{
			center = SKPoint.Empty;
			radius = 0f;
			return false;
		}

		float a2 = a.X * a.X + a.Y * a.Y;
		float b2 = b.X * b.X + b.Y * b.Y;
		float c2 = c.X * c.X + c.Y * c.Y;

		float ux = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
		float uy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;

		center = new SKPoint(ux, uy);
		radius = Distance(center, a);
		return true;
	}

	public static SKRect TransformRect(SKRect rect, SKPoint center, float angleDeg)
	{
		var p1 = RotatePoint(new SKPoint(rect.Left, rect.Top), center, angleDeg);
		var p2 = RotatePoint(new SKPoint(rect.Right, rect.Top), center, angleDeg);
		var p3 = RotatePoint(new SKPoint(rect.Right, rect.Bottom), center, angleDeg);
		var p4 = RotatePoint(new SKPoint(rect.Left, rect.Bottom), center, angleDeg);

		float left = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
		float top = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
		float right = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
		float bottom = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));

		return new SKRect(left, top, right, bottom);
	}

	public static SKRect Union(SKRect a, SKRect b)
	{
		if (a.IsEmpty) return b;
		if (b.IsEmpty) return a;

		return new SKRect(
			MathF.Min(a.Left, b.Left),
			MathF.Min(a.Top, b.Top),
			MathF.Max(a.Right, b.Right),
			MathF.Max(a.Bottom, b.Bottom));
	}

	public static SKRect MoveRectTo(SKRect r, float left, float top)
	{
		float w = r.Width;
		float h = r.Height;
		return new SKRect(left, top, left + w, top + h);
	}

	public static SKRect MoveRectBy(SKRect r, float dx, float dy)
	{
		return new SKRect(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
	}

	public static SKMatrix Multiply(SKMatrix first, SKMatrix second)
	{
		SKMatrix result = SKMatrix.CreateIdentity();
		SKMatrix.Concat(ref result, first, second);
		return result;
	}

	public static void DecomposeMatrix(SKMatrix m, out float scaleX, out float scaleY, out float rotation)
	{
		float a = m.ScaleX;
		float b = m.SkewY;
		float c = m.SkewX;
		float d = m.ScaleY;

		scaleX = MathF.Sqrt(a * a + b * b);
		scaleY = MathF.Sqrt(c * c + d * d);

		if (scaleX < 0.0001f)
			scaleX = 1f;

		if (scaleY < 0.0001f)
			scaleY = 1f;

		rotation = MathF.Atan2(b, a);
	}

	public static SKColor LerpColor(SKColor a, SKColor b, float t)
	{
		t = Math.Clamp(t, 0f, 1f);

		byte r = (byte)(a.Red + (b.Red - a.Red) * t);
		byte g = (byte)(a.Green + (b.Green - a.Green) * t);
		byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
		byte al = (byte)(a.Alpha + (b.Alpha - a.Alpha) * t);

		return new SKColor(r, g, bl, al);
	}

	public static string ColorToHex(SKColor c)
		=> $"{c.Alpha:X2}{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

	public static bool TryParseHexColor(string s, out SKColor color)
	{
		color = SKColors.Transparent;
		s = (s ?? string.Empty).Trim();

		if (s.StartsWith("#"))
			s = s[1..];

		if (s.Length != 8)
			return false;

		if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
			return false;

		byte a = (byte)((argb >> 24) & 0xFF);
		byte r = (byte)((argb >> 16) & 0xFF);
		byte g = (byte)((argb >> 8) & 0xFF);
		byte b = (byte)(argb & 0xFF);

		color = new SKColor(r, g, b, a);
		return true;
	}
}