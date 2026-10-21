using System.IO;
using Bannerlord.XmlPreview.Assets;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class TpacMetadataTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(300)]
    public void ConsecutiveTexturesRemainReadableAcrossReservedMetadata(int padding)
    {
        using var fixture = new RegressionFixture();
        var path = Path.Combine(fixture.Root, "metadata.tpac");
        var first = Record("first");
        var second = Record("other");
        var bytes = first.Concat(second[36..]).ToArray();
        BitConverter.GetBytes(2u).CopyTo(bytes, 24);
        File.WriteAllBytes(path, bytes);
        var textures = TpacTextureReader.Read(path);
        Assert.Equal(new[] { "first", "other" }, textures.Select(t => t.Name));
        Assert.All(textures, texture =>
        {
            Assert.Equal(4, texture.Width);
            Assert.Equal(4, texture.Height);
            Assert.Equal("DXT1", texture.Format);
            Assert.Null(texture.Payload);
            Assert.Throws<InvalidDataException>(() => TpacTextureReader.Load(texture));
        });

        byte[] Record(string name)
        {
            var source = File.ReadAllBytes(ImageAssetTests.WritePackage(path, name, embedPixels: false));
            var sizeOffset = 76 + name.Length;
            var metadataSize = BitConverter.ToUInt64(source, sizeOffset);
            var end = sizeOffset + 8 + (int)metadataSize;
            var padded = source[..end].Concat(new byte[padding]).Concat(source[end..]).ToArray();
            BitConverter.GetBytes(metadataSize + (ulong)padding).CopyTo(padded, sizeOffset);
            return padded;
        }
    }

    [Fact]
    public void MetadataCannotRewindIntoItsOwnFields()
    {
        using var fixture = new RegressionFixture();
        var path = ImageAssetTests.WritePackage(Path.Combine(fixture.Root, "invalid.tpac"), "texture", embedPixels: false);
        var bytes = File.ReadAllBytes(path);
        // The texture's declared metadata stops before the required dimensions/format.
        BitConverter.GetBytes(1ul).CopyTo(bytes, 76 + "texture".Length);
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => TpacTextureReader.Read(path));
    }
}
