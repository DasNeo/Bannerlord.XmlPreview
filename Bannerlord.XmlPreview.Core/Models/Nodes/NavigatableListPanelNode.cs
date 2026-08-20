using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class NavigatableListPanelNode : PrefabNode
{
    public NavigatableListPanelNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("NavigatableListPanel", attributes, children)
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

        var contentNodes = Children.Where(static child => child is not ItemTemplateNode).ToList();
        var templateNode = Children.OfType<ItemTemplateNode>().FirstOrDefault();
        context.AttachChildren(panel, contentNodes, context.ResolveChildMeasureSize(panel, Attributes, parentSize));

        var templateItemBindings = templateNode is null
            ? Array.Empty<IReadOnlyDictionary<string, string>>()
            : context.GetTemplateItemBindings(Name, Attributes);

        if (templateNode is not null && templateItemBindings.Count > 0)
        {
            RenderTemplateItems(panel, templateNode, context, parentSize, templateItemBindings);
        }
        else if (templateNode is not null)
        {
            context.AttachChildren(panel, [templateNode], context.ResolveChildMeasureSize(panel, Attributes, parentSize));
        }

        return new RenderedPrefabNode(panel, panel);
    }

    private static void RenderTemplateItems(
        Panel panel,
        PrefabNode templateNode,
        PrefabRenderContext context,
        Size parentSize,
        IReadOnlyList<IReadOnlyDictionary<string, string>> templateItemBindings)
    {
        foreach (var itemBindings in templateItemBindings)
        {
            using var _ = context.PushBindingScope(itemBindings);
            var rendered = context.RegisterRenderedNode(templateNode.Name, templateNode.Attributes, parentSize, templateNode.Render(context, parentSize));
            var layoutAttributes = templateNode.Children.FirstOrDefault()?.Attributes ?? templateNode.Attributes;
            context.ApplyFlowLayout(rendered.Element, layoutAttributes);
            if (panel is GauntletStackLayoutPanel gauntletStackPanel)
            {
                context.PrepareGauntletStackLayoutChild(rendered.Element, layoutAttributes, gauntletStackPanel.LayoutMethod);
            }
            panel.Children.Add(rendered.Element);
        }
    }
}
