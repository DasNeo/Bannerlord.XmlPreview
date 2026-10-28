using System.Collections.Immutable;
using System.IO;
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

public sealed class NumericTextBehaviorTests
{
    [Theory]
    [InlineData("3", false, false, "3")]
    [InlineData("1.05", true, false, "1.05")]
    [InlineData("2", true, true, "+2.00")]
    [InlineData("-1.2", false, true, "-2")]
    [InlineData("0", false, true, "0")]
    public void CountersRenderSettledValuesWithGameFormatting(string value, bool floating, bool sign, string expected)
    {
        var plan = Plan(Input("CounterTextBrushWidget", "counter", ("FloatTarget", value),
            ("ShowFloatingPoint", floating.ToString()), ("ShowSign", sign.ToString())));
        Assert.Equal(expected, Text(plan, "counter"));
        Assert.Equal(expected, Assert.Single(plan.Contributions.Select(c => c.Contribution).OfType<ElementBlueprint>()).Text);
    }

    [Theory]
    [InlineData("-10", "0")]
    [InlineData("120", "100")]
    [InlineData("42", "42")]
    public void IntegerTargetHonorsDeclaredRange(string value, string expected) => Assert.Equal(expected,
        Text(Plan(Input("CounterTextBrushWidget", "counter", ("IntTarget", value), ("Clamped", "true"), ("MinValue", "0"), ("MaxValue", "100"))), "counter"));

    [Theory]
    [InlineData("3", true, true, "(+3)", "true", "positive")]
    [InlineData("-2", false, true, "-2", "true", "negative")]
    [InlineData("0", false, true, "0", "false", null)]
    [InlineData("3", false, false, "+3", "false", "positive")]
    public void ChangeAmountsDeriveSignsBrushesAndVisibility(string amount, bool parentheses, bool visible, string text, string visibility, string? brush)
    {
        var plan = Plan(Input("ChangeAmountTextWidget", "change", ("Amount", amount), ("UseParentheses", parentheses.ToString()),
            ("ShouldBeVisible", visible.ToString()), ("IsVisible", "false"), ("PositiveBrushName", "positive"), ("NegativeBrushName", "negative")));
        Assert.Equal(text, Text(plan, "change"));
        Assert.Equal(visibility, Description(plan, "change", "IsVisible"));
        Assert.Equal(brush, Description(plan, "change", "Brush"));
    }

    [Theory]
    [InlineData("3", "+3", "#00FF00FF")]
    [InlineData("-2", "-2", "#FF0000FF")]
    [InlineData("0", "0", "#FFFFFFFF")]
    public void RelationUsesItsDeclaredColors(string amount, string text, string color)
    {
        var plan = Plan(Input("RelationTextWidget", "relation", ("Amount", amount), ("PositiveColor", "#00FF00FF"),
            ("NegativeColor", "#FF0000FF"), ("ZeroColor", "#FFFFFFFF")));
        Assert.Equal(text, Text(plan, "relation"));
        Assert.Equal(color, Description(plan, "relation", "Brush.FontColor"));
    }

    [Theory]
    [InlineData("InventoryArmorAnimationTextWidget", "FloatAmount", "42.5", "42.5", null)]
    [InlineData("BarterItemCountTextWidget", "Count", "12", "12", "true")]
    [InlineData("BarterItemCountTextWidget", "Count", "1", "1", "false")]
    [InlineData("BarterItemCountTextWidget", "Count", "0", "0", "false")]
    [InlineData("ScoreboardAnimatedTextWidget", "ValueAsInt", "12", "12", null)]
    [InlineData("ScoreboardAnimatedTextWidget", "ValueAsInt", "0", "", null)]
    public void NumericPropertiesProduceTextWithoutTextBindings(string type, string property, string value, string text, string? visible)
    {
        var plan = Plan(Input(type, "number", (property, value)));
        Assert.Equal(text, Text(plan, "number"));
        Assert.Equal(visible, Description(plan, "number", "IsVisible"));
    }

    [Fact]
    public void ScoreboardCanShowZeroWithoutCollapsingItsLayout() => Assert.Equal("0",
        Text(Plan(Input("ScoreboardAnimatedTextWidget", "score", ("ValueAsInt", "0"), ("ShowZero", "true"))), "score"));

