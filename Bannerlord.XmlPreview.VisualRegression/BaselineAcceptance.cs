using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record BaselineAcceptanceRequest(string ScenarioPath, string CandidateDirectory, string SourceMetadataDirectory,
    string DestinationMetadataDirectory, string? InGameReferencePng = null, string? DestinationPackageDirectory = null,
    string? SourcePackageDirectory = null, string? CalibrationPath = null);

public static class BaselineAcceptance
{
    public static string Accept(BaselineAcceptanceRequest request, Action<string> identifyEvidence)
    {
        ArgumentNullException.ThrowIfNull(identifyEvidence);
        var (scenario, scenarioHash) = ScenarioLoader.LoadWithHash(request.ScenarioPath);
        var evidence = JsonSerializer.Deserialize<CaptureEvidence>(File.ReadAllText(Path.Combine(request.CandidateDirectory, "capture.json")), ScenarioLoader.JsonOptions)
            ?? throw new InvalidDataException("Capture evidence is required.");
        if (evidence.SchemaVersion != 1 || evidence.Reference.CaseId != scenario.Id || evidence.ScenarioSha256 != scenarioHash ||
            evidence.PrefabIdentity != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) || evidence.CaptureProfile != scenario.CaptureProfile ||
            evidence.Scene != scenario.Scene || evidence.DataState != scenario.DataState ||
            evidence.CompletionStatus != RenderCompletionStatus.Complete ||
            DiagnosticsComparator.Compare(scenario.ExpectedWarnings, evidence.Diagnostics).Length != 0)
            throw new InvalidDataException("Capture must be complete and match valid scenario metadata and Expected Warnings.");
        var structurePath = Path.Combine(request.CandidateDirectory, "structure.json");
        var layoutPath = Path.Combine(request.CandidateDirectory, "layout.json");
        ReferenceStore.VerifyHash(structurePath, evidence.StructureSha256);
        ReferenceStore.VerifyHash(layoutPath, evidence.LayoutSha256);
        var sourceManifestPath = Path.Combine(request.SourceMetadataDirectory, "manifest.json");
        var source = File.Exists(sourceManifestPath)
            ? JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(sourceManifestPath), ScenarioLoader.JsonOptions)! : null;
        if (source is not null && (source.SchemaVersion != 1 || source.GameVersion != evidence.Reference.GameVersion ||
            source.ModuleVersion != evidence.Reference.ModuleVersion || source.CaptureProfile != scenario.CaptureProfile))
            throw new InvalidDataException("Cannot merge expectations across package versions or Capture Profiles.");
        var destination = Path.GetFullPath(request.DestinationMetadataDirectory);
        RequireNewDirectory(destination);
        foreach (var sourceRoot in new[] { request.SourceMetadataDirectory, request.CandidateDirectory, request.SourcePackageDirectory })
            if (sourceRoot is not null && destination.StartsWith(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Acceptance cannot create a destination inside an immutable source or capture.");
        string? packageDestination = request.DestinationPackageDirectory is null ? null : Path.GetFullPath(request.DestinationPackageDirectory);
        if (packageDestination is not null)
        {
            RequireNewDirectory(packageDestination);
            foreach (var sourceRoot in new[] { request.SourceMetadataDirectory, request.CandidateDirectory, request.SourcePackageDirectory })
                if (sourceRoot is not null && packageDestination.StartsWith(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Acceptance cannot create a package inside an immutable source or capture.");
            if (InsideEither(destination, packageDestination)) throw new InvalidDataException("Metadata and PNG destinations must be separate.");
        }
        var tolerance = source?.GlobalPixelTolerance;
        string? referenceName = null, referenceHash = null;
        if (request.InGameReferencePng is not null)
        {
            if (packageDestination is null || evidence.ActualSha256 is null)
                throw new InvalidDataException("Full acceptance needs a new PNG package directory and an actual capture.");
            var actualPath = Path.Combine(request.CandidateDirectory, "actual.png");
            ReferenceStore.VerifyHash(actualPath, evidence.ActualSha256);
            var calibrationPath = request.CalibrationPath ?? throw new InvalidDataException("Full acceptance requires five-capture calibration evidence.");
            var calibration = JsonSerializer.Deserialize<ReferenceCalibrationReport>(File.ReadAllText(calibrationPath), ScenarioLoader.JsonOptions)
                ?? throw new InvalidDataException("Calibration evidence is null.");
            referenceHash = ReferenceStore.Hash(request.InGameReferencePng);
            if (calibration.Width != scenario.CaptureProfile.Width || calibration.Height != scenario.CaptureProfile.Height ||
                calibration.Background != scenario.CaptureProfile.Background ||
                !calibration.Cases.TryGetValue(scenario.Id, out var samples) || samples.SampleSha256.Count != 5 ||
                (samples.ScenarioSha256 is not null && samples.ScenarioSha256 != scenarioHash) ||
                !samples.SampleSha256.Contains(referenceHash, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Reference must be one of five calibrated captures for this scenario and Capture Profile.");
            tolerance = calibration.Tolerance;
            ScenarioLoader.ValidateTolerance(tolerance);
            var reference = WpfPreviewCapture.ReadPng(request.InGameReferencePng, scenario.CaptureProfile.Background);
            var actual = WpfPreviewCapture.ReadPng(actualPath, scenario.CaptureProfile.Background);
            if (reference.Width != scenario.CaptureProfile.Width || reference.Height != scenario.CaptureProfile.Height)
                throw new InvalidDataException("In-game reference dimensions do not match the Capture Profile.");
            var comparison = PixelComparator.Compare(reference, actual, scenario.PixelToleranceOverride ?? tolerance, scenario.Masks);
            var diff = Path.Combine(request.CandidateDirectory, "accept-diff.png");
            var overlay = Path.Combine(request.CandidateDirectory, "accept-overlay.png");
            WpfPreviewCapture.WritePng(diff, comparison.Diff); WpfPreviewCapture.WritePng(overlay, comparison.Overlay);
            identifyEvidence($"Reference: {Path.GetFullPath(request.InGameReferencePng)}\nActual: {Path.GetFullPath(actualPath)}\nDiff: {Path.GetFullPath(diff)}\nOverlay: {Path.GetFullPath(overlay)}\n" +
                $"Maximum channel difference: {comparison.MaximumChannelDifference}; differing pixel proportion: {comparison.DifferingPixelProportion:R}; masked pixels: {comparison.MaskedPixels}.");
            referenceName = scenario.Id + ".png";
        }
        else
        {
            if (packageDestination is not null) throw new InvalidDataException("Structure Only acceptance does not create a PNG package.");
            identifyEvidence($"Structure Only acceptance: {structurePath}; {layoutPath}. Pixel work: NotRun.");
            if (source?.Cases.Values.Any(e => e.ReferencePng is not null) == true)
                throw new InvalidDataException("Structure Only acceptance cannot silently drop an existing PNG package.");
        }

        // Validate and copy into staging directories. Neither an existing manifest nor package is modified in place.
        var metadataStage = destination + ".staging-" + Guid.NewGuid().ToString("N");
        var packageStage = packageDestination is null ? null : packageDestination + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(metadataStage);
        if (packageStage is not null) Directory.CreateDirectory(packageStage);
        var cases = new SortedDictionary<string, ReferenceEntry>(StringComparer.Ordinal);
        try
        {
            foreach (var pair in source?.Cases ?? new Dictionary<string, ReferenceEntry>())
            {
                if (pair.Key == scenario.Id) continue;
                CopyVerified(request.SourceMetadataDirectory, metadataStage, pair.Value.StructureFile, pair.Value.StructureSha256);
                CopyVerified(request.SourceMetadataDirectory, metadataStage, pair.Value.LayoutFile, pair.Value.LayoutSha256);
                if (pair.Value.ReferencePng is not null)
                {
                    if (request.SourcePackageDirectory is null || packageStage is null || pair.Value.ReferenceSha256 is null)
                        throw new InvalidDataException("An existing PNG package must be supplied to carry forward references.");
                    CopyVerified(request.SourcePackageDirectory, packageStage, pair.Value.ReferencePng, pair.Value.ReferenceSha256);
                }
                cases.Add(pair.Key, pair.Value);
            }
            var structureName = scenario.Id + ".structure.json";
            var layoutName = scenario.Id + ".layout.json";
            File.Copy(structurePath, Path.Combine(metadataStage, structureName));
            File.Copy(layoutPath, Path.Combine(metadataStage, layoutName));
            if (referenceName is not null) File.Copy(request.InGameReferencePng!, Path.Combine(packageStage!, referenceName));
            cases.Add(scenario.Id, new(evidence.PrefabIdentity, evidence.ScenarioSha256, scenario.Scene, scenario.DataState,
                structureName, evidence.StructureSha256, layoutName, evidence.LayoutSha256, referenceName, referenceHash));
            var manifest = new ReferenceManifest(1, evidence.Reference.GameVersion, evidence.Reference.ModuleVersion,
                scenario.CaptureProfile, tolerance, cases);
            File.WriteAllText(Path.Combine(metadataStage, "manifest.json"), JsonSerializer.Serialize(manifest, ScenarioLoader.JsonOptions));
            if (request.CalibrationPath is not null && packageStage is not null)
                File.Copy(request.CalibrationPath, Path.Combine(packageStage, "calibration.json"));
            if (packageStage is not null) Directory.Move(packageStage, packageDestination!);
            Directory.Move(metadataStage, destination);
            return Path.Combine(destination, "manifest.json");
        }
        finally
        {
            // Only the two unique staging directories created above can be cleaned up.
            CleanupStage(metadataStage, destination);
            if (packageStage is not null) CleanupStage(packageStage, packageDestination!);
        }
    }

    private static void RequireNewDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new InvalidDataException("Acceptance requires a new immutable destination: " + path);
    }
    private static void CleanupStage(string stage, string destination)
    {
        var resolved = Path.GetFullPath(stage);
        if (!resolved.Equals(stage, StringComparison.OrdinalIgnoreCase) ||
            !resolved.StartsWith(Path.GetFullPath(destination) + ".staging-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe acceptance staging cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static bool InsideEither(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(b.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(a.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CopyVerified(string sourceRoot, string destinationRoot, string relative, string hash)
    {
        var source = ReferenceStore.ResolveInside(sourceRoot, relative);
        var destination = ReferenceStore.ResolveInside(destinationRoot, relative);
        ReferenceStore.VerifyHash(source, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
    }
}
