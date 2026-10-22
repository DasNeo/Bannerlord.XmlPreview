using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Assets;
using Bannerlord.XmlPreview.Behaviors.Bannerlord;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Preview.Bannerlord;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class InstalledImageTests
{
    [InstalledImageFact]
    public void LandManagementLoadMeasurements()
    {
        var output = Environment.GetEnvironmentVariable("BANNERLORD_LOADING_ARTIFACTS");
        if (output is null) return;
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var repo = Path.GetFullPath(Path.Combine(NativeSuiteTests.DataRoot(), "../.."));
            var results = new List<object>();
            foreach (var path in new[] { Path.Combine(repo, "fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml"),
                Path.Combine(root, "Modules/AgricultureEstate/GUI/Prefabs/LandManagement.xml") })
            {
                var (assets, loader, renderer) = EnvironmentFor(root);
                for (int iteration = 0; iteration < 6; iteration++)
                {
                    var diagnostics = new PreviewDiagnostics();
                    using var scope = diagnostics.BeginTimingScope();
                    var allocated = GC.GetTotalAllocatedBytes(true);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var document = loader.Load(path, diagnostics);
                    var loadMs = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    _ = renderer.Render(document, diagnostics);
                    var renderMs = watch.Elapsed.TotalMilliseconds;
                    Assert.NotEqual(RenderCompletionStatus.Failed, diagnostics.CompletionStatus);
                    var decodeStatistics = ((BannerlordAssetCatalog)assets).DecodeStatistics;
                    Assert.Equal(0, decodeStatistics.CachedAtlasBytes);
                    Assert.True(decodeStatistics.DecodedPixelCount < decodeStatistics.FullAtlasPixelCount);
                    Assert.InRange(decodeStatistics.CachedPayloadBytes, 0, 64L * 1024 * 1024);
                    results.Add(new { Path = path, Iteration = iteration, LoadMs = loadMs, RenderMs = renderMs,
                        CombinedMs = loadMs + renderMs, Nodes = Nodes(document.RootNode).Count(),
                        AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                        PeakWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,
                        DecodeStatistics = decodeStatistics,
                        Phases = diagnostics.ToDisplayText() });
                }
            }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"loading-{Environment.ProcessId}.json"), JsonSerializer.Serialize(new
            { Machine = Environment.MachineName, Runtime = Environment.Version.ToString(), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        });
    }

    [InstalledImageFact]
    public void ProductionCacheRecordsFirstLoadAndFiveWarmCrops()
    {
        var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
        var store = new TpacAtlasStore();
        var records = new List<object>();
        foreach (var (package, name) in new[]
        {
            (Path.Combine(root, "Modules/Native/AssetPackages/gauntlet_ui.tpac"), "ui_mplobby_1"),
            (Path.Combine(root, "Modules/Native/AssetPackages/gauntlet_ui.tpac"), "ui_group1_3"),
            (Path.Combine(root, "Modules/AgricultureEstate/Assets/GauntletUI/ui_agriculture_estate_1_tex.tpac"), "ui_agriculture_estate_1")
        })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var texture = store.Textures(package).Single(t => t.Name == name);
            var metadataMs = watch.Elapsed.TotalMilliseconds;
            var allocated = GC.GetTotalAllocatedBytes(true);
            watch.Restart(); _ = store.Crop(texture, 0, 0, 4, 4);
            var firstMs = watch.Elapsed.TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            var samples = new List<double>();
            for (int i = 0; i < 5; i++) { watch.Restart(); _ = store.Crop(texture, 0, 0, 4, 4); samples.Add(watch.Elapsed.TotalMilliseconds); }
            records.Add(new { Texture = name, texture.Format, texture.PayloadPath, texture.Width, texture.Height, MetadataMs = metadataMs,
                FirstPayloadDecodeCropMs = firstMs, WarmCropMs = samples, AllocatedBytes = allocated,
                CachedBytes = store.CachedBytes, ProcessPeakWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 });
            Assert.InRange(store.CachedBytes, 0, 128L * 1024 * 1024);
        }
        Assert.Equal(3, store.DecodeCount);
        var output = Environment.GetEnvironmentVariable("BANNERLORD_M3_ARTIFACTS");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "production-performance.json"), JsonSerializer.Serialize(new
            { Machine = Environment.MachineName, Runtime = Environment.Version.ToString(), ColdDefinition = "First use in process, OS cache uncontrolled", Records = records }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    [InstalledImageFact]
    public void NativeAndAgricultureEstateSpritesLoadDirectlyFromPackages()
    {
        var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
        var catalog = new BannerlordAssetCatalog(new ImageAssetTests.Settings(root), new BannerlordInstallationIndex());
        foreach (var (owner, names) in new[]
        {
            ("Native", new[] { "crosshair", "clan_banner", "BannerBuilder\\align_center", "101" }),
            ("AgricultureEstate", new[] { "ae_gardens", "ae_granary", "ae_storehouse", "ae_irrigation", "ae_patrols" })
        })
        {
            var scoped = catalog.ForPrefab(Path.Combine(root, "Modules", owner, "GUI", "Prefabs", "screen.xml"));
            foreach (var name in names)
            {
                var result = scoped.ResolveImage(new(name));
                Assert.True(result.Status == ImageResolutionStatus.Resolved, $"{owner}/{name}: {result.Status}: {result.Reason}");
                Assert.EndsWith(".tpac", result.SourcePath, StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(result.Crop); Assert.True(result.Image!.IsFrozen);
                if (owner == "AgricultureEstate") Assert.EndsWith(".rdc", result.PayloadPath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(owner, result.Module);
            }
        }
    }

    [InstalledImageFact]
    public void NativeScreensVerifyAllImageFamiliesAndRuntimeTableau()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            foreach (var (relative, maskCount) in new[] { ("Conversation/SPConversation.xml", 0), ("BannerBuilder/BannerBuilderScreen.xml", 5) })
            {
                var path = Path.Combine(root, "Modules/Native/GUI/Prefabs", relative);
                var (assets, loader, renderer) = EnvironmentFor(root);
                var diagnostics = new PreviewDiagnostics();
                var document = loader.Load(path, diagnostics);
                var scenario = ScenarioLoader.Load(Path.Combine(NativeSuiteTests.DataRoot(), "m3-scenarios", maskCount == 0 ? "m3-conversation.json" : "m3-banner-builder.json"));
                var options = new RegressionRenderOptions(scenario.PrefabIdentity, scenario.Bindings, scenario.WidgetStates, scenario.KeyBoundsSelectors) { AssetModule = scenario.AssetModule };
                // Scoped image tree retains Native XML properties/brushes/constants while excluding unrelated behavior families.
                var targets = Nodes(document.RootNode).Where(n => n.Name is "ImageWidget" or "ImageIdentifierWidget" or "MaskedTextureWidget" or "BannerTableauWidget").ToArray();
                var scopedRoot = new WindowNode(new Dictionary<string, string> { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = "Window",
                    ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "1920", ["SuggestedHeight"] = "1080" }, targets);
                var rendered = renderer.Render(document with { RootNode = scopedRoot }, diagnostics, options);
                var imageElements = ImageBehaviorTests.Elements(rendered).Where(e => PreviewElementDebugProperties.GetImageResolution(e) is not null).ToArray();
                WriteEvidence(Path.GetFileNameWithoutExtension(relative), diagnostics, imageElements);
                Assert.Equal(targets.Length, imageElements.Length);
                Assert.Equal(maskCount, imageElements.Count(e => PreviewElementDebugProperties.GetDebugInfo(e)?.NodeName == "MaskedTextureWidget"));
                foreach (var element in imageElements)
                {
                    var result = PreviewElementDebugProperties.GetImageResolution(element)!;
                    if (result.Request.Provider == "Runtime.Tableau") Assert.Equal(ImageResolutionStatus.RuntimeOnly, result.Status);
                    else
                    {
                        Assert.True(result.Status == ImageResolutionStatus.Resolved, $"{relative}: {result.Request}: {result.Status}: {result.Reason}");
                        if (result.Image is not null) { Assert.True(result.Width > 0); Assert.True(result.Height > 0); }
                        else Assert.Equal("Brush has no visible image layers in the selected state.", result.Reason);
                    }
                    if (PreviewElementDebugProperties.GetMaskResolution(element) is { } mask)
                    {
                        Assert.Equal(ImageResolutionStatus.Resolved, mask.Status);
                        Assert.True(mask.Width > 0); Assert.True(mask.Height > 0);
                        Assert.True(element.ActualWidth > 0); Assert.True(element.ActualHeight > 0);
                    }
                }
                Assert.Equal(maskCount == 0 ? RenderCompletionStatus.Complete : RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
                WriteEvidence(Path.GetFileNameWithoutExtension(relative), diagnostics, imageElements);
            }
            return 0;
        });
    }

    [InstalledImageFact]
    public void LandManagementDashboardRetainsPanelGeometryAndResolvesSprites()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (_, loader, renderer) = EnvironmentFor(root);
            var repo = Path.GetFullPath(Path.Combine(NativeSuiteTests.DataRoot(), "../.."));
            var path = Path.Combine(repo, "fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml");
            var diagnostics = new PreviewDiagnostics();
            var document = loader.Load(path, diagnostics);
            Assert.DoesNotContain(Nodes(document.RootNode), n => n.Name.Contains("CodeWidget", StringComparison.Ordinal));
            var scenario = ScenarioLoader.Load(Path.Combine(NativeSuiteTests.DataRoot(), "m3-scenarios", "m3-land-management.json"));
            var rendered = renderer.Render(document, diagnostics, new(scenario.PrefabIdentity,
                scenario.Bindings, scenario.WidgetStates, scenario.KeyBoundsSelectors) { AssetModule = scenario.AssetModule });
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            Assert.Empty(diagnostics.Messages);
            Assert.True(rendered.ActualWidth > 0); Assert.True(rendered.ActualHeight > 0);
            Assert.NotNull(diagnostics.Layout); Assert.NotNull(diagnostics.RenderStructure);
            var assets = new BannerlordAssetCatalog(new ImageAssetTests.Settings(root), new BannerlordInstallationIndex()).ForPrefab(path);
            var metrics = new BannerlordBrushMetricsProvider(assets);
            Assert.Equal(960d, metrics.ResolveMetric("Encyclopedia.Frame", "Default", "Height"));
            Assert.Equal("960", document.Constants["Encyclopedia.Frame.Height"]);
            var popup = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("BrushWidget[Id=TownManagementPopup]"));
            Assert.Equal(1500d, popup.Width); Assert.Equal(960d, popup.Height);
            foreach (var (id, width, height) in new[]
            {
                ("EstateDashboard", 1400d, 696d), ("LandHolding0", 580d, 180d), ("LandHolding1", 580d, 180d),
                ("EstateUpgrades", 800d, 380d), ("Upgrade0", 244d, 304d), ("Upgrade1", 244d, 304d), ("Upgrade2", 244d, 304d),
                ("EstateWorkforce", 520d, 296d), ("EstateStockpile", 410d, 296d), ("EstateProject", 430d, 296d),
                ("ProjectProgressTrack", 254d, 18d)
            })
            {
                var bounds = Assert.Single(diagnostics.Layout.KeyBounds, b => b.ExpandedPath.EndsWith($"Widget[Id={id}]"));
                Assert.Equal(width, bounds.Width); Assert.Equal(height, bounds.Height);
            }
            foreach (var name in Nodes(document.RootNode).Select(n => n.Attributes.GetValueOrDefault("Sprite"))
                .OfType<string>().Where(name => !name.StartsWith('@')).Distinct())
                Assert.Equal(ImageResolutionStatus.Resolved, assets.ResolveImage(new(name)).Status);
            WriteEvidence("land-management", diagnostics, []);
            return 0;
        });
    }

    internal static (IAssetCatalog, PrefabLoader, PrefabRenderer) EnvironmentFor(string root)
    {
        var settings = new ImageAssetTests.Settings(root); var index = new BannerlordInstallationIndex();
        var assets = new BannerlordAssetCatalog(settings, index);
        var reflection = new BannerlordWidgetReflectionService(settings, index);
        var metrics = new BannerlordBrushMetricsProvider(assets);
        var loader = new PrefabLoader(assets, metrics, new BannerlordPrefabReferenceResolver(settings, index),
            new BannerlordWidgetPropertyResolver(settings, reflection), new WidgetReferenceResolver(), new BannerlordWidgetNodeFactory(reflection));
        var renderer = new PrefabRenderer(assets, metrics, new BannerlordBrushOverlayFeatureHandler(), new BannerlordPreviewDataSourceProvider(),
            new BannerlordRootLayoutHints(), new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider(), new BannerlordBehaviorRuleProvider(reflection));
        return (assets, loader, renderer);
    }
    private static IEnumerable<PrefabNode> Nodes(PrefabNode root)
    { yield return root; foreach (var child in root.Children) foreach (var node in Nodes(child)) yield return node; }
    private static void WriteEvidence(string name, PreviewDiagnostics diagnostics, FrameworkElement[] elements)
    {
        var directory = Environment.GetEnvironmentVariable("BANNERLORD_M3_ARTIFACTS");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            diagnostics.CompletionStatus, diagnostics.RenderStructure, diagnostics.Layout, Diagnostics = diagnostics.Messages,
            Images = elements.Select(e => { var r = PreviewElementDebugProperties.GetImageResolution(e)!; return new
            { r.Request, r.Status, r.Module, r.SourcePath, r.PayloadPath, r.Texture, r.Format, r.Crop, r.Width, r.Height, r.Reason, r.Attempts }; })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class InstalledImageFactAttribute : FactAttribute
{
    public InstalledImageFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANNERLORD_ROOT")))
            Skip = "Installed M3 acceptance requires BANNERLORD_ROOT; portable M3 tests always run.";
    }
}
