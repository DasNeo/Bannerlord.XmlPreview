using Microsoft.Win32;
using System.IO;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenPrefab(string seedPath)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bannerlord Prefabs (*.xml)|*.xml|All Files (*.*)|*.*",
            Title = "Open Bannerlord Prefab",
            FileName = seedPath
        };

        var initialDirectory = Path.GetDirectoryName(seedPath);
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFolder(string seedPath, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };

        var initialDirectory = Directory.Exists(seedPath)
            ? seedPath
            : Path.GetDirectoryName(seedPath);
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
