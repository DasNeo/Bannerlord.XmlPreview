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
        var textAttributes = textNode?.Attributes ?? Attributes;
        var horizontalAlignment = textNode is not null
            ? context.ResolveWidgetHorizontalAlignment(
                textNode.Attributes,
                context.ResolveTextHorizontalAlignment(textBrushId, textAttributes))
            : context.ResolveTextHorizontalAlignment(textBrushId, textAttributes);
        var verticalAlignment = textNode is not null
            ? context.ResolveWidgetVerticalAlignment(
                textNode.Attributes,
                context.ResolveTextVerticalAlignment(textBrushId, textAttributes))
            : context.ResolveTextVerticalAlignment(textBrushId, textAttributes);

        var textBlock = new TextBlock
        {
            Text = ResolveButtonText(context),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            TextWrapping = TextWrapping.NoWrap
        };
        context.ApplyTextStyle(textBlock, textBrushId, textAttributes, 20);
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
        if (!string.IsNullOrEmpty(childText))
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
