namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BannerlordSpriteAssetResolution(
    string RequestedName,
    SpriteAssetResolutionKind Kind,
    string? MatchedKey,
    string? ImagePath,
    BannerlordSpriteDefinition? SpriteDefinition,
    string? Note)
{
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    public bool UsesFallback =>
        Kind is SpriteAssetResolutionKind.SpriteDefinitionName or
            SpriteAssetResolutionKind.SpritePartName or
            SpriteAssetResolutionKind.BaseNameFallback;
}
