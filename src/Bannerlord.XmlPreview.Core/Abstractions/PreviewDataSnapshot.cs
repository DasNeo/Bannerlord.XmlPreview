using System.Collections.Immutable;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Core.Abstractions;

public sealed record PreviewPanelColors(string Fill, string Stroke);

/// <summary>Data copied once at the render boundary; contains no dispatcher-owned objects.</summary>
public sealed record PreviewDataSnapshot
{
    public ImmutableDictionary<string, string> Bindings { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<string, PreviewPanelColors> PanelColors { get; init; } = ImmutableDictionary.Create<string, PreviewPanelColors>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<string, string> TextColors { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<string, string> OptionalConnectionExplanations { get; init; } = ImmutableDictionary<string, string>.Empty;
    public string FrameBorderColor { get; init; } = "#B7985A";
    public ImmutableDictionary<string, ImmutableArray<ImmutableDictionary<string, string>>> TemplateItems { get; init; } = ImmutableDictionary<string, ImmutableArray<ImmutableDictionary<string, string>>>.Empty;
    public static PreviewDataSnapshot Empty { get; } = new();

    public static PreviewDataSnapshot Capture(IPreviewDataSourceProvider provider, PrefabNode root, PreviewDataSnapshot defaults)
    {
        var items = ImmutableDictionary.CreateBuilder<string, ImmutableArray<ImmutableDictionary<string, string>>>();
        void Visit(PrefabNode node)
        {
            if (provider.TryGetTemplateItemBindings(node, out var rows))
                items[Path(node)] = rows.Select(row => row.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)).ToImmutableArray();
            foreach (var child in node.Children) Visit(child);
        }
        Visit(root);
        return defaults with { TemplateItems = items.ToImmutable() };
    }

    internal static string Path(PrefabNode node) => node.Attributes.GetValueOrDefault(Bannerlord.XmlPreview.Models.PrefabVisualTreeHelper.DebugWidgetPathAttributeName, node.Name);
}
