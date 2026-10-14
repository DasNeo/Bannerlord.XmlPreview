using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Preview.Bannerlord;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.ViewModels;
using Bannerlord.XmlPreview.ViewModels.Mocks;
using Bannerlord.XmlPreview.VisualRegression;
using Xunit;

namespace Bannerlord.XmlPreview.Tests;

public sealed class AuthoringTests
{
    [Theory]
    [InlineData("root.xml")]
    [InlineData("Part.xml")]
    [InlineData("brush.xml")]
    [InlineData("image.png")]
    public async Task DependencyBatchPublishesOneNewPreview(string dependency) => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        var before = fixture.ViewModel.PreviewContent;
        Assert.Contains(Path.Combine(fixture.Directory, dependency), fixture.Watcher.Paths);
        fixture.Watcher.Change(Path.Combine(fixture.Directory, dependency));
        await WaitUntil(() => fixture.Loader.Count == 2 && !fixture.ViewModel.IsPreviewStale);
        Assert.NotSame(before, fixture.ViewModel.PreviewContent);
        Assert.Equal(2, fixture.Loader.Count);
        Assert.Contains(dependency, fixture.ViewModel.DiagnosticsText);
        Assert.Equal(1, fixture.Assets.Invalidations);
    });

    [Fact]
    public async Task MalformedIncludeRetainsLastGoodPreviewTreeAndDependenciesAndRecovers() => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        var before = fixture.ViewModel.PreviewContent;
        var tree = fixture.ViewModel.PreviewInspector.PreviewTree.ToArray();
        var dependencies = fixture.Watcher.Paths.ToArray();
        var include = Path.Combine(fixture.Directory, "Part.xml");
        File.WriteAllText(include, "<Prefab>\n<Window>");
        fixture.Watcher.Change(include);
        await WaitUntil(() => fixture.ViewModel.StatusText.StartsWith("Load failed"));
        Assert.Same(before, fixture.ViewModel.PreviewContent);
        Assert.True(fixture.ViewModel.IsPreviewStale);
        Assert.Equal(tree, fixture.ViewModel.PreviewInspector.PreviewTree);
        Assert.Equal(dependencies, fixture.Watcher.Paths);
        Assert.Equal(include, fixture.ViewModel.SelectedSource!.FilePath);
        Assert.True(fixture.ViewModel.SelectedSource.Line > 0);
        fixture.ViewModel.AuthoringDiagnostics[0].OpenCommand.Execute(null);
        Assert.Equal(include, fixture.Navigator.Opened!.FilePath);
        File.WriteAllText(include, Fixture.IncludeXml);
        fixture.Watcher.Change(include);
        await WaitUntil(() => fixture.Loader.Count == 3 && !fixture.ViewModel.IsPreviewStale);
        Assert.NotSame(before, fixture.ViewModel.PreviewContent);
    });

    [Fact]
    public async Task ScenarioSwitchRestoresValuesStateSizeAndDirtyChangesSurviveDependencyReload() => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        fixture.ViewModel.ScenarioName = "first";
        fixture.ViewModel.CreateScenarioCommand.Execute(null);
        await WaitUntil(() => fixture.ViewModel.SelectedScenarioId == "first");
        var first = fixture.Repository.Load("first");
        var path = "Window > Widget[Id=root]";
        var second = first with { Id = "second", Bindings = new() { ["Label"] = "second" },
            WidgetStates = new() { [path] = new Dictionary<string, string> { ["IsVisible"] = "false" } }, CaptureProfile = new("small", 24, 12) };
        fixture.Repository.Create(second);
        fixture.ViewModel.SelectedScenarioId = "second";
        await WaitUntil(() => fixture.ViewModel.SelectedScenarioId == "second");
        Assert.Equal("24", fixture.ViewModel.PreviewWidth);
        Assert.Equal("second", fixture.Bindings.Resolve("Label"));
        Assert.Contains("second", fixture.ViewModel.DiagnosticsText);
        var binding = Assert.Single(fixture.ViewModel.MockEditor.Bindings, value => value.Key == "Label");
        binding.Value = "edited";
        Assert.True(fixture.ViewModel.ScenarioDirty);
        await WaitUntil(() => !fixture.ViewModel.IsPreviewStale);
        var loadsBeforeDependency = fixture.Loader.Count;
        fixture.Watcher.Change(fixture.RootPrefab);
        await WaitUntil(() => fixture.Loader.Count > loadsBeforeDependency && !fixture.ViewModel.IsPreviewStale);
        Assert.True(fixture.ViewModel.ScenarioDirty);
        Assert.Equal("edited", fixture.Bindings.Resolve("Label"));
        fixture.ViewModel.SaveScenarioCommand.Execute(null);
        Assert.False(fixture.ViewModel.ScenarioDirty);
        Assert.Equal("edited", fixture.Repository.Load("second").Bindings["Label"]);
        fixture.ViewModel.SelectedScenarioId = "first";
        await WaitUntil(() => fixture.ViewModel.SelectedScenarioId == "first");
        Assert.Equal("Label", fixture.Bindings.Resolve("Label"));
    });

    [Fact]
    public async Task InvalidScenarioAndRenderFailureKeepActiveValidScenarioAndPreview() => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        fixture.ViewModel.CreateScenarioCommand.Execute(null);
        await WaitUntil(() => fixture.ViewModel.SelectedScenarioId == "new-scenario");
        var before = fixture.ViewModel.PreviewContent;
        var valid = fixture.Repository.Load("new-scenario");
        var scenarioPath = fixture.Repository.PathFor(valid.Id);
        File.WriteAllText(scenarioPath, "{\"unknown\":true}");
        fixture.Watcher.Change(scenarioPath);
        await WaitUntil(() => fixture.ViewModel.StatusText.StartsWith("Load failed"));
        Assert.Same(before, fixture.ViewModel.PreviewContent);
        Assert.Equal(valid.Id, fixture.ViewModel.SelectedScenarioId);
        fixture.Repository.Save(valid with { KeyBoundsSelectors = ["no-such-widget"] });
        fixture.Watcher.Change(scenarioPath);
        await WaitUntil(() => fixture.Loader.Count == 4 && fixture.ViewModel.StatusText.StartsWith("Load failed"));
        Assert.Same(before, fixture.ViewModel.PreviewContent);
        fixture.Repository.Save(valid);
        fixture.Watcher.Change(scenarioPath);
        await WaitUntil(() => fixture.Loader.Count == 5 && !fixture.ViewModel.IsPreviewStale);
    });

    [Fact]
    public async Task NewDependencyChangeCancelsSlowObsoleteReload() => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        fixture.Loader.SlowNext = true;
        fixture.ViewModel.ReloadCommand.Execute(null);
        await fixture.Loader.SlowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Watcher.Change(fixture.RootPrefab);
        await WaitUntil(() => fixture.Loader.Count == 3 && !fixture.ViewModel.IsPreviewStale);
        Assert.Equal(1, fixture.Loader.Cancelled);
        Assert.StartsWith("Loaded", fixture.ViewModel.StatusText);
        Assert.DoesNotContain("Load failed", fixture.ViewModel.DiagnosticsText);
    });

    [Fact]
    public async Task ComparisonWindowOffersSynchronizedViewsAndLocatesMappedDifferences() => await Sta(() =>
    {
        var scenario = new PreviewScenario { Id = "comparison", PrefabIdentity = "fixture.xml", Scene = "test", DataState = "test", CaptureProfile = new("test", 2, 1) };
        var bounds = new WidgetBounds("widget", "Window", 0, 0, 2, 1);
        var result = ComparisonPresenter.Compare(new RgbImage(2, 1, [0, 0, 0, 0, 0, 0]), new RgbImage(2, 1, [255, 0, 0, 0, 0, 0]), scenario, new LayoutSnapshot(1, [bounds]));
        WidgetBounds? located = null;
        var window = new global::Bannerlord.XmlPreview.ComparisonWindow(result, scenario, value => located = value);
        var mode = (System.Windows.Controls.ComboBox)window.FindName("Mode");
        var second = (System.Windows.Controls.Image)window.FindName("Second");
        Assert.Equal(5, mode.Items.Count);
        Assert.Equal(Visibility.Visible, second.Visibility);
        mode.SelectedIndex = 3;
        Assert.Equal(Visibility.Collapsed, second.Visibility);
        var image = (System.Windows.Media.Imaging.BitmapSource)((System.Windows.Controls.Image)window.FindName("First")).Source;
        var pixels = new byte[6]; image.CopyPixels(pixels, 6, 0);
        Assert.Equal(result.Overlay.Pixels, pixels);
        mode.SelectedIndex = 4;
        image = (System.Windows.Media.Imaging.BitmapSource)((System.Windows.Controls.Image)window.FindName("First")).Source;
        image.CopyPixels(pixels, 6, 0); Assert.Equal(result.Diff.Pixels, pixels);
        ((System.Windows.Controls.ListBox)window.FindName("Differences")).SelectedIndex = 0;
        Assert.Equal(bounds, located);
        ((System.Windows.Controls.Slider)window.FindName("Zoom")).Value = 2;
        var scale = (ScaleTransform)window.FindName("ImageScale");
        Assert.Equal(2, scale.ScaleX); Assert.Equal(2, scale.ScaleY);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public async Task TreeSourceNavigationPreservesIncludeFileAndLine() => await Sta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.ApplyStartupOptionsAsync(null, fixture.RootPrefab);
        var nodes = Flatten(fixture.ViewModel.PreviewInspector.PreviewTree).ToArray();
        var node = Assert.Single(nodes, item => PreviewElementDebugProperties.GetSourceLocation(item.Element)?.FilePath.EndsWith("Part.xml") == true);
        var source = PreviewElementDebugProperties.GetSourceLocation(node.Element)!;
        Assert.Equal(3, source.Line);
        fixture.ViewModel.SelectedSource = source;
        fixture.ViewModel.OpenSourceCommand.Execute(null);
        Assert.Equal(source, fixture.Navigator.Opened);
    });

    private static IEnumerable<PreviewTreeNodeViewModel> Flatten(IEnumerable<PreviewTreeNodeViewModel> nodes)
    { foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; } }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private static Task Sta(Func<Task> action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); result.SetResult(); }
                catch (Exception exception) { result.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return result.Task;
    }
    private sealed class Fixture : IDisposable
    {
        public const string IncludeXml = "<Prefab>\n<Window>\n<Widget Id=\"included\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"10\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"5\"/>\n</Window></Prefab>";
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "authoring-" + Guid.NewGuid().ToString("N"));
        public string RootPrefab => Path.Combine(Directory, "root.xml");
        public MainWindowViewModel ViewModel { get; }
        public TestWatcher Watcher { get; } = new();
        public Navigator Navigator { get; } = new();
        public PreviewBindingStore Bindings { get; } = new();
        public TestAssets Assets { get; }
        public CountingLoader Loader { get; }
        public ScenarioRepository Repository { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(RootPrefab, "<Prefab><Window><Widget Id=\"root\"><Children><Part/><TextWidget Text=\"@Label\"/></Children></Widget></Window></Prefab>");
            File.WriteAllText(Path.Combine(Directory, "Part.xml"), IncludeXml);
            File.WriteAllText(Path.Combine(Directory, "brush.xml"), "<Brushes/>");
            File.WriteAllBytes(Path.Combine(Directory, "image.png"), []);
            Assets = new TestAssets([Path.Combine(Directory, "brush.xml"), Path.Combine(Directory, "image.png")]);
            var metrics = new BannerlordBrushMetricsProvider(Assets);
            Loader = new CountingLoader(new PrefabLoader(Assets, metrics, new References(Directory), new Properties(), new WidgetReferenceResolver(), new Factory()));
            var data = new BannerlordPreviewDataSourceProvider(Bindings);
            var layout = new Layout();
            var renderer = new PrefabRenderer(Assets, metrics, new BannerlordBrushOverlayFeatureHandler(), data, layout,
                new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider());
            Repository = new ScenarioRepository(Path.Combine(Directory, "scenarios"));
            var editor = new MockEditorViewModel(new TemplateMockService(data, Bindings), Bindings);
            ViewModel = new(Loader, renderer, new Dialog(), Watcher, new Settings(Directory), layout, new DiagnosticsSnapshotWriter(), new EnvironmentInitializer(),
                editor, new PreviewInspectorViewModel(new PreviewTreeBuilder()), Repository, Navigator, Bindings, Assets, data);
        }
        public void Dispose() { ViewModel.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }
    private sealed class CountingLoader(IPrefabLoader loader) : IPrefabLoader
    {
        public void Invalidate() => loader.Invalidate();
        public int Count;
        public bool SlowNext;
        public int Cancelled;
        public TaskCompletionSource SlowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PrefabDocument Load(string path, PreviewDiagnostics diagnostics, CancellationToken token = default)
        {
            Interlocked.Increment(ref Count);
            if (SlowNext)
            {
                SlowNext = false; SlowStarted.TrySetResult();
                try { Task.Delay(Timeout.Infinite, token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { Interlocked.Increment(ref Cancelled); throw; }
            }
            return loader.Load(path, diagnostics, token);
        }
    }
    private sealed class TestWatcher : IFileWatcherService
    {
        public string[] Paths { get; private set; } = [];
        public event EventHandler<IReadOnlyList<string>>? DependenciesChanged;
        public event EventHandler<FileInfo>? FileChanged { add { } remove { } }
        public void ReplaceDependencies(IEnumerable<string> paths) => Paths = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        public void Change(string path) => DependenciesChanged?.Invoke(this, [path]);
        public int Start(FileInfo file) => 0;
        public void Stop(FileInfo file) { }
    }
    private sealed class TestAssets(string[] dependencies) : IAssetCatalog
    {
        public int Invalidations;
        public IReadOnlyCollection<string> Dependencies => dependencies;
        public void Invalidate() => Invalidations++;
        public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task WarmUpAssetsAsync(IReadOnlyCollection<string> brushes, IReadOnlyCollection<string> sprites, CancellationToken token = default) => Task.CompletedTask;
        public SpriteDefinition? FindSprite(string? name) => null;
        public BrushDefinition? FindBrush(string? name) => null;
        public ImageSource? GetSpriteImage(string? name) => null;
        public SpriteAssetResolution ResolveSpriteAsset(string? name) => new(name ?? "", SpriteAssetResolutionKind.Missing, null, null, null, null);
    }
    private sealed class References(string directory) : IPrefabReferenceResolver
    {
        public PrefabReferenceResolveResult Resolve(string source, string name) => name == "Part"
            ? new(name, Path.Combine(directory, "Part.xml"), [], PrefabReferenceResolveKind.SameDirectory)
            : new(name, null, [], PrefabReferenceResolveKind.NotFound);
    }
    private sealed class Properties : IWidgetPropertyResolver
    {
        public PrefabWidgetPropertyMap Resolve(XElement root, PrefabWidgetReferenceMap map, PreviewDiagnostics diagnostics) => PrefabWidgetPropertyMap.Empty;
    }
    private sealed class Factory : IWidgetNodeFactory
    {
        public PrefabNode CreateNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children) => name switch
        { "Window" => new WindowNode(attributes, children), "TextWidget" => new TextWidgetNode(attributes, children), _ => new WidgetNode(name, attributes, children) };
    }
    private sealed class Layout : IRootLayoutHints
    {
        public Size PreviewSize { get; set; } = new(40, 20);
        public Size ResolveRootSize(Func<string?, double, double> resolve) => PreviewSize;
    }
    private sealed class Navigator : ISourceNavigator
    {
        public SourceLocation? Opened;
        public void Open(SourceLocation location) => Opened = location;
        public void CopyPath(SourceLocation location) => Opened = location;
    }
    private sealed class Settings(string directory) : IAppSettingsService
    {
        public string BannerlordInstallRoot => directory;
        public bool TrySetBannerlordInstallRoot(string path, out string normalized, out string error) { normalized = path; error = ""; return true; }
    }
    private sealed class Dialog : IFileDialogService
    {
        public string? OpenPrefab(string seed) => null;
        public string? OpenFolder(string seed, string title) => null;
    }
    private sealed class EnvironmentInitializer : IPreviewEnvironmentInitializer
    { public Task WarmUpAsync(CancellationToken token = default) => Task.CompletedTask; }
}
