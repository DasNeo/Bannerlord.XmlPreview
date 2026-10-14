using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
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
using System.Runtime.CompilerServices;

namespace Bannerlord.XmlPreview.Rendering;

public sealed partial class PrefabRenderContext
{
    private const byte SignificantAlphaThreshold = 64;
    private const byte ContentCoreAlphaThreshold = 192;
    private const int MaxRenderedNodeCount = 10000;
    private const string WidgetPathSeparator = " > ";
    private static readonly ConditionalWeakTable<BitmapSource, SpritePixelBounds> SpritePixelBoundsCache = new();
    private readonly Dictionary<string, SpriteDefinition?> _spriteDefinitionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FrameworkElement, Rect?> _alignmentBoundsByElement = [];
    private readonly Dictionary<string, BrushDefinition?> _brushDefinitionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BrushDebugInfo> _brushDebugInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<BrushLayerDefinition>> _visibleBrushLayersCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Brush> _renderBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteAssetResolution> _spriteAssetResolutionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderedWidgetRegistration> _renderedWidgetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IReadOnlyDictionary<string, string>> _bindingScopes = [];

    private readonly Lazy<Dictionary<string, Brush>> _panelBrushes;
    private Dictionary<string, Brush> PanelBrushes => _panelBrushes.Value;
    private readonly Lazy<Dictionary<string, Brush>> _textBrushes;
    private Dictionary<string, Brush> TextBrushes => _textBrushes.Value;
    private readonly PreviewDataSnapshot _previewData;
    public string FrameBorderColor => _previewData.FrameBorderColor;

    private readonly PreviewDiagnostics _diagnostics;
    private readonly IReadOnlyDictionary<string, string> _constants;
    private readonly PrefabWidgetPropertyMap _propertyMap;
    private readonly PrefabWidgetReferenceMap _referenceMap;
    private readonly IAssetCatalog _assetCatalog;
    internal IAssetCatalog Assets => _assetCatalog;
    private readonly IBrushMetricsProvider _brushMetricsProvider;
    private readonly IBrushOverlayFeatureHandler _brushOverlayFeatureHandler;
    private readonly IWidgetTextSourceProvider _widgetTextSourceProvider;
    private readonly IWidgetDebugMetadataProvider _widgetDebugMetadataProvider;
    private readonly HashSet<string> _reportedBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedNodes = new(StringComparer.OrdinalIgnoreCase);
    private int _renderedNodeCount;
    private bool _renderBudgetExceeded;
    private readonly Dictionary<string, string> _runBindings;
    private readonly RegressionRenderOptions? _regressionOptions;
    private readonly Dictionary<WidgetId, RenderedWidgetRegistration> _graphElements = [];
    internal string GraphPrefabIdentity { get; set; } = "preview.xml";
    private readonly List<string> _instanceKeys = [];
    internal List<SnapshotElement> SnapshotElements { get; } = [];

    public PrefabRenderContext(
        IReadOnlyDictionary<string, string> constants,
        PrefabWidgetPropertyMap propertyMap,
        PrefabWidgetReferenceMap referenceMap,
        PreviewDiagnostics diagnostics,
        IAssetCatalog assetCatalog,
        IBrushMetricsProvider brushMetricsProvider,
        IBrushOverlayFeatureHandler brushOverlayFeatureHandler,
        IWidgetTextSourceProvider widgetTextSourceProvider,
        IWidgetDebugMetadataProvider widgetDebugMetadataProvider,
        RegressionRenderOptions? regressionOptions = null,
        PreviewDataSnapshot? previewData = null)
    {
        _previewData = previewData ?? PreviewDataSnapshot.Empty;
        _panelBrushes = new(() => _previewData.PanelColors.ToDictionary(p => p.Key, p => CreateGradientBrush(p.Value.Fill, p.Value.Stroke), StringComparer.OrdinalIgnoreCase));
        _textBrushes = new(() => _previewData.TextColors.ToDictionary(p => p.Key, p => (Brush)CreateSolidBrushInternal(p.Value), StringComparer.OrdinalIgnoreCase));
        _constants = constants;
        _propertyMap = propertyMap;
        _referenceMap = referenceMap;
        _diagnostics = diagnostics;
        _assetCatalog = assetCatalog;
        _brushMetricsProvider = brushMetricsProvider;
        _brushOverlayFeatureHandler = brushOverlayFeatureHandler;
        _widgetTextSourceProvider = widgetTextSourceProvider;
        _widgetDebugMetadataProvider = widgetDebugMetadataProvider;
        _regressionOptions = regressionOptions;
        _runBindings = regressionOptions is null
            ? new Dictionary<string, string>(_previewData.Bindings, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in regressionOptions?.Bindings ?? new Dictionary<string, string>())
            _runBindings[binding.Key] = binding.Value;
    }

    public void EnsureBindings(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (_runBindings.ContainsKey(key))
            {
                continue;
            }

            _runBindings[key] = HumanizeBindingKey(key);
            if (_reportedBindings.Add(key))
            {
                _diagnostics.Add($"Binding '@{key}' had no mock value. Preview generated '{_runBindings[key]}'.");
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

        return _runBindings.GetValueOrDefault(key, HumanizeBindingKey(key));
    }

    public bool TryGetTemplateItemBindings(
        PrefabNode node,
        out IReadOnlyList<IReadOnlyDictionary<string, string>> itemBindings)
    {
        if (_previewData.TemplateItems.TryGetValue(PreviewDataSnapshot.Path(node), out var items))
        {
            itemBindings = items.Select(item => (IReadOnlyDictionary<string, string>)item).ToArray();
            return true;
        }
        itemBindings = [];
        return false;
    }

    public string ResolveWidgetText(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        string fallbackText)
    {
        var textSource = _widgetTextSourceProvider.GetTextSource(nodeName, attributes);
        var resolvedText = ResolveBinding(textSource);
        return string.IsNullOrEmpty(resolvedText) ? fallbackText : resolvedText;
    }

    public IDisposable PushBindingScope(IReadOnlyDictionary<string, string> values, string? instanceKey = null)
    {
        _bindingScopes.Add(values);
        _instanceKeys.Add(instanceKey ?? values.GetValueOrDefault("__key", "0"));
        return new SnapshotScopeHandle(new BindingScopeHandle(_bindingScopes, values), _instanceKeys);
    }

    internal IReadOnlyDictionary<string, string> SnapshotAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        var path = TryGetWidgetPath(attributes);
        if (path is null || _regressionOptions is null || !_regressionOptions.WidgetStates.TryGetValue(path, out var states))
            return attributes;
        var copy = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
        foreach (var state in states) copy[state.Key] = state.Value;
        return copy;
    }

    internal string ResolveSnapshotValue(IReadOnlyDictionary<string, string> attributes, string key, string raw)
    {
        var path = TryGetWidgetPath(attributes);
        if (path is not null && _regressionOptions?.WidgetStates.GetValueOrDefault(path)?.ContainsKey(key) == true) return raw.StartsWith('@') ? ResolveBinding(raw) : raw;
        if (raw.StartsWith('@')) return ResolveBinding(raw);
        if (raw.StartsWith('!') && _constants.TryGetValue(raw[1..], out var constant)) return constant;
        if (TryGetResolvedWidgetProperty(attributes, key, out var property)) return property.GetDisplayValue();
        return raw;
    }

