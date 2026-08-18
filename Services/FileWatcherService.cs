using System.IO;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public class FileWatcherService : IFileWatcherService
{
    private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
    private readonly Dictionary<FileSystemWatcher, string> _watchedFiles = new();
    
    public event EventHandler<FileInfo>? FileChanged;

    public int Start(FileInfo file)
    {
        if (string.IsNullOrWhiteSpace(file.DirectoryName))
        {
            throw new InvalidOperationException("The watched file must have a directory.");
        }

        if (_watchedFiles.Values.Any(path => string.Equals(path, file.FullName, StringComparison.OrdinalIgnoreCase)))
            return -1;
        
        var watcher = new FileSystemWatcher(file.DirectoryName);
        watcher.EnableRaisingEvents = true;
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.Filter = file.Name;
        watcher.Changed += WatcherOnChanged;
        _watchers.Add(watcher);
        _watchedFiles[watcher] = file.FullName;
        return _watchers.IndexOf(watcher);
    }

    public void Stop(FileInfo file)
    {
        var watcher = _watchedFiles
            .FirstOrDefault(entry => string.Equals(entry.Value, file.FullName, StringComparison.OrdinalIgnoreCase))
            .Key;

        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
    }
    
    private void WatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
        {
            return;
        }

        if (_watchedFiles.TryGetValue(watcher, out var watchedFile))
        {
            FileChanged?.Invoke(this, new FileInfo(watchedFile));
        }
    }
}
