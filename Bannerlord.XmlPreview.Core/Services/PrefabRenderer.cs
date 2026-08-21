using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Bannerlord.XmlPreview.Services;

public sealed class PrefabRenderer : IPrefabRenderer
{
    private readonly IAssetCatalog _assetCatalog;
    private readonly IBrushMetricsProvider _brushMetricsProvider;
    private readonly IBrushOverlayFeatureHandler _brushOverlayFeatureHandler;
    private readonly IPreviewDataSourceProvider _previewDataSourceProvider;
    private readonly IRootLayoutHints _rootLayoutHints;
    private readonly IWidgetTextSourceProvider _widgetTextSourceProvider;
    private readonly IWidgetDebugMetadataProvider _widgetDebugMetadataProvider;

    public PrefabRenderer(
        IAssetCatalog assetCatalog,
        IBrushMetricsProvider brushMetricsProvider,
        IBrushOverlayFeatureHandler brushOverlayFeatureHandler,
        IPreviewDataSourceProvider previewDataSourceProvider,
        IRootLayoutHints rootLayoutHints,
        IWidgetTextSourceProvider widgetTextSourceProvider,
        IWidgetDebugMetadataProvider widgetDebugMetadataProvider)
    {
        _assetCatalog = assetCatalog;
        _brushMetricsProvider = brushMetricsProvider;
        _brushOverlayFeatureHandler = brushOverlayFeatureHandler;
        _previewDataSourceProvider = previewDataSourceProvider;
        _rootLayoutHints = rootLayoutHints;
        _widgetTextSourceProvider = widgetTextSourceProvider;
        _widgetDebugMetadataProvider = widgetDebugMetadataProvider;
    }

