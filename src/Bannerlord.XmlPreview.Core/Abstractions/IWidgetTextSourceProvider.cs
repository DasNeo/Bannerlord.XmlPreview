namespace Bannerlord.XmlPreview.Core.Abstractions;

public interface IWidgetTextSourceProvider
{
    string? GetTextSource(string nodeName, IReadOnlyDictionary<string, string> attributes);
}
