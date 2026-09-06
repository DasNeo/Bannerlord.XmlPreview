using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.DemoData;
using Bannerlord.XmlPreview.Models.Nodes;
using System.Runtime.CompilerServices;

namespace Bannerlord.XmlPreview.Preview.Bannerlord;

public sealed class BannerlordPreviewDataSourceProvider : IPreviewDataSourceProvider
{
    private readonly ConditionalWeakTable<PrefabNode, ConfiguredTemplateItems> _configuredItems = new();

    public bool TryGetTemplateItemBindings(
        PrefabNode node,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
    {
        if (_configuredItems.TryGetValue(node, out var configuredItems))
        {
            itemBindings = configuredItems.Items;
            return true;
        }

        if (string.Equals(node.Name, "NavigatableListPanel", StringComparison.OrdinalIgnoreCase) &&
            node.Attributes.TryGetValue("DataSource", out var dataSource) &&
            string.Equals(dataSource, "{MenuItems}", StringComparison.OrdinalIgnoreCase))
        {
            itemBindings = PreviewDemoCatalog.EscapeMenuItems
                .Select(static item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["ActionText"] = item.ActionText,
                    ["IsDisabled"] = item.IsDisabled ? "true" : "false",
                    ["IsPositiveBehaviored"] = item.IsPositiveBehaviored ? "true" : "false"
                })
                .ToArray();
            return true;
        }

        itemBindings = Array.Empty<IReadOnlyDictionary<string, string>>();
        return false;
    }

    public void SetTemplateItemBindings(
        PrefabNode node,
        IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
    {
        var snapshot = new ConfiguredTemplateItems(
            itemBindings
                .Select(static item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                    item,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray());
        _configuredItems.Remove(node);
        _configuredItems.Add(node, snapshot);
    }

    private sealed record ConfiguredTemplateItems(
        IReadOnlyList<IReadOnlyDictionary<string, string>> Items);
}
