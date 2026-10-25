using System.Collections.Immutable;
using System.Globalization;
using Bannerlord.XmlPreview.Core.Behaviors;

namespace Bannerlord.XmlPreview.Behaviors.Bannerlord;

/// <summary>Trusted executable rules ship with the adapter, never with mod XML.</summary>
public static partial class BannerlordBehaviorRules
{
    public static ImmutableArray<BehaviorRule> All { get; } = Build();
    private static ImmutableArray<BehaviorRule> Build()
    {
        var rules = new List<BehaviorRule>();
        void Add(string id, BehaviorPhase phase, MatcherCriteria matcher, IBehaviorHandler handler) => rules.Add(new(new("bannerlord." + id), phase, matcher, handler));
        Add("image.static", BehaviorPhase.Instantiate, new(ExactType: "ImageWidget"), new StaticImage());
        Add("image.provider", BehaviorPhase.Instantiate, new(TrustedBaseType: "ImageIdentifierWidget"), new ProviderImage(false));
        Add("image.loading", BehaviorPhase.Describe, new(TrustedBaseType: "ImageIdentifierWidget", Attribute: "LoadingIconWidget"), new ImageLoading());
        Add("image.loading-connection", BehaviorPhase.Connect, new(TrustedBaseType: "ImageIdentifierWidget", Attribute: "LoadingIconWidget"), new Connections([("LoadingIconWidget", ConnectionRequirement.Required)]));
        Add("image.masked", BehaviorPhase.Instantiate, new(ExactType: "MaskedTextureWidget"), new ProviderImage(true));
        Add("image.tableau", BehaviorPhase.Instantiate, new(Capability: "runtime-tableau"), new RuntimeImage());
        Add("state.describe", BehaviorPhase.Describe, new(Capability: "preview-state"), new PreviewState());
        Add("state.boolean", BehaviorPhase.Describe, new(TrustedBaseType: "BoolStateChangerWidget"), new BooleanState());
        Add("state.boolean-container", BehaviorPhase.Instantiate, new(TrustedBaseType: "BoolStateChangerWidget"), new StaticContainer());
        Add("state.boolean-connection", BehaviorPhase.Connect, new(TrustedBaseType: "BoolStateChangerWidget", Attribute: "TargetWidget"), new Connections([("TargetWidget", ConnectionRequirement.Required)]));
        Add("inventory.tuple", BehaviorPhase.Describe, new(ExactType: "InventoryItemTupleWidget"), new InventoryTuple());
        Add("inventory.container", BehaviorPhase.Instantiate, new(ExactType: "InventoryItemTupleWidget"), new StaticContainer());
        Add("inventory.connections", BehaviorPhase.Connect, new(ExactType: "InventoryItemTupleWidget"), new Connections([
            ("MainContainer", ConnectionRequirement.Required), ("ExtendedControlsContainer", ConnectionRequirement.Required),
            ("NameTextWidget", ConnectionRequirement.Required), ("EquipButton", ConnectionRequirement.Required)]));
        Add("inventory.value", BehaviorPhase.Describe, new(ExactType: "InventoryItemValueTextWidget"), new InventoryValue());
        Add("visibility.sibling", BehaviorPhase.Describe, new(TrustedBaseType: "SiblingIndexVisibilityWidget"), new SiblingVisibility());
        Add("visibility.value", BehaviorPhase.Describe, new(TrustedBaseType: "ValueBasedVisibilityWidget"), new ValueVisibility());
        Add("visibility.string", BehaviorPhase.Describe, new(TrustedBaseType: "StringBasedVisibilityWidget"), new StringVisibility());
        Add("scroll.connections", BehaviorPhase.Connect, new(Capability: "scroll-viewport"), new Connections([("ClipRect", ConnectionRequirement.Required), ("InnerPanel", ConnectionRequirement.Required), ("HorizontalScrollbar", ConnectionRequirement.Optional), ("VerticalScrollbar", ConnectionRequirement.Optional)]));
        Add("scroll.layout", BehaviorPhase.PrepareLayout, new(Capability: "scroll-viewport"), new Intent(LayoutIntent.ScrollViewport));
        Add("state.connections", BehaviorPhase.Connect, new(TrustedBaseType: "StateSyncWidget"), new StateSync());
        Add("dimension.connections", BehaviorPhase.Connect, new(TrustedBaseType: "DimensionSyncWidget"), new DimensionConnections());
        Add("dimension.layout", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "DimensionSyncWidget"), new DimensionLayout());
        Add("clip", BehaviorPhase.PrepareLayout, new(Attribute: "ClipContents"), new Intent(LayoutIntent.Clip));
        Add("toggle", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "TabToggleWidget"), new Intent(LayoutIntent.ToggleSelection));
        Add("dropdown", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "AnimatedDropdownWidget"), new Intent(LayoutIntent.Dropdown));
        Add("radial", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "CircleLoadingAnimWidget"), new Intent(LayoutIntent.RadialChildren));
        Add("scroll.handle", BehaviorPhase.PrepareLayout, new(Capability: "scroll-handle"), new Intent(LayoutIntent.ScrollHandle));
        Add("fill", BehaviorPhase.PrepareLayout, new(Attribute: "FillWidget"), new Intent(LayoutIntent.Fill));
        Add("sort", BehaviorPhase.PrepareLayout, new(Attribute: "SortVisualWidget"), new SortPresentation());
        Add("slider.connections", BehaviorPhase.Connect, new(TrustedBaseType: "SliderWidget"), new Connections([("Handle", ConnectionRequirement.Optional), ("Filler", ConnectionRequirement.Optional)]));
        Add("slider.range", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "SliderWidget"), new RangeValue());
        Add("editable.instantiate", BehaviorPhase.Instantiate, new(TrustedBaseType: "EditableTextWidget"), new TextEntry());
        Add("key.instantiate", BehaviorPhase.Instantiate, new(TrustedBaseType: "InputKeyVisualWidget"), new KeyVisual());
        Add("key.inspect", BehaviorPhase.Inspect, new(TrustedBaseType: "InputKeyVisualWidget"), new KeySupport());
        Add("toggle.state", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "ToggleButtonWidget"), new ToggleState());
        Add("target", BehaviorPhase.PrepareLayout, new(Attribute: "TargetWidget"), new Intent(LayoutIntent.TargetState));
        Add("delayed", BehaviorPhase.PrepareLayout, new(TrustedBaseType: "DelayedStateChanger"), new Intent(LayoutIntent.DelayedState));
        Add("slider", BehaviorPhase.PrepareLayout, new(Attribute: "ChangeFillWidget"), new Intent(LayoutIntent.SliderFill));
        foreach (var name in new[] { "NavigationAutoScrollWidget", "NavigationTargetSwitcher", "NavigationForcedScopeCollectionTargeter" })
            Add("navigation." + name, BehaviorPhase.Describe, new(TrustedBaseType: name), new HiddenHelper());
        Add("instantiate.unknown", BehaviorPhase.Instantiate, new(Capability: "unknown"), new Placeholder());
        Add("instantiate.code", BehaviorPhase.Instantiate, new(Capability: "code-widget"), new Placeholder());
        Add("support.unknown", BehaviorPhase.Inspect, new(Capability: "unknown"), new Partial("Unknown widget rendered as a visible placeholder."));
        Add("support.code", BehaviorPhase.Inspect, new(Capability: "code-widget"), new Partial("Code Widget executable behavior is unavailable in preview."));
        AddNumericTextRules(rules);
        AddPresentationRules(rules);
        // Scroll handling consumes final handle placement rather than being overwritten by it.
        var handle = new RuleId("bannerlord.scroll.handle");
        rules = rules.Select(r => r.Id.Value == "bannerlord.scroll.layout" ? r with { After = [handle] } : r).ToList();
        rules = rules.Select(r => r.Id.Value is "bannerlord.inventory.tuple" or "bannerlord.inventory.value" or "bannerlord.state.boolean"
            ? r with { Before = [new("bannerlord.state.describe")] } : r).ToList();
        return rules.ToImmutableArray();
    }

    private static string Value(BehaviorInput input, string key, string fallback = "") => input.Values.GetValueOrDefault(key, fallback);
    private static bool Boolean(BehaviorInput input, string key, bool fallback = false)
    {
        var value = Value(input, key).Trim();
        if (bool.TryParse(value, out var boolean)) return boolean;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number != 0;
        return value.ToLowerInvariant() switch { "yes" or "on" or "visible" or "enabled" => true, "no" or "off" or "hidden" or "collapsed" or "disabled" => false, _ => fallback };
    }
    private static WidgetId? Reference(BehaviorInput input, string key) => input.References.TryGetValue(key, out var id) ? id : null;
    private static double? Number(BehaviorInput input, string key) => double.TryParse(Value(input, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    private sealed class PreviewState : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var state = Value(input, "CurrentState");
            if (string.IsNullOrWhiteSpace(state)) state = Boolean(input, "IsDisabled") || !Boolean(input, "IsEnabled", true) ? "Disabled" : Boolean(input, "IsPressed") ? "Pressed" : Boolean(input, "IsHovered") ? "Hovered" : Boolean(input, "IsSelected") ? "Selected" : "Default";
            return [new DescriptionContribution("Preview.State", state)];
        }
    }
    private sealed class BooleanState : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var target = Reference(input, "TargetWidget") ?? input.Widget.Id;
            var state = Value(input, Boolean(input, "BooleanCheck") ? "TrueState" : "FalseState");
            if (string.IsNullOrWhiteSpace(state)) throw new InvalidOperationException("Boolean state changer requires the selected TrueState or FalseState.");
            var contributions = ImmutableArray.CreateBuilder<BehaviorContribution>();
            void Set(WidgetId id)
            {
                contributions.Add(new DescriptionContribution("CurrentState", state, Target: id));
                if (Boolean(input, "IncludeChildren"))
                    foreach (var child in input.Children.GetValueOrDefault(id, [])) Set(child);
            }
            Set(target);
            return contributions.ToImmutable();
        }
    }
    private sealed class ImageLoading : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => Reference(input, "LoadingIconWidget") is { } target
            ? [new DescriptionContribution("IsVisible", "false", Target: target)] : [];
    }
    private sealed class InventoryTuple : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var result = ImmutableArray.CreateBuilder<BehaviorContribution>();
            // The hidden drag image and closed controls must not enlarge an inventory row.
            result.Add(new DescriptionContribution("Preview.ExcludeCollapsedChildren", "true"));
            void Set(string role, string property, string value)
            {
                if (Reference(input, role) is { } target) result.Add(new DescriptionContribution(property, value, Target: target));
            }
            var selected = Boolean(input, "IsSelected");
            var state = Value(input, "CurrentState");
            if (state.Length == 0) state = Boolean(input, "IsDisabled") || !Boolean(input, "IsEnabled", true) ? "Disabled"
                : Boolean(input, "IsPressed") ? "Pressed" : Boolean(input, "IsHovered") ? "Hovered" : selected ? "Selected" : "Default";
            Set("MainContainer", "CurrentState", state);
            Set("NameTextWidget", "CurrentState", state == "Pressed" ? "Pressed" : "Default");
            Set("ExtendedControlsContainer", "CurrentState", selected ? "Selected" : "Default");
            Set("ExtendedControlsContainer", "IsVisible", selected ? "true" : "false");
            Set("ExtendedControlsContainer", "IsDisabled", selected ? "false" : "true");
            var equipable = Boolean(input, "IsEquipable");
            var cannotUse = equipable && !Boolean(input, "CanCharacterUseItem", true);
            // EquipmentMode: 0 civilian, 1 battle, 2 stealth. Standalone rows default to battle.
            var mode = Value(input, "Preview.EquipmentMode", "1");
            var wrongSet = equipable && (mode == "0" && !Boolean(input, "IsCivilian") || mode == "2" && !Boolean(input, "IsStealth"));
            Set("MainContainer", "Brush", Value(input, cannotUse ? "CharacterCantUseBrush" : wrongSet ? "CantUseInSetBrush" : "DefaultBrush"));
            Set("EquipButton", "IsVisible", equipable ? "true" : "false");
            Set("EquipButton", "IsEnabled", equipable && !cannotUse && !wrongSet ? "true" : "false");
            Set("EquipButton", "IsDisabled", equipable && !cannotUse && !wrongSet ? "false" : "true");
            return result.ToImmutable();
        }
    }
    private sealed class StaticContainer : IBehaviorHandler
    {
        // These widgets supply their own layout; inherited generic chrome would add padding or a frame.
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new ElementBlueprint(ElementKind.Container)];
    }
    private sealed class InventoryValue : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var state = Value(input, "CurrentState");
            if (state.Length > 0) return [];
            state = Value(input, "ProfitType", "0") switch { "-2" => "VeryBad", "-1" => "Bad", "1" => "Good", "2" => "VeryGood", _ => "Default" };
            return [new DescriptionContribution("CurrentState", state)];
        }
    }
    private sealed class SiblingVisibility : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var values = Reference(input, "WidgetToWatch") is { } target ? input.RunValues[target] : input.Values;
            if (!int.TryParse(values.GetValueOrDefault("Preview.SiblingIndex"), out var index)) return [];
            var visible = Value(input, "WatchType") switch { "Odd" => index % 2 != 0, "Even" => index % 2 == 0, _ => true };
            return [new DescriptionContribution("IsVisible", visible ? "true" : "false")];
        }
    }
    private sealed class ValueVisibility : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var watch = Number(input, "IndexToWatchFloat") ?? Number(input, "IndexToWatch");
            var target = Number(input, "IndexToBeVisibleFloat") ?? Number(input, "IndexToBeVisible");
            if (watch is null || target is null) return [];
            var equal = Math.Abs(watch.Value - target.Value) < 0.0001;
            bool? visible = Value(input, "WatchType", "Equal").Trim() switch
            {
                "Equal" => equal, "NotEqual" => !equal, "BiggerThan" => watch > target,
                "BiggerThanEqual" => watch >= target, "LessThan" => watch < target, "LessThanEqual" => watch <= target, _ => null
            };
            return visible is null ? [] : [new DescriptionContribution("IsVisible", visible.Value ? "true" : "false")];
        }
    }
    private sealed class StringVisibility : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var equal = string.Equals(Value(input, "FirstString"), Value(input, "SecondString"), StringComparison.OrdinalIgnoreCase);
            var kind = Value(input, "WatchType", "Equal");
            return kind is not ("Equal" or "NotEqual") ? [] : [new DescriptionContribution("IsVisible", (kind == "Equal" ? equal : !equal) ? "true" : "false")];
        }
    }
    private sealed class Connections(ImmutableArray<(string Role, ConnectionRequirement Requirement)> roles) : IBehaviorHandler
    {
        private readonly ImmutableArray<(string Role, ConnectionRequirement Requirement)> _roles = roles;
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => _roles.Select(role => (BehaviorContribution)new ConnectionContribution(
            role.Role, Reference(input, role.Role), role.Requirement,
            input.Values.GetValueOrDefault("Preview.Optional." + role.Role))).ToImmutableArray();
    }
    private sealed class StateSync : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var source = Reference(input, "SourceWidget");
            var target = Reference(input, "TargetWidget") ?? input.Widget.Id;
            var result = ImmutableArray.CreateBuilder<BehaviorContribution>();
            result.Add(new ConnectionContribution("SourceWidget", source, ConnectionRequirement.Required));
            if (input.Widget.XmlAttributes.ContainsKey("TargetWidget")) result.Add(new ConnectionContribution("TargetWidget", Reference(input, "TargetWidget"), ConnectionRequirement.Required));
            if (source is not null) result.Add(new ConnectionContribution("State", target, ConnectionRequirement.Required, StateSource: source));
            return result.ToImmutable();
        }
    }
    private sealed class DimensionConnections : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => Value(input, "DimensionToSync", "None") == "None" ? [] :
            [new ConnectionContribution("WidgetToCopyHeightFrom", Reference(input, "WidgetToCopyHeightFrom"),
                input.Values.ContainsKey("Preview.Optional.WidgetToCopyHeightFrom") ? ConnectionRequirement.Optional : ConnectionRequirement.Required,
                input.Values.GetValueOrDefault("Preview.Optional.WidgetToCopyHeightFrom"))];
    }
    private sealed class DimensionLayout : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var axis = Value(input, "DimensionToSync", "None");
            if (axis is not ("None" or "Horizontal" or "Vertical" or "HorizontalAndVertical")) throw new InvalidOperationException($"Unknown dimension axis '{axis}'.");
            var source = Reference(input, "WidgetToCopyHeightFrom");
            return axis == "None" || source is null ? [] : [new LayoutContribution(LayoutIntent.CopyDimensions, source, Axis: axis, Padding: Number(input, "PaddingAmount") ?? 0)];
        }
    }
    private sealed class Intent(LayoutIntent intent) : IBehaviorHandler
    {
        private readonly LayoutIntent _intent = intent;
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            _intent == LayoutIntent.TargetState && (input.Widget.Type == "BoolStateChangerWidget" || input.Widget.TrustedBaseTypes.Contains("BoolStateChangerWidget"))
                ? [] : [new LayoutContribution(_intent, Value: _intent == LayoutIntent.Fill && input.Widget.Type.Contains("Vertical", StringComparison.Ordinal) ? "Vertical" : null)];
    }
    private sealed class SortPresentation : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var state = Value(input, "SortState").Trim();
            var selected = Value(input, "IsSelected", string.IsNullOrEmpty(state) ? "false" : "true").Equals("true", StringComparison.OrdinalIgnoreCase);
            var direction = !selected && state.Length == 0 ? "Hidden" : state.Contains("desc", StringComparison.OrdinalIgnoreCase) || state.Contains("down", StringComparison.OrdinalIgnoreCase) || state.Contains("high", StringComparison.OrdinalIgnoreCase) ? "Descending" : "Ascending";
            return [new LayoutContribution(LayoutIntent.SortIndicator, Target: Reference(input, "SortVisualWidget"), Value: direction)];
        }
    }
    private sealed class RangeValue : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var min = Number(input, "MinValueFloat") ?? Number(input, "MinValueInt") ?? 0;
            var max = Number(input, "MaxValueFloat") ?? Number(input, "MaxValueInt") ?? 1;
            var value = Number(input, "ValueFloat") ?? Number(input, "ValueInt") ?? min;
            if (Boolean(input, "IsDiscrete") && Number(input, "DiscreteIncrementInterval") is > 0 and var step)
                value = min + Math.Round((value - min) / step) * step;
            var fraction = max <= min ? 0 : Math.Clamp((value - min) / (max - min), 0, 1);
            var handle = Reference(input, "Handle");
            var filler = Reference(input, "Filler");
            return handle is not null || filler is not null ? [new LayoutContribution(LayoutIntent.RangeValue, Source: handle,
                Axis: Value(input, "AlignmentAxis", "Horizontal"), Value: fraction.ToString("R", CultureInfo.InvariantCulture), SecondaryTarget: filler)] : [];
        }
    }
    private sealed class TextEntry : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new ElementBlueprint(ElementKind.Text, Text: Value(input, "Text"))];
    }
    private sealed class KeyVisual : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new ElementBlueprint(ElementKind.KeyGlyph,
            Text: Value(input, "KeyText", "Key " + Value(input, "KeyID", "?")))];
    }
    private sealed class KeySupport : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => input.Values.ContainsKey("KeyText") ?
            [new InspectionContribution(BehaviorSupport.Supported)] : [new InspectionContribution(BehaviorSupport.Partial, "Key artwork and localized key names require scenario KeyText; a labeled key placeholder is shown.")];
    }
    private sealed class ToggleState : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new LayoutContribution(LayoutIntent.SetState, Value: Value(input, "Preview.State", "Default"))];
    }
    private sealed class HiddenHelper : IBehaviorHandler
    {
        // Navigation helpers have no disabled visual treatment; disabling input must not add a visual fade.
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new DescriptionContribution("IsVisible", "false"), new DescriptionContribution("IsEnabled", "false"), new DescriptionContribution("IsHitTestVisible", "false"), new DescriptionContribution("Preview.DisabledOpacity", "1")];
    }
    private sealed class Placeholder : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new ElementBlueprint(ElementKind.Placeholder, "Behavior is unavailable in preview.")];
    }
    private sealed class StaticImage : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new ElementBlueprint(ElementKind.Image, Image: new(new(Value(input, "Sprite", "")),
                Brush: Value(input, "Brush", ""), Stretch: Value(input, "Stretch", "Fill"), Color: Value(input, "Color", "")))];
    }
    private sealed class ProviderImage(bool masked) : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new ElementBlueprint(ElementKind.Image, Image: new(new(Value(input, "ImageId", ""),
                Value(input, "TextureProviderName", ""), Value(input, "AdditionalArgs", ""),
                input.Values.GetValueOrDefault("Preview.ImageSprite")), Brush: Value(input, "Brush", ""),
                Mask: input.Values.GetValueOrDefault("MaskSprite"), Masked: masked,
                HideWhenNull: Boolean(input, "HideWhenNull"), Stretch: Value(input, "Stretch", "Fill"), Color: Value(input, "Color", "")))];
    }
    private sealed class RuntimeImage : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new ElementBlueprint(ElementKind.Image, Image: new(new(Value(input, "StringId", Value(input, "BannerCodeText", "")),
                "Runtime.Tableau", ScenarioSprite: input.Values.GetValueOrDefault("Preview.ImageSprite"))))];
    }
    private sealed class Partial(string diagnostic) : IBehaviorHandler
    {
        private readonly string _diagnostic = diagnostic;
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) => [new InspectionContribution(BehaviorSupport.Partial, _diagnostic)];
    }
}
