using System.Collections.Immutable;

namespace Bannerlord.XmlPreview.Models;

/// <summary>Prefab-scoped, constant-resolved properties for static visual-state previews.</summary>
public sealed record PrefabVisualDefinition(
    ImmutableDictionary<string, ImmutableDictionary<string, string>> States);
