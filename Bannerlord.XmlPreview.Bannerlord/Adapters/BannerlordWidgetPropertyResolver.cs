using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Adapters.Bannerlord;

public sealed class BannerlordWidgetPropertyResolver : IWidgetPropertyResolver
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> SkippedAttributePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Debug.",
        "Command.",
        "Parameter."
    };

    private readonly IAppSettingsService _appSettingsService;
    private readonly object _sync = new();
    private WidgetReflectionSnapshot? _snapshot;

    public BannerlordWidgetPropertyResolver(IAppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
    }

    public PrefabWidgetPropertyMap Resolve(
        XElement rootElement,
        PrefabWidgetReferenceMap referenceMap,
        PreviewDiagnostics diagnostics)
    {
        var snapshot = GetSnapshot();
        if (snapshot is null)
        {
            return PrefabWidgetPropertyMap.Empty;
        }

        var propertiesByWidgetPath = new Dictionary<string, IReadOnlyDictionary<string, ResolvedWidgetProperty>>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in rootElement.DescendantsAndSelf())
        {
            if (!snapshot.WidgetTypesByName.TryGetValue(element.Name.LocalName, out var widgetType))
            {
                continue;
            }

            var widgetPath = PrefabVisualTreeHelper.GetCanonicalElementPath(element);
            var elementProperties = new Dictionary<string, ResolvedWidgetProperty>(StringComparer.OrdinalIgnoreCase);

            foreach (var attribute in element.Attributes())
            {
                if (ShouldSkipAttribute(attribute.Name.LocalName) ||
                    !TryResolveProperty(widgetType, attribute.Name.LocalName, snapshot.WidgetBaseType, out var metadata))
                {
                    continue;
                }

                var property = CreateResolvedProperty(widgetPath, attribute.Name.LocalName, attribute.Value, metadata, referenceMap);
                elementProperties[attribute.Name.LocalName] = property;
                diagnostics.AddResolvedWidgetProperty(
                    widgetPath,
                    property.AttributeName,
                    property.RawValue,
                    property.DeclaredTypeName,
                    property.ValueKind,
                    property.IsSupported);
            }

            if (elementProperties.Count > 0)
            {
                propertiesByWidgetPath[widgetPath] = elementProperties;
            }
        }

        return propertiesByWidgetPath.Count == 0
            ? PrefabWidgetPropertyMap.Empty
            : new PrefabWidgetPropertyMap(propertiesByWidgetPath);
    }

    private WidgetReflectionSnapshot? GetSnapshot()
    {
        var installRoot = Path.GetFullPath(_appSettingsService.BannerlordInstallRoot);
        lock (_sync)
        {
            if (_snapshot is not null &&
                PathComparer.Equals(_snapshot.InstallRoot, installRoot))
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

        var widgetBaseType = loadedAssemblies
            .Select(assembly => assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", throwOnError: false, ignoreCase: false))
            .FirstOrDefault(type => type is not null);
        if (widgetBaseType is null)
        {
            loadContext.Unload();
            return null;
        }

        var widgetTypesByName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
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
            }
        }

        return widgetTypesByName.Count == 0
            ? null
            : new WidgetReflectionSnapshot(installRoot, loadContext, widgetBaseType, widgetTypesByName);
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

    private static bool TryResolveProperty(
        Type widgetType,
        string attributeName,
        Type widgetBaseType,
        out WidgetPropertyMetadata metadata)
    {
        metadata = default;
        var currentType = widgetType;
        PropertyInfo? property = null;
        foreach (var segment in attributeName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            property = currentType.GetProperty(
                segment,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null)
            {
                return false;
            }

            currentType = UnwrapNullableType(property.PropertyType);
        }

        if (property is null)
        {
            return false;
        }

        var kind = ClassifyPropertyKind(currentType, widgetBaseType);
        metadata = new WidgetPropertyMetadata(currentType, GetDeclaredTypeName(currentType), kind, kind != ResolvedWidgetPropertyValueKind.Unknown);
        return true;
    }

    private static ResolvedWidgetProperty CreateResolvedProperty(
        string widgetPath,
        string attributeName,
        string rawValue,
        WidgetPropertyMetadata metadata,
        PrefabWidgetReferenceMap referenceMap)
    {
        string? referenceTargetPath = null;
        if (metadata.Kind == ResolvedWidgetPropertyValueKind.WidgetReference &&
            referenceMap.TryGetReference(widgetPath, attributeName, out var reference) &&
            reference.IsResolved)
        {
            referenceTargetPath = reference.TargetWidgetPath;
        }

        var (parsedValue, hasParsedValue) = ParseValue(rawValue, metadata, referenceTargetPath);
        return new ResolvedWidgetProperty(
            widgetPath,
            attributeName,
            rawValue,
            metadata.DeclaredTypeName,
            metadata.Kind,
            parsedValue,
            hasParsedValue,
            metadata.IsSupported,
            referenceTargetPath);
    }

    private static (object? ParsedValue, bool HasParsedValue) ParseValue(
        string rawValue,
        WidgetPropertyMetadata metadata,
        string? referenceTargetPath)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (rawValue, false);
        }

        if (IsDynamicValue(rawValue))
        {
            return metadata.Kind switch
            {
                ResolvedWidgetPropertyValueKind.String => (rawValue, true),
                ResolvedWidgetPropertyValueKind.Brush => (rawValue, true),
                ResolvedWidgetPropertyValueKind.Sprite => (rawValue, true),
                ResolvedWidgetPropertyValueKind.VisualDefinition => (rawValue, true),
                ResolvedWidgetPropertyValueKind.XmlElement => (rawValue, true),
                _ => (rawValue, false)
            };
        }

        return metadata.Kind switch
        {
            ResolvedWidgetPropertyValueKind.Boolean when bool.TryParse(rawValue, out var boolValue)
                => (boolValue, true),
            ResolvedWidgetPropertyValueKind.Integer when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                => (intValue, true),
            ResolvedWidgetPropertyValueKind.Float when double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
                => (doubleValue, true),
            ResolvedWidgetPropertyValueKind.Enum => TryParseEnum(metadata.Type, rawValue),
            ResolvedWidgetPropertyValueKind.WidgetReference when !string.IsNullOrWhiteSpace(referenceTargetPath)
                => (referenceTargetPath, true),
            ResolvedWidgetPropertyValueKind.String => (rawValue, true),
            ResolvedWidgetPropertyValueKind.Color => (rawValue, true),
            ResolvedWidgetPropertyValueKind.Brush => (rawValue, true),
            ResolvedWidgetPropertyValueKind.Sprite => (rawValue, true),
            ResolvedWidgetPropertyValueKind.VisualDefinition => (rawValue, true),
            ResolvedWidgetPropertyValueKind.XmlElement => (rawValue, true),
            _ => (rawValue, false)
        };
    }

    private static (object? ParsedValue, bool HasParsedValue) TryParseEnum(Type enumType, string rawValue)
    {
        try
        {
            return (Enum.Parse(enumType, rawValue, ignoreCase: true), true);
        }
        catch
        {
            return (rawValue, false);
        }
    }

    private static bool IsDynamicValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        return value[0] is '@' or '!' or '*' ||
               value.IndexOfAny(['{', '}', ':']) >= 0;
    }

    private static bool ShouldSkipAttribute(string attributeName)
    {
        foreach (var prefix in SkippedAttributePrefixes)
        {
            if (attributeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetDeclaredTypeName(Type type)
    {
        var nonNullableType = UnwrapNullableType(type);
        return nonNullableType.FullName ?? nonNullableType.Name;
    }

    private static Type UnwrapNullableType(Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    private static ResolvedWidgetPropertyValueKind ClassifyPropertyKind(Type type, Type widgetBaseType)
    {
        if (type == typeof(bool))
        {
            return ResolvedWidgetPropertyValueKind.Boolean;
        }

        if (type == typeof(byte) ||
            type == typeof(short) ||
            type == typeof(int) ||
            type == typeof(long))
        {
            return ResolvedWidgetPropertyValueKind.Integer;
        }

        if (type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal))
        {
            return ResolvedWidgetPropertyValueKind.Float;
        }

        if (type.IsEnum)
        {
            return ResolvedWidgetPropertyValueKind.Enum;
        }

        if (type == typeof(string))
        {
            return ResolvedWidgetPropertyValueKind.String;
        }

        if (widgetBaseType.IsAssignableFrom(type))
        {
            return ResolvedWidgetPropertyValueKind.WidgetReference;
        }

        return type.FullName switch
        {
            "TaleWorlds.Library.Color" => ResolvedWidgetPropertyValueKind.Color,
            "TaleWorlds.TwoDimension.Brush" => ResolvedWidgetPropertyValueKind.Brush,
            "TaleWorlds.TwoDimension.Sprite" => ResolvedWidgetPropertyValueKind.Sprite,
            "TaleWorlds.GauntletUI.PrefabSystem.VisualDefinition" => ResolvedWidgetPropertyValueKind.VisualDefinition,
            "System.Xml.XmlElement" => ResolvedWidgetPropertyValueKind.XmlElement,
            _ => ResolvedWidgetPropertyValueKind.Unknown
        };
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
        IReadOnlyDictionary<string, Type> WidgetTypesByName);

    private readonly record struct WidgetPropertyMetadata(
        Type Type,
        string DeclaredTypeName,
        ResolvedWidgetPropertyValueKind Kind,
        bool IsSupported);
}
