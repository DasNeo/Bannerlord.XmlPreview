using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Controls;

public class Accordion : Grid
{
    public Accordion()
    {
        Loaded += (_, _) => RefreshLayout();
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        Unsubscribe(visualRemoved);
        Subscribe(visualAdded);
        RefreshLayout();
    }

    private void Subscribe(DependencyObject element)
    {
        if (element is not Expander expander)
        {
            return;
        }

        expander.Expanded += OnExpanderStateChanged;
        expander.Collapsed += OnExpanderStateChanged;
    }

    private void Unsubscribe(DependencyObject element)
    {
        if (element is not Expander expander)
        {
            return;
        }

        expander.Expanded -= OnExpanderStateChanged;
        expander.Collapsed -= OnExpanderStateChanged;
    }

    private void OnExpanderStateChanged(object sender, RoutedEventArgs e)
    {
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        if (!IsLoaded)
        {
            return;
        }

        RowDefinitions.Clear();

        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            var isExpanded = child is Expander { IsExpanded: true };

            RowDefinitions.Add(new RowDefinition
            {
                Height = isExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });

            SetRow(child, index);
        }
    }
}
