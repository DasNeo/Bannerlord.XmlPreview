using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class HintWidgetNode : PrefabNode
{
    public HintWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("HintWidget", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var hint = new Border
        {
            Width = 0,
            Height = 0,
            Background = Brushes.Transparent,
            MinWidth = 0,
            MinHeight = 0
        };

        context.ApplyOpacity(hint, Attributes);
        context.ApplyVisibility(hint, Attributes);
        context.ApplyToolTip(hint, Name, Attributes);
        context.AttachElementDebugMetadata(hint, Name, Attributes);
        return new RenderedPrefabNode(hint, null);
    }
}
