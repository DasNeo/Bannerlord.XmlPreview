using System.IO;
using System.Text.Json;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class ScenarioRepositoryTests
{
    [Fact]
    public async Task PresenterLoadsVerifiedFullArtifactsAndRejectsAnotherScenarioAndStructureOnly()
    {
        using var fixture = new RegressionFixture();
        var report = await fixture.Runner.CompareAsync(fixture.Request, cancellationToken: TestContext.Current.CancellationToken);
        var presented = ComparisonPresenter.LoadArtifacts(report.Artifacts.ReportJson!, fixture.ScenarioPath);
        Assert.Equal("Full", presented.RunMode);
        Assert.Equal(fixture.ReferencePixels.Pixels, presented.Reference.Pixels);
        Assert.Equal(fixture.Frame.Pixels!.Pixels, presented.Actual.Pixels);
        Assert.Equal(report.AppliedPixelTolerance, presented.Tolerance);
        Assert.Contains("Passed", presented.Summary);
        var scenarioPath = Path.Combine(fixture.Root, "other.json");
        File.WriteAllText(scenarioPath, JsonSerializer.Serialize(fixture.Scenario with { Id = "other" }, ScenarioLoader.JsonOptions));
        Assert.Throws<InvalidDataException>(() => ComparisonPresenter.LoadArtifacts(report.Artifacts.ReportJson!, scenarioPath));
        var structural = await fixture.Runner.CompareAsync(fixture.Request with { Mode = VisualRegressionMode.StructureOnly }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => ComparisonPresenter.LoadArtifacts(structural.Artifacts.ReportJson!, fixture.ScenarioPath));
    }

    [Fact]
    public void LifecyclePreservesAllScenarioValuesAndRejectsInvalidSavesWithoutReplacingValidFiles()
    {
        using var fixture = new RegressionFixture();
        var repository = new ScenarioRepository(Path.Combine(fixture.Root, "authoring"));
        var scenario = new PreviewScenario
        {
            Id = "first", PrefabIdentity = "fixture.xml", Scene = "test", DataState = "selected",
            CaptureProfile = new("test", 4, 2), Bindings = new() { ["Label"] = "Text" },
            Lists = new() { ["Window > ListPanel"] = [new() { ["__key"] = "row-1", ["Label"] = "First" }] },
            WidgetStates = new() { ["Window"] = new Dictionary<string, string> { ["IsVisible"] = "true" } },
            Masks = [new("portrait", "dynamic", 0, 0, 1, 1)],
            ExpectedWarnings = [new("test.warning", "Window", "Expected", "controlled")], KeyBoundsSelectors = ["Window"],
            OptionalConnectionExplanations = new() { ["Window:Scrollbar"] = "intentionally absent" }
        };
        repository.Create(scenario);
        Assert.Equal(JsonSerializer.Serialize(scenario, ScenarioLoader.JsonOptions), JsonSerializer.Serialize(repository.Load("first"), ScenarioLoader.JsonOptions));
        var original = File.ReadAllBytes(repository.PathFor("first"));
        Assert.Throws<InvalidDataException>(() => repository.Save(scenario with { SchemaVersion = 2 }));
        Assert.Equal(original, File.ReadAllBytes(repository.PathFor("first")));
        Assert.Throws<IOException>(() => repository.Create(scenario));
        repository.Clone("first", "clone");
        repository.Rename("clone", "renamed");
        Assert.Equal(new[] { "first", "renamed" }, repository.List());
        repository.Delete("renamed");
        Assert.Equal("first", Assert.Single(repository.List()));
        Assert.Throws<InvalidDataException>(() => repository.PathFor("../escape"));
        Assert.Throws<InvalidDataException>(() => repository.Save(scenario with { Lists = new() { ["rows"] = [new() { ["__key"] = "key", ["bad"] = null! }] } }));
        File.WriteAllText(repository.PathFor("first"), JsonSerializer.Serialize(scenario, ScenarioLoader.JsonOptions).Replace("\"Scene\":", "\"FutureField\": true,\"Scene\":"));
        Assert.Throws<JsonException>(() => repository.Load("first"));
    }

    [Fact]
    public void PersistedScenarioListsStatesAndResolutionReachTheRenderer()
    {
        using var fixture = new RegressionFixture();
        const string listPath = "Window > ListPanel[Id=rows]";
        const string templatePath = listPath + " > ItemTemplate";
        const string textPath = templatePath + " > TextWidget[Id=row]";
        Dictionary<string, string> Attributes(string path) => new()
        { ["Debug.WidgetPath"] = path, ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "10", ["SuggestedHeight"] = "6" };
        var text = Attributes(textPath); text["Text"] = "@Label"; text["Id"] = "row";
        var list = Attributes(listPath); list["Id"] = "rows";
        var root = new WindowNode(new Dictionary<string, string> { ["Debug.WidgetPath"] = "Window" },
            [new ListPanelNode(list, [new ItemTemplateNode(Attributes(templatePath), [new TextWidgetNode(text, [])])])]);
        var document = new PrefabDocument("fixture.xml", root, new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
        var scenario = new PreviewScenario { Id = "lists", PrefabIdentity = "fixture.xml", Scene = "test", DataState = "test", CaptureProfile = new("small", 12, 6),
            Lists = new() { [listPath] = [new() { ["__key"] = "first", ["Label"] = "First" }, new() { ["__key"] = "second", ["Label"] = "Second" }] },
            WidgetStates = new() { [listPath] = new Dictionary<string, string> { ["IsVisible"] = "false" } } };
        var repository = new ScenarioRepository(Path.Combine(fixture.Root, "scenarios"));
        repository.Create(scenario);
        var loaded = repository.Load(scenario.Id);
        SnapshotCaptureTests.Sta(() =>
        {
            var diagnostics = new PreviewDiagnostics();
            var preview = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData()).Render(ScenarioApplication.Apply(document, loaded), diagnostics, ScenarioApplication.Options(loaded));
            var rows = Assert.Single(diagnostics.RenderStructure!.Root.Children);
            Assert.Equal(2, rows.Children.Count);
            Assert.Equal(new[] { "First", "Second" }, rows.Children.Select(row => Assert.Single(row.Children).Properties["Text"]));
            Assert.Equal("false", rows.Properties["IsVisible"]);
            Assert.Equal(12, preview.ActualWidth); Assert.Equal(6, preview.ActualHeight);
            return 0;
        });
    }

    [Fact]
    public void InteractiveComparisonUsesRegressionMasksAndTolerance()
    {
        var scenario = new PreviewScenario { Id = "comparison", PrefabIdentity = "fixture.xml", Scene = "test", DataState = "test",
            CaptureProfile = new("test", 2, 1), Masks = [new("dynamic", "runtime", 0, 0, 1, 1)] };
        var reference = new RgbImage(2, 1, [0, 0, 0, 0, 0, 0]);
        var actual = new RgbImage(2, 1, [255, 255, 255, 0, 0, 0]);
        var result = ComparisonPresenter.Compare(reference, actual, scenario);
        Assert.True(result.Passed);
        Assert.Equal(0, result.DifferingPixelProportion);
        Assert.Equal(new byte[] { 255, 0, 255, 0, 0, 0 }, result.Diff.Pixels);
        Assert.Equal(new byte[] { 255, 127, 255, 0, 0, 0 }, result.Overlay.Pixels);
        Assert.Contains("uncalibrated", result.Summary);
    }
}
