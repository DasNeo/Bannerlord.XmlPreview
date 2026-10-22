using System.Globalization;

namespace Bannerlord.XmlPreview.Models;

public sealed record ResolvedWidgetProperty(
    string WidgetPath,
    string AttributeName,
    string RawValue,
    string DeclaredTypeName,
    ResolvedWidgetPropertyValueKind ValueKind,
    object? ParsedValue,
    bool HasParsedValue,
    bool IsSupported,
    string? ReferenceTargetPath = null)
{
    public bool TryGetBoolean(out bool value)
    {
        if (ParsedValue is bool booleanValue)
        {
            value = booleanValue;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetInteger(out int value)
    {
        if (ParsedValue is int integerValue)
        {
            value = integerValue;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetDouble(out double value)
    {
        switch (ParsedValue)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case int integerValue:
                value = integerValue;
                return true;
            default:
                value = default;
                return false;
        }
    }

    public string GetDisplayValue()
    {
        if (ValueKind == ResolvedWidgetPropertyValueKind.WidgetReference &&
            !string.IsNullOrWhiteSpace(ReferenceTargetPath))
        {
            return $"{RawValue} -> {ReferenceTargetPath}";
        }

        return ParsedValue switch
        {
            null => RawValue,
            double doubleValue => doubleValue.ToString("0.###", CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("0.###", CultureInfo.InvariantCulture),
            bool booleanValue => booleanValue ? "true" : "false",
            _ => ParsedValue.ToString() ?? RawValue
        };
    }
}
