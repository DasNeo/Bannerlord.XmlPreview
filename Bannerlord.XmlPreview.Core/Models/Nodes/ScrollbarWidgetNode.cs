namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ScrollbarWidgetNode : ContainerPrefabNode
{
    public ScrollbarWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, false, attributes, children)
    {
    }
}
