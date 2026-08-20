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
        context.ApplyBrushBorderStyle(border, chrome.PrimaryBrushId, chrome.IsDisabled, false);
        context.ApplyOpacity(border, Attributes);
        context.ApplyVisibility(border, Attributes);
        context.ApplyToolTip(border, Name, Attributes);
        context.AttachElementDebugMetadata(border, Name, Attributes, null, chrome.DebugBrushIds, chrome.IsDisabled);

        var initialChromeSize = context.Measure(border, parentSize);
        PopulateContent(layout, context, initialChromeSize, chrome);

        var contentSize = context.Measure(layout, parentSize);
        if (context.ApplyCoverChildrenSize(border, Attributes, contentSize))
        {
            initialChromeSize = context.Measure(border, parentSize);
        }

        context.AddBrushOverlay(layout, chrome.PrimaryBrushId, chrome.IsDisabled, initialChromeSize.Width, initialChromeSize.Height, 0);

        return new RenderedPrefabNode(border, null);
    }

    protected abstract BrushChromeRenderInfo ResolveChrome(PrefabRenderContext context);

    protected abstract void PopulateContent(
        Grid layout,
        PrefabRenderContext context,
        Size chromeSize,
        BrushChromeRenderInfo chrome);

    protected sealed record BrushChromeRenderInfo(
        string? PrimaryBrushId,
        bool IsDisabled,
        IReadOnlyList<string?> DebugBrushIds);
}
