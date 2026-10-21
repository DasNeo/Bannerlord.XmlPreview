namespace Bannerlord.XmlPreview.Services;

/// <summary>
/// Serializes load work and guarantees that only the newest request may publish a result or failure.
/// The caller's synchronization context is preserved for publication callbacks.
/// </summary>
public sealed class LatestLoadCoordinator<TRequest, TResult> : IDisposable
{
    private readonly Func<TRequest, CancellationToken, Task<TResult>> _loadAsync;
    private readonly Action<TRequest, TResult> _publishResult;
    private readonly Action<TRequest, Exception> _publishFailure;
    private readonly Func<CancellationTokenSource> _cancellationSourceFactory;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _activeCancellation;
    private long _generation;
    private bool _disposed;

    public LatestLoadCoordinator(
        Func<TRequest, CancellationToken, Task<TResult>> loadAsync,
        Action<TRequest, TResult> publishResult,
        Action<TRequest, Exception> publishFailure)
        : this(loadAsync, publishResult, publishFailure, static () => new CancellationTokenSource())
    {
    }

    internal LatestLoadCoordinator(
        Func<TRequest, CancellationToken, Task<TResult>> loadAsync,
        Action<TRequest, TResult> publishResult,
        Action<TRequest, Exception> publishFailure,
        Func<CancellationTokenSource> cancellationSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(loadAsync);
        ArgumentNullException.ThrowIfNull(publishResult);
        ArgumentNullException.ThrowIfNull(publishFailure);
        ArgumentNullException.ThrowIfNull(cancellationSourceFactory);

        _loadAsync = loadAsync;
        _publishResult = publishResult;
        _publishFailure = publishFailure;
        _cancellationSourceFactory = cancellationSourceFactory;
    }

    public async Task RunAsync(TRequest request)
    {
        var operation = BeginOperation();
        var gateEntered = false;

        try
        {
            await _loadGate.WaitAsync(operation.Token);
            gateEntered = true;
            var result = await _loadAsync(request, operation.Token);
            operation.Token.ThrowIfCancellationRequested();

            PublishIfActive(operation, () => _publishResult(request, result));
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            // Superseded and explicitly cancelled loads intentionally publish nothing.
        }
        catch (Exception exception)
        {
            PublishIfActive(operation, () => _publishFailure(request, exception));
        }
        finally
        {
            if (gateEntered)
                _loadGate.Release();

            EndOperation(operation);
        }
    }

    private LoadOperation BeginOperation()
    {
        CancellationTokenSource? replacedCancellation;
        LoadOperation operation;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var cancellation = _cancellationSourceFactory();
            operation = new LoadOperation(++_generation, cancellation, cancellation.Token);
            replacedCancellation = _activeCancellation;
            _activeCancellation = operation.Cancellation;
        }

        TryCancel(replacedCancellation);
        return operation;
    }

    private void PublishIfActive(LoadOperation operation, Action publish)
    {
        lock (_sync)
        {
            if (_disposed ||
                operation.Generation != _generation ||
                !ReferenceEquals(_activeCancellation, operation.Cancellation) ||
                operation.Token.IsCancellationRequested)
                return;

            publish();
        }
    }

    private void EndOperation(LoadOperation operation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeCancellation, operation.Cancellation))
                _activeCancellation = null;
        }

        operation.Cancellation.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource? activeCancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            activeCancellation = _activeCancellation;
            _activeCancellation = null;
        }

        TryCancel(activeCancellation);
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
            // The operation completed and disposed its source concurrently.
        }
    }

    private sealed record LoadOperation(long Generation, CancellationTokenSource Cancellation, CancellationToken Token);
}
