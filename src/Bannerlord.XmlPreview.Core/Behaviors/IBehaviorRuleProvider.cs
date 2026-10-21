using System.Collections.Immutable;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Core.Behaviors;

public sealed record BehaviorTypeMetadata(ImmutableHashSet<string> BaseTypes, ImmutableHashSet<string> Capabilities);
public interface IBehaviorRuleProvider
{
    ImmutableArray<BehaviorRule> Rules { get; }
    BehaviorTypeMetadata Describe(PrefabNode node);
}
