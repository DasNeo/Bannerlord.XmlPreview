namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IPreviewDataSourceProvider
{
    IReadOnlyList<IReadOnlyDictionary<string, string>> GetTemplateItemBindings(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes);
}
