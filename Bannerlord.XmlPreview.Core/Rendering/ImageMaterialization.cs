using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Rendering;

public sealed partial class PrefabRenderContext
{
    private RenderedPrefabNode RenderImage(PrefabNode node, Size parentSize, ImageBlueprint blueprint)
    {
        var dynamicImage = !string.IsNullOrEmpty(blueprint.Request.Provider) || blueprint.Masked || blueprint.HideWhenNull || blueprint.Request.ScenarioSprite is not null;
        var attributes = new Dictionary<string, string>(node.Attributes, StringComparer.OrdinalIgnoreCase);
        // Image Color tints source pixels; it must not also paint the container background.
        attributes.Remove("Color");
        if (dynamicImage)
        {
            attributes.Remove("Brush"); attributes.Remove("Sprite");
        }
        var rendered = new WidgetNode(node.Name, attributes, node.Children).Render(this, parentSize);
        var border = (Border)rendered.Element;
        var grid = (Grid)border.Child;
        var request = blueprint.Request;
        var layer = ResolveVisibleBrushLayers(blueprint.Brush, IsDisabled(node.Attributes)).FirstOrDefault(l => !string.IsNullOrEmpty(l.SpriteName));
        if (!dynamicImage && string.IsNullOrEmpty(request.Identity) && layer?.SpriteName is { } brushSprite)
            request = request with { Identity = brushSprite };
        if (!dynamicImage && string.IsNullOrEmpty(request.Identity) && layer is null && _assetCatalog.FindBrush(blueprint.Brush) is not null)
        {
            PreviewElementDebugProperties.SetImageResolution(border, new(request, ImageResolutionStatus.Resolved, null, Reason: "Brush has no visible image layers in the selected state."));
            return rendered;
        }
        if (dynamicImage && blueprint.HideWhenNull && string.IsNullOrEmpty(request.Identity) && request.ScenarioSprite is null)
        {
            border.Visibility = Visibility.Collapsed;
            PreviewElementDebugProperties.SetImageResolution(border, new(request, ImageResolutionStatus.Resolved, null, Reason: "Null image hidden by HideWhenNull."));
            return rendered;
        }
        var resolution = _assetCatalog.ResolveImage(request);
        if (resolution.Status == ImageResolutionStatus.Resolved && resolution.Image is not null && blueprint.Masked)
        {
            var maskIdentity = blueprint.Mask ?? layer?.SpriteName;
            var mask = _assetCatalog.ResolveImage(new(maskIdentity ?? ""));
            PreviewElementDebugProperties.SetMaskResolution(border, mask);
            ReportImage(border, node, mask, "mask");
            if (mask.Status != ImageResolutionStatus.Resolved || mask.Image is null)
                resolution = mask with { Request = request, Reason = "Mask: " + mask.Reason };
            else
            {
                var image = ImageElement(resolution.Image, blueprint.Stretch, string.IsNullOrWhiteSpace(blueprint.Color) ? layer?.Color : blueprint.Color);
                image.OpacityMask = new ImageBrush(MaskBitmap(mask)) { Stretch = Stretch.Fill };
                image.Opacity = Math.Clamp(layer?.AlphaFactor ?? 1, 0, 1);
                grid.Children.Insert(0, image);
            }
        }
        else if (resolution.Status == ImageResolutionStatus.Resolved && resolution.Image is not null && dynamicImage)
            grid.Children.Insert(0, ImageElement(resolution.Image, blueprint.Stretch, blueprint.Color));
        else if (resolution.Status == ImageResolutionStatus.Resolved && resolution.Image is not null && !string.IsNullOrEmpty(blueprint.Request.Identity))
        {
            // Replace the generic sprite overlay to honor stretch and tint explicitly.
            foreach (var old in grid.Children.OfType<Image>().ToArray()) grid.Children.Remove(old);
            grid.Children.Insert(0, ImageElement(resolution.Image, blueprint.Stretch, blueprint.Color));
        }
        if (dynamicImage && resolution.Status == ImageResolutionStatus.Resolved && resolution.Image is not null &&
            (UsesCoverChildren(node.Attributes, "WidthSizePolicy") || UsesCoverChildren(node.Attributes, "HeightSizePolicy")))
        {
            // Provider images arrive after container layout; include their intrinsic dimensions in content sizing.
            var containerSize = ResolveElementRenderSize(border, new Size(0, 0));
            var contentSize = new Size(Math.Max(resolution.Width, containerSize.Width), Math.Max(resolution.Height, containerSize.Height));
            var canvas = (Canvas)rendered.ChildHost!;
            var size = FinalizeCoverChildrenSize(border, node.Attributes, contentSize, () =>
            {
                RelayoutCanvasChildren(canvas, node.Children, ResolveElementRenderSize(border, parentSize));
                var bounds = MeasureCanvasChildrenBounds(canvas);
                return bounds.IsEmpty ? contentSize : new Size(
                    Math.Max(contentSize.Width, bounds.Width), Math.Max(contentSize.Height, bounds.Height));
            });
            PreviewElementDebugProperties.SetSelectionBounds(border, new Rect(new Point(0, 0), size));
        }
        if (node.Attributes.TryGetValue("Brush.AlphaFactor", out var alpha))
            border.Opacity *= Math.Clamp(ResolveDouble(alpha, 1), 0, 1);
        ReportImage(border, node, resolution, "source");
        if (resolution.Status is ImageResolutionStatus.Missing or ImageResolutionStatus.RuntimeOnly or ImageResolutionStatus.Invalid)
        {
            var color = resolution.Status switch
            {
                ImageResolutionStatus.RuntimeOnly => Colors.MediumPurple,
                ImageResolutionStatus.Invalid => Colors.Magenta,
                _ => Colors.Crimson
            };
            border.BorderBrush = new SolidColorBrush(color);
            border.BorderThickness = new Thickness(2);
            border.Background = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B));
            grid.Children.Add(new TextBlock { Text = $"{resolution.Status}: {request.Provider ?? request.Identity}",
                Foreground = new SolidColorBrush(color), TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        }
        return rendered;
    }

