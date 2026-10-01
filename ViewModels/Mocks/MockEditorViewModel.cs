using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Rendering;

namespace Bannerlord.XmlPreview.ViewModels.Mocks;

public sealed class MockEditorViewModel : ViewModelBase
{
    private readonly Dictionary<EditableListMockViewModel, TemplateMockDefinition> _definitions = [];
    private readonly ITemplateMockService _templateMockService;
    private string _bindingsFilter = string.Empty;
    private string? _currentSourcePath;
    private bool _isPopulating;

    public MockEditorViewModel(ITemplateMockService templateMockService)
    {
        _templateMockService = templateMockService;
        BindingsView = CollectionViewSource.GetDefaultView(Bindings);
        BindingsView.Filter = FilterBinding;
    }

    public ObservableCollection<EditableBindingViewModel> Bindings { get; } = [];

    public ICollectionView BindingsView { get; }

    public ObservableCollection<EditableListMockViewModel> Mocks { get; } = [];

    public string BindingsFilter
    {
        get => _bindingsFilter;
        set
        {
            if (!SetProperty(ref _bindingsFilter, value)) return;
            BindingsView.Refresh();
        }
    }

    public event EventHandler? Changed;

    public void Load(PrefabDocument document)
    {
        _isPopulating = true;
        try
        {
            _currentSourcePath = document.SourcePath;
            PopulateBindings(document);
            PopulateListMocks(document);
        }
        finally
        {
            _isPopulating = false;
        }
    }

    public void Clear()
    {
        _isPopulating = true;
        try
        {
            _currentSourcePath = null;
            Bindings.Clear();
            Mocks.Clear();
            _definitions.Clear();
        }
        finally
        {
            _isPopulating = false;
        }
    }

    private void PopulateBindings(PrefabDocument document)
    {
        Bindings.Clear();
        foreach (var key in document.BindingKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var value = PrefabRenderContext.ResolveMockBindingValue(key);
            PrefabRenderContext.SetMockBindingValue(key, value);
            Bindings.Add(new EditableBindingViewModel(key, value, OnBindingValueChanged));
        }
    }

    private void PopulateListMocks(PrefabDocument document)
    {
        Mocks.Clear();
        _definitions.Clear();

        foreach (var definition in _templateMockService.Discover(document))
        {
            var items = _templateMockService.PrepareItems(document.SourcePath, definition);
            var mock = new EditableListMockViewModel(
                definition.TargetKey,
                definition.DisplayName,
                definition.Description,
                definition.Node,
                definition.FieldKeys,
                items,
                PrefabRenderContext.ResolveMockBindingValue,
                OnListMockChanged);
            _definitions[mock] = definition;
            Mocks.Add(mock);
        }
    }

    private void OnBindingValueChanged(EditableBindingViewModel binding)
    {
        if (_isPopulating) return;

        PrefabRenderContext.SetMockBindingValue(binding.Key, binding.Value);
        if (!string.IsNullOrWhiteSpace(BindingsFilter)) BindingsView.Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnListMockChanged(EditableListMockViewModel mock)
    {
        if (_isPopulating ||
            string.IsNullOrWhiteSpace(_currentSourcePath) ||
            !_definitions.TryGetValue(mock, out var definition))
            return;

        _templateMockService.ApplyItems(_currentSourcePath, definition, mock.CreateSnapshot());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool FilterBinding(object item)
    {
        if (item is not EditableBindingViewModel binding) return false;
        if (string.IsNullOrWhiteSpace(BindingsFilter)) return true;

        return binding.Key.Contains(BindingsFilter, StringComparison.OrdinalIgnoreCase) ||
               binding.DisplayKey.Contains(BindingsFilter, StringComparison.OrdinalIgnoreCase) ||
               binding.Value.Contains(BindingsFilter, StringComparison.OrdinalIgnoreCase);
    }
}