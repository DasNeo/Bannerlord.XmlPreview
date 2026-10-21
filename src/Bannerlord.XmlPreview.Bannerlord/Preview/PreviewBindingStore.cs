using System.Collections.Immutable;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.DemoData;

namespace Bannerlord.XmlPreview.Preview.Bannerlord;

public sealed class PreviewBindingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _bindings = new(PreviewDemoCatalog.MockBindings, StringComparer.OrdinalIgnoreCase);
    public string Resolve(string key) { lock (_gate) return _bindings.GetValueOrDefault(key, key); }
    public void Set(string key, string value) { lock (_gate) _bindings[key] = value; }
    public void Replace(IReadOnlyDictionary<string, string> values)
    {
        lock (_gate) { _bindings.Clear(); foreach (var pair in values) _bindings[pair.Key] = pair.Value; }
    }
    public PreviewDataSnapshot Capture()
    {
        lock (_gate) return new()
        {
            Bindings = _bindings.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            PanelColors = PreviewDemoCatalog.PanelBrushes.ToImmutableDictionary(p => p.Key, p => new PreviewPanelColors(p.Value.FillHex, p.Value.StrokeHex), StringComparer.OrdinalIgnoreCase),
            TextColors = PreviewDemoCatalog.TextBrushColors.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            FrameBorderColor = PreviewDemoCatalog.FrameBorderColor
        };
    }
}
