# M1 Implementation Plan: Measurable Visual Fidelity

## Objective

Create a deep Visual Regression module that produces repeatable structural, layout, Diagnostics, and pixel outcomes for the nine accepted Native cases. M1 measures performance but does not optimize it.

## Prerequisite

M0 is complete. The repository has an honest technical status, deterministic Phase-9 exit behavior, automated load-cancellation coverage, and a clean baseline.

## Target module and seams

Add a dedicated `Bannerlord.XmlPreview.VisualRegression` project targeting `net10.0-windows`. It references Core and owns comparison, masking, status calculation, report generation, and artifact generation.

Keep snapshot production close to rendering:

- Core produces `RenderStructureSnapshot` and `LayoutSnapshot`.
- Visual Regression consumes snapshots and captured pixels.
- The CLI and automated tests call the same Visual Regression interface.
- File access and reference-package lookup remain internal or local-substitutable dependencies; do not expose them through the main interface.

Add a small CLI project, `Bannerlord.XmlPreview.VisualRegression.Cli`, for `compare`, `capture`, and `accept` workflows. Add a Visual Regression test project using the repository's selected test framework.

## Work package 1.1: Define result and request contracts

Define:

- run modes: `Full` and `StructureOnly`
- outcomes: `Passed`, `Failed`, `Partial`, `MissingBaseline`, `InvalidScenario`, and `CaptureFailed`
- `VisualRegressionRequest`: prefab identity, Preview Scenario, run mode, reference metadata, output location
- `VisualRegressionReport`: status, mode, comparison metrics, Diagnostics differences, artifact paths, timing
- a single main comparison operation on the Visual Regression interface

Invariants:

- `Passed` means every check required by the selected mode passed.
- `StructureOnly` reports pixel work as `NotRun`.
- every non-`Passed` result maps to a non-zero CLI exit code unless expected `Partial` is explicitly enabled
- accepting a baseline is a separate operation and never occurs during comparison

## Work package 1.2: Create stable snapshots

1. Add stable Widget ID generation using canonical prefab path, expanded XML path, and template or list-instance key.
2. Capture `RenderStructureSnapshot` after include/template expansion and current behavior resolution, before WPF layout.
3. Exclude the WPF Visual Tree from structural identity.
4. Capture `LayoutSnapshot` after final Arrange.
5. Select Key Bounds from named widgets, root, and scenario-declared layout nodes.
6. Serialize snapshots deterministically: stable child order, property order, invariant numeric formatting, and explicit schema version.

Tests must prove that object identity, allocation order, and Visual Tree helper elements do not alter snapshot identity.

## Work package 1.3: Introduce the minimum Preview Scenario format

M1 needs read-only persistent scenarios before M4 adds authoring UI.

Define a versioned scenario document containing:

- prefab identity
- bindings and list contents
- widget states
- preview resolution
- Key Bounds selectors
- expected warnings with code, Widget ID or path, and reason
- dynamic masks with name, reason, and bounds
- scene and data-state metadata used by Reference Screenshots

Place accepted scenario files under a repository test-data directory. Keep serialization behind a scenario loader module so M4 can add editing without changing consumers.

## Work package 1.4: Implement deterministic capture

1. Add headless WPF capture after final Measure, Arrange, and layout update.
2. Require exact pixel dimensions from the active Capture Profile.
3. Composite onto the defined background before comparison.
4. Normalize output to sRGB RGB.
5. Produce PNG output with deterministic metadata handling.
6. Return `CaptureFailed` with structured diagnostics when capture cannot complete.

Capture must operate on an isolated root and must not require the main window to be visible.

## Work package 1.5: Implement comparators

Implement separate internal comparators for:

- Render Structure equality
- Key Bounds equality or documented numeric tolerance
- Diagnostics equality using Expected Warnings
- pixel dimensions
- per-channel pixel difference
- proportion of differing pixels
- named dynamic masks

Aggregate comparator results inside the Visual Regression module. Callers receive one report rather than coordinating individual comparators.

## Work package 1.6: Implement artifact and acceptance workflow

For each Full comparison, write:

- actual PNG
- diff PNG
- overlay PNG
- normalized structural snapshot
- layout snapshot
- machine-readable report
- concise Markdown report

The `accept` command must:

1. refuse missing or invalid scenario metadata
2. display or identify reference, actual, diff, overlay, and metrics
3. require explicit invocation
4. update metadata and expected hashes together
5. never update an immutable Reference Package in place

## Work package 1.7: Define the Reference Package

Create a versioned manifest keyed by:

- game version
- module version
- Capture Profile
- package schema version

The repository stores manifest expectations, scenario files, and expected hashes. Reference PNG files remain in the separately supplied package root. Hash mismatch invalidates the package.

Initial Capture Profile:

- 1920 x 1080
- UI scale 1.0
- English
- sRGB
- fixed game version

Capture each reference five times, measure internal variation, and document the selected global Pixel Tolerance. Permit a fixture-specific override only with a written reason in metadata.

## Work package 1.8: Build the initial suite

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

For each case:

1. create the minimum Preview Scenario
2. capture and document the in-game reference
3. record Key Bounds and Expected Warnings
4. run Structure Only until structural drift is resolved
5. calibrate and run Full comparison
6. store reports as milestone evidence

## Work package 1.9: Integrate tests and CI

Add automated tests for:

- each comparator in isolation
- deterministic snapshot serialization
- mask validation
- package hash validation
- exit-code mapping
- explicit baseline acceptance
- all nine Structure Only cases
- all nine Full cases when a Reference Package is configured

CI behavior:

- pull requests run Structure Only
- a trusted local or private job runs Full
- missing Reference Package never appears as a successful Full run

## Work package 1.10: Record performance

For every case, run five warm iterations and report median and p95. Record machine identity, Capture Profile, load time, render time, capture time, comparison time, and artifact-write time. Do not make performance a gate in M1.

## Negative acceptance tests

- intentional layout change produces reproducible `Failed`
- missing baseline produces `MissingBaseline`
- invalid scenario produces `InvalidScenario`
- capture failure produces `CaptureFailed`
- new warning fails unless added as an Expected Warning with reason
- masked region remains visible in the report
- accept never runs as a side effect of compare

## Exit checklist

- [ ] Visual Regression module has one small caller-facing interface.
- [ ] CLI and tests use the same module.
- [ ] Render Structure and Layout Snapshots are deterministic.
- [ ] All nine Native cases pass in Full mode.
- [ ] Public CI runs the complete Structure Only suite.
- [ ] Negative acceptance tests pass.
- [ ] Reference Package manifest and calibration are documented.
- [ ] M1 completion report contains metrics and artifacts.
