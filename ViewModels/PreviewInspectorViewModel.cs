using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class PreviewInspectorViewModel : ViewModelBase
{
    private const string DefaultEmptyMessage =
        "Click an element in the preview to inspect its layout and assets.";

    private readonly IPreviewTreeBuilder _previewTreeBuilder;
    private FrameworkElement? _interactivePreviewRoot;
    private IReadOnlyDictionary<FrameworkElement, PreviewTreeNodeViewModel> _previewTreeLookup =
        new Dictionary<FrameworkElement, PreviewTreeNodeViewModel>();
    private bool _isSynchronizingSelection;
    private SelectedElementInfoViewModel _selectedElementInfo =
        SelectedElementInfoViewModel.Empty(DefaultEmptyMessage);
    private SelectionBorderAdorner? _selectedPreviewAdorner;
    private AdornerLayer? _selectedPreviewAdornerLayer;
    private FrameworkElement? _selectedPreviewElement;
    private PreviewTreeNodeViewModel? _selectedPreviewTreeNode;

    public PreviewInspectorViewModel(IPreviewTreeBuilder previewTreeBuilder)
    {
        _previewTreeBuilder = previewTreeBuilder;
    }

    public ObservableCollection<PreviewTreeNodeViewModel> PreviewTree { get; } = [];

    public SelectedElementInfoViewModel SelectedElementInfo
    {
        get => _selectedElementInfo;
        private set => SetProperty(ref _selectedElementInfo, value);
    }

    public void Attach(FrameworkElement root)
    {
        DetachInteractions();
        ClearSelection();

        _interactivePreviewRoot = root;
        _interactivePreviewRoot.PreviewMouseLeftButtonDown += PreviewRootOnPreviewMouseLeftButtonDown;
        RebuildTree(root);
        SelectedElementInfo = SelectedElementInfoViewModel.Empty(DefaultEmptyMessage);
    }

    public void Reset(string message)
    {
        DetachInteractions();
        ClearSelection();
        ClearTree();
        SelectedElementInfo = SelectedElementInfoViewModel.Empty(message);
    }

    public void SelectPreviewTreeNode(PreviewTreeNodeViewModel? node)
    {
        if (node?.Element is null || _isSynchronizingSelection) return;
        SelectElement(node.Element);
    }
    public void SelectSource(SourceLocation source)
    {
        var node = _previewTreeLookup.Keys.FirstOrDefault(element => PreviewElementDebugProperties.GetSourceLocation(element) == source);
        if (node is not null) SelectElement(node);
    }

    private void DetachInteractions()
    {
        if (_interactivePreviewRoot is not null)
            _interactivePreviewRoot.PreviewMouseLeftButtonDown -= PreviewRootOnPreviewMouseLeftButtonDown;

        _interactivePreviewRoot = null;
    }

    private void PreviewRootOnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject originalSource) return;

        var element = FindDebugElement(originalSource);
        if (element is not null) SelectElement(element);
    }

    private static FrameworkElement? FindDebugElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element &&
                PreviewElementDebugProperties.GetDebugInfo(element) is not null)
                return element;

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

    private void SelectElement(FrameworkElement element)
    {
        if (_isSynchronizingSelection) return;

        _isSynchronizingSelection = true;
        try
        {
            if (!ReferenceEquals(_selectedPreviewElement, element))
            {
                ClearSelection();
                _selectedPreviewElement = element;
                _selectedPreviewAdornerLayer = AdornerLayer.GetAdornerLayer(element);
                if (_selectedPreviewAdornerLayer is not null)
                {
                    _selectedPreviewAdorner = new SelectionBorderAdorner(element);
                    _selectedPreviewAdornerLayer.Add(_selectedPreviewAdorner);
                }
            }

            SelectedElementInfo = SelectedElementInfoViewModel.FromDebugInfo(
                PreviewElementDebugProperties.GetDebugInfo(element));
            SetSelectedTreeNode(_previewTreeLookup.GetValueOrDefault(element));
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void ClearSelection()
    {
        if (_selectedPreviewAdornerLayer is not null && _selectedPreviewAdorner is not null)
            _selectedPreviewAdornerLayer.Remove(_selectedPreviewAdorner);

        _selectedPreviewElement = null;
        _selectedPreviewAdornerLayer = null;
        _selectedPreviewAdorner = null;
        SetSelectedTreeNode(null);
    }

    private void RebuildTree(FrameworkElement root)
    {
        ClearTree();
        var result = _previewTreeBuilder.Build(root);
        _previewTreeLookup = result.NodesByElement;
        foreach (var node in result.Roots) PreviewTree.Add(node);
    }

    private void ClearTree()
    {
        PreviewTree.Clear();
        _previewTreeLookup = new Dictionary<FrameworkElement, PreviewTreeNodeViewModel>();
        _selectedPreviewTreeNode = null;
    }

    private void SetSelectedTreeNode(PreviewTreeNodeViewModel? node)
    {
        if (ReferenceEquals(_selectedPreviewTreeNode, node)) return;

        if (_selectedPreviewTreeNode is not null) _selectedPreviewTreeNode.IsSelected = false;
        _selectedPreviewTreeNode = node;
        if (_selectedPreviewTreeNode is null) return;

        _selectedPreviewTreeNode.ExpandPath();
        _selectedPreviewTreeNode.IsSelected = true;
    }
}
