using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using System.IO;
using System.Globalization;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.Services;

public sealed class BannerlordAssetCatalog : IAssetCatalog
{
    private readonly IAppSettingsService _settingsService;
    private readonly IBannerlordInstallationIndex _installationIndex;
    private readonly Dictionary<string, ImageSource> _spriteImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _spriteImageCacheLock = new();
    private readonly object _snapshotLock = new();
    private AssetSnapshot? _snapshot;
    private string? _snapshotInstallRoot;

    public BannerlordAssetCatalog(
        IAppSettingsService settingsService,
        IBannerlordInstallationIndex installationIndex)
    {
        _settingsService = settingsService;
        _installationIndex = installationIndex;
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = GetSnapshot();
            },
            cancellationToken);
    }

    public Task WarmUpAssetsAsync(
        IReadOnlyCollection<string> brushNames,
        IReadOnlyCollection<string> spriteNames,
        CancellationToken cancellationToken = default)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(
                () => WarmUpAssets(brushNames, spriteNames, cancellationToken),
                cancellationToken);
        }
    }

    private void WarmUpAssets(
        IEnumerable<string> brushNames,
        IEnumerable<string> spriteNames,
        CancellationToken cancellationToken)
    {
        var snapshot = GetSnapshot();
        var requiredSprites = new HashSet<string>(
            spriteNames.Where(IsStaticAssetName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var brushName in brushNames.Where(IsStaticAssetName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!snapshot.Brushes.TryGetValue(brushName, out var brush)) continue;

            foreach (var layer in brush.Layers)
            {
                AddSpriteName(requiredSprites, layer.SpriteName);
                AddSpriteName(requiredSprites, layer.OverlaySpriteName);
            }

            foreach (var style in brush.Styles.Values)
            {
                foreach (var layer in style.LayerOverrides.Values)
                {
                    AddSpriteName(requiredSprites, layer.SpriteName);
                    AddSpriteName(requiredSprites, layer.OverlaySpriteName);
                }
            }
        }

        foreach (var spriteName in requiredSprites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _ = GetSpriteImage(spriteName);
            }
            catch (IOException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }
    }

    private static void AddSpriteName(ISet<string> spriteNames, string? spriteName)
    {
        if (IsStaticAssetName(spriteName)) spriteNames.Add(spriteName!);
    }

    private static bool IsStaticAssetName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value[0] is not ('@' or '!' or '*');
    }

    public SpriteDefinition? FindSprite(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        return TryFindSprite(GetSnapshot().Sprites, spriteName);
    }

    public SpriteAssetResolution ResolveSpriteAsset(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return new SpriteAssetResolution(
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
                return new SpriteAssetResolution(
                    normalizedName,
                    candidate.Kind,
                    candidate.Key,
                    imagePath,
                    sprite,
                    candidate.Note);
            }
        }

        if (snapshot.SpriteImages.EnsureModuleSpriteImagesIndexed())
        {
            foreach (var candidate in BuildImageLookupCandidates(normalizedName, sprite))
            {
                if (TryGetSpriteImagePath(snapshot.SpriteImages, candidate.Key, candidate.UseBaseName, out var imagePath))
                {
                    return new SpriteAssetResolution(
                        normalizedName,
                        candidate.Kind,
                        candidate.Key,
                        imagePath,
                        sprite,
                        candidate.Note);
                }
            }
        }

        var note = sprite is null
            ? "No matching sprite metadata or extracted PNG was found."
            : $"Sprite metadata exists in {sprite.CategoryName} sheet {sprite.SheetId}, but no extracted PNG matched.";

        return new SpriteAssetResolution(
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

    public BrushDefinition? FindBrush(string? brushName)
    {
        if (string.IsNullOrWhiteSpace(brushName))
        {
            return null;
        }

        var snapshot = GetSnapshot();
        return snapshot.Brushes.TryGetValue(brushName, out var brush) ? brush : null;
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

    private AssetSnapshot LoadSnapshot(string installRoot)
    {
        using var totalTiming = PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSnapshot.Total");
        var spriteParts = new Dictionary<string, SpritePartDefinition>(StringComparer.OrdinalIgnoreCase);
        var sprites = new Dictionary<string, SpriteDefinition>(StringComparer.OrdinalIgnoreCase);
        var brushes = new Dictionary<string, BrushDefinition>(StringComparer.OrdinalIgnoreCase);
        var spriteImages = LoadSpriteImages(installRoot);

        var installationSnapshot = _installationIndex.GetSnapshot(installRoot);

        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSnapshot.SpriteDataFiles"))
        {
            foreach (var spriteDataPath in installationSnapshot.SpriteDataFiles)
            {
                LoadSpriteData(spriteDataPath, spriteParts, sprites);
            }
        }

        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSnapshot.BrushXmlFiles"))
        {
            foreach (var brushPath in installationSnapshot.BrushFiles)
            {
                LoadBrushData(brushPath, brushes);
            }
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
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            source.StreamSource = stream;
            source.EndInit();
            source.Freeze();
            bitmap = source;
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

    private static SpriteImageIndex LoadSpriteImages(string installRoot)
    {
        using var totalTiming = PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSpriteImages.Total");
        var exactMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baseNameCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSpriteImages.BaseRoots"))
        {
            foreach (var root in EnumerateSpriteImageRoots())
            {
                if (!IndexManifest(root, exactMatches, baseNameCandidates))
                    IndexPngFiles(root, exactMatches, baseNameCandidates);
            }
        }

        return new SpriteImageIndex(installRoot, exactMatches, baseNameCandidates);
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

    private static bool IndexManifest(
        string root,
        Dictionary<string, string> exactMatches,
        Dictionary<string, HashSet<string>> baseNameCandidates)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSpriteImages.IndexManifest");
        var manifestPath = Path.Combine(root, "_manifest.csv");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var indexedAny = false;
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
            indexedAny = true;
        }

        return indexedAny;
    }

    private static void IndexPngFiles(
        string root,
        Dictionary<string, string> exactMatches,
        Dictionary<string, HashSet<string>> baseNameCandidates)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSpriteImages.IndexExtractedPngFiles");
        foreach (var filePath in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, filePath);
            var relativeName = NormalizeSpriteName(Path.ChangeExtension(relativePath, null));
            AddExactMatch(exactMatches, relativeName, filePath);
            RegisterBaseNameCandidate(baseNameCandidates, relativeName, filePath);
        }
    }

    private static void IndexModulePngFiles(
        string installRoot,
        Dictionary<string, string> exactMatches,
        Dictionary<string, HashSet<string>> baseNameCandidates)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase("BannerlordAssetCatalog.LoadSpriteImages.IndexModulePngFiles");
        var modulesPath = Path.Combine(installRoot, "Modules");
        if (!Directory.Exists(modulesPath))
        {
            return;
        }

        foreach (var modulePath in Directory.EnumerateDirectories(modulesPath))
        {
            foreach (var filePath in Directory.EnumerateFiles(modulePath, "*.png", SearchOption.AllDirectories))
            {
                foreach (var key in GetModuleSpriteKeys(modulePath, modulesPath, filePath))
                {
                    AddExactMatch(exactMatches, key, filePath, replaceMissing: true);
                    RegisterBaseNameCandidate(
                        baseNameCandidates,
                        key,
                        filePath,
                        removeMissingCandidates: true);
                }
            }
        }
    }

    private static void AddExactMatch(
        Dictionary<string, string> exactMatches,
        string key,
        string imagePath,
        bool replaceMissing = false)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (exactMatches.TryGetValue(key, out var existingPath) &&
            (!replaceMissing || File.Exists(existingPath)))
            return;

        exactMatches[key] = imagePath;
    }

    private static void RegisterBaseNameCandidate(
        Dictionary<string, HashSet<string>> baseNameCandidates,
        string key,
        string imagePath,
        bool removeMissingCandidates = false)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

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
        else if (removeMissingCandidates)
        {
            paths.RemoveWhere(path => !File.Exists(path));
        }

        paths.Add(imagePath);
    }

    private static IEnumerable<string> GetModuleSpriteKeys(string modulePath, string modulesPath, string filePath)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in EnumerateModuleSpriteKeys(modulePath, modulesPath, filePath, seenKeys))
        {
            yield return key;
        }
    }

    private static IEnumerable<string> EnumerateModuleSpriteKeys(
        string modulePath,
        string modulesPath,
        string filePath,
        HashSet<string> seenKeys)
    {
        if (TryNormalizeRelativeSpriteKey(modulePath, filePath, out var moduleRelativeKey) &&
            seenKeys.Add(moduleRelativeKey))
        {
            yield return moduleRelativeKey;
        }

        if (TryNormalizeRelativeSpriteKey(modulesPath, filePath, out var modulesRelativeKey) &&
            seenKeys.Add(modulesRelativeKey))
        {
            yield return modulesRelativeKey;
        }

        var guiIndex = filePath.IndexOf(
            $"{Path.DirectorySeparatorChar}GUI{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
        if (guiIndex >= 0)
        {
            var guiRoot = filePath[..(guiIndex + 1)];
            if (TryNormalizeRelativeSpriteKey(guiRoot, filePath, out var guiRelativeKey) &&
                seenKeys.Add(guiRelativeKey))
            {
                yield return guiRelativeKey;
            }
        }

        var spritePartsMarker = $"{Path.DirectorySeparatorChar}GUI{Path.DirectorySeparatorChar}SpriteParts{Path.DirectorySeparatorChar}";
        var spritePartsIndex = filePath.IndexOf(spritePartsMarker, StringComparison.OrdinalIgnoreCase);
        if (spritePartsIndex >= 0)
        {
            var spritePartsRoot = filePath[..(spritePartsIndex + spritePartsMarker.Length)];
            if (TryNormalizeRelativeSpriteKey(spritePartsRoot, filePath, out var spritePartsRelativeKey) &&
                seenKeys.Add(spritePartsRelativeKey))
            {
                yield return spritePartsRelativeKey;
            }
        }
    }

    private static bool TryNormalizeRelativeSpriteKey(string rootPath, string filePath, out string key)
    {
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(rootPath, filePath);
        }
        catch
        {
            return false;
        }

        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        key = NormalizeSpriteName(Path.ChangeExtension(relativePath, null));
        return !string.IsNullOrWhiteSpace(key);
    }

    private static IEnumerable<ImageLookupCandidate> BuildImageLookupCandidates(
        string normalizedName,
        SpriteDefinition? sprite)
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
        if (!string.IsNullOrWhiteSpace(baseName))
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
        if (!useBaseName &&
            index.ExactMatches.TryGetValue(key, out imagePath!) &&
            File.Exists(imagePath))
        {
            return true;
        }

        if (useBaseName &&
            index.UniqueBaseNameMatches.TryGetValue(key, out imagePath!) &&
            File.Exists(imagePath))
        {
            return true;
        }

        imagePath = null!;
        return false;
    }

    private static SpriteDefinition? TryFindSprite(
        IReadOnlyDictionary<string, SpriteDefinition> sprites,
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
        Dictionary<string, SpritePartDefinition> spriteParts,
        Dictionary<string, SpriteDefinition> sprites)
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

            var part = new SpritePartDefinition(
                name,
                spriteDataPath,
                categoryName,
                ParseInt(spritePartElement.Element("SheetID")?.Value),
                ParseInt(spritePartElement.Element("SheetX")?.Value),
                ParseInt(spritePartElement.Element("SheetY")?.Value),
                ParseInt(spritePartElement.Element("Width")?.Value),
                ParseInt(spritePartElement.Element("Height")?.Value));

            spriteParts[name] = part;
            sprites[name] = new SpriteDefinition(
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

            sprites[name] = new SpriteDefinition(
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

    private static void LoadBrushData(string brushPath, Dictionary<string, BrushDefinition> brushes)
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
                    new Dictionary<string, BrushStyleDefinition>(StringComparer.OrdinalIgnoreCase),
                    (map, style) =>
                    {
                        map[style.Name] = style;
                        return map;
                    })
                ?? new Dictionary<string, BrushStyleDefinition>(StringComparer.OrdinalIgnoreCase);

            brushes[name] = new BrushDefinition(
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

    private static BrushStyleDefinition ParseBrushStyle(XElement styleElement)
    {
        var layerOverrides = styleElement.Elements("BrushLayer")
            .Select(ParseBrushLayer)
            .Aggregate(
                new Dictionary<string, BrushLayerDefinition>(StringComparer.OrdinalIgnoreCase),
                (map, layer) =>
                {
                    map[layer.Name] = layer;
                    return map;
                });

        return new BrushStyleDefinition(
            styleElement.Attribute("Name")?.Value ?? "Default",
            styleElement.Attribute("FontColor")?.Value,
            ParseDouble(styleElement.Attribute("FontSize")?.Value),
            ParseDouble(styleElement.Attribute("TextAlphaFactor")?.Value),
            layerOverrides);
    }

    private static BrushLayerDefinition ParseBrushLayer(XElement layerElement)
    {
        return new BrushLayerDefinition(
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
        IReadOnlyDictionary<string, SpriteDefinition> Sprites,
        IReadOnlyDictionary<string, BrushDefinition> Brushes,
        SpriteImageIndex SpriteImages);

    private sealed class SpriteImageIndex
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, HashSet<string>> _baseNameCandidates;
        private Dictionary<string, string> _uniqueBaseNameMatches;
        private bool _moduleSpriteImagesIndexed;

        public SpriteImageIndex(
            string installRoot,
            Dictionary<string, string> exactMatches,
            Dictionary<string, HashSet<string>> baseNameCandidates)
        {
            InstallRoot = installRoot;
            ExactMatches = exactMatches;
            _baseNameCandidates = baseNameCandidates;
            _uniqueBaseNameMatches = BuildUniqueBaseNameMatches(baseNameCandidates);
        }

        public string InstallRoot { get; }

        public Dictionary<string, string> ExactMatches { get; }

        public IReadOnlyDictionary<string, string> UniqueBaseNameMatches => _uniqueBaseNameMatches;

        public bool EnsureModuleSpriteImagesIndexed()
        {
            lock (_sync)
            {
                if (_moduleSpriteImagesIndexed)
                {
                    return false;
                }

                IndexModulePngFiles(InstallRoot, ExactMatches, _baseNameCandidates);
                _uniqueBaseNameMatches = BuildUniqueBaseNameMatches(_baseNameCandidates);
                _moduleSpriteImagesIndexed = true;
                return true;
            }
        }

        private static Dictionary<string, string> BuildUniqueBaseNameMatches(
            IReadOnlyDictionary<string, HashSet<string>> baseNameCandidates)
        {
            return baseNameCandidates
                .Where(entry => entry.Value.Count == 1)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Single(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record ImageLookupCandidate(
        string Key,
        SpriteAssetResolutionKind Kind,
        bool UseBaseName,
        string? Note);
}
