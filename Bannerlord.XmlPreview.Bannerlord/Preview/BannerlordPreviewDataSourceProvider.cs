using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.DemoData;

namespace Bannerlord.XmlPreview.Preview.Bannerlord;

public sealed class BannerlordPreviewDataSourceProvider : IPreviewDataSourceProvider
{
    public IReadOnlyList<IReadOnlyDictionary<string, string>> GetTemplateItemBindings(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes)
    {
        if (string.Equals(nodeName, "NavigatableListPanel", StringComparison.OrdinalIgnoreCase) &&
            attributes.TryGetValue("DataSource", out var dataSource) &&
            string.Equals(dataSource, "{MenuItems}", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewDemoCatalog.EscapeMenuItems
                .Select(static item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["ActionText"] = item.ActionText,
                    ["IsDisabled"] = item.IsDisabled ? "true" : "false",
                    ["IsPositiveBehaviored"] = item.IsPositiveBehaviored ? "true" : "false"
                })
                .ToArray();
        }

        return Array.Empty<IReadOnlyDictionary<string, string>>();
    }
}
