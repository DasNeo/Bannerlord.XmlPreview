namespace Bannerlord.XmlPreview.Interfaces;

public interface IPreviewEnvironmentInitializer
{
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
