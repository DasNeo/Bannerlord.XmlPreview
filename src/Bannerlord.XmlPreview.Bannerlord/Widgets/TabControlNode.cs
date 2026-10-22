using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class TabControlNode : PrefabNode
{
    public TabControlNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base("TabControl", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var border = new Border
        {
            Background = Brushes.Transparent
        };

        var childCanvas = new Canvas();
        border.Child = childCanvas;

        context.ApplySizing(border, Attributes, parentSize);
        context.ApplyOpacity(border, Attributes, childCanvas);
        context.ApplyVisibility(border, Attributes);
        context.ApplyToolTip(border, Name, Attributes);
        context.AttachElementDebugMetadata(border, Name, Attributes);

        var measureInput = context.ResolveChildMeasureSize(border, Attributes, parentSize);
        var containerSize = context.Measure(border, Attributes, parentSize);

        Size RenderContent(Size? overrideContainerSize = null)
        {
            childCanvas.Children.Clear();

            if (TryGetSelectedChild(context, out var selectedChild))
            {
                var contentHostSize = overrideContainerSize ?? context.ResolveElementRenderSize(border, measureInput);
                context.AttachChildren(childCanvas, [selectedChild], contentHostSize);
            }

            var contentBounds = context.MeasureCanvasChildrenBounds(childCanvas);
            context.ApplyCanvasLayout(childCanvas, contentBounds);
            return contentBounds.IsEmpty ? new Size(0, 0) : contentBounds.Size;
        }

        var initialSize = new Size(
            containerSize.Width > 0 ? containerSize.Width : measureInput.Width,
            containerSize.Height > 0 ? containerSize.Height : measureInput.Height);
        var contentSize = RenderContent(initialSize);
        containerSize = context.FinalizeCoverChildrenSize(
            border,
            Attributes,
            contentSize,
            () => RenderContent());

        return new RenderedPrefabNode(border, childCanvas);
    }

    private bool TryGetSelectedChild(PrefabRenderContext context, out PrefabNode selectedChild)
    {
        if (Children.Count == 0)
        {
            selectedChild = null!;
            return false;
        }

        var selectedIndex = (int)Math.Round(context.ResolveDouble(Attributes, "SelectedIndex", 0));
        selectedIndex = Math.Clamp(selectedIndex, 0, Children.Count - 1);
        selectedChild = Children[selectedIndex];
        return true;
    }
}
