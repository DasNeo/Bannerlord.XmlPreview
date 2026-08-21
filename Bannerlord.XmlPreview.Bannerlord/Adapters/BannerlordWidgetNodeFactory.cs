using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Widgets.Bannerlord;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetNodeFactory : IWidgetNodeFactory
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly object _sync = new();
    private WidgetReflectionSnapshot? _snapshot;

    public BannerlordWidgetNodeFactory(IAppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
    }

    public PrefabNode CreateNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        return CreateBuiltInNode(name, attributes, children) ??
               CreateInheritedWidgetNode(name, attributes, children) ??
               new FallbackNode(name, attributes, children);
    }

    private static PrefabNode? CreateBuiltInNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        return name switch
        {
            "Window" => new WindowNode(attributes, children),
            "RichTextWidget" => new RichTextWidgetNode(attributes, children),
            "TextWidget" => new TextWidgetNode(attributes, children),
            "ButtonWidget" => new ButtonWidgetNode(name, attributes, children),
            "Standard.PopupCloseButton" => new ButtonWidgetNode(name, attributes, children),
            "EscapeMenuButtonWidget" => new EscapeMenuButtonWidgetNode(attributes, children),
            "TabControl" => new TabControlNode(attributes, children),
            "ListPanel" => new ListPanelNode(attributes, children),
            "NavigatableListPanel" => new NavigatableListPanelNode(attributes, children),
            "ScrollablePanelFixedHeaderWidget" => new ScrollablePanelFixedHeaderWidgetNode(attributes, children),
            "NavigationScopeTargeter" => new NavigationScopeTargeterNode(attributes, children),
            "ItemTemplate" => new ItemTemplateNode(attributes, children),
            "HintWidget" => new HintWidgetNode(attributes, children),
            "Widget" => new WidgetNode(attributes, children),
            "BrushWidget" => new BrushWidgetNode(attributes, children),
            _ when string.Equals(name, "ScrollablePanel", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("ScrollablePanel", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("ScrollablePanelWidget", StringComparison.OrdinalIgnoreCase) =>
                new ScrollablePanelNode(name, attributes, children),
            _ when string.Equals(name, "ScrollbarWidget", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("ScrollbarWidget", StringComparison.OrdinalIgnoreCase) =>
                new ScrollbarWidgetNode(name, attributes, children),
            _ when name.EndsWith("NavigatableGridWidget", StringComparison.OrdinalIgnoreCase) =>
                new NavigatableGridWidgetNode(name, attributes, children),
            _ when string.Equals(name, "GridWidget", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("GridWidget", StringComparison.OrdinalIgnoreCase) =>
                new GridWidgetNode(name, attributes, children),
            _ => null
        };
    }

    private PrefabNode? CreateInheritedWidgetNode(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<PrefabNode> children)
    {
        var renderKind = ResolveRenderKind(name);
        return renderKind switch
        {
            WidgetRenderKind.Widget => new WidgetNode(attributes, children),
            WidgetRenderKind.BrushWidget => new BrushWidgetNode(attributes, children),
            WidgetRenderKind.ButtonWidget => new ButtonWidgetNode(name, attributes, children),
            WidgetRenderKind.ListPanel => new ListPanelNode(attributes, children),
            WidgetRenderKind.NavigationScopeTargeter => new NavigationScopeTargeterNode(attributes, children),
            WidgetRenderKind.GridWidget => new GridWidgetNode(name, attributes, children),
            WidgetRenderKind.ScrollablePanel => new ScrollablePanelNode(name, attributes, children),
            WidgetRenderKind.ScrollbarWidget => new ScrollbarWidgetNode(name, attributes, children),
            _ => null
        };
    }

    private WidgetRenderKind ResolveRenderKind(string widgetName)
    {
        var snapshot = GetSnapshot();
        return snapshot is not null &&
               snapshot.RenderKindsByWidgetName.TryGetValue(widgetName, out var renderKind)
            ? renderKind
            : WidgetRenderKind.Unknown;
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
        var assemblyPathsByName = CollectAssemblyPaths(installRoot);
        if (assemblyPathsByName.Count == 0)
        {
            return null;
        }

        var loadContext = new WidgetReflectionLoadContext(assemblyPathsByName);
        var loadedAssemblies = new List<Assembly>();
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

        var renderKindsByWidgetName = new Dictionary<string, WidgetRenderKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in loadedAssemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                var renderKind = ClassifyRenderKind(type);
                if (renderKind == WidgetRenderKind.Unknown)
                {
                    continue;
                }

                renderKindsByWidgetName.TryAdd(type.Name, renderKind);
            }
        }

        return renderKindsByWidgetName.Count == 0
            ? null
            : new WidgetReflectionSnapshot(installRoot, loadContext, renderKindsByWidgetName);
    }

    private static Dictionary<string, string> CollectAssemblyPaths(string installRoot)
    {
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

    private static WidgetRenderKind ClassifyRenderKind(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            switch (current.FullName)
            {
                case "TaleWorlds.GauntletUI.BaseTypes.ScrollbarWidget":
                    return WidgetRenderKind.ScrollbarWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.ScrollablePanel":
                    return WidgetRenderKind.ScrollablePanel;
                case "TaleWorlds.GauntletUI.BaseTypes.GridWidget":
                    return WidgetRenderKind.GridWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.ListPanel":
                    return WidgetRenderKind.ListPanel;
                case "TaleWorlds.GauntletUI.BaseTypes.NavigationScopeTargeter":
                    return WidgetRenderKind.NavigationScopeTargeter;
                case "TaleWorlds.GauntletUI.BaseTypes.ButtonWidget":
                    return WidgetRenderKind.ButtonWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.BrushWidget":
                    return WidgetRenderKind.BrushWidget;
                case "TaleWorlds.GauntletUI.BaseTypes.Widget":
                    return WidgetRenderKind.Widget;
            }
        }

        return WidgetRenderKind.Unknown;
    }

    private enum WidgetRenderKind
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
        IReadOnlyDictionary<string, WidgetRenderKind> RenderKindsByWidgetName);
}
