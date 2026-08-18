using Bannerlord.XmlPreview.Models.Assets;

namespace Bannerlord.XmlPreview.Models;

public sealed class PreviewDiagnostics
{
    private readonly List<string> _messages = [];
    private readonly Dictionary<string, string> _resolvedConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedBrushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resolvedSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedMessages = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Messages => _messages;

    public void Add(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _messages.Add(message);
        }
    }

    public string ToDisplayText()
    {
        return _messages.Count == 0
            ? "No warnings. The preview used built-in placeholder assets and mock bindings."
            : string.Join(Environment.NewLine, _messages);
    }

    public void AddResolvedConstant(string constantName, string brushName, string brushLayerName, string valueType, double value)
    {
        _resolvedConstants[constantName] =
            $"{valueType} = {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} via {brushName}/{brushLayerName}";
    }

    public void AddResolvedBrush(string requestedName, BannerlordBrushDefinition brush)
    {
        var metadata = new List<string>();
        if (!string.IsNullOrWhiteSpace(brush.FontName))
        {
            metadata.Add($"Font: {brush.FontName}");
        }

        var defaultStyle = brush.Styles.TryGetValue("Default", out var style)
            ? style
            : brush.Styles.Values.FirstOrDefault();
        if (defaultStyle is not null)
        {
            if (!string.IsNullOrWhiteSpace(defaultStyle.FontColor))
            {
                metadata.Add($"FontColor: {defaultStyle.FontColor}");
            }

            if (defaultStyle.FontSize.HasValue)
            {
                metadata.Add($"FontSize: {defaultStyle.FontSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }

        var layerNames = brush.Layers
            .Where(layer => !string.IsNullOrWhiteSpace(layer.SpriteName) || !string.IsNullOrWhiteSpace(layer.Color))
            .Select(layer =>
            {
                var segments = new List<string> { layer.Name };
                if (!string.IsNullOrWhiteSpace(layer.SpriteName))
                {
                    segments.Add($"Sprite={layer.SpriteName}");
                }

                if (!string.IsNullOrWhiteSpace(layer.Color))
                {
                    segments.Add($"Color={layer.Color}");
                }

                return string.Join(" ", segments);
            })
            .ToArray();

        var lines = new List<string>();
        lines.AddRange(metadata);
        if (layerNames.Length > 0)
        {
            lines.AddRange(layerNames);
        }

        lines.Add(brush.SourcePath);
        _resolvedBrushes[requestedName] = string.Join(Environment.NewLine, lines);
    }

    public void AddResolvedSprite(string requestedName, BannerlordSpriteAssetResolution resolution)
    {
        var lines = new List<string> { $"Status: {resolution.Kind}" };
        if (resolution.SpriteDefinition is not null)
        {
            var sprite = resolution.SpriteDefinition;
            lines.Add(
                $"Catalog: {sprite.Name} [{sprite.Kind}] {sprite.Width}x{sprite.Height} in {sprite.CategoryName} sheet {sprite.SheetId}");
            lines.Add($"SpritePart: {sprite.SpritePartName}");
            lines.Add($"SpriteSource: {sprite.SourcePath}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.MatchedKey))
        {
            lines.Add($"ImageKey: {resolution.MatchedKey}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.ImagePath))
        {
            lines.Add($"ImagePath: {resolution.ImagePath}");
        }

        if (!string.IsNullOrWhiteSpace(resolution.Note))
        {
            lines.Add($"Note: {resolution.Note}");
        }

        _resolvedSprites[requestedName] = string.Join(Environment.NewLine, lines);

        if (resolution.Kind == SpriteAssetResolutionKind.Missing)
        {
            AddOnce($"Sprite '{requestedName}' is missing. {resolution.Note ?? "No extracted PNG matched the request."}");
        }
        else if (resolution.UsesFallback)
        {
            AddOnce($"Sprite '{requestedName}' used fallback '{resolution.MatchedKey}'.");
        }
    }

    private void AddOnce(string message)
    {
        if (_reportedMessages.Add(message))
        {
            _messages.Add(message);
        }
    }

    public string ToAssetDebugText()
    {
        var sections = new List<string>();

        if (_resolvedConstants.Count > 0)
        {
            sections.Add("Constants");
            sections.AddRange(_resolvedConstants
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}: {entry.Value}"));
        }

        if (_resolvedBrushes.Count > 0)
        {
            if (sections.Count > 0)
            {
                sections.Add(string.Empty);
            }

            sections.Add("Brushes");
            sections.AddRange(_resolvedBrushes
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}:{Environment.NewLine}{entry.Value}"));
        }

        if (_resolvedSprites.Count > 0)
        {
            if (sections.Count > 0)
            {
                sections.Add(string.Empty);
            }

            sections.Add("Sprites");
            sections.AddRange(_resolvedSprites
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}:{Environment.NewLine}{entry.Value}"));
        }

        return sections.Count == 0
            ? "No Bannerlord asset metadata has been resolved yet."
            : string.Join(Environment.NewLine, sections);
    }
}
