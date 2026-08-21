using System.Windows;

namespace Bannerlord.XmlPreview.Rendering;

public static class PreviewElementDebugProperties
{
    public static readonly DependencyProperty DebugBaseTextProperty =
        DependencyProperty.RegisterAttached(
            "DebugBaseText",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DebugTextProperty =
        DependencyProperty.RegisterAttached(
            "DebugText",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TreeLabelProperty =
        DependencyProperty.RegisterAttached(
            "TreeLabel",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectionBoundsProperty =
        DependencyProperty.RegisterAttached(
            "SelectionBounds",
            typeof(Rect),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(Rect.Empty));

    public static readonly DependencyProperty LayoutParentSizeProperty =
        DependencyProperty.RegisterAttached(
            "LayoutParentSize",
            typeof(Size),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(Size.Empty));

    public static readonly DependencyProperty LayoutMeasuredSizeProperty =
        DependencyProperty.RegisterAttached(
            "LayoutMeasuredSize",
            typeof(Size),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(Size.Empty));

    public static readonly DependencyProperty LayoutAlignmentBoundsProperty =
        DependencyProperty.RegisterAttached(
            "LayoutAlignmentBounds",
            typeof(Rect),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(Rect.Empty));

    public static readonly DependencyProperty LayoutComputedPositionProperty =
        DependencyProperty.RegisterAttached(
            "LayoutComputedPosition",
            typeof(Point),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(new Point(double.NaN, double.NaN)));

    public static readonly DependencyProperty LayoutDebugTextProperty =
        DependencyProperty.RegisterAttached(
            "LayoutDebugText",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PreviewStateProperty =
        DependencyProperty.RegisterAttached(
            "PreviewState",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PreviewBaseStateProperty =
        DependencyProperty.RegisterAttached(
            "PreviewBaseState",
            typeof(string),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(string.Empty));

    public static string GetDebugBaseText(DependencyObject element)
    {
        return (string)element.GetValue(DebugBaseTextProperty);
    }

    public static void SetDebugBaseText(DependencyObject element, string value)
    {
        element.SetValue(DebugBaseTextProperty, value);
    }

    public static string GetDebugText(DependencyObject element)
    {
        return (string)element.GetValue(DebugTextProperty);
    }

    public static void SetDebugText(DependencyObject element, string value)
    {
        element.SetValue(DebugTextProperty, value);
    }

    public static string GetTreeLabel(DependencyObject element)
    {
        return (string)element.GetValue(TreeLabelProperty);
    }

    public static void SetTreeLabel(DependencyObject element, string value)
    {
        element.SetValue(TreeLabelProperty, value);
    }

    public static Rect GetSelectionBounds(DependencyObject element)
    {
        return (Rect)element.GetValue(SelectionBoundsProperty);
    }

    public static void SetSelectionBounds(DependencyObject element, Rect value)
    {
        element.SetValue(SelectionBoundsProperty, value);
    }

    public static Size GetLayoutParentSize(DependencyObject element)
    {
        return (Size)element.GetValue(LayoutParentSizeProperty);
    }

    public static void SetLayoutParentSize(DependencyObject element, Size value)
    {
        element.SetValue(LayoutParentSizeProperty, value);
    }

    public static Size GetLayoutMeasuredSize(DependencyObject element)
    {
        return (Size)element.GetValue(LayoutMeasuredSizeProperty);
    }

    public static void SetLayoutMeasuredSize(DependencyObject element, Size value)
    {
        element.SetValue(LayoutMeasuredSizeProperty, value);
    }

    public static Rect GetLayoutAlignmentBounds(DependencyObject element)
    {
        return (Rect)element.GetValue(LayoutAlignmentBoundsProperty);
    }

    public static void SetLayoutAlignmentBounds(DependencyObject element, Rect value)
    {
        element.SetValue(LayoutAlignmentBoundsProperty, value);
    }

    public static Point GetLayoutComputedPosition(DependencyObject element)
    {
        return (Point)element.GetValue(LayoutComputedPositionProperty);
    }

    public static void SetLayoutComputedPosition(DependencyObject element, Point value)
    {
        element.SetValue(LayoutComputedPositionProperty, value);
    }

    public static string GetLayoutDebugText(DependencyObject element)
    {
        return (string)element.GetValue(LayoutDebugTextProperty);
    }

    public static void SetLayoutDebugText(DependencyObject element, string value)
    {
        element.SetValue(LayoutDebugTextProperty, value);
    }

    public static string GetPreviewState(DependencyObject element)
    {
        return (string)element.GetValue(PreviewStateProperty);
    }

    public static void SetPreviewState(DependencyObject element, string value)
    {
        element.SetValue(PreviewStateProperty, value);
    }

    public static string GetPreviewBaseState(DependencyObject element)
    {
        return (string)element.GetValue(PreviewBaseStateProperty);
    }

    public static void SetPreviewBaseState(DependencyObject element, string value)
    {
        element.SetValue(PreviewBaseStateProperty, value);
    }
}
