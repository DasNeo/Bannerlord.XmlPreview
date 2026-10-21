namespace Bannerlord.XmlPreview.Models;

public sealed class PrefabWidgetReferenceMap
{
    public static PrefabWidgetReferenceMap Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, ResolvedWidgetReference>>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedWidgetReference>> _referencesByWidgetPath;

    public PrefabWidgetReferenceMap(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedWidgetReference>> referencesByWidgetPath)
    {
        _referencesByWidgetPath = referencesByWidgetPath;
    }

    public bool TryGetReference(string widgetPath, string attributeName, out ResolvedWidgetReference reference)
    {
        if (!string.IsNullOrWhiteSpace(widgetPath) &&
            _referencesByWidgetPath.TryGetValue(widgetPath, out var references) &&
            references.TryGetValue(attributeName, out reference!))
        {
            return true;
        }

        reference = null!;
        return false;
    }

    public IReadOnlyDictionary<string, ResolvedWidgetReference> GetReferences(string widgetPath)
    {
        return !string.IsNullOrWhiteSpace(widgetPath) &&
               _referencesByWidgetPath.TryGetValue(widgetPath, out var references)
            ? references
            : EmptyDictionary;
    }

    private static readonly IReadOnlyDictionary<string, ResolvedWidgetReference> EmptyDictionary =
        new Dictionary<string, ResolvedWidgetReference>(StringComparer.OrdinalIgnoreCase);
}
