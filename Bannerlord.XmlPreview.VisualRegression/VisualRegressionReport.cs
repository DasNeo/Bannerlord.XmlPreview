using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.VisualRegression;

public enum VisualRegressionOutcome
{
    Passed,
    Failed,
    Partial,
    MissingBaseline,
    InvalidScenario,
    CaptureFailed
}

/// <summary>
/// Owns outcome calculation so callers cannot independently label incomplete checks as Passed.
/// A mismatch or missing required check takes precedence over render Partial.
/// </summary>
public sealed class VisualRegressionReport
{
    private VisualRegressionReport(
        VisualRegressionMode mode,
        VisualRegressionOutcome outcome,
        ComparisonResults checks,
        string? failureReason,
        ComparisonMetrics? metrics,
        IEnumerable<DiagnosticDifference>? diagnosticDifferences,
        VisualRegressionArtifacts? artifacts,
        VisualRegressionTiming? timing)
    {
        Mode = mode;
        Outcome = outcome;
        Checks = checks;
        FailureReason = failureReason;
        Metrics = metrics ?? new();
        DiagnosticDifferences = Array.AsReadOnly((diagnosticDifferences ?? []).ToArray());
        Artifacts = artifacts ?? new();
        Timing = timing ?? new();
    }

    public VisualRegressionMode Mode { get; }
    public int SchemaVersion => 1;
    public VisualRegressionOutcome Outcome { get; }
    public ComparisonResults Checks { get; }
    public string? FailureReason { get; }
    public ComparisonMetrics Metrics { get; }
    public IReadOnlyList<DiagnosticDifference> DiagnosticDifferences { get; }
    public VisualRegressionArtifacts Artifacts { get; }
    public VisualRegressionTiming Timing { get; }
    public IReadOnlyList<DynamicMask> Masks { get; private init; } = Array.Empty<DynamicMask>();

    internal VisualRegressionReport WithFailureReason(string reason) =>
        new(Mode, Outcome, Checks, reason, Metrics, DiagnosticDifferences, Artifacts, Timing) { Masks = Masks };

    internal VisualRegressionReport WithRunDetails(VisualRegressionArtifacts artifacts, VisualRegressionTiming timing,
        IReadOnlyList<DynamicMask> masks) => new(Mode, Outcome, Checks, FailureReason, Metrics, DiagnosticDifferences, artifacts, timing)
        { Masks = Array.AsReadOnly(masks.ToArray()) };

    public static VisualRegressionReport FromComparison(
        VisualRegressionMode mode,
        ComparisonResults checks,
        RenderCompletionStatus completionStatus,
        ComparisonMetrics? metrics = null,
        IEnumerable<DiagnosticDifference>? diagnosticDifferences = null,
        VisualRegressionArtifacts? artifacts = null,
        VisualRegressionTiming? timing = null)
    {
        ValidateChecks(mode, checks);
        if (!Enum.IsDefined(completionStatus))
            throw new ArgumentOutOfRangeException(nameof(completionStatus));

        var differences = (diagnosticDifferences ?? []).ToArray();
        var requiredChecksPassed =
            checks.RenderStructure == ComparisonStatus.Passed &&
            checks.KeyBounds == ComparisonStatus.Passed &&
            checks.Diagnostics == ComparisonStatus.Passed &&
            differences.Length == 0 &&
            (mode == VisualRegressionMode.StructureOnly ||
             (checks.PixelDimensions == ComparisonStatus.Passed && checks.Pixels == ComparisonStatus.Passed));

        var outcome = !requiredChecksPassed || completionStatus == RenderCompletionStatus.Failed
            ? VisualRegressionOutcome.Failed
            : completionStatus == RenderCompletionStatus.Partial
                ? VisualRegressionOutcome.Partial
                : VisualRegressionOutcome.Passed;

        return new(mode, outcome, checks, null, metrics, differences, artifacts, timing);
    }

    /// <summary>Creates a prerequisite failure, preserving any checks and artifacts already produced.</summary>
    public static VisualRegressionReport UnableToCompare(
        VisualRegressionMode mode,
        VisualRegressionOutcome outcome,
        string reason,
        ComparisonResults? checks = null,
        VisualRegressionArtifacts? artifacts = null,
        VisualRegressionTiming? timing = null,
        ComparisonMetrics? metrics = null,
        IEnumerable<DiagnosticDifference>? diagnosticDifferences = null)
    {
        if (outcome is not (VisualRegressionOutcome.MissingBaseline or
            VisualRegressionOutcome.InvalidScenario or VisualRegressionOutcome.CaptureFailed))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        checks ??= ComparisonResults.NotRun;
        ValidateChecks(mode, checks);
        return new(mode, outcome, checks, reason, metrics, diagnosticDifferences, artifacts, timing);
    }

    /// <summary>An expected Partial must be explicitly permitted; all other failures remain nonzero.</summary>
    public int ToExitCode(bool allowExpectedPartial = false) =>
        Outcome == VisualRegressionOutcome.Passed ||
        (allowExpectedPartial && Outcome == VisualRegressionOutcome.Partial) ? 0 : 1;

    private static void ValidateChecks(VisualRegressionMode mode, ComparisonResults checks)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(checks);
        if (!Enum.IsDefined(checks.RenderStructure) || !Enum.IsDefined(checks.KeyBounds) ||
            !Enum.IsDefined(checks.Diagnostics) || !Enum.IsDefined(checks.PixelDimensions) ||
            !Enum.IsDefined(checks.Pixels))
            throw new ArgumentException("Unknown comparison status.", nameof(checks));
        if (mode == VisualRegressionMode.StructureOnly &&
            (checks.PixelDimensions != ComparisonStatus.NotRun || checks.Pixels != ComparisonStatus.NotRun))
            throw new ArgumentException("StructureOnly requires pixel checks to be NotRun.", nameof(checks));
    }
}
