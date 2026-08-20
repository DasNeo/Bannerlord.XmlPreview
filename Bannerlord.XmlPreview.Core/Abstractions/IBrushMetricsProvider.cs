using System.Windows;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IBrushMetricsProvider
{
    double? ResolveMetric(string? brushName, string? brushLayerName, string valueType);

    Thickness ResolveContentPadding(BrushPaddingRequest request);
}

public sealed record BrushPaddingRequest(
    double ExtendLeft,
    double ExtendTop,
    double ExtendRight,
    double ExtendBottom,
    double SpriteLeftWidth,
    double SpriteTopHeight,
    double SpriteRightWidth,
    double SpriteBottomHeight,
    Thickness Fallback);
