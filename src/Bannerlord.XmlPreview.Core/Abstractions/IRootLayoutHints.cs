using System.Windows;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IRootLayoutHints
{
    Size PreviewSize { get; set; }

    Size ResolveRootSize(Func<string?, double, double> resolveDouble);
}
