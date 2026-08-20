namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BrushDebugInfo(
    string RequestedName,
    BrushDefinition? BrushDefinition,
    bool IsPreviewFallback,
    IReadOnlyList<BrushLayerDefinition> VisibleLayers);
