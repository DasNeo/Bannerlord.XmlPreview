using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public sealed class PrefabLoader : IPrefabLoader
{
    private const int MaxLoadAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);
    private readonly IBannerlordAssetCatalog _assetCatalog;

    private static readonly Dictionary<(string BrushName, string ValueType), string> BrushMetricFallbacks =
        new()
        {
            [("Encyclopedia.Frame", "Width")] = "1500",
            [("Encyclopedia.Frame", "Height")] = "1000"
        };

    public PrefabLoader(IBannerlordAssetCatalog assetCatalog)
    {
        _assetCatalog = assetCatalog;
    }

    public PrefabDocument Load(string path, PreviewDiagnostics diagnostics)
    {
        var document = LoadDocumentWithRetry(path, diagnostics);
        var root = document.Root ?? throw new InvalidOperationException("The prefab XML is missing a root element.");

        var bindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var constants = ParseConstants(root.Element("Constants"), diagnostics);
        var rootNode = ParseNode(root.Element("Window") ?? root, bindingKeys);

        return new PrefabDocument(path, rootNode, constants, bindingKeys);
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

    private Dictionary<string, string> ParseConstants(XElement? constantsElement, PreviewDiagnostics diagnostics)
    {
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (constantsElement is null)
        {
            diagnostics.Add("No <Constants> section found. Using generic preview defaults.");
            return constants;
        }

        foreach (var constantElement in constantsElement.Elements("Constant"))
        {
            var name = constantElement.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var value = constantElement.Attribute("Value")?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                constants[name] = value;
                continue;
            }

            var brushName = constantElement.Attribute("BrushName")?.Value;
            var brushLayerName = constantElement.Attribute("BrushLayer")?.Value;
            var valueType = constantElement.Attribute("BrushValueType")?.Value;
            var resolvedMetric = _assetCatalog.ResolveBrushMetric(brushName, brushLayerName, valueType ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(brushName) &&
                !string.IsNullOrWhiteSpace(valueType) &&
                resolvedMetric.HasValue)
            {
                constants[name] = resolvedMetric.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                diagnostics.AddResolvedConstant(
                    name,
                    brushName,
                    brushLayerName ?? "Default",
                    valueType,
                    resolvedMetric.Value);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(brushName) &&
                !string.IsNullOrWhiteSpace(valueType) &&
                BrushMetricFallbacks.TryGetValue((brushName, valueType), out var fallback))
            {
                constants[name] = fallback;
                diagnostics.Add(
                    $"Constant '{name}' uses Bannerlord brush metrics. Preview substituted {fallback} for {brushName}.{valueType}.");
                continue;
            }

            diagnostics.Add($"Constant '{name}' could not be resolved and was skipped.");
        }

        return constants;
    }

    private static PrefabNode ParseNode(XElement element, HashSet<string> bindingKeys)
    {
        var attributes = element.Attributes()
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var value in attributes.Values)
        {
            CollectBindingKey(value, bindingKeys);
        }

        var children = new List<PrefabNode>();
        var childrenElement = element.Element("Children");

        if (childrenElement is not null)
        {
            foreach (var child in childrenElement.Elements())
            {
                children.Add(ParseNode(child, bindingKeys));
            }
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName is "Children" or "Constants")
            {
                continue;
            }

            children.Add(ParseNode(child, bindingKeys));
        }

        return CreateNode(element.Name.LocalName, attributes, children);
    }

    private static PrefabNode CreateNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        return name switch
        {
            "Window" => new WindowNode(attributes, children),
            "RichTextWidget" => new RichTextWidgetNode(attributes, children),
            "TextWidget" => new TextWidgetNode(attributes, children),
            "ButtonWidget" => new ButtonWidgetNode(name, attributes, children),
            "Standard.PopupCloseButton" => new ButtonWidgetNode(name, attributes, children),
            "EscapeMenuButtonWidget" => new EscapeMenuButtonWidgetNode(attributes, children),
            "ListPanel" => new ListPanelNode(attributes, children),
            "NavigatableListPanel" => new NavigatableListPanelNode(attributes, children),
            "NavigationScopeTargeter" => new NavigationScopeTargeterNode(attributes, children),
            "ItemTemplate" => new ItemTemplateNode(attributes, children),
            "HintWidget" => new HintWidgetNode(attributes, children),
            "Widget" => new WidgetNode(attributes, children),
            "BrushWidget" => new BrushWidgetNode(attributes, children),
            _ => new FallbackNode(name, attributes, children)
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
}
