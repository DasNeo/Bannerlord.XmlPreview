using System.Windows;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IPrefabRenderer
{
    FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics);
    FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics, Bannerlord.XmlPreview.Rendering.RegressionRenderOptions? options)
        => Render(document, diagnostics);
}
