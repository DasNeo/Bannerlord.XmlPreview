using Bannerlord.XmlPreview.Configuration;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models.Assets;
using System.IO;
using System.Globalization;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.Services;

public sealed class BannerlordAssetCatalog : IBannerlordAssetCatalog
{
    private readonly IAppSettingsService _settingsService;
    private readonly Dictionary<string, ImageSource> _spriteImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _spriteImageCacheLock = new();
    private readonly object _snapshotLock = new();
    private AssetSnapshot? _snapshot;
    private string? _snapshotInstallRoot;

    public BannerlordAssetCatalog(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public BannerlordSpriteDefinition? FindSprite(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        return TryFindSprite(GetSnapshot().Sprites, spriteName);
    }

    public BannerlordSpriteAssetResolution ResolveSpriteAsset(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return new BannerlordSpriteAssetResolution(
                string.Empty,
                SpriteAssetResolutionKind.Missing,
                null,
                null,
                null,
                "No sprite name was provided.");
        }

        var snapshot = GetSnapshot();
        var normalizedName = NormalizeSpriteName(spriteName);
        var sprite = TryFindSprite(snapshot.Sprites, normalizedName);

        foreach (var candidate in BuildImageLookupCandidates(normalizedName, sprite))
        {
            if (TryGetSpriteImagePath(snapshot.SpriteImages, candidate.Key, candidate.UseBaseName, out var imagePath))
            {
                return new BannerlordSpriteAssetResolution(
                    normalizedName,
                    candidate.Kind,
                    candidate.Key,
                    imagePath,
                    sprite,
                    candidate.Note);
            }
        }

        var note = sprite is null
            ? "No matching sprite metadata or extracted PNG was found."
            : $"Sprite metadata exists in {sprite.CategoryName} sheet {sprite.SheetId}, but no extracted PNG matched.";

        return new BannerlordSpriteAssetResolution(
            normalizedName,
            SpriteAssetResolutionKind.Missing,
            sprite?.Name ?? normalizedName,
            null,
            sprite,
            note);
    }

    public ImageSource? GetSpriteImage(string? spriteName)
    {
        var resolution = ResolveSpriteAsset(spriteName);
        return resolution.HasImage ? LoadSpriteImage(resolution.MatchedKey!, resolution.ImagePath!) : null;
    }

    public BannerlordBrushDefinition? FindBrush(string? brushName)
    {
        if (string.IsNullOrWhiteSpace(brushName))
        {
            return null;
        }

        var snapshot = GetSnapshot();
        return snapshot.Brushes.TryGetValue(brushName, out var brush) ? brush : null;
    }

