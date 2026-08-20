using System.Windows;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IBrushOverlayFeatureHandler
{
    FrameworkElement? CreateOverlayVisual(BrushOverlayFeatureRequest request);
}

public sealed record BrushOverlayFeatureRequest(
    string? OverlayMethod,
    string OverlaySpriteName,
    double Width,
    double Height,
    FrameworkElement? MainVisual,
    Func<string, double, double, FrameworkElement?> CreateSpriteVisual,
    Func<string, ImageSource?> ResolveSpriteImage,
    Func<FrameworkElement, double, double, FrameworkElement> CreateVisualClone);
