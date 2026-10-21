namespace Bannerlord.XmlPreview.Models;

public sealed record ResolvedWidgetReference(
    string WidgetPath,
    string AttributeName,
    string RawValue,
    string? TargetWidgetPath,
    bool IsResolved,
    string? FailureReason);
