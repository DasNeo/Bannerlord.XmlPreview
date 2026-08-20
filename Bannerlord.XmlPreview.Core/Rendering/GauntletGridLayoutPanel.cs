using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Rendering;

public sealed class GauntletGridLayoutPanel : Panel
{
    public static readonly DependencyProperty RowCountProperty =
        DependencyProperty.Register(
            nameof(RowCount),
            typeof(int),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ColumnCountProperty =
        DependencyProperty.Register(
            nameof(ColumnCount),
            typeof(int),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty UseDynamicCellWidthProperty =
        DependencyProperty.Register(
            nameof(UseDynamicCellWidth),
            typeof(bool),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty UseDynamicCellHeightProperty =
        DependencyProperty.Register(
            nameof(UseDynamicCellHeight),
            typeof(bool),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty DefaultCellWidthProperty =
        DependencyProperty.Register(
            nameof(DefaultCellWidth),
            typeof(double),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty DefaultCellHeightProperty =
        DependencyProperty.Register(
            nameof(DefaultCellHeight),
            typeof(double),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(
            nameof(Direction),
            typeof(GauntletGridDirection),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                GauntletGridDirection.RowFirst,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty HorizontalLayoutMethodProperty =
        DependencyProperty.Register(
            nameof(HorizontalLayoutMethod),
            typeof(GauntletGridHorizontalLayoutMethod),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                GauntletGridHorizontalLayoutMethod.LeftToRight,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty VerticalLayoutMethodProperty =
        DependencyProperty.Register(
            nameof(VerticalLayoutMethod),
            typeof(GauntletGridVerticalLayoutMethod),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                GauntletGridVerticalLayoutMethod.TopToBottom,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty WidthSizePolicyProperty =
        DependencyProperty.Register(
            nameof(WidthSizePolicy),
            typeof(string),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty HeightSizePolicyProperty =
        DependencyProperty.Register(
            nameof(HeightSizePolicy),
            typeof(string),
            typeof(GauntletGridLayoutPanel),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public int RowCount
    {
        get => (int)GetValue(RowCountProperty);
        set => SetValue(RowCountProperty, value);
    }

    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    public bool UseDynamicCellWidth
    {
        get => (bool)GetValue(UseDynamicCellWidthProperty);
        set => SetValue(UseDynamicCellWidthProperty, value);
    }

    public bool UseDynamicCellHeight
    {
        get => (bool)GetValue(UseDynamicCellHeightProperty);
        set => SetValue(UseDynamicCellHeightProperty, value);
    }

    public double DefaultCellWidth
    {
        get => (double)GetValue(DefaultCellWidthProperty);
        set => SetValue(DefaultCellWidthProperty, value);
    }

    public double DefaultCellHeight
    {
        get => (double)GetValue(DefaultCellHeightProperty);
        set => SetValue(DefaultCellHeightProperty, value);
    }

    public GauntletGridDirection Direction
    {
        get => (GauntletGridDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public GauntletGridHorizontalLayoutMethod HorizontalLayoutMethod
    {
        get => (GauntletGridHorizontalLayoutMethod)GetValue(HorizontalLayoutMethodProperty);
        set => SetValue(HorizontalLayoutMethodProperty, value);
    }

    public GauntletGridVerticalLayoutMethod VerticalLayoutMethod
    {
        get => (GauntletGridVerticalLayoutMethod)GetValue(VerticalLayoutMethodProperty);
        set => SetValue(VerticalLayoutMethodProperty, value);
    }

    public string WidthSizePolicy
    {
        get => (string)GetValue(WidthSizePolicyProperty);
        set => SetValue(WidthSizePolicyProperty, value);
    }

    public string HeightSizePolicy
    {
        get => (string)GetValue(HeightSizePolicyProperty);
        set => SetValue(HeightSizePolicyProperty, value);
    }

    public IReadOnlyList<double> RowHeights { get; private set; } = Array.Empty<double>();

    public IReadOnlyList<double> ColumnWidths { get; private set; } = Array.Empty<double>();

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleChildren = GetVisibleChildren();
        if (visibleChildren.Length == 0)
        {
            RowHeights = Array.Empty<double>();
            ColumnWidths = Array.Empty<double>();
            return new Size(0, 0);
        }

        PreMeasureCoverChildren(visibleChildren);
        CalculateRowColumnCounts(
            visibleChildren.Length,
            out var rowCount,
            out var usedRowCount,
            out var columnCount,
            out var usedColumnCount);
        UpdateCellSizes(
            visibleChildren,
            rowCount,
            usedRowCount,
            columnCount,
            usedColumnCount,
            availableSize.Width,
            availableSize.Height);

        var visibleIndex = 0;
        foreach (var child in visibleChildren)
        {
            CalculateRowColumnIndices(visibleIndex, rowCount, columnCount, out var row, out var column);
            var cellWidth = GetElement(ColumnWidths, column);
            var cellHeight = GetElement(RowHeights, row);
            child.Measure(CreateChildMeasureSize(child, cellWidth, cellHeight));
            visibleIndex++;
        }

        return new Size(ColumnWidths.Sum(), RowHeights.Sum());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildren = GetVisibleChildren();
        if (visibleChildren.Length == 0)
        {
            return finalSize;
        }

        CalculateRowColumnCounts(
            visibleChildren.Length,
            out var rowCount,
            out var usedRowCount,
            out var columnCount,
            out var usedColumnCount);
        UpdateCellSizes(
            visibleChildren,
            rowCount,
            usedRowCount,
            columnCount,
            usedColumnCount,
            finalSize.Width,
            finalSize.Height);

        var rowOffsets = BuildOffsets(RowHeights);
        var columnOffsets = BuildOffsets(ColumnWidths);
        var visibleIndex = 0;

        foreach (var child in visibleChildren)
        {
            CalculateRowColumnIndices(visibleIndex, rowCount, columnCount, out var row, out var column);
            var reversedRow = usedRowCount - row - 1;
            var reversedColumn = usedColumnCount - column - 1;
            var cellWidth = GetElement(ColumnWidths, column);
            var cellHeight = GetElement(RowHeights, row);

            var top = 0d;
            var left = 0d;

            if (VerticalLayoutMethod == GauntletGridVerticalLayoutMethod.TopToBottom)
            {
                top = GetElement(rowOffsets, row);
            }
            else if (VerticalLayoutMethod == GauntletGridVerticalLayoutMethod.Center)
            {
                if (Direction == GauntletGridDirection.ColumnFirst)
                {
                    var centeredRow = Math.Max(0, (column + 1) * usedRowCount - visibleChildren.Length) / 2 + row;
                    top = GetElement(rowOffsets, centeredRow);
                }
            }
            else if (VerticalLayoutMethod == GauntletGridVerticalLayoutMethod.BottomToTop)
            {
                top = GetElement(rowOffsets, reversedRow);
            }

            if (HorizontalLayoutMethod == GauntletGridHorizontalLayoutMethod.LeftToRight)
            {
                left = GetElement(columnOffsets, column);
            }
            else if (HorizontalLayoutMethod == GauntletGridHorizontalLayoutMethod.Center)
            {
                if (Direction == GauntletGridDirection.RowFirst)
                {
                    var centeredColumn = Math.Max(0, (row + 1) * usedColumnCount - visibleChildren.Length) / 2 + column;
                    left = GetElement(columnOffsets, centeredColumn);
                }
            }
            else if (HorizontalLayoutMethod == GauntletGridHorizontalLayoutMethod.RightToLeft)
            {
                left = GetElement(columnOffsets, reversedColumn);
            }

            ArrangeChild(child, left, top, cellWidth, cellHeight);
            visibleIndex++;
        }

        return finalSize;
    }

    public static GauntletGridDirection ParseDirection(string? rawValue)
    {
        return Enum.TryParse<GauntletGridDirection>(rawValue, true, out var parsed)
            ? parsed
            : GauntletGridDirection.RowFirst;
    }

    public static GauntletGridHorizontalLayoutMethod ParseHorizontalLayoutMethod(string? rawValue)
    {
        return Enum.TryParse<GauntletGridHorizontalLayoutMethod>(rawValue, true, out var parsed)
            ? parsed
            : GauntletGridHorizontalLayoutMethod.LeftToRight;
    }

    public static GauntletGridVerticalLayoutMethod ParseVerticalLayoutMethod(string? rawValue)
    {
        return Enum.TryParse<GauntletGridVerticalLayoutMethod>(rawValue, true, out var parsed)
            ? parsed
            : GauntletGridVerticalLayoutMethod.TopToBottom;
    }

    private FrameworkElement[] GetVisibleChildren()
    {
        return InternalChildren.OfType<UIElement>()
            .Where(static child => child.Visibility != Visibility.Collapsed)
            .Cast<FrameworkElement>()
            .ToArray();
    }

    private void PreMeasureCoverChildren(IEnumerable<FrameworkElement> visibleChildren)
    {
        foreach (var child in visibleChildren)
        {
            var widthPolicy = GauntletLayoutProperties.GetWidthSizePolicy(child);
            var heightPolicy = GauntletLayoutProperties.GetHeightSizePolicy(child);
            if (string.Equals(widthPolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(heightPolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase))
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
        }
    }

    private void UpdateCellSizes(
        IReadOnlyList<FrameworkElement> visibleChildren,
        int rowCount,
        int usedRowCount,
        int columnCount,
        int usedColumnCount,
        double totalWidth,
        double totalHeight)
    {
        var rowHeights = new double[usedRowCount];
        var columnWidths = new double[usedColumnCount];
        var widthUsesCoverChildren = string.Equals(WidthSizePolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase);
        var heightUsesCoverChildren = string.Equals(HeightSizePolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase);

        var visibleIndex = 0;
        foreach (var child in visibleChildren)
        {
            CalculateRowColumnIndices(visibleIndex, rowCount, columnCount, out var row, out var column);
            var margin = child.Margin;
            var childWidthPolicy = GauntletLayoutProperties.GetWidthSizePolicy(child);
            var childHeightPolicy = GauntletLayoutProperties.GetHeightSizePolicy(child);
            var measuredWidthWithMargin = ResolveMeasuredWidth(child) + margin.Left + margin.Right;
            var measuredHeightWithMargin = ResolveMeasuredHeight(child) + margin.Top + margin.Bottom;

            var cellWidth = !widthUsesCoverChildren
                ? ResolveFixedAxisCellSize(totalWidth, columnCount, DefaultCellWidth, measuredWidthWithMargin)
                : !UseDynamicCellWidth || string.Equals(childWidthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase)
                    ? DefaultCellWidth
                    : measuredWidthWithMargin;
            var cellHeight = !heightUsesCoverChildren
                ? ResolveFixedAxisCellSize(totalHeight, rowCount, DefaultCellHeight, measuredHeightWithMargin)
                : !UseDynamicCellHeight || string.Equals(childHeightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase)
                    ? DefaultCellHeight
                    : measuredHeightWithMargin;

            if (row >= 0 && row < rowHeights.Length)
            {
                rowHeights[row] = Math.Max(cellHeight, rowHeights[row]);
            }

            if (column >= 0 && column < columnWidths.Length)
            {
                columnWidths[column] = Math.Max(cellWidth, columnWidths[column]);
            }

            visibleIndex++;
        }

        RowHeights = rowHeights;
        ColumnWidths = columnWidths;
    }

    private void CalculateRowColumnCounts(
        int visibleChildrenCount,
        out int rowCount,
        out int usedRowCount,
        out int columnCount,
        out int usedColumnCount)
    {
        var rowCountIsDefault = RowCount < 0;
        var columnCountIsDefault = ColumnCount < 0;
        rowCount = rowCountIsDefault ? 3 : Math.Max(1, RowCount);
        columnCount = columnCountIsDefault ? 3 : Math.Max(1, ColumnCount);

        int usedColumns;
        int usedRows;
        if (Direction == GauntletGridDirection.RowFirst)
        {
            usedColumns = Math.Min(visibleChildrenCount, columnCount);
            usedRows = (visibleChildrenCount % columnCount > 0 ? 1 : 0) + visibleChildrenCount / columnCount;
        }
        else
        {
            usedRows = Math.Min(visibleChildrenCount, rowCount);
            usedColumns = (visibleChildrenCount % rowCount > 0 ? 1 : 0) + visibleChildrenCount / rowCount;
        }

        var heightUsesCoverChildren = string.Equals(HeightSizePolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase);
        var widthUsesCoverChildren = string.Equals(WidthSizePolicy, "CoverChildren", StringComparison.OrdinalIgnoreCase);
        usedRowCount = heightUsesCoverChildren ? Math.Max(usedRows, 0) : rowCount;
        usedColumnCount = widthUsesCoverChildren ? Math.Max(usedColumns, 0) : columnCount;
    }

    private void CalculateRowColumnIndices(
        int visibleIndex,
        int rowCount,
        int columnCount,
        out int row,
        out int column)
    {
        if (Direction == GauntletGridDirection.RowFirst)
        {
            row = visibleIndex / columnCount;
            column = visibleIndex % columnCount;
            return;
        }

        row = visibleIndex % rowCount;
        column = visibleIndex / rowCount;
    }

    private static double ResolveFixedAxisCellSize(
        double totalSize,
        int count,
        double defaultSize,
        double measuredSize)
    {
        if (count > 0 && !double.IsNaN(totalSize) && !double.IsInfinity(totalSize))
        {
            return Math.Max(0d, totalSize / count);
        }

        if (defaultSize > 0)
        {
            return defaultSize;
        }

        return Math.Max(0d, measuredSize);
    }

    private static Size CreateChildMeasureSize(FrameworkElement child, double cellWidth, double cellHeight)
    {
        var margin = child.Margin;
        var width = Math.Max(0d, cellWidth - margin.Left - margin.Right);
        var height = Math.Max(0d, cellHeight - margin.Top - margin.Bottom);
        return new Size(width, height);
    }

    private static double[] BuildOffsets(IReadOnlyList<double> sizes)
    {
        var offsets = new double[sizes.Count + 1];
        for (var index = 0; index < sizes.Count; index++)
        {
            offsets[index + 1] = offsets[index] + sizes[index];
        }

        return offsets;
    }

    private static double GetElement(IReadOnlyList<double> values, int index)
    {
        return index >= 0 && index < values.Count
            ? values[index]
            : 0d;
    }

    private static double ResolveMeasuredWidth(FrameworkElement child)
    {
        var desiredSize = child.DesiredSize.Width;
        if (!double.IsNaN(desiredSize) && !double.IsInfinity(desiredSize) && desiredSize > 0)
        {
            return desiredSize;
        }

        if (child is { Width: > 0 } && !double.IsNaN(child.Width))
        {
            return child.Width;
        }

        if (child is { Height: > 0 } && !double.IsNaN(child.Height))
        {
            return child.Height;
        }

        return 0d;
    }

    private static double ResolveMeasuredHeight(FrameworkElement child)
    {
        var desiredSize = child.DesiredSize.Height;
        if (!double.IsNaN(desiredSize) && !double.IsInfinity(desiredSize) && desiredSize > 0)
        {
            return desiredSize;
        }

        if (child is { Height: > 0 } && !double.IsNaN(child.Height))
        {
            return child.Height;
        }

        return 0d;
    }

    private static void ArrangeChild(FrameworkElement child, double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var margin = child.Margin;
        var availableWidth = Math.Max(0d, cellWidth - margin.Left - margin.Right);
        var availableHeight = Math.Max(0d, cellHeight - margin.Top - margin.Bottom);
        var stretchWidth = string.Equals(
            GauntletLayoutProperties.GetWidthSizePolicy(child),
            "StretchToParent",
            StringComparison.OrdinalIgnoreCase);
        var stretchHeight = string.Equals(
            GauntletLayoutProperties.GetHeightSizePolicy(child),
            "StretchToParent",
            StringComparison.OrdinalIgnoreCase);

        var arrangedWidth = stretchWidth
            ? availableWidth
            : Math.Min(ResolveDesiredDimension(child.Width, child.DesiredSize.Width), availableWidth);
        var arrangedHeight = stretchHeight
            ? availableHeight
            : Math.Min(ResolveDesiredDimension(child.Height, child.DesiredSize.Height), availableHeight);

        var left = cellLeft + margin.Left + ResolveHorizontalOffset(child.HorizontalAlignment, availableWidth, arrangedWidth);
        var top = cellTop + margin.Top + ResolveVerticalOffset(child.VerticalAlignment, availableHeight, arrangedHeight);
        child.Arrange(new Rect(left, top, Math.Max(0d, arrangedWidth), Math.Max(0d, arrangedHeight)));
    }

    private static double ResolveDesiredDimension(double explicitSize, double desiredSize)
    {
        if (!double.IsNaN(explicitSize) && !double.IsInfinity(explicitSize) && explicitSize > 0)
        {
            return explicitSize;
        }

        if (!double.IsNaN(desiredSize) && !double.IsInfinity(desiredSize) && desiredSize > 0)
        {
            return desiredSize;
        }

        return 0d;
    }

    private static double ResolveHorizontalOffset(HorizontalAlignment alignment, double availableWidth, double arrangedWidth)
    {
        var remaining = Math.Max(0d, availableWidth - arrangedWidth);
        return alignment switch
        {
            HorizontalAlignment.Right => remaining,
            HorizontalAlignment.Center => remaining / 2d,
            _ => 0d
        };
    }

    private static double ResolveVerticalOffset(VerticalAlignment alignment, double availableHeight, double arrangedHeight)
    {
        var remaining = Math.Max(0d, availableHeight - arrangedHeight);
        return alignment switch
        {
            VerticalAlignment.Bottom => remaining,
            VerticalAlignment.Center => remaining / 2d,
            _ => 0d
        };
    }
}

public enum GauntletGridDirection
{
    RowFirst,
    ColumnFirst
}

public enum GauntletGridHorizontalLayoutMethod
{
    LeftToRight,
    Center,
    RightToLeft
}

public enum GauntletGridVerticalLayoutMethod
{
    TopToBottom,
    Center,
    BottomToTop
}
