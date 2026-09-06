namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class WidgetNode : ContainerPrefabNode
{
    public WidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, false, attributes, children)
    {
    }

    public WidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this("Widget", attributes, children)
    {
    }
}