    public double? ResolveBrushMetric(string? brushName, string? brushLayerName, string valueType)
    {
        var brush = FindBrush(brushName);
        if (brush is null)
        {
            return null;
        }

        var layer = brush.Layers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, brushLayerName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.SpriteName))
            ?? brush.Layers.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.SpriteName));

        var sprite = FindSprite(layer?.SpriteName);
        if (sprite is null)
        {
            return null;
        }

        return valueType switch
        {
            "Width" => Math.Max(0, sprite.Width - (layer?.ExtendLeft ?? 0) - (layer?.ExtendRight ?? 0)),
            "Height" => Math.Max(0, sprite.Height - (layer?.ExtendTop ?? 0) - (layer?.ExtendBottom ?? 0)),
            _ => null
        };
    }

    private AssetSnapshot GetSnapshot()
    {
        var installRoot = _settingsService.BannerlordInstallRoot;
        lock (_snapshotLock)
        {
            if (_snapshot is not null &&
                string.Equals(_snapshotInstallRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _snapshot;
            }

            var snapshot = LoadSnapshot(installRoot);
            _snapshot = snapshot;
            _snapshotInstallRoot = installRoot;

            lock (_spriteImageCacheLock)
            {
                _spriteImageCache.Clear();
            }

            return snapshot;
        }
    }

    private static AssetSnapshot LoadSnapshot(string installRoot)
    {
        var spriteParts = new Dictionary<string, BannerlordSpritePartDefinition>(StringComparer.OrdinalIgnoreCase);
        var sprites = new Dictionary<string, BannerlordSpriteDefinition>(StringComparer.OrdinalIgnoreCase);
        var brushes = new Dictionary<string, BannerlordBrushDefinition>(StringComparer.OrdinalIgnoreCase);
        var spriteImages = LoadSpriteImages();

        var modulesPath = Path.Combine(installRoot, "Modules");
        if (!Directory.Exists(modulesPath))
        {
            return new AssetSnapshot(sprites, brushes, spriteImages);
        }

        foreach (var spriteDataPath in Directory.EnumerateFiles(modulesPath, "*SpriteData.xml", SearchOption.AllDirectories))
        {
            LoadSpriteData(spriteDataPath, spriteParts, sprites);
        }

        foreach (var brushPath in Directory.EnumerateFiles(modulesPath, "*.xml", SearchOption.AllDirectories)
                     .Where(path => path.Contains($"{Path.DirectorySeparatorChar}GUI{Path.DirectorySeparatorChar}Brushes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            LoadBrushData(brushPath, brushes);
        }

        return new AssetSnapshot(sprites, brushes, spriteImages);
    }

    private ImageSource LoadSpriteImage(string cacheKey, string imagePath)
    {
        lock (_spriteImageCacheLock)
        {
            if (_spriteImageCache.TryGetValue(cacheKey, out var cachedImage))
            {
                return cachedImage;
            }
        }

        BitmapSource bitmap;
        using (var stream = File.OpenRead(imagePath))
        {
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            bitmap = decoder.Frames[0];
        }

        if (bitmap.Format != PixelFormats.Pbgra32)
        {
            bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        }

        bitmap.Freeze();

        lock (_spriteImageCacheLock)
        {
            _spriteImageCache[cacheKey] = bitmap;
        }

        return bitmap;
    }

    private static SpriteImageIndex LoadSpriteImages()
    {
        var exactMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baseNameCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateSpriteImageRoots())
        {
            IndexManifest(root, exactMatches, baseNameCandidates);
            IndexPngFiles(root, exactMatches, baseNameCandidates);
        }

        var uniqueBaseNameMatches = baseNameCandidates
            .Where(entry => entry.Value.Count == 1)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Single(),
                StringComparer.OrdinalIgnoreCase);

        return new SpriteImageIndex(exactMatches, uniqueBaseNameMatches);
    }

    private static IEnumerable<string> EnumerateSpriteImageRoots()
    {
        var roots = new List<string>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateNamedRoots("res"))
        {
            if (Directory.Exists(candidate))
            {
                var fullPath = Path.GetFullPath(candidate);
                if (seenRoots.Add(fullPath))
                {
                    roots.Add(fullPath);
                }
            }
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateNamedRoots(string directoryName)
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null)
            {
                yield return Path.Combine(current.FullName, directoryName);
                current = current.Parent;
            }
        }
    }

    private static void IndexManifest(
        string root,
        Dictionary<string, string> exactMatches,
        Dictionary<string, HashSet<string>> baseNameCandidates)
    {
        var manifestPath = Path.Combine(root, "_manifest.csv");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        foreach (var line in File.ReadLines(manifestPath).Skip(1))
        {
            var parts = line.Split(',', 7);
            if (parts.Length < 6)
            {
                continue;
            }

            var spriteName = NormalizeSpriteName(parts[0]);
            var imagePath = parts[5];
            if (!Path.IsPathRooted(imagePath))
            {
                imagePath = Path.GetFullPath(Path.Combine(root, imagePath));
            }

            AddExactMatch(exactMatches, spriteName, imagePath);
            RegisterBaseNameCandidate(baseNameCandidates, spriteName, imagePath);
        }
    }

    private static void IndexPngFiles(
        string root,
        Dictionary<string, string> exactMatches,
        Dictionary<string, HashSet<string>> baseNameCandidates)
    {
        foreach (var filePath in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, filePath);
            var relativeName = NormalizeSpriteName(Path.ChangeExtension(relativePath, null));
            AddExactMatch(exactMatches, relativeName, filePath);
            RegisterBaseNameCandidate(baseNameCandidates, relativeName, filePath);
        }
    }

    private static void AddExactMatch(Dictionary<string, string> exactMatches, string key, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(key) || !File.Exists(imagePath) || exactMatches.ContainsKey(key))
        {
            return;
        }

        exactMatches[key] = imagePath;
    }

    private static void RegisterBaseNameCandidate(
        Dictionary<string, HashSet<string>> baseNameCandidates,
        string key,
        string imagePath)
    {
        var baseName = Path.GetFileName(key);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return;
        }

        if (!baseNameCandidates.TryGetValue(baseName, out var paths))
        {
            paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            baseNameCandidates[baseName] = paths;
        }

        paths.Add(imagePath);
    }

    private static IEnumerable<ImageLookupCandidate> BuildImageLookupCandidates(
        string normalizedName,
        BannerlordSpriteDefinition? sprite)
    {
        yield return new ImageLookupCandidate(
            normalizedName,
            SpriteAssetResolutionKind.ExactName,
            false,
            null);

        if (sprite is not null &&
            !string.Equals(sprite.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ImageLookupCandidate(
                sprite.Name,
                SpriteAssetResolutionKind.SpriteDefinitionName,
                false,
                $"Used catalog sprite name '{sprite.Name}'.");
        }

        if (sprite is not null &&
            !string.Equals(sprite.SpritePartName, normalizedName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sprite.SpritePartName, sprite.Name, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ImageLookupCandidate(
                sprite.SpritePartName,
                SpriteAssetResolutionKind.SpritePartName,
                false,
                $"Used sprite part '{sprite.SpritePartName}'.");
        }

        var baseName = Path.GetFileName(normalizedName);
        if (!string.Equals(baseName, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ImageLookupCandidate(
                baseName,
                SpriteAssetResolutionKind.BaseNameFallback,
                true,
                $"Used unique base-name fallback '{baseName}'.");
        }
    }

    private static bool TryGetSpriteImagePath(SpriteImageIndex index, string key, bool useBaseName, out string imagePath)
    {
        if (!useBaseName && index.ExactMatches.TryGetValue(key, out imagePath!))
        {
            return true;
        }

        if (useBaseName && index.UniqueBaseNameMatches.TryGetValue(key, out imagePath!))
        {
            return true;
        }

        imagePath = null!;
        return false;
    }

    private static BannerlordSpriteDefinition? TryFindSprite(
        IReadOnlyDictionary<string, BannerlordSpriteDefinition> sprites,
        string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        var normalizedName = NormalizeSpriteName(spriteName);
        if (sprites.TryGetValue(normalizedName, out var sprite))
        {
            return sprite;
        }

        var suffixMatches = sprites.Values
            .Where(candidate => candidate.Name.EndsWith(normalizedName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return suffixMatches.Length == 1 ? suffixMatches[0] : null;
    }

    private static string NormalizeSpriteName(string? spriteName)
    {
        return (spriteName ?? string.Empty).Trim().Replace('/', '\\');
    }

    private static void LoadSpriteData(
        string spriteDataPath,
        Dictionary<string, BannerlordSpritePartDefinition> spriteParts,
        Dictionary<string, BannerlordSpriteDefinition> sprites)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(spriteDataPath, LoadOptions.None);
        }
        catch
        {
            return;
        }

        var root = document.Root;
        if (root is null)
        {
            return;
        }

        foreach (var spritePartElement in root.Descendants("SpritePart"))
        {
            var name = spritePartElement.Element("Name")?.Value;
            var categoryName = spritePartElement.Element("CategoryName")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(categoryName))
            {
                continue;
            }

            var part = new BannerlordSpritePartDefinition(
                name,
                spriteDataPath,
                categoryName,
                ParseInt(spritePartElement.Element("SheetID")?.Value),
                ParseInt(spritePartElement.Element("SheetX")?.Value),
                ParseInt(spritePartElement.Element("SheetY")?.Value),
                ParseInt(spritePartElement.Element("Width")?.Value),
                ParseInt(spritePartElement.Element("Height")?.Value));

            spriteParts[name] = part;
            sprites[name] = new BannerlordSpriteDefinition(
                part.Name,
                spriteDataPath,
                "SpritePart",
                part.Name,
                part.CategoryName,
                part.SheetId,
                part.Width,
                part.Height,
                null,
                null,
                null,
                null);
        }

        var spritesElement = root.Element("Sprites");
        if (spritesElement is null)
        {
            return;
        }

        foreach (var spriteElement in spritesElement.Elements())
        {
            var name = spriteElement.Element("Name")?.Value;
            var spritePartName = spriteElement.Element("SpritePartName")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(spritePartName))
            {
                continue;
            }

            if (!spriteParts.TryGetValue(spritePartName, out var spritePart))
            {
                continue;
            }

            sprites[name] = new BannerlordSpriteDefinition(
                name,
                spriteDataPath,
                spriteElement.Name.LocalName,
                spritePartName,
                spritePart.CategoryName,
                spritePart.SheetId,
                spritePart.Width,
                spritePart.Height,
                ParseNullableInt(spriteElement.Element("LeftWidth")?.Value),
                ParseNullableInt(spriteElement.Element("RightWidth")?.Value),
                ParseNullableInt(spriteElement.Element("TopHeight")?.Value),
                ParseNullableInt(spriteElement.Element("BottomHeight")?.Value));
        }
    }

    private static void LoadBrushData(string brushPath, Dictionary<string, BannerlordBrushDefinition> brushes)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(brushPath, LoadOptions.None);
        }
        catch
        {
            return;
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Brushes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var brushElement in root.Elements("Brush"))
        {
            var name = brushElement.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var layers = brushElement.Element("Layers")?
                .Elements("BrushLayer")
                .Select(ParseBrushLayer)
                .ToArray() ?? [];

            var styles = brushElement.Element("Styles")?
                .Elements("Style")
                .Select(ParseBrushStyle)
                .Aggregate(
                    new Dictionary<string, BannerlordBrushStyleDefinition>(StringComparer.OrdinalIgnoreCase),
                    (map, style) =>
                    {
                        map[style.Name] = style;
                        return map;
                    })
                ?? new Dictionary<string, BannerlordBrushStyleDefinition>(StringComparer.OrdinalIgnoreCase);

            brushes[name] = new BannerlordBrushDefinition(
                name,
                brushPath,
                layers,
                styles,
                brushElement.Attribute("BaseBrush")?.Value,
                brushElement.Attribute("Font")?.Value,
                brushElement.Attribute("TextHorizontalAlignment")?.Value,
                brushElement.Attribute("TextVerticalAlignment")?.Value);
        }
    }

    private static BannerlordBrushStyleDefinition ParseBrushStyle(XElement styleElement)
    {
        var layerOverrides = styleElement.Elements("BrushLayer")
            .Select(ParseBrushLayer)
            .Aggregate(
                new Dictionary<string, BannerlordBrushLayerDefinition>(StringComparer.OrdinalIgnoreCase),
                (map, layer) =>
                {
                    map[layer.Name] = layer;
                    return map;
                });

        return new BannerlordBrushStyleDefinition(
            styleElement.Attribute("Name")?.Value ?? "Default",
            styleElement.Attribute("FontColor")?.Value,
            ParseDouble(styleElement.Attribute("FontSize")?.Value),
            ParseDouble(styleElement.Attribute("TextAlphaFactor")?.Value),
            layerOverrides);
    }

    private static BannerlordBrushLayerDefinition ParseBrushLayer(XElement layerElement)
    {
        return new BannerlordBrushLayerDefinition(
            layerElement.Attribute("Name")?.Value ?? string.Empty,
            layerElement.Attribute("Sprite")?.Value,
            layerElement.Attribute("OverlaySprite")?.Value,
            layerElement.Attribute("OverlayMethod")?.Value,
            layerElement.Attribute("Color")?.Value,
            ParseDouble(layerElement.Attribute("ColorFactor")?.Value),
            ParseDouble(layerElement.Attribute("AlphaFactor")?.Value),
            ParseDouble(layerElement.Attribute("SaturationFactor")?.Value),
            ParseDouble(layerElement.Attribute("ValueFactor")?.Value),
            ParseDouble(layerElement.Attribute("HueFactor")?.Value),
            ParseBool(layerElement.Attribute("IsHidden")?.Value),
            ParseDouble(layerElement.Attribute("ExtendLeft")?.Value),
            ParseDouble(layerElement.Attribute("ExtendRight")?.Value),
            ParseDouble(layerElement.Attribute("ExtendTop")?.Value),
            ParseDouble(layerElement.Attribute("ExtendBottom")?.Value),
            ParseBool(layerElement.Attribute("HorizontalFlip")?.Value),
            ParseBool(layerElement.Attribute("VerticalFlip")?.Value));
    }

    private static int ParseInt(string? rawValue)
    {
        return int.TryParse(rawValue, out var value) ? value : 0;
    }

    private static int? ParseNullableInt(string? rawValue)
    {
        return int.TryParse(rawValue, out var value) ? value : null;
    }

    private static double? ParseDouble(string? rawValue)
    {
        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool? ParseBool(string? rawValue)
    {
        return bool.TryParse(rawValue, out var value) ? value : null;
    }

    private sealed record AssetSnapshot(
        IReadOnlyDictionary<string, BannerlordSpriteDefinition> Sprites,
        IReadOnlyDictionary<string, BannerlordBrushDefinition> Brushes,
        SpriteImageIndex SpriteImages);

    private sealed record SpriteImageIndex(
        IReadOnlyDictionary<string, string> ExactMatches,
        IReadOnlyDictionary<string, string> UniqueBaseNameMatches);

    private sealed record ImageLookupCandidate(
        string Key,
        SpriteAssetResolutionKind Kind,
        bool UseBaseName,
        string? Note);
}
