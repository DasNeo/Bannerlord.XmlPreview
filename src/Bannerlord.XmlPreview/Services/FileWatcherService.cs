using System.IO;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

/// <summary>One debounce window for the complete dependency set, including atomic editor saves.</summary>
public sealed class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<CancellationToken, Task> _delay;
    private CancellationTokenSource? _debounce;
    private bool _disposed;
    public FileWatcherService() : this(token => Task.Delay(250, token)) { }
    public FileWatcherService(Func<CancellationToken, Task> delay) => _delay = delay;
    public event EventHandler<FileInfo>? FileChanged;
    public event EventHandler<IReadOnlyList<string>>? DependenciesChanged;
    public void ReplaceDependencies(IEnumerable<string> paths)
    {
        var replacement = paths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var directories = replacement.Select(path => ExistingDirectory(Path.GetDirectoryName(path)!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var directory in directories.Except(_watchers.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName
                    };
                    watcher.Changed += OnChanged; watcher.Created += OnChanged; watcher.Deleted += OnChanged;
                    watcher.Renamed += OnRenamed; watcher.Error += OnError;
                    added[directory] = watcher;
                    watcher.EnableRaisingEvents = true;
                }
            }
            catch { foreach (var watcher in added.Values) watcher.Dispose(); throw; }
            foreach (var directory in _watchers.Keys.Except(directories, StringComparer.OrdinalIgnoreCase).ToArray())
            { _watchers[directory].Dispose(); _watchers.Remove(directory); }
            foreach (var pair in added) _watchers.Add(pair.Key, pair.Value);
            _paths = replacement;
            _pending.IntersectWith(replacement);
        }
    }
    private static string ExistingDirectory(string directory)
    {
        while (!Directory.Exists(directory)) directory = Path.GetDirectoryName(directory)
            ?? throw new DirectoryNotFoundException("No parent directory exists for the dependency.");
        return directory;
    }
    public int Start(FileInfo file) { lock (_sync) ReplaceDependencies(_paths.Append(file.FullName)); return 0; }
    public void Stop(FileInfo file) { lock (_sync) ReplaceDependencies(_paths.Where(path => !path.Equals(file.FullName, StringComparison.OrdinalIgnoreCase))); }
    private void OnChanged(object sender, FileSystemEventArgs e) => NotifyChanged([e.FullPath]);
    private void OnRenamed(object sender, RenamedEventArgs e) => NotifyChanged([e.OldFullPath, e.FullPath]);
    private void OnError(object sender, ErrorEventArgs e) { lock (_sync) NotifyChanged(_paths.ToArray()); }
    public void NotifyChanged(IEnumerable<string> changedPaths)
    {
        lock (_sync)
        {
            if (_disposed) return;
            foreach (var path in changedPaths.Select(Path.GetFullPath))
                foreach (var dependency in _paths.Where(p => p.Equals(path, StringComparison.OrdinalIgnoreCase) || p.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    _pending.Add(dependency);
            if (_pending.Count == 0) return;
            _debounce?.Cancel();
            var cancellation = new CancellationTokenSource();
            _debounce = cancellation;
            _ = FlushAsync(cancellation);
        }
    }
    private async Task FlushAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await _delay(cancellation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || cancellation.IsCancellationRequested || _debounce != cancellation) return;
                var batch = _pending.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                _pending.Clear(); _debounce = null;
                if (batch.Length == 0) return;
                DependenciesChanged?.Invoke(this, batch);
                foreach (var path in batch) FileChanged?.Invoke(this, new FileInfo(path));
            }
        }
        catch (OperationCanceledException) { }
        finally { cancellation.Dispose(); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _debounce?.Cancel(); _debounce = null;
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear(); _pending.Clear();
        }
    }
}
