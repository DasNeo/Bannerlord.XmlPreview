using Bannerlord.XmlPreview.Models;
namespace Bannerlord.XmlPreview.Interfaces;
public interface ISourceNavigator
{
    void Open(SourceLocation location);
    void CopyPath(SourceLocation location);
}
