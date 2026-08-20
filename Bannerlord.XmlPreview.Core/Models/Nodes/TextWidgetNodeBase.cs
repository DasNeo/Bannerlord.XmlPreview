using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public abstract class TextWidgetNodeBase : PrefabNode
{
    protected TextWidgetNodeBase(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    protected abstract double FallbackFontSize { get; }

    public sealed override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var brushId = context.ResolveBinding(GetAttribute("Brush"));
        var isDisabled = context.IsDisabled(Attributes);
        var host = new Grid
        {
            ClipToBounds = false
        };
        var text = new TextBlock
        {
            Text = context.ResolveBinding(GetAttribute("Text")),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = context.ResolveTextHorizontalAlignment(brushId, Attributes),
            VerticalAlignment = context.ResolveTextVerticalAlignment(brushId, Attributes)
        };

        var centerHorizontally = context.UsesCoverChildren(Attributes, "WidthSizePolicy") &&
            string.Equals(GetAttribute("HorizontalAlignment"), "Center", StringComparison.OrdinalIgnoreCase) &&
            parentSize.Width > 0;
        var centerVertically = context.UsesCoverChildren(Attributes, "HeightSizePolicy") &&
            string.Equals(GetAttribute("VerticalAlignment"), "Center", StringComparison.OrdinalIgnoreCase) &&
            parentSize.Height > 0;

        if (centerHorizontally)
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
        }

        if (centerVertically)
        {
            text.VerticalAlignment = VerticalAlignment.Center;
        }

        context.ApplyTextStyle(text, brushId, Attributes, FallbackFontSize);
        var visualOffset = context.ApplyVisualTextCentering(text, parentSize.Width);
        text.Measure(parentSize);
        var textSize = text.DesiredSize;

        context.ApplySizing(host, Attributes, parentSize);

        context.ApplyOpacity(host, Attributes);
        context.ApplyVisibility(host, Attributes);
        context.ApplyToolTip(host, Name, Attributes);
        context.AttachElementDebugMetadata(host, Name, Attributes, null, [brushId], isDisabled);
        host.Children.Add(text);

        if (centerHorizontally || centerVertically)
        {
            var hostWidth = host.Width > 0 ? host.Width : textSize.Width;
            var hostHeight = host.Height > 0 ? host.Height : textSize.Height;
            var selectionWidth = Math.Min(textSize.Width, hostWidth);
            var selectionHeight = Math.Min(textSize.Height, hostHeight);
            var selectionX = centerHorizontally && hostWidth > selectionWidth
                ? (hostWidth - selectionWidth) / 2d
                : 0d;
            var selectionY = centerVertically && hostHeight > selectionHeight
                ? (hostHeight - selectionHeight) / 2d
                : 0d;
            selectionY += visualOffset;
            PreviewElementDebugProperties.SetSelectionBounds(host, new Rect(selectionX, selectionY, selectionWidth, selectionHeight));
        }

        return new RenderedPrefabNode(
            host,
            null,
            null,
            centerHorizontally,
            centerVertically);
    }
}
