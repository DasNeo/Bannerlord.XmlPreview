using System.Collections.Immutable;

namespace Bannerlord.XmlPreview.Core.Behaviors;

public enum BehaviorPhase { Describe, Instantiate, Connect, PrepareLayout, Inspect }
public enum BehaviorSupport { Supported, Partial }
public enum ConnectionRequirement { Required, Optional }
public enum CollectionMerge { Replace, Append }
public enum ElementKind { Generic, Container, Placeholder, Text, KeyGlyph }
public enum LayoutIntent { CopyDimensions, ScrollViewport, Clip, ToggleSelection, Dropdown, RadialChildren, ScrollHandle, Fill, SortIndicator, TargetState, DelayedState, SliderFill, RangeValue, SetState }
public readonly record struct RuleId(string Value) : IComparable<RuleId>
{
    public int CompareTo(RuleId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}
public readonly record struct WidgetId(string Value);

/// <summary>Matcher input deliberately excludes resolved values and scenario data.</summary>
public sealed record WidgetDescription(WidgetId Id, string Path, string Type,
    ImmutableDictionary<string, string> XmlAttributes, ImmutableHashSet<string> TrustedBaseTypes,
    ImmutableHashSet<string> Capabilities, string? ParentType, ImmutableArray<string> ChildTypes);
public sealed record MatcherCriteria(string? ExactType = null, string? TrustedBaseType = null,
    string? Capability = null, string? Attribute = null, string? ParentType = null, string? ChildType = null)
{
    public bool Matches(WidgetDescription widget) =>
        (ExactType is null || widget.Type == ExactType) &&
        (TrustedBaseType is null || widget.Type == TrustedBaseType || widget.TrustedBaseTypes.Contains(TrustedBaseType)) &&
        (Capability is null || widget.Capabilities.Contains(Capability)) &&
        (Attribute is null || widget.XmlAttributes.ContainsKey(Attribute)) &&
        (ParentType is null || widget.ParentType == ParentType) &&
        (ChildType is null || widget.ChildTypes.Contains(ChildType));
}

public sealed record BehaviorInput(WidgetDescription Widget, ImmutableDictionary<string, string> Values,
    ImmutableDictionary<string, WidgetId> References, ImmutableDictionary<WidgetId, ImmutableDictionary<string, string>> RunValues);
public interface IBehaviorHandler
{
    ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input);
}
public sealed record BehaviorRule(RuleId Id, BehaviorPhase Phase, MatcherCriteria Matcher, IBehaviorHandler Handler,
    ImmutableArray<RuleId> Before = default, ImmutableArray<RuleId> After = default);
public abstract record BehaviorContribution(BehaviorPhase Phase);
public sealed record DescriptionContribution(string Property, string Value, CollectionMerge Merge = CollectionMerge.Replace) : BehaviorContribution(BehaviorPhase.Describe);
public sealed record ElementBlueprint(ElementKind Kind, string? Explanation = null, string? Text = null) : BehaviorContribution(BehaviorPhase.Instantiate);
public sealed record ConnectionContribution(string Role, WidgetId? Target, ConnectionRequirement Requirement, string? ScenarioExplanation = null, WidgetId? StateSource = null, string? StateValue = null) : BehaviorContribution(BehaviorPhase.Connect);
public sealed record LayoutContribution(LayoutIntent Intent, WidgetId? Source = null, WidgetId? Target = null,
    string Axis = "None", double Padding = 0, string? Value = null, WidgetId? SecondaryTarget = null) : BehaviorContribution(BehaviorPhase.PrepareLayout);
public sealed record InspectionContribution(BehaviorSupport Support, string? Diagnostic = null, bool KeyBounds = false) : BehaviorContribution(BehaviorPhase.Inspect);
public sealed record PlannedContribution(WidgetId Widget, RuleId Rule, BehaviorContribution Contribution);
public sealed record BehaviorRuleTrace(RuleId Rule, BehaviorPhase Phase, MatcherCriteria Matcher, bool Selected, int? Order);
public sealed record BehaviorTrace(WidgetId Widget, ImmutableArray<RuleId> Selected, ImmutableArray<RuleId> Skipped,
    ImmutableArray<PlannedContribution> Contributions, BehaviorSupport Support)
{
    public ImmutableArray<BehaviorRuleTrace> Rules { get; init; } = [];
}
public sealed record BehaviorGraphSummary(int RuleCount, int MatchCount, int EdgeCount,
    ImmutableDictionary<BehaviorPhase, int> ContributionCounts, ImmutableDictionary<BehaviorSupport, int> SupportCounts,
    double MatchingMilliseconds, double SortingMilliseconds, double TotalMilliseconds)
{
    public int MatchedRuleCount { get; init; }
}
public sealed record BehaviorGraphPlan(ImmutableArray<PlannedContribution> Contributions, BehaviorGraphSummary Summary,
    ImmutableArray<BehaviorTrace> Trace, ImmutableArray<string> Diagnostics);
public sealed record BehaviorGraphError(string Code, string Message, WidgetId? Widget = null, RuleId? Rule = null);
public sealed class BehaviorGraphException(BehaviorGraphError error, ImmutableArray<BehaviorTrace> trace = default, Exception? inner = null)
    : Exception(error.Message, inner)
{
    public BehaviorGraphError Error { get; } = error;
    public BehaviorGraphSummary? Summary { get; init; }
    public ImmutableArray<BehaviorTrace> Trace { get; } = trace.IsDefault ? [] : trace;
}
