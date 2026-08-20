using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Models;
using System.Globalization;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Services;

public sealed class WidgetReferenceResolver : IWidgetReferenceResolver
{
    private const string DebugAttributePrefix = "Debug.WidgetRef.";

    public void Resolve(XElement rootElement, PreviewDiagnostics diagnostics)
    {
        foreach (var element in rootElement.DescendantsAndSelf())
        {
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
                    var resolvedTargetPath = BuildCanonicalElementPath(targetElement);
                    element.SetAttributeValue(
                        $"{DebugAttributePrefix}{attribute.Name.LocalName}",
                        resolvedTargetPath);
                    diagnostics.AddResolvedWidgetReference(
                        BuildCanonicalElementPath(element),
                        attribute.Name.LocalName,
                        attribute.Value,
                        resolvedTargetPath);
                    continue;
                }

                diagnostics.AddUnresolvedWidgetReference();
                diagnostics.Add(
                    $"Widget reference '{attribute.Name.LocalName}=\"{attribute.Value}\"' on '{DescribeElement(element)}' could not be resolved: {failureReason}");
            }
        }
    }

    private static bool IsReferenceCandidate(string attributeName)
    {
        if (attributeName.StartsWith(DebugAttributePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return attributeName.Contains("Widget", StringComparison.OrdinalIgnoreCase) ||
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
                failureReason = $"segment '{segment}' was not found under '{DescribeElement(targetElement)}'";
                return false;
            }

            if (childMatches.Length > 1)
            {
                failureReason = $"segment '{segment}' is ambiguous under '{DescribeElement(targetElement)}'";
                return false;
            }

            targetElement = childMatches[0];
        }

        return true;
    }

    private static XElement? GetVisualParent(XElement element)
    {
        var parent = element.Parent as XElement;
        if (parent is null)
        {
            return null;
        }

        return string.Equals(parent.Name.LocalName, "Children", StringComparison.OrdinalIgnoreCase)
            ? parent.Parent
            : parent;
    }

    private static IEnumerable<XElement> GetVisualChildren(XElement element)
    {
        var childrenElement = element.Element("Children");
        if (childrenElement is not null)
        {
            foreach (var child in childrenElement.Elements())
            {
                yield return child;
            }
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName is "Children" or "Constants")
            {
                continue;
            }

            yield return child;
        }
    }

    private static string BuildCanonicalElementPath(XElement element)
    {
        var segments = new Stack<string>();
        XElement? current = element;
        while (current is not null)
        {
            segments.Push(DescribeElement(current));
            current = GetVisualParent(current);
        }

        return string.Join(" > ", segments);
    }

    private static string DescribeElement(XElement element)
    {
        var id = element.Attribute("Id")?.Value;
        return string.IsNullOrWhiteSpace(id)
            ? element.Name.LocalName
            : $"{element.Name.LocalName}[Id={id}]";
    }
}
