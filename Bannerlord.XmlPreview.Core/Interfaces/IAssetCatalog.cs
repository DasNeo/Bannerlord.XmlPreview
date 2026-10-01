using Bannerlord.XmlPreview.Models.Assets;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IAssetCatalog
{
    Task WarmUpAsync(CancellationToken cancellationToken = default);

    Task WarmUpAssetsAsync(
        IReadOnlyCollection<string> brushNames,
        IReadOnlyCollection<string> spriteNames,
        CancellationToken cancellationToken = default);

    SpriteDefinition? FindSprite(string? spriteName);

    SpriteAssetResolution ResolveSpriteAsset(string? spriteName);

    ImageSource? GetSpriteImage(string? spriteName);

    BrushDefinition? FindBrush(string? brushName);
}
