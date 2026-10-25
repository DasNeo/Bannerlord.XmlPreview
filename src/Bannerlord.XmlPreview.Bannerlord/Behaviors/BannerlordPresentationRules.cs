using System.Collections.Immutable;
using Bannerlord.XmlPreview.Core.Behaviors;

namespace Bannerlord.XmlPreview.Behaviors.Bannerlord;

public static partial class BannerlordBehaviorRules
{
    private static void AddPresentationRules(List<BehaviorRule> rules)
    {
        void Describe(string id, string type, IBehaviorHandler handler) => rules.Add(new(new("bannerlord." + id),
            BehaviorPhase.Describe, new(TrustedBaseType: type), handler, Before: [new("bannerlord.state.describe")]));
        Describe("text.auto-hide", "AutoHideTextWidget", new AutoHideText(false));
        Describe("text.auto-hide-rich", "AutoHideRichTextWidget", new AutoHideText(false));
        Describe("text.auto-hide-zero", "AutoHideZeroTextWidget", new AutoHideText(true));
        Describe("text.scrolling", "ScrollingTextWidget", new ActualText());
        Describe("text.scrolling-rich", "ScrollingRichTextWidget", new ActualText());
        Describe("brush.boolean", "BoolBrushChangerBrushWidget", new BooleanBrush());
        rules.Add(new(new("bannerlord.brush.boolean-connection"), BehaviorPhase.Connect,
            new(TrustedBaseType: "BoolBrushChangerBrushWidget", Attribute: "TargetWidget"), new Connections([("TargetWidget", ConnectionRequirement.Required)])));
        foreach (var type in new[] { "AutoHideTextWidget", "AutoHideRichTextWidget" })
            rules.Add(new(new("bannerlord.text.empty-connection." + type), BehaviorPhase.Connect, new(TrustedBaseType: type, Attribute: "WidgetToHideIfEmpty"),
                new Connections([("WidgetToHideIfEmpty", ConnectionRequirement.Required)])));
        Describe("brush.selected", "SelectedStateBrushWidget", new SelectedBrush());
        Describe("brush.material", "CraftingMaterialVisualBrushWidget", new MaterialBrush());
        Describe("brush.icon", "IconBrushWidget", new LayerSelection(true));
        Describe("brush.default-sprite", "DefaultLayerSpriteChangerBrushWidget", new LayerSelection(false));
        Describe("image.skill", "SkillIconVisualWidget", new SkillSprite());
        rules.Add(new(new("bannerlord.image.tableau-support"), BehaviorPhase.Inspect, new(Capability: "runtime-tableau"), new TableauSupport()));
    }

    private sealed class ActualText : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            input.Values.TryGetValue("ActualText", out var text) ? [new DescriptionContribution("Text", text)] : [];
    }

    private sealed class AutoHideText(bool zero) : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var visible = zero ? (Number(input, "IntText") ?? 0) != 0 :
                Value(input, "Text", Value(input, "IntText", Value(input, "FloatText"))).Length != 0;
            var value = visible ? "true" : "false";
            return Reference(input, "WidgetToHideIfEmpty") is { } target
                ? [new DescriptionContribution("IsVisible", value), new DescriptionContribution("IsVisible", value, Target: target)]
                : [new DescriptionContribution("IsVisible", value)];
        }
    }

    private sealed class BooleanBrush : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var brush = Value(input, Boolean(input, "BooleanCheck") ? "TrueBrush" : "FalseBrush");
            var result = ImmutableArray.CreateBuilder<BehaviorContribution>();
            var target = Reference(input, "TargetWidget") ?? input.Widget.Id;
            result.Add(new DescriptionContribution("Brush", brush, Target: target));
            void Visit(WidgetId parent)
            {
                foreach (var child in input.Children.GetValueOrDefault(parent, []))
                {
                    if (input.RunValues[child].GetValueOrDefault("Preview.Capability.brush") == "true")
                        result.Add(new DescriptionContribution("Brush", brush, Target: child));
                    Visit(child);
                }
            }
            if (Boolean(input, "IncludeChildren")) Visit(target);
            return result.ToImmutable();
        }
    }

    private sealed class SelectedBrush : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            Value(input, "SelectedState") is { Length: > 0 } state ? [new DescriptionContribution("CurrentState", state)] : [];
    }

    private sealed class MaterialBrush : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new DescriptionContribution("CurrentState", Value(input, "MaterialType") + (Boolean(input, "IsBig") ? "Big" : ""))];
    }

    private sealed class LayerSelection(bool allLayers) : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var source = Value(input, allLayers ? "IconBrush" : "SpriteBrush");
            var layer = Value(input, allLayers ? "IconID" : "SpriteBrushLayerName");
            if (allLayers && (source.Length == 0 || layer.Length == 0)) return [];
            return [new DescriptionContribution("Preview.BrushSource", source),
                new DescriptionContribution("Preview.BrushSourceLayer", layer),
                new DescriptionContribution("Preview.BrushTargetLayers", allLayers ? "All" : "Default"),
                new DescriptionContribution("Preview.BrushCopyStyle", Boolean(input, "UseStylesFromSourceIcon") ? "true" : "false"),
                new DescriptionContribution("Preview.BrushUseSize", Boolean(input, "UseIconSize") ? "true" : "false")];
        }
    }

    private sealed class SkillSprite : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new DescriptionContribution("Sprite", "SPGeneral\\Skills\\gui_skills_icon_" + Value(input, "SkillId").ToLowerInvariant()),
                new DescriptionContribution("Preview.SpriteVariant", string.Join("|", new[]
                    { Boolean(input, "UseSmallestVariation") ? "_tiny" : "", Boolean(input, "UseSmallVariation") ? "_small" : "" }.Where(v => v.Length > 0)))];
    }

    private sealed class TableauSupport : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => string.IsNullOrWhiteSpace(Value(input, "Preview.ImageSprite"))
            ? [new InspectionContribution(BehaviorSupport.Partial, "Runtime-only Placeholder: tableau content requires game execution or an explicit scenario image.")]
            : [];
    }
}
