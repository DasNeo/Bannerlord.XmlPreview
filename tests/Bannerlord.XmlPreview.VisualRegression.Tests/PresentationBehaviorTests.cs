using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Behaviors.Bannerlord;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.VisualRegression.Cli;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class PresentationBehaviorTests
{
    [InstalledImageFact]
    public void InheritedRichTextWidgetsKeepTextPresentation()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var reflection = new BannerlordWidgetReflectionService(new Settings(), new BannerlordInstallationIndex());
            var factory = new BannerlordWidgetNodeFactory(reflection);
            foreach (var name in new[] { "AutoHideRichTextWidget", "ScrollingRichTextWidget" })
            {
                var node = factory.CreateNode(name, Attributes(name, (name.StartsWith("Scrolling") ? "ActualText" : "Text", "@Label")), []);
                Assert.IsType<RichTextWidgetNode>(node);
                var (element, _) = Render(node, bindings: new Dictionary<string, string> { ["Label"] = "Bound rich text" });
                Assert.Contains(Elements(element).OfType<TextBlock>(), t => t.Text == "Bound rich text");
            }
            return 0;
        });
    }

    [Theory]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("Bound text", Visibility.Visible)]
    public void EmptyRichTextAlsoHidesItsReferencedDivider(string text, Visibility expected)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var rich = new RichTextWidgetNode("AutoHideRichTextWidget", Attributes("Window > AutoHideRichTextWidget", ("Text", "@Label"), ("WidgetToHideIfEmpty", "..\\divider")), []);
            var divider = new WidgetNode(Attributes("Window > Widget[Id=divider]", ("Id", "divider")), []);
            var (element, _) = Render(new WindowNode(Attributes("Window"), [rich, divider]), bindings: new Dictionary<string, string> { ["Label"] = text });
            Assert.All(Elements(element).Where(e => PreviewElementDebugProperties.GetDebugInfo(e)?.NodeName == "AutoHideRichTextWidget" ||
                PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "divider"), e => Assert.Equal(expected, e.Visibility));
            return 0;
        });
    }

    [Fact]
    public void RichTextKeepsTextAndInlineSpritesAndReportsUnsupportedMarkup()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new RichTextWidgetNode(Attributes("rich", ("Text", "Before <img src='green' width='12' height='8'> <span color='#FF0000FF'>after</span>")), []);
            var (element, diagnostics) = Render(node);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            Assert.Contains(Elements(element).OfType<Image>(), i => i.ActualWidth == 12 && i.ActualHeight == 8);
            Assert.Contains(Elements(element).OfType<TextBlock>(), t => t.Text == "Before");
            Assert.Contains(Elements(element).OfType<TextBlock>(), t => t.Text == "after" && ((SolidColorBrush)t.Foreground).Color == Colors.Red);
            var (_, partial) = Render(new RichTextWidgetNode(Attributes("rich", ("Text", "<unknown>Still visible</unknown>")), []));
            Assert.Equal(RenderCompletionStatus.Partial, partial.CompletionStatus);
            Assert.Contains(partial.Messages, m => m.Contains("richtext.tag.unsupported"));
            return 0;
        });
    }

    [Theory]
    [InlineData(false, "#FF0000FF")]
    [InlineData(true, "#00FF00FF")]
    public void BooleanBrushPropagatesOnlyToBrushDescendants(bool flag, string color)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var text = new TextWidgetNode(Attributes("changer > TextWidget", ("Text", "text")), []);
            var plain = new WidgetNode(Attributes("changer > Widget"), []);
            var node = new BrushWidgetNode("BoolBrushChangerBrushWidget", Attributes("changer", ("BooleanCheck", flag.ToString()),
                ("IncludeChildren", "true"), ("TrueBrush", "positive"), ("FalseBrush", "negative")), [text, plain]);
            var (element, diagnostics) = Render(node);
            Assert.Contains(Elements(element).OfType<TextBlock>(), t => t.Text == "text" && ((SolidColorBrush)t.Foreground).Color == PrefabRenderContext.ParseBannerlordColor(color));
            Assert.DoesNotContain(diagnostics.BehaviorTrace.SelectMany(t => t.Contributions), c => c.Contribution is Bannerlord.XmlPreview.Core.Behaviors.DescriptionContribution d &&
                d.Property == "Brush" && d.Target?.Value == StableWidgetId.Create("fixture.xml", "changer > Widget", ""));
            Assert.Equal(new Thickness(), Assert.IsType<Border>(element).BorderThickness);
            return 0;
        });
    }

    [Theory]
    [InlineData("IconBrushWidget", "IconBrush", "IconID")]
    [InlineData("DefaultLayerSpriteChangerBrushWidget", "SpriteBrush", "SpriteBrushLayerName")]
    public void BrushLayerSelectionDoesNotMutateOrLeakBetweenRenders(string type, string sourceProperty, string layerProperty)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var assets = new Assets();
            var node = new BrushWidgetNode(type, Attributes(type, ("Brush", "target"), (sourceProperty, "source"), (layerProperty, "@Icon")), []);
            foreach (var identity in new[] { "A", "B", "A" })
            {
                var (element, _) = Render(node, assets, new Dictionary<string, string> { ["Icon"] = identity });
                Assert.Contains(Elements(element).OfType<Image>(), i => ReferenceEquals(i.Source, assets.GetSpriteImage(identity == "A" ? "red" : "green")));
            }
            Assert.Equal("red", assets.FindBrush("target")!.Layers[0].SpriteName);
            Assert.Equal("@Icon", node.Attributes[layerProperty]);
            if (type == "DefaultLayerSpriteChangerBrushWidget")
            {
                var (hidden, diagnostics) = Render(node, assets, new Dictionary<string, string> { ["Icon"] = "Absent" });
                Assert.Equal(Visibility.Collapsed, hidden.Visibility);
                Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            }
            return 0;
        });
    }

    [Theory]
    [InlineData("ItemTableauWidget")]
    [InlineData("CharacterTableauWidget")]
    [InlineData("MapConversationTableauWidget")]
    public void TableausHaveLabeledPartialPlaceholdersAndAcceptExplicitImages(string type)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new WidgetNode(type, Attributes(type, ("StringId", "crafted-item")), []);
            var (placeholder, diagnostics) = Render(node);
            Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            Assert.Equal(ImageResolutionStatus.RuntimeOnly, PreviewElementDebugProperties.GetImageResolution(placeholder)!.Status);
            Assert.Contains(Elements(placeholder).OfType<TextBlock>(), t => t.Text.Contains("RuntimeOnly"));
            var image = new WidgetNode(type, Attributes(type, ("Preview.ImageSprite", "green")), []);
            var (_, resolved) = Render(image);
            Assert.Equal(RenderCompletionStatus.Complete, resolved.CompletionStatus);
            return 0;
        });
    }

    [Fact]
    public void SpriteExtensionsExpandPaintWithoutChangingWidgetBounds()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new WidgetNode(Attributes("frame", ("Sprite", "red"), ("ExtendLeft", "5"), ("ExtendRight", "7"),
                ("ExtendTop", "3"), ("ExtendBottom", "4")), []);
            var (element, _) = Render(node);
            Assert.Equal(100, element.ActualWidth); Assert.Equal(30, element.ActualHeight);
            var image = Assert.Single(Elements(element).OfType<Image>());
            Assert.Equal(112, image.ActualWidth); Assert.Equal(37, image.ActualHeight);
            Assert.Equal(new Point(-5, -3), image.TranslatePoint(new Point(), element));
            return 0;
        });
    }

    [Fact]
    public void StackOffsetsMovePaintingWithoutChangingSiblingAllocation()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var one = new WidgetNode(Attributes("list > Widget[Id=one]", ("Id", "one"), ("SuggestedWidth", "20"), ("PositionXOffset", "3"), ("PositionYOffset", "-4")), []);
            var two = new WidgetNode(Attributes("list > Widget[Id=two]", ("Id", "two"), ("SuggestedWidth", "20")), []);
            var list = new ListPanelNode(Attributes("list", ("StackLayout.LayoutMethod", "HorizontalLeftToRight")), [one, two]);
            var (element, diagnostics) = Render(list);
            var first = diagnostics.Layout!.KeyBounds.Single(b => b.ExpandedPath.EndsWith("[Id=one]"));
            var second = diagnostics.Layout.KeyBounds.Single(b => b.ExpandedPath.EndsWith("[Id=two]"));
            Assert.Equal(3, first.X); Assert.Equal(-4, first.Y); Assert.Equal(20, second.X);
            return 0;
        });
    }

    [Fact]
    public void CoverChildrenSettlesCenteredContentWithoutViewportHeightLeakingIntoTheResult()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var content = new WidgetNode(Attributes("auto > Widget[Id=content]", ("Id", "content"), ("SuggestedHeight", "30")), []);
            var marker = new WidgetNode(Attributes("auto > Widget[Id=marker]", ("Id", "marker"), ("SuggestedHeight", "20"), ("VerticalAlignment", "Center")), []);
            var node = new BrushWidgetNode(Attributes("auto", ("HeightSizePolicy", "CoverChildren")), [content, marker]);
            var (element, diagnostics) = Render(node);
            Assert.Equal(30, element.ActualHeight, 1);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            Assert.Equal(new Thickness(), Assert.IsType<Border>(element).BorderThickness);
            return 0;
        });
    }

    [Fact]
    public void AdjacentIconsKeepIndependentBrushSelectionsInOneRender()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var one = new BrushWidgetNode("IconBrushWidget", Attributes("Window > IconBrushWidget#1", ("Brush", "target"), ("IconBrush", "source"), ("IconID", "A")), []);
            var two = new BrushWidgetNode("IconBrushWidget", Attributes("Window > IconBrushWidget#2", ("Brush", "target"), ("IconBrush", "source"), ("IconID", "B"), ("PositionXOffset", "100")), []);
            var assets = new Assets();
            var (element, _) = Render(new WindowNode(Attributes("Window"), [one, two]), assets);
            var images = Elements(element).OfType<Image>().ToArray();
            Assert.Contains(images, i => ReferenceEquals(i.Source, assets.GetSpriteImage("red")));
            Assert.Contains(images, i => ReferenceEquals(i.Source, assets.GetSpriteImage("green")));
            return 0;
        });
    }

    [Fact]
    public void IconsCanUseSourceDimensionsAndCopyLayerGeometry()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var assets = new Assets();
            var sized = new BrushWidgetNode("IconBrushWidget", Attributes("icon", ("Brush", "target"), ("IconBrush", "source"),
                ("IconID", "A"), ("UseIconSize", "true")), []);
            var (small, _) = Render(sized, assets);
            Assert.Equal(8, small.ActualWidth); Assert.Equal(4, small.ActualHeight);
            var styled = new BrushWidgetNode("IconBrushWidget", Attributes("icon", ("Brush", "target"), ("IconBrush", "source"),
                ("IconID", "A"), ("UseStylesFromSourceIcon", "true")), []);
            var (copied, _) = Render(styled, assets);
            var painted = VisualTreeHelper.GetDescendantBounds(copied);
            Assert.Equal(103, painted.Width);
            Assert.Equal(-3, painted.X);
            return 0;
        });
    }

    [Fact]
    public async Task ExplicitCaptureScaleScalesPixelsAndKeyBoundsTogether()
    {
        var data = NativeSuiteTests.DataRoot();
        var scenario = ScenarioLoader.Load(Path.Combine(data, "m3-portable-scenarios/standard-window.json"));
        using var capture = new BannerlordPreviewCapture(".", Path.Combine(data, "corpus"));
        var original = await capture.CaptureAsync(scenario, VisualRegressionMode.StructureOnly, TestContext.Current.CancellationToken);
        var scaled = scenario with { CaptureProfile = scenario.CaptureProfile with { Width = scenario.CaptureProfile.Width * 2,
            Height = scenario.CaptureProfile.Height * 2, UiScale = 2 } };
        ScenarioLoader.Validate(scaled);
        var result = await capture.CaptureAsync(scaled, VisualRegressionMode.StructureOnly, TestContext.Current.CancellationToken);
        foreach (var bounds in original.Layout.KeyBounds)
        {
            var next = result.Layout.KeyBounds.Single(b => b.WidgetId == bounds.WidgetId);
            Assert.Equal(bounds.X * 2, next.X); Assert.Equal(bounds.Y * 2, next.Y);
            Assert.Equal(bounds.Width * 2, next.Width); Assert.Equal(bounds.Height * 2, next.Height);
        }
        SnapshotCaptureTests.Sta(() =>
        {
            var (element, _) = Render(new WindowNode(Attributes("Window"), [new WidgetNode(Attributes("Window > Widget", ("Sprite", "red"),
                ("SuggestedWidth", "20"), ("SuggestedHeight", "10"), ("PositionXOffset", "10")), [])]));
            var pixels = WpfPreviewCapture.Capture(element, new("scaled", 400, 240, UiScale: 2));
            Assert.Equal(400, pixels.Width);
            Assert.Equal(0, pixels.Pixels[19 * 3]);
            Assert.Equal(255, pixels.Pixels[20 * 3]);
            Assert.Equal(255, pixels.Pixels[59 * 3]);
            Assert.Equal(0, pixels.Pixels[60 * 3]);
            return 0;
        });
    }

    private static Dictionary<string, string> Attributes(string path, params (string Key, string Value)[] properties)
    {
        var result = new Dictionary<string, string> { ["Debug.WidgetPath"] = path, ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "100", ["SuggestedHeight"] = "30" };
        foreach (var (key, value) in properties) result[key] = value;
        return result;
    }
    private static (FrameworkElement, PreviewDiagnostics) Render(PrefabNode node, Assets? assets = null, IReadOnlyDictionary<string, string>? bindings = null)
    {
        assets ??= new Assets();
        var diagnostics = new PreviewDiagnostics();
        var renderer = new PrefabRenderer(assets, new BannerlordBrushMetricsProvider(assets), new BannerlordBrushOverlayFeatureHandler(),
            new SnapshotCaptureTests.EmptyData(), new BannerlordRootLayoutHints(), new BannerlordWidgetTextSourceProvider(),
            new BannerlordWidgetDebugMetadataProvider(), new BannerlordBehaviorRuleProvider());
        var document = new PrefabDocument("fixture.xml", node, new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty,
            new WidgetReferenceResolver().Resolve(node, diagnostics));
        var element = renderer.Render(document, diagnostics, new("fixture.xml", bindings ?? new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(), []) { PreviewSize = new(200, 120) });
        return (element, diagnostics);
    }
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class Settings : IAppSettingsService
    {
        public string BannerlordInstallRoot => Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
        public bool TrySetBannerlordInstallRoot(string path, out string normalizedPath, out string errorMessage) { normalizedPath = path; errorMessage = ""; return true; }
    }
    private sealed class Assets : IAssetCatalog
    {
        private readonly Dictionary<string, BitmapSource> _images = new();
        internal Assets()
        {
            foreach (var (name, color) in new[] { ("red", Colors.Red), ("green", Colors.Green) })
            {
                var pixels = Enumerable.Range(0, 32).SelectMany(_ => new byte[] { color.B, color.G, color.R, 255 }).ToArray();
                var bitmap = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 32); bitmap.Freeze(); _images[name] = bitmap;
            }
        }
        private static BrushLayerDefinition Layer(string name, string sprite) => new(name, sprite, null, null, null, null, null, null, null, null, false, 0, 0, 0, 0, false, false);
        public BrushDefinition? FindBrush(string? name) => name switch
        {
            "source" => new(name, "", [Layer("A", "red") with { ExtendLeft = 3 }, Layer("B", "green")], new Dictionary<string, BrushStyleDefinition>(), null, null, null, null),
            "target" => new(name, "", [Layer("Default", "red")], new Dictionary<string, BrushStyleDefinition>(), null, null, null, null),
            "positive" or "negative" => new(name, "", [], new Dictionary<string, BrushStyleDefinition> { ["Default"] = new("Default", name == "positive" ? "#00FF00FF" : "#FF0000FF", 16, 1, new Dictionary<string, BrushLayerDefinition>()) }, null, null, null, null),
            _ => null
        };
        public SpriteDefinition? FindSprite(string? name) => name is not null && _images.ContainsKey(name) ? new(name, "", "Sprite", "", "", 0, 8, 4) : null;
        public ImageSource? GetSpriteImage(string? name) => name is not null ? _images.GetValueOrDefault(name) : null;
        public SpriteAssetResolution ResolveSpriteAsset(string? name) => new(name ?? "", SpriteAssetResolutionKind.ExactName, name, FindSprite(name) is null ? null : "fixture", FindSprite(name), null);
        public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushNames, IReadOnlyCollection<string> spriteNames, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
