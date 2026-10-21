using System.IO;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record CalibrationCase(IReadOnlyList<string> SampleSha256, byte MaximumInternalChannelDifference, string? ScenarioSha256 = null);
public sealed record ReferenceCalibrationReport(int Width, int Height, string Background, PixelTolerance Tolerance,
    IReadOnlyDictionary<string, CalibrationCase> Cases);

public static class ReferenceCalibration
{
    /// <summary>Samples are grouped in case-id subdirectories containing exactly five captures.</summary>
    public static ReferenceCalibrationReport Calibrate(string sampleRoot, string background = "#000000", string? scenarioDirectory = null)
    {
        var cases = new SortedDictionary<string, CalibrationCase>(StringComparer.Ordinal);
        int width = 0, height = 0;
        byte maximum = 0;
        foreach (var directory in Directory.GetDirectories(sampleRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var files = Directory.GetFiles(directory, "*.png").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (files.Length != 5) throw new InvalidDataException("Calibration requires exactly five PNGs per case: " + directory);
            var images = files.Select(p => WpfPreviewCapture.ReadPng(p, background)).ToArray();
            if (width == 0) { width = images[0].Width; height = images[0].Height; }
            if (images.Any(i => i.Width != width || i.Height != height)) throw new InvalidDataException("Calibration dimensions differ.");
            IReadOnlyList<DynamicMask> masks = [];
            string? scenarioHash = null;
            if (scenarioDirectory is not null)
            {
                var loaded = ScenarioLoader.LoadWithHash(Path.Combine(scenarioDirectory, Path.GetFileName(directory) + ".json"));
                if (loaded.Scenario.CaptureProfile.Width != width || loaded.Scenario.CaptureProfile.Height != height ||
                    loaded.Scenario.CaptureProfile.Background != background)
                    throw new InvalidDataException("Calibration scenario Capture Profile mismatch.");
                masks = loaded.Scenario.Masks; scenarioHash = loaded.Sha256;
            }
            byte caseMaximum = 0;
            for (var first = 0; first < images.Length; first++)
            for (var second = first + 1; second < images.Length; second++)
            {
                var comparison = PixelComparator.Compare(images[first], images[second], new(0, 0, "Calibration measurement"), masks);
                caseMaximum = Math.Max(caseMaximum, comparison.MaximumChannelDifference);
            }
            maximum = Math.Max(maximum, caseMaximum);
            cases.Add(Path.GetFileName(directory), new(Array.AsReadOnly(files.Select(ReferenceStore.Hash).ToArray()), caseMaximum, scenarioHash));
        }
        if (cases.Count == 0) throw new InvalidDataException("Calibration has no cases.");
        if (maximum >= 254) throw new InvalidDataException("Reference captures are too unstable to select a meaningful channel threshold.");
        var tolerance = new PixelTolerance((byte)(maximum + 1), 0.0001,
            $"Five captures per case; all ten pairwise comparisons. Maximum internal channel variation {maximum}; channel threshold is variation + 1. " +
            "A pixel differs when any channel exceeds that threshold; permitted differing proportion is 0.0001. " +
            (scenarioDirectory is null ? "No masks were applied during calibration." : "Scenario masks were applied and scenario hashes are recorded."));
        return new(width, height, background, tolerance, cases);
    }
}