    public FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics)
    {
        var context = new PrefabRenderContext(
            document.Constants,
            document.PropertyMap,
            document.ReferenceMap,
            diagnostics,
            _assetCatalog,
            _brushMetricsProvider,
            _brushOverlayFeatureHandler,
            _previewDataSourceProvider,
            _widgetTextSourceProvider,
            _widgetDebugMetadataProvider);
        context.EnsureBindings(document.BindingKeys);

        var rootSize = _rootLayoutHints.ResolveRootSize(context.ResolveDouble);
        var renderedRoot = context.RegisterRenderedNode(
            document.RootNode.Name,
            document.RootNode.Attributes,
            rootSize,
            document.RootNode.Render(context, rootSize));
        context.ApplyDeferredReferenceBehaviors();
        var measuredRootSize = context.Measure(renderedRoot.Element, document.RootNode.Attributes, rootSize);
        context.AttachElementLayoutDebugMetadata(renderedRoot.Element, document.RootNode.Attributes, rootSize, measuredRootSize);
        ArrangeRenderedRoot(renderedRoot.Element, rootSize, measuredRootSize);
        RefreshElementDebugText(renderedRoot.Element);
        diagnostics.SetRenderSummary(
            rootSize.Width,
            rootSize.Height,
            measuredRootSize.Width,
            measuredRootSize.Height,
            renderedRoot.Element.ActualWidth,
            renderedRoot.Element.ActualHeight);
        diagnostics.SetElementLayoutSnapshots(CaptureElementLayoutSnapshots(renderedRoot.Element));
        return renderedRoot.Element;
    }

    private static void ArrangeRenderedRoot(FrameworkElement rootElement, Size requestedRootSize, Size measuredRootSize)
    {
        var finalSize = new Size(
            ResolveFinalAxis(requestedRootSize.Width, measuredRootSize.Width),
            ResolveFinalAxis(requestedRootSize.Height, measuredRootSize.Height));
        rootElement.Measure(finalSize);
        rootElement.Arrange(new Rect(new Point(0, 0), finalSize));
        rootElement.UpdateLayout();
    }

    private static double ResolveFinalAxis(double requested, double measured)
    {
        var requestedValue = IsFinitePositive(requested) ? requested : 0d;
        var measuredValue = IsFinitePositive(measured) ? measured : 0d;
        return Math.Max(requestedValue, measuredValue);
    }

    private static bool IsFinitePositive(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    }

    private static void RefreshElementDebugText(FrameworkElement rootElement)
    {
        RefreshElementDebugText(rootElement, rootElement);
    }

    private static void RefreshElementDebugText(DependencyObject current, FrameworkElement rootElement)
    {
        if (current is FrameworkElement element)
        {
            var baseDebugText = PreviewElementDebugProperties.GetDebugBaseText(element);
            if (!string.IsNullOrWhiteSpace(baseDebugText))
            {
                var layoutDebugText = PreviewElementDebugProperties.GetLayoutDebugText(element);
                var actualLayoutDebugText = BuildActualLayoutDebugText(element, rootElement);
                var sections = new List<string> { baseDebugText };

                if (!string.IsNullOrWhiteSpace(layoutDebugText))
                {
                    sections.Add(layoutDebugText);
                }

                sections.Add(actualLayoutDebugText);
                PreviewElementDebugProperties.SetDebugText(
                    element,
                    string.Join(Environment.NewLine + Environment.NewLine, sections));
            }
        }

        if (current is not Visual && current is not Visual3D)
        {
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var index = 0; index < childCount; index++)
        {
            RefreshElementDebugText(VisualTreeHelper.GetChild(current, index), rootElement);
        }
    }

    private static IReadOnlyList<string> CaptureElementLayoutSnapshots(FrameworkElement rootElement)
    {
        var snapshots = new List<string>();
        CaptureElementLayoutSnapshots(rootElement, rootElement, "0", snapshots);
        return snapshots;
    }

    private static void CaptureElementLayoutSnapshots(
        DependencyObject current,
        FrameworkElement rootElement,
        string visualPath,
        ICollection<string> snapshots)
    {
        if (current is FrameworkElement element)
        {
            var debugText = PreviewElementDebugProperties.GetDebugText(element);
            if (!string.IsNullOrWhiteSpace(debugText))
            {
                snapshots.Add(BuildElementLayoutSnapshot(element, rootElement, visualPath));
            }
        }

        if (current is not Visual && current is not Visual3D)
        {
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            CaptureElementLayoutSnapshots(child, rootElement, $"{visualPath}.{index}", snapshots);
        }
    }

    private static string BuildElementLayoutSnapshot(
        FrameworkElement element,
        FrameworkElement rootElement,
        string visualPath)
    {
        var label = PreviewElementDebugProperties.GetTreeLabel(element);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = element.GetType().Name;
        }

        var lines = BuildActualLayoutLines(element, rootElement, includeSectionHeader: false);
        lines.Insert(0, $"Type: {element.GetType().Name}");
        lines.Insert(0, $"[{visualPath}] {label}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildActualLayoutDebugText(FrameworkElement element, FrameworkElement rootElement)
    {
        return string.Join(
            Environment.NewLine,
            BuildActualLayoutLines(element, rootElement, includeSectionHeader: true));
    }

    private static List<string> BuildActualLayoutLines(
        FrameworkElement element,
        FrameworkElement rootElement,
        bool includeSectionHeader)
    {
        var actualBounds = GetActualBounds(element, rootElement);
        var lines = new List<string>();
        if (includeSectionHeader)
        {
            lines.Add("Actual Layout");
        }

        lines.Add($"ActualBounds: {FormatRect(actualBounds)}");
        lines.Add($"RenderSize: {FormatSize(element.RenderSize)}");
        lines.Add($"DesiredSize: {FormatSize(element.DesiredSize)}");
        lines.Add($"ParentSize: {FormatSizeOrUnknown(PreviewElementDebugProperties.GetLayoutParentSize(element))}");
        lines.Add($"MeasuredSize: {FormatSizeOrUnknown(PreviewElementDebugProperties.GetLayoutMeasuredSize(element))}");
        lines.Add($"WpfSize: W={FormatOptionalDimension(double.IsNaN(element.Width) ? null : element.Width)} H={FormatOptionalDimension(double.IsNaN(element.Height) ? null : element.Height)}");
        lines.Add($"Margin: {FormatThickness(element.Margin)}");

        var canvasLeft = Canvas.GetLeft(element);
        var canvasTop = Canvas.GetTop(element);
        if (!double.IsNaN(canvasLeft) || !double.IsNaN(canvasTop))
        {
            lines.Add($"CanvasPosition: ({FormatOptionalAxis(canvasLeft)}, {FormatOptionalAxis(canvasTop)})");
        }

        var computedPosition = PreviewElementDebugProperties.GetLayoutComputedPosition(element);
        if (!double.IsNaN(computedPosition.X) || !double.IsNaN(computedPosition.Y))
        {
            lines.Add($"ComputedCanvasPosition: {FormatPoint(computedPosition)}");
        }

        var selectionBounds = PreviewElementDebugProperties.GetSelectionBounds(element);
        if (!selectionBounds.IsEmpty)
        {
            lines.Add($"SelectionBoundsLocal: {FormatRect(selectionBounds)}");
            lines.Add($"SelectionBoundsRoot: {FormatRect(TranslateRect(selectionBounds, actualBounds.Location))}");
        }

        var alignmentBounds = PreviewElementDebugProperties.GetLayoutAlignmentBounds(element);
        if (!alignmentBounds.IsEmpty)
        {
            lines.Add($"AlignmentBoundsLocal: {FormatRect(alignmentBounds)}");
            lines.Add($"AlignmentBoundsRoot: {FormatRect(TranslateRect(alignmentBounds, actualBounds.Location))}");
        }

        return lines;
    }

    private static Rect GetActualBounds(FrameworkElement element, UIElement rootElement)
    {
        try
        {
            var topLeft = element.TranslatePoint(new Point(0, 0), rootElement);
            return new Rect(topLeft, element.RenderSize);
        }
        catch (InvalidOperationException)
        {
            return new Rect(new Point(0, 0), element.RenderSize);
        }
    }

    private static Rect TranslateRect(Rect rect, Point offset)
    {
        return new Rect(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
    }

    private static string FormatRect(Rect rect)
    {
        return $"{FormatPoint(rect.Location)} {FormatSize(rect.Size)}";
    }

    private static string FormatPoint(Point point)
    {
        return $"({FormatDouble(point.X)}, {FormatDouble(point.Y)})";
    }

    private static string FormatSize(Size size)
    {
        return $"{FormatDouble(size.Width)} x {FormatDouble(size.Height)}";
    }

    private static string FormatSizeOrUnknown(Size size)
    {
        return size.IsEmpty ? "-" : FormatSize(size);
    }

    private static string FormatThickness(Thickness thickness)
    {
        return $"L={FormatDouble(thickness.Left)} T={FormatDouble(thickness.Top)} R={FormatDouble(thickness.Right)} B={FormatDouble(thickness.Bottom)}";
    }

    private static string FormatOptionalDimension(double? value)
    {
        return value.HasValue ? FormatDouble(value.Value) : "Auto";
    }

    private static string FormatOptionalAxis(double value)
    {
        return double.IsNaN(value) ? "Auto" : FormatDouble(value);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
