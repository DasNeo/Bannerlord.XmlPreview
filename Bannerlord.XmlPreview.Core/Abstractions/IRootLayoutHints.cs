using System.Windows;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IRootLayoutHints
{
    Size ResolveRootSize(Func<string?, double, double> resolveDouble);
}
