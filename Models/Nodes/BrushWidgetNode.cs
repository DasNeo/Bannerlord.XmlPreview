namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class BrushWidgetNode : ContainerPrefabNode
{
    public BrushWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("BrushWidget", true, attributes, children)
    {
    }
}
