using System.Windows.Media.Imaging;

namespace Bannerlord.XmlPreview.Models.Assets;

public enum ImageResolutionStatus { Resolved, Missing, RuntimeOnly, Invalid }
public enum ImageFailureKind { None, InvalidMetadata, UnsupportedFormat, AbsentPixelData, CorruptPayload, InvalidCrop, InvalidIdentity }
public sealed record ImageDiagnostic(string Code, string Message);
public sealed record ImageRequest(string Identity, string? Provider = null, string? AdditionalArgs = null,
    string? ScenarioSprite = null);
public sealed record ImageResolution(ImageRequest Request, ImageResolutionStatus Status, BitmapSource? Image,
    string? Module = null, string? SourcePath = null, string? Texture = null, string? Format = null,
    SpritePartDefinition? Crop = null, string? Reason = null, IReadOnlyList<string>? Attempts = null)
{
    public string? PayloadPath { get; init; }
    public ImageFailureKind Failure { get; init; }
    public IReadOnlyList<ImageDiagnostic> Diagnostics =>
        (Attempts ?? []).Where(a => a.StartsWith("module.dependency.missing:", StringComparison.Ordinal))
        .Select(a => new ImageDiagnostic("module.dependency.missing", a))
        .Concat(Status == ImageResolutionStatus.Resolved ? [] : new[] { new ImageDiagnostic("image." + (Failure == ImageFailureKind.None ? Status.ToString() : Failure.ToString()).ToLowerInvariant(), Reason ?? Status.ToString()) }).ToArray();
    public int Width => Image?.PixelWidth ?? Crop?.Width ?? 0;
    public int Height => Image?.PixelHeight ?? Crop?.Height ?? 0;
}
