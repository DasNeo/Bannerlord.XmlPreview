using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.DemoData;
using Bannerlord.XmlPreview.Interfaces;
using System.Globalization;
using System.Windows;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordBrushMetricsProvider : IBrushMetricsProvider
{
    public IBrushMetricsProvider ForAssets(IAssetCatalog assets) => new BannerlordBrushMetricsProvider(assets);
    private readonly IAssetCatalog _assetCatalog;

    public BannerlordBrushMetricsProvider(IAssetCatalog assetCatalog)
    {
        _assetCatalog = assetCatalog;
    }

    public double? ResolveMetric(string? brushName, string? brushLayerName, string valueType)
    {
        var brush = _assetCatalog.FindBrush(brushName);
        if (brush is null)
        {
            return TryResolveFallbackMetric(brushName, valueType, out var fallbackMetric)
                ? fallbackMetric
                : null;
        }

        var layer = brush.Layers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, brushLayerName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.SpriteName))
            ?? brush.Layers.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.SpriteName));

        var sprite = _assetCatalog.FindSprite(layer?.SpriteName);
        if (sprite is null)
        {
            return TryResolveFallbackMetric(brushName, valueType, out var fallbackMetric)
                ? fallbackMetric
                : null;
        }

        return valueType switch
        {
            "Width" => Math.Max(0, sprite.Width - (layer?.ExtendLeft ?? 0) - (layer?.ExtendRight ?? 0)),
            "Height" => Math.Max(0, sprite.Height - (layer?.ExtendTop ?? 0) - (layer?.ExtendBottom ?? 0)),
            _ => null
        };
    }

    public Thickness ResolveContentPadding(BrushPaddingRequest request)
    {
        var leftIntrusion = Math.Max(0, request.SpriteLeftWidth - request.ExtendLeft);
        var rightIntrusion = Math.Max(0, request.SpriteRightWidth - request.ExtendRight);
        var topIntrusion = Math.Max(0, request.SpriteTopHeight - request.ExtendTop);
        var bottomIntrusion = Math.Max(0, request.SpriteBottomHeight - request.ExtendBottom);

        var left = Math.Clamp(Math.Max(request.ExtendLeft * 0.33, leftIntrusion + 4), 8, 20);
        var top = Math.Clamp(Math.Max(request.ExtendTop * 0.1, topIntrusion + 1), 2, 6);
        var right = Math.Clamp(Math.Max(request.ExtendRight * 0.33, rightIntrusion + 4), 8, 20);
        var bottom = Math.Clamp(Math.Max(request.ExtendBottom * 0.1, bottomIntrusion + 1), 2, 6);
        return new Thickness(left, top, right, bottom);
    }

    private static bool TryResolveFallbackMetric(string? brushName, string valueType, out double metric)
    {
        if (!string.IsNullOrWhiteSpace(brushName) &&
            PreviewDemoCatalog.BrushMetricFallbacks.TryGetValue(brushName, out var brushFallbacks) &&
            brushFallbacks.TryGetValue(valueType, out var rawMetric) &&
            double.TryParse(rawMetric, NumberStyles.Float, CultureInfo.InvariantCulture, out metric))
        {
            return true;
        }

        metric = 0;
        return false;
    }
}
