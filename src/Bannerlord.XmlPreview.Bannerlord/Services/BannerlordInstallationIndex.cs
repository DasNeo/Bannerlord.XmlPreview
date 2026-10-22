using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using System.IO;

namespace Bannerlord.XmlPreview.Services;

public sealed class BannerlordInstallationIndex : IBannerlordInstallationIndex
{
    private readonly object _sync = new();
    private string? _indexedRoot;
    private Task<BannerlordInstallationSnapshot>? _snapshotTask;

    public BannerlordInstallationSnapshot GetSnapshot(string installRoot)
    {
        return GetOrCreateSnapshotTask(installRoot).GetAwaiter().GetResult();
    }

    public Task<BannerlordInstallationSnapshot> GetSnapshotAsync(
        string installRoot,
        CancellationToken cancellationToken = default)
    {
        return GetOrCreateSnapshotTask(installRoot).WaitAsync(cancellationToken);
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _indexedRoot = null;
            _snapshotTask = null;
        }
    }

    private Task<BannerlordInstallationSnapshot> GetOrCreateSnapshotTask(string installRoot)
    {
        var normalizedRoot = Path.GetFullPath(installRoot);
        lock (_sync)
        {
            if (_snapshotTask is { IsFaulted: false, IsCanceled: false } &&
                string.Equals(_indexedRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return _snapshotTask;

            _indexedRoot = normalizedRoot;
            _snapshotTask = Task.Run(() => BuildSnapshot(normalizedRoot));
            return _snapshotTask;
        }
    }

    private static BannerlordInstallationSnapshot BuildSnapshot(string installRoot)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase(
            "BannerlordInstallationIndex.BuildSnapshot.Total");

        var prefabFiles = new List<string>();
        var brushFiles = new List<string>();
        var spriteDataFiles = new List<string>();
        var assemblyFiles = new List<string>();
        var modules = new List<BannerlordModule>();
        var modulesPath = Path.Combine(installRoot, "Modules");

        AddFiles(
            Path.Combine(installRoot, "bin", "Win64_Shipping_Client"),
            "*.dll",
            SearchOption.TopDirectoryOnly,
            assemblyFiles);

        if (Directory.Exists(modulesPath))
        {
            foreach (var modulePath in Directory.EnumerateDirectories(modulesPath))
            {
                modules.Add(ModuleAssetOrder.Read(modulePath));
                AddFiles(
                    Path.Combine(modulePath, "GUI", "Prefabs"),
                    "*.xml",
                    SearchOption.AllDirectories,
                    prefabFiles);
                AddFiles(
                    Path.Combine(modulePath, "GUI", "Brushes"),
                    "*.xml",
                    SearchOption.AllDirectories,
                    brushFiles);
                AddFiles(
                    Path.Combine(modulePath, "bin", "Win64_Shipping_Client"),
                    "*.dll",
                    SearchOption.TopDirectoryOnly,
                    assemblyFiles);

                // LauncherGUI is a separate atlas namespace and reuses game sprite names.
                AddFiles(Path.Combine(modulePath, "GUI"), "*SpriteData.xml",
                    SearchOption.AllDirectories, spriteDataFiles);
            }
        }

        return new BannerlordInstallationSnapshot(
            installRoot,
            NormalizeAndSort(prefabFiles),
            NormalizeAndSort(brushFiles),
            NormalizeAndSort(spriteDataFiles),
            NormalizeAndSort(assemblyFiles)) { Modules = modules.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private static void AddFiles(
        string directory,
        string pattern,
        SearchOption searchOption,
        ICollection<string> target)
    {
        if (!Directory.Exists(directory)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern, searchOption))
                target.Add(file);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static IReadOnlyList<string> NormalizeAndSort(IEnumerable<string> paths)
    {
        return paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
