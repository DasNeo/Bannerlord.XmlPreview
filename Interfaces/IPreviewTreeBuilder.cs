using System.Windows;
using Bannerlord.XmlPreview.ViewModels;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IPreviewTreeBuilder
{
    PreviewTreeBuildResult Build(FrameworkElement root);
}

public sealed record PreviewTreeBuildResult(
    IReadOnlyList<PreviewTreeNodeViewModel> Roots,
    IReadOnlyDictionary<FrameworkElement, PreviewTreeNodeViewModel> NodesByElement);
