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
            viewModel.PreviewInspector.SelectPreviewTreeNode(e.NewValue as PreviewTreeNodeViewModel);
            if (e.NewValue is PreviewTreeNodeViewModel node)
                viewModel.SelectedSource = Bannerlord.XmlPreview.Rendering.PreviewElementDebugProperties.GetSourceLocation(node.Element);
        }
    }
    private void PreviewTreeOnDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.SelectedSource is not null)
            viewModel.OpenSourceCommand.Execute(null);
    }
}
