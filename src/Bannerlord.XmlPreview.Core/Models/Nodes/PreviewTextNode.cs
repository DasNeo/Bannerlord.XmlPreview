namespace Bannerlord.XmlPreview.Models.Nodes;

/// <summary>Generic static text blueprint materialization, also used for read-only entry previews.</summary>
public sealed class PreviewTextNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
    : TextWidgetNodeBase(name, attributes, children)
{
    protected override double FallbackFontSize => 20;
}
