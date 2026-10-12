using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;

namespace VDraw.Ui;

public enum SkThemeKind
{
	Dark = 0,
	Light = 1
}

public sealed class SkUiPalette
{
	public SKColor PanelBack { get; init; }
	public SKColor PanelBack2 { get; init; }
	public SKColor Surface { get; init; }
	public SKColor Surface2 { get; init; }
	public SKColor Border { get; init; }
	public SKColor Text { get; init; }
	public SKColor TextMuted { get; init; }
	public SKColor Accent { get; init; }
	public SKColor Hover { get; init; }
	public SKColor Selected { get; init; }
	public SKColor Shadow { get; init; }
	public bool IsDark { get; init; }
	public SKColor ButtonPressed { get; init; }
}

public static class SkUiTheme
{
	public const float Radius = 6f;
	public const float RadiusSmall = 4f;
	public const float HeaderHeight = 34f;
	public const float RowHeight = 30f;
	public const float InputHeight = 30f;
	public const float Gap = 8f;
	public const float Padding = 10f;

	public static readonly SkUiPalette Dark = new()
	{
		PanelBack = new SKColor(32, 34, 39),
		PanelBack2 = new SKColor(38, 40, 46),
		Surface = new SKColor(48, 51, 58),
		Surface2 = new SKColor(58, 62, 70),
		Border = new SKColor(77, 82, 92),
		Text = new SKColor(236, 239, 244),
		TextMuted = new SKColor(160, 168, 180),
		Accent = new SKColor(88, 166, 255),
		Hover = new SKColor(255, 255, 255, 24),
		Selected = new SKColor(70, 86, 114),
		Shadow = new SKColor(0, 0, 0, 120),
		IsDark = true,
		ButtonPressed = new SKColor(50, 60, 30)
	};

	public static readonly SkUiPalette Light = new()
	{
		PanelBack = new SKColor(243, 244, 247),
		PanelBack2 = new SKColor(250, 250, 252),
		Surface = new SKColor(255, 255, 255),
		Surface2 = new SKColor(244, 247, 251),
		Border = new SKColor(198, 204, 214),
		Text = new SKColor(28, 32, 40),
		TextMuted = new SKColor(102, 112, 128),
		Accent = new SKColor(34, 102, 227),
		Hover = new SKColor(30, 41, 59, 18),
		Selected = new SKColor(210, 225, 248),
		Shadow = new SKColor(0, 0, 0, 45),
		IsDark = false,
		ButtonPressed = new SKColor(220, 235, 250)
	};

	public static SkThemeKind CurrentKind { get; private set; } = SkThemeKind.Dark;
	public static SkUiPalette Current { get; private set; } = Dark;

	public static event Action? ThemeChanged;

	public static void SetTheme(SkThemeKind kind)
	{
		CurrentKind = kind;
		Current = kind == SkThemeKind.Light ? Light : Dark;
		ThemeChanged?.Invoke();
	}

	public static void ToggleTheme()
	{
		SetTheme(CurrentKind == SkThemeKind.Dark ? SkThemeKind.Light : SkThemeKind.Dark);
	}

	public static SKPaint Fill(SKColor c) => new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Fill,
		Color = c
	};

	public static SKPaint Stroke(SKColor c, float w = 1f) => new()
	{
		IsAntialias = true,
		Style = SKPaintStyle.Stroke,
		Color = c,
		StrokeWidth = w
	};
}