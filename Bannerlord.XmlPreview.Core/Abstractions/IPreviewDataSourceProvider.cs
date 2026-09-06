using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IPreviewDataSourceProvider
{
    bool TryGetTemplateItemBindings(
        PrefabNode node,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings);

    void SetTemplateItemBindings(
        PrefabNode node,
        IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings);
}
