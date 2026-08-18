using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Services;

public sealed class PrefabRenderer : IPrefabRenderer
{
    private readonly IBannerlordAssetCatalog _assetCatalog;

    public PrefabRenderer(IBannerlordAssetCatalog assetCatalog)
    {
        _assetCatalog = assetCatalog;
    }

    public FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics)
    {
        var context = new PrefabRenderContext(document.Constants, diagnostics, _assetCatalog);
        context.EnsureBindings(document.BindingKeys);

        var rootSize = new Size(context.ResolveRootWidth(), context.ResolveRootHeight());
        return document.RootNode.Render(context, rootSize).Element;
    }
}
