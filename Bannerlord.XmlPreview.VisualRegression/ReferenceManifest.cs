using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record ReferenceEntry(string PrefabIdentity, string ScenarioSha256, string Scene, string DataState,
    string StructureFile, string StructureSha256, string LayoutFile, string LayoutSha256,
    string? ReferencePng, string? ReferenceSha256);

public sealed record ReferenceManifest(int SchemaVersion, string GameVersion, string ModuleVersion,
    CaptureProfile CaptureProfile, PixelTolerance? GlobalPixelTolerance, IReadOnlyDictionary<string, ReferenceEntry> Cases);

internal sealed class ReferenceStore(string metadataRoot, string? packageRoot)
{
    public ReferenceManifest Load(ReferenceMetadata metadata)
    {
        var manifest = JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(Path.Combine(metadataRoot, "manifest.json")), ScenarioLoader.JsonOptions)
            ?? throw new InvalidDataException("Null reference manifest.");
        if (manifest.SchemaVersion != 1 || metadata.PackageSchemaVersion != manifest.SchemaVersion ||
            manifest.GameVersion != metadata.GameVersion || manifest.ModuleVersion != metadata.ModuleVersion ||
            manifest.CaptureProfile.Id != metadata.CaptureProfileId || manifest.Cases is null)
            throw new InvalidDataException("Reference Package version or Capture Profile mismatch.");
        if (manifest.GlobalPixelTolerance is not null) ScenarioLoader.ValidateTolerance(manifest.GlobalPixelTolerance);
        return manifest;
    }

    public (RenderStructureSnapshot Structure, LayoutSnapshot Layout) ReadSnapshots(ReferenceEntry entry)
    {
        var structure = ReadVerified(metadataRoot, entry.StructureFile, entry.StructureSha256);
        var layout = ReadVerified(metadataRoot, entry.LayoutFile, entry.LayoutSha256);
        return (JsonSerializer.Deserialize<RenderStructureSnapshot>(structure, SnapshotSerialization.Options) ?? throw new InvalidDataException("Null structure."),
            JsonSerializer.Deserialize<LayoutSnapshot>(layout, SnapshotSerialization.Options) ?? throw new InvalidDataException("Null layout."));
    }

    public string ReadReferencePath(ReferenceEntry entry)
    {
        if (packageRoot is null || entry.ReferencePng is null || entry.ReferenceSha256 is null)
            throw new FileNotFoundException("Full comparison requires a separately supplied Reference Screenshot.");
        var path = ResolveInside(packageRoot, entry.ReferencePng);
        VerifyHash(path, entry.ReferenceSha256);
        return path;
    }

    public void VerifyPackage(ReferenceManifest manifest, VisualRegressionMode mode)
    {
        foreach (var entry in manifest.Cases.Values)
        {
            VerifyHash(ResolveInside(metadataRoot, entry.StructureFile), entry.StructureSha256);
            VerifyHash(ResolveInside(metadataRoot, entry.LayoutFile), entry.LayoutSha256);
            if (mode == VisualRegressionMode.Full && packageRoot is not null && entry.ReferencePng is not null)
                VerifyHash(ResolveInside(packageRoot, entry.ReferencePng), entry.ReferenceSha256 ?? throw new InvalidDataException("Missing screenshot hash."));
        }
    }

    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void VerifyHash(string path, string expected)
    {
        if (!string.Equals(Hash(path), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reference Package hash mismatch: " + Path.GetFileName(path));
    }
    internal static string ResolveInside(string root, string relative)
    {
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(absoluteRoot, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reference path escapes the package root.");
        return path;
    }
    private static string ReadVerified(string root, string relative, string hash)
    {
        var path = ResolveInside(root, relative); VerifyHash(path, hash); return File.ReadAllText(path);
    }
}
