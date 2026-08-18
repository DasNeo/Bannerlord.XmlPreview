using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public abstract class PrefabNode
{
    protected PrefabNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
    {
        Name = name;
        Attributes = attributes;
        Children = children;
    }

    public string Name { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    public IReadOnlyList<PrefabNode> Children { get; }

    public abstract RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize);

    protected string? GetAttribute(string key)
    {
        return Attributes.TryGetValue(key, out var value) ? value : null;
    }
}
