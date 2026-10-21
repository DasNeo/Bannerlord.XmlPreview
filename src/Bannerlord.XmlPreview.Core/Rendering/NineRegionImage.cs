using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Rendering;

// An overlay has no intrinsic layout size: its widget owns sizing. Drawing at the
// arranged size preserves borders during resize and scales them down for tiny widgets.
internal sealed class NineRegionImage : FrameworkElement
{
    private readonly BitmapSource?[,] _regions = new BitmapSource?[3, 3];
    private readonly int _left, _right, _top, _bottom;

    public NineRegionImage(BitmapSource source, SpriteDefinition sprite)
    {
        IsHitTestVisible = false;
        _left = Math.Clamp(sprite.LeftWidth ?? 0, 0, source.PixelWidth);
        _right = Math.Clamp(sprite.RightWidth ?? 0, 0, source.PixelWidth - _left);
        _top = Math.Clamp(sprite.TopHeight ?? 0, 0, source.PixelHeight);
        _bottom = Math.Clamp(sprite.BottomHeight ?? 0, 0, source.PixelHeight - _top);
        var xs = new[] { 0, _left, source.PixelWidth - _right, source.PixelWidth };
        var ys = new[] { 0, _top, source.PixelHeight - _bottom, source.PixelHeight };
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                var width = xs[column + 1] - xs[column];
                var height = ys[row + 1] - ys[row];
                if (width <= 0 || height <= 0) continue;
                var region = new CroppedBitmap(source, new Int32Rect(xs[column], ys[row], width, height));
                region.Freeze();
                _regions[row, column] = region;
            }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var horizontalScale = _left + _right == 0 ? 1 : Math.Min(1, RenderSize.Width / (_left + _right));
        var verticalScale = _top + _bottom == 0 ? 1 : Math.Min(1, RenderSize.Height / (_top + _bottom));
        var xs = new[] { 0, _left * horizontalScale, RenderSize.Width - _right * horizontalScale, RenderSize.Width };
        var ys = new[] { 0, _top * verticalScale, RenderSize.Height - _bottom * verticalScale, RenderSize.Height };
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                var width = xs[column + 1] - xs[column];
                var height = ys[row + 1] - ys[row];
                if (width <= 0 || height <= 0 || _regions[row, column] is not { } image) continue;
                drawingContext.DrawImage(image, new Rect(xs[column], ys[row], width, height));
            }
    }
}
