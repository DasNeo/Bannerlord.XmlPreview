using System.Windows;
using System.Windows.Controls;

namespace Bannerlord.XmlPreview.Rendering;

public readonly record struct RenderedPrefabNode(FrameworkElement Element, Panel? ChildHost);
