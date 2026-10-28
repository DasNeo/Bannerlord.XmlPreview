namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BrushStyleDefinition(
    string Name,
    string? FontColor,
    double? FontSize,
    double? TextAlphaFactor,
    IReadOnlyDictionary<string, BrushLayerDefinition> LayerOverrides)
{
    public double? TextOutlineAmount { get; init; }
    public string? TextOutlineColor { get; init; }

    public BrushStyleDefinition Merge(BrushStyleDefinition overrides)
    {
        var layers = LayerOverrides.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides.LayerOverrides)
            layers[pair.Key] = layers.TryGetValue(pair.Key, out var inherited) ? inherited.Merge(pair.Value) : pair.Value;
        return overrides with
        {
            FontColor = overrides.FontColor ?? FontColor,
            FontSize = overrides.FontSize ?? FontSize,
            TextAlphaFactor = overrides.TextAlphaFactor ?? TextAlphaFactor,
            TextOutlineAmount = overrides.TextOutlineAmount ?? TextOutlineAmount,
            TextOutlineColor = overrides.TextOutlineColor ?? TextOutlineColor,
            LayerOverrides = layers
        };
    }
}
