using System.Windows;

namespace Bannerlord.XmlPreview.Rendering;

public static class PreviewElementDebugProperties
{
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
}
