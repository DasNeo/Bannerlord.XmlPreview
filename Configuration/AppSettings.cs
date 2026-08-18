namespace Bannerlord.XmlPreview.Configuration;

public sealed record AppSettings
{
    public string BannerlordInstallRoot { get; init; } = BannerlordInstallation.DefaultInstallRoot;
}
