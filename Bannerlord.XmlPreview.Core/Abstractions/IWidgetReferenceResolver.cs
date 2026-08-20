using Bannerlord.XmlPreview.Models;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IWidgetReferenceResolver
{
    void Resolve(XElement rootElement, PreviewDiagnostics diagnostics);
}
