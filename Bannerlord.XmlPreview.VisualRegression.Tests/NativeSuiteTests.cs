using Bannerlord.XmlPreview.VisualRegression.Cli;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class NativeSuiteTests
{
    [Fact]
    public async Task CliDefaultsToStructuralValidationWithoutReferenceScreenshots()
    {
        using var fixture = new RegressionFixture();
        var data = DataRoot();
        var output = Path.Combine(fixture.Root, "cli-default");
        var result = await Program.RunAsync(["compare", "--scenario", Path.Combine(data, "portable-scenarios", "standard-window.json"),
            "--metadata", Path.Combine(data, "portable-baselines"), "--corpus", Path.Combine(data, "corpus"), "--output", output]);
        Assert.Equal(0, result);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "report.json")));
        Assert.Equal("StructureOnly", report.RootElement.GetProperty("Mode").GetString());
        Assert.Equal("NotRun", report.RootElement.GetProperty("Checks").GetProperty("Pixels").GetString());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("Artifacts").GetProperty("ActualPng").ValueKind);
    }

    public static IEnumerable<object[]> Cases() => new[] { "standard-window", "standard-scrollable-panel", "hint-tooltip", "crosshair",
        "standard-dropdown", "option-item", "options", "conversation", "single-query-popup" }.Select(id => new object[] { id });

    public static IEnumerable<object[]> FullCases() => new[] { "options", "conversation", "single-query-popup" }
        .Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AllNinePortableNativeCasesUseRealRendererAndPassStructureOnly(string id)
    {
        var data = DataRoot();
        using var fixture = new RegressionFixture();
        using var capture = new BannerlordPreviewCapture(".", Path.Combine(data, "corpus"));
        var scenarioPath = Path.Combine(data, "portable-scenarios", id + ".json");
        var scenario = ScenarioLoader.Load(scenarioPath);
        IVisualRegression regression = new VisualRegressionRunner(capture.CaptureAsync, Path.Combine(data, "m2-portable-baselines"));
        var report = await regression.CompareAsync(new(scenario.PrefabIdentity, scenarioPath, VisualRegressionMode.StructureOnly,
            new(id, "v1.4.7", "v1.4.7", scenario.CaptureProfile.Id, 1), Path.Combine(fixture.Root, "native-actual")));
        Assert.True(report.Outcome == VisualRegressionOutcome.Passed,
            $"{id}: {report.Outcome}; {report.FailureReason}; {string.Join(" | ", report.DiagnosticDifferences.Select(d => d.Description))}");
        Assert.Equal(ComparisonStatus.NotRun, report.Checks.Pixels);
        Assert.Equal(0, report.ToExitCode());
    }

    [NativeFullTheory]
    [MemberData(nameof(FullCases))]
    public async Task AllThreeFullScreensPassWithConfiguredInGameReferencePackage(string id)
    {
        var data = DataRoot();
        var metadataRoot = Environment.GetEnvironmentVariable("BANNERLORD_REFERENCE_METADATA") ?? Path.Combine(data, "native-baselines");
        var manifest = JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(Path.Combine(metadataRoot, "manifest.json")), ScenarioLoader.JsonOptions)!;
        using var fixture = new RegressionFixture();
        using var capture = new BannerlordPreviewCapture(Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!);
        var scenarioPath = Path.Combine(data, "scenarios", id + ".json");
        var scenario = ScenarioLoader.Load(scenarioPath);
        IVisualRegression regression = new VisualRegressionRunner(capture.CaptureAsync, metadataRoot,
            Environment.GetEnvironmentVariable("BANNERLORD_REFERENCE_PACKAGE"));
        var report = await regression.CompareAsync(new(scenario.PrefabIdentity, scenarioPath, VisualRegressionMode.Full,
            new(id, manifest.GameVersion, manifest.ModuleVersion, scenario.CaptureProfile.Id, manifest.SchemaVersion), Path.Combine(fixture.Root, "full-actual")));
        Assert.Equal(VisualRegressionOutcome.Passed, report.Outcome);
    }

    internal static string DataRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "test-data", "visual-regression");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Visual Regression test data not found.");
    }
}

public sealed class NativeFullTheoryAttribute : TheoryAttribute
{
    public NativeFullTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("BANNERLORD_RUN_PIXEL_TESTS"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANNERLORD_ROOT")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANNERLORD_REFERENCE_PACKAGE")))
            Skip = "Optional pixel comparison requires BANNERLORD_RUN_PIXEL_TESTS=true, BANNERLORD_ROOT and BANNERLORD_REFERENCE_PACKAGE; pixels did not run.";
    }
}
