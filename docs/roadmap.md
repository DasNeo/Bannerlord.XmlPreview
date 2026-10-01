# Bannerlord.XmlPreview Development Roadmap

Status: 2026-09-29

## Product direction

Bannerlord.XmlPreview combines three product goals in this order:

1. renderer fidelity
2. analysis and diagnostics
3. authoring support

When these goals conflict, the earlier goal wins. The supported reference scope is the current Bannerlord version and its Native modules. Mod prefabs and data assets should work through the same Gauntlet model. Executable mod-defined widgets are outside the supported execution boundary and follow [ADR 0001](./adr/0001-do-not-execute-mod-code-widgets.md).

The project uses the canonical terms in [GLOSSARY.md](../GLOSSARY.md). In particular, a Correctly Rendered Prefab requires a matching Render Structure, tolerated pixel differences, and no unexplained Diagnostics warnings.

## Roadmap overview

| Stage | Outcome |
|---|---|
| M0 | Current async, performance, and responsibility refactor is stable and committed |
| M1 | Renderer fidelity is objectively measurable through structural and visual regression |
| M2 | Bannerlord-specific widget behavior runs through a generic Core Behavior Graph |
| M3 | Image and asset fidelity covers Native and mod-provided data assets |
| M4 | Authoring loop supports persistent scenarios, dependency watching, navigation, and visual comparison |

M0 precedes the four product milestones. Work should remain vertical and reviewable. New fidelity behavior requires a fixture and regression coverage.

Detailed execution steps live in the [milestone implementation plans](./implementation-plans/README.md).

## M0: Stabilize the current refactor

### Scope

Finish and review the existing async loading, cancellation, environment warm-up, installation indexing, file watching, diagnostics timing, and responsibility extraction work. Preserve the current work unless review identifies a concrete defect; do not discard it wholesale.

### Required verification

- The solution builds cleanly.
- All six Phase-9 fixtures complete with honest statuses.
- Truncation is reported as `Partial`, never `Completed` or `Passed`.
- The tournament fixture either renders fully with an appropriate budget or uses a representative mock size that stays within the configured budget.
- Async loading has an automated integration test using a deliberately slow loader.
- The integration test proves cancellation-token propagation, latest-load-wins behavior, and absence of UI publication from cancelled loads.
- Documentation reflects the resulting project structure and commands.
- The working tree ends in an intentional, clean Git state.

### Exit criteria

Build, Phase 9, code review, async/cancellation verification, documentation, and Git state are all complete. M1 does not start on top of an ambiguous refactor.

## M1: Measurable visual fidelity

### VisualRegression module

Create a deep `VisualRegression` module with a small interface. Its inputs are a prefab, a `PreviewScenario`, and reference metadata. Its outputs are a structured comparison report and the relevant image artifacts. Capture, Render Structure comparison, Layout Snapshot comparison, pixel comparison, masks, status calculation, and artifact generation remain hidden implementation details.

The same module serves two callers:

- `dotnet test` for automated regression checks
- a CLI for artifact generation and explicit baseline acceptance

The CLI must never replace a baseline automatically. The explicit accept operation presents reference, actual, diff, overlay, and metrics before writing accepted artifacts.

### Run modes and statuses

Supported run modes:

- `Full`: Render Structure, Key Bounds, and pixel comparison are required.
- `StructureOnly`: Render Structure and Key Bounds are required; pixel comparison is visibly reported as `NotRun`.

`Passed` means that every check required by the selected mode passed. Other top-level outcomes are `Failed`, `Partial`, `MissingBaseline`, `InvalidScenario`, and `CaptureFailed`. Every non-`Passed` result returns a non-zero exit code unless the caller explicitly permits an expected `Partial` result.

Public pull-request CI runs `StructureOnly`. A trusted local or private job runs `Full` with the optional Reference Package. Public CI must never imply that pixel comparison ran when the package was absent.

### Render Structure and layout

Structural comparison targets the expanded Gauntlet widget tree after includes, templates, and behavior resolution. The WPF Visual Tree is an implementation detail and is not compared.

Two immutable snapshots remain distinct:

- `RenderStructureSnapshot`: semantic expanded widget tree before WPF layout, without final WPF bounds
- `LayoutSnapshot`: Key Bounds captured after final Arrange

Key Bounds include named widgets, the root, and explicitly selected layout nodes. Comparing every WPF element would create excessive noise; comparing only named widgets would miss container errors.

### Reference screenshots and package

Reference Screenshots are captured manually in the game. The initial Capture Profile is:

- 1920 x 1080
- UI scale 1.0
- English
- sRGB
- fixed game version

Each reference also records module version, scene, data state, and crop rule. Scene and data state belong to the associated `PreviewScenario`.

The Reference Package is optional and distributed separately from the open-source repository. The repository contains scenarios, metadata, and expected hashes. Each package is immutable for a game version, module version, and Capture Profile. A hash mismatch invalidates the package; it never causes a silent update.

