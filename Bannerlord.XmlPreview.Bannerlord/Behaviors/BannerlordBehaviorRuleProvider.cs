using System.Collections.Immutable;
using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Adapters.Bannerlord;

namespace Bannerlord.XmlPreview.Behaviors.Bannerlord;

public sealed class BannerlordBehaviorRuleProvider(IBannerlordWidgetReflectionService? reflection = null) : IBehaviorRuleProvider
{
    public ImmutableArray<BehaviorRule> Rules => BannerlordBehaviorRules.All;
    public BehaviorTypeMetadata Describe(PrefabNode node)
    {
        var bases = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (reflection is not null && reflection.TryGetWidgetType(node.Name, out var type))
            for (var parent = type.BaseType; parent is not null; parent = parent.BaseType) bases.Add(parent.Name);
        var caps = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        caps.Add("preview-state");
        if (node is ScrollablePanelNode || node.Name == "ScrollablePanel" || bases.Contains("ScrollablePanel")) caps.Add("scroll-viewport");
        if (node is ScrollbarWidgetNode || node.Name == "ScrollbarWidget" || bases.Contains("ScrollbarWidget")) caps.Add("scroll-handle");
        if (node is FallbackNode && node.Name is not ("EditableTextWidget" or "InputKeyVisualWidget") && !node.Name.Contains("CodeWidget", StringComparison.Ordinal)) caps.Add("unknown");
        if (node.Name.Contains("CodeWidget", StringComparison.Ordinal)) caps.Add("code-widget");
        return new(bases.ToImmutable(), caps.ToImmutable());
    }
}
