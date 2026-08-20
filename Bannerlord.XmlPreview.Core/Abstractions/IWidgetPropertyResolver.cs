using Bannerlord.XmlPreview.Models;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IWidgetPropertyResolver
{
    PrefabWidgetPropertyMap Resolve(
        XElement rootElement,
        PrefabWidgetReferenceMap referenceMap,
        PreviewDiagnostics diagnostics);
}
