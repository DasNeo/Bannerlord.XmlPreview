using System.ComponentModel;
using System.IO;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IFileWatcherService
{
    event EventHandler<IReadOnlyList<string>> DependenciesChanged;
    void ReplaceDependencies(IEnumerable<string> paths);
    /// <summary>
    /// Gets fired when the file watched is changed
    /// </summary>
    public event EventHandler<FileInfo> FileChanged;

    /// <summary>
    /// Starts the file watched
    /// </summary>
    /// <param name="file">The file to be watched</param>
    public int Start(FileInfo file);

    /// <summary>
    /// Stops the file watcher
    /// </summary>
    /// <param name="file"></param>
    public void Stop(FileInfo file);
}
