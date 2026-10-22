using System.IO;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

/// <summary>Only installed game binaries and shipped modules may supply executable type metadata.</summary>
public static class BannerlordWidgetMetadataTrust
{
    private static readonly string[] ShippedModules = ["Native", "SandBox", "SandBoxCore", "StoryMode", "CustomBattle", "Multiplayer", "BirthAndDeath"];
    public static bool IsTrustedAssemblyPath(string installationRoot, string assemblyPath)
    {
        var root = Path.GetFullPath(installationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(assemblyPath);
        bool Under(string directory) => path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return Under(Path.Combine(root, "bin")) || ShippedModules.Any(module => Under(Path.Combine(root, "Modules", module, "bin")));
    }
}
