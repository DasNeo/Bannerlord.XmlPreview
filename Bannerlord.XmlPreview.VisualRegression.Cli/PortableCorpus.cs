using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.VisualRegression.Cli;

internal sealed record CorpusNode(string ModelType, string Name, IReadOnlyDictionary<string, string> Attributes, IReadOnlyList<CorpusNode> Children);
internal sealed record PortableCorpus(int SchemaVersion, string PrefabIdentity, string OriginalSourceSha256, string ModuleVersion,
    CorpusNode Root, IReadOnlyDictionary<string, string> Constants, IReadOnlyList<string> BindingKeys,
    IReadOnlyList<ResolvedWidgetProperty> Properties, IReadOnlyList<ResolvedWidgetReference> References,
    IReadOnlyDictionary<string, SpriteDefinition?> Sprites, IReadOnlyDictionary<string, BrushDefinition?> Brushes,
    IReadOnlyList<string> LoaderDiagnostics)
{
    public PrefabDocument Document()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported portable corpus schema.");
        var properties = Properties.Select(p => p with { ParsedValue = Parsed(p) }).GroupBy(p => p.WidgetPath)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, ResolvedWidgetProperty>)g.ToDictionary(p => p.AttributeName, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var references = References.GroupBy(r => r.WidgetPath)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, ResolvedWidgetReference>)g.ToDictionary(p => p.AttributeName, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var root = CreateNode(Root);
        // Re-resolve XML ports using the current resolver; archived M1 maps omitted ClipRect, Handle and scrollbars.
        var referenceMap = new Bannerlord.XmlPreview.Services.WidgetReferenceResolver().Resolve(root, new PreviewDiagnostics());
        return new(PrefabIdentity, root, Constants, BindingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new(properties), referenceMap);
    }

    private static object? Parsed(ResolvedWidgetProperty p)
    {
        if (p.ParsedValue is not JsonElement element) return p.ParsedValue;
        return element.ValueKind switch
        {
            JsonValueKind.Number when p.ValueKind == ResolvedWidgetPropertyValueKind.Integer => element.GetInt32(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            _ => null
        };
    }

    private static PrefabNode CreateNode(CorpusNode node)
    {
        // Only model classes in our two production assemblies are eligible. No game or mod classes are instantiated.
        var assemblies = new[] { typeof(PrefabNode).Assembly, typeof(BannerlordWidgetNodeFactory).Assembly };
        var type = assemblies.Select(a => a.GetType(node.ModelType, false)).FirstOrDefault(t => t is not null)
            ?? throw new InvalidDataException("Unknown portable model type: " + node.ModelType);
        if (!typeof(PrefabNode).IsAssignableFrom(type) || type.IsAbstract)
            throw new InvalidDataException("Invalid portable model type.");
        var attributes = new Dictionary<string, string>(node.Attributes, StringComparer.OrdinalIgnoreCase);
        var children = node.Children.Select(CreateNode).ToArray();
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().Length is 2 or 3)
            ?? throw new InvalidDataException("Portable model has no supported constructor.");
        return (PrefabNode)constructor.Invoke(constructor.GetParameters().Length == 3
            ? [node.Name, attributes, children] : [attributes, children]);
    }

    public static PortableCorpus FromDocument(PrefabDocument document, string identity, RecordingAssets assets, IReadOnlyList<string> loaderDiagnostics,
        string moduleVersion)
    {
        static CorpusNode Node(PrefabNode node) => new(node.GetType().FullName!, node.Name,
            new SortedDictionary<string, string>(node.Attributes.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal), node.Children.Select(Node).ToArray());
        static IEnumerable<PrefabNode> Nodes(PrefabNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        var all = Nodes(document.RootNode).ToArray();
        var paths = all.Select(n => n.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, n.Name)).ToArray();
        return new(1, StableWidgetId.CanonicalPrefab(identity), HashFile(document.SourcePath), moduleVersion, Node(document.RootNode),
            document.Constants, document.BindingKeys.Order(StringComparer.Ordinal).ToArray(),
            paths.SelectMany(p => document.PropertyMap.GetProperties(p).Values).ToArray(),
            paths.SelectMany(p => document.ReferenceMap.GetReferences(p).Values).ToArray(), assets.Sprites, assets.Brushes, loaderDiagnostics);
    }

    private static string HashFile(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}

internal sealed class RecordingAssets(IAssetCatalog source) : IAssetCatalog
{
    public SortedDictionary<string, SpriteDefinition?> Sprites { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, BrushDefinition?> Brushes { get; } = new(StringComparer.Ordinal);
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => source.WarmUpAsync(cancellationToken);
    public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) =>
        source.WarmUpAssetsAsync(brushes, sprites, token);
    public SpriteDefinition? FindSprite(string? name)
    { var result = source.FindSprite(name); if (!string.IsNullOrWhiteSpace(name)) Sprites[name] = result; return result; }
    public BrushDefinition? FindBrush(string? name)
    { var result = source.FindBrush(name); if (!string.IsNullOrWhiteSpace(name)) Brushes[name] = result; return result; }
    public ImageSource? GetSpriteImage(string? name) => source.GetSpriteImage(name);
    public SpriteAssetResolution ResolveSpriteAsset(string? name)
    { var result = source.ResolveSpriteAsset(name); if (!string.IsNullOrWhiteSpace(name)) Sprites[name] = result.SpriteDefinition; return result; }
}

internal sealed class CorpusAssets(PortableCorpus corpus) : IAssetCatalog
{
    private readonly Dictionary<string, ImageSource> _images = new(StringComparer.OrdinalIgnoreCase);
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
    public SpriteDefinition? FindSprite(string? name) => name is null ? null : corpus.Sprites.GetValueOrDefault(name);
    public BrushDefinition? FindBrush(string? name) => name is null ? null : corpus.Brushes.GetValueOrDefault(name);
    public SpriteAssetResolution ResolveSpriteAsset(string? name)
    {
        var sprite = FindSprite(name);
        return new(name ?? "", sprite is null ? SpriteAssetResolutionKind.Missing : SpriteAssetResolutionKind.ExactName,
            sprite?.Name, sprite is null ? null : "{portable-metadata}", sprite, "Portable Structure Only fixture; image pixels are unavailable.");
    }
    public ImageSource? GetSpriteImage(string? name)
    {
        var sprite = FindSprite(name);
        if (sprite is null || sprite.Width <= 0 || sprite.Height <= 0) return null;
        if (_images.TryGetValue(name!, out var image)) return image;
        var bitmap = BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Bgra32, null,
            new byte[checked(sprite.Width * sprite.Height * 4)], sprite.Width * 4);
        bitmap.Freeze(); _images.Add(name!, bitmap); return bitmap;
    }
}
