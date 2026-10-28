using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bannerlord.XmlPreview.VisualRegression;

public sealed record CaptureProfile(string Id, int Width, int Height, double UiScale = 1,
    string Language = "English", string ColorSpace = "sRGB", string Background = "#000000", string CropRule = "FullFrame");
public sealed record ExpectedWarning(string Code, string WidgetIdOrPath, string Message, string Reason);
public sealed record DynamicMask(string Name, string Reason, int X, int Y, int Width, int Height);
public sealed record PixelTolerance(byte MaximumChannelDifference, double MaximumDifferingPixelProportion,
    string CalibrationReason);

public sealed record PreviewScenario
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string PrefabIdentity { get; init; }
    public required CaptureProfile CaptureProfile { get; init; }
    public required string Scene { get; init; }
    public required string DataState { get; init; }
    public string? AssetModule { get; init; }
    public string[] ScopeWidgetTypes { get; init; } = [];
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<Dictionary<string, string>>> Lists { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyDictionary<string, string>> WidgetStates { get; init; } = new(StringComparer.Ordinal);
    public string[] KeyBoundsSelectors { get; init; } = [];
    public Dictionary<string, string> OptionalConnectionExplanations { get; init; } = new(StringComparer.Ordinal);
    public ExpectedWarning[] ExpectedWarnings { get; init; } = [];
    public DynamicMask[] Masks { get; init; } = [];
    public double BoundsTolerance { get; init; }
    public string? BoundsToleranceReason { get; init; }
    public PixelTolerance? PixelToleranceOverride { get; init; }
}

