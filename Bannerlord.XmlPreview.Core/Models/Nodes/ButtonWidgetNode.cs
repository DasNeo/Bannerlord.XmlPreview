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
        var textHorizontalAlignment = textNode?.Attributes.TryGetValue("Brush.TextHorizontalAlignment", out var childHorizontalAlignment) == true
            ? childHorizontalAlignment
            : null;
        var textVerticalAlignment = textNode?.Attributes.TryGetValue("Brush.TextVerticalAlignment", out var childVerticalAlignment) == true
            ? childVerticalAlignment
            : null;
        var horizontalAlignment = textNode is not null
            ? context.ResolveWidgetHorizontalAlignment(
                textNode.Attributes,
                context.ResolveTextHorizontalAlignment(textBrushId, textHorizontalAlignment))
            : context.ResolveTextHorizontalAlignment(textBrushId, textHorizontalAlignment);
        var verticalAlignment = textNode is not null
            ? context.ResolveWidgetVerticalAlignment(
                textNode.Attributes,
                context.ResolveTextVerticalAlignment(textBrushId, textVerticalAlignment))
            : context.ResolveTextVerticalAlignment(textBrushId, textVerticalAlignment);

        var textBlock = new TextBlock
        {
            Text = ResolveButtonText(context),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            TextWrapping = TextWrapping.NoWrap
        };
        context.ApplyTextStyle(textBlock, textBrushId, textFontSize, textAttributes, 20);
        context.ApplyVisualTextCentering(textBlock, chromeSize.Width);

        var contentHost = new Grid
        {
            Margin = context.ResolveBrushContentPadding(chrome.PrimaryBrushId, chrome.IsDisabled),
            ClipToBounds = false,
            IsHitTestVisible = false
        };
        contentHost.Children.Add(textBlock);
        layout.Children.Add(contentHost);
    }

    private string ResolveButtonText(PrefabRenderContext context)
    {
        var textNode = GetTextNode();
        var childText = context.ResolveBinding(textNode?.Attributes.TryGetValue("Text", out var value) == true ? value : null);
        if (!string.IsNullOrWhiteSpace(childText))
        {
            return childText;
        }

        return context.ResolveWidgetText(Name, Attributes, "Button");
    }

    private PrefabNode? GetTextNode()
    {
        return Children.FirstOrDefault(static child => child.Name is "RichTextWidget" or "TextWidget");
    }
}
