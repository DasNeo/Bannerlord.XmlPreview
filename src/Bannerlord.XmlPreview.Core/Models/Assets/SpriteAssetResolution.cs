namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record SpriteAssetResolution(
    string RequestedName,
    SpriteAssetResolutionKind Kind,
    string? MatchedKey,
    string? ImagePath,
    SpriteDefinition? SpriteDefinition,
    string? Note)
{
    public ImageResolution? ImageResolution { get; init; }
    public bool HasImage => ImageResolution is not null ? ImageResolution.Status == ImageResolutionStatus.Resolved : !string.IsNullOrWhiteSpace(ImagePath);

    public bool UsesFallback =>
        Kind is SpriteAssetResolutionKind.SpriteDefinitionName or
            SpriteAssetResolutionKind.SpritePartName or
            SpriteAssetResolutionKind.BaseNameFallback;
}
