namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record BannerlordSpritePartDefinition(
    string Name,
    string SourcePath,
    string CategoryName,
    int SheetId,
    int SheetX,
    int SheetY,
    int Width,
    int Height);
