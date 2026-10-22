namespace Bannerlord.XmlPreview.Interfaces;

public interface IFileDialogService
{
    string? OpenPrefab(string seedPath);

    string? OpenFolder(string seedPath, string title);
}
