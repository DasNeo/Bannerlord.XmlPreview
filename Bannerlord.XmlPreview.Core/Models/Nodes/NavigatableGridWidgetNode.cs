using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class NavigatableGridWidgetNode : GridWidgetNode
{
    public NavigatableGridWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    protected override IReadOnlyList<IReadOnlyDictionary<string, string>> GetTemplateItemBindings(PrefabRenderContext context)
    {
        return context.GetTemplateItemBindings(Name, Attributes);
    }
}
