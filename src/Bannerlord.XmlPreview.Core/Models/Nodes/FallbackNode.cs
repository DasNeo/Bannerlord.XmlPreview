using Bannerlord.XmlPreview.Rendering;
using System.Windows;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class FallbackNode : ContainerPrefabNode
{
    public FallbackNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, false, attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        context.ReportUnsupportedNode(Name);
        var rendered = base.Render(context, parentSize);
        if (rendered.Element is System.Windows.Controls.Border border)
        {
            border.BorderBrush = System.Windows.Media.Brushes.DarkOrange;
            border.BorderThickness = new Thickness(1);
            border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 140, 0));
        }
        if (rendered.ChildHost is System.Windows.Controls.Canvas host)
            host.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Preview unavailable: " + Name,
                Foreground = System.Windows.Media.Brushes.DarkOrange,
                FontSize = 11,
                IsHitTestVisible = false
            });
        return rendered;
    }
}
