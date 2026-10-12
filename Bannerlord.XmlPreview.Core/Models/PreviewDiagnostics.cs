using Bannerlord.XmlPreview.Models.Assets;
using System.Globalization;
using System.Diagnostics;
using System.Threading;

namespace Bannerlord.XmlPreview.Models;

public sealed class PreviewDiagnostics
{
    private static readonly AsyncLocal<PreviewDiagnostics?> AmbientDiagnostics = new();
    private readonly List<string> _messages = [];
    private readonly Dictionary<string, string> _resolvedConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedBrushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _nodeTypeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resolvedWidgetReferenceSamples = [];
    private readonly HashSet<string> _resolvedWidgetReferenceSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resolvedWidgetPropertySamples = [];
    private readonly HashSet<string> _resolvedWidgetPropertySet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _measureFallbackSamples = [];
    private readonly HashSet<string> _measureFallbackSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ElementLayoutSnapshot> _elementLayoutSnapshots = [];
    private readonly Dictionary<string, TimingAggregate> _timings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _truncationReasons = [];
    private string? _sourcePath;
    private string? _rootNodeName;
    private int _bindingCount;
    private int _constantCount;
    private int _nodeCount;
    private int _maxDepth;
    private int _resolvedWidgetReferenceCount;
    private int _unresolvedWidgetReferenceCount;
    private int _supportedWidgetPropertyCount;
    private int _unsupportedWidgetPropertyCount;
    private int _measureFallbackCount;
    private string? _requestedRootSize;
    private string? _measuredRootSize;
    private string? _actualRootSize;

    public RenderCompletionStatus CompletionStatus { get; private set; } = RenderCompletionStatus.Complete;

    public IReadOnlyList<string> TruncationReasons => _truncationReasons;

    public IReadOnlyList<string> Messages => _messages;

    public Bannerlord.XmlPreview.Core.Behaviors.BehaviorGraphSummary? BehaviorGraph { get; internal set; }
    public System.Collections.Immutable.ImmutableArray<Bannerlord.XmlPreview.Core.Behaviors.BehaviorTrace> BehaviorTrace { get; internal set; } = [];
    public RenderStructureSnapshot? RenderStructure { get; internal set; }
    public LayoutSnapshot? Layout { get; internal set; }

    public IDisposable BeginTimingScope()
    {
        return new DiagnosticsTimingContext(this);
    }

    public IDisposable MeasurePhase(string phaseName)
    {
        return new TimingScope(this, phaseName);
    }

    public static IDisposable MeasureCurrentPhase(string phaseName)
    {
        return new TimingScope(AmbientDiagnostics.Value, phaseName);
    }

