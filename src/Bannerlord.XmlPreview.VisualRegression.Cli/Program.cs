using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression.Cli;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { return RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "help")
        {
            Console.WriteLine("Visual Regression: compare | capture | accept | calibrate | benchmark\n" +
                "compare/capture/benchmark: --scenario <json> --metadata <directory> --output <new run directory> " +
                "--bannerlord-root <installation> [--package <reference PNG root>] [--mode Full|StructureOnly]\n" +
                "accept: --scenario <json> --candidate <capture directory> --metadata <source directory> " +
                "--destination <new metadata directory> [--reference <in-game PNG> --package-destination <new PNG root> --calibration <json>]\n" +
                "Portable Structure Only: --corpus <archived corpus directory> instead of --bannerlord-root.\n" +
                "calibrate: --samples <directory containing five PNGs per case> --output <calibration.json>\n" +
                "StructureOnly is the default validation mode. Comparison never accepts a baseline.\n" +
                "Full is optional and requires a configured, calibrated Reference Package for comparison.");
            return 0;
        }
        var options = ParseOptions(args.Skip(1).ToArray());
        string Require(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing " + name);
        if (args[0] == "calibrate")
        {
            var result = ReferenceCalibration.Calibrate(Require("--samples"), scenarioDirectory: options.GetValueOrDefault("--scenarios"));
            File.WriteAllText(Require("--output"), JsonSerializer.Serialize(result, ScenarioLoader.JsonOptions));
            Console.WriteLine(JsonSerializer.Serialize(result, ScenarioLoader.JsonOptions));
            return 0;
        }
        if (args[0] == "accept")
        {
            var path = BaselineAcceptance.Accept(new(Require("--scenario"), Require("--candidate"), Require("--metadata"),
                Require("--destination"), options.GetValueOrDefault("--reference"), options.GetValueOrDefault("--package-destination"),
                options.GetValueOrDefault("--package"), options.GetValueOrDefault("--calibration")), Console.WriteLine);
            Console.WriteLine("Accepted immutable manifest: " + path);
            return 0;
        }
        if (args[0] is not ("compare" or "capture" or "benchmark")) throw new ArgumentException("Unknown command.");
        var scenarioPath = Require("--scenario");
        var mode = options.TryGetValue("--mode", out var modeText)
            ? Enum.Parse<VisualRegressionMode>(modeText, ignoreCase: false) : VisualRegressionMode.StructureOnly;
        var metadataRoot = Require("--metadata");
        PreviewScenario scenario;
        try { scenario = ScenarioLoader.Load(scenarioPath); }
        catch (Exception e) when (args[0] == "compare" && e is System.Text.Json.JsonException or InvalidDataException or ArgumentException or IOException)
        {
            var invalidRunner = new VisualRegressionRunner((_, _, _) => throw new InvalidOperationException("Invalid scenarios must not capture."), metadataRoot);
            var invalidReport = await invalidRunner.CompareAsync(new("invalid/scenario.xml", scenarioPath, mode,
                new("invalid", "unconfigured", "unconfigured", "invalid", 1), Require("--output")));
            Console.WriteLine($"{invalidReport.Outcome}; {invalidReport.Artifacts.ReportMarkdown}");
            return invalidReport.ToExitCode();
        }
        var manifestPath = Path.Combine(metadataRoot, "manifest.json");
        ReferenceManifest? manifest = null;
        try
        {
            if (File.Exists(manifestPath)) manifest = JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(manifestPath), ScenarioLoader.JsonOptions);
        }
        catch (JsonException) when (args[0] == "compare") { }
        var metadata = new ReferenceMetadata(scenario.Id,
            manifest?.GameVersion ?? options.GetValueOrDefault("--game-version", "unconfigured"),
            manifest?.ModuleVersion ?? options.GetValueOrDefault("--module-version", "unconfigured"),
            scenario.CaptureProfile.Id, 1);
        if (args[0] == "capture" && (metadata.GameVersion == "unconfigured" || metadata.ModuleVersion == "unconfigured"))
            throw new InvalidDataException("Capture requires game and module versions, from a manifest or explicit --game-version and --module-version.");
        using var adapter = new BannerlordPreviewCapture(options.GetValueOrDefault("--bannerlord-root", "."),
            options.GetValueOrDefault("--corpus"), options.GetValueOrDefault("--archive-fixtures"));
        if (!options.ContainsKey("--bannerlord-root") && !options.ContainsKey("--corpus")) throw new ArgumentException("Supply --bannerlord-root or --corpus.");
        var runner = new VisualRegressionRunner(adapter.CaptureAsync, metadataRoot, options.GetValueOrDefault("--package"));
        var request = new VisualRegressionRequest(scenario.PrefabIdentity, scenarioPath, mode, metadata, Require("--output"));
        if (args[0] == "capture")
        {
            var evidence = await runner.CaptureAsync(request);
            Console.WriteLine($"Captured {evidence.PrefabIdentity}: {evidence.CompletionStatus}; {Path.GetFullPath(request.OutputDirectory)}");
            return evidence.CompletionStatus == Models.RenderCompletionStatus.Complete ? 0 : 1;
        }
        if (args[0] == "benchmark")
        {
            var performance = await RegressionPerformance.MeasureAsync(runner, request);
            Console.WriteLine(JsonSerializer.Serialize(performance, ScenarioLoader.JsonOptions));
            return performance.Outcomes.All(o => o == VisualRegressionOutcome.Passed) ? 0 : 1;
        }
        var report = await runner.CompareAsync(request);
        Console.WriteLine($"{report.Outcome} ({report.Mode}); pixels: {report.Checks.Pixels}; {report.Artifacts.ReportMarkdown}");
        return report.ToExitCode(options.ContainsKey("--allow-expected-partial"));
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal) { "--scenario", "--metadata", "--output", "--bannerlord-root", "--package",
            "--mode", "--game-version", "--module-version", "--candidate", "--destination", "--reference", "--package-destination", "--samples", "--scenarios", "--calibration", "--corpus", "--archive-fixtures", "--allow-expected-partial" };
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!supported.Contains(key)) throw new ArgumentException("Unknown option: " + key);
            var value = key == "--allow-expected-partial" ? "true" :
                ++i < args.Length && !args[i].StartsWith("--") ? args[i] : throw new ArgumentException("Missing value for " + key);
            if (!options.TryAdd(key, value)) throw new ArgumentException("Duplicate option: " + key);
        }
        return options;
    }
}
