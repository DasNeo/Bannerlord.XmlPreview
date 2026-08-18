namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record ResolvedBrushDebugInfo(
    string RequestedName,
    BannerlordBrushDefinition? BrushDefinition,
    bool IsPreviewFallback,
    IReadOnlyList<BannerlordBrushLayerDefinition> VisibleLayers);
