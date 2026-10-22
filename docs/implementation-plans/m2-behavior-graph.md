# M2 Implementation Plan: Behavior Graph and Widget Fidelity

Historical plan; completed 2026-10-12. The starting point and migration steps describe the original work. See the [plan index](README.md) for current guidance.

## Objective

Move Bannerlord-specific widget behavior out of Core into a per-render Behavior Graph. Remove Core's DemoData dependency and global preview state. Prove the graph with four contrasting widgets before migrating additional families.

## Prerequisite

M1 is complete. Render Structure, Layout, Diagnostics, and pixel changes can be measured before the renderer architecture changes.

## Starting point

Current behavior is split across:

- `BannerlordWidgetNodeFactory.cs` for name and reflection mapping
- concrete Core node types
- concrete Bannerlord name checks in `PrefabRenderContext.cs`
- `ApplyDeferredReferenceBehaviors()` in `PrefabRenderContext.cs`
- Core references to `PreviewDemoCatalog` and DemoData

The migration must remove old logic as each Widget Behavior moves. A permanent dual pipeline is prohibited.

## Target module shape

Core owns:

- Behavior Graph contracts and engine
- five Behavior Phases
- phase-specific Behavior Contribution types
- matcher execution and deterministic ordering
- conflict, cycle, and single-writer validation
- Core fallback rules
- run-local immutable state
- Behavior Trace production

The Bannerlord adapter owns:

- every Bannerlord matcher
- every Bannerlord Rule ID
- trusted Bannerlord type metadata
- Bannerlord Widget Behavior handlers
- Code Widget classification

Mods supply XML and data assets only. They never register executable rules.

## Work package 2.1: Remove global preview state from Core

1. Define an immutable run-local preview-data snapshot derived from the active Preview Scenario.
2. Pass the snapshot into rendering instead of reading static dictionaries.
3. Move DemoData-backed values behind an app or Bannerlord adapter that creates the snapshot.
4. Remove `PreviewDemoCatalog` use from `PrefabRenderContext` and Core nodes.
5. Remove the DemoData project reference from `Bannerlord.XmlPreview.Core.csproj`.
6. Add parallel-render tests proving that two scenarios cannot leak values into each other.

The Bannerlord adapter may continue to use DemoData temporarily to construct a snapshot, but Core must not depend on the DemoData project.

## Work package 2.2: Define graph contracts

Add Core contracts for:

- `BehaviorPhase`: `Describe`, `Instantiate`, `Connect`, `PrepareLayout`, `Inspect`
- `DescriptionContribution`
- `ElementBlueprint`
- `ConnectionContribution`
- `LayoutContribution`
- `InspectionContribution`
- stable `RuleId`
- stable Widget ID
- matcher criteria
- explicit `Before` and `After` edges
- support status and structured graph errors

Contract rules:

- handlers are stateless
- contributions are immutable and declarative
- handlers cannot access WPF or raw `PrefabRenderContext`
- `Inspect` is read-only after Arrange
- adding a sixth public phase requires explicit project review
- public graph interface has no more than three entry points

## Work package 2.3: Implement per-render graph planning

For every render run:

1. expand the prefab and assign stable Widget IDs
2. match rules using exact type, trusted base type, capability, XML attributes, and parent or child context
3. reject matchers that depend on scene or Preview Scenario values
4. construct phase-specific contributions and dependency edges
5. topologically sort by stable Rule ID and explicit edges
6. validate cycles, duplicate Rule IDs, exclusive Instantiate conflicts, and single-writer constraints
7. finish preflight for the complete expanded tree before materializing WPF
8. discard all graph state after the render run

Do not add a graph cache or game-version cache key. Record matching, sorting, total graph time, rule count, edge count, and match count.

## Work package 2.4: Implement execution semantics

Core materializes declarative contributions:

- `Describe`: normalize classification and generic properties
- `Instantiate`: produce exactly one specialized Element Blueprint or use Core fallback
- `Connect`: resolve Required and Optional Connections
- `PrepareLayout`: apply generic layout intent before Measure
- `Inspect`: emit Diagnostics, support status, Key Bounds markers, and debug metadata without mutation

Rules:

- Core fallback applies only when no specialized Instantiate rule matches.
- Two specialized exclusive Instantiate matches fail the render.
- One writer may set a property in a phase.
- Ordered collections combine only through an explicit append contract.
- missing Required Connection fails
- missing Optional Connection requires a scenario explanation and still emits a Diagnostic
- unknown connection requirement fails
- handler exception fails
- known product limitation produces a visible placeholder and `Partial`
- cancellation publishes no new root

