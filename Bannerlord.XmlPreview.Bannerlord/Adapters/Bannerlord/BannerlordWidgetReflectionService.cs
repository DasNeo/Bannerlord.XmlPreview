using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public interface IBannerlordWidgetReflectionService
{
    bool TryGetRenderKind(string widgetName, out BannerlordWidgetRenderKind renderKind);

    bool TryGetWidgetType(string widgetName, out Type widgetType);

    bool TryGetWidgetBaseType(out Type widgetBaseType);
}

public enum BannerlordWidgetRenderKind
{
    Unknown,
    Widget,
    BrushWidget,
    ButtonWidget,
    ListPanel,
    NavigationScopeTargeter,
    GridWidget,
    ScrollablePanel,
    ScrollbarWidget
}

public sealed class BannerlordWidgetReflectionService : IBannerlordWidgetReflectionService
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly object _sync = new();
    private WidgetReflectionSnapshot? _snapshot;

    public BannerlordWidgetReflectionService(IAppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
    }

    public bool TryGetRenderKind(string widgetName, out BannerlordWidgetRenderKind renderKind)
    {
        var snapshot = GetSnapshot();
        if (snapshot is not null &&
            snapshot.RenderKindsByWidgetName.TryGetValue(widgetName, out renderKind))
        {
            return true;
        }

        renderKind = BannerlordWidgetRenderKind.Unknown;
        return false;
    }

    public bool TryGetWidgetType(string widgetName, out Type widgetType)
    {
        var snapshot = GetSnapshot();
        if (snapshot is not null &&
            snapshot.WidgetTypesByName.TryGetValue(widgetName, out widgetType!))
        {
            return true;
        }

        widgetType = null!;
        return false;
    }

    public bool TryGetWidgetBaseType(out Type widgetBaseType)
    {
        var snapshot = GetSnapshot();
        if (snapshot?.WidgetBaseType is not null)
        {
            widgetBaseType = snapshot.WidgetBaseType;
            return true;
        }

        widgetBaseType = null!;
        return false;
    }

    private WidgetReflectionSnapshot? GetSnapshot()
    {
        var installRoot = Path.GetFullPath(_appSettingsService.BannerlordInstallRoot);
        lock (_sync)
        {
            if (_snapshot is not null &&
                string.Equals(_snapshot.InstallRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _snapshot;
            }

            _snapshot?.LoadContext.Unload();
            _snapshot = BuildSnapshot(installRoot);
            return _snapshot;
        }
    }

    private static WidgetReflectionSnapshot? BuildSnapshot(string installRoot)
    {
        using var totalTiming = PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.Total");

        var assemblyPathsByName = CollectAssemblyPaths(installRoot);
        if (assemblyPathsByName.Count == 0)
        {
            return null;
        }

        var loadContext = new WidgetReflectionLoadContext(assemblyPathsByName);
        var loadedAssemblies = new List<Assembly>();
        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.LoadAssemblies"))
        {
            foreach (var assemblyPath in assemblyPathsByName.Values)
            {
                try
                {
                    loadedAssemblies.Add(loadContext.LoadFromAssemblyPath(assemblyPath));
                }
                catch
                {
                }
            }
        }

        var widgetBaseType = loadedAssemblies
            .Select(assembly => assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", throwOnError: false, ignoreCase: false))
            .FirstOrDefault(type => type is not null);
        if (widgetBaseType is null)
        {
            loadContext.Unload();
            return null;
        }

        var widgetTypesByName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var renderKindsByWidgetName = new Dictionary<string, BannerlordWidgetRenderKind>(StringComparer.OrdinalIgnoreCase);
        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.ScanWidgetTypes"))
        {
            foreach (var assembly in loadedAssemblies)
            {
                foreach (var type in GetLoadableTypes(assembly))
                {
                    if (!type.IsClass ||
                        type.IsAbstract ||
                        !widgetBaseType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    widgetTypesByName.TryAdd(type.Name, type);

                    var renderKind = ClassifyRenderKind(type);
                    if (renderKind != BannerlordWidgetRenderKind.Unknown)
                    {
                        renderKindsByWidgetName.TryAdd(type.Name, renderKind);
                    }
                }
            }
        }

        return widgetTypesByName.Count == 0
            ? null
            : new WidgetReflectionSnapshot(
                installRoot,
                loadContext,
                widgetBaseType,
                widgetTypesByName,
                renderKindsByWidgetName);
    }

    private static Dictionary<string, string> CollectAssemblyPaths(string installRoot)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.CollectAssemblyPaths");
        var pathsByAssemblyName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyPath in EnumerateAssemblyPaths(installRoot))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath).Name;
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    pathsByAssemblyName.TryAdd(assemblyName, assemblyPath);
                }
            }
            catch
            {
            }
        }

        return pathsByAssemblyName;
    }

    private static IEnumerable<string> EnumerateAssemblyPaths(string installRoot)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GetAssemblyDirectories(installRoot))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (seenPaths.Add(path))
                {
                    yield return Path.GetFullPath(path);
                }
            }
        }
    }

    private static IEnumerable<string> GetAssemblyDirectories(string installRoot)
    {
        yield return Path.Combine(installRoot, "bin", "Win64_Shipping_Client");

        var modulesPath = Path.Combine(installRoot, "Modules");
        if (!Directory.Exists(modulesPath))
        {
            yield break;
        }

        foreach (var modulePath in Directory.EnumerateDirectories(modulesPath))
        {
            yield return Path.Combine(modulePath, "bin", "Win64_Shipping_Client");
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static BannerlordWidgetRenderKind ClassifyRenderKind(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            switch (current.FullName)
            {
                case "TaleWorlds.GauntletUI.BaseTypes.ScrollbarWidget":
                    return BannerlordWidgetRenderKind.ScrollbarWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.ScrollablePanel":
                    return BannerlordWidgetRenderKind.ScrollablePanel;
                case "TaleWorlds.GauntletUI.BaseTypes.GridWidget":
                    return BannerlordWidgetRenderKind.GridWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.ListPanel":
                    return BannerlordWidgetRenderKind.ListPanel;
                case "TaleWorlds.GauntletUI.BaseTypes.NavigationScopeTargeter":
                    return BannerlordWidgetRenderKind.NavigationScopeTargeter;
                case "TaleWorlds.GauntletUI.BaseTypes.ButtonWidget":
                    return BannerlordWidgetRenderKind.ButtonWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.BrushWidget":
                    return BannerlordWidgetRenderKind.BrushWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.Widget":
                    return BannerlordWidgetRenderKind.Widget;
            }
        }

        return BannerlordWidgetRenderKind.Unknown;
    }

    private sealed class WidgetReflectionLoadContext : AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, string> _assemblyPathsByName;

        public WidgetReflectionLoadContext(IReadOnlyDictionary<string, string> assemblyPathsByName)
            : base(isCollectible: true)
        {
            _assemblyPathsByName = assemblyPathsByName;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (_assemblyPathsByName.TryGetValue(assemblyName.Name ?? string.Empty, out var assemblyPath))
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }
    }

    private sealed record WidgetReflectionSnapshot(
        string InstallRoot,
        WidgetReflectionLoadContext LoadContext,
        Type WidgetBaseType,
        IReadOnlyDictionary<string, Type> WidgetTypesByName,
        IReadOnlyDictionary<string, BannerlordWidgetRenderKind> RenderKindsByWidgetName);
}
