using Bannerlord.XmlPreview.Models;
using System.IO;

namespace Bannerlord.XmlPreview.VisualRegression;

internal static class StructureComparator
{
    public static int Compare(RenderStructureSnapshot expected, RenderStructureSnapshot actual)
    {
        if (expected.SchemaVersion != 1 || actual.SchemaVersion != 1 || expected.PrefabIdentity != actual.PrefabIdentity)
            return 1;
        return SnapshotSerialization.Serialize(expected) == SnapshotSerialization.Serialize(actual) ? 0 : 1;
    }
}

internal static class LayoutComparator
{
    public static (bool Passed, double? MaximumDifference) Compare(LayoutSnapshot expected, LayoutSnapshot actual, double tolerance)
    {
        if (expected.SchemaVersion != 1 || actual.SchemaVersion != 1 || expected.KeyBounds.Count != actual.KeyBounds.Count)
            return (false, null);
        Validate(expected); Validate(actual);
        var expectedById = expected.KeyBounds.ToDictionary(b => b.WidgetId, StringComparer.Ordinal);
        var maximum = 0d;
        foreach (var bounds in actual.KeyBounds)
        {
            if (!expectedById.TryGetValue(bounds.WidgetId, out var baseline) || baseline.ExpandedPath != bounds.ExpandedPath)
                return (false, null);
            maximum = Math.Max(maximum, new[] { Math.Abs(bounds.X - baseline.X), Math.Abs(bounds.Y - baseline.Y),
                Math.Abs(bounds.Width - baseline.Width), Math.Abs(bounds.Height - baseline.Height) }.Max());
        }
        return (maximum <= tolerance, maximum);
    }

    private static void Validate(LayoutSnapshot snapshot)
    {
        if (snapshot.KeyBounds.Select(b => b.WidgetId).Distinct(StringComparer.Ordinal).Count() != snapshot.KeyBounds.Count ||
            snapshot.KeyBounds.Any(b => !double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) ||
                !double.IsFinite(b.Height) || b.Width < 0 || b.Height < 0))
            throw new InvalidDataException("Layout Snapshot contains duplicate identities or invalid bounds.");
    }
}

internal static class DiagnosticsComparator
{
    public static DiagnosticDifference[] Compare(IReadOnlyList<ExpectedWarning> expected, IReadOnlyList<CapturedDiagnostic> actual)
    {
        var remaining = expected.Select(w => new CapturedDiagnostic(w.Code, w.WidgetIdOrPath, w.Message)).ToList();
        var differences = new List<DiagnosticDifference>();
        foreach (var warning in actual)
        {
            if (!remaining.Remove(warning))
                differences.Add(new(warning.Code, warning.WidgetIdOrPath, "Unexpected: " + warning.Message));
        }
        differences.AddRange(remaining.Select(w => new DiagnosticDifference(w.Code, w.WidgetIdOrPath, "Expected warning absent: " + w.Message)));
        return differences.OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.WidgetIdOrPath, StringComparer.Ordinal).ToArray();
    }
}

internal sealed record PixelComparison(bool DimensionsPassed, bool Passed, byte MaximumChannelDifference,
    double DifferingPixelProportion, long ComparedPixels, long MaskedPixels, RgbImage Diff, RgbImage Overlay);

internal static class PixelComparator
{
    public static PixelComparison Compare(RgbImage reference, RgbImage actual, PixelTolerance tolerance, IReadOnlyList<DynamicMask> masks)
    {
        reference.Validate(); actual.Validate();
        var diff = new byte[actual.Pixels.Length];
        var overlay = new byte[actual.Pixels.Length];
        var dimensions = reference.Width == actual.Width && reference.Height == actual.Height;
        byte maximum = 0;
        long different = 0, compared = 0, masked = 0;
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
        {
            var index = (y * actual.Width + x) * 3;
            var isMasked = masks.Any(m => x >= m.X && y >= m.Y && x < m.X + m.Width && y < m.Y + m.Height);
            if (isMasked)
            {
                masked++;
                diff[index] = 255; diff[index + 2] = 255;
                overlay[index] = (byte)((actual.Pixels[index] + 255) / 2);
                overlay[index + 1] = (byte)(actual.Pixels[index + 1] / 2);
                overlay[index + 2] = (byte)((actual.Pixels[index + 2] + 255) / 2);
                continue;
            }
            compared++;
            byte pixelMaximum = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                var target = dimensions ? reference.Pixels[index + channel] : (byte)0;
                var delta = (byte)Math.Abs(actual.Pixels[index + channel] - target);
                pixelMaximum = Math.Max(pixelMaximum, delta);
                diff[index + channel] = delta;
                overlay[index + channel] = (byte)((actual.Pixels[index + channel] + target) / 2);
            }
            maximum = Math.Max(maximum, pixelMaximum);
            if (pixelMaximum > tolerance.MaximumChannelDifference) different++;
        }
        if (compared == 0) throw new InvalidDataException("Masks cannot exclude the entire capture.");
        var proportion = (double)different / compared;
        return new(dimensions, dimensions && proportion <= tolerance.MaximumDifferingPixelProportion,
            maximum, proportion, compared, masked,
            new(actual.Width, actual.Height, diff), new(actual.Width, actual.Height, overlay));
    }
}
