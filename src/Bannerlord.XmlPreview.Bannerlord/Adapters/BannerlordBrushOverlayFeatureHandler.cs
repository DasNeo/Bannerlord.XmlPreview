using Bannerlord.XmlPreview.Core.Abstractions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordBrushOverlayFeatureHandler : IBrushOverlayFeatureHandler
{
    public FrameworkElement? CreateOverlayVisual(BrushOverlayFeatureRequest request)
    {
        if (!string.Equals(request.OverlayMethod, "CoverWithTexture", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var overlaySource = request.ResolveSpriteImage(request.OverlaySpriteName);
        if (overlaySource is null)
        {
            return null;
        }

        var textureFill = new ImageBrush(overlaySource)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(
                0,
                0,
                Math.Max(1, overlaySource.Width),
                Math.Max(1, overlaySource.Height))
        };

        var overlayRect = new Rectangle
        {
            Width = request.Width,
            Height = request.Height,
            Fill = textureFill,
            IsHitTestVisible = false
        };

        if (request.MainVisual is not null)
        {
            overlayRect.OpacityMask = new VisualBrush(request.CreateVisualClone(request.MainVisual, request.Width, request.Height))
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }

        return overlayRect;
    }
}
