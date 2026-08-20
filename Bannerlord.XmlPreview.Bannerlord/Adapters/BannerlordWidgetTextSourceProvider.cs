using Bannerlord.XmlPreview.Core.Abstractions;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetTextSourceProvider : IWidgetTextSourceProvider
{
    public string? GetTextSource(string nodeName, IReadOnlyDictionary<string, string> attributes)
    {
        return attributes.TryGetValue("Parameter.ButtonText", out var buttonText)
            ? buttonText
            : null;
    }
}
