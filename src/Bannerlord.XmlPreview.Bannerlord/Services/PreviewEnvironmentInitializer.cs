using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Interfaces;
using System.IO;

namespace Bannerlord.XmlPreview.Services;

public sealed class PreviewEnvironmentInitializer : IPreviewEnvironmentInitializer, IDisposable
{
    private readonly IAppSettingsService _settingsService;
    private readonly IBannerlordInstallationIndex _installationIndex;
    private readonly IAssetCatalog _assetCatalog;
    private readonly IBannerlordWidgetReflectionService _reflectionService;
    private readonly object _sync = new();
    private string? _warmRoot;
    private Task? _warmTask;
    private CancellationTokenSource? _warmCancellation;
    private bool _disposed;

    public PreviewEnvironmentInitializer(
        IAppSettingsService settingsService,
        IBannerlordInstallationIndex installationIndex,
        IAssetCatalog assetCatalog,
        IBannerlordWidgetReflectionService reflectionService)
    {
        _settingsService = settingsService;
        _installationIndex = installationIndex;
        _assetCatalog = assetCatalog;
        _reflectionService = reflectionService;
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var installRoot = Path.GetFullPath(_settingsService.BannerlordInstallRoot);
        Task warmTask;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_warmTask is null ||
                _warmTask.IsFaulted ||
                _warmTask.IsCanceled ||
                !string.Equals(_warmRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                TryCancel(_warmCancellation);
                _warmCancellation = new CancellationTokenSource();
                _warmRoot = installRoot;
                _warmTask = WarmCoreOwnedAsync(installRoot, _warmCancellation);
            }

            warmTask = _warmTask;
        }

        return warmTask.WaitAsync(cancellationToken);
    }

    private async Task WarmCoreOwnedAsync(string installRoot, CancellationTokenSource cancellation)
    {
        try
        {
            await WarmCoreAsync(installRoot, cancellation.Token);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task WarmCoreAsync(string installRoot, CancellationToken cancellationToken)
    {
        await _installationIndex.GetSnapshotAsync(installRoot, cancellationToken);
        await Task.WhenAll(
            _assetCatalog.WarmUpAsync(cancellationToken),
            _reflectionService.WarmUpAsync(cancellationToken));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            TryCancel(_warmCancellation);
            _warmCancellation = null;
            _warmTask = null;
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The warm-up task completed and released its source concurrently.
        }
    }
}
