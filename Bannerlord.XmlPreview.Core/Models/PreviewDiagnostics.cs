using Bannerlord.XmlPreview.Models.Assets;
using System.Globalization;

namespace Bannerlord.XmlPreview.Models;

public sealed class PreviewDiagnostics
{
    private readonly List<string> _messages = [];
    private readonly Dictionary<string, string> _resolvedConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedBrushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _nodeTypeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resolvedWidgetReferenceSamples = [];
    private readonly HashSet<string> _resolvedWidgetReferenceSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _measureFallbackSamples = [];
    private readonly HashSet<string> _measureFallbackSet = new(StringComparer.OrdinalIgnoreCase);
    private string? _sourcePath;
    private string? _rootNodeName;
    private int _bindingCount;
    private int _constantCount;
    private int _nodeCount;
    private int _maxDepth;
    private int _resolvedWidgetReferenceCount;
    private int _unresolvedWidgetReferenceCount;
    private int _measureFallbackCount;
    private string? _requestedRootSize;
    private string? _measuredRootSize;

    public IReadOnlyList<string> Messages => _messages;

    public void Add(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _messages.Add(message);
        }
    }

    public string ToDisplayText()
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(_sourcePath))
        {
            sections.Add("Summary");
            sections.Add($"Source: {_sourcePath}");
            if (!string.IsNullOrWhiteSpace(_rootNodeName))
            {
                sections.Add($"RootNode: {_rootNodeName}");
            }

            sections.Add($"Nodes: {_nodeCount}");
            sections.Add($"MaxDepth: {_maxDepth}");
            sections.Add($"Constants: {_constantCount}");
            sections.Add($"Bindings: {_bindingCount}");

            if (_nodeTypeCounts.Count > 0)
            {
                sections.Add(
                    "TopNodeTypes: " +
                    string.Join(
                        ", ",
                        _nodeTypeCounts
                            .OrderByDescending(entry => entry.Value)
                            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .Select(entry => $"{entry.Key}={entry.Value}")));
            }
        }

        if (_resolvedWidgetReferenceCount > 0 || _unresolvedWidgetReferenceCount > 0)
        {
            AddSectionSeparator(sections);
            sections.Add("WidgetReferences");
            sections.Add($"Resolved: {_resolvedWidgetReferenceCount}");
            sections.Add($"Unresolved: {_unresolvedWidgetReferenceCount}");
            foreach (var sample in _resolvedWidgetReferenceSamples.Take(8))
            {
                sections.Add(sample);
            }
        }

        if (!string.IsNullOrWhiteSpace(_requestedRootSize) || _measureFallbackCount > 0)
        {
            AddSectionSeparator(sections);
            sections.Add("Layout");
            if (!string.IsNullOrWhiteSpace(_requestedRootSize))
            {
                sections.Add($"RootHint: {_requestedRootSize}");
            }

            if (!string.IsNullOrWhiteSpace(_measuredRootSize))
            {
                sections.Add($"RootMeasured: {_measuredRootSize}");
            }

            sections.Add($"MeasureFallbacks: {_measureFallbackCount}");
            foreach (var sample in _measureFallbackSamples.Take(6))
            {
                sections.Add(sample);
            }
        }

        AddSectionSeparator(sections);
        sections.Add("Warnings");
        if (_messages.Count == 0)
        {
            sections.Add("None. The preview used built-in placeholder assets and mock bindings.");
        }
        else
        {
            sections.AddRange(_messages);
        }

        return string.Join(Environment.NewLine, sections);
    }

    public void SetDocumentSummary(
        string sourcePath,
        string rootNodeName,
        int constantCount,
        int bindingCount,
        int nodeCount,
        int maxDepth,
        IReadOnlyDictionary<string, int> nodeTypeCounts)
    {
        _sourcePath = sourcePath;
        _rootNodeName = rootNodeName;
        _constantCount = constantCount;
        _bindingCount = bindingCount;
        _nodeCount = nodeCount;
        _maxDepth = maxDepth;

        _nodeTypeCounts.Clear();
        foreach (var entry in nodeTypeCounts)
        {
            _nodeTypeCounts[entry.Key] = entry.Value;
        }
    }

    public void SetRenderSummary(double requestedWidth, double requestedHeight, double measuredWidth, double measuredHeight)
    {
        _requestedRootSize = FormatSize(requestedWidth, requestedHeight);
        _measuredRootSize = FormatSize(measuredWidth, measuredHeight);
    }

    public void AddResolvedWidgetReference(string originPath, string attributeName, string rawValue, string targetPath)
    {
        _resolvedWidgetReferenceCount++;

        var sample = $"{originPath} :: {attributeName}=\"{rawValue}\" -> {targetPath}";
        if (_resolvedWidgetReferenceSet.Add(sample))
        {
            _resolvedWidgetReferenceSamples.Add(sample);
        }
    }

    public void AddUnresolvedWidgetReference()
    {
        _unresolvedWidgetReferenceCount++;
    }

    public void AddMeasureFallback(string elementType, double parentWidth, double parentHeight)
    {
        _measureFallbackCount++;

        var sample = $"{elementType} fallback within {FormatSize(parentWidth, parentHeight)}";
        if (_measureFallbackSet.Add(sample))
        {
            _measureFallbackSamples.Add(sample);
        }
    }

    public void AddResolvedConstant(string constantName, string brushName, string brushLayerName, string valueType, double value)
    {
        _resolvedConstants[constantName] =
            $"{valueType} = {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} via {brushName}/{brushLayerName}";
    }

    public void AddResolvedBrush(string requestedName, BrushDefinition brush)
    {
        var metadata = new List<string>();
        if (!string.IsNullOrWhiteSpace(brush.FontName))
        {
            metadata.Add($"Font: {brush.FontName}");
        }

        var defaultStyle = brush.Styles.TryGetValue("Default", out var style)
            ? style
            : brush.Styles.Values.FirstOrDefault();
        if (defaultStyle is not null)
        {
            if (!string.IsNullOrWhiteSpace(defaultStyle.FontColor))
            {
                metadata.Add($"FontColor: {defaultStyle.FontColor}");
            }

            if (defaultStyle.FontSize.HasValue)
            {
                metadata.Add($"FontSize: {defaultStyle.FontSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }

        var layerNames = brush.Layers
            .Where(layer => !string.IsNullOrWhiteSpace(layer.SpriteName) || !string.IsNullOrWhiteSpace(layer.Color))
            .Select(layer =>
            {
                var segments = new List<string> { layer.Name };
                if (!string.IsNullOrWhiteSpace(layer.SpriteName))
                {
                    segments.Add($"Sprite={layer.SpriteName}");
                }

                if (!string.IsNullOrWhiteSpace(layer.Color))
                {
                    segments.Add($"Color={layer.Color}");
                }

                return string.Join(" ", segments);
            })
            .ToArray();

        var lines = new List<string>();
        lines.AddRange(metadata);
        if (layerNames.Length > 0)
        {
            lines.AddRange(layerNames);
        }

        lines.Add(brush.SourcePath);
        _resolvedBrushes[requestedName] = string.Join(Environment.NewLine, lines);
    }

    public void AddResolvedSprite(string requestedName, SpriteAssetResolution resolution)
    {
        var lines = new List<string> { $"Status: {resolution.Kind}" };
        if (resolution.SpriteDefinition is not null)
        {
            var sprite = resolution.SpriteDefinition;
            lines.Add(
                $"Catalog: {sprite.Name} [{sprite.Kind}] {sprite.Width}x{sprite.Height} in {sprite.CategoryName} sheet {sprite.SheetId}");
            lines.Add($"SpritePart: {sprite.SpritePartName}");
            lines.Add($"SpriteSource: {sprite.SourcePath}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.MatchedKey))
        {
            lines.Add($"ImageKey: {resolution.MatchedKey}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.ImagePath))
        {
            lines.Add($"ImagePath: {resolution.ImagePath}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.Note))
        {
            lines.Add($"Note: {resolution.Note}");
        }

        _resolvedSprites[requestedName] = string.Join(Environment.NewLine, lines);

        if (resolution.Kind == SpriteAssetResolutionKind.Missing)
        {
            AddOnce($"Sprite '{requestedName}' is missing. {resolution.Note ?? "No extracted PNG matched the request."}");
        }
        else if (resolution.UsesFallback)
        {
            AddOnce($"Sprite '{requestedName}' used fallback '{resolution.MatchedKey}'.");
        }
    }

    private void AddOnce(string message)
    {
        if (_reportedMessages.Add(message))
        {
            _messages.Add(message);
        }
    }

    public string ToAssetDebugText()
    {
        var sections = new List<string>();

        if (_resolvedConstants.Count > 0)
        {
            sections.Add("Constants");
            sections.AddRange(_resolvedConstants
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}: {entry.Value}"));
        }

        if (_resolvedBrushes.Count > 0)
        {
            if (sections.Count > 0)
            {
                sections.Add(string.Empty);
            }

            sections.Add("Brushes");
            sections.AddRange(_resolvedBrushes
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}:{Environment.NewLine}{entry.Value}"));
        }

        if (_resolvedSprites.Count > 0)
        {
            if (sections.Count > 0)
            {
                sections.Add(string.Empty);
            }

            sections.Add("Sprites");
            sections.AddRange(_resolvedSprites
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}:{Environment.NewLine}{entry.Value}"));
        }

        return sections.Count == 0
            ? "No Bannerlord asset metadata has been resolved yet."
            : string.Join(Environment.NewLine, sections);
    }

    private static void AddSectionSeparator(ICollection<string> sections)
    {
        if (sections.Count > 0)
        {
            sections.Add(string.Empty);
        }
    }

    private static string FormatSize(double width, double height)
    {
        return $"{FormatNumber(width)} x {FormatNumber(height)}";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
