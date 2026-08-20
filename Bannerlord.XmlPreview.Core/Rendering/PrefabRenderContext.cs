using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.DemoData;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Assets;
using Bannerlord.XmlPreview.Models.Nodes;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text;

namespace Bannerlord.XmlPreview.Rendering;

public sealed class PrefabRenderContext
{
    private static readonly Size DefaultMeasureFallbackSize = new(1600, 1100);
    private const byte SignificantAlphaThreshold = 64;
    private const byte ContentCoreAlphaThreshold = 192;
    private readonly Dictionary<string, Int32Rect?> _spriteOpaqueBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Top, int Bottom)?> _spriteCoreVerticalBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IReadOnlyDictionary<string, string>> _bindingScopes = [];

    private static readonly Dictionary<string, string> MockBindings =
        new(PreviewDemoCatalog.MockBindings, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Brush> PanelBrushes = PreviewDemoCatalog.PanelBrushes
        .ToDictionary(
            entry => entry.Key,
            entry => CreateGradientBrush(entry.Value.FillHex, entry.Value.StrokeHex),
            StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Brush> TextBrushes = PreviewDemoCatalog.TextBrushColors
        .ToDictionary(
            entry => entry.Key,
            entry => (Brush)CreateSolidBrushInternal(entry.Value),
            StringComparer.OrdinalIgnoreCase);

    private readonly PreviewDiagnostics _diagnostics;
    private readonly IReadOnlyDictionary<string, string> _constants;
    private readonly IAssetCatalog _assetCatalog;
    private readonly IBrushMetricsProvider _brushMetricsProvider;
    private readonly IBrushOverlayFeatureHandler _brushOverlayFeatureHandler;
    private readonly IPreviewDataSourceProvider _previewDataSourceProvider;
    private readonly IWidgetTextSourceProvider _widgetTextSourceProvider;
    private readonly IWidgetDebugMetadataProvider _widgetDebugMetadataProvider;
    private readonly HashSet<string> _reportedBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedNodes = new(StringComparer.OrdinalIgnoreCase);

    public PrefabRenderContext(
        IReadOnlyDictionary<string, string> constants,
        PreviewDiagnostics diagnostics,
        IAssetCatalog assetCatalog,
        IBrushMetricsProvider brushMetricsProvider,
        IBrushOverlayFeatureHandler brushOverlayFeatureHandler,
        IPreviewDataSourceProvider previewDataSourceProvider,
        IWidgetTextSourceProvider widgetTextSourceProvider,
        IWidgetDebugMetadataProvider widgetDebugMetadataProvider)
    {
        _constants = constants;
        _diagnostics = diagnostics;
        _assetCatalog = assetCatalog;
        _brushMetricsProvider = brushMetricsProvider;
        _brushOverlayFeatureHandler = brushOverlayFeatureHandler;
        _previewDataSourceProvider = previewDataSourceProvider;
        _widgetTextSourceProvider = widgetTextSourceProvider;
        _widgetDebugMetadataProvider = widgetDebugMetadataProvider;
    }

    public void EnsureBindings(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (MockBindings.ContainsKey(key))
            {
                continue;
            }

            MockBindings[key] = HumanizeBindingKey(key);
            if (_reportedBindings.Add(key))
            {
                _diagnostics.Add($"Binding '@{key}' had no mock value. Preview generated '{MockBindings[key]}'.");
            }
        }
    }

    public void ReportUnsupportedNode(string nodeName)
    {
        if (_reportedNodes.Add(nodeName))
        {
            _diagnostics.Add($"Widget '{nodeName}' is not implemented exactly. Rendering a generic placeholder.");
        }
    }

    public string ResolveBinding(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        if (rawValue[0] != '@' || rawValue.Length < 2)
        {
            return rawValue;
        }

        var key = rawValue[1..];
        if (TryResolveScopedBindingValue(key, out var scopedValue))
        {
            return scopedValue;
        }

        return ResolveMockBindingValue(key);
    }

    public static string ResolveMockBindingValue(string key)
    {
        return MockBindings.TryGetValue(key, out var value) ? value : HumanizeBindingKey(key);
    }

    public static void SetMockBindingValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        MockBindings[key] = value;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> GetTemplateItemBindings(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes)
    {
        return _previewDataSourceProvider.GetTemplateItemBindings(nodeName, attributes);
    }

    public string ResolveWidgetText(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        string fallbackText)
    {
        var textSource = _widgetTextSourceProvider.GetTextSource(nodeName, attributes);
        var resolvedText = ResolveBinding(textSource);
        return string.IsNullOrWhiteSpace(resolvedText) ? fallbackText : resolvedText;
    }

    public IDisposable PushBindingScope(IReadOnlyDictionary<string, string> values)
    {
        _bindingScopes.Add(values);
        return new BindingScopeHandle(_bindingScopes, values);
    }

    public bool ResolveBoolean(string? rawValue, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        string resolvedValue;
        if (rawValue[0] == '!' && rawValue.Length > 1)
        {
            var constantKey = rawValue[1..];
            resolvedValue = _constants.TryGetValue(constantKey, out var constantValue)
                ? constantValue
                : rawValue;
        }
        else
        {
            resolvedValue = ResolveBinding(rawValue);
        }

        if (bool.TryParse(resolvedValue, out var booleanValue))
        {
            return booleanValue;
        }

        if (int.TryParse(resolvedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue != 0;
        }

        return resolvedValue.Trim().ToLowerInvariant() switch
        {
            "yes" or "on" or "visible" or "enabled" => true,
            "no" or "off" or "hidden" or "collapsed" or "disabled" => false,
            _ => fallback
        };
    }

    public double ResolveDouble(string? rawValue, double fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        if (rawValue[0] == '!')
        {
            var constantKey = rawValue[1..];
            if (_constants.TryGetValue(constantKey, out var constantValue))
            {
                return ResolveDouble(constantValue, fallback);
            }

            _diagnostics.Add($"Constant '!{constantKey}' was not found. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
            return fallback;
        }

        var resolved = ResolveBinding(rawValue);
        return double.TryParse(resolved, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    public bool IsDisabled(IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("IsEnabled", out var enabledValue) &&
            !ResolveBoolean(enabledValue, true))
        {
            return true;
        }

        return attributes.TryGetValue("IsDisabled", out var disabledValue) &&
               ResolveBoolean(disabledValue);
    }

    private bool TryResolveScopedBindingValue(string key, out string value)
    {
        for (var index = _bindingScopes.Count - 1; index >= 0; index--)
        {
            if (_bindingScopes[index].TryGetValue(key, out value!))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private IReadOnlyList<string> GetToolTipAttributeKeys(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes)
    {
        var keys = new List<string> { "Id", "Brush", "Sprite" };
        keys.AddRange(_widgetDebugMetadataProvider.GetAdditionalToolTipAttributeKeys(nodeName, attributes));
        return keys;
    }

    private sealed class BindingScopeHandle : IDisposable
    {
        private readonly List<IReadOnlyDictionary<string, string>> _bindingScopes;
        private readonly IReadOnlyDictionary<string, string> _values;
        private bool _disposed;

        public BindingScopeHandle(
            List<IReadOnlyDictionary<string, string>> bindingScopes,
            IReadOnlyDictionary<string, string> values)
        {
            _bindingScopes = bindingScopes;
            _values = values;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_bindingScopes.Count > 0 &&
                ReferenceEquals(_bindingScopes[^1], _values))
            {
                _bindingScopes.RemoveAt(_bindingScopes.Count - 1);
            }
        }
    }

    public bool IsVisible(IReadOnlyDictionary<string, string> attributes)
    {
        return !attributes.TryGetValue("IsVisible", out var visibleValue) ||
               ResolveBoolean(visibleValue, true);
    }

    public Brush ResolvePanelBrush(string? brushId, bool isDisabled = false)
    {
        if (TryCreateBrushImageBrush(brushId, isDisabled, out var imageBrush))
        {
            return imageBrush;
        }

        if (TryCreateBrushFromCatalog(brushId, isDisabled, out var catalogBrush))
        {
            return catalogBrush;
        }

        return Brushes.Transparent;
    }

    public Brush ResolveTextBrush(string? brushId, bool isDisabled = false)
    {
        if (TryResolveTextColor(brushId, isDisabled, out var textColor))
        {
            return new SolidColorBrush(textColor);
        }

        if (!string.IsNullOrWhiteSpace(brushId) && TextBrushes.TryGetValue(brushId, out var brush))
        {
            return brush;
        }

        return CreateSolidBrushInternal("#E8EEF5");
    }

    public Brush ResolveWidgetBackground(string? spriteId, string? brushId, bool isDisabled = false)
    {
        if (TryCreateBrushImageBrush(brushId, isDisabled, out var imageBrush))
        {
            return imageBrush;
        }

        if (TryCreateBrushFromCatalog(brushId, isDisabled, out var catalogBrush))
        {
            return catalogBrush;
        }

        if (!string.IsNullOrWhiteSpace(spriteId))
        {
            var spriteAsset = ResolveSpriteAsset(spriteId);
            if (spriteAsset.HasImage || spriteAsset.SpriteDefinition is not null)
            {
                return Brushes.Transparent;
            }
        }

        return Brushes.Transparent;
    }

    public SolidColorBrush CreateSolidBrush(string hex)
    {
        return CreateSolidBrushInternal(hex);
    }

    public FrameworkElement? CreateSpriteOverlay(string spriteId)
    {
        var spriteAsset = ResolveSpriteAsset(spriteId);
        var spriteImage = ResolveSpriteImage(spriteId);
        if (spriteImage is not null)
        {
            return new Image
            {
                Source = spriteImage,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }

        return null;
    }

    public Rect ResolveSpriteContentLayoutRect(string? spriteName, Size renderedSize)
    {
        if (string.IsNullOrWhiteSpace(spriteName) ||
            renderedSize.Width <= 0 ||
            renderedSize.Height <= 0)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        var spriteImage = ResolveSpriteImage(spriteName) as BitmapSource;
        if (spriteImage is null ||
            spriteImage.PixelWidth <= 0 ||
            spriteImage.PixelHeight <= 0)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        var coreBounds = ResolveSpriteCoreVerticalBounds(spriteName);
        if (coreBounds is not null)
        {
            return new Rect(
                0,
                renderedSize.Height * coreBounds.Value.Top / spriteImage.PixelHeight,
                renderedSize.Width,
                renderedSize.Height * (coreBounds.Value.Bottom - coreBounds.Value.Top + 1) / spriteImage.PixelHeight);
        }

        var opaqueBounds = ResolveSpriteOpaqueBounds(spriteName);
        if (opaqueBounds is null)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        return new Rect(
            0,
            renderedSize.Height * opaqueBounds.Value.Y / spriteImage.PixelHeight,
            renderedSize.Width,
            renderedSize.Height * opaqueBounds.Value.Height / spriteImage.PixelHeight);
    }

    public Rect ResolveSpriteSelectionRect(string? spriteName, Size renderedSize)
    {
        var opaqueBounds = ResolveSpriteOpaqueBounds(spriteName);
        if (opaqueBounds is null ||
            renderedSize.Width <= 0 ||
            renderedSize.Height <= 0)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        var spriteImage = ResolveSpriteImage(spriteName) as BitmapSource;
        if (spriteImage is null ||
            spriteImage.PixelWidth <= 0 ||
            spriteImage.PixelHeight <= 0)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        return new Rect(
            renderedSize.Width * opaqueBounds.Value.X / spriteImage.PixelWidth,
            renderedSize.Height * opaqueBounds.Value.Y / spriteImage.PixelHeight,
            renderedSize.Width * opaqueBounds.Value.Width / spriteImage.PixelWidth,
            renderedSize.Height * opaqueBounds.Value.Height / spriteImage.PixelHeight);
    }

    private Int32Rect? ResolveSpriteOpaqueBounds(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName) ||
            ResolveCatalogSprite(spriteName)?.IsNineRegion == true)
        {
            return null;
        }

        var spriteAsset = ResolveSpriteAsset(spriteName);
        if (!spriteAsset.HasImage)
        {
            return null;
        }

        var spriteImage = ResolveSpriteImage(spriteName) as BitmapSource;
        if (spriteImage is null ||
            spriteImage.PixelWidth <= 0 ||
            spriteImage.PixelHeight <= 0)
        {
            return null;
        }

        var cacheKey = spriteAsset.MatchedKey ?? spriteAsset.RequestedName;
        return GetSpriteOpaqueBounds(cacheKey, spriteImage);
    }

    private (int Top, int Bottom)? ResolveSpriteCoreVerticalBounds(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName) ||
            ResolveCatalogSprite(spriteName)?.IsNineRegion == true)
        {
            return null;
        }

        var spriteAsset = ResolveSpriteAsset(spriteName);
        if (!spriteAsset.HasImage)
        {
            return null;
        }

        var spriteImage = ResolveSpriteImage(spriteName) as BitmapSource;
        if (spriteImage is null ||
            spriteImage.PixelWidth <= 0 ||
            spriteImage.PixelHeight <= 0)
        {
            return null;
        }

        var cacheKey = spriteAsset.MatchedKey ?? spriteAsset.RequestedName;
        return GetSpriteCoreVerticalBounds(cacheKey, spriteImage);
    }

    public FrameworkElement? CreateBrushOverlay(string? brushName, bool isDisabled, double referenceWidth, double referenceHeight)
    {
        var visibleLayers = ResolveVisibleBrushLayers(brushName, isDisabled)
            .Select(layer => CreateBrushLayerVisual(layer, referenceWidth, referenceHeight))
            .Where(layer => layer is not null)
            .Cast<FrameworkElement>()
            .ToArray();
        if (visibleLayers.Length == 0)
        {
            return null;
        }

        var visual = new Grid
        {
            Width = referenceWidth,
            Height = referenceHeight,
            ClipToBounds = false,
            IsHitTestVisible = false
        };
        foreach (var layer in visibleLayers)
        {
            visual.Children.Add(layer);
        }

        return visual;
    }

    public FrameworkElement? AddBrushOverlay(
        Grid layout,
        string? brushName,
        bool isDisabled,
        double referenceWidth,
        double referenceHeight,
        int insertIndex = 0)
    {
        var brushOverlay = CreateBrushOverlay(brushName, isDisabled, referenceWidth, referenceHeight);
        if (brushOverlay is null)
        {
            return null;
        }

        var targetIndex = Math.Clamp(insertIndex, 0, layout.Children.Count);
        layout.Children.Insert(targetIndex, brushOverlay);
        return brushOverlay;
    }

    public void ApplySizing(FrameworkElement element, IReadOnlyDictionary<string, string> attributes, Size parentSize)
    {
        var width = ResolveDimension(attributes, "WidthSizePolicy", "SuggestedWidth", parentSize.Width);
        var height = ResolveDimension(attributes, "HeightSizePolicy", "SuggestedHeight", parentSize.Height);

        if (width.HasValue)
        {
            element.Width = width.Value;
        }

        if (height.HasValue)
        {
            element.Height = height.Value;
        }
    }

    public void ApplyAssetSizing(FrameworkElement element, string? spriteId, string? brushId, bool isDisabled = false)
    {
        var sprite = ResolveCatalogSprite(spriteId) ?? ResolvePrimaryBrushSprite(brushId, isDisabled);
        if (sprite is null)
        {
            return;
        }

        if (double.IsNaN(element.Width))
        {
            element.Width = sprite.Width;
        }

        if (double.IsNaN(element.Height))
        {
            element.Height = sprite.Height;
        }
    }

    public void ApplyButtonSizing(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        string? brushId,
        bool isDisabled = false)
    {
        var metricWidth = _brushMetricsProvider.ResolveMetric(brushId, "Default", "Width");
        var metricHeight = _brushMetricsProvider.ResolveMetric(brushId, "Default", "Height");
        if (metricWidth.HasValue || metricHeight.HasValue)
        {
            var widthUsesCoverChildren = UsesCoverChildren(attributes, "WidthSizePolicy");
            var heightUsesCoverChildren = UsesCoverChildren(attributes, "HeightSizePolicy");

            if (double.IsNaN(element.Width))
            {
                if (widthUsesCoverChildren)
                {
                    var padding = ResolveBrushContentPadding(brushId, isDisabled);
                    element.MinWidth = Math.Max(element.MinWidth, padding.Left + padding.Right + 24);
                }
                else
                {
                    element.MinWidth = Math.Max(element.MinWidth, Math.Max(64, metricWidth ?? 64));
                }
            }

            if (double.IsNaN(element.Height) && metricHeight.HasValue)
            {
                if (heightUsesCoverChildren)
                {
                    element.MinHeight = Math.Max(element.MinHeight, metricHeight.Value);
                }
                else
                {
                    element.Height = metricHeight.Value;
                }
            }

            return;
        }

        EnsureMinimumButtonSize(element);
    }

    public Thickness ResolveBrushContentPadding(string? brushId, bool isDisabled, Thickness? fallback = null)
    {
        var layer = ResolveVisibleBrushLayers(brushId, isDisabled)
            .OrderByDescending(candidate => candidate.MaxExtend)
            .FirstOrDefault();
        if (layer is null)
        {
            return fallback ?? new Thickness(16, 4, 16, 4);
        }

        var sprite = ResolvePrimaryBrushSprite(brushId, isDisabled);
        return _brushMetricsProvider.ResolveContentPadding(
            new BrushPaddingRequest(
                layer.ExtendLeft ?? 0,
                layer.ExtendTop ?? 0,
                layer.ExtendRight ?? 0,
                layer.ExtendBottom ?? 0,
                sprite?.LeftWidth ?? 0,
                sprite?.TopHeight ?? 0,
                sprite?.RightWidth ?? 0,
                sprite?.BottomHeight ?? 0,
                fallback ?? new Thickness(16, 4, 16, 4)));
    }

    public void EnsureMinimumButtonSize(FrameworkElement element)
    {
        if (double.IsNaN(element.Width))
        {
            element.MinWidth = 72;
        }

        if (double.IsNaN(element.Height))
        {
            element.MinHeight = 36;
        }
    }

    public void ApplyOpacity(UIElement element, IReadOnlyDictionary<string, string> attributes)
    {
        var opacity = attributes.TryGetValue("AlphaFactor", out var alphaFactor)
            ? Math.Clamp(ResolveDouble(alphaFactor, 1), 0, 1)
            : 1d;

        if (IsDisabled(attributes))
        {
            opacity *= 0.88;
        }

        element.Opacity = opacity;
    }

    public void ApplyVisibility(UIElement element, IReadOnlyDictionary<string, string> attributes)
    {
        element.Visibility = IsVisible(attributes) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyTextStyle(
        TextBlock textBlock,
        string? brushId,
        string? rawFontSize,
        IReadOnlyDictionary<string, string> attributes,
        double fallbackFontSize)
    {
        var isDisabled = IsDisabled(attributes);
        textBlock.Foreground = ResolveTextBrush(brushId, isDisabled);
        textBlock.FontSize = ResolveTextFontSize(brushId, rawFontSize, isDisabled, fallbackFontSize);
        textBlock.FontFamily = ResolveFontFamily(brushId);
        textBlock.TextAlignment = ResolveTextAlignment(
            brushId,
            attributes.TryGetValue("Brush.TextHorizontalAlignment", out var textHorizontalAlignment)
                ? textHorizontalAlignment
                : null);
    }

    public HorizontalAlignment ResolveTextHorizontalAlignment(string? brushName, string? rawOverride = null)
    {
        return ResolveHorizontalAlignment(
            rawOverride ?? ResolveCatalogBrush(brushName)?.TextHorizontalAlignment,
            HorizontalAlignment.Left);
    }

    public VerticalAlignment ResolveTextVerticalAlignment(string? brushName, string? rawOverride = null)
    {
        return ResolveVerticalAlignment(
            rawOverride ?? ResolveCatalogBrush(brushName)?.TextVerticalAlignment,
            VerticalAlignment.Center);
    }

    public HorizontalAlignment ResolveWidgetHorizontalAlignment(
        IReadOnlyDictionary<string, string> attributes,
        HorizontalAlignment fallback = HorizontalAlignment.Left)
    {
        return attributes.TryGetValue("HorizontalAlignment", out var rawAlignment)
            ? ResolveHorizontalAlignment(rawAlignment, fallback)
            : fallback;
    }

    public VerticalAlignment ResolveWidgetVerticalAlignment(
        IReadOnlyDictionary<string, string> attributes,
        VerticalAlignment fallback = VerticalAlignment.Top)
    {
        return attributes.TryGetValue("VerticalAlignment", out var rawAlignment)
            ? ResolveVerticalAlignment(rawAlignment, fallback)
            : fallback;
    }

    private static HorizontalAlignment ResolveHorizontalAlignment(string? rawAlignment, HorizontalAlignment fallback)
    {
        return rawAlignment switch
        {
            "Center" => HorizontalAlignment.Center,
            "Right" => HorizontalAlignment.Right,
            "Left" => HorizontalAlignment.Left,
            "Stretch" => HorizontalAlignment.Stretch,
            _ => fallback
        };
    }

    private static VerticalAlignment ResolveVerticalAlignment(string? rawAlignment, VerticalAlignment fallback)
    {
        return rawAlignment switch
        {
            "Center" => VerticalAlignment.Center,
            "Bottom" => VerticalAlignment.Bottom,
            "Stretch" => VerticalAlignment.Stretch,
            _ => fallback
        };
    }

    public void ApplyBrushBorderStyle(Border border, string? brushId, bool isDisabled, bool frameBrush)
    {
        var layer = ResolveVisibleBrushLayers(brushId, isDisabled)
            .OrderByDescending(candidate => candidate.MaxExtend)
            .FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        if (layer.MaxExtend > 0)
        {
            var thickness = Math.Clamp(layer.MaxExtend / 8d, frameBrush ? 1.5 : 1, frameBrush ? 3.5 : 2.5);
            border.BorderThickness = new Thickness(thickness);
            border.CornerRadius = new CornerRadius(Math.Clamp(layer.MaxExtend / 3d, frameBrush ? 4 : 2, frameBrush ? 10 : 6));
        }

        if (TryResolveLayerColor(layer, out var layerColor))
        {
            border.BorderBrush = new SolidColorBrush(AdjustColor(layerColor, 0.78, 1.0));
        }
    }

    public void ApplyToolTip(FrameworkElement element, string nodeName, IReadOnlyDictionary<string, string> attributes)
    {
        var toolTipParts = new List<string> { nodeName };
        foreach (var key in GetToolTipAttributeKeys(nodeName, attributes))
        {
            if (attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                toolTipParts.Add($"{key}: {value}");
            }
        }

        if (attributes.TryGetValue("Sprite", out var spriteName) && !string.IsNullOrWhiteSpace(spriteName))
        {
            var spriteAsset = ResolveSpriteAsset(ResolveBinding(spriteName));
            toolTipParts.Add($"SpriteStatus: {spriteAsset.Kind}");
            if (!string.IsNullOrWhiteSpace(spriteAsset.MatchedKey))
            {
                toolTipParts.Add($"SpriteImageKey: {spriteAsset.MatchedKey}");
            }

            if (!string.IsNullOrWhiteSpace(spriteAsset.ImagePath))
            {
                toolTipParts.Add($"SpriteImagePath: {spriteAsset.ImagePath}");
            }

            if (spriteAsset.SpriteDefinition is not null)
            {
                var resolvedSprite = spriteAsset.SpriteDefinition;
                toolTipParts.Add(
                    $"ResolvedSprite: {resolvedSprite.Name} ({resolvedSprite.Kind}, {resolvedSprite.Width}x{resolvedSprite.Height}, {resolvedSprite.CategoryName}, Sheet {resolvedSprite.SheetId})");
                toolTipParts.Add($"SpritePart: {resolvedSprite.SpritePartName}");
                toolTipParts.Add($"SpriteSource: {resolvedSprite.SourcePath}");
            }

            if (!string.IsNullOrWhiteSpace(spriteAsset.Note))
            {
                toolTipParts.Add($"SpriteNote: {spriteAsset.Note}");
            }
        }

        if (attributes.TryGetValue("Brush", out var brushName) && !string.IsNullOrWhiteSpace(brushName))
        {
            var resolvedBrush = ResolveCatalogBrush(ResolveBinding(brushName));
            if (resolvedBrush is not null)
            {
                toolTipParts.Add($"ResolvedBrush: {resolvedBrush.Name}");
                var style = ResolveBrushStyle(resolvedBrush, IsDisabled(attributes));
                if (style is not null)
                {
                    toolTipParts.Add($"BrushState: {style.Name}");
                    if (!string.IsNullOrWhiteSpace(style.FontColor))
                    {
                        toolTipParts.Add($"BrushFontColor: {style.FontColor}");
                    }

                    if (style.FontSize.HasValue)
                    {
                        toolTipParts.Add($"BrushFontSize: {style.FontSize.Value.ToString(CultureInfo.InvariantCulture)}");
                    }
                }

                foreach (var layer in resolvedBrush.Layers.Where(layer => !string.IsNullOrWhiteSpace(layer.SpriteName)))
                {
                    var colorSuffix = string.IsNullOrWhiteSpace(layer.Color) ? string.Empty : $" Color={layer.Color}";
                    toolTipParts.Add($"Layer {layer.Name}: {layer.SpriteName}{colorSuffix}");
                }

                toolTipParts.Add($"BrushSource: {resolvedBrush.SourcePath}");
            }
        }

        element.ToolTip = string.Join(Environment.NewLine, toolTipParts);
    }

    public Size Measure(FrameworkElement element, Size parentSize)
    {
        element.Measure(parentSize);
        var desired = element.DesiredSize;

        var width = !double.IsNaN(element.Width) && element.Width > 0 ? element.Width : desired.Width;
        var height = !double.IsNaN(element.Height) && element.Height > 0 ? element.Height : desired.Height;

        return new Size(
            width > 0 ? width : Math.Min(parentSize.Width, DefaultMeasureFallbackSize.Width),
            height > 0 ? height : Math.Min(parentSize.Height, DefaultMeasureFallbackSize.Height));
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize)
    {
        AttachChildren(host, children, parentSize, new Point(0, 0));
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize, Point origin)
    {
        foreach (var child in children)
        {
            var rendered = child.Render(this, parentSize);
            if (host is StackPanel stackPanel)
            {
                if (rendered.AutoWidthInFlowLayout)
                {
                    rendered.Element.Width = double.NaN;
                }

                if (rendered.AutoHeightInFlowLayout)
                {
                    rendered.Element.Height = double.NaN;
                }

                ApplyFlowLayout(rendered.Element, child.Attributes);
                stackPanel.Children.Add(rendered.Element);
                continue;
            }

            var elementSize = Measure(rendered.Element, parentSize);
            var position = ComputePosition(child.Attributes, parentSize, elementSize, rendered.AlignmentBounds);
            host.Children.Add(rendered.Element);
            Canvas.SetLeft(rendered.Element, origin.X + position.X);
            Canvas.SetTop(rendered.Element, origin.Y + position.Y);
        }
    }

    public Rect MeasureCanvasChildrenBounds(Canvas canvas)
    {
        var hasChildren = false;
        var minLeft = 0d;
        var minTop = 0d;
        double maxRight = 0;
        double maxBottom = 0;

        foreach (var child in canvas.Children.OfType<FrameworkElement>())
        {
            var left = Canvas.GetLeft(child);
            if (double.IsNaN(left))
            {
                left = 0;
            }

            var top = Canvas.GetTop(child);
            if (double.IsNaN(top))
            {
                top = 0;
            }

            var width = !double.IsNaN(child.Width) && child.Width > 0
                ? child.Width
                : child.DesiredSize.Width;
            var height = !double.IsNaN(child.Height) && child.Height > 0
                ? child.Height
                : child.DesiredSize.Height;

            if (!hasChildren)
            {
                minLeft = left;
                minTop = top;
                hasChildren = true;
            }
            else
            {
                minLeft = Math.Min(minLeft, left);
                minTop = Math.Min(minTop, top);
            }

            maxRight = Math.Max(maxRight, left + width);
            maxBottom = Math.Max(maxBottom, top + height);
        }

        if (!hasChildren)
        {
            return Rect.Empty;
        }

        return new Rect(
            minLeft,
            minTop,
            Math.Max(0, maxRight - minLeft),
            Math.Max(0, maxBottom - minTop));
    }

    public Size MeasureCanvasChildren(Canvas canvas)
    {
        var bounds = MeasureCanvasChildrenBounds(canvas);
        return bounds.IsEmpty ? new Size(0, 0) : bounds.Size;
    }

    public void ApplyCanvasLayout(Canvas canvas, Rect contentBounds)
    {
        if (contentBounds.IsEmpty)
        {
            canvas.Margin = new Thickness(0);
            canvas.Width = 0;
            canvas.Height = 0;
            return;
        }

        var offsetX = contentBounds.X;
        var offsetY = contentBounds.Y;
        if (!DoubleUtil.AreClose(offsetX, 0) || !DoubleUtil.AreClose(offsetY, 0))
        {
            foreach (var child in canvas.Children.OfType<FrameworkElement>())
            {
                var left = Canvas.GetLeft(child);
                if (double.IsNaN(left))
                {
                    left = 0;
                }

                var top = Canvas.GetTop(child);
                if (double.IsNaN(top))
                {
                    top = 0;
                }

                Canvas.SetLeft(child, left - offsetX);
                Canvas.SetTop(child, top - offsetY);
            }
        }

        canvas.Margin = new Thickness(offsetX, offsetY, 0, 0);
        canvas.Width = contentBounds.Width;
        canvas.Height = contentBounds.Height;
    }

    public bool ApplyCoverChildrenSize(FrameworkElement element, IReadOnlyDictionary<string, string> attributes, Size contentSize)
    {
        var resized = false;

        if (UsesCoverChildren(attributes, "WidthSizePolicy"))
        {
            var targetWidth = Math.Max(
                ResolveDouble(attributes.TryGetValue("SuggestedWidth", out var suggestedWidth) ? suggestedWidth : null, 0),
                contentSize.Width);
            if (!DoubleUtil.AreClose(element.Width, targetWidth))
            {
                element.Width = targetWidth;
                resized = true;
            }
        }

        if (UsesCoverChildren(attributes, "HeightSizePolicy"))
        {
            var targetHeight = Math.Max(
                ResolveDouble(attributes.TryGetValue("SuggestedHeight", out var suggestedHeight) ? suggestedHeight : null, 0),
                contentSize.Height);
            if (!DoubleUtil.AreClose(element.Height, targetHeight))
            {
                element.Height = targetHeight;
                resized = true;
            }
        }

        return resized;
    }

    public bool UsesCoverChildren(IReadOnlyDictionary<string, string> attributes, string policyKey)
    {
        return attributes.TryGetValue(policyKey, out var policy) &&
               string.Equals(policy, "CoverChildren", StringComparison.OrdinalIgnoreCase);
    }

    public Point ComputePosition(
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        Size elementSize,
        Rect? alignmentBounds = null)
    {
        var horizontalAlignment = attributes.TryGetValue("HorizontalAlignment", out var horizontalValue) ? horizontalValue : "Left";
        var verticalAlignment = attributes.TryGetValue("VerticalAlignment", out var verticalValue) ? verticalValue : "Top";
        var offsetX = ResolveDouble(attributes.TryGetValue("PositionXOffset", out var xOffsetValue) ? xOffsetValue : null, 0);
        var offsetY = ResolveDouble(attributes.TryGetValue("PositionYOffset", out var yOffsetValue) ? yOffsetValue : null, 0);
        var marginLeft = ResolveDouble(attributes.TryGetValue("MarginLeft", out var marginLeftValue) ? marginLeftValue : null, 0);
        var marginTop = ResolveDouble(attributes.TryGetValue("MarginTop", out var marginTopValue) ? marginTopValue : null, 0);
        var marginRight = ResolveDouble(attributes.TryGetValue("MarginRight", out var marginRightValue) ? marginRightValue : null, 0);
        var marginBottom = ResolveDouble(attributes.TryGetValue("MarginBottom", out var marginBottomValue) ? marginBottomValue : null, 0);
        var anchorBounds = alignmentBounds is { IsEmpty: false } bounds
            ? bounds
            : new Rect(new Point(0, 0), elementSize);

        var anchorLeft = horizontalAlignment switch
        {
            "Center" => (parentSize.Width - anchorBounds.Width) / 2 + offsetX + (marginLeft - marginRight) / 2,
            "Right" => parentSize.Width - anchorBounds.Width - marginRight - offsetX,
            _ => marginLeft + offsetX
        };

        var anchorTop = verticalAlignment switch
        {
            "Center" => (parentSize.Height - anchorBounds.Height) / 2 + offsetY + (marginTop - marginBottom) / 2,
            "Bottom" => parentSize.Height - anchorBounds.Height - marginBottom - offsetY,
            _ => marginTop + offsetY
        };

        return new Point(anchorLeft - anchorBounds.X, anchorTop - anchorBounds.Y);
    }

    private static class DoubleUtil
    {
        public static bool AreClose(double first, double second)
        {
            return Math.Abs(first - second) < 0.1;
        }
    }

    public SpriteDefinition? FindSprite(string? spriteName)
    {
        return _assetCatalog.FindSprite(spriteName);
    }

    public bool IsSpriteMissing(string? spriteName)
    {
        return !string.IsNullOrWhiteSpace(spriteName) &&
               ResolveSpriteAsset(spriteName).Kind == SpriteAssetResolutionKind.Missing;
    }

    public BrushDefinition? FindBrush(string? brushName)
    {
        return _assetCatalog.FindBrush(brushName);
    }

    public void AttachElementDebugMetadata(
        FrameworkElement element,
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        IEnumerable<string?>? spriteNames = null,
        IEnumerable<string?>? brushNames = null,
        bool isDisabled = false)
    {
        PreviewElementDebugProperties.SetDebugText(
            element,
            BuildElementDebugText(nodeName, attributes, spriteNames, brushNames, isDisabled));
        PreviewElementDebugProperties.SetTreeLabel(element, BuildElementTreeLabel(nodeName, attributes));
    }

    public bool BrushExists(string? brushName)
    {
        if (string.IsNullOrWhiteSpace(brushName))
        {
            return false;
        }

        return FindBrush(brushName) is not null;
    }

    public double ResolveTextFontSize(string? brushId, string? rawFontSize, bool isDisabled, double fallback)
    {
        if (!string.IsNullOrWhiteSpace(rawFontSize))
        {
            return ResolveDouble(rawFontSize, fallback);
        }

        var style = ResolveBrushStyle(ResolveCatalogBrush(brushId), isDisabled);
        return style?.FontSize ?? fallback;
    }

    private SpriteDefinition? ResolveCatalogSprite(string? spriteName)
    {
        return ResolveSpriteAsset(spriteName).SpriteDefinition;
    }

    private SpriteAssetResolution ResolveSpriteAsset(string? spriteName)
    {
        var spriteAsset = _assetCatalog.ResolveSpriteAsset(spriteName);
        if (!string.IsNullOrWhiteSpace(spriteName))
        {
            _diagnostics.AddResolvedSprite(spriteName, spriteAsset);
        }

        return spriteAsset;
    }

    private BrushDefinition? ResolveCatalogBrush(string? brushName)
    {
        var brush = FindBrush(brushName);
        if (brush is not null && !string.IsNullOrWhiteSpace(brushName))
        {
            _diagnostics.AddResolvedBrush(brushName, brush);
        }

        return brush;
    }

    private BrushDebugInfo ResolveBrushDebugInfo(string? brushName, bool isDisabled)
    {
        if (string.IsNullOrWhiteSpace(brushName))
        {
            return new BrushDebugInfo(string.Empty, null, false, []);
        }

        var brushDefinition = FindBrush(brushName);
        if (brushDefinition is null)
        {
            return new BrushDebugInfo(
                brushName,
                null,
                PanelBrushes.ContainsKey(brushName) || TextBrushes.ContainsKey(brushName),
                []);
        }

        var style = ResolveBrushStyle(brushDefinition, isDisabled);
        var visibleLayers = brushDefinition.Layers
            .Select(layer =>
            {
                BrushLayerDefinition? overrideLayer = null;
                style?.LayerOverrides.TryGetValue(layer.Name, out overrideLayer);
                return layer.Merge(overrideLayer);
            })
            .Where(layer => layer.IsHidden != true)
            .ToArray();

        return new BrushDebugInfo(brushName, brushDefinition, false, visibleLayers);
    }

    private IReadOnlyList<BrushLayerDefinition> ResolveVisibleBrushLayers(string? brushName, bool isDisabled)
    {
        var brush = ResolveCatalogBrush(brushName);
        if (brush is null)
        {
            return [];
        }

        var style = ResolveBrushStyle(brush, isDisabled);
        var layers = brush.Layers
            .Select(layer =>
            {
                BrushLayerDefinition? overrideLayer = null;
                style?.LayerOverrides.TryGetValue(layer.Name, out overrideLayer);
                return layer.Merge(overrideLayer);
            })
            .Where(layer => layer.IsHidden != true)
            .ToArray();

        return layers;
    }

    private BrushStyleDefinition? ResolveBrushStyle(BrushDefinition? brush, bool isDisabled)
    {
        if (brush is null)
        {
            return null;
        }

        var state = isDisabled ? "Disabled" : "Default";
        if (brush.Styles.TryGetValue(state, out var style))
        {
            return style;
        }

        if (brush.Styles.TryGetValue("Default", out style))
        {
            return style;
        }

        return brush.Styles.Values.FirstOrDefault();
    }

    private SpriteDefinition? ResolvePrimaryBrushSprite(string? brushName, bool isDisabled)
    {
        var spriteName = ResolveVisibleBrushLayers(brushName, isDisabled)
            .Select(layer => layer.SpriteName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        return ResolveCatalogSprite(spriteName);
    }

    private ImageSource? ResolveSpriteImage(string? spriteName)
    {
        var spriteAsset = ResolveSpriteAsset(spriteName);
        return spriteAsset.HasImage
            ? _assetCatalog.GetSpriteImage(spriteAsset.MatchedKey)
            : null;
    }

    private bool TryCreateBrushImageBrush(string? brushName, bool isDisabled, out Brush brush)
    {
        var referenceSprite = ResolvePrimaryBrushSprite(brushName, isDisabled);
        var referenceWidth = Math.Max(
            1,
            _brushMetricsProvider.ResolveMetric(brushName, "Default", "Width") ??
            referenceSprite?.Width ??
            1);
        var referenceHeight = Math.Max(
            1,
            _brushMetricsProvider.ResolveMetric(brushName, "Default", "Height") ??
            referenceSprite?.Height ??
            1);

        var visual = CreateBrushOverlay(brushName, isDisabled, referenceWidth, referenceHeight);
        if (visual is null)
        {
            brush = null!;
            return false;
        }

        brush = new VisualBrush(visual)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        return true;
    }

    private FrameworkElement? CreateBrushLayerVisual(BrushLayerDefinition layer, double referenceWidth, double referenceHeight)
    {
        var extendedWidth = Math.Max(1, referenceWidth + (layer.ExtendLeft ?? 0) + (layer.ExtendRight ?? 0));
        var extendedHeight = Math.Max(1, referenceHeight + (layer.ExtendTop ?? 0) + (layer.ExtendBottom ?? 0));

        var layerVisual = new Grid
        {
            Width = extendedWidth,
            Height = extendedHeight,
            Margin = new Thickness(
                -(layer.ExtendLeft ?? 0),
                -(layer.ExtendTop ?? 0),
                -(layer.ExtendRight ?? 0),
                -(layer.ExtendBottom ?? 0)),
            ClipToBounds = false,
            IsHitTestVisible = false,
            Opacity = Math.Clamp(layer.AlphaFactor ?? 1.0, 0, 1)
        };

        var mainVisual = CreateSpriteVisual(layer.SpriteName, extendedWidth, extendedHeight);
        if (mainVisual is not null)
        {
            layerVisual.Children.Add(mainVisual);
        }

        var overlayVisual = CreateOverlayVisual(layer, extendedWidth, extendedHeight, mainVisual);
        if (overlayVisual is not null)
        {
            layerVisual.Children.Add(overlayVisual);
        }

        if (layerVisual.Children.Count == 0)
        {
            return null;
        }

        if (layer.HorizontalFlip == true || layer.VerticalFlip == true)
        {
            layerVisual.RenderTransform = new ScaleTransform(
                layer.HorizontalFlip == true ? -1 : 1,
                layer.VerticalFlip == true ? -1 : 1);
            layerVisual.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        return layerVisual;
    }

    private FrameworkElement? CreateOverlayVisual(
        BrushLayerDefinition layer,
        double width,
        double height,
        FrameworkElement? mainVisual)
    {
        if (string.IsNullOrWhiteSpace(layer.OverlaySpriteName))
        {
            return null;
        }

        var featureVisual = _brushOverlayFeatureHandler.CreateOverlayVisual(
            new BrushOverlayFeatureRequest(
                layer.OverlayMethod,
                layer.OverlaySpriteName,
                width,
                height,
                mainVisual,
                CreateSpriteVisual,
                ResolveSpriteImage,
                CreateVisualClone));
        if (featureVisual is not null)
        {
            return featureVisual;
        }

        return CreateSpriteVisual(layer.OverlaySpriteName, width, height);
    }

    private FrameworkElement CreateVisualClone(FrameworkElement sourceVisual, double width, double height)
    {
        var brush = new VisualBrush(sourceVisual)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };

        return new Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            IsHitTestVisible = false
        };
    }

    private FrameworkElement? CreateSpriteVisual(string? spriteName, double width, double height)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        var spriteDefinition = ResolveCatalogSprite(spriteName);
        var spriteImage = ResolveSpriteImage(spriteName);
        if (spriteImage is not BitmapSource bitmapSource)
        {
            return spriteImage is null
                ? null
                : new Image
                {
                    Source = spriteImage,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };
        }

        if (spriteDefinition?.IsNineRegion == true)
        {
            return CreateNineSliceVisual(bitmapSource, spriteDefinition, width, height);
        }

        return new Image
        {
            Source = spriteImage,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private Int32Rect? GetSpriteOpaqueBounds(string cacheKey, BitmapSource bitmapSource)
    {
        if (_spriteOpaqueBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
        {
            return cachedBounds;
        }

        var width = bitmapSource.PixelWidth;
        var height = bitmapSource.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            _spriteOpaqueBoundsCache[cacheKey] = null;
            return null;
        }

        var normalized = bitmapSource.Format == PixelFormats.Bgra32
            ? bitmapSource
            : new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[rowOffset + (x * 4) + 3];
                if (alpha < SignificantAlphaThreshold)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        Int32Rect? bounds = maxX >= 0 && maxY >= 0
            ? new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : null;
        _spriteOpaqueBoundsCache[cacheKey] = bounds;
        return bounds;
    }

    private (int Top, int Bottom)? GetSpriteCoreVerticalBounds(string cacheKey, BitmapSource bitmapSource)
    {
        if (_spriteCoreVerticalBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
        {
            return cachedBounds;
        }

        var width = bitmapSource.PixelWidth;
        var height = bitmapSource.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            _spriteCoreVerticalBoundsCache[cacheKey] = null;
            return null;
        }

        var normalized = bitmapSource.Format == PixelFormats.Bgra32
            ? bitmapSource
            : new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        var centerX = width / 2;
        var halfBand = Math.Min(4, Math.Max(0, width / 40));
        var startX = Math.Max(0, centerX - halfBand);
        var endX = Math.Min(width - 1, centerX + halfBand);

        var top = -1;
        var bottom = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            var hasCorePixel = false;
            for (var x = startX; x <= endX; x++)
            {
                var alpha = pixels[rowOffset + (x * 4) + 3];
                if (alpha < ContentCoreAlphaThreshold)
                {
                    continue;
                }

                hasCorePixel = true;
                break;
            }

            if (!hasCorePixel)
            {
                continue;
            }

            if (top < 0)
            {
                top = y;
            }

            bottom = y;
        }

        (int Top, int Bottom)? bounds = top >= 0 && bottom >= top
            ? (top, bottom)
            : null;
        _spriteCoreVerticalBoundsCache[cacheKey] = bounds;
        return bounds;
    }

    public double ApplyVisualTextCentering(TextBlock textBlock, double availableWidth)
    {
        if (textBlock.FontSize <= 0)
        {
            return 0;
        }

        textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        textBlock.LineHeight = textBlock.FontSize;

        var offset = ResolveTextVisualVerticalOffset(textBlock, availableWidth);
        if (Math.Abs(offset) < 0.1)
        {
            return 0;
        }

        textBlock.RenderTransform = new TranslateTransform(0, offset);
        return offset;
    }

    private static double ResolveTextVisualVerticalOffset(TextBlock textBlock, double availableWidth)
    {
        if (string.IsNullOrWhiteSpace(textBlock.Text) || textBlock.FontSize <= 0)
        {
            return 0;
        }

        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);
        var formattedText = new FormattedText(
            textBlock.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            textBlock.FontSize,
            Brushes.Black,
            1.0);

        if (textBlock.TextWrapping == TextWrapping.Wrap &&
            availableWidth > 0 &&
            !double.IsInfinity(availableWidth))
        {
            formattedText.MaxTextWidth = availableWidth;
        }

        // FormattedText.BuildGeometry uses the provided point as the baseline origin.
        // Using the real baseline keeps the glyph bounds in the same coordinate space
        // as the rendered line box instead of shifting centered titles downward.
        var glyphGeometry = formattedText.BuildGeometry(new Point(0, formattedText.Baseline));
        var bounds = glyphGeometry.Bounds;
        if (bounds.IsEmpty || formattedText.Height <= 0)
        {
            return 0;
        }

        var lineHeight = textBlock.LineHeight > 0 ? textBlock.LineHeight : formattedText.Height;
        var offset = ((lineHeight - bounds.Height) / 2d) - bounds.Top;
        return Math.Clamp(offset, -textBlock.FontSize * 0.35, textBlock.FontSize * 0.35);
    }

    private FrameworkElement CreateNineSliceVisual(
        BitmapSource bitmapSource,
        SpriteDefinition spriteDefinition,
        double targetWidth,
        double targetHeight)
    {
        var leftWidth = spriteDefinition.LeftWidth ?? 0;
        var rightWidth = spriteDefinition.RightWidth ?? 0;
        var topHeight = spriteDefinition.TopHeight ?? 0;
        var bottomHeight = spriteDefinition.BottomHeight ?? 0;

        double actualLeft = leftWidth;
        double actualRight = rightWidth;
        double actualTop = topHeight;
        double actualBottom = bottomHeight;

        if (actualLeft + actualRight > targetWidth && actualLeft + actualRight > 0)
        {
            var scale = targetWidth / (actualLeft + actualRight);
            actualLeft *= scale;
            actualRight *= scale;
        }

        if (actualTop + actualBottom > targetHeight && actualTop + actualBottom > 0)
        {
            var scale = targetHeight / (actualTop + actualBottom);
            actualTop *= scale;
            actualBottom *= scale;
        }

        var centerWidth = Math.Max(0, targetWidth - actualLeft - actualRight);
        var centerHeight = Math.Max(0, targetHeight - actualTop - actualBottom);

        var grid = new Grid
        {
            Width = targetWidth,
            Height = targetHeight,
            ClipToBounds = false,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actualLeft) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(centerWidth, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actualRight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(actualTop) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(centerHeight, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(actualBottom) });

        var pixelWidth = bitmapSource.PixelWidth;
        var pixelHeight = bitmapSource.PixelHeight;
        var centerPixelWidth = Math.Max(0, pixelWidth - leftWidth - rightWidth);
        var centerPixelHeight = Math.Max(0, pixelHeight - topHeight - bottomHeight);

        AddNineSliceSegment(grid, bitmapSource, 0, 0, leftWidth, topHeight, 0, 0);
        AddNineSliceSegment(grid, bitmapSource, leftWidth, 0, centerPixelWidth, topHeight, 0, 1);
        AddNineSliceSegment(grid, bitmapSource, leftWidth + centerPixelWidth, 0, rightWidth, topHeight, 0, 2);
        AddNineSliceSegment(grid, bitmapSource, 0, topHeight, leftWidth, centerPixelHeight, 1, 0);
        AddNineSliceSegment(grid, bitmapSource, leftWidth, topHeight, centerPixelWidth, centerPixelHeight, 1, 1);
        AddNineSliceSegment(grid, bitmapSource, leftWidth + centerPixelWidth, topHeight, rightWidth, centerPixelHeight, 1, 2);
        AddNineSliceSegment(grid, bitmapSource, 0, topHeight + centerPixelHeight, leftWidth, bottomHeight, 2, 0);
        AddNineSliceSegment(grid, bitmapSource, leftWidth, topHeight + centerPixelHeight, centerPixelWidth, bottomHeight, 2, 1);
        AddNineSliceSegment(grid, bitmapSource, leftWidth + centerPixelWidth, topHeight + centerPixelHeight, rightWidth, bottomHeight, 2, 2);

        return grid;
    }

    private static void AddNineSliceSegment(
        Grid grid,
        BitmapSource source,
        int x,
        int y,
        int width,
        int height,
        int row,
        int column)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();

        var image = new Image
        {
            Source = cropped,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        Grid.SetRow(image, row);
        Grid.SetColumn(image, column);
        grid.Children.Add(image);
    }

    private bool TryResolveTextColor(string? brushName, bool isDisabled, out Color color)
    {
        var style = ResolveBrushStyle(ResolveCatalogBrush(brushName), isDisabled);
        if (style is not null &&
            TryResolveStyledColor(style.FontColor, style.TextAlphaFactor, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    private bool TryCreateBrushFromCatalog(string? brushName, bool isDisabled, out Brush brush)
    {
        var visibleLayers = ResolveVisibleBrushLayers(brushName, isDisabled)
            .Where(layer => TryResolveLayerColor(layer, out _))
            .ToArray();
        if (visibleLayers.Length == 0)
        {
            brush = null!;
            return false;
        }

        if (visibleLayers.Length == 1 && TryResolveLayerColor(visibleLayers[0], out var singleColor))
        {
            brush = new SolidColorBrush(singleColor);
            return true;
        }

        var firstColor = TryResolveLayerColor(visibleLayers[0], out var startColor) ? startColor : Colors.Transparent;
        var lastColor = TryResolveLayerColor(visibleLayers[^1], out var endColor) ? endColor : firstColor;
        brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(firstColor, 0),
                new(lastColor, 1)
            },
            new Point(0, 0),
            new Point(1, 1));
        return true;
    }

    private FontFamily ResolveFontFamily(string? brushName)
    {
        var brush = ResolveCatalogBrush(brushName);
        return !string.IsNullOrWhiteSpace(brush?.FontName)
            ? new FontFamily(brush.FontName)
            : new FontFamily("Segoe UI");
    }

    private TextAlignment ResolveTextAlignment(string? brushName, string? rawOverride = null)
    {
        var rawAlignment = rawOverride ?? ResolveCatalogBrush(brushName)?.TextHorizontalAlignment;
        return rawAlignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            "Justified" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };
    }

    private bool TryResolveStyledColor(string? rawColor, double? alphaFactor, out Color color)
    {
        var parsedColor = ParseBannerlordColor(rawColor);
        if (parsedColor is null)
        {
            color = default;
            return false;
        }

        color = AdjustColor(parsedColor.Value, 1.0, alphaFactor ?? 1.0);
        return true;
    }

    private bool TryResolveLayerColor(BrushLayerDefinition layer, out Color color)
    {
        var parsedColor = ParseBannerlordColor(layer.Color);
        if (parsedColor is null)
        {
            color = default;
            return false;
        }

        color = AdjustColor(parsedColor.Value, layer.ColorFactor ?? 1.0, layer.AlphaFactor ?? 1.0);
        return true;
    }

    private static Color AdjustColor(Color color, double colorFactor, double alphaFactor)
    {
        var red = ClampByte(color.R * colorFactor);
        var green = ClampByte(color.G * colorFactor);
        var blue = ClampByte(color.B * colorFactor);
        var alpha = ClampByte(color.A * alphaFactor);
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static byte ClampByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue);
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string rawValue)
    {
        return rawValue switch
        {
            "Center" => HorizontalAlignment.Center,
            "Right" => HorizontalAlignment.Right,
            "Stretch" => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Left
        };
    }

    private static VerticalAlignment ParseVerticalAlignment(string rawValue)
    {
        return rawValue switch
        {
            "Center" => VerticalAlignment.Center,
            "Bottom" => VerticalAlignment.Bottom,
            "Stretch" => VerticalAlignment.Stretch,
            _ => VerticalAlignment.Top
        };
    }

    public static Color? ParseBannerlordColor(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var value = rawValue.Trim();
        if (!value.StartsWith('#'))
        {
            return null;
        }

        var hex = value[1..];
        return hex.Length switch
        {
            6 when byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
                   byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
                   byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
                => Color.FromRgb(red, green, blue),
            8 when byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                   byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                   byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) &&
                   byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
                => Color.FromArgb(a, r, g, b),
            _ => null
        };
    }

    private double? ResolveDimension(
        IReadOnlyDictionary<string, string> attributes,
        string policyKey,
        string sizeKey,
        double parentSize)
    {
        attributes.TryGetValue(policyKey, out var policy);
        var suggestedSize = ResolveDouble(attributes.TryGetValue(sizeKey, out var rawSize) ? rawSize : null, 0);
        var startMarginKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MarginLeft" : "MarginTop";
        var endMarginKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MarginRight" : "MarginBottom";
        var marginStart = ResolveDouble(attributes.TryGetValue(startMarginKey, out var startMarginValue) ? startMarginValue : null, 0);
        var marginEnd = ResolveDouble(attributes.TryGetValue(endMarginKey, out var endMarginValue) ? endMarginValue : null, 0);

        return policy switch
        {
            "Fixed" when suggestedSize > 0 => suggestedSize,
            "StretchToParent" => Math.Max(0, parentSize - marginStart - marginEnd),
            "CoverChildren" when suggestedSize > 0 => suggestedSize,
            _ when suggestedSize > 0 => suggestedSize,
            _ => null
        };
    }

    private static Brush CreateGradientBrush(string fill, string stroke)
    {
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(ParseBannerlordColor(fill) ?? Colors.Transparent, 0),
                new(ParseBannerlordColor(stroke) ?? Colors.Transparent, 1)
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private static SolidColorBrush CreateSolidBrushInternal(string hex)
    {
        return new(ParseBannerlordColor(hex) ?? Colors.Transparent);
    }

    private static string HumanizeBindingKey(string key)
    {
        var trimmed = key
            .Replace("String", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Title", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Text", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Icon", " Icon", StringComparison.OrdinalIgnoreCase)
            .Replace("ID", " ID", StringComparison.OrdinalIgnoreCase);

        var buffer = new List<char>(trimmed.Length * 2);
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (index > 0 && char.IsUpper(current) && char.IsLetterOrDigit(trimmed[index - 1]))
            {
                buffer.Add(' ');
            }

            buffer.Add(current);
        }

        return string.Join(" ", new string(buffer.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private string BuildElementDebugText(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        IEnumerable<string?>? spriteNames,
        IEnumerable<string?>? brushNames,
        bool isDisabled)
    {
        var lines = new List<string> { $"Node: {nodeName}" };

        if (attributes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Attributes");
            foreach (var attribute in attributes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var resolvedValue = ResolveDebugValue(attribute.Value);
                lines.Add(string.Equals(resolvedValue, attribute.Value, StringComparison.Ordinal)
                    ? $"{attribute.Key}: {attribute.Value}"
                    : $"{attribute.Key}: {attribute.Value} -> {resolvedValue}");
            }
        }

        var resolvedBrushes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (brushNames is not null)
        {
            foreach (var brushName in brushNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                resolvedBrushes.Add(brushName!);
            }
        }

        var resolvedSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spriteNames is not null)
        {
            foreach (var spriteName in spriteNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                resolvedSprites.Add(spriteName!);
            }
        }

        if (resolvedBrushes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Brushes");
            foreach (var brushName in resolvedBrushes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                AppendBrushDebug(lines, brushName, isDisabled, resolvedSprites);
            }
        }

        if (resolvedSprites.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Sprites");
            foreach (var spriteName in resolvedSprites.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                AppendSpriteDebug(lines, spriteName, "Direct");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string ResolveDebugValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        if (rawValue[0] == '@')
        {
            return ResolveBinding(rawValue);
        }

        if (rawValue[0] == '!')
        {
            var constantKey = rawValue[1..];
            return _constants.TryGetValue(constantKey, out var constantValue)
                ? constantValue
                : rawValue;
        }

        return rawValue;
    }

    private string BuildElementTreeLabel(string nodeName, IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("Id", out var idValue) && !string.IsNullOrWhiteSpace(idValue))
        {
            return $"{nodeName} [{ResolveDebugValue(idValue)}]";
        }

        if (attributes.TryGetValue("Text", out var textValue) && !string.IsNullOrWhiteSpace(textValue))
        {
            return $"{nodeName}: {TrimTreeLabel(ResolveDebugValue(textValue), 36)}";
        }

        if (attributes.TryGetValue("Brush", out var brushValue) && !string.IsNullOrWhiteSpace(brushValue))
        {
            return $"{nodeName} ({ResolveDebugValue(brushValue)})";
        }

        if (attributes.TryGetValue("Sprite", out var spriteValue) && !string.IsNullOrWhiteSpace(spriteValue))
        {
            return $"{nodeName} ({ResolveDebugValue(spriteValue)})";
        }

        return nodeName;
    }

    private static string TrimTreeLabel(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..Math.Max(0, maxLength - 1)]}...";
    }

    private void AppendBrushDebug(
        ICollection<string> lines,
        string brushName,
        bool isDisabled,
        ISet<string> resolvedSprites)
    {
        var brushInfo = ResolveBrushDebugInfo(brushName, isDisabled);
        if (brushInfo.BrushDefinition is null)
        {
            lines.Add(brushInfo.IsPreviewFallback
                ? $"Brush: {brushName} (preview fallback)"
                : $"Brush: {brushName} (missing)");
            return;
        }

        lines.Add($"Brush: {brushInfo.RequestedName}");
        lines.Add($"  Source: {brushInfo.BrushDefinition.SourcePath}");
        lines.Add($"  LayerCount: {brushInfo.VisibleLayers.Count}");

        foreach (var layer in brushInfo.VisibleLayers)
        {
            var layerBuilder = new StringBuilder($"  Layer {layer.Name}:");
            if (!string.IsNullOrWhiteSpace(layer.SpriteName))
            {
                layerBuilder.Append($" Sprite={layer.SpriteName}");
                resolvedSprites.Add(layer.SpriteName);
            }

            if (!string.IsNullOrWhiteSpace(layer.OverlaySpriteName))
            {
                layerBuilder.Append($" Overlay={layer.OverlaySpriteName}");
                resolvedSprites.Add(layer.OverlaySpriteName);
            }

            if (!string.IsNullOrWhiteSpace(layer.OverlayMethod))
            {
                layerBuilder.Append($" OverlayMethod={layer.OverlayMethod}");
            }

            if (layer.AlphaFactor.HasValue)
            {
                layerBuilder.Append($" Alpha={layer.AlphaFactor.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            if (layer.MaxExtend > 0)
            {
                layerBuilder.Append(
                    $" Extend=({(layer.ExtendLeft ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                    $"{(layer.ExtendTop ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                    $"{(layer.ExtendRight ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                    $"{(layer.ExtendBottom ?? 0).ToString(CultureInfo.InvariantCulture)})");
            }

            if (layer.HorizontalFlip == true || layer.VerticalFlip == true)
            {
                layerBuilder.Append($" Flip=({(layer.HorizontalFlip == true ? "H" : "-")},{(layer.VerticalFlip == true ? "V" : "-")})");
            }

            lines.Add(layerBuilder.ToString());
        }
    }

    public void ApplyFlowLayout(FrameworkElement element, IReadOnlyDictionary<string, string> attributes)
    {
        var marginLeft = ResolveDouble(attributes.TryGetValue("MarginLeft", out var marginLeftValue) ? marginLeftValue : null, 0);
        var marginTop = ResolveDouble(attributes.TryGetValue("MarginTop", out var marginTopValue) ? marginTopValue : null, 0);
        var marginRight = ResolveDouble(attributes.TryGetValue("MarginRight", out var marginRightValue) ? marginRightValue : null, 0);
        var marginBottom = ResolveDouble(attributes.TryGetValue("MarginBottom", out var marginBottomValue) ? marginBottomValue : null, 0);
        element.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);

        if (attributes.TryGetValue("HorizontalAlignment", out var horizontalAlignment))
        {
            element.HorizontalAlignment = ParseHorizontalAlignment(horizontalAlignment);
        }
        else if (attributes.TryGetValue("WidthSizePolicy", out var widthPolicy) &&
                 string.Equals(widthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (attributes.TryGetValue("VerticalAlignment", out var verticalAlignment))
        {
            element.VerticalAlignment = ParseVerticalAlignment(verticalAlignment);
        }
        else if (attributes.TryGetValue("HeightSizePolicy", out var heightPolicy) &&
                 string.Equals(heightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private void AppendSpriteDebug(ICollection<string> lines, string spriteName, string usage)
    {
        var spriteAsset = _assetCatalog.ResolveSpriteAsset(spriteName);
        lines.Add($"Sprite: {spriteName} [{usage}]");
        lines.Add($"  Status: {spriteAsset.Kind}");
        if (!string.IsNullOrWhiteSpace(spriteAsset.MatchedKey))
        {
            lines.Add($"  ImageKey: {spriteAsset.MatchedKey}");
        }

        if (!string.IsNullOrWhiteSpace(spriteAsset.ImagePath))
        {
            lines.Add($"  ImagePath: {spriteAsset.ImagePath}");
        }

        if (spriteAsset.SpriteDefinition is not null)
        {
            var definition = spriteAsset.SpriteDefinition;
            lines.Add(
                $"  Catalog: {definition.Name} ({definition.Kind}, {definition.Width}x{definition.Height}, {definition.CategoryName}, Sheet {definition.SheetId})");
            lines.Add($"  SpritePart: {definition.SpritePartName}");
            lines.Add($"  Source: {definition.SourcePath}");
        }

        if (!string.IsNullOrWhiteSpace(spriteAsset.Note))
        {
            lines.Add($"  Note: {spriteAsset.Note}");
        }
    }
}
