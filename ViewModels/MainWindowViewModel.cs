using Bannerlord.XmlPreview.Infrastructure;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Services;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Rendering;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Collections.ObjectModel;
using System.Windows.Documents;
using System.Text;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string DefaultWindowTitle = "Bannerlord XML Preview";
    private const string Phase0PrimaryPrefabRelativePath =
        @"Modules\Native\GUI\Prefabs\Standard\Standard.ScrollablePanel.xml";
    private const string Phase0SecondaryPrefabRelativePath =
        @"Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml";

    private readonly IPrefabLoader _prefabLoader;
    private readonly IPrefabRenderer _prefabRenderer;
    private readonly IFileDialogService _fileDialogService;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly RelayCommand _reloadCommand;
    private readonly RelayCommand _applyBannerlordInstallCommand;
    private readonly Dispatcher _dispatcher;

    private readonly ObservableCollection<EditableBindingViewModel> _bindings = [];
    private readonly ObservableCollection<PreviewTreeNodeViewModel> _previewTree = [];
    private readonly Dictionary<FrameworkElement, PreviewTreeNodeViewModel> _previewTreeLookup = [];
    private FrameworkElement? _previewContent;
    private PrefabDocument? _currentDocument;
    private string? _currentPrefabPath;
    private string _title = DefaultWindowTitle;
    private string _filePath = string.Empty;
    private string _statusText = "Waiting for prefab...";
    private string _diagnosticsText = string.Empty;
    private string _selectedElementDebugText = "Click an element in the preview to inspect its resolved assets.";
    private string _bannerlordInstallPath = string.Empty;
    private FrameworkElement? _interactivePreviewRoot;
    private FrameworkElement? _selectedPreviewElement;
    private AdornerLayer? _selectedPreviewAdornerLayer;
    private SelectionBorderAdorner? _selectedPreviewAdorner;
    private PreviewTreeNodeViewModel? _selectedPreviewTreeNode;
    private bool _isPopulatingBindings;
    private bool _isSynchronizingSelection;

    public MainWindowViewModel(
        IPrefabLoader prefabLoader,
        IPrefabRenderer prefabRenderer,
        IFileDialogService fileDialogService,
        IFileWatcherService fileWatcherService,
        IAppSettingsService appSettingsService)
    {
        _prefabLoader = prefabLoader;
        _prefabRenderer = prefabRenderer;
        _fileDialogService = fileDialogService;
        _fileWatcherService = fileWatcherService;
        _appSettingsService = appSettingsService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _bannerlordInstallPath = _appSettingsService.BannerlordInstallRoot;

        OpenPrefabCommand = new RelayCommand(OpenPrefab);
        _reloadCommand = new RelayCommand(ReloadPrefab, CanReloadPrefab);
        ReloadCommand = _reloadCommand;
        BrowseBannerlordInstallCommand = new RelayCommand(BrowseBannerlordInstall);
        _applyBannerlordInstallCommand = new RelayCommand(ApplyBannerlordInstall, CanApplyBannerlordInstall);
        ApplyBannerlordInstallCommand = _applyBannerlordInstallCommand;
        
        _fileWatcherService.FileChanged += FileWatcherServiceOnFileChanged;
    }
    
    #region Events
    private void FileWatcherServiceOnFileChanged(object? sender, FileInfo e)
    {
        if (!string.Equals(e.FullName, _filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

    public string SelectedElementDebugText
    {
        get => _selectedElementDebugText;
        set => SetProperty(ref _selectedElementDebugText, value);
    }

    public ObservableCollection<EditableBindingViewModel> Bindings => _bindings;

    public ObservableCollection<PreviewTreeNodeViewModel> PreviewTree => _previewTree;

    public string BannerlordInstallPath
    {
        get => _bannerlordInstallPath;
        set
        {
            if (SetProperty(ref _bannerlordInstallPath, value))
            {
                _applyBannerlordInstallCommand.RaiseCanExecuteChanged();
            }
        }
    }
    #endregion

    #region Methods
    public void ApplyStartupOptions(string? bannerlordInstallPath, string? prefabPath)
    {
        if (!string.IsNullOrWhiteSpace(bannerlordInstallPath))
        {
            BannerlordInstallPath = bannerlordInstallPath;
            ApplyBannerlordInstall();
        }

        if (!string.IsNullOrWhiteSpace(prefabPath))
        {
            OpenPrefabPath(prefabPath);
        }
    }

    public void ExportDiagnosticsSnapshot(string outputPath)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => ExportDiagnosticsSnapshot(outputPath));
            return;
        }

        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var lines = new List<string>
        {
            $"GeneratedAtUtc: {DateTime.UtcNow:O}",
            $"Status: {StatusText}"
        };

        if (!string.IsNullOrWhiteSpace(_currentPrefabPath))
        {
            lines.Add($"Prefab: {_currentPrefabPath}");
        }

        if (!string.IsNullOrWhiteSpace(BannerlordInstallPath))
        {
            lines.Add($"BannerlordRoot: {BannerlordInstallPath}");
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(DiagnosticsText)
            ? "No diagnostics were generated."
            : DiagnosticsText);

        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
    }

    private void OpenPrefab()
    {
        var selectedPath = _fileDialogService.OpenPrefab(GetOpenPrefabSeedPath());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            OpenPrefabPath(selectedPath);
        }
    }

    private string GetOpenPrefabSeedPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentPrefabPath))
        {
            return _currentPrefabPath;
        }

        if (!string.IsNullOrWhiteSpace(BannerlordInstallPath) &&
            Directory.Exists(BannerlordInstallPath))
        {
            var primaryPhase0Path = Path.Combine(BannerlordInstallPath, Phase0PrimaryPrefabRelativePath);
            if (File.Exists(primaryPhase0Path))
            {
                return primaryPhase0Path;
            }

            var secondaryPhase0Path = Path.Combine(BannerlordInstallPath, Phase0SecondaryPrefabRelativePath);
            if (File.Exists(secondaryPhase0Path))
            {
                return secondaryPhase0Path;
            }
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

    private void ReloadPrefab()
    {
        if (!string.IsNullOrWhiteSpace(_currentPrefabPath))
        {
            LoadPrefab(_currentPrefabPath);
        }
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

        if (!_appSettingsService.TrySetBannerlordInstallRoot(BannerlordInstallPath, out var normalizedPath, out var errorMessage))
        {
            StatusText = errorMessage;
            return;
        }

        BannerlordInstallPath = normalizedPath;
        StatusText = $"Bannerlord installation set to {normalizedPath}";

        if (!string.IsNullOrWhiteSpace(_currentPrefabPath))
        {
            LoadPrefab(_currentPrefabPath);
        }
    }

    private void OpenPrefabPath(string path)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => OpenPrefabPath(path));
            return;
        }

        SwitchWatchedFile(path);
        LoadPrefab(path);
    }

    private void LoadPrefab(string path)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => LoadPrefab(path));
            return;
        }

        try
        {
            var diagnostics = new PreviewDiagnostics();
            var document = _prefabLoader.Load(path, diagnostics);
            _currentDocument = document;
            PopulateBindings(document);
            RenderPreview(document, diagnostics);
            FilePath = path;
            _currentPrefabPath = path;
            Title = $"{DefaultWindowTitle} - {Path.GetFileName(path)}";
            StatusText = $"Loaded {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            _currentDocument = null;
            ClearPreviewSelection();
            PreviewContent = null;
            AttachPreviewInteractions(null);
            DiagnosticsText = exception.ToString();
            _bindings.Clear();
            ClearPreviewTree();
            SelectedElementDebugText = "No element selected because loading failed.";
            FilePath = path;
            _currentPrefabPath = path;
            Title = DefaultWindowTitle;
            StatusText = "Load failed.";
        }
        finally
        {
            _reloadCommand.RaiseCanExecuteChanged();
        }
    }

    private void SwitchWatchedFile(string path)
    {
        if (string.Equals(_currentPrefabPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentPrefabPath))
        {
            _fileWatcherService.Stop(new FileInfo(_currentPrefabPath));
        }

        _fileWatcherService.Start(new FileInfo(path));
    }

    private void AttachPreviewInteractions(FrameworkElement? root)
    {
        if (_interactivePreviewRoot is not null)
        {
            _interactivePreviewRoot.PreviewMouseLeftButtonDown -= PreviewRootOnPreviewMouseLeftButtonDown;
        }

        _interactivePreviewRoot = root;
        if (_interactivePreviewRoot is not null)
        {
            _interactivePreviewRoot.PreviewMouseLeftButtonDown += PreviewRootOnPreviewMouseLeftButtonDown;
        }
    }

    private void PreviewRootOnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        var element = FindDebugElement(originalSource);
        if (element is null)
        {
            return;
        }

        SelectPreviewElement(element);
    }

    private FrameworkElement? FindDebugElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element &&
                !string.IsNullOrWhiteSpace(PreviewElementDebugProperties.GetDebugText(element)))
            {
                return element;
            }

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }

    private void SelectPreviewElement(FrameworkElement element)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            if (!ReferenceEquals(_selectedPreviewElement, element))
            {
                ClearPreviewSelection();

                _selectedPreviewElement = element;
                _selectedPreviewAdornerLayer = AdornerLayer.GetAdornerLayer(element);
                if (_selectedPreviewAdornerLayer is not null)
                {
                    _selectedPreviewAdorner = new SelectionBorderAdorner(element);
                    _selectedPreviewAdornerLayer.Add(_selectedPreviewAdorner);
                }
            }

            SelectedElementDebugText = PreviewElementDebugProperties.GetDebugText(element);
            SetSelectedPreviewTreeNode(_previewTreeLookup.GetValueOrDefault(element));
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void ClearPreviewSelection()
    {
        if (_selectedPreviewAdornerLayer is not null && _selectedPreviewAdorner is not null)
        {
            _selectedPreviewAdornerLayer.Remove(_selectedPreviewAdorner);
        }

        _selectedPreviewElement = null;
        _selectedPreviewAdornerLayer = null;
        _selectedPreviewAdorner = null;
        SetSelectedPreviewTreeNode(null);
    }

    private void PopulateBindings(PrefabDocument document)
    {
        _isPopulatingBindings = true;
        try
        {
            _bindings.Clear();
            foreach (var key in document.BindingKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                var value = PrefabRenderContext.ResolveMockBindingValue(key);
                PrefabRenderContext.SetMockBindingValue(key, value);
                _bindings.Add(new EditableBindingViewModel(key, value, OnBindingValueChanged));
            }
        }
        finally
        {
            _isPopulatingBindings = false;
        }
    }

    private void OnBindingValueChanged(EditableBindingViewModel binding)
    {
        if (_isPopulatingBindings)
        {
            return;
        }

        PrefabRenderContext.SetMockBindingValue(binding.Key, binding.Value);
        ReRenderCurrentDocument();
    }

    private void ReRenderCurrentDocument()
    {
        if (_currentDocument is null)
        {
            return;
        }

        var diagnostics = new PreviewDiagnostics();
        RenderPreview(_currentDocument, diagnostics);
        StatusText = $"Updated preview for {Path.GetFileName(_currentDocument.SourcePath)}";
    }

    private void RenderPreview(PrefabDocument document, PreviewDiagnostics diagnostics)
    {
        ClearPreviewSelection();
        PreviewContent = _prefabRenderer.Render(document, diagnostics);
        AttachPreviewInteractions(PreviewContent);
        RebuildPreviewTree(PreviewContent);
        DiagnosticsText = diagnostics.ToDisplayText();
        SelectedElementDebugText = "Click an element in the preview to inspect its resolved assets.";
    }

    public void SelectPreviewTreeNode(PreviewTreeNodeViewModel? node)
    {
        if (node?.Element is null || _isSynchronizingSelection)
        {
            return;
        }

        SelectPreviewElement(node.Element);
    }

    private void RebuildPreviewTree(FrameworkElement? root)
    {
        ClearPreviewTree();
        if (root is null)
        {
            return;
        }

        foreach (var node in BuildPreviewTreeNodes(root))
        {
            _previewTree.Add(node);
        }
    }

    private void ClearPreviewTree()
    {
        _previewTree.Clear();
        _previewTreeLookup.Clear();
        _selectedPreviewTreeNode = null;
    }

    private IEnumerable<PreviewTreeNodeViewModel> BuildPreviewTreeNodes(DependencyObject current)
    {
        var currentNode = TryCreatePreviewTreeNode(current);
        var childNodes = new List<PreviewTreeNodeViewModel>();

        if (current is Visual or Visual3D)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                childNodes.AddRange(BuildPreviewTreeNodes(child));
            }
        }

        if (currentNode is null)
        {
            return childNodes;
        }

        foreach (var childNode in childNodes)
        {
            currentNode.AddChild(childNode);
        }

        return [currentNode];
    }

    private PreviewTreeNodeViewModel? TryCreatePreviewTreeNode(DependencyObject current)
    {
        if (current is not FrameworkElement element)
        {
            return null;
        }

        var debugText = PreviewElementDebugProperties.GetDebugText(element);
        if (string.IsNullOrWhiteSpace(debugText))
        {
            return null;
        }

        var label = PreviewElementDebugProperties.GetTreeLabel(element);
        var node = new PreviewTreeNodeViewModel(
            string.IsNullOrWhiteSpace(label) ? element.GetType().Name : label,
            element);
        _previewTreeLookup[element] = node;
        return node;
    }

    private void SetSelectedPreviewTreeNode(PreviewTreeNodeViewModel? node)
    {
        if (ReferenceEquals(_selectedPreviewTreeNode, node))
        {
            return;
        }

        if (_selectedPreviewTreeNode is not null)
        {
            _selectedPreviewTreeNode.IsSelected = false;
        }

        _selectedPreviewTreeNode = node;

        if (_selectedPreviewTreeNode is not null)
        {
            _selectedPreviewTreeNode.ExpandPath();
            _selectedPreviewTreeNode.IsSelected = true;
        }
    }
    #endregion
}