For tolerance calibration, capture every selected reference five times under identical conditions. Measure internal variance, then define a documented global threshold slightly above that variance. Comparison uses equal pixel dimensions, normalized sRGB RGB data, maximum channel difference, and the proportion of differing pixels. Alpha is ignored after background composition. Fixture-specific overrides require written justification.

Every dynamic mask in a `PreviewScenario` requires a name, reason, and bounds. Masked regions remain visible in reports and are never silently treated as matching.

### Initial suite

Small fixtures:

1. `Standard.Window`
2. `Standard.ScrollablePanel`
3. `HintTooltip`
4. `Crosshair`
5. `Standard.DropdownWithHorizontalControl`
6. `SPOptions/OptionItem`

Full Native screens:

1. `SPOptions/Options`
2. `SPConversation`
3. `SingleQueryPopup`

All nine Native cases must finish as `Passed` in `Full` mode at M1 completion. `Partial` is not an accepted completion result for this suite.

### Diagnostics rules

There is no global warning-count allowance. Every expected warning is recorded in its `PreviewScenario` with diagnostic code, widget path, and reason. A new or changed warning fails the comparison.

### Performance recording

M1 measures but does not gate performance. Run five warm iterations and report median and p95 together with the machine and Capture Profile. Record graph matching, sorting, total graph time, rule count, edge count, and match count when the Behavior Graph becomes available.

Target budgets for later enforcement are:

- small fixture: p95 below 300 ms
- full screen: p95 below 1 s
- stress case: p95 below 3 s

An exceeded target starts a focused performance effort. It does not authorize fidelity-damaging pruning.

### Exit criteria

- All nine selected Native cases pass in `Full` mode.
- An intentional layout change produces a reproducible `Failed` result.
- A missing baseline produces `MissingBaseline`.
- An invalid scenario produces `InvalidScenario`.
- Baselines change only through the explicit accept operation.
- Reports contain Render Structure, Key Bounds, pixel metrics, Diagnostics, mode, and artifacts.
- Public CI can execute the complete structural portion without the Reference Package.

## M2: Behavior Graph and widget fidelity

### Architecture goal

Remove concrete Bannerlord widget names and DemoData/global preview state from Core before adding another large fidelity wave. Core owns the graph engine, contribution types, invariants, and Core fallback rules. The Bannerlord adapter owns every Bannerlord matcher and rule. Mods may provide XML and data assets but never executable rules.

The Behavior Graph is rebuilt for every render run. It has no cache and no version-dependent cache key. Rules are matched and `Before`/`After` edges are sorted dynamically, then all graph state is discarded after the run. This keeps semantics current and avoids cache invalidation. Performance remains measured and is optimized only after evidence identifies graph construction as a bottleneck.

### Fixed phases

The public lifecycle contains exactly five phases:

1. `Describe`
2. `Instantiate`
3. `Connect`
4. `PrepareLayout`
5. `Inspect`

`Inspect` runs after Arrange and is read-only. Adding another public phase requires explicit project-level review and agreement.

Each phase has its own immutable contribution type:

- `DescriptionContribution`
- `ElementBlueprint`
- `ConnectionContribution`
- `LayoutContribution`
- `InspectionContribution`

Handlers are stateless. Run-local results move between phases as immutable data keyed by a stable Widget ID. A Widget ID consists of the canonical prefab path, expanded XML path, and the template or list-instance key from the `PreviewScenario`; it never uses WPF position or object identity.

Handlers return declarative contributions. They do not access WPF, raw `PrefabRenderContext`, or global data. Core materializes the contributions and enforces phase rules. `InspectionContribution` may report Diagnostics, support status, Key Bounds markers, and debug metadata, but cannot change layout, visibility, assets, or state.

### Matching and ordering

Rules may match on:

- exact widget type
- trusted base type
- declared capability
- XML attributes
- parent or child context

Matchers do not depend on scene or `PreviewScenario` values. Those values belong inside handlers.

Every rule has a global stable `RuleId` using `core.<capability>` or `bannerlord.<widget-family>.<purpose>`. Rules use explicit `Before` and `After` edges and a deterministic topological sort.

For `Instantiate`, exactly one specialized rule may win. Core fallback applies only when no specialized rule matches and does not count as a conflict. Two specialized exclusive matches fail the run. Each property has one writer per phase. Multiple contributions may combine ordered collections only through an explicitly defined append contract.

### Preflight, connections, and errors

Matching, sorting, conflict detection, and cycle detection for the whole expanded prefab complete before any WPF element is materialized. A graph error fails only the current render, not the application. The last good preview stays visible and is clearly marked stale.

`ConnectionContribution` classifies every reference as `Required` or `Optional`:

- missing `Required`: `Failed`
- missing `Optional`: Diagnostic and only allowed with a scenario explanation
- unknown requirement: `Failed`

Other error rules:

- graph configuration error: current render is `Failed`
- handler exception: `Failed`
- cancellation: publish no partial result
- known product limitation: visible placeholder and `Partial`
- missing Native behavior: `Partial`, therefore a Full-Golden failure
- unknown regular XML widget: generic Core widget, Diagnostic, and `Partial`
- Code Widget: clearly labeled placeholder and `Partial`

