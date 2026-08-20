namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BrushStyleDefinition(
    string Name,
    string? FontColor,
    double? FontSize,
    double? TextAlphaFactor,
    IReadOnlyDictionary<string, BrushLayerDefinition> LayerOverrides);
