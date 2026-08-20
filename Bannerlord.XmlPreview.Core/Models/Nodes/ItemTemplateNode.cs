using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ItemTemplateNode : PrefabNode
{
    public ItemTemplateNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("ItemTemplate", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        if (Children.Count == 1)
        {
            return Children[0].Render(context, parentSize);
        }

        var canvas = new Canvas
        {
            Background = Brushes.Transparent
        };

        context.ApplySizing(canvas, Attributes, parentSize);
        context.ApplyOpacity(canvas, Attributes);
        context.ApplyVisibility(canvas, Attributes);
        context.ApplyToolTip(canvas, Name, Attributes);
        context.AttachElementDebugMetadata(canvas, Name, Attributes);

        var templateSize = context.ResolveChildMeasureSize(canvas, Attributes, parentSize);
        context.AttachChildren(canvas, Children, templateSize);
        return new RenderedPrefabNode(canvas, canvas);
    }
}
