namespace Bannerlord.XmlPreview.ViewModels.Mocks;

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
            if (SetProperty(ref _value, value)) _changed();
        }
    }
}