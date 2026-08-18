namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BannerlordBrushDefinition(
    string Name,
    string SourcePath,
    IReadOnlyList<BannerlordBrushLayerDefinition> Layers,
    IReadOnlyDictionary<string, BannerlordBrushStyleDefinition> Styles,
    string? BaseBrushName,
    string? FontName,
    string? TextHorizontalAlignment,
    string? TextVerticalAlignment);
