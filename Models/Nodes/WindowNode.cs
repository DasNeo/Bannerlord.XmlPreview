using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class WindowNode : PrefabNode
{
    public WindowNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("Window", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var canvas = new Canvas
        {
            Width = parentSize.Width,
            Height = parentSize.Height,
            Background = System.Windows.Media.Brushes.Transparent
        };

        context.AttachElementDebugMetadata(canvas, Name, Attributes);
        context.AttachChildren(canvas, Children, parentSize);
        return new RenderedPrefabNode(canvas, canvas);
    }
}
