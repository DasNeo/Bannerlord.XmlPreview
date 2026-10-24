using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Behaviors.Bannerlord;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class InventoryRenderingTests
{
    [InstalledImageFact]
    public void InstalledTupleMatchesSideSelectionAndImageExpectations()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var directory = Path.Combine(Path.GetTempPath(), "inventory-installed-" + Guid.NewGuid().ToString("N"), "SandBox");
            Directory.CreateDirectory(directory);
            try
            {
                var (_, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
                foreach (var player in new[] { false, true })
                {
                    var path = Path.Combine(directory, "Tuple.xml");
                    File.WriteAllText(path, $"<Prefab><Constants/><Window><InventoryItemTuple Id=\"Tuple\" Parameter.IsPlayerItem=\"{player.ToString().ToLowerInvariant()}\"/></Window></Prefab>");
                    var document = loader.Load(path, new PreviewDiagnostics());
                    foreach (var selected in new[] { false, true })
                    {
                        var bindings = document.BindingKeys.ToDictionary(k => k, _ => "0");
                        foreach (var (key, value) in new[] { ("IsSelected", selected.ToString().ToLowerInvariant()), ("ItemDescription", "Camel"),
                            ("ItemCount", "2"), ("ItemCost", "289"), ("IsTransferable", "true"), ("CanCharacterUseItem", "true"),
                            ("IsTradeable", "true"), ("IsEquipableItem", "true"), ("CanBeSlaughtered", "false"), ("TotalStock", "2"), ("ThisStock", "2"),
                            ("InitialThisStock", "2"), ("Id", "camel"), ("TextureProviderName", "ItemThumbnail") }) bindings[key] = value;
                        var diagnostics = new PreviewDiagnostics();
                        var options = Options() with { Bindings = bindings, PreviewSize = new(740, 300), AssetModule = "SandBox" };
                        var rendered = renderer.Render(document, diagnostics, options);
                        var elements = Elements(rendered).ToArray();
                        var main = elements.Single(e => Id(e) == "Main");
                        Assert.Equal(player ? "Inventory.Tuple.Right" : "Inventory.Tuple.Left",
                            PreviewElementDebugProperties.GetDebugInfo(main)!.GetAttributeDisplayValue("Brush"));
                        Assert.Contains(elements.OfType<BitmapFontTextBlock>(), e => e.StyleSource.Text == "289");
                        Assert.Equal(selected ? Visibility.Visible : Visibility.Collapsed, elements.Single(e => Id(e) == "Extension").Visibility);
                        Assert.Equal(player ? Visibility.Visible : Visibility.Collapsed, elements.Single(e => Id(e) == "SellButtonParent").Visibility);
                        Assert.Equal(player ? Visibility.Collapsed : Visibility.Visible, elements.Single(e => Id(e) == "BuyButtonParent").Visibility);
                        Assert.Equal(Visibility.Visible, elements.Single(e => Id(e) == "EquipButton").Visibility);
                        Assert.True(elements.Single(e => Id(e) == "EquipButton").IsEnabled);
                        Assert.Equal(ImageResolutionStatus.RuntimeOnly, PreviewElementDebugProperties.GetImageResolution(elements.Single(e => Id(e) == "ImageIdentifier"))!.Status);
                        Assert.Equal(554, Bounds(diagnostics, "Tuple").Width);
                        if (selected)
                        {
                            Assert.True(Bounds(diagnostics, "Tuple").Height > 55);
                            Assert.Equal(53, Bounds(diagnostics, "Extension").Y - Bounds(diagnostics, "Tuple").Y);
                        }
                        else Assert.Equal(55, Bounds(diagnostics, "Tuple").Height);
                        var output = Environment.GetEnvironmentVariable("BANNERLORD_INVENTORY_ARTIFACTS");
                        if (output is not null)
                        {
                            Directory.CreateDirectory(output);
                            var name = (player ? "inventory" : "market") + (selected ? "-selected" : "-collapsed");
                            WpfPreviewCapture.WritePng(Path.Combine(output, name + ".png"), WpfPreviewCapture.Capture(rendered, new("inventory-investigation", 740, 300)));
                            File.WriteAllText(Path.Combine(output, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                                { diagnostics.CompletionStatus, diagnostics.RenderStructure, diagnostics.Layout, Diagnostics = diagnostics.Messages }));
                        }
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
            return 0;
        });
    }

    [Fact]
    public void IncludedVisualDefinitionsResolveParametersAndRemainIsolatedAcrossRenders()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            using var fixture = new Fixture();
            fixture.Write("Part", """
                <Prefab>
                  <Parameters><Parameter Name="Width" DefaultValue="60"/></Parameters>
                  <Constants><Constant Name="Width" Value="*Width"/><Constant Name="PressedWidth" Value="!Width" Additive="-8"/></Constants>
                  <VisualDefinitions><VisualDefinition Name="Container">
                    <VisualState State="Default" SuggestedWidth="!Width" MarginTop="2"/>
                    <VisualState State="Pressed" SuggestedWidth="!PressedWidth" MarginTop="7"/>
                  </VisualDefinition></VisualDefinitions>
                  <Window><Widget VisualDefinition="Container" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedHeight="10"/></Window>
                </Prefab>
                """);
            var document = fixture.Load("""
                <Prefab><Constants/><Window>
                  <ListPanel WidthSizePolicy="Fixed" HeightSizePolicy="CoverChildren" SuggestedWidth="300" StackLayout.LayoutMethod="VerticalTopToBottom">
                    <Children><Part Id="one" Parameter.Width="80"/><Part Id="two" Parameter.Width="120"/></Children>
                  </ListPanel>
                </Window></Prefab>
                """);
            var target = Nodes(document.RootNode).Single(n => n.Attributes.GetValueOrDefault("Id") == "two");
            var path = target.Attributes["Debug.WidgetPath"];
            var options = Options() with { WidgetStates = new Dictionary<string, IReadOnlyDictionary<string, string>>
                { [path] = new Dictionary<string, string> { ["CurrentState"] = "Pressed" } } };
            var pressed = new PreviewDiagnostics();
            fixture.Renderer.Render(document, pressed, options);
            Assert.Equal(80, Bounds(pressed, "one").Width);
            Assert.Equal(112, Bounds(pressed, "two").Width);
            var defaults = new PreviewDiagnostics();
            fixture.Renderer.Render(document, defaults, Options());
            Assert.Equal(120, Bounds(defaults, "two").Width);
            Assert.Equal("120", target.VisualDefinition!.States["Default"]["SuggestedWidth"]);
            Assert.False(target.Attributes.ContainsKey("SuggestedWidth"));
            return 0;
        });
    }

    [Fact]
    public void SelectedTupleExpandsAndRendersBackgroundPriceAndRuntimeOnlyPlaceholder()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            using var fixture = new Fixture();
            var document = fixture.Load(TupleXml);
            var diagnostics = new PreviewDiagnostics();
            var rendered = fixture.Renderer.Render(document, diagnostics, Options() with
                { Bindings = new Dictionary<string, string> { ["Selected"] = "true" } });
            Assert.Equal(100, Bounds(diagnostics, "Tuple").Width);
            Assert.Equal(60, Bounds(diagnostics, "Tuple").Height);
            Assert.Equal(50, Bounds(diagnostics, "Extension").Height);
            Assert.Equal(10, Bounds(diagnostics, "Extension").Y);
            var elements = Elements(rendered).ToArray();
            Assert.Contains(elements.OfType<TextBlock>(), t => t.Text == "289");
            var main = elements.Single(e => Id(e) == "Main");
            Assert.IsType<SolidColorBrush>(Assert.IsType<Grid>(main).Background);
            Assert.Equal("Selected", PreviewElementDebugProperties.GetPreviewState(main));
            Assert.Equal(Visibility.Visible, elements.Single(e => Id(e) == "Extension").Visibility);
            Assert.Equal(Visibility.Collapsed, elements.Single(e => Id(e) == "Loading").Visibility);
            Assert.Equal(ImageResolutionStatus.RuntimeOnly, PreviewElementDebugProperties.GetImageResolution(elements.Single(e => Id(e) == "Image"))!.Status);
            Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            var collapsed = new PreviewDiagnostics();
            var second = fixture.Renderer.Render(document, collapsed, Options() with
                { Bindings = new Dictionary<string, string> { ["Selected"] = "false" } });
            Assert.Equal(10, Bounds(collapsed, "Tuple").Height);
            Assert.Equal(Visibility.Collapsed, Elements(second).Single(e => Id(e) == "Extension").Visibility);
            Assert.Equal("false", Nodes(document.RootNode).Single(n => n.Attributes.GetValueOrDefault("Id") == "Extension").Attributes["IsVisible"]);
            return 0;
        });
    }

    [Fact]
    public void InheritedProviderImageUsesScenarioSpriteAndHidesLoadingIcon()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            using var fixture = new Fixture();
            var document = fixture.Load("""
                <Prefab><Constants/><Window><InventoryImageIdentifierWidget Id="Image" ImageId="camel" TextureProviderName="ItemThumbnail"
                  LoadingIconWidget="Loading" Preview.ImageSprite="sample" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="20" SuggestedHeight="10">
                  <Children><Widget Id="Loading" WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"/></Children>
                </InventoryImageIdentifierWidget></Window></Prefab>
                """);
            var diagnostics = new PreviewDiagnostics();
            var rendered = fixture.Renderer.Render(document, diagnostics, Options());
            Assert.Equal(ImageResolutionStatus.Resolved, PreviewElementDebugProperties.GetImageResolution(Elements(rendered).Single(e => Id(e) == "Image"))!.Status);
            Assert.Equal(Visibility.Collapsed, Elements(rendered).Single(e => Id(e) == "Loading").Visibility);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Fact]
    public void ProfitStylesKeepDefaultFontMetricsAndDoNotLeakToSiblingValues()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            using var fixture = new Fixture();
            var document = fixture.Load("""
                <Prefab><Constants/><Window><Widget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="100" SuggestedHeight="50"><Children>
                  <InventoryItemValueTextWidget IntText="171" ProfitType="0" Brush="cost" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="50" SuggestedHeight="20"/>
                  <InventoryItemValueTextWidget IntText="250" ProfitType="1" Brush="cost" PositionYOffset="25" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="50" SuggestedHeight="20"/>
                </Children></Widget></Window></Prefab>
                """);
            var rendered = fixture.Renderer.Render(document, new PreviewDiagnostics(), Options());
            var texts = Elements(rendered).OfType<TextBlock>().ToDictionary(t => t.Text);
            Assert.Equal(18, texts["171"].FontSize);
            Assert.Equal(18, texts["250"].FontSize);
            Assert.Equal(Color.FromRgb(255, 217, 135), Assert.IsType<SolidColorBrush>(texts["171"].Foreground).Color);
            Assert.Equal(Colors.Lime, Assert.IsType<SolidColorBrush>(texts["250"].Foreground).Color);
            return 0;
        });
    }

    private const string TupleXml = """
        <Prefab><Constants/>
          <VisualDefinitions>
            <VisualDefinition Name="Container"><VisualState State="Default" SuggestedWidth="100"/></VisualDefinition>
            <VisualDefinition Name="Extension"><VisualState State="Default" MarginTop="2" SuggestedHeight="8"/>
              <VisualState State="Selected" MarginTop="10" SuggestedHeight="50"/></VisualDefinition>
          </VisualDefinitions>
          <Window><InventoryItemTupleWidget Id="Tuple" WidthSizePolicy="Fixed" HeightSizePolicy="CoverChildren" VisualDefinition="Container"
            IsSelected="@Selected" MainContainer="Main" ExtendedControlsContainer="Extension" NameTextWidget="Main\Name" EquipButton="Extension\Equip"
            DefaultBrush="row" IsEquipable="false" CanCharacterUseItem="true">
            <Children>
              <Widget Id="Extension" VisualDefinition="Extension" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="100" IsVisible="false" IsDisabled="true">
                <Children><ButtonWidget Id="Equip" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="10" SuggestedHeight="10"/></Children>
              </Widget>
              <BrushListPanel Id="Main" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="100" SuggestedHeight="10" Brush="unused">
                <Children>
                  <TextWidget Id="Name" Text="Camel" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="40" SuggestedHeight="10"/>
                  <InventoryItemValueTextWidget Id="Cost" IntText="289" ProfitType="0" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="30" SuggestedHeight="10"/>
                  <InventoryImageIdentifierWidget Id="Image" ImageId="camel" TextureProviderName="ItemThumbnail" LoadingIconWidget="Loading"
                    WidthSizePolicy="Fixed" HeightSizePolicy="Fixed" SuggestedWidth="20" SuggestedHeight="10"><Children><Widget Id="Loading"/></Children>
                  </InventoryImageIdentifierWidget>
                </Children>
              </BrushListPanel>
            </Children>
          </InventoryItemTupleWidget></Window>
        </Prefab>
        """;

    private static RegressionRenderOptions Options() => new("fixture.xml", new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), []);
    private static WidgetBounds Bounds(PreviewDiagnostics diagnostics, string id) => Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith($"[Id={id}]", StringComparison.Ordinal));
    private static string? Id(FrameworkElement e) => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id");
    private static IEnumerable<PrefabNode> Nodes(PrefabNode node) { yield return node; foreach (var child in node.Children.SelectMany(Nodes)) yield return child; }
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "inventory-render-" + Guid.NewGuid().ToString("N"));
        private readonly PrefabLoader _loader;
        public PrefabRenderer Renderer { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var assets = new Assets();
            var metrics = new BannerlordBrushMetricsProvider(assets);
            var reflection = new Reflection();
            _loader = new(assets, metrics, new References(_root), new Properties(), new WidgetReferenceResolver(), new BannerlordWidgetNodeFactory(reflection));
            Renderer = new(assets, metrics, new BannerlordBrushOverlayFeatureHandler(), new SnapshotCaptureTests.EmptyData(),
                new BannerlordRootLayoutHints(), new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider(), new BannerlordBehaviorRuleProvider(reflection));
        }
        public void Write(string name, string xml) => File.WriteAllText(Path.Combine(_root, name + ".xml"), xml);
        public PrefabDocument Load(string xml) { Write("Root", xml); return _loader.Load(Path.Combine(_root, "Root.xml"), new PreviewDiagnostics()); }
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed class References(string root) : IPrefabReferenceResolver
    {
        public PrefabReferenceResolveResult Resolve(string source, string name) => name == "Part"
            ? new(name, Path.Combine(root, "Part.xml"), [], PrefabReferenceResolveKind.SameDirectory)
            : new(name, null, [], PrefabReferenceResolveKind.NotFound);
    }
    private sealed class Properties : IWidgetPropertyResolver
    {
        public PrefabWidgetPropertyMap Resolve(XElement root, PrefabWidgetReferenceMap map, PreviewDiagnostics diagnostics) => PrefabWidgetPropertyMap.Empty;
    }
    private class ImageIdentifierWidget { }
    private sealed class InventoryImageIdentifierWidget : ImageIdentifierWidget { }
    private sealed class Reflection : IBannerlordWidgetReflectionService
    {
        public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryGetWidgetBaseType(out Type type) { type = typeof(object); return true; }
        public bool TryGetWidgetType(string name, out Type type) { type = typeof(InventoryImageIdentifierWidget); return name == "InventoryImageIdentifierWidget"; }
        public bool TryGetRenderKind(string name, out BannerlordWidgetRenderKind kind)
        {
            kind = name switch { "BrushListPanel" => BannerlordWidgetRenderKind.BrushListPanel, "InventoryItemValueTextWidget" => BannerlordWidgetRenderKind.TextWidget, _ => BannerlordWidgetRenderKind.Widget };
            return true;
        }
    }
    private sealed class Assets : IAssetCatalog
    {
        public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
        public SpriteDefinition? FindSprite(string? name) => null;
        public BrushDefinition? FindBrush(string? name) => name switch
        {
            "row" => new("row", "fixture", [new("Default", null, null, null, "#403020FF", null, null, null, null, null, null, null, null, null, null, null, null)], new Dictionary<string, BrushStyleDefinition>(), null, null, null, null),
            "cost" => new("cost", "fixture", [], new Dictionary<string, BrushStyleDefinition>
                { ["Default"] = new("Default", "#FFD987FF", 18, null, new Dictionary<string, BrushLayerDefinition>()),
                  ["Good"] = new("Good", "#00FF00FF", null, null, new Dictionary<string, BrushLayerDefinition>()) }, null, null, null, null),
            _ => null
        };
        public ImageSource? GetSpriteImage(string? name)
        {
            if (name != "sample") return null;
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
            image.Freeze(); return image;
        }
        public SpriteAssetResolution ResolveSpriteAsset(string? name) => new(name ?? "", SpriteAssetResolutionKind.Missing, null, null, null, null);
    }
}
