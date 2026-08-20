using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ScrollablePanelFixedHeaderWidgetNode : PrefabNode
{
    public ScrollablePanelFixedHeaderWidgetNode(
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
        : base("ScrollablePanelFixedHeaderWidget", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var spacer = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Focusable = false
        };

        context.ApplySizing(spacer, Attributes, parentSize);
        var headerHeight = context.ResolveDouble(Attributes, "HeaderHeight", 0);
        if ((double.IsNaN(spacer.Height) || spacer.Height <= 0) && headerHeight > 0)
        {
            spacer.Height = headerHeight;
        }

        context.ApplyOpacity(spacer, Attributes);
        context.ApplyVisibility(spacer, Attributes);
        context.ApplyToolTip(spacer, Name, Attributes);
        context.AttachElementDebugMetadata(spacer, Name, Attributes);

        if (Children.Count > 0)
        {
            context.AttachChildren(spacer, Children, context.ResolveChildMeasureSize(spacer, Attributes, parentSize));
        }

        return new RenderedPrefabNode(spacer, spacer);
    }
}
