namespace Bannerlord.XmlPreview.VisualRegression;

public enum ComparisonStatus
{
    NotRun,
    Passed,
    Failed
}

public sealed record ComparisonResults(
    ComparisonStatus RenderStructure,
    ComparisonStatus KeyBounds,
    ComparisonStatus Diagnostics,
    ComparisonStatus PixelDimensions,
    ComparisonStatus Pixels)
{
    public static ComparisonResults NotRun { get; } = new(
        ComparisonStatus.NotRun, ComparisonStatus.NotRun, ComparisonStatus.NotRun,
        ComparisonStatus.NotRun, ComparisonStatus.NotRun);
}

/// <summary>Metrics are absent when the corresponding comparison did not run.</summary>
public sealed record ComparisonMetrics(
    int? StructureDifferenceCount = null,
    double? MaximumBoundsDifference = null,
    byte? MaximumChannelDifference = null,
    double? DifferingPixelProportion = null,
    long? ComparedPixelCount = null,
    long? MaskedPixelCount = null);

public sealed record DiagnosticDifference(string Code, string WidgetIdOrPath, string Description);

public sealed record VisualRegressionArtifacts(
    string? ReferencePng = null,
    string? ActualPng = null,
    string? DiffPng = null,
    string? OverlayPng = null,
    string? RenderStructureJson = null,
    string? LayoutJson = null,
    string? ReportJson = null,
    string? ReportMarkdown = null);

/// <summary>Absent timings indicate phases that did not run, rather than a measured zero.</summary>
public sealed record VisualRegressionTiming(
    TimeSpan? Load = null,
    TimeSpan? Render = null,
    TimeSpan? Capture = null,
    TimeSpan? Comparison = null,
    TimeSpan? ArtifactWrite = null);
