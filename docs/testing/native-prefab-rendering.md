# Shared Native rendering fixes

The inventory work also applies to Native: scoped visual definitions, brush
states, brush-backed list layout, inherited text/provider-image widgets, and CLI
bitmap fonts are shared renderer features.

A local Bannerlord v1.4.7 survey of all 149 XML files in
`Modules/Native/GUI/Prefabs` found 19 source files declaring visual definitions
and 68 Native brushes declaring `BaseBrush`. Twenty-nine expanded prefabs refer
to those inherited brushes. Game files were not edited.

## Additional behavior

- `BaseBrush` resolves through the active module dependency scope, including
  module overrides, multi-level chains, layers, styles, fonts, and alignment.
  Child properties override inherited values without modifying parent brushes.
  Resolution is cached with asset metadata and cleared by invalidation. Missing
  parents and inheritance cycles fail explicitly when the affected brush is used.
- `BoolStateChangerWidget` chooses `TrueState` or `FalseState` before layout.
  `IncludeChildren` uses the expanded per-render tree, including scenario list
  instances. A declared target is a Required Connection; omission targets the
  changer itself, matching the installed game widget. These widgets no longer
  receive the generic target-highlighting effect or generic brush-frame chrome.

`SPChatLog` verifies a concrete change: `ShouldHaveOffset=false` places the chat
panel at X=27; `true` selects its declared `Offset` state and places it at X=187
at 1920×1080. `AgentStatus` verifies that the health canvas uses its declared
`Passive` dimensions, 80% of the default width. Static final states are previewed;
the game's animated transitions and commands are not executed.

## Verification

Portable tests cover both boolean branches, optional propagation to children,
state geometry before Arrange, missing targets, module overrides, brush chains,
parent isolation, concurrent resolution, invalidation, missing bases and cycles.

The installed tests also resolve every Native inherited brush in Native module
scope and exercise the real chat-log and agent-status prefabs:

```powershell
$env:BANNERLORD_ROOT = 'C:/path/to/Mount & Blade II Bannerlord'
$env:BANNERLORD_NATIVE_ARTIFACTS = 'C:/path/to/native-rendering-artifacts'
dotnet test tests/Bannerlord.XmlPreview.VisualRegression.Tests --filter FullyQualifiedName~NativeStateRenderingTests
```

The optional artifact directory receives paired PNGs and JSON with Key Bounds,
Behavior Traces and Diagnostics for default/offset chat and default/passive HUD
states. Existing portable Native comparisons remain separate from these checks;
no accepted baselines are replaced automatically.

## Survey limits

Both the before and after surveys completed all 149 render attempts without
exceptions, reporting 96 `Complete` and 53 `Partial` captures under generated
audit bindings. Only 37 of the `Complete` cases had no Diagnostics. These are
capture statuses, not claims that all Native prefabs are Correctly Rendered.

The survey uses explicit generated values, not real gameplay data or reference
screenshots. Some diagnostics concern absent optional controls, valid prefabs
without constants, unresolved XML references, or missing scenario inputs.
Portraits, tableaus, input-key art and other runtime content can remain Partial.
Other specialized Widget Behavior, including dynamic icon-layer selection, still
requires separate verification. In-game pixel fidelity is not certified.
