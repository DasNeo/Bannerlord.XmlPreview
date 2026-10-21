using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class NavigatableGridWidgetNode : GridWidgetNode
{
    public NavigatableGridWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    protected override bool TryGetTemplateItemBindings(
        PrefabRenderContext context,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
    {
        return context.TryGetTemplateItemBindings(this, out itemBindings);
    }
}
