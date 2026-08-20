using Bannerlord.XmlPreview.DemoData;
using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public abstract class ContainerPrefabNode : PrefabNode
{
    private readonly bool _frameBrush;

    protected ContainerPrefabNode(string name, bool frameBrush, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
        _frameBrush = frameBrush;
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var spriteId = context.ResolveBinding(GetAttribute("Sprite"));
        var brushId = context.ResolveBinding(GetAttribute("Brush"));
        var backgroundColor = PrefabRenderContext.ParseBannerlordColor(GetAttribute("Color"));
        var isDisabled = context.IsDisabled(Attributes);
        var sprite = context.CreateSpriteOverlay(spriteId);
        var brush = (_frameBrush
            ? context.ResolvePanelBrush(brushId, isDisabled)
            : context.ResolveWidgetBackground(spriteId, brushId, isDisabled));
        var hasMissingReference = context.IsSpriteMissing(spriteId) ||
                                  (!string.IsNullOrWhiteSpace(brushId) && !context.BrushExists(brushId));
        var borderColor = hasMissingReference
            ? new SolidColorBrush(Colors.Red)
            : _frameBrush
                ? context.CreateSolidBrush(PreviewDemoCatalog.FrameBorderColor)
                : Brushes.Transparent;
        
        var border = new Border
        {
            Background = backgroundColor is null
                ? _frameBrush
                    ? Brushes.Transparent
                    : brush
                : new SolidColorBrush(backgroundColor.Value),
            BorderBrush = borderColor,
            BorderThickness = hasMissingReference ? new Thickness(1) : _frameBrush ? new Thickness(2) : new Thickness(0),
            CornerRadius = _frameBrush ? new CornerRadius(6) : new CornerRadius(2)
        };

        var layout = new Grid();

        if (!string.IsNullOrWhiteSpace(spriteId) && sprite is not null)
            layout.Children.Add(sprite);

        var childCanvas = new Canvas();
        layout.Children.Add(childCanvas);

        border.Child = layout;

        context.ApplySizing(border, Attributes, parentSize);
        context.ApplyAssetSizing(border, spriteId, brushId, isDisabled);
        context.ApplyBrushBorderStyle(border, brushId, isDisabled, _frameBrush);
        context.ApplyOpacity(border, Attributes);
        context.ApplyVisibility(border, Attributes);
        context.ApplyToolTip(border, Name, Attributes);
        context.AttachElementDebugMetadata(border, Name, Attributes, [spriteId], [brushId], isDisabled);

        var measureInput = context.ResolveChildMeasureSize(border, Attributes, parentSize);
        var containerSize = context.Measure(border, Attributes, parentSize);
        Rect childLayoutRect = Rect.Empty;
        Rect selectionRect = Rect.Empty;

        Size RenderContent(Size? overrideContainerSize = null)
        {
            childCanvas.Children.Clear();

            var baseContainerSize = overrideContainerSize ?? context.ResolveElementRenderSize(border, measureInput);
            childLayoutRect = !string.IsNullOrWhiteSpace(spriteId)
                ? context.ResolveSpriteContentLayoutRect(spriteId, baseContainerSize)
                : new Rect(new Point(0, 0), baseContainerSize);
            selectionRect = !string.IsNullOrWhiteSpace(spriteId)
                ? context.ResolveSpriteSelectionRect(spriteId, baseContainerSize)
                : childLayoutRect;
            context.AttachChildren(childCanvas, Children, childLayoutRect.Size, childLayoutRect.Location);

            var contentBounds = context.MeasureCanvasChildrenBounds(childCanvas);
            context.ApplyCanvasLayout(childCanvas, contentBounds);
            return context.TryMeasureReferencedElement(Attributes, "InnerPanel", childLayoutRect.Size, out var innerPanelSize)
                ? innerPanelSize
                : contentBounds.IsEmpty ? new Size(0, 0) : contentBounds.Size;
        }

        var contentSize = RenderContent(new Size(
            containerSize.Width > 0 ? containerSize.Width : measureInput.Width,
            containerSize.Height > 0 ? containerSize.Height : measureInput.Height));
        containerSize = context.FinalizeCoverChildrenSize(
            border,
            Attributes,
            contentSize,
            () => RenderContent());

        PreviewElementDebugProperties.SetSelectionBounds(border, selectionRect);

        if (_frameBrush && !string.IsNullOrWhiteSpace(brushId))
        {
            var overlayWidth = double.IsNaN(border.Width) || border.Width <= 0 ? containerSize.Width : border.Width;
            var overlayHeight = double.IsNaN(border.Height) || border.Height <= 0 ? containerSize.Height : border.Height;
            context.AddBrushOverlay(layout, brushId, isDisabled, overlayWidth, overlayHeight, 0);
        }

        return new RenderedPrefabNode(border, childCanvas, selectionRect);
    }
}
