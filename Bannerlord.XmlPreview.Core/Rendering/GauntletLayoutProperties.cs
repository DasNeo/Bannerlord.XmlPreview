using System.Windows;

namespace Bannerlord.XmlPreview.Rendering;

public static class GauntletLayoutProperties
{
    public static readonly DependencyProperty WidthSizePolicyProperty =
        DependencyProperty.RegisterAttached(
            "WidthSizePolicy",
            typeof(string),
            typeof(GauntletLayoutProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeightSizePolicyProperty =
        DependencyProperty.RegisterAttached(
            "HeightSizePolicy",
            typeof(string),
            typeof(GauntletLayoutProperties),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty WidthStretchRatioProperty =
        DependencyProperty.RegisterAttached(
            "WidthStretchRatio",
            typeof(double),
            typeof(GauntletLayoutProperties),
            new PropertyMetadata(1d));

    public static readonly DependencyProperty HeightStretchRatioProperty =
        DependencyProperty.RegisterAttached(
            "HeightStretchRatio",
            typeof(double),
            typeof(GauntletLayoutProperties),
            new PropertyMetadata(1d));

    public static string GetWidthSizePolicy(DependencyObject element)
    {
        return (string)element.GetValue(WidthSizePolicyProperty);
    }

    public static void SetWidthSizePolicy(DependencyObject element, string value)
    {
        element.SetValue(WidthSizePolicyProperty, value);
    }

    public static string GetHeightSizePolicy(DependencyObject element)
    {
        return (string)element.GetValue(HeightSizePolicyProperty);
    }

    public static void SetHeightSizePolicy(DependencyObject element, string value)
    {
        element.SetValue(HeightSizePolicyProperty, value);
    }

    public static double GetWidthStretchRatio(DependencyObject element)
    {
        return (double)element.GetValue(WidthStretchRatioProperty);
    }

    public static void SetWidthStretchRatio(DependencyObject element, double value)
    {
        element.SetValue(WidthStretchRatioProperty, value);
    }

    public static double GetHeightStretchRatio(DependencyObject element)
    {
        return (double)element.GetValue(HeightStretchRatioProperty);
    }

    public static void SetHeightStretchRatio(DependencyObject element, double value)
    {
        element.SetValue(HeightStretchRatioProperty, value);
    }
}
