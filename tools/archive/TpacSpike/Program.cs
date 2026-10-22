using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using TpacTool.Lib;

// Isolated candidate evaluation. No production asset lookup or fallback policy lives here.
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: TpacSpike <Native module> <comparison PNG directory> <output directory>");
    return 2;
}
Directory.CreateDirectory(args[2]);
var watch = Stopwatch.StartNew();
var packagePath = Path.Combine(args[0], "AssetPackages", "gauntlet_ui.tpac");
var package = new AssetPackage(packagePath, true, false);
var metadataMs = watch.Elapsed.TotalMilliseconds;
var textures = package.Items.OfType<Texture>().OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
var parts = XDocument.Load(Path.Combine(args[0], "GUI", "NativeSpriteData.xml"))
    .Descendants("SpritePart").Select(p => new SpritePart(
        p.Element("Name")!.Value,
        p.Element("CategoryName")!.Value + "_" + p.Element("SheetID")!.Value,
        int.Parse(p.Element("SheetX")!.Value), int.Parse(p.Element("SheetY")!.Value),
        int.Parse(p.Element("Width")!.Value), int.Parse(p.Element("Height")!.Value))).ToArray();
var results = new List<object>();
bool passed = true;
foreach (var sample in new[] { (TextureFormat.BC7, false), (TextureFormat.BC7, true), (TextureFormat.DXT5, false) })
{
    var (format, largestAtlas) = sample;
    var candidates = (from t in textures
                     where t.Format == format && t.HasPixelData
                     from p in parts
                     where p.Atlas.Equals(t.Name, StringComparison.OrdinalIgnoreCase)
                     where File.Exists(Path.Combine(args[1], p.Name + ".png"))
                     select (texture: t, part: p));
    var candidate = (largestAtlas ? candidates.OrderByDescending(c => (long)c.texture.Width * c.texture.Height) : candidates).FirstOrDefault();
    if (candidate.texture is null)
    {
        results.Add(new { Format = format.ToString(), Status = "MissingComparisonInput" });
        passed = false;
        continue;
    }
    var (texture, part) = candidate;
    var width = checked((int)texture.Width);
    var height = checked((int)texture.Height);
    if (part.X < 0 || part.Y < 0 || part.Width <= 0 || part.Height <= 0 ||
        (long)part.X + part.Width > width || (long)part.Y + part.Height > height)
        throw new InvalidDataException("SpriteData crop lies outside the atlas.");
    var allocationStart = GC.GetTotalAllocatedBytes(true);
    watch.Restart();
    var raw = texture.TexturePixels!.Data.PrimaryRawImage;
    var payloadMs = watch.Elapsed.TotalMilliseconds;
    watch.Restart();
    var atlas = Decode(raw, width, height, format);
    var firstDecodeMs = watch.Elapsed.TotalMilliseconds;
    var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocationStart;
    var warmDecodeMs = new List<double>();
    for (int i = 0; i < 5; i++)
    {
        watch.Restart();
        _ = Decode(raw, width, height, format);
        warmDecodeMs.Add(watch.Elapsed.TotalMilliseconds);
    }
    watch.Restart();
    var crop = Crop(atlas, width, part);
    var retainedAtlasCropMs = watch.Elapsed.TotalMilliseconds;
    var bitmap = BitmapSource.Create(part.Width, part.Height, 96, 96, PixelFormats.Bgra32,
        null, crop, part.Width * 4);
    bitmap.Freeze();
    using (var output = File.Create(Path.Combine(args[2], format + "-" + Path.GetFileName(part.Name) + ".png")))
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }
    using var input = File.OpenRead(Path.Combine(args[1], part.Name + ".png"));
    var reference = new FormatConvertedBitmap(BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat,
        BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
    bool dimensionsMatch = reference.PixelWidth == part.Width && reference.PixelHeight == part.Height;
    int maxDifference = -1, differingPixels = -1, maxAlphaDifference = -1;
    if (dimensionsMatch)
    {
        var expected = new byte[crop.Length];
        reference.CopyPixels(expected, part.Width * 4, 0);
        maxDifference = differingPixels = maxAlphaDifference = 0;
        for (int i = 0; i < crop.Length; i += 4)
        {
            bool different = false;
            for (int c = 0; c < 4; c++)
            {
                var delta = Math.Abs(crop[i + c] - expected[i + c]);
                maxDifference = Math.Max(maxDifference, delta);
                if (c == 3) maxAlphaDifference = Math.Max(maxAlphaDifference, delta);
                different |= delta != 0;
            }
            if (different) differingPixels++;
        }
    }
    bool matches = dimensionsMatch && maxDifference == 0;
    passed &= matches;
    results.Add(new
    {
        Status = matches ? "ExactMatch" : "ComparisonFailed", Format = format.ToString(),
        Package = packagePath, Texture = texture.Name, Width = width, Height = height, Crop = part,
        PayloadMs = payloadMs, FirstDecodeMs = firstDecodeMs, WarmDecodeMs = warmDecodeMs,
        RetainedAtlasCropMs = retainedAtlasCropMs, DecodedAtlasBytes = atlas.LongLength,
        FirstLoadAllocatedBytes = allocationBytes, ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
        DimensionsMatch = dimensionsMatch, MaxChannelDifference = maxDifference,
        MaxAlphaDifference = maxAlphaDifference, DifferingPixels = differingPixels
    });
}
var report = new
{
    SchemaVersion = 1, Machine = Environment.MachineName, Runtime = Environment.Version.ToString(),
    ReaderCommit = "6b115ff3a6cebdbe25af424eb13926078ed22b2d", MetadataMs = metadataMs,
    // Cold means first use in this process; the OS disk cache has not been flushed.
    ColdDefinition = "First payload load and first decode in process; OS cache uncontrolled",
    Inventory = textures.GroupBy(t => t.Format).Select(g => new
    {
        Format = g.Key.ToString(), Count = g.Count(), WithPixels = g.Count(t => t.HasPixelData)
    }), Results = results, Passed = passed
};
File.WriteAllText(Path.Combine(args[2], "report.json"), JsonSerializer.Serialize(report,
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"TPAC spike: {(passed ? "Passed" : "Failed")}; {Path.Combine(args[2], "report.json")}");
return passed ? 0 : 1;

static byte[] Decode(byte[] raw, int width, int height, TextureFormat format)
{
    var writer = new BgraWriter(width, height);
    TextureUtil.DecodeTextureDataToWriter(raw, width, height, format, writer);
    return writer.Pixels;
}

static byte[] Crop(byte[] atlas, int width, SpritePart part)
{
    var result = new byte[checked(part.Width * part.Height * 4)];
    for (int y = 0; y < part.Height; y++)
        Buffer.BlockCopy(atlas, checked(((part.Y + y) * width + part.X) * 4),
            result, y * part.Width * 4, part.Width * 4);
    return result;
}

record SpritePart(string Name, string Atlas, int X, int Y, int Width, int Height);

sealed class BgraWriter : TextureUtil.PipelineWriter
{
    public byte[] Pixels { get; }
    private int offset;
    public BgraWriter(int imageWidth, int imageHeight)
    {
        width = imageWidth;
        Pixels = new byte[checked(imageWidth * imageHeight * 4)];
    }
    public override void WriteLine(byte[] rgba, bool normalized)
    {
        for (int x = 0; x < width * 4; x += 4)
        {
            Pixels[offset++] = rgba[x + 2];
            Pixels[offset++] = rgba[x + 1];
            Pixels[offset++] = rgba[x];
            Pixels[offset++] = rgba[x + 3];
        }
    }
    public override void WriteLine(ushort[] rgba, bool normalized) => throw new NotSupportedException();
    public override void WriteLine(uint[] rgba, bool normalized) => throw new NotSupportedException();
    public override void WriteLine(float[] depth, byte[] stencil) => throw new NotSupportedException();
    public override void WriteLine(float[] rgba) => throw new NotSupportedException();
}
