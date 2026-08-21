using System.Text.RegularExpressions;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class SelectedElementInfoViewModel
{
    private SelectedElementInfoViewModel(
        bool hasContent,
        string title,
        string subtitle,
        string placeholderMessage,
        IReadOnlyList<SelectedInfoFieldViewModel> summaryFields,
        IReadOnlyList<SelectedInfoFieldViewModel> nodeFields,
        IReadOnlyList<SelectedInfoFieldViewModel> layoutFields,
        IReadOnlyList<SelectedBrushInfoViewModel> brushes,
        IReadOnlyList<SelectedSpriteInfoViewModel> sprites)
    {
        HasContent = hasContent;
        Title = title;
        Subtitle = subtitle;
        PlaceholderMessage = placeholderMessage;
        SummaryFields = summaryFields;
        NodeFields = nodeFields;
        LayoutFields = layoutFields;
        Brushes = brushes;
        Sprites = sprites;
    }

    public bool HasContent { get; }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool HasSummaryFields => SummaryFields.Count > 0;

    public bool HasNodeFields => NodeFields.Count > 0;

    public bool HasLayoutFields => LayoutFields.Count > 0;

    public bool HasBrushes => Brushes.Count > 0;

    public bool HasSprites => Sprites.Count > 0;

    public string Title { get; }

    public string Subtitle { get; }

    public string PlaceholderMessage { get; }

    public IReadOnlyList<SelectedInfoFieldViewModel> SummaryFields { get; }

    public IReadOnlyList<SelectedInfoFieldViewModel> NodeFields { get; }

    public IReadOnlyList<SelectedInfoFieldViewModel> LayoutFields { get; }

    public IReadOnlyList<SelectedBrushInfoViewModel> Brushes { get; }

    public IReadOnlyList<SelectedSpriteInfoViewModel> Sprites { get; }

    public static SelectedElementInfoViewModel Empty(string? message)
    {
        return new SelectedElementInfoViewModel(
            false,
            "No element selected",
            string.Empty,
            string.IsNullOrWhiteSpace(message)
                ? "Click an element in the preview to inspect its layout and assets."
                : message.Trim(),
            [],
            [],
            [],
            [],
            []);
    }

    public static SelectedElementInfoViewModel FromDebugInfo(ElementDebugInfo? debugInfo)
    {
        if (debugInfo is null) return Empty(null);

        var title = string.IsNullOrWhiteSpace(debugInfo.NodeName) ? "Selected element" : debugInfo.NodeName;
        var subtitle = FirstNonEmpty(
            debugInfo.GetAttributeDisplayValue("Id"),
            debugInfo.GetAttributeDisplayValue("Text"),
            debugInfo.GetAttributeDisplayValue("Brush"));

        var actualBounds = debugInfo.ActualLayout?.GetValue("ActualBounds");
        var (actualPosition, actualSize) = SplitRectValue(actualBounds);
        var summaryFields = BuildSummaryFields(debugInfo, actualSize, actualPosition);
        var nodeFields = BuildNodeFields(debugInfo);
        var layoutFields = BuildLayoutFields(debugInfo, actualSize, actualPosition);
        var brushes = BuildBrushes(debugInfo);
        var sprites = BuildSprites(debugInfo);

        return new SelectedElementInfoViewModel(
            true,
            title,
            subtitle,
            string.Empty,
            summaryFields,
            nodeFields,
            layoutFields,
            brushes,
            sprites);
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildSummaryFields(
        ElementDebugInfo debugInfo,
        string actualSize,
        string actualPosition)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Id", debugInfo.GetAttributeDisplayValue("Id"));
        AddField(items, "Brush", debugInfo.GetAttributeDisplayValue("Brush"));

        var inputValue = debugInfo.GetAttributeDisplayValue("DoNotAcceptEvents");
        var inputState = bool.TryParse(inputValue, out var isDisabled)
            ? isDisabled ? "Disabled" : "Enabled"
            : null;
        AddField(items, "Input", inputState);
        AddField(items, "Size", actualSize);
        AddField(items, "Position", actualPosition);

        return items;
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildNodeFields(ElementDebugInfo debugInfo)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Id", debugInfo.GetAttributeDisplayValue("Id"));
        AddField(items, "Brush", debugInfo.GetAttributeDisplayValue("Brush"));

        var inputValue = debugInfo.GetAttributeDisplayValue("DoNotAcceptEvents");
        var inputState = bool.TryParse(inputValue, out var isDisabled)
            ? isDisabled ? "Does not accept input" : "Accepts input"
            : null;
        AddField(items, "Input", inputState);
        AddField(items, "Size policy", debugInfo.Layout?.GetValue("SizePolicy"));
        AddField(items, "Suggested size", debugInfo.Layout?.GetValue("SuggestedSize"));
        AddField(items, "Alignment", debugInfo.Layout?.GetValue("Alignment"));

        var marginValue = FirstNonEmpty(
            debugInfo.ActualLayout?.GetValue("Margin"),
            debugInfo.Layout?.GetValue("Margin"));
        if (!string.Equals(marginValue, "L=0 T=0 R=0 B=0", StringComparison.Ordinal))
            AddField(items, "Margin", marginValue);

        return items;
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildLayoutFields(
        ElementDebugInfo debugInfo,
        string actualSize,
        string actualPosition)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Parent",
            FirstNonEmpty(debugInfo.ActualLayout?.GetValue("ParentSize"), debugInfo.Layout?.GetValue("ParentSize")));
        AddField(items, "Actual size", actualSize);
        AddField(items, "Measured",
            FirstNonEmpty(debugInfo.ActualLayout?.GetValue("MeasuredSize"),
                debugInfo.Layout?.GetValue("MeasuredSize")));
        AddField(items, "Position", actualPosition);
        AddField(items, "Selection", debugInfo.ActualLayout?.GetValue("SelectionBoundsRoot"));
        AddField(items, "Selection local", debugInfo.ActualLayout?.GetValue("SelectionBoundsLocal"));

        return items;
    }

    private static IReadOnlyList<SelectedBrushInfoViewModel> BuildBrushes(ElementDebugInfo debugInfo)
    {
        return debugInfo.Brushes
            .Select(brush => new SelectedBrushInfoViewModel(
                brush.IsMissing && brush.IsPreviewFallback ? $"{brush.RequestedName} (preview fallback)" :
                brush.IsMissing ? $"{brush.RequestedName} (missing)" : brush.RequestedName,
                brush.SourcePath ?? string.Empty,
                CompactPath(brush.SourcePath, 4),
                brush.IsMissing ? string.Empty : brush.Layers.Count.ToString(),
                brush.Layers.Select(layer => layer.ToDisplayString().Trim()).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<SelectedSpriteInfoViewModel> BuildSprites(ElementDebugInfo debugInfo)
    {
        return debugInfo.Sprites
            .Select(sprite =>
            {
                var spriteType = sprite.SpriteDefinition?.Kind.ToString() ?? string.Empty;
                var spriteSize = sprite.SpriteDefinition is null
                    ? string.Empty
                    : $"{sprite.SpriteDefinition.Width}x{sprite.SpriteDefinition.Height}";

                return new SelectedSpriteInfoViewModel(
                    sprite.RequestedName,
                    sprite.Usage,
                    sprite.Status.ToString(),
                    spriteType,
                    spriteSize,
                    sprite.ImagePath ?? string.Empty,
                    CompactPath(sprite.ImagePath, 3),
                    sprite.SpriteDefinition?.SourcePath ?? string.Empty,
                    CompactPath(sprite.SpriteDefinition?.SourcePath, 4));
            })
            .ToArray();
    }

    private static void AddField(ICollection<SelectedInfoFieldViewModel> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) items.Add(new SelectedInfoFieldViewModel(label, value));
    }

    private static (string Position, string Size) SplitRectValue(string? rectValue)
    {
        if (string.IsNullOrWhiteSpace(rectValue)) return (string.Empty, string.Empty);

        var match = Regex.Match(rectValue, @"^(?<position>\([^)]+\))\s+(?<size>.+)$");
        return match.Success
            ? (match.Groups["position"].Value.Trim(), match.Groups["size"].Value.Trim())
            : (string.Empty, rectValue.Trim());
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string CompactPath(string? path, int segmentCount)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var normalized = path.Replace('/', '\\').Trim();
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= segmentCount) return normalized;

        return string.Join('\\', segments[^segmentCount..]);
    }

    public sealed record SelectedInfoFieldViewModel(string Label, string Value);

    public sealed record SelectedBrushInfoViewModel(
        string Name,
        string SourcePath,
        string SourceDisplayPath,
        string LayerCount,
        IReadOnlyList<string> Layers)
    {
        public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);

        public bool HasLayerCount => !string.IsNullOrWhiteSpace(LayerCount);

        public bool HasLayers => Layers.Count > 0;
    }

    public sealed record SelectedSpriteInfoViewModel(
        string Name,
        string Usage,
        string Status,
        string SpriteType,
        string SpriteSize,
        string ImagePath,
        string ImageDisplayPath,
        string SourcePath,
        string SourceDisplayPath)
    {
        public bool HasUsage => !string.IsNullOrWhiteSpace(Usage);

        public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

        public bool HasSpriteType => !string.IsNullOrWhiteSpace(SpriteType);

        public bool HasSpriteSize => !string.IsNullOrWhiteSpace(SpriteSize);

        public bool HasImagePath => !string.IsNullOrWhiteSpace(ImagePath);

        public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);
    }
}