using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record PhasePerformance(double? MedianMilliseconds, double? P95Milliseconds);
public sealed record RegressionPerformanceReport(string Machine, string OperatingSystem, string Runtime, CaptureProfile CaptureProfile,
    int WarmIterations, IReadOnlyList<VisualRegressionOutcome> Outcomes, IReadOnlyDictionary<string, PhasePerformance> Phases);

public static class RegressionPerformance
{
    public static async Task<RegressionPerformanceReport> MeasureAsync(IVisualRegression regression, VisualRegressionRequest request,
        CancellationToken cancellationToken = default)
    {
        var scenario = ScenarioLoader.Load(request.ScenarioPath);
        await regression.CompareAsync(request with { OutputDirectory = Path.Combine(request.OutputDirectory, "warmup") }, cancellationToken);
        var reports = new List<VisualRegressionReport>();
        for (var iteration = 0; iteration < 5; iteration++)
            reports.Add(await regression.CompareAsync(request with { OutputDirectory = Path.Combine(request.OutputDirectory, "iteration-" + (iteration + 1)) }, cancellationToken));
        var phases = new SortedDictionary<string, PhasePerformance>(StringComparer.Ordinal)
        {
            ["load"] = Summarize(reports.Select(r => r.Timing.Load)),
            ["render"] = Summarize(reports.Select(r => r.Timing.Render)),
            ["capture"] = Summarize(reports.Select(r => r.Timing.Capture)),
            ["comparison"] = Summarize(reports.Select(r => r.Timing.Comparison)),
            ["artifactWrite"] = Summarize(reports.Select(r => r.Timing.ArtifactWrite))
        };
        var result = new RegressionPerformanceReport(Environment.MachineName, RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription, scenario.CaptureProfile, 5, Array.AsReadOnly(reports.Select(r => r.Outcome).ToArray()), phases);
        Directory.CreateDirectory(request.OutputDirectory);
        File.WriteAllText(Path.Combine(request.OutputDirectory, "performance.json"), JsonSerializer.Serialize(result, ScenarioLoader.JsonOptions));
        return result;
    }

    internal static PhasePerformance Summarize(IEnumerable<TimeSpan?> values)
    {
        var samples = values.ToArray();
        if (samples.Any(v => v is null)) return new(null, null);
        var sorted = samples.Select(v => v!.Value.TotalMilliseconds).Order().ToArray();
        if (sorted.Length == 0) return new(null, null);
        var midpoint = sorted.Length / 2;
        return new(sorted.Length % 2 == 0 ? (sorted[midpoint - 1] + sorted[midpoint]) / 2 : sorted[midpoint],
            sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1]);
    }
}
