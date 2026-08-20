namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BrushDefinition(
    string Name,
    string SourcePath,
    IReadOnlyList<BrushLayerDefinition> Layers,
    IReadOnlyDictionary<string, BrushStyleDefinition> Styles,
    string? BaseBrushName,
    string? FontName,
    string? TextHorizontalAlignment,
    string? TextVerticalAlignment);
