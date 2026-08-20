using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Rendering;
using System.Windows;

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
            diagnostics,
            _assetCatalog,
            _brushMetricsProvider,
            _brushOverlayFeatureHandler,
            _previewDataSourceProvider,
            _widgetTextSourceProvider,
            _widgetDebugMetadataProvider);
        context.EnsureBindings(document.BindingKeys);

        var rootSize = _rootLayoutHints.ResolveRootSize(context.ResolveDouble);
        return document.RootNode.Render(context, rootSize).Element;
    }
}