Build the new root in isolation. Publish it only after the complete render succeeds. On failure or cancellation, retain the Last Good Preview and mark it stale.

## Work package 2.5: Add graph diagnostics

Always record a compact summary containing:

- matched rule count
- edge count
- contribution count by phase
- support-status counts
- matching, sorting, and total graph time

Produce a full Behavior Trace per widget only when:

- debug tracing is enabled
- the render fails
- Visual Regression artifact generation requests it

The full trace includes matchers, selected and skipped rules, order, contributions, and support status.

## Work package 2.6: Pilot `ValueBasedVisibilityWidget`

1. Implement its matcher and stable Rule IDs in the Bannerlord adapter.
2. Express state interpretation through `Describe` and generic visibility intent.
3. Add Behavior interface tests.
4. Run affected Visual Regression fixtures.
5. Remove the corresponding name check and legacy behavior from `PrefabRenderContext` in the same change.

Purpose: prove local state without cross-widget references.

## Work package 2.7: Pilot `ScrollablePanel`

1. Implement specialized Instantiate behavior only where generic fallback is insufficient.
2. Express viewport, inner-panel, and scrollbar relationships as Connection Contributions.
3. Classify every reference as Required or Optional.
4. Cover missing-reference and ordering failures.
5. Remove migrated factory or deferred-reference special cases.
6. Keep `Standard.ScrollablePanel` Structure Only comparison and explicit viewport, inner-panel and scrollbar assertions passing.

Purpose: prove child structure and widget references.

## Work package 2.8: Pilot `StateSyncWidget`

1. Express cross-widget state relationships through Connect.
2. Carry run-local immutable results by Widget ID.
3. Verify deterministic ordering when multiple state relationships exist.
4. Remove the legacy `ApplyStateSyncWidgetBehavior` path.

Purpose: prove phase-spanning, cross-widget behavior without mutable handlers.

## Work package 2.9: Fourth proof with `DimensionSyncWidget`

1. Implement real `PrepareLayout` semantics.
2. Verify Required and Optional source references.
3. Resolve the existing Wave-1 discrepancy with focused fixtures.
4. Remove `ApplyDimensionSyncWidgetBehavior` and related name checks.
5. Confirm that no public graph interface or phase must grow.

If the fourth case requires interface growth, stop migration, revise the graph contract, and rerun all four pilots.

## Work package 2.10: Record the Behavior Graph ADR

After all four proof cases pass:

1. document the final graph interface and five phases
2. record the per-render, no-cache decision
3. record why handlers return declarative contributions
4. record rejection of direct WPF mutation, permanent dual pipelines, and executable mod rules
5. include evidence that the fourth case required no interface expansion

Do not mark the ADR accepted before this evidence exists.

## Work package 2.11: Migrate remaining M2 fidelity scope

Migrate one widget family per vertical change:

1. matcher and rules
2. Behavior tests
3. affected fixture and Visual Regression coverage
4. removal of old name checks and dead node behavior
5. updated Fidelity Status

Target families:

- remaining Wave-1 issues
- `InputKeyVisualWidget`
- sliders
- editable text
- sort behavior
- toggle behavior

## Test strategy

Test through the graph interface:

- deterministic topological order
- cycle detection
- duplicate Rule ID detection
- Instantiate conflict detection
- fallback selection
- single-writer enforcement
- append ordering
- Required and Optional Connections
- cancellation and no-publication behavior
- handler failure mapping
- Code Widget and unknown-widget `Partial` behavior
- scenario isolation
- all four pilots

Visual Regression remains the acceptance layer for observable output. Tests must not assert graph implementation collections beyond documented report fields.

## Exit checklist

- [x] Core has no DemoData project reference or global preview state.
- [x] Core contains no concrete Bannerlord widget names.
- [x] Graph preflight completes before WPF materialization.
- [x] Five fixed phases and contribution contracts are enforced.
- [x] Four proof widgets pass without interface expansion.
- [x] Legacy logic for migrated widgets is deleted.
- [x] Behavior Graph ADR is accepted after proof.
- [x] Selected Wave-1 and Wave-2 families have Behavior and Visual Regression coverage.

The accepted architecture is recorded in [ADR 0002](../adr/0002-per-render-behavior-graph.md). Current verification procedures and pixel-comparison limits are in the [Visual Regression workflow](../../test-data/visual-regression/README.md).
