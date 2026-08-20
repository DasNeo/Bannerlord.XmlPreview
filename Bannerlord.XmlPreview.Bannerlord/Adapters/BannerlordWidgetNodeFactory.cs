using Bannerlord.XmlPreview.Widgets.Bannerlord;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetNodeFactory : IWidgetNodeFactory
{
    public PrefabNode CreateNode(
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
            "ListPanel" => new ListPanelNode(attributes, children),
            "NavigatableListPanel" => new NavigatableListPanelNode(attributes, children),
            "NavigationScopeTargeter" => new NavigationScopeTargeterNode(attributes, children),
            "ItemTemplate" => new ItemTemplateNode(attributes, children),
            "HintWidget" => new HintWidgetNode(attributes, children),
            "Widget" => new WidgetNode(attributes, children),
            "BrushWidget" => new BrushWidgetNode(attributes, children),
            _ => new FallbackNode(name, attributes, children)
        };
    }
}
