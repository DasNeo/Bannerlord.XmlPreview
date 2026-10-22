namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BrushLayerDefinition(
    string Name,
    string? SpriteName,
    string? OverlaySpriteName,
    string? OverlayMethod,
    string? Color,
    double? ColorFactor,
    double? AlphaFactor,
    double? SaturationFactor,
    double? ValueFactor,
    double? HueFactor,
    bool? IsHidden,
    double? ExtendLeft,
    double? ExtendRight,
    double? ExtendTop,
    double? ExtendBottom,
    bool? HorizontalFlip,
    bool? VerticalFlip)
{
    public BrushLayerDefinition Merge(BrushLayerDefinition? overrideLayer)
    {
        if (overrideLayer is null)
        {
            return this;
        }

        return new BrushLayerDefinition(
            overrideLayer.Name,
            overrideLayer.SpriteName ?? SpriteName,
            overrideLayer.OverlaySpriteName ?? OverlaySpriteName,
            overrideLayer.OverlayMethod ?? OverlayMethod,
            overrideLayer.Color ?? Color,
            overrideLayer.ColorFactor ?? ColorFactor,
            overrideLayer.AlphaFactor ?? AlphaFactor,
            overrideLayer.SaturationFactor ?? SaturationFactor,
            overrideLayer.ValueFactor ?? ValueFactor,
            overrideLayer.HueFactor ?? HueFactor,
            overrideLayer.IsHidden ?? IsHidden,
            overrideLayer.ExtendLeft ?? ExtendLeft,
            overrideLayer.ExtendRight ?? ExtendRight,
            overrideLayer.ExtendTop ?? ExtendTop,
            overrideLayer.ExtendBottom ?? ExtendBottom,
            overrideLayer.HorizontalFlip ?? HorizontalFlip,
            overrideLayer.VerticalFlip ?? VerticalFlip);
    }

    public double MaxExtend =>
        new[]
        {
            ExtendLeft ?? 0,
            ExtendRight ?? 0,
            ExtendTop ?? 0,
            ExtendBottom ?? 0
        }.Max();
}
