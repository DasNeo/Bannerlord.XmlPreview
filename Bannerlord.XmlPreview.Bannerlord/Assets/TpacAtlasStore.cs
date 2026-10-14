using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.Assets;

/// <summary>Lazy package metadata and bounded LRUs of frozen atlases and independent sprite crops.
/// One decode at a time bounds transient memory; sprite requests decode intersecting blocks only.</summary>
public sealed class TpacAtlasStore(long capacityBytes = 128L * 1024 * 1024, long cropCapacityBytes = 64L * 1024 * 1024,
    long payloadCapacityBytes = 64L * 1024 * 1024)
{
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<TpacTexture>>> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> lru = new();
    private readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> crops = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> cropLru = new();
    private readonly Dictionary<string, (byte[] Data, LinkedListNode<string> Node)> payloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> payloadLru = new();
    private long payloadUsed;
    private readonly object sync = new();
    private long used;
    private long cropUsed;
    public long CachedBytes { get { lock (sync) return used; } }
    public long CachedCropBytes { get { lock (sync) return cropUsed; } }
    public int DecodeCount { get; private set; }
    public int PayloadLoadCount { get; private set; }
    public long DecodedPixelCount { get; private set; }
    public long FullAtlasPixelCount { get; private set; }
    public long CachedPayloadBytes { get { lock (sync) return payloadUsed; } }
    public AtlasDecodeStatistics Statistics
    {
        get { lock (sync) return new(DecodeCount, PayloadLoadCount, DecodedPixelCount, FullAtlasPixelCount, used, cropUsed, payloadUsed); }
    }
    public void Invalidate()
    {
        lock (sync)
        {
            packages.Clear(); cache.Clear(); crops.Clear(); lru.Clear(); cropLru.Clear();
            payloads.Clear(); payloadLru.Clear();
            used = 0; cropUsed = 0; payloadUsed = 0;
        }
    }
    public IReadOnlyList<TpacTexture> Textures(string package) => packages.GetOrAdd(package,
        path => new(() => TpacTextureReader.Read(path), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public BitmapSource Atlas(TpacTexture texture)
    {
        var key = texture.Package + "|" + texture.Name;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var hit))
            {
                lru.Remove(hit.Node);
                lru.AddFirst(hit.Node);
                return hit.Image;
            }
            var image = TpacTextureReader.Decode(Payload(texture), texture.Width, texture.Height, texture.Format);
            DecodeCount++;
            DecodedPixelCount += (long)texture.Width * texture.Height;
            FullAtlasPixelCount += (long)texture.Width * texture.Height;
            var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
            if (bytes <= capacityBytes)
            {
                while (used + bytes > capacityBytes && lru.Last is { } last)
                {
                    used -= cache[last.Value].Bytes;
                    cache.Remove(last.Value);
                    lru.RemoveLast();
                }
                cache[key] = (image, lru.AddFirst(key), bytes);
                used += bytes;
            }
            return image;
        }
    }

    public BitmapSource Crop(TpacTexture texture, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || (long)x + width > texture.Width || (long)y + height > texture.Height)
            throw new InvalidDataException("Sprite crop lies outside its atlas.");
        var key = $"{texture.Package}|{texture.Name}|{x}|{y}|{width}|{height}";
        lock (sync)
        {
            if (crops.TryGetValue(key, out var hit))
            {
                cropLru.Remove(hit.Node);
                cropLru.AddFirst(hit.Node);
                return hit.Image;
            }
            var image = TpacTextureReader.DecodeRegion(Payload(texture), texture.Width, texture.Height, texture.Format, x, y, width, height);
            DecodeCount++;
            var blockSize = texture.Format.Equals("R8_UNORM", StringComparison.OrdinalIgnoreCase) ? 1 : 4;
            DecodedPixelCount += (long)((x + width + blockSize - 1) / blockSize - x / blockSize) * blockSize *
                ((y + height + blockSize - 1) / blockSize - y / blockSize) * blockSize;
            FullAtlasPixelCount += (long)texture.Width * texture.Height;
            var bytes = (long)width * height * 4;
            if (bytes <= cropCapacityBytes)
            {
                while (cropUsed + bytes > cropCapacityBytes && cropLru.Last is { } last)
                {
                    cropUsed -= crops[last.Value].Bytes;
                    crops.Remove(last.Value);
                    cropLru.RemoveLast();
                }
                crops[key] = (image, cropLru.AddFirst(key), bytes);
                cropUsed += bytes;
            }
            return image;
        }
    }

    // Caller holds sync, coordinating payload reads and region decodes with cache invalidation.
    private byte[] Payload(TpacTexture texture)
    {
        var key = texture.Package + "|" + texture.Name;
        if (payloads.TryGetValue(key, out var hit))
        {
            payloadLru.Remove(hit.Node); payloadLru.AddFirst(hit.Node);
            return hit.Data;
        }
        var data = TpacTextureReader.Load(texture);
        PayloadLoadCount++;
        if (data.LongLength <= payloadCapacityBytes)
        {
            while (payloadUsed + data.LongLength > payloadCapacityBytes && payloadLru.Last is { } last)
            {
                payloadUsed -= payloads[last.Value].Data.LongLength;
                payloads.Remove(last.Value); payloadLru.RemoveLast();
            }
            payloads[key] = (data, payloadLru.AddFirst(key));
            payloadUsed += data.LongLength;
        }
        return data;
    }
}

public sealed record AtlasDecodeStatistics(int DecodeCount, int PayloadLoadCount, long DecodedPixelCount,
    long FullAtlasPixelCount, long CachedAtlasBytes, long CachedCropBytes, long CachedPayloadBytes);
