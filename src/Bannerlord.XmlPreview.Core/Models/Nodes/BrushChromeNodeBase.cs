using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public abstract class BrushChromeNodeBase : PrefabNode
{
    protected BrushChromeNodeBase(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    public sealed override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var chrome = ResolveChrome(context);

        var layout = new Grid
        {
            ClipToBounds = false
        };

        var border = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Child = layout
        };

        context.ApplySizing(border, Attributes, parentSize);
        context.ApplyButtonSizing(border, Attributes, chrome.PrimaryBrushId, chrome.IsDisabled);
        context.ApplyVisibility(border, Attributes);
        context.ApplyToolTip(border, Name, Attributes);
        context.AttachElementDebugMetadata(border, Name, Attributes, null, chrome.DebugBrushIds, chrome.IsDisabled);

        var measureInput = context.ResolveChildMeasureSize(border, Attributes, parentSize);
        var initialChromeSize = context.Measure(border, Attributes, parentSize);
        var contentHostSize = new Size(
            initialChromeSize.Width > 0 ? initialChromeSize.Width : measureInput.Width,
            initialChromeSize.Height > 0 ? initialChromeSize.Height : measureInput.Height);
        var childHost = PopulateContent(layout, context, contentHostSize, measureInput, chrome);

        var contentSize = context.Measure(layout, measureInput);
        initialChromeSize = context.FinalizeCoverChildrenSize(border, Attributes, contentSize);
        FinalizeContent(context, childHost, initialChromeSize, chrome);

        context.AddBrushOverlay(layout, chrome.PrimaryBrushId, chrome.IsDisabled, initialChromeSize.Width, initialChromeSize.Height, 0);
        context.ApplyOpacity(border, Attributes, childHost);

        return new RenderedPrefabNode(border, childHost);
    }

    protected abstract BrushChromeRenderInfo ResolveChrome(PrefabRenderContext context);

    protected abstract Panel? PopulateContent(
        Grid layout,
        PrefabRenderContext context,
        Size chromeSize,
        Size measureInput,
        BrushChromeRenderInfo chrome);

    protected virtual void FinalizeContent(
        PrefabRenderContext context,
        Panel? childHost,
        Size chromeSize,
        BrushChromeRenderInfo chrome)
    {
    }

    protected sealed record BrushChromeRenderInfo(
        string? PrimaryBrushId,
        bool IsDisabled,
        IReadOnlyList<string?> DebugBrushIds);
}
