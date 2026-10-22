using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Rendering;

public sealed class GauntletStackLayoutPanel : Panel
{
    public static readonly DependencyProperty LayoutMethodProperty =
        DependencyProperty.Register(
            nameof(LayoutMethod),
            typeof(GauntletStackLayoutMethod),
            typeof(GauntletStackLayoutPanel),
            new FrameworkPropertyMetadata(
                GauntletStackLayoutMethod.HorizontalLeftToRight,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public GauntletStackLayoutMethod LayoutMethod
    {
        get => (GauntletStackLayoutMethod)GetValue(LayoutMethodProperty);
        set => SetValue(LayoutMethodProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleChildren = InternalChildren.OfType<UIElement>()
            .Where(static child => child.Visibility != Visibility.Collapsed)
            .Cast<FrameworkElement>()
            .ToArray();

        var orientation = GetOrientation(LayoutMethod);
        var occupiedPrimary = 0d;
        var occupiedSecondary = 0d;
        var stretchChildren = new List<StretchChildInfo>();
        var totalStretchRatio = 0d;

        foreach (var child in visibleChildren)
        {
            if (IsStretchOnPrimaryAxis(child, orientation))
            {
                var stretchRatio = GetPrimaryStretchRatio(child, orientation);
                stretchChildren.Add(new StretchChildInfo(child, stretchRatio));
                totalStretchRatio += stretchRatio;
                continue;
            }

            child.Measure(CreateChildMeasureSize(availableSize, orientation, double.NaN));
            occupiedPrimary += GetPrimarySize(child.DesiredSize, orientation);
            occupiedSecondary = Math.Max(occupiedSecondary, GetSecondarySize(child.DesiredSize, orientation));
        }

        var availablePrimary = GetPrimarySize(availableSize, orientation);
        var remainingPrimary = double.IsInfinity(availablePrimary)
            ? double.PositiveInfinity
            : availablePrimary - occupiedPrimary;
        if (double.IsNaN(remainingPrimary))
        {
            remainingPrimary = 0d;
        }

        for (var index = 0; index < stretchChildren.Count; index++)
        {
            var stretchChild = stretchChildren[index];
            var remainingRatio = Math.Max(0d, totalStretchRatio);
            var allocatedPrimary = double.IsInfinity(remainingPrimary)
                ? double.PositiveInfinity
                : index == stretchChildren.Count - 1
                    ? remainingPrimary
                    : remainingRatio > 0
                        ? remainingPrimary * stretchChild.PrimaryStretchRatio / remainingRatio
                    : remainingPrimary;

            stretchChild.Child.Measure(CreateChildMeasureSize(
                availableSize,
                orientation,
                allocatedPrimary));

            var measuredPrimary = GetPrimarySize(stretchChild.Child.DesiredSize, orientation);
            occupiedPrimary += measuredPrimary;
            occupiedSecondary = Math.Max(
                occupiedSecondary,
                GetSecondarySize(stretchChild.Child.DesiredSize, orientation));

            if (!double.IsInfinity(remainingPrimary))
            {
                remainingPrimary -= measuredPrimary;
            }

            totalStretchRatio = Math.Max(0d, totalStretchRatio - stretchChild.PrimaryStretchRatio);
        }

        return CreateSize(orientation, occupiedPrimary, occupiedSecondary);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildren = InternalChildren.OfType<UIElement>()
            .Where(static child => child.Visibility != Visibility.Collapsed)
            .Cast<FrameworkElement>()
            .ToArray();
        if (visibleChildren.Length == 0)
        {
            return finalSize;
        }

        var orientation = GetOrientation(LayoutMethod);
        var reverse = IsReverse(LayoutMethod);
        var spaced = IsSpaced(LayoutMethod);
        var centered = IsCentered(LayoutMethod);

        var occupiedPrimary = visibleChildren.Sum(child => GetPrimarySize(child.DesiredSize, orientation));
        var availablePrimary = GetPrimarySize(finalSize, orientation);
        var spacing = spaced && visibleChildren.Length > 1
            ? (availablePrimary - occupiedPrimary) / (visibleChildren.Length - 1)
            : 0d;

        var startPrimary = 0d;
        if (centered || spaced && visibleChildren.Length == 1)
        {
            startPrimary = (availablePrimary - occupiedPrimary) / 2d;
        }

        var currentPrimary = reverse ? availablePrimary - startPrimary : startPrimary;
        foreach (var child in visibleChildren)
        {
            // WPF DesiredSize and Arrange slots already include Margin and alignment.
            var primarySpan = GetPrimarySize(child.DesiredSize, orientation);
            var primaryStart = reverse ? currentPrimary - primarySpan : currentPrimary;
            child.Arrange(CreateRect(orientation, primaryStart, 0, Math.Max(0, primarySpan),
                Math.Max(0, GetSecondarySize(finalSize, orientation))));
            currentPrimary += reverse ? -primarySpan - spacing : primarySpan + spacing;
        }

        return finalSize;
    }

    public static GauntletStackLayoutMethod Parse(string? rawValue)
    {
        return Enum.TryParse<GauntletStackLayoutMethod>(rawValue, true, out var layoutMethod)
            ? layoutMethod
            : GauntletStackLayoutMethod.HorizontalLeftToRight;
    }

    private static Orientation GetOrientation(GauntletStackLayoutMethod layoutMethod)
    {
        return layoutMethod switch
        {
            GauntletStackLayoutMethod.HorizontalLeftToRight or
            GauntletStackLayoutMethod.HorizontalRightToLeft or
            GauntletStackLayoutMethod.HorizontalCentered or
            GauntletStackLayoutMethod.HorizontalSpaced => Orientation.Horizontal,
            _ => Orientation.Vertical
        };
    }

    private static bool IsReverse(GauntletStackLayoutMethod layoutMethod)
    {
        return layoutMethod is GauntletStackLayoutMethod.HorizontalRightToLeft or GauntletStackLayoutMethod.VerticalBottomToTop;
    }

    private static bool IsCentered(GauntletStackLayoutMethod layoutMethod)
    {
        return layoutMethod is GauntletStackLayoutMethod.HorizontalCentered or GauntletStackLayoutMethod.VerticalCentered;
    }

    private static bool IsSpaced(GauntletStackLayoutMethod layoutMethod)
    {
        return layoutMethod is GauntletStackLayoutMethod.HorizontalSpaced or GauntletStackLayoutMethod.VerticalSpaced;
    }

    private static bool IsStretchOnPrimaryAxis(FrameworkElement child, Orientation orientation)
    {
        return orientation == Orientation.Horizontal
            ? GauntletLayoutProperties.GetWidthSizePolicy(child) == "StretchToParent"
            : GauntletLayoutProperties.GetHeightSizePolicy(child) == "StretchToParent";
    }

    private static double GetPrimaryStretchRatio(FrameworkElement child, Orientation orientation)
    {
        var ratio = orientation == Orientation.Horizontal
            ? GauntletLayoutProperties.GetWidthStretchRatio(child)
            : GauntletLayoutProperties.GetHeightStretchRatio(child);
        return ratio > 0 ? ratio : 1d;
    }

    private static Size CreateChildMeasureSize(
        Size availableSize,
        Orientation orientation,
        double primaryAllocation)
    {
        var availablePrimary = GetPrimarySize(availableSize, orientation);
        var availableSecondary = GetSecondarySize(availableSize, orientation);
        var primary = double.IsNaN(primaryAllocation)
            ? availablePrimary
            : primaryAllocation;
        primary = Math.Max(0, primary);
        var secondary = availableSecondary;

        return CreateSize(orientation, primary, secondary);
    }

    private static double GetPrimarySize(Size size, Orientation orientation) => orientation == Orientation.Horizontal ? size.Width : size.Height;
    private static double GetSecondarySize(Size size, Orientation orientation) => orientation == Orientation.Horizontal ? size.Height : size.Width;
    private static Size CreateSize(Orientation orientation, double primary, double secondary) => orientation == Orientation.Horizontal ? new Size(primary, secondary) : new Size(secondary, primary);
    private static Rect CreateRect(Orientation orientation, double primaryStart, double secondaryStart, double primarySize, double secondarySize) => orientation == Orientation.Horizontal
        ? new Rect(primaryStart, secondaryStart, primarySize, secondarySize)
        : new Rect(secondaryStart, primaryStart, secondarySize, primarySize);

    private sealed record StretchChildInfo(FrameworkElement Child, double PrimaryStretchRatio);
}

public enum GauntletStackLayoutMethod
{
    HorizontalLeftToRight,
    HorizontalRightToLeft,
    HorizontalCentered,
    HorizontalSpaced,
    VerticalTopToBottom,
    VerticalBottomToTop,
    VerticalCentered,
    VerticalSpaced
}
