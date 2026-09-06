using System.Collections.ObjectModel;
using System.Windows.Input;
using Bannerlord.XmlPreview.Infrastructure;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class EditableListMockViewModel : ViewModelBase
{
    private readonly Action<EditableListMockViewModel> _changed;
    private readonly IReadOnlyList<string> _fieldKeys;
    private readonly Func<string, string> _defaultValue;

    public EditableListMockViewModel(
        string targetKey,
        string displayName,
        string description,
        PrefabNode targetNode,
        IReadOnlyList<string> fieldKeys,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items,
        Func<string, string> defaultValue,
        Action<EditableListMockViewModel> changed)
    {
        TargetKey = targetKey;
        DisplayName = displayName;
        Description = description;
        TargetNode = targetNode;
        _fieldKeys = fieldKeys;
        _defaultValue = defaultValue;
        _changed = changed;
        AddItemCommand = new RelayCommand(AddItem);

        foreach (var item in items)
        {
            Items.Add(CreateItem(item));
        }
    }

    public string TargetKey { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public PrefabNode TargetNode { get; }

    public ObservableCollection<EditableListMockItemViewModel> Items { get; } = [];

    public ICommand AddItemCommand { get; }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> CreateSnapshot()
    {
        return Items
            .Select(static item => item.CreateSnapshot())
            .ToArray();
    }

    private void AddItem()
    {
        var values = _fieldKeys.ToDictionary(
            static key => key,
            key => _defaultValue(key),
            StringComparer.OrdinalIgnoreCase);
        Items.Add(CreateItem(values));
        RefreshItemNumbers();
        _changed(this);
    }

    private EditableListMockItemViewModel CreateItem(IReadOnlyDictionary<string, string> values)
    {
        var keys = _fieldKeys
            .Concat(values.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new EditableListMockItemViewModel(
            Items.Count + 1,
            keys,
            values,
            OnItemChanged,
            RemoveItem);
    }

    private void OnItemChanged()
    {
        _changed(this);
    }

    private void RemoveItem(EditableListMockItemViewModel item)
    {
        if (!Items.Remove(item))
        {
            return;
        }

        RefreshItemNumbers();
        _changed(this);
    }

    private void RefreshItemNumbers()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Number = index + 1;
        }
    }
}

public sealed class EditableListMockItemViewModel : ViewModelBase
{
    private int _number;

    public EditableListMockItemViewModel(
        int number,
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, string> values,
        Action changed,
        Action<EditableListMockItemViewModel> remove)
    {
        _number = number;
        Fields = keys
            .Select(key => new EditableListMockFieldViewModel(
                key,
                values.TryGetValue(key, out var value) ? value : string.Empty,
                changed))
            .ToArray();
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public IReadOnlyList<EditableListMockFieldViewModel> Fields { get; }

    public ICommand RemoveCommand { get; }

    public IReadOnlyDictionary<string, string> CreateSnapshot()
    {
        return Fields.ToDictionary(
            static field => field.Key,
            static field => field.Value,
            StringComparer.OrdinalIgnoreCase);
    }

}

public sealed class EditableListMockFieldViewModel : ViewModelBase
{
    private readonly Action _changed;
    private string _value;

    public EditableListMockFieldViewModel(string key, string value, Action changed)
    {
        Key = key;
        _value = value;
        _changed = changed;
    }

    public string Key { get; }

    public string DisplayKey => $"@{Key}";

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _changed();
            }
        }
    }
}
