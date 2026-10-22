namespace Bannerlord.XmlPreview.VisualRegression;

public enum VisualRegressionMode
{
    Full,
    StructureOnly
}

/// <param name="PrefabIdentity">Installation-independent prefab identity, not an absolute installation path.</param>
/// <param name="ScenarioPath">Persistent Preview Scenario document; loading is internal to the module.</param>
/// <param name="Reference">Identifies reference expectations; package lookup is internal to the module.</param>
public sealed record VisualRegressionRequest(
    string PrefabIdentity,
    string ScenarioPath,
    VisualRegressionMode Mode,
    ReferenceMetadata Reference,
    string OutputDirectory);

public sealed record ReferenceMetadata(
    string CaseId,
    string GameVersion,
    string ModuleVersion,
    string CaptureProfileId,
    int PackageSchemaVersion);
