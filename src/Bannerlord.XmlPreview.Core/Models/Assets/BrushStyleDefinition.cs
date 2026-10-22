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
}
