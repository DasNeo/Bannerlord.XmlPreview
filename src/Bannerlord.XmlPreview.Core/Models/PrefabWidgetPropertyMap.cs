namespace Bannerlord.XmlPreview.Models;

public sealed class PrefabWidgetPropertyMap
{
    public static PrefabWidgetPropertyMap Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, ResolvedWidgetProperty>>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedWidgetProperty>> _propertiesByWidgetPath;

    public PrefabWidgetPropertyMap(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedWidgetProperty>> propertiesByWidgetPath)
    {
        _propertiesByWidgetPath = propertiesByWidgetPath;
    }

    public bool TryGetProperty(string widgetPath, string attributeName, out ResolvedWidgetProperty property)
    {
        if (!string.IsNullOrWhiteSpace(widgetPath) &&
            _propertiesByWidgetPath.TryGetValue(widgetPath, out var properties) &&
            properties.TryGetValue(attributeName, out property!))
        {
            return true;
        }

        property = null!;
        return false;
    }

    public IReadOnlyDictionary<string, ResolvedWidgetProperty> GetProperties(string widgetPath)
    {
        return !string.IsNullOrWhiteSpace(widgetPath) &&
               _propertiesByWidgetPath.TryGetValue(widgetPath, out var properties)
            ? properties
            : EmptyDictionary;
    }

    private static readonly IReadOnlyDictionary<string, ResolvedWidgetProperty> EmptyDictionary =
        new Dictionary<string, ResolvedWidgetProperty>(StringComparer.OrdinalIgnoreCase);
}
