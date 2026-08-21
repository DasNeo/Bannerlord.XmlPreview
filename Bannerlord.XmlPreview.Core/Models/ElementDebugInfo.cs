using System.Globalization;
using System.Text;
using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Models;

public sealed class ElementDebugInfo
{
    public ElementDebugInfo(string nodeName)
    {
        NodeName = nodeName;
    }

    public string NodeName { get; }

    public IList<ElementDebugAttribute> Attributes { get; } = [];

    public IList<ElementDebugResolvedProperty> ResolvedProperties { get; } = [];

    public IList<ElementDebugWidgetReference> WidgetReferences { get; } = [];

    public IList<ElementDebugBrush> Brushes { get; } = [];

    public IList<ElementDebugSprite> Sprites { get; } = [];

    public ElementDebugSection? Layout { get; set; }

    public ElementDebugSection? ActualLayout { get; set; }

    public string? GetAttributeDisplayValue(string attributeName)
    {
        return Attributes
            .FirstOrDefault(attribute => string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
            ?.DisplayValue;
    }

    public override string ToString()
    {
        var lines = new List<string> { $"Node: {NodeName}" };

        if (Attributes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Attributes");
            foreach (var attribute in Attributes)
            {
                lines.Add(attribute.ToDisplayString());
            }
        }

        AppendSection(lines, "ResolvedProperties", ResolvedProperties.Select(property => property.ToDisplayString()));
        AppendSection(lines, "WidgetReferences", WidgetReferences.Select(reference => reference.ToDisplayString()));
        AppendSection(lines, "Brushes", Brushes.SelectMany(brush => brush.ToDisplayLines()));
        AppendSection(lines, "Sprites", Sprites.SelectMany(sprite => sprite.ToDisplayLines()));

        if (Layout is not null)
        {
            AppendSection(lines, Layout.Name, Layout.Fields.Select(field => field.ToDisplayString()));
        }

        if (ActualLayout is not null)
        {
            AppendSection(lines, ActualLayout.Name, ActualLayout.Fields.Select(field => field.ToDisplayString()));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendSection(ICollection<string> lines, string sectionName, IEnumerable<string> sectionLines)
    {
        var materializedLines = sectionLines.ToList();
        if (materializedLines.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(sectionName);
        foreach (var line in materializedLines)
        {
            lines.Add(line);
        }
    }
}

public sealed record ElementDebugAttribute(string Name, string RawValue, string? ResolvedValue = null)
{
    public string DisplayValue => string.Equals(RawValue, ResolvedValue, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(ResolvedValue)
        ? RawValue
        : $"{RawValue} -> {ResolvedValue}";

    public string ToDisplayString()
    {
        return $"{Name}: {DisplayValue}";
    }
}

public sealed record ElementDebugResolvedProperty(ResolvedWidgetProperty Property)
{
    public string ToDisplayString()
    {
        return $"{Property.AttributeName}: {Property.DeclaredTypeName} [{Property.ValueKind}] = {Property.GetDisplayValue()}";
    }
}

public sealed record ElementDebugWidgetReference(ResolvedWidgetReference Reference)
{
    public string ToDisplayString()
    {
        return Reference.IsResolved
            ? $"{Reference.AttributeName}: {Reference.RawValue} -> {Reference.TargetWidgetPath}"
            : $"{Reference.AttributeName}: {Reference.RawValue} (unresolved: {Reference.FailureReason})";
    }
}

public sealed record ElementDebugField(string Name, string Value)
{
    public string ToDisplayString()
    {
        return $"{Name}: {Value}";
    }
}

public sealed class ElementDebugSection
{
    public ElementDebugSection(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IList<ElementDebugField> Fields { get; } = [];

    public string? GetValue(string fieldName)
    {
        return Fields
            .FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }
}

public sealed record ElementDebugBrushLayer(
    string Name,
    string? SpriteName,
    string? OverlaySpriteName,
    string? OverlayMethod,
    double? AlphaFactor,
    double? ExtendLeft,
    double? ExtendTop,
    double? ExtendRight,
    double? ExtendBottom,
    double MaxExtend,
    bool HorizontalFlip,
    bool VerticalFlip)
{
    public string ToDisplayString()
    {
        var layerBuilder = new StringBuilder($"  Layer {Name}:");
        if (!string.IsNullOrWhiteSpace(SpriteName))
        {
            layerBuilder.Append($" Sprite={SpriteName}");
        }

        if (!string.IsNullOrWhiteSpace(OverlaySpriteName))
        {
            layerBuilder.Append($" Overlay={OverlaySpriteName}");
        }

        if (!string.IsNullOrWhiteSpace(OverlayMethod))
        {
            layerBuilder.Append($" OverlayMethod={OverlayMethod}");
        }

        if (AlphaFactor.HasValue)
        {
            layerBuilder.Append($" Alpha={AlphaFactor.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (MaxExtend > 0)
        {
            layerBuilder.Append(
                $" Extend=({(ExtendLeft ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                $"{(ExtendTop ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                $"{(ExtendRight ?? 0).ToString(CultureInfo.InvariantCulture)}," +
                $"{(ExtendBottom ?? 0).ToString(CultureInfo.InvariantCulture)})");
        }

        if (HorizontalFlip || VerticalFlip)
        {
            layerBuilder.Append($" Flip=({(HorizontalFlip ? "H" : "-")},{(VerticalFlip ? "V" : "-")})");
        }

        return layerBuilder.ToString();
    }
}

public sealed record ElementDebugBrush(
    string RequestedName,
    string? SourcePath,
    bool IsMissing,
    bool IsPreviewFallback,
    IReadOnlyList<ElementDebugBrushLayer> Layers)
{
    public IEnumerable<string> ToDisplayLines()
    {
        if (IsMissing)
        {
            yield return IsPreviewFallback
                ? $"Brush: {RequestedName} (preview fallback)"
                : $"Brush: {RequestedName} (missing)";
            yield break;
        }

        yield return $"Brush: {RequestedName}";
        if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            yield return $"  Source: {SourcePath}";
        }

        yield return $"  LayerCount: {Layers.Count}";
        foreach (var layer in Layers)
        {
            yield return layer.ToDisplayString();
        }
    }
}

public sealed record ElementDebugSprite(
    string RequestedName,
    string Usage,
    SpriteAssetResolutionKind Status,
    string? ImageKey,
    string? ImagePath,
    SpriteDefinition? SpriteDefinition,
    string? Note)
{
    public IEnumerable<string> ToDisplayLines()
    {
        yield return $"Sprite: {RequestedName} [{Usage}]";
        yield return $"  Status: {Status}";

        if (!string.IsNullOrWhiteSpace(ImageKey))
        {
            yield return $"  ImageKey: {ImageKey}";
        }

        if (!string.IsNullOrWhiteSpace(ImagePath))
        {
            yield return $"  ImagePath: {ImagePath}";
        }

        if (SpriteDefinition is not null)
        {
            yield return
                $"  Catalog: {SpriteDefinition.Name} ({SpriteDefinition.Kind}, {SpriteDefinition.Width}x{SpriteDefinition.Height}, {SpriteDefinition.CategoryName}, Sheet {SpriteDefinition.SheetId})";
            yield return $"  SpritePart: {SpriteDefinition.SpritePartName}";
            yield return $"  Source: {SpriteDefinition.SourcePath}";
        }

        if (!string.IsNullOrWhiteSpace(Note))
        {
            yield return $"  Note: {Note}";
        }
    }
}

public sealed record ElementLayoutSnapshot(
    string VisualPath,
    string Label,
    string TypeName,
    ElementDebugSection ActualLayout)
{
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"[{VisualPath}] {Label}",
            $"Type: {TypeName}"
        };

        foreach (var field in ActualLayout.Fields)
        {
            lines.Add(field.ToDisplayString());
        }

        return string.Join(Environment.NewLine, lines);
    }
}
