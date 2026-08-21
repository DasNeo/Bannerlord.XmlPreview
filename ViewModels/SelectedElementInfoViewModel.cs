using System.Text.RegularExpressions;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class SelectedElementInfoViewModel
{
    private static readonly string[] SectionHeadings =
    [
        "Attributes",
        "ResolvedProperties",
        "WidgetReferences",
        "Brushes",
        "Sprites",
        "Layout",
        "Actual Layout"
    ];

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

    public static SelectedElementInfoViewModel FromDebugText(string? debugText)
    {
        if (string.IsNullOrWhiteSpace(debugText)) return Empty(null);

        var normalizedText = debugText.Replace("\r\n", "\n");
        var lines = normalizedText.Split('\n');
        var titleLine = lines.FirstOrDefault(line => line.StartsWith("Node: ", StringComparison.Ordinal));
        if (titleLine is null) return Empty(debugText);

        var title = titleLine["Node: ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(title)) title = "Selected element";

        var sections = ParseSections(lines);
        var attributes = ParseKeyValueSection(GetSectionLines(sections, "Attributes"));
        var layout = ParseKeyValueSection(GetSectionLines(sections, "Layout"));
        var actualLayout = ParseKeyValueSection(GetSectionLines(sections, "Actual Layout"));
        var brushes = ParseBrushes(GetSectionLines(sections, "Brushes"));
        var sprites = ParseSprites(GetSectionLines(sections, "Sprites"));

        var subtitle = FirstNonEmpty(
            attributes.GetValueOrDefault("Id"),
            attributes.GetValueOrDefault("Text"),
            attributes.GetValueOrDefault("Brush"));

        var actualBounds = actualLayout.GetValueOrDefault("ActualBounds");
        var (actualPosition, actualSize) = SplitRectValue(actualBounds);
        var summaryFields = BuildSummaryFields(attributes, actualSize, actualPosition);
        var nodeFields = BuildNodeFields(attributes, layout, actualLayout);
        var layoutFields = BuildLayoutFields(layout, actualLayout, actualSize, actualPosition);

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

    private static Dictionary<string, List<string>> ParseSections(IEnumerable<string> lines)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Node: ", StringComparison.Ordinal))
            {
                currentSection = null;
                continue;
            }

            if (SectionHeadings.Contains(trimmed, StringComparer.Ordinal))
            {
                currentSection = trimmed;
                sections[currentSection] = [];
                continue;
            }

            if (currentSection is not null) sections[currentSection].Add(line);
        }

        return sections;
    }

    private static IReadOnlyList<string> GetSectionLines(
        IReadOnlyDictionary<string, List<string>> sections,
        string sectionName)
    {
        return sections.TryGetValue(sectionName, out var lines) ? lines : [];
    }

    private static Dictionary<string, string> ParseKeyValueSection(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (TrySplitKeyValue(line, out var key, out var value)) values[key] = value;
        }

        return values;
    }

    private static IReadOnlyList<SelectedBrushInfoViewModel> ParseBrushes(IEnumerable<string> lines)
    {
        var items = new List<SelectedBrushInfoViewModel>();
        SelectedBrushBuilder? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Brush: ", StringComparison.Ordinal))
            {
                if (current is not null) items.Add(current.Build());

                current = new SelectedBrushBuilder(line["Brush: ".Length..].Trim());
                continue;
            }

            if (current is null || !TrySplitKeyValue(line, out var key, out var value)) continue;

            if (string.Equals(key, "Source", StringComparison.OrdinalIgnoreCase))
                current.SourcePath = value;
            else if (string.Equals(key, "LayerCount", StringComparison.OrdinalIgnoreCase))
                current.LayerCount = value;
            else if (key.StartsWith("Layer ", StringComparison.OrdinalIgnoreCase))
                current.Layers.Add($"{key["Layer ".Length..]}: {value}");
        }

        if (current is not null) items.Add(current.Build());

        return items;
    }

    private static IReadOnlyList<SelectedSpriteInfoViewModel> ParseSprites(IEnumerable<string> lines)
    {
        var items = new List<SelectedSpriteInfoViewModel>();
        SelectedSpriteBuilder? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Sprite: ", StringComparison.Ordinal))
            {
                if (current is not null) items.Add(current.Build());

                current = CreateSpriteBuilder(line["Sprite: ".Length..].Trim());
                continue;
            }

            if (current is null || !TrySplitKeyValue(line, out var key, out var value)) continue;

            switch (key)
            {
                case "Status":
                    current.Status = value;
                    break;
                case "ImagePath":
                    current.ImagePath = value;
                    break;
                case "Catalog":
                    current.Catalog = value;
                    break;
                case "Source":
                    current.SourcePath = value;
                    break;
            }
        }

        if (current is not null) items.Add(current.Build());

        return items;
    }

    private static SelectedSpriteBuilder CreateSpriteBuilder(string descriptor)
    {
        var match = Regex.Match(descriptor, @"^(?<name>.+?)\s+\[(?<usage>[^\]]+)\]$");
        return match.Success
            ? new SelectedSpriteBuilder(match.Groups["name"].Value.Trim(), match.Groups["usage"].Value.Trim())
            : new SelectedSpriteBuilder(descriptor, string.Empty);
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildSummaryFields(
        IReadOnlyDictionary<string, string> attributes,
        string actualSize,
        string actualPosition)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Id", attributes.GetValueOrDefault("Id"));
        AddField(items, "Brush", attributes.GetValueOrDefault("Brush"));

        var inputState = attributes.TryGetValue("DoNotAcceptEvents", out var doNotAcceptEvents)
                         && bool.TryParse(doNotAcceptEvents, out var isDisabled)
            ? isDisabled ? "Disabled" : "Enabled"
            : null;
        AddField(items, "Input", inputState);
        AddField(items, "Actual", actualSize);
        AddField(items, "Position", actualPosition);

        return items;
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildNodeFields(
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyDictionary<string, string> layout,
        IReadOnlyDictionary<string, string> actualLayout)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Id", attributes.GetValueOrDefault("Id"));
        AddField(items, "Brush", attributes.GetValueOrDefault("Brush"));

        var inputState = attributes.TryGetValue("DoNotAcceptEvents", out var doNotAcceptEvents)
                         && bool.TryParse(doNotAcceptEvents, out var isDisabled)
            ? isDisabled ? "Does not accept input" : "Accepts input"
            : null;
        AddField(items, "Input", inputState);
        AddField(items, "Size policy", layout.GetValueOrDefault("SizePolicy"));
        AddField(items, "Suggested size", layout.GetValueOrDefault("SuggestedSize"));
        AddField(items, "Alignment", layout.GetValueOrDefault("Alignment"));

        var marginValue = FirstNonEmpty(
            actualLayout.GetValueOrDefault("Margin"),
            layout.GetValueOrDefault("Margin"));
        if (!string.Equals(marginValue, "L=0 T=0 R=0 B=0", StringComparison.Ordinal))
            AddField(items, "Margin", marginValue);

        return items;
    }

    private static IReadOnlyList<SelectedInfoFieldViewModel> BuildLayoutFields(
        IReadOnlyDictionary<string, string> layout,
        IReadOnlyDictionary<string, string> actualLayout,
        string actualSize,
        string actualPosition)
    {
        var items = new List<SelectedInfoFieldViewModel>();

        AddField(items, "Parent",
            FirstNonEmpty(actualLayout.GetValueOrDefault("ParentSize"), layout.GetValueOrDefault("ParentSize")));
        AddField(items, "Actual size", actualSize);
        AddField(items, "Measured",
            FirstNonEmpty(actualLayout.GetValueOrDefault("MeasuredSize"), layout.GetValueOrDefault("MeasuredSize")));
        AddField(items, "Position", actualPosition);
        AddField(items, "Selection", actualLayout.GetValueOrDefault("SelectionBoundsRoot"));
        AddField(items, "Selection local", actualLayout.GetValueOrDefault("SelectionBoundsLocal"));

        return items;
    }

    private static void AddField(ICollection<SelectedInfoFieldViewModel> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) items.Add(new SelectedInfoFieldViewModel(label, value));
    }

    private static bool TrySplitKeyValue(string line, out string key, out string value)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separatorIndex].Trim();
        value = line[(separatorIndex + 1)..].Trim();
        return true;
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

    private sealed class SelectedBrushBuilder
    {
        public SelectedBrushBuilder(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string SourcePath { get; set; } = string.Empty;

        public string LayerCount { get; set; } = string.Empty;

        public List<string> Layers { get; } = [];

        public SelectedBrushInfoViewModel Build()
        {
            return new SelectedBrushInfoViewModel(
                Name,
                SourcePath,
                CompactPath(SourcePath, 4),
                LayerCount,
                Layers);
        }
    }

    private sealed class SelectedSpriteBuilder
    {
        public SelectedSpriteBuilder(string name, string usage)
        {
            Name = name;
            Usage = usage;
        }

        public string Name { get; }

        public string Usage { get; }

        public string Status { get; set; } = string.Empty;

        public string Catalog { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public SelectedSpriteInfoViewModel Build()
        {
            ParseCatalog(Catalog, out var spriteType, out var spriteSize);

            return new SelectedSpriteInfoViewModel(
                Name,
                Usage,
                Status,
                spriteType,
                spriteSize,
                ImagePath,
                CompactPath(ImagePath, 3),
                SourcePath,
                CompactPath(SourcePath, 4));
        }

        private static void ParseCatalog(string catalog, out string spriteType, out string spriteSize)
        {
            spriteType = string.Empty;
            spriteSize = string.Empty;

            if (string.IsNullOrWhiteSpace(catalog)) return;

            var match = Regex.Match(catalog, @"\((?<type>[^,]+),\s*(?<size>[0-9.]+x[0-9.]+)");
            if (!match.Success) return;

            spriteType = match.Groups["type"].Value.Trim();
            spriteSize = match.Groups["size"].Value.Trim();
        }
    }
}