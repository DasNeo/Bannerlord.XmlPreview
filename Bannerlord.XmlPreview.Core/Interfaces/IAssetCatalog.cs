using Bannerlord.XmlPreview.Models.Assets;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IAssetCatalog
{
    SpriteDefinition? FindSprite(string? spriteName);

    SpriteAssetResolution ResolveSpriteAsset(string? spriteName);

    ImageSource? GetSpriteImage(string? spriteName);

    BrushDefinition? FindBrush(string? brushName);
}