    private void ReportImage(FrameworkElement element, PrefabNode node, ImageResolution result, string role)
    {
        PreviewElementDebugProperties.SetImageResolution(element, result);
        element.ToolTip = $"{node.Name}: {role} {result.Status}\n{result.Module} / {result.SourcePath}\n{result.Texture} / {result.Format}\n{result.Width}x{result.Height}\n{result.Reason}\n" + string.Join("\n", result.Attempts ?? []);
        if (result.Status == ImageResolutionStatus.Invalid)
            throw new InvalidDataException($"{node.Name} {role}: {result.Reason}");
        if (result.Status != ImageResolutionStatus.Resolved)
        {
            var message = $"{TryGetWidgetPath(node.Attributes)}: image.{result.Status.ToString().ToLowerInvariant()} {role}: {result.Reason}";
            _diagnostics.Add(message);
            _diagnostics.MarkTruncated(message);
        }
    }

    private static Image ImageElement(BitmapSource source, string stretch, string? tint)
    {
        var color = ParseBannerlordColor(tint);
        if (color is not null)
        {
            var pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(pixels[i] * color.Value.B * color.Value.A / 65025);
                pixels[i + 1] = (byte)(pixels[i + 1] * color.Value.G * color.Value.A / 65025);
                pixels[i + 2] = (byte)(pixels[i + 2] * color.Value.R * color.Value.A / 65025);
                pixels[i + 3] = (byte)(pixels[i + 3] * color.Value.A / 255);
            }
            source = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, source.PixelWidth * 4);
            source.Freeze();
        }
        return new Image { Source = source, Stretch = Enum.TryParse<Stretch>(stretch, true, out var value) ? value : Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    }
    private static BitmapSource MaskBitmap(ImageResolution mask)
    {
        var source = mask.Image!;
        if (mask.Format is not ("BC4" or "R8_UNORM")) return source;
        var pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 2]; // Single-channel masks store coverage in red.
            pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, source.PixelWidth * 4);
        bitmap.Freeze(); return bitmap;
    }
}
