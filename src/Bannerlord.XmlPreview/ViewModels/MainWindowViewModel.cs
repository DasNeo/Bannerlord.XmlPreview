using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Infrastructure;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.ViewModels.Mocks;
using Bannerlord.XmlPreview.VisualRegression;
using Bannerlord.XmlPreview.Rendering;
using System.Collections.ObjectModel;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string DefaultWindowTitle = "Bannerlord XML Preview";

    private const string Phase0PrimaryPrefabRelativePath =
        @"Modules\Native\GUI\Prefabs\Standard\Standard.ScrollablePanel.xml";

    private const string Phase0SecondaryPrefabRelativePath =
        @"Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml";

    private readonly RelayCommand _applyBannerlordInstallCommand;
    private readonly RelayCommand _applyPreviewSizeCommand;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IDiagnosticsSnapshotWriter _diagnosticsSnapshotWriter;
    private readonly IPreviewEnvironmentInitializer _environmentInitializer;

    private readonly Dispatcher _dispatcher;
    private readonly IFileDialogService _fileDialogService;
    private readonly IFileWatcherService _fileWatcherService;

    private readonly IPrefabLoader _prefabLoader;
    private readonly IPrefabRenderer _prefabRenderer;
    private readonly LatestLoadCoordinator<LoadRequest, LoadedPrefab> _loadCoordinator;
    private readonly IScenarioRepository _scenarios;
    private readonly ISourceNavigator _navigator;
    private readonly Bannerlord.XmlPreview.Preview.Bannerlord.PreviewBindingStore _bindings;
    private readonly IAssetCatalog _assets;
    private readonly IPreviewDataSourceProvider _previewData;
    private PreviewScenario? _activeScenario;
    private PreviewScenario? _requestedScenario;
    private string? _selectedScenarioId;
    private string _scenarioName = "new-scenario";
    private bool _scenarioDirty;
    private bool _updatingScenarioSelection;
    private long _reloadGeneration;
    private long _cancelledReloads;
    private string[] _changedDependencies = [];
    private string? _requestedPrefabPath;
    private bool _readScenarioFromDisk;
    private readonly RelayCommand _reloadCommand;
    private readonly IRootLayoutHints _rootLayoutHints;
    private string _bannerlordInstallPath = string.Empty;
    private PrefabDocument? _currentDocument;
    private string? _currentPrefabPath;
    private string _diagnosticsText = string.Empty;
    private PreviewDiagnostics? _currentDiagnostics;
    private string _filePath = string.Empty;
    private FrameworkElement? _previewContent;
    private string _previewHeight = string.Empty;
    private string _previewWidth = string.Empty;
    private string _statusText = "Waiting for prefab...";
    private string _title = DefaultWindowTitle;
    private bool _disposed;

    public MainWindowViewModel(
        IPrefabLoader prefabLoader,
        IPrefabRenderer prefabRenderer,
        IFileDialogService fileDialogService,
        IFileWatcherService fileWatcherService,
        IAppSettingsService appSettingsService,
        IRootLayoutHints rootLayoutHints,
        IDiagnosticsSnapshotWriter diagnosticsSnapshotWriter,
        IPreviewEnvironmentInitializer environmentInitializer,
        MockEditorViewModel mockEditor,
        PreviewInspectorViewModel previewInspector,
        IScenarioRepository scenarios,
        ISourceNavigator navigator,
        Bannerlord.XmlPreview.Preview.Bannerlord.PreviewBindingStore bindings,
        IAssetCatalog assets,
        IPreviewDataSourceProvider previewData)
    {
        _prefabLoader = prefabLoader;
        _prefabRenderer = prefabRenderer;
        _fileDialogService = fileDialogService;
        _fileWatcherService = fileWatcherService;
        _appSettingsService = appSettingsService;
        _rootLayoutHints = rootLayoutHints;
        _diagnosticsSnapshotWriter = diagnosticsSnapshotWriter;
        _environmentInitializer = environmentInitializer;
        _scenarios = scenarios; _navigator = navigator; _bindings = bindings; _assets = assets;
        _previewData = previewData;
        _loadCoordinator = new LatestLoadCoordinator<LoadRequest, LoadedPrefab>(
            LoadPrefabDocumentAsync,
            PublishLoadedPrefab,
            PublishLoadFailure);
        MockEditor = mockEditor;
        PreviewInspector = previewInspector;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _bannerlordInstallPath = _appSettingsService.BannerlordInstallRoot;
        _previewWidth = FormatDimension(_rootLayoutHints.PreviewSize.Width);
        _previewHeight = FormatDimension(_rootLayoutHints.PreviewSize.Height);

        OpenPrefabCommand = new RelayCommand(OpenPrefab);
        _reloadCommand = new RelayCommand(ReloadPrefab, CanReloadPrefab);
        ReloadCommand = _reloadCommand;
        BrowseBannerlordInstallCommand = new RelayCommand(BrowseBannerlordInstall);
        _applyBannerlordInstallCommand = new RelayCommand(ApplyBannerlordInstall, CanApplyBannerlordInstall);
        _applyPreviewSizeCommand = new RelayCommand(ApplyPreviewSize, CanApplyPreviewSize);
        ApplyBannerlordInstallCommand = _applyBannerlordInstallCommand;
        ApplyPreviewSizeCommand = _applyPreviewSizeCommand;

        _fileWatcherService.DependenciesChanged += FileWatcherServiceOnFileChanged;
        MockEditor.Changed += MockEditorChanged;
        RefreshScenarios();
        CreateScenarioCommand = new RelayCommand(() => ScenarioOperation(CreateScenario));
        SaveScenarioCommand = new RelayCommand(() => ScenarioOperation(SaveScenario));
        CloneScenarioCommand = new RelayCommand(() => ScenarioOperation(() => { RequireSavedScenario(); SelectScenario(_scenarios.Clone(_activeScenario!.Id, ScenarioName)); }));
        RenameScenarioCommand = new RelayCommand(() => ScenarioOperation(() => { RequireSavedScenario(); SelectScenario(_scenarios.Rename(_activeScenario!.Id, ScenarioName)); }));
        DeleteScenarioCommand = new RelayCommand(() => ScenarioOperation(() => { RequireSavedScenario(); _scenarios.Delete(_activeScenario!.Id); SelectScenario(null); }));
        EditScenarioCommand = new RelayCommand(() => ScenarioOperation(() => { RequireSavedScenario(); _navigator.Open(new(_scenarios.PathFor(_activeScenario!.Id), 1)); }));
        OpenSourceCommand = new RelayCommand(() => ScenarioOperation(() => _navigator.Open(SelectedSource ?? new(_currentPrefabPath ?? throw new InvalidOperationException("Open a prefab first."), 1))));
        CopySourceCommand = new RelayCommand(() => ScenarioOperation(() => _navigator.CopyPath(SelectedSource ?? new(_currentPrefabPath ?? throw new InvalidOperationException("Open a prefab first."), 1))));
        ShowComparisonCommand = new RelayCommand(() => ScenarioOperation(ShowComparison));
        OpenComparisonReportCommand = new RelayCommand(() => ScenarioOperation(OpenComparisonReport));
        ShowBehaviorTraceCommand = new RelayCommand(() => ScenarioOperation(ShowBehaviorTrace));
    }

    #region Events

    private void FileWatcherServiceOnFileChanged(object? sender, IReadOnlyList<string> paths)
    {
        void Reload()
        {
            if (_disposed) return;
            _changedDependencies = paths.ToArray();
            _assets.Invalidate();
            _prefabLoader.Invalidate();
            if (_requestedScenario is not null && paths.Contains(_scenarios.PathFor(_requestedScenario.Id), StringComparer.OrdinalIgnoreCase))
                _readScenarioFromDisk = true;
            ReloadPrefab();
        }
        if (_dispatcher.CheckAccess()) Reload(); else _dispatcher.BeginInvoke(Reload);
    }

    #endregion

    #region Commands

    public ICommand OpenPrefabCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand BrowseBannerlordInstallCommand { get; }

    public ICommand ApplyBannerlordInstallCommand { get; }

    public ICommand ApplyPreviewSizeCommand { get; }
    public ICommand CreateScenarioCommand { get; }
    public ICommand SaveScenarioCommand { get; }
    public ICommand CloneScenarioCommand { get; }
    public ICommand RenameScenarioCommand { get; }
    public ICommand DeleteScenarioCommand { get; }
    public ICommand EditScenarioCommand { get; }
    public ICommand OpenSourceCommand { get; }
    public ICommand CopySourceCommand { get; }
    public ICommand ShowComparisonCommand { get; }
    public ICommand OpenComparisonReportCommand { get; }
    public ICommand ShowBehaviorTraceCommand { get; }
    public ObservableCollection<AuthoringDiagnosticViewModel> AuthoringDiagnostics { get; } = [];
    public ObservableCollection<string> ScenarioIds { get; } = [];
    public string ScenarioName { get => _scenarioName; set => SetProperty(ref _scenarioName, value); }
    public bool ScenarioDirty { get => _scenarioDirty; private set { SetProperty(ref _scenarioDirty, value); OnPropertyChanged(nameof(ScenarioStatus)); } }
    public string ScenarioStatus => _activeScenario is null ? "Interactive (unsaved)" : $"{_activeScenario.Id}, schema {_activeScenario.SchemaVersion} — {(ScenarioDirty ? "Unsaved changes" : "Saved")}";
    public string? SelectedScenarioId
    {
        get => _selectedScenarioId;
        set
        {
            if (_updatingScenarioSelection || value == _selectedScenarioId) return;
            ScenarioOperation(() => { if (ScenarioDirty) throw new InvalidOperationException("Save the active scenario before switching."); SelectScenario(value is null ? null : _scenarios.Load(value)); });
            OnPropertyChanged();
        }
    }
    public SourceLocation? SelectedSource { get; set; }

    #endregion

    #region Properties

    public FrameworkElement? PreviewContent
    {
        get => _previewContent;
        set => SetProperty(ref _previewContent, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DiagnosticsText
    {
        get => _diagnosticsText;
        set => SetProperty(ref _diagnosticsText, value);
    }

    public MockEditorViewModel MockEditor { get; }

    public PreviewInspectorViewModel PreviewInspector { get; }

    public string BannerlordInstallPath
    {
        get => _bannerlordInstallPath;
        set
        {
            if (SetProperty(ref _bannerlordInstallPath, value)) _applyBannerlordInstallCommand.RaiseCanExecuteChanged();
        }
    }

    public string PreviewWidth
    {
        get => _previewWidth;
        set
        {
            if (SetProperty(ref _previewWidth, value)) _applyPreviewSizeCommand.RaiseCanExecuteChanged();
        }
    }

    public string PreviewHeight
    {
        get => _previewHeight;
        set
        {
            if (SetProperty(ref _previewHeight, value)) _applyPreviewSizeCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion

    #region Methods

    public void ApplyStartupOptions(string? bannerlordInstallPath, string? prefabPath)
    {
        _ = ApplyStartupOptionsAsync(bannerlordInstallPath, prefabPath);
    }

    public async Task ApplyStartupOptionsAsync(string? bannerlordInstallPath, string? prefabPath)
    {
        if (!string.IsNullOrWhiteSpace(bannerlordInstallPath))
        {
            BannerlordInstallPath = bannerlordInstallPath;
            ApplyBannerlordInstall();
        }

        StartEnvironmentWarmUp();
        if (!string.IsNullOrWhiteSpace(prefabPath))
            await OpenPrefabPathAsync(prefabPath);
    }

    public void ExportDiagnosticsSnapshot(string outputPath)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => ExportDiagnosticsSnapshot(outputPath));
            return;
        }

        _diagnosticsSnapshotWriter.Write(
            outputPath,
            new DiagnosticsSnapshot(
                StatusText,
                PrefabPath: _currentPrefabPath,
                BannerlordRoot: BannerlordInstallPath,
                DiagnosticsText: DiagnosticsText,
                CompletionStatus: _currentDiagnostics?.CompletionStatus,
                TruncationReasons: _currentDiagnostics?.TruncationReasons));
    }

    private void OpenPrefab()
    {
        var selectedPath = _fileDialogService.OpenPrefab(GetOpenPrefabSeedPath());
        if (!string.IsNullOrWhiteSpace(selectedPath)) _ = OpenPrefabPathAsync(selectedPath);
    }

    private string GetOpenPrefabSeedPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentPrefabPath)) return _currentPrefabPath;

        if (!string.IsNullOrWhiteSpace(BannerlordInstallPath) &&
            Directory.Exists(BannerlordInstallPath))
        {
            var primaryPhase0Path = Path.Combine(BannerlordInstallPath, Phase0PrimaryPrefabRelativePath);
            if (File.Exists(primaryPhase0Path)) return primaryPhase0Path;

            var secondaryPhase0Path = Path.Combine(BannerlordInstallPath, Phase0SecondaryPrefabRelativePath);
            if (File.Exists(secondaryPhase0Path)) return secondaryPhase0Path;
        }

        return BannerlordInstallPath;
    }

    private bool CanReloadPrefab()
    {
        return !string.IsNullOrWhiteSpace(_requestedPrefabPath ?? _currentPrefabPath);
    }

    private bool CanApplyBannerlordInstall()
    {
        return !string.IsNullOrWhiteSpace(BannerlordInstallPath);
    }

    private bool CanApplyPreviewSize()
    {
        return TryGetRequestedPreviewSize(out _);
    }

    private void ReloadPrefab()
    {
        var path = _requestedPrefabPath ?? _currentPrefabPath;
        if (_requestedScenario is not null && !ScenarioDirty) _readScenarioFromDisk = true;
        if (!string.IsNullOrWhiteSpace(path)) _ = LoadPrefabAsync(path);
    }

    private void BrowseBannerlordInstall()
    {
        var selectedPath = _fileDialogService.OpenFolder(BannerlordInstallPath, "Select Bannerlord Installation");
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            BannerlordInstallPath = selectedPath;
            ApplyBannerlordInstall();
        }
    }

    private void ApplyBannerlordInstall()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(ApplyBannerlordInstall);
            return;
        }

        if (!_appSettingsService.TrySetBannerlordInstallRoot(BannerlordInstallPath, out var normalizedPath,
                out var errorMessage))
        {
            StatusText = errorMessage;
            return;
        }

        BannerlordInstallPath = normalizedPath;
        StatusText = $"Bannerlord installation set to {normalizedPath}";
        StartEnvironmentWarmUp();

        if (!string.IsNullOrWhiteSpace(_currentPrefabPath)) _ = LoadPrefabAsync(_currentPrefabPath);
    }

    private void ApplyPreviewSize()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(ApplyPreviewSize);
            return;
        }

        if (!TryGetRequestedPreviewSize(out var requestedSize))
        {
            StatusText = "Preview size must contain positive numbers for width and height.";
            return;
        }

        if (_activeScenario is not null)
        {
            if (requestedSize.Width != Math.Truncate(requestedSize.Width) || requestedSize.Height != Math.Truncate(requestedSize.Height))
            { StatusText = "Scenario resolution requires whole pixel dimensions."; return; }
            var draft = _requestedScenario ?? _activeScenario;
            _requestedScenario = draft with { CaptureProfile = draft.CaptureProfile with { Width = (int)requestedSize.Width, Height = (int)requestedSize.Height } };
            ScenarioDirty = true;
        }
        else _rootLayoutHints.PreviewSize = requestedSize;
        PreviewWidth = FormatDimension(requestedSize.Width);
        PreviewHeight = FormatDimension(requestedSize.Height);

        if (_currentDocument is not null)
            _ = LoadPrefabAsync(_currentDocument.SourcePath);
        else
            StatusText = $"Preview size set to {FormatSize(requestedSize)}";
    }

    private async Task OpenPrefabPathAsync(string path)
    {
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => OpenPrefabPathAsync(path)).Task.Unwrap();
            return;
        }

        if (ScenarioDirty) { StatusText = "Save the active scenario before opening another prefab."; return; }
        _requestedPrefabPath = Path.GetFullPath(path);
        _requestedScenario = null;
        await LoadPrefabAsync(path);
    }

    private async Task LoadPrefabAsync(string path)
    {
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => LoadPrefabAsync(path)).Task.Unwrap();
            return;
        }

        IsPreviewStale = PreviewContent is not null;
        StatusText = $"Loading {Path.GetFileName(path)}..." + (IsPreviewStale ? " Last Good Preview is stale." : "");
        var generation = ++_reloadGeneration;
        var request = new LoadRequest(path, _requestedScenario, _readScenarioFromDisk, ScenarioDirty);
        _readScenarioFromDisk = false;
        await _loadCoordinator.RunAsync(request);
        if (generation != _reloadGeneration)
        {
            _cancelledReloads++;
            Debug.WriteLine($"Reload generation {generation} cancelled by {_reloadGeneration}.");
        }
        _reloadCommand.RaiseCanExecuteChanged();
    }

    private Task<LoadedPrefab> LoadPrefabDocumentAsync(LoadRequest request, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostics = new PreviewDiagnostics();
                using var diagnosticsTimingScope = diagnostics.BeginTimingScope();
                PrefabDocument? document = null;
                try
                {
                    document = _prefabLoader.Load(request.Path, diagnostics, cancellationToken);
                    var scenario = request.Scenario;
                    if (request.ReadScenarioFromDisk && scenario is not null)
                    {
                        if (request.Dirty) throw new InvalidOperationException("Scenario changed externally while it has unsaved changes. Save the active scenario before reloading it.");
                        scenario = _scenarios.Load(scenario.Id);
                    }
                    if (scenario is not null) _ = ScenarioApplication.Apply(document, scenario);
                    return new LoadedPrefab(document, diagnostics, scenario);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { throw new PreviewLoadException(exception, diagnostics, document); }
            },
            cancellationToken);
    }

    private void PublishLoadedPrefab(LoadRequest request, LoadedPrefab loadedPrefab)
    {
        var scenario = loadedPrefab.Scenario;
        var candidate = RenderCandidate(loadedPrefab.Document, loadedPrefab.Diagnostics, scenario);
        var dependencies = loadedPrefab.Document.Dependencies.Concat(loadedPrefab.Diagnostics.Dependencies).Append(request.Path);
        if (scenario is not null) dependencies = dependencies.Append(_scenarios.PathFor(scenario.Id));
        _fileWatcherService.ReplaceDependencies(dependencies);
        // Commit all preview state only after loading, rendering and watcher preparation succeed.
        _currentDocument = loadedPrefab.Document;
        _currentDiagnostics = loadedPrefab.Diagnostics;
        _activeScenario = scenario;
        _requestedScenario = scenario;
        if (scenario is not null) _rootLayoutHints.PreviewSize = new(scenario.CaptureProfile.Width, scenario.CaptureProfile.Height);
        _bindings.Replace(scenario is null ? _bindings.Capture().Bindings : scenario.Bindings);
        MockEditor.Load(loadedPrefab.Document, scenario);
        PreviewContent = candidate;
        SelectedSource = null;
        PreviewInspector.Attach(candidate);
        IsPreviewStale = false;
        _selectedScenarioId = scenario?.Id;
        ScenarioDirty = request.Dirty && !request.ReadScenarioFromDisk;
        PreviewWidth = FormatDimension(_rootLayoutHints.PreviewSize.Width);
        PreviewHeight = FormatDimension(_rootLayoutHints.PreviewSize.Height);
        OnPropertyChanged(nameof(SelectedScenarioId)); OnPropertyChanged(nameof(ScenarioStatus));
        FilePath = request.Path;
        _currentPrefabPath = request.Path;
        _requestedPrefabPath = request.Path;
        Title = $"{DefaultWindowTitle} - {Path.GetFileName(request.Path)}";
        DiagnosticsText = AuthoringDiagnosticText(loadedPrefab.Diagnostics);
        PopulateDiagnosticSources(loadedPrefab.Diagnostics, loadedPrefab.Document, new(request.Path, 1));
        StatusText = $"Loaded {Path.GetFileName(request.Path)}";
    }

    private void PublishLoadFailure(LoadRequest request, Exception exception)
    {
        _currentDiagnostics = exception is PreviewLoadException failure ? failure.Diagnostics : new PreviewDiagnostics();
        _currentDiagnostics.MarkFailed($"Load failed: {exception.Message}");
        IsPreviewStale = PreviewContent is not null;
        DiagnosticsText = $"{AuthoringDiagnosticText(_currentDiagnostics)}{Environment.NewLine}Stale reason: {exception.Message}{Environment.NewLine}{exception}";
        FilePath = request.Path;
        if (exception.GetBaseException() is System.Xml.XmlException xml) SelectedSource = new(!string.IsNullOrEmpty(xml.SourceUri) && Uri.TryCreate(xml.SourceUri, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : request.Path, xml.LineNumber, xml.LinePosition);
        else if (exception.GetBaseException() is System.Text.Json.JsonException json && request.Scenario is not null)
            SelectedSource = new(_scenarios.PathFor(request.Scenario.Id), (int)(json.LineNumber ?? 0) + 1);
        else SelectedSource = new(request.Path, 1);
        PopulateDiagnosticSources(_currentDiagnostics, exception is PreviewLoadException loadFailure ? loadFailure.Document ?? _currentDocument : _currentDocument, SelectedSource);
        // With no Last Good Preview, watching the requested root permits automatic recovery.
        if (_currentDocument is null) _fileWatcherService.ReplaceDependencies([request.Path]);
        StatusText = IsPreviewStale ? "Load failed. Last Good Preview is stale." : "Load failed.";
    }

    private bool _isPreviewStale;
    public bool IsPreviewStale { get => _isPreviewStale; private set => SetProperty(ref _isPreviewStale, value); }

    private FrameworkElement RenderCandidate(PrefabDocument document, PreviewDiagnostics diagnostics, PreviewScenario? scenario)
    {
        try
        {
            var candidateDocument = scenario is null ? document : ScenarioApplication.Apply(document, scenario);
            var candidate = _prefabRenderer.Render(candidateDocument, diagnostics,
                scenario is null ? null : ScenarioApplication.Options(scenario) with { DeferHiddenSubtrees = true });
            if (diagnostics.CompletionStatus == RenderCompletionStatus.Failed) throw new InvalidDataException("Render failed validation.");
            return candidate;
        }
        catch (Exception exception) { throw new PreviewLoadException(exception, diagnostics, document); }
    }

    private void StartEnvironmentWarmUp()
    {
        _ = WarmEnvironmentAsync();
    }

    private async Task WarmEnvironmentAsync()
    {
        try
        {
            await _environmentInitializer.WarmUpAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Preview environment warm-up failed: {exception}");
        }
    }

    private bool TryGetRequestedPreviewSize(out Size size)
    {
        size = default;
        if (!TryParsePositiveDimension(PreviewWidth, out var width) ||
            !TryParsePositiveDimension(PreviewHeight, out var height))
            return false;

        size = new Size(width, height);
        return true;
    }

    private static bool TryParsePositiveDimension(string? text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value) &&
               value > 0d;
    }

    private static string FormatSize(Size size)
    {
        return $"{FormatDimension(size.Width)} x {FormatDimension(size.Height)}";
    }

    private void ScenarioOperation(Action action)
    {
        try { action(); }
        catch (Exception exception) { StatusText = exception.Message; DiagnosticsText = $"Authoring error: {exception.Message}\n" + DiagnosticsText; }
    }
    private void RefreshScenarios()
    {
        _updatingScenarioSelection = true;
        try { ScenarioIds.Clear(); foreach (var id in _scenarios.List()) ScenarioIds.Add(id); }
        finally { _updatingScenarioSelection = false; }
    }
    private void SelectScenario(PreviewScenario? scenario)
    {
        if (_currentDocument is null) throw new InvalidOperationException("Open a prefab before selecting a scenario.");
        if (scenario is not null && StableWidgetId.CanonicalPrefab(scenario.PrefabIdentity) != StableWidgetId.CanonicalPrefab(_currentDocument.PrefabIdentity ?? Path.GetFileName(_currentDocument.SourcePath)))
            throw new InvalidDataException("Scenario belongs to another prefab.");
        _requestedScenario = scenario;
        RefreshScenarios();
        _ = LoadPrefabAsync(_currentDocument.SourcePath);
    }
    private void RequireSavedScenario()
    {
        if (_activeScenario is null) throw new InvalidOperationException("Create or select a scenario first.");
        if (ScenarioDirty) throw new InvalidOperationException("Save the active scenario first.");
    }
    private void CreateScenario()
    {
        if (_currentDocument is null) throw new InvalidOperationException("Open a prefab first.");
        if (ScenarioDirty) throw new InvalidOperationException("Save the active scenario first.");
        var scenario = new PreviewScenario
        {
            Id = ScenarioName, PrefabIdentity = _currentDocument.PrefabIdentity ?? Path.GetFileName(_currentDocument.SourcePath),
            CaptureProfile = new("authoring", (int)_rootLayoutHints.PreviewSize.Width, (int)_rootLayoutHints.PreviewSize.Height),
            Scene = "Authoring preview", DataState = "Interactive values",
            Bindings = new(_bindings.Capture().Bindings, StringComparer.OrdinalIgnoreCase), Lists = MockEditor.CaptureLists(),
            OptionalConnectionExplanations = new(_previewData.CaptureSnapshot(_currentDocument.RootNode).OptionalConnectionExplanations, StringComparer.Ordinal)
        };
        SelectScenario(_scenarios.Create(scenario));
    }
    private void SaveScenario()
    {
        if (_activeScenario is null) { CreateScenario(); return; }
        _scenarios.Save(_requestedScenario ?? _activeScenario);
        ScenarioDirty = false;
        StatusText = $"Saved {_activeScenario.Id}";
    }
    private void MockEditorChanged(object? sender, EventArgs e)
    {
        if (_activeScenario is not null)
        {
            var draft = _requestedScenario ?? _activeScenario;
            _requestedScenario = draft with { Bindings = new(_bindings.Capture().Bindings, StringComparer.OrdinalIgnoreCase), Lists = MockEditor.CaptureLists() };
            ScenarioDirty = true;
        }
        if (_currentDocument is not null) _ = LoadPrefabAsync(_currentDocument.SourcePath);
    }
    private string AuthoringDiagnosticText(PreviewDiagnostics diagnostics) =>
        $"Preview Scenario: {ScenarioStatus}\nReload generation: {_reloadGeneration}; cancelled obsolete reloads: {_cancelledReloads}\nChanged dependencies: {string.Join(", ", _changedDependencies)}\nSource: {SelectedSource?.FilePath ?? _currentDocument?.SourcePath}\n" + diagnostics.ToDisplayText();
    private void PopulateDiagnosticSources(PreviewDiagnostics diagnostics, PrefabDocument? document, SourceLocation fallback)
    {
        AuthoringDiagnostics.Clear();
        var sources = document?.SourceMap.OrderByDescending(pair => pair.Key.Length).ToArray();
        foreach (var message in diagnostics.Messages)
        {
            var source = sources?.FirstOrDefault(pair => message.Contains(pair.Key, StringComparison.Ordinal)).Value ?? fallback;
            AuthoringDiagnostics.Add(new(message, source, _navigator, ScenarioOperation));
        }
    }
    private void ShowBehaviorTrace()
    {
        if (_currentDocument is null) throw new InvalidOperationException("Open a prefab first.");
        var diagnostics = IsPreviewStale && _currentDiagnostics is { } failed && !failed.BehaviorTrace.IsEmpty ? failed : new PreviewDiagnostics();
        var scenario = _activeScenario;
        var options = scenario is null ? new RegressionRenderOptions(_currentDocument.PrefabIdentity ?? Path.GetFileName(_currentDocument.SourcePath),
            _bindings.Capture().Bindings, new Dictionary<string, IReadOnlyDictionary<string, string>>(), []) { TraceBehaviors = true }
            : ScenarioApplication.Options(scenario) with { TraceBehaviors = true };
        if (diagnostics.BehaviorTrace.IsEmpty)
        {
            try { _ = _prefabRenderer.Render(scenario is null ? _currentDocument : ScenarioApplication.Apply(_currentDocument, scenario), diagnostics, options); }
            catch when (!diagnostics.BehaviorTrace.IsEmpty) { }
        }
        var traceText = System.Text.Json.JsonSerializer.Serialize(diagnostics.BehaviorTrace, ScenarioLoader.JsonOptions);
        var text = new System.Windows.Controls.TextBox { Text = traceText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        new Window { Title = "Behavior Trace", Width = 900, Height = 700, Content = text, Owner = Application.Current?.MainWindow }.Show();
    }
    private void ShowComparison()
    {
        if (PreviewContent is null || _activeScenario is null) throw new InvalidOperationException("Select a persistent scenario and load a preview first.");
        if (IsPreviewStale) throw new InvalidOperationException("The preview is stale. Complete a successful reload before comparing the current preview.");
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Reference screenshot (*.png)|*.png", Title = "Open reference for the active scenario" };
        if (dialog.ShowDialog() != true) return;
        var comparison = ComparisonPresenter.Compare(dialog.FileName, PreviewContent, _activeScenario, _currentDiagnostics?.Layout);
        var window = new ComparisonWindow(comparison, _activeScenario, bounds =>
        {
            if (_currentDocument?.SourceMap.GetValueOrDefault(bounds.ExpandedPath) is { } source)
            { SelectedSource = source; PreviewInspector.SelectSource(source); }
        });
        window.Owner = Application.Current?.MainWindow;
        window.Show();
        DiagnosticsText = $"Image comparison: {comparison.Summary}\n" + AuthoringDiagnosticText(_currentDiagnostics!);
    }
    private void OpenComparisonReport()
    {
        RequireSavedScenario();
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Visual Regression report (*.json)|*.json", Title = "Open comparison report for the active scenario" };
        if (dialog.ShowDialog() != true) return;
        var comparison = ComparisonPresenter.LoadArtifacts(dialog.FileName, _scenarios.PathFor(_activeScenario!.Id));
        new ComparisonWindow(comparison, _activeScenario, bounds =>
        {
            if (_currentDocument?.SourceMap.GetValueOrDefault(bounds.ExpandedPath) is { } source)
            { SelectedSource = source; PreviewInspector.SelectSource(source); }
        }) { Owner = Application.Current?.MainWindow }.Show();
        DiagnosticsText = $"Visual comparison: {comparison.Summary}\n" + AuthoringDiagnosticText(_currentDiagnostics!);
    }

    private static string FormatDimension(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fileWatcherService.DependenciesChanged -= FileWatcherServiceOnFileChanged;
        MockEditor.Changed -= MockEditorChanged;
        _fileWatcherService.ReplaceDependencies([]);
        _loadCoordinator.Dispose();
    }

    private sealed record LoadedPrefab(PrefabDocument Document, PreviewDiagnostics Diagnostics, PreviewScenario? Scenario);
    private sealed record LoadRequest(string Path, PreviewScenario? Scenario, bool ReadScenarioFromDisk = false, bool Dirty = false);
    private sealed class PreviewLoadException(Exception inner, PreviewDiagnostics diagnostics, PrefabDocument? document)
        : Exception(inner.Message, inner)
    {
        public PreviewDiagnostics Diagnostics { get; } = diagnostics;
        public PrefabDocument? Document { get; } = document;
    }

    #endregion
}
