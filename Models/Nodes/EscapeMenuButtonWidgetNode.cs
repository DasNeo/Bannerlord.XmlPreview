using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class EscapeMenuButtonWidgetNode : BrushChromeNodeBase
{
    public EscapeMenuButtonWidgetNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("EscapeMenuButtonWidget", attributes, children)
    {
    }

    protected override BrushChromeRenderInfo ResolveChrome(PrefabRenderContext context)
    {
        var defaultBrushId = context.ResolveBinding(GetAttribute("Brush"));
        var positiveBrushId = context.ResolveBinding(GetAttribute("PositiveBehaviorBrush"));
        var selectedBrushId = context.ResolveBoolean(GetAttribute("IsPositiveBehaviored"))
            ? positiveBrushId
            : defaultBrushId;
        var isDisabled = context.IsDisabled(Attributes);

        return new BrushChromeRenderInfo(
            selectedBrushId,
            isDisabled,
            [defaultBrushId, positiveBrushId]);
    }

    protected override void PopulateContent(
        Grid layout,
        PrefabRenderContext context,
        Size chromeSize,
        BrushChromeRenderInfo chrome)
    {
        var childCanvas = new Canvas();
        layout.Children.Add(childCanvas);

        if (Children.Count > 0)
        {
            context.AttachChildren(childCanvas, Children, chromeSize);
            return;
        }

        childCanvas.Children.Add(CreateFallbackLabel(context, chrome.PrimaryBrushId, chromeSize));
    }

    private FrameworkElement CreateFallbackLabel(PrefabRenderContext context, string? brushId, Size buttonSize)
    {
        var text = new TextBlock
        {
            Text = context.ResolveBinding(GetAttribute("Text")),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Width = buttonSize.Width,
            Height = buttonSize.Height,
            TextAlignment = TextAlignment.Center
        };

        context.ApplyTextStyle(text, brushId, GetAttribute("Brush.FontSize"), Attributes, 20);
        return text;
    }
}
