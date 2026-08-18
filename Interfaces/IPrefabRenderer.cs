using System.Windows;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IPrefabRenderer
{
    FrameworkElement Render(PrefabDocument document, PreviewDiagnostics diagnostics);
}
