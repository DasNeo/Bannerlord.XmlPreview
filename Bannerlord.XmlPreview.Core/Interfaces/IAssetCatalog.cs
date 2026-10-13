using Bannerlord.XmlPreview.Models.Assets;
using System.Windows.Media;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IAssetCatalog
{
    IAssetCatalog ForPrefab(string path) => this;
    IAssetCatalog ForPrefab(string path, string? assetModule) => ForPrefab(path);

    ImageResolution ResolveImage(ImageRequest request)
    {
        if (!string.IsNullOrEmpty(request.Provider) && request.Provider != "Preview.Sprite" && request.ScenarioSprite is null)
            return new(request, ImageResolutionStatus.RuntimeOnly, null, Reason: "Provider requires game runtime data.");
        var image = GetSpriteImage(request.ScenarioSprite ?? request.Identity) as System.Windows.Media.Imaging.BitmapSource;
        return new(request, image is null ? ImageResolutionStatus.Missing : ImageResolutionStatus.Resolved, image);
    }

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
