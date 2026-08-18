namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BannerlordBrushStyleDefinition(
    string Name,
    string? FontColor,
    double? FontSize,
    double? TextAlphaFactor,
    IReadOnlyDictionary<string, BannerlordBrushLayerDefinition> LayerOverrides);
