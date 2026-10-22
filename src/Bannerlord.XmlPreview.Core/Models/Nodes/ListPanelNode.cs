using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ListPanelNode : PrefabNode
{
    public ListPanelNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    public ListPanelNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this("ListPanel", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var panel = new GauntletStackLayoutPanel
        {
            LayoutMethod = GauntletStackLayoutPanel.Parse(context.ResolveStackLayoutMethod(Attributes))
        };

        context.ApplySizing(panel, Attributes, parentSize);
        context.ApplyOpacity(panel, Attributes, panel);
        context.ApplyVisibility(panel, Attributes);
        context.ApplyToolTip(panel, Name, Attributes);
        context.AttachElementDebugMetadata(panel, Name, Attributes);

        var contentNodes = Children.Where(static child => child is not ItemTemplateNode).ToList();
        var templateNode = Children.OfType<ItemTemplateNode>().FirstOrDefault();
        var childMeasureSize = context.ResolveChildMeasureSize(panel, Attributes, parentSize);
        context.AttachChildren(panel, contentNodes, childMeasureSize);

        if (templateNode is not null && context.TryGetTemplateItemBindings(this, out var templateItemBindings))
        {
            RenderTemplateItems(panel, templateNode, context, childMeasureSize, templateItemBindings);
        }
        else if (templateNode is not null)
        {
            context.AttachChildren(panel, [templateNode], childMeasureSize);
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
        for (var itemIndex = 0; itemIndex < templateItemBindings.Count; itemIndex++)
        {
            var itemBindings = templateItemBindings[itemIndex];
            using var _ = context.PushBindingScope(itemBindings,
                itemBindings.GetValueOrDefault("__key", itemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var rendered = context.RegisterRenderedNode(
                templateNode.Name,
                templateNode.Attributes,
                parentSize,
                templateNode.Render(context, parentSize));
            var layoutAttributes = templateNode.Children.FirstOrDefault()?.Attributes ?? templateNode.Attributes;
            context.ApplyFlowLayout(rendered.Element, layoutAttributes);
            context.PrepareGauntletStackLayoutChild(
                rendered.Element,
                layoutAttributes,
                ((GauntletStackLayoutPanel)panel).LayoutMethod);
            panel.Children.Add(rendered.Element);
        }
    }
}
