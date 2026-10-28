using System.IO;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class SandBoxRenderingTests
{
    [InstalledImageFact]
    public void ShippedMissionProgressIndicatorsFillWithoutTheirDanglingHandles()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (_, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
            foreach (var relative in new[] { "Mission/Disguise/MissionLosingTarget.xml", "Mission/MissionQuestBar.xml" })
            {
                var document = loader.Load(Path.Combine(root, "Modules/SandBox/GUI/Prefabs", relative), new PreviewDiagnostics());
                var slider = Nodes(document.RootNode).Single(n => n.Name == "SliderWidget");
                var path = slider.Attributes["Debug.WidgetPath"];
                var fillerPath = slider.Children.Single(n => n.Attributes.GetValueOrDefault("Id") == "Filler").Attributes["Debug.WidgetPath"];
                Assert.False(slider.Attributes.ContainsKey("Debug.WidgetRef.Handle"));
                var bindings = document.BindingKeys.ToDictionary(k => k, _ => "0");
                bindings["IsLosingTarget"] = "true";
                bindings["HasQuestLevel"] = "true";
                bindings["IsEnabled"] = "true";
                bindings["MinimumQuestLevel"] = "0";
                bindings["MaximumQuestLevel"] = "100";
                foreach (var value in new[] { 25, 75 })
                {
                    bindings["LosingTargetRatio"] = value.ToString();
                    bindings["CurrentQuestLevel"] = value.ToString();
                    var diagnostics = new PreviewDiagnostics();
                    renderer.Render(document, diagnostics, new("sandbox-mission-progress", bindings,
                        new Dictionary<string, IReadOnlyDictionary<string, string>>(), [])
                    {
                        PreviewSize = new(1920, 1080),
                        OptionalConnectionExplanations = new Dictionary<string, string>
                        {
                            [path + ":Handle"] = "The shipped display-only slider references SliderHandle but declares no such child."
                        }
                    });
                    var host = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath == path);
                    var filler = Assert.Single(diagnostics.Layout.KeyBounds, b => b.ExpandedPath == fillerPath);
                    Assert.True(host.Width > 0);
                    Assert.Equal(host.Width * value / 100, filler.Width, 6);
                }
            }
            return 0;
        });
    }

    private static IEnumerable<PrefabNode> Nodes(PrefabNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
