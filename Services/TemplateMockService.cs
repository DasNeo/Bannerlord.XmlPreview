using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;

namespace Bannerlord.XmlPreview.Services;

public sealed class TemplateMockService : ITemplateMockService
{
    private readonly Dictionary<string, Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>>
        _itemsBySourcePath = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPreviewDataSourceProvider _previewDataSourceProvider;

    public TemplateMockService(IPreviewDataSourceProvider previewDataSourceProvider)
    {
        _previewDataSourceProvider = previewDataSourceProvider;
    }

    public IReadOnlyList<TemplateMockDefinition> Discover(PrefabDocument document)
    {
        return FindTargets(document.RootNode, "0").ToArray();
    }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> PrepareItems(
        string sourcePath,
        TemplateMockDefinition definition)
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> items;
        if (TryGetStoredItems(sourcePath, definition.TargetKey, out var storedItems))
        {
            items = storedItems;
        }
        else if (_previewDataSourceProvider.TryGetTemplateItemBindings(definition.Node, out var providedItems))
        {
            items = providedItems;
        }
        else
        {
            items =
            [
                definition.FieldKeys.ToDictionary(
                    static key => key,
                    static key => PrefabRenderContext.ResolveMockBindingValue(key),
                    StringComparer.OrdinalIgnoreCase)
            ];
        }

        var snapshot = CreateSnapshot(items);
        StoreItems(sourcePath, definition.TargetKey, snapshot);
        _previewDataSourceProvider.SetTemplateItemBindings(definition.Node, snapshot);
        return snapshot;
    }

    public void ApplyItems(
        string sourcePath,
        TemplateMockDefinition definition,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items)
    {
        var snapshot = CreateSnapshot(items);
        StoreItems(sourcePath, definition.TargetKey, snapshot);
        _previewDataSourceProvider.SetTemplateItemBindings(definition.Node, snapshot);
    }

    private bool TryGetStoredItems(
        string sourcePath,
        string targetKey,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> items)
    {
        if (_itemsBySourcePath.TryGetValue(sourcePath, out var itemsByTarget) &&
            itemsByTarget.TryGetValue(targetKey, out items!))
            return true;

        items = Array.Empty<IReadOnlyDictionary<string, string>>();
        return false;
    }

    private void StoreItems(
        string sourcePath,
        string targetKey,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items)
    {
        if (!_itemsBySourcePath.TryGetValue(sourcePath, out var itemsByTarget))
        {
            itemsByTarget = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(
                StringComparer.OrdinalIgnoreCase);
            _itemsBySourcePath[sourcePath] = itemsByTarget;
        }

        itemsByTarget[targetKey] = items;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> CreateSnapshot(
        IEnumerable<IReadOnlyDictionary<string, string>> items)
    {
        return items
            .Select(static item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                item,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IEnumerable<TemplateMockDefinition> FindTargets(PrefabNode node, string path)
    {
        var template = node.Children.OfType<ItemTemplateNode>().FirstOrDefault();
        if (template is not null && IsSupportedTemplateNode(node))
        {
            node.Attributes.TryGetValue("Id", out var id);
            node.Attributes.TryGetValue("DataSource", out var dataSource);
            var displayName = !string.IsNullOrWhiteSpace(id)
                ? id
                : !string.IsNullOrWhiteSpace(dataSource)
                    ? dataSource
                    : node.Name;
            var descriptionParts = new[] { node.Name, dataSource }
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            yield return new TemplateMockDefinition(
                path,
                displayName,
                string.Join(" · ", descriptionParts),
                node,
                FindBindingKeys(template));
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            foreach (var target in FindTargets(node.Children[index], $"{path}.{index}"))
                yield return target;
        }
    }

    private static bool IsSupportedTemplateNode(PrefabNode node)
    {
        return node is ListPanelNode or NavigatableListPanelNode or GridWidgetNode;
    }

    private static IReadOnlyList<string> FindBindingKeys(PrefabNode template)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectBindingKeys(template, keys);
        return keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void CollectBindingKeys(PrefabNode node, ISet<string> keys)
    {
        foreach (var value in node.Attributes.Values)
        {
            if (value.Length > 1 && value[0] == '@') keys.Add(value[1..]);
        }

        foreach (var child in node.Children) CollectBindingKeys(child, keys);
    }
}
