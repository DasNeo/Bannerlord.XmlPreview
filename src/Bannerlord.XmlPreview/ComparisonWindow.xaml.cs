using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.VisualRegression;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview;

public partial class ComparisonWindow : Window
{
    private readonly PresentedComparison _comparison;
    private readonly Action<WidgetBounds>? _locate;
    public ComparisonWindow(PresentedComparison comparison, PreviewScenario scenario, Action<WidgetBounds>? locate = null)
    {
        _comparison = comparison;
        _locate = locate;
        InitializeComponent();
        Metadata.Text = $"{comparison.RunMode} • Scenario {scenario.Id} • Capture Profile {scenario.CaptureProfile.Id} " +
            $"({scenario.CaptureProfile.Width} × {scenario.CaptureProfile.Height}) • Masks {scenario.Masks.Length}\n" +
            $"Pixel Tolerance: channel {comparison.Tolerance.MaximumChannelDifference}, proportion {comparison.Tolerance.MaximumDifferingPixelProportion:P2}\n{comparison.Summary}";
        Mode.SelectedIndex = 0;
        Zoom.Value = Math.Min(1, 1050d / (comparison.Reference.Width + comparison.Actual.Width + 16));
        Differences.ItemsSource = comparison.Differences;
    }
    private void DifferenceSelected(object sender, SelectionChangedEventArgs e)
    { if (Differences.SelectedItem is WidgetBounds bounds) _locate?.Invoke(bounds); }
    private static BitmapSource Bitmap(RgbImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Rgb24, null, image.Pixels, image.Width * 3);
        bitmap.Freeze(); return bitmap;
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (First is null || _comparison is null) return;
        First.Source = Bitmap(Mode.SelectedIndex switch { 2 => _comparison.Actual, 3 => _comparison.Overlay, 4 => _comparison.Diff, _ => _comparison.Reference });
        Second.Source = Bitmap(_comparison.Actual);
        Second.Visibility = Mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImageScale is null) return;
        ImageScale.ScaleX = ImageScale.ScaleY = e.NewValue;
    }
}
