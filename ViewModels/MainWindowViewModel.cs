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
    private readonly LatestLoadCoordinator<string, LoadedPrefab> _loadCoordinator;
    private readonly RelayCommand _reloadCommand;
    private readonly IRootLayoutHints _rootLayoutHints;
    private string _bannerlordInstallPath = string.Empty;
    private PrefabDocument? _currentDocument;
    private string? _currentPrefabPath;
    private string? _watchedPrefabPath;
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
        PreviewInspectorViewModel previewInspector)
    {
        _prefabLoader = prefabLoader;
        _prefabRenderer = prefabRenderer;
        _fileDialogService = fileDialogService;
        _fileWatcherService = fileWatcherService;
        _appSettingsService = appSettingsService;
        _rootLayoutHints = rootLayoutHints;
        _diagnosticsSnapshotWriter = diagnosticsSnapshotWriter;
        _environmentInitializer = environmentInitializer;
        _loadCoordinator = new LatestLoadCoordinator<string, LoadedPrefab>(
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

        _fileWatcherService.FileChanged += FileWatcherServiceOnFileChanged;
        MockEditor.Changed += (_, _) => ReRenderCurrentDocument();
    }

    #region Events

    private void FileWatcherServiceOnFileChanged(object? sender, FileInfo e)
    {
        if (!string.Equals(e.FullName, _watchedPrefabPath, StringComparison.OrdinalIgnoreCase)) return;

        if (_dispatcher.CheckAccess())
        {
            ReloadPrefab();
            return;
        }

        _dispatcher.BeginInvoke(ReloadPrefab);
    }

    #endregion

    #region Commands

    public ICommand OpenPrefabCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand BrowseBannerlordInstallCommand { get; }

    public ICommand ApplyBannerlordInstallCommand { get; }

    public ICommand ApplyPreviewSizeCommand { get; }

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
        return !string.IsNullOrWhiteSpace(_currentPrefabPath);
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
        if (!string.IsNullOrWhiteSpace(_currentPrefabPath)) _ = LoadPrefabAsync(_currentPrefabPath);
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

        _rootLayoutHints.PreviewSize = requestedSize;
        PreviewWidth = FormatDimension(requestedSize.Width);
        PreviewHeight = FormatDimension(requestedSize.Height);

        if (_currentDocument is not null)
            ReRenderCurrentDocument();
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

        SwitchWatchedFile(path);
        await LoadPrefabAsync(path);
    }

    private async Task LoadPrefabAsync(string path)
    {
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => LoadPrefabAsync(path)).Task.Unwrap();
            return;
        }

        StatusText = $"Loading {Path.GetFileName(path)}...";
        await _loadCoordinator.RunAsync(path);
        _reloadCommand.RaiseCanExecuteChanged();
    }

    private Task<LoadedPrefab> LoadPrefabDocumentAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostics = new PreviewDiagnostics();
                using var diagnosticsTimingScope = diagnostics.BeginTimingScope();
                return new LoadedPrefab(_prefabLoader.Load(path, diagnostics, cancellationToken), diagnostics);
            },
            cancellationToken);
    }

    private void PublishLoadedPrefab(string path, LoadedPrefab loadedPrefab)
    {
        _currentDocument = loadedPrefab.Document;
        _currentDiagnostics = loadedPrefab.Diagnostics;
        using (loadedPrefab.Diagnostics.MeasurePhase("MainWindow.Load.MockEditor"))
            MockEditor.Load(loadedPrefab.Document);
        using (loadedPrefab.Diagnostics.BeginTimingScope())
            RenderPreview(loadedPrefab.Document, loadedPrefab.Diagnostics);
        FilePath = path;
        _currentPrefabPath = path;
        Title = $"{DefaultWindowTitle} - {Path.GetFileName(path)}";
        StatusText = $"Loaded {Path.GetFileName(path)}";
    }

    private void PublishLoadFailure(string path, Exception exception)
    {
        _currentDocument = null;
        _currentDiagnostics = new PreviewDiagnostics();
        _currentDiagnostics.MarkFailed($"Load failed: {exception.Message}");
        PreviewContent = null;
        DiagnosticsText = $"{_currentDiagnostics.ToDisplayText()}{Environment.NewLine}{Environment.NewLine}{exception}";
        MockEditor.Clear();
        PreviewInspector.Reset("No element selected because loading failed.");
        FilePath = path;
        _currentPrefabPath = path;
        Title = DefaultWindowTitle;
        StatusText = "Load failed.";
    }

    private void SwitchWatchedFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.Equals(_watchedPrefabPath, fullPath, StringComparison.OrdinalIgnoreCase)) return;

        if (!string.IsNullOrWhiteSpace(_watchedPrefabPath))
            _fileWatcherService.Stop(new FileInfo(_watchedPrefabPath));

        _fileWatcherService.Start(new FileInfo(fullPath));
        _watchedPrefabPath = fullPath;
    }

    private void ReRenderCurrentDocument()
    {
        if (_currentDocument is null) return;

        var diagnostics = new PreviewDiagnostics();
        RenderPreview(_currentDocument, diagnostics);
        StatusText =
            $"Updated preview for {Path.GetFileName(_currentDocument.SourcePath)} ({FormatSize(_rootLayoutHints.PreviewSize)})";
    }

    private void RenderPreview(PrefabDocument document, PreviewDiagnostics diagnostics)
    {
        _currentDiagnostics = diagnostics;
        using (diagnostics.MeasurePhase("MainWindow.Render.Renderer"))
            PreviewContent = _prefabRenderer.Render(document, diagnostics);
        using (diagnostics.MeasurePhase("MainWindow.Render.PreviewInspector"))
            PreviewInspector.Attach(PreviewContent);
        using (diagnostics.MeasurePhase("MainWindow.Render.DiagnosticsText"))
            DiagnosticsText = diagnostics.ToDisplayText();
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

    private static string FormatDimension(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fileWatcherService.FileChanged -= FileWatcherServiceOnFileChanged;
        if (!string.IsNullOrWhiteSpace(_watchedPrefabPath))
            _fileWatcherService.Stop(new FileInfo(_watchedPrefabPath));
        _loadCoordinator.Dispose();
    }

    private sealed record LoadedPrefab(PrefabDocument Document, PreviewDiagnostics Diagnostics);

    #endregion
}
