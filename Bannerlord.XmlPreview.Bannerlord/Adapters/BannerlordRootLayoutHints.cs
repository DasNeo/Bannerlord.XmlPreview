using Bannerlord.XmlPreview.Core.Abstractions;
using System.Windows;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordRootLayoutHints : IRootLayoutHints
{
    private static readonly Size DefaultPreviewSize = new(1920, 1080);
    private Size _previewSize = DefaultPreviewSize;

    public Size PreviewSize
    {
        get => _previewSize;
        set
        {
            if (value.Width <= 0 || value.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Preview size must be positive.");
            }

            _previewSize = value;
        }
    }

    public Size ResolveRootSize(Func<string?, double, double> resolveDouble)
    {
        var width = Math.Max(
            PreviewSize.Width,
            resolveDouble("!Encyclopedia.Frame.Width", PreviewSize.Width) + 80);
        var height = Math.Max(
            PreviewSize.Height,
            resolveDouble("!Encyclopedia.Frame.Height", PreviewSize.Height) + 120);

        return new Size(width, height);
    }
}
