using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ListPanelNode : PrefabNode
{
    private readonly bool _renderBrush;
    public bool RendersBrush => _renderBrush;

    public ListPanelNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this(name, attributes, children, false) { }

    public ListPanelNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children, bool renderBrush)
        : base(name, attributes, children)
    {
        _renderBrush = renderBrush;
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
        if (!_renderBrush) context.ApplyOpacity(panel, Attributes, panel);
        context.ApplyVisibility(panel, Attributes);
        context.ApplyToolTip(panel, Name, Attributes);
        if (!_renderBrush) context.AttachElementDebugMetadata(panel, Name, Attributes);

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

        if (!_renderBrush) return new RenderedPrefabNode(panel, panel);

        var brushId = context.ResolveBinding(GetAttribute("Brush"));
        var disabled = context.IsDisabled(Attributes);
        var layout = new Grid();
        context.ApplySizing(layout, Attributes, parentSize);
        context.ApplyVisibility(layout, Attributes);
        context.ApplyToolTip(layout, Name, Attributes);
        context.AttachElementDebugMetadata(layout, Name, Attributes, null, [brushId], disabled);
        layout.Children.Add(panel);
        var size = context.Measure(panel, Attributes, parentSize);
        if (context.AddBrushOverlay(layout, brushId, disabled, size.Width, size.Height, 0) is null)
            layout.Background = context.ResolvePanelBrush(brushId, disabled);
        context.ApplyOpacity(layout, Attributes, panel);
        return new RenderedPrefabNode(layout, panel);
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
