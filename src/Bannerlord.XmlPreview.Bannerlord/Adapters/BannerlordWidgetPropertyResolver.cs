using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    private readonly IBannerlordWidgetReflectionService _reflectionService;
    private readonly Dictionary<WidgetPropertyCacheKey, WidgetPropertyMetadata?> _propertyMetadataCache = [];
    private string? _cachedInstallRoot;

    public BannerlordWidgetPropertyResolver(
        IAppSettingsService appSettingsService,
        IBannerlordWidgetReflectionService reflectionService)
    {
        _appSettingsService = appSettingsService;
        _reflectionService = reflectionService;
    }

    public PrefabWidgetPropertyMap Resolve(
        XElement rootElement,
        PrefabWidgetReferenceMap referenceMap,
        PreviewDiagnostics diagnostics)
    {
        using var timing = diagnostics.MeasurePhase("BannerlordWidgetPropertyResolver.Resolve.Total");
        EnsureCacheMatchesInstallRoot();

        if (!_reflectionService.TryGetWidgetBaseType(out var widgetBaseType))
        {
            return PrefabWidgetPropertyMap.Empty;
        }

        var propertiesByWidgetPath = new Dictionary<string, IReadOnlyDictionary<string, ResolvedWidgetProperty>>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in rootElement.DescendantsAndSelf())
        {
            if (IsStructuralElement(element.Name.LocalName))
            {
                continue;
            }

            if (!_reflectionService.TryGetWidgetType(element.Name.LocalName, out var widgetType))
            {
                continue;
            }

            var widgetPath = PrefabVisualTreeHelper.GetCanonicalElementPath(element);
            var elementProperties = new Dictionary<string, ResolvedWidgetProperty>(StringComparer.OrdinalIgnoreCase);

            foreach (var attribute in element.Attributes())
            {
                if (ShouldSkipAttribute(attribute.Name.LocalName) ||
                    !TryResolveProperty(widgetType, attribute.Name.LocalName, widgetBaseType, out var metadata))
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

    private void EnsureCacheMatchesInstallRoot()
    {
        var installRoot = Path.GetFullPath(_appSettingsService.BannerlordInstallRoot);
        if (PathComparer.Equals(_cachedInstallRoot, installRoot))
        {
            return;
        }

        _propertyMetadataCache.Clear();
        _cachedInstallRoot = installRoot;
    }

    private bool TryResolveProperty(
        Type widgetType,
        string attributeName,
        Type widgetBaseType,
        out WidgetPropertyMetadata metadata)
    {
        var cacheKey = new WidgetPropertyCacheKey(widgetType, attributeName);
        if (_propertyMetadataCache.TryGetValue(cacheKey, out var cachedMetadata))
        {
            if (cachedMetadata.HasValue)
            {
                metadata = cachedMetadata.Value;
                return true;
            }

            metadata = default;
            return false;
        }

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
                _propertyMetadataCache[cacheKey] = null;
                return false;
            }

            currentType = UnwrapNullableType(property.PropertyType);
        }

        if (property is null)
        {
            _propertyMetadataCache[cacheKey] = null;
            return false;
        }

        var kind = ClassifyPropertyKind(currentType, widgetBaseType);
        metadata = new WidgetPropertyMetadata(currentType, GetDeclaredTypeName(currentType), kind, kind != ResolvedWidgetPropertyValueKind.Unknown);
        _propertyMetadataCache[cacheKey] = metadata;
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

    private static bool IsStructuralElement(string elementName)
    {
        return elementName is "Window" or "Children" or "ItemTemplate" or "LogicalChildrenLocation";
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

    private readonly record struct WidgetPropertyMetadata(
        Type Type,
        string DeclaredTypeName,
        ResolvedWidgetPropertyValueKind Kind,
        bool IsSupported);

    private readonly record struct WidgetPropertyCacheKey(
        Type WidgetType,
        string AttributeName);
}
