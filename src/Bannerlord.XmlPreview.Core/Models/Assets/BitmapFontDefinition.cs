using System.Windows.Media.Imaging;
namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BitmapFontGlyph(int Id, int X, int Y, int Width, int Height, double Advance, double OffsetX, double OffsetY);
public sealed record BitmapFontDefinition(string Name, double Size, double LineHeight, double Smoothing,
    BitmapSource Atlas, IReadOnlyDictionary<int, BitmapFontGlyph> Glyphs, IReadOnlyDictionary<(int, int), double> Kerning);