    public void Add(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _messages.Add(message);
        }
    }

    public string ToDisplayText()
    {
        var sections = new List<string>
        {
            "Completion",
            $"CompletionStatus: {CompletionStatus}",
            $"TruncationReason: {(_truncationReasons.Count == 0 ? "None" : string.Join(" | ", _truncationReasons))}"
        };

        if (BehaviorGraph is { } graph)
        {
            sections.Add($"Behavior Graph: rules={graph.RuleCount}, matchedRules={graph.MatchedRuleCount}, matches={graph.MatchCount}, edges={graph.EdgeCount}, total={graph.TotalMilliseconds:0.###}ms, matching={graph.MatchingMilliseconds:0.###}ms, sorting={graph.SortingMilliseconds:0.###}ms");
            sections.Add("Contributions: " + string.Join(", ", graph.ContributionCounts.Select(p => $"{p.Key}={p.Value}")));
            sections.Add("Support: " + string.Join(", ", graph.SupportCounts.Select(p => $"{p.Key}={p.Value}")));
        }
        AddSectionSeparator(sections);

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

        if (_supportedWidgetPropertyCount > 0 || _unsupportedWidgetPropertyCount > 0)
        {
            AddSectionSeparator(sections);
            sections.Add("WidgetProperties");
            sections.Add($"Supported: {_supportedWidgetPropertyCount}");
            sections.Add($"Unsupported: {_unsupportedWidgetPropertyCount}");
            foreach (var sample in _resolvedWidgetPropertySamples.Take(8))
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

            if (!string.IsNullOrWhiteSpace(_actualRootSize))
            {
                sections.Add($"RootActual: {_actualRootSize}");
            }

            sections.Add($"MeasureFallbacks: {_measureFallbackCount}");
            foreach (var sample in _measureFallbackSamples.Take(6))
            {
                sections.Add(sample);
            }
        }

        if (_elementLayoutSnapshots.Count > 0)
        {
            AddSectionSeparator(sections);
            sections.Add("ElementLayout");
            sections.Add($"Captured: {_elementLayoutSnapshots.Count}");
            for (var index = 0; index < _elementLayoutSnapshots.Count; index++)
            {
                sections.Add(_elementLayoutSnapshots[index].ToString());
                if (index < _elementLayoutSnapshots.Count - 1)
                {
                    sections.Add(string.Empty);
                }
            }
        }

        if (_timings.Count > 0)
        {
            AddSectionSeparator(sections);
            sections.Add("Timings");
            foreach (var entry in _timings
                         .OrderByDescending(entry => entry.Value.TotalMs)
                         .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                sections.Add(
                    $"{entry.Key}: {FormatNumber(entry.Value.TotalMs)} ms total " +
                    $"({entry.Value.Count}x, avg {FormatNumber(entry.Value.AverageMs)} ms, max {FormatNumber(entry.Value.MaxMs)} ms)");
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

    public void MarkTruncated(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A truncation reason is required.", nameof(reason));

        if (CompletionStatus != RenderCompletionStatus.Failed)
            CompletionStatus = RenderCompletionStatus.Partial;

        if (!_truncationReasons.Contains(reason, StringComparer.Ordinal))
            _truncationReasons.Add(reason);
    }

    public void MarkFailed(string reason)
    {
        CompletionStatus = RenderCompletionStatus.Failed;
        AddOnce(reason);
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

    public void SetRenderSummary(
        double requestedWidth,
        double requestedHeight,
        double measuredWidth,
        double measuredHeight,
        double actualWidth,
        double actualHeight)
    {
        _requestedRootSize = FormatSize(requestedWidth, requestedHeight);
        _measuredRootSize = FormatSize(measuredWidth, measuredHeight);
        _actualRootSize = FormatSize(actualWidth, actualHeight);
    }

    public void SetElementLayoutSnapshots(IEnumerable<ElementLayoutSnapshot> snapshots)
    {
        _elementLayoutSnapshots.Clear();
        _elementLayoutSnapshots.AddRange(snapshots.Where(snapshot => snapshot is not null));
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

    public void AddResolvedWidgetProperty(
        string widgetPath,
        string attributeName,
        string rawValue,
        string declaredTypeName,
        ResolvedWidgetPropertyValueKind valueKind,
        bool isSupported)
    {
        if (isSupported)
        {
            _supportedWidgetPropertyCount++;
        }
        else
        {
            _unsupportedWidgetPropertyCount++;
        }

        var sample = $"{widgetPath} :: {attributeName} [{declaredTypeName} / {valueKind}] = {rawValue}";
        if (_resolvedWidgetPropertySet.Add(sample))
        {
            _resolvedWidgetPropertySamples.Add(sample);
        }
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
        if (_resolvedBrushes.ContainsKey(requestedName))
        {
            return;
        }

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
        if (_resolvedSprites.ContainsKey(requestedName))
        {
            return;
        }

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

    private void RecordTiming(string phaseName, long elapsedTicks)
    {
        if (string.IsNullOrWhiteSpace(phaseName))
        {
            return;
        }

        var elapsedMs = elapsedTicks * 1000d / Stopwatch.Frequency;
        if (_timings.TryGetValue(phaseName, out var aggregate))
        {
            _timings[phaseName] = aggregate.Add(elapsedMs);
        }
        else
        {
            _timings[phaseName] = new TimingAggregate(1, elapsedMs, elapsedMs);
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

    private sealed class DiagnosticsTimingContext : IDisposable
    {
        private readonly PreviewDiagnostics? _previousDiagnostics;

        public DiagnosticsTimingContext(PreviewDiagnostics diagnostics)
        {
            _previousDiagnostics = AmbientDiagnostics.Value;
            AmbientDiagnostics.Value = diagnostics;
        }

        public void Dispose()
        {
            AmbientDiagnostics.Value = _previousDiagnostics;
        }
    }

    private sealed class TimingScope : IDisposable
    {
        private readonly PreviewDiagnostics? _diagnostics;
        private readonly string? _phaseName;
        private readonly long _startedAt;

        public TimingScope(PreviewDiagnostics? diagnostics, string? phaseName)
        {
            _diagnostics = diagnostics;
            _phaseName = phaseName;
            _startedAt = diagnostics is null || string.IsNullOrWhiteSpace(phaseName)
                ? 0
                : Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_diagnostics is null || string.IsNullOrWhiteSpace(_phaseName) || _startedAt == 0)
            {
                return;
            }

            _diagnostics.RecordTiming(_phaseName, Stopwatch.GetTimestamp() - _startedAt);
        }
    }

    private readonly record struct TimingAggregate(
        int Count,
        double TotalMs,
        double MaxMs)
    {
        public double AverageMs => Count > 0 ? TotalMs / Count : 0d;

        public TimingAggregate Add(double elapsedMs)
        {
            return new TimingAggregate(Count + 1, TotalMs + elapsedMs, Math.Max(MaxMs, elapsedMs));
        }
    }
}
