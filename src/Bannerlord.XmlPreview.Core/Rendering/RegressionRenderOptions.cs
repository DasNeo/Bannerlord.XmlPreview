using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using System.Collections.ObjectModel;
using System.Windows;

namespace Bannerlord.XmlPreview.Rendering;

public sealed record RegressionRenderOptions(
    string PrefabIdentity,
    IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WidgetStates,
    IReadOnlyList<string> KeyBoundsSelectors)
{
    public string? AssetModule { get; init; }
    public Size? PreviewSize { get; init; }
    public bool TraceBehaviors { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Lists { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public IReadOnlyDictionary<string, string> OptionalConnectionExplanations { get; init; } = new Dictionary<string, string>();
}

internal static class RegressionSnapshotProducer
{
    public static RenderStructureSnapshot Structure(PrefabDocument document, PrefabRenderContext context, string identity) =>
        new(1, StableWidgetId.CanonicalPrefab(identity), Visit(document.RootNode, context, identity, ""));

    private static RenderWidgetSnapshot Visit(PrefabNode node, PrefabRenderContext context, string identity, string instance)
    {
        var path = node.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, node.Name);
        var attributes = context.SnapshotAttributes(node.Attributes);
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in attributes.Where(p => !p.Key.StartsWith("Debug.", StringComparison.Ordinal)))
            properties[pair.Key] = context.ResolveSnapshotValue(attributes, pair.Key, pair.Value);

        var children = new List<RenderWidgetSnapshot>();
        foreach (var child in node.Children)
        {
            if (child is ItemTemplateNode && context.TryGetTemplateItemBindings(node, out var items))
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var key = items[i].GetValueOrDefault("__key", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var nextInstance = instance + "/" + path + "=" + key;
                    using var scope = context.PushBindingScope(items[i], key);
                    children.Add(Visit(child, context, identity, nextInstance));
                }
            }
            else children.Add(Visit(child, context, identity, instance));
        }
        return new(StableWidgetId.Create(identity, path, instance), path, instance, node.Name,
            new ReadOnlyDictionary<string, string>(properties), Array.AsReadOnly(children.ToArray()));
    }

    public static LayoutSnapshot Layout(FrameworkElement root, IReadOnlyList<SnapshotElement> elements,
        IReadOnlyList<string> selectors)
    {
        var bounds = new List<WidgetBounds>();
        foreach (var entry in elements)
        {
            if (entry.Element != root && !entry.Attributes.ContainsKey("Id") &&
                !selectors.Contains(entry.Path, StringComparer.Ordinal) && !selectors.Contains(entry.Id, StringComparer.Ordinal))
                continue;
            var point = entry.Element.TranslatePoint(new Point(), root);
            bounds.Add(new(entry.Id, entry.Path, point.X, point.Y, entry.Element.RenderSize.Width, entry.Element.RenderSize.Height));
        }
        foreach (var selector in selectors)
            if (!bounds.Any(b => b.WidgetId == selector || b.ExpandedPath == selector))
                throw new InvalidOperationException($"Key Bounds selector '{selector}' did not match a rendered widget.");
        return new(1, Array.AsReadOnly(bounds.OrderBy(b => b.WidgetId, StringComparer.Ordinal).ToArray()));
    }

    public static RenderStructureSnapshot ResolveBehavior(RenderStructureSnapshot snapshot, IReadOnlyList<SnapshotElement> elements)
    {
        var registrations = elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        RenderWidgetSnapshot Resolve(RenderWidgetSnapshot node)
        {
            var properties = new SortedDictionary<string, string>(node.Properties.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);
            if (registrations.TryGetValue(node.WidgetId, out var registration))
            {
                properties["Resolved.IsVisible"] = registration.Element.Visibility == Visibility.Visible ? "true" : "false";
                properties["Resolved.IsEnabled"] = registration.Element.IsEnabled ? "true" : "false";
                properties["Resolved.Opacity"] = registration.Element.Opacity.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (PreviewElementDebugProperties.GetImageResolution(registration.Element) is { } image)
                {
                    properties["Resolved.ImageStatus"] = image.Status.ToString();
                    properties["Resolved.ImageIdentity"] = image.Request.Identity;
                    properties["Resolved.ImageProvider"] = image.Request.Provider ?? "";
                    properties["Resolved.ImageWidth"] = image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    properties["Resolved.ImageHeight"] = image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return node with { Properties = new ReadOnlyDictionary<string, string>(properties), Children = Array.AsReadOnly(node.Children.Select(Resolve).ToArray()) };
        }
        return snapshot with { Root = Resolve(snapshot.Root) };
    }
}

internal sealed record SnapshotElement(string Id, string Path, FrameworkElement Element,
    IReadOnlyDictionary<string, string> Attributes);
