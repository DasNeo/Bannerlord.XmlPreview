using System.IO;
using System.Text;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public sealed class DiagnosticsSnapshotWriter : IDiagnosticsSnapshotWriter
{
    public void Write(string outputPath, DiagnosticsSnapshot snapshot)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);

        var lines = new List<string>
        {
            $"GeneratedAtUtc: {DateTime.UtcNow:O}"
        };

        AddOptional(lines, "Stage", snapshot.Stage);
        lines.Add($"Status: {snapshot.StatusText}");
        if (snapshot.CompletionStatus.HasValue)
            lines.Add($"CompletionStatus: {snapshot.CompletionStatus.Value}");

        lines.Add(
            $"TruncationReason: {(snapshot.TruncationReasons is { Count: > 0 } ? string.Join(" | ", snapshot.TruncationReasons) : "None")}");
        AddOptional(lines, "Prefab", snapshot.PrefabPath);
        AddOptional(lines, "BannerlordRoot", snapshot.BannerlordRoot);

        if (snapshot.LoadDurationMs.HasValue)
            lines.Add($"LoadDurationMs: {snapshot.LoadDurationMs.Value}");

        if (snapshot.RenderDurationMs.HasValue)
            lines.Add($"RenderDurationMs: {snapshot.RenderDurationMs.Value}");

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(snapshot.DiagnosticsText)
            ? "No diagnostics were generated."
            : snapshot.DiagnosticsText);

        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
    }

    private static void AddOptional(ICollection<string> lines, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{name}: {value}");
    }
}
