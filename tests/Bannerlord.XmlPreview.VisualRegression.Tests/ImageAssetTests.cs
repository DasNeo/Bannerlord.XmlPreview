using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Assets;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class ImageAssetTests
{
    [Fact]
    public void BrushInheritanceResolvesModuleOverridesLayersStylesAndInvalidation()
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        Module(fixture.Root, "Mod", "Native", 0x07E0);
        var native = Path.Combine(fixture.Root, "Modules/Native/GUI/Brushes");
        var mod = Path.Combine(fixture.Root, "Modules/Mod/GUI/Brushes");
        Directory.CreateDirectory(native); Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(native, "base.xml"), """
            <Brushes>
              <Brush Name="base" Font="Galahad"><Layers><BrushLayer Name="Default" Sprite="icon" ExtendLeft="2"/></Layers></Brush>
              <Brush Name="middle" BaseBrush="base"><Layers><BrushLayer Name="Default" ExtendRight="4"/></Layers></Brush>
            </Brushes>
            """);
        var overrides = Path.Combine(mod, "overrides.xml");
        File.WriteAllText(overrides, """
            <Brushes>
              <Brush Name="base" Font="FiraSansExtraCondensed-Regular" TextHorizontalAlignment="Center">
                <Layers><BrushLayer Name="Default" Sprite="icon" ExtendLeft="2"/></Layers>
                <Styles><Style Name="Default" FontSize="20" FontColor="#FFFFFFFF"/>
                  <Style Name="Hovered" FontColor="#FFD987FF"/></Styles>
              </Brush>
              <Brush Name="leaf" BaseBrush="middle"><Layers><BrushLayer Name="Default" Color="#FF0000FF"/></Layers>
                <Styles><Style Name="Default" FontColor="#00FF00FF"/></Styles></Brush>
            </Brushes>
            """);
        var catalog = new BannerlordAssetCatalog(new Settings(fixture.Root), new BannerlordInstallationIndex());
        var path = Path.Combine(fixture.Root, "Modules/Mod/GUI/Prefabs/screen.xml");
        var scoped = catalog.ForPrefab(path);
        var leaf = scoped.FindBrush("leaf")!;
        Assert.Equal("FiraSansExtraCondensed-Regular", leaf.FontName);
        Assert.Equal("Center", leaf.TextHorizontalAlignment);
        Assert.Equal(20, leaf.Styles["Default"].FontSize);
        Assert.Equal("#00FF00FF", leaf.Styles["Default"].FontColor);
        Assert.Equal("#FFD987FF", leaf.Styles["Hovered"].FontColor);
        var layer = Assert.Single(leaf.Layers);
        Assert.Equal("icon", layer.SpriteName); Assert.Equal(2, layer.ExtendLeft); Assert.Equal(4, layer.ExtendRight);
        Assert.Equal("#FF0000FF", layer.Color);
        Assert.Null(Assert.Single(scoped.FindBrush("base")!.Layers).Color);
        Assert.Equal("#FFFFFFFF", scoped.FindBrush("base")!.Styles["Default"].FontColor);
        Parallel.For(0, 12, _ => Assert.Same(leaf, scoped.FindBrush("leaf")));
        File.WriteAllText(overrides, File.ReadAllText(overrides).Replace("FontSize=\"20\"", "FontSize=\"26\""));
        catalog.Invalidate();
        Assert.Equal(26, catalog.ForPrefab(path).FindBrush("leaf")!.Styles["Default"].FontSize);
        Assert.Equal(20, leaf.Styles["Default"].FontSize);
    }

    [Fact]
    public void BrushInheritanceReportsMissingParentsAndCyclesOnlyWhenRequested()
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        var directory = Path.Combine(fixture.Root, "Modules/Native/GUI/Brushes"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "invalid.xml"), """
            <Brushes><Brush Name="a" BaseBrush="b"/><Brush Name="b" BaseBrush="a"/>
              <Brush Name="orphan" BaseBrush="absent"/><Brush Name="valid"/></Brushes>
            """);
        var catalog = new BannerlordAssetCatalog(new Settings(fixture.Root), new BannerlordInstallationIndex());
        Assert.NotNull(catalog.FindBrush("valid"));
        Assert.Contains("cycle", Assert.Throws<InvalidDataException>(() => catalog.FindBrush("a")).Message);
        Assert.Contains("missing BaseBrush 'absent'", Assert.Throws<InvalidDataException>(() => catalog.FindBrush("orphan")).Message);
        Assert.Null(catalog.FindBrush("unknown"));
    }

    [Theory]
    [InlineData("DXT1", "00F8000000000000", 255, 0, 0, 255)]
    [InlineData("DXT5", "FF0000000000000000F8000000000000", 255, 0, 0, 255)]
    [InlineData("DXT5", "800000000000000000F8000000000000", 128, 0, 0, 128)]
    [InlineData("BC4", "FF00000000000000", 255, 0, 0, 255)]
    [InlineData("BC7", "40000000000000000000000000000000", 0, 0, 0, 0)]
    [InlineData("R8_UNORM", "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 255, 255, 255, 255)]
    public void KnownBlocksVerifyChannelsAndPremultipliedAlpha(string format, string hex, int r, int g, int b, int a)
    {
        var image = TpacTextureReader.Decode(Convert.FromHexString(hex), 4, 4, format);
        Assert.True(image.IsFrozen);
        Assert.Equal(PixelFormats.Pbgra32, image.Format);
        var pixels = new byte[64]; image.CopyPixels(pixels, 16, 0);
        Assert.Equal(new byte[] { (byte)b, (byte)g, (byte)r, (byte)a }, pixels[..4]);
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal(pixels[..4], pixels[(i * 4)..(i * 4 + 4)]));
    }

    [Fact]
    public void InvalidFormatsDimensionsAndShortPayloadsAreRejected()
    {
        Assert.Throws<NotSupportedException>(() => TpacTextureReader.Decode(new byte[16], 4, 4, "not-a-format"));
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Decode([], 4, 4, "DXT5"));
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Decode([], 0, 4, "DXT1"));
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Decode([], 16384, 16384, "BC7"));
    }

    [Fact]
    public async Task CropsAreIndependentAndConcurrentRequestsDecodeOnceWithinBoundedLru()
    {
        using var fixture = new RegressionFixture();
        var one = WritePackage(Path.Combine(fixture.Root, "one.tpac"), "one");
        var two = WritePackage(Path.Combine(fixture.Root, "two.tpac"), "two");
        var store = new TpacAtlasStore(64);
        var first = Assert.Single(store.Textures(one));
        var second = Assert.Single(store.Textures(two));
        var images = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Crop(first, 1, 1, 2, 2))));
        Assert.Equal(1, store.DecodeCount);
        Assert.All(images, image => { Assert.True(image.IsFrozen); Assert.Equal(2, image.PixelWidth); });
        Assert.Throws<InvalidDataException>(() => store.Crop(first, 3, 3, 2, 2));
        _ = store.Atlas(second);
        Assert.Equal(64, store.CachedBytes);
        _ = store.Atlas(first);
        Assert.Equal(3, store.DecodeCount);
        Assert.Equal(64, store.CachedBytes);
    }

    [Fact]
    public void CachedCropsSurviveAtlasEvictionAndHaveTheirOwnMemoryBound()
    {
        using var fixture = new RegressionFixture();
        var store = new TpacAtlasStore(64, 16);
        var first = Assert.Single(store.Textures(WritePackage(Path.Combine(fixture.Root, "first.tpac"), "first")));
        var second = Assert.Single(store.Textures(WritePackage(Path.Combine(fixture.Root, "second.tpac"), "second", 0x07E0)));
        var crop = store.Crop(first, 0, 0, 2, 2);
        _ = store.Atlas(second);
        Assert.Same(crop, store.Crop(first, 0, 0, 2, 2));
        Assert.Equal(2, store.DecodeCount);
        _ = store.Crop(second, 0, 0, 2, 2);
        Assert.Equal(16, store.CachedCropBytes);
        Assert.NotSame(crop, store.Crop(first, 0, 0, 2, 2));
        Assert.Equal(4, store.DecodeCount);
        Assert.Equal(16, store.CachedCropBytes);
        var pixels = new byte[16]; crop.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]);
    }

    [Fact]
    public void PrefabScopesShareMetadataUntilInstallationIndexIsInvalidated()
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        Module(fixture.Root, "Mod", "Native", 0x07E0);
        var index = new BannerlordInstallationIndex();
        var catalog = new BannerlordAssetCatalog(new Settings(fixture.Root), index);
        var path = Path.Combine(fixture.Root, "Modules/Mod/GUI/Prefabs/screen.xml");
        var first = catalog.ForPrefab(path);
        var second = catalog.ForPrefab(path);
        Assert.Same(first.FindSprite("icon"), second.FindSprite("icon"));
        Assert.NotSame(first.FindSprite("icon"), catalog.ForPrefab(path, "Native").FindSprite("icon"));
        var request = new ImageRequest("icon");
        Assert.Same(first.ResolveImage(request).Image, second.ResolveImage(request).Image);
        var oldMetadata = first.FindSprite("icon");
        index.Invalidate();
        Assert.NotSame(oldMetadata, first.FindSprite("icon"));
    }

    [Fact]
    public void TpacFailuresRemainDistinctAndNeverBecomeFallbackImages()
    {
        using var fixture = new RegressionFixture();
        var path = WritePackage(Path.Combine(fixture.Root, "valid.tpac"), "texture");
        var valid = Assert.Single(TpacTextureReader.Read(path));
        Assert.Equal(4, valid.Width);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Load(valid with { Payload = null }));
        Assert.Throws<NotSupportedException>(() => TpacTextureReader.Load(valid with { Payload = valid.Payload! with { Compression = 8 } }));
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Load(valid with { Payload = valid.Payload! with { Compression = 1, UncompressedSize = 64 } }));
        File.WriteAllBytes(Path.Combine(fixture.Root, "corrupt.tpac"), new byte[36]);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(Path.Combine(fixture.Root, "corrupt.tpac")));
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(path));
    }

    [Fact]
    public void ModuleOrderIsTransitiveDeterministicAndRejectsAmbiguity()
    {
        BannerlordModule M(string id, params string[] deps) => new(id, "/" + id, deps, []);
        var modules = new[] { M("Native"), M("A", "Native"), M("B", "Native"), M("Mod", "A", "B") };
        Assert.Equal(new[] { "Mod", "B", "A", "Native" }, ModuleAssetOrder.Resolve(modules, "Mod").Select(m => m.Id));
        Assert.Throws<InvalidDataException>(() => ModuleAssetOrder.Resolve([M("Mod", "Absent")], "Mod"));
        Assert.Throws<InvalidDataException>(() => ModuleAssetOrder.Resolve([M("A", "B"), M("B", "A")], "A"));
        Assert.Throws<InvalidDataException>(() => ModuleAssetOrder.Resolve([M("A"), M("a")], "A"));
        Assert.Throws<InvalidDataException>(() => ModuleAssetOrder.Resolve([M("A") with { MetadataError = "malformed" }], "A"));
        var unavailable = new List<string>();
        Assert.Equal("Mod", Assert.Single(ModuleAssetOrder.Resolve([M("Mod", "Absent")], "Mod", unavailable)).Id);
        Assert.Contains("Absent", Assert.Single(unavailable));
    }
    [Theory]
    [InlineData(ImageFailureKind.AbsentPixelData)]
    [InlineData(ImageFailureKind.UnsupportedFormat)]
    [InlineData(ImageFailureKind.InvalidCrop)]
    [InlineData(ImageFailureKind.CorruptPayload)]
    public void CatalogReportsMachineReadablePackageFailures(ImageFailureKind expected)
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        var package = Path.Combine(fixture.Root, "Modules/Native/AssetPackages/native.tpac");
        if (expected == ImageFailureKind.AbsentPixelData) WritePackage(package, "ui_test_1", embedPixels: false);
        if (expected == ImageFailureKind.UnsupportedFormat)
        {
            var bytes = File.ReadAllBytes(package);
            var text = Encoding.ASCII.GetString(bytes); var offset = text.IndexOf("DXT1", StringComparison.Ordinal);
            Encoding.ASCII.GetBytes("NOPE").CopyTo(bytes, offset); File.WriteAllBytes(package, bytes);
        }
        if (expected == ImageFailureKind.InvalidCrop)
        {
            var xml = Path.Combine(fixture.Root, "Modules/Native/GUI/TestSpriteData.xml");
            File.WriteAllText(xml, File.ReadAllText(xml).Replace("<SheetX>0</SheetX>", "<SheetX>7</SheetX>"));
        }
        if (expected == ImageFailureKind.CorruptPayload)
        {
            var payload = Assert.Single(TpacTextureReader.Read(package)).Payload!;
            var bytes = File.ReadAllBytes(package); bytes[(int)payload.Offset - 5] = 1; File.WriteAllBytes(package, bytes);
        }
        var result = new BannerlordAssetCatalog(new Settings(fixture.Root), new BannerlordInstallationIndex()).ResolveImage(new("icon"));
        Assert.Equal(ImageResolutionStatus.Invalid, result.Status); Assert.Equal(expected, result.Failure); Assert.Null(result.Image);
        Assert.Equal("image." + expected.ToString().ToLowerInvariant(), Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void OwningModuleOverridesDependencyAndUnrelatedAssetsCannotLeak()
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        Module(fixture.Root, "Mod", "Native", 0x07E0);
        Module(fixture.Root, "Unrelated", "", 0x001F);
        var catalog = new BannerlordAssetCatalog(new Settings(fixture.Root), new BannerlordInstallationIndex());
        var scoped = catalog.ForPrefab(Path.Combine(fixture.Root, "Modules", "Mod", "GUI", "Prefabs", "screen.xml"));
        var result = scoped.ResolveImage(new("icon"));
        Assert.Equal(ImageResolutionStatus.Resolved, result.Status);
        Assert.Equal("Mod", result.Module);
        Assert.Equal("ui_test_1", result.Texture);
        Assert.EndsWith("mod.tpac", result.SourcePath, StringComparison.OrdinalIgnoreCase);
        var pixels = new byte[64]; result.Image!.CopyPixels(pixels, 16, 0);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[..4]);
        Assert.Equal(ImageResolutionStatus.Missing, scoped.ResolveImage(new("unrelated.icon")).Status);
        Assert.Equal(ImageResolutionStatus.RuntimeOnly, scoped.ResolveImage(new("hero", "HeroPortrait", "equipment")).Status);
        Assert.Equal(ImageResolutionStatus.Resolved, scoped.ResolveImage(new("banner-id", "Banner", "args", "icon")).Status);
        Assert.Equal(ImageResolutionStatus.Missing, scoped.ResolveImage(new("prefix.icon")).Status);
    }

    [Fact]
    public void CompanionPixelsRequireMatchingIdentityChecksumAndBounds()
    {
        using var fixture = new RegressionFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "SubModule.xml"), "<Module><Id value='Test'/></Module>");
        var path = WritePackage(Path.Combine(fixture.Root, "Assets", "texture.tpac"), "texture", embedPixels: false);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "RuntimeDataCache"));
        var cache = Path.Combine(fixture.Root, "RuntimeDataCache", Guid.Empty.ToString("D") + ".rdc");
        using (var stream = File.Create(cache))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x30434452u); writer.Write(0u); writer.Write(1u); writer.Write(145ul);
            writer.Write(Guid.Empty.ToByteArray()); writer.Write(Guid.Empty.ToByteArray());
            writer.Write(new Guid("70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c").ToByteArray());
            writer.Write(165ul); writer.Write(8ul); writer.Write(8ul); writer.Write(0u); writer.Write(0ul);
            writer.Write(new byte[61]); writer.Write((ushort)0xF800); writer.Write((ushort)0); writer.Write(0u);
        }
        var texture = Assert.Single(TpacTextureReader.Read(path));
        Assert.Equal(cache, texture.PayloadPath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(8, TpacTextureReader.Load(texture).Length);
        var bytes = File.ReadAllBytes(cache); bytes[96] = 1; File.WriteAllBytes(cache, bytes);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(path));
        bytes[96] = 0; bytes[20] = 1; File.WriteAllBytes(cache, bytes);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(path));
        bytes[20] = 0; bytes[68] = 0; File.WriteAllBytes(cache, bytes);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(path));
    }

    internal static string WritePackage(string path, string name, ushort color = 0xF800, bool embedPixels = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        void Text(string s) { var bytes = Encoding.UTF8.GetBytes(s); writer.Write(bytes.Length); writer.Write(bytes); }
        writer.Write(0x43415054u); writer.Write(2u); writer.Write(Guid.Empty.ToByteArray()); writer.Write(1u); writer.Write(0u); writer.Write(0u);
        writer.Write(new Guid("c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8").ToByteArray()); writer.Write(Guid.Empty.ToByteArray()); writer.Write(0u); Text(name);
        using var metadata = new MemoryStream(); using (var m = new BinaryWriter(metadata, Encoding.UTF8, true))
        {
            m.Write(2u); m.Write(Guid.Empty.ToByteArray()); m.Write(0u); m.Write(0u); m.Write(0ul); m.Write(false); m.Write(0u);
            m.Write(0u); m.Write(0u); m.Write((byte)2); m.Write(4u); m.Write(4u); m.Write(1u); m.Write((byte)1); m.Write((ushort)1);
            m.Write(4u); m.Write(Encoding.UTF8.GetBytes("DXT1"));
        }
        writer.Write((ulong)metadata.Length); writer.Write(metadata.ToArray()); writer.Write(0ul); writer.Write(embedPixels ? 1 : 0);
        if (!embedPixels) { writer.Write(0); writer.Flush(); File.WriteAllBytes(path, stream.ToArray()); return path; }
        var offsetPosition = stream.Position; writer.Write(0ul); writer.Write(8ul); writer.Write(8ul); writer.Write(Guid.Empty.ToByteArray());
        writer.Write(new Guid("70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c").ToByteArray()); writer.Write(0ul); writer.Write(0u); writer.Write((byte)0); writer.Write(0);
        var payloadPosition = stream.Position; writer.Write(color); writer.Write((ushort)0); writer.Write(0u);
        stream.Position = offsetPosition; writer.Write((ulong)payloadPosition); writer.Flush();
        File.WriteAllBytes(path, stream.ToArray()); return path;
    }

    private static void Module(string root, string id, string dependency, ushort color)
    {
        var directory = Path.Combine(root, "Modules", id); Directory.CreateDirectory(Path.Combine(directory, "GUI"));
        File.WriteAllText(Path.Combine(directory, "SubModule.xml"), $"<Module><Id value='{id}'/><DependedModules>{(dependency.Length == 0 ? "" : $"<DependedModule Id='{dependency}'/>")}</DependedModules></Module>");
        var spriteName = id == "Unrelated" ? "unrelated.icon" : "icon";
        File.WriteAllText(Path.Combine(directory, "GUI", "TestSpriteData.xml"), $"<SpriteData><SpriteParts><SpritePart><Name>{spriteName}</Name><CategoryName>ui_test</CategoryName><SheetID>1</SheetID><SheetX>0</SheetX><SheetY>0</SheetY><Width>4</Width><Height>4</Height></SpritePart></SpriteParts></SpriteData>");
        WritePackage(Path.Combine(directory, "AssetPackages", id.ToLowerInvariant() + ".tpac"), "ui_test_1", color);
    }

    [Fact]
    public void DependencyInvalidationReloadsMetadataAndImagePixels()
    {
        using var fixture = new RegressionFixture();
        Module(fixture.Root, "Native", "", 0xF800);
        var metadata = Path.Combine(fixture.Root, "Modules/Native/GUI/TestSpriteData.xml");
        var package = Path.Combine(fixture.Root, "Modules/Native/AssetPackages/native.tpac");
        var catalog = new BannerlordAssetCatalog(new Settings(fixture.Root), new BannerlordInstallationIndex());
        var source = Path.Combine(fixture.Root, "Modules/Native/GUI/Prefabs/screen.xml");
        var scoped = catalog.ForPrefab(source);
        var original = scoped.ResolveImage(new("icon"));
        Assert.Contains(Path.GetFullPath(metadata), scoped.Dependencies);
        Assert.Contains(Path.GetFullPath(package), scoped.Dependencies);
        Assert.Contains(Path.GetFullPath(Path.Combine(fixture.Root, "Modules/Native/SubModule.xml")), scoped.Dependencies);
        WritePackage(package, "ui_test_1", 0x07E0);
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("<Width>4</Width>", "<Width>2</Width>"));
        catalog.Invalidate();
        var updated = catalog.ForPrefab(source).ResolveImage(new("icon"));
        Assert.Equal(2, updated.Image!.PixelWidth);
        var pixels = new byte[32]; updated.Image.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[..4]);
        Assert.Equal(4, original.Image!.PixelWidth);
    }

    internal sealed class Settings(string root) : IAppSettingsService
    {
        public string BannerlordInstallRoot => root;
        public bool TrySetBannerlordInstallRoot(string path, out string normalizedPath, out string error) { normalizedPath = path; error = ""; return false; }
    }
}
