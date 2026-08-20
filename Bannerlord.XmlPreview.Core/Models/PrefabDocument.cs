using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Models;

public sealed record PrefabDocument(
    string SourcePath,
    PrefabNode RootNode,
    IReadOnlyDictionary<string, string> Constants,
    IReadOnlySet<string> BindingKeys);
