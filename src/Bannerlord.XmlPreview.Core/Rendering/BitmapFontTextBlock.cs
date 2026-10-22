using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Rendering;

// Uses the game's distance-field atlas and advances; WPF font substitution loses both.
public sealed class BitmapFontTextBlock : FrameworkElement
{
    public TextBlock StyleSource { get; init; } = null!;
    public string Text => StyleSource.Text;
    private double FontSize => StyleSource.FontSize;
    private TextWrapping TextWrapping => StyleSource.TextWrapping;
    private Brush Foreground => StyleSource.Foreground;
    public BitmapFontDefinition? BitmapFont { get; set; }
    public Color OutlineColor { get; set; } = Colors.Black;
    public double OutlineAmount { get; set; }
    private readonly Dictionary<int, BitmapSource> glyphImages = new();
    private readonly List<(BitmapFontGlyph Glyph, double X, double Y)> layout = [];
    protected override Size MeasureOverride(Size constraint)
    {
        if (BitmapFont is not { } font) return new Size();
        layout.Clear();
        double x = 0, y = 0, max = 0;
        var scale = FontSize / font.Size;
        var previous = -1;
        foreach (var rune in Text.EnumerateRunes())
        {
            if (rune.Value == '\r') continue;
            if (rune.Value == '\n') { max = Math.Max(max, x); x = 0; y += font.LineHeight * scale; previous = -1; continue; }
            if (!font.Glyphs.TryGetValue(rune.Value, out var glyph) && !font.Glyphs.TryGetValue(63, out glyph)) continue;
            var kern = font.Kerning.GetValueOrDefault((previous, glyph.Id)) * scale;
            if (TextWrapping != TextWrapping.NoWrap && x > 0 && x + kern + glyph.Advance * scale > constraint.Width)
            { max = Math.Max(max, x); x = 0; y += font.LineHeight * scale; kern = 0; }
            x += kern;
            layout.Add((glyph, x, y)); x += glyph.Advance * scale; previous = glyph.Id;
        }
        var lineWidth = Math.Max(max, x);
        return new(Math.Min(constraint.Width, lineWidth), y + font.LineHeight * scale);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        if (BitmapFont is not { } font) return;
        var scale = FontSize / font.Size;
        foreach (var (glyph, x, y) in layout)
        {
            if (glyph.Width == 0 || glyph.Height == 0) continue;
            if (!glyphImages.TryGetValue(glyph.Id, out var image))
            {
                var crop = new CroppedBitmap(font.Atlas, new Int32Rect(glyph.X, glyph.Y, glyph.Width, glyph.Height));
                var source = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[glyph.Width * glyph.Height * 4];
                source.CopyPixels(pixels, glyph.Width * 4, 0);
                var color = (Foreground as SolidColorBrush)?.Color ?? Colors.White;
                var smoothing = Math.Min(0.25, 0.08 / scale);
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var distance = pixels[i + 2] / 255d;
                    var fill = Math.Clamp((distance - font.Smoothing) / smoothing + 0.5, 0, 1);
                    var outline = Math.Clamp((distance - font.Smoothing + OutlineAmount * 0.05) / smoothing + 0.5, 0, 1);
                    var a = fill * color.A / 255d;
                    var b = Math.Max(0, outline - fill) * OutlineColor.A / 255d;
                    pixels[i] = (byte)(color.B * a + OutlineColor.B * b);
                    pixels[i + 1] = (byte)(color.G * a + OutlineColor.G * b);
                    pixels[i + 2] = (byte)(color.R * a + OutlineColor.R * b);
                    pixels[i + 3] = (byte)(255 * Math.Min(1, a + b));
                }
                image = BitmapSource.Create(glyph.Width, glyph.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, glyph.Width * 4);
                image.Freeze(); glyphImages[glyph.Id] = image;
            }
            drawing.DrawImage(image, new Rect((x + glyph.OffsetX * scale), (y + glyph.OffsetY * scale), glyph.Width * scale, glyph.Height * scale));
        }
    }
}
