using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Bannerlord.XmlPreview.ViewModels;

namespace Bannerlord.XmlPreview.Controls;

public partial class SelectedInfoView : UserControl
{
    public SelectedInfoView()
    {
        InitializeComponent();
    }

    private void OpenSprite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Hyperlink
            {
                DataContext: SelectedElementInfoViewModel.SelectedSpriteInfoViewModel sprite
            } || !sprite.HasImagePath)
            return;

        try
        {
            if (!File.Exists(sprite.ImagePath))
                throw new FileNotFoundException("The sprite image was not found.", sprite.ImagePath);

            Process.Start(new ProcessStartInfo(sprite.ImagePath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
                                          UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Could not open sprite '{sprite.Name}'.\n\n{sprite.ImagePath}\n\n{exception.Message}",
                "Open sprite", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
