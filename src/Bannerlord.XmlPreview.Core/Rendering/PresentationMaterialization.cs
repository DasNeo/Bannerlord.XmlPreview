using System.Windows;
using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Rendering;

public sealed partial class PrefabRenderContext
{
    private sealed record BrushLayerSelection(string Brush, BrushLayerDefinition? Source, bool All, bool CopyStyle);

    private IReadOnlyDictionary<string, string> PreparePresentationAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        _brushLayerSelection = null;
        if (!attributes.ContainsKey("Preview.SpriteVariant") && !attributes.ContainsKey("Preview.BrushTargetLayers"))
            return attributes;
        var result = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
        if (result.TryGetValue("Preview.SpriteVariant", out var variants) && result.TryGetValue("Sprite", out var sprite))
            foreach (var variant in variants.Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (_assetCatalog.FindSprite(sprite + variant) is not null) { result["Sprite"] = sprite + variant; break; }
        if (!result.TryGetValue("Preview.BrushTargetLayers", out var target)) return result;

        var sourceName = result.GetValueOrDefault("Preview.BrushSource", "");
        var layerName = result.GetValueOrDefault("Preview.BrushSourceLayer", "");
        var source = FindBrush(sourceName)?.Layers.FirstOrDefault(l => l.Name == layerName);
        _brushLayerSelection = new(ResolveBinding(result.GetValueOrDefault("Brush", "")), source, target == "All",
            result.GetValueOrDefault("Preview.BrushCopyStyle") == "true");
        if (target == "Default") result["IsVisible"] = string.IsNullOrEmpty(source?.SpriteName) ? "false" : "true";
        if (source is null && sourceName.Length > 0 && layerName.Length > 0)
        {
            var message = $"{TryGetWidgetPath(attributes)}: brush.layer.missing '{sourceName}/{layerName}'.";
            _diagnostics.Add(message);
            _diagnostics.MarkTruncated(message);
        }
        if (result.GetValueOrDefault("Preview.BrushUseSize") == "true" && _assetCatalog.FindSprite(source?.SpriteName) is { } definition)
        {
            result["SuggestedWidth"] = definition.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["SuggestedHeight"] = definition.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return result;
    }

    public void ApplySpriteExtensions(FrameworkElement visual, IReadOnlyDictionary<string, string> attributes)
    {
        // Extensions change only the painted area; the widget retains its declared layout size.
        visual.Margin = new Thickness(-ResolveDouble(attributes, "ExtendLeft", 0), -ResolveDouble(attributes, "ExtendTop", 0),
            -ResolveDouble(attributes, "ExtendRight", 0), -ResolveDouble(attributes, "ExtendBottom", 0));
    }
}
