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
            var margin = child.Margin;
            if (IsStretchOnPrimaryAxis(child, orientation))
            {
                var stretchRatio = GetPrimaryStretchRatio(child, orientation);
                stretchChildren.Add(new StretchChildInfo(child, stretchRatio));
                totalStretchRatio += stretchRatio;
                continue;
            }

            child.Measure(CreateChildMeasureSize(availableSize, orientation, double.NaN, margin, IsStretchOnSecondaryAxis(child, orientation)));
            occupiedPrimary += GetPrimarySize(child.DesiredSize, orientation) + GetPrimaryMargin(margin, orientation);
            occupiedSecondary = Math.Max(occupiedSecondary, GetSecondarySize(child.DesiredSize, orientation) + GetSecondaryMargin(margin, orientation));
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
                allocatedPrimary,
                stretchChild.Child.Margin,
                IsStretchOnSecondaryAxis(stretchChild.Child, orientation)));

            var measuredPrimary = GetPrimarySize(stretchChild.Child.DesiredSize, orientation) + GetPrimaryMargin(stretchChild.Child.Margin, orientation);
            occupiedPrimary += measuredPrimary;
            occupiedSecondary = Math.Max(
                occupiedSecondary,
                GetSecondarySize(stretchChild.Child.DesiredSize, orientation) + GetSecondaryMargin(stretchChild.Child.Margin, orientation));

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

        var occupiedPrimary = visibleChildren.Sum(child => GetPrimarySize(child.DesiredSize, orientation) + GetPrimaryMargin(child.Margin, orientation));
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
            var margin = child.Margin;
            var desiredPrimary = GetPrimarySize(child.DesiredSize, orientation);
            var primarySpan = desiredPrimary + GetPrimaryMargin(margin, orientation);
            var primaryStart = reverse ? currentPrimary - primarySpan + GetPrimaryMarginStart(margin, orientation) : currentPrimary + GetPrimaryMarginStart(margin, orientation);

            var secondaryAvailable = Math.Max(0d, GetSecondarySize(finalSize, orientation) - GetSecondaryMargin(margin, orientation));
            var stretchSecondary = IsStretchOnSecondaryAxis(child, orientation);
            var arrangedSecondary = stretchSecondary
                ? secondaryAvailable
                : Math.Min(GetSecondarySize(child.DesiredSize, orientation), secondaryAvailable);
            var secondaryStart = GetSecondaryMarginStart(margin, orientation) + ResolveSecondaryOffset(child, orientation, secondaryAvailable, arrangedSecondary);

            child.Arrange(CreateRect(orientation, primaryStart, secondaryStart, Math.Max(0d, desiredPrimary), Math.Max(0d, arrangedSecondary)));

            if (reverse)
            {
                currentPrimary = spaced ? primaryStart - GetPrimaryMarginEnd(margin, orientation) - spacing : primaryStart - GetPrimaryMarginEnd(margin, orientation);
            }
            else
            {
                currentPrimary = spaced ? primaryStart + desiredPrimary + GetPrimaryMarginEnd(margin, orientation) + spacing : primaryStart + desiredPrimary + GetPrimaryMarginEnd(margin, orientation);
            }
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

    private static bool IsStretchOnSecondaryAxis(FrameworkElement child, Orientation orientation)
    {
        return orientation == Orientation.Horizontal
            ? GauntletLayoutProperties.GetHeightSizePolicy(child) == "StretchToParent"
            : GauntletLayoutProperties.GetWidthSizePolicy(child) == "StretchToParent";
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
        double primaryAllocation,
        Thickness margin,
        bool stretchSecondary)
    {
        var availablePrimary = GetPrimarySize(availableSize, orientation);
        var availableSecondary = GetSecondarySize(availableSize, orientation);
        var primary = double.IsNaN(primaryAllocation)
            ? availablePrimary
            : primaryAllocation;
        if (!double.IsInfinity(primary))
        {
            primary = Math.Max(0d, primary - GetPrimaryMargin(margin, orientation));
        }

        var secondary = availableSecondary;
        if (!double.IsInfinity(secondary) && stretchSecondary)
        {
            secondary = Math.Max(0d, secondary - GetSecondaryMargin(margin, orientation));
        }

        return CreateSize(orientation, primary, secondary);
    }

    private static double ResolveSecondaryOffset(
        FrameworkElement child,
        Orientation orientation,
        double secondaryAvailable,
        double arrangedSecondary)
    {
        var remaining = Math.Max(0d, secondaryAvailable - arrangedSecondary);
        return orientation == Orientation.Horizontal
            ? child.VerticalAlignment switch
            {
                VerticalAlignment.Bottom => remaining,
                VerticalAlignment.Center => remaining / 2d,
                _ => 0d
            }
            : child.HorizontalAlignment switch
            {
                HorizontalAlignment.Right => remaining,
                HorizontalAlignment.Center => remaining / 2d,
                _ => 0d
            };
    }

    private static double GetPrimarySize(Size size, Orientation orientation) => orientation == Orientation.Horizontal ? size.Width : size.Height;

    private static double GetSecondarySize(Size size, Orientation orientation) => orientation == Orientation.Horizontal ? size.Height : size.Width;

    private static Size CreateSize(Orientation orientation, double primary, double secondary)
    {
        return orientation == Orientation.Horizontal
            ? new Size(primary, secondary)
            : new Size(secondary, primary);
    }

    private static Rect CreateRect(Orientation orientation, double primaryStart, double secondaryStart, double primarySize, double secondarySize)
    {
        return orientation == Orientation.Horizontal
            ? new Rect(primaryStart, secondaryStart, primarySize, secondarySize)
            : new Rect(secondaryStart, primaryStart, secondarySize, primarySize);
    }

    private static double GetPrimaryMargin(Thickness margin, Orientation orientation) => orientation == Orientation.Horizontal ? margin.Left + margin.Right : margin.Top + margin.Bottom;

    private static double GetSecondaryMargin(Thickness margin, Orientation orientation) => orientation == Orientation.Horizontal ? margin.Top + margin.Bottom : margin.Left + margin.Right;

    private static double GetPrimaryMarginStart(Thickness margin, Orientation orientation) => orientation == Orientation.Horizontal ? margin.Left : margin.Top;

    private static double GetPrimaryMarginEnd(Thickness margin, Orientation orientation) => orientation == Orientation.Horizontal ? margin.Right : margin.Bottom;

    private static double GetSecondaryMarginStart(Thickness margin, Orientation orientation) => orientation == Orientation.Horizontal ? margin.Top : margin.Left;

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
