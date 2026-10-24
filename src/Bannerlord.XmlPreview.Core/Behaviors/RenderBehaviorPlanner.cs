using System.Collections.Immutable;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;

namespace Bannerlord.XmlPreview.Core.Behaviors;

internal static class RenderBehaviorPlanner
{
    internal static ImmutableArray<BehaviorInput> Inputs(PrefabDocument document, RenderStructureSnapshot structure,
        IBehaviorRuleProvider? provider, RegressionRenderOptions? options, Bannerlord.XmlPreview.Core.Abstractions.PreviewDataSnapshot snapshot)
    {
        var definitions = new Dictionary<string, PrefabNode>(StringComparer.OrdinalIgnoreCase);
        void Define(PrefabNode node)
        {
            definitions[node.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, node.Name)] = node;
            foreach (var child in node.Children) Define(child);
        }
        Define(document.RootNode);
        var expanded = new List<RenderWidgetSnapshot>();
        var siblingIndices = new Dictionary<string, int>();
        void Flatten(RenderWidgetSnapshot node, int index)
        {
            expanded.Add(node); siblingIndices[node.WidgetId] = index;
            for (var i = 0; i < node.Children.Count; i++)
                Flatten(node.Children[i], node.Kind == "ItemTemplate" ? index : i);
        }
        Flatten(structure.Root, 0);
        var children = expanded.ToImmutableDictionary(w => new WidgetId(w.WidgetId),
            w => w.Children.Select(c => new WidgetId(c.WidgetId)).ToImmutableArray());
        var runValues = expanded.ToImmutableDictionary(w => new WidgetId(w.WidgetId), w => w.Properties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            .SetItem("Preview.SiblingIndex", siblingIndices[w.WidgetId].ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return expanded.Select(widget =>
        {
            var node = definitions[widget.ExpandedPath];
            var metadata = provider?.Describe(node) ?? new BehaviorTypeMetadata([], []);
            var parentPath = widget.ExpandedPath.LastIndexOf(" > ", StringComparison.Ordinal);
            var parentType = parentPath < 0 ? null : definitions.GetValueOrDefault(widget.ExpandedPath[..parentPath])?.Name;
            var description = new WidgetDescription(new(widget.WidgetId), widget.ExpandedPath, node.Name,
                node.Attributes.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), metadata.BaseTypes, metadata.Capabilities,
                parentType, node.Children.Select(c => c.Name).ToImmutableArray());
            var references = ImmutableDictionary.CreateBuilder<string, WidgetId>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in document.ReferenceMap.GetReferences(widget.ExpandedPath).Values.Where(r => r.IsResolved))
            {
                var candidates = expanded.Where(w => w.ExpandedPath == reference.TargetWidgetPath &&
                    (widget.InstanceKey == w.InstanceKey || (w.InstanceKey.Length == 0 || widget.InstanceKey.StartsWith(w.InstanceKey + "/", StringComparison.Ordinal)))).OrderByDescending(w => w.InstanceKey.Length).ToArray();
                if (candidates.Length > 0) references[reference.AttributeName] = new(candidates[0].WidgetId);
            }
            var values = runValues[new(widget.WidgetId)];
            foreach (var pair in options?.OptionalConnectionExplanations ?? (IReadOnlyDictionary<string, string>)snapshot.OptionalConnectionExplanations)
                if (pair.Key.StartsWith(widget.ExpandedPath + ":", StringComparison.Ordinal))
                    values = values.SetItem("Preview.Optional." + pair.Key[(widget.ExpandedPath.Length + 1)..], pair.Value);
            return new BehaviorInput(description, values, references.ToImmutable(), runValues) { Children = children };
        }).ToImmutableArray();
    }
}
