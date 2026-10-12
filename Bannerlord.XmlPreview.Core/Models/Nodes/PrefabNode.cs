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

    public IReadOnlyDictionary<string, string> Attributes { get; private set; }

    public IReadOnlyList<PrefabNode> Children { get; private set; }

    internal PrefabNode CopyForRender(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> states)
    {
        var copy = (PrefabNode)MemberwiseClone();
        var attributes = new Dictionary<string, string>(Attributes, StringComparer.OrdinalIgnoreCase);
        if (attributes.TryGetValue(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, out var path) && states.TryGetValue(path, out var state))
            foreach (var pair in state) attributes[pair.Key] = pair.Value;
        copy.Attributes = attributes;
        copy.Children = Children.Select(child => child.CopyForRender(states)).ToArray();
        return copy;
    }

    public abstract RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize);

    protected string? GetAttribute(string key)
    {
        return Attributes.TryGetValue(key, out var value) ? value : null;
    }
}
