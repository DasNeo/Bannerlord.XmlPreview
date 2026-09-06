using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Models.Nodes;

public class GridWidgetNode : PrefabNode
{
    public GridWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var panel = CreatePanel(context, Attributes);

        context.ApplySizing(panel, Attributes, parentSize);
        context.ApplyOpacity(panel, Attributes);
        context.ApplyVisibility(panel, Attributes);
        context.ApplyToolTip(panel, Name, Attributes);
        context.AttachElementDebugMetadata(panel, Name, Attributes);
        AttachGridChildren(panel, context, parentSize);
        return new RenderedPrefabNode(panel, panel);
    }

    protected virtual bool TryGetTemplateItemBindings(
        PrefabRenderContext context,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
    {
        return context.TryGetTemplateItemBindings(this, out itemBindings);
    }

    protected void AttachGridChildren(
        GauntletGridLayoutPanel panel,
        PrefabRenderContext context,
        Size parentSize)
    {
        var contentNodes = Children.Where(static child => child is not ItemTemplateNode).ToList();
        var templateNode = Children.OfType<ItemTemplateNode>().FirstOrDefault();
        var childMeasureSize = context.ResolveChildMeasureSize(panel, Attributes, parentSize);

        context.AttachChildren(panel, contentNodes, childMeasureSize);

        var templateMeasureSize = new Size(
            panel.DefaultCellWidth > 0 ? panel.DefaultCellWidth : childMeasureSize.Width,
            panel.DefaultCellHeight > 0 ? panel.DefaultCellHeight : childMeasureSize.Height);

        if (templateNode is not null && TryGetTemplateItemBindings(context, out var templateItemBindings))
        {
            RenderTemplateItems(panel, templateNode, context, templateMeasureSize, templateItemBindings);
        }
        else if (templateNode is not null)
        {
            RenderTemplateItems(
                panel,
                templateNode,
                context,
                templateMeasureSize,
                [new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)]);
        }
    }

    private static GauntletGridLayoutPanel CreatePanel(
        PrefabRenderContext context,
        IReadOnlyDictionary<string, string> attributes)
    {
        return new GauntletGridLayoutPanel
        {
            RowCount = (int)Math.Round(context.ResolveDouble(attributes, "RowCount", -1)),
            ColumnCount = (int)Math.Round(context.ResolveDouble(attributes, "ColumnCount", -1)),
            UseDynamicCellWidth = context.ResolveBoolean(attributes, "UseDynamicCellWidth"),
            UseDynamicCellHeight = context.ResolveBoolean(attributes, "UseDynamicCellHeight"),
            DefaultCellWidth = context.ResolveDouble(attributes, "DefaultCellWidth", 0),
            DefaultCellHeight = context.ResolveDouble(attributes, "DefaultCellHeight", 0),
            Direction = GauntletGridLayoutPanel.ParseDirection(
                ResolveGridAttributeLiteral(context, attributes, "GridLayout.Direction", "LayoutImp.Direction", "Direction")),
            HorizontalLayoutMethod = GauntletGridLayoutPanel.ParseHorizontalLayoutMethod(
                ResolveGridAttributeLiteral(
                    context,
                    attributes,
                    "GridLayout.HorizontalLayoutMethod",
                    "LayoutImp.HorizontalLayoutMethod",
                    "HorizontalLayoutMethod")),
            VerticalLayoutMethod = GauntletGridLayoutPanel.ParseVerticalLayoutMethod(
                ResolveGridAttributeLiteral(
                    context,
                    attributes,
                    "GridLayout.VerticalLayoutMethod",
                    "LayoutImp.VerticalLayoutMethod",
                    "VerticalLayoutMethod")),
            WidthSizePolicy = context.ResolveAttributeLiteral(attributes, "WidthSizePolicy") ?? string.Empty,
            HeightSizePolicy = context.ResolveAttributeLiteral(attributes, "HeightSizePolicy") ?? string.Empty
        };
    }

    private static string? ResolveGridAttributeLiteral(
        PrefabRenderContext context,
        IReadOnlyDictionary<string, string> attributes,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = context.ResolveAttributeLiteral(attributes, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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
            if (panel is GauntletGridLayoutPanel gauntletGridPanel)
            {
                context.PrepareGauntletGridLayoutChild(rendered.Element, layoutAttributes, gauntletGridPanel);
            }

            panel.Children.Add(rendered.Element);
        }
    }
}
