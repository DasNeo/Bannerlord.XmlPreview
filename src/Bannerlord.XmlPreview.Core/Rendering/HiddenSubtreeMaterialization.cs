using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Rendering;

public sealed partial class PrefabRenderContext
{
    private bool _deferHiddenSubtrees;
    private bool _hiddenByAncestor;
    private bool _preserveHiddenLayout;
    private readonly HashSet<string> _protectedHiddenPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedGeometryPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<WidgetId, int> _logicalSubtreeCounts = [];
    private readonly HashSet<WidgetId> _reportedDeferredSubtrees = [];
    private ImmutableDictionary<WidgetId, ImmutableArray<WidgetId>> _logicalChildren =
        ImmutableDictionary<WidgetId, ImmutableArray<WidgetId>>.Empty;

    private void PlanHiddenSubtreeMaterialization(ImmutableArray<BehaviorInput> inputs, BehaviorGraphPlan plan, PrefabNode root)
    {
        _deferHiddenSubtrees = _regressionOptions?.DeferHiddenSubtrees ?? true;
        if (!_deferHiddenSubtrees || inputs.IsEmpty) return;
        _logicalChildren = inputs[0].Children;
        // Canvas content bounds can include collapsed children. Flow panels exclude them.
        void FindGeometryDependencies(PrefabNode node, PrefabNode? parent)
        {
            if (parent is not null && parent is not (ListPanelNode or GridWidgetNode) &&
                !string.Equals(parent.Attributes.GetValueOrDefault("Preview.ExcludeCollapsedChildren"), "true", StringComparison.OrdinalIgnoreCase) &&
                TryGetWidgetPath(node.Attributes) is { } path)
                _collapsedGeometryPaths.Add(path);
            var childParent = node is ItemTemplateNode && node.Children.Count == 1 ? parent : node;
            foreach (var child in node.Children) FindGeometryDependencies(child, childParent);
        }
        FindGeometryDependencies(root, null);
        var widgets = inputs.ToDictionary(i => i.Widget.Id);
        var fullSubtrees = new HashSet<string>(StringComparer.Ordinal);

        void ProtectPath(string path)
        {
            while (true)
            {
                _protectedHiddenPaths.Add(path);
                var parent = path.LastIndexOf(WidgetPathSeparator, StringComparison.Ordinal);
                if (parent < 0) break;
                path = path[..parent];
            }
        }
        void Protect(WidgetId? id, bool entireSubtree = false)
        {
            if (id is not { } value || !widgets.TryGetValue(value, out var input)) return;
            ProtectPath(input.Widget.Path);
            if (entireSubtree) fullSubtrees.Add(input.Widget.Path);
        }

        foreach (var planned in plan.Contributions)
        {
            if (planned.Contribution.Phase is not (BehaviorPhase.Connect or BehaviorPhase.PrepareLayout or BehaviorPhase.Inspect))
                continue;
            // Keep executable behavior owners and the references they can actually use.
            Protect(planned.Widget, planned.Contribution is InspectionContribution { KeyBounds: true });
            var changesVisibility = planned.Contribution is LayoutContribution
            {
                Intent: LayoutIntent.Dropdown or LayoutIntent.ToggleSelection or LayoutIntent.SortIndicator or
                    LayoutIntent.ScrollHandle or LayoutIntent.SliderFill or LayoutIntent.Fill
            };
            if (widgets.TryGetValue(planned.Widget, out var owner))
                foreach (var reference in owner.References.Values) Protect(reference, changesVisibility);
            if (changesVisibility) Protect(planned.Widget, entireSubtree: true);

            switch (planned.Contribution)
            {
                case ConnectionContribution connection:
                    Protect(connection.Target);
                    Protect(connection.StateSource);
                    break;
                case LayoutContribution layout:
                    // A CoverChildren measurement source may depend on hidden descendants.
                    Protect(layout.Source, layout.Intent == LayoutIntent.CopyDimensions);
                    Protect(layout.Target, changesVisibility);
                    Protect(layout.SecondaryTarget, changesVisibility);
                    break;
            }
        }

        var selectors = (_regressionOptions?.KeyBoundsSelectors ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (selectors.Contains(input.Widget.Path) || selectors.Contains(input.Widget.Id.Value))
                Protect(input.Widget.Id, entireSubtree: true);
        }
        var fullPrefixes = fullSubtrees.Select(path => path + WidgetPathSeparator).ToArray();
        foreach (var input in inputs)
        {
            if (fullSubtrees.Contains(input.Widget.Path) ||
                fullPrefixes.Any(prefix => input.Widget.Path.StartsWith(prefix, StringComparison.Ordinal)))
                ProtectPath(input.Widget.Path);
        }
    }

    private bool NeedsHiddenContentForLayout(IReadOnlyDictionary<string, string> attributes, Size parentSize)
    {
        if (TryGetWidgetPath(attributes) is not { } path || !_collapsedGeometryPaths.Contains(path)) return false;
        if (!string.IsNullOrWhiteSpace(attributes.GetValueOrDefault("Sprite")) ||
            !string.IsNullOrWhiteSpace(attributes.GetValueOrDefault("Brush"))) return true;
        bool ExplicitAxis(string policy, string suggestion, double available) => ResolveSizePolicy(attributes, policy) switch
        {
            "StretchToParent" => double.IsFinite(available) && available > 0,
            "Fixed" => ResolveDouble(attributes, suggestion, double.NaN) > 0,
            _ => false
        };
        return !ExplicitAxis("WidthSizePolicy", "SuggestedWidth", parentSize.Width) ||
               !ExplicitAxis("HeightSizePolicy", "SuggestedHeight", parentSize.Height);
    }

    private bool CanDeferHiddenSubtree(IReadOnlyDictionary<string, string> attributes)
    {
        if (!_deferHiddenSubtrees || _preserveHiddenLayout || TryGetWidgetPath(attributes) is not { } path || _protectedHiddenPaths.Contains(path))
            return false;
        return _logicalChildren.ContainsKey(ResolveWidgetId(path));
    }

    private int CountLogicalSubtree(WidgetId id)
    {
        if (_logicalSubtreeCounts.TryGetValue(id, out var count)) return count;
        _regressionOptions?.CancellationToken.ThrowIfCancellationRequested();
        count = 1;
        foreach (var child in _logicalChildren.GetValueOrDefault(id, [])) count += CountLogicalSubtree(child);
        _logicalSubtreeCounts.Add(id, count);
        return count;
    }

    private RenderedPrefabNode RenderDeferredHiddenSubtree(PrefabNode node, Size parentSize, bool locallyVisible)
    {
        var path = TryGetWidgetPath(node.Attributes)!;
        var instance = BuildInstanceKey(path);
        var id = ResolveWidgetId(path, instance);
        var deferred = new DeferredHiddenSubtree(id.Value, path, instance, CountLogicalSubtree(id), locallyVisible);
        var element = new Border { Visibility = Visibility.Collapsed };
        ApplySizing(element, node.Attributes, parentSize);
        AttachElementDebugMetadata(element, node.Name, node.Attributes);
        PreviewElementDebugProperties.SetDeferredHiddenSubtree(element, deferred);
        PreviewElementDebugProperties.SetTreeLabel(element,
            $"{PreviewElementDebugProperties.GetTreeLabel(element)} [hidden; {deferred.LogicalWidgetCount} logical widgets deferred]");
        element.ToolTip = $"{path}\nHidden subtree deferred. Hidden assets are validated when visible or in a strict capture.";
        if (_reportedDeferredSubtrees.Add(id)) _diagnostics.DeferHiddenSubtree(deferred);
        return new(element, null);
    }
}
