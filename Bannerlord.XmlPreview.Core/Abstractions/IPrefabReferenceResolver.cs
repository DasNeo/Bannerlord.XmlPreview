namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IPrefabReferenceResolver
{
    PrefabReferenceResolveResult Resolve(string sourcePrefabPath, string referenceName);
}

public sealed record PrefabReferenceResolveResult(
    string ReferenceName,
    string? ResolvedPath,
    IReadOnlyList<string> CandidatePaths,
    PrefabReferenceResolveKind Kind)
{
    public bool IsResolved => !string.IsNullOrWhiteSpace(ResolvedPath);
}

public enum PrefabReferenceResolveKind
{
    NotFound = 0,
    SameDirectory = 1,
    SameModule = 2,
    UniqueGlobal = 3,
    Heuristic = 4
}
