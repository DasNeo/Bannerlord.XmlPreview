using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class TournamentLeaderboardTests
{
    [Fact]
    public void BitmapFontUsesAtlasCoverageAdvancesAndKerning()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var atlas = System.Windows.Media.Imaging.BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 0, 255, 128, 128, 128, 255, 255, 255, 255, 255 }, 12);
            atlas.Freeze();
            var font = new Bannerlord.XmlPreview.Models.Assets.BitmapFontDefinition("fixture", 32, 4, 0.5, atlas,
                new Dictionary<int, Bannerlord.XmlPreview.Models.Assets.BitmapFontGlyph> { [65] = new(65, 0, 0, 3, 1, 5, 0, 0) },
                new Dictionary<(int, int), double> { [(65, 65)] = -1 });
            var text = new BitmapFontTextBlock { BitmapFont = font,
                StyleSource = new TextBlock { Text = "AA", FontSize = 32, Foreground = Brushes.White } };
            text.Measure(new Size(20, 10));
            Assert.Equal(9, text.DesiredSize.Width);
            Assert.Equal(4, text.DesiredSize.Height);
            var pixels = WpfPreviewCapture.Capture(text, new("font", 20, 10));
            Assert.Contains(pixels.Pixels, channel => channel == 255);
            Assert.Contains(pixels.Pixels, channel => channel is > 0 and < 255);
            return 0;
        });
    }

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void LocalVisibilityAndIntTextAllocateOneNameAndOneVictoryCell(string mainHero)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            Dictionary<string, string> Attributes(string path, params (string Key, string Value)[] values)
            {
                var result = new Dictionary<string, string> { ["Debug.WidgetPath"] = path, ["WidthSizePolicy"] = "StretchToParent", ["HeightSizePolicy"] = "StretchToParent" };
                foreach (var (key, value) in values) result[key] = value;
                return result;
            }
            var children = new PrefabNode[]
            {
                new RichTextWidgetNode(Attributes("ListPanel > normal", ("Text", "@Name"), ("IsHidden", "@IsMainHero"), ("MarginLeft", "10")), []),
                new RichTextWidgetNode(Attributes("ListPanel > player", ("Text", "@Name"), ("IsVisible", "@IsMainHero"), ("MarginLeft", "10")), []),
                new TextWidgetNode(Attributes("ListPanel > normalCount", ("IntText", "@Victories"), ("IsHidden", "@IsMainHero")), []),
                new TextWidgetNode(Attributes("ListPanel > playerCount", ("IntText", "@Victories"), ("IsVisible", "@IsMainHero")), [])
            };
            var root = new ListPanelNode(Attributes("ListPanel", ("WidthSizePolicy", "Fixed"), ("HeightSizePolicy", "Fixed"), ("SuggestedWidth", "400"), ("SuggestedHeight", "60")), children);
            var document = new PrefabDocument("fixture.xml", root, new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
            var rendered = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData()).Render(document, new PreviewDiagnostics(),
                new("fixture.xml", new Dictionary<string, string> { ["Name"] = "Ironwood", ["Victories"] = "12", ["IsMainHero"] = mainHero }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), []));
            var panel = Assert.IsType<GauntletStackLayoutPanel>(rendered);
            var cells = panel.Children.OfType<FrameworkElement>().Where(e => e.Visibility == Visibility.Visible).ToArray();
            Assert.Equal(2, cells.Length);
            Assert.Equal(new[] { "Ironwood", "12" }, cells.SelectMany(e => Elements(e).OfType<TextBlock>()).Select(t => t.Text));
            Assert.Equal(190, cells[0].ActualWidth);
            Assert.Equal(200, cells[1].ActualWidth);
            Assert.Equal(10, cells[0].TranslatePoint(new Point(), panel).X);
            Assert.Equal(200, cells[1].TranslatePoint(new Point(), panel).X);
            return 0;
        });
    }

    [InstalledImageFact]
    public void PopulatedAndEmptyLeaderboardPreserveColumnsAndConfineScrollbarPainting()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (assets, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
            var path = Path.Combine(root, "Modules/Native/GUI/Prefabs/GameMenu/GameMenuTournamentLeaderboard.xml");
            var font = assets.FindFont("FiraSansExtraCondensed-Regular");
            Assert.NotNull(font);
            Assert.Equal(1024, font.Atlas.PixelHeight);
            var document = loader.Load(path, new PreviewDiagnostics());
            var nodes = Nodes(document.RootNode).ToArray();
            string WidgetPath(PrefabNode n) => n.Attributes["Debug.WidgetPath"];
            var list = Assert.Single(nodes, n => n.Attributes.GetValueOrDefault("Id") == "EntriesList");
            var scroll = Assert.Single(nodes, n => n.Attributes.GetValueOrDefault("Id") == "EntriesListScrollablePanel");
            foreach (var count in new[] { 3, 0 })
            {
                var rows = new[] { ("Caladog", "24", "false"), ("Rhagaea", "18", "false"), ("Ironwood", "12", "true") }
                    .Take(count).Select((r, i) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    { ["__key"] = r.Item1, ["Name"] = r.Item1, ["RankText"] = (i + 1).ToString(), ["Victories"] = r.Item2,
                        ["IsMainHero"] = r.Item3, ["IsChampion"] = i == 0 ? "true" : "false", ["Id"] = "", ["TextureProviderName"] = "" }).ToArray();
                var states = nodes.Where(n => n.Name is "ImageIdentifierWidget" or "MaskedTextureWidget")
                    .ToDictionary(WidgetPath, _ => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["HideWhenNull"] = "true" });
                var diagnostics = new PreviewDiagnostics();
                var options = new RegressionRenderOptions("modules/native/gui/prefabs/gamemenu/gamemenutournamentleaderboard.xml",
                    new Dictionary<string, string> { ["TitleText"] = "Leaderboard", ["DoneText"] = "Done", ["RankText"] = "#", ["HeroText"] = "Hero", ["VictoriesText"] = "Victories" }, states, [])
                {
                    PreviewSize = new Size(1920, 1080),
                    Lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { [WidgetPath(list)] = rows },
                    OptionalConnectionExplanations = new Dictionary<string, string> { [WidgetPath(scroll) + ":HorizontalScrollbar"] = "The leaderboard scrolls vertically only." }
                };
                var rendered = renderer.Render(document, diagnostics, options);
                var pixels = WpfPreviewCapture.Capture(rendered, new("leaderboard", 1920, 1080));
                var output = Environment.GetEnvironmentVariable("BANNERLORD_M3_ARTIFACTS");
                if (output is not null) { Directory.CreateDirectory(output); WpfPreviewCapture.WritePng(Path.Combine(output, $"leaderboard-{count}.png"), pixels); }
                var elements = Elements(rendered).ToArray();
                if (output is not null) File.WriteAllText(Path.Combine(output, $"leaderboard-{count}.txt"), string.Join("\n", elements.Where(e => PreviewElementDebugProperties.GetDebugInfo(e) is not null).Select(e => $"{PreviewElementDebugProperties.GetDebugInfo(e)!.NodeName} {e.Visibility} {e.RenderSize} {e.TranslatePoint(new Point(), rendered)}")) + diagnostics.ToDisplayText());
                var names = elements.OfType<BitmapFontTextBlock>().Where(t => rows.Any(r => r["Name"] == t.Text) && ((FrameworkElement)t.Parent).Visibility == Visibility.Visible).ToArray();
                Assert.Equal(count, names.Length);
                Assert.All(names, n => Assert.True(((FrameworkElement)n.Parent).ActualWidth > 150));
                var counts = elements.OfType<BitmapFontTextBlock>().Where(t => new[] { "24", "18", "12" }.Contains(t.Text) && ((FrameworkElement)t.Parent).Visibility == Visibility.Visible).ToArray();
                Assert.Equal(count, counts.Length);
                var ranks = elements.OfType<BitmapFontTextBlock>().Where(t => new[] { "1", "2", "3" }.Contains(t.Text)).ToArray();
                Assert.Equal(count, ranks.Length);
                Assert.All(ranks, t =>
                {
                    Assert.Equal(HorizontalAlignment.Left, t.HorizontalAlignment);
                    Assert.Equal(655, t.TranslatePoint(new Point(), rendered).X);
                });
                if (count > 0)
                {
                    Assert.All(names, n => Assert.InRange(n.TranslatePoint(new Point(), rendered).X, 800, 900));
                    Assert.All(counts, n => Assert.InRange(n.TranslatePoint(new Point(), rendered).X, 1050, 1250));
                }
                var scrollbar = Assert.Single(elements, e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "Scrollbar");
                var painted = VisualTreeHelper.GetDescendantBounds(scrollbar);
                var outer = Assert.Single(elements, e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "EntriesListScrollbar");
                var paintOrigin = scrollbar.TranslatePoint(painted.TopLeft, outer);
                Assert.True(paintOrigin.Y >= 0 && paintOrigin.Y + painted.Height <= outer.RenderSize.Height,
                    $"Scrollbar paint {painted}; outer size {outer.RenderSize}");
                var bands = elements.Where(e => PreviewElementDebugProperties.GetDebugInfo(e)?.NodeName == "SiblingIndexVisibilityWidget" && e.Visibility == Visibility.Visible).ToArray();
                Assert.Equal(count, bands.Length);
                Assert.All(bands, b =>
                {
                    Assert.True(b.ActualWidth > 600);
                    Assert.Equal(60, b.ActualHeight);
                    // AlphaFactor fades the band sprite, independently of the widget's children.
                    Assert.Equal(1, b.Opacity);
                    Assert.Equal(0.1, PreviewElementDebugProperties.GetOwnVisualAlpha(b));
                    var sprite = Assert.Single(Assert.IsType<Grid>(Assert.IsType<Border>(b).Child).Children
                        .OfType<FrameworkElement>(), e => e is not Canvas);
                    Assert.Equal(0.1, sprite.Opacity);
                });
                var plaque = Assert.Single(elements, e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Sprite") == @"StdAssets\tabbar_popup");
                var plaqueImage = Assert.IsType<Grid>(Assert.IsType<Border>(plaque).Child).Children.OfType<FrameworkElement>().First();
                Assert.Equal(-1, Assert.IsType<ScaleTransform>(plaqueImage.RenderTransform).ScaleY);
                Assert.All(counts, t => Assert.Equal("Galahad", t.BitmapFont!.Name));
                Assert.All(names, t => Assert.Equal("FiraSansExtraCondensed-Regular", t.BitmapFont!.Name));
                if (count > 0)
                {
                    var unresolved = new PreviewDiagnostics();
                    var unresolvedPreview = renderer.Render(document, unresolved, options with { WidgetStates = new Dictionary<string, IReadOnlyDictionary<string, string>>() });
                    Assert.Equal(RenderCompletionStatus.Partial, unresolved.CompletionStatus);
                    var missingImages = Elements(unresolvedPreview).OfType<Border>()
                        .Where(e => PreviewElementDebugProperties.GetImageResolution(e)?.Status == Bannerlord.XmlPreview.Models.Assets.ImageResolutionStatus.Missing).ToArray();
                    Assert.Equal(count * 2, missingImages.Length);
                    Assert.All(missingImages, e => Assert.Equal(Colors.Crimson, Assert.IsType<SolidColorBrush>(e.BorderBrush).Color));
                    if (output is not null) WpfPreviewCapture.WritePng(Path.Combine(output, $"leaderboard-{count}-missing-images.png"),
                        WpfPreviewCapture.Capture(unresolvedPreview, new("leaderboard", 1920, 1080)));
                }
                Assert.DoesNotContain(elements, e => PreviewElementDebugProperties.GetImageResolution(e)?.Status == Bannerlord.XmlPreview.Models.Assets.ImageResolutionStatus.Missing);

            }
            return 0;
        });
    }

    private static IEnumerable<PrefabNode> Nodes(PrefabNode node) { yield return node; foreach (var child in node.Children) foreach (var n in Nodes(child)) yield return n; }
    private static IEnumerable<FrameworkElement> Elements(DependencyObject node) { if (node is FrameworkElement e) yield return e; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Elements(VisualTreeHelper.GetChild(node, i))) yield return child; }
}
