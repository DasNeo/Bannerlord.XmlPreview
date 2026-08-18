using Bannerlord.XmlPreview.Models.Assets;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IBannerlordAssetCatalog
{
    BannerlordSpriteDefinition? FindSprite(string? spriteName);

    BannerlordSpriteAssetResolution ResolveSpriteAsset(string? spriteName);

    ImageSource? GetSpriteImage(string? spriteName);

    BannerlordBrushDefinition? FindBrush(string? brushName);

    double? ResolveBrushMetric(string? brushName, string? brushLayerName, string valueType);
}
