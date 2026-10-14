// TPAC field layout adapted from szszss/TpacTool (MIT); see THIRD-PARTY-NOTICES.md.
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using K4os.Compression.LZ4;

namespace Bannerlord.XmlPreview.Assets;

public sealed record TpacPayload(long Offset, int UncompressedSize, int StoredSize, byte Compression);
public sealed record TpacTexture(string Package, string Name, int Width, int Height, string Format, TpacPayload? Payload)
{
    public string? PayloadPath { get; init; }
}

public static class TpacTextureReader
{
    private static readonly Guid TextureType = new("c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8");
    private static readonly Guid PixelType = new("70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c");
    private const int MaxPayload = 512 * 1024 * 1024;
    public static IReadOnlyList<TpacTexture> Read(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x43415054) throw new InvalidDataException("Invalid TPAC signature.");
        var version = reader.ReadUInt32();
        if (version is not (1 or 2)) throw new NotSupportedException($"Unsupported TPAC version {version}.");
        var packageGuid = new Guid(Bytes(reader, 16));
        var count = Count(reader);
        Skip(reader, 8);
        var textures = new List<TpacTexture>();
        for (int i = 0; i < count; i++)
        {
            var type = new Guid(Bytes(reader, 16));
            var assetGuid = new Guid(Bytes(reader, 16));
            if (version == 2) Skip(reader, 4);
            var name = Text(reader);
            var metadataSize = Size(reader.ReadUInt64(), MaxPayload);
            var end = checked(file.Position + metadataSize);
            if (end > file.Length) throw new InvalidDataException("Truncated TPAC metadata.");
            int width = 0, height = 0;
            string format = "";
            ulong sourceChecksum = 0;
            if (type == TextureType)
            {
                Skip(reader, 24); // metadata version, material Guid, flags
                _ = Text(reader); // source
                sourceChecksum = reader.ReadUInt64();
                Skip(reader, 5); // bool, reserved
                Strings(reader);
                Skip(reader, 5); // metadata extension and byte
                width = Size(reader.ReadUInt32(), 16384);
                height = Size(reader.ReadUInt32(), 16384);
                Skip(reader, 7); // depth, mip count, array count
                format = Text(reader);
                if (file.Position > end) throw new InvalidDataException("Texture metadata exceeds declared bounds.");
            }
            file.Position = end;
            Skip(reader, 8);
            var segments = Count(reader);
            TpacPayload? payload = null;
            for (int j = 0; j < segments; j++)
            {
                var offset = reader.ReadUInt64();
                var actual = reader.ReadUInt64();
                var stored = reader.ReadUInt64();
                Skip(reader, 16);
                var segmentType = new Guid(Bytes(reader, 16));
                Skip(reader, 12);
                var compression = reader.ReadByte();
                if (type == TextureType && segmentType == PixelType)
                {
                    if (offset > (ulong)file.Length || stored > (ulong)file.Length - offset)
                        throw new InvalidDataException("TPAC payload lies outside the package.");
                    if (payload is not null) throw new InvalidDataException("Duplicate texture pixel payload.");
                    payload = new((long)offset, Size(actual, MaxPayload), Size(stored, MaxPayload), compression);
                }
            }
            Skip(reader, checked((long)Count(reader) * 48));
            if (type == TextureType)
            {
                var texture = new TpacTexture(path, name, width, height, format, payload);
                if (payload is null) texture = WithCompanion(texture, packageGuid, assetGuid, sourceChecksum);
                textures.Add(texture);
            }
        }
        return textures;
    }

    public static byte[] Load(TpacTexture texture)
    {
        var payload = texture.Payload ?? throw new InvalidDataException("Texture has no pixel payload.");
        using var file = File.OpenRead(texture.PayloadPath ?? texture.Package);
        file.Position = payload.Offset;
        using var reader = new BinaryReader(file);
        var stored = Bytes(reader, payload.StoredSize);
        if (payload.Compression == 0)
        {
            if (stored.Length != payload.UncompressedSize) throw new InvalidDataException("Uncompressed payload size mismatch.");
            return stored;
        }
        if (payload.Compression != 1) throw new NotSupportedException($"Unsupported TPAC compression {payload.Compression}.");
        var decoded = new byte[payload.UncompressedSize];
        if (LZ4Codec.Decode(stored, decoded) != decoded.Length) throw new InvalidDataException("Corrupt LZ4 payload.");
        return decoded;
    }

    private static TpacTexture WithCompanion(TpacTexture texture, Guid package, Guid asset, ulong sourceChecksum)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(texture.Package)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SubModule.xml"))) directory = directory.Parent;
        if (directory is null) return texture;
        var path = Path.Combine(directory.FullName, "RuntimeDataCache", package.ToString("D").ToUpperInvariant() + ".rdc");
        if (!File.Exists(path)) return texture;
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (reader.ReadUInt32() != 0x30434452 || reader.ReadUInt32() != 0)
            throw new InvalidDataException("Invalid or unsupported RDC header.");
        // Version 0's single-texture record is verified against AgricultureEstate and controlled fixtures.
        if (reader.ReadUInt32() != 1 || reader.ReadUInt64() != 145)
            throw new NotSupportedException("Unsupported RDC record table (requires one 145-byte texture record).");
        if (new Guid(Bytes(reader, 16)) != asset || new Guid(Bytes(reader, 16)) != asset || new Guid(Bytes(reader, 16)) != PixelType)
            throw new InvalidDataException("RDC texture identity does not match TPAC metadata.");
        var offset = reader.ReadUInt64(); var actual = reader.ReadUInt64(); var stored = reader.ReadUInt64();
        var compression = reader.ReadByte(); Skip(reader, 3);
        if (reader.ReadUInt64() != sourceChecksum) throw new InvalidDataException("Stale RDC source checksum.");
        if (offset < 165 || offset > (ulong)file.Length || stored > (ulong)file.Length - offset)
            throw new InvalidDataException("RDC payload lies outside its record bounds.");
        return texture with { Payload = new((long)offset, Size(actual, MaxPayload), Size(stored, MaxPayload), compression), PayloadPath = path };
    }

    /// <summary>Decode only the blocks intersecting a sprite, then trim their aligned edges.</summary>
    public static BitmapSource DecodeRegion(byte[] data, int width, int height, string format, int x, int y, int regionWidth, int regionHeight)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 > 128L * 1024 * 1024 ||
            x < 0 || y < 0 || regionWidth <= 0 || regionHeight <= 0 || (long)x + regionWidth > width || (long)y + regionHeight > height)
            throw new InvalidDataException("Invalid texture dimensions or sprite region.");
        var blockBytes = format.ToUpperInvariant() switch
        {
            "BC7" or "DXT5" => 16, "DXT1" or "BC4" => 8, "R8_UNORM" => 1,
            _ => throw new NotSupportedException($"Unsupported texture format '{format}'.")
        };
        var blockSize = blockBytes == 1 ? 1 : 4;
        var columns = (width + blockSize - 1) / blockSize;
        var rows = (height + blockSize - 1) / blockSize;
        if (data.Length < checked(columns * rows * blockBytes)) throw new InvalidDataException("Truncated texture pixels.");
        var left = x / blockSize;
        var top = y / blockSize;
        var regionColumns = (x + regionWidth + blockSize - 1) / blockSize - left;
        var regionRows = (y + regionHeight + blockSize - 1) / blockSize - top;
        var packed = new byte[checked(regionColumns * regionRows * blockBytes)];
        for (int row = 0; row < regionRows; row++)
            Buffer.BlockCopy(data, ((top + row) * columns + left) * blockBytes,
                packed, row * regionColumns * blockBytes, regionColumns * blockBytes);
        var aligned = Decode(packed, Math.Min(regionColumns * blockSize, width - left * blockSize),
            Math.Min(regionRows * blockSize, height - top * blockSize), format);
        var pixels = new byte[checked(regionWidth * regionHeight * 4)];
        aligned.CopyPixels(new System.Windows.Int32Rect(x % blockSize, y % blockSize, regionWidth, regionHeight), pixels, regionWidth * 4, 0);
        var image = BitmapSource.Create(regionWidth, regionHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, regionWidth * 4);
        image.Freeze();
        return image;
    }

    public static BitmapSource Decode(byte[] data, int width, int height, string format)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 > 128L * 1024 * 1024)
            throw new InvalidDataException("Invalid or oversized texture dimensions (128 MiB decoded limit).");
        var compression = format.ToUpperInvariant() switch
        {
            "BC7" => CompressionFormat.Bc7, "DXT1" => CompressionFormat.Bc1,
            "DXT5" => CompressionFormat.Bc3, "BC4" => CompressionFormat.Bc4,
            "R8_UNORM" => CompressionFormat.R, _ => throw new NotSupportedException($"Unsupported texture format '{format}'.")
        };
        var required = compression == CompressionFormat.R ? checked(width * height) :
            checked(((width + 3) / 4) * ((height + 3) / 4) * (compression is CompressionFormat.Bc1 or CompressionFormat.Bc4 ? 8 : 16));
        if (data.Length < required) throw new InvalidDataException("Truncated texture pixels.");
        // Only mip zero is decoded; remaining mip/array payloads do not become bitmaps.
        var pixels = new BcDecoder().DecodeRaw(data.AsSpan(0, required).ToArray(), width, height, compression);
        var bgra = new byte[checked(width * height * 4)];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            bgra[i * 4] = p.b;
            bgra[i * 4 + 1] = p.g;
            bgra[i * 4 + 2] = p.r;
            bgra[i * 4 + 3] = p.a;
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var premultiplied = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        premultiplied.Freeze();
        return premultiplied;
    }

    private static int Size(ulong value, int limit) => value <= (ulong)limit ? (int)value : throw new InvalidDataException("TPAC field exceeds size limit.");
    private static int Count(BinaryReader reader) => Size(reader.ReadUInt32(), 100000);
    private static string Text(BinaryReader reader) => Encoding.UTF8.GetString(Bytes(reader, Size(reader.ReadUInt32(), 1024 * 1024)));
    private static void Strings(BinaryReader reader) { var count = Count(reader); for (int i = 0; i < count; i++) _ = Text(reader); }
    private static byte[] Bytes(BinaryReader reader, int count)
    {
        var result = reader.ReadBytes(count);
        return result.Length == count ? result : throw new InvalidDataException("Truncated TPAC data.");
    }
    private static void Skip(BinaryReader reader, long count)
    {
        var end = checked(reader.BaseStream.Position + count);
        if (end > reader.BaseStream.Length) throw new InvalidDataException("Truncated TPAC data.");
        reader.BaseStream.Position = end;
    }
}
