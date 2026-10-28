# Text, brushes, runtime images and frame layout

The Bannerlord adapter now preserves `RichTextWidget` inheritance instead of
materializing rich-text subclasses as brush containers. `AutoHideTextWidget`,
`AutoHideRichTextWidget` and `AutoHideZeroTextWidget` derive visibility from
their text; empty-text widgets also update a declared `WidgetToHideIfEmpty`.
Scrolling text widgets show their bound `ActualText` in a settled static state.
Named target connections are validated before rendering.

Rich text preserves ordinary bitmap-font text and supports inline sprite
images, line breaks, color spans and basic text tags. Unknown tags, invalid
markup, named styles and missing inline sprites produce Diagnostics and
Partial status rather than silently claiming full fidelity. Hyperlinks are
display-only, and scrolling animation is not simulated. Inline wrapping and
image alignment have synthetic coverage; in-game pixel calibration remains
separate.

`BoolBrushChangerBrushWidget` selects its declared true/false brush and applies
it to a target or itself, optionally propagating only to brush descendants.
`SelectedStateBrushWidget` and crafting material widgets select their brush
states. `IconBrushWidget` selects a source layer, copies its sprite to target
layers, and honors source-size/style settings. The default-layer sprite
changer replaces only the default layer and hides when no sprite is selected.
Skill icons select their skill sprite and use an available requested variant.
Selections are scoped to each widget render and included in brush cache keys;
shared asset definitions are never mutated. Missing selected layers remain
visible in Diagnostics.

Tableau widget families render a labeled Runtime-only Placeholder and produce
Partial status. A scenario may explicitly supply `Preview.ImageSprite` for a
static region; that does not reproduce game tableau rendering or claim that a
crafted weapon can be reconstructed from `StringId` alone. Labels are centered
so they remain readable when a large tableau is clipped by its parent.

Brush containers no longer acquire invented gold borders or rounded corners.
Declared sprite/layer chrome remains. Widget `ExtendLeft`, `ExtendRight`,
`ExtendTop` and `ExtendBottom` expand sprite painting without changing widget
layout bounds. Flow layouts honor position offsets without moving sibling
allocation. CoverChildren layout iterates to a bounded settled result; failure
to converge produces a Diagnostic and Partial status.

## Capture scale and the crafting reference

Capture Profiles accept an explicit finite `UiScale` from 0.25 to 4. The CLI
uses a logical viewport of `Width / UiScale` by `Height / UiScale`, then captures
at the requested physical pixel size. Key Bounds are transformed into those
same physical coordinates. Scale 1 remains the default and keeps existing
capture semantics. No automatic scale is inferred from viewport resolution.

The user confirmed the crafting Reference Screenshot came from 3440x1440.
The original scale and crop are unknown. An effective scale of 4/3 is consistent
with its fixed-width left panel and row spacing; the diagnostic scenario
records this as an inference, not confirmed capture metadata. The local
`artifacts/presentation-validation/crafting-final` capture uses that profile.
Its weapon tableau is deliberately a placeholder. The original bound flag
icon markup was not supplied, so the reference's three weapon flags are not
invented. No calibrated pixel comparison with the cropped reference is claimed.

`PresentationBehaviorTests` covers rich inheritance against installed game
metadata, text/visibility propagation, inline images, brush selection and
isolation, source dimensions/styles, runtime placeholders, sprite extensions,
flow offsets, CoverChildren convergence and capture-scale pixel/bounds
agreement. The reviewed structural geometry changes are recorded in the new
`presentation-portable-baselines` set; previous accepted sets are unchanged.
