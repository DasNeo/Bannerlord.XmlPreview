using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class SnapshotCaptureTests
{
    [Theory]
    [InlineData("Left", "Top", 1, 2)]
    [InlineData("Center", "Center", 9, 5)]
    [InlineData("Right", "Bottom", 17, 8)]
    public void FixedWidgetAlignmentAndOffsetsHaveExplicitBounds(string horizontal, string vertical, double x, double y)
    {
        Sta(() =>
        {
            var attributes = Attributes("Widget > Widget[Id=child]", "child");
            attributes["SuggestedWidth"] = "4";
            attributes["SuggestedHeight"] = "4";
            attributes["HorizontalAlignment"] = horizontal;
            attributes["VerticalAlignment"] = vertical;
            attributes["PositionXOffset"] = "1";
            attributes["PositionYOffset"] = "2";
            var rootAttributes = Attributes("Widget");
            rootAttributes["SuggestedWidth"] = "20";
            rootAttributes["SuggestedHeight"] = "10";
            var document = new PrefabDocument("fixture.xml", new WidgetNode(rootAttributes, [new WidgetNode(attributes, [])]),
                new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
            var diagnostics = new PreviewDiagnostics();
            Renderer(new EmptyData()).Render(document, diagnostics, Options());
            var bounds = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath == attributes[PrefabVisualTreeHelper.DebugWidgetPathAttributeName]);
            Assert.Equal(x, bounds.X, 6);
            Assert.Equal(y, bounds.Y, 6);
            Assert.Equal(4, bounds.Width, 6);
            Assert.Equal(4, bounds.Height, 6);
            Assert.Empty(diagnostics.Messages);
            return 0;
        });
    }

    [Fact]
    public void BoundTextAndVisibilityReachRenderedElementsWithoutLeakingScenarioState()
    {
        Sta(() =>
        {
            var attributes = Attributes("Window > TextWidget[Id=label]", "label");
            attributes["Text"] = "@Greeting";
            attributes["IsVisible"] = "true";
            var document = Document(new TextWidgetNode(attributes, []));
            var renderer = Renderer(new EmptyData());
            var options = new RegressionRenderOptions("fixture.xml", new Dictionary<string, string> { ["Greeting"] = "Hello" },
                new Dictionary<string, IReadOnlyDictionary<string, string>> { [attributes[PrefabVisualTreeHelper.DebugWidgetPathAttributeName]] =
                    new Dictionary<string, string> { ["IsVisible"] = "false" } }, []);
            var root = renderer.Render(document, new PreviewDiagnostics(), options);
            var text = Assert.Single(Visuals(root).OfType<TextBlock>());
            Assert.Equal("Hello", text.Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)VisualTreeHelper.GetParent(text)).Visibility);
            var next = renderer.Render(document, new PreviewDiagnostics(), options with
            {
                Bindings = new Dictionary<string, string> { ["Greeting"] = "Goodbye" },
                WidgetStates = new Dictionary<string, IReadOnlyDictionary<string, string>>()
            });
            var nextText = Assert.Single(Visuals(next).OfType<TextBlock>());
            Assert.Equal("Goodbye", nextText.Text);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)VisualTreeHelper.GetParent(nextText)).Visibility);
            Assert.Equal("@Greeting", document.RootNode.Children[0].Attributes["Text"]);
            return 0;
        });
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Fact]
    public void ObjectAllocationAndVisualHelperElementsDoNotChangeSemanticSnapshots()
    {
        var first = Sta(() => RenderHelper(false));
        var second = Sta(() => RenderHelper(true));
        Assert.Equal(SnapshotSerialization.Serialize(first.Structure), SnapshotSerialization.Serialize(second.Structure));
        Assert.Equal(SnapshotSerialization.Serialize(first.Layout), SnapshotSerialization.Serialize(second.Layout));
    }

    [Fact]
    public void ListKeysPreserveWidgetIdentityAcrossReorderingAndMatchLayoutIds()
    {
        var first = Sta(() => RenderList(["alice", "bob"]));
        var reordered = Sta(() => RenderList(["bob", "alice"]));
        var firstRows = first.Structure.Root.Children[0].Children.SelectMany(t => t.Children).ToDictionary(n => n.InstanceKey);
        var secondRows = reordered.Structure.Root.Children[0].Children.SelectMany(t => t.Children).ToDictionary(n => n.InstanceKey);
        Assert.Equal(2, firstRows.Count);
        Assert.Equal(new[] { "alice", "bob" }, firstRows.Values.Select(row => row.Properties["Text"]));
        Assert.Equal(new[] { "bob", "alice" }, secondRows.Values.Select(row => row.Properties["Text"]));
        foreach (var row in firstRows)
        {
            Assert.Equal(row.Value.WidgetId, secondRows[row.Key].WidgetId);
            Assert.Contains(first.Layout.KeyBounds, b => b.WidgetId == row.Value.WidgetId);
        }
        Assert.NotEqual(SnapshotSerialization.Serialize(first.Structure), SnapshotSerialization.Serialize(reordered.Structure));
    }

    [Fact]
    public void WidgetStateAndBindingScopesDoNotLeakBetweenRuns()
    {
        Sta(() =>
        {
            var attributes = Attributes("Window > Widget[Id=sample]", "sample");
            attributes["IsVisible"] = "true";
            var child = new HelperWidgetNode(attributes, false);
            var document = Document(child);
            var renderer = Renderer(new EmptyData());
            var first = new PreviewDiagnostics();
            renderer.Render(document, first, new("fixture.xml", new Dictionary<string, string>(),
                new Dictionary<string, IReadOnlyDictionary<string, string>> { [attributes[PrefabVisualTreeHelper.DebugWidgetPathAttributeName]] = new Dictionary<string, string> { ["IsVisible"] = "false" } }, []));
            Assert.Equal("false", first.RenderStructure!.Root.Children[0].Properties["IsVisible"]);
            Assert.Equal("true", child.Attributes["IsVisible"]);
            var second = new PreviewDiagnostics();
            renderer.Render(document, second, Options());
            Assert.Equal("true", second.RenderStructure!.Root.Children[0].Properties["IsVisible"]);
            return 0;
        });
    }

    [Fact]
    public void IsolatedWpfCaptureHasExactRgbPixelsAndDeterministicPng()
    {
        Sta(() =>
        {
            using var fixture = new RegressionFixture();
            var root = new Border { Background = new SolidColorBrush(Color.FromRgb(128, 64, 255)) };
            var image = WpfPreviewCapture.Capture(root, new("synthetic", 2, 1));
            Assert.Equal(2, image.Width); Assert.Equal(1, image.Height);
            Assert.Equal(new byte[] { 128, 64, 255, 128, 64, 255 }, image.Pixels);
            var first = Path.Combine(fixture.Root, "first.png");
            var second = Path.Combine(fixture.Root, "second.png");
            WpfPreviewCapture.WritePng(first, image); WpfPreviewCapture.WritePng(second, image);
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            return 0;
        });
    }

    [Fact]
    public void ReferencePngAlphaIsCompositedBeforeRgbComparison()
    {
        Sta(() =>
        {
            using var fixture = new RegressionFixture();
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 20, 40, 60, 128 }, 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(fixture.Root, "alpha.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            Assert.Equal(new byte[] { 38, 36, 34 }, WpfPreviewCapture.ReadPng(path, "#102030").Pixels);
            return 0;
        });
    }

    [Fact]
    public void CapturePreservesRootCoordinatesAndClipsOverflowWithoutRecentering()
    {
        Sta(() =>
        {
            var root = new Canvas { Background = Brushes.Black };
            var outside = new Border { Width = 1, Height = 1, Background = Brushes.Red };
            var inside = new Border { Width = 1, Height = 1, Background = Brushes.Blue };
            Canvas.SetLeft(outside, -1); Canvas.SetLeft(inside, 1);
            root.Children.Add(outside); root.Children.Add(inside);
            var image = WpfPreviewCapture.Capture(root, new("synthetic", 2, 1));
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 255 }, image.Pixels);
            return 0;
        });
    }

    private static (RenderStructureSnapshot Structure, LayoutSnapshot Layout) RenderHelper(bool helper)
    {
        if (helper) _ = Enumerable.Range(0, 100).Select(_ => new object()).ToArray();
        var child = new HelperWidgetNode(Attributes("Window > Widget[Id=sample]", "sample"), helper);
        var diagnostics = new PreviewDiagnostics();
        Renderer(new EmptyData()).Render(Document(child), diagnostics, Options());
        return (diagnostics.RenderStructure!, diagnostics.Layout!);
    }

    private static (RenderStructureSnapshot Structure, LayoutSnapshot Layout) RenderList(string[] keys)
    {
        var listPath = "Window > ListPanel[Id=rows]";
        var textPath = listPath + " > ItemTemplate > TextWidget[Id=row]";
        var textAttributes = Attributes(textPath, "row"); textAttributes["Text"] = "@StringItem";
        var template = new ItemTemplateNode(Attributes(listPath + " > ItemTemplate"), [new TextWidgetNode(textAttributes, [])]);
        var list = new ListPanelNode(Attributes(listPath, "rows"), [template]);
        var diagnostics = new PreviewDiagnostics();
        Renderer(new KeyedData(keys)).Render(Document(list), diagnostics, Options());
        return (diagnostics.RenderStructure!, diagnostics.Layout!);
    }

    private static Dictionary<string, string> Attributes(string path, string? id = null)
    {
        var result = new Dictionary<string, string> { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = path,
            ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "2", ["SuggestedHeight"] = "1" };
        if (id is not null) result["Id"] = id;
        return result;
    }
    private static PrefabDocument Document(PrefabNode child) => new("ignored-absolute-path.xml", new WindowNode(Attributes("Window"), [child]),
        new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
    private static RegressionRenderOptions Options() => new("fixture.xml", new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(), []);
    internal static PrefabRenderer Renderer(IPreviewDataSourceProvider data, Bannerlord.XmlPreview.Core.Behaviors.IBehaviorRuleProvider? rules = null)
    {
        var assets = new EmptyAssets();
        return new(assets, new BannerlordBrushMetricsProvider(assets), new BannerlordBrushOverlayFeatureHandler(), data,
            new FixedLayout(), new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider(), rules);
    }

    internal static T Sta<T>(Func<T> action)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { source.SetResult(action()); } catch (Exception e) { source.SetException(e); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return source.Task.GetAwaiter().GetResult();
    }

    private sealed class HelperWidgetNode(IReadOnlyDictionary<string, string> attributes, bool helper) : PrefabNode("Widget", attributes, [])
    {
        public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
        {
            var border = new Border { Width = 2, Height = 1 };
            if (helper) border.Child = new Grid { Children = { new Border { Visibility = Visibility.Collapsed } } };
            context.ApplyVisibility(border, Attributes);
            return new(border, null);
        }
    }
    private sealed class FixedLayout : IRootLayoutHints
    {
        public Size PreviewSize { get; set; } = new(20, 10);
        public Size ResolveRootSize(Func<string?, double, double> resolveDouble) => PreviewSize;
    }
    internal class EmptyData : IPreviewDataSourceProvider
    {
        public virtual bool TryGetTemplateItemBindings(PrefabNode node, out IReadOnlyList<IReadOnlyDictionary<string, string>> items) { items = []; return false; }
        public void SetTemplateItemBindings(PrefabNode node, IReadOnlyList<IReadOnlyDictionary<string, string>> items) => throw new NotSupportedException();
    }
    private sealed class KeyedData(string[] keys) : EmptyData
    {
        public override bool TryGetTemplateItemBindings(PrefabNode node, out IReadOnlyList<IReadOnlyDictionary<string, string>> items)
        { items = keys.Select(key => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["__key"] = key, ["StringItem"] = key }).ToArray(); return true; }
    }
    private sealed class EmptyAssets : IAssetCatalog
    {
        public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
        public SpriteDefinition? FindSprite(string? name) => null;
        public BrushDefinition? FindBrush(string? name) => null;
        public ImageSource? GetSpriteImage(string? name) => null;
        public SpriteAssetResolution ResolveSpriteAsset(string? name) => new(name ?? "", SpriteAssetResolutionKind.Missing, null, null, null, null);
    }
}
