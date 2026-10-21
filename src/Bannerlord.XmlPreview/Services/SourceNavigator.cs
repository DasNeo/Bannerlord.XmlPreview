using System.Diagnostics;
using System.Windows;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
namespace Bannerlord.XmlPreview.Services;
/// <summary>An optional editor executable receives --goto file:line:column; otherwise copy the location.</summary>
public sealed class SourceNavigator : ISourceNavigator
{
    public void Open(SourceLocation location)
    {
        var editor = Environment.GetEnvironmentVariable("BANNERLORD_PREVIEW_EDITOR");
        if (string.IsNullOrWhiteSpace(editor)) { CopyPath(location); return; }
        var start = new ProcessStartInfo(editor) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--goto");
        start.ArgumentList.Add($"{location.FilePath}:{Math.Max(1, location.Line)}:{Math.Max(1, location.Column)}");
        Process.Start(start);
    }
    public void CopyPath(SourceLocation location) => Clipboard.SetText($"{location.FilePath}:{Math.Max(1, location.Line)}:{Math.Max(1, location.Column)}");
}
