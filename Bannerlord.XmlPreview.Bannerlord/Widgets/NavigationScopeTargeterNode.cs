using Bannerlord.XmlPreview.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Models.Nodes;

public sealed class NavigationScopeTargeterNode : PrefabNode
{
    public NavigationScopeTargeterNode(string name, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : base(name, attributes, children)
    {
    }

    public NavigationScopeTargeterNode(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<PrefabNode> children)
        : this("NavigationScopeTargeter", attributes, children)
    {
    }

    public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
    {
        var marker = new Border
        {
            Width = 0,
            Height = 0,
            Background = Brushes.Transparent,
            Focusable = false,
            IsHitTestVisible = false
        };

        context.ApplyOpacity(marker, Attributes);
        context.ApplyVisibility(marker, Attributes);
        context.ApplyToolTip(marker, Name, Attributes);
        context.AttachElementDebugMetadata(marker, Name, Attributes);
        return new RenderedPrefabNode(marker, null);
    }
}