public static class ScenarioLoader
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PreviewScenario Load(string path)
        => LoadWithHash(path).Scenario;

    internal static (PreviewScenario Scenario, string Sha256) LoadWithHash(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        // Scenario identity uses UTF-8 text with LF endings, independent of checkout conventions.
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var scenario = JsonSerializer.Deserialize<PreviewScenario>(text, JsonOptions)
            ?? throw new InvalidDataException("Scenario document is null.");
        Validate(scenario);
        return (scenario, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));
    }

    public static void Validate(PreviewScenario scenario)
    {
        if (scenario.SchemaVersion != 1) throw new InvalidDataException("Unsupported scenario schema.");
        if (scenario.ScopeWidgetTypes is null || scenario.ScopeWidgetTypes.Any(string.IsNullOrWhiteSpace) || scenario.ScopeWidgetTypes.Distinct(StringComparer.Ordinal).Count() != scenario.ScopeWidgetTypes.Length)
            throw new InvalidDataException("Widget scope requires unique nonempty type names.");
        if (string.IsNullOrWhiteSpace(scenario.PrefabIdentity)) throw new InvalidDataException("Prefab identity is required.");
        try { _ = StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid prefab identity.", exception); }
        if (string.IsNullOrWhiteSpace(scenario.Id) || !System.Text.RegularExpressions.Regex.IsMatch(scenario.Id, "^[a-zA-Z0-9][a-zA-Z0-9_.-]*$") ||
            string.IsNullOrWhiteSpace(scenario.Scene) || string.IsNullOrWhiteSpace(scenario.DataState))
            throw new InvalidDataException("Scenario requires identity, scene and data state.");
        var p = scenario.CaptureProfile ?? throw new InvalidDataException("Capture Profile is required.");
        if (string.IsNullOrWhiteSpace(p.Id) || p.Width <= 0 || p.Height <= 0 || p.Width > 8192 || p.Height > 8192 ||
            !double.IsFinite(p.UiScale) || p.UiScale is < 0.25 or > 4 || p.Language != "English" || p.ColorSpace != "sRGB" || p.CropRule != "FullFrame" ||
            !System.Text.RegularExpressions.Regex.IsMatch(p.Background, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidDataException("Unsupported Capture Profile; use a finite scale from 0.25 to 4, English, sRGB and FullFrame with an RGB background.");
        if (scenario.Bindings is null || scenario.Lists is null || scenario.WidgetStates is null || scenario.OptionalConnectionExplanations is null ||
            scenario.KeyBoundsSelectors is null || scenario.ExpectedWarnings is null || scenario.Masks is null)
            throw new InvalidDataException("Scenario collections cannot be null.");
        if (scenario.Masks.Length > 512 || scenario.Masks.Any(m => m is null) || scenario.ExpectedWarnings.Any(w => w is null))
            throw new InvalidDataException("Invalid mask or warning collection.");
        if (!double.IsFinite(scenario.BoundsTolerance) || scenario.BoundsTolerance < 0 ||
            (scenario.BoundsTolerance > 0 && string.IsNullOrWhiteSpace(scenario.BoundsToleranceReason)))
            throw new InvalidDataException("Bounds tolerance requires a finite value and a reason.");
        if (scenario.Masks.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() != scenario.Masks.Length)
            throw new InvalidDataException("Mask names must be unique.");
        foreach (var mask in scenario.Masks)
            if (string.IsNullOrWhiteSpace(mask.Name) || string.IsNullOrWhiteSpace(mask.Reason) || mask.X < 0 || mask.Y < 0 ||
                mask.Width <= 0 || mask.Height <= 0 || (long)mask.X + mask.Width > p.Width || (long)mask.Y + mask.Height > p.Height)
                throw new InvalidDataException("Masks require a name, reason and bounds inside the capture.");
        if (MaskedArea(scenario.Masks) >= (long)p.Width * p.Height)
            throw new InvalidDataException("Masks cannot exclude the entire capture.");
        foreach (var warning in scenario.ExpectedWarnings)
            if (string.IsNullOrWhiteSpace(warning.Code) || string.IsNullOrWhiteSpace(warning.WidgetIdOrPath) ||
                string.IsNullOrWhiteSpace(warning.Message) || string.IsNullOrWhiteSpace(warning.Reason))
                throw new InvalidDataException("Expected Warnings require code, path, message and reason.");
        foreach (var list in scenario.Lists)
        {
            if (string.IsNullOrWhiteSpace(list.Key) || list.Value is null ||
                list.Value.Any(item => item is null || !item.TryGetValue("__key", out var key) || string.IsNullOrWhiteSpace(key)) ||
                list.Value.Select(item => item["__key"]).Distinct(StringComparer.Ordinal).Count() != list.Value.Count)
                throw new InvalidDataException("Every list item requires a unique stable __key.");
            if (list.Value.Any(item => item.Any(field => string.IsNullOrWhiteSpace(field.Key) || field.Value is null)))
                throw new InvalidDataException("List fields require nonempty keys and nonnull values.");
        }
        if (scenario.Bindings.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null) ||
            scenario.WidgetStates.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null ||
                p.Value.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value is null || v.Key.StartsWith("Debug."))))
            throw new InvalidDataException("Invalid bindings or widget states.");
        foreach (var states in scenario.WidgetStates.Values)
            foreach (var key in states.Keys)
                if (!(key.StartsWith("Is", StringComparison.Ordinal) || key is "Opacity" or "SelectedIndex" or "IntValue" or "Value" or "CurrentState" or "Text" or "KeyText" or "KeyID" or "ValueFloat" or "ValueInt" or "SortState" or "IndexToWatch" or "IndexToWatchFloat" or "IndexToBeVisible" or "IndexToBeVisibleFloat" or "CurrentSelectedIndex" or "DimensionToSync" or "PaddingAmount" or "ImageId" or "TextureProviderName" or "AdditionalArgs" or "Preview.ImageSprite" or "MaskSprite"))
                    throw new InvalidDataException("Widget States accept state values, not structural attributes or widget references: " + key);
        if (scenario.OptionalConnectionExplanations.Any(p => p.Key.LastIndexOf(':') <= 0 || p.Key.EndsWith(':') || string.IsNullOrWhiteSpace(p.Value)))
            throw new InvalidDataException("Optional Connection explanations require an expanded path, role and nonempty scenario reason.");
        if (scenario.KeyBoundsSelectors.Any(string.IsNullOrWhiteSpace) ||
            scenario.KeyBoundsSelectors.Distinct(StringComparer.Ordinal).Count() != scenario.KeyBoundsSelectors.Length)
            throw new InvalidDataException("Key Bounds selectors must be nonempty and unique.");
        if (scenario.PixelToleranceOverride is not null) ValidateTolerance(scenario.PixelToleranceOverride);
    }

    internal static void ValidateTolerance(PixelTolerance tolerance)
    {
        if (!double.IsFinite(tolerance.MaximumDifferingPixelProportion) ||
            tolerance.MaximumDifferingPixelProportion is < 0 or > 1 || string.IsNullOrWhiteSpace(tolerance.CalibrationReason))
            throw new InvalidDataException("Pixel Tolerance requires a proportion in [0,1] and a calibration reason.");
    }

    private static long MaskedArea(IReadOnlyList<DynamicMask> masks)
    {
        var edges = masks.SelectMany(m => new[] { m.X, m.X + m.Width }).Distinct().Order().ToArray();
        long area = 0;
        for (var i = 1; i < edges.Length; i++)
        {
            var intervals = masks.Where(m => m.X <= edges[i - 1] && m.X + m.Width >= edges[i])
                .Select(m => (Start: m.Y, End: m.Y + m.Height)).OrderBy(v => v.Start).ToArray();
            var covered = 0; var end = 0;
            foreach (var interval in intervals)
            {
                covered += Math.Max(0, interval.End - Math.Max(interval.Start, end));
                end = Math.Max(end, interval.End);
            }
            area += (long)(edges[i] - edges[i - 1]) * covered;
        }
        return area;
    }
}
