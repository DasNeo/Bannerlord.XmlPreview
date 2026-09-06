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

    protected override Panel? PopulateContent(
        Grid layout,
        PrefabRenderContext context,
        Size chromeSize,
        Size measureInput,
        BrushChromeRenderInfo chrome)
    {
        var contentPadding = context.ResolveBrushContentPadding(chrome.PrimaryBrushId, chrome.IsDisabled);
        var contentHost = new Grid
        {
            Margin = contentPadding,
            ClipToBounds = false,
            IsHitTestVisible = false
        };
        layout.Children.Add(contentHost);

        if (Children.Count > 0)
        {
            var childCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = false,
                IsHitTestVisible = false
            };
            contentHost.Children.Add(childCanvas);

            var childMeasureSize = new Size(
                ResolveChildMeasureAxis(
                    context.UsesCoverChildren(Attributes, "WidthSizePolicy"),
                    measureInput.Width,
                    chromeSize.Width,
                    contentPadding.Left + contentPadding.Right),
                ResolveChildMeasureAxis(
                    context.UsesCoverChildren(Attributes, "HeightSizePolicy"),
                    measureInput.Height,
                    chromeSize.Height,
                    contentPadding.Top + contentPadding.Bottom));
            context.AttachChildren(childCanvas, Children, childMeasureSize);
            var contentBounds = context.MeasureCanvasChildrenBounds(childCanvas);
            context.ApplyCanvasLayout(childCanvas, contentBounds);
            return childCanvas;
        }

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
        contentHost.Children.Add(textBlock);
        return null;
    }

    protected override void FinalizeContent(
        PrefabRenderContext context,
        Panel? childHost,
        Size chromeSize,
        BrushChromeRenderInfo chrome)
    {
        if (childHost is not Canvas childCanvas || Children.Count == 0)
        {
            return;
        }

        var contentPadding = context.ResolveBrushContentPadding(chrome.PrimaryBrushId, chrome.IsDisabled);
        var innerSize = new Size(
            Math.Max(0d, chromeSize.Width - contentPadding.Left - contentPadding.Right),
            Math.Max(0d, chromeSize.Height - contentPadding.Top - contentPadding.Bottom));
        context.RelayoutCanvasChildren(childCanvas, Children, innerSize);
    }

    private static double ResolveChildMeasureAxis(
        bool parentCoversChildren,
        double availableSize,
        double chromeSize,
        double padding)
    {
        var measureSize = parentCoversChildren &&
                          double.IsFinite(availableSize) &&
                          availableSize > 0
            ? availableSize
            : chromeSize;
        return Math.Max(0d, measureSize - Math.Max(0d, padding));
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
