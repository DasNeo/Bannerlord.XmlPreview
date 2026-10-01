namespace Bannerlord.XmlPreview.Interfaces;

public interface IBannerlordInstallationIndex
{
    BannerlordInstallationSnapshot GetSnapshot(string installRoot);

    Task<BannerlordInstallationSnapshot> GetSnapshotAsync(
        string installRoot,
        CancellationToken cancellationToken = default);

    void Invalidate();
}

public sealed record BannerlordInstallationSnapshot(
    string InstallRoot,
    IReadOnlyList<string> PrefabFiles,
    IReadOnlyList<string> BrushFiles,
    IReadOnlyList<string> SpriteDataFiles,
    IReadOnlyList<string> AssemblyFiles);
