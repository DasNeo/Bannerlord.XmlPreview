using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using System.IO;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordPrefabReferenceResolver : IPrefabReferenceResolver
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly IBannerlordInstallationIndex _installationIndex;
    private readonly object _sync = new();
    private string? _indexedInstallRoot;
    private Dictionary<string, List<PrefabReferenceCandidate>> _index = new(StringComparer.OrdinalIgnoreCase);

    public BannerlordPrefabReferenceResolver(
        IAppSettingsService appSettingsService,
        IBannerlordInstallationIndex installationIndex)
    {
        _appSettingsService = appSettingsService;
        _installationIndex = installationIndex;
    }

    public PrefabReferenceResolveResult Resolve(string sourcePrefabPath, string referenceName)
    {
        if (string.IsNullOrWhiteSpace(sourcePrefabPath) ||
            string.IsNullOrWhiteSpace(referenceName))
        {
            return new PrefabReferenceResolveResult(
                referenceName,
                null,
                Array.Empty<string>(),
                PrefabReferenceResolveKind.NotFound);
        }

        var candidates = GetCandidates(referenceName);
        if (candidates.Count == 0)
        {
            return new PrefabReferenceResolveResult(
                referenceName,
                null,
                Array.Empty<string>(),
                PrefabReferenceResolveKind.NotFound);
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePrefabPath)) ?? string.Empty;
        var sameDirectoryMatches = candidates
            .Where(candidate => string.Equals(candidate.DirectoryPath, sourceDirectory, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sameDirectoryMatches.Length == 1)
        {
            return CreateResult(referenceName, sameDirectoryMatches[0].FullPath, candidates, PrefabReferenceResolveKind.SameDirectory);
        }

        var sourceModuleRoot = TryGetModuleRoot(sourcePrefabPath);
        if (!string.IsNullOrWhiteSpace(sourceModuleRoot))
        {
            var sameModuleMatches = candidates
                .Where(candidate => string.Equals(candidate.ModuleRootPath, sourceModuleRoot, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => GetRelativeDirectoryDistance(sourceDirectory, candidate.DirectoryPath))
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sameModuleMatches.Length == 1)
            {
                return CreateResult(referenceName, sameModuleMatches[0].FullPath, candidates, PrefabReferenceResolveKind.SameModule);
            }

            if (sameModuleMatches.Length > 1)
            {
                return CreateResult(referenceName, sameModuleMatches[0].FullPath, candidates, PrefabReferenceResolveKind.Heuristic);
            }
        }

        if (candidates.Count == 1)
        {
            return CreateResult(referenceName, candidates[0].FullPath, candidates, PrefabReferenceResolveKind.UniqueGlobal);
        }

        var heuristicMatch = candidates
            .OrderBy(candidate => GetRelativeDirectoryDistance(sourceDirectory, candidate.DirectoryPath))
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .First();

        return CreateResult(referenceName, heuristicMatch.FullPath, candidates, PrefabReferenceResolveKind.Heuristic);
    }

    private static PrefabReferenceResolveResult CreateResult(
        string referenceName,
        string? resolvedPath,
        IEnumerable<PrefabReferenceCandidate> candidates,
        PrefabReferenceResolveKind kind)
    {
        return new PrefabReferenceResolveResult(
            referenceName,
            resolvedPath,
            candidates
                .Select(candidate => candidate.FullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            kind);
    }

    private IReadOnlyList<PrefabReferenceCandidate> GetCandidates(string referenceName)
    {
        EnsureIndex();
        return _index.TryGetValue(referenceName, out var candidates)
            ? candidates
            : Array.Empty<PrefabReferenceCandidate>();
    }

    private void EnsureIndex()
    {
        var installRoot = Path.GetFullPath(_appSettingsService.BannerlordInstallRoot);
        lock (_sync)
        {
            if (string.Equals(_indexedInstallRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _index = BuildIndex(_installationIndex.GetSnapshot(installRoot).PrefabFiles);
            _indexedInstallRoot = installRoot;
        }
    }

    private static Dictionary<string, List<PrefabReferenceCandidate>> BuildIndex(
        IEnumerable<string> prefabFiles)
    {
        var index = new Dictionary<string, List<PrefabReferenceCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in prefabFiles)
        {
            var normalizedPath = Path.GetFullPath(file);
            var referenceName = Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(referenceName))
            {
                continue;
            }

            var candidate = new PrefabReferenceCandidate(
                normalizedPath,
                Path.GetDirectoryName(normalizedPath) ?? string.Empty,
                TryGetModuleRoot(normalizedPath) ?? string.Empty);

            if (!index.TryGetValue(referenceName, out var candidates))
            {
                candidates = [];
                index[referenceName] = candidates;
            }

            candidates.Add(candidate);
        }

        return index;
    }

    private static string? TryGetModuleRoot(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var pathParts = normalizedPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var modulesIndex = Array.FindIndex(
            pathParts,
            part => string.Equals(part, "Modules", StringComparison.OrdinalIgnoreCase));
        if (modulesIndex < 0 || modulesIndex + 1 >= pathParts.Length)
        {
            return null;
        }

        return Path.Combine(pathParts.Take(modulesIndex + 2).ToArray());
    }

    private static int GetRelativeDirectoryDistance(string sourceDirectory, string targetDirectory)
    {
        try
        {
            var relative = Path.GetRelativePath(sourceDirectory, targetDirectory);
            return relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch
        {
            return int.MaxValue;
        }
    }

    private sealed record PrefabReferenceCandidate(
        string FullPath,
        string DirectoryPath,
        string ModuleRootPath);
}
