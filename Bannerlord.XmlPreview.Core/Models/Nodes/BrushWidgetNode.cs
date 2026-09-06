namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class BrushWidgetNode : ContainerPrefabNode
{
    public BrushWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, true, attributes, children)
    {
    }

    public BrushWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this("BrushWidget", attributes, children)
    {
    }
}
