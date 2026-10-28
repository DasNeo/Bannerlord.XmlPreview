namespace Bannerlord.XmlPreview.Models;

public sealed record DeferredHiddenSubtree(string WidgetId, string ExpandedPath, string InstanceKey,
    int LogicalWidgetCount, bool IsLocallyVisible);
