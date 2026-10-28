using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class NativeStateRenderingTests
{
    [InstalledImageFact]
    public void AgentStatusPassiveStateShrinksTheRealHealthCanvas()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (_, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
            var document = loader.Load(Path.Combine(root, "Modules/Native/GUI/Prefabs/Mission/AgentStatus.xml"), new PreviewDiagnostics());
            var canvas = Nodes(document.RootNode).Single(n => n.Attributes.GetValueOrDefault("Id") == "HeroHealthWidget")
                .Children.Single(n => n.Attributes.GetValueOrDefault("Id") == "Canvas");
            var path = canvas.Attributes["Debug.WidgetPath"];
            var bindings = document.BindingKeys.ToDictionary(k => k, _ => "0");
            bindings["IsCombatUIActive"] = "true"; bindings["ShowAgentHealthBar"] = "true";
            bindings["AgentHealth"] = "75"; bindings["AgentHealthMax"] = "100";
            var widths = new List<double>();
            foreach (var prioritized in new[] { true, false })
            {
                bindings["IsAgentStatusPrioritized"] = prioritized.ToString();
                var diagnostics = new PreviewDiagnostics();
                var element = renderer.Render(document, diagnostics, new("native-agent-status", bindings,
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(), []) { PreviewSize = new(1920, 1080) });
                var bound = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath == path);
                widths.Add(bound.Width);
                Assert.Equal(double.Parse(canvas.VisualDefinition!.States[prioritized ? "Default" : "Passive"]["SuggestedWidth"], System.Globalization.CultureInfo.InvariantCulture), bound.Width);
                var output = Environment.GetEnvironmentVariable("BANNERLORD_NATIVE_ARTIFACTS");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    var name = prioritized ? "agent-status-default" : "agent-status-passive";
                    WpfPreviewCapture.WritePng(Path.Combine(output, name + ".png"), WpfPreviewCapture.Capture(element, new("native-state-1920x1080", 1920, 1080)));
                    File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new { diagnostics.Layout, diagnostics.BehaviorTrace, Diagnostics = diagnostics.Messages }));
                }
            }
            Assert.Equal(widths[0] * 0.8, widths[1], 6);
            return 0;
        });
    }

    [InstalledImageFact]
    public void ChatLogBooleanBindingMovesTheRealPanelByItsDeclaredOffset()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (_, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
            var path = Path.Combine(root, "Modules/Native/GUI/Prefabs/SPChatLog.xml");
            var document = loader.Load(path, new PreviewDiagnostics());
            var bindings = document.BindingKeys.ToDictionary(k => k, _ => "0");
            var optional = Nodes(document.RootNode).Where(n => n.Attributes.ContainsKey("ClipRect") && n.Attributes.ContainsKey("InnerPanel") && !n.Attributes.ContainsKey("HorizontalScrollbar"))
                .ToDictionary(n => n.Attributes["Debug.WidgetPath"] + ":HorizontalScrollbar", _ => "The Native chat log declares only a vertical scrollbar.");
            var positions = new List<double>();
            foreach (var offset in new[] { false, true })
            {
                bindings["ShouldHaveOffset"] = offset.ToString();
                var diagnostics = new PreviewDiagnostics();
                var element = renderer.Render(document, diagnostics, new("native-chat-log", bindings,
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(), []) { PreviewSize = new(1920, 1080), OptionalConnectionExplanations = optional });
                var panel = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=ChatLogWidget]"));
                positions.Add(panel.X);
                var parent = Elements(element).Single(e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "ChatLogWidgetParent");
                Assert.Equal(offset ? "Offset" : "Default", PreviewElementDebugProperties.GetPreviewState(parent));
                Assert.Null(parent.Effect);
                var output = Environment.GetEnvironmentVariable("BANNERLORD_NATIVE_ARTIFACTS");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    var name = offset ? "chat-log-offset" : "chat-log-default";
                    WpfPreviewCapture.WritePng(Path.Combine(output, name + ".png"), WpfPreviewCapture.Capture(element, new("native-state-1920x1080", 1920, 1080)));
                    File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new { diagnostics.Layout, diagnostics.BehaviorTrace, Diagnostics = diagnostics.Messages }));
                }
            }
            Assert.Equal(160, positions[1] - positions[0]);
            return 0;
        });
    }

    [InstalledImageFact]
    public void EveryNativeBrushWithABaseResolvesInNativeModuleScope()
    {
        var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
        var (assets, _, _) = InstalledImageTests.EnvironmentFor(root);
        var scope = assets.ForPrefab(Path.Combine(root, "Modules/Native/GUI/Prefabs/InitialScreen.xml"));
        var inherited = Directory.EnumerateFiles(Path.Combine(root, "Modules/Native/GUI/Brushes"), "*.xml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Root!.Elements("Brush"))
            .Where(brush => brush.Attribute("BaseBrush") is not null).ToArray();
        Assert.NotEmpty(inherited);
        foreach (var brush in inherited)
        {
            var resolved = scope.FindBrush(brush.Attribute("Name")!.Value);
            Assert.NotNull(resolved);
            var parent = scope.FindBrush(brush.Attribute("BaseBrush")!.Value);
            Assert.NotNull(parent);
            foreach (var layer in parent.Layers)
                Assert.Contains(resolved.Layers, l => l.Name == layer.Name);
        }
    }

    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static IEnumerable<PrefabNode> Nodes(PrefabNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
