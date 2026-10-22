using System.IO;
using System.Globalization;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Models.Assets;
namespace Bannerlord.XmlPreview.Assets;
internal static class BannerlordAssetXml
{
    public static void LoadSpriteData(
        string spriteDataPath,
        Dictionary<string, SpritePartDefinition> spriteParts,
        Dictionary<string, SpriteDefinition> sprites)
    {
        var document = XDocument.Load(spriteDataPath, LoadOptions.None);

        var root = document.Root;
        if (root is null)
        {
            return;
        }

        foreach (var spritePartElement in root.Descendants("SpritePart"))
        {
            var name = spritePartElement.Element("Name")?.Value;
            var categoryName = spritePartElement.Element("CategoryName")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(categoryName))
            {
                continue;
            }

            var part = new SpritePartDefinition(
                name,
                spriteDataPath,
                categoryName,
                ParseInt(spritePartElement.Element("SheetID")?.Value),
                ParseInt(spritePartElement.Element("SheetX")?.Value),
                ParseInt(spritePartElement.Element("SheetY")?.Value),
                ParseInt(spritePartElement.Element("Width")?.Value),
                ParseInt(spritePartElement.Element("Height")?.Value));

            spriteParts[name] = part;
            sprites[name] = new SpriteDefinition(
                part.Name,
                spriteDataPath,
                "SpritePart",
                part.Name,
                part.CategoryName,
                part.SheetId,
                part.Width,
                part.Height,
                null,
                null,
                null,
                null) { SheetX = part.SheetX, SheetY = part.SheetY };
        }

        var spritesElement = root.Element("Sprites");
        if (spritesElement is null)
        {
            return;
        }

        foreach (var spriteElement in spritesElement.Elements())
        {
            var name = spriteElement.Element("Name")?.Value;
            var spritePartName = spriteElement.Element("SpritePartName")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(spritePartName))
            {
                continue;
            }

            if (!spriteParts.TryGetValue(spritePartName, out var spritePart))
            {
                continue;
            }

            sprites[name] = new SpriteDefinition(
                name,
                spriteDataPath,
                spriteElement.Name.LocalName,
                spritePartName,
                spritePart.CategoryName,
                spritePart.SheetId,
                spritePart.Width,
                spritePart.Height,
                ParseNullableInt(spriteElement.Element("LeftWidth")?.Value),
                ParseNullableInt(spriteElement.Element("RightWidth")?.Value),
                ParseNullableInt(spriteElement.Element("TopHeight")?.Value),
                ParseNullableInt(spriteElement.Element("BottomHeight")?.Value)) { SheetX = spritePart.SheetX, SheetY = spritePart.SheetY };
        }
    }

    public static void LoadBrushData(string brushPath, Dictionary<string, BrushDefinition> brushes)
    {
        var document = XDocument.Load(brushPath, LoadOptions.None);

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Brushes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var brushElement in root.Elements("Brush"))
        {
            var name = brushElement.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var layers = brushElement.Element("Layers")?
                .Elements("BrushLayer")
                .Select(ParseBrushLayer)
                .ToArray() ?? [];

            var styles = brushElement.Element("Styles")?
                .Elements("Style")
                .Select(ParseBrushStyle)
                .Aggregate(
                    new Dictionary<string, BrushStyleDefinition>(StringComparer.OrdinalIgnoreCase),
                    (map, style) =>
                    {
                        map[style.Name] = style;
                        return map;
                    })
                ?? new Dictionary<string, BrushStyleDefinition>(StringComparer.OrdinalIgnoreCase);

            brushes[name] = new BrushDefinition(
                name,
                brushPath,
                layers,
                styles,
                brushElement.Attribute("BaseBrush")?.Value,
                brushElement.Attribute("Font")?.Value,
                brushElement.Attribute("TextHorizontalAlignment")?.Value,
                brushElement.Attribute("TextVerticalAlignment")?.Value);
        }
    }

    private static BrushStyleDefinition ParseBrushStyle(XElement styleElement)
    {
        var layerOverrides = styleElement.Elements("BrushLayer")
            .Select(ParseBrushLayer)
            .Aggregate(
                new Dictionary<string, BrushLayerDefinition>(StringComparer.OrdinalIgnoreCase),
                (map, layer) =>
                {
                    map[layer.Name] = layer;
                    return map;
                });

        return new BrushStyleDefinition(
            styleElement.Attribute("Name")?.Value ?? "Default",
            styleElement.Attribute("FontColor")?.Value,
            ParseDouble(styleElement.Attribute("FontSize")?.Value),
            ParseDouble(styleElement.Attribute("TextAlphaFactor")?.Value),
            layerOverrides);
    }

    private static BrushLayerDefinition ParseBrushLayer(XElement layerElement)
    {
        return new BrushLayerDefinition(
            layerElement.Attribute("Name")?.Value ?? string.Empty,
            layerElement.Attribute("Sprite")?.Value,
            layerElement.Attribute("OverlaySprite")?.Value,
            layerElement.Attribute("OverlayMethod")?.Value,
            layerElement.Attribute("Color")?.Value,
            ParseDouble(layerElement.Attribute("ColorFactor")?.Value),
            ParseDouble(layerElement.Attribute("AlphaFactor")?.Value),
            ParseDouble(layerElement.Attribute("SaturationFactor")?.Value),
            ParseDouble(layerElement.Attribute("ValueFactor")?.Value),
            ParseDouble(layerElement.Attribute("HueFactor")?.Value),
            ParseBool(layerElement.Attribute("IsHidden")?.Value),
            ParseDouble(layerElement.Attribute("ExtendLeft")?.Value),
            ParseDouble(layerElement.Attribute("ExtendRight")?.Value),
            ParseDouble(layerElement.Attribute("ExtendTop")?.Value),
            ParseDouble(layerElement.Attribute("ExtendBottom")?.Value),
            ParseBool(layerElement.Attribute("HorizontalFlip")?.Value),
            ParseBool(layerElement.Attribute("VerticalFlip")?.Value));
    }

    private static int ParseInt(string? rawValue)
    {
        return int.TryParse(rawValue, out var value) ? value : 0;
    }

    private static int? ParseNullableInt(string? rawValue)
    {
        return int.TryParse(rawValue, out var value) ? value : null;
    }

    private static double? ParseDouble(string? rawValue)
    {
        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool? ParseBool(string? rawValue)
    {
        return bool.TryParse(rawValue, out var value) ? value : null;
    }

}
