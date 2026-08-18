using System.Collections.ObjectModel;
using System.Windows;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class PreviewTreeNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public PreviewTreeNodeViewModel(string label, FrameworkElement element)
    {
        Label = label;
        Element = element;
    }

    public string Label { get; }

    public FrameworkElement Element { get; }

    public ObservableCollection<PreviewTreeNodeViewModel> Children { get; } = [];

    public PreviewTreeNodeViewModel? Parent { get; private set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void AddChild(PreviewTreeNodeViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void ExpandPath()
    {
        PreviewTreeNodeViewModel? current = this;
        while (current is not null)
        {
            current.IsExpanded = true;
            current = current.Parent;
        }
    }
}
