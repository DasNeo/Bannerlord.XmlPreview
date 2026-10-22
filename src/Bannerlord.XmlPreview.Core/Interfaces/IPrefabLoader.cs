using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IPrefabLoader
{
    void Invalidate() { }
    PrefabDocument Load(
        string path,
        PreviewDiagnostics diagnostics,
        CancellationToken cancellationToken = default);
}
