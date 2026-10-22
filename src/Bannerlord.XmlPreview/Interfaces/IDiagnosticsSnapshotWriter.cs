using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.Interfaces;

public interface IDiagnosticsSnapshotWriter
{
    void Write(string outputPath, DiagnosticsSnapshot snapshot);
}

public sealed record DiagnosticsSnapshot(
    string StatusText,
    string? Stage = null,
    string? PrefabPath = null,
    string? BannerlordRoot = null,
    long? LoadDurationMs = null,
    long? RenderDurationMs = null,
    string? DiagnosticsText = null,
    RenderCompletionStatus? CompletionStatus = null,
    IReadOnlyList<string>? TruncationReasons = null);
