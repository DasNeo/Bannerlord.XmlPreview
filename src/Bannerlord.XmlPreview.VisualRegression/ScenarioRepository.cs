using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.VisualRegression;

public interface IScenarioRepository
{
    IReadOnlyList<string> List();
    PreviewScenario Load(string id);
    void Save(PreviewScenario scenario);
    PreviewScenario Create(PreviewScenario scenario);
    PreviewScenario Clone(string id, string newId);
    PreviewScenario Rename(string id, string newId);
    void Delete(string id);
    string PathFor(string id);
}

/// <summary>Validated, atomic scenario persistence outside the game installation.</summary>
public sealed class ScenarioRepository(string directory) : IScenarioRepository
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bannerlord.XmlPreview", "Scenarios");
    public IReadOnlyList<string> List() => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).Cast<string>().Order(StringComparer.Ordinal).ToArray() : [];
    public string PathFor(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_.-]*$"))
            throw new InvalidDataException("Invalid scenario identity.");
        return Path.GetFullPath(Path.Combine(directory, id + ".json"));
    }
    public PreviewScenario Load(string id)
    {
        var scenario = ScenarioLoader.Load(PathFor(id));
        if (scenario.Id != id) throw new InvalidDataException("Scenario filename and identity differ.");
        return scenario;
    }
    public void Save(PreviewScenario scenario) => Write(scenario, overwrite: true);
    public PreviewScenario Create(PreviewScenario scenario) { Write(scenario, overwrite: false); return scenario; }
    public PreviewScenario Clone(string id, string newId) => Create(Load(id) with { Id = newId });
    public PreviewScenario Rename(string id, string newId)
    {
        if (id == newId) return Load(id);
        var renamed = Clone(id, newId);
        Delete(id);
        return renamed;
    }
    public void Delete(string id) => File.Delete(PathFor(id));
    private void Write(PreviewScenario scenario, bool overwrite)
    {
        ScenarioLoader.Validate(scenario);
        var path = PathFor(scenario.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(scenario, ScenarioLoader.JsonOptions));
            // Verify exactly what consumers will read, before replacing their valid document.
            _ = ScenarioLoader.Load(temporary);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
