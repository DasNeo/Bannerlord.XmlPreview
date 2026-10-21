namespace Bannerlord.XmlPreview.Interfaces;

public interface IAppSettingsService
{
    string BannerlordInstallRoot { get; }

    bool TrySetBannerlordInstallRoot(string path, out string normalizedPath, out string errorMessage);
}
