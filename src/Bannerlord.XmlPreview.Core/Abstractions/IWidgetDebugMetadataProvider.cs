namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IWidgetDebugMetadataProvider
{
    IReadOnlyList<string> GetAdditionalToolTipAttributeKeys(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes);
}
