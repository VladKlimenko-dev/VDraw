using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

public sealed class ShadowTool
{
	public bool Enabled { get; set; }
	public float AngleDeg { get; set; } = 45f;
	public float Distance { get; set; } = 5f;
	public SKColor Color { get; set; } = new SKColor(0, 0, 0, 128);
	public float Blur { get; set; } = 8f;
	public int GradientSteps { get; set; } = 8;
}

public sealed class TransparencyTool
{
	public bool Enabled { get; set; }
	public float Opacity { get; set; } = 1f;
}

public static class ShapeToolExtensions
{
	private static readonly Dictionary<Guid, ShadowTool> _shadow = new();
	private static readonly Dictionary<Guid, TransparencyTool> _transparency = new();

	public static ShadowTool GetShadowTool(this Shape shape)
	{
		if (!_shadow.TryGetValue(shape.Id, out var t))
		{
			t = new ShadowTool();
			_shadow[shape.Id] = t;
		}
		return t;
	}

	public static TransparencyTool GetTransparencyTool(this Shape shape)
	{
		if (!_transparency.TryGetValue(shape.Id, out var t))
		{
			t = new TransparencyTool();
			_transparency[shape.Id] = t;
		}
		return t;
	}
}