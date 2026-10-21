using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Collections.Concurrent;

namespace Bannerlord.XmlPreview.VisualRegression.Cli;

/// <summary>STA capture of an isolated root using the same loader, renderer and Bannerlord adapters as the app.</summary>
public sealed class BannerlordPreviewCapture : IDisposable
{
    private readonly string _installationRoot;
    private readonly BlockingCollection<Action> _jobs = new();
    private readonly Thread _thread;
    private CaptureEnvironment? _environment;
    private readonly string? _corpusRoot;
    private readonly string? _archiveDirectory;

    public BannerlordPreviewCapture(string installationRoot, string? corpusRoot = null, string? archiveDirectory = null)
    {
        _installationRoot = installationRoot;
        _corpusRoot = corpusRoot;
        _archiveDirectory = archiveDirectory;
        _thread = new Thread(() => { foreach (var job in _jobs.GetConsumingEnumerable()) job(); })
            { IsBackground = true, Name = "Visual Regression STA capture" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<CapturedPreview> CaptureAsync(PreviewScenario scenario, VisualRegressionMode mode, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<CapturedPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs.Add(() =>
        {
            try { completion.SetResult(Capture(scenario, mode, cancellationToken)); }
            catch (OperationCanceledException) { completion.SetCanceled(cancellationToken); }
            catch (Exception e) { completion.SetException(e); }
        });
        return completion.Task;
    }

    public void Dispose() { _jobs.CompleteAdding(); _thread.Join(); _jobs.Dispose(); }

    private CapturedPreview Capture(PreviewScenario scenario, VisualRegressionMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = new FixedSettings(Path.GetFullPath(_installationRoot));
        var diagnostics = new PreviewDiagnostics();
        using var timingScope = diagnostics.BeginTimingScope();
        var prefab = ResolvePrefab(settings.BannerlordInstallRoot, scenario.PrefabIdentity);
        if (scenario.PrefabIdentity.StartsWith("fixtures/", StringComparison.Ordinal))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
            if (directory is null) throw new InvalidDataException("Repository fixture root is unavailable.");
            prefab = ResolvePrefab(directory.FullName, scenario.PrefabIdentity);
        }
        var watch = Stopwatch.StartNew();
        PrefabDocument document;
        IAssetCatalog assets;
        IBrushMetricsProvider metrics;
        string moduleVersion;
        if (_corpusRoot is not null)
        {
            if (mode != VisualRegressionMode.StructureOnly || !scenario.CaptureProfile.Id.StartsWith("portable-", StringComparison.Ordinal))
                throw new InvalidScenarioException("Portable metadata fixtures support only Structure Only with a portable Capture Profile.");
            using var corpusFile = File.OpenRead(Path.Combine(_corpusRoot, scenario.Id + ".json.gz"));
            using var decompressed = new System.IO.Compression.GZipStream(corpusFile, System.IO.Compression.CompressionMode.Decompress);
            var corpus = System.Text.Json.JsonSerializer.Deserialize<PortableCorpus>(decompressed, ScenarioLoader.JsonOptions)
                ?? throw new InvalidDataException("Null portable fixture.");
            if (corpus.PrefabIdentity != StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity)) throw new InvalidDataException("Portable fixture identity mismatch.");
            moduleVersion = corpus.ModuleVersion;
            document = corpus.Document(); assets = new CorpusAssets(corpus); metrics = new BannerlordBrushMetricsProvider(assets);
            foreach (var message in corpus.LoaderDiagnostics) diagnostics.Add(message);
        }
        else
        {
            _environment ??= CreateEnvironment(settings);
            assets = _environment.Assets; metrics = _environment.Metrics;
            document = _environment.Loader.Load(prefab, diagnostics, token);
            var installation = new BannerlordInstallationIndex().GetSnapshot(settings.BannerlordInstallRoot);
            var module = ModuleAssetOrder.Owner(installation.Modules, prefab);
            var moduleDirectory = installation.Modules.Single(m => m.Id == module).Directory;
            moduleVersion = System.Xml.Linq.XDocument.Load(Path.Combine(moduleDirectory, "SubModule.xml"))
                .Root?.Element("Version")?.Attribute("value")?.Value ?? throw new InvalidDataException("Owning module version is missing.");
        }
        var loaderMessages = diagnostics.Messages.ToArray();
        var loadTime = watch.Elapsed;
        var allNodes = Descendants(document.RootNode).ToArray();
        var paths = allNodes.Select(n => n.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, n.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var path in scenario.WidgetStates.Keys.Concat(scenario.Lists.Keys))
            if (!paths.Contains(path)) throw new InvalidScenarioException("Scenario selector did not match an expanded widget: " + path);
        if (scenario.ScopeWidgetTypes.Length > 0)
        {
            var targets = allNodes.Where(n => scenario.ScopeWidgetTypes.Contains(n.Name, StringComparer.Ordinal)).ToArray();
            if (targets.Length == 0) throw new InvalidScenarioException("Widget scope did not match any expanded widget.");
            if (targets.Any(t => Descendants(t).Skip(1).Any(n => targets.Contains(n))))
                throw new InvalidScenarioException("Widget scope cannot select both an ancestor and its child.");
            var rootAttributes = new Dictionary<string, string> { [PrefabVisualTreeHelper.DebugWidgetPathAttributeName] = "Window",
                ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = scenario.CaptureProfile.Width.ToString(),
                ["SuggestedHeight"] = scenario.CaptureProfile.Height.ToString() };
            document = document with { RootNode = new WindowNode(rootAttributes, targets) };
        }
        var provider = new ScenarioDataSource(scenario);
        var renderer = new PrefabRenderer(assets, metrics, new BannerlordBrushOverlayFeatureHandler(), provider,
            new FixedRootLayout(new Size(scenario.CaptureProfile.Width, scenario.CaptureProfile.Height)),
            new BannerlordWidgetTextSourceProvider(), new BannerlordWidgetDebugMetadataProvider(), new Bannerlord.XmlPreview.Behaviors.Bannerlord.BannerlordBehaviorRuleProvider());
        token.ThrowIfCancellationRequested();
        watch.Restart();
        var root = renderer.Render(document, diagnostics, new RegressionRenderOptions(scenario.PrefabIdentity, scenario.Bindings,
            scenario.WidgetStates, scenario.KeyBoundsSelectors) { AssetModule = scenario.AssetModule, CancellationToken = token, OptionalConnectionExplanations = scenario.OptionalConnectionExplanations });
        var renderTime = watch.Elapsed;
        if (_archiveDirectory is not null && assets is RecordingAssets recorded)
        {
            Directory.CreateDirectory(_archiveDirectory);
            var corpus = PortableCorpus.FromDocument(document, scenario.PrefabIdentity, recorded,
                loaderMessages.Select(m => m.Replace(settings.BannerlordInstallRoot, "{installation}", StringComparison.OrdinalIgnoreCase)).ToArray(), moduleVersion);
            var json = System.Text.Json.JsonSerializer.Serialize(corpus, new System.Text.Json.JsonSerializerOptions(ScenarioLoader.JsonOptions) { WriteIndented = false })
                .Replace(settings.BannerlordInstallRoot.Replace("\\", "\\\\"), "{installation}", StringComparison.OrdinalIgnoreCase);
            using var corpusFile = File.Create(Path.Combine(_archiveDirectory, scenario.Id + ".json.gz"));
            using var compressed = new System.IO.Compression.GZipStream(corpusFile, System.IO.Compression.CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(compressed);
            writer.Write(json);
        }
        token.ThrowIfCancellationRequested();
        RgbImage? pixels = null;
        TimeSpan? captureTime = null;
        if (mode == VisualRegressionMode.Full)
        {
            watch.Restart();
            pixels = WpfPreviewCapture.Capture(root, scenario.CaptureProfile);
            captureTime = watch.Elapsed;
        }
        var warnings = diagnostics.Messages.Select(message => NormalizeDiagnostic(message, settings.BannerlordInstallRoot)).ToArray();
        var completion = diagnostics.CompletionStatus;
        if (warnings.Any(w => w.Message.Contains("placeholder", StringComparison.OrdinalIgnoreCase)) && completion == RenderCompletionStatus.Complete)
            completion = RenderCompletionStatus.Partial;
        return new(diagnostics.RenderStructure!, diagnostics.Layout!, warnings, completion, pixels,
            new(Load: loadTime, Render: renderTime, Capture: captureTime), moduleVersion) { BehaviorGraph = diagnostics.BehaviorGraph, BehaviorTrace = diagnostics.BehaviorTrace };
    }

    private static CaptureEnvironment CreateEnvironment(FixedSettings settings)
    {
        var index = new BannerlordInstallationIndex();
        var assets = new RecordingAssets(new BannerlordAssetCatalog(settings, index));
        var reflection = new BannerlordWidgetReflectionService(settings, index);
        var metrics = new BannerlordBrushMetricsProvider(assets);
        var factory = new BannerlordWidgetNodeFactory(reflection);
        var loader = new PrefabLoader(assets, metrics, new BannerlordPrefabReferenceResolver(settings, index),
            new BannerlordWidgetPropertyResolver(settings, reflection), new WidgetReferenceResolver(), factory);
        return new(assets, metrics, loader);
    }

    private sealed record CaptureEnvironment(IAssetCatalog Assets, IBrushMetricsProvider Metrics, PrefabLoader Loader);

    public static string ResolvePrefab(string root, string identity)
    {
        var canonical = StableWidgetId.CanonicalPrefab(identity);
        var path = Path.GetFullPath(Path.Combine(root, canonical.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prefab escapes the installation root.");
        return path;
    }

    private static IEnumerable<PrefabNode> Descendants(PrefabNode root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var node in Descendants(child)) yield return node;
    }

    private static CapturedDiagnostic NormalizeDiagnostic(string message, string root)
    {
        var normalized = message.Replace(root, "{installation}", StringComparison.OrdinalIgnoreCase);
        // Legacy diagnostics do not yet carry locations. Keep exact normalized text and a content-derived code;
        // this is an explicit document-scoped warning, never a warning-count allowance.
        var code = "legacy." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return new(code, "$document", normalized);
    }

    private sealed class FixedSettings(string root) : IAppSettingsService
    {
        public string BannerlordInstallRoot => root;
        public bool TrySetBannerlordInstallRoot(string path, out string normalizedPath, out string errorMessage)
        { normalizedPath = root; errorMessage = "Capture installation is immutable."; return false; }
    }

    private sealed class FixedRootLayout(Size size) : IRootLayoutHints
    {
        public Size PreviewSize { get; set; } = size;
        public Size ResolveRootSize(Func<string?, double, double> resolveDouble) => PreviewSize;
    }

    private sealed class ScenarioDataSource(PreviewScenario scenario) : IPreviewDataSourceProvider
    {
        public void SetTemplateItemBindings(PrefabNode node, IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings) =>
            throw new InvalidOperationException("Regression scenarios are read-only.");
        public bool TryGetTemplateItemBindings(PrefabNode node, out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
        {
            var path = node.Attributes.GetValueOrDefault(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, node.Name);
            itemBindings = scenario.Lists.TryGetValue(path, out var items)
                ? items.Select(item => (IReadOnlyDictionary<string, string>)item).ToArray() : [];
            return true;
        }
    }
}
