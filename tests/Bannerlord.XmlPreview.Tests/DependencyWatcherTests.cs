using System.Collections.Concurrent;
using System.IO;
using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.Tests;

public sealed class DependencyWatcherTests
{
    [Fact]
    public async Task RepeatedWritesCoalesceIntoOneBatchAndRemovedDependenciesAreIgnored()
    {
        var directory = Path.Combine(Path.GetTempPath(), "watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var delays = new ConcurrentQueue<TaskCompletionSource>();
            using var watcher = new FileWatcherService(token =>
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                delays.Enqueue(completion);
                return completion.Task.WaitAsync(token);
            });
            var root = Path.Combine(directory, "root.xml"); var include = Path.Combine(directory, "include.xml");
            var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            watcher.DependenciesChanged += (_, batch) => { Interlocked.Increment(ref count); received.TrySetResult(batch); };
            watcher.ReplaceDependencies([root, include]);
            watcher.NotifyChanged([root]); watcher.NotifyChanged([root, include]); watcher.NotifyChanged([include]);
            Assert.Equal(3, delays.Count);
            foreach (var delay in delays) delay.TrySetResult();
            var batch = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(new[] { include, root }, batch);
            Assert.Equal(1, count);
            watcher.ReplaceDependencies([root]);
            watcher.NotifyChanged([include]);
            Assert.Equal(3, delays.Count);
            watcher.NotifyChanged([root]);
            watcher.ReplaceDependencies([]);
            foreach (var delay in delays) delay.TrySetResult();
            Assert.Equal(1, count);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RealAtomicSaveAndMissingFileRecoveryUseOneDependencyBatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "root.xml"); File.WriteAllText(root, "before");
            using var watcher = new FileWatcherService();
            var batches = new ConcurrentQueue<IReadOnlyList<string>>();
            var batchReady = new SemaphoreSlim(0);
            watcher.DependenciesChanged += (_, batch) => { batches.Enqueue(batch); batchReady.Release(); };
            watcher.ReplaceDependencies([root]);
            var temporary = Path.Combine(directory, "editor.tmp");
            File.WriteAllText(temporary, "after");
            File.Move(temporary, root, true);
            Assert.True(await batchReady.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(root, Assert.Single(Assert.Single(batches)));
            File.Delete(root);
            Assert.True(await batchReady.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken));
            File.WriteAllText(root, "recovered");
            Assert.True(await batchReady.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(3, batches.Count);
        }
        finally { Directory.Delete(directory, true); }
    }
}
