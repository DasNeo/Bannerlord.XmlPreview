using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bannerlord.XmlPreview.Behaviors.Bannerlord;
using Bannerlord.XmlPreview.Core.Behaviors;
using Bannerlord.XmlPreview.Models;
using Bannerlord.XmlPreview.Models.Nodes;
using Bannerlord.XmlPreview.Rendering;
using Bannerlord.XmlPreview.Services;
using Xunit;

namespace Bannerlord.XmlPreview.VisualRegression.Tests;

public sealed class HiddenSubtreeTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("true", null, true)]
    [InlineData("false", null, false)]
    [InlineData("@Flag", null, false)]
    [InlineData("@Flag", "true", true)]
    [InlineData("@Flag", "false", false)]
    public void VisibilityDefaultsFalseOnlyWhenAnAttributeHasNoResolvedBoolean(string? visibility, string? value, bool expected)
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var attributes = Attributes("Widget");
            if (visibility is not null) attributes["IsVisible"] = visibility;
            var options = Options(false) with { Bindings = value is null ? new Dictionary<string, string>() : new() { ["Flag"] = value } };
            var rendered = Renderer().Render(Document(new WidgetNode(attributes, [])), new PreviewDiagnostics(), options);
            Assert.Equal(expected ? Visibility.Visible : Visibility.Collapsed, rendered.Visibility);
            return 0;
        });
    }

    [Fact]
    public void HiddenChildrenSkipMaterializationButKeepLogicalStructureSourceAndValidationStatus()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var root = new WidgetNode(Attributes("Widget", ("IsVisible", "false")),
                [new ProbeNode(Attributes("Widget > Probe"), calls)]);
            var source = new SourceLocation("included.xml", 17, 2);
            var document = Document(root) with { SourceMap = new Dictionary<string, SourceLocation> { ["Widget"] = source } };
            var diagnostics = new PreviewDiagnostics();
            var rendered = Renderer().Render(document, diagnostics);
            Assert.Empty(calls);
            var deferred = Assert.Single(diagnostics.DeferredHiddenSubtrees);
            Assert.Equal(2, deferred.LogicalWidgetCount);
            Assert.Single(diagnostics.RenderStructure!.Root.Children);
            Assert.Equal(source, PreviewElementDebugProperties.GetSourceLocation(rendered));
            Assert.Same(deferred, PreviewElementDebugProperties.GetDeferredHiddenSubtree(rendered));
            Assert.Contains("deferred", PreviewElementDebugProperties.GetTreeLabel(rendered));
            Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
            Assert.Empty(diagnostics.TruncationReasons);
            Assert.Contains("asset validation", diagnostics.ToDisplayText());
            return 0;
        });
    }

    [Fact]
    public void StrictCaptureStillMaterializesHiddenContent()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var root = new WidgetNode(Attributes("Widget", ("IsVisible", "false")),
                [new ProbeNode(Attributes("Widget > Probe"), calls)]);
            var diagnostics = new PreviewDiagnostics();
            Renderer().Render(Document(root), diagnostics, Options(false));
            Assert.Single(calls);
            Assert.Empty(diagnostics.DeferredHiddenSubtrees);
            Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
            return 0;
        });
    }

    [Fact]
    public void BindingChangeMaterializesPreviouslyDeferredContentWithoutLeakingAcrossRenders()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var root = new WidgetNode(Attributes("Widget", ("IsVisible", "@Show")),
                [new ProbeNode(Attributes("Widget > Probe"), calls)]);
            var document = Document(root);
            var renderer = Renderer();
            foreach (var show in new[] { false, true, false })
            {
                var diagnostics = new PreviewDiagnostics();
                renderer.Render(document, diagnostics, Options() with { Bindings = new Dictionary<string, string> { ["Show"] = show.ToString() } });
                Assert.Equal(show ? 0 : 1, diagnostics.DeferredHiddenSubtrees.Count);
            }
            Assert.Single(calls);
            Assert.Equal("@Show", root.Attributes["IsVisible"]);
            return 0;
        });
    }

    [Fact]
    public void TemplateVisibilityAndDeferredCountsRespectInstanceKeys()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            const string list = "ListPanel";
            const string row = list + " > ItemTemplate > Widget";
            var calls = new List<string>();
            var root = new ListPanelNode(Attributes(list), [new ItemTemplateNode(Attributes(list + " > ItemTemplate"),
                [new WidgetNode(Attributes(row, ("IsVisible", "@Show")), [new ProbeNode(Attributes(row + " > Probe"), calls)])])]);
            var options = Options() with
            {
                Lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
                {
                    [list] = new IReadOnlyDictionary<string, string>[]
                    {
                        new Dictionary<string, string> { ["__key"] = "hidden", ["Show"] = "false" },
                        new Dictionary<string, string> { ["__key"] = "shown", ["Show"] = "true" }
                    }
                }
            };
            var diagnostics = new PreviewDiagnostics();
            Renderer().Render(Document(root), diagnostics, options);
            Assert.Single(calls);
            var deferred = Assert.Single(diagnostics.DeferredHiddenSubtrees);
            Assert.Equal("/ListPanel=hidden", deferred.InstanceKey);
            Assert.Equal(2, deferred.LogicalWidgetCount);
            Assert.Equal(2, diagnostics.RenderStructure!.Root.Children.Count);
            return 0;
        });
    }

    [Fact]
    public void ProtectedHiddenParentStillDefersUnreferencedVisibleDescendants()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var root = new WidgetNode(Attributes("Widget", ("IsVisible", "false")),
                [new ProbeNode(Attributes("Widget > Probe"), calls)]);
            var diagnostics = new PreviewDiagnostics();
            Renderer(new InspectRoot()).Render(Document(root), diagnostics, Options());
            Assert.Empty(calls);
            var deferred = Assert.Single(diagnostics.DeferredHiddenSubtrees);
            Assert.True(deferred.IsLocallyVisible);
            Assert.Equal("Widget > Probe", deferred.ExpandedPath);
            Assert.Equal("true", diagnostics.RenderStructure!.Root.Children[0].Properties["Resolved.IsVisible"]);
            return 0;
        });
    }

    [Fact]
    public void HiddenMeasurementSourcesAndTheirDescendantsRemainAvailableToDimensionCopying()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            const string sourcePath = "Widget > Widget[Id=source]";
            var source = new WidgetNode(Attributes(sourcePath, ("Id", "source"), ("IsVisible", "false")),
                [new ProbeNode(Attributes(sourcePath + " > Probe"), calls)]);
            var target = new WidgetNode("DimensionSyncWidget", Attributes("Widget > DimensionSyncWidget[Id=target]",
                ("Id", "target"), ("WidgetToCopyHeightFrom", "..\\source"), ("DimensionToSync", "HorizontalAndVertical"), ("PaddingAmount", "3")), []);
            var document = Document(new WidgetNode(Attributes("Widget"), [source, target]));
            var renderer = Renderer(new BannerlordBehaviorRuleProvider());
            var baseline = new PreviewDiagnostics(); renderer.Render(document, baseline, Options(false));
            calls.Clear();
            var optimized = new PreviewDiagnostics(); renderer.Render(document, optimized, Options());
            Assert.Single(calls);
            Assert.Empty(optimized.DeferredHiddenSubtrees);
            Assert.Equal(SnapshotSerialization.Serialize(baseline.Layout!), SnapshotSerialization.Serialize(optimized.Layout!));
            return 0;
        });
    }

    [Fact]
    public void CanvasCoverChildrenPreservesHiddenContentThatStillContributesToParentBounds()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var hidden = new WidgetNode(Attributes("Widget > hidden", ("Id", "hidden"), ("IsVisible", "false"),
                ("WidthSizePolicy", "CoverChildren"), ("HeightSizePolicy", "CoverChildren")),
                [new ProbeNode(Attributes("Widget > hidden > Probe", ("SuggestedWidth", "90"), ("SuggestedHeight", "30")), calls)]);
            var visible = new WidgetNode(Attributes("Widget > shown", ("Id", "shown"), ("HorizontalAlignment", "Right")), []);
            var root = new WidgetNode(Attributes("Widget", ("WidthSizePolicy", "CoverChildren"), ("HeightSizePolicy", "CoverChildren")), [hidden, visible]);
            var renderer = Renderer();
            var baseline = new PreviewDiagnostics(); renderer.Render(Document(root), baseline, Options(false));
            calls.Clear();
            var optimized = new PreviewDiagnostics(); renderer.Render(Document(root), optimized, Options());
            Assert.Single(calls);
            Assert.Empty(optimized.DeferredHiddenSubtrees);
            Assert.Equal(SnapshotSerialization.Serialize(baseline.Layout!), SnapshotSerialization.Serialize(optimized.Layout!));
            return 0;
        });
    }

    [Fact]
    public void DescribeVisibilityOverridesAreAppliedBeforeDeferral()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            var root = new WidgetNode(Attributes("Widget", ("IsVisible", "true")),
                [new ProbeNode(Attributes("Widget > Probe"), calls)]);
            var diagnostics = new PreviewDiagnostics();
            Renderer(new HideRoot()).Render(Document(root), diagnostics, Options());
            Assert.Empty(calls);
            Assert.False(Assert.Single(diagnostics.DeferredHiddenSubtrees).IsLocallyVisible);
            Assert.Equal("true", root.Attributes["IsVisible"]);
            return 0;
        });
    }

    [Fact]
    public void HiddenAssetsAreExplicitlyDeferredWhileStrictCapturesStillValidateThem()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var image = new WidgetNode("ImageWidget", Attributes("Widget > ImageWidget", ("Sprite", "missing")), []);
            var document = Document(new WidgetNode(Attributes("Widget", ("IsVisible", "false")), [image]));
            var renderer = Renderer(new BannerlordBehaviorRuleProvider());
            var deferred = new PreviewDiagnostics(); renderer.Render(document, deferred, Options());
            Assert.Single(deferred.DeferredHiddenSubtrees);
            Assert.Equal(RenderCompletionStatus.Partial, deferred.CompletionStatus);
            Assert.Empty(deferred.TruncationReasons);
            var strict = new PreviewDiagnostics(); renderer.Render(document, strict, Options(false));
            Assert.Empty(strict.DeferredHiddenSubtrees);
            Assert.NotEmpty(strict.Messages);
            return 0;
        });
    }

    [Fact]
    public void LayoutThatRevealsAnInitiallyHiddenTargetKeepsItsVisibleContents()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var calls = new List<string>();
            const string arrowPath = "Widget > Widget[Id=arrow]";
            var arrow = new WidgetNode(Attributes(arrowPath, ("Id", "arrow"), ("IsVisible", "false")),
                [new ProbeNode(Attributes(arrowPath + " > Probe", ("Color", "#FF0000FF")), calls)]);
            var sort = new WidgetNode(Attributes("Widget > sort", ("SortState", "descending"), ("SortVisualWidget", "..\\arrow")), []);
            var document = Document(new WidgetNode(Attributes("Widget"), [arrow, sort]));
            var renderer = Renderer(new BannerlordBehaviorRuleProvider());
            var strict = renderer.Render(document, new PreviewDiagnostics(), Options(false));
            var expected = WpfPreviewCapture.Capture(strict, new("reveal", 20, 10));
            calls.Clear();
            var diagnostics = new PreviewDiagnostics();
            var optimized = renderer.Render(document, diagnostics, Options());
            Assert.Single(calls);
            Assert.Empty(diagnostics.DeferredHiddenSubtrees);
            Assert.Equal(expected.Pixels, WpfPreviewCapture.Capture(optimized, new("reveal", 20, 10)).Pixels);
            return 0;
        });
    }

    [Fact]
    public void MissingRequiredConnectionsStillFailBeforeAnInvisibleBranchCanBeDeferred()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = new WidgetNode("BoolStateChangerWidget", Attributes("Widget", ("IsVisible", "false"),
                ("TargetWidget", "missing"), ("TrueState", "Active"), ("FalseState", "Passive")), []);
            Assert.Equal("MissingRequiredConnection", Assert.Throws<BehaviorGraphException>(() =>
                Renderer(new BannerlordBehaviorRuleProvider()).Render(Document(root), new PreviewDiagnostics(), Options())).Error.Code);
            return 0;
        });
    }

    private static Dictionary<string, string> Attributes(string path, params (string Key, string Value)[] values)
    {
        var attributes = new Dictionary<string, string> { ["Debug.WidgetPath"] = path, ["WidthSizePolicy"] = "Fixed",
            ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "20", ["SuggestedHeight"] = "10" };
        foreach (var (key, value) in values) attributes[key] = value;
        return attributes;
    }
    private static PrefabDocument Document(PrefabNode root) => new("fixture.xml", root, new Dictionary<string, string>(),
        new HashSet<string>(), PrefabWidgetPropertyMap.Empty, new WidgetReferenceResolver().Resolve(root, new PreviewDiagnostics()));
    private static RegressionRenderOptions Options(bool defer = true) => new("fixture.xml", new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(), []) { DeferHiddenSubtrees = defer };
    private static PrefabRenderer Renderer(IBehaviorRuleProvider? rules = null) => SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), rules);
    private sealed class ProbeNode(IReadOnlyDictionary<string, string> attributes, List<string> calls) : PrefabNode("Probe", attributes, [])
    {
        public override RenderedPrefabNode Render(PrefabRenderContext context, Size parentSize)
        {
            calls.Add(Attributes["Debug.WidgetPath"]);
            return new WidgetNode(Name, Attributes, []).Render(context, parentSize);
        }
    }
    private sealed class InspectRoot : IBehaviorRuleProvider, IBehaviorHandler
    {
        public ImmutableArray<BehaviorRule> Rules => [new(new("test.inspect-root"), BehaviorPhase.Inspect, new(ExactType: "Widget"), this)];
        public BehaviorTypeMetadata Describe(PrefabNode node) => new([], []);
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new InspectionContribution(BehaviorSupport.Supported)];
    }
    private sealed class HideRoot : IBehaviorRuleProvider, IBehaviorHandler
    {
        public ImmutableArray<BehaviorRule> Rules => [new(new("test.hide-root"), BehaviorPhase.Describe, new(ExactType: "Widget"), this)];
        public BehaviorTypeMetadata Describe(PrefabNode node) => new([], []);
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new DescriptionContribution("IsVisible", "false")];
    }
}
