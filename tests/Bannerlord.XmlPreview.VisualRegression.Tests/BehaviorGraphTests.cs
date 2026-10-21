using System.Collections.Immutable;
using Bannerlord.XmlPreview.Core.Behaviors;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class BehaviorGraphTests
{
    private static BehaviorInput Widget(string type = "Widget", string id = "a") => new(new(new(id), id, type, ImmutableDictionary<string, string>.Empty, [], [], null, []), ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, WidgetId>.Empty, ImmutableDictionary<WidgetId, ImmutableDictionary<string, string>>.Empty);
    private sealed class Returns(ImmutableArray<BehaviorContribution> values) : IBehaviorHandler { public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => values; }
    private sealed class Throws : IBehaviorHandler { public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => throw new InvalidOperationException("broken handler"); }
    private static BehaviorRule Rule(string id, BehaviorPhase phase, BehaviorContribution value, params string[] after) => new(new(id), phase, new(), new Returns([value]), After: after.Select(v => new RuleId(v)).ToImmutableArray());
    private static BehaviorGraphPlan Plan(params BehaviorRule[] rules) => new BehaviorGraph().Plan([Widget()], rules.ToImmutableArray(), trace: true);

    [Fact] public void FixedPhasesAndSingleEntryPoint() { Assert.Equal(5, Enum.GetValues<BehaviorPhase>().Length); Assert.Equal(5, typeof(BehaviorContribution).Assembly.GetTypes().Count(type => type.IsSubclassOf(typeof(BehaviorContribution)))); Assert.Single(typeof(BehaviorGraph).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)); }
    [Fact] public void TopologicalOrderIsStableAndAppendIsExplicit()
    {
        var a = Rule("a", BehaviorPhase.Describe, new DescriptionContribution("Tags", "A", CollectionMerge.Append), "z");
        var z = Rule("z", BehaviorPhase.Describe, new DescriptionContribution("Tags", "Z", CollectionMerge.Append));
        var first = Plan(a, z); var second = Plan(z, a);
        Assert.Equal(new[] { "z", "a" }, first.Trace[0].Contributions.Where(c => c.Contribution.Phase == BehaviorPhase.Describe).Select(c => c.Rule.Value));
        Assert.Equal(first.Contributions.ToArray(), second.Contributions.ToArray());
        Assert.Equal(1, first.Summary.EdgeCount);
    }
    [Fact] public void CyclesFailWithFullTrace()
    {
        var e = Assert.Throws<BehaviorGraphException>(() => Plan(Rule("a", BehaviorPhase.Describe, new DescriptionContribution("A", "1"), "b"), Rule("b", BehaviorPhase.Describe, new DescriptionContribution("B", "2"), "a")));
        Assert.Equal("Cycle", e.Error.Code); Assert.Single(e.Trace);
    }
    [Fact] public void DuplicateRulesFail() => Assert.Equal("DuplicateRuleId", Assert.Throws<BehaviorGraphException>(() => Plan(Rule("a", BehaviorPhase.Describe, new DescriptionContribution("A", "1")), Rule("a", BehaviorPhase.Describe, new DescriptionContribution("B", "2")))).Error.Code);
    [Fact] public void ExclusiveInstantiationConflictsFail() => Assert.Equal("InstantiateConflict", Assert.Throws<BehaviorGraphException>(() => Plan(Rule("a", BehaviorPhase.Instantiate, new ElementBlueprint(ElementKind.Container)), Rule("b", BehaviorPhase.Instantiate, new ElementBlueprint(ElementKind.Container)))).Error.Code);
    [Fact] public void FallbackOnlyAppliesWithoutSpecializedRule()
    {
        Assert.Equal("core.fallback", Assert.Single(Plan().Contributions).Rule.Value);
        Assert.Equal("special", Assert.Single(Plan(Rule("special", BehaviorPhase.Instantiate, new ElementBlueprint(ElementKind.Container))).Contributions).Rule.Value);
    }
    [Fact] public void SingleWriterAndMixedAppendFail()
    {
        Assert.Equal("MultipleWriters", Assert.Throws<BehaviorGraphException>(() => Plan(Rule("a", BehaviorPhase.Describe, new DescriptionContribution("Visible", "true")), Rule("b", BehaviorPhase.Describe, new DescriptionContribution("Visible", "false")))).Error.Code);
        Assert.Equal("MultipleWriters", Assert.Throws<BehaviorGraphException>(() => Plan(Rule("a", BehaviorPhase.Describe, new DescriptionContribution("Tags", "A", CollectionMerge.Append)), Rule("b", BehaviorPhase.Describe, new DescriptionContribution("Tags", "B")))).Error.Code);
    }
    [Theory] [InlineData(ConnectionRequirement.Required, null, "MissingRequiredConnection")] [InlineData(ConnectionRequirement.Optional, null, "MissingOptionalExplanation")] [InlineData((ConnectionRequirement)99, "reason", "UnknownRequirement")]
    public void InvalidConnectionsFail(ConnectionRequirement requirement, string? reason, string code) => Assert.Equal(code, Assert.Throws<BehaviorGraphException>(() => Plan(Rule("ref", BehaviorPhase.Connect, new ConnectionContribution("Source", null, requirement, reason)))).Error.Code);
    [Fact] public void OptionalConnectionAlwaysEmitsDiagnostic() => Assert.Contains("scenario omits source", Assert.Single(Plan(Rule("ref", BehaviorPhase.Connect, new ConnectionContribution("Source", null, ConnectionRequirement.Optional, "scenario omits source"))).Diagnostics));
    [Fact] public void HandlerFailuresAreStructured() => Assert.Equal("HandlerFailure", Assert.Throws<BehaviorGraphException>(() => new BehaviorGraph().Plan([Widget()], [new(new("broken"), BehaviorPhase.Describe, new(), new Throws())])).Error.Code);
    [Fact] public void CancellationDoesNotProduceAPlan() { using var c = new CancellationTokenSource(); c.Cancel(); Assert.Throws<OperationCanceledException>(() => new BehaviorGraph().Plan([Widget()], [], cancellationToken: c.Token)); }
    [Fact] public void MatcherCannotDependOnScenarioValues()
    {
        var rule = new BehaviorRule(new("exact"), BehaviorPhase.Describe, new(ExactType: "Pilot", Attribute: "Flag"), new Returns([new DescriptionContribution("Label", "matched")]));
        var input = Widget("Pilot") with { Widget = Widget("Pilot").Widget with { XmlAttributes = ImmutableDictionary<string, string>.Empty.Add("Flag", "@Flag") } };
        var first = new BehaviorGraph().Plan([input with { Values = input.Values.Add("Flag", "true") }], [rule]);
        var second = new BehaviorGraph().Plan([input with { Values = input.Values.Add("Flag", "false") }], [rule]);
        Assert.Equal(first.Contributions.ToArray(), second.Contributions.ToArray());
    }
    [Fact] public void CrossWidgetStateOrderAndCyclesAreValidated()
    {
        var widgets = new[] { Widget("First", "a"), Widget("Second", "b"), Widget("Third", "c") }.ToImmutableArray();
        var rules = new[] {
            new BehaviorRule(new("z"), BehaviorPhase.Connect, new(ExactType: "First"), new Returns([new ConnectionContribution("State", new("b"), ConnectionRequirement.Required, StateSource: new("a"))])),
            new BehaviorRule(new("a"), BehaviorPhase.Connect, new(ExactType: "Second"), new Returns([new ConnectionContribution("State", new("c"), ConnectionRequirement.Required, StateSource: new("b"))])) }.ToImmutableArray();
        var plan = new BehaviorGraph().Plan(widgets, rules);
        Assert.Equal(new[] { "z", "a" }, plan.Contributions.Where(c => c.Contribution is ConnectionContribution { StateSource: not null }).Select(c => c.Rule.Value));
        var cycle = rules.Add(new(new("cycle"), BehaviorPhase.Connect, new(ExactType: "Third"), new Returns([new ConnectionContribution("State", new("a"), ConnectionRequirement.Required, StateSource: new("c"))])));
        Assert.Equal("ConnectionCycle", Assert.Throws<BehaviorGraphException>(() => new BehaviorGraph().Plan(widgets, cycle)).Error.Code);
    }
    [Fact] public void CompactSummaryDoesNotProduceFullTrace()
    {
        var plan = new BehaviorGraph().Plan([Widget()], []);
        Assert.Empty(plan.Trace); Assert.Equal(1, plan.Summary.ContributionCounts[BehaviorPhase.Instantiate]); Assert.Equal(1, plan.Summary.SupportCounts[BehaviorSupport.Supported]);
    }
    [Theory]
    [InlineData("bin/Win64_Shipping_Client/TaleWorlds.GauntletUI.dll", true)]
    [InlineData("Modules/Native/bin/Win64_Shipping_Client/Native.GauntletUI.dll", true)]
    [InlineData("Modules/MyMod/bin/Win64_Shipping_Client/MyMod.dll", false)]
    [InlineData("Modules/Native/../MyMod/bin/Fake.dll", false)]
    [InlineData("../OtherGame/bin/Fake.dll", false)]
    public void ModAssembliesCannotSupplyTrustedWidgetMetadata(string relative, bool expected)
    {
        var root = System.IO.Path.GetFullPath("trusted-game");
        Assert.Equal(expected, Bannerlord.XmlPreview.Adapters.Bannerlord.BannerlordWidgetMetadataTrust.IsTrustedAssemblyPath(root, System.IO.Path.Combine(root, relative)));
    }

    [Fact] public void SingleWriterPropertyNamesAreCaseInsensitive()
    {
        Assert.Equal("MultipleWriters", Assert.Throws<BehaviorGraphException>(() => Plan(
            Rule("a", BehaviorPhase.Describe, new DescriptionContribution("IsVisible", "true")),
            Rule("b", BehaviorPhase.Describe, new DescriptionContribution("isvisible", "false")))).Error.Code);
    }

    [Fact] public void DuplicateWidgetIdsFailAndRetainFailureSummary()
    {
        var exception = Assert.Throws<BehaviorGraphException>(() => new BehaviorGraph().Plan([Widget(), Widget()], []));
        Assert.Equal("DuplicateWidgetId", exception.Error.Code); Assert.NotNull(exception.Summary);
    }

}
