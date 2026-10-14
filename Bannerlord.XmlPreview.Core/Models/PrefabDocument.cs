using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Models;

public sealed record PrefabDocument(
    string SourcePath,
    PrefabNode RootNode,
    IReadOnlyDictionary<string, string> Constants,
    IReadOnlySet<string> BindingKeys,
    PrefabWidgetPropertyMap PropertyMap,
    PrefabWidgetReferenceMap ReferenceMap)
{
    public string? PrefabIdentity { get; init; }
    public IReadOnlySet<string> Dependencies { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, SourceLocation> SourceMap { get; init; } = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
}
