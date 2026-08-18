using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class RichTextWidgetNode : TextWidgetNodeBase
{
    public RichTextWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("RichTextWidget", attributes, children)
    {
    }

    protected override double FallbackFontSize => 22;
}
