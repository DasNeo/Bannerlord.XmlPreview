# M4 Implementation Plan: Authoring Loop

## Objective

Make repeated prefab editing fast and diagnosable through persistent Preview Scenarios, dependency-aware reload, source navigation, and visual comparison. Keep editing in the user's chosen external editor.

## Prerequisite

M3 is complete and the first Preview release baseline is available. Visual Regression, Preview Scenarios, Behavior Graph Diagnostics, and structured asset outcomes already exist.

## Target modules and seams

Create or deepen these modules:

- Preview Scenario repository: loads and saves versioned scenarios
- prefab dependency set: reports every source file required by a loaded prefab
- dependency watcher: watches a set of files and emits a debounced change batch
- preview load coordinator: publishes a new preview only after complete success
- source navigator: opens a file and line through an external adapter
- comparison presenter: displays reference, actual, side-by-side, and overlay views

Each interface should stay small. File-system behavior is local-substitutable and uses in-memory or temporary-directory adapters in tests.

## Work package 4.1: Complete Preview Scenario persistence

1. Finalize the versioned serialization format introduced in M1.
2. Add load, save, create, clone, rename, and delete operations behind one repository interface.
3. Validate prefab identity, schema version, bindings, lists, states, resolution, Key Bounds selectors, masks, Expected Warnings, scene, and data state.
4. Preserve unknown forward-compatible fields when practical or reject them explicitly; never discard them silently.
5. Store scenarios outside the Bannerlord installation by default.
6. Show validation errors without replacing the active valid scenario.

## Work package 4.2: Add scenario selection to the app

1. Add scenario selection near the existing prefab controls.
2. Load the selected scenario before rendering.
3. Surface dirty state and save state.
4. Re-render when scenario values change.
5. Keep the currently active scenario visible in Diagnostics and artifact metadata.
6. Preserve app responsiveness during scenario reload and cancellation.

Do not add an embedded XML editor.

## Work package 4.3: Produce a complete dependency set

Extend loader results or add a dependency-report interface that returns:

- root prefab XML
- included prefabs
- template sources
- brush definitions
- sprite or image files used by the active render
- module metadata files that affect asset precedence

Use canonical absolute paths internally and stable source identities in snapshots. Include source line information where the XML parser can provide it.

## Work package 4.4: Replace single-file watching

The current `IFileWatcherService` watches individual files and exposes one event. Replace or deepen it so callers provide one dependency set and receive a debounced batch.

Required behavior:

- atomically replace the watched dependency set after a successful load
- coalesce repeated writes and rename patterns from common editors
- identify changed paths
- cancel obsolete reloads
- dispose removed watchers
- tolerate temporarily missing files during atomic save
- avoid duplicate reloads for one save operation

Tests use a temporary directory and deterministic debounce control.

## Work package 4.5: Implement Last Good Preview publication

Refactor load publication into a single commit point:

1. load root and dependencies
2. resolve the active Preview Scenario
3. build and validate the Behavior Graph
4. render into an isolated root
5. complete Diagnostics and snapshots
6. publish preview, dependency set, source map, and Diagnostics together

On failure:

- retain the Last Good Preview
- mark it stale visibly
- show current error Diagnostics
- do not replace the last good dependency set until a new load succeeds
- allow the next file change or manual reload to recover

## Work package 4.6: Add source navigation

1. Preserve source file and line metadata through prefab expansion.
2. Attach source locations to Preview Tree nodes, Diagnostics, Behavior Trace entries, and missing-asset reports where possible.
3. Define a small source-navigation interface accepting file and line.
4. Implement an external-editor or shell adapter in the app layer.
5. Provide a copy-path fallback when no adapter is configured.

Core never launches external applications directly.

## Work package 4.7: Add side-by-side and overlay views

1. Reuse Visual Regression artifacts and comparison logic.
2. Add reference, actual, side-by-side, overlay, and diff modes.
3. Show active run mode, scenario, Capture Profile, Pixel Tolerance, and mask count.
4. Keep zoom and pan synchronized between reference and actual views.
5. Allow selecting a reported difference and locating its Key Bounds or Widget ID when mapping exists.

Do not duplicate pixel-comparison logic in the ViewModel.

## Work package 4.8: Add authoring-focused Diagnostics

Expose:

- stale-preview reason
- changed dependency paths
- active Preview Scenario and schema version
- reload generation and cancellation outcome
- source location
- visual-comparison summary
- Behavior Trace link when available

Keep the compact default; load full traces on demand.

## Work package 4.9: End-to-end authoring tests

Automate these scenarios:

- change root XML and observe one reload
- change an include and observe one reload
- change a brush and observe one reload
- change an image and observe one reload
- introduce malformed XML and retain Last Good Preview with stale marker
- fix the malformed XML and recover automatically
- switch scenarios and update bindings and state
- navigate from a Diagnostic and Preview Tree node to the correct file and line
- open side-by-side and overlay with the active reference
- cancel a slow reload when a newer dependency change arrives

Run a manual WPF smoke test with an external editor after automated coverage passes.

## Exit checklist

- [ ] Preview Scenarios load, save, and switch persistently.
- [ ] Root XML, includes, brushes, and assets trigger dependency-aware reload.
- [ ] Failed reload retains a visibly stale Last Good Preview.
- [ ] Diagnostics and Preview Tree navigate to file and line.
- [ ] Side-by-side, overlay, and diff use Visual Regression results.
- [ ] No embedded XML editor was introduced.
- [ ] End-to-end authoring tests and WPF smoke test pass.
