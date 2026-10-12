using Bannerlord.XmlPreview.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record CaptureEvidence(ReferenceMetadata Reference, string PrefabIdentity, CaptureProfile CaptureProfile,
    string Scene, string DataState, string ScenarioSha256, RenderCompletionStatus CompletionStatus,
    string StructureSha256, string LayoutSha256, string? ActualSha256,
    IReadOnlyList<CapturedDiagnostic> Diagnostics, VisualRegressionTiming Timing)
{
    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Owns the comparison workflow shared by tests and the CLI. Capture varies by render environment.</summary>
public sealed class VisualRegressionRunner(
    Func<PreviewScenario, VisualRegressionMode, CancellationToken, Task<CapturedPreview>> capture,
    string metadataRoot,
    string? referencePackageRoot = null) : IVisualRegression
{
    private readonly ReferenceStore _references = new(metadataRoot, referencePackageRoot);

    public async Task<VisualRegressionReport> CompareAsync(VisualRegressionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOutput(request);
        PreviewScenario scenario;
        string scenarioHash;
        try { (scenario, scenarioHash) = LoadScenario(request); }
        catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException or IOException)
        {
            return WriteReport(request, VisualRegressionReport.UnableToCompare(request.Mode, VisualRegressionOutcome.InvalidScenario, e.Message), []);
        }

        CapturedPreview actual;
        VisualRegressionArtifacts artifacts;
        try
        {
            actual = await capture(scenario, request.Mode, cancellationToken);
            ValidateCapture(actual, scenario, request);
            var written = WriteCapture(request, scenario, scenarioHash, actual);
            artifacts = written.Artifacts;
            actual = actual with { Timing = written.Timing };
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidScenarioException e)
        {
            return WriteReport(request, VisualRegressionReport.UnableToCompare(request.Mode,
                VisualRegressionOutcome.InvalidScenario, e.Message), scenario.Masks);
        }
        catch (Exception e)
        {
            return WriteReport(request, VisualRegressionReport.UnableToCompare(request.Mode,
                VisualRegressionOutcome.CaptureFailed, e.Message), scenario.Masks);
        }

        var stopwatch = Stopwatch.StartNew();
        VisualRegressionReport report;
        var differences = DiagnosticsComparator.Compare(scenario.ExpectedWarnings, actual.Diagnostics);
        var checks = ComparisonResults.NotRun;
        var metrics = new ComparisonMetrics();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = _references.Load(request.Reference);
            _references.VerifyPackage(manifest, request.Mode);
            if (manifest.CaptureProfile != scenario.CaptureProfile)
                throw new InvalidDataException("Scenario Capture Profile differs from the Reference Package.");
            if (!manifest.Cases.TryGetValue(scenario.Id, out var entry))
                throw new FileNotFoundException("Reference Package has no entry for " + scenario.Id);
            if (entry.PrefabIdentity != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) ||
                entry.ScenarioSha256 != scenarioHash ||
                entry.Scene != scenario.Scene || entry.DataState != scenario.DataState)
                throw new InvalidDataException("Scenario metadata or hash differs from accepted expectations.");
            var baseline = _references.ReadSnapshots(entry);
            var structureDifference = StructureComparator.Compare(baseline.Structure, actual.Structure);
            var layout = LayoutComparator.Compare(baseline.Layout, actual.Layout, scenario.BoundsTolerance);
            checks = new ComparisonResults(Status(structureDifference == 0), Status(layout.Passed), Status(differences.Length == 0),
                ComparisonStatus.NotRun, ComparisonStatus.NotRun);
            metrics = new ComparisonMetrics(StructureDifferenceCount: structureDifference, MaximumBoundsDifference: layout.MaximumDifference);
            if (request.Mode == VisualRegressionMode.Full)
            {
                var referencePath = _references.ReadReferencePath(entry);
                var tolerance = scenario.PixelToleranceOverride ?? manifest.GlobalPixelTolerance
                    ?? throw new InvalidDataException("Full comparison requires calibrated Pixel Tolerance metadata.");
                var reference = WpfPreviewCapture.ReadPng(referencePath, scenario.CaptureProfile.Background);
                var pixels = PixelComparator.Compare(reference, actual.Pixels!, tolerance, scenario.Masks);
                checks = checks with { PixelDimensions = Status(pixels.DimensionsPassed), Pixels = Status(pixels.Passed) };
                metrics = metrics with { MaximumChannelDifference = pixels.MaximumChannelDifference,
                    DifferingPixelProportion = pixels.DifferingPixelProportion, ComparedPixelCount = pixels.ComparedPixels, MaskedPixelCount = pixels.MaskedPixels };
                artifacts = artifacts with { ReferencePng = referencePath, DiffPng = OutputPath(request, "diff.png"), OverlayPng = OutputPath(request, "overlay.png") };
                stopwatch.Stop();
                var artifactWatch = Stopwatch.StartNew();
                WpfPreviewCapture.WritePng(artifacts.DiffPng, pixels.Diff);
                WpfPreviewCapture.WritePng(artifacts.OverlayPng, pixels.Overlay);
                actual = actual with { Timing = actual.Timing with { ArtifactWrite = (actual.Timing.ArtifactWrite ?? TimeSpan.Zero) + artifactWatch.Elapsed } };
            }
            report = VisualRegressionReport.FromComparison(request.Mode, checks, actual.CompletionStatus, metrics, differences, artifacts,
                actual.Timing with { Comparison = stopwatch.Elapsed });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            report = VisualRegressionReport.UnableToCompare(request.Mode, VisualRegressionOutcome.MissingBaseline, e.Message,
                checks, artifacts: artifacts, timing: actual.Timing with { Comparison = stopwatch.Elapsed }, metrics: metrics,
                diagnosticDifferences: differences);
        }
        catch (Exception e)
        {
            report = VisualRegressionReport.FromComparison(request.Mode, ComparisonResults.NotRun, RenderCompletionStatus.Failed,
                diagnosticDifferences: differences, artifacts: artifacts, timing: actual.Timing with { Comparison = stopwatch.Elapsed })
                .WithFailureReason(e.Message);
        }
        return WriteReport(request, report, scenario.Masks);
    }

    /// <summary>Writes review candidates only. It cannot accept or modify reference expectations.</summary>
    public async Task<CaptureEvidence> CaptureAsync(VisualRegressionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOutput(request);
        var (scenario, scenarioHash) = LoadScenario(request);
        var actual = await capture(scenario, request.Mode, cancellationToken);
        ValidateCapture(actual, scenario, request);
        WriteCapture(request, scenario, scenarioHash, actual);
        return JsonSerializer.Deserialize<CaptureEvidence>(File.ReadAllText(OutputPath(request, "capture.json")), ScenarioLoader.JsonOptions)!;
    }

    private static (PreviewScenario Scenario, string Sha256) LoadScenario(VisualRegressionRequest request)
    {
        if (!Enum.IsDefined(request.Mode)) throw new ArgumentException("Unknown run mode.");
        var (scenario, sha256) = ScenarioLoader.LoadWithHash(request.ScenarioPath);
        if (StableWidgetId.CanonicalPrefab(request.PrefabIdentity) != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) ||
            request.Reference.CaseId != scenario.Id || request.Reference.CaptureProfileId != scenario.CaptureProfile.Id ||
            request.Reference.PackageSchemaVersion != 1 || string.IsNullOrWhiteSpace(request.Reference.GameVersion) ||
            string.IsNullOrWhiteSpace(request.Reference.ModuleVersion) || string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new InvalidDataException("Request and scenario identity or reference metadata do not agree.");
        return (scenario, sha256);
    }

    private static void ValidateCapture(CapturedPreview actual, PreviewScenario scenario, VisualRegressionRequest request)
    {
        var mode = request.Mode;
        if (actual.ModuleVersion is not null && request.Reference.ModuleVersion != "unconfigured" && actual.ModuleVersion != request.Reference.ModuleVersion)
            throw new InvalidDataException("Installed module version differs from the Reference Package.");
        if (actual.Structure.SchemaVersion != 1 || actual.Layout.SchemaVersion != 1 ||
            actual.Structure.PrefabIdentity != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity))
            throw new InvalidDataException("Capture returned invalid snapshot identity or schema.");
        if (!actual.Layout.KeyBounds.Any(b => b.WidgetId == actual.Structure.Root.WidgetId))
            throw new InvalidDataException("Layout Snapshot must include root Key Bounds.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        void ValidateWidget(RenderWidgetSnapshot widget)
        {
            if (string.IsNullOrWhiteSpace(widget.WidgetId) || !identities.Add(widget.WidgetId))
                throw new InvalidDataException("Render Structure contains missing or duplicate Widget IDs.");
            foreach (var child in widget.Children) ValidateWidget(child);
        }
        ValidateWidget(actual.Structure.Root);
        if (mode == VisualRegressionMode.StructureOnly && actual.Pixels is not null)
            throw new InvalidDataException("Structure Only capture must not perform pixel work.");
        if (mode == VisualRegressionMode.Full && (actual.Pixels is null ||
            actual.Pixels.Width != scenario.CaptureProfile.Width || actual.Pixels.Height != scenario.CaptureProfile.Height))
            throw new InvalidDataException("Capture did not produce the exact Capture Profile pixel dimensions.");
        actual.Pixels?.Validate();
    }

    private static (VisualRegressionArtifacts Artifacts, VisualRegressionTiming Timing) WriteCapture(VisualRegressionRequest request, PreviewScenario scenario, string scenarioHash, CapturedPreview actual)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(request.OutputDirectory);
        var structurePath = OutputPath(request, "structure.json");
        var layoutPath = OutputPath(request, "layout.json");
        File.WriteAllText(structurePath, SnapshotSerialization.Serialize(actual.Structure));
        File.WriteAllText(layoutPath, SnapshotSerialization.Serialize(actual.Layout));
        string? pngPath = null;
        if (actual.Pixels is not null)
        {
            pngPath = OutputPath(request, "actual.png");
            WpfPreviewCapture.WritePng(pngPath, actual.Pixels);
        }
        var evidence = new CaptureEvidence(request.Reference, actual.Structure.PrefabIdentity, scenario.CaptureProfile,
            scenario.Scene, scenario.DataState, scenarioHash, actual.CompletionStatus,
            ReferenceStore.Hash(structurePath), ReferenceStore.Hash(layoutPath), pngPath is null ? null : ReferenceStore.Hash(pngPath),
            actual.Diagnostics, actual.Timing with { ArtifactWrite = stopwatch.Elapsed });
        File.WriteAllText(OutputPath(request, "capture.json"), JsonSerializer.Serialize(evidence, ScenarioLoader.JsonOptions));
        return (new(ActualPng: pngPath, RenderStructureJson: structurePath, LayoutJson: layoutPath), evidence.Timing);
    }

    private static VisualRegressionReport WriteReport(VisualRegressionRequest request, VisualRegressionReport report, IReadOnlyList<DynamicMask> masks)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(request.OutputDirectory);
        var artifacts = report.Artifacts with { ReportJson = OutputPath(request, "report.json"), ReportMarkdown = OutputPath(request, "report.md") };
        report = report.WithRunDetails(artifacts, report.Timing, masks);
        File.WriteAllText(artifacts.ReportMarkdown, Markdown(report));
        report = report.WithRunDetails(artifacts, report.Timing with
        { ArtifactWrite = (report.Timing.ArtifactWrite ?? TimeSpan.Zero) + stopwatch.Elapsed }, masks);
        File.WriteAllText(artifacts.ReportJson, JsonSerializer.Serialize(report, ScenarioLoader.JsonOptions));
        return report;
    }

    private static string Markdown(VisualRegressionReport report) =>
        $"# Visual Regression: {report.Outcome}\n\nMode: {report.Mode}\n\nMachine: {Environment.MachineName}\n\n" +
        $"Structure: {report.Checks.RenderStructure}; Key Bounds: {report.Checks.KeyBounds}; Diagnostics: {report.Checks.Diagnostics}; " +
        $"pixel dimensions: {report.Checks.PixelDimensions}; pixels: {report.Checks.Pixels}.\n\n" +
        $"{report.FailureReason}\n\nMetrics: `{JsonSerializer.Serialize(report.Metrics)}`\n\n" +
        string.Join("\n", report.DiagnosticDifferences.Select(d => $"- {d.Code} at {d.WidgetIdOrPath}: {d.Description}")) +
        "\n\nMasks (magenta in diff and overlay):\n\n" +
        (report.Masks.Count == 0 ? "None.\n" : string.Join("\n", report.Masks.Select(m => $"- {m.Name}: ({m.X}, {m.Y}, {m.Width}, {m.Height}); {m.Reason}"))) +
        $"\n\nReference: {report.Artifacts.ReferencePng ?? "Not available"}\n\nActual: {report.Artifacts.ActualPng ?? "NotRun"}\n\n" +
        $"Diff: {report.Artifacts.DiffPng ?? "NotRun"}\n\nOverlay: {report.Artifacts.OverlayPng ?? "NotRun"}\n";

    private static ComparisonStatus Status(bool passed) => passed ? ComparisonStatus.Passed : ComparisonStatus.Failed;
    private void ValidateOutput(VisualRegressionRequest request)
    {
        var output = Path.GetFullPath(request.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var root in new[] { metadataRoot, referencePackageRoot })
        {
            if (root is null) continue;
            var reference = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (output.Equals(reference, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(reference + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be outside immutable reference metadata and PNG package directories.");
        }
        if (output.Equals(Path.GetDirectoryName(Path.GetFullPath(request.ScenarioPath)), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be separate from the scenario document directory.");
    }
    private static string OutputPath(VisualRegressionRequest request, string filename) => Path.GetFullPath(Path.Combine(request.OutputDirectory, filename));
}
