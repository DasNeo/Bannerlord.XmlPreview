using System.Collections.Immutable;
using System.Diagnostics;

namespace Bannerlord.XmlPreview.Core.Behaviors;

/// <summary>A fresh plan per render. The only graph entry point performs complete preflight without WPF.</summary>
public sealed class BehaviorGraph
{
    public BehaviorGraphPlan Plan(ImmutableArray<BehaviorInput> widgets, ImmutableArray<BehaviorRule> rules,
        bool trace = false, CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        var contributions = new List<PlannedContribution>();
        var matching = new Stopwatch();
        var sorting = new Stopwatch();
        var edges = 0;
        var diagnostics = new List<string>();
        BehaviorGraphException Fail(string code, string message, WidgetId? widget = null, RuleId? rule = null, Exception? inner = null)
            => new(new(code, message, widget, rule), BuildTrace(), inner)
            {
                Summary = new(rules.Length, widgets.Sum(w => rules.Count(r => r.Matcher.Matches(w.Widget))), edges,
                    Enum.GetValues<BehaviorPhase>().ToImmutableDictionary(p => p, p => contributions.Count(c => c.Contribution.Phase == p)),
                    Enum.GetValues<BehaviorSupport>().ToImmutableDictionary(s => s, s => BuildTrace().Count(t => t.Support == s)),
                    matching.Elapsed.TotalMilliseconds, sorting.Elapsed.TotalMilliseconds, total.Elapsed.TotalMilliseconds)
                { MatchedRuleCount = rules.Count(r => widgets.Any(w => r.Matcher.Matches(w.Widget))) }
            };
        ImmutableArray<BehaviorTrace> BuildTrace() => widgets.Select(input =>
        {
            var applied = contributions.Where(c => c.Widget == input.Widget.Id).ToImmutableArray();
            return new BehaviorTrace(input.Widget.Id,
                rules.Where(r => r.Matcher.Matches(input.Widget)).OrderBy(r => r.Phase).ThenBy(r => r.Id).Select(r => r.Id).ToImmutableArray(),
                rules.Where(r => !r.Matcher.Matches(input.Widget)).OrderBy(r => r.Id).Select(r => r.Id).ToImmutableArray(), applied,
                applied.Any(c => c.Contribution is InspectionContribution { Support: BehaviorSupport.Partial }) ? BehaviorSupport.Partial : BehaviorSupport.Supported)
            {
                Rules = rules.OrderBy(r => r.Phase).ThenBy(r => r.Id).Select(r => new BehaviorRuleTrace(r.Id, r.Phase, r.Matcher,
                    r.Matcher.Matches(input.Widget), applied.Any(c => c.Rule == r.Id) ? Array.FindIndex(applied.ToArray(), c => c.Rule == r.Id) : null)).ToImmutableArray()
            };
        }).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (widgets.Select(w => w.Widget.Id).Distinct().Count() != widgets.Length)
            throw Fail("DuplicateWidgetId", "Expanded widgets must have unique stable IDs.");
        if (rules.Any(r => string.IsNullOrWhiteSpace(r.Id.Value)) || rules.Select(r => r.Id).Distinct().Count() != rules.Length)
            throw Fail("DuplicateRuleId", "Rule IDs must be nonempty and unique.");
        if (rules.Any(r => !Enum.IsDefined(r.Phase))) throw Fail("UnknownPhase", "Only the five reviewed behavior phases are supported.");
        matching.Start();
        var matches = widgets.ToDictionary(w => w.Widget.Id, w => rules.Where(r => r.Matcher.Matches(w.Widget)).ToImmutableArray());
        matching.Stop();
        sorting.Start();
        var explicitEdges = new HashSet<(RuleId Before, RuleId After)>();
        var orderedRules = new List<BehaviorRule>();
        foreach (var phase in Enum.GetValues<BehaviorPhase>())
        {
            var phaseRules = rules.Where(r => r.Phase == phase).ToDictionary(r => r.Id);
            var outgoing = phaseRules.Keys.ToDictionary(id => id, _ => new HashSet<RuleId>());
            var indegrees = phaseRules.Keys.ToDictionary(id => id, _ => 0);
            void Edge(RuleId before, RuleId after)
            {
                if (!phaseRules.ContainsKey(before) || !phaseRules.ContainsKey(after))
                    throw Fail("InvalidEdge", $"Dependency '{before}' -> '{after}' must name rules in the same phase.");
                if (outgoing[before].Add(after)) { indegrees[after]++; edges++; explicitEdges.Add((before, after)); }
            }
            foreach (var rule in phaseRules.Values)
            {
                foreach (var id in rule.Before.IsDefault ? [] : rule.Before) Edge(rule.Id, id);
                foreach (var id in rule.After.IsDefault ? [] : rule.After) Edge(id, rule.Id);
            }
            var ready = new SortedSet<RuleId>(indegrees.Where(p => p.Value == 0).Select(p => p.Key));
            var count = 0;
            while (ready.Count > 0)
            {
                var id = ready.Min; ready.Remove(id); orderedRules.Add(phaseRules[id]); count++;
                foreach (var next in outgoing[id]) if (--indegrees[next] == 0) ready.Add(next);
            }
            if (count != phaseRules.Count) throw Fail("Cycle", $"Behavior dependencies contain a cycle in {phase}.");
        }
        sorting.Stop();
        var merges = new Dictionary<(WidgetId, string), CollectionMerge>();
        var runValues = widgets.ToImmutableDictionary(w => w.Widget.Id, w => w.Values);
        var writers = new HashSet<(WidgetId, BehaviorPhase, string)>();
        var ids = widgets.Select(w => w.Widget.Id).ToHashSet();
        foreach (var input in widgets.OrderBy(w => w.Widget.Id.Value, StringComparer.Ordinal))
        {
            var selected = matches[input.Widget.Id];
            if (selected.Count(r => r.Phase == BehaviorPhase.Instantiate) > 1)
                throw Fail("InstantiateConflict", $"Multiple exclusive Instantiate rules match {input.Widget.Path}.", input.Widget.Id);
        }
        // Phase ordering is global, not one widget at a time.
        foreach (var rule in orderedRules)
        foreach (var input in widgets.OrderBy(w => w.Widget.Id.Value, StringComparer.Ordinal))
        {
            if (!matches[input.Widget.Id].Contains(rule)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<BehaviorContribution> values;
            try { values = rule.Handler.Contribute(input with { Values = runValues[input.Widget.Id], RunValues = runValues }); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { throw Fail("HandlerFailure", $"Rule '{rule.Id}' failed for {input.Widget.Path}: {ex.Message}", input.Widget.Id, rule.Id, ex); }
            if (values.IsDefault || values.Any(v => v is null))
                throw Fail("InvalidContribution", $"Rule '{rule.Id}' returned an invalid contribution array.", input.Widget.Id, rule.Id);
            if (rule.Phase == BehaviorPhase.Instantiate && (values.Length != 1 || values[0] is not ElementBlueprint))
                throw Fail("InvalidInstantiation", $"Instantiate rule '{rule.Id}' must return exactly one Element Blueprint.", input.Widget.Id, rule.Id);
            foreach (var value in values)
            {
                if (value.Phase != rule.Phase) throw Fail("WrongPhase", $"Rule '{rule.Id}' returned a contribution for {value.Phase}.", input.Widget.Id, rule.Id);
                var target = input.Widget.Id;
                string? property = null;
                switch (value)
                {
                    case DescriptionContribution d:
                        target = d.Target ?? target;
                        if (!ids.Contains(target)) throw Fail("MissingDescriptionTarget", "Description target must be in the expanded tree.", input.Widget.Id, rule.Id);
                        if (!Enum.IsDefined(d.Merge)) throw Fail("UnknownMerge", "Unknown collection merge contract.");
                        var key = (target, d.Property.ToUpperInvariant());
                        if (merges.TryGetValue(key, out var merge) && (merge != CollectionMerge.Append || d.Merge != CollectionMerge.Append))
                            throw Fail("MultipleWriters", $"Multiple writers for {target.Value}.{d.Property} in Describe.", input.Widget.Id, rule.Id);
                        merges[key] = d.Merge;
                        if (d.Merge == CollectionMerge.Replace) property = d.Property;
                        break;
                    case ElementBlueprint blueprint:
                        if (!Enum.IsDefined(blueprint.Kind)) throw Fail("UnknownBlueprint", "Unknown element blueprint kind.");
                        property = "Element";
                        break;
                    case ConnectionContribution c:
                        if (!Enum.IsDefined(c.Requirement)) throw Fail("UnknownRequirement", $"Unknown requirement for '{c.Role}'.", input.Widget.Id, rule.Id);
                        property = c.Role;
                        if (c.StateSource is { } stateSource)
                        {
                            if (c.Target is null || !ids.Contains(stateSource)) throw Fail("MissingStateTarget", "State source and target must be in the expanded tree.");
                            target = c.Target.Value;
                            property = "State";
                        }
                        if (c.Target is null || !ids.Contains(c.Target.Value))
                        {
                            if (c.Requirement == ConnectionRequirement.Required)
                                throw Fail("MissingRequiredConnection", $"{input.Widget.Path}: required connection '{c.Role}' is missing.", input.Widget.Id, rule.Id);
                            if (string.IsNullOrWhiteSpace(c.ScenarioExplanation))
                                throw Fail("MissingOptionalExplanation", $"Optional connection '{c.Role}' requires a scenario explanation.", input.Widget.Id, rule.Id);
                            diagnostics.Add($"{input.Widget.Path}: optional '{c.Role}' missing: {c.ScenarioExplanation}");
                        }
                        break;
                    case LayoutContribution l:
                        if (!Enum.IsDefined(l.Intent)) throw Fail("UnknownLayoutIntent", "Unknown layout intent.");
                        if (!double.IsFinite(l.Padding)) throw Fail("InvalidLayoutValue", "Layout padding must be finite.");
                        if ((l.Intent == LayoutIntent.CopyDimensions && l.Source is null) ||
                            (l.Intent == LayoutIntent.RangeValue && l.Source is null && l.SecondaryTarget is null))
                            throw Fail("MissingLayoutSource", "This layout intent requires a source.", input.Widget.Id, rule.Id);
                        if (l.Intent == LayoutIntent.CopyDimensions && l.Axis is not ("Horizontal" or "Vertical" or "HorizontalAndVertical"))
                            throw Fail("UnknownAxis", "Dimension copying requires a reviewed axis.", input.Widget.Id, rule.Id);
                        if (l.Intent == LayoutIntent.RangeValue && (l.Axis is not ("Horizontal" or "Vertical") ||
                            !double.TryParse(l.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fraction) || !double.IsFinite(fraction) || fraction is < 0 or > 1))
                            throw Fail("InvalidLayoutValue", "Range layout requires a valid axis and fraction in [0, 1].", input.Widget.Id, rule.Id);
                        target = l.Target ?? target;
                        property = l.Intent is LayoutIntent.TargetState or LayoutIntent.SetState or LayoutIntent.DelayedState ? "State" : l.Intent.ToString();
                        if ((l.Source is { } source && !ids.Contains(source)) || (l.SecondaryTarget is { } secondary && !ids.Contains(secondary)) || !ids.Contains(target))
                            throw Fail("MissingLayoutTarget", "Layout source or target is not in the expanded tree.", input.Widget.Id, rule.Id);
                        break;
                    case InspectionContribution inspection:
                        if (!Enum.IsDefined(inspection.Support)) throw Fail("UnknownSupport", "Unknown support status.", input.Widget.Id, rule.Id);
                        break;
                    default:
                        throw Fail("UnsupportedContribution", $"Rule '{rule.Id}' returned an unreviewed contribution type.", input.Widget.Id, rule.Id);
                }
                if (value is LayoutContribution layout)
                {
                    var footprints = new List<(WidgetId Target, string Property)>();
                    if (layout.Intent == LayoutIntent.CopyDimensions)
                    {
                        if (layout.Axis is "Horizontal" or "HorizontalAndVertical") footprints.Add((target, "Width"));
                        if (layout.Axis is "Vertical" or "HorizontalAndVertical") footprints.Add((target, "Height"));
                    }
                    if (layout.Intent == LayoutIntent.RangeValue)
                    {
                        if (layout.Source is { } handle) footprints.Add((handle, layout.Axis == "Vertical" ? "Canvas.Top" : "Canvas.Left"));
                        if (layout.SecondaryTarget is { } filler) footprints.Add((filler, layout.Axis == "Vertical" ? "Height" : "Width"));
                    }
                    foreach (var footprint in footprints)
                        if (!writers.Add((footprint.Target, rule.Phase, footprint.Property.ToUpperInvariant())))
                            throw Fail("MultipleWriters", $"Multiple writers for {footprint.Target.Value}.{footprint.Property} in {rule.Phase}.", input.Widget.Id, rule.Id);
                }
                if (property is not null && !writers.Add((target, rule.Phase, property.ToUpperInvariant())))
                    throw Fail("MultipleWriters", $"Multiple writers for {target.Value}.{property} in {rule.Phase}.", input.Widget.Id, rule.Id);
                if (value is DescriptionContribution description)
                {
                    var previous = runValues[target].GetValueOrDefault(description.Property, "");
                    runValues = runValues.SetItem(target, runValues[target].SetItem(description.Property,
                        description.Merge == CollectionMerge.Append ? previous + description.Value : description.Value));
                }
                contributions.Add(new(input.Widget.Id, rule.Id, value));
            }
        }
        foreach (var input in widgets)
            if (!matches[input.Widget.Id].Any(r => r.Phase == BehaviorPhase.Instantiate))
                contributions.Add(new(input.Widget.Id, new("core.fallback"), new ElementBlueprint(ElementKind.Generic)));
        sorting.Start();
        // Cross-widget state and dimension copies depend on the writer of their source.
        foreach (var phase in new[] { BehaviorPhase.Connect, BehaviorPhase.PrepareLayout })
        {
            var transfers = contributions.Where(c => c.Contribution.Phase == phase &&
                (c.Contribution is ConnectionContribution { StateSource: not null } || c.Contribution is LayoutContribution { Intent: LayoutIntent.CopyDimensions })).ToList();
            WidgetId Target(PlannedContribution c) => c.Contribution is ConnectionContribution { StateSource: not null } state ? state.Target!.Value : ((LayoutContribution)c.Contribution).Target ?? c.Widget;
            WidgetId Source(PlannedContribution c) => c.Contribution is ConnectionContribution { StateSource: not null } state ? state.StateSource!.Value : ((LayoutContribution)c.Contribution).Source!.Value;
            var pending = new List<PlannedContribution>(transfers);
            var ordered = new List<PlannedContribution>();
            while (pending.Count > 0)
            {
                bool Precedes(RuleId before, RuleId after, HashSet<RuleId> visited)
                {
                    if (!visited.Add(before)) return false;
                    return explicitEdges.Where(e => e.Before == before).Any(e => e.After == after || Precedes(e.After, after, visited));
                }
                var ready = pending.Where(c => !pending.Any(other => Target(other) == Source(c) ||
                    (other != c && Precedes(other.Rule, c.Rule, [])))).OrderBy(c => transfers.IndexOf(c)).FirstOrDefault();
                if (ready is null) throw Fail("ConnectionCycle", "Cross-widget behavior contains a cycle.");
                if (ready.Contribution is ConnectionContribution { StateSource: not null } transfer)
                {
                    var state = runValues[transfer.StateSource!.Value].GetValueOrDefault("Preview.State", "Default");
                    runValues = runValues.SetItem(transfer.Target!.Value, runValues[transfer.Target.Value].SetItem("Preview.State", state));
                    ordered.Add(ready with { Contribution = transfer with { StateValue = state } });
                }
                else ordered.Add(ready);
                pending.Remove(ready);
            }
            contributions.RemoveAll(c => transfers.Contains(c));
            contributions.AddRange(ordered);
            edges += transfers.Sum(c => transfers.Count(other => Target(other) == Source(c)));
        }
        sorting.Stop();
        var support = widgets.ToImmutableDictionary(w => w.Widget.Id, w => contributions.Any(c => c.Widget == w.Widget.Id && c.Contribution is InspectionContribution { Support: BehaviorSupport.Partial }) ? BehaviorSupport.Partial : BehaviorSupport.Supported);
        return new(contributions.OrderBy(c => c.Contribution.Phase).ToImmutableArray(),
            new(rules.Length, matches.Sum(p => p.Value.Length), edges,
                Enum.GetValues<BehaviorPhase>().ToImmutableDictionary(p => p, p => contributions.Count(c => c.Contribution.Phase == p)),
                Enum.GetValues<BehaviorSupport>().ToImmutableDictionary(s => s, s => support.Count(p => p.Value == s)),
                matching.Elapsed.TotalMilliseconds, sorting.Elapsed.TotalMilliseconds, total.Elapsed.TotalMilliseconds)
                { MatchedRuleCount = matches.Values.SelectMany(r => r).Select(r => r.Id).Distinct().Count() },
            trace ? BuildTrace() : [], diagnostics.ToImmutableArray());
    }
}
