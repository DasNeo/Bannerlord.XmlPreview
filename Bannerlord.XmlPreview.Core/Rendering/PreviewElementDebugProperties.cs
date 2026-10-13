using System.Windows;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Rendering;

public static class PreviewElementDebugProperties
{
    public static readonly DependencyProperty ImageResolutionProperty = DependencyProperty.RegisterAttached(
        "ImageResolution", typeof(Bannerlord.XmlPreview.Models.Assets.ImageResolution), typeof(PreviewElementDebugProperties), new PropertyMetadata(null));
    public static Bannerlord.XmlPreview.Models.Assets.ImageResolution? GetImageResolution(DependencyObject element) =>
        (Bannerlord.XmlPreview.Models.Assets.ImageResolution?)element.GetValue(ImageResolutionProperty);
    public static void SetImageResolution(DependencyObject element, Bannerlord.XmlPreview.Models.Assets.ImageResolution value) => element.SetValue(ImageResolutionProperty, value);
    public static readonly DependencyProperty MaskResolutionProperty = DependencyProperty.RegisterAttached(
        "MaskResolution", typeof(Bannerlord.XmlPreview.Models.Assets.ImageResolution), typeof(PreviewElementDebugProperties), new PropertyMetadata(null));
    public static Bannerlord.XmlPreview.Models.Assets.ImageResolution? GetMaskResolution(DependencyObject element) =>
        (Bannerlord.XmlPreview.Models.Assets.ImageResolution?)element.GetValue(MaskResolutionProperty);
    public static void SetMaskResolution(DependencyObject element, Bannerlord.XmlPreview.Models.Assets.ImageResolution value) => element.SetValue(MaskResolutionProperty, value);
    public static readonly DependencyProperty DebugInfoProperty =
        DependencyProperty.RegisterAttached(
            "DebugInfo",
            typeof(ElementDebugInfo),
            typeof(PreviewElementDebugProperties),
            new PropertyMetadata(null));

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

    public static ElementDebugInfo? GetDebugInfo(DependencyObject element)
    {
        return (ElementDebugInfo?)element.GetValue(DebugInfoProperty);
    }

    public static void SetDebugInfo(DependencyObject element, ElementDebugInfo? value)
    {
        element.SetValue(DebugInfoProperty, value);
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
