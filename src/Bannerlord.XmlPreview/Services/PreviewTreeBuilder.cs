using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.ViewModels;

namespace Bannerlord.XmlPreview.Services;

public sealed class PreviewTreeBuilder : IPreviewTreeBuilder
{
    public PreviewTreeBuildResult Build(FrameworkElement root)
    {
        var nodesByElement = new Dictionary<FrameworkElement, PreviewTreeNodeViewModel>();
        var roots = BuildNodes(root, nodesByElement).ToArray();
        return new PreviewTreeBuildResult(roots, nodesByElement);
    }

    private static IEnumerable<PreviewTreeNodeViewModel> BuildNodes(
        DependencyObject current,
        IDictionary<FrameworkElement, PreviewTreeNodeViewModel> nodesByElement)
    {
        var currentNode = TryCreateNode(current, nodesByElement);
        var childNodes = new List<PreviewTreeNodeViewModel>();

        if (current is Visual or Visual3D)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
                childNodes.AddRange(BuildNodes(VisualTreeHelper.GetChild(current, index), nodesByElement));
        }

        if (currentNode is null) return childNodes;

        foreach (var childNode in childNodes) currentNode.AddChild(childNode);
        return [currentNode];
    }

    private static PreviewTreeNodeViewModel? TryCreateNode(
        DependencyObject current,
        IDictionary<FrameworkElement, PreviewTreeNodeViewModel> nodesByElement)
    {
        if (current is not FrameworkElement element) return null;

        var debugInfo = PreviewElementDebugProperties.GetDebugInfo(element);
        if (debugInfo is null) return null;

        var label = PreviewElementDebugProperties.GetTreeLabel(element);
        var node = new PreviewTreeNodeViewModel(
            string.IsNullOrWhiteSpace(label) ? element.GetType().Name : label,
            element);
        nodesByElement[element] = node;
        return node;
    }
}
