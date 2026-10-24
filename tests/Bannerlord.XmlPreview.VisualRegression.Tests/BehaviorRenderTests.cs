using System.Collections.Immutable;
using System.Windows;
using System.Windows.Media;
using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.Behaviors.Bannerlord;
using Bannerlord.XmlPreview.Preview.Bannerlord;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class BehaviorRenderTests
{
    [Theory]
    [InlineData(false, false, 12, 10)]
    [InlineData(false, true, 12, 6)]
    [InlineData(true, true, 20, 10)]
    public void BooleanStateSelectsVisualGeometryBeforeLayoutAndPropagatesOnlyWhenRequested(bool flag, bool includeChildren, double targetWidth, double childWidth)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            PrefabVisualDefinition Definition(string active, string passive) => new(new Dictionary<string, ImmutableDictionary<string, string>>
            {
                ["Default"] = new Dictionary<string, string> { ["SuggestedWidth"] = active }.ToImmutableDictionary(),
                ["Active"] = new Dictionary<string, string> { ["SuggestedWidth"] = active }.ToImmutableDictionary(),
                ["Passive"] = new Dictionary<string, string> { ["SuggestedWidth"] = passive }.ToImmutableDictionary()
            }.ToImmutableDictionary());
            const string path = "Widget[Id=target]";
            var child = new WidgetNode(Attributes(path + " > Widget[Id=child]", ("Id", "child")), []).WithVisualDefinition(Definition("10", "6"));
            var changer = new BrushWidgetNode("BoolStateChangerWidget", Attributes(path + " > BoolStateChangerWidget", ("TargetWidget", "..\\."),
                ("BooleanCheck", "@Flag"), ("TrueState", "Active"), ("FalseState", "Passive"), ("IncludeChildren", includeChildren.ToString())), []);
            var target = new WidgetNode(Attributes(path, ("Id", "target")), [child, changer]).WithVisualDefinition(Definition("20", "12"));
            var diagnostics = new PreviewDiagnostics();
            var rendered = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider())
                .Render(Document(target), diagnostics, Options() with { Bindings = new Dictionary<string, string> { ["Flag"] = flag.ToString() } });
            Assert.Equal(targetWidth, rendered.ActualWidth);
            Assert.Equal(childWidth, Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=child]")).Width);
            Assert.Equal(flag ? "Active" : "Passive", PreviewElementDebugProperties.GetPreviewState(rendered));
            Assert.Null(rendered.Effect);
            Assert.DoesNotContain(diagnostics.BehaviorTrace.SelectMany(t => t.Contributions), c => c.Contribution is LayoutContribution { Intent: LayoutIntent.TargetState });
            Assert.False(target.Attributes.ContainsKey("CurrentState"));
            return 0;
        });
    }

    [Fact]
    public void BooleanStateMissingDeclaredTargetFailsBeforeMaterialization()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new MustNotRenderNode("BoolStateChangerWidget", Attributes("BoolStateChangerWidget", ("TargetWidget", "missing"), ("TrueState", "Active"), ("FalseState", "Passive")));
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            Assert.Equal("MissingRequiredConnection", Assert.Throws<BehaviorGraphException>(() => renderer.Render(Document(node), new PreviewDiagnostics(), Options())).Error.Code);
            Assert.False(node.Rendered);
            return 0;
        });
    }

    [Fact]
    public void CoverChildrenStackCountsEmptyPersuasionMarginsOnce()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var child = new System.Windows.Controls.Border
            {
                Width = 0, Height = 0, Margin = new Thickness(50, 0, 50, 0)
            };
            var panel = new GauntletStackLayoutPanel
            {
                LayoutMethod = GauntletStackLayoutMethod.VerticalTopToBottom
            };
            panel.Children.Add(child);
            panel.Measure(new Size(1920, 1080));
            Assert.Equal(100, panel.DesiredSize.Width);
            Assert.Equal(0, panel.DesiredSize.Height);
            panel.Arrange(new Rect(new Point(), panel.DesiredSize));
            Assert.Equal(50, child.TranslatePoint(new Point(), panel).X);
            Assert.Equal(0, child.ActualWidth);
            return 0;
        });
    }

    private static Dictionary<string, string> Attributes(string path, params (string Key, string Value)[] values)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Debug.WidgetPath"] = path, ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "20", ["SuggestedHeight"] = "10" };
        foreach (var (key, value) in values) attributes[key] = value;
        return attributes;
    }
    private static PrefabDocument Document(PrefabNode root) => new("fixture.xml", root, new Dictionary<string, string>(), new HashSet<string>(), PrefabWidgetPropertyMap.Empty, new WidgetReferenceResolver().Resolve(root, new PreviewDiagnostics()));
    private static RegressionRenderOptions Options() => new("fixture.xml", new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>(), []);
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Fact] public void ValueVisibilityUsesLocalResolvedState()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new WidgetNode("ValueBasedVisibilityWidget", Attributes("ValueBasedVisibilityWidget", ("IndexToWatch", "@Index"), ("IndexToBeVisible", "2")), []);
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            var doc = Document(node);
            var first = renderer.Render(doc, new PreviewDiagnostics(), Options() with { Bindings = new Dictionary<string, string> { ["Index"] = "1" } });
            var second = renderer.Render(doc, new PreviewDiagnostics(), Options() with { Bindings = new Dictionary<string, string> { ["Index"] = "2" } });
            Assert.Equal(Visibility.Collapsed, first.Visibility); Assert.Equal(Visibility.Visible, second.Visibility);
            Assert.Equal("@Index", node.Attributes["IndexToWatch"]);
            return 0;
        });
    }
    [Fact] public void DimensionSyncUsesMeasuredSourceAndPaddingBeforeFinalArrange()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var source = new WidgetNode(Attributes("Widget > Widget[Id=source]", ("Id", "source"), ("SuggestedWidth", "7"), ("SuggestedHeight", "4")), []);
            var target = new WidgetNode("DimensionSyncWidget", Attributes("Widget > DimensionSyncWidget[Id=target]", ("Id", "target"), ("WidgetToCopyHeightFrom", "..\\source"), ("DimensionToSync", "HorizontalAndVertical"), ("PaddingAmount", "3")), []);
            var doc = Document(new WidgetNode(Attributes("Widget"), [source, target]));
            var diagnostics = new PreviewDiagnostics();
            SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(doc, diagnostics, Options());
            var bounds = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=target]"));
            Assert.Equal(10, bounds.Width); Assert.Equal(7, bounds.Height);
            Assert.True(diagnostics.BehaviorGraph!.ContributionCounts[BehaviorPhase.PrepareLayout] > 0);
            return 0;
        });
    }
    [Fact] public void StateSyncPropagatesAcrossMultipleWidgetsInDependencyOrder()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var source = new WidgetNode(Attributes("Widget > Widget[Id=source]", ("Id", "source"), ("IsDisabled", "true")), []);
            var middle = new WidgetNode("StateSyncWidget", Attributes("Widget > StateSyncWidget[Id=middle]", ("Id", "middle"), ("SourceWidget", "..\\source")), []);
            var last = new WidgetNode("StateSyncWidget", Attributes("Widget > StateSyncWidget[Id=last]", ("Id", "last"), ("SourceWidget", "..\\middle")), []);
            var diagnostics = new PreviewDiagnostics();
            var root = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(new WidgetNode(Attributes("Widget"), [last, middle, source])), diagnostics, Options());
            foreach (var id in new[] { "middle", "last" })
                Assert.Equal("Disabled", PreviewElementDebugProperties.GetPreviewState(Assert.Single(Elements(root), e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == id)));
            var traces = diagnostics.BehaviorTrace.SelectMany(t => t.Contributions).Where(c => c.Contribution is ConnectionContribution { StateSource: not null }).ToArray();
            Assert.Equal(2, traces.Length);
            return 0;
        });
    }
    [Fact] public void ScrollViewportClipsAndResolvesInnerPanelAndBothBars()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            const string path = "ScrollablePanel";
            var inner = new WidgetNode(Attributes(path + " > Widget[Id=clip] > Widget[Id=inner]", ("Id", "inner"), ("SuggestedWidth", "40"), ("SuggestedHeight", "30")), []);
            var clip = new WidgetNode(Attributes(path + " > Widget[Id=clip]", ("Id", "clip")), [inner]);
            var h = new WidgetNode(Attributes(path + " > Widget[Id=h]", ("Id", "h")), []);
            var v = new WidgetNode(Attributes(path + " > Widget[Id=v]", ("Id", "v")), []);
            var root = new WidgetNode("ScrollablePanel", Attributes(path, ("ClipRect", "clip"), ("InnerPanel", "clip\\inner"), ("HorizontalScrollbar", "h"), ("VerticalScrollbar", "v")), [clip, h, v]);
            var diagnostics = new PreviewDiagnostics();
            var rendered = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(root), diagnostics, Options());
            Assert.Contains(Elements(rendered), e => e.ClipToBounds);
            Assert.Equal(4, diagnostics.BehaviorGraph!.ContributionCounts[BehaviorPhase.Connect]);
            Assert.Equal(5, diagnostics.Layout!.KeyBounds.Count);
            return 0;
        });
    }
    [Fact] public void RequiredConnectionsFailBeforeAnyWpfMaterialization()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new MustNotRenderNode("DimensionSyncWidget", Attributes("DimensionSyncWidget", ("DimensionToSync", "Vertical")));
            var diagnostics = new PreviewDiagnostics();
            Assert.Equal("MissingRequiredConnection", Assert.Throws<BehaviorGraphException>(() => SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(node), diagnostics)).Error.Code);
            Assert.False(node.Rendered); Assert.Single(diagnostics.BehaviorTrace);
            return 0;
        });
    }
    [Fact] public void UnknownAndCodeWidgetsAreVisiblePartialPlaceholders()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            foreach (var name in new[] { "UnknownModWidget", "ModCodeWidget" })
            {
                var diagnostics = new PreviewDiagnostics();
                var root = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(new FallbackNode(name, Attributes(name), [])), diagnostics, Options());
                Assert.Equal(Visibility.Visible, root.Visibility); Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
                Assert.Equal(1, diagnostics.BehaviorGraph!.SupportCounts[BehaviorSupport.Partial]);
            }
            return 0;
        });
    }
    [Fact] public async Task ParallelScenariosCannotLeakOverridesOrBindingsOnTheSameDocument()
    {
        var attrs = Attributes("ValueBasedVisibilityWidget", ("IndexToWatch", "@Index"), ("IndexToBeVisible", "2"));
        var doc = Document(new WidgetNode("ValueBasedVisibilityWidget", attrs, []));
        var barrier = new Barrier(2);
        Task<Visibility> Render(string index, string overrideValue) => Task.Run(() => SnapshotCaptureTests.Sta(() =>
        {
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            barrier.SignalAndWait();
            return renderer.Render(doc, new PreviewDiagnostics(), Options() with { Bindings = new Dictionary<string, string> { ["Index"] = index }, WidgetStates = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["ValueBasedVisibilityWidget"] = new Dictionary<string, string> { ["IndexToBeVisible"] = overrideValue } } }).Visibility;
        }));
        var values = await Task.WhenAll(Render("1", "1"), Render("1", "2"));
        Assert.Equal(new[] { Visibility.Visible, Visibility.Collapsed }, values); Assert.Equal("2", attrs["IndexToBeVisible"]);
    }
    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 25, 5)]
    [InlineData(false, 100, 20)]
    [InlineData(true, 25, 2.5)]
    public void DisplayOnlySliderFillsWithoutAHandleWhenScenarioExplainsItsAbsence(bool vertical, double value, double expectedExtent)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var filler = new WidgetNode(Attributes("SliderWidget > Widget[Id=filler]", ("Id", "filler")), []);
            var node = new WidgetNode("SliderWidget", Attributes("SliderWidget", ("Handle", "missing"), ("Filler", "filler"),
                ("AlignmentAxis", vertical ? "Vertical" : "Horizontal"), ("MaxValueFloat", "100"),
                ("ValueFloat", value.ToString(System.Globalization.CultureInfo.InvariantCulture))), [filler]);
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            Assert.Equal("MissingOptionalExplanation", Assert.Throws<BehaviorGraphException>(() => renderer.Render(Document(node), new PreviewDiagnostics(), Options())).Error.Code);
            var diagnostics = new PreviewDiagnostics();
            renderer.Render(Document(node), diagnostics, Options() with
            {
                OptionalConnectionExplanations = new Dictionary<string, string> { ["SliderWidget:Handle"] = "Display-only progress indicator has no handle." }
            });
            var bounds = Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=filler]"));
            Assert.Equal(expectedExtent, vertical ? bounds.Height : bounds.Width, 6);
            Assert.Contains(diagnostics.Messages, m => m.Contains("Display-only progress indicator"));
            return 0;
        });
    }

    [Theory]
    [InlineData(-1, 0, 0, false)] [InlineData(5, 9, 10, false)] [InlineData(20, 18, 20, false)] [InlineData(3, 7.2, 8, true)]
    public void SliderRangeHasExplicitHandleAndFillerBounds(double value, double expectedX, double expectedFillWidth, bool discrete)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var handle = new WidgetNode(Attributes("SliderWidget > Widget[Id=handle]", ("Id", "handle"), ("SuggestedWidth", "2"), ("SuggestedHeight", "2"), ("Color", "#FF0000")), []);
            var filler = new WidgetNode(Attributes("SliderWidget > Widget[Id=filler]", ("Id", "filler"), ("SuggestedHeight", "1")), []);
            var node = new WidgetNode("SliderWidget", Attributes("SliderWidget", ("Handle", "handle"), ("Filler", "filler"), ("MinValueFloat", "0"), ("MaxValueFloat", "10"), ("IsDiscrete", discrete ? "true" : "false"), ("DiscreteIncrementInterval", "4"), ("ValueFloat", value.ToString(System.Globalization.CultureInfo.InvariantCulture))), [handle, filler]);
            var diagnostics = new PreviewDiagnostics();
            var root = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(node), diagnostics, Options());
            Assert.Equal(expectedX, Assert.Single(diagnostics.Layout!.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=handle]")).X);
            Assert.Equal(expectedFillWidth, Assert.Single(diagnostics.Layout.KeyBounds, b => b.ExpandedPath.EndsWith("[Id=filler]")).Width);
            var pixels = WpfPreviewCapture.Capture(root, new("pilot-20x10", 20, 10));
            Assert.Contains(pixels.Pixels, channel => channel > 0);
            return 0;
        });
    }
    [Fact] public void EditableTextAndKeyVisualMaterializeDeclaredBlueprints()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            foreach (var (name, attributes, expected) in new[] {
                ("EditableTextWidget", Attributes("EditableTextWidget", ("Text", "@Name")), "Ada"),
                ("InputKeyVisualWidget", Attributes("InputKeyVisualWidget", ("KeyText", "Space")), "Space") })
            {
                var diagnostics = new PreviewDiagnostics();
                var root = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(new WidgetNode(name, attributes, [])), diagnostics, Options() with { Bindings = new Dictionary<string, string> { ["Name"] = "Ada" } });
                Assert.Equal(expected, Assert.Single(Elements(root).OfType<System.Windows.Controls.TextBlock>()).Text);
                Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
                Assert.Single(diagnostics.BehaviorTrace.SelectMany(t => t.Contributions), c => c.Contribution is ElementBlueprint);
            }
            var partial = new PreviewDiagnostics();
            SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(new WidgetNode("InputKeyVisualWidget", Attributes("InputKeyVisualWidget", ("KeyID", "1")), [])), partial, Options());
            Assert.Equal(RenderCompletionStatus.Partial, partial.CompletionStatus);
            return 0;
        });
    }
    [Fact] public void SortAndToggleUseScenarioState()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var arrow = new WidgetNode(Attributes("Widget > Widget[Id=arrow]", ("Id", "arrow")), []);
            var sort = new WidgetNode(Attributes("Widget > Widget[Id=sort]", ("Id", "sort"), ("SortState", "descending"), ("SortVisualWidget", "..\\arrow")), []);
            var toggle = new WidgetNode("ToggleButtonWidget", Attributes("Widget > ToggleButtonWidget[Id=toggle]", ("Id", "toggle"), ("IsSelected", "true")), []);
            var root = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(new WidgetNode(Attributes("Widget"), [arrow, sort, toggle])), new PreviewDiagnostics(), Options());
            var renderedArrow = Assert.Single(Elements(root), e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "arrow");
            Assert.Equal(180, Assert.IsType<RotateTransform>(renderedArrow.RenderTransform).Angle);
            var renderedToggle = Assert.Single(Elements(root), e => PreviewElementDebugProperties.GetDebugInfo(e)?.GetAttributeDisplayValue("Id") == "toggle");
            Assert.Equal("Selected", PreviewElementDebugProperties.GetPreviewState(renderedToggle));
            return 0;
        });
    }
    [Fact] public void CancellationNeverReturnsACandidateRoot()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            var document = Document(new WidgetNode(Attributes("Widget"), []));
            FrameworkElement published = renderer.Render(document, new PreviewDiagnostics(), Options());
            var lastGood = published;
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => published = renderer.Render(document, new PreviewDiagnostics(), Options() with { CancellationToken = cancellation.Token }));
            Assert.Same(lastGood, published);
            return 0;
        });
    }
    [Fact] public void MaterializationFailureProducesFullTraceAndRetainsTheLastRoot()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            var lastGood = renderer.Render(Document(new WidgetNode(Attributes("Widget"), [])), new PreviewDiagnostics());
            var published = lastGood;
            var diagnostics = new PreviewDiagnostics();
            Assert.Throws<InvalidOperationException>(() => published = renderer.Render(Document(new MustNotRenderNode("Widget", Attributes("Widget"))), diagnostics));
            Assert.Same(lastGood, published);
            Assert.Equal(RenderCompletionStatus.Failed, diagnostics.CompletionStatus);
            Assert.Single(diagnostics.BehaviorTrace); Assert.NotEmpty(diagnostics.BehaviorTrace[0].Rules);
            return 0;
        });
    }
    [Fact] public void MissingOptionalDimensionSourceHasAnExplicitScenarioExplanation()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var node = new WidgetNode("DimensionSyncWidget", Attributes("DimensionSyncWidget", ("DimensionToSync", "Vertical")), []);
            var diagnostics = new PreviewDiagnostics();
            SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider()).Render(Document(node), diagnostics,
                Options() with { OptionalConnectionExplanations = new Dictionary<string, string> { ["DimensionSyncWidget:WidgetToCopyHeightFrom"] = "This fixture omits the optional source and preserves its fixed height." } });
            Assert.Contains(diagnostics.Messages, message => message.Contains("fixture omits the optional source", StringComparison.Ordinal));
            Assert.Equal(10, diagnostics.Layout!.KeyBounds[0].Height);
            return 0;
        });
    }
    private sealed class MustNotRenderNode(string name, IReadOnlyDictionary<string, string> attributes) : PrefabNode(name, attributes, [])
    {
        public bool Rendered { get; private set; }
        public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize) { Rendered = true; throw new InvalidOperationException("Materialization was reached before preflight."); }
    }
}
