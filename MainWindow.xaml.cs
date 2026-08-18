using Bannerlord.XmlPreview.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void PreviewTreeOnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectPreviewTreeNode(e.NewValue as PreviewTreeNodeViewModel);
        }
    }
}
