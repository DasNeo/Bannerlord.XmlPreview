using System.Windows.Input;
using Bannerlord.XmlPreview.Infrastructure;

namespace Bannerlord.XmlPreview.ViewModels.Mocks;

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