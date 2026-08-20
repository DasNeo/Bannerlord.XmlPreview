using Bannerlord.XmlPreview.Core.Abstractions;
using System.Windows;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordRootLayoutHints : IRootLayoutHints
{
    private static readonly Size DefaultPreviewSize = new(1600, 1100);

    public Size ResolveRootSize(Func<string?, double, double> resolveDouble)
    {
        var width = Math.Max(
            DefaultPreviewSize.Width,
            resolveDouble("!Encyclopedia.Frame.Width", DefaultPreviewSize.Width) + 80);
        var height = Math.Max(
            DefaultPreviewSize.Height,
            resolveDouble("!Encyclopedia.Frame.Height", DefaultPreviewSize.Height) + 120);

        return new Size(width, height);
    }
}
