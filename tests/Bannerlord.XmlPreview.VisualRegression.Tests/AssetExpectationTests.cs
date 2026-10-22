using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models.Assets;
using System.Windows.Media;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class AssetExpectationTests
{
    [Fact]
    public void BrushMetricsUseNamedSpriteDimensionsAndExcludeExtensions()
    {
        var metrics = new BannerlordBrushMetricsProvider(new MetricAssets());
        Assert.Equal(90d, metrics.ResolveMetric("test.brush", "frame", "Width"));
        Assert.Equal(44d, metrics.ResolveMetric("test.brush", "frame", "Height"));
        Assert.Equal(20d, metrics.ResolveMetric("test.brush", "icon", "Width"));
        Assert.Equal(10d, metrics.ResolveMetric("test.brush", "icon", "Height"));
    }

    [Theory]
    [InlineData("test.missing-brush", false)]
    [InlineData("test.brush", true)]
    public void MissingBrushOrSpriteCannotSupplyVerifiedMetrics(string brush, bool missingSprite)
    {
        var metrics = new BannerlordBrushMetricsProvider(new MetricAssets(missingSprite));
        Assert.Null(metrics.ResolveMetric(brush, "frame", "Width"));
        Assert.Null(metrics.ResolveMetric(brush, "frame", "Height"));
    }

    private sealed class MetricAssets(bool missingSprite = false) : IAssetCatalog
    {
        private static readonly BrushDefinition Brush = new("test.brush", "fixture.xml",
            [Layer("frame", "test.frame", 3, 7, 2, 4), Layer("icon", "test.icon", 0, 0, 0, 0)],
            new Dictionary<string, BrushStyleDefinition>(), null, null, null, null);

        private static BrushLayerDefinition Layer(string name, string sprite, double left, double right, double top, double bottom) =>
            new(name, sprite, null, null, null, null, null, null, null, null, null, left, right, top, bottom, null, null);

        public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
        public BrushDefinition? FindBrush(string? name) => name == Brush.Name ? Brush : null;
        public SpriteDefinition? FindSprite(string? name) => missingSprite ? null : name switch
        {
            "test.frame" => new(name, "fixture.xml", "Sprite", "frame", "test", 1, 100, 50),
            "test.icon" => new(name, "fixture.xml", "Sprite", "icon", "test", 1, 20, 10),
            _ => null
        };
        public ImageSource? GetSpriteImage(string? name) => null;
        public SpriteAssetResolution ResolveSpriteAsset(string? name)
        {
            var sprite = FindSprite(name);
            return new(name ?? "", sprite is null ? SpriteAssetResolutionKind.Missing : SpriteAssetResolutionKind.ExactName,
                sprite?.Name, sprite?.SourcePath, sprite, null);
        }
    }
}
