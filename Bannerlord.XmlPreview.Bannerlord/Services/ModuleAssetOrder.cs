using System.IO;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Interfaces;

namespace Bannerlord.XmlPreview.Services;

public static class ModuleAssetOrder
{
    public static BannerlordModule Read(string directory)
    {
        var packages = new[] { "AssetPackages", "Assets" }.Select(root => Path.Combine(directory, root))
            .Where(System.IO.Directory.Exists).SelectMany(root => System.IO.Directory.EnumerateFiles(root, "*.tpac", SearchOption.AllDirectories))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            var xml = XDocument.Load(Path.Combine(directory, "SubModule.xml"));
            var id = xml.Root?.Element("Id")?.Attribute("value")?.Value;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Module Id is missing.");
            var dependencies = xml.Root!.Element("DependedModules")?.Elements("DependedModule")
                .Select(e => e.Attribute("Id")?.Value ?? throw new InvalidDataException("Dependency Id is missing."))
                .ToArray() ?? [];
            if (dependencies.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Dependency Id is empty.");
            return new(id, directory, dependencies, packages);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException)
        {
            return new(Path.GetFileName(directory), directory, [], packages, ex.Message);
        }
    }

    // Dependencies load first; later-loaded modules override earlier assets. Lookup reverses that order.
    public static IReadOnlyList<BannerlordModule> Resolve(IReadOnlyList<BannerlordModule> modules, string owner, ICollection<string>? unavailable = null)
    {
        var byId = new Dictionary<string, BannerlordModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
            if (!byId.TryAdd(module.Id, module)) throw new InvalidDataException($"Duplicate module Id '{module.Id}'.");
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<BannerlordModule>();
        void Visit(string id)
        {
            if (done.Contains(id)) return;
            if (!byId.TryGetValue(id, out var module))
            {
                if (unavailable is null || id.Equals(owner, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Missing module dependency '{id}'.");
                unavailable.Add($"module.dependency.missing: '{id}' has no installed data directory; its assets cannot participate in lookup.");
                done.Add(id);
                return;
            }
            if (module.MetadataError is not null) throw new InvalidDataException($"Invalid module '{id}': {module.MetadataError}");
            if (!visiting.Add(id)) throw new InvalidDataException($"Module dependency cycle at '{id}'.");
            foreach (var dependency in module.Dependencies) Visit(dependency);
            visiting.Remove(id);
            done.Add(id);
            order.Add(module);
        }
        Visit(owner);
        order.Reverse();
        return order;
    }

    public static string Owner(IReadOnlyList<BannerlordModule> modules, string? prefab)
    {
        if (prefab is not null)
        {
            var path = Path.GetFullPath(prefab);
            var owned = modules.FirstOrDefault(m => path.StartsWith(Path.GetFullPath(m.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (owned is not null) return owned.Id;
            // Preserved repository fixtures retain their module directory identity.
            var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fixture = modules.FirstOrDefault(m => segments.Contains(m.Id, StringComparer.OrdinalIgnoreCase));
            if (fixture is not null) return fixture.Id;
        }
        return "Native";
    }
}
