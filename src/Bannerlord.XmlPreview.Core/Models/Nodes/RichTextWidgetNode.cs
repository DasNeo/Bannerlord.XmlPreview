using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class RichTextWidgetNode : TextWidgetNodeBase
{
    public RichTextWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this("RichTextWidget", attributes, children)
    {
    }

    public RichTextWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    protected override double FallbackFontSize => 22;
    protected override bool SupportsRichText => true;
}
