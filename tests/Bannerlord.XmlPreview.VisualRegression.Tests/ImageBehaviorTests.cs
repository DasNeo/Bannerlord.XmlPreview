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
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class ImageBehaviorTests
{
    [Fact]
    public void AlphaBoundsAreIdenticalForPremultipliedAndOtherPixelFormats()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var pixels = new byte[4 * 3 * 4];
            pixels[(1 * 4 + 1) * 4 + 3] = 128;
            pixels[(1 * 4 + 2) * 4 + 3] = 255;
            var source = BitmapSource.Create(4, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            foreach (var format in new[] { PixelFormats.Bgra32, PixelFormats.Pbgra32, PixelFormats.Rgba64 })
            {
                var image = new FormatConvertedBitmap(source, format, null, 0);
                image.Freeze();
                var assets = new Images { SourceImage = image };
                var context = new PrefabRenderContext(new Dictionary<string, string>(), PrefabWidgetPropertyMap.Empty,
                    PrefabWidgetReferenceMap.Empty, new PreviewDiagnostics(), assets, new BannerlordBrushMetricsProvider(assets),
                    new BannerlordBrushOverlayFeatureHandler(), new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider());
                Assert.Equal(new Rect(10, 10, 20, 10), context.ResolveSpriteSelectionRect("source", new Size(40, 30)));
                Assert.Equal(new Rect(0, 10, 40, 10), context.ResolveSpriteContentLayoutRect("source", new Size(40, 30)));
            }
            return 0;
        });
    }

    [Theory]
    [InlineData("CoverChildren", "Fixed")]
    [InlineData("Fixed", "CoverChildren")]
    [InlineData("CoverChildren", "CoverChildren")]
    public void ProviderImageDimensionsParticipateInCoverChildrenSizing(string widthPolicy, string heightPolicy)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render("ImageIdentifierWidget", ("ImageId", "source"),
                ("TextureProviderName", "Preview.Sprite"), ("WidthSizePolicy", widthPolicy), ("HeightSizePolicy", heightPolicy));
            var widget = ImageWidget(root);
            var resolution = PreviewElementDebugProperties.GetImageResolution(widget)!;
            Assert.Equal(ImageResolutionStatus.Resolved, resolution.Status);
            Assert.Equal(2, resolution.Width); Assert.Equal(1, resolution.Height);
            Assert.Equal(2, root.ActualWidth); Assert.Equal(1, root.ActualHeight);
            Assert.Equal(2, widget.ActualWidth); Assert.Equal(1, widget.ActualHeight);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("ImageIdentifierWidget", "Fixed", 1, 1)]
    [InlineData("ImageIdentifierWidget", "StretchToParent", 2, 1)]
    [InlineData("MaskedTextureWidget", "Fixed", 1, 1)]
    [InlineData("MaskedTextureWidget", "StretchToParent", 2, 1)]
    [InlineData("MaskedTextureWidget", "CoverChildren", 2, 1)]
    public void ProviderImageSizingPreservesExplicitPoliciesAndMasking(string type, string policy, double width, double height)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render(type, ("ImageId", "source"), ("TextureProviderName", "Preview.Sprite"),
                ("MaskSprite", "mask"), ("WidthSizePolicy", policy), ("HeightSizePolicy", policy), ("SuggestedWidth", "1"));
            var widget = ImageWidget(root);
            Assert.Equal(width, widget.ActualWidth); Assert.Equal(height, widget.ActualHeight);
            if (type == "MaskedTextureWidget")
                Assert.Contains(Elements(widget).OfType<Image>(), i => i.OpacityMask is ImageBrush);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("ImageWidget", "source", "#FF000080", new byte[] { 128, 0, 0, 128, 0, 0 })]
    [InlineData("ImageWidget", "mask", "#FF0000FF", new byte[] { 255, 0, 0, 0, 0, 0 })]
    [InlineData("Widget", "source", "#FF000080", new byte[] { 128, 0, 0, 128, 0, 0 })]
    [InlineData("Widget", "mask", "#FF0000FF", new byte[] { 255, 0, 0, 0, 0, 0 })]
    [InlineData("Widget", "source", "#080806B0", new byte[] { 5, 5, 4, 5, 5, 4 })]
    public void StaticImageTintPreservesSourceTransparencyAndAppliesAlphaOnce(string type, string sprite, string color, byte[] expected)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render(type, ("Sprite", sprite), ("Color", color));
            var widget = Elements(root).OfType<Border>().Single(e => PreviewElementDebugProperties.GetDebugInfo(e)?.NodeName == type);
            widget.CornerRadius = new CornerRadius(0);
            ((Panel)VisualTreeHelper.GetParent(widget)).Children.Remove(widget);
            var parent = new Grid { Width = 2, Height = 1 };
            parent.Children.Add(widget);
            var pixels = WpfPreviewCapture.Capture(parent, new("static-tint", 2, 1, Background: "#000000"));
            Assert.Equal(expected, pixels.Pixels);
            if (type == "ImageWidget")
                Assert.Equal(ImageResolutionStatus.Resolved, PreviewElementDebugProperties.GetImageResolution(widget)!.Status);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("Widget", 12, 8)]
    [InlineData("Widget", 8, 12)]
    [InlineData("Widget", 2, 2)]
    [InlineData("ImageWidget", 12, 8)]
    public void NineRegionFramePreservesBorderAndTransparentCenterAfterResize(string type, int width, int height)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render(type, ("Sprite", "frame"), ("Color", "#FF000080"), ("AlphaFactor", "0.5"),
                ("SuggestedWidth", width.ToString()), ("SuggestedHeight", height.ToString()));
            var widget = Elements(root).OfType<Border>().Single(e => PreviewElementDebugProperties.GetDebugInfo(e)?.NodeName == type);
            ((Panel)VisualTreeHelper.GetParent(widget)).Children.Remove(widget);
            // Re-arrange the same visual at a new size to catch fixed-size or whole-image overlays.
            foreach (var size in new[] { (width, height), (width + 4, height + 4) })
            {
                widget.Width = size.Item1; widget.Height = size.Item2;
                var parent = new Grid { Width = size.Item1, Height = size.Item2 };
                parent.Children.Add(widget);
                var pixels = WpfPreviewCapture.Capture(parent, new("nine-region", size.Item1, size.Item2, Background: "#000000"));
                for (int y = 0; y < size.Item2; y++)
                    for (int x = 0; x < size.Item1; x++)
                    {
                        var index = (y * size.Item1 + x) * 3;
                        var edge = x == 0 || x == size.Item1 - 1 || y == 0 || y == size.Item2 - 1;
                        Assert.InRange(pixels.Pixels[index], edge ? 63 : 0, edge ? 65 : 0);
                        Assert.Equal(0, pixels.Pixels[index + 1]); Assert.Equal(0, pixels.Pixels[index + 2]);
                    }
                parent.Children.Remove(widget);
            }
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("ImageWidget")]
    public void TintConvertsStraightAlphaPixelsBeforeMultiplication(string type)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 255, 255, 255, 128, 255, 255, 255, 0 }, 8);
            source.Freeze();
            var (root, _) = Render(new Images { SourceImage = source }, type, ("Sprite", "source"), ("Color", "#FF000080"));
            var pixels = WpfPreviewCapture.Capture(root, new("straight-alpha", 2, 1, Background: "#000000"));
            Assert.InRange(pixels.Pixels[0], 63, 65);
            Assert.All(pixels.Pixels.Skip(1), value => Assert.Equal(0, value));
            return 0;
        });
    }

    [Fact]
    public void StaticImagePreservesSizingStretchTintAndOpacity()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render("ImageWidget", ("Sprite", "source"), ("Stretch", "Fill"), ("AlphaFactor", "0.5"), ("Color", "#FF0000FF"));
            var widget = ImageWidget(root);
            Assert.Equal(2, widget.ActualWidth); Assert.Equal(1, widget.ActualHeight); Assert.Equal(.5, widget.Opacity);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            var pixels = WpfPreviewCapture.Capture(root, new("images", 2, 1, Background: "#000000"));
            Assert.InRange(pixels.Pixels[0], 127, 128); Assert.Equal(0, pixels.Pixels[1]); Assert.Equal(0, pixels.Pixels[2]);
            Assert.Equal("Resolved", diagnostics.RenderStructure!.Root.Children[0].Properties["Resolved.ImageStatus"]);
            return 0;
        });
    }

    [Fact]
    public void ProviderInputsAndScenarioImageDataRemainExplicit()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var assets = new Images();
            var (root, diagnostics) = Render(assets, "ImageIdentifierWidget", ("ImageId", "banner.42"),
                ("TextureProviderName", "Native.Banner"), ("AdditionalArgs", "red"), ("Preview.ImageSprite", "source"));
            var image = PreviewElementDebugProperties.GetImageResolution(ImageWidget(root))!;
            Assert.Equal("banner.42", image.Request.Identity); Assert.Equal("Native.Banner", image.Request.Provider);
            Assert.Equal("red", image.Request.AdditionalArgs); Assert.Equal("source", image.Request.ScenarioSprite);
            Assert.Equal(ImageResolutionStatus.Resolved, image.Status);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("Missing", "missing", "")]
    [InlineData("RuntimeOnly", "hero", "Game.Portrait")]
    public void UnavailableImagesHaveDistinctVisiblePlaceholdersAndPartial(string status, string id, string provider)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render("ImageIdentifierWidget", ("ImageId", id), ("TextureProviderName", provider));
            var widget = ImageWidget(root);
            Assert.Equal(status, PreviewElementDebugProperties.GetImageResolution(widget)!.Status.ToString());
            Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            Assert.Contains(Elements(root).OfType<TextBlock>(), t => t.Text.StartsWith(status + ":"));
            Assert.Equal(Visibility.Visible, widget.Visibility);
            return 0;
        });
    }

    [Fact]
    public void NullImageOnlyCollapsesWhenHideWhenNullIsDeclared()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (hidden, diagnostics) = Render("ImageIdentifierWidget", ("ImageId", ""), ("HideWhenNull", "true"));
            Assert.Equal(Visibility.Collapsed, ImageWidget(hidden).Visibility);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            var (visible, partial) = Render("ImageIdentifierWidget", ("ImageId", ""));
            Assert.Equal(Visibility.Visible, ImageWidget(visible).Visibility);
            Assert.Equal(RenderCompletionStatus.Partial, partial.CompletionStatus);
            return 0;
        });
    }

    [Theory]
    [InlineData("mask")]
    [InlineData("mask-r8")]
    public void MaskAlphaAndColorDetermineCapturedPixels(string mask)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (root, diagnostics) = Render("MaskedTextureWidget", ("ImageId", "source"), ("TextureProviderName", "Preview.Sprite"),
                ("MaskSprite", mask), ("Color", "#00FF00FF"));
            var pixels = WpfPreviewCapture.Capture(root, new("mask", 2, 1, Background: "#000000"));
            Assert.Equal(new byte[] { 0, 255, 0, 0, 0, 0 }, pixels.Pixels);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            Assert.Contains(Elements(root).OfType<Image>(), i => i.OpacityMask is ImageBrush);
            return 0;
        });
    }

    [Theory]
    [InlineData("missing", "mask")]
    [InlineData("source", "missing")]
    public void MissingSourceOrMaskCannotPass(string source, string mask)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var (_, diagnostics) = Render("MaskedTextureWidget", ("ImageId", source), ("MaskSprite", mask));
            Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Fact]
    public void InvalidDataFailsAndTableauAndCodeWidgetsRemainPartial()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            Assert.Throws<InvalidDataException>(() => Render("ImageIdentifierWidget", ("ImageId", "invalid")));
            var (_, tableau) = Render("BannerTableauWidget", ("BannerCodeText", "fixed-banner-code"));
            Assert.Equal(RenderCompletionStatus.Partial, tableau.CompletionStatus);
            var (_, code) = Render("ModCodeWidget");
            Assert.Equal(RenderCompletionStatus.Partial, code.CompletionStatus);
            return 0;
        });
    }

    private static (FrameworkElement, PreviewDiagnostics) Render(string type, params (string, string)[] attributes) => Render(new Images(), type, attributes);
    private static (FrameworkElement, PreviewDiagnostics) Render(Images assets, string type, params (string, string)[] values)
    {
        var attrs = new Dictionary<string, string> { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = "Window > " + type,
            ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "2", ["SuggestedHeight"] = "1" };
        foreach (var (name, value) in values) attrs[name] = value;
        var node = new WidgetNode(type, attrs, []);
        var rootAttrs = new Dictionary<string, string>(attrs) { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = "Window" };
        rootAttrs["WidthSizePolicy"] = rootAttrs["HeightSizePolicy"] = "Fixed";
        var document = new PrefabDocument("fixture.xml", new WindowNode(rootAttrs, [node]), new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
        var diagnostics = new PreviewDiagnostics();
        var renderer = new PrefabRenderer(assets, new BannerlordBrushMetricsProvider(assets), new BannerlordBrushOverlayFeatureHandler(),
            new SnapshotCaptureTests.EmptyData(), new ImageLayout(), new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider(), new BannerlordBehaviorRuleProvider());
        var root = renderer.Render(document, diagnostics, new("fixture.xml", new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), []));
        return (root, diagnostics);
    }
    private static FrameworkElement ImageWidget(FrameworkElement root) => Elements(root).Single(e => PreviewElementDebugProperties.GetImageResolution(e) is not null);
    internal static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private sealed class Images : IAssetCatalog
    {
        public BitmapSource? SourceImage { get; init; }
        public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
        public BrushDefinition? FindBrush(string? name) => null;
        public SpriteDefinition? FindSprite(string? name) => name == "frame"
            ? new(name, "fixture", "NineRegionSprite", name, "fixture", 1, 3, 3, 1, 1, 1, 1)
            : name is "source" or "mask" ? new(name, "fixture", "Sprite", name, "fixture", 1, 2, 1) : null;
        public ImageSource? GetSpriteImage(string? name) => name switch
        {
            "source" => SourceImage ?? Bitmap([255, 255, 255, 255, 255, 255, 255, 255]),
            "mask" => Bitmap([255, 255, 255, 255, 0, 0, 0, 0]),
            "mask-r8" => Bitmap([255, 255, 255, 255, 0, 0, 0, 255]),
            "frame" => FrameBitmap(), _ => null
        };
        public SpriteAssetResolution ResolveSpriteAsset(string? name) => new(name ?? "", FindSprite(name) is null ? SpriteAssetResolutionKind.Missing : SpriteAssetResolutionKind.ExactName,
            name, FindSprite(name) is null ? null : "fixture", FindSprite(name), null);
        public ImageResolution ResolveImage(ImageRequest request)
        {
            if (request.Identity == "invalid") return new(request, ImageResolutionStatus.Invalid, null, Reason: "Corrupt fixture pixels.");
            if (!string.IsNullOrEmpty(request.Provider) && request.Provider != "Preview.Sprite" && request.ScenarioSprite is null)
                return new(request, ImageResolutionStatus.RuntimeOnly, null, Reason: "Runtime fixture provider.");
            var image = GetSpriteImage(request.ScenarioSprite ?? request.Identity) as BitmapSource;
            return new(request, image is null ? ImageResolutionStatus.Missing : ImageResolutionStatus.Resolved, image,
                Format: request.Identity == "mask-r8" ? "R8_UNORM" : null, Reason: image is null ? "Missing fixture." : null);
        }
        private static BitmapSource Bitmap(byte[] pixels)
        {
            var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Pbgra32, null, pixels, 8); bitmap.Freeze(); return bitmap;
        }
        private static BitmapSource FrameBitmap()
        {
            var pixels = Enumerable.Repeat((byte)255, 3 * 3 * 4).ToArray();
            Array.Clear(pixels, 4 * 4, 4);
            var bitmap = BitmapSource.Create(3, 3, 96, 96, PixelFormats.Pbgra32, null, pixels, 12);
            bitmap.Freeze(); return bitmap;
        }
    }
    private sealed class ImageLayout : Bannerlord.XmlPreview.Core.Abstractions.IRootLayoutHints
    {
        public Size PreviewSize { get; set; } = new(2, 1);
        public Size ResolveRootSize(Func<string?, double, double> resolveDouble) => new(2, 1);
    }
}
