using System.IO;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;

namespace Bannerlord.XmlPreview.VisualRegression;

public static class ScenarioApplication
{
    public static PrefabDocument Apply(PrefabDocument document, PreviewScenario scenario)
    {
        ScenarioLoader.Validate(scenario);
        if (StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) != StableWidgetId.CanonicalPrefab(document.PrefabIdentity ?? Path.GetFileName(document.SourcePath)))
            throw new InvalidDataException("Scenario belongs to another prefab.");
        var nodes = Descendants(document.RootNode).ToArray();
        var paths = nodes.Select(node => node.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, node.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var selector in scenario.WidgetStates.Keys.Concat(scenario.Lists.Keys))
            if (!paths.Contains(selector)) throw new InvalidDataException("Scenario selector did not match: " + selector);
        if (scenario.ScopeWidgetTypes.Length == 0) return document;
        var targets = nodes.Where(node => scenario.ScopeWidgetTypes.Contains(node.Name, StringComparer.Ordinal)).ToArray();
        if (targets.Length == 0 || targets.Any(target => Descendants(target).Skip(1).Any(child => targets.Contains(child))))
            throw new InvalidDataException("Widget scope requires matching, nonoverlapping widgets.");
        return document with { RootNode = new WindowNode(new Dictionary<string, string>
        { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = "Window" }, targets) };
    }
    public static RegressionRenderOptions Options(PreviewScenario scenario, CancellationToken token = default) =>
        new(scenario.PrefabIdentity, scenario.Bindings, scenario.WidgetStates, scenario.KeyBoundsSelectors)
        {
            PreviewSize = new(scenario.CaptureProfile.Width, scenario.CaptureProfile.Height),
            AssetModule = scenario.AssetModule, OptionalConnectionExplanations = scenario.OptionalConnectionExplanations,
            CancellationToken = token,
            Lists = scenario.Lists.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<IReadOnlyDictionary<string, string>>)pair.Value.Cast<IReadOnlyDictionary<string, string>>().ToArray(), StringComparer.Ordinal)
        };
    private static IEnumerable<PrefabNode> Descendants(PrefabNode node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
