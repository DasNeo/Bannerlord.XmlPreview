using System.Collections.Immutable;
using System.Globalization;
using Bannerlord.XmlPreview.Core.Behaviors;

namespace Bannerlord.XmlPreview.Behaviors.Bannerlord;

public static partial class BannerlordBehaviorRules
{
    private static void AddNumericTextRules(List<BehaviorRule> rules)
    {
        void Describe(string id, string type, IBehaviorHandler handler, params string[] before) => rules.Add(new(
            new("bannerlord.text." + id), BehaviorPhase.Describe, new(TrustedBaseType: type), handler,
            Before: before.Select(name => new RuleId(name)).ToImmutableArray()));

        Describe("counter", "CounterTextBrushWidget", new CounterText());
        rules.Add(new(new("bannerlord.text.counter-element"), BehaviorPhase.Instantiate,
            new(TrustedBaseType: "CounterTextBrushWidget"), new CounterTextElement()));
        Describe("crafting-result", "CraftedWeaponDesignResultListPanel", new CraftedResultText(),
            "bannerlord.text.counter", "bannerlord.state.describe");
        rules.Add(new(new("bannerlord.text.crafting-connections"), BehaviorPhase.Connect,
            new(TrustedBaseType: "CraftedWeaponDesignResultListPanel"), new Connections([
                ("ValueTextWidget", ConnectionRequirement.Required),
                ("ChangeValueTextWidget", ConnectionRequirement.Required),
                ("LabelTextWidget", ConnectionRequirement.Required)])));
        Describe("change", "ChangeAmountTextWidget", new ChangeText());
        Describe("relation", "RelationTextWidget", new RelationText());
        Describe("armor", "InventoryArmorAnimationTextWidget", new NumericText("FloatAmount", integer: false));
        Describe("barter-count", "BarterItemCountTextWidget", new NumericText("Count", integer: true));
        Describe("scoreboard", "ScoreboardAnimatedTextWidget", new NumericText("ValueAsInt", integer: true));
    }

    private static string Invariant(double number) => number.ToString(CultureInfo.InvariantCulture);
    private static string Signed(double number) => (number > 0 ? "+" : "") + Invariant(number);

    private sealed class CounterText : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var number = Number(input, "FloatTarget") ?? Number(input, "IntTarget") ?? 0;
            if (Boolean(input, "Clamped"))
            {
                var minimum = Number(input, "MinValue") ?? float.MinValue;
                var maximum = Number(input, "MaxValue") ?? float.MaxValue;
                if (minimum > maximum) throw new InvalidOperationException("Counter MinValue must not exceed MaxValue.");
                number = Math.Clamp(number, minimum, maximum);
            }
            // ForceSetValue uses Floor for integers and F2 for floating-point counters.
            var text = Boolean(input, "ShowFloatingPoint") ? number.ToString("F2", CultureInfo.InvariantCulture) : Invariant(Math.Floor(number));
            if (Boolean(input, "ShowSign") && number > 0) text = "+" + text;
            return [new DescriptionContribution("Text", text)];
        }
    }

    private sealed class CounterTextElement : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input) =>
            [new ElementBlueprint(ElementKind.Text, Text: Value(input, "Text"))];
    }

    private sealed class CraftedResultText : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var initial = Number(input, "InitValue") ?? 0;
            var change = Number(input, "ChangeAmount") ?? 0;
            var result = ImmutableArray.CreateBuilder<BehaviorContribution>();
            void Set(string role, string property, string value)
            {
                if (Reference(input, role) is { } target) result.Add(new DescriptionContribution(property, value, Target: target));
            }
            // The installed widget settles at InitValue + ChangeAmount. TargetValue is not
            // consumed by its OnLateUpdate implementation, even though shipped XML binds it.
            Set("ValueTextWidget", "FloatTarget", Invariant(initial + change));
            Set("ChangeValueTextWidget", "FloatTarget", Invariant(change));
            Set("ChangeValueTextWidget", "ShowSign", "true");
            Set("ChangeValueTextWidget", "IsVisible", change != 0 ? "true" : "false");
            var brush = Value(input, change > 0 ? "PositiveChangeBrush" : change < 0 ? "NegativeChangeBrush" : "NeutralBrush");
            if (brush.Length > 0)
            {
                Set("ValueTextWidget", "Brush", brush);
                if (change != 0) Set("ChangeValueTextWidget", "Brush", brush);
            }
            if (initial == 0)
                Set("LabelTextWidget", "CurrentState", Boolean(input, "IsExceedingBeneficial") ? "Bonus" : "Penalty");
            return result.ToImmutable();
        }
    }

    private sealed class ChangeText : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var amount = Math.Truncate(Number(input, "Amount") ?? 0);
            var text = Signed(amount);
            if (Boolean(input, "UseParentheses")) text = "(" + text + ")";
            var result = ImmutableArray.CreateBuilder<BehaviorContribution>();
            result.Add(new DescriptionContribution("Text", text));
            result.Add(new DescriptionContribution("IsVisible", Boolean(input, "ShouldBeVisible", true) && amount != 0 ? "true" : "false"));
            var brush = Value(input, amount > 0 ? "PositiveBrushName" : "NegativeBrushName");
            if (amount != 0 && brush.Length > 0) result.Add(new DescriptionContribution("Brush", brush));
            return result.ToImmutable();
        }
    }

    private sealed class RelationText : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var amount = Math.Truncate(Number(input, "Amount") ?? 0);
            var color = Value(input, amount > 0 ? "PositiveColor" : amount < 0 ? "NegativeColor" : "ZeroColor");
            return color.Length == 0 ? [new DescriptionContribution("Text", Signed(amount))] :
                [new DescriptionContribution("Text", Signed(amount)), new DescriptionContribution("Brush.FontColor", color)];
        }
    }

    private sealed class NumericText(string property, bool integer) : IBehaviorHandler
    {
        public ImmutableArray<BehaviorContribution> Contribute(BehaviorInput input)
        {
            var number = Number(input, property) ?? 0;
            if (integer) number = Math.Truncate(number);
            var text = Invariant(number);
            // Scoreboards clear zero text without collapsing the widget's layout slot.
            if (property == "ValueAsInt" && number == 0 && !Boolean(input, "ShowZero")) text = "";
            return property == "Count"
                ? [new DescriptionContribution("Text", text), new DescriptionContribution("IsVisible", number > 1 ? "true" : "false")]
                : [new DescriptionContribution("Text", text)];
        }
    }
}
