using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.Tests;

public sealed class LatestLoadCoordinatorTests
{
    [Fact]
    public async Task FirstLoadCompletesAndPublishesNormally()
    {
        var published = new List<string>();
        var failures = new List<string>();
        using var coordinator = new LatestLoadCoordinator<string, string>(
            static (request, _) => Task.FromResult($"loaded:{request}"),
            (request, result) => published.Add($"{request}={result}"),
            (request, _) => failures.Add(request));

        await coordinator.RunAsync("first");

        Assert.Equal(["first=loaded:first"], published);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task SecondLoadCancelsFirstAndDisposesReplacedCancellationSource()
    {
        var sources = new List<TrackingCancellationTokenSource>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();
        using var coordinator = new LatestLoadCoordinator<string, string>(
            async (request, cancellationToken) =>
            {
                if (request == "first")
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return request;
            },
            (_, result) => published.Add(result),
            static (_, _) => { },
            () =>
            {
                var source = new TrackingCancellationTokenSource();
                sources.Add(source);
                return source;
            });

        var first = coordinator.RunAsync("first");
        await firstStarted.Task;
        var second = coordinator.RunAsync("second");
        await Task.WhenAll(first, second);

        Assert.True(sources[0].IsCancellationRequested);
        Assert.True(sources[0].WasDisposed);
        Assert.Equal(["second"], published);
    }

    [Fact]
    public async Task CancelledFirstLoadCannotPublishAfterSecondLoadStarts()
    {
        var firstMayComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();
        using var coordinator = new LatestLoadCoordinator<string, string>(
            async (request, _) =>
            {
                if (request == "first")
                {
                    firstStarted.SetResult();
                    await firstMayComplete.Task;
                }

                return request;
            },
            (_, result) => published.Add(result),
            static (_, _) => { });

        var first = coordinator.RunAsync("first");
        await firstStarted.Task;
        var second = coordinator.RunAsync("second");
        firstMayComplete.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["second"], published);
    }

    [Fact]
    public async Task LoaderFailurePublishesOnlyForActiveGeneration()
    {
        var firstMayFail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<string>();
        using var coordinator = new LatestLoadCoordinator<string, string>(
            async (request, _) =>
            {
                if (request == "first")
                {
                    firstStarted.SetResult();
                    await firstMayFail.Task;
                }

                throw new InvalidOperationException($"failure:{request}");
            },
            static (_, _) => { },
            (request, exception) => failures.Add($"{request}={exception.Message}"));

        var first = coordinator.RunAsync("first");
        await firstStarted.Task;
        var second = coordinator.RunAsync("second");
        firstMayFail.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["second=failure:second"], failures);
    }

    [Fact]
    public async Task CancellationDoesNotPublishUserVisibleFailure()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        using var coordinator = new LatestLoadCoordinator<string, string>(
            async (request, cancellationToken) =>
            {
                if (request == "first")
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return request;
            },
            static (_, _) => { },
            (_, exception) => failures.Add(exception));

        var first = coordinator.RunAsync("first");
        await firstStarted.Task;
        var second = coordinator.RunAsync("second");
        await Task.WhenAll(first, second);

        Assert.Empty(failures);
    }

    private sealed class TrackingCancellationTokenSource : CancellationTokenSource
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
