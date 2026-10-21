using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IWidgetNodeFactory
{
    PrefabNode CreateNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children);
}
