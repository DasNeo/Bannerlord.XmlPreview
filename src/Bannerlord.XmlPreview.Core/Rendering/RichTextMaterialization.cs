using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace Bannerlord.XmlPreview.Rendering;

public sealed partial class PrefabRenderContext
{
    public FrameworkElement CreateRichTextVisual(TextBlock text, string? brushId, IReadOnlyDictionary<string, string> attributes)
    {
        if (!Regex.IsMatch(text.Text, @"</?[a-zA-Z]+(?:\s|/?>)"))
            return CreateTextVisual(text, brushId, attributes);
        try
        {
            var markup = Regex.Replace(text.Text.Replace("&nbsp;", " "), @"&(?!#\d+;|#x[0-9a-fA-F]+;|amp;|lt;|gt;|quot;|apos;)", "&amp;");
            markup = Regex.Replace(markup, @"<(img|br)(\s[^<>]*?)?(?<!/)>", "<$1$2/>", RegexOptions.IgnoreCase);
            var root = XElement.Parse("<root>" + markup + "</root>");
            var panel = new RichTextLayoutPanel { HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = text.VerticalAlignment,
                TextAlignment = text.TextAlignment };
            void Visit(XNode node, Brush foreground, FontWeight weight, FontStyle style)
            {
                if (node is XText content)
                {
                    foreach (Match token in Regex.Matches(content.Value, @"\r?\n|[^\S\r\n]+|[^\s]+"))
                    {
                        if (token.Value.Contains('\n')) { panel.Children.Add(new Border { Tag = "LineBreak" }); continue; }
                        var run = new TextBlock { Text = token.Value, FontSize = text.FontSize, FontFamily = text.FontFamily,
                            FontWeight = weight, FontStyle = style, Foreground = foreground, TextWrapping = TextWrapping.NoWrap };
                        panel.Children.Add(CreateTextVisual(run, brushId, attributes));
                    }
                }
                else if (node is XElement element)
                {
                    var tag = element.Name.LocalName.ToLowerInvariant();
                    if (tag == "br") { panel.Children.Add(new Border { Tag = "LineBreak" }); return; }
                    if (tag == "img")
                    {
                        var identity = element.Attribute("src")?.Value ?? "";
                        if (identity.Length == 0) { Incomplete("richtext.sprite.missing '(empty)' "); return; }
                        var definition = _assetCatalog.FindSprite(identity);
                        var visual = CreateSpriteOverlay(identity);
                        if (visual is null) return;
                        double Dimension(string name, double fallback) => double.TryParse(element.Attribute(name)?.Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0 ? value : fallback;
                        visual.Height = Dimension("height", text.FontSize);
                        visual.Width = Dimension("width", definition is { Height: > 0 } ? visual.Height * definition.Width / definition.Height : visual.Height);
                        panel.Children.Add(visual);
                        if (IsSpriteMissing(identity)) Incomplete($"richtext.sprite.missing '{identity}'");
                        return;
                    }
                    if (tag is not ("span" or "a" or "b" or "i" or "font")) Incomplete($"richtext.tag.unsupported '{tag}'");
                    if (element.Attribute("style") is not null) Incomplete("richtext.named-style.unsupported");
                    var color = ParseBannerlordColor(element.Attribute("color")?.Value);
                    foreach (var child in element.Nodes()) Visit(child, color is { } value ? new SolidColorBrush(value) : foreground,
                        tag == "b" ? FontWeights.Bold : weight, tag == "i" ? FontStyles.Italic : style);
                }
            }
            foreach (var node in root.Nodes()) Visit(node, text.Foreground, text.FontWeight, text.FontStyle);
            return panel;
        }
        catch (XmlException)
        {
            Incomplete("richtext.markup.invalid; showing the original text");
            return CreateTextVisual(text, brushId, attributes);
        }
        void Incomplete(string reason)
        {
            var message = $"{TryGetWidgetPath(attributes)}: {reason}.";
            _diagnostics.Add(message);
            _diagnostics.MarkTruncated(message);
        }
    }
}

internal sealed class RichTextLayoutPanel : Panel
{
    internal TextAlignment TextAlignment { get; init; }
    private readonly List<(FrameworkElement Element, Rect Bounds)> _placements = [];
    private readonly List<(int Start, int Count, double Width)> _lines = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _placements.Clear(); _lines.Clear();
        var x = 0d; var y = 0d; var height = 0d; var width = 0d; var start = 0;
        void Line()
        {
            _lines.Add((start, _placements.Count - start, x)); start = _placements.Count;
            width = Math.Max(width, x); y += height; x = 0; height = 0;
        }
        foreach (var element in InternalChildren.OfType<FrameworkElement>())
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (element.Tag as string == "LineBreak") { Line(); continue; }
            var size = element.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width) Line();
            _placements.Add((element, new Rect(x, y, size.Width, size.Height)));
            x += size.Width; height = Math.Max(height, size.Height);
        }
        Line();
        return new Size(width, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var line in _lines)
        {
            var offset = TextAlignment == TextAlignment.Center ? (finalSize.Width - line.Width) / 2 : TextAlignment == TextAlignment.Right ? finalSize.Width - line.Width : 0;
            var placements = _placements.Skip(line.Start).Take(line.Count).ToArray();
            var height = placements.Select(p => p.Bounds.Height).DefaultIfEmpty().Max();
            foreach (var (element, bounds) in placements)
                element.Arrange(new Rect(bounds.X + offset, bounds.Y + (height - bounds.Height) / 2, bounds.Width, bounds.Height));
        }
        return finalSize;
    }
}
