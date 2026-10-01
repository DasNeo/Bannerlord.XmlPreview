using System.IO;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public sealed class FileWatcherService : IFileWatcherService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);
    private readonly object _sync = new();
    private readonly Dictionary<FileSystemWatcher, WatchRegistration> _registrations = [];
    private int _nextRegistrationId;
    private bool _disposed;
    
    public event EventHandler<FileInfo>? FileChanged;

    public int Start(FileInfo file)
    {
        if (string.IsNullOrWhiteSpace(file.DirectoryName))
        {
            throw new InvalidOperationException("The watched file must have a directory.");
        }

        var fullPath = Path.GetFullPath(file.FullName);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registrations.Values.Any(registration =>
                    string.Equals(registration.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
                return -1;

            var watcher = new FileSystemWatcher(file.DirectoryName, file.Name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += WatcherOnChanged;
            watcher.Renamed += WatcherOnRenamed;
            var registration = new WatchRegistration(++_nextRegistrationId, fullPath);
            _registrations[watcher] = registration;
            watcher.EnableRaisingEvents = true;
            return registration.Id;
        }
    }

    public void Stop(FileInfo file)
    {
        FileSystemWatcher? watcher;
        WatchRegistration? registration;
        var fullPath = Path.GetFullPath(file.FullName);
        lock (_sync)
        {
            watcher = _registrations
                .FirstOrDefault(entry =>
                    string.Equals(entry.Value.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                .Key;
            if (watcher is null || !_registrations.Remove(watcher, out registration))
                return;
        }

        DisposeRegistration(watcher, registration);
    }

    private void WatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher watcher) ScheduleNotification(watcher);
    }

    private void WatcherOnRenamed(object sender, RenamedEventArgs e)
    {
        if (sender is FileSystemWatcher watcher) ScheduleNotification(watcher);
    }

    private void ScheduleNotification(FileSystemWatcher watcher)
    {
        CancellationTokenSource cancellation;
        CancellationToken token;
        string filePath;
        lock (_sync)
        {
            if (_disposed || !_registrations.TryGetValue(watcher, out var registration))
                return;

            TryCancel(registration.PendingNotification);
            cancellation = new CancellationTokenSource();
            token = cancellation.Token;
            registration.PendingNotification = cancellation;
            filePath = registration.FilePath;
        }

        _ = RaiseNotificationAfterDebounceAsync(watcher, filePath, cancellation, token);
    }

    private async Task RaiseNotificationAfterDebounceAsync(
        FileSystemWatcher watcher,
        string filePath,
        CancellationTokenSource cancellation,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed ||
                    !_registrations.TryGetValue(watcher, out var registration) ||
                    !ReferenceEquals(registration.PendingNotification, cancellation))
                    return;

                registration.PendingNotification = null;
            }

            FileChanged?.Invoke(this, new FileInfo(filePath));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        KeyValuePair<FileSystemWatcher, WatchRegistration>[] registrations;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            registrations = _registrations.ToArray();
            _registrations.Clear();
        }

        foreach (var (watcher, registration) in registrations)
            DisposeRegistration(watcher, registration);
    }

    private void DisposeRegistration(
        FileSystemWatcher watcher,
        WatchRegistration registration)
    {
        TryCancel(registration.PendingNotification);
        watcher.EnableRaisingEvents = false;
        watcher.Changed -= WatcherOnChanged;
        watcher.Renamed -= WatcherOnRenamed;
        watcher.Dispose();
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
            // The debounce task completed concurrently.
        }
    }

    private sealed class WatchRegistration(int id, string filePath)
    {
        public int Id { get; } = id;
        public string FilePath { get; } = filePath;
        public CancellationTokenSource? PendingNotification { get; set; }
    }
}
