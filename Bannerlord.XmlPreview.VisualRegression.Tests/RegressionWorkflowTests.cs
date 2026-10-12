using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class RegressionWorkflowTests
{
    [Fact]
    public async Task FullComparisonProducesArtifactsWithoutChangingAcceptedFiles()
    {
        using var fixture = new RegressionFixture();
        var before = File.ReadAllBytes(Path.Combine(fixture.Metadata, "manifest.json"));
        IVisualRegression regression = fixture.Runner;
        var report = await regression.CompareAsync(fixture.Request);

        Assert.Equal(VisualRegressionOutcome.Passed, report.Outcome);
        Assert.Equal(0, report.ToExitCode());
        foreach (var path in new[] { report.Artifacts.ActualPng, report.Artifacts.DiffPng, report.Artifacts.OverlayPng,
                     report.Artifacts.RenderStructureJson, report.Artifacts.LayoutJson, report.Artifacts.ReportJson, report.Artifacts.ReportMarkdown })
            Assert.True(File.Exists(path), path);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Metadata, "manifest.json")));
        Assert.Equal(fixture.ReferencePixels.Pixels, WpfPreviewCapture.ReadPng(report.Artifacts.ActualPng!, "#000000").Pixels);
    }

    [Fact]
    public async Task CaptureAndCompareRejectOutputInsideImmutableReferences()
    {
        using var fixture = new RegressionFixture();
        var before = File.ReadAllBytes(Path.Combine(fixture.Metadata, "manifest.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Runner.CompareAsync(fixture.Request with { OutputDirectory = fixture.Metadata }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Runner.CaptureAsync(fixture.Request with { OutputDirectory = fixture.Package }));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Metadata, "manifest.json")));
    }

    [Fact]
    public async Task StructureOnlyDoesNotReadOrCapturePixels()
    {
        using var fixture = new RegressionFixture();
        Directory.Move(fixture.Package, fixture.Package + "-unavailable");
        var report = await fixture.Runner.CompareAsync(fixture.Request with { Mode = VisualRegressionMode.StructureOnly });

        Assert.Equal(VisualRegressionOutcome.Passed, report.Outcome);
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.Pixels);
        Assert.Null(report.Artifacts.ActualPng);
        Assert.Null(report.Timing.Capture);
    }

    [Fact]
    public async Task MissingPackageNeverPassesFull()
    {
        using var fixture = new RegressionFixture();
        var runner = new VisualRegressionRunner(fixture.Capture, fixture.Metadata);
        var report = await runner.CompareAsync(fixture.Request);

        Assert.Equal(VisualRegressionOutcome.MissingBaseline, report.Outcome);
        Assert.Equal(1, report.ToExitCode(allowExpectedPartial: true));
        Assert.True(File.Exists(report.Artifacts.ActualPng));
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.Pixels);
    }

    [Fact]
    public async Task InvalidScenarioIsReportedBeforeCapture()
    {
        using var fixture = new RegressionFixture();
        File.WriteAllText(fixture.ScenarioPath, "{ invalid json");
        var runner = new VisualRegressionRunner((_, _, _) => throw new Exception("Must not capture"), fixture.Metadata);
        var report = await runner.CompareAsync(fixture.Request);

        Assert.Equal(VisualRegressionOutcome.InvalidScenario, report.Outcome);
        Assert.True(File.Exists(report.Artifacts.ReportJson));
    }

    [Fact]
    public async Task CaptureFailureIsStructuredAndCancellationPropagates()
    {
        using var fixture = new RegressionFixture();
        var runner = new VisualRegressionRunner((_, _, _) => throw new InvalidOperationException("Deliberate capture failure"), fixture.Metadata);
        var report = await runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.CaptureFailed, report.Outcome);
        Assert.Contains("Deliberate", report.FailureReason);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.CompareAsync(fixture.Request, cancellation.Token));
    }

    [Fact]
    public async Task WrongModuleVersionAndCaptureDimensionsCannotPass()
    {
        using var fixture = new RegressionFixture();
        fixture.Frame = fixture.Frame with { ModuleVersion = "different-module" };
        Assert.Equal(VisualRegressionOutcome.CaptureFailed, (await fixture.Runner.CompareAsync(fixture.Request)).Outcome);
        fixture.Frame = fixture.Frame with { ModuleVersion = null, Pixels = new(1, 1, [0, 0, 0]) };
        Assert.Equal(VisualRegressionOutcome.CaptureFailed, (await fixture.Runner.CompareAsync(fixture.Request)).Outcome);
    }

    [Fact]
    public async Task CaptureHashesTheScenarioBytesItActuallyLoaded()
    {
        using var fixture = new RegressionFixture();
        var originalHash = ReferenceStore.Hash(fixture.ScenarioPath);
        var runner = new VisualRegressionRunner((scenario, mode, token) =>
        {
            File.WriteAllText(fixture.ScenarioPath, JsonSerializer.Serialize(scenario with { Scene = "Changed during capture" }, ScenarioLoader.JsonOptions));
            return fixture.Capture(scenario, mode, token);
        }, fixture.Metadata, fixture.Package);
        var evidence = await runner.CaptureAsync(fixture.Request);
        Assert.Equal(originalHash, evidence.ScenarioSha256);
        Assert.NotEqual(ReferenceStore.Hash(fixture.ScenarioPath), evidence.ScenarioSha256);
        Assert.Throws<InvalidDataException>(() => BaselineAcceptance.Accept(new(fixture.ScenarioPath, fixture.Request.OutputDirectory,
            fixture.Metadata, Path.Combine(fixture.Root, "new-expectations")), _ => { }));
    }

    [Fact]
    public async Task CliReportsInvalidScenarioAndMissingBaselineWithNonzeroExitCodes()
    {
        using var fixture = new RegressionFixture();
        File.WriteAllText(fixture.ScenarioPath, "not JSON");
        var exit = await Cli.Program.RunAsync(["compare", "--scenario", fixture.ScenarioPath, "--metadata", fixture.Metadata,
            "--output", fixture.Request.OutputDirectory]);
        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Request.OutputDirectory, "report.json")));
        Assert.Equal("InvalidScenario", report.RootElement.GetProperty("Outcome").GetString());
        var data = NativeSuiteTests.DataRoot();
        var scenarioPath = Path.Combine(data, "portable-scenarios", "standard-window.json");
        exit = await Cli.Program.RunAsync(["compare", "--scenario", scenarioPath, "--metadata", Path.Combine(fixture.Root, "absent"),
            "--output", fixture.Request.OutputDirectory, "--mode", "StructureOnly", "--corpus", Path.Combine(data, "corpus")]);
        Assert.Equal(1, exit);
        using var missing = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Request.OutputDirectory, "report.json")));
        Assert.Equal("MissingBaseline", missing.RootElement.GetProperty("Outcome").GetString());
    }

    [Fact]
    public async Task LayoutChangeFailsReproducibly()
    {
        using var fixture = new RegressionFixture();
        fixture.Frame = fixture.Frame with { Layout = new(1, [fixture.Frame.Layout.KeyBounds[0] with { X = 1 }]) };
        var first = await fixture.Runner.CompareAsync(fixture.Request);
        var second = await fixture.Runner.CompareAsync(fixture.Request);

        Assert.Equal(VisualRegressionOutcome.Failed, first.Outcome);
        Assert.Equal(ComparisonStatus.Failed, first.Checks.KeyBounds);
        Assert.Equal(first.Checks, second.Checks);
        Assert.Equal(1, first.Metrics.MaximumBoundsDifference);
    }

    [Fact]
    public async Task NewWarningFailsUnlessDocumentedWithCodePathMessageAndReason()
    {
        using var fixture = new RegressionFixture();
        var warning = new CapturedDiagnostic("asset.missing", "/root/image", "Missing image.");
        fixture.Frame = fixture.Frame with { Diagnostics = [warning] };
        var failed = await fixture.Runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.Failed, failed.Outcome);
        Assert.Single(failed.DiagnosticDifferences);
        fixture.UpdateScenario(fixture.Scenario with { ExpectedWarnings = [new(warning.Code, warning.WidgetIdOrPath, warning.Message, "Synthetic expected warning.")] });
        var accepted = await fixture.Runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.Passed, accepted.Outcome);
    }

    [Fact]
    public async Task PixelDriftFailsAndNamedMaskRemainsVisible()
    {
        using var fixture = new RegressionFixture();
        fixture.Frame = fixture.Frame with { Pixels = new(2, 1, [10, 20, 30, 255, 255, 255]) };
        var failed = await fixture.Runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.Failed, failed.Outcome);
        Assert.Equal(0.5, failed.Metrics.DifferingPixelProportion);
        fixture.UpdateScenario(fixture.Scenario with { Masks = [new("clock", "Synthetic dynamic clock", 1, 0, 1, 1)] });
        var masked = await fixture.Runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.Passed, masked.Outcome);
        Assert.Equal(1, masked.Metrics.MaskedPixelCount);
        Assert.Equal(1, masked.Metrics.ComparedPixelCount);
        Assert.Single(masked.Masks);
        Assert.Contains("clock", File.ReadAllText(masked.Artifacts.ReportMarkdown!));
        Assert.Equal(new byte[] { 255, 0, 255 }, WpfPreviewCapture.ReadPng(masked.Artifacts.DiffPng!, "#000000").Pixels[3..]);
    }

    [Theory]
    [InlineData("structure.json")]
    [InlineData("layout.json")]
    [InlineData("reference.png")]
    public async Task PackageHashMismatchInvalidatesComparison(string file)
    {
        using var fixture = new RegressionFixture();
        File.AppendAllText(Path.Combine(file.EndsWith(".png") ? fixture.Package : fixture.Metadata, file), "corrupt");
        var report = await fixture.Runner.CompareAsync(fixture.Request);
        Assert.Equal(VisualRegressionOutcome.Failed, report.Outcome);
        Assert.Contains("hash mismatch", report.FailureReason);
        Assert.Equal(1, report.ToExitCode());
    }

    [Fact]
    public async Task AcceptanceIsExplicitAndCreatesNewImmutableExpectations()
    {
        using var fixture = new RegressionFixture();
        var original = File.ReadAllText(Path.Combine(fixture.Metadata, "manifest.json"));
        await fixture.Runner.CaptureAsync(fixture.Request);
        var samples = Path.Combine(fixture.Root, "samples", fixture.Scenario.Id);
        Directory.CreateDirectory(samples);
        for (var i = 0; i < 5; i++) File.Copy(Path.Combine(fixture.Package, "reference.png"), Path.Combine(samples, i + ".png"));
        var calibrationPath = Path.Combine(fixture.Root, "calibration.json");
        File.WriteAllText(calibrationPath, JsonSerializer.Serialize(ReferenceCalibration.Calibrate(Path.GetDirectoryName(samples)!), ScenarioLoader.JsonOptions));
        var destination = Path.Combine(fixture.Root, "new-metadata");
        var packageDestination = Path.Combine(fixture.Root, "new-package");
        var identified = new List<string>();
        var request = new BaselineAcceptanceRequest(fixture.ScenarioPath, fixture.Request.OutputDirectory, fixture.Metadata, destination,
            Path.Combine(samples, "0.png"), packageDestination, fixture.Package, calibrationPath);
        var manifestPath = BaselineAcceptance.Accept(request, identified.Add);
        Assert.True(File.Exists(manifestPath));
        Assert.Contains(identified, text => text.Contains("Diff:") && text.Contains("Overlay:") && text.Contains("proportion"));
        Assert.Equal(original, File.ReadAllText(Path.Combine(fixture.Metadata, "manifest.json")));
        Assert.Throws<InvalidDataException>(() => BaselineAcceptance.Accept(request, identified.Add));
        var runner = new VisualRegressionRunner(fixture.Capture, destination, packageDestination);
        Assert.Equal(VisualRegressionOutcome.Passed, (await runner.CompareAsync(fixture.Request)).Outcome);
    }

    [Fact]
    public async Task AcceptanceRejectsChangedScenarioAndTamperedCapture()
    {
        using var fixture = new RegressionFixture();
        await fixture.Runner.CaptureAsync(fixture.Request);
        var request = new BaselineAcceptanceRequest(fixture.ScenarioPath, fixture.Request.OutputDirectory, fixture.Metadata,
            Path.Combine(fixture.Root, "new-metadata"));
        File.AppendAllText(Path.Combine(fixture.Request.OutputDirectory, "layout.json"), "tamper");
        Assert.Throws<InvalidDataException>(() => BaselineAcceptance.Accept(request, _ => { }));
        Assert.False(Directory.Exists(request.DestinationMetadataDirectory));
        File.WriteAllText(fixture.ScenarioPath, JsonSerializer.Serialize(fixture.Scenario with { Scene = "changed" }, ScenarioLoader.JsonOptions));
        Assert.Throws<InvalidDataException>(() => BaselineAcceptance.Accept(request, _ => { }));
    }

    [Fact]
    public async Task PerformanceUsesFiveWarmIterationsAndReportsUnavailablePixelPhases()
    {
        using var fixture = new RegressionFixture();
        var result = await RegressionPerformance.MeasureAsync(fixture.Runner, fixture.Request with { Mode = VisualRegressionMode.StructureOnly });
        Assert.Equal(5, result.WarmIterations);
        Assert.Equal(5, result.Outcomes.Count);
        Assert.All(result.Outcomes, outcome => Assert.Equal(VisualRegressionOutcome.Passed, outcome));
        Assert.Null(result.Phases["capture"].MedianMilliseconds);
        Assert.Equal(3, RegressionPerformance.Summarize(new[] { 1, 2, 3, 4, 5 }.Select(v => (TimeSpan?)TimeSpan.FromMilliseconds(v))).MedianMilliseconds);
        Assert.Equal(5, RegressionPerformance.Summarize(new[] { 1, 2, 3, 4, 5 }.Select(v => (TimeSpan?)TimeSpan.FromMilliseconds(v))).P95Milliseconds);
    }
}

