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
    IReadOnlyList<string> AssemblyFiles)
{
    public IReadOnlyList<BannerlordModule> Modules { get; init; } = [];
}

public sealed record BannerlordModule(string Id, string Directory, IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Packages, string? MetadataError = null);
