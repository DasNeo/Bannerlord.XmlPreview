using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public interface IBannerlordWidgetReflectionService
{
    Task WarmUpAsync(CancellationToken cancellationToken = default);

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
    ScrollbarWidget,
    TextWidget,
    BrushListPanel
}

public sealed class BannerlordWidgetReflectionService : IBannerlordWidgetReflectionService
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly IBannerlordInstallationIndex _installationIndex;
    private readonly object _sync = new();
    private WidgetReflectionSnapshot? _snapshot;

    public BannerlordWidgetReflectionService(
        IAppSettingsService appSettingsService,
        IBannerlordInstallationIndex installationIndex)
    {
        _appSettingsService = appSettingsService;
        _installationIndex = installationIndex;
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = GetSnapshot();
            },
            cancellationToken);
    }

    public bool TryGetRenderKind(string widgetName, out BannerlordWidgetRenderKind renderKind)
    {
        var snapshot = GetSnapshot();
        if (snapshot is not null &&
            snapshot.RenderKindsByWidgetName.TryGetValue(widgetName, out renderKind))
        {
            return true;
        }

        snapshot = EnsureModuleWidgetsLoaded(snapshot);
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

        snapshot = EnsureModuleWidgetsLoaded(snapshot);
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

    private WidgetReflectionSnapshot? BuildSnapshot(string installRoot)
    {
        using var totalTiming = PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.Total");

        var assemblyPathsByName = CollectAssemblyPaths(
            _installationIndex.GetSnapshot(installRoot).AssemblyFiles.Where(path => BannerlordWidgetMetadataTrust.IsTrustedAssemblyPath(installRoot, path)));
        if (assemblyPathsByName.Count == 0)
        {
            return null;
        }

        var loadContext = new WidgetReflectionLoadContext(assemblyPathsByName);
        var loadedAssemblies = new List<Assembly>();
        var moduleAssemblyPaths = assemblyPathsByName.Values
            .Where(IsModuleAssembly)
            .ToArray();
        var scanAssemblyPaths = assemblyPathsByName.Values
            .Where(path => !IsModuleAssembly(path) && IsWidgetAssemblyCandidate(path))
            .ToArray();
        using (PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.LoadAssemblies"))
        {
            foreach (var assemblyPath in scanAssemblyPaths)
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

        var widgetTypesByName = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var renderKindsByWidgetName = new ConcurrentDictionary<string, BannerlordWidgetRenderKind>(StringComparer.OrdinalIgnoreCase);
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
                renderKindsByWidgetName,
                moduleAssemblyPaths);
    }

    private WidgetReflectionSnapshot? EnsureModuleWidgetsLoaded(WidgetReflectionSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.ModulesLoaded) return snapshot;

        lock (_sync)
        {
            if (!ReferenceEquals(snapshot, _snapshot)) return _snapshot;
            if (snapshot.ModulesLoaded) return snapshot;

            using var timing = PreviewDiagnostics.MeasureCurrentPhase(
                "BannerlordWidgetReflectionService.LoadModuleWidgets.Total");
            foreach (var assemblyPath in snapshot.ModuleAssemblyPaths)
            {
                try
                {
                    var assembly = snapshot.LoadContext.LoadFromAssemblyPath(assemblyPath);
                    AddWidgetTypes(
                        assembly,
                        snapshot.WidgetBaseType,
                        snapshot.WidgetTypesByName,
                        snapshot.RenderKindsByWidgetName);
                }
                catch
                {
                }
            }

            snapshot.ModulesLoaded = true;
            return snapshot;
        }
    }

    private static bool IsWidgetAssemblyCandidate(string assemblyPath)
    {
        return Path.GetFileName(assemblyPath)
            .Contains("GauntletUI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModuleAssembly(string assemblyPath)
    {
        return assemblyPath.Contains(
            $"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> CollectAssemblyPaths(
        IEnumerable<string> assemblyPaths)
    {
        using var timing = PreviewDiagnostics.MeasureCurrentPhase("BannerlordWidgetReflectionService.BuildSnapshot.CollectAssemblyPaths");
        var pathsByAssemblyName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyPath in assemblyPaths)
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

    private static void AddWidgetTypes(
        Assembly assembly,
        Type widgetBaseType,
        IDictionary<string, Type> widgetTypesByName,
        IDictionary<string, BannerlordWidgetRenderKind> renderKindsByWidgetName)
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
                renderKindsByWidgetName.TryAdd(type.Name, renderKind);
        }
    }

    private static BannerlordWidgetRenderKind ClassifyRenderKind(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            switch (current.FullName)
            {
                case "TaleWorlds.GauntletUI.BaseTypes.TextWidget":
                    return BannerlordWidgetRenderKind.TextWidget;
                case "TaleWorlds.GauntletUI.BrushListPanel":
                    return BannerlordWidgetRenderKind.BrushListPanel;
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

    private sealed class WidgetReflectionSnapshot(
        string installRoot,
        WidgetReflectionLoadContext loadContext,
        Type widgetBaseType,
        ConcurrentDictionary<string, Type> widgetTypesByName,
        ConcurrentDictionary<string, BannerlordWidgetRenderKind> renderKindsByWidgetName,
        IReadOnlyList<string> moduleAssemblyPaths)
    {
        public string InstallRoot { get; } = installRoot;
        public WidgetReflectionLoadContext LoadContext { get; } = loadContext;
        public Type WidgetBaseType { get; } = widgetBaseType;
        public ConcurrentDictionary<string, Type> WidgetTypesByName { get; } = widgetTypesByName;
        public ConcurrentDictionary<string, BannerlordWidgetRenderKind> RenderKindsByWidgetName { get; } = renderKindsByWidgetName;
        public IReadOnlyList<string> ModuleAssemblyPaths { get; } = moduleAssemblyPaths;
        public bool ModulesLoaded { get; set; }
    }
}
