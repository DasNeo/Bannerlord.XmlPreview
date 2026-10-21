using Bannerlord.XmlPreview.Models;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class VisualRegressionReportTests
{
    private static readonly ComparisonResults FullPassed = new(
        ComparisonStatus.Passed, ComparisonStatus.Passed, ComparisonStatus.Passed,
        ComparisonStatus.Passed, ComparisonStatus.Passed);

    private static readonly ComparisonResults StructurePassed = FullPassed with
    {
        PixelDimensions = ComparisonStatus.NotRun,
        Pixels = ComparisonStatus.NotRun
    };

    [Fact]
    public void FullRequiresAllChecksAndCompleteRendering()
    {
        var report = VisualRegressionReport.FromComparison(
            VisualRegressionMode.Full, FullPassed, RenderCompletionStatus.Complete);

        Assert.Equal(VisualRegressionOutcome.Passed, report.Outcome);
        Assert.Equal(0, report.ToExitCode());
    }

    [Fact]
    public void StructureOnlyPassesWithPixelsExplicitlyNotRun()
    {
        var report = VisualRegressionReport.FromComparison(
            VisualRegressionMode.StructureOnly, StructurePassed, RenderCompletionStatus.Complete);

        Assert.Equal(VisualRegressionOutcome.Passed, report.Outcome);
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.PixelDimensions);
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.Pixels);
        Assert.Null(report.Metrics.MaximumChannelDifference);
        Assert.Null(report.Timing.Capture);
    }

    public static IEnumerable<object[]> IncompleteChecks()
    {
        foreach (var status in new[] { ComparisonStatus.Failed, ComparisonStatus.NotRun })
        {
            yield return [FullPassed with { RenderStructure = status }];
            yield return [FullPassed with { KeyBounds = status }];
            yield return [FullPassed with { Diagnostics = status }];
            yield return [FullPassed with { PixelDimensions = status }];
            yield return [FullPassed with { Pixels = status }];
        }
    }

    [Theory]
    [MemberData(nameof(IncompleteChecks))]
    public void MissingOrFailedRequiredCheckCannotPassOrBecomeExpectedPartial(ComparisonResults checks)
    {
        foreach (var completion in new[] { RenderCompletionStatus.Complete, RenderCompletionStatus.Partial })
        {
            var report = VisualRegressionReport.FromComparison(VisualRegressionMode.Full, checks, completion);
            Assert.Equal(VisualRegressionOutcome.Failed, report.Outcome);
            Assert.Equal(1, report.ToExitCode(allowExpectedPartial: true));
        }
    }

    [Theory]
    [InlineData(VisualRegressionMode.Full)]
    [InlineData(VisualRegressionMode.StructureOnly)]
    public void RenderPartialIsNeverPassedAndRequiresExplicitExitCodeOptIn(VisualRegressionMode mode)
    {
        var report = VisualRegressionReport.FromComparison(
            mode, mode == VisualRegressionMode.Full ? FullPassed : StructurePassed,
            RenderCompletionStatus.Partial);

        Assert.Equal(VisualRegressionOutcome.Partial, report.Outcome);
        Assert.Equal(1, report.ToExitCode());
        Assert.Equal(0, report.ToExitCode(allowExpectedPartial: true));
    }

    [Fact]
    public void FailedRenderingOverridesPassingChecks()
    {
        var report = VisualRegressionReport.FromComparison(
            VisualRegressionMode.Full, FullPassed, RenderCompletionStatus.Failed);

        Assert.Equal(VisualRegressionOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.ToExitCode(allowExpectedPartial: true));
    }

    [Theory]
    [InlineData(VisualRegressionOutcome.MissingBaseline)]
    [InlineData(VisualRegressionOutcome.InvalidScenario)]
    [InlineData(VisualRegressionOutcome.CaptureFailed)]
    public void PrerequisiteFailuresAreNonzeroAndRetainEvidence(VisualRegressionOutcome outcome)
    {
        var artifacts = new VisualRegressionArtifacts(ActualPng: "actual.png");
        var report = VisualRegressionReport.UnableToCompare(
            VisualRegressionMode.Full, outcome, "Required input unavailable.",
            StructurePassed, artifacts);

        Assert.Equal(outcome, report.Outcome);
        Assert.Equal("Required input unavailable.", report.FailureReason);
        Assert.Equal(ComparisonStatus.Passed, report.Checks.RenderStructure);
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.Pixels);
        Assert.Equal("actual.png", report.Artifacts.ActualPng);
        Assert.Equal(1, report.ToExitCode());
        Assert.Equal(1, report.ToExitCode(allowExpectedPartial: true));
    }

    [Fact]
    public void NewDiagnosticDifferenceFailsEvenIfCheckWasIncorrectlyMarkedPassed()
    {
        var differences = new List<DiagnosticDifference>
        {
            new("missing-asset", "/Window/Image", "Unexpected asset warning.")
        };
        var report = VisualRegressionReport.FromComparison(
            VisualRegressionMode.StructureOnly, StructurePassed, RenderCompletionStatus.Complete,
            diagnosticDifferences: differences);
        differences.Clear();

        Assert.Equal(VisualRegressionOutcome.Failed, report.Outcome);
        Assert.Single(report.DiagnosticDifferences);
        Assert.Equal(1, report.ToExitCode());
    }

    [Fact]
    public void StructureOnlyRejectsClaimsThatPixelWorkRan()
    {
        Assert.Throws<ArgumentException>(() => VisualRegressionReport.FromComparison(
            VisualRegressionMode.StructureOnly, FullPassed, RenderCompletionStatus.Complete));
        Assert.Throws<ArgumentException>(() => VisualRegressionReport.UnableToCompare(
            VisualRegressionMode.StructureOnly, VisualRegressionOutcome.MissingBaseline,
            "Missing reference.", FullPassed));
    }

    [Theory]
    [InlineData(VisualRegressionOutcome.Passed)]
    [InlineData(VisualRegressionOutcome.Failed)]
    [InlineData(VisualRegressionOutcome.Partial)]
    [InlineData((VisualRegressionOutcome)99)]
    public void PrerequisiteFactoryCannotBypassOutcomeCalculation(VisualRegressionOutcome outcome)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualRegressionReport.UnableToCompare(
            VisualRegressionMode.Full, outcome, "Reason."));
    }

    [Fact]
    public void UndefinedModesAndStatusesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualRegressionReport.FromComparison(
            (VisualRegressionMode)99, FullPassed, RenderCompletionStatus.Complete));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualRegressionReport.FromComparison(
            VisualRegressionMode.Full, FullPassed, (RenderCompletionStatus)99));
        Assert.Throws<ArgumentException>(() => VisualRegressionReport.FromComparison(
            VisualRegressionMode.Full, FullPassed with { Pixels = (ComparisonStatus)99 },
            RenderCompletionStatus.Complete));
        Assert.Throws<ArgumentException>(() => VisualRegressionReport.UnableToCompare(
            VisualRegressionMode.Full, VisualRegressionOutcome.CaptureFailed, " "));
    }
}
