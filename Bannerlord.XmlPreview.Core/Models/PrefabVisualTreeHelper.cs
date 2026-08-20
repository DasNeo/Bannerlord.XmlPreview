using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Models;

public static class PrefabVisualTreeHelper
{
    public const string DebugWidgetPathAttributeName = "Debug.WidgetPath";

    private static readonly string[] MetadataElementNames =
    [
        "Children",
        "Constants",
        "Parameters",
        "Variables",
        "VisualDefinitions",
        "LogicalChildrenLocation"
    ];

    public static void AssignCanonicalPaths(XElement rootElement)
    {
        AssignCanonicalPaths(rootElement, null);
    }

    public static string GetCanonicalElementPath(XElement element)
    {
        if (element.Attribute(DebugWidgetPathAttributeName)?.Value is { Length: > 0 } assignedPath)
        {
            return assignedPath;
        }

        var segments = new Stack<string>();
        XElement? current = element;
        while (current is not null)
        {
            segments.Push(DescribeElementForPath(current));
            current = GetVisualParent(current);
        }

        return string.Join(" > ", segments);
    }

    public static XElement? GetVisualParent(XElement element)
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

    public static IEnumerable<XElement> GetVisualChildren(XElement element)
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
            if (MetadataElementNames.Contains(child.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return child;
        }
    }

    public static string DescribeElement(XElement element)
    {
        var id = element.Attribute("Id")?.Value;
        return string.IsNullOrWhiteSpace(id)
            ? element.Name.LocalName
            : $"{element.Name.LocalName}[Id={id}]";
    }

    private static void AssignCanonicalPaths(XElement element, string? parentPath)
    {
        var segment = DescribeElementForPath(element);
        var currentPath = string.IsNullOrWhiteSpace(parentPath)
            ? segment
            : $"{parentPath} > {segment}";
        element.SetAttributeValue(DebugWidgetPathAttributeName, currentPath);

        foreach (var child in GetVisualChildren(element))
        {
            AssignCanonicalPaths(child, currentPath);
        }
    }

    private static string DescribeElementForPath(XElement element)
    {
        var baseLabel = DescribeElement(element);
        var parent = GetVisualParent(element);
        if (parent is null)
        {
            return baseLabel;
        }

        var similarSiblings = GetVisualChildren(parent)
            .Where(sibling => string.Equals(DescribeElement(sibling), baseLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (similarSiblings.Length <= 1)
        {
            return baseLabel;
        }

        var ordinal = 1;
        foreach (var sibling in similarSiblings)
        {
            if (ReferenceEquals(sibling, element))
            {
                break;
            }

            ordinal++;
        }

        return $"{baseLabel}#{ordinal}";
    }
}
