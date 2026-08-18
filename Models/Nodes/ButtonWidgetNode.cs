using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class ButtonWidgetNode : BrushChromeNodeBase
{
    public ButtonWidgetNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    protected override BrushChromeRenderInfo ResolveChrome(PrefabRenderContext context)
    {
        var brushId = context.ResolveBinding(GetAttribute("Brush"));
        var textNode = GetTextNode();
        var textBrushId = textNode?.Attributes.TryGetValue("Brush", out var childBrush) == true
            ? context.ResolveBinding(childBrush)
            : null;
        var textFontSize = textNode?.Attributes.TryGetValue("Brush.FontSize", out var childFontSize) == true
            ? childFontSize
            : null;
        var isDisabled = context.IsDisabled(Attributes);

        return new BrushChromeRenderInfo(
            brushId,
            isDisabled,
            [brushId, textBrushId]);
    }

    protected override void PopulateContent(
        Grid layout,
        PrefabRenderContext context,
        Size chromeSize,
        BrushChromeRenderInfo chrome)
    {
        var textNode = GetTextNode();
        var textBrushId = textNode?.Attributes.TryGetValue("Brush", out var childBrush) == true
            ? context.ResolveBinding(childBrush)
            : null;
        var textFontSize = textNode?.Attributes.TryGetValue("Brush.FontSize", out var childFontSize) == true
            ? childFontSize
            : null;
        var textAttributes = textNode?.Attributes ?? Attributes;

        var textBlock = new TextBlock
        {
            Text = ResolveButtonText(context),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = context.ResolveTextHorizontalAlignment(textBrushId),
            VerticalAlignment = context.ResolveTextVerticalAlignment(textBrushId),
            TextWrapping = TextWrapping.NoWrap
        };
        context.ApplyTextStyle(textBlock, textBrushId, textFontSize, textAttributes, 20);
        context.ApplyVisualTextCentering(textBlock, chromeSize.Width);
        textBlock.Margin = context.ResolveBrushContentPadding(chrome.PrimaryBrushId, chrome.IsDisabled);
        layout.Children.Add(textBlock);
    }

    private string ResolveButtonText(PrefabRenderContext context)
    {
        var textNode = GetTextNode();
        var childText = context.ResolveBinding(textNode?.Attributes.TryGetValue("Text", out var value) == true ? value : null);
        if (!string.IsNullOrWhiteSpace(childText))
        {
            return childText;
        }

        var parameterText = context.ResolveBinding(GetAttribute("Parameter.ButtonText"));
        return string.IsNullOrWhiteSpace(parameterText) ? "Button" : parameterText;
    }

    private PrefabNode? GetTextNode()
    {
        return Children.FirstOrDefault(static child => child.Name is "RichTextWidget" or "TextWidget");
    }
}
