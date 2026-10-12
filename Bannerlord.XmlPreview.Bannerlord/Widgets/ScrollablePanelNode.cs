namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ScrollablePanelNode : ContainerPrefabNode
{
    public ScrollablePanelNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, false, attributes, children)
    {
    }
}
