using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Widgets.Bannerlord;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetNodeFactory : IWidgetNodeFactory
{
    private readonly IBannerlordWidgetReflectionService _reflectionService;

    public BannerlordWidgetNodeFactory(IBannerlordWidgetReflectionService reflectionService)
    {
        _reflectionService = reflectionService;
    }

    public PrefabNode CreateNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        return CreateBuiltInNode(name, attributes, children) ??
               CreateInheritedWidgetNode(name, attributes, children) ??
               new FallbackNode(name, attributes, children);
    }

    private static PrefabNode? CreateBuiltInNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        return name switch
        {
            "Window" => new WindowNode(attributes, children),
            "RichTextWidget" => new RichTextWidgetNode(attributes, children),
            "TextWidget" => new TextWidgetNode(attributes, children),
            "ButtonWidget" => new ButtonWidgetNode(name, attributes, children),
            "Standard.PopupCloseButton" => new ButtonWidgetNode(name, attributes, children),
            "EscapeMenuButtonWidget" => new EscapeMenuButtonWidgetNode(attributes, children),
            "TabControl" => new TabControlNode(attributes, children),
            "ListPanel" => new ListPanelNode(attributes, children),
            "NavigatableListPanel" => new NavigatableListPanelNode(attributes, children),
            "ScrollablePanelFixedHeaderWidget" => new ScrollablePanelFixedHeaderWidgetNode(attributes, children),
            "NavigationScopeTargeter" => new NavigationScopeTargeterNode(attributes, children),
            "ItemTemplate" => new ItemTemplateNode(attributes, children),
            "HintWidget" => new HintWidgetNode(attributes, children),
            "Widget" => new WidgetNode(attributes, children),
            "BrushWidget" => new BrushWidgetNode(attributes, children),
            "ScrollablePanel" => new WidgetNode(name, attributes, children),
            _ when string.Equals(name, "ScrollbarWidget", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("ScrollbarWidget", StringComparison.OrdinalIgnoreCase) =>
                new ScrollbarWidgetNode(name, attributes, children),
            _ when name.EndsWith("NavigatableGridWidget", StringComparison.OrdinalIgnoreCase) =>
                new NavigatableGridWidgetNode(name, attributes, children),
            _ when string.Equals(name, "GridWidget", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("GridWidget", StringComparison.OrdinalIgnoreCase) =>
                new GridWidgetNode(name, attributes, children),
            _ => null
        };
    }

    private PrefabNode? CreateInheritedWidgetNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        var renderKind = ResolveRenderKind(name);
        return renderKind switch
        {
            BannerlordWidgetRenderKind.Widget => new WidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.BrushWidget => new BrushWidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.ButtonWidget => new ButtonWidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.ListPanel => new ListPanelNode(name, attributes, children),
            BannerlordWidgetRenderKind.BrushListPanel => new ListPanelNode(name, attributes, children, renderBrush: true),
            BannerlordWidgetRenderKind.TextWidget => new PreviewTextNode(name, attributes, children),
            BannerlordWidgetRenderKind.RichTextWidget => new RichTextWidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.NavigationScopeTargeter => new NavigationScopeTargeterNode(name, attributes, children),
            BannerlordWidgetRenderKind.GridWidget => new GridWidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.ScrollablePanel => new WidgetNode(name, attributes, children),
            BannerlordWidgetRenderKind.ScrollbarWidget => new ScrollbarWidgetNode(name, attributes, children),
            _ => null
        };
    }

    private BannerlordWidgetRenderKind ResolveRenderKind(string widgetName)
    {
        return _reflectionService.TryGetRenderKind(widgetName, out var renderKind)
            ? renderKind
            : BannerlordWidgetRenderKind.Unknown;
    }
}
