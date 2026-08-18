namespace Bannerlord.XmlPreview.ViewModels;

public sealed class EditableBindingViewModel : ViewModelBase
{
    private readonly Action<EditableBindingViewModel> _valueChanged;
    private string _value;

    public EditableBindingViewModel(string key, string value, Action<EditableBindingViewModel> valueChanged)
    {
        Key = key;
        _value = value;
        _valueChanged = valueChanged;
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
                _valueChanged(this);
            }
        }
    }
}
