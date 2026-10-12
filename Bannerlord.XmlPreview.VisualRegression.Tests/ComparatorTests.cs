using Bannerlord.XmlPreview.Models;
using System.Globalization;
using System.IO;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class ComparatorTests
{
    [Fact]
    public void WidgetIdentityCanonicalizesPathsButPreservesInstanceKeys()
    {
        Assert.Equal(StableWidgetId.Create("Modules\\Native\\Example.XML", "/root", "alice"),
            StableWidgetId.Create("modules/native/example.xml", "/root", "alice"));
        Assert.NotEqual(StableWidgetId.Create("modules/native/example.xml", "/root", "alice"),
            StableWidgetId.Create("modules/native/example.xml", "/root", "bob"));
        Assert.Throws<ArgumentException>(() => StableWidgetId.Create("../escape.xml", "/root"));
        Assert.Throws<ArgumentException>(() => StableWidgetId.Create("C:/escape.xml", "/root"));
    }

    [Fact]
    public void SnapshotSerializationIgnoresDictionaryAllocationAndCultureButPreservesChildOrder()
    {
        var first = new RenderWidgetSnapshot("root", "/root", "", "Widget", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }, []);
        var second = first with { Properties = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } };
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(SnapshotSerialization.Serialize(new RenderStructureSnapshot(1, "fixture.xml", first)),
                SnapshotSerialization.Serialize(new RenderStructureSnapshot(1, "fixture.xml", second)));
            Assert.Contains("1.25", SnapshotSerialization.Serialize(new LayoutSnapshot(1, [new("id", "/root", 1.25, 0, 1, 1)])));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var a = first with { WidgetId = "a" }; var b = first with { WidgetId = "b" };
        Assert.Equal(1, StructureComparator.Compare(new(1, "fixture.xml", first with { Children = [a, b] }),
            new(1, "fixture.xml", first with { Children = [b, a] })));
    }

    [Fact]
    public void LayoutComparatorRequiresIdentityAndFiniteBoundsAndHonorsTolerance()
    {
        var expected = new LayoutSnapshot(1, [new("id", "/root", 0, 0, 10, 10)]);
        var actual = new LayoutSnapshot(1, [expected.KeyBounds[0] with { X = 0.01 }]);
        Assert.False(LayoutComparator.Compare(expected, actual, 0).Passed);
        Assert.True(LayoutComparator.Compare(expected, actual, 0.01).Passed);
        Assert.False(LayoutComparator.Compare(expected, new(1, [actual.KeyBounds[0] with { WidgetId = "other" }]), 1).Passed);
        Assert.Throws<InvalidDataException>(() => LayoutComparator.Compare(expected,
            new(1, [expected.KeyBounds[0] with { X = double.NaN }]), 0));
    }

    [Fact]
    public void DiagnosticsComparatorMatchesExactWarningMultisets()
    {
        var expected = new ExpectedWarning("code", "/widget", "message", "reason");
        var actual = new CapturedDiagnostic("code", "/widget", "message");
        Assert.Empty(DiagnosticsComparator.Compare([expected], [actual]));
        Assert.Single(DiagnosticsComparator.Compare([expected], [actual, actual]));
        Assert.Single(DiagnosticsComparator.Compare([expected], []));
        Assert.Equal(2, DiagnosticsComparator.Compare([expected], [actual with { WidgetIdOrPath = "/other" }]).Length);
    }

    [Fact]
    public void PixelComparatorChecksDimensionsThresholdsAndMaskUnion()
    {
        var expected = new RgbImage(3, 1, [0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var actual = new RgbImage(3, 1, [0, 0, 0, 100, 0, 0, 255, 0, 0]);
        var result = PixelComparator.Compare(expected, actual, new(1, 0, "test"),
            [new("first", "test", 1, 0, 1, 1), new("overlap", "test", 1, 0, 2, 1)]);
        Assert.True(result.Passed);
        Assert.Equal(2, result.MaskedPixels);
        Assert.Equal(1, result.ComparedPixels);
        Assert.False(PixelComparator.Compare(expected, new(1, 1, [0, 0, 0]), new(0, 1, "test"), []).DimensionsPassed);
        Assert.True(PixelComparator.Compare(new(1, 1, [0, 0, 0]), new(1, 1, [1, 0, 0]), new(1, 0, "test"), []).Passed);
    }

    [Fact]
    public void ScenarioValidationRejectsInvalidMasksEvenWhenTheirUnionCoversFrame()
    {
        using var fixture = new RegressionFixture();
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.Validate(fixture.Scenario with { Masks = [new("mask", "", 0, 0, 1, 1)] }));
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.Validate(fixture.Scenario with { Masks = [new("mask", "reason", 1, 0, 2, 1)] }));
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.Validate(fixture.Scenario with
        { Masks = [new("a", "reason", 0, 0, 1, 1), new("b", "reason", 1, 0, 1, 1)] }));
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.Validate(fixture.Scenario with { BoundsTolerance = 1 }));
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.Validate(fixture.Scenario with
        { Lists = new() { ["path"] = [new() { ["__key"] = "same" }, new() { ["__key"] = "same" }] } }));
    }

    [Fact]
    public void PackageLookupRejectsEscapingPaths()
    {
        using var fixture = new RegressionFixture();
        Assert.Throws<InvalidDataException>(() => ReferenceStore.ResolveInside(fixture.Package, "../escape.png"));
        Assert.Throws<InvalidDataException>(() => ReferenceStore.ResolveInside(fixture.Package, fixture.ScenarioPath));
    }

    [Fact]
    public void CalibrationRequiresFiveEqualDimensionSamplesAndMeasuresVariance()
    {
        using var fixture = new RegressionFixture();
        var caseRoot = Path.Combine(fixture.Root, "samples", "synthetic");
        Directory.CreateDirectory(caseRoot);
        var sampleRoot = Path.GetDirectoryName(caseRoot)!;
        Assert.Throws<InvalidDataException>(() => ReferenceCalibration.Calibrate(sampleRoot));
        for (var i = 0; i < 5; i++) WpfPreviewCapture.WritePng(Path.Combine(caseRoot, i + ".png"), new(2, 1, [(byte)i, 0, 0, 0, 0, 0]));
        var calibration = ReferenceCalibration.Calibrate(sampleRoot);
        Assert.Equal(4, calibration.Cases["synthetic"].MaximumInternalChannelDifference);
        Assert.Equal(5, calibration.Tolerance.MaximumChannelDifference);
        Assert.Equal(5, calibration.Cases["synthetic"].SampleSha256.Count);
        WpfPreviewCapture.WritePng(Path.Combine(caseRoot, "0.png"), new(1, 1, [0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => ReferenceCalibration.Calibrate(sampleRoot));
    }
}
