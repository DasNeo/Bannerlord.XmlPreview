using Bannerlord.XmlPreview.Core.Behaviors;
using System.Collections.Immutable;
using System.IO;
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
    private readonly IBehaviorRuleProvider? _behaviorRules;
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
        IWidgetDebugMetadataProvider widgetDebugMetadataProvider,
        IBehaviorRuleProvider? behaviorRules = null)
    {
        _behaviorRules = behaviorRules;
        _assetCatalog = assetCatalog;
        _brushMetricsProvider = brushMetricsProvider;
        _brushOverlayFeatureHandler = brushOverlayFeatureHandler;
        _previewDataSourceProvider = previewDataSourceProvider;
        _rootLayoutHints = rootLayoutHints;
        _widgetTextSourceProvider = widgetTextSourceProvider;
        _widgetDebugMetadataProvider = widgetDebugMetadataProvider;
    }

    public FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics)
        => Render(document, diagnostics, null);

    public FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics, RegressionRenderOptions? options)
    {
        try
        {
            return RenderCore(document, diagnostics, options);
        }
        catch (BehaviorGraphException exception)
        {
            diagnostics.BehaviorTrace = exception.Trace;
            diagnostics.BehaviorGraph = exception.Summary ?? diagnostics.BehaviorGraph;
            diagnostics.MarkFailed($"Behavior Graph {exception.Error.Code}: {exception.Message}");
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.MarkFailed($"Render failed: {exception.Message}");
            throw;
        }
    }

    private FrameworkElement RenderCore(PrefabDocument document, PreviewDiagnostics diagnostics, RegressionRenderOptions? options)
    {
        using var totalTiming = diagnostics.MeasurePhase("PrefabRenderer.Render.Total");
        var matcherDocument = document;
        var previewData = _previewDataSourceProvider.CaptureSnapshot(document.RootNode);
        if (options?.Lists is { } lists)
            previewData = previewData with { TemplateItems = lists.ToImmutableDictionary(p => p.Key,
                p => p.Value.Select(row => row.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)).ToImmutableArray(), StringComparer.Ordinal) };
        document = document with { RootNode = document.RootNode.CopyForRender(options?.WidgetStates ?? new Dictionary<string, IReadOnlyDictionary<string, string>>()) };
        PrefabRenderContext context;
        using (diagnostics.MeasurePhase("PrefabRenderer.Render.CreateContext"))
        {
            var assets = _assetCatalog.ForPrefab(document.SourcePath, options?.AssetModule);
            context = new PrefabRenderContext(
                document.Constants,
                document.PropertyMap,
                document.ReferenceMap,
                diagnostics,
                assets,
                _brushMetricsProvider.ForAssets(assets),
                _brushOverlayFeatureHandler,
                _widgetTextSourceProvider,
                _widgetDebugMetadataProvider,
                options, previewData);
        }

        using (diagnostics.MeasurePhase("PrefabRenderer.Render.EnsureBindings"))
        {
            context.EnsureBindings(document.BindingKeys);
        }

        var token = options?.CancellationToken ?? default;
        token.ThrowIfCancellationRequested();
        var identity = options?.PrefabIdentity ?? document.PrefabIdentity ?? Path.GetFileName(document.SourcePath).ToLowerInvariant();
        context.GraphPrefabIdentity = identity;
        var structure = RegressionSnapshotProducer.Structure(document, context, identity);
        var inputs = RenderBehaviorPlanner.Inputs(matcherDocument, structure, _behaviorRules, options, previewData);
        var plan = new BehaviorGraph().Plan(inputs, _behaviorRules?.Rules ?? [], options is not null, token);
        diagnostics.BehaviorGraph = plan.Summary;
        diagnostics.BehaviorTrace = options?.TraceBehaviors == true || options is not null ? plan.Trace : [];
        foreach (var message in plan.Diagnostics) diagnostics.Add(message);
        context.SetBehaviorPlan(inputs, plan, document.RootNode);
        try
        {
            var rootSize = options?.PreviewSize ?? _rootLayoutHints.ResolveRootSize(context.ResolveDouble);
            if (options is not null)
                diagnostics.RenderStructure = RegressionSnapshotProducer.Structure(document, context, options.PrefabIdentity);
            RenderedPrefabNode renderedRoot;
            using (diagnostics.MeasurePhase("PrefabRenderer.Render.RootNodeRender"))
            {
                renderedRoot = context.RegisterRenderedNode(
                    document.RootNode.Name,
                    document.RootNode.Attributes,
                    rootSize,
                    context.RenderNode(document.RootNode, rootSize));
            }

            token.ThrowIfCancellationRequested();
            context.ExecuteBehaviorPhase(plan, BehaviorPhase.Connect);
            context.ExecuteBehaviorPhase(plan, BehaviorPhase.PrepareLayout);

            if (diagnostics.RenderStructure is not null)
                diagnostics.RenderStructure = RegressionSnapshotProducer.ResolveBehavior(diagnostics.RenderStructure, context.SnapshotElements);

            Size measuredRootSize;
            using (diagnostics.MeasurePhase("PrefabRenderer.Render.MeasureRoot"))
            {
                measuredRootSize = context.Measure(renderedRoot.Element, document.RootNode.Attributes, rootSize);
            }

            using (diagnostics.MeasurePhase("PrefabRenderer.Render.AttachRootLayoutDebugMetadata"))
            {
                context.AttachElementLayoutDebugMetadata(renderedRoot.Element, document.RootNode.Attributes, rootSize, measuredRootSize);
            }

            using (diagnostics.MeasurePhase("PrefabRenderer.Render.ArrangeRoot"))
            {
                ArrangeRenderedRoot(renderedRoot.Element, rootSize, measuredRootSize);
            }

            IReadOnlyList<ElementLayoutSnapshot> elementLayoutSnapshots;
            using (diagnostics.MeasurePhase("PrefabRenderer.Render.CaptureElementLayoutSnapshots"))
            {
                elementLayoutSnapshots = RefreshElementDebugInfoAndCaptureSnapshots(renderedRoot.Element);
            }

            diagnostics.SetRenderSummary(
                rootSize.Width,
                rootSize.Height,
                measuredRootSize.Width,
                measuredRootSize.Height,
                renderedRoot.Element.ActualWidth,
                renderedRoot.Element.ActualHeight);
            diagnostics.SetElementLayoutSnapshots(elementLayoutSnapshots);
            if (options is not null)
                diagnostics.Layout = RegressionSnapshotProducer.Layout(renderedRoot.Element, context.SnapshotElements,
                    options.KeyBoundsSelectors.Concat(plan.Contributions.Where(c => c.Contribution is InspectionContribution { KeyBounds: true }).Select(c => c.Widget.Value)).Distinct(StringComparer.Ordinal).ToArray());
            token.ThrowIfCancellationRequested();
            context.ExecuteBehaviorPhase(plan, BehaviorPhase.Inspect);
            foreach (var element in context.SnapshotElements)
                if (document.SourceMap.TryGetValue(element.Path, out var source))
                    PreviewElementDebugProperties.SetSourceLocation(element.Element, source);
            diagnostics.BehaviorTrace = diagnostics.BehaviorTrace.Select(trace => trace with
            {
                Source = context.SnapshotElements.FirstOrDefault(element => element.Id == trace.Widget.Value) is { } element
                    ? document.SourceMap.GetValueOrDefault(element.Path) : null
            }).ToImmutableArray();
            diagnostics.Dependencies.UnionWith(context.Assets.Dependencies);
            return renderedRoot.Element;
        }
        catch
        {
            diagnostics.BehaviorTrace = plan.Trace.IsEmpty ? inputs.Select(input => new BehaviorTrace(input.Widget.Id,
                plan.Contributions.Where(c => c.Widget == input.Widget.Id).Select(c => c.Rule).Distinct().ToImmutableArray(),
                (_behaviorRules?.Rules ?? []).Where(r => !r.Matcher.Matches(input.Widget)).Select(r => r.Id).ToImmutableArray(),
                plan.Contributions.Where(c => c.Widget == input.Widget.Id).ToImmutableArray(),
                plan.Contributions.Any(c => c.Widget == input.Widget.Id && c.Contribution is InspectionContribution { Support: BehaviorSupport.Partial }) ? BehaviorSupport.Partial : BehaviorSupport.Supported)
                {
                    Rules = (_behaviorRules?.Rules ?? []).Select(r => new BehaviorRuleTrace(r.Id, r.Phase, r.Matcher, r.Matcher.Matches(input.Widget),
                        plan.Contributions.Any(c => c.Widget == input.Widget.Id && c.Rule == r.Id) ? Array.FindIndex(plan.Contributions.ToArray(), c => c.Widget == input.Widget.Id && c.Rule == r.Id) : null)).ToImmutableArray()
                }).ToImmutableArray() : plan.Trace;
            throw;
        }
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

    private static IReadOnlyList<ElementLayoutSnapshot> RefreshElementDebugInfoAndCaptureSnapshots(FrameworkElement rootElement)
    {
        var snapshots = new List<ElementLayoutSnapshot>();
        RefreshElementDebugInfoAndCaptureSnapshots(rootElement, rootElement, "0", snapshots);
        return snapshots;
    }

    private static void RefreshElementDebugInfoAndCaptureSnapshots(
        DependencyObject current,
        FrameworkElement rootElement,
        string visualPath,
        ICollection<ElementLayoutSnapshot> snapshots)
    {
        if (current is FrameworkElement element)
        {
            var debugInfo = PreviewElementDebugProperties.GetDebugInfo(element);
            if (debugInfo is not null)
            {
                debugInfo.ActualLayout = BuildActualLayoutDebugInfo(element, rootElement);
                snapshots.Add(BuildElementLayoutSnapshot(element, visualPath, debugInfo));
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
            RefreshElementDebugInfoAndCaptureSnapshots(child, rootElement, $"{visualPath}.{index}", snapshots);
        }
    }

    private static ElementLayoutSnapshot BuildElementLayoutSnapshot(
        FrameworkElement element,
        string visualPath,
        ElementDebugInfo debugInfo)
    {
        var label = PreviewElementDebugProperties.GetTreeLabel(element);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = element.GetType().Name;
        }

        return new ElementLayoutSnapshot(
            visualPath,
            label,
            element.GetType().Name,
            debugInfo.ActualLayout!);
    }

    private static ElementDebugSection BuildActualLayoutDebugInfo(FrameworkElement element, FrameworkElement rootElement)
    {
        var actualBounds = GetActualBounds(element, rootElement);
        var actualLayout = new ElementDebugSection("Actual Layout");
        actualLayout.Fields.Add(new ElementDebugField("ActualBounds", FormatRect(actualBounds)));
        actualLayout.Fields.Add(new ElementDebugField("RenderSize", FormatSize(element.RenderSize)));
        actualLayout.Fields.Add(new ElementDebugField("DesiredSize", FormatSize(element.DesiredSize)));

        var debugInfo = PreviewElementDebugProperties.GetDebugInfo(element);
        actualLayout.Fields.Add(new ElementDebugField(
            "ParentSize",
            debugInfo?.Layout?.GetValue("ParentSize") ?? "-"));
        actualLayout.Fields.Add(new ElementDebugField(
            "MeasuredSize",
            debugInfo?.Layout?.GetValue("MeasuredSize") ?? "-"));
        actualLayout.Fields.Add(new ElementDebugField(
            "WpfSize",
            $"W={FormatOptionalDimension(double.IsNaN(element.Width) ? null : element.Width)} H={FormatOptionalDimension(double.IsNaN(element.Height) ? null : element.Height)}"));
        actualLayout.Fields.Add(new ElementDebugField("Margin", FormatThickness(element.Margin)));

        var canvasLeft = Canvas.GetLeft(element);
        var canvasTop = Canvas.GetTop(element);
        if (!double.IsNaN(canvasLeft) || !double.IsNaN(canvasTop))
        {
            actualLayout.Fields.Add(new ElementDebugField(
                "CanvasPosition",
                $"({FormatOptionalAxis(canvasLeft)}, {FormatOptionalAxis(canvasTop)})"));
        }

        var computedPosition = debugInfo?.Layout?.GetValue("CanvasPosition");
        if (!string.IsNullOrWhiteSpace(computedPosition))
        {
            actualLayout.Fields.Add(new ElementDebugField("ComputedCanvasPosition", computedPosition));
        }

        var selectionBounds = PreviewElementDebugProperties.GetSelectionBounds(element);
        if (!selectionBounds.IsEmpty)
        {
            actualLayout.Fields.Add(new ElementDebugField("SelectionBoundsLocal", FormatRect(selectionBounds)));
            actualLayout.Fields.Add(new ElementDebugField("SelectionBoundsRoot", FormatRect(TranslateRect(selectionBounds, actualBounds.Location))));
        }

        var alignmentBounds = debugInfo?.Layout?.GetValue("AlignmentBounds");
        if (!string.IsNullOrWhiteSpace(alignmentBounds))
        {
            actualLayout.Fields.Add(new ElementDebugField("AlignmentBoundsLocal", alignmentBounds));

            var localBounds = ParseRect(alignmentBounds);
            if (!localBounds.IsEmpty)
            {
                actualLayout.Fields.Add(new ElementDebugField(
                    "AlignmentBoundsRoot",
                    FormatRect(TranslateRect(localBounds, actualBounds.Location))));
            }
        }

        return actualLayout;
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

    private static Rect ParseRect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Rect.Empty;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^\((?<x>-?[0-9]+(?:\.[0-9]+)?),\s*(?<y>-?[0-9]+(?:\.[0-9]+)?)\)\s+(?<w>-?[0-9]+(?:\.[0-9]+)?)\s+x\s+(?<h>-?[0-9]+(?:\.[0-9]+)?)$");
        if (!match.Success ||
            !double.TryParse(match.Groups["x"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(match.Groups["y"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(match.Groups["w"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(match.Groups["h"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height))
        {
            return Rect.Empty;
        }

        return new Rect(x, y, width, height);
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
