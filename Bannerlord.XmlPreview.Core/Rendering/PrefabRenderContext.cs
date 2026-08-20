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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text;

namespace Bannerlord.XmlPreview.Rendering;

public sealed class PrefabRenderContext
{
    private const byte SignificantAlphaThreshold = 64;
    private const byte ContentCoreAlphaThreshold = 192;
    private readonly Dictionary<string, Int32Rect?> _spriteOpaqueBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Top, int Bottom)?> _spriteCoreVerticalBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderedWidgetRegistration> _renderedWidgetsByPath = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly PrefabWidgetPropertyMap _propertyMap;
    private readonly PrefabWidgetReferenceMap _referenceMap;
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
        PrefabWidgetPropertyMap propertyMap,
        PrefabWidgetReferenceMap referenceMap,
        PreviewDiagnostics diagnostics,
        IAssetCatalog assetCatalog,
        IBrushMetricsProvider brushMetricsProvider,
        IBrushOverlayFeatureHandler brushOverlayFeatureHandler,
        IPreviewDataSourceProvider previewDataSourceProvider,
        IWidgetTextSourceProvider widgetTextSourceProvider,
        IWidgetDebugMetadataProvider widgetDebugMetadataProvider)
    {
        _constants = constants;
        _propertyMap = propertyMap;
        _referenceMap = referenceMap;
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

    public bool ResolveBoolean(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        bool fallback = false)
    {
        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property) &&
            property.TryGetBoolean(out var typedValue))
        {
            return typedValue;
        }

        return ResolveBoolean(
            attributes.TryGetValue(attributeName, out var rawValue) ? rawValue : null,
            fallback);
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

    public double ResolveDouble(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        double fallback)
    {
        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property) &&
            property.TryGetDouble(out var typedValue))
        {
            return typedValue;
        }

        return ResolveDouble(
            attributes.TryGetValue(attributeName, out var rawValue) ? rawValue : null,
            fallback);
    }

    public bool IsDisabled(IReadOnlyDictionary<string, string> attributes)
    {
        if (HasAttribute(attributes, "IsEnabled") &&
            !ResolveBoolean(attributes, "IsEnabled", true))
        {
            return true;
        }

        return HasAttribute(attributes, "IsDisabled") &&
               ResolveBoolean(attributes, "IsDisabled");
    }

    public bool TryGetResolvedWidgetProperty(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        out ResolvedWidgetProperty property)
    {
        var widgetPath = TryGetWidgetPath(attributes);
        if (widgetPath is not null &&
            _propertyMap.TryGetProperty(widgetPath, attributeName, out property))
        {
            return true;
        }

        property = null!;
        return false;
    }

    public bool TryGetResolvedWidgetReference(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        out ResolvedWidgetReference reference)
    {
        var widgetPath = TryGetWidgetPath(attributes);
        if (widgetPath is not null &&
            _referenceMap.TryGetReference(widgetPath, attributeName, out reference))
        {
            return true;
        }

        reference = null!;
        return false;
    }

    public bool TryResolveReferencedElement(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        out FrameworkElement element)
    {
        if (TryResolveReferencedWidget(attributes, attributeName, out var widget))
        {
            element = widget.Element;
            return true;
        }

        element = null!;
        return false;
    }

    public bool TryMeasureReferencedElement(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        Size availableSize,
        out Size measuredSize)
    {
        if (TryResolveReferencedWidget(attributes, attributeName, out var widget))
        {
            measuredSize = Measure(widget.Element, widget.Attributes, availableSize);
            return true;
        }

        measuredSize = default;
        return false;
    }

    public string? ResolveAttributeLiteral(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName)
    {
        if (!attributes.TryGetValue(attributeName, out var rawValue))
        {
            return null;
        }

        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property) &&
            property.ParsedValue is not null)
        {
            return property.ParsedValue.ToString();
        }

        return rawValue;
    }

    private static bool HasAttribute(IReadOnlyDictionary<string, string> attributes, string attributeName)
    {
        return attributes.ContainsKey(attributeName);
    }

    private bool TryResolveReferencedWidget(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        out RenderedWidgetRegistration widget)
    {
        widget = null!;

        if (!TryGetResolvedWidgetReference(attributes, attributeName, out var reference) ||
            !reference.IsResolved ||
            string.IsNullOrWhiteSpace(reference.TargetWidgetPath))
        {
            return false;
        }

        return _renderedWidgetsByPath.TryGetValue(reference.TargetWidgetPath, out widget!);
    }

    private static string? TryGetWidgetPath(IReadOnlyDictionary<string, string> attributes)
    {
        return attributes.TryGetValue(PrefabVisualTreeHelper.DebugWidgetPathAttributeName, out var widgetPath) &&
               !string.IsNullOrWhiteSpace(widgetPath)
            ? widgetPath
            : null;
    }

    private Size ResolveRegisteredParentSize(RenderedWidgetRegistration widget)
    {
        if (TryGetParentWidgetPath(widget.WidgetPath) is { } parentPath &&
            _renderedWidgetsByPath.TryGetValue(parentPath, out var parentWidget))
        {
            return Measure(parentWidget.Element, parentWidget.Attributes, parentWidget.ParentSize);
        }

        return widget.ParentSize;
    }

    private static string? TryGetParentWidgetPath(string widgetPath)
    {
        var separatorIndex = widgetPath.LastIndexOf('\\');
        return separatorIndex > 0 ? widgetPath[..separatorIndex] : null;
    }

    private static bool IsInternalDebugAttribute(string attributeName)
    {
        return attributeName.StartsWith("Debug.", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFillWidgetReferenceBehavior(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "FillWidget", out var fillWidget))
        {
            return;
        }

        var maxAmount = ResolveDouble(widget.Attributes, "MaxAmountAsFloat", double.NaN);
        if (double.IsNaN(maxAmount) || maxAmount <= 0)
        {
            maxAmount = ResolveDouble(widget.Attributes, "MaxAmount", double.NaN);
        }

        var initialAmount = ResolveDouble(widget.Attributes, "InitialAmountAsFloat", double.NaN);
        if (double.IsNaN(initialAmount))
        {
            initialAmount = ResolveDouble(widget.Attributes, "InitialAmount", double.NaN);
        }

        if (double.IsNaN(maxAmount) || maxAmount <= 0 || double.IsNaN(initialAmount))
        {
            return;
        }

        var ratio = Math.Clamp(initialAmount / maxAmount, 0d, 1d);
        var isVertical = widget.NodeName.Contains("Vertical", StringComparison.OrdinalIgnoreCase);
        var fillFromEnd = isVertical
            ? ResolveBoolean(widget.Attributes, "IsDirectionUpward")
            : fillWidget.Element.HorizontalAlignment == HorizontalAlignment.Right;
        fillWidget.Element.RenderTransformOrigin = isVertical
            ? new Point(0.5, fillFromEnd ? 1 : 0)
            : new Point(fillFromEnd ? 1 : 0, 0.5);
        fillWidget.Element.RenderTransform = new ScaleTransform(
            isVertical ? 1d : ratio,
            isVertical ? ratio : 1d);
        fillWidget.Element.Opacity = Math.Max(fillWidget.Element.Opacity, ratio <= 0 ? 0.2 : 0.35);

        if (TryResolveReferencedWidget(widget.Attributes, "ContainerWidget", out var containerWidget))
        {
            var widgetParentSize = ResolveRegisteredParentSize(widget);
            var widgetSize = Measure(widget.Element, widget.Attributes, widgetParentSize);
            var containerSize = Measure(containerWidget.Element, containerWidget.Attributes, widgetSize);
            if (isVertical && containerSize.Height > 0 && double.IsNaN(fillWidget.Element.Height))
            {
                fillWidget.Element.Height = containerSize.Height;
            }
            else if (!isVertical && containerSize.Width > 0 && double.IsNaN(fillWidget.Element.Width))
            {
                fillWidget.Element.Width = containerSize.Width;
            }
        }
    }

    private void ApplySortVisualReferenceBehavior(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "SortVisualWidget", out var sortVisual))
        {
            return;
        }

        var state = ResolveBinding(widget.Attributes.TryGetValue("SortState", out var sortState) ? sortState : string.Empty);
        var isSelected = ResolveBoolean(widget.Attributes, "IsSelected", !string.IsNullOrWhiteSpace(state));
        var normalizedState = state.Trim();
        var showArrow = isSelected || !string.IsNullOrWhiteSpace(normalizedState);

        sortVisual.Element.Visibility = showArrow ? Visibility.Visible : Visibility.Collapsed;
        sortVisual.Element.Opacity = showArrow ? 1d : 0.35;
        sortVisual.Element.RenderTransformOrigin = new Point(0.5, 0.5);

        var isDescending =
            normalizedState.Contains("desc", StringComparison.OrdinalIgnoreCase) ||
            normalizedState.Contains("down", StringComparison.OrdinalIgnoreCase) ||
            normalizedState.Contains("high", StringComparison.OrdinalIgnoreCase);
        sortVisual.Element.RenderTransform = new RotateTransform(isDescending ? 180d : 0d);
    }

    private void ApplyTargetWidgetReferenceBehavior(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "TargetWidget", out var targetWidget))
        {
            return;
        }

        var isActive = ResolveBoolean(widget.Attributes, "Trigger", false);
        if (!isActive && HasAttribute(widget.Attributes, "BooleanCheck"))
        {
            isActive = ResolveBoolean(widget.Attributes, "BooleanCheck");
        }

        if (!isActive &&
            !HasAttribute(widget.Attributes, "Trigger") &&
            !HasAttribute(widget.Attributes, "BooleanCheck") &&
            HasAttribute(widget.Attributes, "State"))
        {
            isActive = true;
        }

        if (!isActive)
        {
            return;
        }

        var stateName = ResolveBinding(widget.Attributes.TryGetValue("State", out var state) ? state : string.Empty);
        if (string.IsNullOrWhiteSpace(stateName) && widget.Attributes.TryGetValue("TrueState", out var trueState))
        {
            stateName = ResolveBinding(trueState);
        }

        var highlightColor = stateName.Contains("warning", StringComparison.OrdinalIgnoreCase)
            ? Colors.OrangeRed
            : stateName.Contains("open", StringComparison.OrdinalIgnoreCase) || stateName.Contains("selected", StringComparison.OrdinalIgnoreCase)
                ? Colors.Gold
                : Colors.DeepSkyBlue;
        targetWidget.Element.Effect = new DropShadowEffect
        {
            Color = highlightColor,
            BlurRadius = 16,
            ShadowDepth = 0,
            Opacity = 0.8
        };
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

    public RenderedPrefabNode RegisterRenderedNode(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        RenderedPrefabNode renderedNode)
    {
        var widgetPath = TryGetWidgetPath(attributes);
        if (!string.IsNullOrWhiteSpace(widgetPath))
        {
            _renderedWidgetsByPath[widgetPath] = new RenderedWidgetRegistration(
                widgetPath,
                nodeName,
                attributes,
                parentSize,
                renderedNode.Element,
                renderedNode.ChildHost);
        }

        return renderedNode;
    }

    public void ApplyDeferredReferenceBehaviors()
    {
        foreach (var widget in _renderedWidgetsByPath.Values.ToArray())
        {
            ApplyFillWidgetReferenceBehavior(widget);
            ApplySortVisualReferenceBehavior(widget);
            ApplyTargetWidgetReferenceBehavior(widget);
        }
    }

    public bool IsVisible(IReadOnlyDictionary<string, string> attributes)
    {
        return !HasAttribute(attributes, "IsVisible") ||
               ResolveBoolean(attributes, "IsVisible", true);
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
        ApplyMeasurementBounds(element, attributes);

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
                    element.MinWidth = Math.Max(
                        element.MinWidth,
                        Math.Max(metricWidth ?? 0, padding.Left + padding.Right + 24));
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

    public void ApplyTextStyle(
        TextBlock textBlock,
        string? brushId,
        IReadOnlyDictionary<string, string> attributes,
        double fallbackFontSize)
    {
        ApplyTextStyle(
            textBlock,
            brushId,
            ResolveAttributeLiteral(attributes, "Brush.FontSize"),
            attributes,
            fallbackFontSize);
    }

    public HorizontalAlignment ResolveTextHorizontalAlignment(string? brushName, string? rawOverride = null)
    {
        return ResolveHorizontalAlignment(
            rawOverride ?? ResolveCatalogBrush(brushName)?.TextHorizontalAlignment,
            HorizontalAlignment.Left);
    }

    public HorizontalAlignment ResolveTextHorizontalAlignment(
        string? brushName,
        IReadOnlyDictionary<string, string> attributes,
        string attributeName = "Brush.TextHorizontalAlignment")
    {
        return ResolveTextHorizontalAlignment(brushName, ResolveAttributeLiteral(attributes, attributeName));
    }

    public VerticalAlignment ResolveTextVerticalAlignment(string? brushName, string? rawOverride = null)
    {
        return ResolveVerticalAlignment(
            rawOverride ?? ResolveCatalogBrush(brushName)?.TextVerticalAlignment,
            VerticalAlignment.Center);
    }

    public VerticalAlignment ResolveTextVerticalAlignment(
        string? brushName,
        IReadOnlyDictionary<string, string> attributes,
        string attributeName = "Brush.TextVerticalAlignment")
    {
        return ResolveTextVerticalAlignment(brushName, ResolveAttributeLiteral(attributes, attributeName));
    }

    public HorizontalAlignment ResolveWidgetHorizontalAlignment(
        IReadOnlyDictionary<string, string> attributes,
        HorizontalAlignment fallback = HorizontalAlignment.Left)
    {
        if (!attributes.TryGetValue("HorizontalAlignment", out var rawAlignment))
        {
            return fallback;
        }

        if (TryGetResolvedWidgetProperty(attributes, "HorizontalAlignment", out var property) &&
            property.ParsedValue is not null)
        {
            return ResolveHorizontalAlignment(property.ParsedValue.ToString(), fallback);
        }

        return ResolveHorizontalAlignment(rawAlignment, fallback);
    }

    public VerticalAlignment ResolveWidgetVerticalAlignment(
        IReadOnlyDictionary<string, string> attributes,
        VerticalAlignment fallback = VerticalAlignment.Top)
    {
        if (!attributes.TryGetValue("VerticalAlignment", out var rawAlignment))
        {
            return fallback;
        }

        if (TryGetResolvedWidgetProperty(attributes, "VerticalAlignment", out var property) &&
            property.ParsedValue is not null)
        {
            return ResolveVerticalAlignment(property.ParsedValue.ToString(), fallback);
        }

        return ResolveVerticalAlignment(rawAlignment, fallback);
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

        return new Size(
            ResolveMeasuredLength(element.Width, desired.Width),
            ResolveMeasuredLength(element.Height, desired.Height));
    }

    public Size Measure(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize)
    {
        ApplyMeasurementBounds(element, attributes);

        var measureInput = ResolveChildMeasureSize(element, attributes, parentSize);
        element.Measure(measureInput);

        var desired = element.DesiredSize;
        var width = ResolveMeasuredAxis(
            element,
            attributes,
            "WidthSizePolicy",
            "SuggestedWidth",
            measureInput.Width,
            desired.Width);
        var height = ResolveMeasuredAxis(
            element,
            attributes,
            "HeightSizePolicy",
            "SuggestedHeight",
            measureInput.Height,
            desired.Height);

        if ((width <= 0 && measureInput.Width > 0) ||
            (height <= 0 && measureInput.Height > 0))
        {
            _diagnostics.AddMeasureFallback(element.GetType().Name, measureInput.Width, measureInput.Height);
        }

        if (UsesStretchToParent(attributes, "WidthSizePolicy") &&
            !DoubleUtil.AreClose(element.Width, width))
        {
            element.Width = width;
        }

        if (UsesStretchToParent(attributes, "HeightSizePolicy") &&
            !DoubleUtil.AreClose(element.Height, height))
        {
            element.Height = height;
        }

        return new Size(width, height);
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize)
    {
        AttachChildren(host, children, parentSize, new Point(0, 0));
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize, Point origin)
    {
        foreach (var child in children)
        {
            var rendered = RegisterRenderedNode(child.Name, child.Attributes, parentSize, child.Render(this, parentSize));
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
                AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, parentSize, Measure(rendered.Element, child.Attributes, parentSize));
                stackPanel.Children.Add(rendered.Element);
                continue;
            }

            if (host is GauntletStackLayoutPanel gauntletStackPanel)
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
                PrepareGauntletStackLayoutChild(rendered.Element, child.Attributes, gauntletStackPanel.LayoutMethod);
                AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, parentSize, Measure(rendered.Element, child.Attributes, parentSize));
                gauntletStackPanel.Children.Add(rendered.Element);
                continue;
            }

            var elementSize = Measure(rendered.Element, child.Attributes, parentSize);
            var position = ComputePosition(child.Attributes, parentSize, elementSize, rendered.AlignmentBounds);
            host.Children.Add(rendered.Element);
            Canvas.SetLeft(rendered.Element, origin.X + position.X);
            Canvas.SetTop(rendered.Element, origin.Y + position.Y);
            AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, parentSize, elementSize, position, rendered.AlignmentBounds);
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
            var targetWidth = CoerceAxisToBoundaries(
                attributes,
                "MinWidth",
                "MaxWidth",
                Math.Max(element.MinWidth, Math.Max(0, contentSize.Width)));
            if (!DoubleUtil.AreClose(element.Width, targetWidth))
            {
                element.Width = targetWidth;
                resized = true;
            }
        }

        if (UsesCoverChildren(attributes, "HeightSizePolicy"))
        {
            var targetHeight = CoerceAxisToBoundaries(
                attributes,
                "MinHeight",
                "MaxHeight",
                Math.Max(element.MinHeight, Math.Max(0, contentSize.Height)));
            if (!DoubleUtil.AreClose(element.Height, targetHeight))
            {
                element.Height = targetHeight;
                resized = true;
            }
        }

        return resized;
    }

    public Size FinalizeCoverChildrenSize(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size contentSize,
        Func<Size>? relayout = null)
    {
        if (ApplyCoverChildrenSize(element, attributes, contentSize) &&
            relayout is not null)
        {
            var updatedContentSize = relayout();
            ApplyCoverChildrenSize(element, attributes, updatedContentSize);
            contentSize = updatedContentSize;
        }

        return ResolveElementRenderSize(element, contentSize);
    }

    public bool UsesCoverChildren(IReadOnlyDictionary<string, string> attributes, string policyKey)
    {
        return string.Equals(ResolveSizePolicy(attributes, policyKey), "CoverChildren", StringComparison.OrdinalIgnoreCase);
    }

    public bool UsesStretchToParent(IReadOnlyDictionary<string, string> attributes, string policyKey)
    {
        return string.Equals(ResolveSizePolicy(attributes, policyKey), "StretchToParent", StringComparison.OrdinalIgnoreCase);
    }

    public Size ResolveChildMeasureSize(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize)
    {
        return new Size(
            ResolveMeasureInputAxis(element, attributes, "WidthSizePolicy", "SuggestedWidth", "MarginLeft", "MarginRight", "MinWidth", "MaxWidth", parentSize.Width, element.Width),
            ResolveMeasureInputAxis(element, attributes, "HeightSizePolicy", "SuggestedHeight", "MarginTop", "MarginBottom", "MinHeight", "MaxHeight", parentSize.Height, element.Height));
    }

    public Size ResolveChildMeasureSize(IReadOnlyDictionary<string, string> attributes, Size parentSize)
    {
        return new Size(
            ResolveMeasureInputAxis(null, attributes, "WidthSizePolicy", "SuggestedWidth", "MarginLeft", "MarginRight", "MinWidth", "MaxWidth", parentSize.Width, double.NaN),
            ResolveMeasureInputAxis(null, attributes, "HeightSizePolicy", "SuggestedHeight", "MarginTop", "MarginBottom", "MinHeight", "MaxHeight", parentSize.Height, double.NaN));
    }

    public Size ResolveElementRenderSize(FrameworkElement element, Size fallback)
    {
        var desired = element.DesiredSize;
        return new Size(
            ResolveMeasuredLength(element.Width, ResolveMeasuredLength(desired.Width, fallback.Width)),
            ResolveMeasuredLength(element.Height, ResolveMeasuredLength(desired.Height, fallback.Height)));
    }

    public Point ComputePosition(
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        Size elementSize,
        Rect? alignmentBounds = null)
    {
        var horizontalAlignment = attributes.TryGetValue("HorizontalAlignment", out var horizontalValue)
            ? ResolveDebugValue(attributes, "HorizontalAlignment", horizontalValue)
            : "Left";
        var verticalAlignment = attributes.TryGetValue("VerticalAlignment", out var verticalValue)
            ? ResolveDebugValue(attributes, "VerticalAlignment", verticalValue)
            : "Top";
        var offsetX = ResolveDouble(attributes, "PositionXOffset", 0);
        var offsetY = ResolveDouble(attributes, "PositionYOffset", 0);
        var marginLeft = ResolveDouble(attributes, "MarginLeft", 0);
        var marginTop = ResolveDouble(attributes, "MarginTop", 0);
        var marginRight = ResolveDouble(attributes, "MarginRight", 0);
        var marginBottom = ResolveDouble(attributes, "MarginBottom", 0);
        var anchorBounds = alignmentBounds is { IsEmpty: false } bounds
            ? bounds
            : new Rect(new Point(0, 0), elementSize);
        var availableLeft = marginLeft;
        var availableTop = marginTop;
        var availableWidth = Math.Max(0, parentSize.Width - marginLeft - marginRight);
        var availableHeight = Math.Max(0, parentSize.Height - marginTop - marginBottom);

        var anchorLeft = horizontalAlignment switch
        {
            "Center" => availableLeft + (availableWidth - anchorBounds.Width) / 2,
            "Right" => availableLeft + availableWidth - anchorBounds.Width,
            _ => availableLeft
        };

        var anchorTop = verticalAlignment switch
        {
            "Center" => availableTop + (availableHeight - anchorBounds.Height) / 2,
            "Bottom" => availableTop + availableHeight - anchorBounds.Height,
            _ => availableTop
        };

        return new Point(anchorLeft - anchorBounds.X + offsetX, anchorTop - anchorBounds.Y + offsetY);
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
        var baseDebugText = BuildElementDebugText(nodeName, attributes, spriteNames, brushNames, isDisabled);
        PreviewElementDebugProperties.SetDebugBaseText(element, baseDebugText);
        PreviewElementDebugProperties.SetDebugText(element, baseDebugText);
        PreviewElementDebugProperties.SetTreeLabel(element, BuildElementTreeLabel(nodeName, attributes));
    }

    public void AttachElementLayoutDebugMetadata(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        Size measuredSize,
        Point? position = null,
        Rect? alignmentBounds = null)
    {
        var baseDebugText = PreviewElementDebugProperties.GetDebugBaseText(element);
        if (string.IsNullOrWhiteSpace(baseDebugText))
        {
            return;
        }

        var layoutDebugText = BuildLayoutDebugText(element, attributes, parentSize, measuredSize, position, alignmentBounds);
        PreviewElementDebugProperties.SetDebugText(
            element,
            string.Concat(baseDebugText, Environment.NewLine, Environment.NewLine, layoutDebugText));
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
        textBlock.ClearValue(TextBlock.LineStackingStrategyProperty);
        textBlock.ClearValue(TextBlock.LineHeightProperty);
        textBlock.RenderTransform = Transform.Identity;
        return 0;
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
        var policy = ResolveSizePolicy(attributes, policyKey);
        var suggestedSize = ResolveDouble(attributes, sizeKey, 0);
        var startMarginKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MarginLeft" : "MarginTop";
        var endMarginKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MarginRight" : "MarginBottom";
        var marginStart = ResolveDouble(attributes, startMarginKey, 0);
        var marginEnd = ResolveDouble(attributes, endMarginKey, 0);

        double? dimension = policy switch
        {
            "Fixed" when suggestedSize > 0 => suggestedSize,
            "StretchToParent" => Math.Max(0, parentSize - marginStart - marginEnd),
            "CoverChildren" => (double?)null,
            _ when suggestedSize > 0 => suggestedSize,
            _ => (double?)null
        };

        if (!dimension.HasValue)
        {
            return null;
        }

        var minKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MinWidth" : "MinHeight";
        var maxKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MaxWidth" : "MaxHeight";
        return CoerceAxisToBoundaries(attributes, minKey, maxKey, dimension.Value);
    }

    private string? ResolveSizePolicy(IReadOnlyDictionary<string, string> attributes, string policyKey)
    {
        attributes.TryGetValue(policyKey, out var policy);
        if (TryGetResolvedWidgetProperty(attributes, policyKey, out var resolvedPolicy) &&
            resolvedPolicy.ParsedValue is not null)
        {
            policy = resolvedPolicy.ParsedValue.ToString() ?? policy;
        }

        return policy;
    }

    private void ApplyMeasurementBounds(FrameworkElement element, IReadOnlyDictionary<string, string> attributes)
    {
        if (ResolveOptionalDouble(attributes, "MinWidth") is { } minWidth)
        {
            element.MinWidth = Math.Max(element.MinWidth, minWidth);
        }

        if (ResolveOptionalDouble(attributes, "MinHeight") is { } minHeight)
        {
            element.MinHeight = Math.Max(element.MinHeight, minHeight);
        }

        if (ResolveOptionalDouble(attributes, "MaxWidth") is { } maxWidth && maxWidth > 0)
        {
            element.MaxWidth = Math.Min(element.MaxWidth, maxWidth);
        }

        if (ResolveOptionalDouble(attributes, "MaxHeight") is { } maxHeight && maxHeight > 0)
        {
            element.MaxHeight = Math.Min(element.MaxHeight, maxHeight);
        }
    }

    private double ResolveMeasureInputAxis(
        FrameworkElement? element,
        IReadOnlyDictionary<string, string> attributes,
        string policyKey,
        string suggestedKey,
        string startMarginKey,
        string endMarginKey,
        string minKey,
        string maxKey,
        double parentSize,
        double explicitSize)
    {
        var policy = ResolveSizePolicy(attributes, policyKey);
        var suggestedSize = ResolveDouble(attributes, suggestedKey, 0);
        var marginStart = ResolveDouble(attributes, startMarginKey, 0);
        var marginEnd = ResolveDouble(attributes, endMarginKey, 0);
        var elementSize = !double.IsNaN(explicitSize) && explicitSize > 0
            ? explicitSize
            : element is null
                ? double.NaN
                : policyKey.StartsWith("Width", StringComparison.Ordinal)
                    ? element.ActualWidth
                    : element.ActualHeight;

        var measureSize = policy switch
        {
            "Fixed" when suggestedSize > 0 => suggestedSize,
            "Fixed" when !double.IsNaN(elementSize) && elementSize > 0 => elementSize,
            "StretchToParent" => Math.Max(0, parentSize - marginStart - marginEnd),
            _ when suggestedSize > 0 => suggestedSize,
            _ when !double.IsNaN(elementSize) && elementSize > 0 => elementSize,
            _ => Math.Max(0, parentSize)
        };

        return CoerceAxisToBoundaries(attributes, minKey, maxKey, measureSize);
    }

    private double ResolveMeasuredAxis(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        string policyKey,
        string suggestedKey,
        double measureInput,
        double desired)
    {
        var policy = ResolveSizePolicy(attributes, policyKey);
        var suggestedSize = ResolveDouble(attributes, suggestedKey, 0);
        var explicitSize = policyKey.StartsWith("Width", StringComparison.Ordinal) ? element.Width : element.Height;
        var minKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MinWidth" : "MinHeight";
        var maxKey = policyKey.StartsWith("Width", StringComparison.Ordinal) ? "MaxWidth" : "MaxHeight";

        var measured = policy switch
        {
            "Fixed" when suggestedSize > 0 => suggestedSize,
            "Fixed" when !double.IsNaN(explicitSize) && explicitSize > 0 => explicitSize,
            "CoverChildren" => desired,
            "StretchToParent" => measureInput,
            _ when !double.IsNaN(explicitSize) && explicitSize > 0 => explicitSize,
            _ when suggestedSize > 0 => suggestedSize,
            _ => desired
        };

        var fallback = policy switch
        {
            "Fixed" => measureInput,
            "StretchToParent" => measureInput,
            _ when !double.IsNaN(explicitSize) && explicitSize > 0 => explicitSize,
            _ when suggestedSize > 0 => suggestedSize,
            _ => 0
        };

        return CoerceAxisToBoundaries(attributes, minKey, maxKey, ResolveMeasuredLength(measured, fallback));
    }

    private double ResolveMeasuredLength(double preferred, double fallback)
    {
        if (!double.IsNaN(preferred) && !double.IsInfinity(preferred) && preferred > 0)
        {
            return preferred;
        }

        if (!double.IsNaN(fallback) && !double.IsInfinity(fallback) && fallback > 0)
        {
            return fallback;
        }

        return 0;
    }

    private double CoerceAxisToBoundaries(
        IReadOnlyDictionary<string, string> attributes,
        string minKey,
        string maxKey,
        double value)
    {
        var result = Math.Max(0, value);
        if (ResolveOptionalDouble(attributes, minKey) is { } minValue)
        {
            result = Math.Max(result, minValue);
        }

        if (ResolveOptionalDouble(attributes, maxKey) is { } maxValue && maxValue > 0)
        {
            result = Math.Min(result, maxValue);
        }

        return result;
    }

    private double? ResolveOptionalDouble(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName)
    {
        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property) &&
            property.TryGetDouble(out var typedValue))
        {
            return typedValue;
        }

        if (!attributes.TryGetValue(attributeName, out var rawValue))
        {
            return null;
        }

        var resolvedValue = ResolveDouble(rawValue, double.NaN);
        return double.IsNaN(resolvedValue) ? null : resolvedValue;
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
                if (IsInternalDebugAttribute(attribute.Key))
                {
                    continue;
                }

                var resolvedValue = ResolveDebugValue(attributes, attribute.Key, attribute.Value);
                lines.Add(string.Equals(resolvedValue, attribute.Value, StringComparison.Ordinal)
                    ? $"{attribute.Key}: {attribute.Value}"
                    : $"{attribute.Key}: {attribute.Value} -> {resolvedValue}");
            }
        }

        var widgetPath = TryGetWidgetPath(attributes);
        if (!string.IsNullOrWhiteSpace(widgetPath))
        {
            var resolvedProperties = _propertyMap.GetProperties(widgetPath);
            if (resolvedProperties.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("ResolvedProperties");
                foreach (var property in resolvedProperties.Values.OrderBy(property => property.AttributeName, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add(
                        $"{property.AttributeName}: {property.DeclaredTypeName} [{property.ValueKind}] = {property.GetDisplayValue()}");
                }
            }

            var resolvedReferences = _referenceMap.GetReferences(widgetPath);
            if (resolvedReferences.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("WidgetReferences");
                foreach (var reference in resolvedReferences.Values.OrderBy(reference => reference.AttributeName, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add(reference.IsResolved
                        ? $"{reference.AttributeName}: {reference.RawValue} -> {reference.TargetWidgetPath}"
                        : $"{reference.AttributeName}: {reference.RawValue} (unresolved: {reference.FailureReason})");
                }
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

    private string BuildLayoutDebugText(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        Size measuredSize,
        Point? position,
        Rect? alignmentBounds)
    {
        var lines = new List<string>
        {
            "Layout",
            $"ParentSize: {FormatSize(parentSize)}",
            $"MeasuredSize: {FormatSize(measuredSize)}"
        };

        attributes.TryGetValue("WidthSizePolicy", out var widthPolicy);
        attributes.TryGetValue("HeightSizePolicy", out var heightPolicy);
        if (!string.IsNullOrWhiteSpace(widthPolicy) || !string.IsNullOrWhiteSpace(heightPolicy))
        {
            lines.Add(
                $"SizePolicy: W={ResolveDebugValue(attributes, "WidthSizePolicy", widthPolicy ?? "-")} " +
                $"H={ResolveDebugValue(attributes, "HeightSizePolicy", heightPolicy ?? "-")}");
        }

        attributes.TryGetValue("SuggestedWidth", out var suggestedWidth);
        attributes.TryGetValue("SuggestedHeight", out var suggestedHeight);
        if (!string.IsNullOrWhiteSpace(suggestedWidth) || !string.IsNullOrWhiteSpace(suggestedHeight))
        {
            lines.Add(
                $"SuggestedSize: W={ResolveDebugValue(attributes, "SuggestedWidth", suggestedWidth ?? "-")} " +
                $"H={ResolveDebugValue(attributes, "SuggestedHeight", suggestedHeight ?? "-")}");
        }

        attributes.TryGetValue("HorizontalAlignment", out var horizontalAlignment);
        attributes.TryGetValue("VerticalAlignment", out var verticalAlignment);
        if (!string.IsNullOrWhiteSpace(horizontalAlignment) || !string.IsNullOrWhiteSpace(verticalAlignment))
        {
            lines.Add(
                $"Alignment: H={ResolveDebugValue(attributes, "HorizontalAlignment", horizontalAlignment ?? "Left")} " +
                $"V={ResolveDebugValue(attributes, "VerticalAlignment", verticalAlignment ?? "Top")}");
        }

        lines.Add($"Margin: {FormatThickness(element.Margin, attributes)}");

        attributes.TryGetValue("PositionXOffset", out var positionXOffset);
        attributes.TryGetValue("PositionYOffset", out var positionYOffset);
        if (!string.IsNullOrWhiteSpace(positionXOffset) || !string.IsNullOrWhiteSpace(positionYOffset))
        {
            lines.Add(
                $"Offsets: X={ResolveDebugValue(attributes, "PositionXOffset", positionXOffset ?? "0")} " +
                $"Y={ResolveDebugValue(attributes, "PositionYOffset", positionYOffset ?? "0")}");
        }

        lines.Add(
            $"WpfSize: W={FormatOptionalDimension(double.IsNaN(element.Width) ? null : element.Width)} H={FormatOptionalDimension(double.IsNaN(element.Height) ? null : element.Height)}");

        if (position.HasValue)
        {
            lines.Add($"CanvasPosition: {FormatPoint(position.Value)}");
        }

        if (alignmentBounds is { IsEmpty: false } bounds)
        {
            lines.Add($"AlignmentBounds: {FormatRect(bounds)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string ResolveDebugValue(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        string rawValue)
    {
        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property))
        {
            return property.GetDisplayValue();
        }

        if (TryGetResolvedWidgetReference(attributes, attributeName, out var reference))
        {
            return reference.IsResolved && !string.IsNullOrWhiteSpace(reference.TargetWidgetPath)
                ? reference.TargetWidgetPath
                : rawValue;
        }

        return ResolveDebugValue(rawValue);
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

    private static string FormatSize(Size size)
    {
        return $"{FormatDouble(size.Width)} x {FormatDouble(size.Height)}";
    }

    private static string FormatPoint(Point point)
    {
        return $"({FormatDouble(point.X)}, {FormatDouble(point.Y)})";
    }

    private static string FormatRect(Rect rect)
    {
        return $"{FormatPoint(rect.Location)} {FormatSize(rect.Size)}";
    }

    private string FormatThickness(Thickness? elementMargin, IReadOnlyDictionary<string, string> attributes)
    {
        if (elementMargin.HasValue)
        {
            var margin = elementMargin.Value;
            return $"L={FormatDouble(margin.Left)} T={FormatDouble(margin.Top)} R={FormatDouble(margin.Right)} B={FormatDouble(margin.Bottom)}";
        }

        var marginLeft = ResolveDebugValue(attributes, "MarginLeft", attributes.TryGetValue("MarginLeft", out var marginLeftValue) ? marginLeftValue : "0");
        var marginTop = ResolveDebugValue(attributes, "MarginTop", attributes.TryGetValue("MarginTop", out var marginTopValue) ? marginTopValue : "0");
        var marginRight = ResolveDebugValue(attributes, "MarginRight", attributes.TryGetValue("MarginRight", out var marginRightValue) ? marginRightValue : "0");
        var marginBottom = ResolveDebugValue(attributes, "MarginBottom", attributes.TryGetValue("MarginBottom", out var marginBottomValue) ? marginBottomValue : "0");
        return $"L={marginLeft} T={marginTop} R={marginRight} B={marginBottom}";
    }

    private static string FormatOptionalDimension(double? value)
    {
        return value.HasValue ? FormatDouble(value.Value) : "Auto";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
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
        var marginLeft = ResolveDouble(attributes, "MarginLeft", 0);
        var marginTop = ResolveDouble(attributes, "MarginTop", 0);
        var marginRight = ResolveDouble(attributes, "MarginRight", 0);
        var marginBottom = ResolveDouble(attributes, "MarginBottom", 0);
        element.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);

        if (HasAttribute(attributes, "HorizontalAlignment"))
        {
            element.HorizontalAlignment = ResolveWidgetHorizontalAlignment(attributes);
        }
        else if (attributes.TryGetValue("WidthSizePolicy", out var widthPolicy) &&
                 string.Equals(widthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (HasAttribute(attributes, "VerticalAlignment"))
        {
            element.VerticalAlignment = ResolveWidgetVerticalAlignment(attributes);
        }
        else if (attributes.TryGetValue("HeightSizePolicy", out var heightPolicy) &&
                 string.Equals(heightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.VerticalAlignment = VerticalAlignment.Stretch;
        }

        GauntletLayoutProperties.SetWidthSizePolicy(element, ResolveSizePolicy(attributes, "WidthSizePolicy") ?? string.Empty);
        GauntletLayoutProperties.SetHeightSizePolicy(element, ResolveSizePolicy(attributes, "HeightSizePolicy") ?? string.Empty);
        var sharedStretchRatio = ResolveOptionalDouble(attributes, "StretchRatio");
        var widthStretchRatio = ResolveOptionalDouble(attributes, "WidthStretchRatio") ?? sharedStretchRatio ?? 1d;
        var heightStretchRatio = ResolveOptionalDouble(attributes, "HeightStretchRatio") ?? sharedStretchRatio ?? 1d;
        GauntletLayoutProperties.SetWidthStretchRatio(element, widthStretchRatio > 0 ? widthStretchRatio : 1d);
        GauntletLayoutProperties.SetHeightStretchRatio(element, heightStretchRatio > 0 ? heightStretchRatio : 1d);
    }

    public void PrepareGauntletStackLayoutChild(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        GauntletStackLayoutMethod layoutMethod)
    {
        var isHorizontal = layoutMethod is GauntletStackLayoutMethod.HorizontalLeftToRight
            or GauntletStackLayoutMethod.HorizontalRightToLeft
            or GauntletStackLayoutMethod.HorizontalCentered
            or GauntletStackLayoutMethod.HorizontalSpaced;
        var widthPolicy = ResolveSizePolicy(attributes, "WidthSizePolicy");
        var heightPolicy = ResolveSizePolicy(attributes, "HeightSizePolicy");

        if (isHorizontal && string.Equals(widthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Width = double.NaN;
        }
        else if (!isHorizontal && string.Equals(heightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Height = double.NaN;
        }

        if (!isHorizontal && string.Equals(widthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Width = double.NaN;
        }
        else if (isHorizontal && string.Equals(heightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Height = double.NaN;
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

    private sealed record RenderedWidgetRegistration(
        string WidgetPath,
        string NodeName,
        IReadOnlyDictionary<string, string> Attributes,
        Size ParentSize,
        FrameworkElement Element,
        Panel? ChildHost);
}