internal sealed class RegressionFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Bannerlord.VisualRegression.Tests", Guid.NewGuid().ToString("N"));
    public string Metadata => Path.Combine(Root, "metadata");
    public string Package => Path.Combine(Root, "package");
    public string ScenarioPath => Path.Combine(Root, "scenario.json");
    public PreviewScenario Scenario { get; private set; }
    public RgbImage ReferencePixels { get; } = new(2, 1, [10, 20, 30, 40, 50, 60]);
    public CapturedPreview Frame { get; set; }
    public VisualRegressionRequest Request { get; }
    public VisualRegressionRunner Runner { get; }

    public RegressionFixture()
    {
        Directory.CreateDirectory(Metadata); Directory.CreateDirectory(Package);
        const string prefab = "modules/native/test.xml";
        var id = StableWidgetId.Create(prefab, "Window");
        Frame = new(new(1, prefab, new(id, "Window", "", "Window", new Dictionary<string, string>(), [])),
            new(1, [new(id, "Window", 0, 0, 2, 1)]), [], RenderCompletionStatus.Complete, ReferencePixels,
            new(Load: TimeSpan.FromMilliseconds(1), Render: TimeSpan.FromMilliseconds(2), Capture: TimeSpan.FromMilliseconds(3)));
        Scenario = new() { Id = "synthetic", PrefabIdentity = prefab, CaptureProfile = new("synthetic", 2, 1), Scene = "Synthetic test", DataState = "Fixed two pixels" };
        File.WriteAllText(Path.Combine(Metadata, "structure.json"), SnapshotSerialization.Serialize(Frame.Structure));
        File.WriteAllText(Path.Combine(Metadata, "layout.json"), SnapshotSerialization.Serialize(Frame.Layout));
        WpfPreviewCapture.WritePng(Path.Combine(Package, "reference.png"), ReferencePixels);
        UpdateScenario(Scenario);
        Request = new(prefab, ScenarioPath, VisualRegressionMode.Full, new(Scenario.Id, "test", "test", "synthetic", 1), Path.Combine(Root, "actual"));
        Runner = new(Capture, Metadata, Package);
    }

    public Task<CapturedPreview> Capture(PreviewScenario scenario, VisualRegressionMode mode, CancellationToken token) =>
        Task.FromResult(mode == VisualRegressionMode.Full ? Frame : Frame with { Pixels = null, Timing = Frame.Timing with { Capture = null } });

    public void UpdateScenario(PreviewScenario scenario)
    {
        Scenario = scenario;
        File.WriteAllText(ScenarioPath, JsonSerializer.Serialize(scenario, ScenarioLoader.JsonOptions));
        var entry = new ReferenceEntry(scenario.PrefabIdentity, ReferenceStore.Hash(ScenarioPath), scenario.Scene, scenario.DataState,
            "structure.json", ReferenceStore.Hash(Path.Combine(Metadata, "structure.json")), "layout.json", ReferenceStore.Hash(Path.Combine(Metadata, "layout.json")),
            "reference.png", ReferenceStore.Hash(Path.Combine(Package, "reference.png")));
        var manifest = new ReferenceManifest(1, "test", "test", scenario.CaptureProfile, new(0, 0, "Synthetic exact test; not Native calibration"),
            new Dictionary<string, ReferenceEntry> { [scenario.Id] = entry });
        File.WriteAllText(Path.Combine(Metadata, "manifest.json"), JsonSerializer.Serialize(manifest, ScenarioLoader.JsonOptions));
    }

    public void Dispose()
    {
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Bannerlord.VisualRegression.Tests")) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Root);
        if (!resolved.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
