# Numeric text from widget inputs

The Bannerlord adapter renders settled numeric Widget Behavior from resolved
bindings and widget properties. There are no prefab filename checks or
hardcoded gameplay labels or values. A Preview Scenario still supplies input
data when the game view model is unavailable.

| Widget family | Input and settled presentation |
| --- | --- |
| `CounterTextBrushWidget` | `FloatTarget` or `IntTarget`; integer floor or two decimal places via `ShowFloatingPoint`, optional positive sign and declared clamping range |
| `CraftedWeaponDesignResultListPanel` | Referenced value counter receives `InitValue + ChangeAmount`; change counter receives signed `ChangeAmount`, is hidden at zero, and uses the declared positive/negative brushes; the value uses the neutral brush when unchanged |
| `ChangeAmountTextWidget` | Signed `Amount`, optional parentheses, positive/negative brush, zero hiding and `ShouldBeVisible` |
| `InventoryArmorAnimationTextWidget` | `FloatAmount` becomes displayed text |
| `RelationTextWidget` | Signed `Amount` and declared positive/negative/zero font colors |
| `BarterItemCountTextWidget` | `Count` becomes text; counts of one or less hide the count widget |
| `ScoreboardAnimatedTextWidget` | `ValueAsInt` becomes text; `ShowZero=false` clears zero text without collapsing the layout slot |

Rules match widget types and trusted inheritance. The counter uses a text
Element Blueprint rather than inherited brush-container chrome. The crafting
rule passes numeric properties to its explicitly referenced counters before
the counter formatting rule. Value, change and label references are Required
Connections; missing targets fail preflight. Every render and each expanded
list item retains its own values.

Behavior was checked against installed v1.4.7 widget method IL without invoking
game widget code. The crafting implementation does not read its `TargetValue`,
`IsOrderResult` or gold-effector reference during `OnLateUpdate`; the preview
therefore does not invent behavior for those properties. `InitValue=0` selects
the label's `Bonus` or `Penalty` state according to `IsExceedingBeneficial`.
The shipped popup's absent gold-effector reference remains a loader Diagnostic.

This preview represents the settled state, not elapsed animation time. Counter
interpolation, fade-in, armor-change transitions and scoreboard animation are
not simulated. Numeric formatting is invariant for the supported English
Capture Profile. Missing numeric inputs use zero defaults; scenarios should
supply meaningful values rather than treating those defaults as gameplay data.

`NumericTextBehaviorTests` checks formatting, signed values, visibility,
clamping, brush/color selection, connection validation and repeated binding
changes without mutating the source document. Its optional installed test
loads the real crafting popup, expands separate decimal/integer property rows,
and renders positive and negative changes from the same loaded document.
Set `BANNERLORD_ROOT` to enable that test.

The diagnostic capture in local `artifacts/numeric-text-validation/crafting`
now displays the reference's weight, reach, speed, damage and handling values
and bonuses. It is not an accepted pixel baseline. Subsequent frame, rich-text,
brush and runtime-placeholder work is documented in
[presentation rendering](presentation-rendering.md).
