using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models;
using System.Globalization;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Services;

public sealed class WidgetReferenceResolver : IWidgetReferenceResolver
{
    private const string DebugAttributeNamespacePrefix = "Debug.";
    private const string DebugAttributePrefix = "Debug.WidgetRef.";

    public PrefabWidgetReferenceMap Resolve(XElement rootElement, PreviewDiagnostics diagnostics)
    {
        using var timing = diagnostics.MeasurePhase("WidgetReferenceResolver.Resolve.Total");
        var referencesByWidgetPath = new Dictionary<string, IReadOnlyDictionary<string, ResolvedWidgetReference>>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in rootElement.DescendantsAndSelf())
        {
            var widgetPath = PrefabVisualTreeHelper.GetCanonicalElementPath(element);
            var referencesForElement = new Dictionary<string, ResolvedWidgetReference>(StringComparer.OrdinalIgnoreCase);
            var attributes = element.Attributes().ToArray();
            foreach (var attribute in attributes)
            {
                if (!IsReferenceCandidate(attribute.Name.LocalName) ||
                    !TryParseReference(attribute.Value, out var segments))
                {
                    continue;
                }

                if (TryResolveTarget(element, segments, out var targetElement, out var failureReason))
                {
                    var resolvedTargetPath = PrefabVisualTreeHelper.GetCanonicalElementPath(targetElement);
                    element.SetAttributeValue(
                        $"{DebugAttributePrefix}{attribute.Name.LocalName}",
                        resolvedTargetPath);
                    diagnostics.AddResolvedWidgetReference(
                        widgetPath,
                        attribute.Name.LocalName,
                        attribute.Value,
                        resolvedTargetPath);
                    referencesForElement[attribute.Name.LocalName] = new ResolvedWidgetReference(
                        widgetPath,
                        attribute.Name.LocalName,
                        attribute.Value,
                        resolvedTargetPath,
                        IsResolved: true,
                        FailureReason: null);
                    continue;
                }

                diagnostics.AddUnresolvedWidgetReference();
                referencesForElement[attribute.Name.LocalName] = new ResolvedWidgetReference(
                    widgetPath,
                    attribute.Name.LocalName,
                    attribute.Value,
                    TargetWidgetPath: null,
                    IsResolved: false,
                    FailureReason: failureReason);
                diagnostics.Add(
                    $"Widget reference '{attribute.Name.LocalName}=\"{attribute.Value}\"' on '{PrefabVisualTreeHelper.DescribeElement(element)}' could not be resolved: {failureReason}");
            }

            if (referencesForElement.Count > 0)
            {
                referencesByWidgetPath[widgetPath] = referencesForElement;
            }
        }

        return referencesByWidgetPath.Count == 0
            ? PrefabWidgetReferenceMap.Empty
            : new PrefabWidgetReferenceMap(referencesByWidgetPath);
    }

    public PrefabWidgetReferenceMap Resolve(Bannerlord.XmlPreview.Models.Nodes.PrefabNode root, PreviewDiagnostics diagnostics)
    {
        static XElement Xml(Bannerlord.XmlPreview.Models.Nodes.PrefabNode node) => new(node.Name,
            node.Attributes.Select(p => new XAttribute(p.Key, p.Value)), node.Children.Select(Xml));
        return Resolve(Xml(root), diagnostics);
    }

    private static bool IsReferenceCandidate(string attributeName)
    {
        if (attributeName.StartsWith(DebugAttributeNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
            attributeName.StartsWith(DebugAttributePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return attributeName is "ClipRect" or "Handle" or "Filler" or "Button" ||
               attributeName.EndsWith("Scrollbar", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Contains("Widget", StringComparison.OrdinalIgnoreCase) ||
               attributeName.EndsWith("Panel", StringComparison.OrdinalIgnoreCase) ||
               attributeName.EndsWith("Container", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseReference(string rawValue, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        if (value[0] is '@' or '!' ||
            bool.TryParse(value, out _) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        if (value.IndexOfAny(['{', '}', '*']) >= 0)
        {
            return false;
        }

        var parsedSegments = value
            .Split(['\\', '/'], StringSplitOptions.None)
            .Select(segment => segment.Trim())
            .ToArray();
        if (parsedSegments.Length == 0 ||
            parsedSegments.Any(string.IsNullOrWhiteSpace) ||
            parsedSegments.Any(segment => !IsValidReferenceSegment(segment)))
        {
            return false;
        }

        segments = parsedSegments;
        return true;
    }

    private static bool IsValidReferenceSegment(string segment)
    {
        return segment is "." or ".." ||
               (segment.IndexOfAny(['@', '!', '{', '}', ':']) < 0);
    }

    private static bool TryResolveTarget(
        XElement originElement,
        IReadOnlyList<string> segments,
        out XElement targetElement,
        out string failureReason)
    {
        targetElement = originElement;
        failureReason = string.Empty;

        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                var parentElement = GetVisualParent(targetElement);
                if (parentElement is null)
                {
                    failureReason = "the path navigates above the visual root";
                    return false;
                }

                targetElement = parentElement;
                continue;
            }

            var childMatches = GetVisualChildren(targetElement)
                .Where(child => string.Equals(child.Attribute("Id")?.Value, segment, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (childMatches.Length == 0)
            {
                failureReason = $"segment '{segment}' was not found under '{PrefabVisualTreeHelper.DescribeElement(targetElement)}'";
                return false;
            }

            if (childMatches.Length > 1)
            {
                failureReason = $"segment '{segment}' is ambiguous under '{PrefabVisualTreeHelper.DescribeElement(targetElement)}'";
                return false;
            }

            targetElement = childMatches[0];
        }

        return true;
    }

    private static XElement? GetVisualParent(XElement element)
    {
        return PrefabVisualTreeHelper.GetVisualParent(element);
    }

    private static IEnumerable<XElement> GetVisualChildren(XElement element)
    {
        return PrefabVisualTreeHelper.GetVisualChildren(element);
    }
}
