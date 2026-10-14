using System.IO;
using System.Windows;
using Bannerlord.XmlPreview.Assets;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class RegionDecodeTests
{
    [Theory]
    [InlineData("BC7", 16)]
    [InlineData("DXT1", 8)]
    [InlineData("DXT5", 16)]
    [InlineData("BC4", 8)]
    [InlineData("R8_UNORM", 1)]
    public void RegionsEqualFullDecodeIncludingUnalignedOriginsEdgesAndAlpha(string format, int blockBytes)
    {
        const int width = 13, height = 11;
        var data = new byte[format == "R8_UNORM" ? width * height : 4 * 3 * blockBytes];
        new Random(1729).NextBytes(data);
        // Force valid BC7 mode 6 while retaining randomized endpoints, alpha and indices.
        if (format == "BC7") for (int i = 0; i < data.Length; i += 16) data[i] = (byte)((data[i] & 128) | 64);
        var full = TpacTextureReader.Decode(data, width, height, format);
        foreach (var rect in new[] { new Int32Rect(1, 2, 7, 5), new Int32Rect(9, 7, 4, 4),
            new Int32Rect(12, 10, 1, 1), new Int32Rect(0, 0, width, height), new Int32Rect(4, 4, 4, 4) })
        {
            var region = TpacTextureReader.DecodeRegion(data, width, height, format, rect.X, rect.Y, rect.Width, rect.Height);
            Assert.True(region.IsFrozen);
            Assert.Equal(full.Format, region.Format);
            var expected = new byte[rect.Width * rect.Height * 4];
            var actual = new byte[expected.Length];
            full.CopyPixels(rect, expected, rect.Width * 4, 0);
            region.CopyPixels(actual, rect.Width * 4, 0);
            Assert.Equal(expected, actual);
        }
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.DecodeRegion(data[..1], width, height, format, 1, 1, 1, 1));
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.DecodeRegion(data, width, height, format, width, 0, 1, 1));
    }

    [Fact]
    public async Task RetainedPayloadAndRegionAreSharedAndEvictedWithinSeparateBounds()
    {
        using var fixture = new RegressionFixture();
        var first = Assert.Single(TpacTextureReader.Read(ImageAssetTests.WritePackage(Path.Combine(fixture.Root, "first.tpac"), "first")));
        var second = Assert.Single(TpacTextureReader.Read(ImageAssetTests.WritePackage(Path.Combine(fixture.Root, "second.tpac"), "second")));
        var store = new TpacAtlasStore(64, 16, 8);
        var images = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Crop(first, 1, 1, 2, 2))));
        Assert.All(images, image => Assert.Same(images[0], image));
        Assert.Equal(1, store.PayloadLoadCount);
        Assert.Equal(1, store.DecodeCount);
        _ = store.Crop(first, 0, 0, 1, 1);
        Assert.Equal(1, store.PayloadLoadCount);
        _ = store.Crop(second, 0, 0, 1, 1);
        Assert.Equal(8, store.CachedPayloadBytes);
        _ = store.Atlas(first);
        Assert.Equal(3, store.PayloadLoadCount);
        Assert.InRange(store.CachedCropBytes, 0, 16);
        store.Invalidate();
        Assert.Equal(0, store.CachedPayloadBytes);
        Assert.Equal(0, store.CachedCropBytes);
        Assert.Equal(0, store.CachedBytes);
        var uncached = new TpacAtlasStore(0, 0, 0);
        _ = uncached.Crop(first, 0, 0, 1, 1);
        Assert.Equal(0, uncached.CachedPayloadBytes);
        Assert.Equal(0, uncached.CachedCropBytes);
    }
}
