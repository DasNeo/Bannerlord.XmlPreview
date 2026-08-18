using Bannerlord.XmlPreview.DemoData;
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
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        context.ApplySizing(panel, Attributes, parentSize);
        context.ApplyOpacity(panel, Attributes);
        context.ApplyVisibility(panel, Attributes);
        context.ApplyToolTip(panel, Name, Attributes);
        context.AttachElementDebugMetadata(panel, Name, Attributes);

        var contentNodes = Children.Where(static child => child is not ItemTemplateNode).ToList();
        var templateNode = Children.OfType<ItemTemplateNode>().FirstOrDefault();
        context.AttachChildren(panel, contentNodes, context.Measure(panel, parentSize));

        if (templateNode is not null && IsEscapeMenuItemsDataSource())
        {
            RenderEscapeMenuTemplateItems(panel, templateNode, context, parentSize);
        }
        else if (templateNode is not null)
        {
            context.AttachChildren(panel, [templateNode], context.Measure(panel, parentSize));
        }

        return new RenderedPrefabNode(panel, panel);
    }

    private bool IsEscapeMenuItemsDataSource()
    {
        return Attributes.TryGetValue("DataSource", out var dataSource) &&
               string.Equals(dataSource, "{MenuItems}", StringComparison.OrdinalIgnoreCase);
    }

    private static void RenderEscapeMenuTemplateItems(
        Panel panel,
        PrefabNode templateNode,
        PrefabRenderContext context,
        Size parentSize)
    {
        foreach (var item in PreviewDemoCatalog.EscapeMenuItems)
        {
            PrefabRenderContext.SetMockBindingValue("ActionText", item.ActionText);
            PrefabRenderContext.SetMockBindingValue("IsDisabled", item.IsDisabled ? "true" : "false");
            PrefabRenderContext.SetMockBindingValue("IsPositiveBehaviored", item.IsPositiveBehaviored ? "true" : "false");

            var rendered = templateNode.Render(context, parentSize);
            var layoutAttributes = templateNode.Children.FirstOrDefault()?.Attributes ?? templateNode.Attributes;
            context.ApplyFlowLayout(rendered.Element, layoutAttributes);
            panel.Children.Add(rendered.Element);
        }
    }
}
