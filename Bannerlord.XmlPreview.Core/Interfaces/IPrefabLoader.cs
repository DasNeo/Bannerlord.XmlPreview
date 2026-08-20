using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IPrefabLoader
{
    PrefabDocument Load(string path, PreviewDiagnostics diagnostics);
}
