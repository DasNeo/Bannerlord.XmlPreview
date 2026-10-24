# SandBox rendering survey

Surveyed the installed Bannerlord v1.4.7 `Modules/SandBox/GUI/Prefabs`
directory: 177 prefabs, including standalone `Window` documents. Three XML
files under `Inventory/.idea` are IDE metadata and were excluded from the
prefab totals. No game files or accepted baselines were changed.

## Shared fixes and additional behavior

The inventory and Native fixes apply across module boundaries. SandBox has
46 source files declaring visual definitions, six using
`BoolStateChangerWidget`, and 80 brush declarations using `BaseBrush` across
12 brush files. These benefit from scoped visual definitions, state-dependent
geometry, boolean state selection and inherited brush resolution.

An additional fix supports display-only sliders. Installed `SliderWidget` IL
shows that filler sizing runs independently of handle placement. The shipped
`Mission/Disguise/MissionLosingTarget.xml` and `Mission/MissionQuestBar.xml`
declare `Handle="SliderHandle"` without that child. Previously this failed
the Required Connection check. The handle is now an Optional Connection:
a Preview Scenario must explain its absence, the Diagnostic remains visible,
and the filler still receives the selected range fraction before Arrange.
Existing sliders retain their handle positioning and filler sizing.

Portable tests cover horizontal and vertical fill, empty/full ranges and the
required explanation. An installed regression test checks both real mission
prefabs at 25% and 75%, asserting filler width against slider width.
Specialized suspicion colors and animated transitions are not verified by
these geometry checks.

## Survey results

Generated audit bindings at 1920×1080 produced:

| Render attempts | Complete | Partial | Failed |
| --- | ---: | ---: | ---: |
| Initial survey | 61 | 107 | 9 |
| With slider fix and explicit optional-control explanations | 65 | 109 | 3 |

Only 19 Complete captures had no Diagnostics. The six fewer failures comprise
two sliders and four prefabs needing explanations for absent optional controls;
the latter are scenario changes, not renderer fixes. These capture statuses
do not certify that the prefabs are Correctly Rendered. Inputs are generated
values rather than gameplay data, and no calibrated Reference Screenshots
were supplied for this survey.

Three failures remain explicit:

- `Map/MapBar.xml` and `Map/ArmyOverlay.xml` use
  `MapBar.GatherArmy.Icon`, which declares missing base
  `MapBar.Left.Generic.Icon.Base`. No definition was found in the installed
  module XML. Correct the base identity or supply its intended definition in
  an asset override after verifying the intended appearance; do not silently
  treat the child brush as complete.
- `Clan/Finance/ClanFinance.xml` contains a scroll panel pointing to
  `ClanFinanceElementsRect\ClanFinanceElementsListPanel` without that inner
  list panel. Its other scroll panel also references a scrollbar with a
  different name from the declared child. Repair those references or restore
  the intended content in a separate override; preserve Required Connection
  validation in the renderer.

Resolving all 80 SandBox inherited brushes in SandBox dependency scope succeeds
for 78. Besides the map icon, the unused-in-this-survey
`GameOver.Category.Visual` references missing base
`GameOver.Category.Visual.Effect`. Both absent bases remain asset errors.

Remaining fidelity work includes dynamic brush-layer selection
(`IconBrushWidget` in three source files and
`DefaultLayerSpriteChangerBrushWidget` in four), specialized Widget Behavior,
portraits, tableaus and other runtime images. Missing sprites, unresolved
references and generated input limitations remain in Diagnostics.

## Local artifacts and verification

`artifacts/sandbox-rendering-audit/harness` contains the local survey runner.
`after/summary.json` records each prefab and its Diagnostics;
`after/brush-summary.json` records all inherited brush resolutions. Each
attempt has its scenario, Render Structure and Key Bounds where available,
with PNGs for selected screens. Initial results, including IDE metadata, are
retained in `before/summary.json`; `before/prefab-summary.json` excludes that
metadata for the table above.

The solution builds with zero warnings/errors. The two regression executables
report 244 passed tests and one optional in-game pixel test skipped. Set
`BANNERLORD_ROOT` to run `SandBoxRenderingTests` against a local installation.
The shipped asset/reference errors above are intentionally not hidden by those
tests or by the survey.
