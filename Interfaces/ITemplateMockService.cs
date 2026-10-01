using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;

namespace Bannerlord.XmlPreview.Interfaces;

public interface ITemplateMockService
{
    IReadOnlyList<TemplateMockDefinition> Discover(PrefabDocument document);

    IReadOnlyList<IReadOnlyDictionary<string, string>> PrepareItems(
        string sourcePath,
        TemplateMockDefinition definition);

    void ApplyItems(
        string sourcePath,
        TemplateMockDefinition definition,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items);
}

public sealed record TemplateMockDefinition(
    string TargetKey,
    string DisplayName,
    string Description,
    PrefabNode Node,
    IReadOnlyList<string> FieldKeys);
