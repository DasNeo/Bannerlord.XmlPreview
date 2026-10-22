using System.IO;
using System.Globalization;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Assets;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Services;

public sealed class BannerlordAssetCatalog : IAssetCatalog
{
    private readonly IAppSettingsService settings;
    private readonly IBannerlordInstallationIndex index;
    private readonly TpacAtlasStore atlases;
    public AtlasDecodeStatistics DecodeStatistics => atlases.Statistics;
    private readonly ConditionalWeakTable<BannerlordInstallationSnapshot, ConcurrentDictionary<string, Lazy<Snapshot>>> scopes;
    private readonly string? prefab;
    private readonly string? pngOverrideRoot;
    private string? assetModule;
    private readonly ConcurrentDictionary<string, byte> usedFiles = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> Dependencies
    {
        get
        {
            var installation = index.GetSnapshot(settings.BannerlordInstallRoot);
            var order = GetSnapshot().Order;
            return installation.BrushFiles.Concat(installation.SpriteDataFiles)
                .Where(path => order.Any(module => path.StartsWith(module.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Concat(order.Select(module => Path.Combine(module.Directory, "SubModule.xml")))
                .Concat(usedFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
    public void Invalidate() { index.Invalidate(); scopes.Clear(); atlases.Invalidate(); fonts.Clear(); }
    public BannerlordAssetCatalog(IAppSettingsService settingsService, IBannerlordInstallationIndex installationIndex)
        : this(settingsService, installationIndex, new TpacAtlasStore(), null, null) { }
    public BannerlordAssetCatalog(IAppSettingsService settingsService, IBannerlordInstallationIndex installationIndex,
        string explicitPngOverrideRoot) : this(settingsService, installationIndex, new TpacAtlasStore(), null, explicitPngOverrideRoot) { }
    private BannerlordAssetCatalog(IAppSettingsService settings, IBannerlordInstallationIndex index,
        TpacAtlasStore atlases, string? prefab, string? pngOverrideRoot,
        ConditionalWeakTable<BannerlordInstallationSnapshot, ConcurrentDictionary<string, Lazy<Snapshot>>>? scopes = null)
    {
        this.settings = settings; this.index = index; this.atlases = atlases; this.prefab = prefab; this.pngOverrideRoot = pngOverrideRoot;
        this.scopes = scopes ?? new();
    }
    public IAssetCatalog ForPrefab(string path) => new BannerlordAssetCatalog(settings, index, atlases, path, pngOverrideRoot, scopes);
    public IAssetCatalog ForPrefab(string path, string? module) => new BannerlordAssetCatalog(settings, index, atlases, path, pngOverrideRoot, scopes) { assetModule = module };
    public Task WarmUpAsync(CancellationToken token = default) => Task.Run(() => { token.ThrowIfCancellationRequested(); _ = GetSnapshot(); }, token);
    public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default)
        => Task.Run(() =>
        {
            var names = new HashSet<string>(sprites, StringComparer.OrdinalIgnoreCase);
            foreach (var name in brushes)
            {
                token.ThrowIfCancellationRequested();
                var brush = FindBrush(name);
                if (brush is null) continue;
                foreach (var layer in brush.Layers.Concat(brush.Styles.Values.SelectMany(s => s.LayerOverrides.Values)))
                {
                    if (layer.SpriteName is { } sprite) names.Add(sprite);
                    if (layer.OverlaySpriteName is { } overlay) names.Add(overlay);
                }
            }
            // Finish each atlas before moving on, avoiding eviction and re-decode during warm-up.
            foreach (var name in names.Where(n => n.Length > 0 && n[0] is not ('@' or '!' or '*'))
                .OrderBy(n => FindSprite(n) is { } sprite ? sprite.CategoryName + "_" + sprite.SheetId : n, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                _ = ResolveImage(new(name));
            }
        }, token);
    public SpriteDefinition? FindSprite(string? name) => GetSnapshot().Sprites.GetValueOrDefault(Normalize(name));
    public BrushDefinition? FindBrush(string? name) => name is null ? null : GetSnapshot().Brushes.GetValueOrDefault(name);
    private readonly Dictionary<string, BitmapFontDefinition?> fonts = new(StringComparer.OrdinalIgnoreCase);
    public BitmapFontDefinition? FindFont(string name)
    {
        if (fonts.TryGetValue(name, out var cached)) return cached;
        // Font identities are names, never file paths supplied by a prefab.
        if (name.IndexOfAny(['/', '\\', ':']) >= 0) return null;
        var path = Path.Combine(settings.BannerlordInstallRoot, "GUI/GauntletUI/Fonts", name, name + ".fnt");
        if (!File.Exists(path)) return fonts[name] = null;
        usedFiles[path] = 0;
        var xml = XDocument.Load(path).Root!;
        double Number(XElement e, string key, double fallback = 0) => double.TryParse(e.Attribute(key)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        var texture = ResolveImage(new(Path.GetFileNameWithoutExtension(xml.Element("pages")!.Element("page")!.Attribute("file")!.Value)));
        if (texture.Image is null) return fonts[name] = null;
        var glyphs = xml.Element("chars")!.Elements("char").Select(e => new BitmapFontGlyph((int)Number(e, "id"),
            (int)Number(e, "x"), (int)Number(e, "y"), (int)Number(e, "width"), (int)Number(e, "height"),
            Number(e, "xadvance"), Number(e, "xoffset"), Number(e, "yoffset"))).ToDictionary(g => g.Id);
        var kern = xml.Element("kernings")?.Elements("kerning").ToDictionary(e => ((int)Number(e, "first"), (int)Number(e, "second")), e => Number(e, "amount")) ?? new();
        return fonts[name] = new(name, Number(xml.Element("info")!, "size", 32), Number(xml.Element("common")!, "lineHeight", 36),
            Number(xml.Element("info")!, "smoothingConstant", 0.5), texture.Image, glyphs, kern);
    }
    public ImageSource? GetSpriteImage(string? name) => ResolveImage(new(name ?? "")).Image;
    public SpriteAssetResolution ResolveSpriteAsset(string? name)
    {
        var result = ResolveImage(new(name ?? ""));
        SpriteDefinition? sprite = null;
        if (result.Status != ImageResolutionStatus.Invalid) sprite = FindSprite(name);
        return new(name ?? "", result.Status == ImageResolutionStatus.Resolved ? SpriteAssetResolutionKind.ExactName : SpriteAssetResolutionKind.Missing,
            sprite?.Name ?? name, result.SourcePath, sprite, result.Reason) { ImageResolution = result };
    }
    public ImageResolution ResolveImage(ImageRequest request)
    {
        if (!string.IsNullOrEmpty(request.Provider) && request.Provider != "Preview.Sprite" && request.ScenarioSprite is null)
            return new(request, ImageResolutionStatus.RuntimeOnly, null, Reason: $"Provider '{request.Provider}' requires game runtime data; supply explicit scenario image data.");
        var name = Normalize(request.ScenarioSprite ?? request.Identity);
        var attempts = new List<string>();
        var failure = ImageFailureKind.InvalidMetadata;
        try
        {
            var data = GetSnapshot();
            attempts.AddRange(data.UnavailableDependencies);
            if (name.Length == 0) return new(request, ImageResolutionStatus.Missing, null, Reason: "Image identity is empty.", Attempts: attempts);
            if (pngOverrideRoot is not null)
            {
                var root = Path.GetFullPath(pngOverrideRoot) + Path.DirectorySeparatorChar;
                failure = ImageFailureKind.InvalidIdentity;
                var path = Path.GetFullPath(Path.Combine(root, name + ".png"));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Image identity leaves override directory.");
                attempts.Add("Explicit PNG override: " + path);
                usedFiles[path] = 0;
                if (File.Exists(path))
                {
                    failure = ImageFailureKind.CorruptPayload;
                    using var input = File.OpenRead(path);
                    var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var image = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                    image.Freeze();
                    return new(request, ImageResolutionStatus.Resolved, image, SourcePath: path, Format: "PNG", Attempts: attempts);
                }
            }
            var sprite = data.Sprites.GetValueOrDefault(name);
            var textureName = sprite is null ? name : sprite.CategoryName + "_" + sprite.SheetId;
            foreach (var module in data.Order)
            foreach (var package in module.Packages.OrderBy(p => Path.GetFileName(p).Equals("gauntlet_ui.tpac", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                usedFiles[package] = 0;
                attempts.Add(module.Id + ": " + package + " / " + textureName);
                failure = ImageFailureKind.InvalidMetadata;
                var matches = atlases.Textures(package).Where(t => t.Name.Equals(textureName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) throw new InvalidDataException($"Duplicate texture identity '{textureName}' in {package}.");
                if (matches.Length == 0) continue;
                var texture = matches[0];
                failure = ImageFailureKind.AbsentPixelData;
                if (texture.Payload is null) throw new InvalidDataException("Texture has no pixel payload.");
                failure = ImageFailureKind.UnsupportedFormat;
                if (texture.Format is not ("BC7" or "DXT1" or "DXT5" or "BC4" or "R8_UNORM")) throw new NotSupportedException($"Unsupported texture format '{texture.Format}'.");
                var crop = sprite is null ? new SpritePartDefinition(name, package, "", 0, 0, 0, texture.Width, texture.Height) :
                    new SpritePartDefinition(sprite.SpritePartName, sprite.SourcePath, sprite.CategoryName, sprite.SheetId, sprite.SheetX, sprite.SheetY, sprite.Width, sprite.Height);
                failure = ImageFailureKind.InvalidCrop;
                if (crop.SheetX < 0 || crop.SheetY < 0 || crop.Width <= 0 || crop.Height <= 0 || (long)crop.SheetX + crop.Width > texture.Width || (long)crop.SheetY + crop.Height > texture.Height)
                    throw new InvalidDataException("Sprite crop lies outside its atlas.");
                failure = ImageFailureKind.CorruptPayload;
                var image = atlases.Crop(texture, crop.SheetX, crop.SheetY, crop.Width, crop.Height);
                if (texture.PayloadPath is { } payloadPath) usedFiles[payloadPath] = 0;
                return new(request, ImageResolutionStatus.Resolved, image, module.Id, package, texture.Name, texture.Format, crop, Attempts: attempts) { PayloadPath = texture.PayloadPath ?? package };
            }
            return new(request, ImageResolutionStatus.Missing, null, Reason: sprite is null ? "No exact sprite or texture identity exists in the module dependency scope." : "Sprite metadata exists but its atlas texture is missing.", Attempts: attempts);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or System.Xml.XmlException or ArgumentException or OverflowException or FormatException or UnauthorizedAccessException)
        {
            return new(request, ImageResolutionStatus.Invalid, null, Reason: ex.Message, Attempts: attempts) { Failure = failure };
        }
    }
    private Snapshot GetSnapshot()
    {
        var installation = index.GetSnapshot(settings.BannerlordInstallRoot);
        var owner = assetModule ?? ModuleAssetOrder.Owner(installation.Modules, prefab);
        var byOwner = scopes.GetValue(installation, _ => new(StringComparer.OrdinalIgnoreCase));
        return byOwner.GetOrAdd(owner, module => new(() => BuildSnapshot(installation, module),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
    private static Snapshot BuildSnapshot(BannerlordInstallationSnapshot installation, string owner)
    {
        var unavailable = new List<string>();
        var order = ModuleAssetOrder.Resolve(installation.Modules, owner, unavailable);
        var parts = new Dictionary<string, SpritePartDefinition>(StringComparer.OrdinalIgnoreCase);
        var sprites = new Dictionary<string, SpriteDefinition>(StringComparer.OrdinalIgnoreCase);
        var brushes = new Dictionary<string, BrushDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in order.Reverse())
        {
            bool Owns(string path) => path.StartsWith(module.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            foreach (var path in installation.SpriteDataFiles.Where(Owns)) BannerlordAssetXml.LoadSpriteData(path, parts, sprites);
            foreach (var path in installation.BrushFiles.Where(Owns)) BannerlordAssetXml.LoadBrushData(path, brushes);
        }
        return new(order, sprites, brushes, unavailable);
    }
    private static string Normalize(string? name) => (name ?? "").Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    private sealed record Snapshot(IReadOnlyList<BannerlordModule> Order, Dictionary<string, SpriteDefinition> Sprites, Dictionary<string, BrushDefinition> Brushes, IReadOnlyList<string> UnavailableDependencies);
}
