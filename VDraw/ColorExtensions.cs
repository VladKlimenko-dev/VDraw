using SkiaSharp;

namespace VDraw.Ui;

/// <summary>
/// Перевод цвета из вещественного представления Skia в байтовое.
///
/// Жил в SkPropertyPanelControl.cs и переехал сюда вместе с заменой панели
/// свойств на Avalonia: к панели он отношения не имеет, им пользуется
/// отрисовка узлов кривых (Curves.cs) и диалог выбора цвета.
/// </summary>
public static class ColorExtensions
{
	public static SKColor ToSKColor(this SKColorF c)
	{
		byte r = (byte)Math.Clamp((int)MathF.Round(c.Red * 255f), 0, 255);
		byte g = (byte)Math.Clamp((int)MathF.Round(c.Green * 255f), 0, 255);
		byte b = (byte)Math.Clamp((int)MathF.Round(c.Blue * 255f), 0, 255);
		byte a = (byte)Math.Clamp((int)MathF.Round(c.Alpha * 255f), 0, 255);

		return new SKColor(r, g, b, a);
	}
}
