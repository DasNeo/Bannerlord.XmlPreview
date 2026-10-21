using System.Windows;
using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record PresentedComparison(RgbImage Reference, RgbImage Actual, RgbImage Diff, RgbImage Overlay,
    bool Passed, double DifferingPixelProportion, PixelTolerance Tolerance, string Summary)
{
    public IReadOnlyList<WidgetBounds> Differences { get; init; } = [];
    public string RunMode { get; init; } = "Interactive images (structure and Key Bounds comparison NotRun)";
}

/// <summary>Interactive image comparison uses the same capture, masks and comparator as Visual Regression.</summary>
public static class ComparisonPresenter
{
    public static PresentedComparison LoadArtifacts(string reportPath, string scenarioPath)
    {
        var (scenario, hash) = ScenarioLoader.LoadWithHash(scenarioPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        var evidence = JsonSerializer.Deserialize<CaptureEvidence>(File.ReadAllText(Path.Combine(directory, "capture.json")), ScenarioLoader.JsonOptions)
            ?? throw new InvalidDataException("Capture evidence is missing.");
        if (evidence.SchemaVersion != 1 || evidence.ScenarioSha256 != hash || evidence.Reference.CaseId != scenario.Id ||
            evidence.PrefabIdentity != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) || evidence.CaptureProfile != scenario.CaptureProfile ||
            evidence.Scene != scenario.Scene || evidence.DataState != scenario.DataState)
            throw new InvalidDataException("Comparison artifacts belong to another scenario or Capture Profile.");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        if (root.GetProperty("SchemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported comparison report schema.");
        var mode = root.GetProperty("Mode").GetString();
        if (mode != "Full") throw new InvalidDataException("Structure Only has pixels NotRun and no image comparison views.");
        var artifacts = root.GetProperty("Artifacts").Deserialize<VisualRegressionArtifacts>(ScenarioLoader.JsonOptions)
            ?? throw new InvalidDataException("Comparison artifacts are missing.");
        var tolerance = root.TryGetProperty("AppliedPixelTolerance", out var toleranceJson)
            ? toleranceJson.Deserialize<PixelTolerance>(ScenarioLoader.JsonOptions) : null;
        if (tolerance is null) throw new InvalidDataException("This report has no recorded Pixel Tolerance; generate a new Full report.");
        ScenarioLoader.ValidateTolerance(tolerance);
        string Required(string? path) => path is null ? throw new InvalidDataException("Image artifact was not produced.")
            : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
        var actualPath = Required(artifacts.ActualPng);
        var layoutPath = Required(artifacts.LayoutJson);
        if (evidence.ActualSha256 is null) throw new InvalidDataException("Actual image evidence is missing.");
        ReferenceStore.VerifyHash(actualPath, evidence.ActualSha256);
        ReferenceStore.VerifyHash(layoutPath, evidence.LayoutSha256);
        var layout = JsonSerializer.Deserialize<LayoutSnapshot>(File.ReadAllText(layoutPath), SnapshotSerialization.Options);
        var result = Compare(WpfPreviewCapture.ReadPng(Required(artifacts.ReferencePng), scenario.CaptureProfile.Background),
            WpfPreviewCapture.ReadPng(actualPath, scenario.CaptureProfile.Background), scenario with { PixelToleranceOverride = tolerance }, layout);
        return result with
        {
            Diff = WpfPreviewCapture.ReadPng(Required(artifacts.DiffPng), "#000000"),
            Overlay = WpfPreviewCapture.ReadPng(Required(artifacts.OverlayPng), "#000000"), RunMode = mode,
            Summary = $"Visual Regression: {root.GetProperty("Outcome").GetString()}; {root.GetProperty("Checks").GetRawText()}"
        };
    }
    public static PresentedComparison Compare(string referencePath, FrameworkElement root, PreviewScenario scenario, LayoutSnapshot? layout = null)
    {
        ScenarioLoader.Validate(scenario);
        return Compare(WpfPreviewCapture.ReadPng(referencePath, scenario.CaptureProfile.Background),
            WpfPreviewCapture.Capture(root, scenario.CaptureProfile), scenario, layout);
    }
    public static PresentedComparison Compare(RgbImage reference, RgbImage actual, PreviewScenario scenario, LayoutSnapshot? layout = null)
    {
        ScenarioLoader.Validate(scenario);
        var tolerance = scenario.PixelToleranceOverride ?? new(0, 0, "Interactive exact comparison; uncalibrated.");
        var result = PixelComparator.Compare(reference, actual, tolerance, scenario.Masks);
        return new(reference, actual, result.Diff, result.Overlay, result.Passed, result.DifferingPixelProportion, tolerance,
            $"{(result.Passed ? "Images match" : "Images differ")}; {result.DifferingPixelProportion:P2} differing; dimensions {(result.DimensionsPassed ? "match" : "differ")}. {tolerance.CalibrationReason}")
        { Differences = layout?.KeyBounds.Where(bounds => HasDifference(bounds, result.Diff, tolerance, scenario.Masks)).ToArray() ?? [] };
    }
    private static bool HasDifference(WidgetBounds bounds, RgbImage diff, PixelTolerance tolerance, IReadOnlyList<DynamicMask> masks)
    {
        for (var y = Math.Max(0, (int)Math.Floor(bounds.Y)); y < Math.Min(diff.Height, Math.Ceiling(bounds.Y + bounds.Height)); y++)
        for (var x = Math.Max(0, (int)Math.Floor(bounds.X)); x < Math.Min(diff.Width, Math.Ceiling(bounds.X + bounds.Width)); x++)
        {
            if (masks.Any(mask => x >= mask.X && y >= mask.Y && x < mask.X + mask.Width && y < mask.Y + mask.Height)) continue;
            var index = (y * diff.Width + x) * 3;
            if (diff.Pixels[index] > tolerance.MaximumChannelDifference || diff.Pixels[index + 1] > tolerance.MaximumChannelDifference || diff.Pixels[index + 2] > tolerance.MaximumChannelDifference) return true;
        }
        return false;
    }
}
