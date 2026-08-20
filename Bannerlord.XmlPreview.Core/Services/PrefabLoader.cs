using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Services;

public sealed class PrefabLoader : IPrefabLoader
{
    private const int MaxLoadAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);
    private static readonly string[] MetadataElementNames =
    [
        "Constants",
        "Parameters",
        "Variables",
        "VisualDefinitions"
    ];

    private readonly IAssetCatalog _assetCatalog;
    private readonly IBrushMetricsProvider _brushMetricsProvider;
    private readonly IPrefabReferenceResolver _prefabReferenceResolver;
    private readonly IWidgetPropertyResolver _widgetPropertyResolver;
    private readonly IWidgetReferenceResolver _widgetReferenceResolver;
    private readonly IWidgetNodeFactory _widgetNodeFactory;

    public PrefabLoader(
        IAssetCatalog assetCatalog,
        IBrushMetricsProvider brushMetricsProvider,
        IPrefabReferenceResolver prefabReferenceResolver,
        IWidgetPropertyResolver widgetPropertyResolver,
        IWidgetReferenceResolver widgetReferenceResolver,
        IWidgetNodeFactory widgetNodeFactory)
    {
        _assetCatalog = assetCatalog;
        _brushMetricsProvider = brushMetricsProvider;
        _prefabReferenceResolver = prefabReferenceResolver;
        _widgetPropertyResolver = widgetPropertyResolver;
        _widgetReferenceResolver = widgetReferenceResolver;
        _widgetNodeFactory = widgetNodeFactory;
    }

    public PrefabDocument Load(string path, PreviewDiagnostics diagnostics)
    {
        var fullPath = Path.GetFullPath(path);
        var state = LoadPrefabState(fullPath, diagnostics, [fullPath], includeRootOnly: false);
        var constants = ParseConstants(
            state.ConstantsElement,
            state.DefaultParameters,
            diagnostics,
            reportMissingConstants: true);
        var expandedRoot = ExpandElement(
            state.RootNodeElement,
            state.DefaultParameters,
            constants,
            diagnostics,
            state.ExpansionStack,
            out _);
        PrefabVisualTreeHelper.AssignCanonicalPaths(expandedRoot);
        var referenceMap = _widgetReferenceResolver.Resolve(expandedRoot, diagnostics);
        var propertyMap = _widgetPropertyResolver.Resolve(expandedRoot, referenceMap, diagnostics);

        var bindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootNode = ParseNode(expandedRoot, bindingKeys);
        var nodeTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var maxDepth = CollectNodeStatistics(rootNode, nodeTypeCounts, 1);
        diagnostics.SetDocumentSummary(
            fullPath,
            rootNode.Name,
            constants.Count,
            bindingKeys.Count,
            nodeTypeCounts.Values.Sum(),
            maxDepth,
            nodeTypeCounts);
        return new PrefabDocument(fullPath, rootNode, constants, bindingKeys, propertyMap, referenceMap);
    }

    private static XDocument LoadDocumentWithRetry(string path, PreviewDiagnostics diagnostics)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxLoadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                return XDocument.Load(stream, LoadOptions.None);
            }
            catch (Exception exception) when (IsTransientLoadException(exception) && attempt < MaxLoadAttempts)
            {
                lastException = exception;
                Thread.Sleep(RetryDelay);
            }
            catch (Exception exception) when (IsTransientLoadException(exception))
            {
                lastException = exception;
                break;
            }
        }

        diagnostics.Add(
            $"The prefab file was temporarily locked or incomplete while reloading. Retried {MaxLoadAttempts} times before failing.");

        throw new IOException(
            $"Could not read prefab '{path}' after {MaxLoadAttempts} attempts. The file is likely still being written by another process.",
            lastException);
    }

    private static bool IsTransientLoadException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or XmlException;
    }

    private PrefabExpansionState LoadPrefabState(
        string path,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> expansionStack,
        bool includeRootOnly)
    {
        var document = LoadDocumentWithRetry(path, diagnostics);
        var root = document.Root ?? throw new InvalidOperationException("The prefab XML is missing a root element.");
        var rootWindow = root.Element("Window") ?? root;
        var rootNodeElement = includeRootOnly
            ? GetIncludeRootElement(rootWindow, path)
            : new XElement(rootWindow);

        return new PrefabExpansionState(
            Path.GetFullPath(path),
            rootNodeElement,
            ParseParameters(root.Element("Parameters")),
            root.Element("Constants"),
            expansionStack);
    }

    private static Dictionary<string, string> ParseParameters(XElement? parametersElement)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parametersElement is null)
        {
            return parameters;
        }

        foreach (var parameterElement in parametersElement.Elements())
        {
            if (!string.Equals(parameterElement.Name.LocalName, "Parameter", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parameterElement.Name.LocalName, "Paramter", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = parameterElement.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            parameters[name] = parameterElement.Attribute("DefaultValue")?.Value ?? string.Empty;
        }

        return parameters;
    }

    private Dictionary<string, string> ParseConstants(
        XElement? constantsElement,
        IReadOnlyDictionary<string, string> parameters,
        PreviewDiagnostics diagnostics,
        bool reportMissingConstants)
    {
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (constantsElement is null)
        {
            if (reportMissingConstants)
            {
                diagnostics.Add("No <Constants> section found. Using generic preview defaults.");
            }

            return constants;
        }

        var constantElements = constantsElement.Elements("Constant")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value))
            .ToDictionary(
                element => element.Attribute("Name")!.Value,
                element => element,
                StringComparer.OrdinalIgnoreCase);

        foreach (var constantName in constantElements.Keys)
        {
            ResolveConstantValue(
                constantName,
                constantElements,
                constants,
                parameters,
                diagnostics,
                []);
        }

        return constants;
    }

    private string ResolveConstantValue(
        string constantName,
        IReadOnlyDictionary<string, XElement> constantElements,
        IDictionary<string, string> resolvedConstants,
        IReadOnlyDictionary<string, string> parameters,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> resolutionStack)
    {
        if (resolvedConstants.TryGetValue(constantName, out var cachedValue))
        {
            return cachedValue;
        }

        if (!constantElements.TryGetValue(constantName, out var constantElement))
        {
            diagnostics.Add($"Constant '{constantName}' could not be resolved and was skipped.");
            return string.Empty;
        }

        if (resolutionStack.Any(entry => string.Equals(entry, constantName, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add($"Constant resolution cycle detected for '{constantName}'.");
            return string.Empty;
        }

        var nextStack = resolutionStack.Append(constantName).ToArray();
        string resolvedValue;

        var rawValue = constantElement.Attribute("Value")?.Value;
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            resolvedValue = ResolveScopedValue(rawValue, parameters, resolvedConstants, constantElements, diagnostics, nextStack);
        }
        else if (constantElement.Attribute("BooleanCheck")?.Value is { Length: > 0 } booleanCheck)
        {
            var resolvedCheck = ResolveScopedValue(booleanCheck, parameters, resolvedConstants, constantElements, diagnostics, nextStack);
            var branchValue = ResolveBooleanValue(resolvedCheck)
                ? constantElement.Attribute("OnTrue")?.Value ?? string.Empty
                : constantElement.Attribute("OnFalse")?.Value ?? string.Empty;
            resolvedValue = ResolveScopedValue(branchValue, parameters, resolvedConstants, constantElements, diagnostics, nextStack);
        }
        else if (!string.IsNullOrWhiteSpace(constantElement.Attribute("BrushName")?.Value) &&
                 !string.IsNullOrWhiteSpace(constantElement.Attribute("BrushValueType")?.Value))
        {
            var brushName = ResolveScopedValue(
                constantElement.Attribute("BrushName")?.Value,
                parameters,
                resolvedConstants,
                constantElements,
                diagnostics,
                nextStack);
            var brushLayerName = constantElement.Attribute("BrushLayer")?.Value;
            var valueType = constantElement.Attribute("BrushValueType")?.Value ?? string.Empty;
            var resolvedMetric = _brushMetricsProvider.ResolveMetric(brushName, brushLayerName, valueType);
            if (!string.IsNullOrWhiteSpace(brushName) && resolvedMetric.HasValue)
            {
                resolvedValue = resolvedMetric.Value.ToString(CultureInfo.InvariantCulture);
                diagnostics.AddResolvedConstant(
                    constantName,
                    brushName,
                    brushLayerName ?? "Default",
                    valueType,
                    resolvedMetric.Value);
            }
            else
            {
                diagnostics.Add($"Constant '{constantName}' could not resolve brush metric '{valueType}' from '{brushName}'.");
                resolvedValue = string.Empty;
            }
        }
        else if (!string.IsNullOrWhiteSpace(constantElement.Attribute("SpriteName")?.Value) &&
                 !string.IsNullOrWhiteSpace(constantElement.Attribute("SpriteValueType")?.Value))
        {
            var spriteName = ResolveScopedValue(
                constantElement.Attribute("SpriteName")?.Value,
                parameters,
                resolvedConstants,
                constantElements,
                diagnostics,
                nextStack);
            var spriteValueType = constantElement.Attribute("SpriteValueType")?.Value ?? string.Empty;
            resolvedValue = ResolveSpriteMetric(spriteName, spriteValueType, diagnostics, constantName);
        }
        else
        {
            diagnostics.Add($"Constant '{constantName}' uses an unsupported shape and was skipped.");
            resolvedValue = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(resolvedValue))
        {
            resolvedValue = ApplyNumericTransform(
                resolvedValue,
                constantElement.Attribute("MultiplyResult")?.Value,
                constantElement.Attribute("Additive")?.Value,
                parameters,
                resolvedConstants,
                constantElements,
                diagnostics,
                nextStack);

            if (!string.IsNullOrWhiteSpace(constantElement.Attribute("Prefix")?.Value))
            {
                var prefix = ResolveScopedValue(
                    constantElement.Attribute("Prefix")?.Value,
                    parameters,
                    resolvedConstants,
                    constantElements,
                    diagnostics,
                    nextStack);
                resolvedValue = $"{prefix}{resolvedValue}";
            }
        }

        resolvedConstants[constantName] = resolvedValue;
        return resolvedValue;
    }

    private string ResolveScopedValue(
        string? rawValue,
        IReadOnlyDictionary<string, string> parameters,
        IDictionary<string, string> resolvedConstants,
        IReadOnlyDictionary<string, XElement> constantElements,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> resolutionStack)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim();
        if (value[0] == '*' && value.Length > 1)
        {
            return parameters.TryGetValue(value[1..], out var parameterValue)
                ? parameterValue
                : rawValue;
        }

        if (value[0] == '!' && value.Length > 1)
        {
            return ResolveConstantValue(
                value[1..],
                constantElements,
                resolvedConstants,
                parameters,
                diagnostics,
                resolutionStack);
        }

        return rawValue;
    }

    private string ResolveSpriteMetric(
        string? spriteName,
        string spriteValueType,
        PreviewDiagnostics diagnostics,
        string constantName)
    {
        var sprite = _assetCatalog.FindSprite(spriteName);
        if (sprite is null)
        {
            diagnostics.Add($"Constant '{constantName}' could not resolve sprite metric '{spriteValueType}' from '{spriteName}'.");
            return string.Empty;
        }

        return spriteValueType switch
        {
            "Width" => sprite.Width.ToString(CultureInfo.InvariantCulture),
            "Height" => sprite.Height.ToString(CultureInfo.InvariantCulture),
            "LeftWidth" => (sprite.LeftWidth ?? 0).ToString(CultureInfo.InvariantCulture),
            "RightWidth" => (sprite.RightWidth ?? 0).ToString(CultureInfo.InvariantCulture),
            "TopHeight" => (sprite.TopHeight ?? 0).ToString(CultureInfo.InvariantCulture),
            "BottomHeight" => (sprite.BottomHeight ?? 0).ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private string ApplyNumericTransform(
        string currentValue,
        string? multiplyValue,
        string? additiveValue,
        IReadOnlyDictionary<string, string> parameters,
        IDictionary<string, string> resolvedConstants,
        IReadOnlyDictionary<string, XElement> constantElements,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> resolutionStack)
    {
        if (!double.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return currentValue;
        }

        if (!string.IsNullOrWhiteSpace(multiplyValue))
        {
            var resolvedMultiplier = ResolveScopedValue(
                multiplyValue,
                parameters,
                resolvedConstants,
                constantElements,
                diagnostics,
                resolutionStack);
            if (double.TryParse(resolvedMultiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
            {
                result *= multiplier;
            }
        }

        if (!string.IsNullOrWhiteSpace(additiveValue))
        {
            var resolvedAdditive = ResolveScopedValue(
                additiveValue,
                parameters,
                resolvedConstants,
                constantElements,
                diagnostics,
                resolutionStack);
            if (double.TryParse(resolvedAdditive, NumberStyles.Float, CultureInfo.InvariantCulture, out var additive))
            {
                result += additive;
            }
        }

        return result.ToString(CultureInfo.InvariantCulture);
    }

    private XElement ExpandElement(
        XElement element,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> constants,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> expansionStack,
        out bool logicalChildrenConsumed)
    {
        var expandedReference = TryExpandPrefabReference(
            element,
            parameters,
            constants,
            diagnostics,
            expansionStack,
            out logicalChildrenConsumed);
        if (expandedReference is not null)
        {
            return expandedReference;
        }

        logicalChildrenConsumed = false;
        var clone = new XElement(element.Name);
        foreach (var attribute in element.Attributes())
        {
            clone.SetAttributeValue(attribute.Name, ResolveSimpleScopedValue(attribute.Value, parameters, constants));
        }

        foreach (var node in element.Nodes())
        {
            if (node is not XElement childElement)
            {
                clone.Add(node);
                continue;
            }

            if (string.Equals(childElement.Name.LocalName, "LogicalChildrenLocation", StringComparison.OrdinalIgnoreCase))
            {
                clone.Add(new XElement(childElement));
                continue;
            }

            var expandedChild = ExpandElement(
                childElement,
                parameters,
                constants,
                diagnostics,
                expansionStack,
                out var childConsumed);
            logicalChildrenConsumed |= childConsumed;
            clone.Add(expandedChild);
        }

        return clone;
    }

    private XElement? TryExpandPrefabReference(
        XElement element,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> constants,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> expansionStack,
        out bool logicalChildrenConsumed)
    {
        logicalChildrenConsumed = false;

        var sourcePath = expansionStack.LastOrDefault();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var resolution = _prefabReferenceResolver.Resolve(sourcePath, element.Name.LocalName);
        if (!resolution.IsResolved)
        {
            return null;
        }

        if (expansionStack.Any(path => string.Equals(path, resolution.ResolvedPath, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add($"Prefab include cycle detected while expanding '{element.Name.LocalName}' from '{sourcePath}'.");
            return null;
        }

        if (resolution.Kind == PrefabReferenceResolveKind.Heuristic &&
            resolution.CandidatePaths.Count > 1)
        {
            diagnostics.Add(
                $"Prefab reference '{element.Name.LocalName}' in '{sourcePath}' matched multiple files. Using '{resolution.ResolvedPath}'.");
        }

        var nextStack = expansionStack.Append(resolution.ResolvedPath!).ToArray();
        var includeState = LoadPrefabState(
            resolution.ResolvedPath!,
            diagnostics,
            nextStack,
            includeRootOnly: true);
        var includeParameters = new Dictionary<string, string>(includeState.DefaultParameters, StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes())
        {
            if (!attribute.Name.LocalName.StartsWith("Parameter.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            includeParameters[attribute.Name.LocalName["Parameter.".Length..]] =
                ResolveSimpleScopedValue(attribute.Value, parameters, constants);
        }

        var includeConstants = ParseConstants(
            includeState.ConstantsElement,
            includeParameters,
            diagnostics,
            reportMissingConstants: false);
        var callerChildren = ExpandCallerChildren(
            element,
            parameters,
            constants,
            diagnostics,
            expansionStack);
        var expandedRoot = ExpandElement(
            includeState.RootNodeElement,
            includeParameters,
            includeConstants,
            diagnostics,
            nextStack,
            out logicalChildrenConsumed);

        if (callerChildren.Count > 0)
        {
            logicalChildrenConsumed = TryInsertLogicalChildrenAtLogicalTarget(expandedRoot, callerChildren);
            if (!logicalChildrenConsumed)
            {
                InsertLogicalChildrenIntoDefaultLocation(expandedRoot, callerChildren);
                logicalChildrenConsumed = true;
            }
        }

        MergeCallerAttributes(expandedRoot, element, parameters, constants);
        return expandedRoot;
    }

    private static bool TryInsertLogicalChildrenAtLogicalTarget(
        XElement expandedRoot,
        IReadOnlyList<XElement> logicalChildren)
    {
        var logicalTarget = FindLogicalChildrenTarget(expandedRoot);
        if (logicalTarget is null)
        {
            return false;
        }

        logicalTarget.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "LogicalChildrenLocation", StringComparison.OrdinalIgnoreCase))
            .Remove();

        InsertLogicalChildrenIntoDefaultLocation(logicalTarget, logicalChildren);
        return true;
    }

    private static XElement? FindLogicalChildrenTarget(XElement root)
    {
        foreach (var child in GetVisualChildElements(root))
        {
            if (HasDirectLogicalChildrenLocation(child))
            {
                return child;
            }
        }

        foreach (var child in GetVisualChildElements(root))
        {
            var nestedTarget = FindLogicalChildrenTarget(child);
            if (nestedTarget is not null)
            {
                return nestedTarget;
            }
        }

        return null;
    }

    private static bool HasDirectLogicalChildrenLocation(XElement element)
    {
        return element.Elements()
            .Any(child => string.Equals(child.Name.LocalName, "LogicalChildrenLocation", StringComparison.OrdinalIgnoreCase));
    }

    private static void InsertLogicalChildrenIntoDefaultLocation(
        XElement targetElement,
        IReadOnlyList<XElement> logicalChildren)
    {
        if (logicalChildren.Count == 0)
        {
            return;
        }

        if (string.Equals(targetElement.Name.LocalName, "Window", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var logicalChild in logicalChildren)
            {
                targetElement.Add(new XElement(logicalChild));
            }

            return;
        }

        var childrenElement = targetElement.Element("Children");
        if (childrenElement is null)
        {
            childrenElement = new XElement("Children");
            targetElement.Add(childrenElement);
        }

        foreach (var logicalChild in logicalChildren)
        {
            childrenElement.Add(new XElement(logicalChild));
        }
    }

    private List<XElement> ExpandCallerChildren(
        XElement callerElement,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> constants,
        PreviewDiagnostics diagnostics,
        IReadOnlyCollection<string> expansionStack)
    {
        var callerChildren = new List<XElement>();
        var childrenElement = callerElement.Element("Children");
        if (childrenElement is not null)
        {
            foreach (var child in childrenElement.Elements())
            {
                callerChildren.Add(
                    ExpandElement(child, parameters, constants, diagnostics, expansionStack, out _));
            }
        }

        return callerChildren;
    }

    private static void MergeCallerAttributes(
        XElement targetRoot,
        XElement callerElement,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> constants)
    {
        foreach (var callerAttribute in callerElement.Attributes())
        {
            if (callerAttribute.Name.LocalName.StartsWith("Parameter.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            targetRoot.SetAttributeValue(
                callerAttribute.Name,
                ResolveSimpleScopedValue(callerAttribute.Value, parameters, constants));
        }
    }

    private static string ResolveSimpleScopedValue(
        string rawValue,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> constants)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        var value = rawValue.Trim();
        if (value[0] == '*' && value.Length > 1)
        {
            return parameters.TryGetValue(value[1..], out var parameterValue)
                ? parameterValue
                : rawValue;
        }

        if (value[0] == '!' && value.Length > 1)
        {
            return constants.TryGetValue(value[1..], out var constantValue)
                ? constantValue
                : rawValue;
        }

        return rawValue;
    }

    private PrefabNode ParseNode(XElement element, HashSet<string> bindingKeys)
    {
        var attributes = element.Attributes()
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var value in attributes.Values)
        {
            CollectBindingKey(value, bindingKeys);
        }

        var children = new List<PrefabNode>();
        foreach (var child in GetParsedChildElements(element))
        {
            children.Add(ParseNode(child, bindingKeys));
        }

        return _widgetNodeFactory.CreateNode(element.Name.LocalName, attributes, children);
    }

    private static IEnumerable<XElement> GetParsedChildElements(XElement element)
    {
        if (string.Equals(element.Name.LocalName, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return GetVisualChildElements(element);
        }

        return element.Element("Children")?.Elements() ?? [];
    }

    private static IEnumerable<XElement> GetVisualChildElements(XElement element)
    {
        if (string.Equals(element.Name.LocalName, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return element.Elements()
                .Where(child => !MetadataElementNames.Contains(child.Name.LocalName, StringComparer.OrdinalIgnoreCase));
        }

        return element.Element("Children")?.Elements() ?? [];
    }

    private static XElement GetIncludeRootElement(XElement rootWindow, string path)
    {
        if (!string.Equals(rootWindow.Name.LocalName, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return new XElement(rootWindow);
        }

        var visualElements = rootWindow.Elements()
            .Where(element => !MetadataElementNames.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return visualElements.Length switch
        {
            1 => new XElement(visualElements[0]),
            0 => throw new InvalidOperationException($"The prefab '{path}' does not contain a visual root inside <Window>."),
            _ => throw new InvalidOperationException($"The prefab '{path}' contains multiple visual roots inside <Window>.")
        };
    }

    private static bool ResolveBooleanValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue != 0;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "yes" or "on" or "visible" or "enabled" => true,
            _ => false
        };
    }

    private static void CollectBindingKey(string? rawValue, HashSet<string> bindingKeys)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue[0] != '@' || rawValue.Length < 2)
        {
            return;
        }

        bindingKeys.Add(rawValue[1..]);
    }

    private static int CollectNodeStatistics(
        PrefabNode node,
        IDictionary<string, int> nodeTypeCounts,
        int depth)
    {
        if (nodeTypeCounts.TryGetValue(node.Name, out var existingCount))
        {
            nodeTypeCounts[node.Name] = existingCount + 1;
        }
        else
        {
            nodeTypeCounts[node.Name] = 1;
        }

        var maxDepth = depth;
        foreach (var child in node.Children)
        {
            maxDepth = Math.Max(maxDepth, CollectNodeStatistics(child, nodeTypeCounts, depth + 1));
        }

        return maxDepth;
    }

    private sealed record PrefabExpansionState(
        string SourcePath,
        XElement RootNodeElement,
        IReadOnlyDictionary<string, string> DefaultParameters,
        XElement? ConstantsElement,
        IReadOnlyCollection<string> ExpansionStack);
}
