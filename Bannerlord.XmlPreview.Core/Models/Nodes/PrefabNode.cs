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

    internal IDisposable ApplyPreviewState(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> states)
    {
        var originals = new List<(PrefabNode Node, IReadOnlyDictionary<string, string> Attributes)>();
        void Apply(PrefabNode node)
        {
            if (node.Attributes.TryGetValue(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, out var path) && states.TryGetValue(path, out var state))
            {
                originals.Add((node, node.Attributes));
                var copy = new Dictionary<string, string>(node.Attributes, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in state) copy[pair.Key] = pair.Value;
                node.Attributes = copy;
            }
            foreach (var child in node.Children) Apply(child);
        }
        Apply(this);
        return new PreviewStateHandle(originals);
    }

    private sealed class PreviewStateHandle(List<(PrefabNode Node, IReadOnlyDictionary<string, string> Attributes)> originals) : IDisposable
    {
        public void Dispose() { foreach (var (node, attributes) in originals) node.Attributes = attributes; originals.Clear(); }
    }

    public IReadOnlyList<PrefabNode> Children { get; }

    public abstract RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize);

    protected string? GetAttribute(string key)
    {
        return Attributes.TryGetValue(key, out var value) ? value : null;
    }
}
