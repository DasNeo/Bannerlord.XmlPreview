using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class TextWidgetNode : TextWidgetNodeBase
{
    public TextWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("TextWidget", attributes, children)
    {
    }

    protected override double FallbackFontSize => 20;
}
