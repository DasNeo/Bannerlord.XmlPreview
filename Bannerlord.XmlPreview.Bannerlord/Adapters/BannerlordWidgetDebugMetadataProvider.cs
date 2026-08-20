using Bannerlord.XmlPreview.Core.Abstractions;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetDebugMetadataProvider : IWidgetDebugMetadataProvider
{
    private static readonly IReadOnlyList<string> ToolTipKeys =
    [
        "Command.Click",
        "Command.HoverBegin"
    ];

    public IReadOnlyList<string> GetAdditionalToolTipAttributeKeys(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes)
    {
        return ToolTipKeys;
    }
}