    private sealed class SnapshotScopeHandle(IDisposable binding, List<string> keys) : IDisposable
    {
        public void Dispose() { binding.Dispose(); keys.RemoveAt(keys.Count - 1); }
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
            _propertyMap.TryGetProperty(widgetPath, attributeName, out property) &&
            attributes.GetValueOrDefault(attributeName) == property.RawValue)
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
        if (_activeGraphWidget is { } active && _graphElements.TryGetValue(active, out var owner))
        {
            var referenceOwner = active;
            if (TryGetWidgetPath(attributes) is { } path && path != owner.WidgetPath)
            {
                var instance = _graphInstanceKeys.GetValueOrDefault(active, "");
                var candidates = _graphElements.Where(p => p.Value.WidgetPath == path && (_graphInstanceKeys.GetValueOrDefault(p.Key, "").Length == 0 || instance == _graphInstanceKeys.GetValueOrDefault(p.Key, "") || instance.StartsWith(_graphInstanceKeys.GetValueOrDefault(p.Key, "") + "/", StringComparison.Ordinal))).OrderByDescending(p => _graphInstanceKeys.GetValueOrDefault(p.Key, "").Length);
                referenceOwner = candidates.Select(p => p.Key).FirstOrDefault();
            }
            if (_graphReferences.TryGetValue(referenceOwner, out var references) && references.TryGetValue(attributeName, out var target))
                return _graphElements.TryGetValue(target, out widget!);
            widget = null!;
            return false;
        }

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
        var separatorIndex = widgetPath.LastIndexOf(WidgetPathSeparator, StringComparison.Ordinal);
        return separatorIndex > 0 ? widgetPath[..separatorIndex] : null;
    }

    private static bool IsInternalDebugAttribute(string attributeName)
    {
        return attributeName.StartsWith("Debug.", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveStringAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        string fallback = "")
    {
        if (TryGetResolvedWidgetProperty(attributes, attributeName, out var property) &&
            property.ParsedValue is not null)
        {
            return property.ParsedValue.ToString() ?? fallback;
        }

        return attributes.TryGetValue(attributeName, out var rawValue)
            ? ResolveDebugValue(rawValue)
            : fallback;
    }

    private double? ResolveOptionalWidgetNumber(
        IReadOnlyDictionary<string, string> attributes,
        string primaryAttributeName,
        string fallbackAttributeName)
    {
        return ResolveOptionalPreviewDouble(attributes, primaryAttributeName, unresolvedBindingFallback: 0d) ??
               ResolveOptionalPreviewDouble(attributes, fallbackAttributeName, unresolvedBindingFallback: 0d);
    }

    private void MaterializeToggleSelection(RenderedWidgetRegistration widget)
    {
        var includeChildren = ResolveBoolean(widget.Attributes, "UpdateChildrenStates");
        var tabName = ResolveStringAttribute(widget.Attributes, "TabName").Trim();
        var hasTabControl = TryResolveReferencedWidget(widget.Attributes, "TabControlWidget", out var tabControlWidget);
        var isDisabled = !hasTabControl || string.IsNullOrWhiteSpace(tabName);
        var isSelected = !isDisabled &&
                         widget.Element.Visibility == Visibility.Visible &&
                         IsRenderedTabSelected(tabControlWidget!, tabName);

        widget.Element.IsEnabled = !isDisabled;
        ApplyPreviewState(
            widget,
            isDisabled ? "Disabled" : isSelected ? "Selected" : GetPreviewBaseState(widget),
            includeChildren);
    }

    private bool IsRenderedTabSelected(RenderedWidgetRegistration tabControlWidget, string tabName)
    {
        var prefix = tabControlWidget.WidgetPath + WidgetPathSeparator;
        foreach (var candidate in _renderedWidgetsByPath.Values)
        {
            if (!candidate.WidgetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directChildSegment = TryGetDirectChildSegment(candidate.WidgetPath, tabControlWidget.WidgetPath);
            if (!string.IsNullOrWhiteSpace(directChildSegment) &&
                directChildSegment.Contains($"[Id={tabName}]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetDirectChildSegment(string widgetPath, string ancestorWidgetPath)
    {
        if (!widgetPath.StartsWith(ancestorWidgetPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (widgetPath.Length == ancestorWidgetPath.Length)
        {
            return null;
        }

        var childPathStart = ancestorWidgetPath.Length + WidgetPathSeparator.Length;
        if (widgetPath.Length <= childPathStart ||
            !widgetPath.StartsWith(ancestorWidgetPath + WidgetPathSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var childPath = widgetPath[childPathStart..];
        var nextSeparatorIndex = childPath.IndexOf(WidgetPathSeparator, StringComparison.Ordinal);
        return nextSeparatorIndex >= 0 ? childPath[..nextSeparatorIndex] : childPath;
    }

    private void MaterializeDropdown(RenderedWidgetRegistration widget)
    {
        var widgetParentSize = ResolveRegisteredParentSize(widget);
        var widgetSize = Measure(widget.Element, widget.Attributes, widgetParentSize);

        var directItemWidgets = TryResolveReferencedWidget(widget.Attributes, "ListPanel", out var listPanelWidget)
            ? EnumerateDirectChildWidgets(listPanelWidget).ToArray()
            : [];
        var selectedIndex = directItemWidgets.Length == 0
            ? -1
            : Math.Clamp(
                (int)Math.Round(ResolveDouble(widget.Attributes, "CurrentSelectedIndex", 0d)),
                0,
                directItemWidgets.Length - 1);
        var isOpen = ResolveBoolean(widget.Attributes, "IsOpen") ||
                     ResolveWidgetPreviewState(widget).Contains("open", StringComparison.OrdinalIgnoreCase);

        if (TryResolveReferencedWidget(widget.Attributes, "TextWidget", out var textWidget) &&
            selectedIndex >= 0 &&
            selectedIndex < directItemWidgets.Length &&
            TryExtractTextFromElement(directItemWidgets[selectedIndex].Element, out var selectedText))
        {
            SetTextOnWidget(textWidget.Element, selectedText);
        }

        for (var index = 0; index < directItemWidgets.Length; index++)
        {
            var itemWidget = directItemWidgets[index];
            var includeChildren = ResolveBoolean(itemWidget.Attributes, "UpdateChildrenStates");
            CapturePreviewBaseState(itemWidget);
            ApplyPreviewState(
                itemWidget,
                index == selectedIndex ? "Selected" : GetPreviewBaseState(itemWidget),
                includeChildren);
        }

        if (TryResolveReferencedWidget(widget.Attributes, "Button", out var buttonWidget))
        {
            CapturePreviewBaseState(buttonWidget);
            ApplyPreviewState(
                buttonWidget,
                isOpen ? "Open" : GetPreviewBaseState(buttonWidget),
                includeChildren: ResolveBoolean(buttonWidget.Attributes, "UpdateChildrenStates"));
        }

        var buttonContainerWidget = EnumerateDirectChildWidgets(widget)
            .FirstOrDefault(candidate =>
                candidate.Attributes.TryGetValue("Id", out var id) &&
                string.Equals(id, "DropdownButtonContainer", StringComparison.OrdinalIgnoreCase));
        var initialButtonContainerSize = buttonContainerWidget is null
            ? new Size(0, 0)
            : Measure(buttonContainerWidget.Element, buttonContainerWidget.Attributes, widgetSize);

        Size dropdownContainerSize = new(0, 0);
        if (TryResolveReferencedWidget(widget.Attributes, "DropdownContainerWidget", out var dropdownContainerWidget))
        {
            dropdownContainerWidget.Element.Visibility = directItemWidgets.Length > 0 && isOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
            dropdownContainerSize = Measure(dropdownContainerWidget.Element, dropdownContainerWidget.Attributes, widgetSize);
        }

        var dropdownHeight = isOpen ? Math.Max(0d, dropdownContainerSize.Height) : 0d;
        var resolvedRootSize = new Size(
            Math.Max(0d, widgetSize.Width),
            initialButtonContainerSize.Height > 0
                ? initialButtonContainerSize.Height + dropdownHeight
                : Math.Max(0d, widgetSize.Height));
        if (resolvedRootSize.Width > 0)
        {
            widget.Element.Width = resolvedRootSize.Width;
        }

        if (resolvedRootSize.Height > 0)
        {
            widget.Element.Height = resolvedRootSize.Height;
        }

        PreviewElementDebugProperties.SetSelectionBounds(
            widget.Element,
            new Rect(new Point(0, 0), resolvedRootSize));
        AttachElementLayoutDebugMetadata(widget.Element, widget.Attributes, widgetParentSize, resolvedRootSize);

        var buttonContainerSize = buttonContainerWidget is null
            ? new Size(0, 0)
            : Measure(buttonContainerWidget.Element, buttonContainerWidget.Attributes, resolvedRootSize);
        if (buttonContainerWidget is not null)
        {
            Canvas.SetLeft(buttonContainerWidget.Element, 0d);
            Canvas.SetTop(buttonContainerWidget.Element, 0d);
            PreviewElementDebugProperties.SetSelectionBounds(
                buttonContainerWidget.Element,
                new Rect(new Point(0, 0), buttonContainerSize));
            AttachElementLayoutDebugMetadata(
                buttonContainerWidget.Element,
                buttonContainerWidget.Attributes,
                resolvedRootSize,
                buttonContainerSize,
                new Point(0, 0));
        }

        if (TryResolveReferencedWidget(widget.Attributes, "DropdownClipWidget", out var dropdownClipWidget))
        {
            var clipWidth = Math.Max(buttonContainerSize.Width, dropdownContainerSize.Width);
            var clipHeight = isOpen
                ? Math.Max(0d, dropdownContainerSize.Height)
                : 0d;
            var clipPosition = new Point(
                Math.Max(0d, (buttonContainerSize.Width - clipWidth) / 2d),
                Math.Max(0d, buttonContainerSize.Height));

            dropdownClipWidget.Element.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            dropdownClipWidget.Element.IsEnabled = isOpen;
            if (clipWidth > 0)
            {
                dropdownClipWidget.Element.Width = clipWidth;
            }

            Canvas.SetLeft(dropdownClipWidget.Element, clipPosition.X);
            Canvas.SetTop(dropdownClipWidget.Element, clipPosition.Y);
            dropdownClipWidget.Element.Height = clipHeight;
            PreviewElementDebugProperties.SetSelectionBounds(
                dropdownClipWidget.Element,
                new Rect(new Point(0, 0), new Size(clipWidth, clipHeight)));
            AttachElementLayoutDebugMetadata(
                dropdownClipWidget.Element,
                dropdownClipWidget.Attributes,
                resolvedRootSize,
                new Size(clipWidth, clipHeight),
                clipPosition);

            if (TryResolveReferencedWidget(widget.Attributes, "DropdownContainerWidget", out var referencedDropdownContainerWidget))
            {
                Canvas.SetLeft(referencedDropdownContainerWidget.Element, 0d);
                Canvas.SetTop(referencedDropdownContainerWidget.Element, 0d);
                PreviewElementDebugProperties.SetSelectionBounds(
                    referencedDropdownContainerWidget.Element,
                    new Rect(new Point(0, 0), dropdownContainerSize));
                AttachElementLayoutDebugMetadata(
                    referencedDropdownContainerWidget.Element,
                    referencedDropdownContainerWidget.Attributes,
                    new Size(clipWidth, clipHeight),
                    dropdownContainerSize,
                    new Point(0, 0));
            }
        }

        if (TryResolveReferencedWidget(widget.Attributes, "ScrollbarWidget", out var scrollbarWidget) &&
            !isOpen)
        {
            scrollbarWidget.Element.Visibility = Visibility.Collapsed;
        }
    }

    private void MaterializeRadialChildren(RenderedWidgetRegistration widget)
    {
        if (widget.ChildHost is not Canvas childCanvas)
        {
            return;
        }

        var directChildWidgets = EnumerateDirectChildWidgets(widget).ToArray();
        if (directChildWidgets.Length == 0)
        {
            return;
        }

        var parentSize = ResolveRegisteredParentSize(widget);
        var hostSize = Measure(widget.Element, widget.Attributes, parentSize);
        if (hostSize.Width <= 0 || hostSize.Height <= 0)
        {
            hostSize = new Size(
                ResolveDouble(widget.Attributes, "SuggestedWidth", 0d),
                ResolveDouble(widget.Attributes, "SuggestedHeight", 0d));
        }

        var radius = ResolveOptionalDouble(widget.Attributes, "CircleRadius") ??
                     Math.Max(0d, Math.Min(hostSize.Width, hostSize.Height) / 2d - 8d);
        if (radius <= 0)
        {
            return;
        }

        var centerX = hostSize.Width / 2d;
        var centerY = hostSize.Height / 2d;
        var angleStep = Math.PI * 2d / directChildWidgets.Length;

        for (var index = 0; index < directChildWidgets.Length; index++)
        {
            var childWidget = directChildWidgets[index];
            var childSize = Measure(childWidget.Element, childWidget.Attributes, hostSize);
            var angle = -Math.PI / 2d + angleStep * index;
            var childCenterX = centerX + Math.Cos(angle) * radius;
            var childCenterY = centerY + Math.Sin(angle) * radius;

            Canvas.SetLeft(childWidget.Element, childCenterX - childSize.Width / 2d);
            Canvas.SetTop(childWidget.Element, childCenterY - childSize.Height / 2d);
            childWidget.Element.Opacity = Math.Clamp(0.35d + ((double)(index + 1) / directChildWidgets.Length) * 0.65d, 0d, 1d);
        }
    }

    private IEnumerable<RenderedWidgetRegistration> EnumerateDirectChildWidgets(RenderedWidgetRegistration widget)
    {
        if (widget.ChildHost is null)
        {
            yield break;
        }

        foreach (var childElement in widget.ChildHost.Children.OfType<FrameworkElement>())
        {
            if (TryGetRenderedWidgetByElement(childElement, out var childWidget))
            {
                yield return childWidget;
            }
        }
    }

    private bool TryGetRenderedWidgetByElement(FrameworkElement element, out RenderedWidgetRegistration widget)
    {
        foreach (var candidate in _renderedWidgetsByPath.Values)
        {
            if (ReferenceEquals(candidate.Element, element))
            {
                widget = candidate;
                return true;
            }
        }

        widget = null!;
        return false;
    }

    private static bool TryExtractTextFromElement(DependencyObject element, out string text)
    {
        if (element is TextBlock textBlock)
        {
            text = textBlock.Text;
            return !string.IsNullOrWhiteSpace(text);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            if (TryExtractTextFromElement(VisualTreeHelper.GetChild(element, index), out text))
            {
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    private static void SetTextOnWidget(DependencyObject element, string text)
    {
        if (element is TextBlock textBlock)
        {
            textBlock.Text = string.IsNullOrWhiteSpace(text) ? " " : text;
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            SetTextOnWidget(VisualTreeHelper.GetChild(element, index), text);
        }
    }

    private void MaterializeFill(RenderedWidgetRegistration widget, bool isVertical)
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

        var currentAmount = ResolveDouble(widget.Attributes, "CurrentAmountAsFloat", double.NaN);
        if (double.IsNaN(currentAmount))
        {
            currentAmount = ResolveDouble(widget.Attributes, "CurrentAmount", double.NaN);
        }

        if (double.IsNaN(maxAmount) || maxAmount <= 0 || double.IsNaN(initialAmount))
        {
            return;
        }

        var fillFromEnd = isVertical
            ? ResolveBoolean(widget.Attributes, "IsDirectionUpward")
            : ResolveBoolean(widget.Attributes, "IsDirectionRightward", fillWidget.Element.HorizontalAlignment != HorizontalAlignment.Right);
        var hostSize = ResolveFillBarHostSize(widget, fillWidget, isVertical);
        var hostLength = isVertical ? hostSize.Height : hostSize.Width;
        if (hostLength <= 0)
        {
            return;
        }

        var fillLength = CalculateFillBarLength(hostLength, fillWidget.Element.Margin, initialAmount, maxAmount, isVertical);
        var changeDelta = double.IsNaN(currentAmount)
            ? 0d
            : Math.Clamp(currentAmount - initialAmount, -maxAmount, maxAmount);
        var changeLength = CalculateFillBarLength(hostLength, fillWidget.Element.Margin, Math.Abs(changeDelta), maxAmount, isVertical);

        ConfigureFillBarElement(fillWidget, fillLength, isVertical, fillFromEnd, 0d);

        if (TryResolveReferencedWidget(widget.Attributes, "ClipWidget", out var clipWidget))
        {
            clipWidget.Element.ClipToBounds = true;
            ConfigureFillBarElement(clipWidget, fillLength, isVertical, fillFromEnd, 0d);
        }

        if (TryResolveReferencedWidget(widget.Attributes, "ChangeWidget", out var changeWidget))
        {
            var completelyFillChange = ResolveBoolean(widget.Attributes, "CompletelyFillChange");
            var showNegativeChange = ResolveBoolean(widget.Attributes, "ShowNegativeChange");
            var customChangeColor = ResolveBoolean(widget.Attributes, "CustomChangeColor");
            var normalizedDelta = maxAmount > 0 ? Math.Clamp(changeDelta / maxAmount, -1d, 1d) : 0d;
            if (normalizedDelta > 0)
            {
                var positiveLength = completelyFillChange
                    ? CalculateFillBarLength(hostLength, changeWidget.Element.Margin, Math.Clamp(currentAmount, 0d, maxAmount), maxAmount, isVertical)
                    : Math.Max(0d, Math.Min(changeLength, hostLength - fillLength));
                ConfigureFillBarElement(changeWidget, positiveLength, isVertical, fillFromEnd, fillLength);
                changeWidget.Element.Visibility = Visibility.Visible;
                if (!customChangeColor)
                {
                    ApplyPreviewState(changeWidget, "Positive", includeChildren: false);
                }
            }
            else if (normalizedDelta < 0 && showNegativeChange)
            {
                ConfigureFillBarElement(changeWidget, changeLength, isVertical, fillFromEnd, fillLength - changeLength);
                changeWidget.Element.Visibility = Visibility.Visible;
                if (!customChangeColor)
                {
                    ApplyPreviewState(changeWidget, "Negative", includeChildren: false);
                }
            }
            else
            {
                ConfigureFillBarElement(changeWidget, 0d, isVertical, fillFromEnd, fillLength);
                changeWidget.Element.Visibility = Visibility.Collapsed;
                ApplyPreviewState(changeWidget, "Default", includeChildren: false);
            }

            if (TryResolveReferencedWidget(widget.Attributes, "DividerWidget", out var dividerWidget))
            {
                dividerWidget.Element.Visibility = Math.Abs(normalizedDelta) > 0.0001
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                PositionDividerWidget(dividerWidget, changeWidget, fillLength, changeLength, changeDelta, isVertical);
            }
        }
    }

    private void MaterializeSortIndicator(RenderedWidgetRegistration widget, string state)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "SortVisualWidget", out var sortVisual))
        {
            return;
        }

        var showArrow = state != "Hidden";
        sortVisual.Element.Visibility = showArrow ? Visibility.Visible : Visibility.Collapsed;
        sortVisual.Element.Opacity = showArrow ? 1d : 0.35;
        sortVisual.Element.RenderTransformOrigin = new Point(0.5, 0.5);

        var isDescending = state == "Descending";
        sortVisual.Element.RenderTransform = new RotateTransform(isDescending ? 180d : 0d);
    }

    private void MaterializeTargetState(RenderedWidgetRegistration widget)
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

    private void MaterializeDelayedState(RenderedWidgetRegistration widget)
    {
        var targetWidget = TryResolveReferencedWidget(widget.Attributes, "TargetWidget", out var resolvedTargetWidget)
            ? resolvedTargetWidget
            : widget;
        var trigger = ResolveBoolean(widget.Attributes, "Trigger");
        var autoStart = ResolveBoolean(widget.Attributes, "AutoStart");
        var stateResetable = ResolveBoolean(widget.Attributes, "StateResetable");
        var includeChildren = ResolveBoolean(widget.Attributes, "IncludeChildren");
        var requestedState = ResolveBinding(widget.Attributes.TryGetValue("State", out var stateValue) ? stateValue : string.Empty);

        CapturePreviewBaseState(targetWidget);
        if ((!trigger && !autoStart) || string.IsNullOrWhiteSpace(requestedState))
        {
            if (stateResetable)
            {
                ApplyPreviewState(targetWidget, GetPreviewBaseState(targetWidget), includeChildren);
            }

            return;
        }

        ApplyPreviewState(targetWidget, requestedState, includeChildren);
    }

    private void MaterializeSliderFill(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "ChangeFillWidget", out var changeFillWidget))
        {
            return;
        }

        var sliderSize = Measure(widget.Element, widget.Attributes, ResolveRegisteredParentSize(widget));
        if (sliderSize.Width <= 0)
        {
            return;
        }

        var minValue = ResolveDouble(widget.Attributes, "MinValueFloat", double.NaN);
        if (double.IsNaN(minValue))
        {
            minValue = ResolveDouble(widget.Attributes, "MinValueInt", 0);
        }

        var maxValue = ResolveDouble(widget.Attributes, "MaxValueFloat", double.NaN);
        if (double.IsNaN(maxValue))
        {
            maxValue = ResolveDouble(widget.Attributes, "MaxValueInt", double.NaN);
        }

        if (double.IsNaN(maxValue) || Math.Abs(maxValue - minValue) < 0.0001)
        {
            return;
        }

        var baseValue = ResolveDouble(widget.Attributes, "BaseValueInt", minValue);
        var currentValue = HasAttribute(widget.Attributes, "ValueFloat")
            ? ResolveDouble(widget.Attributes, "ValueFloat", baseValue)
            : ResolveDouble(widget.Attributes, "ValueInt", baseValue);
        var normalizedBase = Math.Clamp((baseValue - minValue) / (maxValue - minValue), 0d, 1d);
        var baseOffset = normalizedBase * sliderSize.Width;

        changeFillWidget.Element.HorizontalAlignment = HorizontalAlignment.Left;
        if (currentValue < baseValue)
        {
            var width = Math.Clamp((baseValue - currentValue) / (maxValue - minValue), 0d, 1d) * sliderSize.Width;
            changeFillWidget.Element.Width = width;
            Canvas.SetLeft(changeFillWidget.Element, Math.Max(0d, baseOffset - width));
            ApplyPreviewState(changeFillWidget, "Positive", includeChildren: false);
        }
        else if (currentValue > baseValue)
        {
            var width = Math.Clamp((currentValue - baseValue) / (maxValue - minValue), 0d, 1d) * sliderSize.Width;
            changeFillWidget.Element.Width = width;
            Canvas.SetLeft(changeFillWidget.Element, baseOffset);
            ApplyPreviewState(changeFillWidget, "Negative", includeChildren: false);
        }
        else
        {
            changeFillWidget.Element.Width = 0;
            Canvas.SetLeft(changeFillWidget.Element, baseOffset);
            ApplyPreviewState(changeFillWidget, "Default", includeChildren: false);
        }
    }

    private void MaterializeClip(RenderedWidgetRegistration widget)
    {
        if (!ResolveBoolean(widget.Attributes, "ClipContents"))
        {
            return;
        }

        widget.Element.ClipToBounds = true;
        if (widget.ChildHost is not null)
        {
            widget.ChildHost.ClipToBounds = true;
        }
    }

    private void MaterializeScrollViewport(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "ClipRect", out var clipRectWidget) ||
            !TryResolveReferencedWidget(widget.Attributes, "InnerPanel", out var innerPanelWidget))
        {
            return;
        }

        clipRectWidget.Element.ClipToBounds = true;
        if (clipRectWidget.ChildHost is not null)
        {
            clipRectWidget.ChildHost.ClipToBounds = true;
        }

        var viewportSize = Measure(clipRectWidget.Element, clipRectWidget.Attributes, clipRectWidget.ParentSize);
        var contentSize = Measure(innerPanelWidget.Element, innerPanelWidget.Attributes, viewportSize);
        var contentMargin = innerPanelWidget.Element.Margin;
        var contentWidth = contentSize.Width + contentMargin.Left + contentMargin.Right;
        var contentHeight = contentSize.Height + contentMargin.Top + contentMargin.Bottom;
        var horizontalOverflow = Math.Max(0d, contentWidth - viewportSize.Width);
        var verticalOverflow = Math.Max(0d, contentHeight - viewportSize.Height);
        var reverseInitialAlignment = ResolveBoolean(widget.Attributes, "ReverseInitialScrollBarAlignment");

        if (TryResolveReferencedWidget(widget.Attributes, "HorizontalScrollbar", out var horizontalScrollbar))
        {
            var scrollbarValue = ResolveScrollbarValue(horizontalScrollbar.Attributes, horizontalOverflow, reverseInitialAlignment);
            ApplyScrollbarViewportBehavior(
                horizontalScrollbar,
                horizontalOverflow > 0,
                horizontalOverflow,
                viewportSize.Width,
                contentWidth,
                widget.Attributes,
                horizontalAxis: true);
            ApplyScrollOffsetToInnerPanel(innerPanelWidget, clipRectWidget, scrollbarValue, horizontalAxis: true);
        }

        if (TryResolveReferencedWidget(widget.Attributes, "VerticalScrollbar", out var verticalScrollbar))
        {
            var scrollbarValue = ResolveScrollbarValue(verticalScrollbar.Attributes, verticalOverflow, reverseInitialAlignment);
            ApplyScrollbarViewportBehavior(
                verticalScrollbar,
                verticalOverflow > 0,
                verticalOverflow,
                viewportSize.Height,
                contentHeight,
                widget.Attributes,
                horizontalAxis: false);
            ApplyScrollOffsetToInnerPanel(innerPanelWidget, clipRectWidget, scrollbarValue, horizontalAxis: false);
        }
    }

    private void MaterializeScrollHandle(RenderedWidgetRegistration widget)
    {
        if (!TryResolveReferencedWidget(widget.Attributes, "Handle", out var handleWidget))
        {
            return;
        }

        var scrollbarSize = Measure(widget.Element, widget.Attributes, widget.ParentSize);
        var minValue = ResolveDouble(widget.Attributes, "MinValue", 0);
        var maxValue = ResolveDouble(widget.Attributes, "MaxValue", minValue);
        var currentValue = ResolveScrollbarValue(widget.Attributes, maxValue, reverseInitialAlignment: false);
        UpdateScrollbarHandleVisual(widget, handleWidget, scrollbarSize, minValue, maxValue, currentValue);
    }

    private void ApplyScrollbarViewportBehavior(
        RenderedWidgetRegistration scrollbarWidget,
        bool canScroll,
        double overflowAmount,
        double viewportLength,
        double contentLength,
        IReadOnlyDictionary<string, string> ownerAttributes,
        bool horizontalAxis)
    {
        var updateVisibility = ResolveBoolean(ownerAttributes, "UpdateScrollbarVisibility", true);
        var autoHideBars = ResolveBoolean(ownerAttributes, "AutoHideScrollBars");
        var autoHideHandle = ResolveBoolean(ownerAttributes, "AutoHideScrollBarHandle");
        var autoAdjustHandle = !HasAttribute(ownerAttributes, "AutoAdjustScrollbarHandleSize") ||
                               ResolveBoolean(ownerAttributes, "AutoAdjustScrollbarHandleSize", true);
        var minValue = ResolveDouble(scrollbarWidget.Attributes, "MinValue", 0);
        var maxValue = canScroll ? Math.Max(1d, overflowAmount) : minValue;
        var currentValue = ResolveScrollbarValue(scrollbarWidget.Attributes, maxValue, reverseInitialAlignment: false);

        if (TryResolveReferencedWidget(scrollbarWidget.Attributes, "Handle", out var handleWidget))
        {
            if (autoAdjustHandle)
            {
                var trackSize = Measure(scrollbarWidget.Element, scrollbarWidget.Attributes, scrollbarWidget.ParentSize);
                var handleLength = contentLength > 0
                    ? Math.Max(0d, (horizontalAxis ? trackSize.Width : trackSize.Height) * (viewportLength / contentLength))
                    : 0d;
                if (horizontalAxis)
                {
                    if (handleLength > 0)
                    {
                        handleWidget.Element.Width = handleLength;
                    }
                }
                else if (handleLength > 0)
                {
                    handleWidget.Element.Height = handleLength;
                }
            }

            if (updateVisibility)
            {
                handleWidget.Element.Visibility = !canScroll && (autoHideBars || autoHideHandle)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            var scrollbarSize = Measure(scrollbarWidget.Element, scrollbarWidget.Attributes, scrollbarWidget.ParentSize);
            UpdateScrollbarHandleVisual(scrollbarWidget, handleWidget, scrollbarSize, minValue, maxValue, currentValue);
        }

        if (updateVisibility)
        {
            scrollbarWidget.Element.Visibility = !canScroll && autoHideBars
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void ApplyScrollOffsetToInnerPanel(
        RenderedWidgetRegistration innerPanelWidget,
        RenderedWidgetRegistration clipRectWidget,
        double scrollValue,
        bool horizontalAxis)
    {
        if (clipRectWidget.ChildHost is not Canvas)
        {
            return;
        }

        if (horizontalAxis)
        {
            var currentLeft = Canvas.GetLeft(innerPanelWidget.Element);
            if (double.IsNaN(currentLeft))
            {
                currentLeft = 0;
            }

            var alignment = innerPanelWidget.Attributes.TryGetValue("HorizontalAlignment", out var horizontalAlignment)
                ? ResolveDebugValue(innerPanelWidget.Attributes, "HorizontalAlignment", horizontalAlignment)
                : "Left";
            var delta = string.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase)
                ? scrollValue
                : -scrollValue;
            Canvas.SetLeft(innerPanelWidget.Element, currentLeft + delta);
            return;
        }

        var currentTop = Canvas.GetTop(innerPanelWidget.Element);
        if (double.IsNaN(currentTop))
        {
            currentTop = 0;
        }

        var vertical = innerPanelWidget.Attributes.TryGetValue("VerticalAlignment", out var verticalAlignment)
            ? ResolveDebugValue(innerPanelWidget.Attributes, "VerticalAlignment", verticalAlignment)
            : "Top";
        var verticalDelta = string.Equals(vertical, "Bottom", StringComparison.OrdinalIgnoreCase)
            ? scrollValue
            : -scrollValue;
        Canvas.SetTop(innerPanelWidget.Element, currentTop + verticalDelta);
    }

    private double ResolveScrollbarValue(
        IReadOnlyDictionary<string, string> attributes,
        double maxValue,
        bool reverseInitialAlignment)
    {
        var minValue = ResolveDouble(attributes, "MinValue", 0);
        var explicitValue = HasAttribute(attributes, "ValueFloat")
            ? ResolveDouble(attributes, "ValueFloat", minValue)
            : HasAttribute(attributes, "ValueInt")
                ? ResolveDouble(attributes, "ValueInt", minValue)
                : minValue;
        var effectiveMax = Math.Max(minValue, maxValue);
        if (reverseInitialAlignment && Math.Abs(explicitValue - minValue) < 0.01)
        {
            explicitValue = effectiveMax;
        }

        return Math.Clamp(explicitValue, minValue, effectiveMax);
    }

    private void UpdateScrollbarHandleVisual(
        RenderedWidgetRegistration scrollbarWidget,
        RenderedWidgetRegistration handleWidget,
        Size scrollbarSize,
        double minValue,
        double maxValue,
        double currentValue)
    {
        if (scrollbarWidget.ChildHost is not Canvas)
        {
            return;
        }

        var horizontalAxis = string.Equals(
            ResolveAttributeLiteral(scrollbarWidget.Attributes, "AlignmentAxis"),
            "Horizontal",
            StringComparison.OrdinalIgnoreCase);
        var handleSize = Measure(handleWidget.Element, handleWidget.Attributes, scrollbarSize);
        var range = Math.Max(0d, maxValue - minValue);
        var normalized = range <= 0.0001 ? 0d : Math.Clamp((currentValue - minValue) / range, 0d, 1d);

        if (horizontalAxis)
        {
            var maxTravel = Math.Max(0d, scrollbarSize.Width - handleSize.Width);
            Canvas.SetLeft(handleWidget.Element, maxTravel * normalized);
            Canvas.SetTop(handleWidget.Element, 0);
            return;
        }

        var verticalTravel = Math.Max(0d, scrollbarSize.Height - handleSize.Height);
        Canvas.SetLeft(handleWidget.Element, 0);
        Canvas.SetTop(handleWidget.Element, verticalTravel * normalized);
    }

    private Size ResolveFillBarHostSize(RenderedWidgetRegistration widget, RenderedWidgetRegistration fillWidget, bool isVertical)
    {
        if (TryResolveReferencedWidget(widget.Attributes, "ContainerWidget", out var containerWidget))
        {
            return Measure(containerWidget.Element, containerWidget.Attributes, ResolveRegisteredParentSize(containerWidget));
        }

        if (TryResolveReferencedWidget(widget.Attributes, "ClipWidget", out var clipWidget))
        {
            return Measure(clipWidget.Element, clipWidget.Attributes, ResolveRegisteredParentSize(clipWidget));
        }

        return Measure(fillWidget.Element, fillWidget.Attributes, ResolveRegisteredParentSize(fillWidget));
    }

    private static double CalculateFillBarLength(
        double hostLength,
        Thickness margin,
        double amount,
        double maxAmount,
        bool isVertical)
    {
        if (maxAmount <= 0 || hostLength <= 0)
        {
            return 0d;
        }

        var availableLength = isVertical
            ? Math.Max(0d, hostLength - margin.Top - margin.Bottom)
            : Math.Max(0d, hostLength - margin.Left - margin.Right);
        return Math.Clamp(amount / maxAmount, 0d, 1d) * availableLength;
    }

    private static void ConfigureFillBarElement(
        RenderedWidgetRegistration widget,
        double length,
        bool isVertical,
        bool fillFromEnd,
        double offset)
    {
        widget.Element.RenderTransform = Transform.Identity;
        if (isVertical)
        {
            widget.Element.VerticalAlignment = fillFromEnd ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            widget.Element.Height = Math.Max(0d, length);
            Canvas.SetTop(widget.Element, Math.Max(0d, offset));
        }
        else
        {
            widget.Element.HorizontalAlignment = fillFromEnd ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            widget.Element.Width = Math.Max(0d, length);
            Canvas.SetLeft(widget.Element, Math.Max(0d, offset));
        }

        widget.Element.Opacity = Math.Max(widget.Element.Opacity, length <= 0 ? 0.2 : 0.45);
    }

    private static void PositionDividerWidget(
        RenderedWidgetRegistration dividerWidget,
        RenderedWidgetRegistration changeWidget,
        double fillLength,
        double changeLength,
        double changeDelta,
        bool isVertical)
    {
        var dividerSize = isVertical ? dividerWidget.Element.DesiredSize.Height : dividerWidget.Element.DesiredSize.Width;
        if (isVertical)
        {
            var boundary = changeDelta >= 0 ? fillLength + changeLength : fillLength;
            Canvas.SetTop(dividerWidget.Element, Math.Max(0d, boundary - dividerSize));
        }
        else
        {
            var boundary = changeDelta >= 0 ? fillLength + changeLength : fillLength;
            Canvas.SetLeft(dividerWidget.Element, Math.Max(0d, boundary - dividerSize));
        }
    }

    private void ApplyPreviewState(
        RenderedWidgetRegistration widget,
        string stateName,
        bool includeChildren)
    {
        foreach (var target in EnumeratePreviewStateTargets(widget, includeChildren))
        {
            CapturePreviewBaseState(target);
            SetPreviewState(target, stateName);
        }
    }

    private IEnumerable<RenderedWidgetRegistration> EnumeratePreviewStateTargets(
        RenderedWidgetRegistration widget,
        bool includeChildren)
    {
        yield return widget;
        if (!includeChildren)
        {
            yield break;
        }

        var prefix = widget.WidgetPath + WidgetPathSeparator;
        var rootId = _graphElements.FirstOrDefault(p => ReferenceEquals(p.Value.Element, widget.Element)).Key;
        var instance = _graphInstanceKeys.GetValueOrDefault(rootId, "");
        foreach (var entry in _graphElements)
        {
            var childInstance = _graphInstanceKeys.GetValueOrDefault(entry.Key, "");
            if (entry.Value.WidgetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (instance.Length == 0 || childInstance == instance || childInstance.StartsWith(instance + "/", StringComparison.Ordinal)))
                yield return entry.Value;
        }
    }

    private void CapturePreviewBaseState(RenderedWidgetRegistration widget)
    {
        if (!string.IsNullOrWhiteSpace(PreviewElementDebugProperties.GetPreviewBaseState(widget.Element)))
        {
            return;
        }

        PreviewElementDebugProperties.SetPreviewBaseState(widget.Element, ResolveWidgetPreviewState(widget));
    }

    private string GetPreviewBaseState(RenderedWidgetRegistration widget)
    {
        var stored = PreviewElementDebugProperties.GetPreviewBaseState(widget.Element);
        return string.IsNullOrWhiteSpace(stored) ? "Default" : stored;
    }

    private void SetPreviewState(RenderedWidgetRegistration widget, string stateName)
    {
        var normalizedState = string.IsNullOrWhiteSpace(stateName) ? "Default" : stateName.Trim();
        PreviewElementDebugProperties.SetPreviewState(widget.Element, normalizedState);
        ApplyPreviewStatePresentation(widget.Element, normalizedState);
    }

    private string ResolveWidgetPreviewState(RenderedWidgetRegistration widget)
    {
        var stored = PreviewElementDebugProperties.GetPreviewState(widget.Element);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        if (widget.Attributes.TryGetValue("CurrentState", out var currentState))
        {
            var resolved = ResolveBinding(currentState);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        if (ResolveBoolean(widget.Attributes, "IsDisabled") || HasAttribute(widget.Attributes, "IsEnabled") && !ResolveBoolean(widget.Attributes, "IsEnabled", true))
        {
            return "Disabled";
        }

        if (ResolveBoolean(widget.Attributes, "IsSelected"))
        {
            return "Selected";
        }

        return "Default";
    }

    private static void ApplyPreviewStatePresentation(FrameworkElement element, string stateName)
    {
        var normalizedState = stateName.Trim();
        if (normalizedState.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            if (element.Effect is DropShadowEffect)
            {
                element.Effect = null;
            }

            return;
        }

        var highlightColor = normalizedState.Contains("negative", StringComparison.OrdinalIgnoreCase) ||
                             normalizedState.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                             normalizedState.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            ? Colors.OrangeRed
            : normalizedState.Contains("positive", StringComparison.OrdinalIgnoreCase)
                ? Colors.LimeGreen
                : normalizedState.Contains("selected", StringComparison.OrdinalIgnoreCase) ||
                  normalizedState.Contains("open", StringComparison.OrdinalIgnoreCase) ||
                  normalizedState.Contains("hover", StringComparison.OrdinalIgnoreCase)
                    ? Colors.Gold
                    : Colors.DeepSkyBlue;
        element.Effect = new DropShadowEffect
        {
            Color = highlightColor,
            BlurRadius = 12,
            ShadowDepth = 0,
            Opacity = 0.75
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
        _renderedNodeCount++;
        var widgetPath = TryGetWidgetPath(attributes);
        if (!string.IsNullOrWhiteSpace(widgetPath))
        {
            {
                var segments = widgetPath.Split(" > ", StringSplitOptions.None);
                var templatePaths = new List<string>();
                for (var i = 0; i < segments.Length; i++)
                    if (segments[i].StartsWith("ItemTemplate", StringComparison.Ordinal))
                        templatePaths.Add(string.Join(" > ", segments.Take(i)));
                var instance = string.Concat(_instanceKeys.Select((key, i) => "/" +
                    (i < templatePaths.Count ? templatePaths[i] : widgetPath) + "=" + key));
                var id = StableWidgetId.Create(GraphPrefabIdentity, widgetPath, instance);
                // Source navigation also needs registrations in the interactive preview.
                {
                    var previous = SnapshotElements.FindIndex(e => e.Id == id);
                    var snapshot = new SnapshotElement(id, widgetPath, renderedNode.Element, attributes);
                    if (previous < 0) SnapshotElements.Add(snapshot);
                    else SnapshotElements[previous] = snapshot;
                }
            }
            _renderedWidgetsByPath[widgetPath] = new RenderedWidgetRegistration(
                widgetPath,
                nodeName,
                attributes,
                parentSize,
                renderedNode.Element,
                renderedNode.ChildHost);
            var instanceId = BuildInstanceKey(widgetPath);
            _graphInstanceKeys[new WidgetId(StableWidgetId.Create(GraphPrefabIdentity, widgetPath, instanceId))] = instanceId;
            _graphElements[new WidgetId(StableWidgetId.Create(GraphPrefabIdentity, widgetPath, instanceId))] = _renderedWidgetsByPath[widgetPath];
        }

        return renderedNode;
    }

    private string BuildInstanceKey(string widgetPath)
    {
        var segments = widgetPath.Split(" > ", StringSplitOptions.None);
        var templatePaths = new List<string>();
        for (var i = 0; i < segments.Length; i++)
            if (segments[i].StartsWith("ItemTemplate", StringComparison.Ordinal)) templatePaths.Add(string.Join(" > ", segments.Take(i)));
        return string.Concat(_instanceKeys.Select((key, i) => "/" + (i < templatePaths.Count ? templatePaths[i] : widgetPath) + "=" + key));
    }

    private readonly Dictionary<WidgetId, string> _graphInstanceKeys = [];
    private WidgetId? _activeGraphWidget;
    private IReadOnlyDictionary<WidgetId, System.Collections.Immutable.ImmutableDictionary<string, WidgetId>> _graphReferences = new Dictionary<WidgetId, System.Collections.Immutable.ImmutableDictionary<string, WidgetId>>();
    private IReadOnlyDictionary<WidgetId, IReadOnlyDictionary<string, string>> _graphDescriptions = new Dictionary<WidgetId, IReadOnlyDictionary<string, string>>();
    private IReadOnlyDictionary<WidgetId, ElementBlueprint> _graphBlueprints = new Dictionary<WidgetId, ElementBlueprint>();
    internal void SetBehaviorPlan(System.Collections.Immutable.ImmutableArray<BehaviorInput> inputs, BehaviorGraphPlan plan)
    {
        _graphBlueprints = plan.Contributions.Where(c => c.Contribution is ElementBlueprint).ToDictionary(c => c.Widget, c => (ElementBlueprint)c.Contribution);
        _graphReferences = inputs.ToDictionary(i => i.Widget.Id, i => i.References);
        _graphDescriptions = inputs.ToDictionary(i => i.Widget.Id, i => (IReadOnlyDictionary<string, string>)plan.Contributions
            .Where(c => c.Widget == i.Widget.Id && c.Contribution is DescriptionContribution)
            .Select(c => (DescriptionContribution)c.Contribution).GroupBy(c => c.Property, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Concat(g.Select(c => c.Value)), StringComparer.OrdinalIgnoreCase));
    }

    internal RenderedPrefabNode RenderNode(PrefabNode node, Size parentSize)
    {
        var path = TryGetWidgetPath(node.Attributes) ?? node.Name;
        var id = new WidgetId(StableWidgetId.Create(GraphPrefabIdentity, path, BuildInstanceKey(path)));
        if (_graphBlueprints.TryGetValue(id, out var blueprint))
        {
            if (blueprint.Kind == ElementKind.Container) return new WidgetNode(node.Name, node.Attributes, node.Children).Render(this, parentSize);
            if (blueprint.Kind == ElementKind.Placeholder) return new FallbackNode(node.Name, node.Attributes, node.Children).Render(this, parentSize);
            if (blueprint.Kind == ElementKind.Image && blueprint.Image is { } image) return RenderImage(node, parentSize, image);
            if (blueprint.Kind is ElementKind.Text or ElementKind.KeyGlyph)
            {
                var attributes = new Dictionary<string, string>(node.Attributes, StringComparer.OrdinalIgnoreCase) { ["Text"] = blueprint.Text ?? "" };
                return new PreviewTextNode(node.Name, attributes, node.Children).Render(this, parentSize);
            }
        }
        return node.Render(this, parentSize);
    }

    internal void ExecuteBehaviorPhase(BehaviorGraphPlan plan, BehaviorPhase phase)
    {
        foreach (var planned in plan.Contributions.Where(c => c.Contribution.Phase == phase))
        {
            if (!_graphElements.TryGetValue(planned.Widget, out var widget)) continue;
            _activeGraphWidget = planned.Widget;
            switch (planned.Contribution)
            {
                case ConnectionContribution { StateSource: not null, Target: not null } state:
                    var target = _graphElements[state.Target.Value];
                    ApplyPreviewState(target, state.StateValue ?? "Default", ResolveBoolean(target.Attributes, "UpdateChildrenStates"));
                    break;
                case LayoutContribution layout:
                    switch (layout.Intent)
                    {
                        case LayoutIntent.CopyDimensions:
                            var dimensions = _graphElements[layout.Source!.Value];
                            var size = Measure(dimensions.Element, dimensions.Attributes, ResolveRegisteredParentSize(dimensions));
                            if (layout.Axis is "Horizontal" or "HorizontalAndVertical") widget.Element.Width = Math.Max(0, size.Width + layout.Padding);
                            if (layout.Axis is "Vertical" or "HorizontalAndVertical") widget.Element.Height = Math.Max(0, size.Height + layout.Padding);
                            break;
                        case LayoutIntent.ScrollViewport: MaterializeScrollViewport(widget); break;
                        case LayoutIntent.Clip: MaterializeClip(widget); break;
                        case LayoutIntent.ToggleSelection: MaterializeToggleSelection(widget); break;
                        case LayoutIntent.Dropdown: MaterializeDropdown(widget); break;
                        case LayoutIntent.RadialChildren: MaterializeRadialChildren(widget); break;
                        case LayoutIntent.ScrollHandle: MaterializeScrollHandle(widget); break;
                        case LayoutIntent.Fill: MaterializeFill(widget, layout.Value == "Vertical"); break;
                        case LayoutIntent.SortIndicator: MaterializeSortIndicator(widget, layout.Value ?? "Hidden"); break;
                        case LayoutIntent.TargetState: MaterializeTargetState(widget); break;
                        case LayoutIntent.DelayedState: MaterializeDelayedState(widget); break;
                        case LayoutIntent.SliderFill: MaterializeSliderFill(widget); break;
                        case LayoutIntent.SetState: ApplyPreviewState(widget, layout.Value ?? "Default", ResolveBoolean(widget.Attributes, "UpdateChildrenStates")); break;
                        case LayoutIntent.RangeValue:
                            var handle = _graphElements[layout.Source!.Value];
                            var hostSize = Measure(widget.Element, widget.Attributes, ResolveRegisteredParentSize(widget));
                            var handleSize = Measure(handle.Element, handle.Attributes, hostSize);
                            var fraction = double.Parse(layout.Value!, CultureInfo.InvariantCulture);
                            var vertical = layout.Axis == "Vertical";
                            if (vertical) Canvas.SetTop(handle.Element, Math.Max(0, hostSize.Height - handleSize.Height) * fraction);
                            else Canvas.SetLeft(handle.Element, Math.Max(0, hostSize.Width - handleSize.Width) * fraction);
                            if (layout.SecondaryTarget is { } fillerId && _graphElements.TryGetValue(fillerId, out var filler))
                            {
                                if (vertical) filler.Element.Height = hostSize.Height * fraction;
                                else filler.Element.Width = hostSize.Width * fraction;
                            }
                            break;
                    }
                    break;
                case InspectionContribution inspection:
                    if (!string.IsNullOrWhiteSpace(inspection.Diagnostic)) _diagnostics.Add($"{widget.WidgetPath}: {inspection.Diagnostic}");
                    if (inspection.Support == BehaviorSupport.Partial) _diagnostics.MarkTruncated($"{widget.WidgetPath}: {inspection.Diagnostic ?? "Behavior fidelity is partial."}");
                    break;
            }
        }
        _activeGraphWidget = null;
    }

    private bool TryGetDescription(IReadOnlyDictionary<string, string> attributes, string property, out string value)
    {
        var path = TryGetWidgetPath(attributes);
        if (path is not null && _graphDescriptions.TryGetValue(new WidgetId(StableWidgetId.Create(GraphPrefabIdentity, path, BuildInstanceKey(path))), out var description) && description.TryGetValue(property, out value!)) return true;
        value = "";
        return false;
    }

    public bool IsVisible(IReadOnlyDictionary<string, string> attributes)
    {
        var path = TryGetWidgetPath(attributes);
        if (path is not null && _graphDescriptions.TryGetValue(new WidgetId(StableWidgetId.Create(GraphPrefabIdentity, path, BuildInstanceKey(path))), out var description) && description.TryGetValue("IsVisible", out var visible))
            return visible == "true";
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

        if (string.IsNullOrWhiteSpace(spriteId)) return null;
        var canvas = new Canvas();
        canvas.Children.Add(new TextBlock { Text = "Missing asset: " + spriteId, Foreground = Brushes.Crimson, FontSize = 11 });
        return new Border { BorderBrush = Brushes.Crimson, BorderThickness = new Thickness(1), Child = canvas,
            ToolTip = spriteAsset.Note, IsHitTestVisible = false };
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

    public Rect ResolveBrushSelectionRect(string? brushName, bool isDisabled, Size renderedSize)
    {
        if (string.IsNullOrWhiteSpace(brushName) ||
            renderedSize.Width <= 0 ||
            renderedSize.Height <= 0)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        var visibleLayers = ResolveVisibleBrushLayers(brushName, isDisabled);
        var layer = visibleLayers
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.SpriteName))
            ?? visibleLayers
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.SpriteName));
        if (layer is null)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        var extendedWidth = Math.Max(1, renderedSize.Width + (layer.ExtendLeft ?? 0) + (layer.ExtendRight ?? 0));
        var extendedHeight = Math.Max(1, renderedSize.Height + (layer.ExtendTop ?? 0) + (layer.ExtendBottom ?? 0));
        var selectionBounds = ResolveSpriteSelectionRect(layer.SpriteName, new Size(extendedWidth, extendedHeight));
        if (selectionBounds.IsEmpty)
        {
            return new Rect(new Point(0, 0), renderedSize);
        }

        return new Rect(
            selectionBounds.X - (layer.ExtendLeft ?? 0),
            selectionBounds.Y - (layer.ExtendTop ?? 0),
            selectionBounds.Width,
            selectionBounds.Height);
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

        return GetSpriteOpaqueBounds(spriteImage);
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

        return GetSpriteCoreVerticalBounds(spriteImage);
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
                        ResolveCoverChildrenButtonMinWidth(metricWidth, metricHeight, padding));
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

    private static double ResolveCoverChildrenButtonMinWidth(
        double? metricWidth,
        double? metricHeight,
        Thickness padding)
    {
        var paddingWidth = Math.Max(0, padding.Left) + Math.Max(0, padding.Right);

        // CoverChildren buttons should grow from their label and chrome padding instead of
        // inheriting the full fixed-button width baked into the brush metric.
        var centerWidthFloor = metricHeight.HasValue
            ? Math.Max(24, metricHeight.Value * 1.3)
            : Math.Max(24, (metricWidth ?? 0) * 0.22);

        if (metricWidth.HasValue)
        {
            centerWidthFloor = Math.Min(centerWidthFloor, metricWidth.Value);
        }

        return paddingWidth + centerWidthFloor;
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
        if (TryGetDescription(attributes, "IsEnabled", out var enabled)) element.IsEnabled = ResolveBoolean(enabled, true);
        if (TryGetDescription(attributes, "IsHitTestVisible", out var hitTest)) element.IsHitTestVisible = ResolveBoolean(hitTest, true);
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

        if (width.UsedFallback || height.UsedFallback)
        {
            _diagnostics.AddMeasureFallback(element.GetType().Name, measureInput.Width, measureInput.Height);
        }

        if (UsesStretchToParent(attributes, "WidthSizePolicy") &&
            !DoubleUtil.AreClose(element.Width, width.Value))
        {
            element.Width = width.Value;
        }

        if (UsesStretchToParent(attributes, "HeightSizePolicy") &&
            !DoubleUtil.AreClose(element.Height, height.Value))
        {
            element.Height = height.Value;
        }

        return new Size(width.Value, height.Value);
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize)
    {
        AttachChildren(host, children, parentSize, new Point(0, 0));
    }

    public void AttachChildren(Panel host, IEnumerable<PrefabNode> children, Size parentSize, Point origin)
    {
        var childNodes = children as IReadOnlyList<PrefabNode> ?? children.ToArray();
        if (host is GauntletStackLayoutPanel gauntletStackHost)
        {
            AttachGauntletStackChildren(gauntletStackHost, childNodes, parentSize);
            return;
        }

        foreach (var child in childNodes)
        {
            if (!TryAllowAdditionalNodeRendering())
            {
                return;
            }

            var rendered = RegisterRenderedNode(child.Name, child.Attributes, parentSize, RenderNode(child, parentSize));
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

            if (host is GauntletGridLayoutPanel gauntletGridPanel)
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
                PrepareGauntletGridLayoutChild(rendered.Element, child.Attributes, gauntletGridPanel);
                AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, parentSize, Measure(rendered.Element, child.Attributes, parentSize));
                gauntletGridPanel.Children.Add(rendered.Element);
                continue;
            }

            _alignmentBoundsByElement[rendered.Element] = rendered.AlignmentBounds;
            var elementSize = Measure(rendered.Element, child.Attributes, parentSize);
            var position = ComputePosition(child.Attributes, parentSize, elementSize, rendered.AlignmentBounds);
            host.Children.Add(rendered.Element);
            Canvas.SetLeft(rendered.Element, origin.X + position.X);
            Canvas.SetTop(rendered.Element, origin.Y + position.Y);
            AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, parentSize, elementSize, position, rendered.AlignmentBounds);
        }
    }

    private bool TryAllowAdditionalNodeRendering()
    {
        if (_renderedNodeCount < MaxRenderedNodeCount)
        {
            return true;
        }

        if (_renderBudgetExceeded)
        {
            return false;
        }

        _renderBudgetExceeded = true;
        var reason =
            $"Render node budget of {MaxRenderedNodeCount} was reached. Remaining descendant widgets were skipped to keep the preview responsive.";
        _diagnostics.MarkTruncated(reason);
        _diagnostics.Add(reason);
        return false;
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
            canvas.RenderTransform = Transform.Identity;
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

        canvas.Margin = new Thickness(0);
        canvas.RenderTransform = !DoubleUtil.AreClose(offsetX, 0) || !DoubleUtil.AreClose(offsetY, 0)
            ? new TranslateTransform(offsetX, offsetY)
            : Transform.Identity;
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

    public string? ResolveStackLayoutMethod(IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("StackLayout.LayoutMethod", out var stackLayoutMethod) &&
            Enum.TryParse<GauntletStackLayoutMethod>(stackLayoutMethod, true, out var parsedStackLayoutMethod))
        {
            return parsedStackLayoutMethod.ToString();
        }

        if (attributes.TryGetValue("LayoutImp.LayoutMethod", out var layoutImpMethod) &&
            Enum.TryParse<GauntletStackLayoutMethod>(layoutImpMethod, true, out var parsedLayoutImpMethod))
        {
            return parsedLayoutImpMethod.ToString();
        }

        return ResolveAttributeLiteral(attributes, "StackLayout.LayoutMethod") ??
               ResolveAttributeLiteral(attributes, "LayoutImp.LayoutMethod");
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
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        if (_spriteDefinitionCache.TryGetValue(spriteName, out var cachedSprite))
        {
            return cachedSprite;
        }

        var resolvedSprite = _assetCatalog.FindSprite(spriteName);
        _spriteDefinitionCache[spriteName] = resolvedSprite;
        return resolvedSprite;
    }

    public bool IsSpriteMissing(string? spriteName)
    {
        return !string.IsNullOrWhiteSpace(spriteName) &&
               ResolveSpriteAsset(spriteName).Kind == SpriteAssetResolutionKind.Missing;
    }

    public BrushDefinition? FindBrush(string? brushName)
    {
        if (string.IsNullOrWhiteSpace(brushName))
        {
            return null;
        }

        if (_brushDefinitionCache.TryGetValue(brushName, out var cachedBrush))
        {
            return cachedBrush;
        }

        var resolvedBrush = _assetCatalog.FindBrush(brushName);
        _brushDefinitionCache[brushName] = resolvedBrush;
        return resolvedBrush;
    }

    public void AttachElementDebugMetadata(
        FrameworkElement element,
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        IEnumerable<string?>? spriteNames = null,
        IEnumerable<string?>? brushNames = null,
        bool isDisabled = false)
    {
        var debugInfo = BuildElementDebugInfo(nodeName, attributes, spriteNames, brushNames, isDisabled);
        PreviewElementDebugProperties.SetDebugInfo(element, debugInfo);
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
        var debugInfo = PreviewElementDebugProperties.GetDebugInfo(element);
        if (debugInfo is null)
        {
            return;
        }

        debugInfo.Layout = BuildLayoutDebugInfo(element, attributes, parentSize, measuredSize, position, alignmentBounds);
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
        if (!string.IsNullOrWhiteSpace(spriteName) &&
            _spriteAssetResolutionCache.TryGetValue(spriteName, out var cachedSpriteAsset))
        {
            return cachedSpriteAsset;
        }

        var spriteAsset = _assetCatalog.ResolveSpriteAsset(spriteName);
        if (spriteAsset.ImageResolution is { Status: ImageResolutionStatus.Invalid } invalid)
            throw new System.IO.InvalidDataException($"Sprite '{spriteName}': {invalid.Reason}");
        if (!string.IsNullOrWhiteSpace(spriteName))
        {
            _spriteAssetResolutionCache[spriteName] = spriteAsset;
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

        var cacheKey = CreateBrushStateCacheKey(brushName, isDisabled);
        if (_brushDebugInfoCache.TryGetValue(cacheKey, out var cachedInfo))
            return cachedInfo;

        var brushDefinition = FindBrush(brushName);
        if (brushDefinition is null)
        {
            var missingInfo = new BrushDebugInfo(
                brushName,
                null,
                PanelBrushes.ContainsKey(brushName) || TextBrushes.ContainsKey(brushName),
                []);
            _brushDebugInfoCache[cacheKey] = missingInfo;
            return missingInfo;
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

        var info = new BrushDebugInfo(brushName, brushDefinition, false, visibleLayers);
        _brushDebugInfoCache[cacheKey] = info;
        return info;
    }

    private IReadOnlyList<BrushLayerDefinition> ResolveVisibleBrushLayers(string? brushName, bool isDisabled)
    {
        if (string.IsNullOrWhiteSpace(brushName))
            return [];

        var cacheKey = CreateBrushStateCacheKey(brushName, isDisabled);
        if (_visibleBrushLayersCache.TryGetValue(cacheKey, out var cachedLayers))
            return cachedLayers;

        var brush = ResolveCatalogBrush(brushName);
        if (brush is null)
        {
            _visibleBrushLayersCache[cacheKey] = [];
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

        _visibleBrushLayersCache[cacheKey] = layers;
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
        return spriteAsset.ImageResolution?.Image ?? (spriteAsset.HasImage
            ? _assetCatalog.GetSpriteImage(spriteAsset.MatchedKey)
            : null);
    }

    private bool TryCreateBrushImageBrush(string? brushName, bool isDisabled, out Brush brush)
    {
        if (!string.IsNullOrWhiteSpace(brushName) &&
            _renderBrushCache.TryGetValue(
                CreateBrushStateCacheKey(brushName, isDisabled),
                out brush!))
            return true;

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
        if (!string.IsNullOrWhiteSpace(brushName))
            _renderBrushCache[CreateBrushStateCacheKey(brushName, isDisabled)] = brush;
        return true;
    }

    private static string CreateBrushStateCacheKey(string brushName, bool isDisabled)
    {
        return $"{(isDisabled ? '1' : '0')}|{brushName}";
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

        var mainVisual = CreateBrushLayerSpriteVisual(layer, extendedWidth, extendedHeight);
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

    private FrameworkElement? CreateBrushLayerSpriteVisual(BrushLayerDefinition layer, double width, double height)
    {
        var spriteDefinition = ResolveCatalogSprite(layer.SpriteName);
        if (spriteDefinition?.IsNineRegion == true)
        {
            return CreateSpriteVisual(layer.SpriteName, width, height);
        }

        var syntheticNineSlice = ResolveSyntheticBrushNineSlice(layer, spriteDefinition);
        return CreateSpriteVisual(layer.SpriteName, width, height, syntheticNineSlice);
    }

    private static Thickness? ResolveSyntheticBrushNineSlice(BrushLayerDefinition layer, SpriteDefinition? spriteDefinition)
    {
        if (spriteDefinition is null || spriteDefinition.IsNineRegion)
        {
            return null;
        }

        var left = Math.Max(0, layer.ExtendLeft ?? 0);
        var top = Math.Max(0, layer.ExtendTop ?? 0);
        var right = Math.Max(0, layer.ExtendRight ?? 0);
        var bottom = Math.Max(0, layer.ExtendBottom ?? 0);
        if (left <= 0 && top <= 0 && right <= 0 && bottom <= 0)
        {
            return null;
        }

        return new Thickness(left, top, right, bottom);
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
                (spriteName, targetWidth, targetHeight) => CreateSpriteVisual(spriteName, targetWidth, targetHeight),
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

    private FrameworkElement? CreateSpriteVisual(string? spriteName, double width, double height, Thickness? syntheticNineSlice = null)
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

        if (syntheticNineSlice is not null)
        {
            return CreateNineSliceVisual(
                bitmapSource,
                (int)Math.Round(syntheticNineSlice.Value.Left),
                (int)Math.Round(syntheticNineSlice.Value.Right),
                (int)Math.Round(syntheticNineSlice.Value.Top),
                (int)Math.Round(syntheticNineSlice.Value.Bottom),
                width,
                height);
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

    private static Int32Rect? GetSpriteOpaqueBounds(BitmapSource bitmapSource)
    {
        return SpritePixelBoundsCache.GetValue(bitmapSource, AnalyzeSpritePixelBounds).OpaqueBounds;
    }

    private static (int Top, int Bottom)? GetSpriteCoreVerticalBounds(BitmapSource bitmapSource)
    {
        return SpritePixelBoundsCache.GetValue(bitmapSource, AnalyzeSpritePixelBounds).CoreVerticalBounds;
    }

    private static SpritePixelBounds AnalyzeSpritePixelBounds(BitmapSource bitmapSource)
    {
        var width = bitmapSource.PixelWidth;
        var height = bitmapSource.PixelHeight;
        if (width <= 0 || height <= 0)
            return new SpritePixelBounds(null, null);

        // Bounds depend only on alpha, which is identical in BGRA and premultiplied BGRA.
        var normalized = bitmapSource.Format == PixelFormats.Bgra32 || bitmapSource.Format == PixelFormats.Pbgra32
            ? bitmapSource
            : new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        var centerX = width / 2;
        var halfBand = Math.Min(4, Math.Max(0, width / 40));
        var startX = Math.Max(0, centerX - halfBand);
        var endX = Math.Min(width - 1, centerX + halfBand);
        var coreTop = -1;
        var coreBottom = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            var hasCorePixel = false;
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[rowOffset + (x * 4) + 3];
                if (alpha >= ContentCoreAlphaThreshold &&
                    x >= startX &&
                    x <= endX)
                    hasCorePixel = true;

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

            if (hasCorePixel)
            {
                if (coreTop < 0) coreTop = y;
                coreBottom = y;
            }
        }

        Int32Rect? opaqueBounds = maxX >= 0 && maxY >= 0
            ? new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : null;
        (int Top, int Bottom)? coreVerticalBounds = coreTop >= 0 && coreBottom >= coreTop
            ? (coreTop, coreBottom)
            : null;
        return new SpritePixelBounds(opaqueBounds, coreVerticalBounds);
    }

    private sealed record SpritePixelBounds(
        Int32Rect? OpaqueBounds,
        (int Top, int Bottom)? CoreVerticalBounds);

    public double ApplyVisualTextCentering(TextBlock textBlock, double availableWidth)
    {
        textBlock.ClearValue(TextBlock.LineStackingStrategyProperty);
        textBlock.ClearValue(TextBlock.LineHeightProperty);
        if (!ShouldApplyVisualTextCentering(textBlock))
        {
            textBlock.RenderTransform = Transform.Identity;
            return 0;
        }

        var verticalOffset = ComputeVisualTextVerticalOffset(textBlock, availableWidth);
        if (Math.Abs(verticalOffset) < 0.25d)
        {
            textBlock.RenderTransform = Transform.Identity;
            return 0;
        }

        textBlock.RenderTransform = new TranslateTransform(0, verticalOffset);
        return verticalOffset;
    }

    private FrameworkElement CreateNineSliceVisual(
        BitmapSource bitmapSource,
        SpriteDefinition spriteDefinition,
        double targetWidth,
        double targetHeight)
    {
        return CreateNineSliceVisual(
            bitmapSource,
            spriteDefinition.LeftWidth ?? 0,
            spriteDefinition.RightWidth ?? 0,
            spriteDefinition.TopHeight ?? 0,
            spriteDefinition.BottomHeight ?? 0,
            targetWidth,
            targetHeight);
    }

    private FrameworkElement CreateNineSliceVisual(
        BitmapSource bitmapSource,
        int leftWidth,
        int rightWidth,
        int topHeight,
        int bottomHeight,
        double targetWidth,
        double targetHeight)
    {
        leftWidth = Math.Clamp(leftWidth, 0, bitmapSource.PixelWidth);
        rightWidth = Math.Clamp(rightWidth, 0, Math.Max(0, bitmapSource.PixelWidth - leftWidth));
        topHeight = Math.Clamp(topHeight, 0, bitmapSource.PixelHeight);
        bottomHeight = Math.Clamp(bottomHeight, 0, Math.Max(0, bitmapSource.PixelHeight - topHeight));

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
            .Where(layer => string.IsNullOrWhiteSpace(layer.SpriteName) && TryResolveLayerColor(layer, out _))
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

    private static bool ShouldApplyVisualTextCentering(TextBlock textBlock)
    {
        return textBlock.VerticalAlignment == VerticalAlignment.Center ||
               textBlock.HorizontalAlignment == HorizontalAlignment.Center ||
               textBlock.TextAlignment == TextAlignment.Center;
    }

    private static double ComputeVisualTextVerticalOffset(TextBlock textBlock, double availableWidth)
    {
        if (string.IsNullOrWhiteSpace(textBlock.Text) || textBlock.FontSize <= 0)
        {
            return 0;
        }

        try
        {
            var pixelsPerDip = 1d;
            var formattedText = new FormattedText(
                textBlock.Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    textBlock.FontFamily,
                    textBlock.FontStyle,
                    textBlock.FontWeight,
                    textBlock.FontStretch),
                textBlock.FontSize,
                Brushes.Transparent,
                pixelsPerDip)
            {
                TextAlignment = textBlock.TextAlignment,
                Trimming = textBlock.TextTrimming
            };

            if (textBlock.TextWrapping != TextWrapping.NoWrap &&
                !double.IsNaN(availableWidth) &&
                !double.IsInfinity(availableWidth) &&
                availableWidth > 0)
            {
                formattedText.MaxTextWidth = availableWidth;
            }

            var bounds = formattedText.BuildGeometry(new Point(0, 0)).Bounds;
            if (bounds.IsEmpty || bounds.Height <= 0 || formattedText.Height <= 0)
            {
                return 0;
            }

            var layoutCenter = formattedText.Height / 2d;
            var glyphCenter = bounds.Top + (bounds.Height / 2d);
            var verticalOffset = layoutCenter - glyphCenter;
            var maxReasonableOffset = Math.Max(2d, textBlock.FontSize * 0.18d);
            return Math.Clamp(verticalOffset, -maxReasonableOffset, maxReasonableOffset);
        }
        catch
        {
            return 0;
        }
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

    private MeasuredAxisResult ResolveMeasuredAxis(
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

        var resolved = ResolveMeasuredLength(measured, fallback, out var usedFallback);
        return new MeasuredAxisResult(
            CoerceAxisToBoundaries(attributes, minKey, maxKey, resolved),
            usedFallback);
    }

    private double ResolveMeasuredLength(double preferred, double fallback)
    {
        return ResolveMeasuredLength(preferred, fallback, out _);
    }

    private double ResolveMeasuredLength(double preferred, double fallback, out bool usedFallback)
    {
        if (!double.IsNaN(preferred) && !double.IsInfinity(preferred) && preferred > 0)
        {
            usedFallback = false;
            return preferred;
        }

        if (!double.IsNaN(fallback) && !double.IsInfinity(fallback) && fallback > 0)
        {
            usedFallback = true;
            return fallback;
        }

        usedFallback = false;
        return 0;
    }

    private readonly record struct MeasuredAxisResult(double Value, bool UsedFallback);

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

    private double? ResolveOptionalPreviewDouble(
        IReadOnlyDictionary<string, string> attributes,
        string attributeName,
        double? unresolvedBindingFallback = null)
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
        if (!double.IsNaN(resolvedValue))
        {
            return resolvedValue;
        }

        return unresolvedBindingFallback.HasValue &&
               rawValue.StartsWith("@", StringComparison.Ordinal)
            ? unresolvedBindingFallback.Value
            : null;
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

    private ElementDebugInfo BuildElementDebugInfo(
        string nodeName,
        IReadOnlyDictionary<string, string> attributes,
        IEnumerable<string?>? spriteNames,
        IEnumerable<string?>? brushNames,
        bool isDisabled)
    {
        var debugInfo = new ElementDebugInfo(nodeName);

        if (attributes.Count > 0)
        {
            foreach (var attribute in attributes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (IsInternalDebugAttribute(attribute.Key))
                {
                    continue;
                }

                var resolvedValue = ResolveDebugValue(attributes, attribute.Key, attribute.Value);
                debugInfo.Attributes.Add(new ElementDebugAttribute(
                    attribute.Key,
                    attribute.Value,
                    string.Equals(resolvedValue, attribute.Value, StringComparison.Ordinal) ? null : resolvedValue));
            }
        }

        var widgetPath = TryGetWidgetPath(attributes);
        if (!string.IsNullOrWhiteSpace(widgetPath))
        {
            var resolvedProperties = _propertyMap.GetProperties(widgetPath);
            if (resolvedProperties.Count > 0)
            {
                foreach (var property in resolvedProperties.Values.OrderBy(property => property.AttributeName, StringComparer.OrdinalIgnoreCase))
                {
                    debugInfo.ResolvedProperties.Add(new ElementDebugResolvedProperty(property));
                }
            }

            var resolvedReferences = _referenceMap.GetReferences(widgetPath);
            if (resolvedReferences.Count > 0)
            {
                foreach (var reference in resolvedReferences.Values.OrderBy(reference => reference.AttributeName, StringComparer.OrdinalIgnoreCase))
                {
                    debugInfo.WidgetReferences.Add(new ElementDebugWidgetReference(reference));
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
            foreach (var brushName in resolvedBrushes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                debugInfo.Brushes.Add(BuildBrushDebugInfo(brushName, isDisabled, resolvedSprites));
            }
        }

        if (resolvedSprites.Count > 0)
        {
            foreach (var spriteName in resolvedSprites.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                debugInfo.Sprites.Add(BuildSpriteDebugInfo(spriteName, "Direct"));
            }
        }

        return debugInfo;
    }

    private ElementDebugSection BuildLayoutDebugInfo(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        Size parentSize,
        Size measuredSize,
        Point? position,
        Rect? alignmentBounds)
    {
        var layout = new ElementDebugSection("Layout");
        layout.Fields.Add(new ElementDebugField("ParentSize", FormatSize(parentSize)));
        layout.Fields.Add(new ElementDebugField("MeasuredSize", FormatSize(measuredSize)));

        attributes.TryGetValue("WidthSizePolicy", out var widthPolicy);
        attributes.TryGetValue("HeightSizePolicy", out var heightPolicy);
        if (!string.IsNullOrWhiteSpace(widthPolicy) || !string.IsNullOrWhiteSpace(heightPolicy))
        {
            layout.Fields.Add(new ElementDebugField(
                "SizePolicy",
                $"W={ResolveDebugValue(attributes, "WidthSizePolicy", widthPolicy ?? "-")} " +
                $"H={ResolveDebugValue(attributes, "HeightSizePolicy", heightPolicy ?? "-")}"));
        }

        attributes.TryGetValue("SuggestedWidth", out var suggestedWidth);
        attributes.TryGetValue("SuggestedHeight", out var suggestedHeight);
        if (!string.IsNullOrWhiteSpace(suggestedWidth) || !string.IsNullOrWhiteSpace(suggestedHeight))
        {
            layout.Fields.Add(new ElementDebugField(
                "SuggestedSize",
                $"W={ResolveDebugValue(attributes, "SuggestedWidth", suggestedWidth ?? "-")} " +
                $"H={ResolveDebugValue(attributes, "SuggestedHeight", suggestedHeight ?? "-")}"));
        }

        attributes.TryGetValue("HorizontalAlignment", out var horizontalAlignment);
        attributes.TryGetValue("VerticalAlignment", out var verticalAlignment);
        if (!string.IsNullOrWhiteSpace(horizontalAlignment) || !string.IsNullOrWhiteSpace(verticalAlignment))
        {
            layout.Fields.Add(new ElementDebugField(
                "Alignment",
                $"H={ResolveDebugValue(attributes, "HorizontalAlignment", horizontalAlignment ?? "Left")} " +
                $"V={ResolveDebugValue(attributes, "VerticalAlignment", verticalAlignment ?? "Top")}"));
        }

        layout.Fields.Add(new ElementDebugField("Margin", FormatThickness(element.Margin, attributes)));

        attributes.TryGetValue("PositionXOffset", out var positionXOffset);
        attributes.TryGetValue("PositionYOffset", out var positionYOffset);
        if (!string.IsNullOrWhiteSpace(positionXOffset) || !string.IsNullOrWhiteSpace(positionYOffset))
        {
            layout.Fields.Add(new ElementDebugField(
                "Offsets",
                $"X={ResolveDebugValue(attributes, "PositionXOffset", positionXOffset ?? "0")} " +
                $"Y={ResolveDebugValue(attributes, "PositionYOffset", positionYOffset ?? "0")}"));
        }

        layout.Fields.Add(new ElementDebugField(
            "WpfSize",
            $"W={FormatOptionalDimension(double.IsNaN(element.Width) ? null : element.Width)} H={FormatOptionalDimension(double.IsNaN(element.Height) ? null : element.Height)}"));

        if (position.HasValue)
        {
            layout.Fields.Add(new ElementDebugField("CanvasPosition", FormatPoint(position.Value)));
        }

        if (alignmentBounds is { IsEmpty: false } bounds)
        {
            layout.Fields.Add(new ElementDebugField("AlignmentBounds", FormatRect(bounds)));
        }

        return layout;
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

    private ElementDebugBrush BuildBrushDebugInfo(
        string brushName,
        bool isDisabled,
        ISet<string> resolvedSprites)
    {
        var brushInfo = ResolveBrushDebugInfo(brushName, isDisabled);
        if (brushInfo.BrushDefinition is null)
        {
            return new ElementDebugBrush(brushName, null, true, brushInfo.IsPreviewFallback, []);
        }

        var layers = new List<ElementDebugBrushLayer>();

        foreach (var layer in brushInfo.VisibleLayers)
        {
            if (!string.IsNullOrWhiteSpace(layer.SpriteName))
            {
                resolvedSprites.Add(layer.SpriteName);
            }

            if (!string.IsNullOrWhiteSpace(layer.OverlaySpriteName))
            {
                resolvedSprites.Add(layer.OverlaySpriteName);
            }

            layers.Add(new ElementDebugBrushLayer(
                layer.Name,
                layer.SpriteName,
                layer.OverlaySpriteName,
                layer.OverlayMethod,
                layer.AlphaFactor,
                layer.ExtendLeft,
                layer.ExtendTop,
                layer.ExtendRight,
                layer.ExtendBottom,
                layer.MaxExtend,
                layer.HorizontalFlip == true,
                layer.VerticalFlip == true));
        }

        return new ElementDebugBrush(
            brushInfo.RequestedName,
            brushInfo.BrushDefinition.SourcePath,
            false,
            false,
            layers);
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

    public void PrepareGauntletGridLayoutChild(
        FrameworkElement element,
        IReadOnlyDictionary<string, string> attributes,
        GauntletGridLayoutPanel _)
    {
        var widthPolicy = ResolveSizePolicy(attributes, "WidthSizePolicy");
        var heightPolicy = ResolveSizePolicy(attributes, "HeightSizePolicy");

        if (string.Equals(widthPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Width = double.NaN;
        }

        if (string.Equals(heightPolicy, "StretchToParent", StringComparison.OrdinalIgnoreCase))
        {
            element.Height = double.NaN;
        }
    }

    public void RelayoutCanvasChildren(Canvas host, IReadOnlyList<PrefabNode> children, Size parentSize)
    {
        RelayoutCanvasChildren(host, children, parentSize, new Point(0, 0));
    }

    public void RelayoutCanvasChildren(
        Canvas host,
        IReadOnlyList<PrefabNode> children,
        Size parentSize,
        Point origin)
    {
        host.RenderTransform = Transform.Identity;
        host.Margin = new Thickness(0);
        host.Width = double.NaN;
        host.Height = double.NaN;

        var elements = host.Children.OfType<FrameworkElement>().ToArray();
        var childCount = Math.Min(children.Count, elements.Length);
        for (var index = 0; index < childCount; index++)
        {
            var child = children[index];
            var element = elements[index];
            var elementSize = Measure(element, child.Attributes, parentSize);
            _alignmentBoundsByElement.TryGetValue(element, out var alignmentBounds);
            var position = ComputePosition(child.Attributes, parentSize, elementSize, alignmentBounds);
            Canvas.SetLeft(element, origin.X + position.X);
            Canvas.SetTop(element, origin.Y + position.Y);
            AttachElementLayoutDebugMetadata(
                element,
                child.Attributes,
                parentSize,
                elementSize,
                position,
                alignmentBounds);
        }

        var contentBounds = MeasureCanvasChildrenBounds(host);
        ApplyCanvasLayout(host, contentBounds);
    }

    private void AttachGauntletStackChildren(
        GauntletStackLayoutPanel host,
        IReadOnlyList<PrefabNode> children,
        Size parentSize)
    {
        var preparedChildren = new PreparedStackChild?[children.Count];
        var occupiedFixedPrimary = 0d;

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (IsVisible(child.Attributes) &&
                ResolveStackPrimaryStretchRatio(host.LayoutMethod, child.Attributes) > 0d)
            {
                continue;
            }

            if (!TryPrepareGauntletStackChild(host, child, parentSize, out var prepared))
            {
                return;
            }

            preparedChildren[index] = prepared;
            if (prepared.Rendered.Element.Visibility != Visibility.Collapsed)
            {
                occupiedFixedPrimary += GetStackLayoutOccupiedPrimary(
                    host.LayoutMethod,
                    prepared.MeasuredSize,
                    prepared.Rendered.Element.Margin);
            }
        }

        var remainingPrimary = GetStackPrimarySize(host.LayoutMethod, parentSize);
        if (!double.IsInfinity(remainingPrimary))
        {
            remainingPrimary = Math.Max(0d, remainingPrimary - occupiedFixedPrimary);
        }

        var remainingStretchRatio = children
            .Where(child => IsVisible(child.Attributes))
            .Sum(child => ResolveStackPrimaryStretchRatio(host.LayoutMethod, child.Attributes));

        for (var index = 0; index < children.Count; index++)
        {
            if (preparedChildren[index] is not null)
            {
                continue;
            }

            var child = children[index];
            var stretchRatio = ResolveStackPrimaryStretchRatio(host.LayoutMethod, child.Attributes);
            var allocatedPrimary = double.IsInfinity(remainingPrimary)
                ? double.PositiveInfinity
                : remainingStretchRatio > stretchRatio
                    ? remainingPrimary * stretchRatio / remainingStretchRatio
                    : remainingPrimary;
            var renderParentSize = WithStackPrimarySize(host.LayoutMethod, parentSize, allocatedPrimary);
            if (!TryPrepareGauntletStackChild(host, child, renderParentSize, out var prepared))
            {
                return;
            }

            preparedChildren[index] = prepared;
            if (prepared.Rendered.Element.Visibility != Visibility.Collapsed && !double.IsInfinity(remainingPrimary))
            {
                remainingPrimary = Math.Max(
                    0d,
                    remainingPrimary - GetStackLayoutOccupiedPrimary(
                        host.LayoutMethod,
                        prepared.MeasuredSize,
                        prepared.Rendered.Element.Margin));
            }

            remainingStretchRatio = Math.Max(0d, remainingStretchRatio - stretchRatio);
        }

        foreach (var prepared in preparedChildren)
        {
            if (prepared is not null)
            {
                host.Children.Add(prepared.Rendered.Element);
            }
        }
    }

    private bool TryPrepareGauntletStackChild(
        GauntletStackLayoutPanel host,
        PrefabNode child,
        Size renderParentSize,
        out PreparedStackChild prepared)
    {
        if (!TryAllowAdditionalNodeRendering())
        {
            prepared = null!;
            return false;
        }

        var rendered = RegisterRenderedNode(
            child.Name,
            child.Attributes,
            renderParentSize,
            child.Render(this, renderParentSize));
        if (rendered.AutoWidthInFlowLayout)
        {
            rendered.Element.Width = double.NaN;
        }

        if (rendered.AutoHeightInFlowLayout)
        {
            rendered.Element.Height = double.NaN;
        }

        ApplyFlowLayout(rendered.Element, child.Attributes);
        PrepareGauntletStackLayoutChild(rendered.Element, child.Attributes, host.LayoutMethod);
        var measuredSize = Measure(rendered.Element, child.Attributes, renderParentSize);
        AttachElementLayoutDebugMetadata(rendered.Element, child.Attributes, renderParentSize, measuredSize);
        prepared = new PreparedStackChild(rendered, measuredSize);
        return true;
    }

    private double ResolveStackPrimaryStretchRatio(
        GauntletStackLayoutMethod layoutMethod,
        IReadOnlyDictionary<string, string> attributes)
    {
        var primaryPolicy = IsHorizontalStackLayout(layoutMethod)
            ? "WidthSizePolicy"
            : "HeightSizePolicy";
        if (!UsesStretchToParent(attributes, primaryPolicy))
        {
            return 0d;
        }

        var ratioAttribute = IsHorizontalStackLayout(layoutMethod)
            ? "WidthStretchRatio"
            : "HeightStretchRatio";
        var ratio = ResolveOptionalDouble(attributes, ratioAttribute) ??
                    ResolveOptionalDouble(attributes, "StretchRatio") ??
                    1d;
        return ratio > 0d ? ratio : 1d;
    }

    private static Size WithStackPrimarySize(
        GauntletStackLayoutMethod layoutMethod,
        Size size,
        double primarySize)
    {
        return IsHorizontalStackLayout(layoutMethod)
            ? new Size(primarySize, size.Height)
            : new Size(size.Width, primarySize);
    }

    private static bool IsHorizontalStackLayout(GauntletStackLayoutMethod layoutMethod)
    {
        return layoutMethod is GauntletStackLayoutMethod.HorizontalLeftToRight
            or GauntletStackLayoutMethod.HorizontalRightToLeft
            or GauntletStackLayoutMethod.HorizontalCentered
            or GauntletStackLayoutMethod.HorizontalSpaced;
    }

    private static double GetStackPrimarySize(GauntletStackLayoutMethod layoutMethod, Size size)
    {
        return IsHorizontalStackLayout(layoutMethod) ? size.Width : size.Height;
    }

    private static double GetStackLayoutOccupiedPrimary(
        GauntletStackLayoutMethod layoutMethod,
        Size measuredSize,
        Thickness margin)
    {
        return IsHorizontalStackLayout(layoutMethod)
            ? measuredSize.Width + margin.Left + margin.Right
            : measuredSize.Height + margin.Top + margin.Bottom;
    }

    private ElementDebugSprite BuildSpriteDebugInfo(string spriteName, string usage)
    {
        var spriteAsset = ResolveSpriteAsset(spriteName);
        return new ElementDebugSprite(
            spriteName,
            usage,
            spriteAsset.Kind,
            spriteAsset.MatchedKey,
            spriteAsset.ImagePath,
            spriteAsset.SpriteDefinition,
            spriteAsset.Note);
    }

    private sealed record RenderedWidgetRegistration(
        string WidgetPath,
        string NodeName,
        IReadOnlyDictionary<string, string> Attributes,
        Size ParentSize,
        FrameworkElement Element,
        Panel? ChildHost);

    private sealed record PreparedStackChild(RenderedPrefabNode Rendered, Size MeasuredSize);
}
