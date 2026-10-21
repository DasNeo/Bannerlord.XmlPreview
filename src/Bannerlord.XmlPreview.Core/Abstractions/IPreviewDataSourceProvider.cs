using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IPreviewDataSourceProvider
{
    PreviewDataSnapshot CaptureSnapshot(PrefabNode root) => PreviewDataSnapshot.Capture(this, root, PreviewDataSnapshot.Empty);

    bool TryGetTemplateItemBindings(
        PrefabNode node,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings);

    void SetTemplateItemBindings(
        PrefabNode node,
        IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings);
}
