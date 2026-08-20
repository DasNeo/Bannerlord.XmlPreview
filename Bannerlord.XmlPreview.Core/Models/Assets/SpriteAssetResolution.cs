namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record SpriteAssetResolution(
    string RequestedName,
    SpriteAssetResolutionKind Kind,
    string? MatchedKey,
    string? ImagePath,
    SpriteDefinition? SpriteDefinition,
    string? Note)
{
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    public bool UsesFallback =>
        Kind is SpriteAssetResolutionKind.SpriteDefinitionName or
            SpriteAssetResolutionKind.SpritePartName or
            SpriteAssetResolutionKind.BaseNameFallback;
}
