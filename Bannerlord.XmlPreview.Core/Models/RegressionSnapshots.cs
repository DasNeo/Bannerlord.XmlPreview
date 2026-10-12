using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bannerlord.XmlPreview.Models;

public static class StableWidgetId
{
    public static string CanonicalPrefab(string path)
    {
        var canonical = path.Replace('\\', '/').Trim('/').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(canonical) || canonical.Contains(':') ||
            canonical.Split('/').Any(segment => segment is ".." or "." or ""))
            throw new ArgumentException("Prefab identity must be a relative canonical path.", nameof(path));
        return canonical;
    }

    public static string Create(string prefab, string expandedPath, string instanceKey = "") =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { CanonicalPrefab(prefab), expandedPath, instanceKey }))));
}

public sealed record RenderWidgetSnapshot(
    string WidgetId, string ExpandedPath, string InstanceKey, string Kind,
    IReadOnlyDictionary<string, string> Properties, IReadOnlyList<RenderWidgetSnapshot> Children);

public sealed record RenderStructureSnapshot(int SchemaVersion, string PrefabIdentity, RenderWidgetSnapshot Root);
public sealed record WidgetBounds(string WidgetId, string ExpandedPath, double X, double Y, double Width, double Height);
public sealed record LayoutSnapshot(int SchemaVersion, IReadOnlyList<WidgetBounds> KeyBounds);

public static class SnapshotSerialization
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = false };

    public static string Serialize(RenderStructureSnapshot snapshot) => JsonSerializer.Serialize(
        snapshot with { Root = Normalize(snapshot.Root) }, Options);

    public static string Serialize(LayoutSnapshot snapshot) => JsonSerializer.Serialize(
        snapshot with { KeyBounds = Array.AsReadOnly(snapshot.KeyBounds.OrderBy(b => b.WidgetId, StringComparer.Ordinal).ToArray()) }, Options);

    private static RenderWidgetSnapshot Normalize(RenderWidgetSnapshot node) => node with
    {
        Properties = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            node.Properties.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)),
        Children = Array.AsReadOnly(node.Children.Select(Normalize).ToArray())
    };
}