A new preview becomes visible only after the complete render succeeds. Cancellation is checked between widgets and phases; an isolated new root is discarded on cancellation, while the last good preview remains visible.

### Diagnostics

Every run records a compact graph summary. A full Behavior Trace records matchers, selected and skipped rules, ordering, contributions, and support status per widget only when debugging is enabled, a failure occurs, or VisualRegression artifact generation requests it.

### Architecture pilot

Implement the graph first for three contrasting cases:

1. `ValueBasedVisibilityWidget`: local state
2. `ScrollablePanel`: children and widget references
3. `StateSyncWidget`: cross-widget, phase-spanning behavior

Then migrate `DimensionSyncWidget` as the fourth proof case. It must exercise real `PrepareLayout` behavior without enlarging the public interface.

Pilot acceptance criteria:

- no more than three public entry points
- no Bannerlord names in Core
- no raw `PrefabRenderContext` in the adapter
- no arbitrary WPF mutation
- old special-case logic for all four pilot widgets removed
- VisualRegression results unchanged or deliberately improved
- fourth case requires no interface expansion
- failure, `Partial`, and cancellation behavior are explicit
- total complexity is lower than the replaced implementation

Only after the fourth case passes should the project record the final Behavior Graph ADR and continue widget-family migration. Each migrated family removes its old name check and adds Behavior tests in the same change. The project must not retain a permanent old/new parallel pipeline.

### Remaining M2 fidelity scope

- close remaining Wave-1 issues
- support and verify `InputKeyVisualWidget`
- support and verify sliders
- support and verify editable text
- support and verify sort and toggle behavior
- keep all affected VisualRegression cases passing

### Exit criteria

- Core contains no concrete Bannerlord widget names.
- Core has no DemoData reference or global preview state.
- A new Bannerlord Widget Behavior requires no Core edit.
- Pilot and fourth proof case pass.
- Final graph contract is recorded in an accepted ADR.
- Wave-1 remainder and selected Wave-2 families have regression coverage.

## M3: Image and asset fidelity

### Scope

- implement real behavior for `ImageIdentifierWidget`, `ImageWidget`, and `MaskedTextureWidget`
- resolve mod-provided data assets according to module dependency order
- use a real provider when dynamic image data exists
- show a clear runtime-only placeholder and `Partial` when data exists only in the game runtime
- never substitute a silent fallback image

### Acceptance screens

1. Native `SPConversation.xml`: verifies `ImageWidget`, `ImageIdentifierWidget`, and deterministic provider data.
2. Native `BannerBuilderScreen.xml`: verifies `MaskedTextureWidget` and static images. `BannerTableauWidget` remains an explicit runtime-only `Partial` follow-up and is not an M3 blocker.
3. Mod `AgricultureEstate/GUI/Prefabs/LandManagement.xml`: verifies mod sprites and Native brushes using the strongest existing mod baseline.

### Exit criteria

- all three target image families have real behavior
- module dependencies control asset lookup
- no silent replacement images remain
- runtime-only content is visible and reported as `Partial`
- both Native image targets pass their scoped assertions
- the mod screen passes without a Code Widget; an explicit Code Widget case yields the expected `Partial`

## M4: Authoring loop

### Scope

- load, save, and switch persistent `PreviewScenario`s
- watch the main XML, includes, brushes, and relevant assets
- navigate from Diagnostics and Preview Tree to file and line
- provide side-by-side and overlay comparison
- do not build an embedded XML editor

If a changed dependency fails to load, show the error and preserve the last good preview with a visible stale marker.

### Exit criteria

- scenarios persist all required bindings, lists, state, resolution, and dynamic masks
- dependent file changes reload automatically
- failed reloads never replace the last good preview
- Diagnostics and Preview Tree open the correct source location
- side-by-side and overlay work with the active reference

## Fidelity reporting

Never publish a binary `Supported` claim. Report every widget family as one of:

- `Verified`: Full-Golden passed
- `Implemented`: behavior exists, but no Full-Golden exists
- `Partial`: intentionally incomplete fidelity
- `Placeholder`: only a visible replacement exists
- `Unknown`: not evaluated

## Release plan

Publish source and the first Preview binary after M3. The release must:

- identify itself as Preview quality
- publish Fidelity-Status per widget family
- keep the Reference Package optional and separate
- state that only the current Bannerlord version is officially supported

When Bannerlord changes version, create a new immutable Reference Package and rerun full validation. Previous packages remain archived but unsupported.

## Deliberately deferred decisions

The roadmap does not yet fix:

- exact test framework or image-comparison library
- exact manifest and `PreviewScenario` serialization format
- calibrated pixel thresholds
- performance optimization technique
- final Behavior Graph ADR wording before the fourth proof case
- distribution rights and hosting mechanism for the optional Reference Package

These choices are either reversible or require evidence produced by earlier milestones.
