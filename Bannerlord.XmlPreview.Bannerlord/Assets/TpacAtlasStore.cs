using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.Assets;

/// <summary>Lazy package metadata and bounded LRUs of frozen atlases and independent sprite crops.
/// One decode at a time bounds transient memory; retained crops survive atlas eviction.</summary>
public sealed class TpacAtlasStore(long capacityBytes = 128L * 1024 * 1024, long cropCapacityBytes = 64L * 1024 * 1024)
{
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<TpacTexture>>> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> lru = new();
    private readonly Dictionary<string, (BitmapSource Image, LinkedListNode<string> Node, long Bytes)> crops = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> cropLru = new();
    private readonly object sync = new();
    private long used;
    private long cropUsed;
    public long CachedBytes { get { lock (sync) return used; } }
    public long CachedCropBytes { get { lock (sync) return cropUsed; } }
    public int DecodeCount { get; private set; }
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
            var image = TpacTextureReader.Decode(TpacTextureReader.Load(texture), texture.Width, texture.Height, texture.Format);
            DecodeCount++;
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
            var crop = new CroppedBitmap(Atlas(texture), new Int32Rect(x, y, width, height));
            // Copy the crop so holding a sprite does not keep an evicted atlas alive.
            var pixels = new byte[checked(width * height * 4)];
            crop.CopyPixels(pixels, width * 4, 0);
            var image = BitmapSource.Create(width, height, 96, 96, crop.Format, null, pixels, width * 4);
            image.Freeze();
            if (pixels.LongLength <= cropCapacityBytes)
            {
                while (cropUsed + pixels.LongLength > cropCapacityBytes && cropLru.Last is { } last)
                {
                    cropUsed -= crops[last.Value].Bytes;
                    crops.Remove(last.Value);
                    cropLru.RemoveLast();
                }
                crops[key] = (image, cropLru.AddFirst(key), pixels.LongLength);
                cropUsed += pixels.LongLength;
            }
            return image;
        }
    }
}
