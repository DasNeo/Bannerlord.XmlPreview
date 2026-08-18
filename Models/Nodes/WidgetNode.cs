namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class WidgetNode : ContainerPrefabNode
{
    public WidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("Widget", false, attributes, children)
    {
    }
}