    [Theory]
    [InlineData("99", "1", "100", "+1", "positive")]
    [InlineData("44", "-3", "41", "-3", "negative")]
    [InlineData("60", "0", "60", "0", "neutral")]
    public void CraftingRowsPopulateReferencedCountersBeforeFormatting(string initial, string change, string valueText, string deltaText, string brush)
    {
        var row = Input("CraftedWeaponDesignResultListPanel", "row", ("InitValue", initial), ("ChangeAmount", change),
            ("TargetValue", "999"), ("PositiveChangeBrush", "positive"), ("NegativeChangeBrush", "negative"), ("NeutralBrush", "neutral"))
            with { References = new Dictionary<string, WidgetId> { ["ValueTextWidget"] = new("arbitrary-value"),
                ["ChangeValueTextWidget"] = new("arbitrary-change"), ["LabelTextWidget"] = new("label") }.ToImmutableDictionary() };
        var plan = Plan(row, Input("CounterTextBrushWidget", "arbitrary-value"), Input("CounterTextBrushWidget", "arbitrary-change"), Input("RichTextWidget", "label"));
        Assert.Equal(valueText, Text(plan, "arbitrary-value"));
        Assert.Equal(deltaText, Text(plan, "arbitrary-change"));
        Assert.Equal(brush, Description(plan, "arbitrary-value", "Brush"));
        Assert.Equal(change == "0" ? "false" : "true", Description(plan, "arbitrary-change", "IsVisible"));
    }

    [Fact]
    public void MissingCraftingCounterFailsPreflight() => Assert.Equal("MissingRequiredConnection",
        Assert.Throws<BehaviorGraphException>(() => Plan(Input("CraftedWeaponDesignResultListPanel", "row"))).Error.Code);

