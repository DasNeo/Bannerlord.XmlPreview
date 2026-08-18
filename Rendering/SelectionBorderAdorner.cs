using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Rendering;

public sealed class SelectionBorderAdorner : Adorner
{
    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(224, 0, 191, 255));
    private static readonly Pen SelectionPen = CreateSelectionPen();

    public SelectionBorderAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var rect = PreviewElementDebugProperties.GetSelectionBounds(AdornedElement);
        if (rect == Rect.Empty)
        {
            rect = new Rect(AdornedElement.RenderSize);
        }

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var inset = SelectionPen.Thickness / 2d;
        rect = new Rect(
            rect.X + inset,
            rect.Y + inset,
            Math.Max(0, rect.Width - SelectionPen.Thickness),
            Math.Max(0, rect.Height - SelectionPen.Thickness));

        drawingContext.DrawRectangle(null, SelectionPen, rect);
    }

    private static Pen CreateSelectionPen()
    {
        var pen = new Pen(SelectionBrush, 2);
        pen.Freeze();
        return pen;
    }
}
