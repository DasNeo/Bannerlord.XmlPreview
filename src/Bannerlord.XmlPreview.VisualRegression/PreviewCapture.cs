using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record CapturedDiagnostic(string Code, string WidgetIdOrPath, string Message);
public sealed record CapturedPreview(RenderStructureSnapshot Structure, LayoutSnapshot Layout,
    IReadOnlyList<CapturedDiagnostic> Diagnostics, RenderCompletionStatus CompletionStatus,
    RgbImage? Pixels, VisualRegressionTiming Timing, string? ModuleVersion = null)
{
    public Bannerlord.XmlPreview.Core.Behaviors.BehaviorGraphSummary? BehaviorGraph { get; init; }
    public System.Collections.Immutable.ImmutableArray<Bannerlord.XmlPreview.Core.Behaviors.BehaviorTrace> BehaviorTrace { get; init; } = [];
}

public sealed record RgbImage(int Width, int Height, byte[] Pixels)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || (long)Width * Height * 3 != Pixels.LongLength)
            throw new InvalidDataException("Invalid RGB pixel buffer.");
    }
}

public static class WpfPreviewCapture
{
    public static RgbImage Capture(FrameworkElement root, CaptureProfile profile)
    {
        root.Measure(new Size(profile.Width, profile.Height));
        root.Arrange(new Rect(0, 0, profile.Width, profile.Height));
        root.UpdateLayout();
        var content = new RenderTargetBitmap(profile.Width, profile.Height, 96, 96, PixelFormats.Pbgra32);
        content.Render(root);
        var bitmap = new RenderTargetBitmap(profile.Width, profile.Height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(profile.Background)), null,
                new Rect(0, 0, profile.Width, profile.Height));
            drawing.DrawImage(content, new Rect(0, 0, profile.Width, profile.Height));
        }
        bitmap.Render(visual);
        var rgb = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var pixels = new byte[checked(profile.Width * profile.Height * 3)];
        rgb.CopyPixels(pixels, profile.Width * 3, 0);
        return new(profile.Width, profile.Height, pixels);
    }

    public static void WritePng(string path, RgbImage image)
    {
        image.Validate();
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Rgb24, null,
            image.Pixels, image.Width * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static RgbImage ReadPng(string path, string background)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        if (decoder.Frames[0].ColorContexts is { Count: > 0 } contexts)
            frame = new ColorConvertedBitmap(frame, contexts[0], new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var source = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
        bgra.CopyPixels(source, frame.PixelWidth * 4, 0);
        var rgb = new byte[checked(frame.PixelWidth * frame.PixelHeight * 3)];
        var color = (Color)ColorConverter.ConvertFromString(background);
        for (var i = 0; i < frame.PixelWidth * frame.PixelHeight; i++)
        {
            var alpha = source[i * 4 + 3];
            rgb[i * 3] = Composite(source[i * 4 + 2], color.R, alpha);
            rgb[i * 3 + 1] = Composite(source[i * 4 + 1], color.G, alpha);
            rgb[i * 3 + 2] = Composite(source[i * 4], color.B, alpha);
        }
        return new(frame.PixelWidth, frame.PixelHeight, rgb);
    }

    private static byte Composite(byte channel, byte background, byte alpha) =>
        (byte)((channel * alpha + background * (255 - alpha) + 127) / 255);
}
