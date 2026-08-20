namespace Bannerlord.XmlPreview.Models.Assets;

public sealed record SpriteDefinition(
    string Name,
    string SourcePath,
    string Kind,
    string SpritePartName,
    string CategoryName,
    int SheetId,
    int Width,
    int Height,
    int? LeftWidth = null,
    int? RightWidth = null,
    int? TopHeight = null,
    int? BottomHeight = null)
{
    public bool IsNineRegion =>
        LeftWidth.HasValue &&
        RightWidth.HasValue &&
        TopHeight.HasValue &&
        BottomHeight.HasValue;
}