    [InstalledImageFact]
    public void InstalledCraftingRowsKeepTheirOwnValuesAcrossRenders()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var root = Environment.GetEnvironmentVariable("BANNERLORD_ROOT")!;
            var (_, loader, renderer) = InstalledImageTests.EnvironmentFor(root);
            var document = loader.Load(Path.Combine(root, "Modules/SandBox/GUI/Prefabs/Crafting/NewCraftedWeaponPopup.xml"), new PreviewDiagnostics());
            var nodes = Nodes(document.RootNode).ToArray();
            var propertyList = nodes.Single(n => n.Attributes.GetValueOrDefault("Id") == "PropertyList");
            var listPath = propertyList.Attributes["Debug.WidgetPath"];
            var bindings = document.BindingKeys.ToDictionary(k => k, _ => "false");
            bindings["WeaponCraftedText"] = "Weapon Crafted!";
            bindings["ItemName"] = "Test axe";
            bindings["DoneLbl"] = "Done";
            bindings["CanConfirm"] = "true";
            bindings["RequiredValueText"] = "";
            IReadOnlyDictionary<string, string> Row(string key, string initial, string change, string floating) => new Dictionary<string, string>
            {
                ["__key"] = key, ["PropertyLbl"] = key, ["InitialValue"] = initial, ["ChangeAmount"] = change,
                ["TargetValue"] = "999", ["ShowFloatingPoint"] = floating, ["RequiredValueText"] = ""
            };
            var optional = nodes.Where(n => n.Name == "ScrollablePanel").ToDictionary(n => n.Attributes["Debug.WidgetPath"] + ":HorizontalScrollbar", _ => "The shipped popup has only a vertical scrollbar.");
            foreach (var change in new[] { "1", "-2" })
            {
                var diagnostics = new PreviewDiagnostics();
                var rendered = renderer.Render(document, diagnostics, new("Modules/SandBox/GUI/Prefabs/Crafting/NewCraftedWeaponPopup.xml", bindings,
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(), [])
                {
                    PreviewSize = new(1920, 1080), OptionalConnectionExplanations = optional,
                    Lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>
                    {
                        [listPath] = [Row("weight", "1.05", "0", "true"), Row("speed", "99", change, "false")]
                    }
                });
                var counters = Snapshots(diagnostics.RenderStructure!.Root).Where(n => n.Kind == "CounterTextBrushWidget").ToArray();
                Assert.Equal(4, counters.Length);
                var values = counters.Select(n => diagnostics.BehaviorTrace.Single(t => t.Widget.Value == n.WidgetId).Contributions
                    .Select(c => c.Contribution).OfType<DescriptionContribution>().Single(c => c.Property == "Text").Value).ToArray();
                Assert.Contains("1.05", values);
                Assert.Contains(change == "1" ? "100" : "97", values);
                Assert.Contains(change == "1" ? "+1" : "-2", values);
                Assert.Contains(Elements(rendered).OfType<BitmapFontTextBlock>(), t => t.StyleSource.Text == (change == "1" ? "+1" : "-2"));
                Assert.Equal("false", counters.Single(n => n.InstanceKey.EndsWith("=weight") && n.Properties.GetValueOrDefault("Id") == "ChangeValueTextWidget").Properties["Resolved.IsVisible"]);
            }
            Assert.DoesNotContain(Nodes(document.RootNode), n => n.Name == "CounterTextBrushWidget" && n.Attributes.ContainsKey("Text"));
            return 0;
        });
    }

    [Fact]
    public void BindingChangesRerenderTextAndVisibilityWithoutMutatingTheDocument()
    {
        SnapshotCaptureTests.Sta(() =>
        {
            var attributes = new Dictionary<string, string> { ["Debug.WidgetPath"] = "Widget", ["FloatTarget"] = "@Value",
                ["WidthSizePolicy"] = "Fixed", ["HeightSizePolicy"] = "Fixed", ["SuggestedWidth"] = "120", ["SuggestedHeight"] = "40", ["ShowFloatingPoint"] = "true" };
            var node = new BrushWidgetNode("CounterTextBrushWidget", attributes, []);
            var document = new PrefabDocument("renamed.xml", node, new Dictionary<string, string>(), new HashSet<string> { "Value" }, PrefabWidgetPropertyMap.Empty, PrefabWidgetReferenceMap.Empty);
            var renderer = SnapshotCaptureTests.Renderer(new SnapshotCaptureTests.EmptyData(), new BannerlordBehaviorRuleProvider());
            FrameworkElement Render(string value) => renderer.Render(document, new PreviewDiagnostics(), new("renamed.xml",
                new Dictionary<string, string> { ["Value"] = value }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), []));
            var first = Render("1.05");
            var second = Render("7.25");
            Assert.Contains(Elements(first).OfType<TextBlock>(), t => t.Text == "1.05");
            Assert.Contains(Elements(second).OfType<TextBlock>(), t => t.Text == "7.25");
            Assert.Equal("@Value", node.Attributes["FloatTarget"]);
            Assert.False(node.Attributes.ContainsKey("Text"));
            Assert.IsType<Grid>(first); // Counter has text presentation, not generic gold brush-container chrome.
            return 0;
        });
    }

    private static BehaviorInput Input(string type, string id, params (string Key, string Value)[] properties)
    {
        var values = properties.ToImmutableDictionary(p => p.Key, p => p.Value);
        return new(new(new(id), id, type, values, [], ["preview-state"], null, []), values,
            ImmutableDictionary<string, WidgetId>.Empty, ImmutableDictionary<WidgetId, ImmutableDictionary<string, string>>.Empty);
    }
    private static BehaviorGraphPlan Plan(params BehaviorInput[] inputs) => new BehaviorGraph().Plan(inputs.ToImmutableArray(),
        BannerlordBehaviorRules.All, cancellationToken: TestContext.Current.CancellationToken);
    private static string? Description(BehaviorGraphPlan plan, string id, string property) => plan.Contributions
        .Where(c => (c.Contribution is DescriptionContribution d && (d.Target ?? c.Widget).Value == id && d.Property == property))
        .Select(c => ((DescriptionContribution)c.Contribution).Value).SingleOrDefault();
    private static string? Text(BehaviorGraphPlan plan, string id) => Description(plan, id, "Text");
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static IEnumerable<PrefabNode> Nodes(PrefabNode root) => new[] { root }.Concat(root.Children.SelectMany(Nodes));
    private static IEnumerable<RenderWidgetSnapshot> Snapshots(RenderWidgetSnapshot root) => new[] { root }.Concat(root.Children.SelectMany(Snapshots));
}
