using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ListPanelNode : PrefabNode
{
    public ListPanelNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("ListPanel", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var panel = new GauntletStackLayoutPanel
        {
            LayoutMethod = GauntletStackLayoutPanel.Parse(context.ResolveAttributeLiteral(Attributes, "StackLayout.LayoutMethod"))
        };

        context.ApplySizing(panel, Attributes, parentSize);
        context.ApplyOpacity(panel, Attributes);
        context.ApplyVisibility(panel, Attributes);
        context.ApplyToolTip(panel, Name, Attributes);
        context.AttachElementDebugMetadata(panel, Name, Attributes);
        context.AttachChildren(panel, Children, context.ResolveChildMeasureSize(panel, Attributes, parentSize));
        return new RenderedPrefabNode(panel, panel);
    }
}
