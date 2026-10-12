using SkiaSharp;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

public sealed class TextureInfo
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public SKBitmap? Bitmap { get; set; }

	public TextureInfo()
	{
	}

	public TextureInfo(string id, string displayName, string fileName, SKBitmap? bitmap)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		FileName = fileName ?? string.Empty;
		Bitmap = bitmap;
	}
}